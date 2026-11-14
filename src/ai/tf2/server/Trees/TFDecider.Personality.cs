namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// Personality in the root's scores (plan_bots_teams_tf2_part_g G.2.2): the tree scores each
/// branch as before, and what the bot makes of that score is its own. A score becomes its base
/// times what the bot's trait leans it by times a small noise of the bot's own
/// (<see cref="ScoreNoise"/>): Aggression on the fight and on going for the enemy's flag, Tenacity
/// on chasing the enemy who carries the team's, Discipline on defending it and on guarding it
/// dropped, Teamwork on going with the carrier, Nerve (falling) on leaving for health. The
/// numbers are the skill curves' (<see cref="SkillCurves.Drives"/>, tf2_skills.json); curves
/// without them weigh nothing, and a score is then exactly what the tree gave.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>
    /// The most a weighed score may be: under <see cref="DeliverUtility"/>'s 1, so no leaning
    /// puts anything before taking home the flag in the bot's hands.
    /// </summary>
    public const float ScoreCeiling = 0.98f;

    private ulong _scoreSeed;

    /// <summary>
    /// What the bot makes of <paramref name="score"/>, the root's score of <paramref name="branch"/>
    /// (one of the root's names) this decide. Nothing stays nothing, and delivering the flag is
    /// not weighed at all.
    /// </summary>
    internal float Weigh(string branch, float score)
    {
        if (!(score > 0f) || branch == Deliver)
            return score;
        (float drive, int number) = Lean(branch);
        return MathF.Min(score * drive * ScoreNoise.Factor(_scoreSeed, number, _now, _tick, _p.ScoreNoise), ScoreCeiling);
    }

    /// <summary>The trait's factor on a branch, and the branch's number for its noise.</summary>
    private (float Drive, int Number) Lean(string branch) => branch switch
    {
        Survive => (_p.SurviveDrive, 1),
        Fight => (_p.FightDrive, 2),
        ChaseCarrier => (_p.ChaseDrive, 3),
        Guard => (_p.HoldDrive, 4),
        Escort => (_p.EscortDrive, 5),
        Fetch => (_p.FightDrive, 6),
        Defend => (_p.HoldDrive, 7),
        Search => (1f, 8),
        Roam => (1f, 9),
        _ => (1f, 0),
    };
}
