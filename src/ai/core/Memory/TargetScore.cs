namespace SourceSharp.Ai;

/// <summary>
/// What an opponent is worth as the one a bot is about (plan_hl2mp_ai §5.4, §5.5): nearer is
/// worth more, the scoreboard's leader more by Headhunter's bonus, someone the bot holds a
/// grudge against more by Tenacity's weight, and the one it is already about keeps a share over
/// a rival, so a target is not dropped for one slightly better. A game adds terms of its own to
/// the bonus. Pure over the bot's memory.
/// </summary>
public static class TargetScore
{
    /// <summary>The priority the opponent the bot is already about has over a rival.</summary>
    public const float Stick = 1.25f;

    /// <summary>Kills by someone that make the grudge's whole weight.</summary>
    public const float GrudgeFull = 2f;

    /// <summary>The distance at which nearness is worth half.</summary>
    public const float ProximityHalf = 768f;

    /// <summary>How sure an opponent out of sight counts, at a confidence of 1, beside one in sight.</summary>
    public const float UnsightedShare = 0.5f;

    /// <summary>§5.4's rankBonus: rank 1 is 1, falling linearly to 0 at the bottom of the rows; no rank, nothing.</summary>
    public static float RankBonus(int rank, int rows)
    {
        if (rank <= 0 || rows <= 1)
            return rank == 1 ? 1f : 0f;
        return Math.Clamp((rows - rank) / (float)(rows - 1), 0f, 1f);
    }

    /// <summary>
    /// The rows the bonus ranks over: on a team the enemy rows only (plan_bots_teams_tf2 B.5: a
    /// leader on the bot's own team is no target), else the lowest rank the board shows.
    /// </summary>
    public static int Rows(BotMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        int rows = 0;
        Blackboard board = memory.Board;
        bool teamed = TeamRule.IsGameTeam(memory.Team);
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || (teamed && memory.IsFriend(a)))
                continue;
            if (teamed ? a.Rank > 0 : a.Rank > rows)
                rows = teamed ? rows + 1 : a.Rank;
        }

        return rows;
    }

    /// <summary>The rank the bonus reads for <paramref name="a"/>: its scoreboard rank, or on a team its rank among the enemy rows the bot knows (1 the best of them, 0 with no row).</summary>
    public static int RankOf(BotMemory memory, KnownActor a)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(a);
        if (!TeamRule.IsGameTeam(memory.Team))
            return a.Rank;
        if (a.Rank <= 0)
            return 0;
        int above = 0;
        Blackboard board = memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor o = board.Actor(s);
            if (o.Known && !memory.IsFriend(o) && o.Rank > 0 && o.Rank < a.Rank)
                above++;
        }

        return above + 1;
    }

    /// <summary>The grudge's term: <paramref name="grudgeWeight"/> at <see cref="GrudgeFull"/> kills' worth of grudge and over, less in proportion under it.</summary>
    public static float Grudge(KnownActor a, Tick now, float tickSeconds, float grudgeWeight)
    {
        ArgumentNullException.ThrowIfNull(a);
        return grudgeWeight * Math.Min(1f, a.GrudgeAt(now, tickSeconds) / GrudgeFull);
    }

    /// <summary>
    /// The priority of an opponent <paramref name="distance"/> away: nearness (discounted by
    /// <paramref name="easyTargetDiscount"/>) plus <paramref name="bonus"/> (the rank's, a game's own)
    /// plus <paramref name="grudge"/>; one out of sight counts by how sure the bot is of it; and
    /// the <paramref name="current"/> target has <see cref="Stick"/> over a rival.
    /// </summary>
    public static float Priority(bool sighted, float confidence, float distance, float easyTargetDiscount, float bonus, float grudge, bool current)
    {
        float proximity = 1f / (1f + distance / ProximityHalf);
        float priority = (sighted ? 1f : UnsightedShare * confidence) * (proximity * easyTargetDiscount + bonus + grudge);
        if (current)
            priority *= Stick;
        return priority;
    }
}

/// <summary>
/// One decide's pick among the opponents offered (§5.4): the best priority, but someone in plain
/// sight within <c>closeRange</c> outranks everyone else, the nearest of them first (user ruling
/// 2026-11-02: close, firing comes first).
/// </summary>
public struct TargetPick(float closeRange)
{
    private float _best = 0f, _closest = closeRange;
    private bool _close = false;

    /// <summary>Whether the opponent offered is the pick so far.</summary>
    public bool Takes(bool sighted, float distance, float priority)
    {
        bool closer = sighted && distance <= _closest;
        if (closer)
        {
            _closest = distance;
            _close = true;
        }

        if (closer || (!_close && priority > _best))
        {
            _best = priority;
            return true;
        }

        return false;
    }
}
