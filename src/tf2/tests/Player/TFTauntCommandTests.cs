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
        return (rig, new TFTauntCommand(players, rig.Engine, rig.Engine), conditions, players);
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
