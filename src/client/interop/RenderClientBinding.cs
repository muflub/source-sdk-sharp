using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedRenderClient"/>, managed-built (plan_client_novalve C7):
/// View_Render asks it once a frame which path draws the frame, and hands a
/// recorded frame's text back. Every body counts its crossing; a failure
/// answers stock, unrecorded (what the zeroed answer native starts from says).
/// </summary>
public sealed unsafe class RenderClientBinding(Crossings crossings, ILog log, IClientRenderPlan plan)
    : IManagedRenderClient
{
    public int RenderFrame(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientRenderFrame);
        if (a0 == null || a1 == null)
            return 0;
        try
        {
            var answer = default(RenderFrameOut);
            plan.Frame(in *(RenderFrameIn*)a0, ref answer);
            *(RenderFrameOut*)a1 = answer;
            return 1;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.RenderFrame", e);
            *(RenderFrameOut*)a1 = default;
            return 0;
        }
    }

    public int RenderPlanBuild(void* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientRenderPlanBuild);
        if (a0 == null || a1 == null || a2 <= 0)
            return -1;
        try
        {
            return plan.BuildPlan(in *(RenderInputs*)a0, new Span<RenderOp>(a1, a2));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.RenderPlanBuild", e);
            return -1;
        }
    }

    public int RenderViewProgram(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientRenderViewProgram);
        if (a0 == null || a1 <= 0 || a2 == null || a3 <= 0)
            return -1;
        try
        {
            return plan.BuildViewProgram(new ReadOnlySpan<byte>(a0, a1), new Span<RenderOp>(a2, a3));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.RenderViewProgram", e);
            return -1;
        }
    }

    public int RenderShadowDepthPlan(void* a0, void* a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientRenderShadowDepthPlan);
        if (a0 == null || a1 == null || a2 <= 0 || a3 == null)
            return -1;
        try
        {
            int n = plan.BuildShadowDepthPlan(in *(ViewSetup*)a0, new Span<RenderOp>(a1, a2), out int indexBase);
            *(int*)a3 = indexBase;
            return n;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.RenderShadowDepthPlan", e);
            return -1;
        }
    }

    public void ViewSetUp(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientViewSetUp);
        if (a0 == null || a1 == null)
            return;
        try { plan.ViewSetUp(in *(ViewSetUpIn*)a0, out *(ViewSetUpOut*)a1); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewSetUp", e); }
    }

    public void ViewScreenshot(void* a0, int a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientViewScreenshot);
        if (a0 == null || a3 == null || a1 <= 0 || a2 <= 0)
            return;
        try { *(ViewSetup*)a3 = plan.ViewScreenshot(in *(ViewSetup*)a0, a1, a2); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewScreenshot", e); }
    }

    public void ViewFreezeFrame(float a0, float a1)
    {
        crossings.Count(CrossingOp.ClientViewFreezeFrame);
        try { plan.ViewFreezeFrame(a0, a1); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewFreezeFrame", e); }
    }

    public int ViewDemoCamera(int a0, void* a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientViewDemoCamera);
        if (a1 == null || a2 == null)
            return 0;
        try { return plan.ViewDemoCamera(a0, new ReadOnlySpan<float>(a1, 6), new Span<float>(a2, 6)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.ViewDemoCamera", e);
            return 0;
        }
    }

    public int RenderPostProgram(void* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientRenderPostProgram);
        if (a0 == null || a1 == null || a2 <= 0)
            return -1;
        try
        {
            return plan.BuildPostProgram(in *(PostProcessInputs*)a0, new Span<RenderOp>(a1, a2));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedRenderClient.RenderPostProgram", e);
            return -1;
        }
    }

    public void ViewEffectsFade(int a0, int a1, int a2, int a3, int a4, int a5, int a6, float a7)
    {
        crossings.Count(CrossingOp.ClientViewEffectsFade);
        try { plan.ViewEffectsFade(a0, a1, a2, (byte)a3, (byte)a4, (byte)a5, (byte)a6, a7); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewEffectsFade", e); }
    }

    public void ViewEffectsCalcShake(float a0, float a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientViewEffectsCalcShake);
        if (a2 == null)
            return;
        var output = new Span<float>(a2, 4);
        output.Clear();
        try { plan.ViewEffectsCalcShake(a0, a1, output); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewEffectsCalcShake", e); }
    }

    public void ViewEffectsFadeParams(float a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientViewEffectsFadeParams);
        if (a1 == null)
            return;
        var output = new Span<int>(a1, 5);
        output.Clear();
        try { plan.ViewEffectsFadeParams(a0, output); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewEffectsFadeParams", e); }
    }

    public void ScreenSpaceEffectsStage(int a0)
    {
        crossings.Count(CrossingOp.ClientScreenSpaceEffectsStage);
        try { plan.ScreenSpaceEffectsStage(a0); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ScreenSpaceEffectsStage", e); }
    }

    public void ViewEffectsReset(int a0)
    {
        crossings.Count(CrossingOp.ClientViewEffectsReset);
        try { plan.ViewEffectsReset(a0); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.ViewEffectsReset", e); }
    }

    public void RenderTraceDone(int a0, byte* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientRenderTraceDone);
        try
        {
            string text = a1 == null || a2 <= 0 ? string.Empty : Encoding.UTF8.GetString(a1, a2);
            plan.TraceDone(a0, text);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedRenderClient.RenderTraceDone", e); }
    }
}
