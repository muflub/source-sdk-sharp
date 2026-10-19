using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedRenderClient's two leaf-list slots (C7 cluster 12 L1) are retired:
/// the leaf system is managed's since L2 and native asks it through
/// IManagedLeafSystemClient. The table only appends, so the slots stay; each
/// answers -1 (no list) and is still counted, so a caller nobody expected
/// shows in the crossings.
/// </summary>
public unsafe class RenderLeafListBindingTests
{
    [Fact]
    public void TheRetiredLeafListSlotsAnswerNothingAndAreCounted()
    {
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), new RenderPlanHost());
        int[] view = [20, 1, 0, -1];
        var answer = new int[64];
        fixed (int* v = view)
        fixed (int* a = answer)
        {
            Assert.Equal(-1, binding.RenderLeafList(v, a, answer.Length));
            Assert.Equal(-1, binding.RenderLeafViewModels(v, a, answer.Length));
        }
        Assert.All(answer, w => Assert.Equal(0, w));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafList));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRenderLeafViewModels));
    }
}
