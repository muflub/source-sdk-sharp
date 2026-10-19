using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The managed area IManagedLeafSystemClient (docs/managed-interfaces.tsv,
/// C7 cluster 12 L2): what libss_fwd_client's leaf system
/// (src/fwd/native/fwd_leaf_system.cpp) asks for each IClientLeafSystem call
/// the engine and client.so's C++ make. Each slot hands its arguments to the
/// managed leaf system (IClientLeafSystemHost); a slot ending in an operations buffer answers with
/// the operations the forwarder carries out (<see cref="LeafOp"/>, four int64
/// words each, as many as fit), the rest waiting for <see cref="TakeOps"/>.
/// A slot that throws is reported and answers nothing.
/// </summary>
public sealed unsafe class LeafSystemClientBinding(Crossings crossings, ILog log, IClientLeafSystemHost host)
    : IManagedLeafSystemClient
{
    private const int LeavesPerRenderable = 128;
    private const int InvalidHandle = 0xFFFF;
    // A fresh array per call: a slot's native batches may reach the leaf system again.
    private static int[] Leaves(void* p, int count)
    {
        if (count <= 0 || p == null)
            return [];
        var source = new ReadOnlySpan<ushort>(p, count);
        var leaves = new int[count];
        for (int i = 0; i < count; i++)
            leaves[i] = source[i];
        return leaves;
    }

    // The queued operations, as many as the buffer takes.
    private int Answer(void* ops, int maxOps)
    {
        if (ops == null || maxOps <= 0)
            return 0;
        Span<LeafOp> taken = maxOps <= 64 ? stackalloc LeafOp[maxOps] : new LeafOp[maxOps];
        int n = host.TakeOps(taken);
        var words = new Span<long>(ops, maxOps * LeafOp.Words);
        for (int i = 0; i < n; i++)
        {
            words[LeafOp.Words * i] = (long)taken[i].Kind;
            words[LeafOp.Words * i + 1] = taken[i].A;
            words[LeafOp.Words * i + 2] = taken[i].B;
            words[LeafOp.Words * i + 3] = taken[i].C;
        }
        return n;
    }

    private int Fail(string slot, Exception e)
    {
        ManagedVtable.Fail(log, "IManagedLeafSystemClient." + slot, e);
        return 0;
    }

    public void LevelInitPreEntity(int a0)
    {
        crossings.Count(CrossingOp.ClientLeafLevelInitPreEntity);
        try { host.LevelInitPreEntity(a0); }
        catch (Exception e) { Fail(nameof(LevelInitPreEntity), e); }
    }

    public int LevelShutdownPostEntity(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafLevelShutdownPostEntity);
        try
        {
            host.LevelShutdownPostEntity();
            return Answer(a0, a1);
        }
        catch (Exception e) { return Fail(nameof(LevelShutdownPostEntity), e); }
    }

    // The queue first (its entries are read before anything managed does can
    // queue more), then the frame's re-insert.
    public int PreRender(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientLeafPreRender);
        try
        {
            Queued(a0, a1);
            host.PreRender();
            return Answer(a2, a3);
        }
        catch (Exception e) { return Fail(nameof(PreRender), e); }
    }

    public int TakeOps(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafTakeOps);
        try { return Answer(a0, a1); }
        catch (Exception e) { return Fail(nameof(TakeOps), e); }
    }

    public int CreateRenderableHandle(void* a0, int a1, int a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientLeafCreateRenderableHandle);
        try { return host.CreateRenderableHandle((nint)a0, a1 != 0, a2, a3 != 0, a4 != 0); }
        catch (Exception e)
        {
            Fail(nameof(CreateRenderableHandle), e);
            return InvalidHandle;
        }
    }

    public int AddRenderable(void* a0, int a1, int a2)
    {
        crossings.Count(CrossingOp.ClientLeafAddRenderable);
        try { return host.AddRenderable((nint)a0, a1, a2); }
        catch (Exception e)
        {
            Fail(nameof(AddRenderable), e);
            return InvalidHandle;
        }
    }

    public int RemoveRenderable(int a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientLeafRemoveRenderable);
        try
        {
            host.RemoveRenderable(a0 & 0xFFFF);
            return Answer(a1, a2);
        }
        catch (Exception e) { return Fail(nameof(RemoveRenderable), e); }
    }

    public int AddRenderableToLeaves(int a0, void* a1, int a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientLeafAddRenderableToLeaves);
        try
        {
            host.AddRenderableToLeaves(a0 & 0xFFFF, Leaves(a1, a2));
            return Answer(a3, a4);
        }
        catch (Exception e) { return Fail(nameof(AddRenderableToLeaves), e); }
    }

    public void ChangeRenderableRenderGroup(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafChangeRenderableRenderGroup);
        try { host.ChangeRenderableRenderGroup(a0 & 0xFFFF, a1); }
        catch (Exception e) { Fail(nameof(ChangeRenderableRenderGroup), e); }
    }

    public void SetRenderGroup(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafSetRenderGroup);
        try { host.SetRenderGroup(a0 & 0xFFFF, a1); }
        catch (Exception e) { Fail(nameof(SetRenderGroup), e); }
    }

    public void RenderableChanged(int a0)
    {
        crossings.Count(CrossingOp.ClientLeafRenderableChanged);
        try { host.RenderableChanged(a0 & 0xFFFF); }
        catch (Exception e) { Fail(nameof(RenderableChanged), e); }
    }

    public void EnableAlternateSorting(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafEnableAlternateSorting);
        try { host.EnableAlternateSorting(a0 & 0xFFFF, a1 != 0); }
        catch (Exception e) { Fail(nameof(EnableAlternateSorting), e); }
    }

    public void DrawStaticProps(int a0)
    {
        crossings.Count(CrossingOp.ClientLeafDrawStaticProps);
        host.DrawStaticProps(a0 != 0);
    }

    public void DrawSmallEntities(int a0)
    {
        crossings.Count(CrossingOp.ClientLeafDrawSmallEntities);
        host.DrawSmallEntities(a0 != 0);
    }

    public int GetRenderableLeaves(int a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientLeafGetRenderableLeaves);
        if (a1 == null)
            return -1;
        try { return host.GetRenderableLeaves(a0 & 0xFFFF, new Span<int>(a1, LeavesPerRenderable)); }
        catch (Exception e)
        {
            Fail(nameof(GetRenderableLeaves), e);
            return -1;
        }
    }

    public int GetRenderableLeaf(int a0, int a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientLeafGetRenderableLeaf);
        if (a3 == null)
            return 0;
        try
        {
            if (!host.GetRenderableLeaf(a0 & 0xFFFF, a1 != 0, a2, out int leaf, out int iterator))
                return 0;
            ((int*)a3)[0] = leaf;
            ((int*)a3)[1] = iterator;
            return 1;
        }
        catch (Exception e) { return Fail(nameof(GetRenderableLeaf), e); }
    }

    public void* SetSubSystemDataInLeaf(int a0, int a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientLeafSetSubSystemDataInLeaf);
        try { return (void*)host.SetSubSystemDataInLeaf(a0, a1, (nint)a2); }
        catch (Exception e)
        {
            Fail(nameof(SetSubSystemDataInLeaf), e);
            return null;
        }
    }

    public void* GetSubSystemDataInLeaf(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafGetSubSystemDataInLeaf);
        return (void*)host.GetSubSystemDataInLeaf(a0, a1);
    }

    public void SetDetailObjectsInLeaf(int a0, int a1, int a2)
    {
        crossings.Count(CrossingOp.ClientLeafSetDetailObjectsInLeaf);
        host.SetDetailObjectsInLeaf(a0, a1, a2);
    }

    public int GetDetailObjectsInLeaf(int a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientLeafGetDetailObjectsInLeaf);
        if (a1 == null || !host.GetDetailObjectsInLeaf(a0, out int first, out int count))
            return 0;
        ((int*)a1)[0] = first;
        ((int*)a1)[1] = count;
        return 1;
    }

    public void DrawDetailObjectsInLeaf(int a0, int a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientLeafDrawDetailObjectsInLeaf);
        host.DrawDetailObjectsInLeaf(a0, a1, out int first, out int count);
        if (a2 == null)
            return;
        ((int*)a2)[0] = first;
        ((int*)a2)[1] = count;
    }

    public int ShouldDrawDetailObjectsInLeaf(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafShouldDrawDetailObjectsInLeaf);
        return host.ShouldDrawDetailObjectsInLeaf(a0, a1) ? 1 : 0;
    }

    public void DetailLeaves(int a0, void* a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientLeafDetailLeaves);
        if (a1 == null || a3 == null || a2 <= 0)
            return;
        var leaves = new ReadOnlySpan<ushort>(a1, a2);
        var answers = new Span<int>(a3, a2);
        for (int i = 0; i < a2; i++)
            answers[i] = host.ShouldDrawDetailObjectsInLeaf(leaves[i], a0) ? 1 : 0;
    }

    public void ComputeTranslucentRenderLeaf(void* a0, int a1, int a2, int a3, int a4, void* a5, void* a6, int a7)
    {
        crossings.Count(CrossingOp.ClientLeafComputeTranslucentRenderLeaf);
        try
        {
            Queued(a6, a7);
            host.ComputeTranslucentRenderLeaf(Leaves(a0, a1), a2, a3, a4, (nint)a5);
        }
        catch (Exception e) { Fail(nameof(ComputeTranslucentRenderLeaf), e); }
    }

    // nFlags: 1 draw detail objects, 2 draw translucent objects, 4 cl_drawleaf
    // filters (a debug build's stock code).
    public int BuildRenderablesList(void* a0, int a1, void* a2, int a3, int a4, int a5, void* a6, void* a7, int a8, void* a9, int a10)
    {
        crossings.Count(CrossingOp.ClientLeafBuildRenderablesList);
        try
        {
            Queued(a9, a10);
            if (a2 == null)
                return 0;
            var of = new ReadOnlySpan<float>(a2, 6);
            var view = new LeafView(Leaves(a0, a1), a3, a4, (a5 & 1) != 0, (a5 & 2) != 0,
                                    of[0], of[1], of[2], of[3], of[4], of[5], (nint)a6, (a5 & 4) != 0);
            host.BuildRenderablesList(view);
            return Answer(a7, a8);
        }
        catch (Exception e) { return Fail(nameof(BuildRenderablesList), e); }
    }

    public int CollateViewModelRenderables(void* a0, int a1, void* a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientLeafCollateViewModelRenderables);
        try
        {
            Queued(a3, a4);
            host.CollateViewModelRenderables((nint)a2);
            return Answer(a0, a1);
        }
        catch (Exception e) { return Fail(nameof(CollateViewModelRenderables), e); }
    }

    public int AddShadow(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafAddShadow);
        try { return host.AddShadow(a0, a1); }
        catch (Exception e)
        {
            Fail(nameof(AddShadow), e);
            return InvalidHandle;
        }
    }

    public void RemoveShadow(int a0)
    {
        crossings.Count(CrossingOp.ClientLeafRemoveShadow);
        try { host.RemoveShadow(a0 & 0xFFFF); }
        catch (Exception e) { Fail(nameof(RemoveShadow), e); }
    }

    public int ProjectShadow(int a0, void* a1, int a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientLeafProjectShadow);
        try
        {
            host.ProjectShadow(a0 & 0xFFFF, a1 == null ? [] : new ReadOnlySpan<int>(a1, Math.Max(a2, 0)));
            return Answer(a3, a4);
        }
        catch (Exception e) { return Fail(nameof(ProjectShadow), e); }
    }

    public int ProjectFlashlight(int a0, void* a1, int a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientLeafProjectFlashlight);
        try
        {
            host.ProjectShadow(a0 & 0xFFFF, a1 == null ? [] : new ReadOnlySpan<int>(a1, Math.Max(a2, 0)));
            return Answer(a3, a4);
        }
        catch (Exception e) { return Fail(nameof(ProjectFlashlight), e); }
    }

    public int EnumerateShadowsInLeaves(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientLeafEnumerateShadowsInLeaves);
        try
        {
            host.EnumerateShadowsInLeaves(Leaves(a0, a1));
            return Answer(a2, a3);
        }
        catch (Exception e) { return Fail(nameof(EnumerateShadowsInLeaves), e); }
    }

    // The forwarder's queue (LeafPending) a slot carries in its arguments,
    // applied before the slot's own work.
    private void Queued(void* entries, int count)
    {
        if (entries != null && count > 0)
            host.ApplyPending(new ReadOnlySpan<int>(entries, LeafPending.Words * count));
    }

    // The calls the forwarder queued (L3, LeafPending), in order, when a slot
    // other than PreRender is called with entries queued or the queue fills.
    public void Pending(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientLeafPending);
        if (a0 == null || a1 <= 0)
            return;
        try { host.ApplyPending(new ReadOnlySpan<int>(a0, LeafPending.Words * a1)); }
        catch (Exception e) { Fail(nameof(Pending), e); }
    }
}
