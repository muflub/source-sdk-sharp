//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IClientDecals"/> over a <see cref="DecalScript"/>, the same body
/// the server's <c>DecalEmitter</c> wraps. The client's differences from the
/// server's are stock's: the index comes from <c>effects-&gt;Draw_DecalIndexFromName</c>
/// and the pick draws from the client's <c>random</c> (<c>VEngineRandom001</c>).
///
/// <para>
/// <b>A reload carries the tables.</b> The container is rebuilt on
/// <c>cs_cl_reload</c> and native calls <see cref="Init"/> once per process, so
/// a fresh instance would answer −1 to every lookup until the next process;
/// <see cref="Save"/> carries the parse and the indices as bytes instead.
/// </para>
/// </summary>
public sealed class ClientDecals : IClientDecals, IReloadParticipant
{
    private readonly ILog _log;
    private readonly Func<string, string?> _readFile;
    private readonly Func<string, int> _precache;
    private readonly Func<float, float, float> _random;
    private readonly DecalScript _script;

    public ClientDecals(ILog log, IEngineFiles files, IEngineEffects effects, IEngineHost host)
        : this(log, files.ReadGameFile, effects.ClientDecalIndexFromName, host.RandomFloat)
    {
    }

    /// <summary>The seams the oracle replays through: the file, the engine's index, the random stream.</summary>
    internal ClientDecals(ILog log, Func<string, string?> readFile, Func<string, int> precache,
                          Func<float, float, float> random)
    {
        _log = log;
        _readFile = readFile;
        _precache = precache;
        _random = random;
        _script = new DecalScript(log, "[SourceSharp/Decals/client]");
    }

    /// <summary>How many entries, groups and translated materials are held.</summary>
    public (int Entries, int Groups, int Materials) Counts => _script.Counts;

    public void Init() => _script.Load(_readFile(DecalScript.ScriptPath));

    public void LevelInitPreEntity()
    {
        int indexed = _script.Precache(_precache);
        var (entries, groups, materials) = _script.Counts;

        // The only live evidence the script was found: a table never read
        // answers -1 to every lookup, as a working one does for an absent decal.
        _log.Msg($"[SourceSharp/Decals/client] {DecalScript.ScriptPath}: {entries} entry(ies) in " +
                 $"{groups} group(s), {materials} game material(s), {indexed} indexed\n");
    }

    public void Shutdown() => _script.Clear();

    public int IndexForName(string? name) => _script.IndexForName(name, _random);

    public string? NameForIndex(int index) => _script.NameForIndex(index);

    public string? TranslateForGameMaterial(string decalName, int gameMaterial)
        => _script.TranslateForGameMaterial(decalName, gameMaterial);

    void IReloadParticipant.Save(ReloadWriter writer) => _script.Save(writer.Write, writer.Write, writer.Write);

    void IReloadParticipant.Restore(ReloadReader reader)
        => _script.Restore(reader.ReadInt, reader.ReadFloat, reader.ReadString);
}

/// <summary>
/// The decal tables' state, for a live check that the script was read and the
/// table survived a reload: counts, and the index and name one group's pick
/// answers (it draws from the client's random stream, as a real impact does).
/// </summary>
[ConCommand("cs_cl_decals", Help = "SourceSharp: the client's decal tables (counts; with a group name, one pick)")]
public sealed class ClientDecalsReportCommand(ILog log, IClientDecals decals) : IConCommand
{
    public void Execute(string args)
    {
        string group = string.IsNullOrWhiteSpace(args) ? "Impact.Concrete" : args.Trim();
        int index = decals.IndexForName(group);
        string name = decals.NameForIndex(index) ?? "";
        var counts = decals is ClientDecals held ? held.Counts : default;

        log.Msg($"[SourceSharp/Decals/client] {counts.Entries} entry(ies), {counts.Groups} group(s), " +
                $"{counts.Materials} material(s); '{group}' -> {index} '{name}'\n");
    }
}
