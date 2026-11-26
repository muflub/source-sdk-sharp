using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The shared walk costs (round 2's S8, <see cref="TFWalkCosts"/>): a capped search that runs out answers a lower bound, not
/// "no walk"; an answer is kept for every bot that asks again; and no way is infinite.
/// </summary>
public sealed class TFWalkCostsTests
{
    private static readonly AiVector West = new(32f, 32f, 0f), East = new(4000f, 32f, 0f);

    /// <summary>
    /// Across the field with a budget of 5 cells the search runs out: the answer is not whole, and its cost is a bound between
    /// the straight line and the whole walk's cost, not infinite. With room, the walk is whole.
    /// </summary>
    [Fact]
    public void ASearchThatWouldExhaustTheBudgetGivesABoundedAnswer()
    {
        NavGraph g = TFField.Floor();
        TFWalk whole = new TFWalkCosts().Walk(g, West, East, 100_000, 0);
        TFWalk capped = new TFWalkCosts().Walk(g, West, East, 5, 0);
        Assert.True(whole.Whole);
        Assert.False(capped.Whole);
        Assert.InRange(capped.Cost, West.DistanceTo(East) - 64f, whole.Cost);
        Assert.Equal(5, capped.Expanded);
    }

    /// <summary>The same ends asked again within the keep are answered kept (no search, no cells); after it, searched again.</summary>
    [Fact]
    public void AnAnswerIsKeptForTheNextAsker()
    {
        NavGraph g = TFField.Floor();
        var walks = new TFWalkCosts();
        TFWalk first = walks.Walk(g, West, East, 100_000, 100);
        TFWalk again = walks.Walk(g, West, East, 100_000, 100 + TFWalkCosts.KeptTicks);
        TFWalk later = walks.Walk(g, West, East, 100_000, 101 + 2 * TFWalkCosts.KeptTicks);
        Assert.Equal((false, true, false), (first.Kept, again.Kept, later.Kept));
        Assert.Equal(first.Cost, again.Cost);
        Assert.Equal((0, 2L, 1L), (again.Expanded, walks.Work.Searches, walks.Work.KeptHits));
    }

    /// <summary>An end off the graph is no way: infinite.</summary>
    [Fact]
    public void AnEndOffTheGraphIsNoWay()
        => Assert.True(float.IsPositiveInfinity(new TFWalkCosts().Walk(TFField.Floor(), West, new AiVector(9000f, 9000f, 0f), 100_000, 0).Cost));
}
