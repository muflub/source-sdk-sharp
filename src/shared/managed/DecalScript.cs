//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The decal script's tables, <c>CDecalEmitterSystem</c>'s body, as both sides
/// use it. The server's <c>DecalEmitter</c> (plan_gamedll_after.md A3) wraps
/// one, and since plan_client_novalve C2 (lane c2-sims, cluster 18) so does the
/// client's <c>ClientDecals</c>, which replaced <c>game/shared/decals.cpp</c> in
/// client.so. The two sides differ only in WHAT they are handed: the engine call
/// that precaches a decal (<c>PrecacheDecal</c> on the server,
/// <c>Draw_DecalIndexFromName</c> on the client) and the random stream the pick
/// draws from (each side's <c>VEngineRandom001</c>); the table and the pick are
/// stock's, line for line, odd ones included.
/// </summary>
public sealed class DecalScript
{
    /// <summary>
    /// The script stock reads. <c>decals.cpp</c>'s <c>DECAL_LIST_FILE</c>; its
    /// commented-out neighbour <c>scripts/decals.txt</c> is left where it is.
    /// </summary>
    public const string ScriptPath = "scripts/decals_subrect.txt";

    /// <summary>The section that maps a game material to a group. Stock's <c>TRANSLATION_DATA_SECTION</c>.</summary>
    public const string TranslationSection = "TranslationData";

    /// <summary><c>decals.h</c>'s <c>CHAR_TEX_CONCRETE</c>.</summary>
    private const int ConcreteMaterial = 'C';

    /// <summary>One line of a group: the decal material, its weight, and its precache index.</summary>
    private sealed class Entry(string name, float weight)
    {
        public string Name { get; } = name;
        public float Weight { get; } = weight;

        /// <summary>−1 until a level init precaches it: stock's initial value.</summary>
        public int PrecacheIndex { get; set; } = -1;
    }

    private readonly ILog _log;
    private readonly string _tag;

    /// <summary>Every entry in the file, in the order read. Stock's <c>m_AllDecals</c>.</summary>
    private readonly List<Entry> _all = new();

    /// <summary>Group name to the entries in it. Stock's <c>m_Decals</c>.</summary>
    private readonly Dictionary<string, List<int>> _groups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Game material to group name. Stock's <c>m_GameMaterialTranslation</c>.</summary>
    private readonly Dictionary<string, string> _translation = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="log">Where a missing or malformed script is said.</param>
    /// <param name="tag">The log lines' tag, e.g. <c>[SourceSharp/Decals]</c>.</param>
    public DecalScript(ILog log, string tag)
    {
        _log = log;
        _tag = tag;
    }

    /// <summary>How many entries, groups and translated materials the script named.</summary>
    public (int Entries, int Groups, int Materials) Counts => (_all.Count, _groups.Count, _translation.Count);

    /// <summary>
    /// Replaces the tables with the parse of <paramref name="text"/> (null: the
    /// file was not found). Stock's <c>LoadDecalsFromScript</c>.
    /// </summary>
    public void Load(string? text)
    {
        Clear();

        if (text is null)
        {
            // LOUD: with no script every lookup answers −1, which is what a
            // working system answers for a decal that is genuinely absent.
            _log.Warning($"{_tag} unable to load '{ScriptPath}'; every decal lookup will answer -1\n");
            return;
        }

        KeyValuesText document;

        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"{_tag} '{ScriptPath}' is malformed: {e.Message}\n");
            return;
        }

        KeyValuesText? translation = null;

        // Stock walks the document's roots with GetNextKey() and takes only the
        // ones that have subkeys: decals_subrect.txt is several top-level blocks.
        foreach (var section in document.Children)
        {
            if (!section.IsBlock || section.Children.Count == 0)
                continue;

            if (string.Equals(section.Name, TranslationSection, StringComparison.OrdinalIgnoreCase))
            {
                translation = section;
                continue;
            }

            var indices = new List<int>(section.Children.Count);

            foreach (var line in section.Children)
            {
                indices.Add(_all.Count);
                _all.Add(new Entry(line.Name, ParseWeight(line.Value)));
            }

            // Stock Inserts, which on a CUtlDict keeps the FIRST for a repeated key.
            if (!_groups.ContainsKey(section.Name))
                _groups.Add(section.Name, indices);
        }

        if (translation is null)
        {
            _log.Warning($"{_tag} '{ScriptPath}' has no '{TranslationSection}' section; " +
                        "no impact will be translated by surface\n");
            return;
        }

        foreach (var line in translation.Children)
        {
            // "Don't add NULL string to list": an empty translation is the file
            // saying "do not decal this surface".
            if (line.Value.Length == 0)
                continue;

            if (_groups.ContainsKey(line.Value))
                _translation[line.Name] = line.Value;
            else
                _log.Warning($"{_tag} translation for game material type " +
                            $"'{line.Name}' references unknown decal '{line.Value}'\n");
        }
    }

    /// <summary>Stock's <c>Clear</c> (its <c>Shutdown</c>): every table emptied.</summary>
    public void Clear()
    {
        _all.Clear();
        _groups.Clear();
        _translation.Clear();
    }

    /// <summary>
    /// The tables as primitives, in an order <see cref="Restore"/> reads back:
    /// what a client hot reload carries (plan_client_novalve §3.8), since the
    /// parse and the engine's indices are not re-derived mid-level.
    /// </summary>
    public void Save(Action<int> writeInt, Action<float> writeFloat, Action<string> writeString)
    {
        writeInt(_all.Count);
        foreach (var entry in _all)
        {
            writeString(entry.Name);
            writeFloat(entry.Weight);
            writeInt(entry.PrecacheIndex);
        }

        writeInt(_groups.Count);
        foreach (var (name, indices) in _groups)
        {
            writeString(name);
            writeInt(indices.Count);
            foreach (int idx in indices)
                writeInt(idx);
        }

        writeInt(_translation.Count);
        foreach (var (material, group) in _translation)
        {
            writeString(material);
            writeString(group);
        }
    }

    /// <summary>Reads back what <see cref="Save"/> wrote, replacing the tables.</summary>
    public void Restore(Func<int> readInt, Func<float> readFloat, Func<string> readString)
    {
        Clear();

        int entries = readInt();
        for (int i = 0; i < entries; i++)
        {
            string name = readString();
            float weight = readFloat();
            _all.Add(new Entry(name, weight) { PrecacheIndex = readInt() });
        }

        int groups = readInt();
        for (int g = 0; g < groups; g++)
        {
            string name = readString();
            int count = readInt();
            var indices = new List<int>(count);
            for (int k = 0; k < count; k++)
            {
                int idx = readInt();
                if ((uint)idx >= (uint)_all.Count)
                    throw new InvalidOperationException($"decal group '{name}' names entry {idx} of {_all.Count}");
                indices.Add(idx);
            }
            _groups[name] = indices;
        }

        int materials = readInt();
        for (int m = 0; m < materials; m++)
        {
            string material = readString();
            _translation[material] = readString();
        }
    }

    /// <summary>
    /// Stock's <c>LevelInitPreEntity</c>: every entry's index from the side's
    /// engine, in file order. Answers how many came back non-negative.
    /// </summary>
    public int Precache(Func<string, int> precache)
    {
        int indexed = 0;

        foreach (var entry in _all)
        {
            entry.PrecacheIndex = precache(entry.Name);

            if (entry.PrecacheIndex >= 0)
                indexed++;
        }

        return indexed;
    }

    /// <summary>
    /// Stock reads the weight with <c>KeyValues::GetFloat</c>, which answers 0
    /// for anything it cannot read; a zero weight is meaningful in the pick.
    /// </summary>
    private static float ParseWeight(string value)
        => float.TryParse(value, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float f)
         ? f
         : 0f;

    /// <summary>
    /// A weighted pick over one group, as the engine's precache index; −1 for
    /// an unknown name or an empty group. Stock's <c>GetDecalIndexForName</c>.
    /// </summary>
    /// <param name="randomFloat">The side's <c>random-&gt;RandomFloat(min, max)</c>.</param>
    public int IndexForName(string? name, Func<float, float, float> randomFloat)
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        if (!_groups.TryGetValue(name, out var indices) || indices.Count == 0)
            return -1;

        // STOCK'S PICK, REPRODUCED EXACTLY (decals.cpp:158-180). A reservoir
        // sample with a first-entry special case:
        //   * `if (!totalweight) slot = idx;` runs BEFORE the weight is added, so
        //     a group whose weights are all zero answers its first entry;
        //   * the draw is SHORT-CIRCUITED by `!totalweight`, so no number is
        //     drawn while the running total is zero. Drawing one anyway would
        //     advance a stream others share.
        float totalWeight = 0f;
        int slot = indices[0];

        foreach (int idx in indices)
        {
            Entry item = _all[idx];

            if (totalWeight == 0f)
                slot = idx;

            totalWeight += item.Weight;

            if (totalWeight == 0f || randomFloat(0f, totalWeight) < item.Weight)
                slot = idx;
        }

        return _all[slot].PrecacheIndex;
    }

    /// <summary>
    /// The first entry precached under <paramref name="index"/>, or null when
    /// none was (stock answers <c>""</c>; the shim turns null back into that).
    /// </summary>
    public string? NameForIndex(int index)
    {
        foreach (var entry in _all)
        {
            if (entry.PrecacheIndex == index)
                return entry.Name;
        }

        return null;
    }

    /// <summary>
    /// Stock's <c>TranslateDecalForGameMaterial</c>: null means "the caller's
    /// own name"; an empty string means "do not decal" ('-').
    /// </summary>
    public string? TranslateForGameMaterial(string decalName, int gameMaterial)
    {
        if (gameMaterial == ConcreteMaterial)
            return null;

        if (!string.Equals(decalName, "Impact.Concrete", StringComparison.OrdinalIgnoreCase))
            return null;

        if (gameMaterial == '-')
            return string.Empty;

        // The key is ONE BYTE, which is what stock builds and looks up.
        string key = ((char)(byte)gameMaterial).ToString();

        return _translation.TryGetValue(key, out string? group) ? group : null;
    }
}
