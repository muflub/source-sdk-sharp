using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The solid entities as routing cells, and who knows of them (plan_hl2mp_ai §6.2, user
/// ruling 2026-11-02): a crate on a route takes it off the route, a prop moved in a bot's
/// view is learned, one moved out of view is not, a can blocks nothing.
/// </summary>
public sealed class NavObstacleFieldTests
{
    /// <summary>A floor of 16-unit cells, 20 by 11, walked in the four directions.</summary>
    private static (NavGraph Graph, int[,] Cells) Floor(int width = 20, int height = 11)
    {
        var b = new NavGraphBuilder();
        var cells = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cells[x, y] = b.AddCell(new NavCell(x * 16f, y * 16f, x * 16f + 16f, y * 16f + 16f, 0f,
                                                    ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f)).Index;
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x + 1 < width)
                    b.AddBoth(new CellId(cells[x, y]), new CellId(cells[x + 1, y]), NavEdgeType.Walk);
                if (y + 1 < height)
                    b.AddBoth(new CellId(cells[x, y]), new CellId(cells[x, y + 1]), NavEdgeType.Walk);
            }
        }

        return (b.Build(), cells);
    }

    private sealed class World : IObstacleWorld
    {
        public readonly HashSet<EntityHandle> Breakables = [];

        public bool Breakable(EntityHandle entity) => Breakables.Contains(entity);

        public readonly Dictionary<EntityHandle, (ObstacleBodyKind Kind, AiVector At, AiVector Half, float Height)> Bodies = [];

        public int RosterCount => 1;

        public void Roster(int roster, List<(EntityHandle Entity, ObstacleBodyKind Kind)> into)
        {
            foreach (var b in Bodies)
                into.Add((b.Key, b.Value.Kind));
        }

        public bool Pose(EntityHandle entity, out AiVector origin, out AiVector angles)
        {
            angles = default;
            origin = Bodies.TryGetValue(entity, out var b) ? b.At : default;
            return Bodies.ContainsKey(entity);
        }

        public (AiVector Mins, AiVector Maxs) Box(EntityHandle entity, AiVector origin, AiVector angles)
        {
            var b = Bodies[entity];
            return (origin - new AiVector(b.Half.X, b.Half.Y, 0f), origin + new AiVector(b.Half.X, b.Half.Y, b.Height));
        }

        public void Put(EntityHandle e, AiVector at, ObstacleBodyKind kind = ObstacleBodyKind.Fixed, float half = 20f, float height = 40f)
            => Bodies[e] = (kind, at, new AiVector(half, half, 0f), height);

        public void Move(EntityHandle e, AiVector to) => Bodies[e] = Bodies[e] with { At = to };
    }

    /// <summary>A PVS by the x of a box: the viewer sees everything left of <see cref="Wall"/>.</summary>
    private sealed class Pvs : IObstacleSight
    {
        public float Wall = float.MaxValue;
        public int Asked;

        public bool InView(EntityHandle player, AiVector mins, AiVector maxs)
        {
            Asked++;
            return mins.X < Wall;
        }
    }

    private static readonly EntityHandle Bot = new(0x0001_0002u);
    private static readonly EntityHandle Crate = new(0x0002_0001u);

    private static (NavObstacleField Field, World World, Pvs Sight, NavGraph Graph, int[,] Cells) Rig()
    {
        var (g, cells) = Floor();
        var world = new World();
        var sight = new Pvs();
        return (new NavObstacleField(g, world, sight, [], new NavObstacleState(0)), world, sight, g, cells);
    }

    private static void Ticks(NavObstacleField field, int ticks = 1000)
    {
        for (int i = 0; i < ticks; i++)
            field.Tick();
    }

    private static bool RouteCrosses(NavGraph g, int[,] cells, NavObstacleState state, int x)
    {
        var f = new FlowField();
        f.Build(g, new CellId(cells[19, 5]), ClearanceClass.Crouch, state);
        int at = cells[0, 5];
        for (int i = 0; i < 200 && f.NextFrom(at).IsValid; i++)
        {
            at = f.NextFrom(at).Index;
            NavCell c = g.Cell(at);
            if (c.MinX == x * 16f && c.MinY == 5 * 16f)
                return true;
        }

        return false;
    }

    [Fact]
    public void ACratePutOnARouteTakesItsCellsAndTheFieldGoesRound()
    {
        var (field, world, _, g, cells) = Rig();
        AiVector middle = g.Cell(cells[10, 5]).Centre;
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        Assert.True(RouteCrosses(g, cells, state, 10), "the control: with nothing there the straight way goes through");

        world.Put(Crate, middle);
        Ticks(field);

        Assert.True(state.CellBlocked(cells[10, 5]));
        Assert.False(RouteCrosses(g, cells, state, 10), "the field still goes through the crate");
        var f = new FlowField();
        f.Build(g, new CellId(cells[19, 5]), ClearanceClass.Crouch, state);
        Assert.True(f.NextFrom(cells[0, 5]).IsValid, "and it still finds a way");
    }

    /// <summary>A body's centre over the cell would overlap it: its half width round the box, and no further.</summary>
    [Fact]
    public void TheCellsTakenAreThoseABodyWouldOverlap()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[10, 5]).Centre, half: 16f);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);

        Assert.True(state.CellBlocked(cells[12, 5]), "32 u from the crate's centre: inside its 16 and the body's");
        Assert.False(state.CellBlocked(cells[13, 5]), "48 u out: clear of it");
    }

    /// <summary>The layout is every bot's from the start, and a bot that joins late knows it too.</summary>
    [Fact]
    public void TheMapsLayoutIsKnownFromTheStart()
    {
        var (field, world, sight, g, cells) = Rig();
        sight.Wall = float.MinValue;
        world.Put(Crate, g.Cell(cells[10, 5]).Centre);
        field.OnLevelStart();

        Assert.True(field.StateFor(Bot).CellBlocked(cells[10, 5]));
    }

    [Fact]
    public void APropMovedInViewIsLearned()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        Assert.True(state.CellBlocked(cells[5, 5]));

        world.Move(Crate, g.Cell(cells[15, 5]).Centre);
        Ticks(field);

        Assert.False(state.CellBlocked(cells[5, 5]), "it is known to have left");
        Assert.True(state.CellBlocked(cells[15, 5]), "and known where it went");
    }

    [Fact]
    public void APropMovedOutOfViewIsNotLearned()
    {
        var (field, world, sight, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        sight.Wall = float.MinValue;

        world.Move(Crate, g.Cell(cells[15, 5]).Centre);
        Ticks(field);

        Assert.True(sight.Asked > 0, "the stimulus: the moved prop was asked after");
        Assert.True(state.CellBlocked(cells[5, 5]), "the bot still has it where it last knew it");
        Assert.False(state.CellBlocked(cells[15, 5]), "and has not learned the new place through a wall");
        Assert.True(field.TruthCells.Blocked > 0);
    }

    /// <summary>Seeing the old place empty teaches that much, and not where it went.</summary>
    [Fact]
    public void SeeingThePropsOldPlaceEmptyDoesNotTellWhereItWent()
    {
        var (field, world, sight, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        sight.Wall = g.Cell(cells[10, 5]).Centre.X;

        world.Move(Crate, g.Cell(cells[15, 5]).Centre);
        Ticks(field);

        Assert.False(state.CellBlocked(cells[5, 5]));
        Assert.False(state.CellBlocked(cells[15, 5]));
    }

    [Fact]
    public void TwoBotsKnowDifferentThings()
    {
        var (field, world, sight, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState first = field.StateFor(Bot);
        world.Move(Crate, g.Cell(cells[15, 5]).Centre);
        Ticks(field);
        Assert.True(first.CellBlocked(cells[15, 5]));

        // Joined later, out of sight of it: the map's layout, not what another bot saw.
        sight.Wall = float.MinValue;
        NavObstacleState late = field.StateFor(new EntityHandle(0x0001_0003u));
        Ticks(field);
        Assert.True(late.CellBlocked(cells[5, 5]));
        Assert.False(late.CellBlocked(cells[15, 5]));
    }

    /// <summary>A prop that appears after the start is nobody's knowledge until it is seen.</summary>
    [Fact]
    public void APropThatAppearsOutOfViewIsUnknown()
    {
        var (field, world, sight, g, cells) = Rig();
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        sight.Wall = float.MinValue;

        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        Ticks(field);
        Assert.False(state.CellBlocked(cells[5, 5]));

        sight.Wall = float.MaxValue;
        Ticks(field);
        Assert.True(state.CellBlocked(cells[5, 5]));
    }

    [Fact]
    public void ACanBlocksNothing()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[10, 5]).Centre with { Z = 19f }, ObstacleBodyKind.Shovable, half: 4f, height: 12f);
        field.OnLevelStart();

        Assert.Equal((0, 0), field.TruthCells);
    }

    [Fact]
    public void ACanOnTheFloorIsNoObstacleByItsSizeAlone()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[10, 5]).Centre, ObstacleBodyKind.Shovable, half: 10f, height: 30f);
        field.OnLevelStart();

        Assert.Equal((0, 0), field.TruthCells);
    }

    /// <summary>A big prop a player can shove costs a detour, and with no way round the route goes through it.</summary>
    [Fact]
    public void ABigShovablePropCostsAndDoesNotBlock()
    {
        var (g, cells) = Floor(height: 1);
        var world = new World();
        world.Put(Crate, g.Cell(cells[10, 0]).Centre, ObstacleBodyKind.Shovable);
        var field = new NavObstacleField(g, world, new Pvs(), [], new NavObstacleState(0));
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);

        Assert.False(state.CellBlocked(cells[10, 0]));
        Assert.True(state.CellExtra(cells[10, 0]) > 0f);
        var f = new FlowField();
        f.Build(g, new CellId(cells[19, 0]), ClearanceClass.Crouch, state);
        Assert.True(f.NextFrom(cells[0, 0]).IsValid, "the only way is through it");
        Assert.True(f.CostAt(cells[0, 0]) > 1000f, $"and it costs: {f.CostAt(cells[0, 0])}");
    }

    /// <summary>A door where it spawned stays the graph's Door edge; swung, it takes where it went.</summary>
    [Fact]
    public void ADoorTakesCellsOnlyWhereItMovedTo()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre, ObstacleBodyKind.Door);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        Assert.False(state.CellBlocked(cells[5, 5]));

        world.Move(Crate, g.Cell(cells[15, 5]).Centre);
        Ticks(field);

        Assert.True(state.CellBlocked(cells[15, 5]));
    }

    [Fact]
    public void AGoneBodyLeavesItsCellsOnceItsPlaceIsSeen()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);

        world.Bodies.Remove(Crate);
        Ticks(field);

        Assert.False(state.CellBlocked(cells[5, 5]));
    }

    /// <summary>
    /// A follower reacts: told of a crate on its corridor it builds again and goes round; told of
    /// one off its way it builds nothing; and told the crate has gone it does not turn back.
    /// </summary>
    [Fact]
    public void AFollowerBuildsAgainOnlyForWhatIsOnItsWay()
    {
        var (field, world, _, g, cells) = Rig();
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        AiVector start = g.Cell(cells[2, 5]).Centre, goal = g.Cell(cells[19, 5]).Centre;
        var follower = new NavFollower(g, new CellId(cells[19, 5]), goal, ClearanceClass.Crouch, state);
        follower.Rebuild();
        follower.Next(start with { Z = start.Z + 1f }, out _);
        Assert.Equal(0, follower.Replans);

        world.Put(Crate, g.Cell(cells[10, 10]).Centre);
        Ticks(field);
        follower.Next(start with { Z = start.Z + 1f }, out _);
        Assert.Equal(0, follower.Replans);

        world.Put(new EntityHandle(0x0002_0002u), g.Cell(cells[6, 5]).Centre);
        Ticks(field);
        follower.Next(start with { Z = start.Z + 1f }, out _);
        Assert.Equal(1, follower.Replans);

        world.Bodies.Remove(new EntityHandle(0x0002_0002u));
        Ticks(field);
        follower.Next(start with { Z = start.Z + 1f }, out _);
        Assert.Equal(1, follower.Replans);
    }

    /// <summary>A worker reads what it was given: a clone keeps its cells while the bot's move on.</summary>
    [Fact]
    public void AWorkersCopyKeepsTheCellsItWasGiven()
    {
        var (field, world, _, g, cells) = Rig();
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        NavObstacleState clone = state.Clone();
        NavObstacleState shoved = state.Shoved();

        world.Put(Crate, g.Cell(cells[5, 5]).Centre);
        Ticks(field);

        Assert.True(state.CellBlocked(cells[5, 5]));
        Assert.False(clone.CellBlocked(cells[5, 5]));
        Assert.True(shoved.CellBlocked(cells[5, 5]), "a shoving follower keeps up with what the bot knows");
    }

    /// <summary>Angles compare the short way round: 358 and 2 are 4 apart, or every prop on the map has "moved" by the first walk.</summary>
    [Fact]
    public void TheSameAnglesTheOtherWayRoundAreNoMove()
    {
        var (g, cells) = Floor();
        var world = new TurningWorld();
        world.Inner.Put(Crate, g.Cell(cells[5, 5]).Centre);
        var field = new NavObstacleField(g, world, new Pvs(), [], new NavObstacleState(0));
        field.OnLevelStart();

        world.Yaw = 2f;
        Ticks(field);

        Assert.Equal(0, field.Moved);
    }

    private sealed class TurningWorld : IObstacleWorld
    {
        public bool Breakable(EntityHandle entity) => false;

        public readonly World Inner = new();
        public float Yaw = 358f;
        public int RosterCount => 1;
        public void Roster(int roster, List<(EntityHandle Entity, ObstacleBodyKind Kind)> into) => Inner.Roster(roster, into);
        public bool Pose(EntityHandle entity, out AiVector origin, out AiVector angles)
        {
            bool found = Inner.Pose(entity, out origin, out _);
            angles = new AiVector(0f, Yaw, 0f);
            return found;
        }

        public (AiVector Mins, AiVector Maxs) Box(EntityHandle entity, AiVector origin, AiVector angles) => Inner.Box(entity, origin, angles);
    }

    /// <summary>The field's cost is a slice of its bodies a tick: a walk of them takes ScanTicks.</summary>
    [Fact]
    public void ATickReadsASliceOfTheBodies()
    {
        var (g, _) = Floor();
        var world = new CountingWorld();
        for (uint i = 0; i < 80; i++)
            world.Put(new EntityHandle(0x0002_0000u + i), new AiVector(8f, 8f, 0f));
        var field = new NavObstacleField(g, world, new Pvs(), [], new NavObstacleState(0));
        field.OnLevelStart();
        world.Poses = 0;

        field.Tick();

        Assert.Equal(80 / NavObstacleField.ScanTicks, world.Poses);
    }

    private sealed class CountingWorld : IObstacleWorld
    {
        public bool Breakable(EntityHandle entity) => false;

        private readonly World _inner = new();
        public int Poses;
        public void Put(EntityHandle e, AiVector at) => _inner.Put(e, at);
        public int RosterCount => _inner.RosterCount;
        public void Roster(int roster, List<(EntityHandle Entity, ObstacleBodyKind Kind)> into) => _inner.Roster(roster, into);
        public bool Pose(EntityHandle entity, out AiVector origin, out AiVector angles) { Poses++; return _inner.Pose(entity, out origin, out angles); }
        public (AiVector Mins, AiVector Maxs) Box(EntityHandle entity, AiVector origin, AiVector angles) => _inner.Box(entity, origin, angles);
    }

    // ---- breakables ------------------------------------------------------------

    /// <summary>A crate a gun breaks takes breakable cells, one it does not blocks; gone, it takes none.</summary>
    [Fact]
    public void ABreakableTakesCellsARouteMayCross()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[10, 5]).Centre);
        world.Breakables.Add(Crate);
        field.OnLevelStart();
        Ticks(field, 40);
        NavObstacleState state = field.StateFor(Bot);
        Assert.Equal(NavCellBlocks.Breakable, state.Cells!.State(cells[10, 5]));
        Assert.False(state.CellBlocked(cells[10, 5]));
        Assert.Equal(NavCellBlocks.BreakCost, state.CellExtra(cells[10, 5]));
        Assert.True(field.BreakableAt(cells[10, 5], out EntityHandle e, out _));
        Assert.Equal(Crate, e);
        world.Bodies.Remove(Crate);
        Ticks(field, 40);
        Assert.False(field.BreakableAt(cells[10, 5], out _, out _));
    }

    [Fact]
    public void AnUnbreakableOneStillBlocks()
    {
        var (field, world, _, g, cells) = Rig();
        world.Put(Crate, g.Cell(cells[10, 5]).Centre);
        field.OnLevelStart();
        NavObstacleState state = field.StateFor(Bot);
        Assert.True(state.CellBlocked(cells[10, 5]));
        Assert.False(field.BreakableAt(cells[10, 5], out _, out _));
    }

    /// <summary>A breakable across the only short way costs less than a long detour, and more than a short one.</summary>
    [Theory]
    [InlineData(30, true)]   // the gap is far off: through the crate
    [InlineData(2, false)]   // the gap is beside the route
    public void ABreakableCostsLessThanALongWayRound(int gapRow, bool through)
    {
        // 80 cells long, a wall at x=40 with a breakable gate at y=0 and a free gap gapRow rows up.
        int high = gapRow + 1;
        var b = new NavGraphBuilder();
        var at = new int[80, high];
        for (int x = 0; x < 80; x++)
        {
            for (int y = 0; y < high; y++)
                at[x, y] = (x == 40 && y != 0 && y != high - 1) ? -1 : b.AddCell(new NavCell(x * 16f, y * 16f, x * 16f + 16f, y * 16f + 16f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f)).Index;
        }

        for (int x = 0; x < 80; x++)
        {
            for (int y = 0; y < high; y++)
            {
                if (at[x, y] < 0)
                    continue;
                if (x + 1 < 80 && at[x + 1, y] >= 0)
                    b.AddBoth(new CellId(at[x, y]), new CellId(at[x + 1, y]), NavEdgeType.Walk);
                if (y + 1 < high && at[x, y + 1] >= 0)
                    b.AddBoth(new CellId(at[x, y]), new CellId(at[x, y + 1]), NavEdgeType.Walk);
            }
        }

        NavGraph g = b.Build();
        var bytes = new byte[g.CellCount];
        bytes[at[40, 0]] = NavCellBlocks.Breakable;
        var state = new NavObstacleState(0) { Cells = new NavCellBlocks(bytes) };
        var f = new FlowField();
        f.Build(g, new CellId(at[60, 0]), ClearanceClass.Crouch, state);
        int cell = at[20, 0];
        bool crossed = false;
        for (int i = 0; i < 400 && f.NextFrom(cell).IsValid; i++)
        {
            cell = f.NextFrom(cell).Index;
            crossed |= cell == at[40, 0];
        }

        Assert.Equal(through, crossed);
    }

    /// <summary>An explosive drum is not one to shoot walking up to it; a crate of the same hit points is.</summary>
    [Theory]
    [InlineData("models/props_junk/wood_crate001a.mdl", true)]
    [InlineData("models/props_c17/oildrum001_explosive.mdl", false)]
    public void AnExplosiveDrumIsLeftAlone(string model, bool breakable)
    {
        var h = new EntityHandle(0x0003_0001u);
        IEngineEntities engine = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
        {
            "FindAllByClassname" => (string)a[0]! == "prop_physics" ? new[] { h } : Array.Empty<EntityHandle>(),
            "GetSolidType" => SolidType.Bsp,
            "GetSpawnFlags" => 0,
            "GetHealth" => 40,
            _ => throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m),
        });
        var world = new EngineObstacleWorld(engine, _ => model);
        var into = new List<(EntityHandle, ObstacleBodyKind)>();
        for (int r = 0; r < world.RosterCount; r++)
            world.Roster(r, into);
        Assert.Equal(breakable, world.Breakable(h));
    }

    /// <summary>What a gun breaks: props and brushes with few enough hit points, not many, not none, not trigger-only.</summary>
    [Theory]
    [InlineData("prop_physics", 40, 0, true)]
    [InlineData("prop_physics", 0, 0, false)]
    [InlineData("prop_physics", 2000, 0, false)]
    [InlineData("func_breakable", 50, 0, true)]
    [InlineData("func_breakable", 50, 1, false)]
    [InlineData("func_wall", 50, 0, false)]
    public void WhatAGunBreaks(string className, int health, int flags, bool breakable)
    {
        var h = new EntityHandle(0x0003_0001u);
        IEngineEntities engine = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
        {
            "FindAllByClassname" => (string)a[0]! == className ? new[] { h } : Array.Empty<EntityHandle>(),
            "GetSolidType" => SolidType.Bsp,
            "GetSpawnFlags" => flags,
            "GetHealth" => health,
            _ => throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m),
        });
        var world = new EngineObstacleWorld(engine);
        var into = new List<(EntityHandle, ObstacleBodyKind)>();
        for (int r = 0; r < world.RosterCount; r++)
            world.Roster(r, into);
        Assert.Contains(into, x => x.Item1 == h);
        Assert.Equal(breakable, world.Breakable(h));
    }
}
