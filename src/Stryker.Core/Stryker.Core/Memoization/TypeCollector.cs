using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Core.MutationTest;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Memoization;

public class TypeCollector(IEnumerable<SemanticModel> semanticModels)
{
    public HashSet<ITypeSymbol> Types { get; } = new(SymbolEqualityComparer.Default);
    public HashSet<SyntaxNode> ErrorTypeNodes { get; } = [];

    public void CollectTypes(SyntaxTree tree) =>
        new SyntaxTreeTypeWalker(semanticModels.First(x => x.SyntaxTree == tree), Types, ErrorTypeNodes)
            .Visit(tree.GetRoot());

    public void CollectTypes(IEnumerable<SyntaxTree> trees)
    {
        var logger = ApplicationLogging.LoggerFactory.CreateLogger<TypeCollector>();
        foreach (var tree in trees)
        {
            var tc = new SyntaxTreeTypeWalker(semanticModels.First(x => x.SyntaxTree == tree), Types, ErrorTypeNodes);
            tc.Visit(tree.GetRoot());
            // logger.LogInformation($"Tc: {tc.Types.Count}");
        }
    }


    private class SyntaxTreeTypeWalker(
        SemanticModel semanticModel,
        HashSet<ITypeSymbol> types,
        HashSet<SyntaxNode> errorTypeNodes) : CSharpSyntaxWalker
    {
        public override void Visit(SyntaxNode node)
        {
            // Ask Roslyn what type this node represents
            var typeInfo = semanticModel.GetTypeInfo(node);

            if (typeInfo.Type != null)
            {
                if (typeInfo.Type.ToDisplayString() == "?")
                {
                    errorTypeNodes.Add(node);
                }
                else
                {
                    types.Add(typeInfo.Type);
                }
            }

            if (typeInfo.ConvertedType != null)
            {
                if (typeInfo.ConvertedType.ToDisplayString() == "?")
                {
                    errorTypeNodes.Add(node);
                }
                else
                {
                    types.Add(typeInfo.ConvertedType);
                }
            }

            // --- For declarations: use GetDeclaredSymbol ---
            switch (node)
            {
                case VariableDeclaratorSyntax variableDecl:
                    if (semanticModel.GetDeclaredSymbol(variableDecl) is ILocalSymbol local)
                        types.Add(local.Type);
                    break;

                case FieldDeclarationSyntax fieldDecl:
                    foreach (var v in fieldDecl.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(v) is IFieldSymbol field)
                            types.Add(field.Type);
                    }

                    break;

                case PropertyDeclarationSyntax propertyDecl:
                    if (semanticModel.GetDeclaredSymbol(propertyDecl) is IPropertySymbol prop)
                        types.Add(prop.Type);
                    break;

                case MethodDeclarationSyntax methodDecl:
                    if (semanticModel.GetDeclaredSymbol(methodDecl) is IMethodSymbol method)
                        types.Add(method.ReturnType);
                    break;

                case ParameterSyntax paramDecl:
                    if (semanticModel.GetDeclaredSymbol(paramDecl) is IParameterSymbol param)
                        types.Add(param.Type);
                    break;

                case DelegateDeclarationSyntax delegateDecl:
                    if (semanticModel.GetDeclaredSymbol(delegateDecl) is INamedTypeSymbol delegateType)
                        types.Add(delegateType);
                    break;

                case ClassDeclarationSyntax classDecl:
                    if (semanticModel.GetDeclaredSymbol(classDecl) is INamedTypeSymbol classType)
                        types.Add(classType);
                    break;

                case StructDeclarationSyntax structDecl:
                    if (semanticModel.GetDeclaredSymbol(structDecl) is INamedTypeSymbol structType)
                        types.Add(structType);
                    break;

                case RecordDeclarationSyntax recordDecl:
                    if (semanticModel.GetDeclaredSymbol(recordDecl) is INamedTypeSymbol recordType)
                        types.Add(recordType);
                    break;

                case InterfaceDeclarationSyntax interfaceDecl:
                    if (semanticModel.GetDeclaredSymbol(interfaceDecl) is INamedTypeSymbol iface)
                        types.Add(iface);
                    break;

                case EnumDeclarationSyntax enumDecl:
                    if (semanticModel.GetDeclaredSymbol(enumDecl) is INamedTypeSymbol enumType)
                        types.Add(enumType);
                    break;
            }

            base.Visit(node);
        }
    }
}
