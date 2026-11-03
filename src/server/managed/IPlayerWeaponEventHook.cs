using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// What another client is shown of a player's weapon (plan_hl2mp_ai §4.5):
/// the shot and the reload, whose gestures every client that sees the player
/// plays (HL2MP's <c>SetAnimation( PLAYER_ATTACK1 / PLAYER_RELOAD )</c>; here
/// the arsenal's <c>TE_PlayerAnimEvent</c>), and the clip a shotgun's
/// shell-by-shell reload fills.
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
