using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        var declaredType = InferType(vds, semanticModel);

        // If inferred type is var, we cannot perform memoization (Type needed)
        if (declaredType.IsVar || declaredType.ToString() == "")
        {
            return targetNode;
        }

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

            var identifiers = semanticModel.AnalyzeDataFlow(originalVdec.Initializer.Value)?
                .ReadInside
                .Where(s => s.Name != "this" && s.Name != "value")
                .Select(s => IdentifierName(s.Name));


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
            var idExpression = engine.GenerateMemoId(variableId, idsAndMemberAccesses, injection);
            var lambdaExpr = WrapInLambda(rhsExprOriginal);
            var invocation = engine.RetrieveMemoizationExpression(idExpression, lambdaExpr, null, declaredType, injection);
            var newVdec = InjectStatementMemoization(vdec, invocation, targetNode, injection);
            return newVdec;
        });

        var declaration = VariableDeclaration(vdsMutated.Type).WithVariables(SeparatedList(declarators));
        return LocalDeclarationStatement(declaration);
    }
}
