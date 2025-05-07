using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.Instrumentation;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;


internal class MemoizationInstrumentationEngine : BaseEngine<BlockSyntax>
{
    public BlockSyntax InjectMemoizationCheck(
        BlockSyntax block,
        LiteralExpressionSyntax memoizationIdentifier,
        TypeSyntax returnType
        )
    {

        var memVariableName = CodeInjection.GetRandomVariableName();

        var storeDeclaration = LocalDeclarationStatement(
            VariableDeclaration(GenericName(Identifier("Func"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[]
                            {
                                returnType,
                                Token(SyntaxKind.CommaToken),
                                returnType
                            })
                        )
                    ))
                .WithTrailingTrivia(Space)
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("StoreMemoization"))
                        .WithInitializer(EqualsValueClause(
                                ParenthesizedLambdaExpression()
                                    .WithParameterList(
                                        ParameterList(SingletonSeparatedList(
                                            Parameter(Identifier("s"))
                                                .WithLeadingTrivia(Space)
                                                .WithType(returnType)
                                        ))
                                    )
                                    .WithExpressionBody(IdentifierName("s"))
                            )
                        )
                    )
                )
        ).WithTrailingTrivia(CarriageReturnLineFeed);

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
        ).WithTrailingTrivia(CarriageReturnLineFeed);

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
                                                SingletonSeparatedList(Argument(memoizationIdentifier))))))))
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
            ElseClause(block).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed)
        );
        return Block(
            declaration,
            storeDeclaration,
            memoVarDeclaration,
            memoIfStatement
        );
    }

    private BlockSyntax InjectReturnMemoization(
        BlockSyntax block,
        LiteralExpressionSyntax memoizationIdentifier,
        TypeSyntax returnType
        )
    {

        var returnStatements = block.Statements.Where(s => s is ReturnStatementSyntax);
        foreach (var rs in returnStatements)
        {
            var newReturnStatement = ReturnStatement(
                InvocationExpression(IdentifierName("StoreMemoization"))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(Argument(memoizationIdentifier))
                        )
                    )
            ).WithLeadingTrivia(Space);

            block = block.ReplaceNode(rs, newReturnStatement);
        }
        return block;
    }

    public BlockSyntax PlaceWithMemoizationStatement(
        BlockSyntax block,
        LiteralExpressionSyntax identifierForMemoization,
        TypeSyntax returnType)
    {
        block = InjectReturnMemoization(block, identifierForMemoization, returnType);
        return InjectMemoizationCheck(block, identifierForMemoization, returnType);
    }


    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();
}
