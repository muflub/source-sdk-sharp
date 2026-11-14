using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Tests;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>A fact over this tree's built ss_ai_tf_wall: per-tree build output, so a tree without it skips, naming the command.</summary>
public sealed class WallMapFactAttribute : FactAttribute
{
    public WallMapFactAttribute()
    {
        if (!File.Exists(JumpWallMapTests.NavPath) || !File.Exists(JumpWallMapTests.BspPath))
            Skip = $"this tree has no {JumpWallMapTests.NavPath}: `make map MAP={JumpWallMapTests.Map}` builds it";
    }
}

/// <summary>
/// The link pass on the wall jump's course (lane G7c), as the game runs it: ss_ai_tf_double's stages (the spawns on S0, S1
/// one rocket's jump north of it, S2 past one rocket's rise east of S1) with no beam, and a wall south of the way from S1 to
/// S2: a link whose second rocket is fired at the wall; and with that family switched off, no link from S1 to S2.
/// </summary>
public class JumpWallMapTests
{
    internal const string Map = "ss_ai_tf_wall";

    internal static string NavPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".nav3d");

    internal static string BspPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".bsp");

    private static (NavGraph Graph, JumpPlacesResult Result) Pass(Func<JumpSearchSettings, JumpSearchSettings>? settings = null)
    {
        var source = new Nav3dNavSource(Nav3dReader.Open(File.ReadAllBytes(NavPath)));
        NavGraph graph = NavGraphFromNav3d.Derive(source, TFNavProfile.ModNumbers, NavEdgeCosts.Default, []).Graph;
        VoxelColumns columns = VoxelColumns.FromNav(source);
        MapEntities entities = MapEntities.Read(File.ReadAllBytes(BspPath));
        var seeds = new List<int>();
        foreach (MapEntity e in entities.All.Where(e => e.ClassName == "info_player_teamspawn"))
        {
            if (NavPoints.CellNear(graph, e.Origin, ClearanceClass.Stand) is { IsValid: true } cell)
                seeds.Add(cell.Index);
        }

        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        JumpSearchSettings s = settings?.Invoke(new JumpSearchSettings()) ?? new JumpSearchSettings();
        var pass = new JumpPlaces(graph, columns, new JumpSearch(Flier(columns), walk, s), walk, Envelope(), JumpPlacesSettings.Of(TFNavProfile.ModNumbers), seeds);
        return (graph, pass.Find());
    }

    private static readonly Lazy<(NavGraph Graph, JumpPlacesResult Result)> Passed = new(() => Pass());

    [WallMapFact]
    public void The_second_jump_is_a_second_rocket_at_the_wall()
    {
        (NavGraph graph, JumpPlacesResult result) = Passed.Value;
        var pairs = result.Links.Select(l => (From: JumpDoubleMapTests.StageOf(graph, l.From), To: JumpDoubleMapTests.StageOf(graph, l.To), l.Family)).ToList();
        Assert.True(pairs.Any(p => p.From == 0 && p.To == 1 && p.Family is JumpFamily.Up or JumpFamily.Forward), $"no one-rocket link from S0 to S1; the links: {string.Join(", ", pairs)}");
        JumpLink second = Assert.Single(result.Links, l => JumpDoubleMapTests.StageOf(graph, l.From) == 1 && JumpDoubleMapTests.StageOf(graph, l.To) == 2);
        Assert.Equal(JumpFamily.Wall, second.Family);
        Assert.True(Assert.NotNull(second.Recipe.Second).Wall);
        Assert.True(second.Robustness >= new JumpSearchSettings().Keep);
    }

    [WallMapFact]
    public void With_the_wall_family_given_no_flights_nothing_makes_the_second_jump()
    {
        (NavGraph graph, JumpPlacesResult result) = Pass(s => s with { WallMaxFlights = 0 });
        Assert.Contains(result.Links, l => JumpDoubleMapTests.StageOf(graph, l.From) == 0 && JumpDoubleMapTests.StageOf(graph, l.To) == 1);
        Assert.DoesNotContain(result.Links, l => JumpDoubleMapTests.StageOf(graph, l.To) == 2);
        Assert.Contains(result.Candidates, c => JumpDoubleMapTests.StageOf(graph, c.From) == 1 && JumpDoubleMapTests.StageOf(graph, c.To) == 2);
    }
}
