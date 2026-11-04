using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.LeafSystem;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The leaf system's batches reuse their buffers: a warm frame allocates
/// nothing, each batch carries its own count rather than the buffer's capacity,
/// and an answer native leaves unwritten reads as zero, not as last frame's.
/// </summary>
public class LeafBatchBufferTests
{
    private const int FlagsOpaqueEntity = 7;
    private const int StudioModel = 3;

    private sealed class Native : ILeafNative
    {
        public readonly List<nint[]> FactBatches = [];
        public readonly List<float[]> BoxBatches = [];
        public readonly List<int[]> RunBatches = [];
        public readonly List<int> ReceiveBatches = [];
        public readonly List<int> QueryBatches = [];
        public bool Record;
        public bool SilentFacts;
        public bool SilentReceives = false;
        public bool SilentQueries;
        public Action? DuringFacts;

        public int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending) => 0;

        public void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts)
        {
            if (Record)
                FactBatches.Add(renderables.ToArray());
            Action? once = DuringFacts;
            DuringFacts = null;
            once?.Invoke();
            if (SilentFacts)
                return;
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

        public void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers)
        {
            if (Record)
                ReceiveBatches.Add(renderables.Length);
            if (!SilentReceives)
                answers.Fill(1);
        }

        public int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output)
        {
            if (Record)
                BoxBatches.Add(boxes.ToArray());
            int at = 0;
            for (int b = 0; b < boxes.Length / 6; b++)
            {
                output[at++] = 1;
                output[at++] = (int)(boxes[6 * b] / 100);
            }
            return at;
        }

        public void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas)
        {
            if (Record)
                RunBatches.Add(runs.ToArray());
            areas[..count].Fill(1);
        }

        public void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers)
        {
            if (Record)
                QueryBatches.Add(programs.Length);
            if (!SilentQueries)
                answers[..programs.Length].Fill(LeafQuery.Cull);
        }

        public void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models) => models.Clear();
    }

    private sealed class Receivers : SourceSharp.Client.Render.Shadows.IShadowReceivers
    {
        public int Added;

        public void AddShadowToReceiver(int shadow, nint renderable, int kind) => Added++;

        public void RemoveAllShadowsFromReceiver(nint renderable, int kind) { }
    }

    private sealed class Settings : ILeafListSettings
    {
        public bool PortalTestEnts => true;
        public bool PortalsOpenAll => false;
        public int DrawLeaf => -1;
    }

    private static (LeafRegistry Registry, Native Native, Receivers Receivers) Make(int leaves = 8)
    {
        var native = new Native();
        var receivers = new Receivers();
        var registry = new LeafRegistry(native) { Receivers = receivers };
        registry.LevelInitPreEntity(leaves);
        return (registry, native, receivers);
    }

    private static int[] AddSome(LeafRegistry registry, int count)
    {
        var handles = new int[count];
        for (int i = 0; i < count; i++)
            handles[i] = registry.AddRenderable(0x100 + i, FlagsOpaqueEntity, StudioModel);
        return handles;
    }

    private static LeafView View(ReadOnlySpan<int> leaves, int frame)
        => new(leaves, frame, frame, drawDetail: false, drawTranslucent: true, 0, 0, 0, 1, 0, 0, 0, drawLeafFilter: false);

    [Fact]
    public void AWarmPreRenderAllocatesNothing()
    {
        var (registry, _, _) = Make();
        int[] handles = AddSome(registry, 32);
        int shadow = registry.AddShadow(1, 1);
        registry.ProjectShadow(shadow, [1, 2, 3]);
        registry.PreRender();

        void Frame()
        {
            foreach (int handle in handles)
                registry.RenderableChanged(handle);
            registry.PreRender();
        }

        for (int i = 0; i < 16; i++)
            Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AWarmProjectShadowAllocatesNothing()
    {
        var (registry, _, _) = Make();
        AddSome(registry, 16);
        registry.PreRender();
        int shadow = registry.AddShadow(1, 1);
        ReadOnlySpan<int> leaves = [1, 2, 3];
        for (int i = 0; i < 16; i++)
            registry.ProjectShadow(shadow, leaves);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            registry.ProjectShadow(shadow, leaves);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ASmallFrameAfterALargeOneAsksOnlyForItsOwnRenderables()
    {
        var (registry, native, _) = Make();
        int[] handles = AddSome(registry, 40);
        registry.PreRender();

        native.Record = true;
        registry.RenderableChanged(handles[3]);
        registry.PreRender();

        Assert.Equal([(nint)0x103], Assert.Single(native.FactBatches));
        Assert.Equal(6, Assert.Single(native.BoxBatches).Length);
        // The one renderable's run: its count, then its leaf (0x103 / 100).
        Assert.Equal([1, 2], Assert.Single(native.RunBatches));
    }

    [Fact]
    public void ABoxNativeDoesNotAnswerIsZeroAfterAFrameThatAnswered()
    {
        var (registry, native, _) = Make();
        int[] handles = AddSome(registry, 4);
        registry.PreRender();

        native.Record = true;
        native.SilentFacts = true;
        registry.RenderableChanged(handles[0]);
        registry.PreRender();

        Assert.All(Assert.Single(native.BoxBatches), word => Assert.Equal(0f, word));
    }

    [Fact]
    public void AReceiverNativeDoesNotAnswerTakesNoShadowAfterOneThatAnswered()
    {
        var (registry, native, receivers) = Make();
        AddSome(registry, 4);
        registry.PreRender();
        int shadow = registry.AddShadow(1, 1);
        // Every box above lands in leaf 2 or lower: 0x100.. / 100.
        registry.ProjectShadow(shadow, [2]);
        int answered = receivers.Added;
        Assert.True(answered > 0, "the renderables took the shadow when asked");

        native.SilentReceives = true;
        registry.ProjectShadow(shadow, [2]);

        Assert.Equal(answered, receivers.Added);
    }

    [Fact]
    public void ARenderableDirtiedDuringThePreRendersFactsWaitsForTheNextFrame()
    {
        var (registry, native, _) = Make();
        int[] handles = AddSome(registry, 3);
        registry.PreRender();

        native.Record = true;
        registry.RenderableChanged(handles[0]);
        registry.RenderableChanged(handles[1]);
        native.DuringFacts = () => registry.RenderableChanged(handles[2]);
        registry.PreRender();
        Assert.Equal([(nint)0x101, (nint)0x100], Assert.Single(native.FactBatches));

        native.FactBatches.Clear();
        registry.PreRender();
        Assert.Equal([(nint)0x102], Assert.Single(native.FactBatches));

        native.FactBatches.Clear();
        registry.PreRender();
        Assert.Empty(native.FactBatches);
    }

    [Fact]
    public void AnEngineStaticPropStillTakesTheLeavesAndAreaItWasGiven()
    {
        var (registry, _, receivers) = Make();
        int shadow = registry.AddShadow(1, 1);
        registry.ProjectShadow(shadow, [5]);
        int handle = registry.CreateRenderableHandle(0x500, staticProp: true, StudioModel, transparent: false, twoPass: false);

        registry.AddRenderableToLeaves(handle, [5, 6]);

        Span<int> leaves = stackalloc int[4];
        Assert.Equal(2, registry.GetRenderableLeaves(handle, leaves));
        Assert.Equal(1, receivers.Added);
        Assert.Equal(1, registry.Info(handle).Area);
    }

    [Fact]
    public void AWarmViewListAllocatesNothing()
    {
        var (registry, _, _) = Make();
        AddSome(registry, 32);
        registry.PreRender();
        var builder = new RenderableListBuilder(registry, new Settings());
        int[] leaves = [0, 1, 2, 3];
        var ops = new LeafOp[256];

        void Frame(int frame)
        {
            builder.ComputeTranslucentRenderLeaf(leaves, frame, 0, frame, 0);
            builder.BuildRenderablesList(View(leaves, frame));
            builder.CollateViewModelRenderables();
            registry.TakeOps(ops);
        }

        for (int f = 1; f <= 16; f++)
            Frame(f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 17; f <= 80; f++)
            Frame(f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AViewAfterALargerOneQueriesOnlyItsOwnBoxes()
    {
        var (registry, native, _) = Make();
        AddSome(registry, 32);
        registry.PreRender();
        var builder = new RenderableListBuilder(registry, new Settings());
        int[] all = [0, 1, 2, 3];
        builder.ComputeTranslucentRenderLeaf(all, 1, 0, 1, 0);
        builder.BuildRenderablesList(View(all, 1));

        native.Record = true;
        int[] none = [7];
        builder.ComputeTranslucentRenderLeaf(none, 2, 0, 2, 0);
        builder.BuildRenderablesList(View(none, 2));

        Assert.Empty(native.QueryBatches);
    }

    [Fact]
    public void AQueryNativeDoesNotAnswerKeepsTheEntryAfterOneThatRejectedIt()
    {
        var (registry, native, _) = Make();
        AddSome(registry, 1);
        registry.PreRender();
        var builder = new RenderableListBuilder(registry, new Settings());
        int[] leaves = [2];

        builder.ComputeTranslucentRenderLeaf(leaves, 1, 0, 1, 0);
        builder.BuildRenderablesList(View(leaves, 1));
        // The leaf's two markers (opaque static and entity), the renderable rejected.
        Assert.Equal(2, CountEntries(registry));

        native.SilentQueries = true;
        builder.ComputeTranslucentRenderLeaf(leaves, 2, 0, 2, 0);
        builder.BuildRenderablesList(View(leaves, 2));
        Assert.Equal(3, CountEntries(registry));
    }

    private static int CountEntries(LeafRegistry registry)
    {
        var ops = new LeafOp[256];
        int entries = 0;
        int taken = registry.TakeOps(ops);
        foreach (LeafOp op in ops.AsSpan(0, taken))
        {
            if (op.Kind == LeafOpKind.ListEntry)
                entries++;
        }
        return entries;
    }
}
