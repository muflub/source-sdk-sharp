namespace SourceSharp.Ai;

/// <summary>
/// A level's links as the experience keys them (lane G5e, plan_bots_teams_tf2_part_g G.5 step 7): the set a route search
/// takes, and for each of its links, index for index, the <c>link</c> row its tries are written to. The game makes the
/// keys (the class, the bins, the kind, the source and the recipe are its own); a key is the same for the same jump
/// whatever the graph's cell numbers, so a row outlives a rebuilt navigation. Immutable.
/// </summary>
public sealed class ExperienceLinkKeys
{
    private readonly LinkKey[] _keys;

    public ExperienceLinkKeys(NavLinks links, IReadOnlyList<LinkKey> keys)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count != links.Count)
            throw new ArgumentException($"{keys.Count} keys for {links.Count} links", nameof(keys));
        Links = links;
        _keys = [.. keys];
    }

    /// <summary>The links the keys are of.</summary>
    public NavLinks Links { get; }

    /// <summary>The row link <paramref name="index"/> is written to.</summary>
    public LinkKey this[int index] => _keys[index];

    public int Count => _keys.Length;
}

/// <summary>
/// Which links a snapshot of the experience leaves out (plan_bots_teams_tf2_part_g G.5 step 7): a link whose row has
/// been tried at least <see cref="MinTries"/> times and succeeded in under <see cref="MinShare"/> of them. The rows a
/// snapshot holds have decayed (by half each <c>ai_experience_half_life</c> rounds), so a shut link comes back once its
/// tries fade under <see cref="MinTries"/>, and is tried afresh. Pure.
/// </summary>
public static class LinkTrust
{
    /// <summary>The tries a link must have had before its share is believed: five, the plan's number.</summary>
    public const double MinTries = 5d;

    /// <summary>The least share of its tries a link must make: 30 %, the plan's number.</summary>
    public const double MinShare = 0.3d;

    /// <summary>Whether a link with <paramref name="stat"/> is left out.</summary>
    public static bool Shut(in LinkStat stat) => stat.Tries >= MinTries && stat.Successes < MinShare * stat.Tries;

    /// <summary>
    /// The mask <paramref name="snapshot"/>'s rows make of <paramref name="keys"/>' links: each link whose own row
    /// (its key exactly: another class's, another recipe's or another place's row is not its) is <see cref="Shut"/>.
    /// With no row for any of them the mask shuts nothing.
    /// </summary>
    public static NavLinkMask Mask(MapExperience snapshot, ExperienceLinkKeys keys)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(keys);
        var shut = new List<int>();
        for (int i = 0; i < keys.Count; i++)
        {
            if (snapshot.Links.TryGetValue(keys[i], out LinkStat stat) && Shut(stat))
                shut.Add(i);
        }

        return NavLinkMask.Of(keys.Links, shut);
    }
}
