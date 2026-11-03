using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>Why an intent was not taken (§2.4); counted, and reported back as CommandRejected later.</summary>
public enum IntentRefusal : byte
{
    None = 0,

    /// <summary>No bot in the intent's slot.</summary>
    UnknownBot,

    /// <summary>The slot's bot is a later life than the intent's (the serial differs).</summary>
    StaleSerial,

    /// <summary>The bot already took an intent with this sequence number or a later one.</summary>
    OldSeq,

    /// <summary>The intent's lifetime ran out before it was applied: the bot holds.</summary>
    Expired,

    /// <summary>The bot is dead: it presses to respawn and does nothing else.</summary>
    Dead,

    /// <summary>The bot is watching, not playing.</summary>
    Observer,
}

/// <summary>
/// Turns each bot's intent into one usercmd per tick and runs it as a client's
/// (plan_hl2mp_ai §2.4, §3.2, §3.3; Phase 3 step 4). The view moves through the
/// bot's <see cref="HumanMotion"/>; the body moves by forward and side moves in
/// the view's frame and by buttons; a weapon is selected by inventory slot; the
/// command number rises by one per command, which is also what seeds the
/// command's spread on the server. The command goes through
/// <see cref="IPlayerCommandRunner"/>, the human path: this class writes
/// <see cref="UserCmd"/> fields and nothing else (the IL allow-list fact).
///
/// <para>
/// Two channels: the latest intent per bot (move, look, held action), and the
/// one-shots (<see cref="ActionIntent.OneShotSeq"/> non-zero: a weapon select,
/// a tap of fire), each applied exactly once in sequence order. Nothing is
/// trusted: an intent for another life of the slot, or not newer than the last
/// taken, is refused at submission; an expired one, or one for a dead or
/// watching bot, is refused at the tick and the bot holds.
/// </para>
/// </summary>
public sealed class BotCommandApplier(IPlayerCommandRunner runner, IBotSelf self)
{
    /// <summary>A full key's move, as <c>cl_forwardspeed</c> and <c>cl_sidespeed</c> send it; the server's max speed caps it.</summary>
    public const float FullMove = 450f;

    /// <summary>A path corner counts as reached inside this many units (2D).</summary>
    public const float CornerReached = 16f;

    /// <summary>A dead bot presses fire every this many ticks, as a player presses to respawn.</summary>
    public const int RespawnPressTicks = 30;

    private readonly List<BotState> _bots = new();
    private readonly long[] _refused = new long[Enum.GetValues<IntentRefusal>().Length];

    /// <summary>Commands run.</summary>
    public long Applied { get; private set; }

    /// <summary>How many intents were refused for <paramref name="why"/>.</summary>
    public long Refused(IntentRefusal why) => _refused[(int)why];

    public int Count => _bots.Count;

    /// <summary>The bot in <paramref name="slot"/>'s player, or None.</summary>
    /// <summary>The corner the bot's last command steered to and the move it sent, for a stuck line.</summary>
    public (AiVector? Corner, float Forward, float Side) LastOf(ActorId bot)
        => Find(bot.Slot) is { } b && b.Id == bot ? (b.LastCorner, b.LastMove.Forward, b.LastMove.Side) : (null, 0f, 0f);

    public EntityHandle PlayerOf(ActorId bot) => Find(bot.Slot) is { } b && b.Id == bot ? b.Player : EntityHandle.None;

    /// <summary>A bot joins: its actor, its player, its view's motion and its seed.</summary>
    public void Add(ActorId id, EntityHandle player, MotionParams motion, ulong seed)
    {
        if (!id.IsValid)
            throw new ArgumentException("a bot needs a valid actor", nameof(id));
        Remove(id.Slot);
        var bot = new BotState(id, player, new HumanMotion(motion, new AiRandom(seed)));
        int at = _bots.FindIndex(b => b.Id.Slot > id.Slot);
        _bots.Insert(at < 0 ? _bots.Count : at, bot);
    }

    /// <summary>The bot in <paramref name="slot"/> leaves.</summary>
    public void Remove(int slot) => _bots.RemoveAll(b => b.Id.Slot == slot);

    /// <summary>A level change put the bot in <paramref name="slot"/> in a new player.</summary>
    public void Rebind(int slot, EntityHandle player)
    {
        if (Find(slot) is { } b && b.Player != player)
        {
            b.Player = player;
            b.Spawned = false;
        }
    }

    /// <summary>Takes <paramref name="intent"/> into its bot's slot, or refuses it.</summary>
    public IntentRefusal Submit(in IntentCommand intent)
    {
        IntentRefusal why = Check(in intent, out BotState? bot);
        if (why != IntentRefusal.None)
        {
            _refused[(int)why]++;
            return why;
        }

        bot!.Intent = intent;
        bot.LastSeq = intent.Seq;
        if (intent.Action.OneShotSeq > bot.LastOneShot)
        {
            bot.OneShots.Enqueue(intent.Action);
            bot.LastOneShot = intent.Action.OneShotSeq;
        }

        return IntentRefusal.None;
    }

    private IntentRefusal Check(in IntentCommand intent, out BotState? bot)
    {
        bot = Find(intent.Bot.Slot);
        if (bot is null)
            return IntentRefusal.UnknownBot;
        if (bot.Id.Serial != intent.Bot.Serial)
            return IntentRefusal.StaleSerial;
        if (intent.Seq <= bot.LastSeq)
            return IntentRefusal.OldSeq;
        return IntentRefusal.None;
    }

    /// <summary>
    /// One tick: every bot, in slot order, builds its command from its intent
    /// and runs it through the human path.
    /// </summary>
    public void ApplyTick(Tick tick, float dt)
    {
        long run = 0;
        for (int i = 0; i < _bots.Count; i++)
        {
            BotState bot = _bots[i];
            BotSelfFacts facts = self.Read(bot.Player);
            UserCmd cmd = Build(bot, tick, in facts, dt);
            runner.Queue(bot.Player, in cmd);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            runner.RunQueued(bot.Player);
            run += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            Applied++;
        }

        RunMicrosLastTick = run * 1e6 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>
    /// The last tick's commands run as players (the movement and the weapons,
    /// what a human client's command costs the server too), in microseconds:
    /// <c>ai.run.us</c>, kept out of the AI's own <c>ai.apply.us</c>.
    /// </summary>
    public double RunMicrosLastTick { get; private set; }

    /// <summary>The command <paramref name="bot"/> sends this tick. Advances its view, its command number and its channels.</summary>
    internal UserCmd Build(BotState bot, Tick tick, in BotSelfFacts facts, float dt)
    {
        var cmd = new UserCmd { CommandNumber = ++bot.CommandNumber, TickCount = tick.Value };

        if (!facts.Alive || facts.Observer)
        {
            // Not playing: refuse what is held, keep the view, and press fire
            // now and then, which is how a player asks to respawn.
            if (bot.Intent is not null)
            {
                _refused[(int)(facts.Observer ? IntentRefusal.Observer : IntentRefusal.Dead)]++;
                bot.Intent = null;
            }

            bot.OneShots.Clear();
            bot.Spawned = false;
            SetView(ref cmd, facts.View);
            if (tick.Value % RespawnPressTicks == 0)
                cmd.Buttons = (int)InputButtons.Attack;
            return cmd;
        }

        if (!bot.Spawned || Diverged(bot.Motion.View, facts.View))
        {
            // A spawn, or the server turned the view (a teleport, a snap):
            // the view is what the server says it is.
            bot.Motion.Reset(facts.View);
            bot.Spawned = true;
        }

        if (bot.Intent is { } held && held.ExpiredAt(tick))
        {
            _refused[(int)IntentRefusal.Expired]++;
            bot.Intent = null;
        }

        IntentCommand intent = bot.Intent ?? IntentCommand.Hold(bot.Id, tick, 0);

        AiVector? corner = NextCorner(intent.Move, in facts);
        (AiAngles look, int stimulus) = LookTarget(in intent, in facts, corner);
        AiAngles view = bot.Motion.Step(look, stimulus, dt);
        SetView(ref cmd, view);

        int buttons = MoveInto(ref cmd, intent.Move, in facts, view, corner);
        bot.LastCorner = corner;
        bot.LastMove = (cmd.ForwardMove, cmd.SideMove);
        buttons |= HeldAction(bot, intent.Action, tick);

        if (bot.OneShots.TryDequeue(out ActionIntent once))
        {
            if (once.Kind == ActionKind.SelectWeapon)
                cmd.WeaponSelect = self.WeaponSelectFor(bot.Player, once.WeaponSlot);
            else
                buttons |= Press(once.Kind);
        }

        cmd.Buttons = buttons;
        return cmd;
    }

    private static void SetView(ref UserCmd cmd, AiAngles view)
    {
        cmd.Pitch = view.Pitch;
        cmd.Yaw = view.Yaw;
        cmd.Roll = 0f;
    }

    private static bool Diverged(AiAngles mine, AiAngles server)
        => MathF.Abs(HumanMotion.AngleDelta(mine.Yaw, server.Yaw)) > 1f || MathF.Abs(mine.Pitch - server.Pitch) > 1f;

    /// <summary>The first corner of a Follow or Retreat not yet reached, or null.</summary>
    private static AiVector? NextCorner(in MoveIntent move, in BotSelfFacts facts)
    {
        if (move.Kind is not (MoveKind.Follow or MoveKind.Retreat or MoveKind.Jump or MoveKind.CrouchJump))
            return null;

        for (int i = 0; i < move.CornerCount; i++)
        {
            AiVector c = move.Corner(i);
            if (new AiVector(c.X - facts.Origin.X, c.Y - facts.Origin.Y, 0f).Length > CornerReached)
                return c;
        }

        return null;
    }

    /// <summary>Where the view should go, and the stimulus it answers (0: nothing to react to).</summary>
    private static (AiAngles, int) LookTarget(in IntentCommand intent, in BotSelfFacts facts, AiVector? corner)
    {
        LookIntent look = intent.Look;
        switch (look.Kind)
        {
            case LookKind.Path:
                // The next corner at eye height; with no corner, the view stays.
                return corner is { } c
                    ? (AnglesTo(facts.Eye, new AiVector(c.X, c.Y, facts.Eye.Z)), 0)
                    : (facts.View with { Roll = 0f }, 0);

            default:
                // A point (a glance, a corner check, an actor's predicted
                // point until Phase 4 gives sight): a new point is a new
                // stimulus, the same point is held.
                return (AnglesTo(facts.Eye, look.Point), StimulusOf(in look));
        }
    }

    private static int StimulusOf(in LookIntent look)
    {
        // Quantised to 8 units so a point that only drifts stays one stimulus.
        int x = (int)MathF.Round(look.Point.X / 8f), y = (int)MathF.Round(look.Point.Y / 8f), z = (int)MathF.Round(look.Point.Z / 8f);
        int h = HashCode.Combine((int)look.Kind, look.Actor, x, y, z);
        return h == 0 ? 1 : h;
    }

    /// <summary>The view angles from <paramref name="eye"/> to <paramref name="point"/> (Source: pitch down is positive).</summary>
    public static AiAngles AnglesTo(AiVector eye, AiVector point)
    {
        AiVector d = point - eye;
        float flat = d.Length2D;
        if (flat < 1e-3f && MathF.Abs(d.Z) < 1e-3f)
            return default;
        float yaw = MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);
        float pitch = -MathF.Atan2(d.Z, flat) * (180f / MathF.PI);
        return new AiAngles(pitch, yaw, 0f);
    }

    /// <summary>The forward and side moves for <paramref name="move"/> in the view's frame, and the move's buttons.</summary>
    private static int MoveInto(ref UserCmd cmd, in MoveIntent move, in BotSelfFacts facts, AiAngles view, AiVector? corner)
    {
        AiVector wish = default;
        int buttons = 0;
        switch (move.Kind)
        {
            case MoveKind.Follow:
            case MoveKind.Retreat:
                if (corner is { } c)
                    wish = new AiVector(c.X - facts.Origin.X, c.Y - facts.Origin.Y, 0f).Normalised();
                // The brain's separation from the players it sees bends the way (§9 Phase 5 step 4).
                if (wish.LengthSquared > 0f)
                    wish = Steering.Bend(wish, move.Direction);
                buttons |= move.Speed switch
                {
                    MoveSpeed.Sprint => (int)InputButtons.Speed,
                    MoveSpeed.Walk => (int)InputButtons.Walk,
                    _ => 0,
                };
                // A crouch-only stretch of the path: held ducked while on it.
                if (move.CrouchOn)
                    buttons |= (int)InputButtons.Duck;
                break;
            case MoveKind.Strafe:
                wish = new AiVector(move.Direction.X, move.Direction.Y, 0f).Normalised();
                break;
            case MoveKind.Jump:
            case MoveKind.CrouchJump:
                // Jumping on the way: toward the corner it lands by, if any.
                if (corner is { } j)
                    wish = new AiVector(j.X - facts.Origin.X, j.Y - facts.Origin.Y, 0f).Normalised();
                buttons |= move.Kind == MoveKind.Jump ? (int)InputButtons.Jump : (int)(InputButtons.Jump | InputButtons.Duck);
                break;
            case MoveKind.Crouch:
                if (move.CrouchOn)
                    buttons |= (int)InputButtons.Duck;
                break;
        }

        // The view's frame: forward along the yaw, right a quarter turn
        // clockwise from it (AngleVectors with no pitch or roll).
        float yaw = view.Yaw * (MathF.PI / 180f);
        float fx = MathF.Cos(yaw), fy = MathF.Sin(yaw);
        float forward = (wish.X * fx + wish.Y * fy) * FullMove;
        float side = (wish.X * fy - wish.Y * fx) * FullMove;
        cmd.ForwardMove = forward;
        cmd.SideMove = side;

        // The keys a client holds for those moves.
        const float Held = FullMove * 0.25f;
        if (forward > Held)
            buttons |= (int)InputButtons.Forward;
        else if (forward < -Held)
            buttons |= (int)InputButtons.Back;
        if (side > Held)
            buttons |= (int)InputButtons.MoveRight;
        else if (side < -Held)
            buttons |= (int)InputButtons.MoveLeft;
        return buttons;
    }

    /// <summary>The held action's buttons this tick: a spray or burst holds, a tap presses every other tick.</summary>
    private static int HeldAction(BotState bot, in ActionIntent action, Tick tick)
    {
        if (action.OneShotSeq != 0)
            return 0;

        switch (action.Kind)
        {
            case ActionKind.Fire when action.Style == FireStyle.Tap:
                // Released between presses, so a semi-automatic weapon fires again.
                bot.TapDown = !bot.TapDown;
                return bot.TapDown ? (int)InputButtons.Attack : 0;
            case ActionKind.Fire:
            case ActionKind.AltFire:
            case ActionKind.Reload:
            case ActionKind.Use:
                bot.TapDown = false;
                return Press(action.Kind);
            default:
                bot.TapDown = false;
                return 0;
        }
    }

    private static int Press(ActionKind kind) => kind switch
    {
        ActionKind.Fire => (int)InputButtons.Attack,
        ActionKind.AltFire => (int)InputButtons.Attack2,
        ActionKind.Reload => (int)InputButtons.Reload,
        ActionKind.Use => (int)InputButtons.Use,
        _ => 0,
    };

    private BotState? Find(int slot)
    {
        for (int i = 0; i < _bots.Count; i++)
        {
            if (_bots[i].Id.Slot == slot)
                return _bots[i];
        }

        return null;
    }

    /// <summary>The bot in <paramref name="slot"/>'s state, for the facts.</summary>
    internal BotState? StateOf(int slot) => Find(slot);

    /// <summary>One bot's channels and view.</summary>
    internal sealed class BotState(ActorId id, EntityHandle player, HumanMotion motion)
    {
        public ActorId Id { get; } = id;
        public EntityHandle Player { get; set; } = player;
        public HumanMotion Motion { get; } = motion;
        public int CommandNumber { get; set; }

        public AiVector? LastCorner { get; set; }

        public (float Forward, float Side) LastMove { get; set; }
        public IntentCommand? Intent { get; set; }
        public int LastSeq { get; set; }
        public int LastOneShot { get; set; }
        public Queue<ActionIntent> OneShots { get; } = new();
        public bool Spawned { get; set; }
        public bool TapDown { get; set; }
    }
}
