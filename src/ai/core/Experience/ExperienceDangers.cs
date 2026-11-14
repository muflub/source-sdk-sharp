namespace SourceSharp.Ai;

/// <summary>A place a side's players keep being killed from, as seen from where they died: its point, the deaths it took and their share.</summary>
public readonly record struct DangerPlace(AiVector At, WorldBin Killer, double Deaths, double Share);

/// <summary>
/// Where death comes from (plan_bots_teams_tf2_part_g G.4's "danger places", lane G4b): for each bin a side's
/// players died in, the place most of those deaths came from, when one place stands out. Read from a snapshot's
/// <c>sight_line</c> rows of one role (every stage, class and weapon), once a snapshot, and then only read.
///
/// <para>Deaths at a doorway spread over the bins around it, and so do the shots of whoever made them: a bin's
/// deaths are the lines whose victim is in its <see cref="Neighbourhood"/>, and a killer place's score is the lines
/// whose killer is in its own. The best place (the lower packed bin on a tie) is the bin's danger place when it took at
/// least <see cref="MinDeaths"/> of them and <see cref="MinShare"/> of the area's deaths, and lies at least
/// <see cref="MinReach"/> off: a death at arm's length was a fight the bot will see, not a place to check.</para>
/// </summary>
public sealed class ExperienceDangers
{
    /// <summary>The counted deaths a place must have made: two (a third is the one a person would not wait for).</summary>
    public const double MinDeaths = 2d;

    /// <summary>The share of an area's deaths a place must have made: where death came from all round, there is no one place.</summary>
    public const double MinShare = 0.4d;

    /// <summary>The least distance from the victim's bin to the place.</summary>
    public const float MinReach = 256f;

    /// <summary>How far in bins (each way, on each axis) an area and a place reach.</summary>
    public const int Neighbourhood = 1;

    private readonly Dictionary<WorldBin, DangerPlace> _places;

    private ExperienceDangers(MapExperience of, int role, Dictionary<WorldBin, DangerPlace> places)
    {
        Snapshot = of;
        Role = role;
        _places = places;
    }

    /// <summary>The snapshot these were read from.</summary>
    public MapExperience Snapshot { get; }

    /// <summary>The role whose deaths these are.</summary>
    public int Role { get; }

    /// <summary>How many bins have a danger place.</summary>
    public int Count => _places.Count;

    public bool IsEmpty => _places.Count == 0;

    /// <summary>The danger place for one standing in <paramref name="bin"/>, or null.</summary>
    public DangerPlace? At(WorldBin bin) => _places.TryGetValue(bin, out DangerPlace p) ? p : null;

    /// <summary>The bins with a place, in packed order (the facts', the routes').</summary>
    public IEnumerable<(WorldBin Bin, DangerPlace Place)> All => _places.OrderBy(p => p.Key.Packed).Select(p => (p.Key, p.Value));

    /// <summary>The danger places of <paramref name="role"/>'s deaths in <paramref name="snapshot"/>. An empty snapshot reads nothing.</summary>
    public static ExperienceDangers Of(MapExperience snapshot, int role)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var places = new Dictionary<WorldBin, DangerPlace>();
        if (snapshot.Sights.Count == 0)
            return new ExperienceDangers(snapshot, role, places);

        var lines = new Dictionary<(WorldBin Victim, WorldBin Killer), double>();
        foreach ((SightKey k, Tally v) in snapshot.Sights)
        {
            if (k.Context.Role == role && v.Count > 0d)
                lines[(k.Victim, k.Killer)] = lines.GetValueOrDefault((k.Victim, k.Killer)) + v.Count;
        }

        // Each victim bin's lines, so an area is a few lookups, not a pass over every line.
        var byVictim = new Dictionary<WorldBin, List<(WorldBin Killer, double Count)>>();
        foreach (((WorldBin victim, WorldBin killer), double count) in lines.OrderBy(l => l.Key.Victim.Packed).ThenBy(l => l.Key.Killer.Packed))
        {
            if (!byVictim.TryGetValue(victim, out List<(WorldBin, double)>? list))
                byVictim[victim] = list = new List<(WorldBin, double)>();
            list.Add((killer, count));
        }

        var area = new List<(WorldBin Killer, double Count)>();
        var killers = new Dictionary<WorldBin, double>();
        foreach (WorldBin victim in byVictim.Keys.OrderBy(b => b.Packed))
        {
            area.Clear();
            killers.Clear();
            double total = 0d;
            foreach (WorldBin near in Around(victim))
            {
                if (!byVictim.TryGetValue(near, out List<(WorldBin Killer, double Count)>? list))
                    continue;
                foreach ((WorldBin killer, double count) in list)
                {
                    area.Add((killer, count));
                    killers[killer] = killers.GetValueOrDefault(killer) + count;
                    total += count;
                }
            }

            WorldBin best = default;
            double bestScore = 0d;
            foreach (WorldBin candidate in killers.Keys.OrderBy(b => b.Packed))
            {
                double score = 0d;
                foreach (WorldBin near in Around(candidate))
                    score += killers.GetValueOrDefault(near);
                if (score > bestScore)
                    (best, bestScore) = (candidate, score);
            }

            if (bestScore < MinDeaths || bestScore / total < MinShare)
                continue;
            AiVector sum = AiVector.Zero;
            double weight = 0d;
            foreach (WorldBin near in Around(best))
            {
                double w = killers.GetValueOrDefault(near);
                if (w <= 0d)
                    continue;
                sum += near.Centre * (float)w;
                weight += w;
            }

            AiVector at = sum * (float)(1d / weight);
            if (at.DistanceTo(victim.Centre) < MinReach)
                continue;
            places[victim] = new DangerPlace(at, best, bestScore, bestScore / total);
        }

        return new ExperienceDangers(snapshot, role, places);
    }

    /// <summary>The bins within <see cref="Neighbourhood"/> of <paramref name="bin"/> on each axis, itself among them.</summary>
    private static IEnumerable<WorldBin> Around(WorldBin bin)
    {
        for (int dz = -Neighbourhood; dz <= Neighbourhood; dz++)
        {
            for (int dy = -Neighbourhood; dy <= Neighbourhood; dy++)
            {
                for (int dx = -Neighbourhood; dx <= Neighbourhood; dx++)
                    yield return new WorldBin(bin.X + dx, bin.Y + dy, bin.Z + dz);
            }
        }
    }
}
