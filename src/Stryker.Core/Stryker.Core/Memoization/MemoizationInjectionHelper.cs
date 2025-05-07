using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static  Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;

public static class MemoizationInjectionHelper
{
    public static StatementSyntax CheckAndRetrieve(StatementSyntax original) =>
        IfStatement(
            BinaryExpression(
                SyntaxKind.GreaterThanExpression,
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)),
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))
            ), original
            // StatementSyntax(Block())

            // ElseClause(original)

            // IfStatement(
            //     BinaryExpression(SyntaxKind.GreaterThanExpression, LiteralExpression(SyntaxKind.IntKeyword, ))
            //     , original
            //     ));
        );


    public static UsingDirectiveSyntax UsingIO() =>
        SyntaxFactory.UsingDirective(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.IdentifierName("System"),
                SyntaxFactory.IdentifierName("IO")
            )
        );

    public static ExpressionSyntax WriteTestFile() => SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("File"),
                SyntaxFactory.IdentifierName("WriteAllText")
            )
        )
        .WithArgumentList(
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    new SyntaxNodeOrToken[]
                    {
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("C:/Users/JonaL/stryker-test.txt")
                            )
                        ),
                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("Test Output")
                            )
                        )
                    }
                )
            )
        );

    public static ExpressionSyntax ConsoleWriteTest() => SyntaxFactory.InvocationExpression(
        SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("Console"),
            SyntaxFactory.IdentifierName("WriteLine")
        )
    ).WithArgumentList(
        SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("Hello World")
                    )
                )
            )
        )
    );


    public static ExpressionSyntax DiscardInvocationExpression(ExpressionSyntax invocedCode, ExpressionSyntax originalNode) => SyntaxFactory.InvocationExpression(
        SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.CastExpression(
                    SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier("Func")
                    ).WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                SyntaxFactory.PredefinedType(
                                    SyntaxFactory.Token(SyntaxKind.StringKeyword)
                                )
                            )
                        )
                    ),
                    SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.ParenthesizedLambdaExpression()
                            .WithBlock(
                                SyntaxFactory.Block(
                                    SyntaxFactory.ExpressionStatement(invocedCode),
                                    SyntaxFactory.ReturnStatement(originalNode.WithLeadingTrivia(SyntaxFactory.Space))
                                )
                            )
                    )
                )
            ),
            SyntaxFactory.IdentifierName("Invoke")
        )
    );
}
