using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// What another client is shown of a player's weapon (plan_hl2mp_ai §4.5):
/// the shot and reload gestures stock sends every other client as player
/// animation events (<c>CHL2MP_Player::DoAnimationEvent</c>'s
/// <c>TE_PlayerAnimEvent</c>: <c>PLAYERANIMEVENT_ATTACK_PRIMARY</c>,
/// <c>PLAYERANIMEVENT_RELOAD</c>), and the clip a shotgun's shell-by-shell
/// reload fills.
/// </summary>
public enum PlayerWeaponEvent : byte
{
    /// <summary>The weapon fired: <c>count</c> rounds left the clip (or the reserve, for a weapon with none).</summary>
    Fired,

    /// <summary>A reload began.</summary>
    ReloadStarted,

    /// <summary>A shell-by-shell reload put <c>count</c> rounds in (the loop gesture).</summary>
    ReloadShell,

    /// <summary>The reload ended.</summary>
    ReloadFinished,
}

/// <summary>
/// Told, on the game thread, of each new command's weapon events for every
/// player (plan_hl2mp_ai §4.5), by the server's arsenal after the command
/// ran: what a client watching that player would be shown. A predicted
/// re-run of a command is not told twice.
/// </summary>
public interface IPlayerWeaponEventHook
{
    void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count);
}
