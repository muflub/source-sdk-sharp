//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The once-per-install default for <c>r_lightmap_bicubic</c>: stock
/// CHLClient::PostInit (cdll_client_int.cpp). Moved out of shell_clientdll.cpp's
/// PostInit (lane M1-B), which now hands managed the adapter's maximum DX
/// support level and nothing else.
///
/// <para>
/// On a fresh install (<c>r_lightmap_bicubic_set</c> still 0) bicubic
/// lightmaps go on for an adapter of DX 9.5 or better, or DX 9.0 or better on
/// Linux, and the marker is set so a player's later choice is never
/// overwritten. With no material system there is no adapter to read (native
/// says so with a negative level) and nothing is decided, as stock's
/// <c>&amp;&amp; materials</c> skipped the block.
/// </para>
/// </summary>
public sealed class LightmapBicubicDefault(IEngineConsole console, bool linux)
{
    /// <summary>The marker, LightmapBicubicSetConVar's name.</summary>
    public const string MarkerName = "r_lightmap_bicubic_set";

    /// <summary>The engine's convar the default is for.</summary>
    public const string ConVarName = "r_lightmap_bicubic";

    /// <summary>Whether bicubic lightmaps suit an adapter of <paramref name="maxDxLevel"/>.</summary>
    public static bool Wanted(int maxDxLevel, bool linux) => maxDxLevel >= 95 || (maxDxLevel >= 90 && linux);

    /// <summary>
    /// IBaseClientDLL::PostInit: picks the default once.
    /// </summary>
    /// <returns>The value set, or null when nothing was (the marker set, absent, or no adapter).</returns>
    public bool? PostInit(int adapterMaxDxLevel)
    {
        if (adapterMaxDxLevel < 0)
            return null;
        // The marker is managed's declaration; absent before the convars
        // register, when there is nothing to say it was already done.
        if (console.FindConVarValue(MarkerName) is not string marker || (int)VariantParse.Atof(marker) != 0)
            return null;

        bool on = Wanted(adapterMaxDxLevel, linux);
        console.SetConVarValueByName(ConVarName, on ? "1" : "0");
        console.SetConVarValueByName(MarkerName, "1");
        return on;
    }
}
