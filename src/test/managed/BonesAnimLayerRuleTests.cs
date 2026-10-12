using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="AnimLayerRule"/>, hand-derived from
/// src/shared/native/anim_layer_rule.h: IsActive, Weight, Sort.
/// </summary>
public class BonesAnimLayerRuleTests
{
    private static AnimLayer Layer(int sequence, float weight, int order = 0) =>
        new() { Sequence = sequence, Weight = weight, Order = order };

    [Fact]
    public void ResetIsAnUnfilledSlot()
    {
        AnimLayer layer = Layer(3, 0.5f, 2);
        layer.Reset();
        Assert.Equal((-1, 0.0f, 0), (layer.Sequence, layer.Weight, layer.Order));
    }

    [Fact]
    public void AnUnfilledSlotIsInactive()
    {
        Assert.False(AnimLayerRule.IsActive(Layer(-1, 1f), 4));
    }

    [Fact]
    public void ASequenceOutsideTheModelIsInactive()
    {
        Assert.False(AnimLayerRule.IsActive(Layer(4, 1f), 4));
    }

    [Fact]
    public void AZeroWeightLayerIsInactive()
    {
        Assert.False(AnimLayerRule.IsActive(Layer(1, 0f), 4));
    }

    [Fact]
    public void AFilledWeightedLayerInRangeIsActive()
    {
        Assert.True(AnimLayerRule.IsActive(Layer(3, 0.01f), 4));
    }

    [Fact]
    public void ANegativeWeightClampsToZero()
    {
        Assert.Equal(0f, AnimLayerRule.Weight(Layer(0, -0.5f)));
    }

    [Fact]
    public void AWeightAboveOneClampsToOne()
    {
        Assert.Equal(1f, AnimLayerRule.Weight(Layer(0, 1.5f)));
    }

    [Fact]
    public void AWeightInRangeIsKept()
    {
        Assert.Equal(0.25f, AnimLayerRule.Weight(Layer(0, 0.25f)));
    }

    [Fact]
    public void SortOrdersByOrderAscending()
    {
        AnimLayer[] layers = [Layer(0, 1f, 5), Layer(1, 1f, 1), Layer(2, 1f, 3)];
        int[] order = new int[3];
        Assert.Equal(3, AnimLayerRule.Sort(layers, 4, order));
        Assert.Equal([1, 2, 0], order);
    }

    [Fact]
    public void SortIsStableForEqualOrders()
    {
        AnimLayer[] layers = [Layer(0, 1f, 2), Layer(1, 1f, 2), Layer(2, 1f, 1)];
        int[] order = new int[3];
        AnimLayerRule.Sort(layers, 4, order);
        Assert.Equal([2, 0, 1], order);
    }

    [Fact]
    public void SortSkipsInactiveSlots()
    {
        AnimLayer[] layers = [Layer(-1, 1f, 0), Layer(1, 1f, 0), Layer(9, 1f, 0), Layer(2, 0f, 0)];
        int[] order = [7, 7, 7, 7];
        Assert.Equal(1, AnimLayerRule.Sort(layers, 4, order));
        Assert.Equal([1, 7, 7, 7], order);
    }
}
