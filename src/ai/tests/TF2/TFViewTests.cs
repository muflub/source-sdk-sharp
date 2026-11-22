using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What a TF2 client draws of a player (plan_bots_teams_tf2 C.3, P6 step 3), honest both ways:
/// a disguised Spy is the disguise to anyone not on his team (its team, class, the borrowed name,
/// its weapon, no sign of a disguise) and a Spy in a mask to his own; a cloak fades him and past
/// 75 % hides him; the conditions any client sees are drawn; a building's health shows only to
/// its team and to a Spy.
/// </summary>
public sealed class TFViewTests
{
    private const int Red = (int)TFTeam.Red, Blue = (int)TFTeam.Blue;

    /// <summary>A RED Spy disguised as a BLU Soldier borrowing player 6's name.</summary>
    private static TFActorFacts DisguisedSpy(float invisible = 0f) => new(TFClass.Spy, TFTeam.Red, TFClass.Soldier, TFTeam.Blue, 6, invisible, 0,
                                                                          Burning: false, Stunned: false, Ubered: false, Disguised: true, Cloaked: invisible > 0f, Taunting: false);

    private static ActorFacts Engine(int team) => new(new ActorId(4, 9), new AiVector(1, 2, 3), default, WeaponSeen: 11, ActorActivity.None, 1f, team);

    [Fact]
    public void AnEnemyOfTheSpySeesTheDisguise()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(), dressWeapon: 22, ref f));
        Assert.Equal((Blue, (int)TFClass.Soldier, 6, 22), (f.Team, f.Class, f.NameSlot, f.WeaponSeen));
        Assert.Equal(ActorConditions.None, f.Conditions & ActorConditions.Disguised);
    }

    /// <summary>
    /// The negative arm (C.3): a disguised enemy, cue-less, carries nothing of his real team or class
    /// into an enemy's view, so a bot that keys its targets on team never targets him.
    /// </summary>
    [Fact]
    public void AnEnemysViewOfADisguisedSpyCarriesNoneOfHisTruth()
    {
        ActorFacts f = Engine(Red);
        TFBotGameView.Draw(Blue, DisguisedSpy(), 22, ref f);
        Assert.NotEqual(Red, f.Team);
        Assert.NotEqual((int)TFClass.Spy, f.Class);
    }

    [Fact]
    public void TheSpysOwnTeamSeesASpyInAMask()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Red, DisguisedSpy(), 22, ref f));
        Assert.Equal((Red, (int)TFClass.Spy, 0, 11), (f.Team, f.Class, f.NameSlot, f.WeaponSeen));
        Assert.Equal(ActorConditions.Disguised, f.Conditions & ActorConditions.Disguised);
    }

    [Fact]
    public void ACloakFadesTheSpyAndPastThreeQuartersHidesHim()
    {
        ActorFacts half = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(0.5f), -1, ref half));
        Assert.Equal(0.5f, half.VisibleFraction, 3);
        Assert.Equal(ActorConditions.Cloaked, half.Conditions & ActorConditions.Cloaked);
        ActorFacts gone = Engine(Red);
        Assert.False(TFBotGameView.Draw(Blue, DisguisedSpy(TFBotGameView.CloakHidden), -1, ref gone));
    }

    /// <summary>
    /// A Spy cloaked past three quarters is drawn when something on him shows (stock's tf_bot_vision.cpp:264-271, the Pyro
    /// research): burning, Jarate, a blink where he was struck, blood. The control: the same cloak with none hides him.
    /// </summary>
    [Theory]
    [InlineData("burning")]
    [InlineData("jarated")]
    [InlineData("blinking")]
    [InlineData("bleeding")]
    public void ACloakedSpyWhoseBodyShowsIsDrawn(string cue)
    {
        TFActorFacts cloaked = DisguisedSpy(0.9f);
        TFActorFacts shown = cue switch
        {
            "burning" => cloaked with { Burning = true },
            "jarated" => cloaked with { Jarated = true },
            "blinking" => cloaked with { Blinking = true },
            _ => cloaked with { Bleeding = true },
        };
        ActorFacts hidden = Engine(Red), seen = Engine(Red);
        Assert.Equal((false, true), (TFBotGameView.Draw(Blue, cloaked, -1, ref hidden), TFBotGameView.Draw(Blue, shown, -1, ref seen)));
    }

    /// <summary>A fully cloaked Spy on fire shows as much as a cloak just under hiding him (review-p7f F5): not a sighting of nothing.</summary>
    [Fact]
    public void ACueShowsAtLeastAQuarterOfAFullyCloakedSpy()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(1f) with { Burning = true }, -1, ref f));
        Assert.Equal(TFBotGameView.CueShows, f.VisibleFraction, 3);
    }

    /// <summary>A cloaked Spy a cue shows is a target: a Soldier aims at and fights the burning, undisguised Spy its view draws at the cue's quarter.</summary>
    [Fact]
    public void ACloakedSpyACueShowsIsTargeted()
    {
        ActorFacts spy = Engine(Red) with { Origin = new AiVector(2448f, 256f, 0f) };
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(1f) with { Disguised = false, Burning = true }, -1, ref spy));
        var f = new TFField();
        IntentCommand i = f.StepDrawn(TFField.Self(new AiVector(2048f, 256f, 0f), team: TFField.Blue), TFField.Ctf(), spy);
        // Its first decide on him calls "Spy!" (the voice line's one-shot); the aim is on him, and he is the fight's target.
        Assert.Equal((LookKind.Actor, spy.Id, spy.Id), (i.Look.Kind, i.Look.Actor, f.Decider.Doing.Target));
    }

    [Fact]
    public void AnUndisguisedPlayerIsDrawnAsHisClassWithTheConditionsAnyoneSees()
    {
        var heavy = new TFActorFacts(TFClass.HeavyWeapons, TFTeam.Blue, TFClass.Civilian, TFTeam.Unassigned, 0, 0f, 0,
                                     Burning: true, Stunned: false, Ubered: true, Disguised: false, Cloaked: false, Taunting: true);
        ActorFacts f = Engine(Blue);
        Assert.True(TFBotGameView.Draw(Red, heavy, -1, ref f));
        Assert.Equal((Blue, (int)TFClass.HeavyWeapons, 11), (f.Team, f.Class, f.WeaponSeen));
        Assert.Equal(ActorConditions.Burning | ActorConditions.Invulnerable | ActorConditions.Taunting, f.Conditions);
    }

    [Fact]
    public void ABuildingsHealthShowsToItsTeamAndToASpyOnly()
    {
        TFActorFacts Viewer(TFClass c, TFTeam t) => new(c, t, TFClass.Civilian, TFTeam.Unassigned, 0, 0f, 0, false, false, false, false, false, false);
        Assert.True(TFBotGameView.ShowsBuildingHealth(Viewer(TFClass.Engineer, TFTeam.Red), Red));
        Assert.False(TFBotGameView.ShowsBuildingHealth(Viewer(TFClass.Soldier, TFTeam.Blue), Red));
        Assert.True(TFBotGameView.ShowsBuildingHealth(Viewer(TFClass.Spy, TFTeam.Blue), Red));
    }

    [Fact]
    public void ATeleportersEndIsItsMode()
    {
        Assert.Equal(TFThingType.TeleporterEntrance, TFBotGameView.BuildingType(TFObjectType.Teleporter, 0));
        Assert.Equal(TFThingType.TeleporterExit, TFBotGameView.BuildingType(TFObjectType.Teleporter, 1));
        Assert.Equal(TFThingType.Sentry, TFBotGameView.BuildingType(TFObjectType.Sentrygun, 0));
    }
}
