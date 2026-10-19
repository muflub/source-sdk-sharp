using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="CostMeter"/>, the release cost meter's arithmetic:
/// totals, the per-call and per-unit means, and the window's nearest-rank
/// percentiles, each in nanoseconds at a given Stopwatch frequency.
/// </summary>
public class CostMeterTests
{
    // 1 tick = 100 ns, so tick counts read as round nanoseconds.
    private const long TenMHz = 10_000_000;

    [Fact]
    public void An_empty_meter_summarises_to_zeroes()
    {
        CostSummary s = new CostMeter(8).Summary(TenMHz);

        Assert.Equal(0, s.Calls);
        Assert.Equal(0, s.Units);
        Assert.Equal(0d, s.MeanNsPerCall);
        Assert.Equal(0d, s.MeanNsPerUnit);
        Assert.Equal(0d, s.P95Ns);
    }

    [Fact]
    public void Calls_and_units_are_summed_and_the_means_divide_by_each()
    {
        var m = new CostMeter(8);
        m.Record(10, 2);
        m.Record(30, 2);

        CostSummary s = m.Summary(TenMHz);

        Assert.Equal(2, s.Calls);
        Assert.Equal(4, s.Units);
        Assert.Equal(40, s.TotalTicks);
        Assert.Equal(2000d, s.MeanNsPerCall);   // 40 ticks * 100 ns / 2 calls
        Assert.Equal(1000d, s.MeanNsPerUnit);   // 40 ticks * 100 ns / 4 units
    }

    [Fact]
    public void Min_median_p95_and_max_are_nearest_rank_over_the_window()
    {
        var m = new CostMeter(100);
        for (int i = 100; i >= 1; i--)
            m.Record(i, 1);

        CostSummary s = m.Summary(TenMHz);

        Assert.Equal(100d, s.MinNs);
        Assert.Equal(5000d, s.MedianNs);   // rank ceil(0.50 * 100) = 50
        Assert.Equal(9500d, s.P95Ns);      // rank ceil(0.95 * 100) = 95
        Assert.Equal(10000d, s.MaxNs);
    }

    [Fact]
    public void The_window_keeps_only_the_latest_calls_while_totals_keep_all()
    {
        var m = new CostMeter(2);
        m.Record(1000, 1);
        m.Record(1, 1);
        m.Record(3, 1);

        CostSummary s = m.Summary(TenMHz);

        Assert.Equal(3, s.Calls);
        Assert.Equal(2, s.Window);
        Assert.Equal(300d, s.MaxNs);   // the 1000-tick call left the window
        Assert.Equal(1004 * 100d / 3, s.MeanNsPerCall);
    }

    [Fact]
    public void Reset_zeroes_totals_and_the_window()
    {
        var m = new CostMeter(4);
        m.Record(50, 3);
        m.Reset();

        CostSummary s = m.Summary(TenMHz);

        Assert.Equal(0, s.Calls + s.Units + s.TotalTicks + s.Window);
    }

    [Fact]
    public void A_stop_records_the_elapsed_ticks_since_its_start()
    {
        var m = new CostMeter(4);
        long start = CostMeter.Start();
        System.Threading.Thread.SpinWait(1000);
        m.Stop(start, 5);

        CostSummary s = m.Summary(System.Diagnostics.Stopwatch.Frequency);

        Assert.Equal(1, s.Calls);
        Assert.Equal(5, s.Units);
        Assert.True(s.TotalTicks > 0, "a timed call has a duration");
    }

    [Fact]
    public void The_timer_calibration_times_that_many_empty_calls()
    {
        CostMeter m = CostMeter.Calibrate(500);

        Assert.Equal(500, m.Calls);
        Assert.Equal(500, m.Units);
        Assert.True(m.TotalTicks >= 0, "an empty call's duration is never negative");
    }

    [Fact]
    public void The_line_is_one_tab_row_with_the_tag_and_every_number()
    {
        var m = new CostMeter(4);
        m.Record(10, 2);
        m.Record(30, 2);

        string line = CostMeter.Line("physics.frame", m.Summary(TenMHz));

        Assert.Equal("SSCOST\tphysics.frame\tcalls=2\tunits=4\tmean_call_ns=2000.0\tmean_unit_ns=1000.0\t" +
                     "window=2\tmin_ns=1000.0\tmedian_ns=1000.0\tp95_ns=3000.0\tmax_ns=3000.0\n", line);
    }
}
