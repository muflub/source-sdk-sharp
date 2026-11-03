using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// Obstacle flags from entity state (plan_hl2mp_ai §6.2): a door blocks while
/// shut where it spawned, a breakable while it stands, a prop while at its
/// place, a mover never, an unmatched obstacle never; the tracker keeps the
/// graph's state current, and a field routes round a shut door.
/// </summary>
public sealed class NavObstaclesTests
{
    [Theory]
    [InlineData(NavObstacleKind.Door, true, true, 0f, 0f, true)]
    [InlineData(NavObstacleKind.Door, true, true, 40f, 0f, false)]
    [InlineData(NavObstacleKind.Door, true, true, 0f, 90f, false)]
    [InlineData(NavObstacleKind.Breakable, true, true, 0f, 0f, true)]
    [InlineData(NavObstacleKind.Breakable, true, false, 0f, 0f, false)]
    [InlineData(NavObstacleKind.Physics, true, true, 8f, 0f, true)]
    [InlineData(NavObstacleKind.Physics, true, true, 40f, 0f, false)]
    [InlineData(NavObstacleKind.Mover, true, true, 0f, 0f, false)]
    [InlineData(NavObstacleKind.Door, false, false, 0f, 0f, false)]
    public void TheRuleReadsWhatAPlayerSees(NavObstacleKind kind, bool found, bool alive, float moved, float turned, bool blocking)
        => Assert.Equal(blocking, ObstacleRule.Blocking(kind, new ObstacleSighting(found, alive, moved, turned)));

    private sealed class Eyes : IObstacleEyes
    {
        public AiVector DoorAt = new(616, 256, 72);
        public string? Asked;

        public EntityHandle Find(string name, string className, AiVector near)
        {
            Asked = $"{name} {className}";
            return name == "door_1" ? new EntityHandle(0x0001_0005) : EntityHandle.None;
        }

        public bool Alive(EntityHandle entity) => true;

        public AiVector Origin(EntityHandle entity) => DoorAt;

        public AiVector Angles(EntityHandle entity) => default;
    }

    [Fact]
    public void TheTrackerBlocksTheShutDoorAndOpensItWhenItMoves()
    {
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(NavFixtures.Course().Open()), Hl2mpNavNumbers.Stock(56f),
                                                  NavEdgeCosts.Default, []);
        var eyes = new Eyes();
        var obstacles = new NavObstacles(nav, eyes);
        obstacles.Resolve();
        Assert.Equal(("door_1 func_door", 1, true), (eyes.Asked, obstacles.Matched, obstacles.State.Blocking(0)));

        int from = nav.Graph.FindCell(new AiVector(40, 40, 16)).Index, beyond = nav.Graph.FindCell(new AiVector(700, 256, 16)).Index;
        var field = new FlowField();
        field.Build(nav.Graph, new CellId(beyond), ClearanceClass.Stand, obstacles.State);
        // A shut door is a door to open (Phase 5): the route goes through it at its wait, the bot opening it as a player does...
        Assert.True(obstacles.State.OpenDoors);
        Assert.True(float.IsFinite(field.CostAt(from)));
        float shut = field.CostAt(from);
        // ...and to a planner that may not open doors it is a wall.
        var wall = new NavObstacleState(nav.Graph.ObstacleCount);
        wall.Set(0, true);
        field.Build(nav.Graph, new CellId(beyond), ClearanceClass.Stand, wall);
        Assert.False(float.IsFinite(field.CostAt(from)));

        eyes.DoorAt += new AiVector(0, 0, 100);
        obstacles.Update();
        field.Build(nav.Graph, new CellId(beyond), ClearanceClass.Stand, obstacles.State);
        Assert.Equal((false, 0), (obstacles.State.Blocking(0), obstacles.Blocking));
        Assert.True(float.IsFinite(field.CostAt(from)));
        Assert.True(field.CostAt(from) <= shut, $"open {field.CostAt(from)} against shut {shut}");
    }
}
