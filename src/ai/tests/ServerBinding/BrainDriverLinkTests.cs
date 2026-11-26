using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// The drive and a route's link (lane G5c-2b): the follower's report goes out with the step's result,
/// the brain's one-shot naming the link begins it once, and a bot on a link it has begun is the
/// program's: nobody reports it stuck while it stands for its view.
/// </summary>
public sealed class BrainDriverLinkTests
{
    private static readonly ActorId Bot = new(2, 1);

    private static readonly string[] Gap =
    [
        ".................",
        ".               .",
        "........   ......",
    ];

    private static (NavGraph Graph, int[,] Ids, NavObstacleState State) Rig(bool links = true)
    {
        var (g, ids) = Grids.Parse(Gap);
        var hop = new NavLink(ids[7, 2], ids[11, 2], NavLinkKind.Blast, 1f, 30f, 1f, 0) { Start = g.Cell(new CellId(ids[7, 2])).Centre + new AiVector(-10f, 5f, 0f) };
        NavLinks set = NavLinks.Build(g, [hop], new NavLinkCosts { RunSpeed = 240f });
        return (g, ids, new NavObstacleState(0) { Links = links ? set : null, Reach = JumpReach.Blast, LinkHealth = 100f });
    }

    private static AiVector Centre(NavGraph g, int[,] ids, int x, int y) => g.Cell(new CellId(ids[x, y])).Centre;

    private static IntentCommand Brain(AiVector goal, int tick, ActionIntent action = default)
        => new(Bot, new Tick(tick), 1, new Tick(tick + IntentCommand.LifetimeTicks),
               new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, goal, default, default, default, default, false, 0), LookIntent.AlongPath, action, default);

    private static ActionIntent Begin(int link, int seq) => new(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(3, NavLinks.Argument(link)), seq);

    [Fact]
    public void TheDriveSaysTheFollowersLinkStep()
    {
        var (g, ids, state) = Rig();
        var d = new BrainDriver();
        AiVector goal = Centre(g, ids, 14, 2), start = Centre(g, ids, 7, 2) + new AiVector(-10f, 5f, 0f);
        d.Drive(Brain(goal, 1), start, new Tick(1), 1, g, state);
        NavLinkStep step = d.LinkStepOf(Bot.Slot);
        Assert.Equal((true, 0, true), (step.IsLink, step.Link, step.AtStart));
        Assert.False(d.LinkStepOf(Bot.Slot + 1).IsLink);
    }

    /// <summary>The step's result carries the report back to the main thread, as a value: what the brains are handed.</summary>
    [Fact]
    public void AStepsResultCarriesTheLinkStep()
    {
        var (g, ids, state) = Rig();
        var transport = new InlineTransport();
        var lanes = new BotDriveLanes(() => transport, 8);
        AiVector start = Centre(g, ids, 7, 2);
        var input = new DriveInput(Brain(Centre(g, ids, 14, 2), 1), start, new Tick(1), g, state, false, default, EntityHandle.None, [], [], false, BreakableSight.None);
        Assert.True(lanes.Hand(input, new BotSelfFacts(true, false, start, start + new AiVector(0f, 0f, 64f), default)));
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
        Assert.Equal((true, 0, true), (output.Link.IsLink, output.Link.Link, output.Link.AtStart));
    }

    [Fact]
    public void TheBrainsOneShotNamingTheLinkBeginsIt()
    {
        var (g, ids, state) = Rig();
        var d = new BrainDriver();
        AiVector goal = Centre(g, ids, 14, 2), start = Centre(g, ids, 7, 2) + new AiVector(-10f, 5f, 0f);
        d.Drive(Brain(goal, 1), start, new Tick(1), 1, g, state);
        // A one-shot of another row's argument, under the links' range, begins nothing.
        d.Drive(Brain(goal, 2, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(3, 7), 4)), start, new Tick(2), 2, g, state);
        Assert.Equal(0, d.FollowerOf(Bot.Slot)!.LinksBegun);
        d.Drive(Brain(goal, 3, Begin(0, 5)), start, new Tick(3), 3, g, state);
        Assert.Equal(1, d.FollowerOf(Bot.Slot)!.LinksBegun);
        Assert.True(d.FollowerOf(Bot.Slot)!.InLink);
    }

    /// <summary>
    /// A bot standing at a link it has begun (its view coming round, the program walking it onto the takeoff) does not
    /// get nearer its goal for longer than the driver's patience: it is not reported stuck. The control: a bot that
    /// stands as long with no link is.
    /// </summary>
    [Fact]
    public void ABotOnALinkItHasBegunIsNotReportedStuck()
    {
        var (g, ids, state) = Rig();
        var d = new BrainDriver();
        AiVector goal = Centre(g, ids, 14, 2), start = Centre(g, ids, 7, 2) + new AiVector(-10f, 5f, 0f);
        d.Drive(Brain(goal, 1), start, new Tick(1), 1, g, state);
        int ticks = BrainDriver.StuckTicks + 20;
        Assert.True(ticks < NavFollowerLinkTicks.Launch, "the program's time to launch outlasts the driver's patience");
        for (int t = 2; t < ticks; t++)
        {
            d.Drive(Brain(goal, t, Begin(0, 5)), start, new Tick(t), t, g, state);
            Assert.Null(d.TakeStuckLine(Bot.Slot));
        }

        var (g2, ids2, none) = Rig(links: false);
        var control = new BrainDriver();
        AiVector stood = Centre(g2, ids2, 4, 2);
        string? line = null;
        for (int t = 1; t < ticks && line is null; t++)
        {
            control.Drive(Brain(Centre(g2, ids2, 14, 2), t), stood, new Tick(t), t, g2, none);
            line = control.TakeStuckLine(Bot.Slot);
        }

        Assert.NotNull(line);
    }

    /// <summary>
    /// A goal only a link reaches, the link refused (nobody begins it): the bot goes as near as it can get, and when the
    /// link is back in its route the goal itself is tried again. Live on ss_ai_tf_jump a bot stood beside the 250 u
    /// ledge for good: the nearer goal it had been given was never let go.
    /// </summary>
    [Fact]
    public void AGoalGivenUpWhileItsLinkWasLeftOutIsTriedAgainWhenTheLinkIsBack()
    {
        var (g, ids) = Grids.Parse("........   ......");
        var hop = new NavLink(ids[7, 0], ids[11, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0) { Start = g.Cell(new CellId(ids[7, 0])).Centre };
        NavLinks set = NavLinks.Build(g, [hop], new NavLinkCosts { RunSpeed = 240f });
        var state = new NavObstacleState(0) { Links = set, Reach = JumpReach.Blast, LinkHealth = 100f };
        var d = new BrainDriver();
        AiVector goal = Centre(g, ids, 14, 0), start = Centre(g, ids, 7, 0);
        int t = 1;
        bool gaveUp = false, back = false;
        for (; t < 2000 && !back; t++)
        {
            d.Drive(Brain(goal, t), start, new Tick(t), t, g, state);
            NavLinkStep step = d.LinkStepOf(Bot.Slot);
            gaveUp |= d.FollowerOf(Bot.Slot)!.Objective.Index != ids[14, 0];
            back = gaveUp && step.IsLink && step.AtStart;
        }

        Assert.True(gaveUp, "the control: with the link left out it went as near as it could get");
        Assert.True(back, "the link came back and the goal was not tried again");
        Assert.Equal(ids[14, 0], d.FollowerOf(Bot.Slot)!.Objective.Index);
    }

    private static class NavFollowerLinkTicks
    {
        public const int Launch = SourceSharp.Ai.Server.Nav.NavFollower.LinkLaunchMoves;
    }

    /// <summary>
    /// The step's result carries how many presses in the air the route just ahead needs (round 2's S2): a triple jump two
    /// cells on is 2, which the main thread hands the game's route reports.
    /// </summary>
    [Fact]
    public void AStepsResultCarriesTheDashesAhead()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId[] floor = [.. Enumerable.Range(0, 3).Select(i => b.AddCell(new NavCell(i * 64f, 0f, (i + 1) * 64f, 64f, 0f, Both, NavCellFlags.None, 1f)))];
        CellId ledge = b.AddCell(new NavCell(192f, 0f, 256f, 64f, 130f, Both, NavCellFlags.None, 1f));
        b.AddEdge(floor[0], floor[1], NavEdgeType.Walk);
        b.AddEdge(floor[1], floor[2], NavEdgeType.Walk);
        b.AddEdge(floor[2], ledge, NavEdgeType.TripleJump);
        NavGraph g = b.Build();
        var state = new NavObstacleState(0) { Reach = JumpReach.Double | JumpReach.Triple };
        var transport = new InlineTransport();
        var lanes = new BotDriveLanes(() => transport, 8);
        AiVector start = g.Cell(floor[0]).Centre;
        var input = new DriveInput(Brain(g.Cell(ledge).Centre, 1), start, new Tick(1), g, state, false, default, EntityHandle.None, [], [], false, BreakableSight.None);
        Assert.True(lanes.Hand(input, new BotSelfFacts(true, false, start, start + new AiVector(0f, 0f, 64f), default)));
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
        Assert.Equal(2, output.Dashes);
    }

    /// <summary>
    /// The bot's lane plans with the agent's own spends (round 2's S13): the lane copies the live state into its own each step
    /// (BotDriveLanes' CopyFrom), so a link that costs 30 against 20 to spend is not the route's at first, and is once the
    /// agent's spends say 10 on the next step.
    /// </summary>
    [Fact]
    public void TheLanePlansWithTheAgentsOwnSpendsAsTheyStandEachStep()
    {
        var (g, ids, state) = Rig();
        state.LinkHealth = 20f;
        var transport = new InlineTransport();
        var lanes = new BotDriveLanes(() => transport, 8);
        AiVector start = Centre(g, ids, 7, 2);
        bool Step(int tick)
        {
            var input = new DriveInput(Brain(Centre(g, ids, 14, 2), tick), start, new Tick(tick), g, state, false, default, EntityHandle.None, [], [], false, BreakableSight.None);
            Assert.True(lanes.Hand(input, new BotSelfFacts(true, false, start, start + new AiVector(0f, 0f, 64f), default)));
            Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
            return output.Link.IsLink;
        }

        Assert.False(Step(1));
        state.LinkSpends = [10f];
        Assert.True(Step(400));
    }
}
