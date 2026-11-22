using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay fight</c>'s definitions (lane Gfight) on synthetic streams: what a firing tick is,
/// when it is moving, which intent it ran under, the body's changes of side, and the kill feed's
/// kills and deaths counted once.
/// </summary>
public sealed class FightMotionTests
{
    private static readonly ActorId Bot = new(3, 7), Other = new(4, 9);

    private const int Attack = 1, Jump = 2;

    private static AiUserCmd Cmd(int tick, int buttons, float side = 0f, float yaw = 0f)
        => new(Bot, new Tick(tick), tick, tick, new AiAngles(0f, yaw, 0f), 0f, side, 0f, buttons, 0, 0, 0, 0, 0, 0);

    private static AiActorFrame Frame(int tick, AiVector origin) => new(Bot, new Tick(tick), origin, origin, default);

    private static IntentCommand Intent(int tick, MoveKind kind, int node = 0)
        => new(Bot, new Tick(tick), tick, new Tick(tick + 16), MoveIntent.Hold with { Kind = kind }, LookIntent.AlongPath, ActionIntent.None,
               new DecisionReason(node, 0f, 0f, 0f));

    private static SortedList<int, AiActorFrame> Frames(int from, int to, Func<int, AiVector> velocity)
    {
        // The origins a body at these velocities passes, a frame a tick: the measure reads the origins.
        var frames = new SortedList<int, AiActorFrame> { [from - 1] = Frame(from - 1, default) };
        AiVector at = default;
        for (int t = from; t < to; t++)
        {
            at += velocity(t) * AiMetrics.TickSeconds;
            frames[t] = Frame(t, at);
        }

        return frames;
    }

    /// <summary>Only ticks holding attack count, and one just over half a Soldier's run is moving while one just under it is not.</summary>
    [Fact]
    public void AFiringTickOverTheThresholdIsMovingAndOneUnderItIsNot()
    {
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 30; t++)
            cmds.Add(Cmd(t, t < 20 ? Attack : 0));
        SortedList<int, AiActorFrame> frames = Frames(0, 30, t => new AiVector(t < 10 ? AiFightMotion.MovingSpeed + 1f : AiFightMotion.MovingSpeed - 1f, 0f, 0f));

        FightMotionRow row = AiFightMotion.For(Bot, cmds, [], frames, []);

        Assert.Equal((20, 10), (row.FiringTicks, row.MovingTicks));
    }

    /// <summary>A command with no frame at its tick or the four before (a dead bot pressing to respawn), or none before that one to move from, is no firing tick.</summary>
    [Fact]
    public void ACommandWithNoLivingFrameIsNoFiringTick()
    {
        var cmds = new List<AiUserCmd> { Cmd(10, Attack), Cmd(20, Attack), Cmd(30, Attack) };
        var frames = new SortedList<int, AiActorFrame> { [5] = Frame(5, default), [6] = Frame(6, default), [15] = Frame(15, default), [29] = Frame(29, default) };

        Assert.Equal(1, AiFightMotion.For(Bot, cmds, [], frames, []).FiringTicks);
    }

    /// <summary>The body's velocity is read from its origins; a jump of its origin past any run (a teleport, a respawn) is none.</summary>
    [Fact]
    public void TheVelocityIsTheOriginsStepAndATeleportIsNone()
    {
        var frames = new SortedList<int, AiActorFrame> { [0] = Frame(0, default), [2] = Frame(2, new AiVector(6f, 0f, 0f)), [3] = Frame(3, new AiVector(600f, 0f, 0f)) };

        Assert.Equal(200f, AiFightMotion.Motion(frames, 2)!.Value.X, 3);
        Assert.Null(AiFightMotion.Motion(frames, 3));
        Assert.Null(AiFightMotion.Motion(frames, 0));
    }

    /// <summary>A firing tick runs under the last intent at or before it: the brain's ask, told apart by kind and by leaf.</summary>
    [Fact]
    public void AFiringTickTakesTheLastIntentAtOrBeforeIt()
    {
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 20; t++)
            cmds.Add(Cmd(t, Attack));
        IntentCommand[] intents = [Intent(0, MoveKind.Hold, 5), Intent(5, MoveKind.Strafe, 6), Intent(16, MoveKind.Follow, 7)];

        FightMotionRow row = AiFightMotion.For(Bot, cmds, intents, Frames(0, 20, _ => default), []);

        Assert.Equal((5, 11, 4), (row.IntentKinds[(int)MoveKind.Hold], row.IntentKinds[(int)MoveKind.Strafe], row.IntentKinds[(int)MoveKind.Follow]));
        Assert.Equal(11, row.Leaves[6]);
    }

    /// <summary>The body's side across the view flips each time its velocity crosses from right to left; a slow drift has no side.</summary>
    [Fact]
    public void ASideFlipIsTheBodyCrossingFromOneSideOfTheViewToTheOther()
    {
        // The view looks along +x: right of it is -y.
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 40; t++)
            cmds.Add(Cmd(t, Attack));
        SortedList<int, AiActorFrame> frames = Frames(0, 40, t => t switch
        {
            < 10 => new AiVector(0f, -200f, 0f),
            < 20 => new AiVector(0f, 30f, 0f),
            < 30 => new AiVector(0f, 200f, 0f),
            _ => new AiVector(0f, -200f, 0f),
        });

        FightMotionRow row = AiFightMotion.For(Bot, cmds, [], frames, []);

        Assert.Equal(2, row.SideFlips);
        Assert.Equal(2 / (40 * AiMetrics.TickSeconds), row.FlipsPerSecond, 4);
    }

    /// <summary>A pause in the fire longer than a second starts again: a side before it is no flip after it.</summary>
    [Fact]
    public void APauseInTheFireForgetsTheSide()
    {
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 10; t++)
            cmds.Add(Cmd(t, Attack, side: 450f));
        for (int t = 10 + AiFightMotion.RunGapTicks + 1; t < 20 + AiFightMotion.RunGapTicks + 1; t++)
            cmds.Add(Cmd(t, Attack, side: -450f));
        SortedList<int, AiActorFrame> frames = Frames(0, 100, t => new AiVector(0f, t < 10 ? -200f : 200f, 0f));

        FightMotionRow row = AiFightMotion.For(Bot, cmds, [], frames, []);

        Assert.Equal((0, 0), (row.SideFlips, row.CommandFlips));
    }

    /// <summary>A jump counts on its press, and the commands' side flips follow the side move sent.</summary>
    [Fact]
    public void AJumpCountsOnItsPressAndTheCommandsFlipsFollowTheSideMove()
    {
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 30; t++)
            cmds.Add(Cmd(t, Attack | (t is >= 5 and < 9 or >= 20 and < 22 ? Jump : 0), side: t < 15 ? 450f : -450f));

        FightMotionRow row = AiFightMotion.For(Bot, cmds, [], Frames(0, 30, _ => default), []);

        Assert.Equal((2, 1), (row.JumpsFiring, row.CommandFlips));
    }

    /// <summary>Of the ticks whose intent asked to fire, the share whose command held attack: what the hands kept back.</summary>
    [Fact]
    public void FiredOfAskedIsTheShareOfFireAskedThatTheCommandsHeld()
    {
        var cmds = new List<AiUserCmd>();
        for (int t = 0; t < 20; t++)
            cmds.Add(Cmd(t, t % 4 == 0 ? 0 : Attack));
        IntentCommand asks = Intent(0, MoveKind.Hold) with { Action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0) };

        FightMotionRow row = AiFightMotion.For(Bot, cmds, [asks], Frames(0, 20, _ => default), []);

        Assert.Equal((20, 15, 0.75), (row.AskedTicks, row.FiringTicks, row.FiredShare));
    }

    /// <summary>Each HUD shows a death: it counts once, a kill to its killer and a death to its victim; a hit taken is the bot's own event.</summary>
    [Fact]
    public void TheKillFeedCountsEachDeathOnceAndHitsAreTheBotsOwn()
    {
        AiEvent[] events =
        [
            AiEvent.KillFeed(Bot, new Tick(5), Other, Bot), AiEvent.KillFeed(Other, new Tick(5), Other, Bot),
            AiEvent.KillFeed(Other, new Tick(9), Bot, Other), AiEvent.KillFeed(Bot, new Tick(9), Bot, Other),
            AiEvent.KillFeed(Bot, new Tick(12), Other, Bot),
            AiEvent.Damaged(Bot, new Tick(8), Other, 40f, default, 0), AiEvent.Damaged(Other, new Tick(4), Bot, 90f, default, 0),
        ];

        FightMotionRow row = AiFightMotion.For(Bot, [], [], new SortedList<int, AiActorFrame>(), events);

        Assert.Equal((2, 1, 2.0, 1, 40.0), (row.Kills, row.Deaths, row.KillsPerDeath, row.HitsTaken, row.DamageTaken));
    }

    /// <summary>A recording's bots come first and its people after, each by the same definitions; the leaves are named by the recording's tree.</summary>
    [Fact]
    public void ARecordingGivesItsBotsThenItsPeopleAndNamesTheLeaves()
    {
        ActorId person = new(1, 0x4000_0001);
        var bytes = new MemoryStream();
        var r = new AiRecorder(bytes, Recordings.Header);
        r.DecisionTree(["root", "fight at range"], [-1, 0]);
        r.BotAdded(Bot, 1, [1, 2]);
        r.Frame(Frame(-1, default));
        r.Frame(new AiActorFrame(person, new Tick(-1), default, default, default));
        for (int t = 0; t < 10; t++)
        {
            r.Intent(Intent(t, MoveKind.Strafe, 1));
            r.UserCmd(Cmd(t, Attack, side: 450f));
            r.Frame(Frame(t, new AiVector(0f, -3f * (t + 1), 0f)));
            r.UserCmd(Cmd(t, Attack) with { Actor = person });
            r.Frame(new AiActorFrame(person, new Tick(t), default, default, default));
        }

        (List<FightMotionRow> rows, AiDecisionTree? tree) = AiFightMotion.Read(new MemoryStream(bytes.ToArray()));
        string table = AiFightMotion.Table(rows, tree);

        Assert.Equal([(Bot, true, 10, 10), (person, false, 10, 0)], rows.Select(x => (x.Bot, x.IsBot, x.FiringTicks, x.MovingTicks)));
        Assert.Contains("fight: leaf fight at range 100.0 %", table, StringComparison.Ordinal);
        Assert.Contains("fight: people firing 10", table, StringComparison.Ordinal);
    }
}
