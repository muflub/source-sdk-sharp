using System.Buffers.Binary;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The list of extra hulls a game's numbers may carry (<see cref="NavNumbers.ExtraHulls"/>): at
/// most six, in order, compared by value; and its part in a cached graph's name
/// (<see cref="NavGraphCache.NumbersKey"/>), which is none at all when the list is empty, so every
/// name there was before the list is the name there is now.
/// </summary>
public sealed class NavHullsTests
{
    private static readonly NavHull Small = new(29f, 50f), Middle = new(49f, 83f), Giant = new(86f, 145f);

    private static NavNumbers With(NavNumbers n, params NavHull[] hulls) => n with { ExtraHulls = NavHulls.Of(hulls) };

    [Fact]
    public void ExtraHullIIsClassTwoPlusI()
        => Assert.Equal((ClearanceClass.Hull2, ClearanceClass.Hull7), (NavHulls.ClassOf(0), NavHulls.ClassOf(NavHulls.Max - 1)));

    [Fact]
    public void ThereIsNoClassForASeventhHull() => Assert.Throws<ArgumentOutOfRangeException>(() => NavHulls.ClassOf(NavHulls.Max));

    [Fact]
    public void AListHoldsItsHullsInTheirOrder()
    {
        NavHulls three = NavHulls.Of(Small, Middle, Giant);
        Assert.Equal((3, Small, Middle, Giant), (three.Count, three[0], three[1], three[2]));
    }

    [Fact]
    public void AListHoldsAtMostSixHulls()
    {
        NavHull[] seven = [.. Enumerable.Range(0, 7).Select(i => new NavHull(16f + i, 32f))];
        Assert.Equal(6, NavHulls.Of(seven.AsSpan(0, 6)).Count);
        Assert.Throws<ArgumentException>(() => NavHulls.Of(seven));
    }

    [Fact]
    public void AHullHasAWidthAndAHeightAboveZero()
    {
        Assert.Throws<ArgumentException>(() => NavHulls.Of(new NavHull(0f, 50f)));
        Assert.Throws<ArgumentException>(() => NavHulls.Of(new NavHull(29f, float.NaN)));
        Assert.Throws<ArgumentException>(() => NavHulls.Of(new NavHull(float.PositiveInfinity, 50f)));
    }

    /// <summary>Numbers are compared by what they hold: two lists of the same hulls are one list.</summary>
    [Fact]
    public void TwoListsOfTheSameHullsAreEqual()
    {
        NavNumbers a = With(NavNumbers.Stock(56f), Small, Middle, Giant), b = With(NavNumbers.Stock(56f), Small, Middle, Giant);
        Assert.Equal((true, true, true), (a == b, a.GetHashCode() == b.GetHashCode(), a.ExtraHulls == b.ExtraHulls));
    }

    [Fact]
    public void ListsOfOtherHullsOrInAnotherOrderAreNot()
    {
        NavHulls list = NavHulls.Of(Small, Middle);
        Assert.Equal((false, false, false, false), (list == NavHulls.Of(Middle, Small), list == NavHulls.Of(Small), list == NavHulls.Of(Small, Giant), list == default));
    }

    /// <summary>No list, the default and a list of no hulls are the same thing: numbers that never named a hull equal ones given none.</summary>
    [Fact]
    public void TheEmptyListIsTheDefault()
    {
        Assert.Equal((0, true), (default(NavHulls).Count, NavHulls.Of() == default));
        Assert.Equal(NavNumbers.Stock(56f), With(NavNumbers.Stock(56f)));
        Assert.Equal(TFNavProfile.ModNumbers, With(TFNavProfile.ModNumbers));
    }

    // ---------------------------------------------------------------- the cache's name

    /// <summary>The name's rule as it was before the list: the first 16 hex digits of the SHA-256 of the body's ten numbers.</summary>
    private static string KeyOfTheTenNumbers(NavNumbers n)
    {
        Span<byte> bits = stackalloc byte[40];
        ReadOnlySpan<float> values = [n.HullWidth, n.StandHeight, n.CrouchHeight, n.StepHeight, n.MaxJumpRise, n.MaxCrouchJumpRise, n.SafeDrop,
                                      n.MaxDoubleJumpRise, n.WideHull ? 1f : 0f, n.CrouchOnlyCost];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bits[(i * 4)..], values[i]);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bits))[..16];
    }

    [Fact]
    public void WithNoHullsTF2sNameIsTheTenNumbersHash()
        => Assert.Equal(KeyOfTheTenNumbers(TFNavProfile.ModNumbers), NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers), 56f));

    [Fact]
    public void WithNoHullsHl2mpsNameIsEmpty() => Assert.Equal("", NavGraphCache.NumbersKey(With(NavNumbers.Stock(56f)), 56f));

    /// <summary>The core's default movement with hulls is not the default: its graph is another graph, with a name of its own.</summary>
    [Fact]
    public void TheDefaultMovementWithHullsHasAName()
    {
        string key = NavGraphCache.NumbersKey(With(NavNumbers.Stock(56f), Small), 56f);
        Assert.Equal(16, key.Length);
        Assert.NotEqual(KeyOfTheTenNumbers(NavNumbers.Stock(56f)), key);
    }

    [Fact]
    public void AHullListIsPartOfTheName()
        => Assert.NotEqual(NavGraphCache.NumbersKey(TFNavProfile.ModNumbers, 56f), NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small), 56f));

    [Fact]
    public void EachHullsWidthAndHeightArePartOfTheName()
    {
        string key = NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle), 56f);
        Assert.NotEqual(key, NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle with { Width = 50f }), 56f));
        Assert.NotEqual(key, NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle with { Height = 84f }), 56f));
        Assert.NotEqual(key, NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle, Giant), 56f));
    }

    [Fact]
    public void TheOrderOfTheHullsIsPartOfTheName()
        => Assert.NotEqual(NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle), 56f),
                           NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Middle, Small), 56f));

    [Fact]
    public void TheSameHullsGiveTheSameName()
        => Assert.Equal(NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle, Giant), 56f),
                        NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Small, Middle, Giant), 56f));

    [Fact]
    public void ACacheFilesNameCarriesTheHullsThroughItsNumbersPart()
    {
        var cache = new NavGraphCache("/c", NavEdgeCosts.Default);
        string a = new('a', 64), b = new('b', 64), key = NavGraphCache.NumbersKey(With(TFNavProfile.ModNumbers, Giant), 56f);
        Assert.EndsWith($"-g{NavGraphFromNav3d.Revision}-n{key}.navgraph", cache.PathFor("m", a, b, key), StringComparison.Ordinal);
    }
}
