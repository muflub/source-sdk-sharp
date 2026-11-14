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

/// <summary>A fact over this tree's built ss_ai_tf_course: per-tree build output, so a tree without it skips, naming the command.</summary>
public sealed class CourseMapFactAttribute : FactAttribute
{
    public CourseMapFactAttribute()
    {
        if (!File.Exists(JumpCourseMapTests.NavPath) || !File.Exists(JumpCourseMapTests.BspPath))
            Skip = $"this tree has no {JumpCourseMapTests.NavPath}: `make map MAP={JumpCourseMapTests.Map}` builds it";
    }
}

/// <summary>
/// The link pass on the jump course (lane G7a), as the game runs it: the graph TF2 derives, the spawns as its seeds, the
/// pass in rounds from where its links land. The course is a row of stages each reached only by a jump from the one
/// before, so the pass must go on a round a stage: it stopped after four (<see cref="JumpPlacesSettings.MaxRounds"/>),
/// and the last two stages had no link.
/// </summary>
public class JumpCourseMapTests
{
    internal const string Map = "ss_ai_tf_course";

    internal static string NavPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".nav3d");

    internal static string BspPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".bsp");

    /// <summary>The stages' platform tops and their middles, as the map's generator has them (TFAiTestMaps.CourseStages).</summary>
    internal static readonly (float X, float Y, float Top)[] Stages =
    [
        (192f, 304f, 656f), (576f, 368f, 756f), (960f, 464f, 956f), (1616f, 464f, 956f), (1616f, 848f, 1256f), (1616f, 1664f, 1256f), (1232f, 1664f, 1606f),
    ];

    /// <summary>A platform's half side: the stages are 256 u (S1 320 across y: within this of its middle along the course).</summary>
    private const float Half = 160f;

    /// <summary>The stage a cell of <paramref name="graph"/> stands on, or -1.</summary>
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

    [CourseMapFact]
    public void Every_jump_of_the_course_has_a_link_from_its_stage_to_the_next_found_from_the_spawns()
    {
        (NavGraph graph, JumpPlacesResult result) = Passed.Value;
        var pairs = result.Links.Select(l => (From: StageOf(graph, l.From), To: StageOf(graph, l.To))).ToList();
        for (int jump = 1; jump < Stages.Length; jump++)
            Assert.True(pairs.Contains((jump - 1, jump)), $"no link from stage {jump - 1} to {jump}; the links: {string.Join(", ", pairs)}");
    }

    [CourseMapFact]
    public void No_link_skips_a_stage_and_none_leaves_the_pit()
    {
        (NavGraph graph, JumpPlacesResult result) = Passed.Value;
        foreach (JumpLink l in result.Links)
        {
            int from = StageOf(graph, l.From), to = StageOf(graph, l.To);
            Assert.True(from >= 0 && to >= 0, $"a link from cell {l.From} (stage {from}) to {l.To} (stage {to}): off the stages");
            Assert.True(to <= from + 1, $"a link from stage {from} skips to {to}");
        }
    }
}
