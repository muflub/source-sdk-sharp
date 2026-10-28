using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// prop_dynamic's client half: each start the server networks is played once,
/// from wherever the server's clock has got to by this side's now, as stock's
/// client-side animation runs the sequence and rate it is sent. Found beside
/// stock (2026-10-27): no prop's sequence reached the client.
/// </summary>
public class ClientPropDynamicTests
{
    private static (ClientPropDynamic Prop, RecordingEngine Engine) Made(float now)
    {
        var engine = new RecordingEngine();
        engine.Returns("Clock.Now", now).Returns("EntityRender.SequenceDuration", 2f);
        var prop = new ClientPropDynamic(engine, engine);
        prop.State.BindLocal();
        return (prop, engine);
    }

    private static (int Sequence, float Rate, float Cycle)[] Played(RecordingEngine engine)
        => engine.AllOf("EntityRender.PlaySequenceAt").Select(c => (c.ArgAs<int>(1), c.ArgAs<float>(2), c.ArgAs<float>(3))).ToArray();

    [Fact]
    public void A_start_is_played_from_where_the_servers_clock_has_got_to()
    {
        var (prop, engine) = Made(now: 11f);
        prop.State.Sequence = 2;
        prop.State.PlaybackRate = -1f;
        prop.State.StartCycle = 0.999f;
        prop.State.StartTime = 10f;
        prop.State.Starts = 1;

        prop.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Equal([(2, -1f, 0.499f)], Played(engine).Select(p => (p.Sequence, p.Rate, MathF.Round(p.Cycle, 3))));
    }

    [Fact]
    public void An_update_that_kept_the_start_does_not_restart_it_and_a_new_start_does()
    {
        var (prop, engine) = Made(now: 10f);
        prop.State.Sequence = 1;
        prop.State.PlaybackRate = 1f;
        prop.State.StartTime = 10f;
        prop.State.Starts = 1;
        prop.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        prop.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        prop.State.Starts = 2;
        prop.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal([(1, 1f, 0f), (1, 1f, 0f)], Played(engine));
    }

    [Fact]
    public void Nothing_is_played_before_the_server_starts_anything()
    {
        var (prop, engine) = Made(now: 10f);

        prop.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Empty(Played(engine));
    }
}
