using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the server remembers about a player, and the arithmetic of the death
/// cycle.
///
/// The transitions themselves need a live player to move between, so what is
/// checked here is the bookkeeping around them — which is where the mistakes
/// that matter are: a frag credited to the wrong person, or a respawn deadline
/// measured from the wrong moment.
/// </summary>
public class PlayerTests
{
    private static readonly EntityHandle A = new(0x0001_0001u);
    private static readonly EntityHandle B = new(0x0002_0001u);

    // Scoring moved to PlayerFragTests, which states stock's rule: a suicide
    // or a world death costs the victim a frag (this fact pinned zero).

    [Fact]
    public void Waiting()
    {
        var players = new Players(new RecordingLog(), Fake().Ignores("Entities.GetEyeAngles").Returns("Entities.IsPlayer", true), Fake().Ignores("Players.IsAlive", "Players.GetObserverMode", "Players.DropActiveWeapon", "Players.GetActiveWeaponEntity"), Fake(), TestServices.NoButtons);
        players.RespawnDelay = 5f;

        // Alive: nothing to wait for.
        Assert.True(players.WaitRemaining(A, 10f) == 0f, "a living player waits for nothing");

        players.OnKilled(A, B, now: 10f);

        Assert.True(players.WaitRemaining(A, 10f) == 5f, "the wait starts at the full delay");
        Assert.True(players.WaitRemaining(A, 12f) == 3f, "and counts down");
        Assert.True(players.WaitRemaining(A, 15f) == 0f, "reaching zero exactly on time");

        // Never negative, or a HUD counting down would run past zero into
        // nonsense.
        Assert.True(players.WaitRemaining(A, 60f) == 0f, "and never going below zero");

        // Spawning clears it, which is what makes a respawned player not still
        // owe a wait from the death before.
        players.OnSpawned(A, now: 16f);

        Assert.True(players.WaitRemaining(A, 16f) == 0f, "spawning ends the wait");
        Assert.True(players[A].DiedAt < 0f, "and clears the death");
        Assert.True(players[A].SpawnedAt == 16f, "and stamps the spawn, for spawn protection");
    }

    [Fact]
    public void Lifetime()
    {
        var players = new Players(new RecordingLog(), Fake().Ignores("Entities.GetEyeAngles").Returns("Entities.IsPlayer", true), Fake().Ignores("Players.IsAlive", "Players.GetObserverMode", "Players.DropActiveWeapon", "Players.GetActiveWeaponEntity"), Fake(), TestServices.NoButtons);

        Assert.True(!players.Knows(A), "nothing is known about a player who has done nothing");

        // Asking creates a record, which is what makes every caller able to read
        // one without checking first.
        _ = players[A];
        Assert.True(players.Knows(A), "asking about one creates it");

        players.OnKilled(A, B, 1f);
        Assert.True(players.All.Count() == 2, "two players are tracked separately");

        players.Forget(A);
        Assert.True(players.Knows(B) && !players.Knows(A), "forgetting one leaves the other");

        // Handles do not survive a map change, so keeping records across one
        // would leave entries whose handles may since have been reused.
        players.Reset();
        Assert.True(!players.All.Any(), "a level change forgets everyone");
    }
}
