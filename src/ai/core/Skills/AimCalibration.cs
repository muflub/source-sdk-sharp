using System.Collections.Immutable;
using System.Globalization;

namespace SourceSharp.Ai;

/// <summary>A measured hit rate: one weapon in one band, over so many shots.</summary>
public readonly record struct HitRateRow(string Weapon, string Band, double HitRate, long Samples);

/// <summary>One probe of the aim range: a Medium (or other) bot at this curve value hit this often over so many shots.</summary>
public readonly record struct AimProbe(double AimErrorDeg, double HitRate, long Shots);

/// <summary>A probe with what it was of: the preset at its Accuracy level, the weapon and the band.</summary>
public readonly record struct TaggedProbe(string Preset, int Level, string Weapon, string Band, AimProbe Probe);

/// <summary>What a calibration says of one preset.</summary>
/// <param name="Preset">The preset.</param>
/// <param name="Level">Its Accuracy level, which the value belongs to.</param>
/// <param name="Target">The hit rate it should have: Medium's measured, times its ratio.</param>
/// <param name="AimErrorDeg">The curve value that makes it, by interpolating the probes.</param>
/// <param name="Reached">False when no probe reached the target: the value is the probes' nearest end.</param>
public readonly record struct PresetCalibration(string Preset, int Level, double Target, double AimErrorDeg, bool Reached);

/// <summary>
/// The hit rate a Medium bot should match (§12.1, ruling 2026-11-02: "After my
/// recording set med bot to match my hit rate"): the file a recording's
/// metrics write (or a hand-written one), one row per weapon and band, and the
/// arithmetic from it and the aim range's probes to the curve values. Pure.
/// The file is tab- or space-separated: <c>weapon band hit_rate samples</c>,
/// with <c>#</c> comments; the bands are the metrics' (<see cref="AimBands"/>).
/// </summary>
public static class AimCalibration
{
    /// <summary>Reads a hit rate file; anything it cannot use is refused by line (<see cref="InvalidDataException"/>).</summary>
    public static ImmutableArray<HitRateRow> ParseFile(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = ImmutableArray.CreateBuilder<HitRateRow>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string at = $"hit rate file, line {i + 1}";
            if (p.Length != 4)
                throw new InvalidDataException($"{at}: weapon, band, hit rate and samples expected, not '{line}'");
            if (p[1] is not ("under300" or "300-800" or "over800"))
                throw new InvalidDataException($"{at}: no band '{p[1]}' (under300, 300-800, over800)");
            if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || rate is < 0.0 or > 1.0)
                throw new InvalidDataException($"{at}: the hit rate is a share from 0 to 1, not '{p[2]}'");
            if (!long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long samples) || samples <= 0)
                throw new InvalidDataException($"{at}: the samples are a count above 0, not '{p[3]}'");
            rows.Add(new HitRateRow(p[0], p[1], rate, samples));
        }

        if (rows.Count == 0)
            throw new InvalidDataException("hit rate file: no rows");
        return rows.ToImmutable();
    }

    /// <summary>
    /// The rows of <c>ai-replay metrics</c>' hit-rate block for its people:
    /// the bands of each line that says <c>human</c>, pooled across them, with
    /// <c>any</c> for the weapon since the block does not split a band by weapon.
    /// Only the bands: a person's overall rate is mostly shots with nobody near
    /// the crosshair ("no target"), which say nothing of their aim, and a bot on
    /// the range fires only at a target it sees, so the bands are what compare.
    /// </summary>
    public static ImmutableArray<HitRateRow> FromMetrics(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        (string Name, string Metric)[] bands = [("under300", "<300"), ("300-800", "300-800"), ("over800", ">800")];
        long[] hits = new long[bands.Length], shots = new long[bands.Length];
        bool seen = false, inBlock = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("hit rate:", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }

            string[] parts = line.Split('|');
            if (!inBlock || parts.Length < 3 || !parts[0].Contains(" human ", StringComparison.Ordinal))
                continue;
            seen = true;
            string[] bandParts = parts[^1].Split(',', StringSplitOptions.TrimEntries);
            for (int b = 0; b < bands.Length; b++)
            {
                string? part = bandParts.FirstOrDefault(p => p.StartsWith(bands[b].Metric + " ", StringComparison.Ordinal));
                string[]? fraction = part?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Split('/');
                if (fraction is not { Length: 2 } || !long.TryParse(fraction[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long h)
                    || !long.TryParse(fraction[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    throw new InvalidDataException($"metrics: no {bands[b].Metric} band in '{line}'");
                hits[b] += h;
                shots[b] += n;
            }
        }

        if (!seen)
            throw new InvalidDataException("metrics: no line of a person in a hit rate block: a recording with a person in it, through ai-replay metrics");
        var rows = ImmutableArray.CreateBuilder<HitRateRow>();
        for (int b = 0; b < bands.Length; b++)
        {
            if (shots[b] > 0)
                rows.Add(new HitRateRow("any", bands[b].Name, hits[b] / (double)shots[b], shots[b]));
        }

        return rows.ToImmutable();
    }

    /// <summary>The rows as a hit rate file, which <see cref="ParseFile"/> reads back.</summary>
    public static string WriteFile(IEnumerable<HitRateRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new System.Text.StringBuilder("# weapon band hit_rate samples\n");
        foreach (HitRateRow r in rows)
            text.Append(CultureInfo.InvariantCulture, $"{r.Weapon} {r.Band} {r.HitRate:F4} {r.Samples}\n");
        return text.ToString();
    }

    /// <summary>The rows with enough shots to mean anything, most shots first; none is a refusal.</summary>
    public static ImmutableArray<HitRateRow> Usable(ImmutableArray<HitRateRow> rows, long minSamples)
    {
        ImmutableArray<HitRateRow> used = [.. rows.Where(r => r.Samples >= minSamples).OrderByDescending(r => r.Samples).ThenBy(r => r.Weapon, StringComparer.Ordinal).ThenBy(r => r.Band, StringComparer.Ordinal)];
        if (used.IsEmpty)
            throw new InvalidDataException($"hit rate file: no row has {minSamples} samples, so nothing can be matched");
        return used;
    }

    /// <summary>The hit rate of a band that weighs each row by its samples.</summary>
    public static double Pooled(IEnumerable<HitRateRow> rows)
    {
        double hits = 0.0, shots = 0.0;
        foreach (HitRateRow r in rows)
        {
            hits += r.HitRate * r.Samples;
            shots += r.Samples;
        }

        return shots <= 0.0 ? 0.0 : hits / shots;
    }

    /// <summary>
    /// The curve value where the probes reach <paramref name="target"/>. Hit rate
    /// falls as the error grows, but a probe of a hundred shots is noisy, so the
    /// probes are first made to fall by pooling each run that rises, weighed by its
    /// shots (isotonic regression: one low probe at the small end does not cap the
    /// line), then read by interpolating. Outside the line, its nearer end.
    /// </summary>
    public static (double Value, bool Reached) Solve(IReadOnlyList<AimProbe> probes, double target)
    {
        ArgumentNullException.ThrowIfNull(probes);
        if (probes.Count == 0)
            throw new InvalidDataException("calibration: no probes");
        AimProbe[] sorted = [.. probes.OrderBy(p => p.AimErrorDeg)];
        var blocks = new List<(double Rate, double Weight, int Count)>();
        foreach (AimProbe p in sorted)
        {
            (double Rate, double Weight, int Count) b = (p.HitRate, Math.Max(p.Shots, 1), 1);
            while (blocks.Count > 0 && blocks[^1].Rate < b.Rate)
            {
                (double Rate, double Weight, int Count) last = blocks[^1];
                blocks.RemoveAt(blocks.Count - 1);
                double weight = last.Weight + b.Weight;
                b = ((last.Rate * last.Weight + b.Rate * b.Weight) / weight, weight, last.Count + b.Count);
            }

            blocks.Add(b);
        }

        double[] rate = new double[sorted.Length];
        int at = 0;
        foreach ((double r, _, int count) in blocks)
        {
            for (int k = 0; k < count; k++)
                rate[at++] = r;
        }

        if (target > rate[0])
            return (sorted[0].AimErrorDeg, false);
        if (target < rate[^1])
            return (sorted[^1].AimErrorDeg, false);
        for (int i = 1; i < sorted.Length; i++)
        {
            if (target > rate[i])
            {
                double span = rate[i - 1] - rate[i];
                double t = span <= 0.0 ? 0.0 : (rate[i - 1] - target) / span;
                return (sorted[i - 1].AimErrorDeg + t * (sorted[i].AimErrorDeg - sorted[i - 1].AimErrorDeg), true);
            }
        }

        return (sorted[^1].AimErrorDeg, true);
    }

    /// <summary>
    /// Each preset's curve value: for every row of <paramref name="rows"/> with
    /// probes of that preset, the value where its probes reach the row's hit
    /// rate times the preset's ratio, weighed by the row's samples. A preset's
    /// target is always relative to Medium's measured rate, so all four follow
    /// when Medium moves.
    /// </summary>
    public static ImmutableArray<PresetCalibration> Calibrate(IReadOnlyList<HitRateRow> rows, IReadOnlyList<TaggedProbe> probes,
                                                              IReadOnlyList<(string Preset, double Ratio)> ratios)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(ratios);
        var out_ = ImmutableArray.CreateBuilder<PresetCalibration>();
        foreach ((string preset, double ratio) in ratios)
        {
            double sum = 0.0, weight = 0.0, target = 0.0;
            bool reached = true;
            int level = -1;
            foreach (HitRateRow row in rows)
            {
                AimProbe[] own = [.. probes.Where(p => p.Preset == preset && p.Weapon == row.Weapon && p.Band == row.Band).Select(p => p.Probe)];
                if (own.Length == 0)
                    continue;
                level = probes.First(p => p.Preset == preset).Level;
                double wanted = row.HitRate * ratio;
                (double v, bool ok) = Solve(own, wanted);
                sum += v * row.Samples;
                target += wanted * row.Samples;
                weight += row.Samples;
                reached &= ok;
            }

            if (weight > 0.0)
                out_.Add(new PresetCalibration(preset, level, target / weight, sum / weight, reached));
        }

        return out_.ToImmutable();
    }

    /// <summary>Reads a probes file: <c>preset level weapon band aim_error_deg hit_rate shots</c>; a line it cannot use is refused.</summary>
    public static ImmutableArray<TaggedProbe> ParseProbes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = ImmutableArray.CreateBuilder<TaggedProbe>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 7 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                || !long.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long shots))
                throw new InvalidDataException($"probes file, line {i + 1}: preset, level, weapon, band, value, hit rate and shots expected, not '{line}'");
            rows.Add(new TaggedProbe(p[0], level, p[2], p[3], new AimProbe(value, rate, shots)));
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// A curve through the presets' values: straight between their levels and
    /// the shipped curve's slope past them. Refused (<see cref="InvalidDataException"/>)
    /// if the values do not fall as the level rises: the skill's facts would go red.
    /// </summary>
    public static float[] Curve(IReadOnlyList<(int Level, double Value)> anchors, IReadOnlyList<float> shipped)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(shipped);
        (int Level, double Value)[] a = [.. anchors.OrderBy(x => x.Level)];
        if (a.Length == 0)
            throw new InvalidDataException("calibration: no anchors");
        for (int i = 1; i < a.Length; i++)
        {
            if (a[i].Value >= a[i - 1].Value)
                throw new InvalidDataException($"calibration: aimErrorDeg {a[i].Value:F2} at level {a[i].Level} is not below {a[i - 1].Value:F2} at level {a[i - 1].Level}");
        }

        float[] curve = new float[shipped.Count];
        for (int level = 0; level < curve.Length; level++)
        {
            double v;
            if (level <= a[0].Level)
                v = a[0].Value + (shipped[level] - shipped[a[0].Level]);
            else if (level >= a[^1].Level)
                v = a[^1].Value * shipped[level] / Math.Max(shipped[a[^1].Level], 1e-3f);
            else
            {
                int j = 1;
                while (a[j].Level < level)
                    j++;
                double t = (level - a[j - 1].Level) / (double)(a[j].Level - a[j - 1].Level);
                v = a[j - 1].Value + t * (a[j].Value - a[j - 1].Value);
            }

            curve[level] = (float)Math.Round(v, 3);
        }

        return curve;
    }
}
