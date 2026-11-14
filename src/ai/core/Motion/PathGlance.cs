namespace SourceSharp.Ai;

/// <summary>
/// A glance off the path while nobody is in sight (plan_hl2mp_ai §3.4, by Alertness): at
/// <c>glanceRate</c> a second the look turns 50 to 100 degrees to a side of the way to the goal
/// for a moment, as a player checks a doorway in passing. Per bot; its draws come from the
/// stream it is given: one a decide for the chance, two more for a glance begun.
/// </summary>
public sealed class PathGlance(AiRandom random)
{
    /// <summary>How long a glance is held.</summary>
    public const float HoldSeconds = 0.4f;

    /// <summary>A glance turns at least this far off the way, and up to <see cref="SpanDegrees"/> more.</summary>
    public const float LeastDegrees = 50f, SpanDegrees = 50f;

    /// <summary>How far off the point a glance looks at is.</summary>
    public const float Reach = 256f;

    private readonly AiRandom _random = random;
    private Tick _until;
    private AiVector _at;

    /// <summary>Glances begun.</summary>
    public int Taken { get; private set; }

    /// <summary>Whether a glance begun is still held at <paramref name="now"/>: the look is off the way.</summary>
    public bool Holding(Tick now) => now < _until;

    /// <summary>
    /// One decide on the way from <paramref name="self"/> to <paramref name="goal"/> with nobody in
    /// sight. True while the look is a glance, at <paramref name="at"/>: a point
    /// <see cref="Reach"/> off to a side, <paramref name="eyeHeight"/> over the bot's feet.
    /// </summary>
    public bool Step(Tick now, float tickSeconds, float glanceRate, AiVector self, AiVector goal, float eyeHeight, out AiVector at)
    {
        if (now < _until)
        {
            at = _at;
            return true;
        }

        at = default;
        if (_random.NextFloat() >= glanceRate * DecideSchedule.Period * tickSeconds)
            return false;
        float yaw = MathF.Atan2(goal.Y - self.Y, goal.X - self.X);
        float off = (LeastDegrees + SpanDegrees * _random.NextFloat()) * (MathF.PI / 180f) * (_random.NextFloat() < 0.5f ? -1f : 1f);
        _at = self + new AiVector(MathF.Cos(yaw + off) * Reach, MathF.Sin(yaw + off) * Reach, eyeHeight);
        _until = now + (int)(HoldSeconds / tickSeconds);
        Taken++;
        at = _at;
        return true;
    }
}
