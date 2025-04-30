using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.Mutants.CsharpNodeOrchestrators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Stryker.Core.Helpers;
using Stryker.Core.Mutants;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Memoization;

// internal class MethodDeclarationOrchestrator : BaseMethodDeclarationOrchestrator<MethodDeclarationSyntax>
internal class MethodDeclarationOrchestrator : NodeSpecificOrchestrator<MethodDeclarationSyntax, MethodDeclarationSyntax>
{

    // /// <inheritdoc/>
    // /// <remarks>Ensure we return a block after mutants are injected.</remarks>
    protected override MethodDeclarationSyntax InjectMutations(MethodDeclarationSyntax sourceNode, MethodDeclarationSyntax targetNode, SemanticModel semanticModel, MutationContext context) =>
        context.InjectMemoizationMutation(targetNode, sourceNode);

    protected override MutationContext PrepareContext(MethodDeclarationSyntax node, MutationContext context) => base.PrepareContext(node, context.Enter(MutationControl.Block));

    protected override void RestoreContext(MutationContext context) => base.RestoreContext(context.Leave());
    // protected override MethodDeclarationSyntax SwitchToThisBodies(MethodDeclarationSyntax node, BlockSyntax blockBody, ExpressionSyntax expressionBody)
    // {
    //     // Replace the method body with the provided block body
    //     return node.WithBody(blockBody).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
    // }
    //
    // protected override MethodDeclarationSyntax InjectMutations(MethodDeclarationSyntax sourceNode, MethodDeclarationSyntax targetNode, SemanticModel semanticModel, MutationContext context)
    // {
    //     ApplicationLogging.LoggerFactory.CreateLogger<MemoizationMutator>().LogInformation($"BaseMethodDeclarationSyntax: {sourceNode.ToString()}");
    //
    //     var (blockBody, _) = GetBodies(targetNode);
    //
    //     if (blockBody == null)
    //     {
    //         // No block body to mutate
    //         return targetNode;
    //     }
    //
    //     var parameters = ParameterList(sourceNode)?.Parameters ?? SyntaxFactory.SeparatedList<ParameterSyntax>();
    //     var returnType = ReturnType(sourceNode);
    //
    //     // Inject mutations into the block body
    //     var newBody = MutantPlacer.InjectOutParametersInitialization(
    //         context.InjectMutations(blockBody, null, !returnType.IsVoid()),
    //         parameters
        // );
        //
        // return SwitchToThisBodies(targetNode, newBody, null);
    // }
}
