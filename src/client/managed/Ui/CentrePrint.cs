//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>How long a centre print stays, seconds (stock: scr_centertime 2 outside TF).</summary>
[ConVar("scr_centertime", "2", Help = "How long a centre print stays on the screen, seconds")]
public sealed class ScrCenterTimeConVar : FloatConVar
{
}

/// <summary>
/// THE CENTRE PRINT (stock CCenterStringLabel, vgui_centerstringpanel.cpp):
/// the engine's ICenterPrint prints and clears through the shell, and
/// text_message's HUD_PRINTCENTER prints here directly. A print is shown for
/// scr_centertime from now, in its colour (white by default); Clear takes it
/// down. Stock does not fade it: it is shown or not. The core's, so a UI-only
/// reload keeps it.
/// </summary>
public sealed class CentrePrint(IGameClock clock, ScrCenterTimeConVar? time = null) : ICentrePrintSource, ICenterPrintEvents
{
    private float _off;
    private string _text = string.Empty;

    /// <summary>The colour of the last print (stock SetFgColor), 0xRRGGBBAA.</summary>
    public uint Colour { get; private set; } = 0xFFFFFFFF;

    public string CenterPrint => _off > clock.Now ? _text : string.Empty;

    public float CenterPrintAlpha => _off > clock.Now ? 1f : 0f;

    public void Print(string text, int r, int g, int b, int a)
    {
        _text = text;
        Colour = (uint)((r & 0xFF) << 24 | (g & 0xFF) << 16 | (b & 0xFF) << 8 | (a & 0xFF));
        _off = (time?.Value ?? 2f) + clock.Now;
    }

    /// <summary>Stock's Print(text): white.</summary>
    public void Print(string text) => Print(text, 255, 255, 255, 255);

    public void Clear() => _off = 0f;
}
