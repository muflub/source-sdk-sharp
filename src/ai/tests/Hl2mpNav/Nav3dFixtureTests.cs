using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The derivation over a real <c>.nav3d</c> written by map-tools'
/// <c>Nav3dWriter</c> and read by its <c>Nav3dReader</c> (plan_hl2mp_ai
/// §8.2, Phase 2 step 3): <see cref="NavFixtures.Course"/>'s step, ledges,
/// drop, crawlspace, door, ladder and water each come out as the cells and
/// edge types a player crossing them needs, and the places and joins agree
/// with map-tools' own reference (<c>Nav3dWalk</c>).
/// </summary>
public sealed class Nav3dFixtureTests
{
    private static readonly Lazy<(Nav3dReader Reader, DerivedNav Nav)> Course = new(() =>
    {
        Nav3dReader reader = NavFixtures.Course().Open();
        return (reader, NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default,
                                                 [new HurtVolume(new AiVector(672, 32, 0), new AiVector(736, 96, 100))]));
    });

    private static DerivedNav Nav => Course.Value.Nav;

    private static int CellAt(float x, float y, float z)
    {
        CellId c = Nav.Graph.FindCell(new AiVector(x, y, z));
        Assert.True(c.IsValid, $"no cell at ({x} {y} {z})");
        return c.Index;
    }

    private static NavEdgeType? Edge(int from, int to) => Nav.Graph.TryEdge(from, to, out NavEdge e) ? e.Type : null;

    private static FlowField FieldTo(int cell, ClearanceClass cls, NavObstacleState? obstacles = null)
    {
        var field = new FlowField();
        field.Build(Nav.Graph, new CellId(cell), cls, obstacles ?? new NavObstacleState(Nav.Graph.ObstacleCount));
        return field;
    }

    private static List<NavEdgeType> Route(FlowField field, int from)
    {
        var via = new List<NavEdgeType>();
        for (int at = from, guard = 0; field.NextFrom(at).IsValid && guard < 1000; guard++)
        {
            via.Add(field.ViaFrom(at));
            at = field.NextFrom(at).Index;
        }

        return via;
    }

    private static int Floor => CellAt(40, 40, 16);

    [Fact]
    public void AnEighteenUnitStepIsWalkedUpAndDown()
    {
        int below = CellAt(200, 8, 16), top = CellAt(200, 40, 34);
        Assert.Equal((NavEdgeType.Walk, NavEdgeType.Walk), (Edge(below, top), Edge(top, below)));
    }

    [Fact]
    public void AThirtyUnitLedgeIsAJumpUpAndADropDown()
    {
        int below = CellAt(200, 136, 16), top = CellAt(200, 168, 46);
        Assert.Equal((NavEdgeType.Jump, NavEdgeType.Drop), (Edge(below, top), Edge(top, below)));
    }

    [Fact]
    public void AFiftyUnitLedgeIsACrouchJumpUpAndADropDown()
    {
        int below = CellAt(200, 264, 16), top = CellAt(200, 296, 66);
        Assert.Equal((NavEdgeType.CrouchJump, NavEdgeType.Drop), (Edge(below, top), Edge(top, below)));
    }

    [Fact]
    public void AHundredUnitPlatformIsADropWithNoWayBack()
    {
        int top = CellAt(440, 64, 116);
        Assert.True(float.IsFinite(FieldTo(Floor, ClearanceClass.Stand).CostAt(top)));
        Assert.Contains(NavEdgeType.Drop, Route(FieldTo(Floor, ClearanceClass.Stand), top));
        Assert.False(float.IsFinite(FieldTo(top, ClearanceClass.Crouch).CostAt(Floor)));
    }

    [Fact]
    public void AFortyUnitCrawlspaceIsCrouchOnly()
    {
        int crawl = CellAt(440, 192, 16);
        Assert.Equal(ClearanceClasses.Crouch, Nav.Graph.Cell(crawl).Classes);
        Assert.True(float.IsFinite(FieldTo(crawl, ClearanceClass.Crouch).CostAt(Floor)));
        Assert.False(float.IsFinite(FieldTo(crawl, ClearanceClass.Stand).CostAt(Floor)));
    }

    [Fact]
    public void TheWayThroughTheWallIsADoorEdgeOfItsObstacle()
    {
        int beyond = CellAt(700, 256, 16);
        FlowField field = FieldTo(beyond, ClearanceClass.Stand);
        Assert.Contains(NavEdgeType.Door, Route(field, Floor));
        int door = Enumerable.Range(0, Nav.Graph.CellCount).SelectMany(c => Nav.Graph.EdgesFrom(c).ToArray())
            .First(e => e.Type == NavEdgeType.Door).Obstacle;
        Assert.Equal(("door_1", NavObstacleKind.Door), (Nav.Obstacles[door].Name, Nav.Obstacles[door].Kind));
    }

    [Fact]
    public void AShutDoorClosesTheWayThroughTheWall()
    {
        int beyond = CellAt(700, 256, 16);
        var shut = new NavObstacleState(Nav.Graph.ObstacleCount);
        shut.Set(0, true);
        Assert.False(float.IsFinite(FieldTo(beyond, ClearanceClass.Stand, shut).CostAt(Floor)));
    }

    [Fact]
    public void OnlyTheLeavesTheDoorTakesCarryIt()
    {
        // Its records name every leaf within reach; only the gap's are its.
        NavEdge[] carried = [.. Enumerable.Range(0, Nav.Graph.CellCount).SelectMany(c => Nav.Graph.EdgesFrom(c).ToArray()).Where(e => e.Obstacle >= 0)];
        Assert.NotEmpty(carried);
        Assert.All(carried, e => Assert.InRange(Nav.Graph.Cell(e.From).Centre.Y, 224f - 32f, 288f + 32f));
        Assert.All(carried, e => Assert.InRange(Nav.Graph.Cell(e.From).Centre.X, 608f - 48f, 624f + 48f));
    }

    [Fact]
    public void TheHighPlatformIsReachedByItsLadder()
    {
        int top = CellAt(440, 460, 200);
        Assert.Contains(NavEdgeType.Ladder, Route(FieldTo(top, ClearanceClass.Stand), Floor));
        Assert.True(float.IsFinite(FieldTo(Floor, ClearanceClass.Stand).CostAt(top)));
    }

    [Fact]
    public void WaterCellsAreFlaggedAndCostTwice()
    {
        NavCell wet = Nav.Graph.Cell(CellAt(56, 424, 16));
        Assert.Equal((NavCellFlags.Water, 2f), (wet.Flags & NavCellFlags.Water, wet.CostMultiplier));
    }

    [Fact]
    public void AHurtVolumeHasNoCells()
        => Assert.False(Nav.Graph.FindCell(new AiVector(700, 64, 16)).IsValid);

    [Fact]
    public void ThePlacesAreMapToolsPlaces()
    {
        // Nav3dWalk's places (outside the hurt volume) are exactly our cells' leaves.
        (Nav3dReader reader, DerivedNav nav) = Course.Value;
        (int[] component, _) = Nav3dWalk.Components(reader, Nav3dWalker.Player);
        int[] theirs = [.. Enumerable.Range(0, reader.LeafCount).Where(l => component[l] >= 0 && !InHurt(nav, l, reader))];
        Assert.Equal(theirs, nav.LeafOfCell.Order().ToArray());
    }

    [Fact]
    public void EveryJoinMapToolsMakesIsOneOfOurs()
    {
        // Each Nav3dWalk component lies inside one of ours (both-way edges):
        // ours may join more (a step's foot), never less.
        (Nav3dReader reader, DerivedNav nav) = Course.Value;
        (int[] component, int count) = Nav3dWalk.Components(reader, Nav3dWalker.Player);
        int[] ours = BothWayComponents(nav.Graph);
        var seen = new int[count];
        Array.Fill(seen, -1);
        int checkedLeaves = 0;
        for (int leaf = 0; leaf < reader.LeafCount; leaf++)
        {
            if (component[leaf] < 0 || nav.CellOfLeaf[leaf] < 0)
                continue;
            int mine = ours[nav.CellOfLeaf[leaf]];
            if (seen[component[leaf]] < 0)
                seen[component[leaf]] = mine;
            Assert.Equal(seen[component[leaf]], mine);
            checkedLeaves++;
        }

        Assert.True(checkedLeaves > 1000, $"{checkedLeaves} leaves compared");
    }

    [Fact]
    public void TheStepJoinsWhatMapToolsLeavesApart()
    {
        // The finding of §12.4: map-tools' reference leaves a sheer 18 u step
        // in its own component (the foot's column cannot hold a centred
        // player), ours walks it.
        (Nav3dReader reader, DerivedNav nav) = Course.Value;
        (int[] component, _) = Nav3dWalk.Components(reader, Nav3dWalker.Player);
        int floor = nav.LeafOfCell[Floor], step = nav.LeafOfCell[CellAt(200, 40, 34)];
        int[] ours = BothWayComponents(nav.Graph);
        Assert.NotEqual(component[floor], component[step]);
        Assert.Equal(ours[Floor], ours[CellAt(200, 40, 34)]);
    }

    private static bool InHurt(DerivedNav nav, int leaf, Nav3dReader reader)
    {
        (MapFormats.Geometry.Vec3 mins, _) = reader.LeafBounds(leaf);
        return mins.X >= 672 && mins.X < 736 && mins.Y >= 32 && mins.Y < 96;
    }

    /// <summary>Components of the graph's edges that run both ways.</summary>
    internal static int[] BothWayComponents(NavGraph g)
    {
        var parent = Enumerable.Range(0, g.CellCount).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
                x = parent[x] = parent[parent[x]];
            return x;
        }

        for (int c = 0; c < g.CellCount; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                if (g.TryEdge(e.To, e.From, out _))
                {
                    int a = Find(e.From), b = Find(e.To);
                    if (a != b)
                        parent[Math.Max(a, b)] = Math.Min(a, b);
                }
            }
        }

        return [.. Enumerable.Range(0, g.CellCount).Select(Find)];
    }
}
