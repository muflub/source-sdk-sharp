using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server.Interop;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A bot whose player is the blast-jump simulator (plan_bots_teams_tf2_part_g G.5 step 2): the
/// real applier builds its commands, each is run through the simulator as the game runs a
/// player's, and what the applier reads of the bot is where the simulator left it. The rocket
/// jump is TF2's program over a book the fact fills.
/// </summary>
internal sealed class JumpLive : IPlayerCommandRunner, IBotSelf, IAbilityPrograms
{
    internal const int Row = 0, RowCommands = TFAbilities.RocketJumpCommands;
    internal static readonly ActorId Bot = new(2, 1);
    internal static readonly EntityHandle Player = new(0x0001_0002u);

    private readonly BlastJumpSim _sim;
    private JumpPoint _point;
    private Vector _view;
    private int _tick = 1, _seq;

    internal JumpLive(VoxelColumns columns, JumpStart start, MotionParams motion, int rowCommands = RowCommands, int approachCommands = RocketJumpProgram.ApproachCommands,
                      JumpDesk? desk = null, ulong seed = 42)
    {
        _sim = Sim(columns);
        _sim.Begin(start);
        _point = new JumpPoint(start.Origin, start.Velocity, start.OnGround, start.Ducked);
        _view = start.View;
        Book = desk?.Book ?? new JumpBook(new JumpBookSettings());
        Program = new RocketJumpProgram(Book, Classes(), flown =>
        {
            Flown.Add(flown);
            desk?.Flew(flown);
        }, approachCommands);
        // TF2's own table (the jump is its first row) unless the fact wants a row with other commands.
        AbilityTable table = rowCommands == RowCommands ? TFAbilities.Table() : new AbilityTable([new AbilityRow(TFAbilities.RocketJump, AbilityRun.Program, HoldTicks: rowCommands)]);
        Assert.True(table.TryGet(Row, out AbilityRow row) && row.Name == TFAbilities.RocketJump);
        Applier = new BotCommandApplier(this, this, null, table, null, this);
        Applier.Add(Bot, Player, motion, seed);
    }

    /// <summary>A brain's intent, as the binding submits it.</summary>
    internal void Submit(in IntentCommand intent) => Assert.Equal(IntentRefusal.None, Applier.Submit(in intent));

    /// <summary>One tick of the applier with whatever intent it holds.</summary>
    internal UserCmd Tick(int tick)
    {
        Applier.ApplyTick(new Tick(tick), TickSeconds);
        return Sent[^1];
    }

    internal JumpBook Book { get; }

    internal RocketJumpProgram Program { get; }

    internal BotCommandApplier Applier { get; }

    internal BlastJumpSim Simulator => _sim;

    internal bool Alive { get; set; } = true;

    /// <summary>Every command the applier sent, where the simulator left the body after each, and the jumps that ended.</summary>
    internal List<UserCmd> Sent { get; } = [];

    internal List<JumpPoint> Track { get; } = [];

    internal List<JumpFlown> Flown { get; } = [];

    internal JumpPoint Now => _point;

    public IAbilityProgram? Named(string ability) => ability == TFAbilities.RocketJump ? Program : null;

    public void Queue(EntityHandle player, in UserCmd cmd)
    {
        Sent.Add(cmd);
        _view = new Vector(cmd.Pitch, cmd.Yaw, 0f);
        _point = _sim.Step(new JumpCommand(_view, cmd.ForwardMove, cmd.SideMove, 0f, cmd.Buttons));
        Track.Add(_point);
    }

    public int RunQueued(EntityHandle player) => 1;

    public BotSelfFacts Read(EntityHandle player)
        => new(Alive, false, A(_point.Origin), A(_point.Origin) + new AiVector(0f, 0f, 68f), new AiAngles(_view.X, _view.Y, 0f), Velocity: A(_point.Velocity), OnGround: _point.OnGround);

    public int WeaponSelectFor(EntityHandle player, int slot) => 0;

    internal static AiVector A(Vector v) => new(v.X, v.Y, v.Z);

    /// <summary>Puts <paramref name="plan"/> in the book as a worker would, and answers it as kept (numbered).</summary>
    internal JumpPlan Keep(JumpPlan plan)
    {
        Assert.Null(Book.Advise(new JumpStretchAsk(A(plan.Recipe.Takeoff), plan.Way, plan.Goal, new Tick(_tick))));
        Assert.True(Book.TryTake(out JumpStretchAsk ask, out int generation));
        return Book.Answer(ask, generation, new JumpPlanned(plan, JumpRefusal.None, 0))!;
    }

    /// <summary>
    /// One command: the bot's intent is to follow its way to <paramref name="goal"/>, looking along it, with
    /// <paramref name="action"/> in its hands (a one-shot when its sequence number is not 0).
    /// </summary>
    internal UserCmd Command(AiVector goal, ActionIntent action = default)
    {
        var from = new Tick(_tick);
        var move = new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, goal, default, default, default, default, false, 0);
        var intent = new IntentCommand(Bot, from, ++_seq, from + IntentCommand.LifetimeTicks, move, LookIntent.AlongPath, action, default);
        Assert.Equal(IntentRefusal.None, Applier.Submit(in intent));
        Applier.ApplyTick(new Tick(_tick++), TickSeconds);
        return Sent[^1];
    }

    /// <summary>Walks toward <paramref name="goal"/> until the body is past <paramref name="x"/>.</summary>
    internal void WalkPast(float x, AiVector goal)
    {
        for (int i = 0; i < 2000 && _point.Origin.X < x; i++)
            Command(goal);
        Assert.True(_point.Origin.X >= x, $"the walk stopped at {_point.Origin}");
    }

    /// <summary>Asks for the jump numbered <paramref name="plan"/>, then goes on toward <paramref name="goal"/> until no program runs (or <paramref name="most"/> commands).</summary>
    internal int Jump(int plan, AiVector goal, int most = 1500, ActionIntent held = default)
    {
        Command(goal, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(Row, plan), OneShotSeq: _seq + 1));
        int first = Sent.Count;
        for (int i = 0; i < most && Applier.ProgramRunning(Bot); i++)
            Command(goal, held);
        return first;
    }
}
