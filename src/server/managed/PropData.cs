//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// propdata.txt — <c>CPropData</c>'s two lifecycle stages, and the half of it
/// that is a name table.
///
/// <para>
/// plan_gamedll_after.md Phase A3. <c>CPropData</c> was row 9 of
/// <c>kServerSystems[]</c> and is not a row any more: this participant is what
/// runs its <c>LevelInitPreEntity</c> and <c>LevelShutdownPostEntity</c>.
/// </para>
///
/// <para>
/// <b>WHAT MOVED.</b> The <c>BreakableModels</c> section — named groups of gib
/// models — is parsed here, precached here, and picked from here.
/// <c>CPropData::GetRandomChunkModel</c> on the server asks this class, and its
/// own server arm holds no table at all. That is Part 9a's ownership test
/// applied: a name table and a random pick have no engine tie.
/// </para>
///
/// <para>
/// <b>WHAT DID NOT, AND THE BLOCKER.</b> The propdata.txt KeyValues TREE stays
/// native, loaded and freed by the two steps this class calls.
/// <c>SourceSharp_EntityGetPropData</c> walks a MODEL's own <c>prop_data</c>
/// section — <c>modelinfo->GetModelKeyValueText</c>, the engine's studio data —
/// into that tree's <c>base</c> chain, and <c>CPropData::ParsePropFromKV</c>
/// writes the result through <c>IBreakableWithPropData</c> on a native
/// <c>CBaseEntity</c>. Both belong to whichever lane makes <c>prop_physics</c>
/// and <c>func_break</c> managed entity classes. Named at three sites — here,
/// in <c>shell_propdata.cpp</c> and in <c>Abi.cs</c> — because this tree's own
/// Phase 9 finding is that the dullest verdicts are the ones that only ever get
/// written in a table.
/// </para>
///
/// <para>
/// <b>Two readers of one file, and they read DISJOINT sections.</b> The native
/// load reads everything except <c>BreakableModels</c>; this reads
/// <c>BreakableModels</c> and nothing else. That is a partition rather than a
/// duplicate — there is exactly one owner of each fact — and it is what the
/// half-move costs while the blocker above stands.
/// </para>
/// </summary>
public sealed class PropData : IServerPropData, IGameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>The file stock reads, and the one the native step loads too.</summary>
    public const string ScriptPath = "scripts/propdata.txt";

    /// <summary>The one section this side owns.</summary>
    public const string BreakableSection = "BreakableModels";

    private readonly IEngineFiles _files;
    private readonly IEnginePrecache _precache;
    private readonly IEngineHost _host;

    /// <summary>
    /// Group name to its models, IN DOCUMENT ORDER, which is load-bearing:
    /// propdata.txt's own comment says the groups are "sorted in order of
    /// smallest to largest", and <see cref="RandomChunkModel"/>'s size cap is a
    /// cap on the INDEX.
    ///
    /// <para>
    /// A LIST of pairs rather than a dictionary, because the lookup is a PREFIX
    /// match rather than an equality one — see <see cref="RandomChunkModel"/>.
    /// </para>
    /// </summary>
    private readonly List<(string Name, List<string> Models)> _chunks = new();

    private bool _loaded;

    // The file's FIRST root: stock loads propdata.txt INTO one KeyValues, so a
    // base name is looked up among that root's children and nowhere else.
    private KeyValuesText? _fileRoot;

    public PropData(ILog log, IEngineFiles files, IEnginePrecache precache, IEngineHost host)
    {

        _log = log;
        _files = files;
        _precache = precache;
        _host = host;
    }

    /// <summary>
    /// Before <see cref="DecalEmitter"/>, which is stock's order rather than a
    /// preference — see <see cref="LifecycleStage.PropData"/>.
    /// </summary>
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PropData;

    /// <summary>
    /// Stock's <c>CPropData::LevelInitPreEntity</c>: the native tree, then this
    /// side's table, then the precache.
    ///
    /// <para>
    /// <b>The native load runs FIRST and that is not arbitrary.</b> It is what
    /// stock did in one method, and anything reached from the entity spawns
    /// that follow — <c>SourceSharp_EntityGetPropData</c> in particular — reads
    /// that tree. Loading it after the precache would leave a window in which a
    /// prop asked for its health and got nothing.
    /// </para>
    ///
    /// <para>
    /// <b>The precache is per level and the parse is not.</b> propdata.txt does
    /// not change between maps; a model's precache index does, because the
    /// precache tables are rebuilt with the map.
    /// </para>
    /// </summary>
    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        if (!_loaded)
            Load();

        int precached = 0;

        foreach (var (_, models) in _chunks)
        {
            foreach (string model in models)
            {
                _precache.PrecacheModel(model);
                precached++;
            }
        }

        // SAID OUT LOUD, for <see cref="DecalEmitter"/>'s reason. A chunk table
        // that was never read answers null to every group, and a prop that
        // breaks into nothing looks exactly like a prop whose model names no
        // gibs. This line is the difference.
        _log.Msg($"[SourceSharp/PropData] {ScriptPath}: {_chunks.Count} breakable " +
                $"chunk group(s), {precached} model(s) precached for {mapName}\n");
    }

    // NO LEVEL-SHUTDOWN STEP. Stock's CPropData::LevelShutdownPostEntity
    // freed the native tree, and the server holds none since
    // plan_server_novalve.md P2.4. The chunk table and the parsed file are NOT
    // cleared either, and stock did not clear its table: LevelInitPreEntity
    // refilled it from the same file, so the content is identical on every map
    // and keeping it is one file read instead of one per map. What must happen
    // per map is the PRECACHE, and that is above.

    /// <summary>How many chunk groups the file named, and how many models they
    /// hold between them. Counted off the table rather than kept beside it.</summary>
    public (int Groups, int Models) Counts
    {
        get
        {
            int models = 0;

            foreach (var (_, list) in _chunks)
                models += list.Count;

            return (_chunks.Count, models);
        }
    }

    /// <summary>
    /// Reads and parses <c>BreakableModels</c>. Public so a test can drive it
    /// without a level init, and idempotent so the level init may call it blind.
    /// </summary>
    public void Load()
    {
        _loaded = true;
        _chunks.Clear();
        _fileRoot = null;

        string? text = _files.ReadGameFile(ScriptPath);

        if (text is null)
        {
            // Stock is SILENT here -- ParsePropDataFile just returns and
            // m_bPropDataLoaded stays false. Silence is what makes a missing
            // file indistinguishable from a file with no gibs in it, so this
            // says so instead.
            _log.Warning($"[SourceSharp/PropData] unable to load '{ScriptPath}'; " +
                        "no breakable chunk group has any model in it\n");
            return;
        }

        KeyValuesText document;

        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[SourceSharp/PropData] '{ScriptPath}' is malformed: {e.Message}\n");
            return;
        }

        _fileRoot = document.Children.Count > 0 ? document.Children[0] : null;

        // The file has ONE root, and BreakableModels is a child of it. Walked
        // over every root rather than assuming the first, because a KV1
        // document may hold several and the parser reports what is there
        // instead of what this file happens to contain today.
        foreach (var root in document.Children)
        {
            var section = root.Find(BreakableSection);

            if (section is null)
                continue;

            foreach (var group in section.Children)
            {
                if (!group.IsBlock)
                    continue;

                var models = new List<string>(group.Children.Count);

                foreach (var model in group.Children)
                    models.Add(model.Name);

                _chunks.Add((group.Name, models));
            }

            return;
        }

        _log.Warning($"[SourceSharp/PropData] '{ScriptPath}' has no '{BreakableSection}' section\n");
    }

    /// <inheritdoc/>
    /// <summary>
    /// A model's own prop data: its <c>$keyvalues</c> text's <c>prop_data</c>
    /// section, over the propdata.txt sections its <c>base</c> chain names.
    /// plan_server_novalve.md P2.4, out of <c>SourceSharp_EntityGetPropData</c>
    /// and <c>ReadPropDataSection</c> (shell_scripted.cpp), which parsed both
    /// with tier1's KeyValues. Answers the health, 0 for a model with no text,
    /// no parse or no section.
    /// </summary>
    public int ModelPropData(string? modelKeyValueText, out float explosiveDamage, out float explosiveRadius)
    {
        explosiveDamage = 0f;
        explosiveRadius = 0f;

        if (string.IsNullOrEmpty(modelKeyValueText))
            return 0;

        KeyValuesText document;
        try
        {
            document = KeyValuesText.Parse(modelKeyValueText);
        }
        catch (KeyValuesTextException)
        {
            return 0;
        }

        // LoadFromBuffer reads the text's first root INTO the KeyValues it is
        // called on, and FindKey then searches that root's children.
        if (document.Children.Count == 0)
            return 0;

        int health = 0;
        ReadSection(document.Children[0].FindPath("prop_data"), 0,
                    ref health, ref explosiveDamage, ref explosiveRadius);
        return health;
    }

    // The base first, so this section's own values override it; depth-limited
    // because a base cycle in data is a bad file, not a hang.
    private void ReadSection(KeyValuesText? section, int depth,
                             ref int health, ref float explosiveDamage, ref float explosiveRadius)
    {
        if (section is null || depth > 8)
            return;

        string? baseName = section.GetString("base", null);

        if (!string.IsNullOrEmpty(baseName) && _fileRoot is not null)
            ReadSection(_fileRoot.FindPath(baseName), depth + 1, ref health, ref explosiveDamage, ref explosiveRadius);

        health          = section.GetInt("health", health);
        explosiveDamage = section.GetFloat("explosive_damage", explosiveDamage);
        explosiveRadius = section.GetFloat("explosive_radius", explosiveRadius);
    }

    public string? RandomChunkModel(string section, int maxSize)
    {
        if (string.IsNullOrEmpty(section))
            return null;

        // A PREFIX MATCH, CASE-SENSITIVE, and it is stock's
        // (props_shared.cpp: Q_strncmp( STRING(iszChunkType), pszBreakableSection,
        // strlen(pszBreakableSection) )). The GROUP's name must start with the
        // string asked for, so "Wood" finds "WoodChunks".
        //
        // Reproduced rather than tightened to an equality, because the callers
        // are map and model data: a model whose breakable_model reads "Wood"
        // resolves today and would stop resolving under an equality, silently,
        // as a prop that breaks into nothing.
        //
        // FIRST MATCH WINS, which is stock's loop breaking on the first hit.
        foreach (var (name, models) in _chunks)
        {
            if (!name.StartsWith(section, StringComparison.Ordinal))
                continue;

            if (models.Count == 0)
                return null;

            // Stock: RandomInt( 0, count-1 ), or RandomInt( 0, MIN(maxSize, count-1) )
            // -- INCLUSIVE at both ends, and a cap on the INDEX rather than on
            // any measured size.
            int top = maxSize == -1 ? models.Count - 1 : Math.Min(maxSize, models.Count - 1);

            if (top < 0)
                return null;

            return models[RandomIndex(top)];
        }

        return null;
    }

    /// <summary>
    /// Stock's <c>RandomInt( 0, top )</c>, inclusive, from THE ENGINE'S STREAM.
    ///
    /// <para>
    /// <b>The engine's and not <c>System.Random</c>.</b> Server randomness is
    /// replayed by demo playback and a managed generator keeps state the
    /// recording knows nothing about — <c>Engine.RandomFloat</c>'s own comment
    /// makes the rule: managed code may ask for a random number, it may not be
    /// the source of one.
    /// </para>
    ///
    /// <para>
    /// Derived from <c>RandomFloat</c> because there is no <c>RandomInt</c> slot
    /// and adding one would be a slot for a single caller. Both spellings draw
    /// from the same uniform stream, so the state advances either way; the
    /// clamp is for the open-interval edge, where a float draw of exactly
    /// <c>top + 1</c> would otherwise index past the end.
    /// </para>
    /// </summary>
    private int RandomIndex(int top)
    {
        int i = (int)_host.RandomFloat(0f, top + 1);
        return i < 0 ? 0 : (i > top ? top : i);
    }
}
