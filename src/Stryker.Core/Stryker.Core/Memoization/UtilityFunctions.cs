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


    public static bool IsExternalInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol == null)
            return false; // couldn’t resolve — treat as not external (or decide your policy)

        // If there are no syntax references, this method comes from metadata
        return symbol.DeclaringSyntaxReferences.Length == 0;
    }





    // Infers the type of a variable declaration if possible.
    // Returns type if predefined type, returns var if type cannot be inferred from project's semantic model.
    public static (TypeSyntax?, bool) InferType(VariableDeclarationSyntax vds, SemanticModel semanticModel)
    {
        if (vds.Type.IsVar)
        {
            var variableValue = vds.Variables
                .Select(v => v.Initializer?.Value)
                .FirstOrDefault(v => v is not null);
            if (variableValue != null)
            {
                if (variableValue.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any())
                {
                    return (null, true);
                }

                var containsTaskOrValueTasks = variableValue
                    .DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => semanticModel.GetTypeInfo(inv).Type?.Name is "Task" or "ValueTask");
                if (containsTaskOrValueTasks)
                {
                    return (null, true);
                }

                return (ParseTypeName(
                        ModelExtensions.GetTypeInfo(semanticModel, variableValue).Type!.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat)),
                    false);

            }
        }

        if (vds.Type is IdentifierNameSyntax { Identifier.ValueText: "Task" or "ValueTask" } or GenericNameSyntax { Identifier.ValueText: "Task" or "ValueTask" })
        {
            return (null, true);
        }

        return (vds.Type, false);
    }

    public static string CreateMemoizationVariableId(VariableDeclaratorSyntax vds, SemanticModel semanticModel)
    {
        var n = vds.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
        if (n is null) return null;
        return $"{vds.SyntaxTree.GetLineSpan(vds.Span).StartLinePosition}__" +
               $"{MemoizationInstrumentationEngine.GetFullMethodSignature(n, semanticModel)}__RETURN";

        return $"{vds.SyntaxTree.GetLineSpan(vds.Span).StartLinePosition}" +
               $"__" +
               $"{semanticModel.GetEnclosingSymbol(vds.SpanStart)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}" +
               $"__" +
               $"{vds.Identifier.ValueText}";
    }



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
