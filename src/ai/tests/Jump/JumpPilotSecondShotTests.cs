using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The pilot's second rocket (lane G7b): pressed in the air once the launcher is ready, as the body comes over the place
/// the recipe names, or at its latest; never from under its floor or with the view elsewhere. Flown on the open field;
/// where a flight is is asked of the simulator in the fact.
/// </summary>
public class JumpPilotSecondShotTests
{
    /// <summary>A crouched jump from standing with the rocket behind and under, steered for a landing far along.</summary>
    private static JumpRecipe Single => new(OnTheField(), OnTheField() + new Vector(3000f, 0f, 0f), 0f, 60f, 180f, 2, true);

    private static JumpRecipe Doubled(Vector over, int earliest = 54, int latest = 90, float pitch = 89f, float yawOffset = 180f)
        => Single with { Second = new JumpSecondShot(over, earliest, latest, pitch, yawOffset) };

    /// <summary>The commands that press the trigger, in order.</summary>
    private static int[] Triggers(Piloted flown) => [.. Enumerable.Range(0, flown.Sent.Count).Where(i => (flown.Sent[i].Buttons & InAttack) != 0)];

    /// <summary>The first command of the second trigger, or -1.</summary>
    private static int SecondTrigger(Piloted flown)
    {
        int[] pressed = Triggers(flown);
        return pressed.Length > JumpPilot.TriggerCommands ? pressed[JumpPilot.TriggerCommands] : -1;
    }

    /// <summary>How far before <paramref name="over"/> along the way +x the body is.</summary>
    private static float Before(Vector over, Vector body) => over.X - body.X;

    /// <summary>Where the single flight's body is, on the floor's level, <paramref name="sinceShot"/> commands after its trigger: in the air.</summary>
    private static Vector PlaceOfTheSingle(JumpFlier flier, int sinceShot)
    {
        Piloted single = Pilot(flier, Single);
        JumpPoint at = single.Track[single.Trigger + sinceShot];
        Assert.False(at.OnGround, $"the control: the single flight is in the air {sinceShot} commands after its trigger");
        return at.Origin with { Z = 0f };
    }

    [Fact]
    public void TheSecondTriggerIsPressedAsTheBodyComesOverItsPlace()
    {
        JumpFlier flier = Flier(Field());
        Vector over = PlaceOfTheSingle(flier, 64);
        Piloted flown = Pilot(flier, Doubled(over));
        int second = SecondTrigger(flown), first = flown.Trigger;
        Assert.True(second - first > 54, $"the control: the body comes over the place after the launcher is ready: {second - first} commands after the first");

        // The pilot sees the body the command before left: past the place at the press, short of it the command before.
        Assert.True(Before(over, flown.Track[second - 1].Origin) <= 0f, $"pressed {Before(over, flown.Track[second - 1].Origin)} short of the place");
        Assert.True(Before(over, flown.Track[second - 2].Origin) > 0f, "the press came a command late");
        Assert.Equal(2 * JumpPilot.TriggerCommands, Triggers(flown).Length);
        Assert.Equal(2, flown.Outcome.Rockets);
    }

    [Fact]
    public void NeverBeforeTheLauncherIsReady()
    {
        JumpFlier flier = Flier(Field());
        foreach (int earliest in (int[])[54, 70])
        {
            // Its place is the takeoff, which the body is past from the jump on.
            Piloted flown = Pilot(flier, Doubled(OnTheField() with { Z = 0f }, earliest, latest: 100));
            Assert.Equal(earliest, SecondTrigger(flown) - flown.Trigger);
        }
    }

    [Fact]
    public void ABodyThatNeverComesOverItsPlaceFiresNothing()
    {
        // Lane G7c: a second rocket fired at its latest wherever the body was never paid in the game (G7b: 4 of 4 failures).
        JumpFlier flier = Flier(Field());
        Assert.True(SecondTrigger(Pilot(flier, Doubled(PlaceOfTheSingle(flier, 64), latest: 75))) > 0, "the control: a place the body comes over in its time is fired at");
        foreach (int latest in (int[])[60, 75])
        {
            Piloted flown = Pilot(flier, Doubled(OnTheField() + new Vector(9000f, 0f, -OnTheField().Z), latest: latest));
            Assert.True(Before(OnTheField() + new Vector(9000f, 0f, 0f), flown.Track[^1].Origin) > 0f, "the control: the body never comes over the place");
            Assert.Equal(-1, SecondTrigger(flown));
            Assert.Equal(1, flown.Outcome.Rockets);
        }
    }

    [Fact]
    public void ABodyThatComesOverItsPlaceOnlyAfterItsLatestFiresNothing()
    {
        JumpFlier flier = Flier(Field());
        Vector over = PlaceOfTheSingle(flier, 64);
        Assert.True(SecondTrigger(Pilot(flier, Doubled(over, latest: 75))) > 0, "the control: within its time it is fired");
        Piloted late = Pilot(flier, Doubled(over, latest: 60));
        Assert.Equal(-1, SecondTrigger(late));
        Assert.Equal(1, late.Outcome.Rockets);
    }

    [Fact]
    public void NeverFromUnderTheFloorItIsFiredAt()
    {
        JumpFlier flier = Flier(Field());
        Vector over = PlaceOfTheSingle(flier, 64);
        Assert.True(SecondTrigger(Pilot(flier, Doubled(over))) > 0, "the control: over the field's floor it is fired");
        Piloted flown = Pilot(flier, Doubled(over with { Z = 4000f }));
        Assert.Equal(-1, SecondTrigger(flown));
        Assert.Equal(1, flown.Outcome.Rockets);
    }

    [Fact]
    public void AViewNotOnTheSecondShotsInItsTimeFiresNothing()
    {
        // Looking ahead for the second shot after the first looked back: half a turn, which a slow hand does not make in its time.
        Vector over = OnTheField() with { Z = 0f };
        Piloted quick = Pilot(Flier(Field(), 360f), Doubled(over, latest: 70, pitch: 60f, yawOffset: 0f));
        Assert.True(SecondTrigger(quick) > 0, "the control: a quick hand gets there and fires");
        Piloted slow = Pilot(Flier(Field(), 60f), Doubled(over, latest: 70, pitch: 60f, yawOffset: 0f));
        Assert.True(slow.Trigger > 0, "the control: the slow hand did fire its first");
        Assert.Equal(-1, SecondTrigger(slow));
        Assert.Equal(1, slow.Outcome.Rockets);
    }

    [Fact]
    public void TheSecondRocketLeavesAlongTheSecondShotsView()
    {
        JumpFlier flier = Flier(Field());
        JumpRecipe recipe = Doubled(PlaceOfTheSingle(flier, 64), pitch: 80f, yawOffset: 160f);
        Piloted flown = Pilot(flier, recipe);
        int second = SecondTrigger(flown);
        Assert.Equal(new Vector(80f, 160f, 0f), recipe.SecondView);
        Assert.True(JumpPilot.ViewGap(flown.Sent[flown.Trigger].View, recipe.SecondView) > 10f, "the control: the first shot's view was another");
        Assert.True(JumpPilot.ViewGap(flown.Sent[second].View, recipe.SecondView) <= JumpPilot.SecondAimDegrees);
    }

    [Fact]
    public void ItLooksToTheSecondShotFromTheFirstTriggersReleaseUntilItsOwnIsLetGo()
    {
        JumpRecipe recipe = Doubled(new Vector(500f, 0f, 0f), latest: 80) with { Takeoff = Vector.Zero, Landing = new Vector(1000f, 0f, 0f) };
        var way = new Vector(0f, 0f, 0f);
        int released = recipe.ShotDelay + JumpPilot.TriggerCommands;
        Assert.Equal(recipe.ShotView, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, released - 1, true, false, default)));
        Assert.Equal(recipe.SecondView, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, released, true, false, default)));

        // Pressed on command 60 (Second holds it and one more): looked at through its last held command, then the way.
        Assert.Equal(recipe.SecondView, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, 60 + JumpPilot.TriggerCommands - 1, true, true, default, 61)));
        Assert.Equal(way, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, 60 + JumpPilot.TriggerCommands, true, true, default, 61)));

        // Never pressed: looked at until its latest, then the way.
        Assert.Equal(recipe.SecondView, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, recipe.ShotDelay + 80, true, true, default)));
        Assert.Equal(way, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, recipe.ShotDelay + 81, true, true, default)));
    }

    [Fact]
    public void ARecipeWithASecondShotFiresTwoRocketsAndOneWithoutOne()
    {
        Assert.Equal(1, Single.Rockets);
        Assert.Equal(2, Doubled(Vector.Zero).Rockets);
    }
}
