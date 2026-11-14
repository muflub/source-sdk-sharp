using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A link of the level's set as the follower and the program take it (lane G5c-2b): it starts where its recipe's
/// first command is, and its plan is the recipe from standing there, numbered by the link's argument, with the
/// simulator's flight of it.
/// </summary>
public class JumpLinkSetPlanTests
{
    private static JumpLinkSet Set(float runUp)
    {
        var (g, ids) = Grids.Parse("..... .....");
        var recipe = new JumpRecipe(new Vector(144f, 16f, 0f), new Vector(208f, 16f, 0f), runUp, 60f, 180f, 2, false);
        return JumpLinkSet.Of(g, [new JumpLink(ids[4, 0], ids[6, 0], recipe, JumpFamily.Up, 0.9f, 2f, 1.5f, 40f, null)], new NavLinkCosts { RunSpeed = 240f });
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(96f)]
    public void ALinkStartsWhereItsRecipesFirstCommandIs(float runUp)
    {
        JumpLinkSet set = Set(runUp);
        Vector start = set.JumpOf(0).Recipe.Start().Origin;
        Assert.Equal(new AiVector(start.X, start.Y, start.Z), set.Links[0].Start);
        Assert.Equal(144f - runUp, set.Links[0].Start.X);
    }

    [Fact]
    public void ALinksPlanIsItsRecipeFromStandingNumberedByItsArgumentWithTheSimulatorsFlight()
    {
        JumpLinkSet set = Set(0f);
        var flown = new JumpOutcome(true, new Vector(207f, 15f, 0f), new Vector(210f, 15f, 0f), true, 2.1f, 1.4f, 260f, 40f, 1, 140);
        JumpPlan plan = set.PlanOf(0, flown);
        Assert.Equal((NavLinks.Argument(0), true, set.JumpOf(0).Recipe), (plan.Id, plan.StartsStanding, plan.Recipe));
        Assert.Equal((flown.Touch, flown.Apex, flown.AirSeconds), (plan.Touch, plan.Apex, plan.AirSeconds));
        Assert.Equal((40f, 0.9f, JumpLinkSet.LinkSlide), (plan.Health, plan.Robustness, plan.Slide));
    }
}
