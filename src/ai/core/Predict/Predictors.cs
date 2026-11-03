namespace SourceSharp.Ai;

/// <summary>What an opponent is predicted to do next (plan_hl2mp_ai §5.3.2).</summary>
public enum OpponentIntent : byte
{
    Approach = 0,
    Retreat,
    Camp,
    GoForPickup,
    Flank,
    Reloading,
}

/// <summary>The inputs of the intent classifier, all from the bot's memory.</summary>
public readonly record struct IntentInputs(float RetreatTendency, float Aggression, float CampShare, int HealthEstimate, float Distance,
                                           float PEmpty, bool ReloadingNow, bool PickupNear);

/// <summary>
/// The intent classifier (§5.3.2): a score per intent from the habits'
/// ratios, the health estimate, the distance and the ammo estimate; the top
/// one and its score feed the tree. Pure.
/// </summary>
public static class IntentClassifier
{
    public static void Score(in IntentInputs x, Span<float> scores)
    {
        float hurt = 1f - Math.Clamp(x.HealthEstimate / 100f, 0f, 1f);
        float near = 1f - Math.Clamp(x.Distance / 1500f, 0f, 1f);
        scores[(int)OpponentIntent.Approach] = x.Aggression * (1f - 0.5f * hurt) + 0.2f * near;
        scores[(int)OpponentIntent.Retreat] = x.RetreatTendency * (0.5f + hurt) + 0.3f * x.PEmpty;
        scores[(int)OpponentIntent.Camp] = x.CampShare;
        scores[(int)OpponentIntent.GoForPickup] = x.PickupNear ? 0.3f + hurt : 0.1f * hurt;
        scores[(int)OpponentIntent.Flank] = 0.5f * x.Aggression * (1f - near);
        scores[(int)OpponentIntent.Reloading] = x.ReloadingNow ? 2f : 0f;
    }

    public static (OpponentIntent Intent, float Score) Top(in IntentInputs x)
    {
        Span<float> s = stackalloc float[6];
        Score(x, s);
        int best = 0;
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] > s[best])
                best = i;
        }

        return ((OpponentIntent)best, s[best]);
    }
}

/// <summary>
/// Aim lead (§5.3.3): where to aim a projectile at a target moving at a
/// constant velocity, the analytic intercept of a shot at
/// <c>projectileSpeed</c>, scaled toward the target's present position by
/// <c>leadQuality</c> (0 no lead, 1 the full intercept). Hitscan needs none.
/// </summary>
public static class AimLead
{
    /// <summary>The time a shot from <paramref name="from"/> meets the target, or null when it never can.</summary>
    public static float? InterceptTime(AiVector from, AiVector target, AiVector velocity, float projectileSpeed)
    {
        AiVector d = target - from;
        float a = velocity.LengthSquared - projectileSpeed * projectileSpeed;
        float b = 2f * d.Dot(velocity);
        float c = d.LengthSquared;
        if (MathF.Abs(a) < 1e-6f)
            return b < 0f ? -c / b : null;
        float disc = b * b - 4f * a * c;
        if (disc < 0f)
            return null;
        float sq = MathF.Sqrt(disc);
        float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
        float t = t1 > 0f && t2 > 0f ? MathF.Min(t1, t2) : MathF.Max(t1, t2);
        return t > 0f ? t : null;
    }

    /// <summary>The point to aim at.</summary>
    public static AiVector Point(AiVector from, AiVector target, AiVector velocity, float projectileSpeed, float leadQuality)
    {
        if (projectileSpeed <= 0f)
            return target;
        float? t = InterceptTime(from, target, velocity, projectileSpeed);
        if (t is not { } time)
            return target;
        return target + velocity * (time * Math.Clamp(leadQuality, 0f, 1f));
    }
}

/// <summary>
/// The ammo and reload window (§5.3.4): <c>P(empty)</c>, <c>P(reloading
/// now)</c> (a perceived reload under way, or, not perceived, a prior from
/// the reload style when the opponent is probably empty and has gone quiet
/// for a reload's length), and the push test: the bot gets there before
/// the reload ends, by nav distance and its own speed, less a margin.
/// </summary>
public static class AmmoWindow
{
    public readonly record struct Verdict(float PEmpty, float PReloading, Tick WindowEnd, float SecondsLeft, float ArriveSeconds, bool Push);

    public static float PReloading(AmmoEstimate a, in WeaponFacts w, Tick now, float tickSeconds, float reloadStyle)
    {
        if (!a.Known)
            return 0f;
        if (a.ReloadInProgress && now < a.ReloadEndTick)
            return 1f;
        float quiet = (now - a.LastShotTick) * tickSeconds;
        if (a.ShotsSinceReload > 0 && quiet >= w.ReloadSeconds * 0.5f && quiet <= w.ReloadSeconds * 2f)
        {
            // Not perceived: a runner-dry (style near 1) who is probably empty is probably reloading.
            float style = float.IsNaN(reloadStyle) ? 0.5f : reloadStyle;
            return Math.Clamp(a.PEmpty * (0.5f + 0.5f * style), 0f, 1f);
        }

        return 0f;
    }

    /// <summary>
    /// Whether to push: <c>P(empty) + P(reloading) ≥ trust</c> and the bot
    /// arrives (<paramref name="navDistance"/> at <paramref name="runSpeed"/>)
    /// before the window ends, less <paramref name="marginSeconds"/>.
    /// </summary>
    public static Verdict Judge(AmmoEstimate a, in WeaponFacts w, Tick now, float tickSeconds, float reloadStyle, float navDistance, float runSpeed,
                                float trust, float marginSeconds)
    {
        float pEmpty = a.PEmpty;
        float pReload = PReloading(a, w, now, tickSeconds, reloadStyle);
        Tick end = a.ReloadInProgress && a.ReloadEndTick.Value != int.MaxValue
            ? a.ReloadEndTick
            : now + VisionParams.Ticks(Math.Max(w.ReloadSeconds, tickSeconds), tickSeconds);
        float left = (end - now) * tickSeconds;
        float arrive = runSpeed > 0f ? navDistance / runSpeed : float.PositiveInfinity;
        bool push = pEmpty + pReload >= trust && arrive <= left - marginSeconds;
        return new Verdict(pEmpty, pReload, end, left, arrive, push);
    }

}
