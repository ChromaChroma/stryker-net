using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.Instrumentation;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;


internal class MemoizationInstrumentationEngine : BaseEngine<BlockSyntax>
{
    public BlockSyntax PlaceWithMemoizationStatement(
        // ExpressionSyntax condition,
        BlockSyntax original,
        LiteralExpressionSyntax identifierForMemoization,
        TypeSyntax returnType
        )
    {
        var memVariableName = CodeInjection.GetRandomVariableName();

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
        ).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed);

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
                                                SingletonSeparatedList(Argument(identifierForMemoization))))))))
            )
            .WithTrailingTrivia(CarriageReturnLineFeed);

        // Create the if statement for memoization check
        var memoIfStatement = IfStatement(
            BinaryExpression(SyntaxKind.NotEqualsExpression, IdentifierName(memVariableName),
                LiteralExpression(SyntaxKind.NullLiteralExpression)),
            Block(
                SingletonList<StatementSyntax>(
                    ReturnStatement(IdentifierName(memVariableName).WithLeadingTrivia(Space)))
            ).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed),
            ElseClause(original).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed)
        );
        return Block(
            declaration,
            memoVarDeclaration,
            memoIfStatement
        );
    }

    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();
}
