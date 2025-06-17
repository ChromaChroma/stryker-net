using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stryker.Core.InjectedHelpers;

public class CodeInjection
{
    // files to be injected into the mutated assembly
    private static readonly string[] Files = {
        "Stryker.Core.InjectedHelpers.MutantControl.cs",
        "Stryker.Core.InjectedHelpers.Coverage.MutantContext.cs",
        "Stryker.Core.InjectedHelpers.MemoizationControl.cs",
    };
    private const string PatternForCheck = "\\/\\/ *check with: *([^\\r\\n]+)";
    private const string MutantContextClassName = "MutantContext";
    private const string StrykerNamespace = "Stryker";
    private static readonly string Selector;
    private static readonly string AnyActiveSelector;
    private static readonly string MemoizationRetrieveSelector;
    private static readonly string MemoizationStoreSelector;
    private static readonly string MemoizationGenerateIdSelector;

    static CodeInjection() //NOSONAR : no way to get read of static constructors
    {
        var helper = GetSourceFromResource("Stryker.Core.InjectedHelpers.MutantControl.cs");
        var extractor = new Regex(PatternForCheck);
        var results = extractor.Matches(helper);
        if (results.Count < 2)
        {
            throw new InvalidDataException("Internal error: failed to find expression for mutant selection.");
        }

        Selector = MemoizationRetrieveSelector = results[0].Groups[1].Value;
        AnyActiveSelector = MemoizationRetrieveSelector = results[1].Groups[1].Value;

        helper = GetSourceFromResource("Stryker.Core.InjectedHelpers.MemoizationControl.cs");
        var results2 = extractor.Matches(helper);
        if (results2.Count < 3)
        {
            throw new InvalidDataException("Internal error: failed to find expression for memoization retrieval and storing.");
        }
        MemoizationRetrieveSelector = results2[0].Groups[1].Value;
        MemoizationStoreSelector = results2[1].Groups[1].Value;
        MemoizationGenerateIdSelector = results2[2].Groups[1].Value;

    }

    public CodeInjection()
    {
        HelperNamespace = GetRandomNamespace();
        SelectorExpression = Selector.Replace(StrykerNamespace, HelperNamespace);
        AnyActiveSelectorExpression = AnyActiveSelector.Replace(StrykerNamespace, HelperNamespace);
        MemoizationRetrieveSelectorExpression = MemoizationRetrieveSelector.Replace(StrykerNamespace, HelperNamespace);
        MemoizationStoreSelectorExpression = MemoizationStoreSelector.Replace(StrykerNamespace, HelperNamespace);
        MemoizationGenerateIdSelectorExpression = MemoizationGenerateIdSelector.Replace(StrykerNamespace, HelperNamespace);

        foreach (var file in Files)
        {
            var fileContents = GetSourceFromResource(file).Replace(StrykerNamespace, HelperNamespace);
            MutantHelpers.Add(file, fileContents);
        }
    }

    public string SelectorExpression { get; }
    public string AnyActiveSelectorExpression { get; }
    public string MemoizationRetrieveSelectorExpression { get; }
    public string MemoizationStoreSelectorExpression { get; }
    public string MemoizationGenerateIdSelectorExpression { get; }
    public string HelperNamespace { get; }

    private static string GetRandomNamespace()
    {
        // Create a string of characters and numbers allowed in the namespace
        const string ValidChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();

        var chars = new char[15];
        for (var i = 0; i < 15; i++)
        {
            chars[i] = ValidChars[random.Next(0, ValidChars.Length)];
        }
        return StrykerNamespace + new string(chars);
    }

    public static string GetRandomVariableName(string prefix = "")
    {
        // Create a string of characters and numbers allowed in the namespace
        const string ValidChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();

        var chars = new char[15];
        chars[0] = ValidChars[random.Next(0, ValidChars.Length-10)]; // First char cannot be number
        for (var i = 1; i < 15; i++)
        {
            chars[i] = ValidChars[random.Next(0, ValidChars.Length)];
        }
        return prefix + new string(chars);
    }

    public IDictionary<string, string> MutantHelpers { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Get a SyntaxNode describing the creation of static tracking object
    /// </summary>
    /// <returns>Returns new Stryker.MutantContext() with proper namespace</returns>
    public ObjectCreationExpressionSyntax GetContextClassConstructor() =>
        SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.IdentifierName(HelperNamespace),
                SyntaxFactory.IdentifierName(MutantContextClassName))
                .WithLeadingTrivia(SyntaxFactory.Space),
            SyntaxFactory.ArgumentList(),
            null);


    /// <summary>
    /// Get a SyntaxNode describing accessing a member of the mutant context class.
    /// </summary>
    /// <param name="member">Desired member</param>
    /// <returns>Returns Stryker.MutantContext.<paramref name="member"/> with proper namespace </returns>
    public MemberAccessExpressionSyntax GetContextClassAccessExpression(string member) =>
        SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(HelperNamespace),
                SyntaxFactory.IdentifierName(MutantContextClassName)),
            SyntaxFactory.IdentifierName(member));


    /// <summary>
    /// Checks if an expression is describing accessing a member of the mutant context class.
    /// </summary>
    /// <param name="memberAccess">expression to analyze</param>
    /// <param name="member">expected member name</param>
    /// <returns>Returns true if the expression is Stryker.MutantContext.<paramref name="member"/> with proper namespace </returns>
    public static bool IsContextAccessExpression(ExpressionSyntax memberAccess, string member) =>
        memberAccess is MemberAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax { Identifier.ValueText: MutantContextClassName }
            },
            Name.Identifier.ValueText: { } specificMember
        } && specificMember == member;

    private static string GetSourceFromResource(string sourceResourceName)
    {
        using var stream = typeof(CodeInjection).Assembly.GetManifestResourceStream(sourceResourceName);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
