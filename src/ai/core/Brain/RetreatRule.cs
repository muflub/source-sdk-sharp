namespace SourceSharp.Ai;

/// <summary>
/// When a hurt bot gives a fight up (plan_hl2mp_ai §5.4, §10): under threat, with less than the
/// share of its health its skills say. Nerve's <c>retreatHealthPct</c> is the share; Tenacity's
/// <c>giveUpHealth</c> raises it for a bot that does not see things through. Pure.
/// </summary>
public static class RetreatRule
{
    /// <summary>How long after being hurt a bot is still under threat with nobody in sight.</summary>
    public const float ThreatSeconds = 3f;

    /// <summary>Whether the bot is under threat: an opponent in sight, or hurt within <see cref="ThreatSeconds"/>.</summary>
    public static bool Threatened(bool opponentSighted, Tick now, Tick lastDamage, float tickSeconds)
        => opponentSighted || (now - lastDamage) * tickSeconds < ThreatSeconds;

    /// <summary>The share of its health under which the bot gives up: the higher of Nerve's and Tenacity's.</summary>
    public static float HealthShare(in DecisionParams p) => Math.Max(p.RetreatHealthPct, p.GiveUpHealth);

    /// <summary>Whether a bot with <paramref name="healthShare"/> of its health left retreats now.</summary>
    public static bool Retreats(float healthShare, in DecisionParams p, bool threatened) => healthShare < HealthShare(in p) && threatened;
}
