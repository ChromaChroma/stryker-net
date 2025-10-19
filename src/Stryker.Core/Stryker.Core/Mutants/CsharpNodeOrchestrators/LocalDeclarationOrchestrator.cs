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


        if (sourceNode.Declaration.Type is RefTypeSyntax refTypeSyntax)
        {

            NotMemoizedCollector.Add(ReasonType.IllegalModifiersRef, MemoizationLevel.Expression,
                "ref type", sourceNode);
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

            // if (originalVdec.Identifier.ValueText == "enumMemberValue")
            // {
            //     Console.WriteLine();
            // }

            if (rhsExprOriginal == null || vdec.Initializer == null)
            {
                // If no expression preset, continue with target nodes variable declarator
                return vdec;
            }

            if (rhsExprOriginal is InitializerExpressionSyntax ies
                && ies.IsKind(SyntaxKind.ArrayInitializerExpression))
            {

                rhsExprOriginal = ArrayCreationExpression(
                    (ArrayTypeSyntax)declaredType,
                    ies // The initializer from before
                );
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
                        "Uses external api calls that cannot be confirmed to be side-effect free. ",
                        sourceNode);
                    return vdec;
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
                ILocalSymbol local => local.Type.IsRefLikeType || local.IsRef,
                IParameterSymbol param => param.RefKind == RefKind.Ref
                                          || param.RefKind == RefKind.RefReadOnly
                                          || param.Type.IsRefLikeType ,
                IFieldSymbol field => field.Type.IsRefLikeType,
                _ => false,
            });

            // var type = semanticModel.GetTypeInfo(rhsExprOriginal).Type;
            // if (type?.IsRefLikeType == true)
            // {
            //     Console.WriteLine();
            //     hasRefTypes = true;
            // }


            if (hasRefTypes)
            {
                // Disallow ref types due to not being allowed in lambdas, return original
                NotMemoizedCollector.Add(ReasonType.IllegalModifiersRef, MemoizationLevel.Expression,
                    "Expression has uses ref parameters", sourceNode);
                return vdec;
            }

            if (ContainsLinqQuery(originalVdec.Initializer.Value))
            {
                NotMemoizedCollector.Add(ReasonType.ContainsLinqQuery, MemoizationLevel.Expression,
                    "Expression contains old school style linq queries.", sourceNode);
                return vdec;

            }

            var (containsIllegalKeyword, keywordTypeReason) =
                ContainsRefOutIn(semanticModel, originalVdec.Initializer.Value);
            if (containsIllegalKeyword)
            {
                NotMemoizedCollector.Add(keywordTypeReason, MemoizationLevel.Expression,
                    "Illegal keyword", sourceNode);
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
            NotMemoizedCollector.Add(ReasonType.None, MemoizationLevel.Method, "Added",
                sourceNode);
            return newVdec;
        });

        var declaration = VariableDeclaration(vdsMutated.Type).WithVariables(SeparatedList(declarators));
        return LocalDeclarationStatement(declaration);
    }
    public static bool ContainsLinqQuery(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;

        // Check if this expression or any descendant node is a query expression
        return expression
            .DescendantNodesAndSelf()
            .OfType<QueryExpressionSyntax>()
            .Any();
    }
    public static (bool, ReasonType IllegalModifiersIn) ContainsRefOutIn(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        // 🧠 Check if any expression evaluates to a ref-like type (ref struct)
        var typeInfo = semanticModel.GetTypeInfo(expression);
        var typeSymbol = typeInfo.Type;

        if (typeSymbol is ITypeSymbol t && t.IsRefLikeType)
        {
            return (true, ReasonType.IllegalModifiersRef);
        }

        // Walk through all descendant nodes, including the root expression itself
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            // 🧠 Check method calls
            if (node is InvocationExpressionSyntax invocation)
            {
                foreach (var arg in invocation.ArgumentList.Arguments)
                {

                    if (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword))
                        return (true, ReasonType.IllegalModifiersRef);
                    if (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                        return (true, ReasonType.IllegalModifiersOut);
                    if (arg.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
                        return (true, ReasonType.IllegalModifiersIn);


                    // Optional semantic check (to detect parameters that are ref/out/in by signature)
                    var argSymbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
                    if (argSymbol != null)
                    {
                        var parameters = argSymbol.Parameters;
                        var index = invocation.ArgumentList.Arguments.IndexOf(arg);
                        if (index >= 0 && index < parameters.Length)
                        {
                            var refKind = parameters[index].RefKind;

                            if (refKind is RefKind.Ref )
                                return (true, ReasonType.IllegalModifiersRef);
                            if (refKind is RefKind.Out )
                                return (true, ReasonType.IllegalModifiersOut);
                            if (refKind is RefKind.In )
                                return (true, ReasonType.IllegalModifiersIn);
                        }
                    }
                }
            }

            // 🧠 Optionally: Check identifiers or variable usage
            if (node is IdentifierNameSyntax identifier)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;

                if (symbol is IParameterSymbol { RefKind: RefKind.Ref})
                    return (true, ReasonType.IllegalModifiersRef);
                if (symbol is IParameterSymbol { RefKind: RefKind.Out})
                    return (true, ReasonType.IllegalModifiersOut);
                if (symbol is IParameterSymbol { RefKind: RefKind.In})
                    return (true, ReasonType.IllegalModifiersIn);

                if (symbol is ILocalSymbol { RefKind: RefKind.Ref})
                    return (true, ReasonType.IllegalModifiersRef);
                if (symbol is ILocalSymbol { RefKind: RefKind.Out})
                    return (true, ReasonType.IllegalModifiersOut);
                if (symbol is ILocalSymbol { RefKind: RefKind.In})
                    return (true, ReasonType.IllegalModifiersIn);
            }
        }

        return (false, ReasonType.None);
    }
}
