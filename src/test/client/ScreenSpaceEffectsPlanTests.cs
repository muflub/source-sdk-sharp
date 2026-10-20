using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where the screen-space effects reach the frame (C7 screen-space effects):
/// the plan asks the managed manager's RenderEffects at stock's place in
/// CViewRender::RenderView (after GrabPreColorCorrectedFrame, unless Hammer is
/// running), with the view's size, and the effects' own ops land there; the
/// view's level start and end run the manager's Init and Shutdown through
/// IManagedRenderClient::ScreenSpaceEffectsStage.
/// </summary>
public class ScreenSpaceEffectsPlanTests
{
    private sealed class Marker : IScreenSpaceEffect
    {
        public bool On;
        public readonly List<string> Calls = [];
        public (int X, int Y, int W, int H) Asked;

        public void Init() => Calls.Add("Init");
        public void Shutdown() => Calls.Add("Shutdown");
        public void SetParameters(KeyValuesNode? parameters) { }
        public void Enable(bool enable) => On = enable;
        public bool IsEnabled() => On;

        public void Render(int x, int y, int w, int h, RenderPlanBuilder plan)
        {
            Asked = (x, y, w, h);
            if (On)
                plan.Op(RenderOpKind.DebugQuad);
        }
    }

    private static (List<RenderOpKind> Kinds, Marker Effect) Plan(bool on, bool hammer)
    {
        var effect = new Marker { On = on };
        var m = new ScreenSpaceEffectManager(_ => { }, _ => false);
        m.Register("marker", effect);
        RenderInputs input = RenderPlannerTests.Inputs();
        input.IsHammerRunning = hammer ? 1 : 0;
        // A view off the screen's corner: stock asks the effects at 0, 0 whatever the view's origin.
        ViewSetup view = input.View;
        view.X = 10;
        view.Y = 20;
        input.View = view;
        var b = new RenderPlanBuilder();
        Assert.True(RenderPlanner.Plan(input, b, default, DebugOverlayMaterials.None, m));
        return (b.Ops.Select(o => (RenderOpKind)o.Kind).ToList(), effect);
    }

    [Fact]
    public void AnEnabledEffectsOpsLandWhereStocksRenderEffectsDrew()
    {
        (List<RenderOpKind> kinds, Marker effect) = Plan(on: true, hammer: false);
        int grab = kinds.IndexOf(RenderOpKind.GrabPreColorCorrectedFrame);
        Assert.True(grab >= 0, "the plan grabbed the pre-colour-corrected frame");
        Assert.Equal(RenderOpKind.DebugQuad, kinds[grab + 1]);
        Assert.Equal(1, kinds.Count(k => k == RenderOpKind.DebugQuad));
        Assert.Equal((0, 0, 1280, 720), effect.Asked);
    }

    [Fact]
    public void ADisabledEffectAddsNothing()
    {
        (List<RenderOpKind> on, _) = Plan(on: true, hammer: false);
        (List<RenderOpKind> off, Marker effect) = Plan(on: false, hammer: false);
        Assert.Equal((0, 0, 1280, 720), effect.Asked);
        Assert.Equal(on.Count - 1, off.Count);
        Assert.DoesNotContain(RenderOpKind.DebugQuad, off);
    }

    [Fact]
    public void WithHammerRunningNoEffectIsAsked()
    {
        (List<RenderOpKind> kinds, Marker effect) = Plan(on: true, hammer: true);
        Assert.Equal(default, effect.Asked);
        Assert.DoesNotContain(RenderOpKind.DebugQuad, kinds);
    }

    [Fact]
    public void TheLevelStagesRunTheManagersInitAndShutdownAndAreCounted()
    {
        var effect = new Marker();
        var m = new ScreenSpaceEffectManager(_ => { }, _ => false);
        m.Register("marker", effect);
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(),
                                              new RenderPlanHost(screenEffects: m));
        binding.ScreenSpaceEffectsStage(0);
        binding.ScreenSpaceEffectsStage(1);
        Assert.Equal(["Init", "Shutdown"], effect.Calls);
        Assert.Equal(2, crossings.Read(CrossingOp.ClientScreenSpaceEffectsStage));
    }
}
