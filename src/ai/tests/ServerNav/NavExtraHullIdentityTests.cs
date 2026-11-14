using System.Buffers.Binary;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// What the extra hulls' bits (<see cref="NavNumbers.ExtraHulls"/>) must agree with, over the
/// course fixture read by the real reader, by HL2MP's numbers and by TF2's:
/// <list type="bullet">
/// <item>A game that names no extra hull has none: no cell of its graph holds a bit past Stand and
/// Crouch, in the graph or in its cache file's bytes, so its hash and its file are what they were
/// before a graph could be classed for more hulls.</item>
/// <item>The extra hulls are asked the questions the game's own body is: a hull of the body's
/// standing size is admitted exactly where the body stands, one of its crouched size wherever
/// there is a place, and a hull smaller both ways than another wherever that other is.</item>
/// </list>
/// </summary>
public sealed class NavExtraHullIdentityTests
{
    private const ClearanceClasses Body = ClearanceClasses.Stand | ClearanceClasses.Crouch;

    private static readonly NavNumbers Hl2mp = NavNumbers.Stock(56f), TF2 = TFNavProfile.ModNumbers;

    private static DerivedNav Course(NavNumbers n)
        => NavGraphFromNav3d.Derive(new Nav3dNavSource(NavFixtures.Course().Open()), n, NavEdgeCosts.Default,
                                    [new HurtVolume(new AiVector(672, 32, 0), new AiVector(736, 96, 100))]);

    private static NavCell[] Cells(NavGraph g) => [.. Enumerable.Range(0, g.CellCount).Select(g.Cell)];

    private static int CellsPastTheBodysBits(NavNumbers n) => Cells(Course(n).Graph).Count(c => (c.Classes & ~Body) != 0);

    [Fact]
    public void WithNoExtraHullsNoCellOfHl2mpsGraphHoldsAnExtraBit() => Assert.Equal(0, CellsPastTheBodysBits(Hl2mp));

    [Fact]
    public void WithNoExtraHullsNoCellOfTF2sGraphHoldsAnExtraBit() => Assert.Equal(0, CellsPastTheBodysBits(TF2));

    /// <summary>The control for the two above: the same graphs classed for one hull hold its bit.</summary>
    [Fact]
    public void WithAnExtraHullCellsDo()
    {
        NavHulls one = NavHulls.Of(new NavHull(29f, 50f));
        Assert.True(CellsPastTheBodysBits(Hl2mp with { ExtraHulls = one }) > 0 && CellsPastTheBodysBits(TF2 with { ExtraHulls = one }) > 0);
    }

    /// <summary>The classes of each cell as a cache file's bytes hold them: the low byte of the word after a cell's five floats (the format's remarks).</summary>
    private static List<int> ClassesInTheFile(NavNumbers n)
    {
        var cache = new NavGraphCache("/c", NavEdgeCosts.Default);
        string a = new('a', 64), b = new('b', 64);
        ReadOnlySpan<byte> bytes = cache.Encode("m", a, b, Course(n), NavGraphCache.NumbersKey(n, 56f));
        int at = 8;
        for (int s = 0; s < 4; s++)
            at += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
        int cells = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
        at += 8;
        var classes = new List<int>();
        for (int c = 0; c < cells; c++, at += 28)
            classes.Add(BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 20)..]) & 0xFF);
        return classes;
    }

    [Fact]
    public void ACacheFileOfAGraphWithNoExtraHullsHoldsOnlyTheBodysBits()
    {
        List<int> hl2mp = ClassesInTheFile(Hl2mp), tf2 = ClassesInTheFile(TF2);
        Assert.Equal((Course(Hl2mp).Graph.CellCount, Course(TF2).Graph.CellCount), (hl2mp.Count, tf2.Count));
        Assert.All(hl2mp.Concat(tf2), c => Assert.InRange(c, 1, (int)Body));
    }

    /// <summary>The control: the reading of the file above finds an extra hull's bit when there is one.</summary>
    [Fact]
    public void ACacheFileOfAGraphWithAnExtraHullHoldsItsBit()
        => Assert.Contains(ClassesInTheFile(TF2 with { ExtraHulls = NavHulls.Of(new NavHull(29f, 50f)) }), c => (c & (int)ClearanceClasses.Hull2) != 0);

    // ---------------------------------------------------------------- the body's own questions

    private static (int Both, int Neither, int Differ) AgainstTheBodyStanding(NavNumbers n)
    {
        NavCell[] cells = Cells(Course(n with { ExtraHulls = NavHulls.Of(new NavHull(n.HullWidth, n.StandHeight)) }).Graph);
        // A place the body only climbs is classed for both its sizes as it is; an extra hull is asked whether it climbs there.
        NavCell[] stood = [.. cells.Where(c => (c.Flags & NavCellFlags.Ladder) == 0)];
        return (stood.Count(c => c.Admits(ClearanceClass.Stand) && c.Admits(ClearanceClass.Hull2)),
                stood.Count(c => !c.Admits(ClearanceClass.Stand) && !c.Admits(ClearanceClass.Hull2)),
                stood.Count(c => c.Admits(ClearanceClass.Stand) != c.Admits(ClearanceClass.Hull2)));
    }

    [Fact]
    public void AnExtraHullOfHl2mpsStandingSizeIsAdmittedExactlyWhereItsBodyStands()
    {
        (int both, int neither, int differ) = AgainstTheBodyStanding(Hl2mp);
        Assert.True(both > 0 && neither > 0, $"the stimulus: {both} places stood in and {neither} crouched in only");
        Assert.Equal(0, differ);
    }

    /// <summary>The course has no place TF2's body only crouches in (its crawlspace is lower than that): every place is one it stands in.</summary>
    [Fact]
    public void AnExtraHullOfTF2sStandingSizeIsAdmittedExactlyWhereItsBodyStands()
    {
        (int both, _, int differ) = AgainstTheBodyStanding(TF2);
        Assert.True(both > 0, "the stimulus: places stood in");
        Assert.Equal(0, differ);
    }

    [Fact]
    public void AnExtraHullOfTheBodysCrouchedSizeIsAdmittedWhereverThereIsAPlace()
    {
        foreach (NavNumbers n in new[] { Hl2mp, TF2 })
            Assert.All(Cells(Course(n with { ExtraHulls = NavHulls.Of(new NavHull(n.HullWidth, n.CrouchHeight)) }).Graph), c => Assert.True(c.Admits(ClearanceClass.Hull2)));
    }

    /// <summary>29 by 50, 49 by 83, 86 by 145: each smaller both ways than the next, so admitted wherever the next is, and in more places.</summary>
    [Fact]
    public void ASmallerHullIsAdmittedWhereverABiggerOneIs()
    {
        NavHulls three = NavHulls.Of(new(29f, 50f), new(49f, 83f), new(86f, 145f));
        foreach (NavNumbers n in new[] { Hl2mp, TF2 })
        {
            NavCell[] cells = Cells(Course(n with { ExtraHulls = three }).Graph);
            Assert.All(cells, c => Assert.True((!c.Admits(ClearanceClass.Hull4) || c.Admits(ClearanceClass.Hull3)) && (!c.Admits(ClearanceClass.Hull3) || c.Admits(ClearanceClass.Hull2))));
            int small = cells.Count(c => c.Admits(ClearanceClass.Hull2)), middle = cells.Count(c => c.Admits(ClearanceClass.Hull3)), giant = cells.Count(c => c.Admits(ClearanceClass.Hull4));
            Assert.True(small > middle && middle > giant && giant > 0, $"{small} > {middle} > {giant} > 0 places");
        }
    }
}
