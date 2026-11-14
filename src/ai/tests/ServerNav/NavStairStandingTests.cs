using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A body wider than two voxels on a stair (plan_bots_teams_tf2 P6 step 6, live on ctf_2fort): at a
/// step's centre TF2's 48-unit box reaches into the next step's column, so it does not stand at the
/// step's floor, while the game's movement puts its feet on the step beside, one step up. A game
/// whose numbers say <see cref="NavNumbers.WideHull"/> takes such a floor as a place where the
/// body fits a step up; the core's default (HL2MP's) does not, and its graphs keep their names. On
/// ctf_2fort without it, each team's spawn and its flag were in different components (400 of
/// them): no route ran from a spawn to either flag.
/// </summary>
public sealed class NavStairStandingTests
{
    /// <summary>A landing (4 columns at 0), a stair of 8 steps rising 16 each, a landing at 128; the steps reach past a wide box.</summary>
    private static (FakeNavSource Src, int Low, int High) Stair()
    {
        var src = new FakeNavSource();
        int low = src.Add(0, 0, 0f);
        for (int x = 1; x < 4; x++)
            src.Add(x, 0, 0f);
        for (int s = 1; s <= 8; s++)
            src.Step(src.Add(3 + s, 0, s * 16f));
        int high = src.Add(12, 0, 128f);
        for (int x = 13; x < 16; x++)
            src.Add(x, 0, 128f);
        return (src, low, high);
    }

    private static NavNumbers Wide(bool onSteps) => new(48f, 82f, 62f, 18f, 72f, 85f, 270f, 0f, onSteps);

    private static bool Joined(NavNumbers n)
    {
        (FakeNavSource src, int low, int high) = Stair();
        DerivedNav d = NavGraphFromNav3d.Derive(src, n, NavEdgeCosts.Default, []);
        return d.Graph.ComponentOf(d.CellOfLeaf[low]) == d.Graph.ComponentOf(d.CellOfLeaf[high]);
    }

    [Fact]
    public void AWideHullClimbsTheStair() => Assert.True(Joined(Wide(onSteps: true)));

    [Fact]
    public void WithoutItTheStairHasNoPlaceAndTheLandingsAreApart() => Assert.False(Joined(Wide(onSteps: false)));

    [Fact]
    public void AStepTheBodyCannotFitEvenRaisedIsNoPlace()
    {
        (FakeNavSource src, int low, int high) = Stair();
        // A ceiling over the third step too low for the raised body: it stands crouched only, and with no room even for that, nowhere.
        int tight = src.Add(20, 0, 0f, ceiling: 70f);
        src.Step(tight);
        DerivedNav d = NavGraphFromNav3d.Derive(src, Wide(onSteps: true), NavEdgeCosts.Default, []);
        Assert.Equal(-1, d.CellOfLeaf[tight]);
        Assert.Equal(d.Graph.ComponentOf(d.CellOfLeaf[low]), d.Graph.ComponentOf(d.CellOfLeaf[high]));
    }

    /// <summary>A ledge (x 0..3 at z 64) over a floor (x 4..8 at z 0); the two columns below the lip reach back over the ledge for a wide box.</summary>
    private static bool Drops(bool wide)
    {
        var src = new FakeNavSource();
        int top = 0;
        for (int x = 0; x < 4; x++)
            top = src.Add(x, 0, 64f);
        src.BelowLedge(src.Add(4, 0, 0f));
        src.BelowLedge(src.Add(5, 0, 0f));
        int floor = src.Add(6, 0, 0f);
        for (int x = 7; x < 9; x++)
            src.Add(x, 0, 0f);
        DerivedNav d = NavGraphFromNav3d.Derive(src, Wide(wide), NavEdgeCosts.Default, []);
        var field = new FlowField();
        field.Build(d.Graph, new CellId(d.CellOfLeaf[floor]), ClearanceClass.Crouch, new NavObstacleState(0));
        return float.IsFinite(field.CostAt(d.CellOfLeaf[top]));
    }

    /// <summary>Off a ledge, as the two-voxel body that clears its lip: live on ctf_2fort, without it a red bot's spawn reached 13,792 cells, all in its own base.</summary>
    [Fact]
    public void AWideBodyDropsPastALedgesLip() => Assert.True(Drops(wide: true));

    [Fact]
    public void WithoutItTheLedgeIsAOneWayIsland() => Assert.False(Drops(wide: false));

    [Fact]
    public void TF2sHullIsWideAndTheCoresIsNot()
    {
        Assert.True(TFNavProfile.ModNumbers.WideHull);
        Assert.False(NavNumbers.Stock(56f).WideHull);
    }

    [Fact]
    public void AWideHullIsPartOfACachedGraphsName()
        => Assert.NotEqual(NavGraphCache.NumbersKey(TFNavProfile.ModNumbers, 56f),
                           NavGraphCache.NumbersKey(TFNavProfile.ModNumbers with { WideHull = false }, 56f));
}
