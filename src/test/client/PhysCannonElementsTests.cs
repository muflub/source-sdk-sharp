using SourceSharp.Client;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The physcannon's claws (C_WeaponPhysCannon's m_ElementParameter): open
/// splines to 1 over 0.2 s, close to 0 over 0.5 s, from wherever they are.
/// </summary>
public class PhysCannonElementsTests
{
    [Fact]
    public void TheClawsOpenOverAFifthOfASecond()
    {
        var claws = new PhysCannonElements();

        Assert.Equal(0f, claws.Advance(open: false, now: 10f));
        Assert.Equal(0f, claws.Advance(open: true, now: 10f));
        Assert.Equal(0.5f, claws.Advance(open: true, now: 10.1f), precision: 4);
        Assert.Equal(1f, claws.Advance(open: true, now: 10.2f));
    }

    [Fact]
    public void TheClawsCloseOverHalfASecondFromWhereTheyAre()
    {
        var claws = new PhysCannonElements();
        claws.Advance(open: true, now: 10f);
        float halfOpen = claws.Advance(open: true, now: 10.1f);

        // Closing mid-way starts from the half-open claws, not from fully open.
        Assert.Equal(halfOpen, claws.Advance(open: false, now: 10.1f), precision: 4);
        Assert.Equal(halfOpen / 2f, claws.Advance(open: false, now: 10.35f), precision: 4);
        Assert.Equal(0f, claws.Advance(open: false, now: 10.6f));
    }

    [Fact]
    public void AResetShutsThemAtOnce()
    {
        var claws = new PhysCannonElements();
        claws.Advance(open: true, now: 10f);
        claws.Advance(open: true, now: 11f);

        claws.Reset();

        Assert.Equal(0f, claws.Advance(open: false, now: 11f));
    }
}
