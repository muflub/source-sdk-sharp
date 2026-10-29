using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The traffic view's arithmetic — plan_nettable_generic.md Part 14 Step 9.
///
/// <para>
/// This is the instrument Part 17's cost claims get answered with, and the one
/// Part 10's <c>Group</c> hint has to be tuned against, so being wrong here is
/// worse than having no view at all: a wrong number does not look wrong, it
/// looks like an answer. Everything below is checked against bytes this file
/// laid out itself.
/// </para>
///
/// <para>
/// THE UNIT IS THE DWORD. Part 13 says the diff is the delta "byte for byte,
/// because each byte is its own prop", which was true of the byte-granular draft
/// and stopped being true when Part 1B made the element a dword. The tests that
/// matter most are the ones where bytes and words disagree.
/// </para>
/// </summary>
public class NetTrafficTests
{
    // A layout with the two cases that matter: fields sharing one word, and a
    // field spanning several.
    private static readonly NetSchemaField[] Layout =
    {
        new("Wide",   "byte",   NetFieldKind.UInt,   0,  1, 8),
        new("High",   "byte",   NetFieldKind.UInt,   1,  1, 8),
        new("Health", "int",    NetFieldKind.Int,    4,  4, 32),
        new("Where",  "Vector", NetFieldKind.Vector, 8, 12, 32),
    };

    /// <summary>
    /// The route, and the two things it must never let a reader confuse.
    /// </summary>
    [Fact]
    public void Routing()
    {
        using (var unwiredServer = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()))
        {
            // A host that wired nothing says so, rather than answering an empty
            // class list — which would read as "the payload costs nothing".
            string unwired = Reply(unwiredServer, "/nettraffic").Body;
            Assert.True(unwired.Contains("\"available\":false"), "an unwired traffic route says the host does not report it");
        }

        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);
        sampler.Observe("beam", 1, blob, Layout);
        blob[4] = 1;
        sampler.Observe("beam", 1, blob, Layout);

        bool sampling = false;
        bool reset = false;
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server(traffic: () => new NetTrafficReport(sampling, sampler.Elapsed, sampler.Tracked, sampler.Report()),
                                resetTraffic: () => reset = true));

        // NOT SAMPLING is not the same as nothing moving, and the reply has to
        // say which — this is the distinction the whole route turns on.
        string idle = Reply(server, "/nettraffic").Body;
        Assert.True(idle.Contains("\"sampling\":false") && idle.Contains("cs_sv_nettable_profile"), "a route that is not sampling says so");

        sampling = true;
        string live = Reply(server, "/nettraffic").Body;

        Assert.True(live.Contains("\"sampling\":true"), "a sampling route says so");
        Assert.True(live.Contains("beam"), "the measured class is reported");
        Assert.True(live.Contains("Health"), "the changed field is named");
        Assert.True(live.Contains("bitsPerChangedWord\":39"), "the wire unit is stated in the reply");
        Assert.True(live.Contains("fieldsNote"), "and so is the shared-word caveat");

        // Reset is reachable from the route, because a reader watching a panel
        // wants a clean window without restarting the game.
        _ = Reply(server, "/nettraffic?reset=1").Body;
        Assert.True(reset, "reset=1 clears the totals");
    }

    private static HttpReply Reply(DebugApiServer server, string path)
    {
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }

    private static byte[] Blob(int words) => new byte[words * 4];

    private static NetTrafficClass Row(NetTrafficSampler s, string className)
        => s.Report().Single(r => r.ClassName == className);

    /// <summary>
    /// An entity seen for the first time charges nothing, because it has no
    /// previous image to differ from. Charging it would report every spawn as
    /// the most expensive thing on the map.
    /// </summary>
    [Fact]
    public void FirstSightingIsFree()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);
        blob[0] = 0xFF;

        sampler.Observe("a", 1, blob, Layout);

        var row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 0, "a first sighting charges no words");
        Assert.True(row.Seen == 1, "but it is counted as seen");
        Assert.True(sampler.Tracked == 1, "and the entity is now tracked");
    }

    /// <summary>
    /// One byte changing costs a whole word on the wire. This is the correction
    /// to Part 13 and the reason both numbers are reported.
    /// </summary>
    [Fact]
    public void WordsNotBytes()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);

        sampler.Observe("a", 1, blob, Layout);

        blob[0] = 1;                       // one byte, in word 0
        sampler.Observe("a", 1, blob, Layout);

        var row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 1, "one byte changed is one word touched");
        Assert.True(row.BytesChanged == 1, "and one byte changed");

        // Four bytes in the SAME word is still one word.
        blob[4] = 1; blob[5] = 2; blob[6] = 3; blob[7] = 4;
        sampler.Observe("a", 1, blob, Layout);

        row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 2, "four bytes of one word are still one word");
        Assert.True(row.BytesChanged == 5, "though five bytes have now changed in total");

        // One byte in each of two words is TWO words — half the bytes, twice
        // the wire cost. This is the pair that makes the distinction concrete.
        blob[8] = 1; blob[12] = 1;
        sampler.Observe("a", 1, blob, Layout);

        row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 4, "two bytes in two words cost two words");
    }

    /// <summary>
    /// Two fields in one word cost one word between them, however many of them
    /// moved. That is the whole argument for Part 10's packing.
    /// </summary>
    [Fact]
    public void SharedWordCountsOnce()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);

        sampler.Observe("a", 1, blob, Layout);

        blob[0] = 1;   // Wide
        blob[1] = 1;   // High — same word
        sampler.Observe("a", 1, blob, Layout);

        var row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 1, "two fields sharing a word cost one word between them");
    }

    [Fact]
    public void FieldAttribution()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);

        sampler.Observe("a", 1, blob, Layout);

        blob[4] = 9;                       // Health, alone in word 1
        sampler.Observe("a", 1, blob, Layout);

        var row = Row(sampler, "a");
        Assert.True(row.Fields.Any(f => f.Name == "Health"), "the changed field is named");
        Assert.True(row.Fields.Count(f => f.Words > 0) == 1, "and nothing else is charged for it");

        // A word holding two fields is charged to BOTH, because there is no
        // non-arbitrary way to split one word between them.
        blob[0] = 1;
        sampler.Observe("a", 1, blob, Layout);

        row = Row(sampler, "a");
        Assert.True(row.Fields.First(f => f.Name == "Wide").Words == 1
              && row.Fields.First(f => f.Name == "High").Words == 1, "a word shared by two fields is charged to both");

        // A field spanning several words is charged per word it touches, not
        // once for the field.
        var wide = new NetTrafficSampler();
        byte[] b2 = Blob(6);
        wide.Observe("a", 1, b2, Layout);

        b2[8] = 1; b2[12] = 1; b2[16] = 1;   // all three words of Where
        wide.Observe("a", 1, b2, Layout);

        Assert.True(Row(wide, "a").Fields.First(f => f.Name == "Where").Words == 3, "a field spanning three words is charged three");
    }

    [Fact]
    public void UnchangedCostsNothing()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);
        blob[0] = 7;

        sampler.Observe("a", 1, blob, Layout);

        for (int i = 0; i < 100; i++)
            sampler.Observe("a", 1, blob, Layout);

        var row = Row(sampler, "a");
        Assert.True(row.WordsTouched == 0, "a hundred unchanged frames cost nothing");
        Assert.True(row.ChangedFrames == 0, "and none of them counts as a changed frame");
        Assert.True(row.Seen == 101, "though all of them count as seen");
    }

    /// <summary>
    /// Reset clears the totals and KEEPS the images. Dropping them would make
    /// the frame after a reset charge every entity its whole payload, so asking
    /// for a clean window would produce one enormous sample.
    /// </summary>
    [Fact]
    public void ResetKeepsSnapshots()
    {
        var sampler = new NetTrafficSampler();
        byte[] blob = Blob(4);

        sampler.Observe("a", 1, blob, Layout);
        blob[0] = 1;
        sampler.Observe("a", 1, blob, Layout);

        Assert.True(Row(sampler, "a").WordsTouched == 1, "something was measured");

        sampler.Reset();
        Assert.True(sampler.Report().Count == 0, "reset clears the totals");
        Assert.True(sampler.Tracked == 1, "and keeps the snapshot");

        sampler.Observe("a", 1, blob, Layout);
        Assert.True(Row(sampler, "a").WordsTouched == 0, "so the next unchanged frame still costs nothing");

        sampler.Clear();
        Assert.True(sampler.Tracked == 0, "clear drops the snapshots too");
    }

    /// <summary>
    /// The bytes-per-second figure is Part 1B's arithmetic and not a guess: 7
    /// bits of delta index plus 32 of payload, per changed word.
    /// </summary>
    [Fact]
    public void WireCostIsPart1BsArithmetic()
    {
        Assert.True(NetTrafficSampler.BitsPerChangedWord == 39, "a changed word is priced at 39 bits");

        // A clock this test advances, so the rate is exact. It read the wall
        // clock and allowed 50%, which a pause under parallel test legs
        // exceeded: the rate was a few milliseconds' worth, read against a
        // second clock reading taken later.
        double now = 100.0;
        var sampler = new NetTrafficSampler(() => now);
        byte[] blob = Blob(4);
        sampler.Observe("a", 1, blob, Layout);

        blob[0] = 1;
        sampler.Observe("a", 1, blob, Layout);

        now += 2.0;
        var row = Row(sampler, "a");

        // One changed word, 39 bits, over two seconds: 4.875 bytes a second.
        Assert.True(row.WordsTouched == 1 && row.BytesPerSecond == 39 / 8.0 / 2.0,
                    $"the rate is words times 39 bits over elapsed ({row.WordsTouched} word(s), {row.BytesPerSecond} B/s)");
    }

    //-------------------------------------------------------------------------
    // The profiler: one per server module, cleared by its own level step.
    //-------------------------------------------------------------------------

    private static NetTrafficProfiler Profiler()
    {
        var engine = new RecordingEngine()
            .Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());
        return new NetTrafficProfiler(engine, engine);
    }

    [Fact]
    public void AnEnabledFrameMakesTheProfilerSample()
    {
        var profiler = Profiler();

        profiler.Frame(enabled: true, _ => null);

        Assert.True(profiler.IsSampling, "a frame with the convar on samples");
    }

    [Fact]
    public void TheProfilerClearsItselfAtLevelShutdown()
    {
        var profiler = Profiler();
        profiler.Frame(enabled: true, _ => null);

        ((IGameLifecycle)profiler).OnLevelShutdown();

        Assert.False(profiler.IsSampling, "its own level-shutdown step drops the sampling state");
    }

    [Fact]
    public void TwoProfilersDoNotShareASample()
    {
        // The property the static class lacked: one module's frame is not
        // another module's measurement.
        var mine = Profiler();
        var theirs = Profiler();

        theirs.Frame(enabled: true, _ => null);

        Assert.False(mine.IsSampling, "another profiler's frame does not make this one sample");
        Assert.True(theirs.IsSampling, "it samples in its own");
    }
}
