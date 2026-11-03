using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The derivation over a hand-built source shaped as plan_hl2mp_ai §8.2's
/// fixture: a floor with an 18 u step, a 30 u ledge, a 50 u ledge, a 100 u
/// pit, a 40 u ceiling, a door, a leaf nobody fits under, an unwalkable leaf
/// and one in a hurt volume.
/// </summary>
public class NavGraphFromNav3dTests
{
    private static readonly Hl2mpNavNumbers Numbers = Hl2mpNavNumbers.Stock(56f);

    private sealed record Fixture(FakeNavSource Src, int Floor, int Step, int Ledge30, int Ledge50, int Pit, int Low, int Door,
                                  int Squeeze, int Solid, int Hurt, int Tall);

    private static Fixture Build()
    {
        var s = new FakeNavSource();
        int floor = s.Add(0, 0, 0);
        int step = s.Add(1, 0, 18);
        int ledge30 = s.Add(0, 1, 30);
        int ledge50 = s.Add(0, -1, 50);
        int pit = s.Add(-1, 0, -100);
        int low = s.Add(0, 2, 30, ceiling: 30 + 40);
        int door = s.Add(2, 0, 18);
        int squeeze = s.Add(3, 0, 18, ceiling: 18 + 20);
        int solid = s.Add(1, 1, 0, walkable: false);
        int hurt = s.Add(5, 5, 0);
        int tall = s.Add(-1, 1, 70);
        s.JumpLink(floor, ledge30);
        s.JumpLink(floor, ledge50);
        s.JumpLink(floor, tall);
        s.Door("door_east", door);
        s.Prop("crate_1", floor);
        return new Fixture(s, floor, step, ledge30, ledge50, pit, low, door, squeeze, solid, hurt, tall);
    }

    private static DerivedNav Derive(Fixture f)
        => NavGraphFromNav3d.Derive(f.Src, Numbers, NavEdgeCosts.Default,
                                    [new HurtVolume(new AiVector(80, 80, -10), new AiVector(96, 96, 100))]);

    private static NavEdgeType? EdgeType(DerivedNav d, int fromLeaf, int toLeaf)
        => d.Graph.TryEdge(d.CellOfLeaf[fromLeaf], d.CellOfLeaf[toLeaf], out NavEdge e) ? e.Type : null;

    [Fact]
    public void AnEighteenUnitStepIsAWalkBothWays()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal((NavEdgeType.Walk, NavEdgeType.Walk), (EdgeType(d, f.Floor, f.Step), EdgeType(d, f.Step, f.Floor)));
    }

    [Fact]
    public void AThirtyUnitLedgeIsAJumpUpAndADropDown()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal((NavEdgeType.Jump, NavEdgeType.Drop), (EdgeType(d, f.Floor, f.Ledge30), EdgeType(d, f.Ledge30, f.Floor)));
    }

    [Fact]
    public void AFiftyUnitLedgeIsACrouchJumpUp()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal((NavEdgeType.CrouchJump, NavEdgeType.Drop), (EdgeType(d, f.Floor, f.Ledge50), EdgeType(d, f.Ledge50, f.Floor)));
    }

    [Fact]
    public void AHundredUnitPitIsADropWithNoWayBack()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal((NavEdgeType.Drop, (NavEdgeType?)null), (EdgeType(d, f.Floor, f.Pit), EdgeType(d, f.Pit, f.Floor)));
    }

    [Fact]
    public void ALinkAboveTheCrouchJumpIsNotAnEdge()
        => Assert.Null(EdgeType(Derive(Build()), Build().Floor, Build().Tall));

    [Fact]
    public void AFortyUnitCeilingIsACrouchOnlyCell()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal(ClearanceClasses.Crouch, d.Graph.Cell(d.CellOfLeaf[f.Low]).Classes);
    }

    [Fact]
    public void AnOpenFloorAdmitsBothClasses()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        Assert.Equal(ClearanceClasses.Stand | ClearanceClasses.Crouch, d.Graph.Cell(d.CellOfLeaf[f.Floor]).Classes);
    }

    [Fact]
    public void ADoorsLeafIsReachedByADoorEdgeOfItsObstacle()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        d.Graph.TryEdge(d.CellOfLeaf[f.Step], d.CellOfLeaf[f.Door], out NavEdge e);
        Assert.Equal((NavEdgeType.Door, 0, "door_east"), (e.Type, e.Obstacle, d.Obstacles[e.Obstacle].Name));
    }

    [Fact]
    public void EveryObstacleBecomesAGraphObstacle()
        => Assert.Equal(["door_east", "crate_1"], Derive(Build()).Obstacles.Select(o => o.Name));

    [Fact]
    public void AWalkIntoAPropsLeafCarriesTheProp()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        d.Graph.TryEdge(d.CellOfLeaf[f.Step], d.CellOfLeaf[f.Floor], out NavEdge e);
        Assert.Equal((NavEdgeType.Walk, 1), (e.Type, e.Obstacle));
    }

    [Fact]
    public void ALeafNobodyFitsUnderIsNoCell()
    {
        Fixture f = Build();
        Assert.Equal(-1, Derive(f).CellOfLeaf[f.Squeeze]);
    }

    [Fact]
    public void AnUnwalkableLeafIsNoCell()
    {
        Fixture f = Build();
        Assert.Equal(-1, Derive(f).CellOfLeaf[f.Solid]);
    }

    [Fact]
    public void ALeafInAHurtVolumeIsNoCell()
    {
        Fixture f = Build();
        Assert.Equal(-1, Derive(f).CellOfLeaf[f.Hurt]);
    }

    [Fact]
    public void TheFixtureDerivesExactlyItsCells()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        int[] expected = [f.Floor, f.Step, f.Ledge30, f.Ledge50, f.Pit, f.Low, f.Door, f.Tall];
        Assert.Equal(expected.Order().ToArray(), d.LeafOfCell.Order().ToArray());
    }

    [Fact]
    public void TheFixtureDerivesExactlyItsEdgeTypes()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        var types = Enumerable.Range(0, d.Graph.CellCount).SelectMany(c => d.Graph.EdgesFrom(c).ToArray()).GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        // Beside: Walk floor<->step, ledge30<->low; Jump floor->ledge30;
        // CrouchJump floor->ledge50; Drop ledge30 and ledge50 to the floor, the
        // floor into the pit, the tall leaf onto the 30 u ledge, the pit and (by
        // its link) the floor. Two columns over the one between: Door
        // floor<->door and step<->door; Jump floor->low and ledge30->ledge50;
        // CrouchJump ledge30->tall; Drop low->floor, step->pit and
        // ledge50->ledge30.
        Assert.Equal((4, 3, 2, 9, 4),(types[NavEdgeType.Walk], types[NavEdgeType.Jump], types[NavEdgeType.CrouchJump],
                                       types[NavEdgeType.Drop], types[NavEdgeType.Door]));
    }

    [Fact]
    public void EveryCellLeadsBackToItsLeaf()
    {
        DerivedNav d = Derive(Build());
        Assert.All(Enumerable.Range(0, d.Graph.CellCount), c => Assert.Equal(c, d.CellOfLeaf[d.LeafOfCell[c]]));
    }

    [Fact]
    public void AStandFieldReachesTheLedgeByJumping()
    {
        Fixture f = Build();
        DerivedNav d = Derive(f);
        var field = new FlowField();
        field.Build(d.Graph, new CellId(d.CellOfLeaf[f.Ledge30]), ClearanceClass.Stand, new NavObstacleState(d.Graph.ObstacleCount));
        Assert.Equal(NavEdgeType.Jump, field.ViaFrom(d.CellOfLeaf[f.Floor]));
    }
}
