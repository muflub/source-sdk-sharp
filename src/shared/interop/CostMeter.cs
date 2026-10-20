using System.Diagnostics;
using System.Globalization;

namespace SourceSharp.Abi;

/// <summary>What a <see cref="CostMeter"/> has seen, in nanoseconds.</summary>
/// <param name="Calls">Calls timed since the last reset.</param>
/// <param name="Units">The units those calls covered (bodies, steps, ...), summed.</param>
/// <param name="TotalTicks">Stopwatch ticks those calls took, summed.</param>
/// <param name="MeanNsPerCall">TotalTicks over Calls.</param>
/// <param name="MeanNsPerUnit">TotalTicks over Units.</param>
/// <param name="Window">How many of the latest calls the percentiles are over.</param>
/// <param name="MinNs">The window's shortest call.</param>
/// <param name="MedianNs">The window's nearest-rank median call.</param>
/// <param name="P95Ns">The window's nearest-rank 95th percentile call.</param>
/// <param name="MaxNs">The window's longest call.</param>
public readonly record struct CostSummary(long Calls, long Units, long TotalTicks, double MeanNsPerCall,
                                          double MeanNsPerUnit, int Window, double MinNs, double MedianNs,
                                          double P95Ns, double MaxNs);

/// <summary>
/// A cost meter that ships in release (docs/todo.md's "physics paths never
/// measured in release"): two Stopwatch timestamps a call, a sum, and a ring
/// of the latest calls' durations for the percentiles. One per measured path,
/// owned by that path's instance (statics to zero), read out by the path's
/// console command as one <see cref="Line"/>.
///
/// <para>
/// Not thread-safe: each meter is written by the one thread that runs its
/// path (the game thread).
/// </para>
/// </summary>
public sealed class CostMeter
{
    /// <summary>A meter over the latest 4096 calls.</summary>
    public CostMeter() : this(4096) { }

    /// <param name="window">How many of the latest calls the percentiles keep.</param>
    public CostMeter(int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        _window = new long[window];
    }

    /// <summary>The timestamp a timed call starts at.</summary>
    public static long Start() => Stopwatch.GetTimestamp();

    /// <summary>Ends a call that started at <paramref name="start"/>.</summary>
    /// <param name="start">What <see cref="Start"/> returned.</param>
    /// <param name="units">What the call covered (1 unless it covered several bodies or steps).</param>
    public void Stop(long start, int units = 1) => Record(Stopwatch.GetTimestamp() - start, units);

    /// <summary>One call of <paramref name="ticks"/> Stopwatch ticks over <paramref name="units"/> units.</summary>
    public void Record(long ticks, int units)
    {
        Calls++;
        Units += units;
        TotalTicks += ticks;

        _window[_next] = ticks;
        _next = (_next + 1) % _window.Length;
        if (_filled < _window.Length)
            _filled++;
    }

    /// <summary>Calls timed since the last reset.</summary>
    public long Calls { get; private set; }

    /// <summary>Units those calls covered.</summary>
    public long Units { get; private set; }

    /// <summary>Stopwatch ticks those calls took.</summary>
    public long TotalTicks { get; private set; }

    /// <summary>Zeroes the totals and empties the window.</summary>
    public void Reset()
    {
        Calls = Units = TotalTicks = 0;
        _next = _filled = 0;
    }

    /// <summary>The totals, means and the window's percentiles at <paramref name="frequency"/> ticks a second.</summary>
    public CostSummary Summary(long frequency)
    {
        double ns = 1e9 / frequency;

        long[] sorted = _window.AsSpan(0, _filled).ToArray();
        Array.Sort(sorted);

        return new CostSummary(Calls, Units, TotalTicks,
                               Calls == 0 ? 0 : TotalTicks * ns / Calls,
                               Units == 0 ? 0 : TotalTicks * ns / Units,
                               _filled,
                               Rank(sorted, 0.0) * ns, Rank(sorted, 0.50) * ns,
                               Rank(sorted, 0.95) * ns, Rank(sorted, 1.0) * ns);
    }

    /// <summary>
    /// The meter's own floor: <paramref name="calls"/> empty Start/Stop pairs,
    /// back to back, on a fresh meter. What a timed call costs and reads when
    /// it does nothing, so a reading near it is the timer's, not the path's.
    /// </summary>
    public static CostMeter Calibrate(int calls)
    {
        var meter = new CostMeter(calls);
        for (int i = 0; i < calls; i++)
            meter.Stop(Start());
        return meter;
    }

    /// <summary>The summary as one <c>SSCOST</c> tab row, the shape a gate or a measurement script reads.</summary>
    public static string Line(string tag, in CostSummary s)
        => string.Create(CultureInfo.InvariantCulture,
            $"SSCOST\t{tag}\tcalls={s.Calls}\tunits={s.Units}\tmean_call_ns={s.MeanNsPerCall:F1}\t" +
            $"mean_unit_ns={s.MeanNsPerUnit:F1}\twindow={s.Window}\tmin_ns={s.MinNs:F1}\t" +
            $"median_ns={s.MedianNs:F1}\tp95_ns={s.P95Ns:F1}\tmax_ns={s.MaxNs:F1}\n");

    // Nearest rank: the smallest value with at least fraction of the window at
    // or below it (fraction 0 is the minimum).
    private static long Rank(long[] sorted, double fraction)
    {
        if (sorted.Length == 0)
            return 0;

        int rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private readonly long[] _window;
    private int _next;
    private int _filled;
}
