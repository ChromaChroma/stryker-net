using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Core.Mutators;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Memoization;

public class MemoizationMutator : MutatorBase<MethodDeclarationSyntax>
{
    private readonly ILogger _logger;
    public MemoizationMutator()
    {
        _logger = ApplicationLogging.LoggerFactory.CreateLogger<MemoizationMutator>();
    }

    public override MutationLevel MutationLevel => MutationLevel.Basic;

    public override IEnumerable<Mutation> ApplyMutations(MethodDeclarationSyntax node, SemanticModel semanticModel)
    {
        if (node.Body is null || !node.Body.Statements.Any())
        {
            yield break; // No mutations to apply
        }

        // // Create memoization logic
        // var memoizationCode = ParseStatement(@"
        //     if (MemoizationCache.TryGetValue(key, out var cachedValue))
        //     {
        //         return cachedValue;
        //     }
        // ");
        // Create an if-else statement
        // var condition = ParseExpression("1>0"); // Replace with your condition
        // var ifStatement = IfStatement(
        //     condition,
        //     Block(ParseStatement("Console.WriteLine(\"Condition met\");")),
        //     ElseClause(
        //         Block(ParseStatement("Console.WriteLine(\"Condition not met\");")))
        // );

        //
        // // Inject the memoization logic at the beginning of the method
        // var newStatements = node.Body.Statements.Insert(0, ifStatement);
        // var mutatedBody = node.Body.WithStatements(newStatements);
        // var mutatedNode = node.WithBody(mutatedBody);
        //
        // // Yield the mutation
        yield return new Mutation
        {
            OriginalNode = node.Body,
            ReplacementNode = LiteralExpression(SyntaxKind.StringLiteralExpression, Literal($"1234abcd"))
                // ParseStatement($"var  default")
                // .WithLeadingTrivia(Tab)
                // .WithTrailingTrivia(CarriageReturnLineFeed)
                // node.WithBody(
                // node.Body.WithStatements(node.Body.Statements.Insert(0, ParseStatement(";")
                //     .WithLeadingTrivia(Tab)
                //     .WithTrailingTrivia(CarriageReturnLineFeed))))
            ,
            DisplayName = "Memoization Injection",
            Type = Mutator.Memoization,
            Description = "Adds memoization logic to the method"
        };


        // yield return new Mutation
        // {
        //     OriginalNode = node,
        //     ReplacementNode = node,
        //     DisplayName = @"Statement mutation",
        //     Type = Mutator.Statement
        // };


        // // yield break;
        //
        // // Create an if-else statement
        // var condition = SyntaxFactory.ParseExpression("1>0"); // Replace with your condition
        // var ifStatement = SyntaxFactory.IfStatement(
        //     condition,
        //     SyntaxFactory.Block(SyntaxFactory.ParseStatement("Console.WriteLine(\"Condition met\");")),
        //     SyntaxFactory.ElseClause(
        //         SyntaxFactory.Block(SyntaxFactory.ParseStatement("Console.WriteLine(\"Condition not met\");")))
        // );
        //
        // // Prepend the if-else statement to the method's body
        // var newStatements = node.Body.Statements.Insert(0, ifStatement);
        // var mutatedBody = node.Body.WithStatements(newStatements);
        // // Create a mutated method
        // // var mutatedNode = node.WithBody(mutatedBody);
        //
        // var mutatedNode = node.WithBody(Block());
        //
        // // _logger.LogInformation($"Mutated:\n {node.ToString()}");
        // _logger.LogInformation($"Mutated:\n {mutatedNode.ToString()}");



        // // Add the using directive for System at the root
        // var root = node.SyntaxTree.GetRoot();
        // var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName(" System")); // Fixed space
        // var newRoot = root is CompilationUnitSyntax compilationUnit
        //     ? compilationUnit.AddUsings(usingDirective)
        //     : root;
        //
        // // Replace the syntax tree with the updated root
        // var updatedSyntaxTree = node.SyntaxTree.WithRootAndOptions(newRoot, node.SyntaxTree.Options);



        // _logger.LogInformation($"Mutated:\n {node.SyntaxTree.GetRoot().ToString()}");
        // _logger.LogInformation($"Mutated:\n {updatedSyntaxTree.GetRoot().ToString()}");

        // // Yield the mutation for adding the using directive
        // yield return new Mutation
        // {
        //     OriginalNode = root,
        //     ReplacementNode = newRoot,
        //     DisplayName = "Add Using Directive",
        //     Type = Mutator.Memoization,
        //     Description = "Adds a 'using System;' directive to the root of the file"
        // };
        // Yield the mutation
        // yield return new Mutation
        // {
        //     OriginalNode = node,
        //     ReplacementNode = node.WithBody(node.Body),
        //     DisplayName = "If-Else Memoization",
        //     Type = Mutator.Statement,
        //     Description = "Injects an if-else statement for memoization at the beginning of the method"
        // };


    }

    // private SyntaxNode MemoizeReturnStatements(IEnumerable<SyntaxNode> stmts)
    // {
    //     foreach (var stmt in stmts)
    //     {
    //         // if (stmt.GetType() is ReturnStatementSyntax)
    //         // {
    //         //     continue;
    //         // }
    //
    //     }
    //     return stmts
    // }
}
