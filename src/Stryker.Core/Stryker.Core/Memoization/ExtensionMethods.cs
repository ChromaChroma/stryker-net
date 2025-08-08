using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Stryker.Core.Mutants;

namespace Stryker.Core.Memoization;

public static class ExtensionMethods
{
    public static IEnumerable<string> GetDescendantMutantIds(this SyntaxNode node) => node.DescendantNodesAndSelf()
        .SelectMany(n => n.GetAnnotations(MutantPlacer.MutationMarkers.First()))
        .Select(ann => ann.Data)
        .ToList();
}
