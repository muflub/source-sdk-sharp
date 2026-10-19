using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase A1: docs/engineapi-areas.tsv rules every slot
/// the SERVER fills (and every ManagedApi slot server native code names) to
/// the interface it moves into. Phase C moves slots class by class off this
/// table, so a slot with no row, two rows, or a row on the wrong side is a slot
/// that would be ported wrong or not at all.
///
/// Row kinds: <c>root</c> (IShimRoot), <c>shared</c> (a base both DLLs will
/// implement: its slots must be filled on BOTH sides), <c>ext</c> and
/// <c>server</c> (server-only classes: SERVER-only slots).
/// </summary>
public partial class EngineApiAreasTests
{

    [Fact]
    public void EveryServerFilledEngineSlotHasExactlyOneRow()
    {
        var sides = EngineApiCensusTests.Sides();
        var rows = Rows().Where(r => r.Struct == "EngineApi").ToList();

        var served = sides.Where(kv => kv.Value != EngineApiCensusTests.Side.ClientOnly)
                          .Select(kv => kv.Key).ToHashSet();

        // An anti-vacuous floor, not a count: the slots leave as their work
        // goes managed (300 at the entity physics init's ABI, 2026-10-12;
        // 250 -> 200 when plan_server_native_v2 T2 deleted the dead ones).
        // A floor for the reader: 200 -> 150 as the port folds slots (199 at 2026-10-19).
        Assert.True(served.Count > 150, $"the census found the server's slots ({served.Count})");
        Assert.DoesNotContain(served, s => rows.Count(r => r.Slot == s) != 1);
        // A client-only row (plan_client_vtables CC) is the kind rule's to check.
        Assert.Empty(rows.Where(r => r.Kind != "client" && !served.Contains(r.Slot)).Select(r => r.Slot));
    }

    [Fact]
    public void EveryRowsKindAgreesWithTheSideThatFillsIt()
    {
        var sides = EngineApiCensusTests.Sides();
        var wrong = new List<string>();

        foreach (var r in Rows().Where(r => r.Struct == "EngineApi"))
        {
            var want = r.Kind switch
            {
                "shared" or "root" => EngineApiCensusTests.Side.Both,
                "client" => EngineApiCensusTests.Side.ClientOnly,   // plan_client_vtables CC
                _ => EngineApiCensusTests.Side.ServerOnly,
            };

            if (!sides.TryGetValue(r.Slot, out var side) || side != want)
                wrong.Add($"{r.Slot} ({r.Interface}, {r.Kind})");
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The table's <c>suppress</c> column and what compiled agree, for every
    /// slot. The compiled side is the slot's field in its area's forwarder
    /// struct (<c>Shim&lt;X&gt;Fwd</c> in SharedForwarders.g.cs, since
    /// plan_forwarders_everywhere.md), read from metadata: the convention is a
    /// custom modifier, so it goes through GetModifiedFieldType.
    /// </summary>
    [Fact]
    public void EverySuppressedSlotIsMarkedSuppressedAndNoOtherIs()
    {
        var rows = Rows().Where(r => r.Struct == "EngineApi").ToList();
        var missing = rows.Where(r => LayoutField(r) is null).Select(r => $"{r.Slot} ({r.Interface})").ToList();
        var suppressed = rows.Where(r => LayoutField(r) is { } f && IsSuppressed(f)).Select(r => r.Slot).ToHashSet();

        Assert.True(rows.Count > 400, $"compared every slot's row ({rows.Count})");
        Assert.Empty(missing);
        Assert.True(suppressed.Count > 0, $"read the suppressed layout fields ({suppressed.Count})");
        Assert.Empty(rows.Where(r => r.Suppress != suppressed.Contains(r.Slot)).Select(r => r.Slot));
    }

    /// <summary>
    /// plan_client_vtables CE-last deleted the ManagedApi struct, so no row
    /// names it: every managed slot native calls is a row of
    /// docs/managed-interfaces.tsv. The control: the reader sees the engine rows.
    /// </summary>
    [Fact]
    public void NoRowNamesTheDeletedManagedStruct()
    {
        var rows = Rows();

        Assert.True(rows.Count(r => r.Struct == "EngineApi") > 400, "read the table's engine rows");
        Assert.Empty(rows.Where(r => r.Struct == "ManagedApi").Select(r => r.Slot));
    }

    /// <summary>
    /// plan_engineapi_vtables C6a: the table carries each slot's managed
    /// signature, which tools/shim-areas types the generated area calls with,
    /// while tools/vtable-layout types the layout field from the C++ header.
    /// The two must agree slot by slot (the vtable's object pointer aside), or
    /// the typed call's cast hides a mismatch: an <c>int</c> read out of a slot
    /// that returns a <c>float</c> compiles. The one licensed difference is
    /// a typed managed pointer, <c>nint</c> or <c>IntPtr</c> where the layout
    /// says <c>void*</c>: the same width, typed on the managed side only.
    /// Until CC-last this compared the column with the struct's field.
    /// </summary>
    [Fact]
    public void TheManagedSignatureAgreesWithTheLayout()
    {
        var wrong = new List<string>();
        int checkedTypes = 0;

        foreach (var r in Rows().Where(r => r.Struct == "EngineApi"))
        {
            var field = LayoutField(r);
            if (field is null)
            {
                wrong.Add($"{r.Slot}: no field in {LayoutName(r.Interface)}");
                continue;
            }

            var layout = field.FieldType.GetFunctionPointerParameterTypes().Skip(1)
                              .Append(field.FieldType.GetFunctionPointerReturnType())
                              .Select(CSharpName).ToList();
            var managed = r.Managed.Split(',').Select(t => t.Trim()).ToList();

            checkedTypes++;
            if (managed.Count != layout.Count || managed.Zip(layout).Any(p => !SameType(p.First, p.Second)))
                wrong.Add($"{r.Slot}: managed {r.Managed}, layout {string.Join(", ", layout)}");
        }

        Assert.True(checkedTypes > 400, $"compared {checkedTypes} signatures");
        Assert.Empty(wrong);
    }

    private static bool SameType(string managed, string layout)
        => managed == layout
        || (layout == "void*" && (managed.EndsWith('*') || managed is "nint" or "IntPtr"));

    private static string CSharpName(Type t) => t switch
    {
        { IsPointer: true } => CSharpName(t.GetElementType()!) + "*",
        _ when t == typeof(void) => "void",
        _ when t == typeof(int) => "int",
        _ when t == typeof(uint) => "uint",
        _ when t == typeof(float) => "float",
        _ when t == typeof(byte) => "byte",
        _ when t == typeof(nint) => "nint",
        _ => t.Name,
    };

    /// <summary>
    /// plan_client_vtables CB, CC: the client implements each shared base and
    /// each client-only area by forwarding to a body. Since CC-last there is no
    /// struct fill to compare with, so every client forward must call exactly
    /// the body its row names (client_body where the client's differs).
    /// </summary>
    [Fact]
    public void EveryClientForwardCallsItsRowsBody()
    {
        string forwards = File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "shell_shim_areas_client.inc"));
        var calls = Regex.Matches(forwards, @"\bCShim\w+::(\w+)\([^)]*\)\s*\n\s*\{ return ::(\w+)\(")
                         .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var rows = Rows().Where(r => r.Struct == "EngineApi" && r.Kind is "shared" or "client"
                                     && r.Interface is not ("IShimConsole" or "IShimRoot")).ToList();
        Assert.True(rows.Count >= 170, $"{rows.Count} client-served rows");
        Assert.True(calls.Count == rows.Count, $"{calls.Count} forwards for {rows.Count} rows");
        Assert.Contains(rows, r => r.ClientBody.Length > 0);   // the control: some bodies differ

        var wrong = rows.Where(r => !calls.TryGetValue(r.Slot, out var f)
                                    || f != (r.ClientBody.Length > 0 ? r.ClientBody : r.Body))
                        .Select(r => $"{r.Slot}: forwards to {(calls.TryGetValue(r.Slot, out var f) ? f : "nothing")}, " +
                                     $"the table says {(r.ClientBody.Length > 0 ? r.ClientBody : r.Body)}")
                        .ToList();
        Assert.Empty(wrong);
    }

    /// <summary>
    /// plan_client_vtables CB2, retargeted for the retired router (docs/todo.md)
    /// and for the per-area classes (plan_shim_areas_split phase 2): every shared
    /// slot (bar the root, which is not an area) is reached on the client through
    /// an area the client's container holds, whose class (or the base it derives)
    /// calls the slot through its own forwarder table. Since CC-last deleted the
    /// struct, that is the only way the client reaches it.
    /// </summary>
    [Fact]
    public void EverySharedSlotRoutesThroughTheClientsAreaOnTheClient()
    {
        string generated = File.ReadAllText(RepoLayout.Repo("src", "abi", "ShimAreas.g.cs"));
        // Every generated class body, by name, with the class it derives.
        var classes = new Dictionary<string, (string? Base, List<string> Lines)>();
        string? current = null;
        foreach (string line in generated.Split('\n'))
        {
            var head = Regex.Match(line, @"^internal (?:sealed )?unsafe class (\w+)(?: : (\w+))?");
            if (head.Success)
            {
                current = head.Groups[1].Value;
                classes[current] = (head.Groups[2].Success ? head.Groups[2].Value : null, new List<string>());
            }
            else if (current is not null)
                classes[current].Lines.Add(line);
        }
        Assert.True(classes.ContainsKey("ClientShimAreas"), "the generated file holds ClientShimAreas");
        var client = classes["ClientShimAreas"].Lines;
        var held = client.Select(l => Regex.Match(l, @"^    public readonly (\w+) (\w+) = null!;"))
                         .Where(m => m.Success)
                         .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
        Assert.True(held.Count >= 20, $"the client container holds {held.Count} areas");

        bool Calls(string? cls, string slot)
        {
            for (; cls is not null; cls = classes[cls].Base)
                if (classes[cls].Lines.Any(l => l.StartsWith("    public ", StringComparison.Ordinal)
                                                && l.Contains($" {slot}(", StringComparison.Ordinal)
                                                && l.Contains($"_shim.Vt->{slot}(_shim.Self", StringComparison.Ordinal)))
                    return true;
            return false;
        }

        var shared = Rows().Where(r => r.Struct == "EngineApi" && r.Side == "both" && r.Interface != "IShimRoot").ToList();
        // A floor against a parse that stopped early: 50 -> 40 when the four
        // game-file read slots folded (2026-10-19; 46 read).
        Assert.True(shared.Count >= 40, $"{shared.Count} shared slots");

        // Since plan_shim_areas_split phase 9 the container has no calls of its
        // own: a shared slot is routed when an area the client holds calls it.
        var unrouted = shared.Where(r => !held.Values.Any(area => Calls(area, r.Slot)))
                             .Select(r => r.Slot).ToList();

        Assert.Empty(unrouted);
    }

}
