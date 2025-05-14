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
    private ExpressionSyntax _retrieveExpression;
    private ExpressionSyntax _storeExpression;
    private SyntaxNode _retrieveMemoizationPlaceHolderNode;
    private SyntaxNode _storeMemoizationIdPlaceHolderNode;
    private SyntaxNode _storeMemoizationValuePlaceHolderNode;

    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();

    public BlockSyntax PlaceWithMemoizationStatement(BlockSyntax block,
        LiteralExpressionSyntax identifierForMemoization, TypeSyntax returnType, CodeInjection injection)
    {
        block = InjectReturnMemoization(block, identifierForMemoization, returnType, injection);
        return InjectMemoizationCheck(block, identifierForMemoization, returnType, injection);
    }

    private ExpressionSyntax RetrieveMemoizationExpression(LiteralExpressionSyntax id, TypeSyntax type,
        CodeInjection injection)
    {
        // Initialize memoization retrieval invocation expression
        if (_retrieveExpression == null)
        {
            _retrieveExpression = ParseExpression(injection.MemoizationRetrieveSelectorExpression);
            _retrieveMemoizationPlaceHolderNode = _retrieveExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "ID" });
        }

        // Replace the placeholder with memoization id
        var retrieveExpr = _retrieveExpression.ReplaceNode(_retrieveMemoizationPlaceHolderNode, id);

        // Replace generic type with return type
        if (retrieveExpr is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax genericName
                } memberAccess
            } invocationExpression)
        {
            var newTypeArgumentList = TypeArgumentList(SingletonSeparatedList(type));
            var updatedGenericName = genericName.WithTypeArgumentList(newTypeArgumentList);
            var updatedMemberAccess = memberAccess.WithName(updatedGenericName);

            return invocationExpression.WithExpression(updatedMemberAccess);
        }

        throw new InvalidOperationException(
            "Internal Error: Something went wrong with the memoization retrieval invocation. Please report this as a bug.");
    }

    private ExpressionSyntax StoreMemoizationExpression(LiteralExpressionSyntax id, ExpressionSyntax expr,
        TypeSyntax type,
        CodeInjection injection)
    {
        // Initialize memoization retrieval invocation expression
        if (_storeExpression == null)
        {
            _storeExpression = ParseExpression(injection.MemoizationStoreSelectorExpression);
            _storeMemoizationIdPlaceHolderNode = _storeExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "ID" });
            _storeMemoizationValuePlaceHolderNode = _storeExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "VALUE" });
        }

        // Replace the placeholder with memoization id and value
        var storeExpr = _storeExpression.ReplaceNodes(
            [_storeMemoizationIdPlaceHolderNode, _storeMemoizationValuePlaceHolderNode],
            (original, _) => original switch
            {
                _ when original == _storeMemoizationIdPlaceHolderNode => id,
                _ when original == _storeMemoizationValuePlaceHolderNode => expr,
                _ => original
            }
        );

        // Replace generic type with return type
        if (storeExpr is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax genericName
                } memberAccess
            } invocationExpression)
        {
            var newTypeArgumentList = TypeArgumentList(SingletonSeparatedList(type));
            var updatedGenericName = genericName.WithTypeArgumentList(newTypeArgumentList);
            var updatedMemberAccess = memberAccess.WithName(updatedGenericName);

            return invocationExpression.WithExpression(updatedMemberAccess);
        }

        throw new InvalidOperationException(
            "Internal Error: Something went wrong with the memoization store invocation. Please report this as a bug.");
    }

    private BlockSyntax InjectMemoizationCheck(BlockSyntax block, LiteralExpressionSyntax memoizationIdentifier,
        TypeSyntax returnType, CodeInjection injection)
    {
        var e = RetrieveMemoizationExpression(memoizationIdentifier, returnType, injection);
        var memVariableName = CodeInjection.GetRandomVariableName();
        var memoVarDeclaration = LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var").WithTrailingTrivia(Space))
                    .WithVariables(
                        SingletonSeparatedList(
                            VariableDeclarator(Identifier(memVariableName))
                                .WithInitializer(EqualsValueClause(e))))
            )
            .WithTrailingTrivia(CarriageReturnLineFeed);

        var storeDeclaration = LocalDeclarationStatement(
            VariableDeclaration(GenericName(Identifier("Func"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[]
                            {
                                returnType, Token(SyntaxKind.CommaToken), returnType
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
        return Block(storeDeclaration, memoVarDeclaration, memoIfStatement);
    }

    private BlockSyntax InjectReturnMemoization(BlockSyntax block, LiteralExpressionSyntax memoizationIdentifier,
        TypeSyntax returnType, CodeInjection injection)
    {
        foreach (var rs in block.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            var newReturnStatement = ReturnStatement(
                StoreMemoizationExpression(
                        memoizationIdentifier,
                        rs.Expression ?? LiteralExpression(SyntaxKind.NullLiteralExpression),
                        returnType,
                        injection)
                    .WithLeadingTrivia(Space)
            ).WithLeadingTrivia(rs.GetLeadingTrivia());

            block = block.ReplaceNode(rs, newReturnStatement);
        }

        return block;
    }

}
