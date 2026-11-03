using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.Render;

/// <summary>
/// The local player's field of view, stock's client <c>C_BasePlayer::GetFOV</c>
/// (game/client/c_baseplayer.cpp:2437) base arm: the networked m_iFOV words —
/// 0 meaning the default, the ramp from m_iFOVStart over
/// m_Local.m_flFOVRate from m_flFOVTime (<see cref="PlayerFov.Current"/>) —
/// asked by the view setup each frame, stock's <c>view.cpp:676</c> giving the
/// player's answer to CalcView's fov. The demo override, the in-eye observer
/// target's and the vehicle's FOV are stock's other arms: the observer's view
/// rides <see cref="ObserverCamera"/>'s OverrideView and there are no vehicles.
/// </summary>
public sealed class LocalPlayerFov(IClientStandalones standalones, IEngineHud hud, IGameClock clock)
{
    /// <summary>The local player's FOV now; 0 when there is no local player.</summary>
    public int Current()
    {
        int local = hud.LocalPlayerIndex;
        return local <= 0 ? 0 : Of(standalones.GetByIndex(local) as ClientSsPlayer, clock.Now);
    }

    /// <summary><see cref="PlayerFov.Current"/> over a player's words, or 0 for nobody.</summary>
    public static int Of(ClientSsPlayer? local, float now)
    {
        if (local is null)
            return 0;

        PlayerNetState s = local.State;
        return PlayerFov.Current(s.Fov, s.FovStart, s.FovTime, s.FovRate, s.FovDefault, now);
    }
}
