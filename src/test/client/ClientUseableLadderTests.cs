using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.ClientDoubles;

namespace SourceSharp.Tests;

/// <summary>
/// The client half of func_useableladder finds its dismount points as stock's
/// C_FuncLadder does (func_ladder.cpp's CLIENT_DLL arm of
/// FindNearbyDismountPoints): every point it knows within 100 of an end or of
/// a probe 40 apart up the ladder, with no target to filter by, and once each.
/// </summary>
public sealed class ClientUseableLadderTests
{
    private static ClientUseableLadder Ladder(Vector bottom, Vector top)
    {
        var ladder = new ClientUseableLadder(Fake());
        ladder.State.BindLocal();
        ladder.State.Bottom = bottom;
        ladder.State.Top = top;
        return ladder;
    }

    private static ClientLadderDismount Spot(Vector at)
    {
        var spot = new ClientLadderDismount(Fake());
        spot.State.BindLocal();
        spot.State.Origin = at;
        return spot;
    }

    /// <summary>Near an end is claimed; far from every probe is not.</summary>
    [Fact]
    public void ThePointsNearTheLadderAreClaimed()
    {
        var ladder = Ladder(Vector.Zero, new Vector(0f, 0f, 128f));
        var near = Spot(new Vector(0f, 40f, 129f));
        var far = Spot(new Vector(500f, 0f, 0f));

        ladder.SearchForDismountPoints(new[] { near, far });

        Assert.Equal(new[] { new Vector(0f, 40f, 129f) }, ladder.DismountOrigins);
    }

    /// <summary>A point near the middle of a long ladder is found by the probes up it.</summary>
    [Fact]
    public void TheProbesUpALongLadderFindTheMiddle()
    {
        var ladder = Ladder(Vector.Zero, new Vector(0f, 0f, 400f));

        ladder.SearchForDismountPoints(new[] { Spot(new Vector(90f, 0f, 200f)) });

        Assert.Single(ladder.DismountOrigins);
    }

    /// <summary>
    /// Searching again adds what is new and never a point twice: stock's
    /// client searches at each mount and its list only grows.
    /// </summary>
    [Fact]
    public void ASecondSearchAddsOnlyWhatIsNew()
    {
        var ladder = Ladder(Vector.Zero, new Vector(0f, 0f, 128f));
        var first = Spot(new Vector(0f, 40f, 0f));
        var later = Spot(new Vector(0f, -40f, 0f));

        ladder.SearchForDismountPoints(new[] { first });
        ladder.SearchForDismountPoints(new[] { first, later });

        Assert.Equal(new[] { new Vector(0f, 40f, 0f), new Vector(0f, -40f, 0f) }, ladder.DismountOrigins);
    }
}
