// GENERATED FILE CONSUMERS, hand-written. docs/todo.md "Managed code calls the
// engine vtables directly": the end-state these facts pin is that a slot is a
// typed method on the area class that owns it, the attach lives on
// `EngineSide`, and no product code routes through a `Slot_`/`Has_` pair.
//
// Written before the change (test-first, 2026-10-07): every fact here is red
// at the base commit and goes green with the retirement. The rules they encode
// are the item's own "Wanted" paragraph, quoted below.

using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The retirement facts for the shim routing. The callsite walk reads the
/// repository the way the retired router's liveness walk did, so a mention in
/// a comment is never a caller.
/// </summary>
public class ShimAreaTypedCallsTests
{
    /// <summary>Every non-root row of docs/engineapi-areas.tsv, as (slot, kind, side).</summary>
    private static List<(string Slot, string Kind, string Side)> Rows()
    {
        var rows = new List<(string, string, string)>();
        foreach (string line in File.ReadAllLines(RepoLayout.Repo("docs", "engineapi-areas.tsv")).Skip(1))
        {
            if (line.Length == 0)
                continue;
            string[] f = line.Split('\t');
            // slot, struct, interface, kind, base, side
            if (f[1] != "EngineApi" || f[3] == "root")
                continue;
            rows.Add((f[0], f[3], f[5]));
        }
        return rows;
    }

    private static MethodInfo? Method(Type area, string name)
        => area.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Whether one of the areas a side's registration hands declares the slot.
    /// Since plan_shim_areas_split phase 11 nothing holds the areas as a set:
    /// the registration hands each, by type, and a caller is given the area.
    /// </summary>
    private static bool Serves(Action<Action<Type, object>> registration, string slot)
    {
        var types = new List<Type>();
        registration((type, _) => types.Add(type));
        return types.Any(t => Method(t, slot) is not null);
    }

    /// <summary>
    /// "callers use the generated vtable interfaces of each shim area
    /// directly": every slot row is a typed instance method on the area class
    /// that serves it — an area the server's registration hands for the
    /// server's, one the client's hands for the client's, and a both-sides
    /// slot on an area of each.
    /// </summary>
    [Fact]
    public void EverySlotIsATypedMethodOnItsOwningAreaClass()
    {
        var rows = Rows();
        // The positive arm: the walk reaches the rows it claims to check. A
        // floor for the reader, not the population: 500 -> 400 when
        // plan_server_native_v2 T2 deleted the dead slots (499 left).
        Assert.True(rows.Count > 400, $"the tsv walk found {rows.Count} slot rows");

        var missing = new List<string>();
        foreach (var (slot, kind, side) in rows)
        {
            bool serverServes = side == "server" || side == "both";
            bool clientServes = side == "client" || side == "both";
            if (serverServes && !Serves(ServerAreaRegistration.AddDetached, slot))
                missing.Add($"server: {slot} ({kind}/{side})");
            if (clientServes && !Serves(ClientAreaRegistration.AddDetached, slot))
                missing.Add($"client: {slot} ({kind}/{side})");
        }
        Assert.Empty(missing);
    }

    /// <summary>
    /// "with no <c>ShimSlots</c> routing class": the generated file holds the
    /// area classes and no router. The positive arm keeps the walk honest by
    /// counting the typed methods the areas do expose.
    /// </summary>
    [Fact]
    public void GeneratedRoutingClassIsRetired()
    {
        string generated = File.ReadAllText(RepoLayout.Repo("src", "abi", "ShimAreas.g.cs"));
        string code = AbiParityTests.CodeOnly(generated);

        Assert.DoesNotContain("class ShimSlots", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot_", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Has_", code, StringComparison.Ordinal);

        // Positive arms: the areas are real and carry the typed calls.
        Assert.Contains("class ServerAreaRegistration", code, StringComparison.Ordinal);
        Assert.Contains("class ClientAreaRegistration", code, StringComparison.Ordinal);
        Assert.NotNull(Method(typeof(EntitiesServerArea), "EntityGetHealth"));
        Assert.NotNull(Method(typeof(EngineArea), "GetCurrentTime"));
        Assert.True(Serves(ServerAreaRegistration.AddDetached, "GetCurrentTime")
                    && Serves(ClientAreaRegistration.AddDetached, "GetCurrentTime"),
                    "a both-sides slot is on an area each side registers");
    }

    /// <summary>
    /// The attach is <c>EngineSide</c>'s, the side object the areas are
    /// registered with: one <c>AttachOwnShims</c> and one
    /// <c>ShimsAttached</c> per side, and no accessor for a set of areas
    /// (plan_shim_areas_split phase 11).
    /// </summary>
    [Fact]
    public void EngineSideOwnsTheAttach()
    {
        Assert.NotNull(EngineSideType.GetMethod("AttachOwnShims", InstanceBoth));
        Assert.NotNull(EngineSideType.GetProperty("ShimsAttached", InstanceBoth));
        foreach (string gone in new[] { "AttachShims", "AttachClientShims" })
            Assert.Null(EngineSideType.GetMethod(gone, InstanceBoth));
        foreach (string gone in new[] { "ServerAttached", "ClientAttached", "OwnShimsAttached",
                                        "ServerShims", "ClientShims", "OwnShims" })
            Assert.Null(EngineSideType.GetProperty(gone, InstanceBoth));

        // The server's EngineSide attaches the server's areas only: the
        // client's attach is in the client's half (plan_shim_areas_split phase 7).
        using var root = new FakeShimRoot();
        var side = new EngineSide(Side.Server);
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        Assert.NotNull(side.Areas.Resolve(typeof(EntitiesServerArea)));
        Assert.Null(side.Areas.Resolve(typeof(HudClientArea)));
        // A side object is not a router: no Slot_/Has_ members at all.
        Assert.Empty(EngineSideType.GetMethods(InstanceBoth)
            .Where(m => m.Name.StartsWith("Slot_", StringComparison.Ordinal)
                     || m.Name.StartsWith("Has_", StringComparison.Ordinal)
                     || m.Name.StartsWith("get_Has_", StringComparison.Ordinal))
            .Select(m => m.Name));
    }

    private const BindingFlags InstanceBoth = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static Type EngineSideType => typeof(EngineSide);

    /// <summary>
    /// "no pass-through wrappers in EngineSide" with the guards kept: the
    /// attach guard is EngineSide's, and no
    /// product code outside the generated file names a router member.
    /// The control keeps the walk honest: the generated file itself still
    /// compiles typed calls a callsite-walk can see, and EngineSide keeps the
    /// work-doing wrappers (its method count stays in the hundreds).
    /// </summary>
    [Fact]
    public void NoProductCodeRoutesThroughSlotPrefixedNames()
    {
        string root = RepoLayout.Repo();
        string engineSide = RepoLayout.SharedTypeSource("EngineSide");

        Assert.Contains("ShimsAttached", AbiParityTests.CodeOnly(engineSide), StringComparison.Ordinal);

        var routerNames = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file);
            if (rel.StartsWith("src/test", StringComparison.Ordinal)
                || rel.StartsWith("bin", StringComparison.Ordinal)
                || rel.Contains("/obj/", StringComparison.Ordinal)
                || rel.EndsWith("ShimAreas.g.cs", StringComparison.Ordinal))
                continue;
            foreach (Match m in Regex.Matches(AbiParityTests.CodeOnly(File.ReadAllText(file)),
                         @"\b(?:\w+\.)?(Slot_|Has_)[A-Z]\w+"))
                routerNames.Add($"{rel}:{m.Value}");
        }
        Assert.Empty(routerNames);

        // Control: the stimulus reaches the subject — the engine bindings are a
        // large hand-written surface, so an empty walk over vanished files
        // could not pass. The server's (this suite's): since
        // plan_shim_areas_split phase 11 the members that call an area are the
        // bindings', not EngineSide's.
        int bound = EngineServices.Areas(new EngineSide(Side.Server).Log)
            .Sum(a => a.Implementation.GetMethods(InstanceBoth | BindingFlags.DeclaredOnly).Length);
        Assert.True(bound > 400, $"{bound} engine binding methods");
    }

    /// <summary>
    /// The liveness instrument, retargeted from the retired router's walk:
    /// every slot the generator gives the areas a typed call for must have a
    /// product caller outside the generated file and outside src/test. A typed
    /// call nobody makes is dead weight in every side's hot class, and its
    /// native slot travels with it — this is how the retired
    /// <c>TemplateSpawnInstance</c> was found.
    /// </summary>
    [Fact]
    public void EveryTypedAreaCallHasAProductCaller()
    {
        string generatedPath = RepoLayout.Repo("src", "abi", "ShimAreas.g.cs");
        string generated = File.ReadAllText(generatedPath);
        var typed = Regex.Matches(generated, @"^    public \S+ (\w+)\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct().ToList();

        // The positive arm: the walk reaches the typed calls it claims to check.
        Assert.True(typed.Count > 400,
            $"the walk over ShimAreas.g.cs found {typed.Count} typed calls");

        // Product source, comments stripped so a mention in prose is not a
        // caller. A test-only reader does not count: a slot with a reader only
        // in tests is unused.
        var callers = new HashSet<string>();
        string root = RepoLayout.Repo();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file);
            if (rel.StartsWith("src/test", StringComparison.Ordinal)
                || rel.StartsWith("bin", StringComparison.Ordinal)
                || rel.Contains("/obj/", StringComparison.Ordinal)
                || rel.EndsWith("ShimAreas.g.cs", StringComparison.Ordinal))
                continue;
            foreach (Match m in Regex.Matches(AbiParityTests.CodeOnly(File.ReadAllText(file)),
                         @"\.\s*(\w+)\s*\("))
                callers.Add(m.Groups[1].Value);
        }

        // The control for the walk itself: a typed call the product code is
        // known to make, so an empty `callers` cannot pass.
        Assert.Contains("GetCurrentTime", callers);

        var dead = typed.Where(t => !callers.Contains(t)).ToList();
        Assert.Empty(dead);   // names each dead typed call; the list must stay empty
    }
}
