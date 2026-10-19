using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Render.LeafSystem;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedRenderClient's leaf-list slots (C7 cluster 12 L1) as native calls
/// them through <see cref="RenderClientBinding"/>: one crossing per view and
/// per view-model split, the buffer read by its own length prefix, and -1 for
/// native to fall back on whenever there is no managed answer.
/// </summary>
public unsafe class RenderLeafListBindingTests
{
    // One leaf (BSP leaf 3), no renderables, no detail models: the answer is
    // the leaf's two markers.
    private static readonly int[] OneEmptyLeaf =
    [
        20, 1, 1 | 4 | 8 | 32, -1, 0, 0, 0, 0x3f800000, 0, 0, 1, 0, 0, 0,
        3, 0, 0, 0, 0, 0,
    ];

    [Fact]
    public void AViewIsOneCrossingAndItsAnswerIsWrittenWhereNativeAsked()
    {
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), new RenderPlanHost(), new RenderableListBuilder());
        var answer = new int[64];
        int n;
        fixed (int* view = OneEmptyLeaf)
        fixed (int* a = answer)
            n = binding.RenderLeafList(view, a, answer.Length);
        Assert.Equal(2 + 1 + 4 * 2, n);
        Assert.Equal(n, answer[0]);
        Assert.Equal(2, answer[2]);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafList));
    }

    [Fact]
    public void WithNoLeafListsTheBindingAnswersStockAndStillCounts()
    {
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), new RenderPlanHost());
        var answer = new int[64];
        fixed (int* view = OneEmptyLeaf)
        fixed (int* a = answer)
        {
            Assert.Equal(-1, binding.RenderLeafList(view, a, answer.Length));
            Assert.Equal(-1, binding.RenderLeafViewModels(view, a, answer.Length));
        }
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafList));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafViewModels));
    }

    [Fact]
    public void TheViewModelSplitIsReadByItsCount()
    {
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), new RenderPlanHost(), new RenderableListBuilder());
        // Two view models, opaque then translucent, and a word past the count
        // that must not be read as a third.
        int[] groups = [2, 10, 11, 10];
        var answer = new int[3] { 7, 7, 7 };
        fixed (int* g = groups)
        fixed (int* a = answer)
            Assert.Equal(2, binding.RenderLeafViewModels(g, a, answer.Length));
        Assert.Equal(new[] { 0, 1, 7 }, answer);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafViewModels));
    }

    [Fact]
    public void TheSwitchOffAnswersStock()
    {
        bool on = false;
        var builder = new RenderableListBuilder(() => on);
        var answer = new int[64];
        Assert.Equal(-1, builder.BuildRenderableList(OneEmptyLeaf, answer));
        on = true;
        Assert.True(builder.BuildRenderableList(OneEmptyLeaf, answer) > 0);
    }
}
