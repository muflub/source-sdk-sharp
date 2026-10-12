using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client winds (lane c2-sims): a frame thinks every due wind and
/// publishes the head wind's vector, the count and the last thinking wind's
/// sway; a wind whose gusts cannot advance never thinks; the list keeps
/// creation order through updates; the entity carries its wind through a
/// reload and re-joins the list.
/// </summary>
public class ClientWindsTests
{
    private sealed class Publisher : IClientWindPublisher
    {
        public readonly List<(Vector Wind, Vector? Sway, int Winds)> Calls = new();

        public void Publish(Vector wind, Vector? sway, int winds) => Calls.Add((wind, sway, winds));
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private static ClientWind Wind(IClientWinds winds, uint index, float delay)
    {
        var engine = new RecordingEngine();
        var wind = new ClientWind(engine, winds);
        wind.BindEngine(engine, engine);
        wind.State.BindLocal();
        wind.Handle = new EntityHandle(index);
        wind.State.MinWind = 20;
        wind.State.MaxWind = 60;
        wind.State.MinGust = 80;
        wind.State.MaxGust = 150;
        wind.State.MinGustDelay = delay;
        wind.State.MaxGustDelay = delay;
        wind.State.GustDuration = delay;
        wind.State.GustDirChange = 30;
        wind.State.InitialWindDir = 90;
        wind.State.StartTime = 10f;
        return wind;
    }

    [Fact]
    public void AFrameThinksAndPublishesTheHeadWind()
    {
        var publisher = new Publisher();
        var clock = new Clock();
        var winds = new ClientWinds(publisher, clock);
        var first = Wind(winds, 91, 0.5f);
        var second = Wind(winds, 92, 0.7f);
        first.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        second.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        for (int f = 0; f < 60; f++)
        {
            clock.Now = 10f + f * 0.05f;
            winds.Frame();
        }

        var last = publisher.Calls[^1];
        Assert.True(last.Winds == 2 && last.Wind.Equals(first.Wind.Sim.CurrentWind) && last.Wind.Length > 1f,
            $"the head wind's vector is published ({last.Wind.Length}), with the count");
        Assert.True(last.Sway is { } sway && sway.Equals(second.Wind.Sim.SwayParameter), "and the last thinking wind's sway");

        first.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        clock.Now += 0.05f;
        winds.Frame();
        Assert.True(publisher.Calls[^1].Wind.Equals(first.Wind.Sim.CurrentWind), "an update re-starts the wind and it keeps the head");

        first.OnRemoved();
        clock.Now += 0.05f;
        winds.Frame();
        Assert.True(publisher.Calls[^1].Winds == 1 && publisher.Calls[^1].Wind.Equals(second.Wind.Sim.CurrentWind), "a removal hands the head on");
    }

    [Fact]
    public void AWindWhoseGustsCannotAdvanceNeverThinks()
    {
        var publisher = new Publisher();
        var clock = new Clock();
        var winds = new ClientWinds(publisher, clock);
        var still = Wind(winds, 93, 0f);
        still.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        // Asked BEFORE any frame: an armed one would think, and stock's think
        // loops for ever on a switch that cannot advance.
        Assert.True(!still.Wind.Armed, "no delay and no duration: disarmed, as the native schedule was");
        clock.Now = 11f;
        winds.Frame();
        Assert.True(publisher.Calls[^1].Sway is null && publisher.Calls[^1].Wind.Equals(default(Vector)), "and it never thinks");
    }

    [Fact]
    public void AReloadCarriesTheWindAndRejoinsTheList()
    {
        var publisher = new Publisher();
        var clock = new Clock();
        var winds = new ClientWinds(publisher, clock);
        var before = Wind(winds, 94, 0.5f);
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        for (int f = 0; f < 30; f++)
        {
            clock.Now = 10f + f * 0.05f;
            winds.Frame();
        }

        var record = new SavedEntity(94u, before.ClassName, string.Empty);
        before.SaveState(record);

        var reloaded = new ClientWinds(publisher, clock);
        var after = Wind(reloaded, 94, 0.5f);
        Assert.True(reloaded.Count == 0 && after.Wind.Sim.SwitchTime == 0, "a rebuilt wind holds nothing and is not listed");
        after.LoadState(record);
        Assert.True(reloaded.Count == 1 && after.Wind.Sim.SwitchTime == before.Wind.Sim.SwitchTime, "the wind came across and re-joined");

        clock.Now += 0.05f;
        winds.Frame();
        reloaded.Frame();
        Assert.True(before.Wind.Sim.CurrentWind.Equals(after.Wind.Sim.CurrentWind), "and blows as the original does");
    }

    /// <summary>
    /// The think schedule the native CWindThinkSchedule kept (its gtest went
    /// with it): a think asks to be called a hundredth of a second on, so at 60
    /// Hz every frame thinks and at 400 Hz only every fourth or fifth (the
    /// float clock rounds).
    /// </summary>
    [Fact]
    public void AWindThinksAtMostEveryHundredthOfASecond()
    {
        var wind = new WindThinker();
        wind.Sim.MinWind = 20;
        wind.Sim.MaxWind = 60;
        wind.Sim.MinGustDelay = 0.5f;
        wind.Start(0, 10f, 0, 0f);

        int at60 = 0;
        for (int f = 0; f < 100; f++)
            at60 += wind.ThinkIfDue(10f + f / 60f) ? 1 : 0;

        var fast = new WindThinker();
        fast.Sim.MinWind = 20;
        fast.Sim.MaxWind = 60;
        fast.Sim.MinGustDelay = 0.5f;
        fast.Start(0, 10f, 0, 0f);
        int at400 = 0;
        for (int f = 0; f < 100; f++)
            at400 += fast.ThinkIfDue(10f + f / 400f) ? 1 : 0;

        Assert.True(at60 == 100 && at400 >= 20 && at400 <= 26, $"60 Hz: {at60} of 100; 400 Hz: {at400} of 100");
    }
}
