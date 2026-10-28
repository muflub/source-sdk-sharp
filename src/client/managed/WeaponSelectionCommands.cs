using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The weapon-selection binds (lane M1-B, from shell_hud.cpp's native
/// CON_COMMANDs): the stock bind names, kept so cfg/config.cfg's mouse wheel,
/// number keys and q keep landing somewhere, each sending the server the line
/// the managed Arsenal answers as a client command. There is no selection UI to
/// drive: the HUD draws the weapon list, and the choice is the server's.
///
/// <para>
/// EACH COMMAND SENDS ITS OWN NAME. For a while invnext and invprev both sent
/// <c>lastinv</c>, and with two weapons that is indistinguishable from working
/// (forward and back are the same move), so it read as "switching is flaky in
/// some states" rather than as one verb wearing three names.
/// </para>
///
/// <para>
/// NO TRAILING NEWLINE: ServerCmd sends a network stringcmd, not a console
/// buffer, and the server tokenizes what arrives verbatim; an appended
/// <c>\n</c> rode into the last argument and made the Arsenal's
/// <c>lastinv</c> comparison miss.
/// </para>
/// </summary>
public static class WeaponSelection
{
    /// <summary>
    /// The server line a bind sends: its own name for the three cycling
    /// commands, <c>ss_slot N</c> (zero-based) for <c>slotN</c>; null for a name
    /// that is not a selection bind.
    /// </summary>
    public static string? ServerLine(string command) => command switch
    {
        "invnext" or "invprev" or "lastinv" => command,
        ['s', 'l', 'o', 't', >= '1' and <= '6'] => "ss_slot " + (command[4] - '1'),
        _ => null,
    };

    /// <summary>Sends <paramref name="command"/>'s line, when it has one.</summary>
    public static void Forward(IWeaponSelectionCommand send, string command)
    {
        if (ServerLine(command) is string line)
            send.SendSelection(line);
    }
}

[ConCommand("invnext", Help = "Select the next weapon")]
public sealed class InvNextCommand(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "invnext");
}

[ConCommand("invprev", Help = "Select the previous weapon")]
public sealed class InvPrevCommand(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "invprev");
}

[ConCommand("lastinv", Help = "Select the previously held weapon")]
public sealed class LastInvCommand(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "lastinv");
}

[ConCommand("slot1", Help = "Select slot 1")]
public sealed class Slot1Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot1");
}

[ConCommand("slot2", Help = "Select slot 2")]
public sealed class Slot2Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot2");
}

[ConCommand("slot3", Help = "Select slot 3")]
public sealed class Slot3Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot3");
}

[ConCommand("slot4", Help = "Select slot 4")]
public sealed class Slot4Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot4");
}

[ConCommand("slot5", Help = "Select slot 5")]
public sealed class Slot5Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot5");
}

[ConCommand("slot6", Help = "Select slot 6")]
public sealed class Slot6Command(IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args) => WeaponSelection.Forward(send, "slot6");
}
