using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Renames a player as if its <c>name</c> convar had changed: the rule in
/// <see cref="PlayerNames"/>, so player_changename fires to every client. A
/// Steam client's engine restores its persona name on every change, so a real
/// rename cannot be driven on a box with one Steam account; the live step
/// changename drives this. A cheat. The engine's own copy of the name (its
/// userinfo, the scoreboard's source) does not change.
/// </summary>
[ConCommand("cs_sv_rename", Flags = ConVarFlags.Cheat,
            Help = "cs_sv_rename <player index> <name>: rename that player as a userinfo change would.")]
public sealed class RenameCommand(ILog log, PlayerNames names) : IConCommand
{
    public void Execute(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int player) || player <= 0)
        {
            log.Msg("usage: cs_sv_rename <player index> <name>\n");
            return;
        }

        string old = names.NameOf(player);
        bool renamed = names.Rename(player, parts[1].Trim('"'));
        log.Msg($"[SourceSharp/Server] rename {player}: '{old}' -> '{names.NameOf(player)}' ({(renamed ? "player_changename" : "unchanged")})\n");
    }
}
