namespace TF2.Descent.Rules;

/// <summary>
/// Which depths the town may send a party to (task C5, plan_descent_mine_levels M10,
/// section 8). The cage serves levels 1 to 5, each once the party has reached it
/// (level 1 always); killing the level 5 boss opens the camp at level 6, and the
/// level 10 boss the camp at 11. The levels between camps are walked down on each
/// run, so nothing else is offered. This narrows R6 (any level reached).
/// </summary>
public static class TownDescents
{
    /// <summary>The deepest level the town's cage serves.</summary>
    public const int CageDepths = 5;

    /// <summary>The boss depths and the camp each kill opens.</summary>
    public const int FirstBoss = 5, FirstCamp = 6, SecondBoss = 10, SecondCamp = 11, LastBoss = 15;

    /// <summary>The depths the town may send a party to, shallowest first.</summary>
    /// <param name="highestDepthReached">The deepest level the party has reached; 0 for one that has never gone down.</param>
    /// <param name="bossDepthsKilled">The boss depths (5, 10, 15) whose boss the party has killed.</param>
    public static IReadOnlyList<int> Destinations(int highestDepthReached, IEnumerable<int> bossDepthsKilled)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(highestDepthReached);
        var kills = bossDepthsKilled.ToHashSet();
        foreach (var depth in kills)
            if (depth is not (FirstBoss or SecondBoss or LastBoss))
                throw new ArgumentException($"depth {depth} has no boss (the bosses are at {FirstBoss}, {SecondBoss} and {LastBoss})", nameof(bossDepthsKilled));

        var depths = Enumerable.Range(1, Math.Clamp(highestDepthReached, 1, CageDepths)).ToList();
        if (kills.Contains(FirstBoss)) depths.Add(FirstCamp);
        if (kills.Contains(SecondBoss)) depths.Add(SecondCamp);
        return depths;
    }
}
