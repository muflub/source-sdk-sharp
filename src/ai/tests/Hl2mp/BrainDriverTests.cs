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

    /// <summary>The view a bot has once it looks where the command says, as the applier's motion gets it there.</summary>
    private static AiAngles Looked(in IntentCommand c, AiVector origin)
        => BotCommandApplier.AnglesTo(origin + new AiVector(0f, 0f, 64f), c.Look.Point);

    /// <summary>
    /// At the ladder's foot, with the loft as the goal, the bot looks up past
    /// the top first, and holds +use once it does: forward, the moment the
    /// mount lands, gets a bot looking across the ladder off again (live,
    /// dm_lockdown: on and off at the foot in a tick, every 33).
    /// </summary>
    [Fact]
    public void AtALaddersFootTheBotLooksUpAndThenHoldsUse()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre;
        IntentCommand c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(2), 2, g, new NavObstacleState(0));

        Assert.NotEqual(ActionKind.Use, c.Action.Kind);
        Assert.True(c.Look.Point.Z > 192f, $"looks past the top: {c.Look.Point}");
        Assert.True(d.Mounts > 0);

        c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(3), 3, g, new NavObstacleState(0), view: Looked(c, foot));
        Assert.Equal(ActionKind.Use, c.Action.Kind);
        Assert.Equal(0, c.Action.OneShotSeq);
    }

    /// <summary>
    /// +use takes a ladder the player faces (stock's LookingAtLadder), and the far
    /// end's cell can be behind a bot that has come past the ladder: it looks at
    /// the ladder (live, dm_lockdown: refused at its foot, for good).
    /// </summary>
    [Fact]
    public void AtTheFootTheBotFacesTheLadderNotTheFarEnd()
    {
        var (g, ground, loft) = Course();
        AiVector foot = g.Cell(ground[4]).Centre;
        var d = new BrainDriver { Ladders = [foot + new AiVector(-30f, 0f, 165f)] };
        IntentCommand c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(2), 2, g, new NavObstacleState(0));
        Assert.True(c.Look.Point.X < foot.X - 8f && MathF.Abs(c.Look.Point.Y - foot.Y) < 1f, $"faces the ladder to its west: {c.Look.Point} from {foot}");
        Assert.True(c.Look.Point.Z > 192f, $"and up past the top: {c.Look.Point}");
    }

    /// <summary>
    /// At the foot the bot stands to mount: a walk into the ladder's cone mounts
    /// it too, and +use on the tick that lands gets it off again (live,
    /// dm_lockdown). The walk comes back if +use has not taken it.
    /// </summary>
    [Fact]
    public void AtTheFootTheBotStandsToMountAndWalksAgainIfItDoesNotTake()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre, goal = g.Cell(loft[0]).Centre;
        IntentCommand c = d.Drive(Brain(goal), foot, new Tick(2), 2, g, new NavObstacleState(0));
        Assert.Equal(MoveKind.Hold, c.Move.Kind);
        for (int t = 3; t < 3 + BrainDriver.SettleTicks * 2 + BrainDriver.MountEvery * 2; t++)
            c = d.Drive(Brain(goal), foot, new Tick(t), t, g, new NavObstacleState(0), view: Looked(c, foot));
        Assert.NotEqual(MoveKind.Hold, c.Move.Kind);
    }

    /// <summary>A view that never gets there does not hold the bot off the ladder for good.</summary>
    [Fact]
    public void AMountStopsWaitingForTheView()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre;
        bool used = false;
        for (int t = 2; t < 2 + BrainDriver.SettleTicks + 2; t++)
            used |= d.Drive(Brain(g.Cell(loft[0]).Centre), foot, new Tick(t), t, g, new NavObstacleState(0)).Action.Kind == ActionKind.Use;
        Assert.True(used);
    }

    /// <summary>Mounted with its view still across the ladder, the bot holds no forward until it is looking along it.</summary>
    [Fact]
    public void MountedLookingAcrossTheLadderTheBotHoldsNoForwardYet()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre;
        d.Drive(Brain(goal), foot, new Tick(2), 2, g, new NavObstacleState(0));
        IntentCommand c = d.Drive(Brain(goal), foot, new Tick(3), 3, g, new NavObstacleState(0), onLadder: true);
        Assert.Equal(MoveKind.Hold, c.Move.Kind);
        c = d.Drive(Brain(goal), foot, new Tick(4), 4, g, new NavObstacleState(0), onLadder: true, view: Looked(c, foot));
        Assert.Equal(MoveKind.Strafe, c.Move.Kind);
    }

    /// <summary>Pitched right but still turning to the far end's way, forward would be back and side: the bot holds no forward until it has turned.</summary>
    [Fact]
    public void MountedStillTurningTheBotHoldsNoForwardYet()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre;
        // However long it stood at the foot first: the wait is the ladder's, from the mount.
        for (int t = 2; t < 2 + BrainDriver.SettleTicks * 2; t++)
            d.Drive(Brain(goal), foot, new Tick(t), t, g, new NavObstacleState(0));
        int on = 2 + BrainDriver.SettleTicks * 2;
        IntentCommand c = d.Drive(Brain(goal), foot, new Tick(on), on, g, new NavObstacleState(0), onLadder: true);
        AiAngles looked = Looked(c, foot);
        c = d.Drive(Brain(goal), foot, new Tick(on + 1), on + 1, g, new NavObstacleState(0), onLadder: true, view: looked with { Yaw = looked.Yaw + 150f });
        Assert.Equal(MoveKind.Hold, c.Move.Kind);
    }

    /// <summary>Holds a bot on the ladder through a mount at the foot, then with its view along it.</summary>
    private static IntentCommand Mounted(BrainDriver d, NavGraph g, AiVector goal, AiVector foot)
    {
        d.Drive(Brain(goal), foot, new Tick(2), 2, g, new NavObstacleState(0));
        IntentCommand c = d.Drive(Brain(goal), foot, new Tick(3), 3, g, new NavObstacleState(0), onLadder: true);
        return d.Drive(Brain(goal), foot, new Tick(4), 4, g, new NavObstacleState(0), onLadder: true, view: Looked(c, foot));
    }

    /// <summary>On the ladder, the bot holds forward toward the far end, looks past it, and lets go of +use.</summary>
    [Fact]
    public void OnTheLadderTheBotHoldsForwardAndLooksPastTheTop()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre;
        Mounted(d, g, g.Cell(loft[0]).Centre, foot);
        IntentCommand c = d.Drive(Brain(g.Cell(loft[0]).Centre), foot + new AiVector(0f, 8f, 40f), new Tick(5), 5, g, new NavObstacleState(0), onLadder: true);

        Assert.Equal(MoveKind.Strafe, c.Move.Kind);
        Assert.True(c.Move.Direction.Y > 0.5f, $"forward, toward the loft's side: {c.Move.Direction}");
        Assert.True(c.Look.Point.Z > 192f, $"looks past the top: {c.Look.Point}");
        Assert.NotEqual(ActionKind.Use, c.Action.Kind);
        Assert.True(d.Climbs > 0);
    }

    /// <summary>
    /// A climb gaining height never asks to get off: the stall's first height
    /// was never written, so +use fired every 33 ticks and let go of the ladder
    /// wherever the bot was (live, dm_lockdown: off at z 27, 63, 96).
    /// </summary>
    [Fact]
    public void AClimbGainingHeightNeverPressesUse()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre;
        Mounted(d, g, goal, foot);
        for (int t = 5; t < 5 + 3 * BrainDriver.MountEvery; t++)
        {
            IntentCommand c = d.Drive(Brain(goal), foot + new AiVector(0f, 8f, (t - 5) * 1.5f), new Tick(t), t, g, new NavObstacleState(0), onLadder: true);
            Assert.NotEqual(ActionKind.Use, c.Action.Kind);
        }
    }

    /// <summary>
    /// On the ladder but gaining no height for half a second (its end, or
    /// someone on it), the bot holds +use to get off at the dismount; the moment
    /// it climbs again it lets go.
    /// </summary>
    [Fact]
    public void StalledAtTheLaddersEndTheBotHoldsUseToGetOff()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre, high = foot + new AiVector(0f, 8f, 150f);
        Mounted(d, g, goal, foot);
        d.Drive(Brain(goal), high, new Tick(5), 5, g, new NavObstacleState(0), onLadder: true);
        int first = -1;
        for (int t = 6; t < 80 && first < 0; t++)
        {
            if (d.Drive(Brain(goal), high, new Tick(t), t, g, new NavObstacleState(0), onLadder: true).Action.Kind == ActionKind.Use)
                first = t;
        }

        Assert.Equal(5 + BrainDriver.MountEvery, first);
        Assert.NotEqual(ActionKind.Use, d.Drive(Brain(goal), high + new AiVector(0f, 0f, 10f), new Tick(first + BrainDriver.MountHold), first + BrainDriver.MountHold, g,
                                                new NavObstacleState(0), onLadder: true).Action.Kind);
    }

    /// <summary>With no dismount in reach +use cannot get a player off an end; a jump does, and a stalled bot ends up jumping.</summary>
    [Fact]
    public void StalledOnPastItsUsesTheBotJumpsOff()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre, high = foot + new AiVector(0f, 8f, 150f);
        Mounted(d, g, goal, foot);
        bool jumped = false;
        for (int t = 5; t < 5 + (BrainDriver.UsesBeforeJump + 2) * BrainDriver.MountEvery; t++)
            jumped |= d.Drive(Brain(goal), high, new Tick(t), t, g, new NavObstacleState(0), onLadder: true).Move.Kind == MoveKind.Jump;
        Assert.True(jumped);
    }

    /// <summary>
    /// Past the height of its top floor (a ladder runs on above it), the climb
    /// still looks up: asked from where the bot is, it looked down and climbed
    /// back, never reaching the end its dismount is at (live, dm_lockdown).
    /// </summary>
    [Fact]
    public void PastItsTopFloorTheClimbStillLooksUp()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre;
        Mounted(d, g, goal, foot);
        AiVector past = foot + new AiVector(0f, 8f, 200f);
        IntentCommand c = d.Drive(Brain(goal), past, new Tick(5), 5, g, new NavObstacleState(0), onLadder: true);
        Assert.True(c.Look.Point.Z > past.Z, $"looks up past the top floor: {c.Look.Point}");
    }

    /// <summary>
    /// A route whose first ladder step is a cell on the bot's own floor climbs to
    /// the far end of the run of Ladder edges, not to that cell: live, a climb
    /// aimed at it looked at its floor from half way up and came back down.
    /// </summary>
    [Fact]
    public void AClimbAimsAtTheFarEndOfTheRunOfLadderEdges()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId near = b.AddCell(new NavCell(0f, 0f, 32f, 32f, 0f, Both, NavCellFlags.None, 1f));
        CellId foot = b.AddCell(new NavCell(32f, 0f, 64f, 32f, 0f, Both, NavCellFlags.Ladder, 1f));
        CellId top = b.AddCell(new NavCell(64f, 0f, 96f, 32f, 128f, Both, NavCellFlags.None, 1f));
        b.AddBoth(near, foot, NavEdgeType.Ladder);
        b.AddBoth(foot, top, NavEdgeType.Ladder);
        NavGraph g = b.Build();
        var d = new BrainDriver();
        IntentCommand c = d.Drive(Brain(g.Cell(top).Centre), g.Cell(near).Centre, new Tick(2), 2, g, new NavObstacleState(0));
        Assert.True(c.Look.Point.Z > 128f, $"looks past the far end: {c.Look.Point}");
    }

    /// <summary>
    /// A ladder link along the floor (a ladder's foot beside the bot, the route
    /// going on across the floor) is walked: mounting for it, the bot got off at
    /// the foot and walked back to mount again (live, dm_lockdown).
    /// </summary>
    [Fact]
    public void ALadderLinkAlongTheFloorIsWalkedNotClimbed()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId near = b.AddCell(new NavCell(0f, 0f, 32f, 32f, 0f, Both, NavCellFlags.None, 1f));
        CellId foot = b.AddCell(new NavCell(32f, 0f, 64f, 32f, 0f, Both, NavCellFlags.Ladder, 1f));
        CellId far = b.AddCell(new NavCell(64f, 0f, 96f, 32f, 0f, Both, NavCellFlags.None, 1f));
        b.AddEdge(near, foot, NavEdgeType.Ladder);
        b.AddBoth(foot, far, NavEdgeType.Walk);
        NavGraph g = b.Build();
        var d = new BrainDriver();
        IntentCommand c = default;
        for (int t = 2; t < 2 + BrainDriver.MountEvery * 2; t++)
        {
            c = d.Drive(Brain(g.Cell(far).Centre), g.Cell(near).Centre, new Tick(t), t, g, new NavObstacleState(0));
            Assert.NotEqual(ActionKind.Use, c.Action.Kind);
        }

        Assert.Equal(MoveKind.Follow, c.Move.Kind);
    }

    /// <summary>
    /// A descent looks down the whole way, and holds forward with the view there.
    /// </summary>
    [Fact]
    public void ADescentLooksDownTheLadder()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector top = g.Cell(loft[4]).Centre, goal = g.Cell(ground[0]).Centre;
        IntentCommand c = d.Drive(Brain(goal), top, new Tick(2), 2, g, new NavObstacleState(0));
        Assert.True(c.Look.Point.Z < 0f, $"looks past the foot: {c.Look.Point}");
        c = d.Drive(Brain(goal), top, new Tick(3), 3, g, new NavObstacleState(0), view: Looked(c, top));
        Assert.Equal(ActionKind.Use, c.Action.Kind);
    }

    /// <summary>
    /// The climb outlives its follower: a new goal every half second is a new
    /// follower, and one forgetting the climb left the bot on the ladder with
    /// the walking move (live, dm_lockdown: at the top pressing forward at a
    /// goal behind it, for good). So does a brain that has stopped following.
    /// </summary>
    [Fact]
    public void ANewGoalMidClimbFinishesTheClimb()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector foot = g.Cell(ground[4]).Centre, up = foot + new AiVector(0f, 8f, 60f);
        Mounted(d, g, g.Cell(loft[0]).Centre, foot);
        IntentCommand c = d.Drive(Brain(g.Cell(ground[0]).Centre), up, new Tick(5), 5, g, new NavObstacleState(0), onLadder: true);
        Assert.Equal(MoveKind.Strafe, c.Move.Kind);
        Assert.True(c.Look.Point.Z > 192f, $"still up: {c.Look.Point}");

        var hold = new IntentCommand(Bot, new Tick(1), 1, new Tick(17), MoveIntent.Hold, LookIntent.AlongPath, ActionIntent.None, default);
        c = d.Drive(in hold, up + new AiVector(0f, 0f, 3f), new Tick(6), 6, g, new NavObstacleState(0), onLadder: true);
        Assert.Equal(MoveKind.Strafe, c.Move.Kind);
    }

    /// <summary>A mount's forced move is no walking: the climb it asked for is still the one it lands on.</summary>
    [Fact]
    public void TheClimbSurvivesTheMountsForcedMove()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(loft[0]).Centre, foot = g.Cell(ground[4]).Centre;
        d.Drive(Brain(goal), foot, new Tick(2), 2, g, new NavObstacleState(0));
        // Mid forced move: not on the ladder, and the follower's cell is no foot any more.
        d.Drive(Brain(goal), g.Cell(ground[2]).Centre, new Tick(3), 3, g, new NavObstacleState(0));
        // On it, mid-air off the graph: only the climb asked for says which way.
        IntentCommand c = d.Drive(Brain(goal), foot + new AiVector(0f, 24f, 100f), new Tick(4), 4, g, new NavObstacleState(0), onLadder: true);
        Assert.True(c.Look.Point.Z > 192f, $"looks past the top: {c.Look.Point}");
        Assert.NotEqual(ActionKind.Use, c.Action.Kind);
    }

    /// <summary>
    /// Put on a ladder its route does not climb (a walk into its cone, a fall
    /// past it): the bot asks to get off at once, and with +use refused, jumps.
    /// </summary>
    [Fact]
    public void OnALadderItsRouteDoesNotClimbTheBotGetsOff()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[0]).Centre, at = g.Cell(ground[2]).Centre + new AiVector(0f, 0f, 30f);
        IntentCommand c = d.Drive(Brain(goal), at, new Tick(2), 2, g, new NavObstacleState(0), onLadder: true);
        Assert.Equal((MoveKind.Hold, ActionKind.Use), (c.Move.Kind, c.Action.Kind));
        bool jumped = false;
        for (int t = 3; t < 3 + (BrainDriver.UsesBeforeJump + 1) * BrainDriver.MountEvery; t++)
            jumped |= d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0), onLadder: true).Move.Kind == MoveKind.Jump;
        Assert.True(jumped);
    }

    /// <summary>A bot of the brains making no progress for 3 s acts, on any edge: the detector was armed only for a scripted walk (live: no stuck line for the walkway's bots).</summary>
    [Fact]
    public void ABrainDrivenBotThatMakesNoProgressReportsStuckAndActs()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, at = g.Cell(ground[0]).Centre;
        // Moving about (so its body has not stopped) and getting no nearer.
        AiVector About(int t) => at + new AiVector(0f, t % 2 * (BrainDriver.BodyBlockUnits + 1f), 0f);
        int t = 2;
        for (; t < 2 + BrainDriver.StuckTicks; t++)
            d.Drive(Brain(goal), About(t), new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(0, d.Stucks);
        Assert.Null(d.TakeStuckLine(Bot.Slot));
        for (; t < 2 + BrainDriver.StuckTicks + 4; t++)
            d.Drive(Brain(goal), About(t), new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(1, d.Stucks);
        Assert.Contains("is stuck at", d.TakeStuckLine(Bot.Slot), StringComparison.Ordinal);
        Assert.Null(d.TakeStuckLine(Bot.Slot));
    }

    /// <summary>A body stopped dead on a plain floor acts inside a second: a heater in the way looks the same as no way at all for three.</summary>
    [Fact]
    public void ABodyStoppedDeadActsInsideASecond()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, at = g.Cell(ground[0]).Centre;
        int t = 2;
        for (; t < 2 + BrainDriver.BodyBlockTicks; t++)
            d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(0, d.Stucks);
        for (; t < 2 + BrainDriver.BodyBlockTicks + 2; t++)
            d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(1, d.Stucks);
        Assert.Equal(40, BrainDriver.BodyBlockTicks);
    }

    /// <summary>Stopped at one place again and again, whatever the route says, it tries a different way each time, and a jump every other; moved away, that is over.</summary>
    [Fact]
    public void ABodyWedgedTriesEachWayInTurnAndMovingFreesIt()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, at = g.Cell(ground[0]).Centre;
        var ways = new List<(MoveKind Kind, AiVector Way)>();
        MoveKind last = MoveKind.Hold;
        int t = 2;
        for (; t < 2 + 6 * (BrainDriver.BodyBlockTicks + BrainDriver.WriggleTicks); t++)
        {
            IntentCommand c = d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0));
            bool wriggling = c.Move.Kind == MoveKind.Strafe || (c.Move.Kind == MoveKind.Jump && c.Move.CornerCount == 1 && c.Move.Corner0.DistanceTo(at) < 65f);
            if (wriggling && last != c.Move.Kind)
                ways.Add((c.Move.Kind, c.Move.Kind == MoveKind.Strafe ? c.Move.Direction : (c.Move.Corner0 - at).Normalised()));
            last = wriggling ? c.Move.Kind : MoveKind.Hold;
        }

        Assert.True(ways.Count >= 2, $"tries: {ways.Count}");
        Assert.Contains(ways, w => w.Kind == MoveKind.Strafe);
        Assert.Contains(ways, w => w.Kind == MoveKind.Jump);
        for (int i = 1; i < ways.Count; i++)
            Assert.True(ways[i].Way.DistanceTo(ways[i - 1].Way) > 1f, "the same way again");

        // Free, in the middle of a try: it goes back to following at once.
        for (int k = 0; k < 2 * (BrainDriver.BodyBlockTicks + BrainDriver.WriggleTicks); k++, t++)
        {
            if (d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0)).Move.Kind is MoveKind.Strafe or MoveKind.Jump)
                break;
        }

        AiVector away = at + new AiVector(0f, BrainDriver.WedgeUnits + 16f, 0f);
        MoveKind next = d.Drive(Brain(goal), away, new Tick(t + 1), t + 1, g, new NavObstacleState(0)).Move.Kind;
        Assert.False(next is MoveKind.Strafe or MoveKind.Jump, $"still wriggling: {next}");
    }

    /// <summary>The first stop is a route round only: no one wriggles at a crate before trying to walk round it.</summary>
    [Fact]
    public void AFirstStopDoesNotWriggle()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, at = g.Cell(ground[0]).Centre;
        for (int t = 2; t < 2 + BrainDriver.BodyBlockTicks + 2; t++)
            Assert.NotEqual(MoveKind.Strafe, d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0)).Move.Kind);
        Assert.Equal(1, d.Stucks);
    }

    /// <summary>The body creeping on (a shove of a crate) has not stopped, and arrival is no blockage.</summary>
    [Fact]
    public void ABodyStillMovingOrArrivedHasNotStopped()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, at = g.Cell(ground[0]).Centre;
        for (int t = 2; t < 2 + BrainDriver.BodyBlockTicks * 2; t++)
            d.Drive(Brain(goal), at + (goal - at).Normalised() * ((t - 2) * 0.4f), new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(0, d.Stucks);
        var there = new BrainDriver();
        for (int t = 2; t < 2 + BrainDriver.BodyBlockTicks * 3; t++)
            there.Drive(Brain(goal), goal, new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(0, there.Stucks);
    }

    /// <summary>A goal re-chosen every half second is no progress: a hunt stood at the walkway's wall for good, its patience started over each time.</summary>
    [Fact]
    public void ANewGoalEveryHalfSecondDoesNotResetThePatience()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector at = g.Cell(ground[0]).Centre;
        for (int t = 2; t < 2 + BrainDriver.StuckTicks + BrainDriver.RegoalTicks * 2; t++)
            d.Drive(Brain(g.Cell(ground[1 + (t / BrainDriver.RegoalTicks) % 2 + 2]).Centre), at + new AiVector(0f, t % 2 * (BrainDriver.BodyBlockUnits + 1f), 0f), new Tick(t), t, g, new NavObstacleState(0));
        Assert.True(d.Stucks >= 1, "no stuck report");
    }

    /// <summary>A bot walking its route is not stuck, and neither is one that has arrived: that half keeps the detector from firing always.</summary>
    [Fact]
    public void ABotWalkingOrArrivedIsNotStuck()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver();
        AiVector goal = g.Cell(ground[4]).Centre, from = g.Cell(ground[0]).Centre;
        int walk = BrainDriver.StuckTicks * 2;
        for (int t = 2; t < 2 + walk; t++)
            d.Drive(Brain(goal), from + (goal - from) * ((t - 2) / (float)walk), new Tick(t), t, g, new NavObstacleState(0));
        for (int t = 2 + walk; t < 2 + walk + BrainDriver.StuckTicks * 2; t++)
            d.Drive(Brain(goal), goal, new Tick(t), t, g, new NavObstacleState(0));

        Assert.Equal(0, d.Stucks);
    }

    /// <summary>
    /// A goal nothing reaches: after half a second the bot goes as near as it can
    /// get, not a stand at the first wall (live: 45 s at "NoRoute", jumping). Its
    /// goal coming within reach would be a new goal.
    /// </summary>
    [Fact]
    public void AGoalNothingReachesIsGoneAsNearAsTheBotCan()
    {
        var b = new NavGraphBuilder();
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var row = new CellId[5];
        for (int x = 0; x < 5; x++)
            row[x] = b.AddCell(new NavCell(x * 32f, 0f, x * 32f + 32f, 32f, 0f, Both, NavCellFlags.None, 1f));
        for (int x = 0; x < 4; x++)
            b.AddBoth(row[x], row[x + 1], NavEdgeType.Walk);
        CellId island = b.AddCell(new NavCell(320f, 0f, 352f, 32f, 0f, Both, NavCellFlags.None, 1f));
        NavGraph g = b.Build();
        var d = new BrainDriver();
        AiVector at = g.Cell(row[0]).Centre, goal = g.Cell(island).Centre;
        IntentCommand c = default;
        for (int t = 2; t < 2 + BrainDriver.NoRouteTicks + 3; t++)
            c = d.Drive(Brain(goal), at, new Tick(t), t, g, new NavObstacleState(0));
        Assert.Equal(MoveKind.Follow, c.Move.Kind);
        Assert.True(c.Move.Corner0.X > g.Cell(row[3]).MinX && c.Move.Corner0.X < 320f, $"heads for the nearest it can reach: {c.Move.Corner0}");
        Assert.Equal(goal, d.TakeUnreachable(Bot.Slot));   // and the brain hears, once
        Assert.Null(d.TakeUnreachable(Bot.Slot));
    }

    // ---- a breakable on the route (§6.2) ---------------------------------------

    private sealed class Aim(AiVector point, bool clear = true) : SourceSharp.Ai.Hl2mp.Nav.IBreakableAim
    {
        public bool TryAim(int cell, AiVector eye, SourceSharp.Abi.EntityHandle shooter, out AiVector at)
        {
            at = point;
            return clear;
        }
    }

    private static NavObstacleState Breaking(NavGraph g, params int[] cells)
    {
        var bytes = new byte[g.CellCount];
        foreach (int c in cells)
            bytes[c] = NavCellBlocks.Breakable;
        return new NavObstacleState(0) { Cells = new NavCellBlocks(bytes) };
    }

    private static IntentCommand Walk(BrainDriver d, NavGraph g, IntentCommand brain, AiVector at, NavObstacleState state, int tick = 2)
        => d.Drive(brain, at, new Tick(tick), tick, g, state);

    [Fact]
    public void ACrateOnTheRouteIsShotAndWalkedThroughOnceItIsGone()
    {
        var (g, ground, _) = Course();
        AiVector crate = g.Cell(ground[2]).Centre + new AiVector(0f, 0f, 24f);
        var d = new BrainDriver { Breakables = new Aim(crate) };
        IntentCommand i = Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, Breaking(g, ground[2].Index));
        Assert.Equal((ActionKind.Fire, LookKind.CornerCheck, crate), (i.Action.Kind, i.Look.Kind, i.Look.Point));
        Assert.Equal(1, d.Breaks);
        // Gone: no state, no shot, on it goes.
        i = Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, new NavObstacleState(0), 3);
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.Equal(MoveKind.Follow, i.Move.Kind);
    }

    /// <summary>Near it, it stands and shoots; walking into a crate does no good.</summary>
    [Fact]
    public void AtTheCrateItStands()
    {
        var (g, ground, _) = Course();
        AiVector crate = g.Cell(ground[2]).Centre;
        var d = new BrainDriver { Breakables = new Aim(crate) };
        IntentCommand i = Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[1]).Centre, Breaking(g, ground[2].Index));
        Assert.Equal(MoveKind.Hold, i.Move.Kind);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>An unbreakable one is not fired at: its cells are not breakable, whatever is asked.</summary>
    [Fact]
    public void AnUnbreakableOneIsNotShot()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver { Breakables = new Aim(g.Cell(ground[2]).Centre) };
        var bytes = new byte[g.CellCount];
        bytes[ground[2].Index] = NavCellBlocks.Costly;
        IntentCommand i = Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, new NavObstacleState(0) { Cells = new NavCellBlocks(bytes) });
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.Equal(0, d.Breaks);
    }

    /// <summary>Someone in view comes first: the brain's look and hands stand.</summary>
    [Fact]
    public void ASeenEnemyComesFirst()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver { Breakables = new Aim(g.Cell(ground[2]).Centre) };
        IntentCommand brain = Brain(g.Cell(ground[4]).Centre) with { Look = new LookIntent(LookKind.Actor, new AiVector(0f, 500f, 48f), new ActorId(3, 1), AimPoint.Body, 0f, 0), Action = new ActionIntent(ActionKind.Fire, FireStyle.Burst, 0, 0) };
        IntentCommand i = Walk(d, g, brain, g.Cell(ground[0]).Centre, Breaking(g, ground[2].Index));
        Assert.Equal((LookKind.Actor, FireStyle.Burst), (i.Look.Kind, i.Action.Style));
        Assert.Equal(0, d.Breaks);
    }

    /// <summary>A prop that is not in the way is left alone: the loft's crate, while the route is along the ground.</summary>
    [Fact]
    public void APropNotInTheWayIsLeftAlone()
    {
        var (g, ground, loft) = Course();
        var d = new BrainDriver { Breakables = new Aim(g.Cell(loft[2]).Centre) };
        IntentCommand i = Walk(d, g, Brain(g.Cell(ground[3]).Centre), g.Cell(ground[0]).Centre, Breaking(g, loft[2].Index));
        Assert.Equal(ActionKind.None, i.Action.Kind);
    }

    /// <summary>With no clear line it does not fire: it walks on until it has one.</summary>
    [Fact]
    public void NoLineNoShot()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver { Breakables = new Aim(g.Cell(ground[2]).Centre, clear: false) };
        IntentCommand i = Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, Breaking(g, ground[2].Index));
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.Equal(MoveKind.Follow, i.Move.Kind);
    }

    /// <summary>One that does not go gets its patience, and then the bot asks for a way round.</summary>
    [Fact]
    public void ItGivesUpOnOneThatWillNotBreak()
    {
        var (g, ground, _) = Course();
        var d = new BrainDriver { Breakables = new Aim(g.Cell(ground[2]).Centre) };
        NavObstacleState state = Breaking(g, ground[2].Index);
        for (int t = 2; t < BrainDriver.BreakPatienceTicks + 4; t++)
            Walk(d, g, Brain(g.Cell(ground[4]).Centre), g.Cell(ground[0]).Centre, state, t);
        Assert.Equal(1, d.BreaksGivenUp);
    }
}
