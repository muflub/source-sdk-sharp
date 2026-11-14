namespace SourceSharp.Ai;

/// <summary>
/// The noise on a tree's scores (plan_bots_teams_tf2_part_g G.2.2): a factor about 1 for each
/// score of each bot, which wanders slowly, so two bots of the same skills given the same view do
/// not always rank their branches alike, and one bot does not flip between two branches from
/// decide to decide. It is a function of the bot's seed, the score's number and the tick, and
/// keeps nothing: a replay of the same ticks reads the same factors, whichever decides it runs.
/// </summary>
public static class ScoreNoise
{
    /// <summary>How long a score's noise takes to go from one draw to the next.</summary>
    public const float PeriodSeconds = 12f;

    /// <summary>
    /// The factor on score <paramref name="score"/> of the bot seeded <paramref name="seed"/> at
    /// <paramref name="now"/>: within <paramref name="amplitude"/> of 1, eased from one draw to the
    /// next over <see cref="PeriodSeconds"/>, each score on a phase of its own. Exactly 1 at no amplitude.
    /// </summary>
    public static float Factor(ulong seed, int score, Tick now, float tickSeconds, float amplitude)
    {
        if (amplitude <= 0f || tickSeconds <= 0f)
            return 1f;
        ulong own = AiRandom.SeedFor(seed, score);
        long period = Math.Max(1L, (long)(PeriodSeconds / tickSeconds));
        long at = now.Value + (long)(own % (ulong)period);
        long epoch = Math.DivRem(at, period, out long into);
        float t = into / (float)period;
        float eased = t * t * (3f - 2f * t);
        float from = Draw(own, epoch), to = Draw(own, epoch + 1);
        return 1f + amplitude * (from + (to - from) * eased);
    }

    /// <summary>The draw of one period: in [-1, 1).</summary>
    private static float Draw(ulong own, long epoch) => (AiRandom.SeedFor(own, (int)epoch) >> 40) * (2f / (1 << 24)) - 1f;
}
