using SourceSharp.Abi;

namespace SourceSharp.Client;

// THE BINDS (weapon_selection.cpp's HOOK_COMMANDs): each reaches the
// selection, which decides what the key means under hud_fastswitch.

[ConCommand("invnext", Help = "Select the next weapon")]
public sealed class InvNextCommand(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.NextWeapon();
}

[ConCommand("invprev", Help = "Select the previous weapon")]
public sealed class InvPrevCommand(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.PrevWeapon();
}

[ConCommand("lastinv", Help = "Select the previously held weapon")]
public sealed class LastInvCommand(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.LastWeapon();
}

/// <summary>UserCmd_Close: an open selection closes; with none, stock runs <c>escape</c>.</summary>
[ConCommand("cancelselect", Help = "Close the weapon selection")]
public sealed class CancelSelectCommand(WeaponSelectionHud selection, IWeaponSelectionCommand send) : IConCommand
{
    public void Execute(string args)
    {
        if (!selection.Close())
            send.Escape();
    }
}

[ConCommand("slot1", Help = "Select slot 1")]
public sealed class Slot1Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(1);
}

[ConCommand("slot2", Help = "Select slot 2")]
public sealed class Slot2Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(2);
}

[ConCommand("slot3", Help = "Select slot 3")]
public sealed class Slot3Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(3);
}

[ConCommand("slot4", Help = "Select slot 4")]
public sealed class Slot4Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(4);
}

[ConCommand("slot5", Help = "Select slot 5")]
public sealed class Slot5Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(5);
}

[ConCommand("slot6", Help = "Select slot 6")]
public sealed class Slot6Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(6);
}

[ConCommand("slot7", Help = "Select slot 7")]
public sealed class Slot7Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(7);
}

[ConCommand("slot8", Help = "Select slot 8")]
public sealed class Slot8Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(8);
}

[ConCommand("slot9", Help = "Select slot 9")]
public sealed class Slot9Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(9);
}

[ConCommand("slot10", Help = "Select slot 10")]
public sealed class Slot10Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(10);
}

[ConCommand("slot0", Help = "Select slot 0")]
public sealed class Slot0Command(WeaponSelectionHud selection) : IConCommand
{
    public void Execute(string args) => selection.SlotCommand(0);
}
