namespace SourceSharp.Ai;

/// <summary>A place a side's players held: its bin, where that is, the yaw faced, its counts, and what holding it earned.</summary>
public readonly record struct HoldSpot(WorldBin Bin, AiVector At, float Yaw, SpotStat Stat, double Worth);

/// <summary>
/// Where a side's players held (plan_bots_teams_tf2_part_g G.4's "spots", lane G4b): a snapshot's <c>spot</c> rows of one
/// kind and role, every stage and class, summed by bin and yaw, each with its <see cref="HoldSpot.Worth"/>: the seconds
/// held times one more than the kills made there, over one more than the deaths there. Read once a snapshot, then only read.
/// </summary>
public sealed class ExperienceHolds
{
    private readonly HoldSpot[] _spots;

    private ExperienceHolds(MapExperience of, int role, int kind, HoldSpot[] spots)
    {
        Snapshot = of;
        Role = role;
        Kind = kind;
        _spots = spots;
    }

    public MapExperience Snapshot { get; }

    public int Role { get; }

    public int Kind { get; }

    /// <summary>Every spot, in packed bin then yaw order.</summary>
    public IReadOnlyList<HoldSpot> All => _spots;

    public bool IsEmpty => _spots.Length == 0;

    /// <summary>What holding a place earned: seconds held x (1 + kills) / (1 + deaths), counts as the snapshot has them.</summary>
    public static double WorthOf(SpotStat s) => Math.Max(0d, s.SecondsHeld) * (1d + Math.Max(0d, s.Kills)) / (1d + Math.Max(0d, s.Deaths));

    /// <summary>The spots of <paramref name="kind"/> that <paramref name="role"/>'s players held in <paramref name="snapshot"/>; none with worth 0.</summary>
    public static ExperienceHolds Of(MapExperience snapshot, int role, int kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Spots.Count == 0)
            return new ExperienceHolds(snapshot, role, kind, []);
        var sums = new Dictionary<(WorldBin Bin, int Yaw), SpotStat>();
        foreach ((SpotKey k, SpotStat v) in snapshot.Spots)
        {
            if (k.Kind == kind && k.Context.Role == role && (uint)k.Yaw < LookBuckets.YawCount)
                sums[(k.Bin, k.Yaw)] = sums.GetValueOrDefault((k.Bin, k.Yaw)).Plus(v);
        }

        HoldSpot[] spots = [.. sums.OrderBy(s => s.Key.Bin.Packed).ThenBy(s => s.Key.Yaw)
                                   .Select(s => new HoldSpot(s.Key.Bin, s.Key.Bin.Centre, LookBuckets.YawCentre(s.Key.Yaw), s.Value, WorthOf(s.Value)))
                                   .Where(s => s.Worth > 0d)];
        return new ExperienceHolds(snapshot, role, kind, spots);
    }

    /// <summary>The spots whose place is within <paramref name="radius"/> of <paramref name="near"/>, in <see cref="All"/>'s order.</summary>
    public IReadOnlyList<HoldSpot> Near(AiVector near, float radius)
    {
        float r2 = radius * radius;
        var found = new List<HoldSpot>();
        foreach (HoldSpot s in _spots)
        {
            if ((s.At - near).LengthSquared <= r2)
                found.Add(s);
        }

        return found;
    }

    /// <summary>
    /// One of <paramref name="spots"/> drawn by worth with <paramref name="random"/> (one draw), or null when none has any.
    /// </summary>
    public static HoldSpot? Draw(IReadOnlyList<HoldSpot> spots, AiRandom random)
    {
        ArgumentNullException.ThrowIfNull(spots);
        ArgumentNullException.ThrowIfNull(random);
        double total = 0d;
        foreach (HoldSpot s in spots)
            total += s.Worth;
        if (total <= 0d)
            return null;
        double pick = random.NextFloat() * total;
        foreach (HoldSpot s in spots)
        {
            pick -= s.Worth;
            if (pick < 0d)
                return s;
        }

        return spots[^1];
    }
}
