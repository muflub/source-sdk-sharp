using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Portal;

/// <summary><c>tf2_descent_portal &lt;player&gt;</c>: opens a town portal for the player as a Teleporter Blueprint would, with no scroll (dev).</summary>
[ConCommand("tf2_descent_portal", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_portal <player slot>: open a town portal in front of the player as the scroll does, without a scroll")]
public sealed class PortalOpenCommand(TownPortals portals, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var opened = portals.Open(slot);
        log.Msg(opened.Opened
            ? $"[TF2Descent/Portal] tf2_descent_portal: opened for slot {slot}\n"
            : $"[TF2Descent/Portal] tf2_descent_portal: refused for slot {slot}: {opened.Why}\n");
    }
}

/// <summary><c>tf2_descent_portals</c>: every portal's record and state, and this map's pads (dev).</summary>
[ConCommand("tf2_descent_portals", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_portals: print every town portal's record and state, and the pads on this map")]
public sealed class PortalListCommand(TownPortals portals, ILog log)
{
    public void Execute() => log.Msg(portals.Report());
}

/// <summary>
/// <c>tf2_descent_portal_use &lt;player&gt; [portal]</c>: the player uses a pad on this map by the
/// path standing on it for the charge time takes: the nearest he may use, or portal
/// <c>portal</c>'s (a spent or gone one is refused as such) (dev).
/// </summary>
[ConCommand("tf2_descent_portal_use", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_portal_use <player slot> [portal]: use a town portal pad as standing on it does; with a portal number, that portal's")]
public sealed class PortalUseCommand(TownPortals portals, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, ushort.MaxValue)] int portal = 0)
        => _ = Report(slot, portal == 0 ? portals.Use(slot) : portals.Use(slot, portal));

    private async Task Report(int slot, Task<PortalUse> use)
    {
        var used = await use;
        log.Msg(used.Started
            ? $"[TF2Descent/Portal] tf2_descent_portal_use: slot {slot} is on his way\n"
            : $"[TF2Descent/Portal] tf2_descent_portal_use: refused for slot {slot}: {used.Why}\n");
    }
}
