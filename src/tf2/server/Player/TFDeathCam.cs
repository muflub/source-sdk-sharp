using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Player;

/// <summary>
/// The dead player's camera, as stock's <c>State_Think_DYING</c> starts it
/// (tf_player.cpp:13923-13935): once the ragdoll exists (<c>m_hRagdoll</c>),
/// <c>StartObserverMode( OBS_MODE_DEATHCAM )</c> — and "still draw player
/// body". Without the mode the view stays the death eye, inside the corpse,
/// and the corpse's hands and weapon fill the screen. The client pulls the
/// eye back from the ragdoll over DEATH_ANIMATION_TIME (the observer view's
/// death cam); the mode lasts until the respawn's
/// <see cref="Observers.Stop"/> (the wave's <c>ForceRespawn</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFDeathCam(IEntityDirectory directory, Observers observers, ILog log)
    : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.DeathCam;

    public void OnPlayerDied(PlayerDeath death)
    {
        // Stock's `if we have a ragdoll`: a death that left no body has no
        // camera to look at.
        if (death.Body == EntityHandle.None)
            return;

        if (directory.Get(death.Player) is not SsPlayer watcher)
            return;

        if (observers.StartDeathCam(watcher))
            log.Msg($"[TF2/Player] player {death.EntIndex} died: death cam\n");
    }
}
