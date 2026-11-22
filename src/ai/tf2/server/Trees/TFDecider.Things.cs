using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// An enemy sentry gun as the bot remembers it: the binding's id for it, where it stands, its level, and when the bot first and
/// last saw it. One it knows only from a teammate's call has an id under 0, no level, and the caller's place.
/// </summary>
public readonly record struct SeenSentry(int Id, AiVector At, int Level, Tick First, Tick Last);

/// <summary>
/// Reacting to what the bot sees that is not a player (plan_bots_teams_tf2_part_g G.2.3): the
/// projectiles and buildings its client draws (<see cref="PerceivedView.Things"/>), and the
/// conditions drawn on the enemies in sight.
///
/// <para>A rocket, a pipe or a sticky that can hurt it, whose flight comes within its blast of
/// the bot (<see cref="BlastDodge"/>, the blast's reach and the fall being
/// <see cref="TFKnowledge"/>'s): it steps out across the path, or jumps when the blast is nearly
/// on it, after its reaction time and by its Agility (<c>dodgeChance</c>, drawn once for each
/// projectile, and <c>dodgeLateMs</c>: a clumsy bot gets out of few, and late).</para>
///
/// <para>An enemy sentry gun in sight: its place is remembered for the round
/// (<see cref="Sentries"/>). In sight and in range (<see cref="TFKnowledge.SentryRange"/>) the bot
/// goes back to where it last stood out of range; and for <see cref="SentryShySeconds"/> after it
/// last saw the sentry it does not walk back in (it stops at the edge when its way leads into
/// the range: <see cref="WalksIntoASentry"/>), and the objectives that lie inside the range are
/// worth a fraction to it (<see cref="SentryWeight"/>), so another branch takes it elsewhere when
/// one can. After that it is willing to look again. A class that kills sentries is P7's
/// (<see cref="AvoidsSentries"/>): until then a bot whose only way lies through a sentry's range
/// waits at its edge.</para>
///
/// <para>An invulnerable enemy in sight within <see cref="InvulnerableBackOff"/>: it backs off,
/// still firing if its hands were. A bot carrying the flag runs home through all of it but the
/// blast.</para>
///
/// <para><see cref="SeeThings"/> reads the view before the tree runs and works the reflex out;
/// <see cref="ReactAndSpeak"/> puts it over the move the tree chose, after the hands. A bot the
/// game holds still (<see cref="HeldStill"/>: TF2's pre-round freeze) does neither: it works no
/// reflex out, carries none out and says nothing until it is let go.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>How many projectiles the bot keeps track of at once, and how many sentries it remembers.</summary>
    public const int WatchedShots = 8, RememberedSentries = 8;

    /// <summary>A blast this near in time is jumped away from, a step being too slow; the jump is toward a point this far out.</summary>
    public const float DodgeJumpSeconds = 0.45f, DodgeJumpUnits = 128f;

    /// <summary>A projectile not seen for this long is no longer tracked.</summary>
    public const float ShotForgottenSeconds = 1f;

    /// <summary>The bot keeps this far past a sentry's range, and within this much more of that it stops rather than walk in.</summary>
    public const float SentryMargin = 100f, SentryEdgeBand = 200f;

    /// <summary>For this long after it last saw a sentry, an objective inside the sentry's range is worth this share to the bot.</summary>
    public const float SentryShySeconds = 45f, SentryShy = 0.25f;

    /// <summary>An invulnerable enemy in sight within this is backed away from.</summary>
    public const float InvulnerableBackOff = FightFar;

    private struct WatchedShot
    {
        public int Id;
        public Tick First, Last;
        public bool Dodges;
    }

    private readonly WatchedShot[] _shots = new WatchedShot[WatchedShots];
    private readonly SeenSentry[] _sentries = new SeenSentry[RememberedSentries];
    private int _shotCount, _sentryCount;
    private AiRandom _thingRng;
    private MoveIntent _reflex;
    private string _reflexWhy = "";
    private bool _sentrySeenNew, _safeSpotKnown;
    private AiVector _safeSpot;

    /// <summary>The enemy sentries the bot remembers this round, in the order it first saw them.</summary>
    public ReadOnlySpan<SeenSentry> Sentries => _sentries.AsSpan(0, _sentryCount);

    /// <summary>Whether this decide's view showed the bot an enemy sentry it had not seen before.</summary>
    internal bool SawNewSentry => _sentrySeenNew;

    /// <summary>What the bot is about to do about what it sees ("" for nothing) and the move that does it, for the facts and the inspector.</summary>
    internal (string Why, MoveIntent Move) Reflex => (_reflexWhy, _reflex);

    /// <summary>Reflexes carried out by kind, for the facts and the counters.</summary>
    public int Dodges { get; private set; }

    public int SentryRetreats { get; private set; }

    public int InvulnerableBackOffs { get; private set; }

    /// <summary>The reflexes' verdicts on the doing record.</summary>
    public const string DodgeVerdict = "out of a blast's way", SentryVerdict = "out of a sentry's range", InvulnerableVerdict = "backing off (invulnerable)",
                        SentryEdgeVerdict = "a sentry ahead";

    /// <summary>Decides on which the bot stopped at the edge of a sentry's range rather than walk in.</summary>
    public int SentryHolds { get; private set; }

    /// <summary>Whether the bot keeps out of a sentry's range: the class's say (P7, <see cref="ClassPlay.AvoidsSentries"/>), every class's by default.</summary>
    private bool AvoidsSentries => _classPlay?.AvoidsSentries ?? true;

    private AiVector BodyMiddle
        => _self.Origin + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Body, _class).At((_self.Flags & SelfFlags.Ducking) != 0));

    private float DodgeAfterSeconds => (_p.ReactionMs + _p.DodgeLateMs) * 0.001f;

    /// <summary>
    /// Whether the game holds the bot still, as its own HUD shows it (<see cref="SelfFlags.Frozen"/>:
    /// TF2's pre-round freeze): it cannot step out of anything, and a player waiting for the round
    /// calls for nobody.
    /// </summary>
    private bool HeldStill => (_self.Flags & SelfFlags.Frozen) != 0;

    /// <summary>
    /// Reads the things in view: tracks the projectiles that could hurt the bot, remembers the
    /// enemy sentries, and works out this decide's reflex, the most urgent first (a blast, then
    /// a sentry's range, then an invulnerable enemy). Before the tree, once a decide.
    /// </summary>
    internal void SeeThings()
    {
        _reflexWhy = "";
        _sentrySeenNew = false;
        if (_ownFlag is { Setup: true } || _enemyFlag is { Setup: true })
            _sentryCount = 0;
        if (HeldStill)
            return;
        HearSentryCalls();

        AiVector body = BodyMiddle;
        float soonest = float.MaxValue, nearestSentry = float.MaxValue;
        AiVector sentryAt = default;
        foreach (ref readonly ThingFacts t in _view.Things)
        {
            var type = (TFThingType)t.Type;
            if (t.Kind == ThingKind.Building)
            {
                if (type != TFThingType.Sentry || !TeamRule.IsGameTeam(t.Team) || t.Team == _self.Team)
                    continue;
                Remember(in t);
                float d = _self.Origin.DistanceTo(t.Origin);
                if (d < nearestSentry)
                    (nearestSentry, sentryAt) = (d, t.Origin);
                continue;
            }

            float blast = TFKnowledge.BlastRadius(type);
            if (t.Kind != ThingKind.Projectile || blast <= 0f || !CanHurtMe(in t))
                continue;
            ref WatchedShot shot = ref Watch(t.Id);
            if (!BlastDodge.Threatens(body, _self.Origin.Z, t.Origin, t.Velocity, TFKnowledge.FallOf(type), blast + TFKnowledge.BodyHalfWidth,
                                      out float seconds, out AiVector nearest))
                continue;
            if (!shot.Dodges || (_now - shot.First) * _tick < DodgeAfterSeconds || seconds >= soonest)
                continue;
            soonest = seconds;
            AiVector away = BlastDodge.Away(body, t.Velocity, nearest, (t.Id & 1) == 0 ? 1 : -1);
            bool jump = seconds > 0f && seconds < DodgeJumpSeconds && (_self.Flags & SelfFlags.Grounded) != 0;
            _reflex = jump
                ? new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, _self.Origin + away * DodgeJumpUnits, default, default, default, default, false, 0)
                : Step(away);
            _reflexWhy = DodgeVerdict;
        }

        ForgetShots();
        // Where the bot last stood out of every watched sentry's range: the way back when one has it in range.
        if (!InAWatchedSentrysRange())
            (_safeSpot, _safeSpotKnown) = (_self.Origin, true);
        if (_reflexWhy.Length != 0 || Carrying)
            return;
        if (AvoidsSentries && nearestSentry < TFKnowledge.SentryRange + SentryMargin)
        {
            _reflex = _safeSpotKnown
                ? new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, _safeSpot, default, default, default, default, false, 0)
                : Step(Flat(_self.Origin - sentryAt));
            _reflexWhy = SentryVerdict;
            return;
        }

        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if ((f.Conditions & ActorConditions.Invulnerable) == 0 || TeamRule.Friends(_self.Team, f.Team)
                || _self.Origin.DistanceTo(f.Origin) > InvulnerableBackOff)
                continue;
            (_reflex, _reflexWhy) = (Step(Flat(_self.Origin - f.Origin)), InvulnerableVerdict);
            return;
        }
    }

    /// <summary>
    /// Carries this decide's reflex out over the move the tree chose (not while a rocket jump's
    /// sequence has the body), then says what the bot has to say (<see cref="Speak"/>). After the
    /// hands, once a decide; nothing while the game holds the bot still.
    /// </summary>
    internal void ReactAndSpeak()
    {
        if (HeldStill)
            return;
        if (_reflexWhy.Length != 0 && !Busy && (_classPlay?.TakesReflex(_reflexWhy) ?? true))
        {
            _move = _reflex;
            _verdict = _reflexWhy;
            Dodges += _reflexWhy == DodgeVerdict ? 1 : 0;
            SentryRetreats += _reflexWhy == SentryVerdict ? 1 : 0;
            InvulnerableBackOffs += _reflexWhy == InvulnerableVerdict ? 1 : 0;
        }
        else if (!Busy && WalksIntoASentry())
        {
            _move = MoveIntent.Hold;
            _verdict = SentryEdgeVerdict;
            SentryHolds++;
        }

        Speak();
    }

    /// <summary>Whether a sentry the bot has itself seen within <see cref="SentryShySeconds"/> (not one it only heard called) is still watched for.</summary>
    private bool Watched(in SeenSentry sentry) => sentry.Id >= 0 && (_now - sentry.Last) * _tick < SentryShySeconds;

    private bool InAWatchedSentrysRange()
    {
        float reach = TFKnowledge.SentryRange + SentryMargin;
        for (int i = 0; i < _sentryCount; i++)
        {
            if (Watched(in _sentries[i]) && _self.Origin.DistanceTo(_sentries[i].At) < reach)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the move the tree chose walks the bot into a watched sentry's range: it is within
    /// <see cref="SentryEdgeBand"/> of the range's edge (or inside it, out of the sentry's sight),
    /// and the straight way to its goal passes nearer the sentry than it stands, inside the range.
    /// Not the carrier's business: it runs home.
    /// </summary>
    private bool WalksIntoASentry()
    {
        if (Carrying || !AvoidsSentries || !_hasGoal || _move.Kind != MoveKind.Follow)
            return false;
        float reach = TFKnowledge.SentryRange + SentryMargin;
        var way = new AiVector(_goal.X - _self.Origin.X, _goal.Y - _self.Origin.Y, 0f);
        float length2 = way.LengthSquared;
        if (length2 < 1f)
            return false;
        for (int i = 0; i < _sentryCount; i++)
        {
            if (!Watched(in _sentries[i]))
                continue;
            var to = new AiVector(_sentries[i].At.X - _self.Origin.X, _sentries[i].At.Y - _self.Origin.Y, 0f);
            float stands = to.Length;
            float along = Math.Min(1f, to.Dot(way) / length2);
            if (stands > reach + SentryEdgeBand || along <= 0f)
                continue;
            float passes = (to - way * along).Length;
            if (passes < reach && passes < stands - 1f)
                return true;
        }

        return false;
    }

    private static AiVector Flat(AiVector v) => new AiVector(v.X, v.Y, 0f).Normalised();

    private static MoveIntent Step(AiVector way)
        => way.LengthSquared < 0.01f ? MoveIntent.Hold : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, way, false, 0);

    /// <summary>
    /// Whether a projectile's blast can hurt the bot, as far as its client's drawing says: not its
    /// own rocket (it means that one), nor a teammate's while friendly fire is off.
    /// </summary>
    private bool CanHurtMe(in ThingFacts t)
        => t.OwnerSlot != _bot.Slot && (_memory.FriendlyFireOn || !TeamRule.Friends(_self.Team, t.Team));

    /// <summary>The track of projectile <paramref name="id"/>, begun now if it is new: when it was first seen, and whether the bot will dodge it (drawn once, by Agility).</summary>
    private ref WatchedShot Watch(int id)
    {
        for (int i = 0; i < _shotCount; i++)
        {
            if (_shots[i].Id == id)
            {
                _shots[i].Last = _now;
                return ref _shots[i];
            }
        }

        // Full: the one seen longest ago gives way.
        int at = _shotCount;
        if (at == _shots.Length)
        {
            at = 0;
            for (int i = 1; i < _shots.Length; i++)
            {
                if (_shots[i].Last < _shots[at].Last)
                    at = i;
            }
        }
        else
        {
            _shotCount++;
        }

        _shots[at] = new WatchedShot { Id = id, First = _now, Last = _now, Dodges = _thingRng.NextFloat() < _p.DodgeChance };
        return ref _shots[at];
    }

    private void ForgetShots()
    {
        int n = 0;
        for (int i = 0; i < _shotCount; i++)
        {
            if ((_now - _shots[i].Last) * _tick <= ShotForgottenSeconds)
                _shots[n++] = _shots[i];
        }

        _shotCount = n;
    }

    /// <summary>A teammate's "Sentry ahead!" is about a sentry the bot knows when one stands within this of where the teammate called from.</summary>
    public const float CalledSentryNear = 1200f;

    private Tick _sentryCallHeard;
    private int _sentryCalls;

    /// <summary>
    /// A teammate's "Sentry ahead!" (the voice menu, in: <see cref="BotMemory.LastHazardCall"/>)
    /// marks the place as a sighting does: a sentry the bot remembers near where the call came
    /// from is there still, and with none the caller's own place is remembered as one, by
    /// hearsay (an id under 0, no level), until the bot sees the sentry itself.
    /// </summary>
    private void HearSentryCalls()
    {
        BotMemory.HeardCallout call = _memory.LastHazardCall;
        if (call.Kind != CalloutKind.Hazard || call.Tick == _sentryCallHeard)
            return;
        _sentryCallHeard = call.Tick;
        for (int i = 0; i < _sentryCount; i++)
        {
            if (_sentries[i].At.DistanceTo(call.Place) < CalledSentryNear)
            {
                _sentries[i] = _sentries[i] with { Last = _now };
                return;
            }
        }

        Keep(new SeenSentry(-1 - _sentryCalls++, call.Place, 0, _now, _now));
    }

    /// <summary>
    /// An enemy sentry in sight is remembered, or its memory brought up to date. One a teammate
    /// called and the bot now sees takes the call's place in the memory, and is no news to tell.
    /// </summary>
    private void Remember(in ThingFacts sentry)
    {
        int called = -1;
        for (int i = 0; i < _sentryCount; i++)
        {
            if (_sentries[i].Id == sentry.Id)
            {
                _sentries[i] = _sentries[i] with { At = sentry.Origin, Level = sentry.Level, Last = _now };
                return;
            }

            if (_sentries[i].Id < 0 && _sentries[i].At.DistanceTo(sentry.Origin) < CalledSentryNear)
                called = i;
        }

        if (called >= 0)
        {
            _sentries[called] = new SeenSentry(sentry.Id, sentry.Origin, sentry.Level, _sentries[called].First, _now);
            return;
        }

        _sentrySeenNew = true;
        Keep(new SeenSentry(sentry.Id, sentry.Origin, sentry.Level, _now, _now));
    }

    /// <summary>Adds a sentry to the memory; when it is full the one seen longest ago gives way.</summary>
    private void Keep(SeenSentry seen)
    {
        if (_sentryCount < _sentries.Length)
        {
            _sentries[_sentryCount++] = seen;
            return;
        }

        int oldest = 0;
        for (int i = 1; i < _sentryCount; i++)
        {
            if (_sentries[i].Last < _sentries[oldest].Last)
                oldest = i;
        }

        // In the order first seen: the rest move up and the new one is last.
        Array.Copy(_sentries, oldest + 1, _sentries, oldest, _sentryCount - oldest - 1);
        _sentries[_sentryCount - 1] = seen;
    }

    /// <summary>
    /// What a remembered sentry makes of <paramref name="branch"/>'s score, <see cref="Weigh"/>'s:
    /// <see cref="SentryShy"/> while the place the branch goes to (the enemy's flag, the bot's own
    /// away from home, the carrier) lies inside the range of a sentry the bot saw within
    /// <see cref="SentryShySeconds"/>; else 1.
    /// </summary>
    private float SentryWeight(string branch)
    {
        if (_sentryCount == 0 || !AvoidsSentries)
            return 1f;
        Objective? goes = branch switch
        {
            Fetch or Escort => _enemyFlag,
            ChaseCarrier or Guard => _ownFlag,
            _ => null,
        };
        if (goes is not { } place)
            return 1f;
        float range = TFKnowledge.SentryRange;
        for (int i = 0; i < _sentryCount; i++)
        {
            if ((_now - _sentries[i].Last) * _tick < SentryShySeconds && _sentries[i].At.DistanceTo(place.Position) < range)
                return SentryShy;
        }

        return 1f;
    }
}
