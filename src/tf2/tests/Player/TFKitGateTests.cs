using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;

using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The class game's veto on the spawn kit (<see cref="TFKitGate"/> over the
/// core's <see cref="IKitGate"/>): the player with no class — the unassigned
/// who has not joined a team or a class — is given no weapon and no ammo, as
/// stock's <c>TF_STATE_WELCOME</c> never reaches <c>GiveDefaultItems</c>
/// (tf_player.cpp:3618, :4157). The classed player's kit stands.
/// </summary>
public sealed class TFKitGateTests
{
    private static readonly EntityHandle Player = PlayerRig.Player;

    /// <summary>The rules as the module makes them, with TF2's one gate on the chain.</summary>
    private static GameRules Rules(PlayerRig rig)
    {
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        return new GameRules(rig.Log, convars.Holder<GameRulesConVars>(), rig.Engine, rig.Engine, rig.Engine,
                             new Scoreboard(rig.Engine, new PlayerDisconnectEvent(), rig.Log),
                             new PlayerDeathEvent(),
                             kitGates: new ListHooks<IKitGate>(new TFKitGate(rig.Carriers)));
    }

    [Fact]
    public void An_unassigned_player_is_refused_the_kit()
    {
        var rig = new PlayerRig();
        var gate = new TFKitGate(rig.Carriers);

        Assert.False(gate.ShouldGiveKit(Player));
    }

    [Fact]
    public void A_soldier_is_granted_the_kit()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var gate = new TFKitGate(rig.Carriers);

        Assert.True(gate.ShouldGiveKit(Player));
    }

    [Fact]
    public void The_unassigned_spawn_is_given_no_weapons_and_no_ammo()
    {
        var rig = new PlayerRig();

        Rules(rig).OnPlayerSpawned(Player, 0f);

        Assert.Equal(0, rig.Engine.CountOf("Players.GiveItem"));
        Assert.Equal(0, rig.Engine.CountOf("Players.GiveAmmo"));
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("no kit, a gate refused it"));
    }

    [Fact]
    public void The_classed_spawn_keeps_its_kit()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);

        Rules(rig).OnPlayerSpawned(Player, 0f);

        Assert.Equal(GameRules.Loadout.Length, rig.Engine.CountOf("Players.GiveItem"));
    }
}
