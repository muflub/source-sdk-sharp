//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Sounds;

/// <summary>
/// The client's sound emitter SYSTEM: stock's <c>CSoundEmitterSystem</c>
/// game-system stages as SoundEmitterSystem.cpp compiled them under
/// <c>CLIENT_DLL</c> -- <c>Init</c> (the script table's <c>ModInit</c>),
/// <c>Shutdown</c>, the level's own <c>maps/&lt;map&gt;_level_sounds.txt</c>
/// over the table before the entities, and the overrides cleared after them.
/// The client has no precache log and no preload walk: those arms are the
/// server's (its SoundEmitterSystem).
/// </summary>
public sealed class ClientSoundEmitterSystem(IClientSoundSystemEngine engine)
{
    /// <summary><c>Init</c>: false stops the client coming up, as the row's false did.</summary>
    public bool Init() => engine.ModInit();

    /// <summary><c>Shutdown</c>.</summary>
    public void Shutdown() => engine.ModShutdown();

    /// <summary>
    /// <c>LevelInitPreEntity</c>: the map's own override script, from the
    /// level the engine names, added over the table when the game has one.
    /// </summary>
    public void LevelInitPreEntity()
    {
        string scriptFile = SoundScriptFiles.LevelSoundsScript(SoundScriptFiles.FileName(engine.LevelName()));
        if (engine.FileExists(scriptFile, "GAME"))
            engine.AddSoundOverrides(scriptFile, false);
    }

    /// <summary><c>LevelShutdownPostEntity</c>: the level's overrides cleared.</summary>
    public void LevelShutdownPostEntity() => engine.ClearSoundOverrides();
}
