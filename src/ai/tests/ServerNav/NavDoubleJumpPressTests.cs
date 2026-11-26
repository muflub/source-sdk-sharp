using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A Scout's double jump on a route, as the game is handed it (plan_bots_teams_tf2 C.7, P7 Scout): the follower's moves
/// through the applier's buttons. TF's Scout dashes on a fresh press of the jump in the air (stock
/// CTFGameMovement::CheckJumpButton, m_nOldButtons), so the only fresh press after the take-off must come at the top.
/// </summary>
public class NavDoubleJumpPressTests
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

    /// <summary>A floor and a ledge 100 u over the column beside it, joined by a double jump up.</summary>
    private static (NavGraph G, CellId Ledge) Course()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId floor = b.AddCell(new NavCell(0f, 0f, 16f, 16f, 0f, Both, NavCellFlags.None, 1f));
        CellId ledge = b.AddCell(new NavCell(0f, 16f, 16f, 32f, 100f, Both, NavCellFlags.None, 1f));
        b.AddEdge(floor, ledge, NavEdgeType.DoubleJump);
        b.AddEdge(ledge, floor, NavEdgeType.Drop);
        return (b.Build(), ledge);
    }

    /// <summary>
    /// The tick of each fresh press of the jump (pressed, and not on the tick before) over a jump's rise of 289 u/s under
    /// 800 u/s² (TF's ground jump), one move and one usercmd a tick, as the brain driver hands them on.
    /// </summary>
    private static List<int> FreshPresses(out int topTick)
    {
        var (g, ledge) = Course();
        var follower = new NavFollower(g, ledge, g.Cell(ledge).Centre, ClearanceClass.Crouch, new NavObstacleState(0) { Reach = JumpReach.Double });
        var runner = new Runner();
        var self = new Self();
        var applier = new BotCommandApplier(runner, self);
        var bot = new ActorId(3, 1);
        applier.Add(bot, new EntityHandle(0x0001_0003u), new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
        const float Dt = 0.015f, JumpSpeed = 289f, Gravity = 800f;
        var presses = new List<int>();
        bool was = false, jumped = false;
        float z = 0f, vz = 0f;
        topTick = -1;
        for (int t = 0; t < 40; t++)
        {
            var at = new AiVector(8f, 8f, z);
            self.Facts = self.Facts with { Origin = at, Eye = at + new AiVector(0f, 0f, 64f) };
            MoveIntent move = follower.Next(at, out _);
            var tick = new Tick(100 + t);
            applier.Submit(new IntentCommand(bot, tick, t + 1, tick + IntentCommand.LifetimeTicks, move, LookIntent.AlongPath, default, default));
            applier.ApplyTick(tick, Dt);
            bool pressed = runner.Queued[^1].IsPressed(InputButtons.Jump);
            if (pressed && !was)
                presses.Add(t);
            was = pressed;
            if (pressed && !jumped)
                (jumped, vz) = (true, JumpSpeed);
            if (jumped)
            {
                z += vz * Dt;
                if (vz > 0f && vz - Gravity * Dt <= 0f && topTick < 0)
                    topTick = t + 1;
                vz -= Gravity * Dt;
            }
        }

        return presses;
    }

    /// <summary>
    /// Pressed at the take-off, and afresh only once the bot has stopped rising: a fresh press while it still rises is the
    /// Scout's one dash spent early, under the height the edge was built for (live, cp_granary: the Scouts' stuck lines,
    /// most "next Double jump", in the foundation's baseline).
    /// </summary>
    [Fact]
    public void TheSecondPressComesAtTheTopNotWhileTheBotRises()
    {
        List<int> presses = FreshPresses(out int top);

        Assert.True(presses.Count >= 2, $"pressed afresh {presses.Count} times: [{string.Join(", ", presses)}]");
        Assert.Equal(0, presses[0]);
        Assert.True(presses[1] >= top, $"the second press at tick {presses[1]}, the top at {top}: [{string.Join(", ", presses)}]");
    }
}
