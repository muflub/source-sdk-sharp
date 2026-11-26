using SourceSharp.Abi;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>How a jump a bot began ended.</summary>
public enum JumpEnd : byte
{
    /// <summary>It flew on its own blast and came down.</summary>
    Landed = 0,

    /// <summary>Called off before the launch: the view did not come onto the shot's line in the room and the time the plan allows, running on or standing for it.</summary>
    ViewLate,

    /// <summary>Called off before the launch: the bot was off the plan's line, or off the ground.</summary>
    Astray,

    /// <summary>Launched, and no blast carried it: it never left the ground, was back down in under half the plan's time in the air, or rose under half as high as the plan.</summary>
    Fizzled,

    /// <summary>The bot died first.</summary>
    Died,

    /// <summary>The ability's commands ran out first.</summary>
    TimedOut,

    /// <summary>
    /// The game moved the body further in one command than any jump carries it (<see cref="RocketJumpProgram.MovedUnits"/>):
    /// a round's restart put it on its spawn, or a teleport. What it came down on says nothing of the jump.
    /// </summary>
    Moved,

    /// <summary>
    /// The game put the body, fallen under where the jump started, back at the start (lane G7a): a jump map's reset, a
    /// <c>trigger_teleport</c> under the jump. The jump fell short: a failed try of it.
    /// </summary>
    Reset,
}

/// <summary>
/// One jump as a bot made it, for the tally and the differential against the simulator
/// (plan_bots_teams_tf2_part_g G.5 steps 3 and 7). <paramref name="Recipe"/> is the plan's with the
/// takeoff where the bot did launch, <paramref name="Launch"/> the body's own state at the launch's
/// first command, <paramref name="Slid"/> how far past the plan's takeoff that was.
/// <paramref name="ApproachCommands"/> are the commands before the launch,
/// <paramref name="AirCommands"/> those the body was off the ground for, and
/// <paramref name="Touch"/> where it came down (where it was when the jump ended, for one that did
/// not land). <paramref name="Planned"/> is the plan it flew. <see cref="Stood"/> are the commands
/// of the approach the bot was told to stand for, waiting for its view.
/// </summary>
public sealed record JumpFlown(JumpPlan Planned, JumpRecipe Recipe, JumpStart Launch, bool Launched, float Slid, int ApproachCommands, int AirCommands,
                               bool Felt, Vector Touch, JumpEnd End, float TickSeconds)
{
    /// <summary>The highest the body rose over where it launched.</summary>
    public float Rise { get; init; }

    public int Stood { get; init; }

    /// <summary>
    /// The command, counted from the trigger's first, on whose body the pilot felt a blast
    /// (<see cref="JumpPilotState.Felt"/>), or -1 for none: a rocket that went off near the bot
    /// is felt a few commands after the trigger, one that went off where the plan aimed it later.
    /// </summary>
    public int FeltAfterShot { get; init; } = -1;

    /// <summary>The leap in the body's velocity the pilot felt as the blast: up and along the way for a blast under it, level or down for one beside or over it.</summary>
    public Vector Leap { get; init; }

    /// <summary>
    /// For a recipe with a second rocket (lane G7b): the command, counted from the first trigger's first, on which its
    /// trigger was pressed, or -1 when it never was (the view was not there in its time, or the jump ended first).
    /// </summary>
    public int SecondAfterShot { get; init; } = -1;

    /// <summary>The commands of the flight that carried something lent to the class's play (round 2's S1): a key, a look or a weapon.</summary>
    public int Lent { get; init; }

    /// <summary>The commands in the air at which a parachute was lent open, and at which the program closed it; -1 for none.</summary>
    public int ChuteOpenedAir { get; init; } = -1;

    public int ChuteClosedAir { get; init; } = -1;
}

/// <summary>
/// The Soldier's rocket jump as the applier runs it (plan_bots_teams_tf2_part_g G.5 step 2): a
/// program that flies a planned recipe with the pure pilot, a command at a time, from the bot's own
/// state.
///
/// <list type="bullet">
/// <item><b>The approach is the program's.</b> The bot runs on along the plan's line (eased back
/// onto it when it is a little to a side) while its view comes round to the shot's through its own
/// motion: a reaction, then a flick, as fast as this bot turns. The jump is launched where the view
/// has come to rest on the shot's line, within a tolerance by how well the bot turns
/// (<see cref="Tolerance"/>), on or past the plan's takeoff: the recipe's takeoff is moved to where
/// the bot is then, no further past the plan's than its slide.</item>
/// <item><b>A view that will be late is waited for, standing.</b> A hand's turn takes no longer
/// than <see cref="TurnCommandsAtMost"/> says. A bot that comes within twice the plan's hold run
/// (<see cref="JumpPlan.HoldRun"/>) of the slide's end, whose view may not be round before a run
/// from where it is passes the slide's end, stops: it has one run to stop in and one to run at
/// the jump. It stands until its view is round, or surely will be before the slide's end, then
/// runs on and launches no sooner than the hold run from where it stood, which is the jump the
/// planner flew from a standstill. So how slowly a hand turns costs it time and not the jump,
/// while the jump is worth the wait: a bot whose view is likely to take longer yet
/// (<see cref="TurnCommandsLikely"/>) than the jump from a standstill saves
/// (<see cref="JumpPlan.HoldSeconds"/>) calls it off as it would stop, and does not stand at
/// all.</item>
/// <item><b>Called off</b> before the launch, with nothing pressed: such a wait; no room left to
/// stop and run in; a view not round when no turn takes longer; a bot past the slide (a plan that
/// allows no hold is run on through); an approach of more than <see cref="ApproachCommands"/>
/// commands (unless told another count); a bot off the line or off the ground. It never fires
/// sideways.</item>
/// <item><b>The launch and the flight are the pilot's</b> (<see cref="JumpPilot.Step"/>, its turn
/// limit the bot's own): its buttons and its move, the view turned to where the pilot is turning
/// (<see cref="JumpPilot.LooksTo"/>): the shot's line, which it is on already, until the trigger
/// is let go.</item>
/// <item><b>The end.</b> When the pilot says the body is back on the ground the program is over,
/// and what the jump did goes to the sink (<see cref="JumpFlown"/>).</item>
/// </list>
/// </summary>
public sealed class RocketJumpProgram(JumpBook book, TFClassTable classes, Action<JumpFlown> flew, int approachCommands = RocketJumpProgram.ApproachCommands) : IAbilityProgram
{
    /// <summary>How near the shot's line a view counts as on it: for the slowest hand the envelope has, what a recipe's trials are perturbed by, and for the fastest.</summary>
    public const float LooseDegrees = 3f, TightDegrees = 1f;

    /// <summary>Commands an approach may take before the jump is called off (3 s).</summary>
    public const int ApproachCommands = 200;

    /// <summary>How far to a side of the plan's line a bot may be before the launch, and the offset at which the pull back onto it is half the move.</summary>
    public const float AsideMax = 48f, AsidePull = 32f;

    /// <summary>
    /// A jump flew when the body was in the air for at least this share of the plan's air time and
    /// rose at least this share of the plan's apex; one back down sooner, or that never rose, had
    /// no blast under it. Both are asked: the simulator's flight may be in the air for less than
    /// twice as long as a hop on the legs alone, and it is the rise that tells the two apart then.
    /// (The pilot's own sense of the blast, a leap in speed, misses one that comes with the jump's
    /// own command: live, a flight that landed on its plan's touch was counted as none.)
    /// </summary>
    public const float FlownShare = 0.5f;

    /// <summary>Commands over the sum of a turn's parts that a turn is given: the reaction ends on a whole command, the program sees the view a command after it is sent, and asks it to be at rest for one.</summary>
    public const int TurnSlackCommands = 3;

    /// <summary>The share of a hand's overshoot a flick lands off by on the average: it is drawn evenly from none to all of it, past the target or short.</summary>
    public const float LikelyOvershoot = 0.5f;

    /// <summary>
    /// The commands within which a hand's view has come to rest on a new look
    /// <paramref name="degrees"/> away, at the most, as the motion layer moves it
    /// (<see cref="HumanMotion"/>): its reaction, the flick at its longest (its peak speed under the
    /// hand's cap, landing past the target by all its overshoot), and the correction that takes
    /// that out at a third of the peak.
    /// </summary>
    public static int TurnCommandsAtMost(in MotionParams motion, float degrees, float tickSeconds) => TurnCommands(motion, degrees, tickSeconds, 1f, 1f);

    /// <summary>
    /// The commands a hand's view is likely to take to come to rest on a new look
    /// <paramref name="degrees"/> away: as <see cref="TurnCommandsAtMost"/>, with a flick as long
    /// as the look (one past the target is as likely as one short) and
    /// <see cref="LikelyOvershoot"/> of the overshoot to take out.
    /// </summary>
    public static int TurnCommandsLikely(in MotionParams motion, float degrees, float tickSeconds) => TurnCommands(motion, degrees, tickSeconds, 0f, LikelyOvershoot);

    private static int TurnCommands(in MotionParams motion, float degrees, float tickSeconds, float longer, float off)
    {
        float over = motion.FlickOvershootPct / 100f, peak = motion.TurnPeakDegPerSec;
        float fitts = HumanMotion.FittsA + HumanMotion.FittsB * MathF.Log2(1f + degrees / HumanMotion.TargetWidthDeg);
        float flick = MathF.Max(fitts, 1.875f * degrees * (1f + over * longer) / peak);
        float correction = over * off * degrees / (peak / 3f);
        return (int)MathF.Ceiling((motion.ReactionMs / 1000f + flick + correction) / tickSeconds) + TurnSlackCommands;
    }

    /// <summary>
    /// The looks the program asks of the motion: the shot's, then the way it goes; the second shot's between them for a recipe with one;
    /// a look the class's play was lent in flight (round 2's S1).
    /// </summary>
    public const int ShotLook = 1, WayLook = 2, SecondLook = 3, LentLook = 4;

    /// <summary>Jumps begun, and one-shots that named no plan the book holds; of the jumps begun, those of a link's plan.</summary>
    public long Begun => Interlocked.Read(ref _begun);

    public long Unknown => Interlocked.Read(ref _unknown);

    public long LinksBegun => Interlocked.Read(ref _linksBegun);

    private long _begun, _unknown, _linksBegun;

    /// <summary>
    /// What the program lent in flight (round 2's S1, <see cref="JumpHands"/>): commands that carried anything lent, of them those
    /// with a lent trigger, a lent look and a lent weapon; parachutes a lent press opened and those the program closed.
    /// </summary>
    public long LentCommands => Interlocked.Read(ref _lentCommands);

    public long LentAttacks => Interlocked.Read(ref _lentAttacks);

    public long LentLooks => Interlocked.Read(ref _lentLooks);

    public long LentWeapons => Interlocked.Read(ref _lentWeapons);

    public long ChuteOpens => Interlocked.Read(ref _chuteOpens);

    public long ChuteCloses => Interlocked.Read(ref _chuteCloses);

    private long _lentCommands, _lentAttacks, _lentLooks, _lentWeapons, _chuteOpens, _chuteCloses;

    /// <summary>The book the program's plans and the class plays' asks are read from.</summary>
    internal JumpBook Book => book;

    /// <summary>How near the recipe's start a plan that starts standing is placed before it is flown, and how slowly the body must be moving.</summary>
    public const float PlaceSlack = 8f, StillSpeed = 20f;

    /// <summary>
    /// How near the start a placing walks the body before it lets it stop (lane G7c): half the slack, so a body that slides on
    /// when it stops walking still stops inside it, and one begun just outside it is not placed at its edge (in the game every
    /// two-rocket jump that failed was launched 7 to 8 u past its start).
    /// </summary>
    public const float PlaceNear = PlaceSlack * 0.5f;

    /// <summary>How far from the recipe's start a plan that starts standing may be begun: the follower holds a bot within 48 u of it.</summary>
    public const float PlaceReach = 96f;

    /// <summary>
    /// A body further than this from where it was the command before was moved by the game, not by its jump: the fastest a
    /// body goes is the engine's 3,500 u/s cap, 52.5 u a command at 66 a second.
    /// </summary>
    public const float MovedUnits = 256f;

    /// <summary>
    /// A move of the game's is a reset when the body was put within <see cref="ResetReach"/> of the start, flat, and
    /// <see cref="ResetDrop"/> of its height (a jump map's teleport back puts a player at the jump's start), having fallen
    /// more than <see cref="ResetDrop"/> under the start's floor, or still moving at <see cref="StillSpeed"/> or more where it
    /// was put (lane G7b: a teleport keeps the body's speed, so one that put it back from a ledge over the start, the beam a
    /// second rocket is fired at, is a reset too). A round's restart puts the body on a spawn at rest: moved.
    /// </summary>
    public const float ResetReach = 384f, ResetDrop = 64f;

    /// <summary>The speed a body is walked onto the recipe's start at, per unit it has still to go, and the least: over the floor's friction to the end.</summary>
    public const float PlaceGain = 4f, PlaceSpeedMin = 60f;

    /// <summary>The tolerance for a bot whose view turns at most as <paramref name="motion"/> says.</summary>
    public static float Tolerance(in MotionParams motion)
    {
        float skill = Math.Clamp((motion.TurnPeakDegPerSec - MotionEnvelope.MinPeakDegPerSec) / (MotionEnvelope.MaxPeakDegPerSec - MotionEnvelope.MinPeakDegPerSec), 0f, 1f);
        return LooseDegrees + (TightDegrees - LooseDegrees) * skill;
    }

    public IAbilityRun? Begin(int argument, in AbilityBody body, in MotionParams motion, float tickSeconds)
    {
        if (book.Plan(argument) is not { } plan)
        {
            Interlocked.Increment(ref _unknown);
            return null;
        }

        Interlocked.Increment(ref _begun);
        if (plan.StartsStanding)
            Interlocked.Increment(ref _linksBegun);
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, classes[TFClass.Soldier].MaxSpeed, tickSeconds, motion.TurnPeakDegPerSec);
        return new Run(plan, limits, motion, flew, approachCommands) { Owner = this, Bot = body.Bot, Before = book.HandsAsked(body.Bot) };
    }

    /// <summary>One jump under way.</summary>
    public sealed class Run : IAbilityRun
    {
        private readonly JumpPlan _plan;
        private readonly JumpPilotLimits _limits;
        private readonly float _tolerance;
        private readonly MotionParams _motion;
        private readonly Action<JumpFlown> _flew;
        private readonly int _approachMost;
        private JumpRecipe _recipe;
        private JumpPilotState _pilot;
        private JumpStart _launch;
        private Vector _lastView;
        private bool _launched, _over, _seen;
        private int _approach, _air, _stood;
        private float _slid, _top;
        private float? _stoodAt;
        private int _roundBy, _likelyBy;
        private bool _placed, _aimedStanding;
        private int _feltAfterShot = -1, _secondAfterShot = -1;
        private Vector _leap;
        private Vector? _lastOrigin;
        private AirAsk? _ask;
        private AirFlight? _flight;
        private int _askAge, _pressing, _weaponing = -1, _lent, _chuteOpenedAir = -1, _chuteClosedAir = -1;
        private bool _sentJump, _ownJumpLast, _chuteOpen, _chuteDeployed;

        internal Run(JumpPlan plan, in JumpPilotLimits limits, in MotionParams motion, Action<JumpFlown> flew, int approachMost)
        {
            _approachMost = approachMost;
            _plan = plan;
            _limits = limits;
            _motion = motion;
            // A Double's first shot is held to the tightest view whatever the hand: its second rocket's floor is met only on the flight the search found (lane G7b).
            _tolerance = plan.Recipe.Second is null ? Tolerance(motion) : TightDegrees;
            _flew = flew;
            _recipe = plan.Recipe;
        }

        /// <summary>The program that began the run, whose book holds the class's asks (<see cref="JumpBook.HandsAsked"/>) and whose counters it adds to; null: nothing is lent.</summary>
        internal RocketJumpProgram? Owner { get; init; }

        /// <summary>The bot flying it, as the applier says (<see cref="AbilityBody.Bot"/>): its ask is the one lent to.</summary>
        internal ActorId Bot { get; init; }

        /// <summary>The ask the bot's play had standing when the run began (one a flight before left): never lent, only a fresh one is.</summary>
        internal AirAsk? Before
        {
            get => _ask;
            init => (_ask, _askAge) = (value, JumpHands.AskStaleCommands + 1);
        }

        /// <summary>The recipe as it is being flown: the plan's, its takeoff where the bot launched once it has.</summary>
        public JumpRecipe Recipe => _recipe;

        /// <summary>The pilot's state, and whether the launch has begun.</summary>
        public JumpPilotState Pilot => _pilot;

        public bool Launched => _launched;

        public float ToleranceDegrees => _tolerance;

        public JumpPilotLimits Limits => _limits;

        public AbilityCommand Step(in AbilityBody body)
        {
            var b = new JumpBody(new Vector(body.Origin.X, body.Origin.Y, body.Origin.Z), new Vector(body.Velocity.X, body.Velocity.Y, body.Velocity.Z),
                                 new Vector(body.View.Pitch, body.View.Yaw, 0f), body.OnGround);
            Vector shot = _recipe.ShotView;
            Vector? last = _lastOrigin;
            _lastOrigin = b.Origin;
            if (last is { } was && (b.Origin - was).Length > MovedUnits)
                return Finish(PutBack(was, b.Origin, b.Velocity) ? JumpEnd.Reset : JumpEnd.Moved, b.Origin);
            if (!_launched)
            {
                if (Approach(b, shot) is { } command)
                    return command;
                _launched = true;
                if (Owner is { } owner && Bot.IsValid)
                    owner.Book.FlightBegan(_flight = new AirFlight(Bot, _plan));
                _launch = new JumpStart(b.Origin, b.Velocity, b.View, Ducked: false, OnGround: true);
                _top = b.Origin.Z;
            }

            JumpPilotStep step = JumpPilot.Step(_recipe, _pilot, b, _limits);
            if (step.Next.Felt && !_pilot.Felt)
                (_feltAfterShot, _leap) = (_pilot.Count - Math.Max(0, _recipe.ShotDelay), b.Velocity - _pilot.LastVelocity);
            if (step.Next.Second > 0 && _pilot.Second == 0)
                _secondAfterShot = step.Next.Second - 1 - Math.Max(0, _recipe.ShotDelay);
            _pilot = step.Next;
            if (step.Next.Phase == JumpPhase.Landed)
                return Finish(step.Next.Airborne && Flew ? JumpEnd.Landed : JumpEnd.Fizzled, b.Origin);
            if (!b.OnGround)
            {
                _air++;
                _top = MathF.Max(_top, b.Origin.Z);
            }

            // The view stays asked onto the shot's line until the pilot has let the trigger go, then onto the second shot's
            // for a recipe with one, then back to the way it goes.
            Vector looks = JumpPilot.LooksTo(_recipe, step.Next with { Count = step.Next.Count - 1 });
            Vector second = _recipe.SecondView;
            int look = looks.X == shot.X && looks.Y == shot.Y ? ShotLook
                : _recipe.Second is not null && looks.X == second.X && looks.Y == second.Y ? SecondLook : WayLook;
            var own = new AbilityCommand(new AiAngles(looks.X, looks.Y, 0f), look, new AiVector(step.Wish.X, step.Wish.Y, 0f), step.Command.Buttons);
            // In the air once the blast is felt, or once the pilot steers without it (JumpPilot.Step: past BlastWaitCommands after the
            // trigger a blast that is coming has come), and with the view let go to the way it goes.
            bool blasted = step.Next.Felt || step.Next.Count - 1 > Math.Max(0, _recipe.ShotDelay) + JumpPilot.BlastWaitCommands;
            return Lend(own, blasted && step.Next.Airborne && !b.OnGround && look == WayLook);
        }

        /// <summary>
        /// The command with what the program lends added (round 2's S1, <see cref="JumpHands"/>): in <paramref name="air"/> (in the air
        /// after the launch, its blast felt and its triggers let go) the class's ask's held keys, its pressed keys on the first command
        /// the ask is seen, its look and its weapon, none of the program's own keys (this command's or the last's) among them; outside it
        /// the program's command alone. A parachute it lent open it closes with its own press for the landing it needs.
        /// </summary>
        private AbilityCommand Lend(AbilityCommand own, bool air)
        {
            AirAsk? ask = Owner is { } owner && Bot.IsValid ? owner.Book.HandsAsked(Bot) : null;
            if (ReferenceEquals(ask, _ask))
                _askAge++;
            else
                (_ask, _askAge, _pressing, _weaponing) = (ask, 0, ask?.Press ?? 0, ask?.Weapon ?? -1);
            int buttons = own.Buttons;
            bool ownJumpLast = _ownJumpLast;
            _ownJumpLast = false;
            if (air && _chuteOpen && _air * _limits.TickSeconds >= _plan.AirSeconds + JumpHands.ChuteKeepSeconds)
            {
                // The program's own press: the chute closes, and the bot comes down to its landing.
                buttons |= GameMovement.InJump;
                (_chuteOpen, _ownJumpLast, _chuteClosedAir) = (false, true, _air);
                Interlocked.Increment(ref Owner!._chuteCloses);
            }

            int mine = buttons | (ownJumpLast ? GameMovement.InJump : 0);
            AbilityCommand sent = own with { Buttons = buttons };
            if (air && ask is { Empty: false } && _askAge <= JumpHands.AskStaleCommands)
            {
                // A press and a weapon go out once an ask's, on the first command in the air they may.
                int lent = (ask.Hold | _pressing) & JumpHands.LendKeys & ~mine, weapon = _weaponing;
                _pressing &= ~lent;
                _weaponing = -1;
                sent = sent with { Buttons = buttons | lent, Weapon = weapon >= 0 ? weapon : null };
                if (ask.Look is { } at)
                    sent = sent with { View = at, Look = LentLook };
                if (lent != 0 || weapon >= 0 || ask.Look is not null)
                {
                    _lent++;
                    Interlocked.Increment(ref Owner!._lentCommands);
                    if ((lent & (GameMovement.InAttack | GameMovement.InAttack2)) != 0)
                        Interlocked.Increment(ref Owner!._lentAttacks);
                    if (ask.Look is not null)
                        Interlocked.Increment(ref Owner!._lentLooks);
                    if (weapon >= 0)
                        Interlocked.Increment(ref Owner!._lentWeapons);
                }

                // A lent press of the jump key, on its edge: the parachute's toggle (stock's ToggleParachute: open, then shut, never open again).
                if ((lent & GameMovement.InJump) != 0 && !_sentJump)
                {
                    if (_chuteOpen)
                        (_chuteOpen, _chuteClosedAir) = (false, _air);
                    else if (!_chuteDeployed)
                    {
                        (_chuteOpen, _chuteDeployed, _chuteOpenedAir) = (true, true, _air);
                        Interlocked.Increment(ref Owner!._chuteOpens);
                    }
                }
            }

            _sentJump = (sent.Buttons & GameMovement.InJump) != 0;
            return sent;
        }

        /// <summary>Whether what the body has done since the launch is a flight on a blast: see <see cref="FlownShare"/>.</summary>
        private bool Flew => _air * _limits.TickSeconds >= _plan.AirSeconds * FlownShare && _top - _launch.Origin.Z >= _plan.Apex * FlownShare;

        /// <summary>
        /// One command before the launch: null when the jump is to be launched with this command
        /// (the recipe's takeoff is then where the bot is), else the command that runs on along
        /// the line with the view turning to the shot's, the one that stands for a view that is
        /// late, or the one that calls the jump off.
        /// </summary>
        private AbilityCommand? Approach(in JumpBody b, Vector shot)
        {
            _approach++;
            if (_plan.StartsStanding && Place(b, shot) is { } placing)
                return placing;
            float yaw = _recipe.TravelYaw * (MathF.PI / 180f), wx = MathF.Cos(yaw), wy = MathF.Sin(yaw);
            float dx = _plan.Recipe.Takeoff.X - b.Origin.X, dy = _plan.Recipe.Takeoff.Y - b.Origin.Y;
            float along = dx * wx + dy * wy, aside = dx * wy - dy * wx;
            if (!b.OnGround || MathF.Abs(aside) > AsideMax)
                return Finish(JumpEnd.Astray, b.Origin);

            // On the shot's line and at rest there: a view sweeping through it mid-flick is not.
            bool onAim = JumpPilot.ViewGap(b.View, shot) <= _tolerance && _approach > 1 && JumpPilot.ViewGap(_lastView, b.View) <= _tolerance * 0.5f;
            _lastView = b.View;
            bool runs = _recipe.RunUp > 0f;
            float speed = MathF.Max(0f, b.Velocity.X * wx + b.Velocity.Y * wy);
            bool onLine = !runs || along <= speed * _limits.TickSeconds * 0.5f + JumpPilot.LineSlack;
            float past = -along;
            if (_approach == 1)
            {
                float turn = JumpPilot.ViewGap(b.View, shot);
                (_roundBy, _likelyBy) = (TurnCommandsAtMost(_motion, turn, _limits.TickSeconds), TurnCommandsLikely(_motion, turn, _limits.TickSeconds));
            }

            // A plan flown from standing runs at its jump only once its view is round, as the simulator flew it.
            if (_plan.StartsStanding && runs && !_aimedStanding)
            {
                if (!onAim)
                    return _approach > _approachMost ? Finish(JumpEnd.ViewLate, b.Origin) : new AbilityCommand(new AiAngles(shot.X, shot.Y, 0f), ShotLook, default, 0);
                _aimedStanding = true;
            }

            // On the line with the view there: launched, a bot that stood no sooner than the plan's hold run from where it did, at its speed again.
            if (onAim && onLine && (_stoodAt is not { } stood || past >= stood + _plan.HoldRun))
            {
                _slid = MathF.Max(0f, past);
                _recipe = _recipe with { Takeoff = new Vector(b.Origin.X, b.Origin.Y, _plan.Recipe.Takeoff.Z) };
                return null;
            }

            if (past > _plan.Slide || _approach > _approachMost)
                return Finish(JumpEnd.ViewLate, b.Origin);

            // The view may not be round before a run from here passes the slide's end, which is two hold runs off or less: stand for it.
            // Not with no room left to run at the jump, nor past the time no turn outlasts, nor, as it first stops, for a wait likely longer than the jump is worth.
            // A view that has been on the line once is not stood for again: it is a flick's last few degrees being taken out, and back before the run is done.
            _seen |= onAim;
            int left = _roundBy - _approach;
            bool late = left <= 0 || past + left * _limits.RunSpeed * _limits.TickSeconds > _plan.Slide;
            bool stands = runs && !_seen && late && _plan.HoldRun > 0f && past >= _plan.Slide - 2f * _plan.HoldRun;
            if (stands)
            {
                bool worth = _stood > 0 || (_likelyBy - _approach) * _limits.TickSeconds <= _plan.HoldSeconds;
                if (past + _plan.HoldRun > _plan.Slide || left <= 0 || !worth)
                    return Finish(JumpEnd.ViewLate, b.Origin);
                _stood++;
                _stoodAt = past;
            }

            // Along the line, pulled back onto it from a side; a jump from standing waits where it is.
            AiVector wish = default;
            if (runs && !stands)
            {
                float pull = Math.Clamp(aside / AsidePull, -1f, 1f) * 0.5f;
                float x = wx + wy * pull, y = wy - wx * pull, length = MathF.Sqrt(x * x + y * y);
                wish = new AiVector(x / length * _limits.FullMove, y / length * _limits.FullMove, 0f);
            }

            return new AbilityCommand(new AiAngles(shot.X, shot.Y, 0f), ShotLook, wish, 0);
        }

        /// <summary>
        /// A plan that starts standing (a link's): the body walked onto the recipe's start, slower as it nears it, until it
        /// is within <see cref="PlaceNear"/> and moving under <see cref="StillSpeed"/>, its view turning to the shot's line
        /// meanwhile; null once it is placed. Off the ground, or begun further than <see cref="PlaceReach"/> from the start,
        /// the jump is called off (astray); so is a placing that takes the approach's whole time.
        /// </summary>
        private AbilityCommand? Place(in JumpBody b, Vector shot)
        {
            if (_placed)
                return null;
            JumpStart start = _plan.Recipe.Start();
            float dx = start.Origin.X - b.Origin.X, dy = start.Origin.Y - b.Origin.Y, far = MathF.Sqrt(dx * dx + dy * dy);
            if (!b.OnGround || far > PlaceReach)
                return Finish(JumpEnd.Astray, b.Origin);
            if (_approach > _approachMost)
                return Finish(JumpEnd.ViewLate, b.Origin);
            float speed = MathF.Sqrt(b.Velocity.X * b.Velocity.X + b.Velocity.Y * b.Velocity.Y);
            AiVector wish = default;
            if (far > PlaceNear)
            {
                // A wish's length is the speed it asks for (the movement holds it to the class's own).
                float want = Math.Clamp(far * PlaceGain, PlaceSpeedMin, _limits.RunSpeed);
                wish = new AiVector(dx / far * want, dy / far * want, 0f);
            }
            else if (speed <= StillSpeed)
            {
                _placed = true;
                return null;
            }

            return new AbilityCommand(new AiAngles(shot.X, shot.Y, 0f), ShotLook, wish, 0);
        }

        /// <summary>
        /// Whether the game's move from <paramref name="was"/> to <paramref name="now"/>, the body moving at
        /// <paramref name="velocity"/> after it, put the body back at the jump's start (<see cref="JumpEnd.Reset"/>).
        /// </summary>
        private bool PutBack(Vector was, Vector now, Vector velocity)
        {
            Vector start = _recipe.Start().Origin;
            float flat = MathF.Sqrt((now.X - start.X) * (now.X - start.X) + (now.Y - start.Y) * (now.Y - start.Y));
            return (was.Z < start.Z - ResetDrop || velocity.Length >= StillSpeed) && MathF.Abs(now.Z - start.Z) <= ResetDrop && flat <= ResetReach;
        }

        private AbilityCommand Finish(JumpEnd end, Vector at)
        {
            if (!_over)
            {
                _over = true;
                if (_flight is { } flight)
                    Owner!.Book.FlightEnded(flight);
                _flew(new JumpFlown(_plan, _recipe, _launch, _launched, _slid, _approach - (_launched ? 1 : 0), _air, _pilot.Felt, at, end, _limits.TickSeconds)
                {
                    Rise = _launched ? _top - _launch.Origin.Z : 0f, Stood = _stood, FeltAfterShot = _feltAfterShot, Leap = _leap,
                    SecondAfterShot = _secondAfterShot, Lent = _lent, ChuteOpenedAir = _chuteOpenedAir, ChuteClosedAir = _chuteClosedAir,
                });
            }

            return AbilityCommand.Done;
        }

        public void Ended(AbilityEnd why, in AbilityBody body)
        {
            if (why != AbilityEnd.Over)
                Finish(why == AbilityEnd.Died ? JumpEnd.Died : JumpEnd.TimedOut, new Vector(body.Origin.X, body.Origin.Y, body.Origin.Z));
        }
    }
}
