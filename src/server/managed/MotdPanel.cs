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
/// </summary>
public sealed class MotdPanel(IUserMessageSender messages, IEngineConsole console, ConVarRegistry convars)
{
    /// <summary>PANEL_INFO.</summary>
    public const string InfoPanel = "info";

    /// <summary>The title when the server has no hostname.</summary>
    public const string DefaultTitle = "MESSAGE OF THE DAY";

    private readonly MotdUnloadOnDismissalConVar? _unload = convars.Get<MotdUnloadOnDismissalConVar>();

    /// <summary>Panels sent, since the module started.</summary>
    public int Sent { get; private set; }

    public void OnClientActive(int entIndex)
    {
        string title = console.FindConVarValue("hostname") ?? DefaultTitle;

        bool sent = messages.Send(new VguiMenu
        {
            Name = InfoPanel,
            Show = true,
            Keys = ["title", "type", "msg", "unload"],
            Values = [title, "1", "motd", (_unload?.Value ?? false) ? "1" : "0"],
        }, entIndex, reliable: true);

        if (sent)
            Sent++;
    }
}
