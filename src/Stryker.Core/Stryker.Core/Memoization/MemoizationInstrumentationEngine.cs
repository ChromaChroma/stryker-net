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
    // Code expression for memoization value retrieval
    private ExpressionSyntax _retrieveExpression;
    private SyntaxNode _retrieveMemoizationPlaceHolderNode;
    // Code expression for memoization value storage
    private ExpressionSyntax _storeExpression;
    private SyntaxNode _storeMemoizationIdPlaceHolderNode;
    private SyntaxNode _storeMemoizationValuePlaceHolderNode;
    // Code expression for generation of memoization id to store and retrieve memoization values
    private ExpressionSyntax _generateIdExpression;
    private SyntaxNode _generateIdMethodIdentifierPlaceHolderNode;
    private SyntaxNode _generateIdParamsPlaceHolderNode;


    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();

    public BlockSyntax PlaceWithMemoizationStatement(BlockSyntax block, string methodIdentifier, TypeSyntax returnType,
        IdentifierNameSyntax[] inputParameters, CodeInjection injection)
    {
        var memoizationIdentifier = LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            Literal(CodeInjection.GetRandomVariableName("memoization_id__"))
        );
        block = InjectMemoizationIdentityDeclaration(block, memoizationIdentifier, methodIdentifier, inputParameters, injection);
        block = InjectReturnMemoization(block, memoizationIdentifier, returnType, injection);
        return InjectMemoizationCheck(block, memoizationIdentifier, returnType, injection);
    }

    private BlockSyntax InjectMemoizationIdentityDeclaration(BlockSyntax block,
        LiteralExpressionSyntax memoizationIdVariableIdentifier, string methodIdentifier, IdentifierNameSyntax[] inputParameters, CodeInjection injection)
    {
        // Initialize memoization id generation expression
        if (_generateIdExpression == null)
        {
            _generateIdExpression = ParseExpression(injection.MemoizationGenerateIdSelectorExpression);
            _generateIdMethodIdentifierPlaceHolderNode = _generateIdExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "ID" });
            _generateIdParamsPlaceHolderNode = _generateIdExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "PARAMS" });
        }

        var arrayExpression = ArrayCreationExpression(
            ArrayType(PredefinedType(Token(SyntaxKind.ObjectKeyword)).WithLeadingTrivia(Space))
                .WithRankSpecifiers(SingletonList(
                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression()))))
        ).WithInitializer(InitializerExpression(SyntaxKind.ArrayInitializerExpression,
            SeparatedList<ExpressionSyntax>(inputParameters)));

        var generateIdExpr = _generateIdExpression.ReplaceNodes(
            [_generateIdMethodIdentifierPlaceHolderNode, _generateIdParamsPlaceHolderNode],
            (original, _) => original switch
            {
                _ when original == _generateIdMethodIdentifierPlaceHolderNode => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(methodIdentifier)),
                _ when original == _generateIdParamsPlaceHolderNode => arrayExpression,
                _ => original
            }
        );

        var memoizationIdentifierDeclaration = DeclareLocal(memoizationIdVariableIdentifier.Token.Text, generateIdExpr);
        return block.WithStatements(block.Statements.Insert(0, memoizationIdentifierDeclaration));
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


    private LocalDeclarationStatementSyntax DeclareLocal(string variableName, ExpressionSyntax expr) =>
        LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var").WithTrailingTrivia(Space))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier(variableName)).WithInitializer(EqualsValueClause(expr))))
        ).WithTrailingTrivia(CarriageReturnLineFeed);

    private BlockSyntax InjectMemoizationCheck(BlockSyntax block, LiteralExpressionSyntax memoizationIdentifier,
        TypeSyntax returnType, CodeInjection injection)
    {
        var retrieveMemoizationExpr = RetrieveMemoizationExpression(memoizationIdentifier, returnType, injection);
        var memVariableName = CodeInjection.GetRandomVariableName("memoized_value__");
        var memoVarDeclaration = DeclareLocal(memVariableName, retrieveMemoizationExpr);


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
        return Block(memoVarDeclaration, memoIfStatement);
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
