namespace SourceSharp.Ai;

/// <summary>Where players face from a bin: the yaw and pitch most looked along, the seconds seen and the yaw's share of them.</summary>
public readonly record struct ExperienceLook(float Yaw, float Pitch, double Seconds, double Share);

/// <summary>A spot of a kind: its bin, where that is, the yaw faced and its counts.</summary>
public readonly record struct ExperienceSpot(WorldBin Bin, AiVector At, float Yaw, SpotStat Stat);

/// <summary>Where death came from: a victim's bin, the killer's, the weapon's class and how often.</summary>
public readonly record struct ExperienceSightLine(WorldBin Victim, WorldBin Killer, int WeaponClass, double Count);

/// <summary>A learned movement and its counts.</summary>
public readonly record struct ExperienceLink(LinkKey Key, LinkStat Stat);

/// <summary>
/// One reader's part of a <see cref="MapExperience"/> (G.4): the rows whose
/// stage, role and class its context takes, summed by place. Built once (a
/// pass over the snapshot's rows) and then only read, so a decider keeps the
/// view of its own context and asks it per bin. Every answer with no row is
/// zero or empty, never a null.
/// </summary>
public sealed class ExperienceView
{
    /// <summary>The seconds a bin's deaths are divided by at least: a bin crossed in an instant is not judged deadly by one death.</summary>
    public const double HazardFloorSeconds = 1d;

    private readonly Dictionary<WorldBin, CellStat> _cells = new();
    private readonly Dictionary<WorldBin, double[]> _looks = new();
    private readonly Dictionary<(int Kind, WorldBin Bin, int Yaw), SpotStat> _spots = new();
    private readonly Dictionary<(WorldBin Victim, WorldBin Killer, int WeaponClass), double> _sights = new();
    private readonly Dictionary<(int Kind, long Choice), OutcomeStat> _outcomes = new();
    private readonly ExperienceLink[] _links;

    internal ExperienceView(MapExperience of, ExperienceContext context)
    {
        Experience = of;
        Context = context;
        foreach ((CellKey k, CellStat v) in of.Cells)
        {
            if (context.Takes(k.Context))
                _cells[k.Bin] = _cells.GetValueOrDefault(k.Bin).Plus(v);
        }

        foreach ((LookKey k, Tally v) in of.Looks)
        {
            if (!context.Takes(k.Context) || (uint)k.Yaw >= LookBuckets.YawCount || (uint)k.Pitch >= LookBuckets.PitchCount)
                continue;
            if (!_looks.TryGetValue(k.Bin, out double[]? buckets))
                _looks[k.Bin] = buckets = new double[LookBuckets.YawCount + LookBuckets.PitchCount];
            buckets[k.Yaw] += v.Count;
            buckets[LookBuckets.YawCount + k.Pitch] += v.Count;
        }

        foreach ((SpotKey k, SpotStat v) in of.Spots)
        {
            if (context.Takes(k.Context))
                _spots[(k.Kind, k.Bin, k.Yaw)] = _spots.GetValueOrDefault((k.Kind, k.Bin, k.Yaw)).Plus(v);
        }

        foreach ((SightKey k, Tally v) in of.Sights)
        {
            if (context.Takes(k.Context))
                _sights[(k.Victim, k.Killer, k.WeaponClass)] = _sights.GetValueOrDefault((k.Victim, k.Killer, k.WeaponClass)) + v.Count;
        }

        foreach ((OutcomeKey k, OutcomeStat v) in of.Outcomes)
        {
            if (context.Takes(k.Context))
                _outcomes[(k.Kind, k.Choice)] = _outcomes.GetValueOrDefault((k.Kind, k.Choice)).Plus(v);
        }

        // A link is a class's, in any stage and role.
        _links = [.. of.Links.Where(l => context.Class == ExperienceContext.Any || context.Class == l.Key.Class)
                             .Select(l => new ExperienceLink(l.Key, l.Value))
                             .OrderBy(l => l.Key.From.Packed).ThenBy(l => l.Key.To.Packed).ThenBy(l => l.Key.Kind).ThenBy(l => l.Key.Class)
                             .ThenBy(l => (int)l.Key.Source).ThenByDescending(l => l.Stat.Successes)];
    }

    /// <summary>The snapshot this reads.</summary>
    public MapExperience Experience { get; }

    public ExperienceContext Context { get; }

    /// <summary>A bin's counts, humans' and bots' together (a bot's already weigh a quarter); zeros where nobody was.</summary>
    public CellStat Cell(WorldBin bin) => _cells.GetValueOrDefault(bin);

    /// <summary>How used a bin is: the seconds players were present in it.</summary>
    public double Traffic(WorldBin bin) => _cells.GetValueOrDefault(bin).Seconds;

    /// <summary>How deadly a bin is: deaths per second present, over at least <see cref="HazardFloorSeconds"/>.</summary>
    public double Hazard(WorldBin bin)
    {
        CellStat c = _cells.GetValueOrDefault(bin);
        return c.Deaths <= 0d ? 0d : c.Deaths / Math.Max(c.Seconds, HazardFloorSeconds);
    }

    /// <summary>
    /// Where players face from <paramref name="bin"/>: the yaw bucket with the
    /// most seconds (the lower bucket on a tie) and the pitch bucket likewise;
    /// null where nobody was seen looking.
    /// </summary>
    public ExperienceLook? Look(WorldBin bin)
    {
        if (!_looks.TryGetValue(bin, out double[]? buckets))
            return null;
        int yaw = 0, pitch = 0;
        double total = 0d;
        for (int i = 0; i < LookBuckets.YawCount; i++)
        {
            total += buckets[i];
            if (buckets[i] > buckets[yaw])
                yaw = i;
        }

        for (int i = 1; i < LookBuckets.PitchCount; i++)
        {
            if (buckets[LookBuckets.YawCount + i] > buckets[LookBuckets.YawCount + pitch])
                pitch = i;
        }

        return total <= 0d ? null : new ExperienceLook(LookBuckets.YawCentre(yaw), LookBuckets.PitchCentre(pitch), total, buckets[yaw] / total);
    }

    /// <summary>
    /// The spots of <paramref name="kind"/> whose bin's middle is within
    /// <paramref name="radius"/> of <paramref name="near"/>, most used first
    /// (then by bin and yaw, so the order is the same every time).
    /// </summary>
    public IReadOnlyList<ExperienceSpot> Spots(int kind, AiVector near, float radius)
    {
        float r2 = radius * radius;
        return [.. _spots.Where(s => s.Key.Kind == kind && (s.Key.Bin.Centre - near).LengthSquared <= r2)
                         .OrderByDescending(s => s.Value.Uses).ThenBy(s => s.Key.Bin.Packed).ThenBy(s => s.Key.Yaw)
                         .Select(s => new ExperienceSpot(s.Key.Bin, s.Key.Bin.Centre, LookBuckets.YawCentre(s.Key.Yaw), s.Value))];
    }

    /// <summary>The learned movements this context's class can do, by where they start.</summary>
    public IReadOnlyList<ExperienceLink> Links => _links;

    /// <summary>The learned movements that start in <paramref name="from"/>.</summary>
    public IReadOnlyList<ExperienceLink> LinksFrom(WorldBin from) => [.. _links.Where(l => l.Key.From == from)];

    /// <summary>Where death came from for one standing in <paramref name="victim"/>, the most kills first.</summary>
    public IReadOnlyList<ExperienceSightLine> Threats(WorldBin victim)
        => Lines(_sights.Where(s => s.Key.Victim == victim));

    /// <summary>The bins one standing in <paramref name="killer"/> killed into, the most kills first.</summary>
    public IReadOnlyList<ExperienceSightLine> Targets(WorldBin killer)
        => Lines(_sights.Where(s => s.Key.Killer == killer));

    /// <summary>What a choice came to: its tries and its scores' sum; zeros when never tried.</summary>
    public OutcomeStat Outcome(int kind, long choice) => _outcomes.GetValueOrDefault((kind, choice));

    private static ExperienceSightLine[] Lines(IEnumerable<KeyValuePair<(WorldBin Victim, WorldBin Killer, int WeaponClass), double>> rows)
        => [.. rows.OrderByDescending(s => s.Value).ThenBy(s => s.Key.Victim.Packed).ThenBy(s => s.Key.Killer.Packed).ThenBy(s => s.Key.WeaponClass)
                   .Select(s => new ExperienceSightLine(s.Key.Victim, s.Key.Killer, s.Key.WeaponClass, s.Value))];
}
