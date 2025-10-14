using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Abstractions.Memoization;
using Stryker.Core.Memoization;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Stryker.Core.Memoization.UtilityFunctions;

namespace Stryker.Core.Mutants.CsharpNodeOrchestrators;

/// <summary>
/// Handle const declarations.
/// </summary>
internal class LocalDeclarationOrchestrator : StatementSpecificOrchestrator<LocalDeclarationStatementSyntax>
{
    // we don't inject mutations here, we want them promoted at block level
    protected override StatementSyntax InjectMutations(LocalDeclarationStatementSyntax sourceNode,
        StatementSyntax targetNode,
        SemanticModel semanticModel,
        MutationContext context)

    {
        // Variables: VariableDeclaratorSyntax { Initializer: InitializerExpressionSyntax { Value: rhsExpr }} rhsExp
        if (sourceNode is not LocalDeclarationStatementSyntax { Declaration: { } vds }
            || targetNode is not LocalDeclarationStatementSyntax { Declaration: { } vdsMutated })
        {
            return targetNode;
        }

        var (declaredType, isAsync) = InferType(vds, semanticModel);

        // If inferred type is var, we cannot perform memoization (Type needed)
        if (isAsync || declaredType.IsVar || declaredType.ToString() == "")
        {
            return targetNode;
        }

        var sourceNodeContainingType = semanticModel.GetEnclosingSymbol(sourceNode.SpanStart)?.ContainingType;

        var engine = MutantPlacer.MemoizationInstrumentationEngine;
        var injection = context.Placer._injection;
        var declarators = vdsMutated.Variables.Select(vdec =>
        {
            var originalVdec = vds.Variables
                .First(v => v.Identifier.ValueText == vdec.Identifier.ValueText);
            var rhsExprOriginal = originalVdec.Initializer?.Value;

            if (rhsExprOriginal == null || vdec.Initializer == null)
            {
                // If no expression preset, continue with target nodes variable declarator
                return vdec;
            }

            if (rhsExprOriginal.DescendantNodesAndSelf().OfType<SwitchExpressionSyntax>().Any())
            {
                NotMemoizedCollector.Add(ReasonType.SwitchExpressionInRightHandSide, MemoizationLevel.Expression,
                    "Expression has Switch expression which is currently not handled in memoization implementation", sourceNode);
                return vdec;

            }

            var hasAsync = rhsExprOriginal.DescendantNodesAndSelf()
                               .OfType<AnonymousFunctionExpressionSyntax>()
                               .Any(f => f.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                           || rhsExprOriginal.DescendantNodesAndSelf()
                               .OfType<AnonymousMethodExpressionSyntax>()
                               .Any(f => f.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword));
            if (hasAsync)
            {
                NotMemoizedCollector.Add(ReasonType.UsesThreadingOrAsynchronousOperations, MemoizationLevel.Expression,
                    "Expression ues await, and is async", sourceNode);
                return vdec;
            }

            var awaitInvocations = rhsExprOriginal.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>();
            if (awaitInvocations.Any())
            {
                NotMemoizedCollector.Add(ReasonType.UsesThreadingOrAsynchronousOperations, MemoizationLevel.Expression,
                    "Expression has await calls", sourceNode);
                return vdec;
            }

            var invocations = rhsExprOriginal.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(inv).Symbol;


                if (symbolInfo is IMethodSymbol { IsAsync: true })
                {
                    NotMemoizedCollector.Add(ReasonType.UsesThreadingOrAsynchronousOperations, MemoizationLevel.Expression,
                        $"Expression has async calls: {inv.ToString()}", sourceNode);
                    return vdec;
                }

                if (IsExternalInvocation(inv, semanticModel))
                {
                    NotMemoizedCollector.Add(ReasonType.UsesExternalLibrariesOrAPIs, MemoizationLevel.Expression,
                        "Uses external api calls that cannot be confirmed to be side-effect free: " + inv.ToString(),
                        sourceNode);
                }
            }

            var dataFlow = semanticModel.AnalyzeDataFlow(originalVdec.Initializer.Value);
            // var declaredInRhs = dataFlow?.VariablesDeclared ?? Enumerable.Empty<ISymbol>();
            var analyzedIdentifiers = dataFlow?
                .ReadInside
                .Where(s => s.Name != "this" && s.Name != "value")
                .Where(s => !dataFlow.VariablesDeclared.Contains(s))
                .Where(s => s switch
                {
                    ILocalSymbol local => !local.IsImplicitlyDeclared,
                    IParameterSymbol param => !(param.ContainingSymbol is IMethodSymbol method &&
                                                method.MethodKind == MethodKind.AnonymousFunction),
                    IFieldSymbol or IPropertySymbol => true,
                    _ => false
                })
                .ToArray();

            var hasRefTypes = (analyzedIdentifiers ?? []).Any(s => s switch
            {
                ILocalSymbol local => local.Type.IsRefLikeType,
                IParameterSymbol param => param.Type.IsRefLikeType || param.RefKind == RefKind.RefReadOnly,
                IFieldSymbol field => field.Type.IsRefLikeType,
                _ => false,
            });


            if (hasRefTypes)
            {
                // Disallow ref types due to not being allowed in lambdas, return original
                NotMemoizedCollector.Add(ReasonType.IllegalModifiers, MemoizationLevel.Expression,
                    "Expression has uses ref parameters", sourceNode);
                return vdec;
            }


            var variablesRead = ExternalVariableUsageAnalyser
                .GetExternalFieldReads(originalVdec.Initializer.Value, semanticModel).ToList();
            var thisVariablesRead = variablesRead.Where(s =>
                s.ContainingType != null &&
                SymbolEqualityComparer.Default.Equals(s.ContainingType, sourceNodeContainingType)).ToList();
            var externalVariablesRead = variablesRead.Except(variablesRead).ToList();

            if (externalVariablesRead.Count > 0)
            {
                NotMemoizedCollector.Add(ReasonType.ReliesOnExternalState, MemoizationLevel.Expression,
                    "Expression relies on external state: " + string.Join(", ",
                        externalVariablesRead.Select(s => s.OriginalDefinition.ToString())), sourceNode);
                return vdec;
            }

            if (semanticModel.GetEnclosingSymbol(sourceNode.SpanStart)?.ContainingType.IsValueType ?? false)
            {
                NotMemoizedCollector.Add(ReasonType.ParentIsValueType, MemoizationLevel.Method,
                    "Defined in value-typed parent, does not allow this access in lambdas",
                    sourceNode);
                return vdec;
            }

            // if (sourceNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Any())
            // {
            //     NotMemoizedCollector.Add(ReasonType.ParentIsValueType, MemoizationLevel.Method,
            //         "Defined in struct parent, does not allow this access in lambdas",
            //         sourceNode);
            //     // return vdec;
            // }

            if (thisVariablesRead.Count > 0 && thisVariablesRead.All(s => !s.IsConst))
            {
                //Todo, Allow Serializable non-const values, like primitives, strings, structs of those And serializable objects. (input params of key)
                // Compile time serilizability check

                NotMemoizedCollector.Add(ReasonType.ReliesOnThisState, MemoizationLevel.Expression,
                    "Expression relies on this state: " + string.Join(", ",
                        thisVariablesRead.Select(s => s.OriginalDefinition.ToString())),
                    sourceNode);
                return vdec;
            }

            var writtenExternalVariables = ExternalVariableUsageAnalyser
                .GetExternalWrites(originalVdec.Initializer.Value, semanticModel)
                .Where(s => s is not IParameterSymbol).ToList();

            if (writtenExternalVariables.Count > 0)
            {
                NotMemoizedCollector.Add(ReasonType.AltersStateOutsideScope, MemoizationLevel.Expression,
                    "Alters state outside scope: " + string.Join(", ",
                        writtenExternalVariables.Select(s => s.OriginalDefinition.ToString())), sourceNode);
                return vdec;
            }

            var identifiers = analyzedIdentifiers?.Select(s => IdentifierName(s.Name));

            //todo: Check how we can use vars from memeraccesses, and also return values from method calls.
            var idsAndMemberAccesses = identifiers.ToArray();
            // originalVdec
            //     .DescendantNodesAndSelf()
            //     .OfType<MemberAccessExpressionSyntax>()
            //     .Where(maes => semanticModel.GetSymbolInfo(maes.Expression).Symbol is IPropertySymbol or IFieldSymbol)
            //     .Concat<ExpressionSyntax>(identifiers)
            //     .ToArray();


            // Injects: T ? = RetrieveMemo<T>(GenerateMemoizationId("1:10__NS.Method__x", Relevant_vars), () => { return expr })
            var variableId = CreateMemoizationVariableId(originalVdec, semanticModel);
            if (variableId is null)
            {
                return vdec;
            }

            var idExpression = engine.GenerateMemoId(variableId, idsAndMemberAccesses, injection);
            var lambdaExpr = WrapInLambda(rhsExprOriginal);

            var mutantIds = vdec.GetDescendantMutantIds().ToList();
            var invocation = engine.RetrieveMemoizationExpression(
                idExpression,
                lambdaExpr,
                UtilityFunctions.WrapInLambda(
                    MutantPlacer.MemoizationInstrumentationEngine.AnyActiveMutantsCheck(mutantIds,
                        context.Placer._injection)),
                declaredType,
                injection
            );
            var newVdec = InjectStatementMemoization(vdec, invocation, targetNode, injection);
            return newVdec;
        });

        var declaration = VariableDeclaration(vdsMutated.Type).WithVariables(SeparatedList(declarators));
        return LocalDeclarationStatement(declaration);
    }
}
