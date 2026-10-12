extern alias client;
using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client.Interface;
using client::SourceSharp.Client;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>frame</c> route (plan_client_novalve §4.4): what CFPSPanel
/// drew and cl_showpos printed, read through the client's real services.
/// </summary>
public class ClientFrameRouteTests
{
    /// <summary>A clock a test moves by hand.</summary>
    internal sealed class ManualTime : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }

    private sealed class FixedClock(float now) : IGameClock
    {
        public float Now => now;
    }

    [Fact]
    public void The_window_holds_the_last_second_only()
    {
        var time = new ManualTime();
        var window = new FrameTimeWindow(time);

        window.Add(0.050f);           // t=0, 50 ms: leaves the window below
        time.Now = 600;
        window.Add(0.010f);
        time.Now = 900;
        window.Add(0.020f);

        Assert.Equal((3, 10.0, 26.667, 50.0), Rounded(window.Read()));

        time.Now = 1200;              // the first frame is 1.2 s old
        Assert.Equal((2, 10.0, 15.0, 20.0), Rounded(window.Read()));

        time.Now = 5000;
        Assert.Equal((0, 0.0, 0.0, 0.0), window.Read());
    }

    private static (int, double, double, double) Rounded((int F, double Min, double Avg, double Max) r)
        => (r.F, Math.Round(r.Min, 3), Math.Round(r.Avg, 3), Math.Round(r.Max, 3));

    [Fact]
    public void The_route_reads_the_frames_the_module_counted()
    {
        var stats = new ClientFrameStats();
        var time = new ManualTime();
        var window = new FrameTimeWindow(time);
        var view = new ViewReadout();

        for (int i = 0; i < 5; i++)
        {
            stats.CountFrame();
            stats.NoteStage(ClientFrameStage.Start);
            window.Add(0.016f);
            time.Now += 16;
        }
        view.PublishEye(new Vector(10, 20, 30));
        view.PublishAngles(5, 45);

        var frame = new ClientDebugFrame(stats, window, view, new FixedClock(3.25f));
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(frame: frame));
        using var json = JsonDocument.Parse(client.Answer(DebugChannelQuery.Frame));
        var root = json.RootElement;

        // The positive arm: non-zero, and exactly what was driven.
        Assert.Equal(5, root.GetProperty("frames").GetInt32());
        Assert.Equal(5, root.GetProperty("stages").GetProperty("Start").GetInt64());
        Assert.Equal(5, root.GetProperty("window").GetProperty("frames").GetInt32());
        Assert.Equal(16.0, root.GetProperty("window").GetProperty("frameMsAvg").GetDouble(), 3);
        Assert.True(root.GetProperty("pose").GetProperty("known").GetBoolean());
        Assert.Equal(20f, root.GetProperty("pose").GetProperty("eye")[1].GetSingle());
        Assert.Equal(3.25f, root.GetProperty("pose").GetProperty("time").GetSingle());
    }

    [Fact]
    public void A_game_thread_question_waits_for_the_pump()
    {
        Assert.True(DebugChannelClient.NeedsGameThread(DebugChannelQuery.Frame));
        Assert.False(DebugChannelClient.NeedsGameThread(DebugChannelQuery.Status));
    }
}
