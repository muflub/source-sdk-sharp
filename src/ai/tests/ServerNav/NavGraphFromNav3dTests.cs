using Xunit;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The derivation over a hand-built source shaped as plan_hl2mp_ai §8.2's
/// fixture: a floor with an 18 u step, a 30 u ledge, a 50 u ledge, a 100 u
/// pit, a 40 u ceiling, a door, a leaf nobody fits under, an unwalkable leaf
/// and one in a hurt volume.
/// </summary>
public class NavGraphFromNav3dTests
{
    private static readonly NavNumbers Numbers = NavNumbers.Stock(56f);

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

    /// <summary>
    /// A ladder's column whose middle does not hold a crouched body at its centre, as
    /// dm_lockdown's (-4148 4188) does between z 96 and z 128 beside a floor's lip:
    /// <paramref name="ceiling"/> over the low and high climbs, the gap's two leaves having none.
    /// </summary>
    private static (FakeNavSource Src, int Low, int High) GappedLadder()
    {
        var s = new FakeNavSource();
        int low = s.Add(0, 0, 48f, 96f, walkable: false, ladder: true);
        int gapA = s.Add(0, 0, 96f, 112f, walkable: false, ladder: true);
        int gapB = s.Add(0, 0, 112f, 128f, walkable: false, ladder: true);
        int high = s.Add(0, 0, 128f, 200f, walkable: false, ladder: true);
        s.Stack(low, gapA, gapB, high);
        return (s, low, high);
    }

    private static DerivedNav DeriveLadder(FakeNavSource s, params LadderClimb[] climbs)
        => NavGraphFromNav3d.Derive(s, Numbers, NavEdgeCosts.Default, [], climbs);

    [Fact]
    public void AUseableLaddersLineBridgesTheLeavesABodyDoesNotFitAtTheCentreOf()
    {
        var (s, low, high) = GappedLadder();

        DerivedNav d = DeriveLadder(s, new LadderClimb(new AiVector(8f, 8f, 4f), new AiVector(8f, 8f, 133f)));

        Assert.Equal((NavEdgeType.Ladder, NavEdgeType.Ladder), (EdgeType(d, low, high), EdgeType(d, high, low)));
    }

    /// <summary>The control, the file's own climb: with no ladder's line there the gap is a gap.</summary>
    [Fact]
    public void WithNoLineTheGapStays()
    {
        var (s, low, high) = GappedLadder();

        Assert.Null(EdgeType(DeriveLadder(s), low, high));
    }

    [Fact]
    public void ALineElsewhereDoesNotBridgeIt()
    {
        var (s, low, high) = GappedLadder();

        Assert.Null(EdgeType(DeriveLadder(s, new LadderClimb(new AiVector(200f, 8f, 4f), new AiVector(200f, 8f, 133f))), low, high));
    }

    /// <summary>A line that stops below the far side of the gap does not climb to it.</summary>
    [Fact]
    public void ALineEndingInTheGapDoesNotBridgeIt()
    {
        var (s, low, high) = GappedLadder();

        Assert.Null(EdgeType(DeriveLadder(s, new LadderClimb(new AiVector(8f, 8f, 4f), new AiVector(8f, 8f, 110f))), low, high));
    }

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

    /// <summary>Numbers with a double jump that reaches 117 u (TF's Scout's: 52 + 45 + 20).</summary>
    private static readonly NavNumbers DoubleJumper = Numbers with { MaxDoubleJumpRise = 117f };

    [Fact]
    public void ASeventyUnitLedgeIsADoubleJumpForNumbersThatHaveOne()
    {
        Fixture f = Build();
        DerivedNav d = NavGraphFromNav3d.Derive(f.Src, DoubleJumper, NavEdgeCosts.Default, []);
        Assert.Equal((NavEdgeType.DoubleJump, NavEdgeType.Drop), (EdgeType(d, f.Floor, f.Tall), EdgeType(d, f.Tall, f.Floor)));
    }

    [Fact]
    public void ADoubleJumpCostsItsOwnExtra()
    {
        float Cost(float extra)
        {
            Fixture f = Build();
            DerivedNav d = NavGraphFromNav3d.Derive(f.Src, DoubleJumper, NavEdgeCosts.Default with { DoubleJump = extra }, []);
            Assert.True(d.Graph.TryEdge(d.CellOfLeaf[f.Floor], d.CellOfLeaf[f.Tall], out NavEdge e));
            return e.Cost;
        }

        // The edge's length and the extra: only the extra moves with it.
        Assert.Equal(51f, Cost(99f) - Cost(48f), 3);
    }

    [Fact]
    public void AHundredAndTwentyUnitLedgeIsBeyondTheDoubleJump()
    {
        var s = new FakeNavSource();
        int floor = s.Add(0, 0, 0);
        int high = s.Add(0, 1, 120);
        s.JumpLink(floor, high);
        DerivedNav d = NavGraphFromNav3d.Derive(s, DoubleJumper, NavEdgeCosts.Default, []);
        Assert.Null(EdgeType(d, floor, high));
    }

    [Fact]
    public void OnlyAnAgentWithTheReachRoutesUpADoubleJump()
    {
        Fixture f = Build();
        DerivedNav d = NavGraphFromNav3d.Derive(f.Src, DoubleJumper, NavEdgeCosts.Default, []);
        var field = new FlowField();
        var plain = new NavObstacleState(d.Graph.ObstacleCount);
        field.Build(d.Graph, new CellId(d.CellOfLeaf[f.Tall]), ClearanceClass.Crouch, plain);
        Assert.NotEqual(NavEdgeType.DoubleJump, field.ViaFrom(d.CellOfLeaf[f.Floor]));

        var scout = new NavObstacleState(d.Graph.ObstacleCount) { Reach = JumpReach.Double };
        field.Build(d.Graph, new CellId(d.CellOfLeaf[f.Tall]), ClearanceClass.Crouch, scout);
        Assert.Equal(NavEdgeType.DoubleJump, field.ViaFrom(d.CellOfLeaf[f.Floor]));
    }

    [Fact]
    public void TheReachRidesEveryCopyOfTheState()
    {
        var state = new NavObstacleState(2) { Reach = JumpReach.Double };
        var lane = new NavObstacleState(2);
        lane.CopyFrom(state);
        Assert.Equal((JumpReach.Double, JumpReach.Double, JumpReach.Double), (state.Clone().Reach, state.Shoved().Reach, lane.Reach));
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
