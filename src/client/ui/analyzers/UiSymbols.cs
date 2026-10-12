using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>How the analyzer recognises the toolkit's types: by full name, so it needs no reference to them.</summary>
internal static class UiSymbols
{
    public const string Frame = "SourceSharp.Client.Ui.Toolkit.UiFrame";
    public const string Context = "SourceSharp.Client.Ui.Toolkit.UiContext";
    public const string Color = "SourceSharp.Client.Ui.UiColor";
    public const string Category = "SourceSharp.Ui";

    public static string Name(ITypeSymbol? t) => t?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "") ?? "";

    /// <summary>
    /// A describe method: one that builds UI into a frame, i.e. takes a
    /// <c>ref UiFrame</c> (Describe itself, a panel's Describe, a widget
    /// extension method, a screen's helper). The frame's own members and the
    /// context's are the toolkit's plumbing and are not describe methods.
    /// </summary>
    public static bool IsDescribe(ISymbol? symbol)
    {
        if (symbol is not IMethodSymbol m)
            return false;
        string owner = Name(m.ContainingType);
        if (owner == Frame || owner == Context)
            return false;
        foreach (var p in m.Parameters)
            if (p.RefKind == RefKind.Ref && Name(p.Type) == Frame)
                return true;
        return false;
    }

    /// <summary>
    /// Code that only runs when names are recorded (previews and tests), under
    /// <c>if (ui.Naming)</c> or <c>if (... RecordNames)</c>, or that throws:
    /// allowed to allocate.
    /// </summary>
    public static bool IsExempt(IOperation op)
    {
        for (var p = op.Parent; p is not null; p = p.Parent)
        {
            if (p is IThrowOperation)
                return true;
            if (p is IConditionalOperation c && Mentions(c.Condition, "Naming", "RecordNames") && !Mentions(c.WhenFalse, op))
                return true;
        }
        return false;
    }

    private static bool Mentions(IOperation? condition, params string[] names)
    {
        if (condition is null) return false;
        foreach (var d in condition.DescendantsAndSelf())
            if (d is IPropertyReferenceOperation pr && System.Array.IndexOf(names, pr.Property.Name) >= 0)
                return true;
        return false;
    }

    private static bool Mentions(IOperation? branch, IOperation target)
    {
        if (branch is null) return false;
        foreach (var d in branch.DescendantsAndSelf())
            if (d == target) return true;
        return false;
    }
}
