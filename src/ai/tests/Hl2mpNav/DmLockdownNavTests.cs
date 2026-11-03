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
