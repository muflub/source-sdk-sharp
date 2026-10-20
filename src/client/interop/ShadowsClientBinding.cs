using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedShadowsClient"/>, managed-built (C7 cluster 12 S2):
/// kClientSystems[]'s CClientShadowMgr row, the plan's shadow ops and the
/// restore callback (shell_shadows_client.cpp) ask the managed shadow manager.
/// Each body counts its crossing first and keeps its own catch; a failure
/// does nothing (and Init's refuses nothing).
/// </summary>
public sealed unsafe class ShadowsClientBinding(Crossings crossings, ILog log, IClientShadowsHost host) : IManagedShadowsClient
{
    private const int PendingWords = 3;

    private void Fail(string slot, Exception e) => ManagedVtable.Fail(log, "IManagedShadowsClient." + slot, e);

    private static ReadOnlySpan<int> Entries(void* p, int count)
        => p == null || count <= 0 ? [] : new ReadOnlySpan<int>(p, PendingWords * count);

    public int Init()
    {
        crossings.Count(CrossingOp.ClientShadowsInit);
        try { return host.Init() ? 1 : 0; }
        catch (Exception e)
        {
            Fail(nameof(Init), e);
            return 1;
        }
    }

    public void Shutdown()
    {
        crossings.Count(CrossingOp.ClientShadowsShutdown);
        try { host.Shutdown(); }
        catch (Exception e) { Fail(nameof(Shutdown), e); }
    }

    public void LevelInitPreEntity()
    {
        crossings.Count(CrossingOp.ClientShadowsLevelInitPreEntity);
        try { host.LevelInitPreEntity(); }
        catch (Exception e) { Fail(nameof(LevelInitPreEntity), e); }
    }

    public void LevelShutdownPostEntity()
    {
        crossings.Count(CrossingOp.ClientShadowsLevelShutdownPostEntity);
        try { host.LevelShutdownPostEntity(); }
        catch (Exception e) { Fail(nameof(LevelShutdownPostEntity), e); }
    }

    public void PreRender(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientShadowsPreRender);
        try { host.PreRender(Entries(a0, a1)); }
        catch (Exception e) { Fail(nameof(PreRender), e); }
    }

    public void AdvanceFrame(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientShadowsAdvanceFrame);
        try { host.AdvanceFrame(Entries(a0, a1)); }
        catch (Exception e) { Fail(nameof(AdvanceFrame), e); }
    }

    public void ComputeShadowTextures(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientShadowsComputeShadowTextures);
        try
        {
            var leaves = new int[Math.Max(0, a1)];
            if (a0 != null)
            {
                var source = new ReadOnlySpan<ushort>(a0, leaves.Length);
                for (int i = 0; i < leaves.Length; i++)
                    leaves[i] = source[i];
            }
            host.ComputeShadowTextures(leaves, Entries(a2, a3));
        }
        catch (Exception e) { Fail(nameof(ComputeShadowTextures), e); }
    }

    public void ComputeShadowDepthTextures(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientShadowsComputeShadowDepthTextures);
        if (a0 == null || a1 < 10)
            return;
        try
        {
            float* v = (float*)a0;
            var view = new ShadowViewSetup
            {
                Origin = new Vector(v[0], v[1], v[2]),
                Angles = new QAngle(v[3], v[4], v[5]),
                ZNear = v[6],
                ZFar = v[7],
                Fov = v[8],
                AspectRatio = v[9],
            };
            host.ComputeShadowDepthTextures(view, Entries(a2, a3));
        }
        catch (Exception e) { Fail(nameof(ComputeShadowDepthTextures), e); }
    }

    public void UnlockAllShadowDepthTextures(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientShadowsUnlockAllShadowDepthTextures);
        try { host.UnlockAllShadowDepthTextures(Entries(a0, a1)); }
        catch (Exception e) { Fail(nameof(UnlockAllShadowDepthTextures), e); }
    }

    public void RestoreRenderState()
    {
        crossings.Count(CrossingOp.ClientShadowsRestoreRenderState);
        try { host.RestoreRenderState(); }
        catch (Exception e) { Fail(nameof(RestoreRenderState), e); }
    }

    public void RenderShadowTexture(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientShadowsRenderShadowTexture);
        try { host.RenderShadowTexture(a0, a1); }
        catch (Exception e) { Fail(nameof(RenderShadowTexture), e); }
    }

    public void Pending(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientShadowsPending);
        try { host.Pending(Entries(a0, a1)); }
        catch (Exception e) { Fail(nameof(Pending), e); }
    }
}
