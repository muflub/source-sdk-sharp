using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Player;

/// <summary>
/// The dying player's cameras, as stock's <c>CTFPlayer::StateThinkDYING</c>
/// runs them (tf_player.cpp:13919-14011):
/// <list type="bullet">
/// <item>Once the ragdoll exists, <c>StartObserverMode( OBS_MODE_DEATHCAM )</c>
/// — and "still draw player body". Without the mode the view stays the death
/// eye, inside the corpse. The client pulls the eye back from the ragdoll.</item>
/// <item><c>Event_Killed</c>'s "show killer in death cam mode"
/// (:12429-12490): the observer target is the player who killed us, or
/// nobody (a suicide, the world).</item>
/// <item>At <c>m_flDeathTime + TF_DEATH_ANIMATION_TIME</c>, with a killer
/// who is not us, <c>StartObserverMode( OBS_MODE_FREEZECAM )</c> until the
/// freeze end (<c>spec_freeze_traveltime + spec_freeze_time</c> later): the
/// client flies the camera to frame the killer and freezes the picture. A
/// spectator key once the camera has arrived (<c>AttemptToExitFreezeCam</c>,
/// <see cref="TFObserverRules.ExitFreezeCam"/>) aborts it into chase.</item>
/// <item>Past the freeze end, <c>StateTransition( TF_STATE_OBSERVER )</c>: the
/// player watches, in chase first (<see cref="TFObserverRules.Enter"/>), until
/// the wave's respawn (<see cref="Observers.Stop"/>).</item>
/// </list>
/// A killer that is no player (a sentry, a boss) is not watched: the observer
/// target holds only players here, so such a death keeps the death cam.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDeathCam(IEntityDirectory directory, Observers observers, ILog log, IGameClock clock,
                               SpecFreezeTimeConVar freezeTime, SpecFreezeTravelTimeConVar travelTime)
    : IPlayerDeathHook, IOrderedHook, IFrameLifecycle
{
    /// <summary>TF_DEATH_ANIMATION_TIME (tf_shareddefs.h:1728).</summary>
    public const float DeathAnimationTime = 2.0f;

    // Who is dying, and when they died (m_flDeathTime), by handle.
    private readonly Dictionary<uint, float> _dying = [];

    public int HookOrder => TFPlayerOrders.DeathCam;

    // Its own frame slot: StageUniqueness refuses two participants at the
    // default (TFAutobalance holds it), and the server would not load.
    int IFrameLifecycle.FrameOrder => TFPlayerOrders.DeathCam;

    /// <summary>When the freeze cam is over: the death, its animation, the travel and the hold.</summary>
    public float FreezeEnd(float died) => died + DeathAnimationTime + travelTime.Value + freezeTime.Value;

    public void OnPlayerDied(PlayerDeath death)
    {
        // Stock's `if we have a ragdoll`: a death that left no body has no
        // camera to look at.
        if (death.Body == EntityHandle.None)
            return;

        if (directory.Get(death.Player) is not SsPlayer watcher)
            return;

        // StateEnterDYING's m_bAbortFreezeCam = false (:13906), m_flDeathTime.
        float now = clock.Now;
        watcher.Observer.AbortFreezeCam = false;
        watcher.Observer.DeathTime = now;

        // Event_Killed's target: the killer when a player who is not us killed
        // us, else nobody.
        watcher.State.ObserverTarget = death.Attacker != EntityHandle.None && death.Attacker != death.Player
                                       && directory.Get(death.Attacker) is SsPlayer
            ? death.Attacker.Value
            : EntityHandle.None.Value;

        if (observers.StartDeathCam(watcher))
        {
            _dying[watcher.Handle.Value] = now;
            log.Msg($"[TF2/Player] player {death.EntIndex} died: death cam\n");
        }
    }

    /// <summary>StateThinkDYING, each frame for the dying.</summary>
    void IFrameLifecycle.OnThink(float frameTime) => Think(clock.Now);

    public void Think(float now)
    {
        if (_dying.Count == 0)
            return;

        foreach (var (raw, died) in _dying.ToArray())
        {
            // Respawned, gone, or watching already: the dying is over.
            if (directory.Get(new EntityHandle(raw)) is not SsPlayer player
                || (ObserverMode)player.State.ObserverMode is not (ObserverMode.DeathCam or ObserverMode.FreezeCam))
            {
                _dying.Remove(raw);
                continue;
            }

            if (now < died + DeathAnimationTime)
                continue;

            float freezeEnd = FreezeEnd(died);
            bool killer = observers.TargetOf(player) is { } target && !ReferenceEquals(target, player);
            if (killer && !player.Observer.AbortFreezeCam && now < freezeEnd)
            {
                if ((ObserverMode)player.State.ObserverMode != ObserverMode.FreezeCam)
                    observers.StartFreezeCam(player);
                continue;
            }

            // Out of the freeze cam on the player's word: watching at once,
            // though the respawn still waits for the freeze end.
            bool aborted = (ObserverMode)player.State.ObserverMode == ObserverMode.FreezeCam
                           && player.Observer.AbortFreezeCam;
            if (!aborted && now < freezeEnd)
                continue;

            _dying.Remove(raw);
            if (observers.EnterAfterDeath(player, now))
                log.Msg($"[TF2/Player] {player.Handle} death cam over: watching\n");
        }
    }
}
