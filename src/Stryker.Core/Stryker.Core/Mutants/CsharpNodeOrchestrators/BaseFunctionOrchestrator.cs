using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Stryker.Core.Helpers;
using Stryker.Core.Instrumentation;
using Stryker.Core.Memoization;
using Stryker.Utilities.Logging;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Stryker.Core.Mutants.CsharpNodeOrchestrators;

/// <summary>
/// This class implements a roslyn type independent orchestrator for method, functions, getters....
/// When adding a syntax construct, one needs to implement the various information extractor and operations to match Roslyn type
/// </summary>
/// <typeparam name="T">SyntaxNode type</typeparam>
/// <remarks>This class is helpful because there is no (useful) shared parent class for those syntax construct</remarks>
internal abstract class BaseFunctionOrchestrator<T> : MemberDefinitionOrchestrator<T>, IInstrumentCode
    where T : SyntaxNode
{
    private readonly SeparatedSyntaxList<ParameterSyntax> _emptyParameterList;

    protected BaseFunctionOrchestrator()
    {
        Marker = MutantPlacer.RegisterEngine(this, true);
        _emptyParameterList = SeparatedList<ParameterSyntax>();
    }

    private SyntaxAnnotation Marker { get; }

    /// <inheritdoc/>
    public string InstrumentEngineId => GetType().Name;

    protected abstract bool IsStatic(T node);

    /// <summary>
    /// Get the function body (block or expression)
    /// </summary>
    /// <param name="node"></param>
    /// <returns>a tuple with the block body as first item and the expression body as the second. At least one of them is expected to be null.</returns>
    protected abstract (BlockSyntax block, ExpressionSyntax expression) GetBodies(T node);

    /// <summary>
    /// Gets the parameter list of the function
    /// </summary>
    /// <param name="node">instance of <see cref="T"/></param>
    /// <returns>a parameter list</returns>
    protected abstract ParameterListSyntax ParameterList(T node);

    /// <summary>
    /// Get the return type
    /// </summary>
    /// <param name="node">instance of <see cref="T"/></param>
    /// <returns>return type of the function</returns>
    protected abstract TypeSyntax ReturnType(T node);

    /// <summary>
    /// Use the provided syntax block for the body of the function (set the expression body part to null)
    /// </summary>
    /// <param name="node">instance of <see cref="T"/></param>
    /// <param name="blockBody">desired body</param>
    /// <param name="expressionBody">desired expression body</param>
    /// <returns>an instance of <typeparamref name="T"/> with <paramref name="blockBody"/> body</returns>
    protected abstract T SwitchToThisBodies(T node, BlockSyntax blockBody, ExpressionSyntax expressionBody);

    private static BlockSyntax GenerateBlockBody(ExpressionSyntax expressionBody, TypeSyntax returnType)
    {
        StatementSyntax statementLine = returnType.IsVoid()
            ? ExpressionStatement(expressionBody)
            : ReturnStatement(expressionBody.WithLeadingTrivia(Space));

        var result = Block(statementLine);
        return result;
    }

    public T ConvertToBlockBody(T node) => ConvertToBlockBody(node, ReturnType(node));

    protected T ConvertToBlockBody(T node, TypeSyntax returnType)
    {
        var (block, expression) = GetBodies(node);
        if (block != null)
        {
            return node;
        }

        var blockBody = GenerateBlockBody(expression, returnType);
        return SwitchToThisBodies(node, blockBody, null).WithAdditionalAnnotations(Marker);
    }

    /// <inheritdoc/>
    public SyntaxNode RemoveInstrumentation(SyntaxNode node)
    {
        if (node is not T typedNode)
        {
            throw new InvalidOperationException($"Expected a {typeof(T)}, found:\n{node.ToFullString()}.");
        }

        var (block, _) = GetBodies(typedNode);
        var expression = block?.Statements[0] switch
        {
            ReturnStatementSyntax returnStatement => returnStatement.Expression!,
            ExpressionStatementSyntax expressionStatement => expressionStatement.Expression!,
            _ => throw new InvalidOperationException($"Can't extract original expression from {block}")
        };

        return SwitchToThisBodies(typedNode, null, expression).WithoutAnnotations(Marker);
    }

    /// <summary>
    /// Decide whether to inject memoization or not. Inject it if so.
    /// </summary>
    /// <param name="context">Mutation context needed to use Placer</param>
    /// <param name="semanticModel"></param>
    /// <param name="blockBody">body to be memoized</param>
    /// <param name="methodIdentifier">identifier unique to function</param>
    /// <param name="returnType">return type of code block</param>
    /// <param name="inputParameters">non-out parameters used for identifier</param>
    /// <param name="node">node of the body and return type, used for identifier creation</param>
    /// <returns>return possibly memoized version of <paramref name="blockBody"/></returns>
    protected abstract BlockSyntax MemoizeBlock(MutationContext context, SemanticModel semanticModel,
        BlockSyntax blockBody, string methodIdentifier, TypeSyntax returnType, IdentifierNameSyntax[] inputParameters);

    /// <summary>
    /// Calls the placer to inject memoization in the code block. This can be called from MemoizeBlock if T needs memoization.
    /// </summary>
    ///
    ///
    ///
    ///
    ///
    /// <returns>an body with memoization injected</returns>
    protected BlockSyntax InjectMemoization(MutationContext context, SemanticModel semanticModel, BlockSyntax blockBody,
        string methodIdentifier, TypeSyntax returnType, IdentifierNameSyntax[] inputParameters) =>
        context.Placer.PlaceMemoizationControlledMutations(
            semanticModel,
            blockBody,
            methodIdentifier,
            returnType,
            inputParameters
        );

    /// <inheritdoc/>
    protected override T InjectMutations(T sourceNode, T targetNode, SemanticModel semanticModel,
        MutationContext context)
    {

        static bool IsJsonSerializable(Type type)
        {
            try
            {
                var obj = Activator.CreateInstance(type);
                JsonSerializer.Serialize(obj, type);
                return true;
            }
            catch
            {
                return false;
            }
        }
        var (blockBody, expressionBody) = GetBodies(targetNode);

        if (expressionBody == null && blockBody == null)
        {
            // no implementation provided
            return targetNode;
        }

        var wasInExpressionForm = GetBodies(sourceNode).expression != null;
        var returnType = ReturnType(sourceNode);
        var parameters = ParameterList(sourceNode)?.Parameters ?? _emptyParameterList;

        var outParams = parameters.Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)))
            .ToList();
        var refParams = parameters.Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword)))
            .ToList();
        var inParams = parameters.Where(p => !p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)))
            .ToList();

        var methodMemoizationId =
            $"{sourceNode.SyntaxTree.GetLineSpan(sourceNode.Span).StartLinePosition}" +
            $"__" +
            $"{semanticModel.GetEnclosingSymbol(sourceNode.SpanStart)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}" +
            $"__";
        var methodReturnMemoizationId = methodMemoizationId + "RETURN";

        // no mutations to inject
        if (!context.HasLeftOverMutations)
        {
            if (blockBody == null)
            {
                //  TODO we could inject memoization to expression bodies instead of block bodies also
                // we can't do any other injection
                return targetNode;
            }


            var originalBody = blockBody;
            // inject default initializers (if any)
            blockBody = MutantPlacer.InjectOutParametersInitialization(blockBody, parameters);

            if (!wasInExpressionForm)
            {
                // add ending return (to mitigate compilation error due to control flow change)
                // not needed for an expression form method as no control flow may be present
                blockBody = MutantPlacer.AddEndingReturn(blockBody, returnType);
            }


            // TODO Memoize out vars (IGNORE FOR NOW)
            if (outParams is { Count: > 0 }  || refParams is { Count: > 0 })
            {
                //todo
                // If id.... set out params to its memoization value.
                // Last instance of setting the out param, Save its value (var stmt like) in memoization

                // out var en return val kunnen ook ene niet andere wel gememoized zijn.
            }
            // sourceNode.SyntaxTree.GetRoot().DescendantNodes().Select(n => )

            if (refParams.Count > 0)
            {
                Console.Write("");
            }
            if (!returnType.IsVoid()
                && !sourceNode.DescendantNodes().OfType<YieldStatementSyntax>().Any()
                && outParams.Count == 0
                && refParams.Count == 0) //Assuming Stryker will not inject Yields
            {
                // Input: (in)Params, other variables, code location+method.
                var df = semanticModel.AnalyzeDataFlow(GetBodies(sourceNode).block).ReadInside;
                var allParameters = inParams
                    // .Where(p => semanticModel.GetTypeInfo(p).Type.)
                    .Select(p => IdentifierName(p.Identifier.Text))
                    .Cast<ExpressionSyntax>().ToArray();

                if (!IsStatic(sourceNode))
                {
                    allParameters = allParameters.Append(ThisExpression()).ToArray();

                }
                //Ignored vvoor nu, data in exact (method calls ook, niet te herkennen)
                // df.Where(s => s.Name != "this" && s.Name != "value")
                    // .Select(s => IdentifierName(s.Name))
                    // .Concat( inParams.Select(p => IdentifierName(p.Identifier.Text)).ToArray());



                //todo make syntax factory code that takes all (hopefully) args/parameters into account for value
                var memoizationIdentifier = MutantPlacer.MemoizationInstrumentationEngine
                    .GenerateMemoId(methodReturnMemoizationId, allParameters, context.Placer._injection);
                var lambdaExpr = UtilityFunctions.WrapInLambda(blockBody);
                var mutantIds = targetNode.GetDescendantMutantIds().ToList();
                var invocation = MutantPlacer.MemoizationInstrumentationEngine
                    .RetrieveMemoizationExpression(
                        memoizationIdentifier, lambdaExpr,
                        UtilityFunctions.WrapInLambda(MutantPlacer.MemoizationInstrumentationEngine.AnyActiveMutantsCheck(mutantIds, context.Placer._injection)),
                        returnType, context.Placer._injection);

                blockBody = Block(ReturnStatement(invocation.WithLeadingTrivia(Space)));
            }


            // do we need to change the body
            return originalBody == blockBody
                ? targetNode
                : SwitchToThisBodies(targetNode, MutantPlacer.AddEndingReturn(blockBody, returnType), null);
        }

        targetNode = ConvertToBlockBody(targetNode, returnType);

        var newBody = MutantPlacer.InjectOutParametersInitialization(
            context.InjectMutations(GetBodies(targetNode).block, GetBodies(sourceNode).expression,
                !returnType.IsVoid()),
            parameters);

        targetNode = SwitchToThisBodies(targetNode, newBody, null);
        return targetNode;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="localFunction"></param>
    /// <param name="semanticModel"></param>
    /// <returns></returns>
    private string GetFullyQualifiedName(SyntaxNode localFunction, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(localFunction);
        if (symbol == null)
        {
            //todo Randomized string?
            return string.Empty;
        }

        // Start with the local function name
        var nameParts = new List<string> { symbol.Name };

        // Traverse the containing symbols (e.g., methods, classes, namespaces)
        var containingSymbol = symbol.ContainingSymbol;
        while (containingSymbol != null)
        {
            nameParts.Insert(0, containingSymbol.Name);
            containingSymbol = containingSymbol.ContainingSymbol;
        }

        // Join the parts with dots to form the fully qualified name
        return string.Join("::", nameParts.Where(part => !string.IsNullOrEmpty(part)).Distinct());
    }
}
