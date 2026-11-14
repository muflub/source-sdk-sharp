using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
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
        public NavNumbers Numbers(float fileJumpHeight) => TF2NavNumbers.Numbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => TF2TeamSolids.Read(entities);
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

    [PerTreeTFNavFact(Map)]
    public void ItsCompressedEntitiesAreReadAsNone() => Assert.NotNull(Loaded.Value.Nav!.Entities.Unread);
}
