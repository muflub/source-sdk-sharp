using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What reaches the managed view effects (C7): the Shake and Fade messages,
/// native's calls through the render client (the fade, CalcShake, GetFadeParams,
/// the level reset), and CalcShake's rumble angle to the mixer.
/// </summary>
public class ViewEffectsHostTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private sealed class Half : IUniformRandom
    {
        public float RandomFloat(float min, float max) => 0.5f * max;

        public int RandomInt(int min, int max) => min;

    }

    private static (ViewEffectsHost Host, RenderPlanHost Plan, Clock Clock, List<float> Rumble) Rig()
    {
        var clock = new Clock { Now = 1f };
        var rumble = new List<float>();
        var host = new ViewEffectsHost(new Half(), clock, () => false, new DefaultForwarders.NoEngineClient(), rumble.Add);
        var plan = new RenderPlanHost(new PostProcessHost(new DefaultForwarders.NoEngineClient(), new ListHooks<IPostProcessContributor>()), host);
        return (host, plan, clock, rumble);
    }

    [Fact]
    public void TheShakeMessageShakesTheViewNativeAsksForAndFeedsTheRumble()
    {
        var (host, plan, _, rumble) = Rig();
        var effects = new ClientMessageEffects(new SourceSharp.Abi.RecordingEngine(), viewEffects: () => host, clock: new Clock { Now = 1f });
        effects.Shake(new Shake { Command = (byte)ShakeCommand.Start, Amplitude = 4, Frequency = 10, Duration = 2 });
        Assert.Equal(1, host.Effects.ShakeCount);

        Span<float> shake = stackalloc float[4];
        ((IClientRenderPlan)plan).ViewEffectsCalcShake(1.3f, 0.01f, shake);
        Assert.NotEqual(0f, shake[3]);
        Assert.Equal(host.Effects.AppliedAngle, shake[3]);
        Assert.Single(rumble);
        Assert.NotEqual(0f, rumble[0]);
    }

    [Fact]
    public void AFadeFromNativeOrTheMessageIsWhatGetFadeParamsAnswers()
    {
        var (host, plan, _, _) = Rig();
        IClientRenderPlan client = plan;
        // No duration: stock leaves End and Reset as given (0 and 1 s), not offset by the time.
        client.ViewEffectsFade(0, 512, (int)FadeFlags.Modulate, 10, 20, 30, 40, 0.25f);
        Span<int> fade = stackalloc int[5];
        client.ViewEffectsFadeParams(0.5f, fade);
        Assert.Equal([10, 20, 30, 40, 1], fade.ToArray());

        client.ViewEffectsReset(0);
        client.ViewEffectsFadeParams(0.5f, fade);
        Assert.Equal([0, 0, 0, 0, 0], fade.ToArray());

        var effects = new ClientMessageEffects(new SourceSharp.Abi.RecordingEngine(), viewEffects: () => host, clock: new Clock { Now = 2f });
        effects.Fade(new Fade { Duration = 0, HoldTime = 512, Flags = 0, Color = new Color32(1, 2, 3, 4) });
        client.ViewEffectsFadeParams(0.5f, fade);
        Assert.Equal([1, 2, 3, 4, 0], fade.ToArray());
    }

    [Fact]
    public void ShakeStopClearsEveryShake()
    {
        var (host, _, _, _) = Rig();
        host.Shake(ShakeCommand.Start, 4, 10, 2);
        host.Shake(ShakeCommand.Start, 4, 10, 2);
        new ShakeStopCommand(host).Execute("");
        Assert.Equal(0, host.Effects.ShakeCount);
    }
}
