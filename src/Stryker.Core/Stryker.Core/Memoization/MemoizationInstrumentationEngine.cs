using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.Instrumentation;
using Stryker.Core.Mutants;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Memoization;

internal class MemoizationInstrumentationEngine : BaseEngine<BlockSyntax>
{
    // Code expression for active mutant id check
    private ExpressionSyntax _activeIdsExpression;

    private SyntaxNode _activeIdsIdPlaceHolderNode;

    // // Code expression for memoization value retrieval
    // private ExpressionSyntax _retrieveExpression;
    //
    // private SyntaxNode _retrieveMemoizationPlaceHolderNode;

    // Code expression for memoization value storage
    private ExpressionSyntax _storeExpression;
    private SyntaxNode _storeMemoizationIdPlaceHolderNode;

    private SyntaxNode _storeMemoizationValuePlaceHolderNode;

    // Code expression for generation of memoization id to store and retrieve memoization values
    private ExpressionSyntax _generateIdExpression;
    private SyntaxNode _generateIdMethodIdentifierPlaceHolderNode;
    private SyntaxNode _generateIdParamsPlaceHolderNode;



    //
    private ExpressionSyntax _retrieveMemoizationExpression;
    private SyntaxNode _retrieveMemoizationIdPlaceHolderNode;
    private SyntaxNode _retrieveMemoizationFuncPlaceHolderNode;
    private SyntaxNode _retrieveMemoizationPredPlaceHolderNode;


    protected override SyntaxNode Revert(BlockSyntax node) => throw new NotImplementedException();

    private IEnumerable<string> GetMutantIdsInBlock(BlockSyntax block) =>
        block.DescendantNodes()
            .SelectMany(n => n.GetAnnotations(MutantPlacer.MutationIdMarker))
            .Select(ann => ann.Data)
            .Distinct();

    private bool IsMemoizableType(ITypeSymbol type)
    {
        if (type == null)
            return false;
        return type.IsValueType;

        // // For now, we only memoize primitive types and strings
        // return type is PredefinedTypeSyntax { Keyword: { RawKind:
        //            (int)SyntaxKind.IntKeyword or (int)SyntaxKind.StringKeyword
        //            or (int)SyntaxKind.FloatKeyword or (int)SyntaxKind.DoubleKeyword
        //            or (int)SyntaxKind.LongKeyword or (int)SyntaxKind.DecimalKeyword
        //            or (int)SyntaxKind.BoolKeyword
        //
        //        } }
        //        // || type is IdentifierNameSyntax { Identifier.Text: "bool" or "double" or "float" or "decimal" }
        //        ;
    }

    bool IsSafeBinaryOperator(SyntaxKind kind) =>
        kind switch
        {
            // Arithmetic
            SyntaxKind.AddExpression or
                SyntaxKind.SubtractExpression or
                SyntaxKind.MultiplyExpression or
                SyntaxKind.DivideExpression or
                SyntaxKind.ModuloExpression or

                // Logical
                SyntaxKind.LogicalAndExpression or
                SyntaxKind.LogicalOrExpression or

                // Comparison
                SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression => true,

            _ => false
        };

    bool IsMemoizableCore(ExpressionSyntax expr, SemanticModel model)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax:
                return true;

            case IdentifierNameSyntax identifier:
                var symbol = model.GetSymbolInfo(identifier).Symbol as ILocalSymbol;
                return symbol != null && IsMemoizableType(symbol.Type)
                       // IsPrimitive(symbol.Type)
                       ;

            case PrefixUnaryExpressionSyntax prefix:
                return prefix.IsKind(SyntaxKind.UnaryMinusExpression) ||
                       prefix.IsKind(SyntaxKind.LogicalNotExpression);

            case BinaryExpressionSyntax binary:
                return IsSafeBinaryOperator(binary.Kind());

            case ParenthesizedExpressionSyntax:
                return true;

            default:
                return false;
        }
    }
    bool IsPrimitive(ITypeSymbol type) =>
        type != null && type.SpecialType switch
        {
            SpecialType.System_Boolean or
                SpecialType.System_Byte or
                SpecialType.System_Char or
                SpecialType.System_Double or
                SpecialType.System_Int16 or
                SpecialType.System_Int32 or
                SpecialType.System_Int64 or
                SpecialType.System_SByte or
                SpecialType.System_Single or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 => true,
            _ => false
        };
    public static bool IsPrimitive(TypeSyntax typeSyntax) =>
        typeSyntax is PredefinedTypeSyntax predefined && predefined.Keyword.Kind() switch
        {
            SyntaxKind.BoolKeyword     => true,
            SyntaxKind.ByteKeyword     => true,
            SyntaxKind.SByteKeyword    => true,
            SyntaxKind.ShortKeyword    => true,
            SyntaxKind.UShortKeyword   => true,
            SyntaxKind.IntKeyword      => true,
            SyntaxKind.UIntKeyword     => true,
            SyntaxKind.LongKeyword     => true,
            SyntaxKind.ULongKeyword    => true,
            SyntaxKind.FloatKeyword    => true,
            SyntaxKind.DoubleKeyword   => true,
            SyntaxKind.CharKeyword     => true,
            SyntaxKind.DecimalKeyword  => true,
            SyntaxKind.StringKeyword   => true,
            _ => false
        };


    public InvocationExpressionSyntax RetrieveMemoizationExpression(
        ExpressionSyntax id, ParenthesizedLambdaExpressionSyntax func,
        ExpressionSyntax pred, TypeSyntax type,
        CodeInjection injection)
    {
        // Initialize memoization retrieval invocation expression
        if (_retrieveMemoizationExpression == null)
        {
            _retrieveMemoizationExpression = ParseExpression(injection.MemoizationRetrieveSelectorExpression);
            _retrieveMemoizationIdPlaceHolderNode = _retrieveMemoizationExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "ID" });
            _retrieveMemoizationFuncPlaceHolderNode = _retrieveMemoizationExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "FUNC" });
            _retrieveMemoizationPredPlaceHolderNode = _retrieveMemoizationExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "PRED" });
        }

        pred ??= LiteralExpression(SyntaxKind.NullLiteralExpression);

        var retrieveExpr = _retrieveMemoizationExpression.ReplaceNodes(
            [_retrieveMemoizationIdPlaceHolderNode, _retrieveMemoizationFuncPlaceHolderNode, _retrieveMemoizationPredPlaceHolderNode],
            (original, _) => original switch
            {
                _ when original == _retrieveMemoizationIdPlaceHolderNode => id,
                _ when original == _retrieveMemoizationFuncPlaceHolderNode => func,
                _ when original == _retrieveMemoizationPredPlaceHolderNode => pred,
                _ => original
            }
        );

        // Replace generic type with return type
        if (retrieveExpr is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax genericName
                } memberAccess
            } invocationExpression)
        {
            var newTypeArgumentList = TypeArgumentList(SingletonSeparatedList(type.WithoutTrivia()));
            var updatedGenericName = genericName.WithTypeArgumentList(newTypeArgumentList);
            var updatedMemberAccess = memberAccess.WithName(updatedGenericName);

            return invocationExpression.WithExpression(updatedMemberAccess);
        }

        throw new InvalidOperationException(
            "Internal Error: Something went wrong with the memoization retrieval invocation. Please report this as a bug.");
    }








    public BlockSyntax PlaceWithMemoizationStatement(SemanticModel semanticModel, BlockSyntax block,
        string methodIdentifier, TypeSyntax returnType,
        IdentifierNameSyntax[] inputParameters, CodeInjection injection)
    {
        // foreach (var stmt in block.DescendantNodes().OfType<StatementSyntax>())
        // {
        //     if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax aes })
        //     {
        //         var str = stmt.ToString();
        //         var canBenMemoized = IsMemoizableType(semanticModel.GetTypeInfo(aes.Left).Type) && IsMemoizableCore( aes.Right, semanticModel);
        //     }
        //     else
        //     if (stmt is LocalDeclarationStatementSyntax { Declaration: VariableDeclarationSyntax vds } && IsPrimitive(vds.Type))
        //     {
        //         foreach (var vdsVar in vds.Variables)
        //         {
        //             // Only do memoization if there is an initializer
        //             if (vdsVar is { Initializer: EqualsValueClauseSyntax initializer } vdsVarDecl && IsMemoizableCore(initializer.Value, semanticModel))
        //             {
        //                 // memoize statement
        //
        //             }
        //         }
        //     }
        // }

        return block;





        // var memoizationIdentifier = LiteralExpression(
        //     SyntaxKind.StringLiteralExpression,
        //     Literal(CodeInjection.GetRandomVariableName("memoization_id__"))
        // );
        // block = InjectMemoizationIdentityDeclaration(block, memoizationIdentifier, methodIdentifier, inputParameters,
        //     injection);
        // block = InjectReturnMemoization(block, memoizationIdentifier, returnType, injection);
        // return InjectMemoizationCheck(block, memoizationIdentifier, returnType, injection);
    }

    public ExpressionSyntax AnyActiveMutantsCheck(IEnumerable<string> ids, CodeInjection injection)
    {
        if (_activeIdsExpression == null)
        {
            _activeIdsExpression = ParseExpression(injection.AnyActiveSelectorExpression);
            _activeIdsIdPlaceHolderNode = _activeIdsExpression.DescendantNodes()
                .First(n => n is IdentifierNameSyntax { Identifier.Text: "IDS" });
        }

        var arrayExpression = ArrayCreationExpression(
            ArrayType(PredefinedType(Token(SyntaxKind.StringKeyword)).WithLeadingTrivia(Space))
                .WithRankSpecifiers(
                    SingletonList(
                        ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression()))))
        ).WithInitializer(
            InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                SeparatedList<ExpressionSyntax>(
                    ids.Select(id => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(id))))
            )
        );
        return _activeIdsExpression.ReplaceNode(_activeIdsIdPlaceHolderNode, arrayExpression);
    }

    // Returns an InvocationExpressionSyntax calling memoization control to generate a memoization id at runtime
    public ExpressionSyntax GenerateMemoId(string methodIdentifier,  ExpressionSyntax[] inputParameters, CodeInjection injection)
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
                _ when original == _generateIdMethodIdentifierPlaceHolderNode => LiteralExpression(
                    SyntaxKind.StringLiteralExpression, Literal(methodIdentifier)),
                _ when original == _generateIdParamsPlaceHolderNode => arrayExpression,
                _ => original
            }
        );
        return generateIdExpr;
    }

    private BlockSyntax InjectMemoizationIdentityDeclaration(BlockSyntax block,
        LiteralExpressionSyntax memoizationIdVariableIdentifier, string methodIdentifier,
        IdentifierNameSyntax[] inputParameters, CodeInjection injection)
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
                _ when original == _generateIdMethodIdentifierPlaceHolderNode => LiteralExpression(
                    SyntaxKind.StringLiteralExpression, Literal(methodIdentifier)),
                _ when original == _generateIdParamsPlaceHolderNode => arrayExpression,
                _ => original
            }
        );

        var memoizationIdentifierDeclaration =
            DeclareMemoizedValueLocal(memoizationIdVariableIdentifier.Token.Text, generateIdExpr);
        return block.WithStatements(block.Statements.Insert(0, memoizationIdentifierDeclaration));
    }

    // public ExpressionSyntax RetrieveMemoizationExpression(LiteralExpressionSyntax id, TypeSyntax type,
    //     CodeInjection injection)
    // {
    //     // Initialize memoization retrieval invocation expression
    //     if (_retrieveExpression == null)
    //     {
    //         _retrieveExpression = ParseExpression(injection.MemoizationRetrieveSelectorExpression);
    //         _retrieveMemoizationPlaceHolderNode = _retrieveExpression.DescendantNodes()
    //             .First(n => n is IdentifierNameSyntax { Identifier.Text: "ID" });
    //     }
    //
    //     // Replace the placeholder with memoization id
    //     var retrieveExpr = _retrieveExpression.ReplaceNode(_retrieveMemoizationPlaceHolderNode, id);
    //
    //     // Replace generic type with return type
    //     if (retrieveExpr is InvocationExpressionSyntax
    //         {
    //             Expression: MemberAccessExpressionSyntax
    //             {
    //                 Name: GenericNameSyntax genericName
    //             } memberAccess
    //         } invocationExpression)
    //     {
    //         var newTypeArgumentList = TypeArgumentList(SingletonSeparatedList(type));
    //         var updatedGenericName = genericName.WithTypeArgumentList(newTypeArgumentList);
    //         var updatedMemberAccess = memberAccess.WithName(updatedGenericName);
    //
    //         return invocationExpression.WithExpression(updatedMemberAccess);
    //     }
    //
    //     throw new InvalidOperationException(
    //         "Internal Error: Something went wrong with the memoization retrieval invocation. Please report this as a bug.");
    // }

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


    private LocalDeclarationStatementSyntax DeclareMemoizedValueLocal(string variableName, ExpressionSyntax expr) =>
        LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var").WithTrailingTrivia(Space))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier(variableName))
                            .WithInitializer(EqualsValueClause(expr))))
        ).WithTrailingTrivia(CarriageReturnLineFeed);

    // private BlockSyntax InjectMemoizationCheck(BlockSyntax block, LiteralExpressionSyntax memoizationIdentifier,
    //     TypeSyntax returnType, CodeInjection injection)
    // {
    //     var retrieveMemoizationExpr = RetrieveMemoizationExpression(memoizationIdentifier, returnType, injection);
    //     var memVariableName = CodeInjection.GetRandomVariableName("memoized_value__");
    //
    //     var activeMutantsExpression = AnyActiveMutantsCheck(GetMutantIdsInBlock(block), injection);
    //
    //     var conditionalRetrieveMemoizedValueExpr = ParenthesizedExpression(ConditionalExpression(
    //         condition: activeMutantsExpression,
    //         whenTrue: retrieveMemoizationExpr,
    //         whenFalse: LiteralExpression(SyntaxKind.NullLiteralExpression)
    //     ));
    //     var memoVarDeclaration = DeclareMemoizedValueLocal(memVariableName, conditionalRetrieveMemoizedValueExpr);
    //
    //
    //     // Create the if statement for memoization check
    //     var memoIfStatement = IfStatement(
    //         BinaryExpression(SyntaxKind.NotEqualsExpression, IdentifierName(memVariableName),
    //             LiteralExpression(SyntaxKind.NullLiteralExpression)),
    //         Block(
    //             SingletonList<StatementSyntax>(
    //                 ReturnStatement(IdentifierName(memVariableName).WithLeadingTrivia(Space)))
    //         ).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed),
    //         ElseClause(block).WithLeadingTrivia(CarriageReturnLineFeed).WithTrailingTrivia(CarriageReturnLineFeed)
    //     );
    //     return Block(memoVarDeclaration, memoIfStatement);
    // }

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
