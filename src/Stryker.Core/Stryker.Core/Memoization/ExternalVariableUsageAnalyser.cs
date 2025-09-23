using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stryker.Core.Memoization;

public static class ExternalVariableUsageAnalyser
{
    /// <summary>
    /// Finds all variables (symbols) that are written to inside the given method body,
    /// but are not declared as locals or parameters of the method.
    /// </summary>
    public static IEnumerable<ISymbol> GetExternalWrites(BlockSyntax block, SemanticModel semanticModel)
    {
        var results = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // 1️⃣ Use DataFlowAnalysis for locals/params from outer scopes
        var flow = ModelExtensions.AnalyzeDataFlow(semanticModel, block);
        foreach (var sym in flow.WrittenOutside)
        {
            results.Add(sym);
        }

        // 2️⃣ Walk assignments for fields/properties/etc.
        var assignments = block.DescendantNodes().OfType<AssignmentExpressionSyntax>();

        foreach (var assign in assignments)
        {
            var leftSymbol = ModelExtensions.GetSymbolInfo(semanticModel, assign.Left).Symbol;
            if (leftSymbol == null)
                continue;

            if (!IsLocalOrParameterOfThisMethod(leftSymbol, flow))
                results.Add(leftSymbol);
        }

        // Also handle ++ / -- operators
        var increments = block.DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Concat<ExpressionSyntax>(block.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>());

        foreach (var inc in increments)
        {
            var operandSymbol = ModelExtensions.GetSymbolInfo(semanticModel, inc is PrefixUnaryExpressionSyntax pre ? pre.Operand : ((PostfixUnaryExpressionSyntax)inc).Operand
            ).Symbol;

            if (operandSymbol != null && !IsLocalOrParameterOfThisMethod(operandSymbol, flow))
                results.Add(operandSymbol);
        }

        return results;
    }
    /// <summary>
    /// Returns all external fields that are written to in the given initializer/assignment expression.
    /// Ignores locals and parameters.
    /// </summary>
    public static IEnumerable<IFieldSymbol> GetExternalWrites(
        ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var writtenFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        // Assignment expressions (field = ...)
        var assignments = expression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>();
        foreach (var assign in assignments)
        {
            var symbol = semanticModel.GetSymbolInfo(assign.Left).Symbol;
            if (symbol is IFieldSymbol field)
                writtenFields.Add(field);
        }

        // Prefix/Postfix increments/decrements (field++, ++field, etc.)
        var unaryWrites = expression.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Where(u => u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression))
            .Cast<ExpressionSyntax>()
            .Concat(expression.DescendantNodesAndSelf()
                .OfType<PostfixUnaryExpressionSyntax>()
                .Where(u => u.IsKind(SyntaxKind.PostIncrementExpression) || u.IsKind(SyntaxKind.PostDecrementExpression)));

        foreach (var unary in unaryWrites)
        {
            ExpressionSyntax operand = unary switch
            {
                PrefixUnaryExpressionSyntax pre => pre.Operand,
                PostfixUnaryExpressionSyntax post => post.Operand,
                _ => null
            };

            if (operand == null) continue;

            var symbol = semanticModel.GetSymbolInfo(operand).Symbol;
            if (symbol is IFieldSymbol field)
                writtenFields.Add(field);
        }

        return writtenFields;
    }
    private static bool IsLocalOrParameterOfThisMethod(ISymbol symbol, DataFlowAnalysis flow)
    {
        // locals declared inside
        if (flow.VariablesDeclared.Contains(symbol))
            return true;

        // method parameters
        if (symbol.Kind == SymbolKind.Parameter)
            return true;

        return false;
    }


     /// <summary>
    /// Returns all fields that are read (accessed as r-values) inside the given method block.
    /// Ignores locals, parameters, and writes.
    /// </summary>
    public static IEnumerable<IFieldSymbol> GetExternalFieldReads(BlockSyntax block, SemanticModel semanticModel)
    {
        var readFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        // Handle member accesses like this.field, obj.field, Class.field
        var memberAccesses = block.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

        foreach (var member in memberAccesses)
        {
            var symbol = ModelExtensions.GetSymbolInfo(semanticModel, member).Symbol;

            if (symbol is IFieldSymbol field)
            {
                // Determine if it's actually read, not just written
                if (IsRead(member))
                    readFields.Add(field);
            }
        }

        // Handle simple identifiers that might refer to instance/static fields directly
        var identifiers = block.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var id in identifiers)
        {
            var symbol = ModelExtensions.GetSymbolInfo(semanticModel, id).Symbol;

            if (symbol is IFieldSymbol field)
            {
                if (IsRead(id))
                    readFields.Add(field);
            }
        }

        return readFields;
    }

    /// <summary>
    /// Checks if the expression is used as a read (r-value), not written to.
    /// </summary>
    private static bool IsRead(ExpressionSyntax expr)
    {
        // If parent is assignment and this is the left side, it's written, not read
        if (expr.Parent is AssignmentExpressionSyntax assign && assign.Left == expr)
        {
                return false;
        }

        // If parent is prefix/postfix increment/decrement, it's written (skip)
        if (expr.Parent is PrefixUnaryExpressionSyntax pre &&
            (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression)))
            return false;

        if (expr.Parent is PostfixUnaryExpressionSyntax post &&
            (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression)))
            return false;

        // Otherwise, consider it read
        return true;
    }
    /// <summary>
    /// Returns all external fields that are read within the given expression.
    /// Works for identifiers, member accesses, and nested subexpressions.
    /// </summary>
    public static IEnumerable<IFieldSymbol> GetExternalFieldReads(
        ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var readFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        // Collect identifier names like "field"
        var identifiers = expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
        foreach (var id in identifiers)
        {
            var symbol = semanticModel.GetSymbolInfo(id).Symbol;
            if (symbol is IFieldSymbol field && IsRead(id))
                readFields.Add(field);
        }

        // Collect member accesses like "this.field", "obj.field", "Class.field"
        var memberAccesses = expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>();
        foreach (var member in memberAccesses)
        {
            var symbol = semanticModel.GetSymbolInfo(member).Symbol;
            if (symbol is IFieldSymbol field && IsRead(member))
                readFields.Add(field);
        }

        return readFields;
    }
}
