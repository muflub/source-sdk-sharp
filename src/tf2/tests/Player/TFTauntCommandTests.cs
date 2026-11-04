using SourceSharp.Abi;

using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFTauntCommand"/>: stock's base-taunt command arms with
/// <c>IsAllowedToTaunt</c>'s gates and the timed <c>TF_COND_TAUNTING</c> the
/// success stamp leaves behind (tf_player.cpp:7507-7533, :17732-17768;
/// tf_player_shared.cpp:676-766).
/// </summary>
public sealed class TFTauntCommandTests
{
    private static (PlayerRig Rig, TFTauntCommand Command, TFPlayerConditions Conditions, TFPlayerMapPlayers Players)
        Rig(TFClass cls = TFClass.Soldier)
    {
        var rig = new PlayerRig().As(cls);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        return (rig, new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers), conditions, players);
    }

    /// <summary>The voice over the rig's engine: the staged rules answer, nothing else named.</summary>
    private static TF2.Server.Voice.TFConceptSpeech Voice(PlayerRig rig, TF2.Tests.Entities.FakeConceptRules rules)
        => new(rules, rig.Carriers, new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
               TF2.Tests.Entities.ObjectiveRig.ConceptSceneImage(), rig.Engine, new TF2.Tests.Entities.FakeRandom());

    [Fact]
    public void TheTauntIsItsScenesSpeak()
    {
        // Taunt (tf_player.cpp:17858-17862): MP_CONCEPT_PLAYER_TAUNT with the
        // held weapon's word — the per-class rules' WeaponIs* criterion — and
        // the taunt stands because the search answered.
        var rig = new PlayerRig().As(TFClass.Soldier);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_shotgun_soldier");
        var rules = new TF2.Tests.Entities.FakeConceptRules();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        var command = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, Voice(rig, rules));

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.Equal(TF2.Server.Voice.TFConceptSpeech.PlayerTaunt, rules.Asked.Single().Concept);
        Assert.Contains(("playerweapon", "tf_shotgun_soldier"), rules.Words);
        Assert.True(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void TheTauntAskHandsBackTheMatchedResponseName()
    {
        // SpeakConceptIfAllowed's buffer (tf_player.cpp:17862): the matched
        // response's name comes back with the yes — the scene the timed
        // attack is keyed on (:17979).
        var rig = new PlayerRig().As(TFClass.Soldier);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_shotgun_soldier");
        var rules = new TF2.Tests.Entities.FakeConceptRules { Name = "scenes/player/soldier/low/taunt05.vcd" };
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        var voice = new TF2.Server.Voice.TFConceptSpeech(rules, rig.Carriers, new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
            TF2.Tests.Entities.ObjectiveRig.ConceptSceneImage(), rig.Engine, new TF2.Tests.Entities.FakeRandom());

        Assert.True(voice.Speak(PlayerRig.Player, TF2.Server.Voice.TFConceptSpeech.PlayerTaunt, "tf_shotgun_soldier", out string scene));

        Assert.Equal("scenes/player/soldier/low/taunt05.vcd", scene);
    }

    [Fact]
    public void ARulelessTauntIsConsumedWithNothingBehindIt()
    {
        // bTauntSucceeded false (tf_player.cpp:17867-17870): m_bInitTaunt back
        // down — no condition, and the search asked again next press, the
        // window being OnTauntSucceeded's alone.
        var rig = new PlayerRig().As(TFClass.Soldier);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        var rules = new TF2.Tests.Entities.FakeConceptRules { Match = false };
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        var command = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, Voice(rig, rules));

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));
        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.False(rig.State.InCond(TFCond.Taunting));
        Assert.Equal(2, rules.Asked.Count);
    }

    [Fact]
    public void TheTauntAddsTheConditionCountingDownTheScenesWindow()
    {
        var (rig, command, conditions, _) = Rig();

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.True(rig.State.InCond(TFCond.Taunting));
        Assert.NotNull(conditions.Of(PlayerRig.Index));
        Assert.Equal(TFTauntCommand.BaseTauntSceneSeconds + TFTauntCommand.TauntTailSeconds,
                     conditions.Of(PlayerRig.Index)!.Duration(TFCond.Taunting));
    }

    [Fact]
    public void TheWindowIsTheMatchedScenesOwnLength()
    {
        // GetSceneDuration plus the tail (tf_player.cpp:17734): the spy's fencing
        // stab lands 4 s in, so a fixed length ended the taunt before it.
        const string Scene = "scenes/player/spy/low/taunt03_v2.vcd";
        var rig = new PlayerRig().As(TFClass.Soldier);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        var rules = new TF2.Tests.Entities.FakeConceptRules { Kind = ResponseKind.Scene, Name = Scene };
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        var image = new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files(
            ("scenes/scenes.image", TF2.Tests.Voice.TFSceneImageTests.Image((Scene, 4200, ["vo/taunt.ogg"])))));
        var voice = new TF2.Server.Voice.TFConceptSpeech(rules, rig.Carriers, new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
                                                         image, rig.Engine, new TF2.Tests.Entities.FakeRandom());
        var command = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, voice, scenes: image);

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.Equal(4.2f + TFTauntCommand.TauntTailSeconds, conditions.Of(PlayerRig.Index)!.Duration(TFCond.Taunting), 3);
    }

    [Fact]
    public void AHeldKeyInsideTheWindowConsumesWithoutRenewingIt()
    {
        var (_, command, conditions, _) = Rig();
        command.Handle(PlayerRig.Player, TFTauntCommand.Command, []);

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        // The window stays the one window: no re-stamp while it runs.
        Assert.Equal(TFTauntCommand.BaseTauntSceneSeconds + TFTauntCommand.TauntTailSeconds,
                     conditions.Of(PlayerRig.Index)!.Duration(TFCond.Taunting));
    }

    [Fact]
    public void TheConditionExpiresWhenTheWindowRunsOut()
    {
        var (rig, command, conditions, _) = Rig();
        command.Handle(PlayerRig.Player, TFTauntCommand.Command, []);

        // The server ticker's countdown (TFConditions.Tick, server only):
        // the whole window, and the condition is gone — stock's remove-time
        // expiry (tf_player_shared.cpp:2761-2766).
        conditions.Of(PlayerRig.Index)!.Tick(TFTauntCommand.BaseTauntSceneSeconds + TFTauntCommand.TauntTailSeconds,
                                             healers: 0, expiresFast: default);

        Assert.False(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void ADeadPlayerKeepsTheTaunt()
    {
        var (rig, command, _, _) = Rig();
        rig.Engine.Returns("Players.IsAlive", false);

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.False(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void APlayerOffTheGroundKeepsTheTaunt()
    {
        var (rig, command, _, _) = Rig();
        rig.Engine.Returns("Networking.EntityGetState", EntityState.None);

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.False(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void ASpyInCloakHoldsTheTaunt()
    {
        var (rig, command, _, players) = Rig(TFClass.Spy);
        players.AddCond(PlayerRig.Player, TFCond.Stealthed, 5f);

        Assert.True(command.Handle(PlayerRig.Player, TFTauntCommand.Command, []));

        Assert.False(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void TheStopAndReleaseArmsAreConsumedWithNothingMore()
    {
        var (rig, command, _, _) = Rig();

        Assert.True(command.Handle(PlayerRig.Player, "stop_taunt", []));
        Assert.True(command.Handle(PlayerRig.Player, "-taunt", []));

        Assert.False(rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void OtherCommandsPassThrough()
    {
        var (_, command, _, _) = Rig();

        Assert.False(command.Handle(PlayerRig.Player, "joinclass", ["soldier"]));
    }
}
