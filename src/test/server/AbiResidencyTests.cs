using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The census of the real tree, measured once for the class that holds it
/// (an xUnit class fixture: no static, so no state outlives the class).
/// <see cref="Consumers"/> is the census's most expensive input, the names
/// every real consumer tier uses; planted measurements reuse it. The real
/// rulings, graph and partial groups are one measurement, taken on first use.
/// </summary>
public sealed class AbiCensusFixture
{
    private readonly Lazy<(IReadOnlyList<AbiResidency.Ruling> Rulings,
                           IReadOnlyDictionary<string, HashSet<string>> Graph,
                           IReadOnlyList<IReadOnlySet<string>> Partials)> _real;

    public AbiCensusFixture()
    {
        Consumers = AbiResidency.ReadConsumers();
        _real = new(() =>
        {
            var rulings = AbiResidency.Measure(out var graph, out var partials, [], consumers: Consumers);
            return (rulings, graph, partials);
        });
    }

    internal IReadOnlyList<(string Tier, HashSet<string> Words)> Consumers { get; }
    internal IReadOnlyList<AbiResidency.Ruling> Rulings => _real.Value.Rulings;
    internal IReadOnlyDictionary<string, HashSet<string>> Graph => _real.Value.Graph;
    internal IReadOnlyList<IReadOnlySet<string>> Partials => _real.Value.Partials;
}

/// <summary>Planted Abi trees for the census's arms that the real tree no longer exercises.</summary>
internal static class AbiResidencyPlanting
{
    /// <summary>A directory of planted Abi files (namespace SourceSharp.Abi), each named by its type.</summary>
    internal static string PlantAbi(params (string Type, string Body)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"abi-census-abi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var (type, body) in files)
            File.WriteAllText(Path.Combine(dir, Path.ChangeExtension(type, "cs")), "namespace SourceSharp.Abi;\n" + body);
        return dir;
    }

    internal static List<AbiResidency.Ruling> NotPlumbing(IEnumerable<AbiResidency.Ruling> rulings)
        => rulings.Where(r => r.Kind != "plumbing").ToList();
}

/// <summary>
/// plan_abi_hotreload §2 / §5 row 12: the census of SourceSharp.Abi
/// (<see cref="AbiResidency"/>), held to docs/abi-residency.tsv and seeded from
/// docs/abi-plumbing.tsv, and the count of files that are not plumbing, which
/// may only fall and ends at 0 when Phase 1 is done.
/// The census's facts are four classes so xUnit runs them side by side (one
/// class was 25 facts, 14.7 s, one after another): this one holds the real
/// table, <see cref="AbiResidencyRuleTests"/> the rules that need no
/// measurement, and the two planted classes the arms the real tree no
/// longer reaches.
/// </summary>
public class AbiResidencyTests(AbiCensusFixture census) : IClassFixture<AbiCensusFixture>
{
    /// <summary>
    /// The files that are not binding plumbing (moves + stays + stuck) at the
    /// last measurement: Phase 1's work queue. LOWER IT as files leave; a new
    /// non-plumbing file in Abi raises the count and fails this.
    /// </summary>
    // 175 at the census rewrite (plan_abi_hotreload §5 row 12, 2026-10-02):
    // 87 moves, 3 stay (named by plumbing), 85 stuck (22 roots named by the
    // DebugApi assemblies, which see only Abi and the contracts). Measured.
    // 143 after Phase 1 wave 1 (2026-10-02): 32 files left for their
    // destinations; 55 moves, 3 stay, 85 stuck. Measured.
    // 130 after wave 2 (2026-10-02): 13 more left; 42 moves, 3 stay, 85
    // stuck. Measured.
    // 123 after wave 3 (2026-10-02): 7 more left; 35 moves, 3 stay, 85
    // stuck. Measured.
    // 120 after wave 4 (2026-10-02): HeardSound, MapLoad, StageUniqueness;
    // 32 moves, 3 stay, 85 stuck. Measured.
    // 119 after wave 5 (2026-10-02): SoundType; 31 moves (10 unused, 21
    // held: blocked by an unused file, a partial or a ruling), 3 stay, 85
    // stuck. Measured.
    // 115 after the census's blind spots were closed (2026-10-02): partial types
    // credit every file with a part (and the parts move together), a
    // qualified SourceSharp.Abi.X counts as naming X, and docs/abi-edges.tsv
    // supplies the reflection and generator edges source cannot show. 4 files
    // turned out plumbing (TempEntSpawn.Prepared with its other half, and what
    // it names); 15 movers turned out stuck behind EngineServices' Areas()
    // reflection. 12 move, 3 stay, 100 stuck. Measured.
    // 110 (2026-10-02, integrator): ThirdPersonCamera to Client.Interop, Angles,
    // UserMessageSchema and EventCatalog to Shared, AnimListTables deleted (only
    // its own tests named it). Measured.
    // 110 (2026-10-02, plan_devapi_interfaces P5): the dev API names no Abi
    // type and its tiers are gone, and the census no longer reads a consumer's
    // own method calls or its own declared types as Abi names. Every stuck
    // file is re-ruled: 107 move, 3 stay, 0 stuck (was 12 move, 3 stay, 95
    // stuck at the start of P3/P4 in this lane, 100 stuck at P0). The total is
    // unchanged: nothing left Abi, the files are now free to. Measured.
    // 104 (2026-10-02, Phase 1 second run, wave 1): CollisionRules,
    // LocalCommandLine, UserMessage and UserMessageKey to Shared, NetTraffic to
    // Server, PredictionErrors to Client. 101 move, 3 stay. Measured.
    // 103 (2026-10-02, wave 2): EngineCollisionGroup to Shared. 100 move,
    // 3 stay. Measured.
    // 102 (2026-10-02, run 2): RecordingEngine, the census's one "unused"
    // mover, to the test-only SourceSharp.TestSupport (the user's ruling).
    // 99 move, 3 stay. Measured.
    // 101 (2026-10-02, run 2): PredictionJudge to Client (the integrator's
    // ruling: rename detection carries debug-regressions' additive edit).
    // 98 move, 3 stay. Measured.
    // 100 (2026-10-02, run 2): PrecacheAttribute to Shared, its sweep split
    // to the server (ServerPrecache). 97 move, 3 stay. Measured.
    // 99 (2026-10-02, run 2): StringTableRegistry to Shared, its create path
    // split to Server.Interop (StringTableCreation). 96 move, 3 stay. Measured.
    // 97 (2026-10-02, run 2, the integrator's ruling 4): ScopedBindings split
    // out of EngineStateScope.cs as plumbing (c), EntityOp and Transform ruled
    // b; the three stays are gone and EngineStateScope moves. 97 move, 0 stay.
    // Measured.
    // 96 (2026-10-02, run 2): EngineStateScope, "unused" (only the tests open
    // it), to SourceSharp.TestSupport beside RecordingEngine. 96 move. Measured.
    // 95 (2026-10-02, run 3): EngineEffectsForwarder to Server beside its one
    // subclass, once SideReach treats a pure forwarder as pass-through (the
    // integrator's ruling: no split of IEngineEffects). 95 move. Measured.
    // 97 (2026-10-02, run 3, RAISED by 2 with a reason): the integrator's
    // ruling (a) splits one-side classes out of shared files so each can move
    // to its side: OverlapReader.cs (out of Overlap.cs, to Server) and
    // StandaloneSlotReference.cs (out of StandaloneMirror.cs, to Client). Two
    // new files, nothing new landed in Abi; the drain takes the count to 0.
    // 98 (2026-10-02, run 3, RAISED by 1 with a reason): NetStateServerBinding.cs,
    // the server's blob-bind arm split out of NetState.cs and NetStateBinding.cs
    // (ruling (a), seam B2). A new file, nothing new landed in Abi.
    // 99 (2026-10-02, run 3, RAISED by 1 with a reason): EntityIoQueue.cs, the
    // server's event queue and connections split out of EntityIo.cs's shared
    // vocabulary (ruling (a), seams E2/E4). A new file, nothing new in Abi.
    // 100 (2026-10-02, run 3, RAISED by 1 with a reason): ServerEntityEngine.cs,
    // the server registry's engine needs past the areas, so the registry (server
    // game code) holds no EngineSide. A new file, nothing new in Abi.
    // 76 (2026-10-02, run 3, move M1): the one-side files leave for their side
    // once the cycle is cut (3d8bb74b..64fece86): the server entity cluster
    // (ScriptedEntity, EntityDirectory, IEntityDirectory, EntityRef,
    // EntityOpMask, ThinkScheduler, EntityIoQueue, OverlapReader) to Server,
    // ServerEntityEngine and NetStateServerBinding to Server.Interop, the
    // client fetches and StandaloneSlotReference to Client.Interop, the save
    // and reload vocabulary to Shared, MovementBridge to Shared.Interop.
    // 24 files. Measured.
    // 75 (2026-10-02, run 3): SteamGameServerFlat ruled plumbing (c): its
    // interface is fetched when Steam activates the server, once per server
    // start, so a reloaded copy would read null until the next activation.
    // Measured.
    // 15 (2026-10-02, run 3, move M2, wave A): the engine surface, its
    // vocabulary and the netcode leave for Shared.Interop (60 files), and
    // EventIdStamps is split out of EntityIo.cs as plumbing (c). Measured.
    // 0 (2026-10-02, run 3, move M3): the last fifteen (AmmoType, ClientReplay,
    // Color32, EngineBlockMarks, EntityVocabulary, IInterpolationBlocks,
    // IRenderPolicyBlocks, Movement, MoveMath, NameMatches, Overlap,
    // ParentProbe, StandaloneBones, TemplateSpawn, VariantValue) to
    // Shared.Interop. The ceiling became NoFileInAbiIsNotPlumbing below: Abi
    // holds plumbing only, and a file that is not plumbing is a red, not a
    // count to ratchet.


    private static readonly string[] Kinds = { "plumbing", "stays", "stuck", "moves", "delete" };

    [Fact]
    public void TheCensusRulesEveryKind()
    {
        var r = census.Rulings;
        int files = Directory.GetFiles(RepoLayout.Repo("src", "abi"), "*.cs").Length;
        Assert.True(files > 0 && r.Count == files, $"every Abi file is ruled ({r.Count} of {files})");
        Assert.All(r, x => Assert.Contains(x.Kind, Kinds));

        // Each seed route and each outcome is exercised by the real tree, so a
        // broken arm shows as a missing kind rather than a quieter table.
        Assert.Contains(r, x => x.Why == "a: the generated boundary");
        Assert.Contains(r, x => x.Why.StartsWith("a: named by ", StringComparison.Ordinal));
        Assert.Contains(r, x => x.Kind == "plumbing" && x.Why[0] is 'b' or 'c' or 'd' or 'e');
        // No real file stays since the integrator's ruling 4 (ScopedBindings
        // split out as plumbing; EntityOp and Transform ruled b): the stays
        // arm is exercised by a planted seed in
        // A_planted_delete_row_is_ruled_delete_and_keeps_nothing's control.
        Assert.DoesNotContain(r, x => x.Kind == "stays");
        // No real consumer is stuck since plan_devapi_interfaces P5: the
        // stuck arms are exercised by a planted tier (below).
        Assert.DoesNotContain(r, x => x.Kind == "stuck");
        // No real row is a delete target since Engine.cs was deleted (Phase 1):
        // the delete arm is exercised by a planted row (below).
        Assert.DoesNotContain(r, x => x.Kind == "delete");
        // No real file moves since P1 run 3 drained Abi: the moves arm is
        // exercised by planted Abi files (NoFileInAbiIsNotPlumbing's control,
        // the stuck, delete and engine-side-holder facts).
        Assert.DoesNotContain(r, x => x.Kind == "moves");
    }

    /// <summary>
    /// The census's invariant: Abi cannot reference out, so nothing a file that
    /// stays in Abi names (by source, partial part or a docs/abi-edges.tsv
    /// row) may be ruled to move.
    /// </summary>
    [Fact]
    public void NothingAFileThatStaysNamesMoves()
    {
        var graph = census.Graph;
        var rulings = census.Rulings.ToDictionary(r => r.File, StringComparer.Ordinal);
        int edges = 0;

        // The listed edges added here as well as by the census, so a census
        // that stopped reading them would be caught rather than agreed with.
        var all = graph.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (from, to, _) in AbiResidency.Edges().Where(e => e.From.EndsWith(".cs", StringComparison.Ordinal)))
            all[from].Add(to);

        foreach (var (from, targets) in all)
        {
            if (rulings[from].Kind is "moves" or "delete")
                continue;
            foreach (string to in targets)
            {
                edges++;
                Assert.True(rulings[to].Kind != "moves",
                            $"{from} ({rulings[from].Kind}) names {to}, which is ruled to move to {rulings[to].Destination}");
            }
        }

        // 78 at plan_devapi_interfaces P5 (2026-10-02): the 95 stuck files no
        // longer stay, so fewer edges leave staying files. Measured.
        Assert.True(edges > 70, $"{edges} edges out of staying files were checked");
    }

    /// <summary>A partial type cannot span assemblies: its files share one ruling.</summary>
    [Fact]
    public void EveryPartOfAPartialTypeIsRuledAlike()
    {
        var partials = census.Partials;
        var rulings = census.Rulings.ToDictionary(r => r.File, StringComparer.Ordinal);
        Assert.NotEmpty(partials);

        foreach (var parts in partials)
        {
            var seen = parts.Select(p => (rulings[p].Kind, rulings[p].Destination)).Distinct().ToList();
            Assert.True(seen.Count == 1,
                        $"the parts {string.Join(", ", parts)} are ruled {string.Join(" / ", seen)}");
        }
    }

    [Fact]
    public void TheTableIsCurrent()
    {
        string path = RepoLayout.Repo("docs", "abi-residency.tsv");
        string table = AbiResidency.Table(census.Rulings);

        if (Environment.GetEnvironmentVariable("ABI_RESIDENCY_WRITE") == "1")
        {
            File.WriteAllText(path, table);
            return;
        }

        Assert.True(File.Exists(path), "docs/abi-residency.tsv is written (tools/abi-residency)");
        Assert.True(File.ReadAllText(path) == table,
            "docs/abi-residency.tsv is stale: run tools/abi-residency and read the diff");
    }
}

/// <summary>The census's rules that read the tree's files but measure nothing.</summary>
public class AbiResidencyRuleTests
{
    [Fact]
    public void TheAbiSourcesCompileOnTheirOwn()
    {
        // The graph is only as good as the binding: a file whose names do not
        // resolve has no edges, and would read as staying.
        var (count, first) = AbiResidency.CompileErrors();
        Assert.True(count == 0, $"{count} error(s) binding src/abi alone:\n{first}");
    }

    /// <summary>
    /// Each destination arm, from the consumer tiers alone. The real tree
    /// exercised them while Phase 1's queue was full; as files leave, a
    /// destination's last mover goes and its arm would go unread.
    /// </summary>
    [Theory]
    [InlineData("Server", false, "Server")]
    [InlineData("Client", false, "Client")]
    [InlineData("Server.Interop", false, "Server.Interop", "Server")]
    [InlineData("Client.Interop", false, "Client.Interop")]
    [InlineData("Shared", false, "Server", "Client")]
    [InlineData("Shared", false, "Shared", "Server.Interop")]
    [InlineData("Shared.Interop", false, "Shared.Interop", "Client")]
    [InlineData("Server.Interop", true, "Server")]
    [InlineData("Client.Interop", true, "Client")]
    [InlineData("Shared.Interop", true, "Server", "Client")]
    [InlineData("none: Debug.Api.Server+Server", false, "Server", "Debug.Api.Server")]
    public void EachDestinationIsPickedForTheTiersItServes(string expected, bool pointerCode, params string[] tiers)
        => Assert.Equal(expected, AbiResidency.Pick(new HashSet<string>(tiers, StringComparer.Ordinal), pointerCode));

    [Fact]
    public void EveryPlumbingRowNamesAnAbiFile()
    {
        var rows = AbiResidency.Plumbing();
        Assert.True(rows.Count > 50, $"docs/abi-plumbing.tsv was read ({rows.Count} rows)");

        foreach (var (file, (rule, reason)) in rows)
        {
            Assert.True(File.Exists(RepoLayout.Repo("src", "abi", file)), $"docs/abi-plumbing.tsv names {file}, which is not in src/abi");
            Assert.True(rule is "b" or "c" or "d" or "e" or "x", $"{file}: rule '{rule}' is not b-e or x (rule a is derived, never listed)");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{file} has no reason");
        }
    }

    /// <summary>
    /// How a consumer's names are read: a qualified SourceSharp.Abi.X names X,
    /// while the member half of x.Y and an argument label name nothing.
    /// </summary>
    [Fact]
    public void AConsumersNamesAreItsTypePositions()
    {
        var words = AbiResidency.NamesIn(
            "class C { void M() { var a = global::SourceSharp.Abi.Foo.Bar; var b = SourceSharp.Abi.Qux.Run(); " +
            "var c = thing.Baz; Call(label: 1); Widget w = null; } }");

        Assert.Contains("Foo", words);
        Assert.Contains("Qux", words);
        Assert.Contains("Widget", words);
        Assert.DoesNotContain("Bar", words);
        Assert.DoesNotContain("Baz", words);
        Assert.DoesNotContain("label", words);
    }

    /// <summary>
    /// The census's three word-match false positives (plan_devapi_interfaces
    /// P5): a call to a consumer's own method (DebugApiServer's ConVar and
    /// NetTable) and a use of a type the same file declares (DebugChannelListener's
    /// Sink) name no Abi type. A type the file does not declare, and one it
    /// constructs, still count.
    /// </summary>
    [Fact]
    public void A_consumers_own_methods_and_types_are_not_names()
    {
        var words = AbiResidency.NamesIn(
            "class C { string ConVar(string s) => s; void M() { ConVar(\"x\"); NetTable<int>(); var s = new Sink(); " +
            "Widget w = new Gadget(); Gizmo.Run(); } T NetTable<T>() => default!; private sealed class Sink { } }");

        Assert.DoesNotContain("ConVar", words);
        Assert.DoesNotContain("NetTable", words);
        Assert.DoesNotContain("Sink", words);
        Assert.Contains("Widget", words);
        Assert.Contains("Gadget", words);
        Assert.Contains("Gizmo", words);
    }

    [Fact]
    public void EveryEdgeNamesAnAbiFile()
    {
        // Empty since P1 run 3: its four rows were EngineServices.Areas()'s
        // reflection edges, and both ends left Abi together. The file and its
        // header are still read; a row whose file is gone fails below.
        Assert.True(File.Exists(RepoLayout.Repo("docs", "abi-edges.tsv")), "docs/abi-edges.tsv is there to read");
        var edges = AbiResidency.Edges();
        var tiers = new[] { "Shared.Interop", "Shared", "Server.Interop", "Client.Interop", "Server", "Client" };

        foreach (var (from, to, how) in edges)
        {
            Assert.True(File.Exists(RepoLayout.Repo("src", "abi", to)), $"docs/abi-edges.tsv: {to} is not in src/abi");
            Assert.True(from.EndsWith(".cs", StringComparison.Ordinal)
                            ? File.Exists(RepoLayout.Repo("src", "abi", from))
                            : tiers.Contains(from),
                        $"docs/abi-edges.tsv: '{from}' is neither an Abi file nor a tier");
            Assert.False(string.IsNullOrWhiteSpace(how), $"{from} -> {to} says nothing of how");
        }
    }
}

/// <summary>The stuck and delete arms, on planted Abi files.</summary>
public class AbiResidencyPlantedArmTests(AbiCensusFixture census) : IClassFixture<AbiCensusFixture>
{
    /// <summary>
    /// The stuck arms, on a planted consumer no destination serves (P5): the
    /// Abi file it names is stuck "named by" that tier, and what that file
    /// names is stuck behind it.
    /// </summary>
    [Fact]
    public void A_consumer_no_destination_serves_sticks_what_it_names_and_what_that_names()
    {
        // Planted Abi files since P1 run 3 drained the real movers: A names B.
        string abi = AbiResidencyPlanting.PlantAbi(("PlantedA", "public class PlantedA { public PlantedB B = new(); }"),
                              ("PlantedB", "public class PlantedB { }"));
        string dir = Path.Combine(Path.GetTempPath(), $"abi-census-planted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // A source file of its own, outside the tree (named by extension, not by a literal
            // the suite's file-name check would look for in the tree).
            File.WriteAllText(Path.Combine(dir, Path.ChangeExtension("Planted", "cs")), "class P { PlantedA a; }");
            var r = AbiResidency.Measure(out _, out _, [("Planted", [dir])], plantedAbi: abi, consumers: census.Consumers)
                                .ToDictionary(x => x.File, StringComparer.Ordinal);

            string a = Path.ChangeExtension("PlantedA", "cs"), b = Path.ChangeExtension("PlantedB", "cs");
            Assert.Equal(("stuck", "named by Planted"), (r[a].Kind, r[a].Why));
            Assert.Equal(("stuck", "named by stuck " + a), (r[b].Kind, r[b].Why));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(abi, recursive: true);
        }
    }

    /// <summary>
    /// The delete arm, on a planted row (Phase 1 deleted the last real one,
    /// Engine.cs): a file ruled x is a delete target, and what it names is not
    /// kept by it. The control plants the same file as a seed instead, and what
    /// it names then stays "named by plumbing" it.
    /// </summary>
    [Fact]
    public void A_planted_delete_row_is_ruled_delete_and_keeps_nothing()
    {
        // Planted Abi files since P1 run 3 drained the real movers: from names to.
        string abi = AbiResidencyPlanting.PlantAbi(("PlantedFrom", "public class PlantedFrom { public PlantedTo To = new(); }"),
                              ("PlantedTo", "public class PlantedTo { }"));
        try
        {
            string from = Path.ChangeExtension("PlantedFrom", "cs"), to = Path.ChangeExtension("PlantedTo", "cs");
            var real = AbiResidency.Measure(out var graph, out _, [], plantedAbi: abi, consumers: census.Consumers).ToDictionary(x => x.File, StringComparer.Ordinal);
            Assert.True(real[from].Kind == "moves" && graph[from].Contains(to), "the planted file moves and names the other");

            var seeded = AbiResidency.Measure(out _, out _, [], [(from, "c", "planted seed")], abi, census.Consumers)
                                     .ToDictionary(x => x.File, StringComparer.Ordinal);
            Assert.Equal(("plumbing", "c: planted seed"), (seeded[from].Kind, seeded[from].Why));
            Assert.Equal(("stays", "named by plumbing " + from), (seeded[to].Kind, seeded[to].Why));

            var deleted = AbiResidency.Measure(out _, out _, [], [(from, "x", "planted delete target")], abi, census.Consumers)
                                      .ToDictionary(x => x.File, StringComparer.Ordinal);
            Assert.Equal(("delete", "x: planted delete target"), (deleted[from].Kind, deleted[from].Why));
            Assert.Equal("moves", deleted[to].Kind);
        }
        finally
        {
            Directory.Delete(abi, recursive: true);
        }
    }
}

/// <summary>Destinations, engine-side holders and the not-plumbing ceiling, on planted Abi files.</summary>
public class AbiResidencyPlantedDestinationTests(AbiCensusFixture census) : IClassFixture<AbiCensusFixture>
{
    /// <summary>
    /// A moving file's destination is visible to every consumer its row names,
    /// and pointer code only goes to an Interop assembly.
    /// </summary>
    [Fact]
    public void EveryDestinationIsVisibleToItsConsumers()
    {
        var seenBy = new Dictionary<string, string[]>
        {
            ["Server"] = new[] { "Server" },
            ["Client"] = new[] { "Client" },
            ["Server.Interop"] = new[] { "Server.Interop", "Server" },
            ["Client.Interop"] = new[] { "Client.Interop", "Client" },
            ["Shared"] = new[] { "Shared", "Server.Interop", "Client.Interop", "Server", "Client" },
            ["Shared.Interop"] = new[] { "Shared.Interop", "Shared", "Server.Interop", "Client.Interop", "Server", "Client" },
        };

        // Since P1 run 3 drained Abi, over planted Abi files and planted
        // consumers: one both sides name (to Shared) and one pointer file the
        // server names (to Server.Interop), beside whatever the real tree has.
        string abi = AbiResidencyPlanting.PlantAbi(("PlantedBoth", "public class PlantedBoth { }"),
                              ("PlantedPointer", "public unsafe class PlantedPointer { public byte* P; }"));
        string server = Path.Combine(Path.GetTempPath(), $"abi-census-sv-{Guid.NewGuid():N}");
        string client = Path.Combine(Path.GetTempPath(), $"abi-census-cl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(client);
        List<AbiResidency.Ruling> moving;
        try
        {
            File.WriteAllText(Path.Combine(server, Path.ChangeExtension("S", "cs")), "class S { PlantedBoth b; PlantedPointer p; }");
            File.WriteAllText(Path.Combine(client, Path.ChangeExtension("C", "cs")), "class C { PlantedBoth b; }");
            moving = AbiResidency.Measure(out _, out _, [("Server", [server]), ("Client", [client])], plantedAbi: abi, consumers: census.Consumers)
                                 .Where(x => x.Kind == "moves" && x.Destination != "unused").ToList();
        }
        finally
        {
            Directory.Delete(abi, recursive: true);
            Directory.Delete(server, recursive: true);
            Directory.Delete(client, recursive: true);
        }

        Assert.True(moving.Count >= 2, $"{moving.Count} moving files with a destination");
        Assert.Contains(moving, m => m.Destination == "Shared");
        Assert.Contains(moving, m => m.Destination == "Server.Interop" && m.Why.EndsWith("; pointer code", StringComparison.Ordinal));

        foreach (var m in moving)
        {
            Assert.True(seenBy.ContainsKey(m.Destination), $"{m.File}: '{m.Destination}' is not a destination");
            string named = m.Why.Split(';')[0];
            if (named.StartsWith("named by ", StringComparison.Ordinal))
                foreach (string tier in named["named by ".Length..].Split(", "))
                    Assert.True(seenBy[m.Destination].Contains(tier), $"{m.File} goes to {m.Destination}, which {tier} cannot see");
            if (m.Why.EndsWith("; pointer code", StringComparison.Ordinal))
                Assert.True(m.Destination.EndsWith(".Interop", StringComparison.Ordinal), $"{m.File} is pointer code bound for {m.Destination}");
        }
    }

    /// <summary>
    /// A mover that holds the engine side goes only to an Interop assembly (P1
    /// run 3): no game assembly may reach EngineSide, and none sees the
    /// internals it is reached through.
    /// </summary>
    [Fact]
    public void AFileThatHoldsTheEngineSideMovesOnlyToAnInterop()
    {
        // Planted since P1 run 3 drained the real holders: a file the server
        // names that holds EngineSide goes to Server.Interop, not Server.
        string abi = AbiResidencyPlanting.PlantAbi(("PlantedHolder", "public class PlantedHolder { internal EngineSide? Side; }"));
        string dir = Path.Combine(Path.GetTempPath(), $"abi-census-server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, Path.ChangeExtension("PlantedServer", "cs")), "class S { PlantedHolder h; }");
            var holder = AbiResidency.Measure(out _, out _, [("Server", [dir])], plantedAbi: abi, consumers: census.Consumers)
                                     .Single(r => r.File == Path.ChangeExtension("PlantedHolder", "cs"));

            Assert.Equal(("moves", "named by Server; holds the engine side", "Server.Interop"),
                         (holder.Kind, holder.Why, holder.Destination));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(abi, recursive: true);
        }

        // And the real table: every holder still in Abi, if any, goes to an Interop.
        var holders = census.Rulings.Where(r => r.Kind == "moves" && r.Why.Contains("holds the engine side", StringComparison.Ordinal));
        Assert.True(holders.All(r => r.Destination.EndsWith(".Interop", StringComparison.Ordinal)),
            "each goes to an Interop assembly: " + string.Join(", ", holders.Select(r => $"{r.File} -> {r.Destination}")));
    }

    [Fact]
    public void NoFileInAbiIsNotPlumbing()
    {
        // The control first: a planted Abi file that no plumbing names is
        // ruled not plumbing, and the same predicate names it.
        string abi = AbiResidencyPlanting.PlantAbi(("PlantedLoose", "public class PlantedLoose { }"));
        try
        {
            var planted = AbiResidencyPlanting.NotPlumbing(AbiResidency.Measure(out _, out _, [], plantedAbi: abi, consumers: census.Consumers));
            Assert.Contains(Path.ChangeExtension("PlantedLoose", "cs"), planted.Select(r => r.File));
        }
        finally
        {
            Directory.Delete(abi, recursive: true);
        }

        var stray = AbiResidencyPlanting.NotPlumbing(census.Rulings);
        Assert.True(census.Rulings.Count > 50, $"the census ruled the real tree ({census.Rulings.Count} files)");
        Assert.True(stray.Count == 0,
            "SourceSharp.Abi holds plumbing only (plan_abi_hotreload §2); not plumbing: "
            + string.Join(", ", stray.Select(r => $"{r.File} ({r.Kind}: {r.Why} -> {r.Destination})")));
    }
}
