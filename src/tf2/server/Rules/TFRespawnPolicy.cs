using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Rules;

/// <summary>
/// The TF2 modes' <see cref="IRespawnPolicy"/>: the core never lets a TF2
/// player out on a button. Stock TF2 respawns the dead on their team's wave
/// (<c>CheckRespawnWaves</c> → <c>RespawnTeam</c> → <c>ForceRespawn</c>),
/// which <see cref="TeamRoundFlow"/> does itself; so the core's release always
/// answers no, and its wait answers the time to the player's wave (what the
/// core's <c>WaitRemaining</c> and its death-cam line read).
/// </summary>
public sealed class TFRespawnPolicy(ITeamRespawnWaves waves, ITeamRoundRoster roster) : IRespawnPolicy
{
    public float Delay(EntityHandle player)
    {
        float died = roster.DeathTime(player);
        if (died < 0f)
            return 0f;

        return Math.Max(0f, waves.NextRespawnWave(roster.TeamOf(player), player) - died);
    }

    public bool WaveRelease(EntityHandle player, float now) => false;
}
