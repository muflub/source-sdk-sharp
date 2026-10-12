using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="EngineApiAreasTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class EngineApiAreasTests
{
    internal sealed record Row(string Slot, string Struct, string Interface, string Kind, string Base, string Side, bool Suppress, string Body, string Managed, string ClientBody);

    internal static List<Row> Rows()
    {
        string path = RepoLayout.Repo("docs", "engineapi-areas.tsv");
        Assert.True(File.Exists(path), $"the slot table at {path}");

        var lines = File.ReadAllLines(path);
        Assert.Equal("slot\tstruct\tinterface\tkind\tbase\tside\tsuppress\tbody\tmanaged\tclient_body", lines[0]);

        return lines.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var c = l.Split('\t');
            Assert.True(c.Length == 10, $"ten columns: {l}");
            return new Row(c[0], c[1], c[2], c[3], c[4], c[5], c[6] == "S", c[7], c[8], c[9]);
        }).ToList();
    }

    /// <summary>
    /// The generated layout of an area (tools/vtable-layout names it
    /// <c>Shim&lt;X&gt;Vtbl</c> after <c>IShim&lt;X&gt;</c>), or null.
    /// </summary>
    internal static Type? Layout(string iface)
        => typeof(ShimRootVtbl).Assembly.GetType("SourceSharp.Abi." + LayoutName(iface));

    private static string LayoutName(string iface) => "Shim" + iface["IShim".Length..] + "Vtbl";

    /// <summary>A row's field in its area's generated layout, or null.</summary>
    private static FieldInfo? LayoutField(Row r) => Layout(r.Interface)?.GetField(r.Slot);

    /// <summary>
    /// A former engine API slot's field in its area's generated layout, found
    /// through its row; null when the table has no row for it.
    /// </summary>
    internal static FieldInfo? SlotField(string slot)
        => Rows().Where(r => r.Struct == "EngineApi" && r.Slot == slot)
                 .Select(LayoutField).FirstOrDefault();

    /// <summary>Whether a layout field carries [SuppressGCTransition].</summary>
    internal static bool IsSuppressed(FieldInfo f)
        => f.GetModifiedFieldType() is { IsFunctionPointer: true } t
        && t.GetFunctionPointerCallingConventions().Any(c => c.Name == "CallConvSuppressGCTransition");
}
