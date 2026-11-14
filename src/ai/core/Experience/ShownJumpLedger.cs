namespace SourceSharp.Ai;

/// <summary>
/// A jump players showed as a snapshot holds it (lane G6a): its count row (<see cref="ShownJumpRecipe.CountRecipe"/>:
/// how often it was seen, the seconds in the air and the health it cost, as sums) and, when the snapshot has one, the
/// numbers and commands of one sighting of it (the exemplar row's recipe, decoded).
/// </summary>
public readonly record struct ExperienceShownJump(LinkKey Key, LinkStat Stat, ShownJumpRecipe? Exemplar)
{
    /// <summary>How it was made.</summary>
    public ShownJumpKind Kind => (ShownJumpKind)Key.Kind;
}

/// <summary>
/// The rows a learned jump adds to the store (plan_bots_teams_tf2_part_g G.6, lane G6a), and which jumps already have
/// their exemplar: a learned jump is a try and a success of its count row, with its seconds in the air and its health
/// cost, and the first sighting of it while the store holds none is its exemplar row, whose counts are zero (every count
/// is the count row's). Pure; one thread at a time (the experience worker).
/// </summary>
public sealed class ShownJumpLedger
{
    private readonly HashSet<(int Class, WorldBin From, WorldBin To, int Kind)> _exemplars = new();

    /// <summary>The jumps with an exemplar, in the snapshot or handed since.</summary>
    public int Exemplars => _exemplars.Count;

    /// <summary>The exemplars <paramref name="snapshot"/> holds are known: none is written again for them.</summary>
    public void Seed(MapExperience snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (LinkKey k in snapshot.Links.Keys)
        {
            if (IsExemplar(k))
                _exemplars.Add((k.Class, k.From, k.To, k.Kind));
        }
    }

    /// <summary>Another map or mode: nothing is known.</summary>
    public void Clear() => _exemplars.Clear();

    /// <summary>
    /// The rows <paramref name="jump"/>, made by a player of <paramref name="cls"/> with commands of
    /// <paramref name="tickSeconds"/>, adds: its count row, and its exemplar when none is known (which it then is).
    /// </summary>
    public void Rows(ShownJumpRecipe jump, int cls, double tickSeconds, List<(LinkKey Key, LinkStat Stat)> rows)
    {
        ArgumentNullException.ThrowIfNull(jump);
        ArgumentNullException.ThrowIfNull(rows);
        rows.Add((jump.CountKey(cls), new LinkStat(1d, 1d, jump.AirCommands * tickSeconds, jump.HealthCost)));
        if (_exemplars.Add((cls, jump.From, jump.To, (int)jump.Kind)))
            rows.Add((jump.ExemplarKey(cls), default));
    }

    /// <summary>Whether <paramref name="key"/> is a jump players showed: its count row.</summary>
    public static bool IsCount(in LinkKey key) => key.Source == LinkSource.Human && key.Recipe.Equals(ShownJumpRecipe.CountRecipe);

    /// <summary>Whether <paramref name="key"/> is a jump players showed: its exemplar row.</summary>
    public static bool IsExemplar(in LinkKey key)
        => key.Source == LinkSource.Human && key.Recipe.Bytes.Length > 0 && key.Recipe.Bytes[0] == ShownJumpRecipe.Format;
}
