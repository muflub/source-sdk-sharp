using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Tests;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>A fact over this tree's built ss_ai_tf_double: per-tree build output, so a tree without it skips, naming the command.</summary>
public sealed class DoubleMapFactAttribute : FactAttribute
{
    public DoubleMapFactAttribute()
    {
        if (!File.Exists(JumpDoubleMapTests.NavPath) || !File.Exists(JumpDoubleMapTests.BspPath))
            Skip = $"this tree has no {JumpDoubleMapTests.NavPath}: `make map MAP={JumpDoubleMapTests.Map}` builds it";
    }
}

/// <summary>
/// The link pass on the second rocket's course (lane G7b), as the game runs it: the spawns as its seeds, rounds from where
/// its links land. S1 is one rocket's jump from S0; S2 is past one rocket's rise from S1, over a beam: a link with a second
/// rocket fired at the beam's top.
/// </summary>
public class JumpDoubleMapTests
{
    internal const string Map = "ss_ai_tf_double";

    internal static string NavPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".nav3d");

    internal static string BspPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".bsp");

    /// <summary>The stages' middles and tops, as the map's generator has them (TFAiTestMaps.DoubleStages), and the beam's top.</summary>
    internal static readonly (float X, float Y, float Top)[] Stages = [(576f, 648f, 656f), (576f, 1032f, 848f), (1248f, 1032f, 1424f)];

    internal const float BeamTop = 1232f;

    private const float Half = 128f;

    internal static int StageOf(NavGraph graph, int cell)
    {
        AiVector c = graph.Cell(new CellId(cell)).Centre;
        float floor = graph.Cell(new CellId(cell)).FloorZ;
        for (int s = 0; s < Stages.Length; s++)
        {
            if (MathF.Abs(floor - Stages[s].Top) <= 2f && MathF.Abs(c.X - Stages[s].X) <= Half && MathF.Abs(c.Y - Stages[s].Y) <= Half)
                return s;
        }

        return -1;
    }

    private static readonly Lazy<(NavGraph Graph, JumpPlacesResult Result)> Passed = new(() =>
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
        var pass = new JumpPlaces(graph, columns, Search(columns, walk: walk), walk, Envelope(), JumpPlacesSettings.Of(TFNavProfile.ModNumbers), seeds);
        return (graph, pass.Find());
    });

    [DoubleMapFact]
    public void The_first_jump_is_one_rockets_and_the_second_a_second_rocket_off_the_beam()
    {
        (NavGraph graph, JumpPlacesResult result) = Passed.Value;
        var pairs = result.Links.Select(l => (From: StageOf(graph, l.From), To: StageOf(graph, l.To), l.Family)).ToList();
        Assert.True(pairs.Any(p => p.From == 0 && p.To == 1 && p.Family != JumpFamily.Double), $"no one-rocket link from S0 to S1; the links: {string.Join(", ", pairs)}");
        JumpLink second = Assert.Single(result.Links, l => StageOf(graph, l.From) == 1 && StageOf(graph, l.To) == 2);
        Assert.Equal(JumpFamily.Double, second.Family);
        Assert.Equal(BeamTop, Assert.NotNull(second.Recipe.Second).Over.Z, 0);
        Assert.True(second.Robustness >= new JumpSearchSettings().Keep);
    }

    [DoubleMapFact]
    public void No_link_lands_on_the_beam_or_skips_a_stage()
    {
        (NavGraph graph, JumpPlacesResult result) = Passed.Value;
        foreach (JumpLink l in result.Links)
        {
            int from = StageOf(graph, l.From), to = StageOf(graph, l.To);
            Assert.True(from >= 0 && to >= 0, $"a link from cell {l.From} (stage {from}) to {l.To} (stage {to}, floor {graph.Cell(new CellId(l.To)).FloorZ}): off the stages");
            Assert.True(to <= from + 1, $"a link from stage {from} skips to {to}");
        }
    }
}
