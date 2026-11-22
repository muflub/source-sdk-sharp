using SourceSharp.Ai.TF2.Knowledge;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>What a team is doing in the mode, for the classes it wants (plan_bots_teams_tf2 C.4): attacking, defending, or both (CTF, 5CP).</summary>
public enum TFTeamStance : byte
{
    Symmetric = 0,
    Attack,
    Defend,
}

/// <summary>
/// A team's roster target and the class a bot picks or changes to (plan_bots_teams_tf2 C.4, P7 step 4): our own
/// weights per stance (stock's offense/defense vocabulary, not its tables), as shares of the team. A bot picks the
/// ready class furthest below its share (its count against share x team size, the bot itself counted), with a Medic
/// once the team has four and an Engineer on defence; a class not ready (its file's flag) is never fielded. A change
/// is worth it only when the best class's need beats the bot's own class's by <see cref="StayBonus"/>. Pure: the same
/// counts give the same answer, ties to the lowest class number.
/// </summary>
public static class TFClassComposition
{
    /// <summary>How much more a class must be needed than the bot's own for a change: a whole player.</summary>
    public const float StayBonus = 1f;

    /// <summary>A team this big (the bot counted) wants a Medic first, and on defence an Engineer.</summary>
    public const int MedicFrom = 4, EngineerFrom = 3;

    /// <summary>The need a missing must-have class (the Medic from four, the Engineer on defence) is given: above any share's.</summary>
    public const float MustHave = 2f;

    /// <summary>The share of a team <paramref name="playerClass"/> is wanted as, by <paramref name="stance"/>.</summary>
    public static float Share(TFClass playerClass, TFTeamStance stance) => (stance, playerClass) switch
    {
        (TFTeamStance.Attack, TFClass.Scout) => 0.14f,
        (TFTeamStance.Attack, TFClass.Soldier) => 0.20f,
        (TFTeamStance.Attack, TFClass.Pyro) => 0.10f,
        (TFTeamStance.Attack, TFClass.Demoman) => 0.16f,
        (TFTeamStance.Attack, TFClass.HeavyWeapons) => 0.12f,
        (TFTeamStance.Attack, TFClass.Engineer) => 0.04f,
        (TFTeamStance.Attack, TFClass.Medic) => 0.14f,
        (TFTeamStance.Attack, TFClass.Sniper) => 0.04f,
        (TFTeamStance.Attack, TFClass.Spy) => 0.06f,
        (TFTeamStance.Defend, TFClass.Scout) => 0.06f,
        (TFTeamStance.Defend, TFClass.Soldier) => 0.16f,
        (TFTeamStance.Defend, TFClass.Pyro) => 0.12f,
        (TFTeamStance.Defend, TFClass.Demoman) => 0.16f,
        (TFTeamStance.Defend, TFClass.HeavyWeapons) => 0.14f,
        (TFTeamStance.Defend, TFClass.Engineer) => 0.14f,
        (TFTeamStance.Defend, TFClass.Medic) => 0.10f,
        (TFTeamStance.Defend, TFClass.Sniper) => 0.08f,
        (TFTeamStance.Defend, TFClass.Spy) => 0.04f,
        (_, TFClass.Scout) => 0.14f,
        (_, TFClass.Soldier) => 0.18f,
        (_, TFClass.Pyro) => 0.10f,
        (_, TFClass.Demoman) => 0.14f,
        (_, TFClass.HeavyWeapons) => 0.12f,
        (_, TFClass.Engineer) => 0.08f,
        (_, TFClass.Medic) => 0.12f,
        (_, TFClass.Sniper) => 0.06f,
        (_, TFClass.Spy) => 0.06f,
        _ => 0f,
    };

    /// <summary>
    /// How much the team needs one more <paramref name="playerClass"/>: its share of a team of <paramref name="size"/> (the
    /// bot counted) less the <paramref name="have"/> it has without the bot; <see cref="MustHave"/> for a missing Medic or
    /// defence's Engineer.
    /// </summary>
    public static float Need(TFClass playerClass, int have, int size, TFTeamStance stance)
    {
        if (have == 0 && ((playerClass == TFClass.Medic && size >= MedicFrom) || (playerClass == TFClass.Engineer && stance == TFTeamStance.Defend && size >= EngineerFrom)))
            return MustHave;
        return Share(playerClass, stance) * size - have;
    }

    /// <summary>
    /// The ready class the team needs most (<paramref name="counts"/>: the team's classes by number, the bot's own left
    /// out), and its need; Soldier with no ready class at all.
    /// </summary>
    public static (TFClass Class, float Need) Best(ReadOnlySpan<int> counts, TFTeamStance stance, Func<TFClass, bool> ready)
    {
        int size = 1;
        foreach (int n in counts)
            size += n;
        (TFClass Class, float Need) best = (TFClass.Undefined, float.NegativeInfinity);
        foreach (TFClass c in TFKnowledge.Playable())
        {
            if (!ready(c))
                continue;
            float need = Need(c, (int)c < counts.Length ? counts[(int)c] : 0, size, stance);
            if (need > best.Need)
                best = (c, need);
        }

        return best.Class == TFClass.Undefined ? (TFClass.Soldier, 0f) : best;
    }

    /// <summary>The class a bot with none picks: the ready class its team needs most.</summary>
    public static TFClass Pick(ReadOnlySpan<int> counts, TFTeamStance stance, Func<TFClass, bool> ready) => Best(counts, stance, ready).Class;

    /// <summary>
    /// What a dead bot playing <paramref name="own"/> gains by changing: the best ready class's need over its own class's
    /// less <see cref="StayBonus"/>; a gain over 0 is worth a change, and the bigger gain wins a shared window.
    /// </summary>
    public static (TFClass Class, float Gain) Change(TFClass own, ReadOnlySpan<int> counts, TFTeamStance stance, Func<TFClass, bool> ready)
    {
        (TFClass best, float need) = Best(counts, stance, ready);
        if (best == own)
            return (own, 0f);
        int size = 1;
        foreach (int n in counts)
            size += n;
        float mine = ready(own) ? Need(own, (int)own < counts.Length ? counts[(int)own] : 0, size, stance) : float.NegativeInfinity;
        return (best, need - mine - StayBonus);
    }
}
