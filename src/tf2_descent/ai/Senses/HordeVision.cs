using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;

namespace TF2.Descent.Ai.Senses;

/// <summary>What the vision pass asks of the world (the server's PVS and line traces; a fake in facts).</summary>
public interface IHordeLines
{
    /// <summary>Whether the box is in the potentially visible set of hero <paramref name="hero"/>'s eye (the hero's index, 0 to 3).</summary>
    bool InPvs(int hero, AiVector mins, AiVector maxs);

    /// <summary>Whether a line from <paramref name="from"/> to <paramref name="to"/> is clear. One call is one trace.</summary>
    bool LineClear(AiVector from, AiVector to);
}

/// <summary>Where the vision pass's answers go: a robot recognised a hero, or lost it.</summary>
public interface IHordeSightSink
{
    void Gained(int body, int hero, AiVector at, AiVector velocity, float fraction);

    void Lost(int body, int hero, AiVector at, AiVector velocity);
}

/// <summary>
/// A lent body's eye as the vision pass needs it: where, which way (degrees),
/// how far and how wide it sees, and how long a hero must stay clear to be
/// recognised and stay hidden to be lost, in ticks; within <see cref="Around"/>
/// units it sees whichever way it faces (still never through a wall). Not present: no brain.
/// </summary>
public readonly record struct BodyEye(bool Present, AiVector Eye, float Yaw, float Range, float CosHalfCone, int RecognizeTicks, int LostTicks,
                                      float Around = 0f);

/// <summary>
/// A hero as the vision pass looks at it: its eye and chest (the two sample points), feet (what
/// is told) and velocity; <see cref="SightScale"/>, the multiplier on every robot's range for
/// him (the dark and his room's light: 1 is a robot's own range); <see cref="Veil"/>, whether robots may
/// recognise him at all this tick (his respawn's grace); <see cref="LampReach"/>, how far his lit
/// lamp shows him however dark it is (0: out), up to a robot's own range.
/// </summary>
public readonly record struct HeroTarget(bool Present, AiVector Eye, AiVector Chest, AiVector Feet, AiVector Velocity, float SightScale = 1f,
                                         HeroVeil Veil = HeroVeil.None, float LampReach = 0f);

/// <summary>
/// Whether the robots may recognise a hero this tick (plan_descent_logic 3.6; this lane's rule
/// for the respawn's grace). A veiled hero is still looked at: the stages and the lines run, so
/// the pass knows which robots have him clear, but none is told it saw him.
/// </summary>
public enum HeroVeil : byte
{
    /// <summary>Seen by the ordinary rules.</summary>
    None,

    /// <summary>
    /// No robot recognises him (he is in his respawn's grace): a sight held is lost at once, and
    /// a robot that has him clear is marked as watching him (<see cref="HordeVision.Watching"/>).
    /// </summary>
    Hidden,

    /// <summary>
    /// After the grace: a robot still watching him from it stays blind to him until it has lost
    /// him from view once (out of range, cone, PVS or line); every other robot sees him by the
    /// ordinary rules.
    /// </summary>
    Watched,
}

/// <summary>Every stage of one robot-hero pair as it stands now, for the dev print: nothing is told and nothing kept.</summary>
public readonly record struct SightProbe(float Distance, float Range, bool InRange, float OffConeDeg, float HalfConeDeg, bool InCone, bool InPvs,
                                         bool EyeClear, bool ChestClear, HeroVeil Veil, bool Held, bool Watching);

/// <summary>
/// The horde's sight (plan_descent_logic HO5; plan_tf2_rpg B.3), the one sense
/// that needs the engine, on the game thread: every robot-hero pair, in pair
/// order (body, then hero), through range, the robot's view cone, the hero's
/// PVS, then a line to the hero's eye and, if that is blocked, one to its
/// chest. The cheap stages cost no trace. The lines come from a FIXED budget a
/// tick (<see cref="Budget"/>), served round-robin: each tick starts where the
/// last one stopped, so a pair that passes the cheap stages waits at most
/// <c>ceil(2 · candidates / budget)</c> ticks for its lines. A pair the budget
/// did not reach this tick is not observed at all (its sight holds), never
/// counted as blocked.
///
/// <para>
/// A pair clear for <see cref="BodyEye.RecognizeTicks"/> ticks running (Alertness,
/// <see cref="VisionCurves.RecognizeAt"/>) is recognised: <c>SightGained</c>. A held
/// pair not clear for <see cref="BodyEye.LostTicks"/> is lost: <c>SightLost</c>,
/// with the hero's place and velocity when it was last clear. While held, the
/// robot's view carries the hero's kinematics (<see cref="Holds"/>); never else.
/// </para>
///
/// <para>
/// <b>Per hero</b>: a pair's range is the robot's <see cref="BodyEye.Range"/> times the
/// hero's <see cref="HeroTarget.SightScale"/> (plan_tf2_rpg B.3: "cloak, disguise and darkness
/// are multipliers on range"; the depth's dark and his room's light, folded in by the brains), or
/// his lit lamp's reach where that is farther, up to the robot's range (<see cref="RangeFor"/>).
/// Within <see cref="BodyEye.Around"/> the cone is not asked (a hero very close behind is seen). A hero
/// under a <see cref="HeroTarget.Veil"/> is looked at but not recognised (see
/// <see cref="HeroVeil"/>). Allocates nothing after it is made.
/// </para>
/// </summary>
public sealed class HordeVision
{
    /// <summary>Lines a tick unless told otherwise.</summary>
    public const int DefaultBudget = 32;

    private const long Never = long.MinValue;

    private readonly int _bodies;
    private readonly bool[] _candidate;
    private readonly bool[] _held;
    private readonly long[] _firstClear;
    private readonly long[] _lastClear;
    private readonly long[] _waitingSince;
    private readonly AiVector[] _lastAt;
    private readonly AiVector[] _lastVel;
    private readonly float[] _fraction;
    private readonly bool[] _watching;
    private int _cursor;

    public HordeVision(int budget, int bodies, bool[] candidate, bool[] held, long[] firstClear, long[] lastClear, long[] waitingSince,
                       AiVector[] lastAt, AiVector[] lastVel, float[] fraction, bool[] watching)
    {
        _watching = watching;
        Budget = budget;
        _bodies = bodies;
        _candidate = candidate;
        _held = held;
        _firstClear = firstClear;
        _lastClear = lastClear;
        _waitingSince = waitingSince;
        _lastAt = lastAt;
        _lastVel = lastVel;
        _fraction = fraction;
    }

    /// <summary>Sight for bodies 0 to <paramref name="lastBody"/> (0 unused) and <see cref="HordeSlots.Heroes"/> heroes, at <paramref name="budget"/> lines a tick.</summary>
    public static HordeVision For(int lastBody, int budget = DefaultBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
        int pairs = (lastBody + 1) * HordeSlots.Heroes;
        var first = new long[pairs];
        var last = new long[pairs];
        var waiting = new long[pairs];
        Array.Fill(first, Never);
        Array.Fill(last, Never);
        Array.Fill(waiting, Never);
        return new HordeVision(budget, lastBody + 1, new bool[pairs], new bool[pairs], first, last, waiting,
                               new AiVector[pairs], new AiVector[pairs], new float[pairs], new bool[pairs]);
    }

    public int Budget { get; }

    /// <summary>Lines traced on the last tick.</summary>
    public int TracesThisTick { get; private set; }

    /// <summary>The most lines any tick traced.</summary>
    public int MostTraces { get; private set; }

    /// <summary>Lines traced since the level began.</summary>
    public long Traces { get; private set; }

    /// <summary>Pairs that passed the cheap stages on the last tick.</summary>
    public int CandidatesThisTick { get; private set; }

    /// <summary>Candidates the last tick's budget did not reach.</summary>
    public int UnservedThisTick { get; private set; }

    /// <summary>The longest any candidate pair has waited for its lines, in ticks.</summary>
    public long LongestWait { get; private set; }

    public long Gains { get; private set; }

    public long Losses { get; private set; }

    /// <summary>Whether <paramref name="body"/> holds sight of hero <paramref name="hero"/> (recognised and not yet lost).</summary>
    public bool Holds(int body, int hero) => _held[Pair(body, hero)];

    /// <summary>The share of the hero's sample points clear at the pair's last clear look: 1 the eye, 0.5 the chest only.</summary>
    public float Fraction(int body, int hero) => _fraction[Pair(body, hero)];

    /// <summary>Whether <paramref name="body"/> has had veiled hero <paramref name="hero"/> clear since his veil began and not lost him from view since.</summary>
    public bool Watching(int body, int hero) => _watching[Pair(body, hero)];

    /// <summary>Forgets a body's pairs without telling anyone: its brain is gone (returned, re-lent, dead).</summary>
    public void ForgetBody(int body)
    {
        for (int h = 0; h < HordeSlots.Heroes; h++)
            Reset(Pair(body, h));
    }

    /// <summary>
    /// Forgets every pair of hero <paramref name="hero"/> without telling anyone: he is dead, and
    /// every brain has been told to forget him, so no robot loses sight of him (a
    /// <c>SightLost</c> would carry his last place into its memory).
    /// </summary>
    public void ForgetHero(int hero)
    {
        for (int b = 0; b < _bodies; b++)
            Reset(Pair(b, hero));
    }

    /// <summary>
    /// Every stage of the pair of <paramref name="body"/>'s eye <paramref name="e"/> and hero
    /// <paramref name="hero"/>'s target <paramref name="t"/> as it stands now, both lines traced
    /// whatever the cheap stages say (the dev print's; never on the tick's path).
    /// </summary>
    public SightProbe Probe(int body, int hero, in BodyEye e, in HeroTarget t, IHordeLines lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        AiVector to = t.Eye - e.Eye;
        float range = RangeFor(in e, in t);
        float flat = to.Length2D;
        float off = 0f;
        if (flat > 1f)
        {
            float yaw = e.Yaw * (MathF.PI / 180f);
            float dot = (MathF.Cos(yaw) * to.X + MathF.Sin(yaw) * to.Y) / flat;
            off = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);
        }

        int p = Pair(body, hero);
        return new SightProbe(to.Length, range, to.Length <= range, off, MathF.Acos(Math.Clamp(e.CosHalfCone, -1f, 1f)) * (180f / MathF.PI),
                              flat <= 1f || to.Length <= e.Around || MathF.Cos(off * (MathF.PI / 180f)) >= e.CosHalfCone, PvsOf(in e, hero, lines),
                              lines.LineClear(e.Eye, t.Eye), lines.LineClear(e.Eye, t.Chest), t.Veil, _held[p], _watching[p]);
    }

    /// <summary>One tick of sight.</summary>
    public void Run(long now, ReadOnlySpan<BodyEye> bodies, ReadOnlySpan<HeroTarget> heroes, IHordeLines lines, IHordeSightSink sink)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(sink);
        if (bodies.Length > _bodies || heroes.Length > HordeSlots.Heroes)
            throw new ArgumentException($"sight holds {_bodies - 1} bodies and {HordeSlots.Heroes} heroes");

        // The cheap stages: range, cone, PVS. A pair that fails one is seen not clear now.
        int candidates = 0;
        for (int b = 1; b < _bodies; b++)
        {
            for (int h = 0; h < HordeSlots.Heroes; h++)
            {
                int p = Pair(b, h);
                _candidate[p] = false;
                bool bodyHere = b < bodies.Length && bodies[b].Present, heroHere = h < heroes.Length && heroes[h].Present;
                if (!bodyHere || !heroHere)
                {
                    // No brain: nobody to tell. No hero: the robot loses it now.
                    if (bodyHere && _held[p])
                        LoseNow(p, b, h, sink);
                    Reset(p);
                    continue;
                }

                ref readonly BodyEye e = ref bodies[b];
                ref readonly HeroTarget t = ref heroes[h];
                if (t.Veil == HeroVeil.None)
                    _watching[p] = false;
                else if (t.Veil == HeroVeil.Hidden && _held[p])
                    LoseNow(p, b, h, sink);   // a sight held is lost the moment he is hidden

                if (!Passes(in e, in t, h, lines))
                {
                    NotClear(p, b, h, now, e.LostTicks, sink);
                    continue;
                }

                _candidate[p] = true;
                if (_waitingSince[p] == Never)
                    _waitingSince[p] = now;
                candidates++;
            }
        }

        // The lines: round-robin from the cursor, within the budget.
        int traces = 0, unserved = 0, pairs = _bodies * HordeSlots.Heroes, next = -1;
        for (int k = 0; k < pairs; k++)
        {
            int p = (_cursor + k) % pairs;
            if (!_candidate[p])
                continue;
            if (traces >= Budget)
            {
                unserved++;
                if (next < 0)
                    next = p;
                continue;
            }

            int b = p / HordeSlots.Heroes, h = p % HordeSlots.Heroes;
            ref readonly BodyEye e = ref bodies[b];
            ref readonly HeroTarget t = ref heroes[h];
            traces++;
            float fraction = lines.LineClear(e.Eye, t.Eye) ? 1f : 0f;
            if (fraction == 0f)
            {
                if (traces >= Budget)
                {
                    // Its second line is beyond the budget: it waits, unjudged, at the head of the next tick.
                    unserved++;
                    if (next < 0)
                        next = p;
                    continue;
                }

                traces++;
                fraction = lines.LineClear(e.Eye, t.Chest) ? 0.5f : 0f;
            }

            LongestWait = Math.Max(LongestWait, now - _waitingSince[p]);
            _waitingSince[p] = Never;
            if (fraction > 0f && Blind(p, in t))
                Watch(p);
            else if (fraction > 0f)
                Clear(p, b, h, now, fraction, in e, in t, sink);
            else
                NotClear(p, b, h, now, e.LostTicks, sink);
        }

        if (next >= 0)
            _cursor = next;
        TracesThisTick = traces;
        MostTraces = Math.Max(MostTraces, traces);
        Traces += traces;
        CandidatesThisTick = candidates;
        UnservedThisTick = unserved;
    }

    /// <summary>
    /// How far eye <paramref name="e"/> sees hero <paramref name="t"/>: its range times his
    /// <see cref="HeroTarget.SightScale"/>, or his lit lamp's reach where that is farther, but never
    /// beyond the robot's own range.
    /// </summary>
    public static float RangeFor(in BodyEye e, in HeroTarget t)
    {
        float range = e.Range * t.SightScale;
        return t.LampReach > 0f ? MathF.Max(range, MathF.Min(e.Range, t.LampReach)) : range;
    }

    private static bool Passes(in BodyEye e, in HeroTarget t, int hero, IHordeLines lines)
    {
        AiVector to = t.Eye - e.Eye;
        if (to.Length > RangeFor(in e, in t))
            return false;

        float flat = to.Length2D;
        if (flat > 1f && to.Length > e.Around)
        {
            float yaw = e.Yaw * (MathF.PI / 180f);
            float dot = (MathF.Cos(yaw) * to.X + MathF.Sin(yaw) * to.Y) / flat;
            if (dot < e.CosHalfCone)
                return false;
        }

        return PvsOf(in e, hero, lines);
    }

    /// <summary>The robot's box from the hero's eye: the PVS is symmetric enough for a gate.</summary>
    private static bool PvsOf(in BodyEye e, int hero, IHordeLines lines)
    {
        var half = new AiVector(16f, 16f, 0f);
        return lines.InPvs(hero, e.Eye - half - new AiVector(0f, 0f, 64f), e.Eye + half + new AiVector(0f, 0f, 8f));
    }

    /// <summary>Whether the pair may not recognise its hero now: he is hidden, or watched and this robot is still watching him.</summary>
    private bool Blind(int p, in HeroTarget t) => t.Veil == HeroVeil.Hidden || (t.Veil == HeroVeil.Watched && _watching[p]);

    /// <summary>A veiled hero clear to the robot: it watches him, and its recognition has not begun.</summary>
    private void Watch(int p)
    {
        _watching[p] = true;
        _firstClear[p] = Never;
    }

    private void Clear(int p, int b, int h, long now, float fraction, in BodyEye e, in HeroTarget t, IHordeSightSink sink)
    {
        if (_firstClear[p] == Never)
            _firstClear[p] = now;
        _lastClear[p] = now;
        _lastAt[p] = t.Feet;
        _lastVel[p] = t.Velocity;
        _fraction[p] = fraction;
        if (!_held[p] && now - _firstClear[p] >= e.RecognizeTicks)
        {
            _held[p] = true;
            Gains++;
            sink.Gained(b, h, t.Feet, t.Velocity, fraction);
        }
    }

    private void NotClear(int p, int b, int h, long now, int lostTicks, IHordeSightSink sink)
    {
        _waitingSince[p] = Never;
        _firstClear[p] = Never;
        _watching[p] = false;   // out of its view once: it sees him again only by the ordinary rules
        if (_held[p] && now - _lastClear[p] >= lostTicks)
            LoseNow(p, b, h, sink);
    }

    private void LoseNow(int p, int b, int h, IHordeSightSink sink)
    {
        _held[p] = false;
        Losses++;
        sink.Lost(b, h, _lastAt[p], _lastVel[p]);
    }

    private void Reset(int p)
    {
        _candidate[p] = false;
        _held[p] = false;
        _firstClear[p] = Never;
        _lastClear[p] = Never;
        _waitingSince[p] = Never;
        _fraction[p] = 0f;
        _watching[p] = false;
    }

    private static int Pair(int body, int hero) => body * HordeSlots.Heroes + hero;
}
