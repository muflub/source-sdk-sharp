//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Text;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The server's sound emitter SYSTEM, managed (plan_server_native_v2 T4a, the
/// precache path): stock's <c>CSoundEmitterSystem</c> server half
/// (game/shared/SoundEmitterSystem.cpp) less the emit path, which is
/// <see cref="SoundEmitter"/>'s. Its <c>Init</c> and <c>Shutdown</c> (the
/// script table's <c>ModInit</c> / <c>ModShutdown</c>), the level's
/// <c>maps/&lt;map&gt;_level_sounds.txt</c> overrides and the preload walk,
/// the level's end, <c>Flush</c>, the -makereslists precache log
/// (<c>reslists\&lt;map&gt;.snd</c>), <c>PrecacheScriptSound</c> and
/// <c>PrefetchScriptSound</c>, and the free functions
/// <c>GameSound_Precache</c> / <c>GameSound_Prefetch</c> they call.
///
/// <para>
/// Which sounds are precached, and what is warned about, is decided here.
/// The precache window (<c>CBaseEntity::IsPrecacheAllowed</c>) stays native,
/// where the world's own precache opens it; this asks it.
/// </para>
///
/// <para>
/// <b>Kept from sound_emitter.cpp's server arm</b>: the "shown once" sets are
/// case-sensitive, as the default <c>CUtlSymbolTable</c>s stock used were.
/// The failure set is per system rather than per process: a managed reload
/// reports a missing script once more.
/// </para>
/// </summary>
public sealed class SoundEmitterSystem
{
    private readonly ISoundPrecacheEngine _engine;
    private readonly Func<string> _mapName;
    private readonly Action<string> _warning;
    private readonly Action<string> _devMsg;

    // m_bLogPrecache: -makereslists, and off for good after an i/o failure.
    private bool _logPrecache;

    // m_hPrecacheLogFile: FILESYSTEM_INVALID_HANDLE is 0.
    private nint _logFile;

    // m_PrecachedScriptSounds: what this level's log already holds.
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    // PrecacheScriptSound's s_PrecacheScriptSoundFailures.
    private readonly HashSet<string> _failures = new(StringComparer.Ordinal);

    // g_bPermitDirectSoundPrecache: true only while a script's own waves, or
    // a wave PrecacheScriptSound was handed, are precached.
    private bool _permitDirect;

    /// <param name="engine">Every engine call this makes.</param>
    /// <param name="makeResLists">
    /// <c>-makereslists</c> on the command line: log every script precached,
    /// per level. Stock read it in <c>Init</c>; it is read once, at
    /// construction, so a system built by a managed reload logs as the first did.
    /// </param>
    /// <param name="mapName"><c>gpGlobals->mapname</c>, read when it is needed.</param>
    /// <param name="warning">The console's <c>Warning</c>.</param>
    /// <param name="devMsg">The developer console (<c>DevMsg</c>).</param>
    public SoundEmitterSystem(ISoundPrecacheEngine engine, bool makeResLists, Func<string> mapName,
                              Action<string> warning, Action<string> devMsg)
    {
        _engine = engine;
        _logPrecache = makeResLists;
        _mapName = mapName;
        _warning = warning;
        _devMsg = devMsg;
    }

    /// <summary>
    /// <c>SourceSharp_PrecacheSound</c>'s rule (shell_sound.cpp): a name with
    /// no slash is a soundscript entry ("Doors.Move18"), one with a slash a
    /// path to a wave ("ambient/alarms/foo.wav").
    /// </summary>
    public static bool IsScriptSound(string name) => name.IndexOfAny(['/', '\\']) < 0;

    //-------------------------------------------------------------------------
    // The system's stages
    //-------------------------------------------------------------------------

    /// <summary><c>Init</c>: the script table's <c>ModInit</c>; false stops the server coming up.</summary>
    public bool Init() => _engine.ModInit();

    /// <summary><c>Shutdown</c>: the log closed, then the table's <c>ModShutdown</c>.</summary>
    public void Shutdown()
    {
        FinishLog();
        _engine.ModShutdown();
    }

    /// <summary>
    /// <c>LevelInitPreEntity</c>: this level's log started, the map's own
    /// <c>maps/&lt;map&gt;_level_sounds.txt</c> added over the table when the
    /// game has one, and every entry that asks to be preloaded precached.
    /// </summary>
    public void LevelInitPreEntity()
    {
        StartLog();

        // "maps/%s", Q_FixSlashes, Q_strlower, Q_StripExtension (SoundScriptFiles,
        // both sides' rule).
        string scriptFile = SoundScriptFiles.LevelSoundsScript(_mapName());

        if (_engine.FileExists(scriptFile, "GAME"))
            _engine.AddSoundOverrides(scriptFile);

        for (int i = _engine.First(); i != _engine.InvalidIndex(); i = _engine.Next(i))
        {
            if (_engine.ShouldPreload(i))
                InternalPrecacheWaves(i);
        }
    }

    /// <summary><c>LevelShutdownPostEntity</c>: the level's overrides cleared and its log closed.</summary>
    public void LevelShutdownPostEntity()
    {
        _engine.ClearSoundOverrides();
        FinishLog();
    }

    /// <summary>
    /// <c>Flush</c>: the log closed and the table flushed (its own
    /// ModShutdown and ModInit). Stock's only caller was the deleted
    /// sv_soundemitter_flush command; ported, not deleted.
    /// </summary>
    public void Flush()
    {
        FinishLog();
        _engine.Flush();
    }

    /// <summary>
    /// The log closed and nothing else: what a managed reload does in place of
    /// <see cref="Shutdown"/>, since the script table the native Init loaded
    /// outlives this system (no stock counterpart: stock had no reload).
    /// </summary>
    public void CloseLog() => FinishLog();

    //-------------------------------------------------------------------------
    // The script precache and prefetch
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>PrecacheScriptSound</c>: every wave of the script, logged under
    /// -makereslists, answering the script's handle. A name the table does
    /// not have is a .wav (either case) or .mp3 (lower case: stock's
    /// <c>strstr</c>) precached as named, or a missing script, reported once
    /// on the developer console; either answers the table's invalid index.
    /// </summary>
    public short PrecacheScriptSound(string soundName)
    {
        int soundIndex = _engine.SoundIndex(soundName);
        if (!_engine.IsValidIndex(soundIndex))
        {
            if (IsDirectWave(soundName))
            {
                _permitDirect = true;
                Precache(soundName);
                _permitDirect = false;
                return SoundEmitter.InvalidHandle;
            }

            // Make sure we only show the message once
            if (soundName.Length > 0 && _failures.Add(soundName))
                _devMsg($"PrecacheScriptSound '{soundName}' failed, no such sound script entry\n");

            return (short)soundIndex;
        }

        LogPrecache(soundName);

        InternalPrecacheWaves(soundIndex);
        return (short)soundIndex;
    }

    /// <summary>
    /// <c>PrefetchScriptSound</c>: every wave of the script prefetched, or a
    /// .wav / .mp3 the table does not have prefetched as named. Ported dead
    /// arm: stock's server has no caller.
    /// </summary>
    public void PrefetchScriptSound(string soundName)
    {
        int soundIndex = _engine.SoundIndex(soundName);
        if (!_engine.IsValidIndex(soundIndex))
        {
            if (IsDirectWave(soundName))
                Prefetch(soundName);
            return;
        }

        InternalPrefetchWaves(soundIndex);
    }

    /// <summary>
    /// <c>GameSound_Precache</c> (stock's <c>CBaseEntity::PrecacheSound</c>):
    /// warned about when nothing permitted a direct precache, and when it
    /// comes after the precache window shut on a sound the engine has not
    /// got; then precached, preloaded, either way.
    /// </summary>
    public bool Precache(string name)
    {
        // IsPC(): this port runs on nothing else.
        if (!_permitDirect)
            _warning($"Direct precache of {name}\n");

        // If this is out of order, warn
        if (!_engine.PrecacheAllowed() && !_engine.IsSoundPrecached(name))
            _warning($"Late precache of {name}\n");

        return _engine.PrecacheSound(name, true);
    }

    /// <summary><c>GameSound_Prefetch</c>: the engine's prefetch.</summary>
    public void Prefetch(string name) => _engine.PrefetchSound(name);

    /// <summary>
    /// <c>SourceSharp_PrecacheSound</c> (shell_sound.cpp), the precache every
    /// managed entity's <c>PrecacheSound</c> reaches: a script by its name,
    /// a path to a wave directly (and so warned about); nothing for an empty name.
    /// </summary>
    public void PrecacheSound(string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        if (IsScriptSound(name))
            PrecacheScriptSound(name);
        else
            Precache(name);
    }

    /// <summary><c>Q_strlower</c>: A-Z only; every other character as it is.</summary>
    private static bool IsDirectWave(string name)
        => name.Contains(".wav", StringComparison.OrdinalIgnoreCase)
           || name.Contains(".mp3", StringComparison.Ordinal);

    private void InternalPrecacheWaves(int soundIndex)
    {
        IReadOnlyList<string>? waves = _engine.Waves(soundIndex);
        if (waves is null)
            return;

        if (waves.Count == 0)
        {
            NoWaves(soundIndex);
            return;
        }

        _permitDirect = true;

        foreach (string wave in waves)
            Precache(wave);

        _permitDirect = false;
    }

    private void InternalPrefetchWaves(int soundIndex)
    {
        IReadOnlyList<string>? waves = _engine.Waves(soundIndex);
        if (waves is null)
            return;

        if (waves.Count == 0)
        {
            NoWaves(soundIndex);
            return;
        }

        foreach (string wave in waves)
            Prefetch(wave);
    }

    private void NoWaves(int soundIndex)
        => _devMsg($"CSoundEmitterSystem:  sounds.txt entry '{_engine.SoundName(soundIndex)}' has no waves listed under 'wave' or 'rndwave' key!!!\n");

    //-------------------------------------------------------------------------
    // The -makereslists precache log
    //-------------------------------------------------------------------------

    private void LogPrecache(string soundName)
    {
        if (!_logPrecache)
            return;

        // Make sure we only show the message once
        if (_logged.Contains(soundName))
            return;

        if (_logFile == 0)
            StartLog();

        _logged.Add(soundName);

        if (_logFile != 0)
        {
            _engine.Write("\""u8, _logFile);
            _engine.Write(Encoding.UTF8.GetBytes(soundName), _logFile);
            _engine.Write("\"\n"u8, _logFile);
        }
        else
        {
            _warning("Disabling precache logging due to file i/o problem!!!\n");
            _logPrecache = false;
        }
    }

    private void StartLog()
    {
        _logged.Clear();

        if (!_logPrecache)
            return;

        if (_logFile != 0)
            return;

        _engine.CreateDirHierarchy("reslists", "DEFAULT_WRITE_PATH");

        // open the new level reslist
        _logFile = _engine.Open($"reslists\\{_mapName()}.snd", "wt", "GAME");
    }

    private void FinishLog()
    {
        if (_logFile != 0)
        {
            _engine.Close(_logFile);
            _logFile = 0;
        }

        _logged.Clear();
    }
}
