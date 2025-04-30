using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.Instrumentation;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;

/// <summary>
/// Injects a mutation controlled by a conditional operator.
/// </summary>
internal class MemoizationInstrumentationEngine : BaseEngine<BlockSyntax>
{
    private static BlockSyntax AsBlock(StatementSyntax code) => code as BlockSyntax ?? Block(code);

    // /// <summary>
    // /// Injects a conditional operator with the original code or the mutated one, depending on condition's result.
    // /// </summary>
    // /// <param name="condition">Expression for the condition.</param>
    // /// <param name="original">Original code</param>
    // /// <param name="mutated">Mutated code</param>
    // /// <returns>A new expression containing the expected construct.</returns>
    public BlockSyntax PlaceWithMemoizationStatement(
        // ExpressionSyntax condition,
        TypeSyntax returnType,
        BlockSyntax original,
        LiteralExpressionSyntax identifierForMemoization)
    {
        var memVariableName = CodeInjection.GetRandomVariableName();

        var memoVarDeclaration = LocalDeclarationStatement(
            VariableDeclaration(
                    IdentifierName("var").WithTrailingTrivia(Space))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier(memVariableName))
                            .WithInitializer(
                                EqualsValueClause(
                                    InvocationExpression(IdentifierName("GetMemoization"))
                                        .WithArgumentList(
                                            ArgumentList(
                                                SingletonSeparatedList(Argument(identifierForMemoization)))))))));
        var declaration = LocalDeclarationStatement(
            VariableDeclaration(GenericName(Identifier("Func"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[]
                            {
                                PredefinedType(Token(SyntaxKind.StringKeyword)),
                                Token(SyntaxKind.CommaToken),
                                returnType
                            })
                        )
                    ))
                .WithTrailingTrivia(Space)
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("GetMemoization"))
                        .WithInitializer(EqualsValueClause(
                                ParenthesizedLambdaExpression()
                                    .WithParameterList(
                                        ParameterList(SingletonSeparatedList(
                                            Parameter(Identifier("s"))
                                                .WithLeadingTrivia(Space)
                                                .WithType(PredefinedType(Token(SyntaxKind.StringKeyword)))
                                        ))
                                    )
                                    .WithExpressionBody(LiteralExpression(SyntaxKind.NullLiteralExpression))
                            )
                        )
                    )
                )
        );
        // Create the if statement for memoization check
        var memoIfStatement = IfStatement(
            BinaryExpression(SyntaxKind.NotEqualsExpression, IdentifierName(memVariableName),
                LiteralExpression(SyntaxKind.NullLiteralExpression)),
            Block(
                SingletonList<StatementSyntax>(
                    ReturnStatement(IdentifierName(memVariableName)).WithTrailingTrivia(Space))
            ),
            ElseClause(original)
        );
        return Block(
            declaration,
            memoVarDeclaration,
            memoIfStatement
        );
    }
    //
    // Block(
    //     LocalDeclarationStatement()
    //         IfStatement(
    //         )
    // );
    //
    // IfStatement(condition,
    //     AsBlock(mutatedNode),
    // SyntaxFactory.ElseClause(AsBlock(originalNode.WithoutTrivia())))
    // .WithTriviaFrom(originalNode).WithAdditionalAnnotations(Marker);
    //
    // SyntaxFactory.ParenthesizedExpression(
    // SyntaxFactory.ConditionalExpression(
    // condition: condition,
    // whenTrue: mutated,
    // whenFalse: original)).
    //
    // WithTriviaFrom(original).

    // // Mark this node as a MutationConditional node. Store the MutantId in the annotation to retrace the mutant later
    // WithAdditionalAnnotations(Marker);

    // protected override SyntaxNode Revert(ParenthesizedExpressionSyntax parenthesized)
    // {
    //     if (parenthesized.Expression is ConditionalExpressionSyntax conditional)
    //     {
    //         return conditional.WhenFalse.WithTriviaFrom(parenthesized);
    //     }
    //     throw new InvalidOperationException($"Expected a block containing a conditional expression, found:\n{parenthesized.ToFullString()}.");
    // }
    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();
}
