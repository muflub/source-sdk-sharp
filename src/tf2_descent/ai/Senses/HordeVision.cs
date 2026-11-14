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
/// recognised and stay hidden to be lost, in ticks. Not present: no brain.
/// </summary>
public readonly record struct BodyEye(bool Present, AiVector Eye, float Yaw, float Range, float CosHalfCone, int RecognizeTicks, int LostTicks);

/// <summary>A hero as the vision pass looks at it: its eye and chest (the two sample points), feet (what is told) and velocity.</summary>
public readonly record struct HeroTarget(bool Present, AiVector Eye, AiVector Chest, AiVector Feet, AiVector Velocity);

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
/// Cloak, disguise and darkness are multipliers on a hero's sight range that
/// the caller can fold into <see cref="BodyEye.Range"/> per pair later: there is
/// no such multiplier yet. Allocates nothing after it is made.
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
    private int _cursor;

    public HordeVision(int budget, int bodies, bool[] candidate, bool[] held, long[] firstClear, long[] lastClear, long[] waitingSince,
                       AiVector[] lastAt, AiVector[] lastVel, float[] fraction)
    {
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
                               new AiVector[pairs], new AiVector[pairs], new float[pairs]);
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

    /// <summary>Forgets a body's pairs without telling anyone: its brain is gone (returned, re-lent, dead).</summary>
    public void ForgetBody(int body)
    {
        for (int h = 0; h < HordeSlots.Heroes; h++)
            Reset(Pair(body, h));
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
            if (fraction > 0f)
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

    private static bool Passes(in BodyEye e, in HeroTarget t, int hero, IHordeLines lines)
    {
        AiVector to = t.Eye - e.Eye;
        if (to.Length > e.Range)
            return false;

        float flat = to.Length2D;
        if (flat > 1f)
        {
            float yaw = e.Yaw * (MathF.PI / 180f);
            float dot = (MathF.Cos(yaw) * to.X + MathF.Sin(yaw) * to.Y) / flat;
            if (dot < e.CosHalfCone)
                return false;
        }

        // The robot's box from the hero's eye: the PVS is symmetric enough for a gate.
        var half = new AiVector(16f, 16f, 0f);
        return lines.InPvs(hero, e.Eye - half - new AiVector(0f, 0f, 64f), e.Eye + half + new AiVector(0f, 0f, 8f));
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
    }

    private static int Pair(int body, int hero) => body * HordeSlots.Heroes + hero;
}
