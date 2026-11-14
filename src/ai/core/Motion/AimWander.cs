namespace SourceSharp.Ai;

/// <summary>
/// The error of a held aim (plan_hl2mp_ai §3.4.1's drift): drawn when the target comes into
/// sight, wandering slowly from there, not a new draw at each decide; a person's crosshair
/// drifts, it does not jump. It is wider at first sight (a first shot is usually worse than the
/// fifth) and smaller up than across (a body is taller than it is wide, and people misjudge
/// across more than up). In units of the aimer's settled error, which the skill gives in degrees.
/// Per bot; its draws come from the stream it is given, two Gaussians a step whatever their values.
/// </summary>
public sealed class AimWander(AiRandom random)
{
    /// <summary>How fast the wander changes, seconds to fall to 1/e of its value.</summary>
    public const float WanderSeconds = 0.7f;

    /// <summary>The error at first sight, on top of its settled size: 1.5 is 2.5 times the error.</summary>
    public const float SettleExtra = 1.5f;

    /// <summary>How fast that extra error goes, seconds to fall to 1/e.</summary>
    public const float SettleSeconds = 0.6f;

    /// <summary>How smooth the wander's second filter makes it, seconds; its size stays nearly the same (a sixth smaller) at this.</summary>
    public const float SmoothSeconds = 0.25f;

    /// <summary>The wander stops at this many of its standard deviations.</summary>
    public const float Clip = 2.5f;

    /// <summary>The vertical error, as a share of the horizontal one.</summary>
    public const float UpShare = 0.6f;

    private readonly AiRandom _random = random;
    private float _x, _z;
    private ActorId _of;
    private Tick _since;
    private bool _sighted;

    /// <summary>The wander across the line to the target now, in standard deviations of the settled error.</summary>
    public float Across { get; private set; }

    /// <summary>The wander up now, in the same units, before <see cref="UpShare"/>.</summary>
    public float Up { get; private set; }

    /// <summary>The target is out of sight: back in sight, the aim starts over.</summary>
    public void Lost() => _sighted = false;

    private float Gaussian()
    {
        // Both draws every time, so the stream does not depend on the value.
        float u = Math.Max(_random.NextFloat(), 1e-6f), v = _random.NextFloat();
        return MathF.Sqrt(-2f * MathF.Log(u)) * MathF.Cos(2f * MathF.PI * v);
    }

    /// <summary>
    /// One decide's wander on <paramref name="target"/>. Answers whether the aim is new: another
    /// target than the last, or one that was out of sight.
    /// </summary>
    public bool Step(ActorId target, bool sighted, Tick now, float tickSeconds)
    {
        bool fresh = target != _of || !_sighted;
        _sighted = sighted;
        float a = Gaussian(), b = Gaussian();
        if (fresh)
        {
            _of = target;
            _since = now;
            _x = a;
            _z = b;
        }
        else
        {
            float rho = MathF.Exp(-DecideSchedule.Period * tickSeconds / WanderSeconds);
            float rest = MathF.Sqrt(1f - rho * rho);
            _x = rho * _x + rest * a;
            _z = rho * _z + rest * b;
        }

        _x = Math.Clamp(_x, -Clip, Clip);
        _z = Math.Clamp(_z, -Clip, Clip);

        // Smoothed once more, so the crosshair's drift has no 10 Hz steps in it: each
        // would turn the view back and forth (yaw reversals, ai-replay metrics).
        float keep = fresh ? 0f : MathF.Exp(-DecideSchedule.Period * tickSeconds / SmoothSeconds);
        Across = keep * Across + (1f - keep) * _x;
        Up = keep * Up + (1f - keep) * _z;
        return fresh;
    }

    /// <summary>How many times its settled size the error is at <paramref name="now"/>: <c>1 + <see cref="SettleExtra"/></c> at first sight, falling to 1.</summary>
    public float FirstSight(Tick now, float tickSeconds)
    {
        float seconds = (now - _since) * tickSeconds;
        return 1f + SettleExtra * MathF.Exp(-seconds / SettleSeconds);
    }

    /// <summary>
    /// What to add to <paramref name="point"/>, aimed at from <paramref name="from"/> and
    /// <paramref name="distance"/> away, by an aimer whose settled error is <paramref name="errorDeg"/>:
    /// across the line to it and up, wider at first sight.
    /// </summary>
    public AiVector Error(AiVector from, AiVector point, float errorDeg, float distance, Tick now, float tickSeconds)
    {
        float seconds = (now - _since) * tickSeconds;
        float size = MathF.Tan(errorDeg * (MathF.PI / 180f)) * distance * (1f + SettleExtra * MathF.Exp(-seconds / SettleSeconds));
        float err = size * Across, errUp = size * Up * UpShare;
        AiVector to = new AiVector(point.X - from.X, point.Y - from.Y, 0f).Normalised();
        return new AiVector(-to.Y * err, to.X * err, errUp);
    }
}
