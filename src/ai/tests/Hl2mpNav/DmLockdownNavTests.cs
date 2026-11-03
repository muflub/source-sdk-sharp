using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// dm_lockdown's per-tree navigation (plan_hl2mp_ai Phase 2 gate): the graph
/// derived from <c>ssmap nav3d</c>'s file joins every deathmatch spawn to every
/// weapon the map places by a standing route, holds every join map-tools'
/// reference makes, is bound to this tree's bsp, and names the obstacles and
/// ladders the entity lump has.
/// </summary>
public sealed class DmLockdownNavTests
{
    private const string Map = "dm_lockdown";

    private const ClearanceClass Class = ClearanceClass.Crouch;

    private static readonly Lazy<(Nav3dReader Reader, DerivedNav Nav, MapEntities Entities, byte[] Bsp)> Loaded = new(() =>
    {
        byte[] bsp = File.ReadAllBytes(PerTreeNav.BspPath(Map));
        Nav3dReader reader = Nav3dReader.Open(File.ReadAllBytes(PerTreeNav.NavPath(Map)));
        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(reader.JumpHeight), NavEdgeCosts.Default,
                                                  entities.Hurt);
        return (reader, nav, entities, bsp);
    });

    [PerTreeNavFact(Map)]
    public void TheNavIsBoundToThisTreesBsp() => Assert.True(Loaded.Value.Reader.MatchesBsp(Loaded.Value.Bsp));

    [PerTreeNavFact(Map)]
    public void EverySpawnReachesEveryWeaponStanding()
    {
        (int count, List<string> unreached) = Unreached(Loaded.Value.Entities.Weapons);
        Assert.Equal(18, count);
        Assert.Empty(unreached);
    }

    [PerTreeNavFact(Map)]
    public void EverySpawnReachesEveryItemStanding()
    {
        (int count, List<string> unreached) = Unreached(Loaded.Value.Entities.Items);
        Assert.Equal(59, count);
        Assert.Empty(unreached);
    }

    /// <summary>How many targets, and which a spawn's cell cannot reach by a standing route.</summary>
    private static (int, List<string>) Unreached(IEnumerable<MapEntity> targets)
    {
        (_, DerivedNav nav, MapEntities entities, _) = Loaded.Value;
        int[] spawns = [.. entities.Spawns.Select(s => NavPoints.CellNear(nav.Graph, s.Origin, Class).Index)];
        Assert.Equal(9, spawns.Length);
        Assert.DoesNotContain(-1, spawns);
        var unreached = new List<string>();
        var field = new FlowField();
        var open = new NavObstacleState(nav.Graph.ObstacleCount);
        int count = 0;
        foreach (MapEntity w in targets)
        {
            count++;
            List<CellId> near = NavPoints.CellsNear(nav.Graph, w.Origin, Class);
            if (near.Count == 0)
            {
                unreached.Add($"{w.ClassName} at {w.Origin}: no cell within reach");
                continue;
            }

            // Reached when some cell that touches it is reached from every spawn.
            bool reached = false;
            foreach (CellId target in near.Take(8))
            {
                field.Build(nav.Graph, target, Class, open);
                if (spawns.All(s => float.IsFinite(field.CostAt(s))))
                {
                    reached = true;
                    break;
                }
            }

            if (!reached)
                unreached.Add($"{w.ClassName} at {w.Origin}");
        }

        return (count, unreached);
    }

    [PerTreeNavFact(Map)]
    public void EveryJoinMapToolsMakesIsOneOfOurs()
    {
        (Nav3dReader reader, DerivedNav nav, _, _) = Loaded.Value;
        (int[] component, int count) = Nav3dWalk.Components(reader, Nav3dWalker.Player);
        int[] ours = Nav3dFixtureTests.BothWayComponents(nav.Graph);
        var seen = new int[count];
        Array.Fill(seen, -1);
        int compared = 0;
        foreach (int leaf in Enumerable.Range(0, reader.LeafCount).Where(l => component[l] >= 0))
        {
            Assert.True(nav.CellOfLeaf[leaf] >= 0, $"map-tools' place {leaf} is no cell of ours");
            int mine = ours[nav.CellOfLeaf[leaf]];
            if (seen[component[leaf]] < 0)
                seen[component[leaf]] = mine;
            Assert.Equal(seen[component[leaf]], mine);
            compared++;
        }

        Assert.True(compared > 10_000, $"{compared} places compared");
    }

    /// <summary>
    /// The live reports' places: a walkway cell whose route to the floor dropped
    /// through the wall south of it, and the rail at y 4313 whose top is 40 u
    /// over the walkway, measured with the game's own traces and a player's
    /// crouch-jump onto it.
    /// </summary>
    [PerTreeNavFact(Map)]
    public void TheWalkwaysWallIsNoDropAndItsRailIsACrouchJump()
    {
        NavGraph g = Loaded.Value.Nav.Graph;
        CellId byWall = g.FindCell(new AiVector(-3544f, 4424f, 129f)), walkway = g.FindCell(new AiVector(-3544f, 4344f, 129f));
        CellId rail = g.FindCell(new AiVector(-3544f, 4312f, 169f)), under = g.FindCell(new AiVector(-3544f, 4392f, 1f));
        Assert.Equal((128f, 128f, 168f, 0f), (g.Cell(byWall).FloorZ, g.Cell(walkway).FloorZ, g.Cell(rail).FloorZ, g.Cell(under).FloorZ));
        Assert.False(g.TryEdge(byWall.Index, under.Index, out _), "a drop through the wall south of the walkway");
        NavEdge[] drops = [.. g.EdgesFrom(walkway.Index).ToArray().Where(e => e.Type == NavEdgeType.Drop)];
        Assert.DoesNotContain(drops, e => g.FindCell(g.Cell(e.To).Centre with { Z = 129f }) is { IsValid: true } over && g.Cell(over).FloorZ == 128f);
        Assert.True(g.TryEdge(walkway.Index, rail.Index, out NavEdge up), "no way onto the rail");
        Assert.Equal(NavEdgeType.CrouchJump, up.Type);
    }

    /// <summary>
    /// The way down from the rail's top, where a real file's column is split in
    /// short leaves: the floor beyond it, and no floor under the walkway behind
    /// it. A body asked of the lower floor's own leaf at the rail's height fits
    /// nowhere, which took every drop off this map's floors away.
    /// </summary>
    [PerTreeNavFact(Map)]
    public void TheRailsTopDropsToTheFloorBeyondIt()
    {
        NavGraph g = Loaded.Value.Nav.Graph;
        CellId rail = g.FindCell(new AiVector(-3544f, 4312f, 169f));
        NavEdge[] drops = [.. g.EdgesFrom(rail.Index).ToArray().Where(e => e.Type == NavEdgeType.Drop)];
        Assert.Contains(drops, e => g.Cell(e.To).FloorZ == 0f && g.Cell(e.To).Centre.Y < 4304f);
        Assert.DoesNotContain(drops, e => g.Cell(e.To).FloorZ == 0f && g.Cell(e.To).Centre.Y > 4320f);
    }

    /// <summary>
    /// Nowhere a bot can get to from a spawn is a trap: the drops the walkway's
    /// fact takes away were the only way out of nothing a player can reach.
    /// </summary>
    [PerTreeNavFact(Map)]
    public void EveryCellASpawnReachesReachesTheSpawnBack()
    {
        (_, DerivedNav nav, MapEntities entities, _) = Loaded.Value;
        NavGraph g = nav.Graph;
        CellId spawn = NavPoints.CellNear(g, entities.Spawns.First().Origin, Class);
        var seen = new bool[g.CellCount];
        var open = new Stack<int>();
        seen[spawn.Index] = true;
        open.Push(spawn.Index);
        int reached = 0;
        while (open.Count > 0)
        {
            int at = open.Pop();
            reached++;
            foreach (NavEdge e in g.EdgesFrom(at))
            {
                if (!seen[e.To] && g.Cell(e.To).Admits(Class))
                {
                    seen[e.To] = true;
                    open.Push(e.To);
                }
            }
        }

        var field = new FlowField();
        field.Build(g, spawn, Class, new NavObstacleState(g.ObstacleCount));
        NavCell[] traps = [.. Enumerable.Range(0, g.CellCount).Where(c => seen[c] && !float.IsFinite(field.CostAt(c))).Select(c => g.Cell(c))];
        Assert.True(reached > 10_000, $"{reached} cells reached");
        Assert.True(traps.Length == 0, $"{traps.Length} cells reached from the spawn with no way back: {string.Join(" ", traps.Take(12).Select(c => c.Centre))}");
    }

    /// <summary>A window's sill in the control room: the drop through its pane carries the pane, whatever prop's leaf the sill is also in (a prop would be shoved through).</summary>
    [PerTreeNavFact(Map)]
    public void TheDropOffAWindowsSillCarriesThePane()
    {
        (_, DerivedNav nav, _, _) = Loaded.Value;
        NavGraph g = nav.Graph;
        CellId sill = g.FindCell(new AiVector(-2985f, 4600f, 163f));
        NavEdge[] outside = [.. g.EdgesFrom(sill.Index).ToArray().Where(e => e.Type == NavEdgeType.Drop && g.Cell(e.To).Centre.X > -2970f)];
        Assert.NotEmpty(outside);
        Assert.All(outside, e => Assert.Equal("func_breakable_surf", nav.Obstacles[e.Obstacle].ClassName));
    }

    [PerTreeNavFact(Map)]
    public void TheNavsDoorsAreTheLumpsDoors()
    {
        (Nav3dReader reader, _, MapEntities entities, _) = Loaded.Value;
        string[] lump = [.. entities.All.Where(e => e.ClassName is "func_door" or "func_door_rotating" or "prop_door_rotating")
                             .Select(e => e.ClassName).Order()];
        string[] nav = [.. Enumerable.Range(0, reader.ObstacleCount).Select(reader.Obstacle)
                            .Where(o => o.Kind == Nav3dObstacleKind.Door).Select(o => o.ClassName).Order()];
        Assert.NotEmpty(lump);
        Assert.Equal(lump, nav);
    }

    [PerTreeNavFact(Map)]
    public void EveryUseableLadderHasLadderPlacesInTheGraph()
    {
        (_, DerivedNav nav, MapEntities entities, _) = Loaded.Value;
        MapEntity[] ladders = [.. entities.All.Where(e => e.ClassName == "func_useableladder")];
        Assert.Equal(3, ladders.Length);
        int ladderCells = Enumerable.Range(0, nav.Graph.CellCount).Count(c => (nav.Graph.Cell(c).Flags & NavCellFlags.Ladder) != 0);
        Assert.True(ladderCells > 0, "no ladder cell");
        Assert.All(ladders, l => Assert.True(
            Enumerable.Range(0, nav.Graph.CellCount).Any(c => (nav.Graph.Cell(c).Flags & NavCellFlags.Ladder) != 0
                                                             && nav.Graph.Cell(c).Centre.DistanceTo(l.Origin) < 256f),
            $"no ladder cell near the func_useableladder at {l.Origin}"));
    }
}
