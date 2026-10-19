using System.Diagnostics;

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.LeafSystem;

/// <summary>
/// The managed client leaf system as the forwarder reaches it (C7 cluster 12
/// L2): the registry's calls and the builder's three decisions behind the one
/// surface <see cref="LeafSystemClientBinding"/> marshals to, each call timed
/// into <see cref="LeafSystemStats"/> (L3).
/// </summary>
public sealed class ClientLeafSystem(LeafRegistry registry, RenderableListBuilder builder, LeafSystemStats stats) : IClientLeafSystemHost
{
    public ClientLeafSystem(LeafRegistry registry, RenderableListBuilder builder) : this(registry, builder, new LeafSystemStats()) { }

    public LeafRegistry Registry => registry;

    public LeafSystemStats Stats => stats;

    private static long Now() => Stopwatch.GetTimestamp();

    public void LevelInitPreEntity(int leafCount)
    {
        long t = Now();
        registry.LevelInitPreEntity(leafCount);
        stats.Add(LeafSystemStats.Kind.Other, t);
    }

    public void LevelShutdownPostEntity()
    {
        long t = Now();
        registry.LevelShutdownPostEntity();
        stats.Add(LeafSystemStats.Kind.Other, t);
    }

    public void ApplyPending(ReadOnlySpan<int> entries)
    {
        long t = Now();
        registry.ApplyPending(entries);
        stats.Add(LeafSystemStats.Kind.Queued, t);
    }

    public void PreRender()
    {
        long t = Now();
        registry.PreRender();
        stats.Add(LeafSystemStats.Kind.PreRender, t);
    }

    public int TakeOps(Span<LeafOp> destination) => registry.TakeOps(destination);

    public int CreateRenderableHandle(nint renderable, bool staticProp, int modelType, bool transparent, bool twoPass)
    {
        long t = Now();
        int handle = registry.CreateRenderableHandle(renderable, staticProp, modelType, transparent, twoPass);
        stats.Add(LeafSystemStats.Kind.AddOrRemove, t);
        return handle;
    }

    public int AddRenderable(nint renderable, int group, int modelType)
    {
        long t = Now();
        int handle = registry.AddRenderable(renderable, group, modelType);
        stats.Add(LeafSystemStats.Kind.AddOrRemove, t);
        return handle;
    }

    public void RemoveRenderable(int handle)
    {
        long t = Now();
        registry.RemoveRenderable(handle);
        stats.Add(LeafSystemStats.Kind.AddOrRemove, t);
    }

    public void AddRenderableToLeaves(int handle, ReadOnlySpan<int> leaves)
    {
        long t = Now();
        registry.AddRenderableToLeaves(handle, leaves);
        stats.Add(LeafSystemStats.Kind.AddOrRemove, t);
    }

    public void ChangeRenderableRenderGroup(int handle, int group)
    {
        long t = Now();
        registry.ChangeRenderableRenderGroup(handle, group);
        stats.Add(LeafSystemStats.Kind.Other, t);
    }

    public void SetRenderGroup(int handle, int group)
    {
        long t = Now();
        registry.SetRenderGroup(handle, group);
        stats.Add(LeafSystemStats.Kind.Other, t);
    }

    public void RenderableChanged(int handle)
    {
        long t = Now();
        registry.RenderableChanged(handle);
        stats.Add(LeafSystemStats.Kind.Queued, t);
    }

    public void EnableAlternateSorting(int handle, bool enable) => registry.EnableAlternateSorting(handle, enable);

    public void DrawStaticProps(bool enable) => registry.DrawStaticProps(enable);

    public void DrawSmallEntities(bool enable) => registry.DrawSmallEntities(enable);

    public int GetRenderableLeaves(int handle, Span<int> leaves)
    {
        long t = Now();
        int n = registry.GetRenderableLeaves(handle, leaves);
        stats.Add(LeafSystemStats.Kind.Other, t);
        return n;
    }

    public bool GetRenderableLeaf(int handle, bool hasIterator, int iterator, out int leaf, out int nextIterator)
        => registry.GetRenderableLeaf(handle, hasIterator, iterator, out leaf, out nextIterator);

    public long SetSubSystemDataInLeaf(int leaf, int index, long data) => registry.SetSubSystemDataInLeaf(leaf, index, data);

    public long GetSubSystemDataInLeaf(int leaf, int index) => registry.GetSubSystemDataInLeaf(leaf, index);

    public void SetDetailObjectsInLeaf(int leaf, int first, int count) => registry.SetDetailObjectsInLeaf(leaf, first, count);

    public bool GetDetailObjectsInLeaf(int leaf, out int first, out int count) => registry.GetDetailObjectsInLeaf(leaf, out first, out count);

    public void DrawDetailObjectsInLeaf(int leaf, int frame, out int first, out int count)
    {
        long t = Now();
        registry.DrawDetailObjectsInLeaf(leaf, frame, out first, out count);
        stats.Add(LeafSystemStats.Kind.Detail, t);
    }

    public bool ShouldDrawDetailObjectsInLeaf(int leaf, int frame)
    {
        long t = Now();
        bool draw = registry.ShouldDrawDetailObjectsInLeaf(leaf, frame);
        stats.Add(LeafSystemStats.Kind.Detail, t);
        return draw;
    }

    public void ComputeTranslucentRenderLeaf(ReadOnlySpan<int> leaves, int frameNumber, int viewId, int globalFrame, nint system)
    {
        long t = Now();
        builder.ComputeTranslucentRenderLeaf(leaves, frameNumber, viewId, globalFrame, system);
        stats.Add(LeafSystemStats.Kind.TranslucentLeaf, t);
    }

    public void BuildRenderablesList(in LeafView view)
    {
        long t = Now();
        builder.BuildRenderablesList(view);
        stats.Add(LeafSystemStats.Kind.BuildList, t);
    }

    public void CollateViewModelRenderables(nint system)
    {
        long t = Now();
        builder.CollateViewModelRenderables(system);
        stats.Add(LeafSystemStats.Kind.ViewModels, t);
    }

    public int AddShadow(int userId, int flags)
    {
        long t = Now();
        int handle = registry.AddShadow(userId, flags);
        stats.Add(LeafSystemStats.Kind.Shadows, t);
        return handle;
    }

    public void RemoveShadow(int handle)
    {
        long t = Now();
        registry.RemoveShadow(handle);
        stats.Add(LeafSystemStats.Kind.Shadows, t);
    }

    public void ProjectShadow(int handle, ReadOnlySpan<int> leaves)
    {
        long t = Now();
        registry.ProjectShadow(handle, leaves);
        stats.Add(LeafSystemStats.Kind.Shadows, t);
    }

    public void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves)
    {
        long t = Now();
        registry.EnumerateShadowsInLeaves(leaves);
        stats.Add(LeafSystemStats.Kind.Shadows, t);
    }
}
