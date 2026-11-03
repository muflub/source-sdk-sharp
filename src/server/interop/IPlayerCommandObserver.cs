using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Every player's command as the server runs it, humans' and bots' alike
/// (plan_hl2mp_ai §3.4.4): a hook a service implements and registers, called
/// from the command loop's module sink (<see cref="PlayerCommandQueue"/>) at
/// phase 0, once per command, after the command's prepare and before its
/// move. One tap for both, so a recording of either is taken the same way.
/// Implement <see cref="IOrderedHook"/> beside it when more than one is
/// registered.
/// </summary>
public interface IPlayerCommandObserver
{
    /// <summary>
    /// <paramref name="player"/> runs <paramref name="cmd"/>. The command is
    /// the server's copy: read it, do not keep a reference into it.
    /// </summary>
    void OnCommand(EntityHandle player, in UserCmd cmd);
}
