using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The frags the scoreboard shows (PlayerRecord.Kills, read by the player
/// resource), by stock's CMultiplayRules::PlayerKilled with HL2MP's rules
/// (no teamplay, UseSuicidePenalty true): every death counts against the
/// victim; a kill by ANOTHER PLAYER is +1 to that player; a kill by yourself,
/// the world or anything that is not a player is -1 to the victim.
/// </summary>
public class PlayerFragTests
{
    private static readonly EntityHandle A = new(0x0001_0001u);
    private static readonly EntityHandle B = new(0x0002_0001u);
    private static readonly EntityHandle Prop = new(0x0040_0005u);
    private static readonly EntityHandle World = new(0u);

    private static Players Roster()
    {
        // IsPlayer answers for A and B only: the factory reads the call it is
        // answering (Record logs the call before it asks for the answer).
        var entities = Fake().Ignores("Entities.GetEyeAngles");
        entities.ReturnsEachCall("Entities.IsPlayer",
            () => entities.Calls[^1].Args[0] is EntityHandle h && (h == A || h == B));
        return new Players(new RecordingLog(), entities,
                           Fake().Ignores("Players.IsAlive", "Players.GetObserverMode", "Players.DropActiveWeapon",
                                          "Players.GetActiveWeaponEntity", "Players.RemoveAllItems"),
                           Fake(), NoButtons);
    }

    [Fact]
    public void A_player_killing_another_scores_one_and_the_victim_a_death()
    {
        var players = Roster();
        players.OnKilled(A, B, now: 1f);
        Assert.Equal((0, 1, 1, 0), (players[A].Kills, players[A].Deaths, players[B].Kills, players[B].Deaths));
    }

    [Fact]
    public void Killing_yourself_costs_a_frag()
    {
        var players = Roster();
        players.OnKilled(A, A, now: 1f);
        Assert.Equal((-1, 1), (players[A].Kills, players[A].Deaths));
    }

    [Fact]
    public void The_world_or_no_attacker_costs_the_victim_a_frag_and_credits_nobody()
    {
        var players = Roster();
        players.OnKilled(A, EntityHandle.None, now: 1f);
        players.OnKilled(A, World, now: 2f);
        Assert.Equal((-2, 2), (players[A].Kills, players[A].Deaths));
        Assert.False(players.Knows(World), "the world is given no record");
    }

    [Fact]
    public void A_prop_is_not_a_scorer()
    {
        var players = Roster();
        players.OnKilled(A, Prop, now: 1f);
        Assert.Equal(-1, players[A].Kills);
        Assert.False(players.Knows(Prop), "a prop is given no record, and so no frag");
    }
}
