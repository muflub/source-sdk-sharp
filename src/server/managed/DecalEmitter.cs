//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The decal script — <c>CDecalEmitterSystem</c>, in C#.
///
/// <para>
/// plan_gamedll_after.md Phase A3. It was row 11 of <c>kServerSystems[]</c> and
/// <c>game/shared/decals.cpp</c> was in <c>server_csharp.vpc</c>; neither is
/// true now. What answers <c>decalsystem</c> on the server is a three-method
/// shim (<c>shell_decals.cpp</c>) forwarding here, so the two compiled callers
/// — <c>baseentity_shared.cpp</c>'s impact decal and <c>world_adapter.cpp</c>'s
/// <c>infodecal</c> — are unedited.
/// </para>
///
/// <para>
/// <b>The CLIENT still has its own native copy, and must.</b> <c>decals.cpp</c>
/// compiles into <c>client.so</c> as well and its impact effects read the same
/// table. The file left the SERVER's project rather than growing a
/// <c>CLIENT_DLL</c> split down the middle of its class, which is T1's lesson
/// applied rather than risked.
/// </para>
///
/// <para>
/// <b>Every behaviour below is stock's, line for line, including the odd
/// ones.</b> This is a port and not a redesign: the weighted pick's first-slot
/// special case, the empty translation meaning "do not decal", and the
/// game-material key being one byte are all reproduced rather than tidied,
/// because the file they read is shipped content nobody here controls.
/// </para>
/// </summary>
public sealed class DecalEmitter : IServerDecals, IGameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>The script stock reads (<see cref="DecalScript.ScriptPath"/>).</summary>
    public const string ScriptPath = DecalScript.ScriptPath;

    /// <summary>The translation section (<see cref="DecalScript.TranslationSection"/>).</summary>
    public const string TranslationSection = DecalScript.TranslationSection;

    private readonly IEngineFiles _files;
    private readonly IEnginePrecache _precache;
    private readonly IEngineHost _host;

    /// <summary>
    /// The tables and the pick, shared with the client's <c>ClientDecals</c>
    /// since lane c2-sims (plan_client_novalve C2): one port of stock's body,
    /// two sides handing it their own precache and random stream.
    /// </summary>
    private readonly DecalScript _script;

    private bool _loaded;

    public DecalEmitter(ILog log, IEngineFiles files, IEnginePrecache precache, IEngineHost host)
    {

        _log = log;
        _files = files;
        _precache = precache;
        _host = host;
        _script = new DecalScript(log, "[SourceSharp/Decals]");
    }

    /// <summary>
    /// After <c>PropData</c>, which is stock's order rather than a preference —
    /// see <see cref="LifecycleStage.Decals"/>.
    /// </summary>
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Decals;

    /// <summary>
    /// Stock's <c>Init</c> and <c>LevelInitPreEntity</c>, in one place because
    /// the split between them was about WHEN, and this half of it is "once".
    ///
    /// <para>
    /// <b>The load is lazy and the precache is not.</b> Stock loaded the script
    /// from <c>Init</c> — before <c>OnSystemStart</c>, in the game-system walk —
    /// and precached from every level init. The load happens on the first level
    /// init here instead, which is the same file read the same number of times
    /// for any server that loads a map and one fewer for one that never does.
    /// </para>
    ///
    /// <para>
    /// <b>PRE-ENTITY, and that is the load-bearing half.</b> A decal index only
    /// exists once the engine has precached the material, and the precache
    /// window is opened by the world entity's own precache and closed by the
    /// first <c>Spawn()</c>. This stage is inside it; the PostEntity half is
    /// not.
    /// </para>
    /// </summary>
    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        if (!_loaded)
            Load();

        int indexed = _script.Precache(_precache.PrecacheDecal);
        var (entries, groups, materials) = _script.Counts;

        // SAID OUT LOUD BECAUSE THE FAILURE IS OTHERWISE SILENT. A decal table
        // that was never read answers -1 to every lookup, which is exactly what
        // a working one answers for a decal that is genuinely absent -- so
        // "nothing appeared" is the same observation either way. This line is
        // the only live evidence that the script was found at all.
        _log.Msg($"[SourceSharp/Decals] {ScriptPath}: {entries} entry(ies) in " +
                $"{groups} group(s), {materials} game material(s), " +
                $"{indexed} precached for {mapName}\n");
    }

    /// <summary>
    /// How many entries the script named, and how many groups they fall into.
    /// Read by the startup line and by the tests; counted off the tables.
    /// </summary>
    public (int Entries, int Groups, int Materials) Counts => _script.Counts;

    /// <summary>
    /// Reads and parses the script. Public so a test can drive it without a
    /// level init, and idempotent so the level init above may call it blind.
    /// </summary>
    public void Load()
    {
        _loaded = true;
        _script.Load(_files.ReadGameFile(ScriptPath));
    }

    /// <inheritdoc/>
    public int IndexForName(string name) => _script.IndexForName(name, _host.RandomFloat);

    /// <inheritdoc/>
    public string? NameForIndex(int index) => _script.NameForIndex(index);

    /// <inheritdoc/>
    public string? TranslateForGameMaterial(string decalName, int gameMaterial)
        => _script.TranslateForGameMaterial(decalName, gameMaterial);
}
