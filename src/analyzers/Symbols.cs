using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SourceSharp.Analyzers;

/// <summary>Symbol helpers the analyzers share: names as the CLR spells them, attributes by name.</summary>
internal static class Symbols
{
    /// <summary>The CLR's <c>Type.FullName</c> of an unconstructed type: namespace, then each enclosing type by metadata name, nested ones after '+'.</summary>
    public static string ClrFullName(INamedTypeSymbol? type)
    {
        if (type is null)
            return "";

        var sb = new StringBuilder(type.MetadataName);

        for (INamedTypeSymbol? at = type.ContainingType; at is not null; at = at.ContainingType)
            sb.Insert(0, at.MetadataName + "+");

        for (INamespaceSymbol? ns = type.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
            sb.Insert(0, ns.MetadataName + ".");

        return sb.ToString();
    }

    public static bool HasAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().Any(a => ClrFullName(a.AttributeClass) == fullName);

    /// <summary>A type the compiler made, or one inside such a type: [CompilerGenerated] on it or an enclosing type.</summary>
    public static bool IsCompilerGenerated(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? at = type; at is not null; at = at.ContainingType)
        {
            if (HasAttribute(at, "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
                return true;
        }

        return false;
    }

    public static Location LocationOf(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
}
