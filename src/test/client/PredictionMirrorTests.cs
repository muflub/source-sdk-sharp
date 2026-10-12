using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Debug.Api.Shared's PredictionMirror counts and windows exactly as the game's
/// PredictionErrorRing does (plan_devapi_interfaces P4), so the server's mirror
/// did not change by moving off the Abi.
/// </summary>
public class PredictionMirrorTests
{
    private static PredictionErrorRecord Record(long sequence, int standing)
        => new(sequence, 1, 3, "ss_thing", 8, standing, 0, standing == 0 ? 1 : 0, DateTime.UnixEpoch, []);

    [Theory]
    [InlineData(4, 3)]
    [InlineData(4, 9)]
    [InlineData(1, 5)]
    public void The_mirror_answers_as_the_games_ring(int capacity, int added)
    {
        var ring = new PredictionErrorRing(capacity);
        var mirror = new PredictionMirror(capacity);

        for (int i = 0; i < added; i++)
        {
            var record = Record(i * 2, standing: i % 3 == 0 ? 0 : 4);
            ring.Add(record);
            mirror.Add(record.ToWire());
        }

        Assert.Equal((ring.Capacity, ring.Count, ring.NextSequence, ring.Observed, ring.Mispredicted, ring.ToleratedOnly),
                     (mirror.Capacity, mirror.Count, mirror.NextSequence, mirror.Observed, mirror.Mispredicted, mirror.ToleratedOnly));

        foreach (long since in new long[] { 0, 3, 8, 100 })
            Assert.Equal(ring.Since(since, 2).Select(r => r.Sequence), mirror.Since(since, 2).Select(r => r.Sequence));

        ring.Reset();
        mirror.Reset();
        Assert.Equal((ring.Count, ring.Observed, ring.NextSequence), (mirror.Count, mirror.Observed, mirror.NextSequence));
    }

    [Fact]
    public void The_codecs_field_limit_and_capacity_are_the_games()
    {
        Assert.Equal(PredictionErrorAnalysis.MaxFieldsPerRecord, DebugChannelCodec.MaxFieldsPerRecord);
        Assert.Equal(PredictionErrorRing.DefaultCapacity, PredictionMirror.DefaultCapacity);
    }
}

/// <summary>The client's IDebugClientPrediction reads its ring's totals and records exactly (P3b).</summary>
public class ClientDebugPredictionTests
{
    [Fact]
    public void The_window_is_the_rings_totals_and_records()
    {
        var ring = new PredictionErrorRing(3);
        for (int i = 0; i < 5; i++)
            ring.Add(new PredictionErrorRecord(i, 1, 3, "ss_thing", 8, i % 2 == 0 ? 4 : 0, 0, 0, DateTime.UnixEpoch, []));

        var window = new SourceSharp.Client.ClientDebugPrediction(ring).Since(3, 8);

        Assert.Equal((ring.Capacity, ring.Count, ring.NextSequence, ring.Observed, ring.Mispredicted, ring.ToleratedOnly),
                     (window.Capacity, window.Held, window.NextSequence, window.Observed, window.Mispredicted, window.ToleratedOnly));
        Assert.Equal(ring.Since(3, 8).Select(r => r.Sequence), window.Records.Select(r => r.Sequence));
        Assert.Equal([3L, 4L], window.Records.Select(r => r.Sequence));
    }

    [Fact]
    public void Reset_clears_the_ring()
    {
        var ring = new PredictionErrorRing(3);
        ring.Add(new PredictionErrorRecord(0, 1, 3, "ss_thing", 8, 4, 0, 0, DateTime.UnixEpoch, []));
        new SourceSharp.Client.ClientDebugPrediction(ring).Reset();
        Assert.Equal(0, ring.Count);
    }
}
