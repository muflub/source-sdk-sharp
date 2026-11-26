using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// An Atomizer Scout's triple jump on a route (round 2's S2), as the game is handed it: the follower's moves through the
/// applier's buttons. Each dash is a fresh press of the jump in the air (stock CTFGameMovement::CheckJumpButton), which
/// sets the speed upward to the dash's 268.33 u/s (CTFGameMovement::AirDash); the presses after the take-off must come
/// at each top, the third at the top of the second jump's own rise.
/// </summary>
public class NavTripleJumpPressTests
{
    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Facts = new(true, false, default, new AiVector(0f, 0f, 64f), default);

        public BotSelfFacts Read(EntityHandle player) => Facts;

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    /// <summary>A floor and a ledge 130 u over the column beside it, joined by an edge of <paramref name="type"/>.</summary>
    private static (NavGraph G, CellId Ledge) Course(NavEdgeType type)
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId floor = b.AddCell(new NavCell(0f, 0f, 16f, 16f, 0f, Both, NavCellFlags.None, 1f));
        CellId ledge = b.AddCell(new NavCell(0f, 16f, 16f, 32f, 130f, Both, NavCellFlags.None, 1f));
        b.AddEdge(floor, ledge, type);
        b.AddEdge(ledge, floor, NavEdgeType.Drop);
        return (b.Build(), ledge);
    }

    /// <summary>
    /// The ticks of each fresh press of the jump, and the tick of each top, over TF's ground jump (289 u/s) and its dashes
    /// (268.33 u/s, at most <paramref name="dashes"/>) under 800 u/s², one move and one usercmd a tick.
    /// </summary>
    private static (List<int> Presses, List<int> Tops, float Highest, long Counted, int Landed) Fly(NavEdgeType type, int dashes)
    {
        var (g, ledge) = Course(type);
        var follower = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch,
                                       new NavObstacleState(0) { Reach = JumpReach.Double | JumpReach.Triple });
        var runner = new Runner();
        var self = new Self();
        var applier = new BotCommandApplier(runner, self);
        var bot = new ActorId(3, 1);
        applier.Add(bot, new EntityHandle(0x0001_0003u), new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
        const float Dt = 0.015f, JumpSpeed = 289f, DashSpeed = 268.3281573f, Gravity = 800f;
        var presses = new List<int>();
        var tops = new List<int>();
        bool was = false, jumped = false;
        int dashed = 0, landed = -1;
        float z = 0f, vz = 0f, highest = 0f;
        for (int t = 0; t < 120; t++)
        {
            var at = new AiVector(8f, 8f, z);
            self.Facts = self.Facts with { Origin = at, Eye = at + new AiVector(0f, 0f, 64f) };
            MoveIntent move = follower.Next(at, out _);
            var tick = new Tick(100 + t);
            applier.Submit(new IntentCommand(bot, tick, t + 1, tick + IntentCommand.LifetimeTicks, move, LookIntent.AlongPath, default, default));
            applier.ApplyTick(tick, Dt);
            bool pressed = runner.Queued[^1].IsPressed(InputButtons.Jump), fresh = pressed && !was;
            if (fresh)
                presses.Add(t);
            was = pressed;
            if (fresh && !jumped)
                (jumped, vz) = (true, JumpSpeed);
            else if (fresh && jumped && z > 0f && dashed < dashes)
                (dashed, vz) = (dashed + 1, DashSpeed);
            if (jumped)
            {
                z += vz * Dt;
                if (vz > 0f && vz - Gravity * Dt <= 0f)
                    tops.Add(t + 1);
                vz -= Gravity * Dt;
                highest = MathF.Max(highest, z);
                if (z <= 0f)
                {
                    (z, vz, jumped, dashed) = (0f, 0f, false, 0);
                    if (landed < 0)
                        landed = t;
                }
            }
        }

        return (presses, tops, highest, follower.DashPresses, landed);
    }

    /// <summary>
    /// Pressed at the take-off, at the first top and again at the dash's own top: three fresh presses, each in the air
    /// after the body stopped rising, so the body rises the ground jump's 52.2 and two dashes' 45 each, over 130. (The
    /// follower goes on pressing at each top while the edge is its way, as it did for a double jump: a press with no dash
    /// left does nothing.)
    /// </summary>
    [Fact]
    public void ATripleJumpPressesAtTheTakeOffAndAtEachTop()
    {
        var (presses, tops, highest, counted, landed) = Fly(NavEdgeType.TripleJump, dashes: 2);
        Assert.True(presses.Count(p => landed < 0 || p <= landed) >= 3, $"presses [{string.Join(", ", presses)}], landed {landed}");
        Assert.True(presses.Count >= 3, $"pressed afresh {presses.Count} times: [{string.Join(", ", presses)}]");
        Assert.True(tops.Count >= 3, $"tops at [{string.Join(", ", tops)}]");
        Assert.Equal(0, presses[0]);
        Assert.True(presses[1] >= tops[0] && presses[1] < tops[1], $"the second press at {presses[1]}, tops [{string.Join(", ", tops)}]");
        Assert.True(presses[2] >= tops[1] && presses[2] < tops[2], $"the third press at {presses[2]}, tops [{string.Join(", ", tops)}]");
        Assert.True(highest > 130f, $"rose {highest}");
        Assert.True(counted >= 2, $"counted {counted}");
    }

    /// <summary>A run of three floor cells 64 u wide, then a ledge 130 u up joined by an edge of <paramref name="type"/>.</summary>
    private static (NavGraph G, CellId Start, CellId Ledge) Run(NavEdgeType type)
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId[] floor = [.. Enumerable.Range(0, 3).Select(i => b.AddCell(new NavCell(i * 64f, 0f, (i + 1) * 64f, 64f, 0f, Both, NavCellFlags.None, 1f)))];
        CellId ledge = b.AddCell(new NavCell(192f, 0f, 256f, 64f, 130f, Both, NavCellFlags.None, 1f));
        for (int i = 0; i < 2; i++)
        {
            b.AddEdge(floor[i], floor[i + 1], NavEdgeType.Walk);
            b.AddEdge(floor[i + 1], floor[i], NavEdgeType.Walk);
        }

        b.AddEdge(floor[2], ledge, type);
        return (b.Build(), floor[0], ledge);
    }

    private static int DashesAhead(NavEdgeType type, float reach, JumpReach agent = JumpReach.Double | JumpReach.Triple)
    {
        var (g, start, ledge) = Run(type);
        var follower = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch, new NavObstacleState(0) { Reach = agent });
        follower.Next(g.Cell(start).Centre, out _);
        return follower.DashesAhead(reach);
    }

    /// <summary>
    /// The route's next 384 u hold the triple jump two cells on: two presses in the air ahead, what the hands get the
    /// Atomizer out for; a double jump there is one; looked for only 100 u on, the jump is not yet ahead.
    /// </summary>
    [Fact]
    public void TheRouteAheadSaysHowManyPressesInTheAirItsNextJumpNeeds()
        => Assert.Equal((2, 1, 0), (DashesAhead(NavEdgeType.TripleJump, 384f), DashesAhead(NavEdgeType.DoubleJump, 384f), DashesAhead(NavEdgeType.TripleJump, 100f)));

    /// <summary>
    /// A follower whose agent loses the third jump (the Atomizer taken away by a new kit) builds its field again on its next
    /// move: the triple jump it was about to take is no longer its way.
    /// </summary>
    [Fact]
    public void AFollowerBuildsAgainWhenItsAgentsReachChanges()
    {
        var (g, _, ledge) = Run(NavEdgeType.TripleJump);
        CellId edge = new(2);
        var state = new NavObstacleState(0) { Reach = JumpReach.Double | JumpReach.Triple };
        var follower = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch, state);
        follower.Next(g.Cell(edge).Centre, out _);
        Assert.Equal(NavEdgeType.TripleJump, follower.NextVia);
        state.Reach = JumpReach.Double;
        follower.Next(g.Cell(edge).Centre, out _);
        Assert.NotEqual(NavEdgeType.TripleJump, follower.NextVia);
    }

    /// <summary>An agent without the third jump is not asked: a stock Scout's double jump ahead says 0 (nothing reads it), and HL2MP's agents never look.</summary>
    [Fact]
    public void AnAgentWithoutTheThirdJumpLooksForNoAirJump()
        => Assert.Equal((0, 1), (DashesAhead(NavEdgeType.DoubleJump, 384f, JumpReach.Double), DashesAhead(NavEdgeType.DoubleJump, 384f)));

    /// <summary>
    /// A triple jump the body cannot fly (a ceiling the graph did not see, a body that clips the face): it takes off, rises
    /// 60 u and comes back down on the take-off's floor. The follower leaves that triple jump out from then on, and the
    /// followers it goes on to (a new goal) do too; the control: before the failed try the triple jump is its way.
    /// </summary>
    [Fact]
    public void ATripleJumpTheBodyFailedToFlyIsLeftOut()
    {
        var (g, _, ledge) = Run(NavEdgeType.TripleJump);
        CellId edge = new(2);
        var state = new NavObstacleState(0) { Reach = JumpReach.Double | JumpReach.Triple };
        var follower = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch, state);
        AiVector at = g.Cell(edge).Centre;
        follower.Next(at, out _);
        Assert.Equal(NavEdgeType.TripleJump, follower.NextVia);
        // The flight: up 60 and back, a tick at a time, then a quarter second on the floor.
        float z = 0f, vz = 289f;
        for (int t = 0; t < 60; t++)
        {
            follower.Next(at with { Z = at.Z + z }, out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
        }

        Assert.True(follower.FailedJumps > 0, "the failed triple jump is remembered");
        Assert.NotEqual(NavEdgeType.TripleJump, follower.NextVia);
        var next = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch, state);
        next.GoOnFrom(follower);
        next.Next(at, out _);
        Assert.NotEqual(NavEdgeType.TripleJump, next.NextVia);
    }
}
