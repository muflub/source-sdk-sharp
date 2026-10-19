//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The engine's violence ConVars, read the way stock reads them. The four are
/// the engine's own (ConVars.cs says why none is declared here), so each is
/// read by name.
/// </summary>
public static class Violence
{
    /// <summary>
    /// <c>UTIL_ShouldShowBlood</c> (util_shared.cpp): no blood for
    /// <see cref="BloodColor.None"/>; red blood when <c>violence_hblood</c>
    /// is on, any other colour when <c>violence_ablood</c> is.
    /// </summary>
    public static bool ShouldShowBlood(BloodColor color, IEngineConsole console)
    {
        if (color == BloodColor.None)
            return false;

        return On(console, color == BloodColor.Red ? "violence_hblood" : "violence_ablood");
    }

    /// <summary>
    /// <c>ConVar::GetBool</c> over the engine's value: the int part of the
    /// number is non-zero, so "0.5" and text that is no number are both off.
    /// A missing value reads as on, which is each of the four's default.
    /// </summary>
    public static bool On(IEngineConsole console, string name)
    {
        string? value = console.FindConVarValue(name);

        if (string.IsNullOrEmpty(value))
            return true;

        return float.TryParse(value, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float f)
            && (int)f != 0;
    }
}
