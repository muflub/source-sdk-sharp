namespace SourceSharp.Ai;

/// <summary>
/// How a bot sees (plan_hl2mp_ai §4.1): how far, how wide, and how long a
/// target must stay clear before it is recognised, and how long it may stay
/// hidden before it is lost. Lighting is not modelled (ruling 2026-11-01).
/// </summary>
public readonly record struct VisionParams(float SightRange, float FovDeg, float RecognizeSeconds, float LostSightSeconds)
{
    /// <summary>Stock HL2MP's bot range (hl2mp_bot_vision.cpp:151) and the client's own 90 degree field.</summary>
    public const float StockRange = 6000f;

    /// <summary>A player's screen: the client's <c>default_fov</c>. No bot sees wider.</summary>
    public const float MaxFovDeg = 90f;

    /// <summary>The ticks <paramref name="seconds"/> takes at <paramref name="tickSeconds"/> a tick, at least one.</summary>
    public static int Ticks(float seconds, float tickSeconds) => Math.Max(1, (int)MathF.Ceiling(seconds / tickSeconds - 1e-4f));
}

/// <summary>
/// Vision from skills (§4.1, §10): recognition by Alertness (0.5 / 0.25 / 0.12 /
/// 0.06 s at the easy, normal, hard and expert presets' Alertness 2 / 4 / 6 / 7:
/// half stock's 1.0 / 0.5 / 0.3 / 0.2, hl2mp_bot_vision.cpp:128-131, since a
/// reaction, the decide's latency and a flick all come after it here and the
/// first shot is what the aim's targets name, §12.3); the range is stock's, the
/// field the client's.
/// </summary>
public static class VisionCurves
{
    private static ReadOnlySpan<float> RecognizeByAlertness => [0.72f, 0.6f, 0.5f, 0.37f, 0.25f, 0.18f, 0.12f, 0.06f, 0.05f, 0.04f, 0.04f];

    /// <summary>How long an unseen target stays held before <c>SightLost</c>.</summary>
    public const float LostSightSeconds = 0.5f;

    /// <summary>The recognition at an Alertness between its levels (§5.5's streaks).</summary>
    public static float RecognizeAt(float alertness)
    {
        float l = Math.Clamp(alertness, 0f, StandardSkills.Cap);
        int below = (int)MathF.Floor(l), above = Math.Min(below + 1, StandardSkills.Cap);
        return RecognizeByAlertness[below] + (RecognizeByAlertness[above] - RecognizeByAlertness[below]) * (l - below);
    }

    /// <summary>A bot's vision, with what a streak makes of its recognition; 0 for none (§5.5).</summary>
    public static VisionParams For(SkillProfile profile, float streakRecognizeSeconds)
    {
        VisionParams own = For(profile);
        return streakRecognizeSeconds > 0f ? own with { RecognizeSeconds = streakRecognizeSeconds } : own;
    }

    public static VisionParams For(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int i = profile.IndexOf("alertness");
        int alertness = i < 0 ? 0 : Math.Clamp(profile.Levels[i], 0, StandardSkills.Cap);
        return new VisionParams(VisionParams.StockRange, VisionParams.MaxFovDeg, RecognizeByAlertness[alertness], LostSightSeconds);
    }
}

/// <summary>Where a candidate stopped in the vision pass this tick (§4.1, the inspector's perception colours, §7.1).</summary>
public enum SightStage : byte
{
    /// <summary>Not a candidate this tick (dead, gone, the bot itself).</summary>
    None = 0,

    /// <summary>Farther than the bot's sight range.</summary>
    Range,

    /// <summary>Outside the bot's field of view.</summary>
    Cone,

    /// <summary>The engine would not send it to this client (PVS).</summary>
    Pvs,

    /// <summary>Every sample point's line is blocked.</summary>
    Trace,

    /// <summary>Passed range, cone and PVS, but the trace budget has never served the pair.</summary>
    Unserved,

    /// <summary>At least one sample point's line is clear.</summary>
    Clear,
}

/// <summary>What one tick's observation changed.</summary>
public enum SightChange : byte
{
    None = 0,
    Gained,
    Lost,
}

/// <summary>
/// One bot's sight of every other actor (§4.1 step 5, recognition): a target
/// becomes <c>SightGained</c> only after <see cref="VisionParams.RecognizeSeconds"/>
/// of continuous clear answers, and <c>SightLost</c> after
/// <see cref="VisionParams.LostSightSeconds"/> of failures. Fixed size, by
/// target slot; pure, so the facts drive it tick by tick.
/// </summary>
public sealed class SightTracker
{
    private readonly Pair[] _pairs;

    public SightTracker(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);
        _pairs = new Pair[slots];
    }

    public int Slots => _pairs.Length;

    /// <summary>
    /// Feeds one tick's outcome for <paramref name="target"/>: the stage it
    /// reached and, at <see cref="SightStage.Clear"/>, the share of its sample
    /// points whose line is clear.
    /// </summary>
    public SightChange Observe(ActorId target, SightStage stage, float clearFraction, in VisionParams p, float tickSeconds)
    {
        ref Pair pair = ref _pairs[target.Slot];
        SightChange change = SightChange.None;
        if (pair.Target != target)
        {
            // Another life in the slot: whatever was held of the last one is lost.
            change = pair.Recognised ? SightChange.Lost : SightChange.None;
            pair = new Pair { Target = target };
        }

        pair.Stage = stage;
        bool clear = stage == SightStage.Clear && clearFraction > 0f;
        pair.Fraction = clear ? clearFraction : 0f;
        if (clear)
        {
            pair.ClearTicks++;
            pair.FailTicks = 0;
            if (!pair.Recognised && pair.ClearTicks >= VisionParams.Ticks(p.RecognizeSeconds, tickSeconds))
            {
                pair.Recognised = true;
                return SightChange.Gained;
            }

            return change;
        }

        pair.ClearTicks = 0;
        if (pair.Recognised && ++pair.FailTicks >= VisionParams.Ticks(p.LostSightSeconds, tickSeconds))
        {
            pair.Recognised = false;
            pair.FailTicks = 0;
            return SightChange.Lost;
        }

        return change;
    }

    /// <summary>
    /// Forgets <paramref name="target"/>'s slot (it died, left, or the bot
    /// did); <see cref="SightChange.Lost"/> when it was held.
    /// </summary>
    public SightChange Forget(int slot)
    {
        bool held = _pairs[slot].Recognised;
        _pairs[slot] = default;
        return held ? SightChange.Lost : SightChange.None;
    }

    /// <summary>Whether the bot holds sight of the slot's actor (recognised, not yet lost).</summary>
    public bool Recognised(int slot) => _pairs[slot].Recognised;

    /// <summary>Recognised and clear this tick: what the bot's view shows (§4.3).</summary>
    public bool Sees(int slot) => _pairs[slot].Recognised && _pairs[slot].Fraction > 0f;

    public ActorId TargetOf(int slot) => _pairs[slot].Target;

    public SightStage StageOf(int slot) => _pairs[slot].Stage;

    /// <summary>The share of the target's sample points clear this tick (§4.1: partial occlusion).</summary>
    public float FractionOf(int slot) => _pairs[slot].Fraction;

    private struct Pair
    {
        public ActorId Target;
        public SightStage Stage;
        public float Fraction;
        public int ClearTicks;
        public int FailTicks;
        public bool Recognised;
    }
}

/// <summary>
/// The trace budget (§4.1 step 4): a fixed number of line traces a tick,
/// served round-robin over the pairs that passed range, cone and PVS, a
/// whole pair at a time (all its sample points). The cursor is the last
/// served pair's key, so the next tick starts after it whatever came and went.
/// </summary>
public sealed class TraceBudget
{
    private long _last = -1;

    /// <summary>
    /// Marks in <paramref name="served"/> which of <paramref name="keys"/>
    /// (ascending, one per pair) get their <paramref name="costs"/> in traces
    /// this tick within <paramref name="budget"/>; answers how many traces.
    /// </summary>
    public int Select(ReadOnlySpan<long> keys, ReadOnlySpan<int> costs, int budget, Span<bool> served)
    {
        served[..keys.Length].Clear();
        if (keys.IsEmpty || budget <= 0)
            return 0;

        int start = 0;
        while (start < keys.Length && keys[start] <= _last)
            start++;

        int spent = 0;
        for (int k = 0; k < keys.Length; k++)
        {
            int i = (start + k) % keys.Length;
            if (spent + costs[i] > budget)
                break;
            served[i] = true;
            spent += costs[i];
            _last = keys[i];
        }

        return spent;
    }

    /// <summary>Starts over from the first pair.</summary>
    public void Reset() => _last = -1;
}

/// <summary>The geometry of the first two stages (§4.1).</summary>
public static class VisionMath
{
    /// <summary>The unit forward of view angles (degrees; pitch down is positive, as a usercmd's).</summary>
    public static AiVector Forward(AiAngles a)
    {
        float p = a.Pitch * (MathF.PI / 180f), y = a.Yaw * (MathF.PI / 180f);
        float cp = MathF.Cos(p);
        return new AiVector(cp * MathF.Cos(y), cp * MathF.Sin(y), -MathF.Sin(p));
    }

    /// <summary>Whether <paramref name="point"/> lies within half of <paramref name="fovDeg"/> of the eye's forward.</summary>
    public static bool InCone(AiVector eye, AiVector forward, AiVector point, float fovDeg)
    {
        AiVector to = point - eye;
        float len = to.Length;
        if (len <= 0f)
            return true;
        float cos = forward.Dot(to) / len;
        return cos >= MathF.Cos(fovDeg * 0.5f * (MathF.PI / 180f));
    }
}
