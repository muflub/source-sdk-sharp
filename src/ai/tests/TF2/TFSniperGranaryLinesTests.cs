using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.ServerNav;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The long-line pass on a shipped map (round2.md §5 "Sniper"): on cp_granary, with this tree's navigation, the spots a red
/// Sniper finds onto the middle point stand on lines past the near table's reach. Skipped, visibly, in a tree without the map's navigation (`make ai-nav-tf`).
/// </summary>
public sealed class TFSniperGranaryLinesTests
{
    private const string Map = "cp_granary";

    private sealed class Profile : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => TFNavProfile.ModNumbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => TFTeamSolids.Read(entities);
    }

    private static readonly Lazy<(LoadedNav Nav, NavSight Sight)> Loaded = new(() =>
    {
        NavLoadResult r = NavLoader.Load(Map, p => File.ReadAllBytes(p.EndsWith(".bsp", StringComparison.Ordinal) ? TF2Nav.BspPath(Map) : TF2Nav.NavPath(Map)),
                                         null, new Profile());
        Assert.True(r.Nav is not null, r.Refusal);
        return (r.Nav!, NavSight.Build(r.Nav!.Nav.Graph, r.Nav.File.Space));
    });

    /// <summary>The control point nearest the points' middle (granary's middle point), and the red team's spawns' middle.</summary>
    private static (AiVector Point, AiVector RedSpawn) Places(LoadedNav nav)
    {
        var points = nav.Entities.All.Where(e => e.ClassName == "team_control_point").Select(e => e.Origin).ToList();
        Assert.True(points.Count >= 3, $"{points.Count} control points");
        var mean = new AiVector(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        AiVector middle = points.OrderBy(p => p.DistanceTo(mean)).First();
        var red = nav.Entities.All.Where(e => e.ClassName == "info_player_teamspawn" && e.Team == 2).Select(e => e.Origin).ToList();
        Assert.NotEmpty(red);
        return (middle, new AiVector(red.Average(p => p.X), red.Average(p => p.Y), red.Average(p => p.Z)));
    }

    [PerTreeTFNavFact(Map)]
    public void TheMiddlePointsSpotsStandOnLongLines()
    {
        (LoadedNav nav, NavSight sight) = Loaded.Value;
        (AiVector point, AiVector redSpawn) = Places(nav);
        var spots = TFSniperSpots.Find(nav.Nav.Graph, sight, null, point, redSpawn, attack: true);
        Assert.NotEmpty(spots);
        float farthest = spots.Max(s => s.At.DistanceTo(point));
        Assert.True(farthest > TFSniperSpots.FarthestUnits, $"farthest spot {farthest:F0} u; spots {string.Join(", ", spots.Select(s => $"{s.At.DistanceTo(point):F0}"))}");
    }
}
