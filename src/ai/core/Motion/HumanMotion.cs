namespace SourceSharp.Ai;

/// <summary>
/// The numbers a bot's view moves by (plan_hl2mp_ai §3.4.1), from its skill
/// curves (<see cref="MotionCurves"/>). Every one is clamped to the human
/// envelope (<see cref="MotionEnvelope"/>) before the motion uses it.
/// </summary>
/// <param name="ReactionMs">Before any aim starts on a new stimulus.</param>
/// <param name="TurnPeakDegPerSec">The fastest the view turns in one tick, per second.</param>
/// <param name="FlickOvershootPct">How far past (or short of) the target a flick lands, at most, in percent of its length.</param>
/// <param name="TrackingLagMs">How far behind a moving target the pursuit runs.</param>
public readonly record struct MotionParams(
    float ReactionMs,
    float TurnPeakDegPerSec,
    float FlickOvershootPct,
    float TrackingLagMs);

/// <summary>
/// The human envelope (ruling 2026-11-01: "as fast as a human would, ± by
/// skill"; §3.3): a turn's peak speed is 300–900°/s, easy to expert, whatever
/// a curve says, and the other numbers stay where a person's are.
/// </summary>
public static class MotionEnvelope
{
    public const float MinPeakDegPerSec = 300f;
    public const float MaxPeakDegPerSec = 900f;
    public const float MinReactionMs = 100f;
    public const float MaxReactionMs = 1500f;
    public const float MaxOvershootPct = 40f;
    public const float MinTrackingLagMs = 30f;
    public const float MaxTrackingLagMs = 400f;

    /// <summary><paramref name="p"/> with every number inside the envelope.</summary>
    public static MotionParams Clamp(MotionParams p) => new(
        Math.Clamp(p.ReactionMs, MinReactionMs, MaxReactionMs),
        Math.Clamp(p.TurnPeakDegPerSec, MinPeakDegPerSec, MaxPeakDegPerSec),
        Math.Clamp(p.FlickOvershootPct, 0f, MaxOvershootPct),
        Math.Clamp(p.TrackingLagMs, MinTrackingLagMs, MaxTrackingLagMs));
}

/// <summary>What the view is doing (§3.4.1).</summary>
public enum AimPhase : byte
{
    /// <summary>On target, or following a look that needs no reaction.</summary>
    Pursuit = 0,

    /// <summary>A new stimulus, not yet acted on.</summary>
    Reacting,

    /// <summary>The ballistic move toward a new target.</summary>
    Flick,

    /// <summary>The slower move that takes out a flick's over- or undershoot.</summary>
    Correct,
}

/// <summary>
/// A bot's view, moved as a person moves theirs (plan_hl2mp_ai §3.4.1; the
/// first skeleton, Phase 3 step 4: the reaction delay, the envelope, the
/// flick with its correction and the pursuit; drift, noise, recoil and the
/// rest are Phase 5b). Pure and per bot: the same parameters, seed and inputs
/// give the same angles.
///
/// <para>
/// A NEW STIMULUS (a different stimulus id) waits <see cref="MotionParams.ReactionMs"/>,
/// then flicks: a minimum-jerk move whose duration follows Fitts' law,
/// <c>t = a + b·log2(1 + D/W)</c>, stretched where needed so its peak speed
/// stays under the cap; it lands past or short of the target by a signed
/// random share of <see cref="MotionParams.FlickOvershootPct"/>, and a
/// correction at no more than a third of the peak takes the rest out. The
/// SAME stimulus is pursued with a lag of <see cref="MotionParams.TrackingLagMs"/>.
/// Stimulus 0 is a look with nothing to react to (along a path): pursued from
/// the start. No tick turns the view further than the peak allows.
/// </para>
/// </summary>
public sealed class HumanMotion
{
    /// <summary>Fitts' intercept and slope for a flick, in seconds.</summary>
    public const float FittsA = 0.08f, FittsB = 0.06f;

    /// <summary>The target's angular size W in Fitts' law, degrees (a body at mid range).</summary>
    public const float TargetWidthDeg = 4f;

    /// <summary>A correction ends within this many degrees of the target.</summary>
    public const float SettleDeg = 0.25f;

    private readonly AiRandom _random;

    private int _stimulus;
    private float _timer;
    private AiAngles _flickFrom;
    private float _flickPitch, _flickYaw;
    private float _flickSeconds;

    public HumanMotion(MotionParams parameters, AiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Parameters = MotionEnvelope.Clamp(parameters);
        _random = random;
    }

    /// <summary>The parameters in use: the ones given, inside the envelope.</summary>
    public MotionParams Parameters { get; }

    /// <summary>The view now.</summary>
    public AiAngles View { get; private set; }

    public AimPhase Phase { get; private set; }

    /// <summary>The overshoot drawn for the flick under way, as a signed fraction of its length.</summary>
    public float FlickOvershoot { get; private set; }

    /// <summary>The most the view may turn in one step of <paramref name="dt"/> seconds.</summary>
    public float MaxStep(float dt) => Parameters.TurnPeakDegPerSec * dt;

    /// <summary>The view is <paramref name="view"/> (a spawn, or the server's own snap); nothing is under way.</summary>
    public void Reset(AiAngles view)
    {
        View = view;
        Phase = AimPhase.Pursuit;
        _stimulus = 0;
        _timer = 0f;
    }

    /// <summary>
    /// One step of <paramref name="dt"/> seconds toward <paramref name="target"/>
    /// (pitch and yaw; roll is left alone) for <paramref name="stimulus"/>.
    /// Answers the new view.
    /// </summary>
    public AiAngles Step(AiAngles target, int stimulus, float dt)
    {
        if (stimulus != _stimulus)
        {
            _stimulus = stimulus;
            if (stimulus != 0)
            {
                Phase = AimPhase.Reacting;
                _timer = Parameters.ReactionMs / 1000f;
            }
            else
            {
                Phase = AimPhase.Pursuit;
            }
        }

        switch (Phase)
        {
            case AimPhase.Reacting:
                _timer -= dt;
                if (_timer <= 0f)
                    StartFlick(target);
                break;

            case AimPhase.Flick:
                FlickStep(dt);
                break;

            case AimPhase.Correct:
                if (Toward(target, Parameters.TurnPeakDegPerSec / 3f * dt) <= SettleDeg)
                    Phase = AimPhase.Pursuit;
                break;

            default:
                PursuitStep(target, dt);
                break;
        }

        return View;
    }

    private void StartFlick(AiAngles target)
    {
        float dPitch = target.Pitch - View.Pitch;
        float dYaw = AngleDelta(View.Yaw, target.Yaw);
        float distance = MathF.Sqrt(dPitch * dPitch + dYaw * dYaw);

        // Signed: past the target or short of it, at most the skill's share.
        float pct = Parameters.FlickOvershootPct / 100f;
        FlickOvershoot = pct * (2f * _random.NextFloat() - 1f);
        float scale = 1f + FlickOvershoot;

        _flickFrom = View;
        _flickPitch = dPitch * scale;
        _flickYaw = dYaw * scale;

        // Fitts, and long enough that the minimum-jerk peak (1.875 x the mean
        // speed) stays under the cap.
        float length = distance * scale;
        float fitts = FittsA + FittsB * MathF.Log2(1f + distance / TargetWidthDeg);
        _flickSeconds = MathF.Max(fitts, 1.875f * length / Parameters.TurnPeakDegPerSec);
        _timer = 0f;
        Phase = AimPhase.Flick;
    }

    private void FlickStep(float dt)
    {
        _timer += dt;
        float tau = MathF.Min(1f, _timer / _flickSeconds);
        float s = tau * tau * tau * (10f + tau * (-15f + 6f * tau));
        var planned = new AiAngles(_flickFrom.Pitch + _flickPitch * s, Wrap(_flickFrom.Yaw + _flickYaw * s), View.Roll);
        Toward(planned, MaxStep(dt));
        if (tau >= 1f)
            Phase = AimPhase.Correct;
    }

    private void PursuitStep(AiAngles target, float dt)
    {
        float lag = Parameters.TrackingLagMs / 1000f;
        float alpha = dt / (lag + dt);
        float dPitch = (target.Pitch - View.Pitch) * alpha;
        float dYaw = AngleDelta(View.Yaw, target.Yaw) * alpha;
        Move(dPitch, dYaw, MaxStep(dt));
    }

    /// <summary>Moves the view toward <paramref name="target"/> by at most <paramref name="maxStep"/>; answers the distance left.</summary>
    private float Toward(AiAngles target, float maxStep)
    {
        float dPitch = target.Pitch - View.Pitch;
        float dYaw = AngleDelta(View.Yaw, target.Yaw);
        Move(dPitch, dYaw, maxStep);
        float rp = target.Pitch - View.Pitch, ry = AngleDelta(View.Yaw, target.Yaw);
        return MathF.Sqrt(rp * rp + ry * ry);
    }

    // THE CAP: whatever asked for the move, one step turns at most maxStep.
    private void Move(float dPitch, float dYaw, float maxStep)
    {
        float length = MathF.Sqrt(dPitch * dPitch + dYaw * dYaw);
        if (length > maxStep && length > 0f)
        {
            float k = maxStep / length;
            dPitch *= k;
            dYaw *= k;
        }

        View = new AiAngles(Math.Clamp(View.Pitch + dPitch, -89f, 89f), Wrap(View.Yaw + dYaw), View.Roll);
    }

    /// <summary>The signed turn from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
    public static float AngleDelta(float from, float to)
    {
        float d = (to - from) % 360f;
        if (d > 180f)
            d -= 360f;
        else if (d <= -180f)
            d += 360f;
        return d;
    }

    /// <summary>A yaw in (-180, 180].</summary>
    public static float Wrap(float yaw) => AngleDelta(0f, yaw);
}

/// <summary>
/// The motion's skill curves (§10), the first cut: the turn peak follows the
/// profile's overall level (300°/s at the easy preset's 20 points, 900°/s at
/// the whole pool), the reaction follows Alertness through stock's
/// recognition times (1.0/0.5/0.3/0.2 s at levels 2/4/6/7,
/// hl2mp_bot_vision.cpp:122-151), and the overshoot and tracking lag fall
/// with Accuracy. Phase 5b replaces these with the JSON curves.
/// </summary>
public static class MotionCurves
{
    private static ReadOnlySpan<float> ReactionByAlertness => [1400f, 1200f, 1000f, 750f, 500f, 400f, 300f, 200f, 170f, 140f, 120f];

    public static MotionParams For(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int alertness = Level(profile, "alertness");
        int accuracy = Level(profile, "accuracy");
        float overall = Math.Clamp((profile.Spent - 20f) / (StandardSkills.Pool - 20f), 0f, 1f);
        return new MotionParams(
            ReactionByAlertness[alertness],
            MotionEnvelope.MinPeakDegPerSec + overall * (MotionEnvelope.MaxPeakDegPerSec - MotionEnvelope.MinPeakDegPerSec),
            25f - 2.2f * accuracy,
            250f - 19f * accuracy);
    }

    private static int Level(SkillProfile profile, string key)
    {
        int i = profile.IndexOf(key);
        return i < 0 ? 0 : Math.Clamp(profile.Levels[i], 0, StandardSkills.Cap);
    }
}
