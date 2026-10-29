using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The frame's interpolation gate, from stock's
/// <c>C_BaseEntity::InterpolateServerEntities</c> (c_baseentity.cpp): off for
/// cl_interpolate 0, timedemo playback, a paused engine, and a net channel
/// silent for more than half a second outside demo playback.
/// </summary>
public class InterpolationGateTests
{
    [Fact]
    public void AHealthyConnectionInterpolates()
        => Assert.True(InterpolationGate.Decide(true, false, false, false, 0.01f));

    [Fact]
    public void ClInterpolateOffStopsInterpolation()
        => Assert.False(InterpolationGate.Decide(false, false, false, false, 0.01f));

    [Fact]
    public void ATimedemoStopsInterpolation()
        => Assert.False(InterpolationGate.Decide(true, true, false, true, 0.01f));

    [Fact]
    public void APausedEngineStopsInterpolation()
        => Assert.False(InterpolationGate.Decide(true, false, true, false, 0.01f));

    [Fact]
    public void AChannelSilentForMoreThanHalfASecondStopsInterpolation()
        => Assert.False(InterpolationGate.Decide(true, false, false, false, 0.51f));

    [Fact]
    public void HalfASecondExactlyIsNotAStall()
        => Assert.True(InterpolationGate.Decide(true, false, false, false, InterpolationGate.StallSeconds));

    [Fact]
    public void ASilentChannelDuringDemoPlaybackIsNotAStall()
        => Assert.True(InterpolationGate.Decide(true, false, false, true, 5f));

    [Fact]
    public void NoNetChannelIsNotAStall()
        => Assert.True(InterpolationGate.Decide(true, false, false, false, null));

    [Fact]
    public void ClInterpolatesDefaultIsOn()
        => Assert.True(InterpolationGate.ConVarOn("1.0f"));

    [Fact]
    public void ClInterpolateZeroIsOff()
        => Assert.False(InterpolationGate.ConVarOn("0"));

    [Fact]
    public void ClInterpolateTruncatesLikeConVarGetInt()
        => Assert.False(InterpolationGate.ConVarOn("0.5"));

    [Fact]
    public void AConVarTheEngineDoesNotAnswerIsItsDeclaredDefault()
        => Assert.True(InterpolationGate.ConVarOn(null));

    [Fact]
    public void OpenClosesOnTheChannelsSilence()
    {
        var channel = new FakeChannel { SinceLastReceived = 2f };
        var gate = new InterpolationGate(new FakePlayback(), channel, _ => "1");

        Assert.False(gate.Open());
    }

    [Fact]
    public void OpenReadsTheConVarByItsProtocolName()
    {
        string? asked = null;
        var gate = new InterpolationGate(new FakePlayback(), new FakeChannel(), name => { asked = name; return "0"; });

        Assert.False(gate.Open());
        Assert.Equal("cl_interpolate", asked);
    }

    [Fact]
    public void OpenDoesNotAskTheChannelDuringDemoPlayback()
    {
        var channel = new FakeChannel { SinceLastReceived = 2f };
        var gate = new InterpolationGate(new FakePlayback { IsPlayingDemo = true }, channel, _ => "1");

        Assert.True(gate.Open());
        Assert.Equal(0, channel.Asked);
    }

    [Fact]
    public void OpenClosesWhileTheEngineIsPaused()
    {
        var gate = new InterpolationGate(new FakePlayback { IsPaused = true }, new FakeChannel(), _ => "1");

        Assert.False(gate.Open());
    }

    /// <summary>
    /// THE CALL IS THE GATE. Without the answer passed to the render pass the
    /// game interpolates through every stall, as it did while the native gate
    /// computed a flag nothing read; so the RenderStart stage is read for it.
    /// </summary>
    [Fact]
    public void TheRenderStartPassIsHandedTheGatesAnswer()
    {
        string module = File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));
        int stage = module.IndexOf("case ClientFrameStage.RenderStart:", StringComparison.Ordinal);
        Assert.True(stage >= 0, "ClientModule has no RenderStart stage");
        int end = module.IndexOf("break;", stage, StringComparison.Ordinal);
        int pass = module.IndexOf("_interpolation.RenderPass(_interpolationBlocks, _clock.Now, _interpolationGate?.Open() ?? true);",
                                  stage, StringComparison.Ordinal);
        Assert.True(pass > stage && pass < end, "the RenderStart stage does not hand the render pass the gate's answer");
        Assert.Contains("_interpolationGate = new InterpolationGate(", module);
    }

    private sealed class FakePlayback : IPlaybackState
    {
        public bool IsPaused { get; init; }

        public bool IsPlayingDemo { get; init; }

        public bool IsPlayingTimeDemo { get; init; }
    }

    private sealed class FakeChannel : IClientNetChannel
    {
        public float SinceLastReceived { get; init; }

        public int Asked { get; private set; }

        public ClientNetChannelRow? Channel()
        {
            Asked++;
            return new ClientNetChannelRow(0f, 0f, 0, 0, false, false, SinceLastReceived, 0f, 0f, 0f, 0f);
        }

        public ClientNetFlowRow? Flow(int flow) => null;

        public ClientNetPacketRow? Packet(int flow, int sequence) => null;
    }
}
