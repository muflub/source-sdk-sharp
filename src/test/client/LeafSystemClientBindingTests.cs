using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.LeafSystem;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedLeafSystemClient as libss_fwd_client's leaf system calls it
/// (C7 cluster 12 L2), through <see cref="LeafSystemClientBinding"/> over the
/// managed leaf system: every slot counts its crossing, leaves arrive as the
/// engine's 16-bit indices, and an answer's operations are written as four
/// int64 words each, as many as the forwarder's buffer holds, the rest
/// waiting for TakeOps.
/// </summary>
public unsafe class LeafSystemClientBindingTests
{
    // Each renderable answers from its pointer: a box at x = pointer, ten wide;
    // it takes every projected texture.
    private sealed class Native : ILeafNative
    {
        public int Blends;

        // A blend batch: each blend queues the group change Queue names, as a
        // fading static prop's does, and the batch answers the queue.
        public (int Handle, int Group)? Queue;

        public int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending)
        {
            Blends += renderables.Length;
            if (Queue is not { } q)
                return 0;
            pending[0] = LeafPending.ChangeRenderGroup;
            pending[1] = q.Handle;
            pending[2] = q.Group;
            return 1;
        }

        public void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts)
        {
            for (int i = 0; i < renderables.Length; i++)
            {
                Span<int> f = facts.Slice(i * LeafFacts.Words, LeafFacts.Words);
                float x = renderables[i];
                f[0] = 255;
                f[2] = BitConverter.SingleToInt32Bits(x);
                f[5] = BitConverter.SingleToInt32Bits(x + 10);
                f[6] = f[7] = BitConverter.SingleToInt32Bits(10);
            }
        }

        public void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers) => answers.Fill(1);

        public int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output)
        {
            int at = 0;
            for (int b = 0; b < boxes.Length / 6; b++)
            {
                output[at++] = 1;
                output[at++] = (int)(boxes[6 * b] / 100);
            }
            return at;
        }

        public void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas) => areas[..count].Fill(1);

        public void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers) => answers[..programs.Length].Clear();

        public void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models) => models.Clear();
    }

    private sealed class Settings : ILeafListSettings
    {
        public bool PortalTestEnts => true;
        public bool PortalsOpenAll => false;
        public int DrawLeaf => -1;
    }

    // The managed shadow manager's receiver calls (C7 cluster 12 S2), held.
    private sealed class HeldReceivers : SourceSharp.Client.Render.Shadows.IShadowReceivers
    {
        public readonly List<(nint Renderable, int Kind)> Cleared = [];

        public void AddShadowToReceiver(int shadow, nint renderable, int kind) { }

        public void RemoveAllShadowsFromReceiver(nint renderable, int kind) => Cleared.Add((renderable, kind));
    }

    private static List<(nint Renderable, int Kind)> Cleared(LeafRegistry registry) => ((HeldReceivers)registry.Receivers!).Cleared;

    private static (LeafSystemClientBinding Binding, LeafRegistry Registry, Crossings Crossings) Make()
    {
        var registry = new LeafRegistry(new Native()) { Receivers = new HeldReceivers() };
        var system = new ClientLeafSystem(registry, new RenderableListBuilder(registry, new Settings()));
        var crossings = new Crossings();
        return (new LeafSystemClientBinding(crossings, new StandardErrorLog(), system), registry, crossings);
    }

    [Fact]
    public void ARenderableIsRegisteredAndRemovedWithItsOperationsWrittenOut()
    {
        var (binding, registry, crossings) = Make();
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x150, 7, 3);   // opaque entity, a studio model
        Assert.Equal(0, handle);
        var ops = new long[4 * 8];
        fixed (long* p = ops)
        {
            // The first insert's receivers cleared, by the managed manager in-process (S2): no operation.
            Assert.Equal(0, binding.PreRender(null, 0, p, 8));
            Assert.Equal([((nint)0x150, 2)], Cleared(registry));

            Assert.Equal(1, binding.RemoveRenderable(handle, p, 8));
        }
        Assert.Equal((long)LeafOpKind.SetRenderHandle, ops[0]);
        Assert.Equal(0x150, ops[1]);
        Assert.Equal(0xFFFF, ops[2]);
        Assert.Equal([((nint)0x150, 2), ((nint)0x150, 2)], Cleared(registry));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafAddRenderable));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafPreRender));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafRemoveRenderable));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafLevelInitPreEntity));
    }

    [Fact]
    public void AnAnswerLongerThanTheBufferWaitsForTakeOps()
    {
        var (binding, registry, crossings) = Make();
        binding.LevelInitPreEntity(10);
        for (int i = 0; i < 10; i++)
            binding.SetSubSystemDataInLeaf(i, 0, (void*)(0x100 + i));   // each leaf's data is deleted at the level's end
        var ops = new long[4 * 4];
        fixed (long* p = ops)
        {
            Assert.Equal(4, binding.LevelShutdownPostEntity(p, 4));
            Assert.Equal(6, registry.PendingOps);
            Assert.Equal(4, binding.TakeOps(p, 4));
            Assert.Equal(2, binding.TakeOps(p, 4));
            Assert.Equal(0, binding.TakeOps(p, 4));
        }
        Assert.Equal(3, crossings.Read(CrossingOp.ClientLeafTakeOps));
    }

    [Fact]
    public void TheEnginesLeavesAreSixteenBitAndALeafWalkIsTheRegistrys()
    {
        var (binding, _, _) = Make();
        binding.LevelInitPreEntity(300);
        int handle = binding.CreateRenderableHandle((void*)0x200, 1, 3, 0, 0);
        ushort[] leaves = [4, 260, 7];
        var ops = new long[4 * 8];
        fixed (ushort* l = leaves)
        fixed (long* p = ops)
            binding.AddRenderableToLeaves(handle, l, leaves.Length, p, 8);
        var back = new int[128];
        fixed (int* b = back)
            Assert.Equal(3, binding.GetRenderableLeaves(handle, b));
        Assert.Equal([7, 260, 4], back[..3]);   // newest first, as CBidirectionalSet links them

        var leafAndIterator = new int[2];
        var walked = new List<int>();
        fixed (int* li = leafAndIterator)
        {
            int more = binding.GetRenderableLeaf(handle, 0, -1, li);
            while (more != 0)
            {
                walked.Add(leafAndIterator[0]);
                more = binding.GetRenderableLeaf(handle, 1, leafAndIterator[1], li);
            }
        }
        Assert.Equal([7, 260, 4], walked);
    }

    [Fact]
    public void AViewsListIsWrittenAsEntriesWithTheHandleLeafAndTwoPassPacked()
    {
        var (binding, _, crossings) = Make();
        binding.LevelInitPreEntity(4);
        binding.AddRenderable((void*)0x105, 7, 3);   // a box at x 261: leaf 2
        var ops = new long[4 * 64];
        fixed (long* p = ops)
            binding.PreRender(null, 0, p, 64);
        ushort[] leaves = [2, 0];
        float[] originForward = [0, 0, 0, 1, 0, 0];
        int n;
        fixed (ushort* l = leaves)
        fixed (float* of = originForward)
        fixed (long* p = ops)
            n = binding.BuildRenderablesList(l, leaves.Length, of, 1, 1, 1 | 2, (void*)0, p, 64, null, 0);
        // Two markers per leaf, and the renderable in leaf 2 (world-list leaf 0),
        // bucketed by its 10-unit box into the smallest opaque entity group.
        Assert.Equal(5, n);
        var entries = new List<(long Ptr, long Packed, long Group)>();
        for (int i = 0; i < n; i++)
        {
            Assert.Equal((long)LeafOpKind.ListEntry, ops[4 * i]);
            entries.Add((ops[4 * i + 1], ops[4 * i + 2], ops[4 * i + 3]));
        }
        var renderable = Assert.Single(entries, e => e.Ptr != 0);
        Assert.Equal(7, renderable.Group);
        Assert.Equal(0L, renderable.Packed);   // handle 0, world-list leaf 0, not two-pass
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafBuildRenderablesList));
    }

    [Fact]
    public void TheQueuedCallsArriveInOrderInPreRendersOwnCrossing()
    {
        var (binding, registry, crossings) = Make();
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x150, 7, 3);
        var ops = new long[4 * 8];
        fixed (long* p = ops)
            binding.PreRender(null, 0, p, 8);
        // RenderableChanged, then two group changes: the last group stands, and
        // the renderable is dirty again, so this PreRender clears its receivers.
        int[] entries = [1, handle, 0, 2, handle, 8, 2, handle, 6];
        int clearedBefore = Cleared(registry).Count;
        fixed (int* e = entries)
        fixed (long* p = ops)
            Assert.Equal(0, binding.PreRender(e, 3, p, 8));
        Assert.Equal(6, registry.Info(handle).Group);
        Assert.Equal(clearedBefore + 1, Cleared(registry).Count);
        Assert.Equal(2, crossings.Read(CrossingOp.ClientLeafPreRender));
        Assert.Equal(0, crossings.Read(CrossingOp.ClientLeafPending));
        Assert.Equal(0, crossings.Read(CrossingOp.ClientLeafRenderableChanged));
    }

    [Fact]
    public void AGroupChangeABlendQueuedIsAppliedBeforeTheViewModelIsSorted()
    {
        var native = new Native();
        var registry = new LeafRegistry(native);
        var system = new ClientLeafSystem(registry, new RenderableListBuilder(registry, new Settings()));
        var binding = new LeafSystemClientBinding(new Crossings(), new StandardErrorLog(), system);
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x160, 10, 3);   // an opaque view model
        native.Queue = (handle, 11);                               // its blend fades it: translucent
        var ops = new long[4 * 8];
        fixed (long* p = ops)
            Assert.Equal(1, binding.CollateViewModelRenderables(p, 8, (void*)0x1, null, 0));
        Assert.Equal(1, native.Blends);
        Assert.Equal((long)LeafOpKind.ViewModel, ops[0]);
        Assert.Equal(0x160, ops[1]);
        Assert.Equal(1, ops[2]);   // translucent: the queued change was applied first
        Assert.Equal(11, registry.Info(handle).Group);
    }

    [Fact]
    public void APerViewCallAppliesTheQueueItCarriesBeforeItsOwnWork()
    {
        var (binding, registry, crossings) = Make();
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x160, 10, 3);   // an opaque view model
        int[] entries = [2, handle, 11];                           // faded since PreRender: translucent
        var ops = new long[4 * 8];
        fixed (int* e = entries)
        fixed (long* p = ops)
            Assert.Equal(1, binding.CollateViewModelRenderables(p, 8, null, e, 1));
        Assert.Equal(11, registry.Info(handle).Group);
        Assert.Equal((long)LeafOpKind.ViewModel, ops[0]);
        Assert.Equal(1, ops[2]);
        Assert.Equal(0, crossings.Read(CrossingOp.ClientLeafPending));
    }

    [Fact]
    public void AQueuedSetRenderGroupIsApplied()
    {
        var (binding, registry, _) = Make();
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x150, 7, 3);
        int[] entries = [3, handle, 8];
        var ops = new long[4 * 8];
        fixed (int* e = entries)
        fixed (long* p = ops)
            binding.PreRender(e, 1, p, 8);
        Assert.Equal(8, registry.Info(handle).Group);
    }

    [Fact]
    public void TheQueueHandedOverOnItsOwnIsAppliedInOrder()
    {
        var (binding, registry, crossings) = Make();
        binding.LevelInitPreEntity(8);
        int handle = binding.AddRenderable((void*)0x150, 7, 3);
        int[] entries = [2, handle, 8, 2, handle, 6];
        fixed (int* e = entries)
            binding.Pending(e, 2);
        Assert.Equal(6, registry.Info(handle).Group);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafPending));
    }

    [Fact]
    public void ASlotWithNoBufferOrAThrowingHostAnswersNothing()
    {
        var crossings = new Crossings();
        var binding = new LeafSystemClientBinding(crossings, new StandardErrorLog(), new Throwing());
        Assert.Equal(0, binding.PreRender(null, 0, null, 0));
        Assert.Equal(0xFFFF, binding.AddRenderable((void*)1, 7, 0));
        Assert.Equal(-1, binding.GetRenderableLeaves(0, null));
        Assert.Equal(0, binding.RemoveRenderable(0, null, 0));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientLeafAddRenderable));
    }

    private sealed class Throwing : IClientLeafSystemHost
    {
        private static Exception E() => new InvalidOperationException("the test's host throws");
        public void LevelInitPreEntity(int leafCount) => throw E();
        public void LevelShutdownPostEntity() => throw E();
        public void ApplyPending(ReadOnlySpan<int> entries) => throw E();
        public void PreRender() => throw E();
        public int TakeOps(Span<LeafOp> destination) => throw E();
        public int CreateRenderableHandle(nint renderable, bool staticProp, int modelType, bool transparent, bool twoPass) => throw E();
        public int AddRenderable(nint renderable, int group, int modelType) => throw E();
        public void RemoveRenderable(int handle) => throw E();
        public void AddRenderableToLeaves(int handle, ReadOnlySpan<int> leaves) => throw E();
        public void SetRenderGroup(int handle, int group) => throw E();
        public void RenderableChanged(int handle) => throw E();
        public void EnableAlternateSorting(int handle, bool enable) => throw E();
        public void DrawStaticProps(bool enable) => throw E();
        public void DrawSmallEntities(bool enable) => throw E();
        public int GetRenderableLeaves(int handle, Span<int> leaves) => throw E();
        public bool GetRenderableLeaf(int handle, bool hasIterator, int iterator, out int leaf, out int nextIterator) => throw E();
        public long SetSubSystemDataInLeaf(int leaf, int index, long data) => throw E();
        public long GetSubSystemDataInLeaf(int leaf, int index) => throw E();
        public void SetDetailObjectsInLeaf(int leaf, int first, int count) => throw E();
        public bool GetDetailObjectsInLeaf(int leaf, out int first, out int count) => throw E();
        public void DrawDetailObjectsInLeaf(int leaf, int frame, out int first, out int count) => throw E();
        public bool ShouldDrawDetailObjectsInLeaf(int leaf, int frame) => throw E();
        public void ComputeTranslucentRenderLeaf(ReadOnlySpan<int> leaves, int frameNumber, int viewId, int globalFrame, nint system) => throw E();
        public void BuildRenderablesList(in LeafView view) => throw E();
        public void CollateViewModelRenderables(nint system) => throw E();
    }
}
