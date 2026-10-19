//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
namespace SourceSharp.Shared;

/// <summary>
/// The level's own sound script, as <c>CSoundEmitterSystem::LevelInitPreEntity</c>
/// names it on both sides: <c>"maps/%s"</c> of the map's file name,
/// <c>Q_FixSlashes</c>, <c>Q_strlower</c>, <c>Q_StripExtension</c> and
/// <c>"_level_sounds.txt"</c>. GetCleanMapName is the identity here (its TF
/// arms never compiled), and so is stock's TF_DLL arm's absence: Mann vs
/// Machine's override scripts are not this mod's.
/// </summary>
public static class SoundScriptFiles
{
    /// <summary>
    /// The override script of the map whose file name (no directory) is
    /// <paramref name="mapFileName"/>: "dm_lockdown" or "dm_lockdown.bsp"
    /// both name maps/dm_lockdown_level_sounds.txt.
    /// </summary>
    public static string LevelSoundsScript(string mapFileName)
    {
        // char mapname[ 256 ]: Q_snprintf keeps 255 characters.
        string mapName = "maps/" + mapFileName;
        if (mapName.Length > 255)
            mapName = mapName[..255];

        mapName = AsciiLower(mapName.Replace('\\', '/'));
        return StripExtension(mapName) + "_level_sounds.txt";
    }

    /// <summary><c>V_GetFileName</c>: what follows the last '/' or '\', or the whole path.</summary>
    public static string FileName(string path)
    {
        int at = path.Length - 1;
        while (at >= 0 && path[at] != '/' && path[at] != '\\')
            --at;
        return path[(at + 1)..];
    }

    /// <summary><c>Q_strlower</c>: ASCII letters only, as the C library's tolower in the "C" locale.</summary>
    public static string AsciiLower(string text)
        => string.Create(text.Length, text, static (span, from) =>
        {
            for (int i = 0; i < from.Length; i++)
                span[i] = from[i] is >= 'A' and <= 'Z' ? (char)(from[i] + ('a' - 'A')) : from[i];
        });

    /// <summary>
    /// <c>V_StripExtension</c>: everything before the last '.' that comes after
    /// the last path separator and is not the first character; the name as it
    /// is when there is none.
    /// </summary>
    public static string StripExtension(string path)
    {
        int end = path.Length - 1;
        while (end > 0 && path[end] != '.' && path[end] != '/' && path[end] != '\\')
            --end;

        return end > 0 && path[end] == '.' ? path[..end] : path;
    }
}
