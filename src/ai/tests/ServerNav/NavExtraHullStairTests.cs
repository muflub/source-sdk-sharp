using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The extra hulls (<see cref="NavNumbers.ExtraHulls"/>) on a stair, as the derivation classes it
/// today: what these facts pin is a limit to know of, not a goal.
/// <list type="bullet">
/// <item>A hull is admitted on a step where its box stands at the column's centre on the step's own
/// floor, or fits there one step up (the rule the game's own wide hull has,
/// <see cref="NavNumbers.WideHull"/>). A box that reaches further along the stair than that, to a
/// step two or more up, is admitted nowhere on it, though the game's movement would carry such a
/// body up the stair with its feet on the highest step under it.</item>
/// <item>Over a real <c>.nav3d</c> (the voxel fixture's, read by the real reader) with treads two
/// voxels deep, the game's own 48-unit hull climbs, a 29-unit one climbs, and a 49-unit one, a
/// unit wider than the game's, has no way up; nor has an 86-unit one. With treads one voxel
/// deep nobody climbs, the game's own hull included: the fixture's clearance records name a
/// step beside by its bottom, so no body is ever clear of it by being raised.</item>
/// <item>The step-up itself is held on the hand-built source the wide hull's own facts use
/// (<see cref="NavStairStandingTests"/>), whose steps hold no box at their own floor and any box a
/// step up, whatever its width: there every extra hull with the head room is stood on the steps
/// the game's own hull is stood on.</item>
/// <item>On the real ctf_2fort (measured 2026-11-11 at map-tools 3551fa8, the tree's .nav3d
/// 7ee026d58b2a, TF2's numbers): 29 by 50 is admitted in every cell and has every way from a
/// spawn to a flag the game's own hull has; 49 by 83 is admitted in 90.3 % of the cells and has
/// none of the four ways (its flow field reaches 3,526 cells of 162,712); 86 by 145 is admitted
/// in 64.9 % and reaches 30 cells. A hull of the game's own size is admitted in exactly the
/// 160,766 cells the game stands in.</item>
/// </list>
/// </summary>
public sealed class NavExtraHullStairTests
{
    private static readonly NavHulls Three = NavHulls.Of(new(29f, 50f), new(49f, 83f), new(86f, 145f));

    private const ClearanceClass Body = ClearanceClass.Crouch, Small = ClearanceClass.Hull2, Middle = ClearanceClass.Hull3, Giant = ClearanceClass.Hull4;

    private static readonly NavNumbers Numbers = TFNavProfile.ModNumbers with { ExtraHulls = Three };

    private const float Floor = 16f, Top = Floor + 8 * 16f;

    /// <summary>A floor (top at 16) 1024 by 256, eight steps across its whole width rising 16 each, each <paramref name="tread"/> deep, from x 256; then the landing at 144.</summary>
    private static NavGraph Stair(int tread)
    {
        var f = new VoxelNavFixture(4, 1, 24).Solid(0, 0, 0, 1024, 256, Floor);
        for (int s = 1; s <= 8; s++)
            f.Solid(256 + tread * (s - 1), 0, Floor, 1024, 256, Floor + 16 * s);
        return NavGraphFromNav3d.Derive(new Nav3dNavSource(f.Open()), Numbers, NavEdgeCosts.Default, []).Graph;
    }

    private static readonly Lazy<NavGraph> Deep = new(() => Stair(32)), Steep = new(() => Stair(16));

    /// <summary>Whether a body of <paramref name="cls"/> has a way from the floor to the landing, both of which admit it.</summary>
    private static bool Climbs(NavGraph g, int tread, ClearanceClass cls)
    {
        CellId low = g.FindCell(new AiVector(72f, 120f, Floor + 1f)), high = g.FindCell(new AiVector(256f + tread * 8 + 104f, 120f, Top + 1f));
        Assert.True(low.IsValid && high.IsValid, "the floor and the landing are places");
        Assert.True(g.Cell(low).Admits(cls) && g.Cell(high).Admits(cls), $"{cls} stands on the floor and on the landing");
        var field = new FlowField();
        field.Build(g, high, cls, new NavObstacleState(g.ObstacleCount));
        return float.IsFinite(field.CostAt(low.Index));
    }

    private static int StepsAdmitting(NavGraph g, ClearanceClass cls)
        => Enumerable.Range(0, g.CellCount).Count(c => g.Cell(c).Admits(cls) && g.Cell(c).FloorZ > Floor && g.Cell(c).FloorZ < Top);

    /// <summary>The control: the stair is one the game's own hull climbs.</summary>
    [Fact]
    public void OnTreadsTwoVoxelsDeepTheGamesOwnHullClimbs() => Assert.True(Climbs(Deep.Value, 32, Body));

    [Fact]
    public void TheSmallHullClimbsThemToo() => Assert.True(Climbs(Deep.Value, 32, Small));

    /// <summary>Today's limit: 49 across reaches half a unit past the tread beside, into the step two up.</summary>
    [Fact]
    public void AHullAUnitWiderThanTheGamesHasNoWayUpThem()
        => Assert.Equal((false, 0), (Climbs(Deep.Value, 32, Middle), StepsAdmitting(Deep.Value, Middle)));

    /// <summary>Today's limit: the 86-unit hull stands on no step of a stair the game's own hull climbs.</summary>
    [Fact]
    public void TheGiantHasNoWayUpThem()
        => Assert.Equal((false, 0), (Climbs(Deep.Value, 32, Giant), StepsAdmitting(Deep.Value, Giant)));

    /// <summary>The fixture's own limit, the same for every body: with treads one voxel deep not even the game's hull has a step to stand on.</summary>
    [Fact]
    public void OnTreadsOneVoxelDeepNoHullClimbsTheGamesOwnIncluded()
    {
        NavGraph g = Steep.Value;
        Assert.Equal((false, false, false, false), (Climbs(g, 16, Body), Climbs(g, 16, Small), Climbs(g, 16, Middle), Climbs(g, 16, Giant)));
    }

    // ---------------------------------------------------------------- the step up

    /// <summary>NavStairStandingTests' stair: a landing, eight steps a wide box reaches past (it stands on none at its floor, and fits a step up), a landing at 128.</summary>
    private static (FakeNavSource Src, int Low, int High, int FirstStep) ModelStair()
    {
        var src = new FakeNavSource();
        int low = src.Add(0, 0, 0f);
        for (int x = 1; x < 4; x++)
            src.Add(x, 0, 0f);
        int first = -1;
        for (int s = 1; s <= 8; s++)
        {
            int step = src.Add(3 + s, 0, s * 16f);
            src.Step(step);
            first = first < 0 ? step : first;
        }

        int high = src.Add(12, 0, 128f);
        for (int x = 13; x < 16; x++)
            src.Add(x, 0, 128f);
        return (src, low, high, first);
    }

    /// <summary>Whether a body of <paramref name="cls"/> has a way up the model's stair, whose landings admit it.</summary>
    private static bool ClimbsTheModel(ClearanceClass cls)
    {
        (FakeNavSource src, int low, int high, int first) = ModelStair();
        DerivedNav d = NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, []);
        Assert.True(d.Graph.Cell(d.CellOfLeaf[low]).Admits(cls) && d.Graph.Cell(d.CellOfLeaf[high]).Admits(cls), $"{cls} stands on both landings");
        var field = new FlowField();
        field.Build(d.Graph, new CellId(d.CellOfLeaf[high]), cls, new NavObstacleState(0));
        return d.Graph.Cell(d.CellOfLeaf[first]).Admits(cls) && float.IsFinite(field.CostAt(d.CellOfLeaf[low]));
    }

    [Fact]
    public void AnExtraHullIsStoodAStepUpOnAStair() => Assert.True(ClimbsTheModel(Middle));

    /// <summary>
    /// A hull narrower than the game's own is stood a step up too: its box, wider than a voxel,
    /// meets the step beside as the game's does. Without it, on ctf_2fort, the 29-unit hull had
    /// no way from a spawn to a flag.
    /// </summary>
    [Fact]
    public void AHullNarrowerThanTheGamesIsStoodAStepUpToo() => Assert.True(ClimbsTheModel(Small));

    /// <summary>
    /// The control for the two above: under numbers whose own hull is not stood a step up, the
    /// model's steps are no places at all, so no extra hull is admitted on one.
    /// </summary>
    [Fact]
    public void WhereTheGamesOwnHullHasNoPlaceAnExtraHullHasNone()
    {
        (FakeNavSource src, _, _, int first) = ModelStair();
        DerivedNav d = NavGraphFromNav3d.Derive(src, Numbers with { WideHull = false }, NavEdgeCosts.Default, []);
        Assert.Equal(-1, d.CellOfLeaf[first]);
    }

    /// <summary>A step under a ceiling too low for the hull raised is not one it stands on: 100 of room holds 83 a step up and not 145.</summary>
    [Fact]
    public void AStepTooLowForTheRaisedHullAdmitsItNot()
    {
        var src = new FakeNavSource();
        int step = src.Add(0, 0, 0f, ceiling: 18f + 100f);
        src.Step(step);
        NavCell cell = NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, []).Graph.Cell(0);
        Assert.Equal((true, false), (cell.Admits(Middle), cell.Admits(Giant)));
    }
}
