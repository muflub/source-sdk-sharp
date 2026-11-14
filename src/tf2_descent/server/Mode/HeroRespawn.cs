using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;

namespace TF2.Descent.Server.Mode;

/// <summary>
/// The Descent mode's respawn (HE1b): a dead hero comes back at the level's
/// (or the town's) spawn once the core's wait
/// (<see cref="IPlayerDirectory.RespawnDelay"/>) is over and every
/// <see cref="IRespawnGate"/> lets them (hosted, <c>DeathService</c> holds a
/// hero whose death the host has not taken yet).
///
/// <para>
/// WHY THE MODE NEEDS ONE. The core's death cycle releases a dead player on a
/// button, but never one who is observing (stock's <c>PlayerDeathThink</c>),
/// and TF2's death cam (<c>TFDeathCam</c>) makes every dead TF player an
/// observer, so under the core's cycle alone a hero stays dead. TF2's modes
/// bring theirs back on the team's wave (<c>TeamRoundFlow</c>'s
/// <c>ForceRespawn</c>); the Descent mode has no rounds and no waves, so this
/// takes the same arm (<see cref="ITeamRoundRoster.ForceRespawn"/>) after the
/// core's wait, with no button.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroRespawn(IDescentModeState mode, ITeamRoundRoster roster, IPlayerDirectory records,
                                IHooks<IRespawnGate> gates, IGameClock clock, ILog log)
    : IFrameLifecycle
{
    /// <summary>At TF2 Descent's first mode stage, after the core's own death cycle.</summary>
    public const int Order = ModStages.First + 100;

    int IFrameLifecycle.FrameOrder => Order;

    void IFrameLifecycle.OnThink(float frameTime) => Think(clock.Now);

    /// <summary>Every dead hero whose wait is over and whom no gate holds comes back now.</summary>
    public void Think(float now)
    {
        if (!mode.Active)
            return;

        foreach (var player in roster.Players(DescentMode.HeroTeam))
        {
            if (roster.IsAlive(player))
                continue;

            float died = roster.DeathTime(player);
            if (died < 0f || now - died < records.RespawnDelay)
                continue;

            if (!MayRespawn(player))
                continue;

            log.Msg($"[TF2Descent/Mode] player {roster.EntIndex(player)} respawns after {now - died:F1}s dead\n");
            roster.ForceRespawn(player);
        }
    }

    /// <summary>Every gate's yes; a gate that throws is logged and holds nobody (the core's rule).</summary>
    private bool MayRespawn(EntityHandle player)
    {
        int entIndex = roster.EntIndex(player);
        foreach (var gate in gates.All)
        {
            try
            {
                if (!gate.MayRespawn(player, entIndex))
                    return false;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2Descent/Mode] respawn gate {gate.GetType().FullName} threw: {e.Message}\n");
            }
        }

        return true;
    }
}
