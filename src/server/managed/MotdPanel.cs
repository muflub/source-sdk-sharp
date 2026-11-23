//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Stock's sv_motd_unload_on_dismissal (hl2mp_client.cpp).</summary>
[ConVar("sv_motd_unload_on_dismissal", "0",
        Help = "If enabled, the MOTD contents will be unloaded when the player closes the MOTD.")]
public sealed class MotdUnloadOnDismissalConVar : BoolConVar { }

/// <summary>
/// The message of the day on join: stock's FinishClientPutInServer, reached
/// from ClientActive, shows the joining player the info panel (VGUIMenu
/// <c>info</c>) -- title the hostname, else "MESSAGE OF THE DAY"; type 1, the
/// string-table entry named by msg, <c>motd</c> (MessageOfTheDay loads it into
/// InfoPanel); unload sv_motd_unload_on_dismissal. Reliable, to them alone.
/// A game's <see cref="IMotdGate"/> may keep it from a player.
/// </summary>
public sealed class MotdPanel(IUserMessageSender messages, HostnameConVar hostname, MotdUnloadOnDismissalConVar unload,
                              IHooks<IMotdGate>? gates = null)
{
    /// <summary>PANEL_INFO.</summary>
    public const string InfoPanel = "info";

    /// <summary>The title when the server has no hostname.</summary>
    public const string DefaultTitle = "MESSAGE OF THE DAY";

    /// <summary>Panels sent, since the module started.</summary>
    public int Sent { get; private set; }

    public void OnClientActive(int entIndex)
    {
        // Every gate is asked (each may count the activation); a throw is
        // logged and refuses nothing.
        bool shown = true;
        gates?.Each(nameof(IMotdGate.ShowsMotd), g => shown &= g.ShowsMotd(entIndex));
        if (!shown)
            return;

        string title = hostname.HasValue ? hostname.Value : DefaultTitle;

        bool sent = messages.Send(new VguiMenu
        {
            Name = InfoPanel,
            Show = true,
            Keys = ["title", "type", "msg", "unload"],
            Values = [title, "1", "motd", unload.Value ? "1" : "0"],
        }, entIndex, reliable: true);

        if (sent)
            Sent++;
    }
}

/// <summary>
/// Whether a client becoming active is shown the message of the day
/// (<see cref="MotdPanel"/>): a game's registered veto, as
/// <see cref="INameChangeGate"/> is the rename's. Every registered gate must
/// allow it for the panel to be sent; none registered, every activation shows
/// it, as stock's does on every level. Every gate is asked once per activation,
/// so a gate may count them (a game that shows it only on a player's first level of a session).
/// </summary>
public interface IMotdGate
{
    /// <summary>Whether <paramref name="entIndex"/>, active now, is shown the message of the day.</summary>
    bool ShowsMotd(int entIndex);
}
