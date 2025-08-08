using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.Memoization;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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

        var declaredType = vds.Type;
        if (vds.Type.IsVar)
        {
            var variableValue = vds.Variables.Select(v => v.Initializer?.Value).FirstOrDefault(v => v is not null);
            if (variableValue != null)
            {
                declaredType = ParseTypeName(semanticModel.GetTypeInfo(variableValue).Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            }
        }
        // If infered type is var, we cannot perform memoization (Type needed)
        if (declaredType.IsVar || declaredType.ToString() == "")
        {
            return targetNode;
        }

        var declarators = new List<VariableDeclaratorSyntax>();
        foreach (var vdec in vdsMutated.Variables)
        {
            var originalVdec = vds.Variables
                .FirstOrDefault(v => v.Identifier.ValueText == vdec.Identifier.ValueText);
            var rhsExprOriginal = originalVdec?.Initializer?.Value;

            if (rhsExprOriginal == null || vdec.Initializer == null)
            {
                // If no expression preset, continue with target nodes variable declarator
                declarators.Add(vdec);
                continue;
            }

            var id =
                $"{originalVdec.SyntaxTree.GetLineSpan(originalVdec.Span).StartLinePosition}" +
                $"__" +
                $"{semanticModel.GetEnclosingSymbol(originalVdec.SpanStart)?.ContainingNamespace}" +
                $"__" +
                $"{originalVdec.Identifier.ValueText}";


            // Create "memoId" argument
            //todo: change literal to add input args
            var stringArg = LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(id));

            // () => { return expr; }
            var lambdaExpr = ParenthesizedLambdaExpression()
                .WithParameterList(ParameterList())
                .WithBlock(Block(ReturnStatement(rhsExprOriginal.WithLeadingTrivia(Space))));


            // RetrieveMemo<T>("memoId", () => { return expr })
            var invocation = MutantPlacer.MemoizationInstrumentationEngine
                .RetrieveMemoizationExpression(stringArg, declaredType, lambdaExpr, context.Placer._injection);

            // Inject memoization instrumentation depending on the number of mutants
            var mutantIds = targetNode.GetDescendantMutantIds().ToList();
            var initializer = mutantIds.Count switch
            {
                0 => invocation,
                1 when vdec.Initializer.Value is ConditionalExpressionSyntax ce =>
                    ParenthesizedExpression(ConditionalExpression(ce.Condition, ce.WhenTrue, invocation))
                        .WithTriviaFrom(ce),
                _ =>
                    ParenthesizedExpression(ConditionalExpression(PrefixUnaryExpression(
                            SyntaxKind.LogicalNotExpression,
                            MutantPlacer.MemoizationInstrumentationEngine
                                .AnyActiveMutantsCheck(mutantIds, context.Placer._injection)
                        ),
                        invocation,
                        vdec.Initializer.Value
                    ))
            };
            declarators.Add( VariableDeclarator(vdec.Identifier).WithInitializer(EqualsValueClause(initializer)) );
        }

        var declaration = VariableDeclaration(vdsMutated.Type).WithVariables(SeparatedList(declarators));
        return LocalDeclarationStatement(declaration);
    }
}
