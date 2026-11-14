using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.TF2;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// What one jump a bot made of a blast link adds to the link's row in the experience (lane G5e, plan G.5 step 7): a
/// landing near its touch a try and a success, any other end after the launch a try, and nothing for a jump called off
/// before the launch, a fizzle on a teammate's body, a body the game moved, or a stretch's jump; and the row's key, the
/// same for the same recipe and another for another.
/// </summary>
public sealed class TFLinkExperienceTests
{
    private const float Tick = 0.015f;

    /// <summary>A link's plan whose touch is at (1690, 256): the facts' plan, numbered as link 0's.</summary>
    private static JumpPlan LinkPlan => TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };

    private static JumpFlown Flown(JumpEnd end, Vector touch, bool launched = true, int feltAfterShot = -1, JumpPlan? plan = null)
    {
        JumpPlan p = plan ?? LinkPlan;
        return new JumpFlown(p, p.Recipe, p.Recipe.Start(), launched, 0f, 40, 120, feltAfterShot >= 0, touch, end, Tick) { FeltAfterShot = feltAfterShot };
    }

    [Fact]
    public void ALandingNearTheTouchIsATryAndASuccessWithItsSecondsAndHealth()
    {
        LinkStat? stat = TFLinkExperience.StatOf(Flown(JumpEnd.Landed, LinkPlan.Touch + new Vector(10f, 0f, 0f)));
        Assert.Equal(new LinkStat(1d, 1d, (40 + 120) * Tick, LinkPlan.Health), stat);
    }

    [Fact]
    public void ALandingElsewhereIsATryThatFailed()
        => Assert.Equal(new LinkStat(1d, 0d, 0d, 0d), TFLinkExperience.StatOf(Flown(JumpEnd.Landed, LinkPlan.Touch + new Vector(0f, JumpTally.NearUnits + 1f, 0f))));

    [Fact]
    public void AFizzleWithNoBlastFeltIsATryThatFailed()
        => Assert.Equal(new LinkStat(1d, 0d, 0d, 0d), TFLinkExperience.StatOf(Flown(JumpEnd.Fizzled, LinkPlan.Recipe.Takeoff)));

    [Fact]
    public void AFizzleFeltRightAfterTheTriggerAddsNothing()
    {
        Assert.Null(TFLinkExperience.StatOf(Flown(JumpEnd.Fizzled, LinkPlan.Recipe.Takeoff, feltAfterShot: TFLinkExperience.TeammateFeltCommands)));
        // The control: a blast felt later went off where the recipe aimed it, and the fizzle is the link's.
        Assert.NotNull(TFLinkExperience.StatOf(Flown(JumpEnd.Fizzled, LinkPlan.Recipe.Takeoff, feltAfterShot: TFLinkExperience.TeammateFeltCommands + 1)));
    }

    [Fact]
    public void AJumpCalledOffBeforeTheLaunchAddsNothing()
    {
        Assert.Null(TFLinkExperience.StatOf(Flown(JumpEnd.ViewLate, LinkPlan.Recipe.Takeoff, launched: false)));
        Assert.Null(TFLinkExperience.StatOf(Flown(JumpEnd.Astray, LinkPlan.Recipe.Takeoff, launched: false)));
    }

    [Fact]
    public void ABodyTheGameMovedAddsNothing()
        => Assert.Null(TFLinkExperience.StatOf(Flown(JumpEnd.Moved, new Vector(0f, 0f, 0f))));

    [Fact]
    public void AStretchsJumpIsNoLinksTry()
        => Assert.Null(TFLinkExperience.StatOf(Flown(JumpEnd.Landed, LinkPlan.Touch, plan: TFJumpAdvice.Plan(id: 7))));

    [Fact]
    public void ALinksRowIsTheSoldiersFromItsStartsBinToItsLandingsBin()
    {
        JumpRecipe recipe = LinkPlan.Recipe;
        LinkKey key = TFLinkExperience.KeyOf(recipe);
        Vector start = recipe.Start().Origin;
        Assert.Equal((int)TFClass.Soldier, key.Class);
        Assert.Equal(WorldBin.Of(new AiVector(start.X, start.Y, start.Z)), key.From);
        Assert.Equal(WorldBin.Of(new AiVector(recipe.Landing.X, recipe.Landing.Y, recipe.Landing.Z)), key.To);
        Assert.Equal(((int)NavLinkKind.Blast, LinkSource.Solver, TFLinkExperience.RecipeFormat), (key.Kind, key.Source, key.Recipe.Bytes[0]));
    }

    [Fact]
    public void TheSameRecipeIsTheSameRowAndAnotherIsAnother()
    {
        JumpRecipe recipe = LinkPlan.Recipe;
        Assert.Equal(TFLinkExperience.KeyOf(recipe), TFLinkExperience.KeyOf(recipe with { }));
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe), TFLinkExperience.KeyOf(recipe with { Pitch = recipe.Pitch + 1f }));
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe), TFLinkExperience.KeyOf(recipe with { ShotDelay = recipe.ShotDelay + 1 }));
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe), TFLinkExperience.KeyOf(recipe with { Ducked = !recipe.Ducked }));
    }

    [Fact]
    public void ALinkRowsRecipeFormatsAreFiveDifferentBytes()
    {
        // A solver's one-rocket, two-rocket and wall-jump recipes and a shown jump's exemplar and count rows: a reader tells a
        // row's recipe by its first byte whatever its source, so no two share one.
        byte[] formats = [TFLinkExperience.RecipeFormat, TFLinkExperience.SecondRecipeFormat, TFLinkExperience.WallRecipeFormat, ShownJumpRecipe.Format, ShownJumpRecipe.CountFormat];
        Assert.Equal(5, formats.Distinct().Count());
    }

    [Fact]
    public void AWallJumpsRecipeIsFormatFiveAndOtherwiseADoublesBytes()
    {
        var shot = new JumpSecondShot(new Vector(7.25f, 8.5f, 380.125f), 54, 78, 58f, -90f);
        JumpRecipe floor = LinkPlan.Recipe with { Second = shot }, wall = LinkPlan.Recipe with { Second = shot with { Wall = true } };
        byte[] two = [.. TFLinkExperience.RecipeOf(floor).Bytes], walled = [.. TFLinkExperience.RecipeOf(wall).Bytes];
        Assert.Equal((TFLinkExperience.SecondRecipeFormat, TFLinkExperience.WallRecipeFormat), (two[0], walled[0]));
        Assert.Equal(two[1..], walled[1..]);
        Assert.NotEqual(TFLinkExperience.KeyOf(floor), TFLinkExperience.KeyOf(wall));
    }

    [Fact]
    public void AOneRocketRecipesBytesAreFormatOnesAndASecondRocketsAreFormatTwos()
    {
        // Format 1: the byte, nine floats, an int and the duck: what a one-rocket row has always been keyed by.
        JumpRecipe recipe = LinkPlan.Recipe;
        byte[] one = [.. TFLinkExperience.RecipeOf(recipe).Bytes];
        Assert.Equal((1 + 9 * 4 + 4 + 1, TFLinkExperience.RecipeFormat), (one.Length, one[0]));
        Assert.Equal(recipe.Pitch, BitConverter.ToSingle(one, 1 + 7 * 4));

        // Format 2: the same, then the second shot's place, earliest and latest, pitch and yaw offset.
        var second = new JumpSecondShot(new Vector(11.5f, 22.25f, 380f), 54, 78, 89f, 175f);
        byte[] two = [.. TFLinkExperience.RecipeOf(recipe with { Second = second }).Bytes];
        Assert.Equal((one.Length + 5 * 4 + 2 * 4, TFLinkExperience.SecondRecipeFormat), (two.Length, two[0]));
        Assert.Equal(one[1..], two[1..one.Length]);
        Assert.Equal((11.5f, 22.25f, 380f), (BitConverter.ToSingle(two, one.Length), BitConverter.ToSingle(two, one.Length + 4), BitConverter.ToSingle(two, one.Length + 8)));
        Assert.Equal((54, 78), (BitConverter.ToInt32(two, one.Length + 12), BitConverter.ToInt32(two, one.Length + 16)));
        Assert.Equal((89f, 175f), (BitConverter.ToSingle(two, one.Length + 20), BitConverter.ToSingle(two, one.Length + 24)));
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe), TFLinkExperience.KeyOf(recipe with { Second = second }));
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe with { Second = second }), TFLinkExperience.KeyOf(recipe with { Second = second with { Latest = 79 } }));
    }

    [Fact]
    public void EachSolverFormatIsReadBackAsTheRecipeItWasWrittenFrom()
    {
        // Lane G8: a practice row's recipe is read back to be flown. One, two and wall rockets, each the recipe it was.
        var shot = new JumpSecondShot(new Vector(7.25f, 8.5f, 380.125f), 54, 78, 58f, -90f);
        foreach (JumpRecipe recipe in (JumpRecipe[])[LinkPlan.Recipe, LinkPlan.Recipe with { Second = shot }, LinkPlan.Recipe with { Second = shot with { Wall = true } }])
        {
            Assert.True(TFLinkExperience.TryRecipe(TFLinkExperience.RecipeOf(recipe), out JumpRecipe read));
            Assert.Equal(recipe, read);
        }
    }

    [Fact]
    public void BytesNoSolverWroteAreNoRecipe()
    {
        byte[] one = [.. TFLinkExperience.RecipeOf(LinkPlan.Recipe).Bytes];
        Assert.True(TFLinkExperience.TryRecipe(new LinkRecipe([.. one]), out _));

        // A person's exemplar and count rows, a cut-short row, a long one, a duck byte of 2, and a pitch that is no number.
        Assert.False(TFLinkExperience.TryRecipe(ShownJumpRecipe.CountRecipe, out _));
        Assert.False(TFLinkExperience.TryRecipe(new LinkRecipe([ShownJumpRecipe.Format, .. one[1..]]), out _));
        Assert.False(TFLinkExperience.TryRecipe(new LinkRecipe([.. one[..^1]]), out _));
        Assert.False(TFLinkExperience.TryRecipe(new LinkRecipe([.. one, 0]), out _));
        Assert.False(TFLinkExperience.TryRecipe(new LinkRecipe([.. one[..^1], 2]), out _));
        byte[] nan = [.. one];
        BitConverter.GetBytes(float.NaN).CopyTo(nan, 1 + 7 * 4);
        Assert.False(TFLinkExperience.TryRecipe(new LinkRecipe([.. nan]), out _));
        Assert.False(TFLinkExperience.TryRecipe(LinkRecipe.None, out _));
    }

    [Fact]
    public void APracticeRowIsTheSolversRowOfAnotherSource()
    {
        JumpRecipe recipe = LinkPlan.Recipe;
        LinkKey practice = TFLinkExperience.PracticeKeyOf(recipe);
        Assert.Equal(LinkSource.Practice, practice.Source);
        Assert.Equal(TFLinkExperience.KeyOf(recipe) with { Source = LinkSource.Practice }, practice);
        Assert.NotEqual(TFLinkExperience.KeyOf(recipe), practice);
    }

    [Fact]
    public void AFamilyIsReadFromTheRecipesOwnNumbers()
    {
        var shot = new JumpSecondShot(new Vector(7.25f, 8.5f, 380.125f), 54, 78, 58f, -90f);
        JumpRecipe r = LinkPlan.Recipe;
        Assert.Equal(JumpFamily.Up, TFLinkExperience.FamilyOf(r with { RunUp = 0f }));
        Assert.Equal(JumpFamily.Forward, TFLinkExperience.FamilyOf(r with { RunUp = 160f }));
        Assert.Equal(JumpFamily.Double, TFLinkExperience.FamilyOf(r with { RunUp = 0f, Second = shot }));
        Assert.Equal(JumpFamily.Wall, TFLinkExperience.FamilyOf(r with { RunUp = 0f, Second = shot with { Wall = true } }));
    }

    [Fact]
    public void ASetsKeysAreItsLinksRecipesIndexForIndexAndMadeOnceASet()
    {
        var (g, ids) = Grids.Parse("..... ..... .....");
        var a = new JumpRecipe(new Vector(144f, 16f, 0f), new Vector(208f, 16f, 0f), 0f, 60f, 180f, 2, false);
        JumpRecipe b = a with { Takeoff = new Vector(336f, 16f, 0f), Landing = new Vector(400f, 16f, 0f) };
        JumpLinkSet set = JumpLinkSet.Of(g, [new JumpLink(ids[4, 0], ids[6, 0], a, JumpFamily.Up, 0.9f, 2f, 1.5f, 40f, null), new JumpLink(ids[10, 0], ids[12, 0], b, JumpFamily.Up, 0.9f, 2f, 1.5f, 40f, null)],
                                         new NavLinkCosts { RunSpeed = 240f });
        var published = new TFBlastLinks();
        var experience = new TFLinkExperience(published);
        Assert.Null(experience.Keys);
        published.Set = set;
        ExperienceLinkKeys keys = experience.Keys!;
        Assert.Same(set.Links, keys.Links);
        Assert.Equal((TFLinkExperience.KeyOf(a), TFLinkExperience.KeyOf(b)), (keys[0], keys[1]));
        Assert.Same(keys, experience.Keys);
    }
}

/// <summary>
/// A body the game moves in the middle of a jump (a round's restart puts it on its spawn) ends the jump as moved, not as
/// a landing wherever it was put (lane G5e: the tool counted one as landed 1,415 u from its touch).
/// </summary>
public sealed class RocketJumpMovedTests
{
    private static readonly MotionParams Normal = new(250f, 600f, 10f, 150f);

    [Fact]
    public void ABodyMovedFurtherThanAnyJumpCarriesItEndsTheJumpAsMoved()
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        JumpPlan plan = TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [plan]);
        var flown = new List<JumpFlown>();
        var program = new RocketJumpProgram(book, JumpRig.Classes(), flown.Add);
        Vector start = plan.Recipe.Start().Origin;
        var body = new AbilityBody(new AiVector(start.X - 30f, start.Y, start.Z), AiVector.Zero, new AiAngles(0f, 0f, 0f), true);
        IAbilityRun run = program.Begin(NavLinks.Argument(0), body, Normal, 0.015f)!;
        Assert.False(run.Step(body).Over);
        // The control: a step a body can take is no move of the game's.
        body = body with { Origin = new AiVector(start.X - 20f, start.Y, start.Z) };
        Assert.False(run.Step(body).Over);
        Assert.Empty(flown);
        Assert.True(run.Step(body with { Origin = new AiVector(start.X - 20f + RocketJumpProgram.MovedUnits + 1f, start.Y, start.Z) }).Over);
        Assert.Equal(JumpEnd.Moved, Assert.Single(flown).End);
    }
}
