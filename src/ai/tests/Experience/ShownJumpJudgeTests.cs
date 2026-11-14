using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// Whether a jump a player showed is one the level has no way for (lane G6a, <see cref="ShownJumpJudge"/>): no walk at
/// all, or one over 2.5 times the straight line, is learned; a shorter walk, one cell, a place off the graph or one of the
/// level's links for the class is not.
/// </summary>
public sealed class ShownJumpJudgeTests
{
    private const int Soldier = 3;

    /// <summary>The middle of grid cell (x, y), at <paramref name="z"/>.</summary>
    private static AiVector At(int x, int y, int rows, float z = 0f) => new((x + 0.5f) * Grids.Size, (rows - 1 - y + 0.5f) * Grids.Size, z);

    private static ShownJumpRecipe Jump(AiVector from, AiVector to)
        => new(ShownJumpKind.Blast, ShownJumpFlags.OwnBlast, from, to, default, default, default, default, 40, 30, 0, -1, -1, 0, []);

    [Fact]
    public void AJumpBetweenPlacesWithNoWayIsLearned()
    {
        (NavGraph graph, _) = Grids.Parse("....  ....");
        Assert.Equal(ShownJumpVerdict.NoWay, ShownJumpJudge.Of(graph).Judge(Jump(At(1, 0, 1), At(7, 0, 1)), Soldier, null));
    }

    [Fact]
    public void AJumpThatSavesAWalkOverTwoAndAHalfTimesItsLineIsLearned()
    {
        (NavGraph graph, _) = Grids.Parse(
            "..........",
            ".........x",
            "x........x",
            "xxxxxxxx.x",
            "..........");
        // Over the wall from the bottom row's left end to the row above it: the walk goes round the right end.
        Assert.Equal(ShownJumpVerdict.LongWalk, ShownJumpJudge.Of(graph).Judge(Jump(At(0, 4, 5), At(1, 2, 5)), Soldier, null));
    }

    [Fact]
    public void AJumpAlongAWalkIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse("..........");
        Assert.Equal(ShownJumpVerdict.Walkable, ShownJumpJudge.Of(graph).Judge(Jump(At(0, 0, 1), At(9, 0, 1)), Soldier, null));
    }

    [Fact]
    public void AJumpWithinOneCellIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse("..........");
        Assert.Equal(ShownJumpVerdict.Same, ShownJumpJudge.Of(graph).Judge(Jump(At(2, 0, 1), At(2, 0, 1) + new AiVector(3f, 3f, 0f)), Soldier, null));
    }

    [Fact]
    public void AJumpWithinOneBinIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse("..........");
        Assert.Equal(ShownJumpVerdict.Same, ShownJumpJudge.Of(graph).Judge(Jump(At(0, 0, 1), At(1, 0, 1)), Soldier, null));
    }

    [Fact]
    public void AJumpThatSavesUnderTwoBinsOfWalkIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse(
            "..",
            "x.",
            "..");
        // Over the wall from the bottom left cell's top edge to the top left cell's bottom edge, 36 u: round it is 128 u
        // from cell to cell, 3.6 times the line but 92 u saved. The ends are in two bins (y 30 and 66).
        Assert.Equal(ShownJumpVerdict.Walkable, ShownJumpJudge.Of(graph).Judge(Jump(new AiVector(16f, 30f, 0f), new AiVector(16f, 66f, 0f)), Soldier, null));
    }

    [Fact]
    public void ALandingJustUnderItsCellsFloorIsOnIt()
    {
        (NavGraph graph, _) = Grids.Parse("....  ....");
        Assert.Equal(ShownJumpVerdict.NoWay, ShownJumpJudge.Of(graph).Judge(Jump(At(1, 0, 1), At(7, 0, 1, z: -10f)), Soldier, null));
    }

    [Fact]
    public void ALandingOffTheGraphIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse("....  ....");
        Assert.Equal(ShownJumpVerdict.NoCell, ShownJumpJudge.Of(graph).Judge(Jump(At(1, 0, 1), At(5, 0, 1)), Soldier, null));
    }

    [Fact]
    public void ALandingWellAboveItsCellsFloorIsNotLearned()
    {
        (NavGraph graph, _) = Grids.Parse("....  ....");
        Assert.Equal(ShownJumpVerdict.NoCell, ShownJumpJudge.Of(graph).Judge(Jump(At(1, 0, 1), At(7, 0, 1, z: 60f)), Soldier, null));
    }

    /// <summary>A link of the soldier's whose row says it starts in <paramref name="from"/>'s bin and lands in <paramref name="to"/>'s.</summary>
    private static ExperienceLinkKeys Link(NavGraph graph, int[,] ids, AiVector from, AiVector to, int cls = Soldier)
    {
        NavLinks set = NavLinks.Build(graph, [new NavLink(ids[1, 0], ids[7, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        return new ExperienceLinkKeys(set, [new LinkKey(cls, WorldBin.Of(from), WorldBin.Of(to), 0, LinkSource.Solver, new LinkRecipe([1, 2]))]);
    }

    [Fact]
    public void AJumpOneOfTheLevelsLinksMakesIsNotLearned()
    {
        (NavGraph graph, int[,] ids) = Grids.Parse("....  ....");
        ShownJumpRecipe jump = Jump(At(1, 0, 1), At(7, 0, 1));
        Assert.Equal(ShownJumpVerdict.Known, ShownJumpJudge.Of(graph).Judge(jump, Soldier, Link(graph, ids, jump.Takeoff, jump.Landing)));
    }

    [Fact]
    public void ALinkThatStartsARunUpBackStillMakesTheJump()
    {
        (NavGraph graph, int[,] ids) = Grids.Parse("....  ....");
        ShownJumpRecipe jump = Jump(At(3, 0, 1), At(7, 0, 1));
        Assert.Equal(ShownJumpVerdict.Known, ShownJumpJudge.Of(graph).Judge(jump, Soldier, Link(graph, ids, jump.Takeoff - new AiVector(150f, 0f, 0f), jump.Landing)));
    }

    [Fact]
    public void ALinkLandingTwoBinsAwayIsAnotherJump()
    {
        (NavGraph graph, int[,] ids) = Grids.Parse("....  ....");
        ShownJumpRecipe jump = Jump(At(1, 0, 1), At(7, 0, 1));
        Assert.Equal(ShownJumpVerdict.NoWay, ShownJumpJudge.Of(graph).Judge(jump, Soldier, Link(graph, ids, jump.Takeoff, jump.Landing + new AiVector(0f, 128f, 0f))));
    }

    [Fact]
    public void AnotherClasssLinkIsNoWayForThisOne()
    {
        (NavGraph graph, int[,] ids) = Grids.Parse("....  ....");
        ShownJumpRecipe jump = Jump(At(1, 0, 1), At(7, 0, 1));
        Assert.Equal(ShownJumpVerdict.NoWay, ShownJumpJudge.Of(graph).Judge(jump, Soldier, Link(graph, ids, jump.Takeoff, jump.Landing, cls: 7)));
    }

    [Fact]
    public void OnlyTheNoWayAndTheLongWalkAreLearned()
        => Assert.Equal([ShownJumpVerdict.LongWalk, ShownJumpVerdict.NoWay], Enum.GetValues<ShownJumpVerdict>().Where(ShownJumpJudge.Learned));
}
