using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>A fact that needs this tree's TF2 navigation of <c>map</c>, which `make ai-nav-tf MAP=map` builds; it skips without it.</summary>
public sealed class PerTreeTFNavFactAttribute : FactAttribute
{
    public PerTreeTFNavFactAttribute(string map)
    {
        if (!File.Exists(TF2Nav.NavPath(map)) || !File.Exists(TF2Nav.BspPath(map)))
            Skip = $"this tree has no {TF2Nav.NavPath(map)} beside {TF2Nav.BspPath(map)}: `make ai-nav-tf MAP={map}` builds it";
    }
}

/// <summary>Where this tree keeps a TF2 map (the content the tf2 mod mounts) and its navigation (the mod's own maps/).</summary>
internal static class TF2Nav
{
    public static string NavPath(string map) => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", map + ".nav3d");

    public static string BspPath(string map) => RepoLayout.Repo("bin", PerTreeNav.Config, "content", "tf", "maps", map + ".bsp");
}

/// <summary>
/// ctf_2fort's per-tree navigation through the loader the TF2 binding runs (plan_bots_teams_tf2
/// P6 step 2): bound to the shipped map, derived by TF2's numbers, with the Scout's double jump
/// a reach of its own; the map's compressed entity lump is read as none and says so.
/// </summary>
public sealed class TF2NavTests
{
    private const string Map = "ctf_2fort";

    private sealed class TF2Profile : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => TFNavProfile.ModNumbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => TFTeamSolids.Read(entities);
    }

    private static readonly Lazy<NavLoadResult> Loaded = new(() => NavLoader.Load(
        Map, p => File.ReadAllBytes(p.EndsWith(".bsp", StringComparison.Ordinal) ? TF2Nav.BspPath(Map) : TF2Nav.NavPath(Map)), null, new TF2Profile()));

    [PerTreeTFNavFact(Map)]
    public void TheLoaderLoadsItBoundToTheShippedMap()
    {
        Assert.Null(Loaded.Value.Refusal);
        Assert.True(Loaded.Value.Nav!.Nav.Graph.CellCount > 10_000, $"{Loaded.Value.Nav.Nav.Graph.CellCount} cells");
    }

    [PerTreeTFNavFact(Map)]
    public void ItIsDerivedByTF2sNumbersWithDoubleJumps()
    {
        LoadedNav nav = Loaded.Value.Nav!;
        Assert.Equal(16, nav.NumbersKey.Length);
        int doubles = Enumerable.Range(0, nav.Nav.Graph.CellCount).Sum(c => nav.Nav.Graph.EdgesFrom(c).ToArray().Count(e => e.Type == NavEdgeType.DoubleJump));
        Assert.True(doubles > 0, "no double-jump edge");
    }

    /// <summary>
    /// Round 2's S2: TF2's numbers have the Atomizer's third jump, so the shipped map has triple-jump edges, each a rise past
    /// the double jump's 97.2 and up to 142.2.
    /// </summary>
    [PerTreeTFNavFact(Map)]
    public void ItHasTripleJumpsBetweenTheDoubleJumpsReachAndTheTriples()
    {
        NavGraph g = Loaded.Value.Nav!.Nav.Graph;
        var triples = Enumerable.Range(0, g.CellCount).SelectMany(c => g.EdgesFrom(c).ToArray()).Where(e => e.Type == NavEdgeType.TripleJump).ToArray();
        Assert.True(triples.Length > 0, "no triple-jump edge");
        Assert.All(triples, e => Assert.InRange(g.Cell(new CellId(e.To)).FloorZ - g.Cell(new CellId(e.From)).FloorZ, 97.2f, 142.3f));
    }

    /// <summary>
    /// Each team's spawn has a way to both flags, as a Soldier walks and drops (P6 step 6): live,
    /// before the stairs were places for TF2's wide body each spawn's component held neither flag,
    /// and before a wide body dropped past a ledge's lip a red spawn's way reached 13,792 cells,
    /// all in its own base (<see cref="NavNumbers.WideHull"/>).
    /// </summary>
    [PerTreeTFNavFact(Map)]
    public void EachTeamsSpawnHasAWayToBothFlags()
    {
        NavGraph g = Loaded.Value.Nav!.Nav.Graph;
        // The flags' homes and a spawn of each team, as the live game has them.
        AiVector[] flags = [new(489f, -3348.5f, -131f), new(-488.7f, 3348.5f, -131f)];
        AiVector[] spawns = [new(-1888f, -1280f, 257f), new(1888f, 1368f, 257f)];
        var open = new NavObstacleState(g.ObstacleCount) { OpenDoors = true };
        foreach (AiVector flag in flags)
        {
            var field = new FlowField();
            field.Build(g, g.FindCell(flag), ClearanceClass.Crouch, open);
            foreach (AiVector spawn in spawns)
                Assert.True(float.IsFinite(field.CostAt(g.FindCell(spawn).Index)), $"no way from the spawn at {spawn} to the flag at {flag}");
        }
    }

    /// <summary>
    /// Each spawn has a dry way to both flags, as a Soldier walks, jumps and drops without the
    /// Scout's double jump: over the courtyards and the bridge, not only through the sewers. Built
    /// by map-tools before claude/tf2-nav-exits, the vis clusters' and occluders' brushes walled
    /// each base's front doorway and the sides of the bridge, and the only way between the bases
    /// was 250 cells of water (live: bots swam the moat, and stuck there).
    /// </summary>
    [PerTreeTFNavFact(Map)]
    public void EachTeamsSpawnHasADryWayToBothFlags()
    {
        NavGraph g = Loaded.Value.Nav!.Nav.Graph;
        AiVector[] flags = [new(489f, -3348.5f, -131f), new(-488.7f, 3348.5f, -131f)];
        AiVector[] spawns = [new(-1888f, -1280f, 257f), new(1888f, 1368f, 257f)];
        foreach (AiVector spawn in spawns)
        {
            bool[] dry = DryReach(g, g.FindCell(spawn).Index);
            foreach (AiVector flag in flags)
                Assert.True(dry[g.FindCell(flag).Index], $"no dry way from the spawn at {spawn} to the flag at {flag}");
        }
    }

    /// <summary>The cells reached from <paramref name="from"/> along every edge but a double jump, through no water cell.</summary>
    private static bool[] DryReach(NavGraph g, int from)
    {
        var seen = new bool[g.CellCount];
        var queue = new Queue<int>();
        seen[from] = true;
        queue.Enqueue(from);
        while (queue.TryDequeue(out int c))
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                if (e.Type == NavEdgeType.DoubleJump || seen[e.To] || (g.Cell(e.To).Flags & NavCellFlags.Water) != 0)
                    continue;
                seen[e.To] = true;
                queue.Enqueue(e.To);
            }
        }

        return seen;
    }

    [PerTreeTFNavFact(Map)]
    public void ItsCompressedEntitiesAreReadAsNone() => Assert.NotNull(Loaded.Value.Nav!.Entities.Unread);
}
