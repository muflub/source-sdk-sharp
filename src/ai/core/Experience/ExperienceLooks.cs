namespace SourceSharp.Ai;

/// <summary>
/// Where players of one context faced from each bin (plan_bots_teams_tf2_part_g G.4, lane G4a): the
/// <c>look</c> rows of a snapshot that the context takes, summed by bin over their pitch, each bin's
/// most looked-along yaw with its seconds and its share. Only the look table is read, so a decider
/// makes one for its own side when its world's snapshot changes, not at every decide.
/// </summary>
public sealed class ExperienceLooks
{
    private readonly Dictionary<WorldBin, ExperienceLook> _looks;

    private ExperienceLooks(MapExperience of, ExperienceContext context, Dictionary<WorldBin, ExperienceLook> looks)
    {
        Snapshot = of;
        Context = context;
        _looks = looks;
    }

    /// <summary>The snapshot these were read from.</summary>
    public MapExperience Snapshot { get; }

    public ExperienceContext Context { get; }

    /// <summary>How many bins have a look.</summary>
    public int Count => _looks.Count;

    public bool IsEmpty => _looks.Count == 0;

    /// <summary>
    /// The looks of <paramref name="snapshot"/> that <paramref name="context"/> takes: for each bin the yaw
    /// bucket with the most seconds (the lower bucket on a tie, as <see cref="ExperienceView.Look"/>), at
    /// level pitch. An empty snapshot reads nothing.
    /// </summary>
    public static ExperienceLooks Of(MapExperience snapshot, ExperienceContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var looks = new Dictionary<WorldBin, ExperienceLook>();
        if (snapshot.Looks.Count == 0)
            return new ExperienceLooks(snapshot, context, looks);
        var buckets = new Dictionary<WorldBin, double[]>();
        foreach ((LookKey k, Tally v) in snapshot.Looks)
        {
            if (!context.Takes(k.Context) || (uint)k.Yaw >= LookBuckets.YawCount || v.Count <= 0d)
                continue;
            if (!buckets.TryGetValue(k.Bin, out double[]? b))
                buckets[k.Bin] = b = new double[LookBuckets.YawCount];
            b[k.Yaw] += v.Count;
        }

        foreach ((WorldBin bin, double[] b) in buckets)
        {
            int yaw = 0;
            double total = 0d;
            for (int i = 0; i < b.Length; i++)
            {
                total += b[i];
                if (b[i] > b[yaw])
                    yaw = i;
            }

            if (total > 0d)
                looks[bin] = new ExperienceLook(LookBuckets.YawCentre(yaw), 0f, total, b[yaw] / total);
        }

        return new ExperienceLooks(snapshot, context, looks);
    }

    /// <summary>Where players faced from <paramref name="bin"/>, or null where nobody was seen looking.</summary>
    public ExperienceLook? At(WorldBin bin) => _looks.TryGetValue(bin, out ExperienceLook l) ? l : null;
}
