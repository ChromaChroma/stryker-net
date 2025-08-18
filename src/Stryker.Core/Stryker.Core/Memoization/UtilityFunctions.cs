using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.Mutants;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;

public class UtilityFunctions
{
    // Infers the type of a variable declaration if possible.
    // Returns type if predefined type, returns var if type cannot be inferred from project's semantic model.
    public static TypeSyntax InferType(VariableDeclarationSyntax vds, SemanticModel semanticModel)
    {
        if (vds.Type.IsVar)
        {
            var variableValue = vds.Variables
                .Select(v => v.Initializer?.Value)
                .FirstOrDefault(v => v is not null);
            if (variableValue != null)
            {
                return ParseTypeName(
                    ModelExtensions.GetTypeInfo(semanticModel, variableValue).Type!.ToDisplayString(SymbolDisplayFormat
                        .FullyQualifiedFormat));
            }
        }

        return vds.Type;
    }

    public static string CreateMemoizationVariableId(VariableDeclaratorSyntax vds, SemanticModel semanticModel) =>
        $"{vds.SyntaxTree.GetLineSpan(vds.Span).StartLinePosition}" +
        $"__" +
        $"{semanticModel.GetEnclosingSymbol(vds.SpanStart)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}" +
        $"__" +
        $"{vds.Identifier.ValueText}";

    public static ParenthesizedLambdaExpressionSyntax WrapInLambda(ExpressionSyntax expr) =>
        ParenthesizedLambdaExpression()
            .WithParameterList(ParameterList())
            .WithBlock(Block(ReturnStatement(expr.WithLeadingTrivia(Space))));
    public static ParenthesizedLambdaExpressionSyntax WrapInLambda(BlockSyntax block) =>
        ParenthesizedLambdaExpression()
            .WithParameterList(ParameterList())
            .WithBody(block);

    public static VariableDeclaratorSyntax InjectStatementMemoization(
        VariableDeclaratorSyntax vdec, ExpressionSyntax invocation, SyntaxNode targetNode, CodeInjection injection)
    {
        var mutantIds = targetNode.GetDescendantMutantIds().ToList();
        // Inject memoization instrumentation depending on the number of mutants
        var initializer = mutantIds.Count switch
        {
            0 => invocation,
            1 when vdec.Initializer is { Value: ConditionalExpressionSyntax ce } =>
                ParenthesizedExpression(ConditionalExpression(ce.Condition, ce.WhenTrue, invocation))
                    .WithTriviaFrom(ce),
            _ => ParenthesizedExpression(ConditionalExpression(
                PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    MutantPlacer.MemoizationInstrumentationEngine.AnyActiveMutantsCheck(mutantIds, injection)
                ),
                invocation,
                vdec.Initializer.Value
            ))
        };

        return VariableDeclarator(vdec.Identifier).WithInitializer(EqualsValueClause(initializer));
    }
}
