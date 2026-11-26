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

    private static ActorFacts Engine(int team) => new(new ActorId(4, 9), new AiVector(1, 2, 3), default, WeaponSeen: 11, ActorActivity.None, 1f, team, NameSlot: 4);

    [Fact]
    public void AnEnemyOfTheSpySeesTheDisguise()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(), dressWeapon: 22, ref f, TFBody.Unread));
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
        TFBotGameView.Draw(Blue, DisguisedSpy(), 22, ref f, TFBody.Unread);
        Assert.NotEqual(Red, f.Team);
        Assert.NotEqual((int)TFClass.Spy, f.Class);
    }

    /// <summary>
    /// The Spy's fact 2 (plan_bots_teams_tf2 C.3): a disguised Spy and a real teammate of the disguise's class are alike in
    /// kind to an enemy. Both show a player's name (a slot above 0), the Spy his borrowed player's, the teammate his own;
    /// team and class are the same. Before, every actor but a disguise read <c>NameSlot</c> 0, so a nonzero one was a Spy.
    /// </summary>
    [Fact]
    public void ADisguisedSpysFactsMatchARealTeammatesInKind()
    {
        ActorFacts spy = Engine(Red);
        TFBotGameView.Draw(Blue, DisguisedSpy(), 22, ref spy, TFBody.Unread);
        var soldier = new TFActorFacts(TFClass.Soldier, TFTeam.Blue, TFClass.Undefined, TFTeam.Unassigned, 0, 0f, 0,
                                       Burning: false, Stunned: false, Ubered: false, Disguised: false, Cloaked: false, Taunting: false);
        ActorFacts mate = new(new ActorId(6, 3), new AiVector(9, 9, 9), default, WeaponSeen: 22, ActorActivity.None, 1f, Blue, NameSlot: 6);
        TFBotGameView.Draw(Blue, soldier, -1, ref mate, TFBody.Unread);
        Assert.Equal((mate.Team, mate.Class, mate.Conditions, mate.WeaponSeen), (spy.Team, spy.Class, spy.Conditions, spy.WeaponSeen));
        Assert.True(spy.NameSlot > 0 && mate.NameSlot > 0);
        Assert.Equal(mate.Id.Slot, mate.NameSlot);
        Assert.Equal(6, spy.NameSlot);
    }

    /// <summary>Stock's target ID keeps a disguised Spy's own name when his disguise borrowed nobody's (<c>tf_hud_target_id.cpp:764-793</c>).</summary>
    [Fact]
    public void ADisguiseThatBorrowedNoNameShowsTheSpysOwn()
    {
        ActorFacts f = Engine(Red);
        TFBotGameView.Draw(Blue, DisguisedSpy() with { DisguiseTargetIndex = 0 }, 22, ref f, TFBody.Unread);
        Assert.Equal((Blue, 4), (f.Team, f.NameSlot));
    }

    /// <summary>
    /// Cloaked and still drawn (a fade, a blink), he keeps the borrowed name (review F7): stock shows a stealthed enemy no
    /// name, and his own slot here would mark him. The control: a disguise that borrowed nobody shows his own (above).
    /// </summary>
    [Fact]
    public void ACloakedDisguisedSpyKeepsTheBorrowedName()
    {
        ActorFacts f = Engine(Red);
        TFBotGameView.Draw(Blue, DisguisedSpy(0.5f), 22, ref f, TFBody.Unread);
        Assert.Equal(6, f.NameSlot);
    }

    [Fact]
    public void TheSpysOwnTeamSeesASpyInAMask()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Red, DisguisedSpy(), 22, ref f, TFBody.Unread));
        Assert.Equal((Red, (int)TFClass.Spy, 4, 11), (f.Team, f.Class, f.NameSlot, f.WeaponSeen));
        Assert.Equal(ActorConditions.Disguised, f.Conditions & ActorConditions.Disguised);
    }

    [Fact]
    public void ACloakFadesTheSpyAndPastThreeQuartersHidesHim()
    {
        ActorFacts half = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(0.5f), -1, ref half, TFBody.Unread));
        Assert.Equal(0.5f, half.VisibleFraction, 3);
        Assert.Equal(ActorConditions.Cloaked, half.Conditions & ActorConditions.Cloaked);
        ActorFacts gone = Engine(Red);
        Assert.False(TFBotGameView.Draw(Blue, DisguisedSpy(TFBotGameView.CloakHidden), -1, ref gone, TFBody.Unread));
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
        Assert.Equal((false, true), (TFBotGameView.Draw(Blue, cloaked, -1, ref hidden, TFBody.Unread), TFBotGameView.Draw(Blue, shown, -1, ref seen, TFBody.Unread)));
    }

    /// <summary>A fully cloaked Spy on fire shows as much as a cloak just under hiding him (review-p7f F5): not a sighting of nothing.</summary>
    [Fact]
    public void ACueShowsAtLeastAQuarterOfAFullyCloakedSpy()
    {
        ActorFacts f = Engine(Red);
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(1f) with { Burning = true }, -1, ref f, TFBody.Unread));
        Assert.Equal(TFBotGameView.CueShows, f.VisibleFraction, 3);
    }

    /// <summary>A cloaked Spy a cue shows is a target: a Soldier aims at and fights the burning, undisguised Spy its view draws at the cue's quarter.</summary>
    [Fact]
    public void ACloakedSpyACueShowsIsTargeted()
    {
        ActorFacts spy = Engine(Red) with { Origin = new AiVector(2448f, 256f, 0f) };
        Assert.True(TFBotGameView.Draw(Blue, DisguisedSpy(1f) with { Disguised = false, Burning = true }, -1, ref spy, TFBody.Unread));
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
        Assert.True(TFBotGameView.Draw(Red, heavy, -1, ref f, TFBody.Unread));
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
