using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests;

/// <summary><see cref="TFCondMask"/>: stock's five condition words as a value.</summary>
public sealed class TFConditionsMaskTests
{
    [Fact]
    public void Each_condition_lands_in_word_n_over_32_bit_n_mod_32()
    {
        for (int n = 0; n < (int)TFCond.Last; n++)
        {
            TFCondMask mask = TFCondMask.Of((TFCond)n);

            Assert.Equal(1 << (n % 32), mask.WordAt(n / 32));
            Assert.Equal(1, mask.Count);
        }
    }

    [Fact]
    public void Enumeration_is_ascending_and_complete()
    {
        TFCondMask mask = TFCondMask.Of([TFCond.ImmuneToPushback, TFCond.Aiming, TFCond.SpeedBoost, TFCond.Burning]);
        var seen = new List<TFCond>();

        foreach (TFCond cond in mask)
        {
            seen.Add(cond);
        }

        Assert.Equal([TFCond.Aiming, TFCond.Burning, TFCond.SpeedBoost, TFCond.ImmuneToPushback], seen);
    }

    [Fact]
    public void Complement_stays_inside_the_valid_conditions()
    {
        TFCondMask all = ~default(TFCondMask);

        Assert.Equal((int)TFCond.Last, all.Count);
    }

    [Fact]
    public void Added_and_Removed_split_a_change()
    {
        TFCondMask before = TFCondMask.Of([TFCond.Aiming, TFCond.Zoomed]);
        TFCondMask now = TFCondMask.Of([TFCond.Zoomed, TFCond.Taunting]);

        Assert.Equal(TFCondMask.Of(TFCond.Taunting), TFCondMask.Added(before, now));
        Assert.Equal(TFCondMask.Of(TFCond.Aiming), TFCondMask.Removed(before, now));
    }

    [Fact]
    public void CopyTo_refuses_a_span_too_short()
    {
        TFCondMask mask = TFCondMask.Of([TFCond.Aiming, TFCond.Zoomed]);

        Assert.Throws<ArgumentException>(() => mask.CopyTo(new TFCond[1]));
    }

    [Fact]
    public void CopyTo_writes_the_set_in_order()
    {
        TFCondMask mask = TFCondMask.Of([TFCond.Zoomed, TFCond.Aiming]);
        Span<TFCond> into = stackalloc TFCond[4];

        int count = mask.CopyTo(into);

        Assert.Equal(2, count);
        Assert.Equal(TFCond.Aiming, into[0]);
        Assert.Equal(TFCond.Zoomed, into[1]);
    }

    [Fact]
    public void The_words_round_trip_through_the_constructor()
    {
        TFCondMask mask = TFCondMask.Of([TFCond.Aiming, TFCond.Megaheal, TFCond.Blastjumping, TFCond.Plague, TFCond.ImmuneToPushback]);

        var copy = new TFCondMask(mask.Word0, mask.Word1, mask.Word2, mask.Word3, mask.Word4);

        Assert.Equal(mask, copy);
    }
}
