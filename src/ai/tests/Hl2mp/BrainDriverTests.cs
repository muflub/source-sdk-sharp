using SourceSharp.Ai.Hl2mp;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The brain's goal carried out each tick (plan_hl2mp_ai §2.3), and a
/// ladder climbed as a player climbs a func_useableladder: walk to its foot,
/// hold +use to mount, then look past the top and hold forward.
/// </summary>
public sealed class BrainDriverTests
{
    private static readonly ActorId Bot = new(2, 1);

    // A ground row at z 0 (x 0..4) and a loft row at z 192 above it; a ladder joins their east ends.
    private static (NavGraph Graph, CellId[] Ground, CellId[] Loft) Course()
    {
        var b = new NavGraphBuilder();
        var ground = new CellId[5];
        var loft = new CellId[5];
        for (int x = 0; x < 5; x++)
        {
            ground[x] = b.AddCell(new NavCell(x * 32f, 0f, x * 32f + 32f, 32f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            loft[x] = b.AddCell(new NavCell(x * 32f, 64f, x * 32f + 32f, 96f, 192f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        }

        for (int x = 0; x < 4; x++)
        {
            b.AddBoth(ground[x], ground[x + 1], NavEdgeType.Walk);
            b.AddBoth(loft[x], loft[x + 1], NavEdgeType.Walk);
        }

        b.AddBoth(ground[4], loft[4], NavEdgeType.Ladder);
        return (b.Build(), ground, loft);
    }

    private static IntentCommand Brain(AiVector goal)
        => new(Bot, new Tick(1), 1, new Tick(17), new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, goal, default, default, default, default, false, 0),
               LookIntent.AlongPath, ActionIntent.None, default);

    [Fact]
    public void AGoalOnTheGroundIsFollowedByCorners()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        IntentCommand c = d.Drive(Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, new Tick(2), 2, g, new NavObstacleState(0));
        Assert.Equal(MoveKind.Follow, c.Move.Kind);
        Assert.True(c.Move.Corner0.X > g.Cell(ground[0]).Centre.X, $"the first corner {c.Move.Corner0} leads east");
    }

    /// <summary>At the ladder's foot, with the loft as the goal, the bot holds +use and looks up past the top.</summary>
    [Fact]
    public void AtALaddersFootTheBotHoldsUseAndLooksUp()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre;
        IntentCommand c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(2), 2, g, new NavObstacleState(0));

        Assert.Equal(ActionKind.Use, c.Action.Kind);
        Assert.Equal(0, c.Action.OneShotSeq);
        Assert.True(c.Look.Point.Z > 192f, $"looks past the top: {c.Look.Point}");
        Assert.True(d.Mounts > 0);
    }

    /// <summary>On the ladder, the bot holds forward toward the far end, looks past it, and lets go of +use.</summary>
    [Fact]
    public void OnTheLadderTheBotHoldsForwardAndLooksPastTheTop()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre;
        d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(2), 2, g, new NavObstacleState(0));
        IntentCommand c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot + new AiVector(0f, 8f, 40f), new Tick(3), 3, g, new NavObstacleState(0), onLadder: true);

        Assert.Equal(MoveKind.Strafe, c.Move.Kind);
        Assert.True(c.Move.Direction.Y > 0.5f, $"forward, toward the loft's side: {c.Move.Direction}");
        Assert.True(c.Look.Point.Z > 192f, $"looks past the top: {c.Look.Point}");
        Assert.NotEqual(ActionKind.Use, c.Action.Kind);
        Assert.True(d.Climbs > 0);
    }

    /// <summary>On the ladder but gaining no height for half a second (its end), the bot holds +use to get off at the dismount.</summary>
    [Fact]
    public void StalledAtTheLaddersEndTheBotHoldsUseToGetOff()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre, high = foot + new AiVector(0f, 8f, 150f);
        d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(2), 2, g, new NavObstacleState(0));
        bool used = false;
        for (int t = 3; t < 80; t++)
            used |= d.Drive(Brain(g.Cell(loft[0]).Centre), high, new Tick(t), t, g, new NavObstacleState(0), onLadder: true).Action.Kind == ActionKind.Use;
        Assert.True(used);
        Assert.NotEqual(ActionKind.Use, d.Drive(Brain(g.Cell(loft[0]).Centre), high + new AiVector(0f, 0f, 10f), new Tick(80), 80, g,
                                                new NavObstacleState(0), onLadder: true).Action.Kind);
    }
}
