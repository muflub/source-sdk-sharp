using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// ss_ai_nav (plan_hl2mp_ai §6.6, Phase 2 gate): the per-tree map linked from
/// its room library by map-tools' rooms pipeline loads by its level id, and
/// its derived graph joins the spawn to every pickup, each route crossing the
/// edge type its room is built to need. Skipped, visibly, in a tree that has
/// not built it (`make map MAP=ss_ai_nav`).
/// </summary>
public sealed class SsAiNavTests
{
    private const string Map = "ss_ai_nav";

    private static readonly Lazy<LoadedNav> Loaded = new(() =>
    {
        NavLoadResult r = NavLoader.Load(Map, p => File.Exists(Path.Combine(PerTreeNav.MapsDir, Path.GetFileName(p)))
                                              ? File.ReadAllBytes(Path.Combine(PerTreeNav.MapsDir, Path.GetFileName(p))) : null, null);
        Assert.True(r.Nav is not null, r.Refusal);
        return r.Nav!;
    });

    /// <summary>The edge types of the route from the spawn to the pickup of <paramref name="className"/>.</summary>
    private static List<NavEdgeType> Route(string className, ClearanceClass cls)
    {
        LoadedNav n = Loaded.Value;
        NavGraph g = n.Nav.Graph;
        MapEntity spawn = n.Entities.Spawns.First();
        MapEntity item = Assert.Single(n.Entities.All, e => e.ClassName == className);
        int from = NavPoints.CellNear(g, spawn.Origin, cls).Index;
        foreach (CellId target in NavPoints.CellsNear(g, item.Origin, cls).Take(8))
        {
            var field = new FlowField();
            field.Build(g, target, cls, new NavObstacleState(g.ObstacleCount));
            if (!float.IsFinite(field.CostAt(from)))
                continue;
            var via = new List<NavEdgeType>();
            for (int at = from; field.NextFrom(at).IsValid; at = field.NextFrom(at).Index)
                via.Add(field.ViaFrom(at));
            return via;
        }

        Assert.Fail($"the spawn reaches no cell that touches the {className}");
        return [];
    }

    [PerTreeNavFact(Map)]
    public void TheLinkedLevelBindsByItsLevelId() => Assert.False(string.IsNullOrEmpty(Loaded.Value.Entities.LevelId));

    [PerTreeNavFact(Map)]
    public void TheHealthKitIsAWalk() => Assert.All(Route("item_healthkit", ClearanceClass.Stand), t => Assert.Equal(NavEdgeType.Walk, t));

    [PerTreeNavFact(Map)]
    public void TheShotgunIsUpTheStepsWalking()
    {
        List<NavEdgeType> via = Route("weapon_shotgun", ClearanceClass.Stand);
        Assert.NotEmpty(via);
        Assert.DoesNotContain(NavEdgeType.Jump, via);
        Assert.DoesNotContain(NavEdgeType.CrouchJump, via);
    }

    [PerTreeNavFact(Map)]
    public void TheSmgIsAJumpUp() => Assert.Contains(NavEdgeType.Jump, Route("weapon_smg1", ClearanceClass.Stand));

    [PerTreeNavFact(Map)]
    public void The357IsACrouchJumpUp() => Assert.Contains(NavEdgeType.CrouchJump, Route("weapon_357", ClearanceClass.Stand));

    [PerTreeNavFact(Map)]
    public void TheBatteryIsUnderTheCrawlOnlyCrouching()
    {
        Assert.NotEmpty(Route("item_battery", ClearanceClass.Crouch));
        LoadedNav n = Loaded.Value;
        MapEntity battery = Assert.Single(n.Entities.All, e => e.ClassName == "item_battery");
        Assert.All(NavPoints.CellsNear(n.Nav.Graph, battery.Origin, ClearanceClass.Crouch).Take(4),
                   c => Assert.True(n.Nav.Graph.Cell(c).CrouchOnly, $"cell {c.Index} by the battery admits a standing body"));
    }

    [PerTreeNavFact(Map)]
    public void TheCrossbowIsUpTheLadder() => Assert.Contains(NavEdgeType.Ladder, Route("weapon_crossbow", ClearanceClass.Stand));

    [PerTreeNavFact(Map)]
    public void TheAr2IsThroughTheDoor()
    {
        List<NavEdgeType> via = Route("weapon_ar2", ClearanceClass.Stand);
        Assert.True(via.Contains(NavEdgeType.Door), string.Join(",", via));
    }

    [PerTreeNavFact(Map)]
    public void TheHurtStripHasNoCells()
    {
        LoadedNav n = Loaded.Value;
        HurtVolume hurt = Assert.Single(n.Entities.Hurt);
        AiVector mid = (hurt.Min + hurt.Max) * 0.5f;
        Assert.False(n.Nav.Graph.FindCell(mid with { Z = hurt.Min.Z + 1f }).IsValid);
    }
}
