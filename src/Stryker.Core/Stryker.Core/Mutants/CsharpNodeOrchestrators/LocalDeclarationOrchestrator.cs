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
        if (sourceNode is not LocalDeclarationStatementSyntax { Declaration: VariableDeclarationSyntax vds }
            || targetNode is not LocalDeclarationStatementSyntax { Declaration: VariableDeclarationSyntax vdsMutated })
        {
            return targetNode;
        }

        var declaredType = vds.Type.IsVar
            ? ParseTypeName(semanticModel.GetTypeInfo(vds.Variables.First().Initializer?.Value).Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            : vds.Type;

        if (declaredType.IsVar || declaredType.ToString() == "")
        {
            return targetNode;
        }

        // int x = 1, y = 2;
        // int x = IsActive(112)? -1,1, y = 2;

        var declarators = new List<VariableDeclaratorSyntax>();
        foreach (VariableDeclaratorSyntax vdec in vdsMutated.Variables)
        {
            var originalVdec = vds.Variables
                .FirstOrDefault(v => v.Identifier.ValueText == vdec.Identifier.ValueText);
            var rhsExprOriginal = originalVdec?.Initializer?.Value;

            if (rhsExprOriginal != null)
            {

                var id =
                    $"{originalVdec.SyntaxTree.GetLineSpan(originalVdec.Span).StartLinePosition}__{semanticModel.GetEnclosingSymbol(originalVdec.SpanStart)?.ContainingNamespace}__{originalVdec.Identifier.ValueText}";


                // Create "id" argument
                //todo: change literal
                var stringArg = LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(id));

                // () => { return expr; }
                var lambdaExpr = ParenthesizedLambdaExpression()
                    .WithParameterList(ParameterList())
                    .WithBlock(Block(ReturnStatement(rhsExprOriginal.WithLeadingTrivia(Space))));

                // RetrieveMemo<T>("key::123123", () => { return expr })
                var invocation = MutantPlacer.MemoizationInstrumentationEngine
                    .RetrieveMemoizationExpression(stringArg, declaredType, lambdaExpr,
                        context.Placer._injection);

                var mutantIds = targetNode.GetDescendantMutantIds().ToList();
                if (mutantIds.Count != 0 && vdec.Initializer != null)
                {
                    var anyActiveMutantsInvocation = MutantPlacer.MemoizationInstrumentationEngine
                        .AnyActiveMutantsCheck(mutantIds, context.Placer._injection);
                    var negatedNode = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, anyActiveMutantsInvocation);
                    var ternaryNode = ConditionalExpression(negatedNode, invocation, vdec.Initializer.Value);


                    // Add as declarator to the list of declarators
                    declarators.Add(
                        VariableDeclarator(vdec.Identifier)
                            .WithInitializer(EqualsValueClause(ternaryNode))
                    );
                }
                else //If no mutants in rhs expr, continue with the memoized invocation
                {

                    // Add as declarator to the list of declarators
                    declarators.Add(
                        VariableDeclarator(vdec.Identifier)
                            .WithInitializer(EqualsValueClause(invocation))
                    );
                }
            }
            else
            {
                // Else just use the generated mutated code
                declarators.Add(vdec);
            }
        }

        var declaration = VariableDeclaration(vdsMutated.Type)
            .WithVariables(SeparatedList(declarators));
        return LocalDeclarationStatement(declaration);

    }
}
