using SourceSharp.MapFormats.Nav;
using Xunit;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// ss_ai_tdm's per-tree navigation (plan_bots_teams_tf2 B.8): four spawns a base, every
/// team spawn reaches the other base and the mid room's pickups by a standing route,
/// and there are two routes between the bases, one through each row's corners (the
/// deathmatch spawns stand in them), neither much longer than the best.
/// </summary>
public sealed class SsAiTdmNavTests
{
    private const string Map = "ss_ai_tdm";

    private const ClearanceClass Class = ClearanceClass.Crouch;

    private static readonly Lazy<(DerivedNav Nav, MapEntities Entities)> Loaded = new(() =>
    {
        byte[] bsp = File.ReadAllBytes(PerTreeNav.BspPath(Map));
        Nav3dReader reader = Nav3dReader.Open(File.ReadAllBytes(PerTreeNav.NavPath(Map)));
        MapEntities entities = MapEntities.Read(bsp);
        return (NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), NavNumbers.Stock(reader.JumpHeight), NavEdgeCosts.Default,
                                         entities.Hurt, entities.LadderClimbs), entities);
    });

    private static CellId[] Cells(string className)
        => [.. Loaded.Value.Entities.All.Where(e => e.ClassName == className).Select(e => NavPoints.CellNear(Loaded.Value.Nav.Graph, e.Origin, Class))];

    private static FlowField From(CellId cell)
    {
        var field = new FlowField();
        field.Build(Loaded.Value.Nav.Graph, cell, Class, new NavObstacleState(Loaded.Value.Nav.Graph.ObstacleCount));
        return field;
    }

    [PerTreeNavFact(Map)]
    public void EachBaseHasFourTeamSpawnsOnTheNav()
    {
        CellId[] combine = Cells("info_player_combine"), rebel = Cells("info_player_rebel");
        Assert.Equal((4, 4), (combine.Length, rebel.Length));
        Assert.All(combine.Concat(rebel), c => Assert.True(c.IsValid));
    }

    [PerTreeNavFact(Map)]
    public void EveryTeamSpawnReachesTheOtherBaseAndTheMidPickups()
    {
        CellId[] rebel = Cells("info_player_rebel");
        CellId[] targets = [.. Cells("info_player_combine"), .. Cells("weapon_ar2"), .. Cells("item_healthkit"), .. Cells("item_battery")];
        Assert.Equal(7, targets.Length);
        foreach (CellId target in targets)
        {
            FlowField f = From(target);
            Assert.All(rebel, r => Assert.True(float.IsFinite(f.CostAt(r.Index)), $"a rebel spawn cannot reach cell {target}"));
        }
    }

    [PerTreeNavFact(Map)]
    public void TwoRoutesJoinTheBasesThroughEachRowsCorners()
    {
        FlowField fromCombine = From(Cells("info_player_combine")[0]), fromRebel = From(Cells("info_player_rebel")[0]);
        float best = fromCombine.CostAt(Cells("info_player_rebel")[0].Index);
        Assert.True(float.IsFinite(best));
        // One deathmatch spawn of each corner room's three: the middle one of each, by x then y.
        MapEntity[] all = [.. Loaded.Value.Entities.Spawns];
        Assert.Equal(12, all.Length);
        // A room's three share an x and stand 80 u apart in y: runs of them, by x, split where y jumps.
        var corners = new List<MapEntity>();
        foreach (IGrouping<float, MapEntity> column in all.GroupBy(e => e.Origin.X))
        {
            MapEntity[] byY = [.. column.OrderBy(e => e.Origin.Y)];
            for (int i = 0; i < byY.Length; i += 3)
                corners.Add(byY[i + 1]);
        }

        corners.Sort((a, b) => a.Origin.Y.CompareTo(b.Origin.Y));
        Assert.Equal(4, corners.Count);
        foreach (MapEntity corner in corners)
        {
            CellId c = NavPoints.CellNear(Loaded.Value.Nav.Graph, corner.Origin, Class);
            float via = fromCombine.CostAt(c.Index) + fromRebel.CostAt(c.Index);
            Assert.True(via <= best * 1.6f, $"the corner at {corner.Origin} is on no route near the best: {via} against {best}");
        }

        // The rows: two corners lie well apart from the other two across the bases' line.
        Assert.True(corners[2].Origin.Y - corners[1].Origin.Y > 1000f);
    }
}
