using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The two respawn-override arms of the round machine: the wave hold beside
/// the minimum-respawn-time check (stock's <c>IsReadyToSpawn</c> arm,
/// tf_player.cpp:3266, through the PREROUND-gated gate at
/// teamplayroundbased_gamerules.cpp:2660) and the forced release after the
/// team loop of the wave (<c>CTFGameRules::CheckRespawnWaves</c>,
/// tf_gamerules.cpp:9485-9503).
/// </summary>
public sealed class RespawnOverrideRoundTests
{
    /// <summary>A round machine whose flow sees a carrier set, one player per team.</summary>
    private static (RoundRig Rig, TFRespawnOverride Override, EntityHandle Red, float Died) Killing()
    {
        var carriers = new TFPlayerCarriers();
        var rig = new RoundRig(carriers: carriers);
        var (red, _) = rig.Running();
        rig.Roster.Respawns.Clear(); // the round-start mass respawn is not this fact's release
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(red);
        rig.Roster.Kill(red);
        return (rig, ov, red, rig.Clock.Now);
    }

    [Fact]
    public void An_expired_override_forces_the_dead_player_out_before_their_wave()
    {
        var (rig, ov, red, died) = Killing();
        ov.Apply(2f, null); // the pair releases at died+2; the wave would let them out near died+16

        rig.RunUntil(() => rig.Clock.Now >= died + 1.9f);
        Assert.DoesNotContain(rig.Roster.Respawns, r => r.Player == red); // held: neither the wave nor the release

        rig.Clock.Now = died + 2.1f;
        rig.Flow.Think(rig.Clock.Now);

        Assert.Contains(rig.Roster.Respawns, r => r.Player == red && r.At == died + 2.1f); // :9500 ForceRespawn
    }

    [Fact]
    public void An_unexpired_override_holds_the_dead_player_past_their_wave()
    {
        var (rig, ov, red, died) = Killing();
        ov.Apply(30f, null); // past the died+16.4 maturity of the wave without the pair

        // Past died+21.4 (the maturity plus one 5 s scaled wave) the wave alone
        // would certainly have had them out, so the arm is what holds them here.
        rig.RunUntil(() => rig.Clock.Now >= died + 21.6f, limit: 60f);
        Assert.DoesNotContain(rig.Roster.Respawns, r => r.Player == red); // :3266

        rig.RunUntil(() => rig.Clock.Now >= died + 30.2f, limit: 60f);
        Assert.Contains(rig.Roster.Respawns, r => r.Player == red); // and the release arm takes them at died+30
    }

    [Fact]
    public void A_dead_player_without_the_pair_still_rides_the_wave()
    {
        var carriers = new TFPlayerCarriers();
        var rig = new RoundRig(carriers: carriers);
        var (red, _) = rig.Running();
        rig.Roster.Respawns.Clear(); // the round-start mass respawn is not this fact's release
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(red); // attached, never applied: OverrideTime stands at the cleared -1
        rig.Roster.Kill(red);
        float died = rig.Clock.Now;

        Assert.True(rig.RunUntil(() => rig.Roster.Respawns.Any(r => r.Player == red), limit: 60f));
        // The wave alone: the first wave moment past the died+16.4 minimum, no later than one 5 s wave on.
        Assert.InRange(rig.Roster.Respawns.First(r => r.Player == red).At, died + 16.4f, died + 21.5f);
    }
}
