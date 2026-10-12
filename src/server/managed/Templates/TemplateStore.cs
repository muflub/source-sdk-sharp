//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The template store: the keyvalue text of every entity a
/// <see cref="PointTemplate"/> has claimed, and the entity-I/O rewriting that
/// makes a copy of a group wire up to itself instead of to the originals.
///
/// <para>
/// This is <c>game/server/TemplateEntities.cpp</c>, ported. The argument for
/// its being native ran "a template is map-parse machinery" — and that was
/// true of two things only: producing the text (the parse hands it over) and
/// turning the text back into an entity (the parser). Everything in between —
/// a list, a case-insensitive name match, a counter, and a string substitution
/// — is arithmetic over bytes, and it is here.
/// </para>
///
/// <para>
/// <b>Not static, and not a singleton.</b> Stock kept <c>g_Templates</c> and
/// <c>g_iCurrentTemplateInstance</c> at file scope, which is what made every
/// one of its functions a free function and what makes stock's version
/// untestable without a map. One instance is registered for the game; a test
/// makes its own and nothing leaks between them.
/// </para>
/// </summary>
public sealed class TemplateStore(ILog log) : IServerTemplates, IGameLifecycle
{
    /// <inheritdoc/>
    public int LifecycleOrder => (int)LifecycleStage.Templates;

    /// <summary>
    /// <c>CTemplatesHook::LevelInitPreEntity</c>, and stock's reason for its
    /// existing at all: a level that shut down between a schedule and its drain
    /// — a load cancelled, a map changed twice — would otherwise carry indices
    /// into a store that has been emptied into the next level.
    ///
    /// <para>
    /// It clears the WHOLE store rather than only the pending list, which stock
    /// did not, and the difference cannot show: the pass that fills the store is
    /// the map parse, which has not run yet at this stage, and the shutdown
    /// below has already emptied it.
    /// </para>
    /// </summary>
    public void OnLevelInitPreEntity(string mapName) => RemoveAll();

    /// <summary><c>CTemplatesHook::LevelShutdownPostEntity</c>.</summary>
    public void OnLevelShutdownPostEntity() => RemoveAll();

    /// <summary>
    /// Appended to a value that has to be unique per instance of a template.
    ///
    /// The leading <c>&amp;</c> is the marker the substitution pass searches
    /// for; the four digits are the instance number. Both halves matter: the
    /// pass will only stamp over a <c>&amp;</c> followed by exactly four
    /// digits, so a map value that happens to contain an ampersand is left
    /// alone.
    /// </summary>
    public const string FixupString = "&0000";

    /// <summary>
    /// <c>VMF_IOPARAM_STRING_DELIMITER</c> from <c>public/fgdlib/entitydefs.h</c>
    /// — ESC, chosen so an I/O parameter can itself contain a comma.
    /// </summary>
    public const char IoParamDelimiter = '\x1b';

    /// <summary>One stored template.</summary>
    private sealed class Entry
    {
        public required string Name { get; init; }

        /// <summary>
        /// The map text, with room to grow.
        ///
        /// Stock allocated <c>nLen + (len(FixupString) + 1) * keys + 1</c> and
        /// then rewrote values in place; the size is kept because
        /// <see cref="MapEntityBlock.SetValue"/> refuses a replacement that
        /// would not fit, and a template whose room was mis-sized should fail
        /// visibly rather than silently drop a connection.
        /// </summary>
        public required MapEntityBlock MapData { get; init; }

        /// <summary>The allocation stock made. Reported by
        /// <see cref="StringSize"/>, which the spawn path passes to the native
        /// parser as its buffer size.</summary>
        public required int MapDataLength { get; init; }

        /// <summary>Whether this template's stored text carries fixup markers
        /// that have to be stamped with an instance number before it is used.</summary>
        public bool NeedsEntityIoFixup { get; set; }

        /// <summary>The one working copy the substitution pass stamps over,
        /// made on first use. Stock's <c>pszFixedMapData</c>.</summary>
        public byte[]? FixedMapData { get; set; }
    }

    private readonly List<Entry> _templates = new();

    /// <summary>
    /// The pending precache list.
    ///
    /// Stock kept this on the ENTITY — a game system holding
    /// <c>CHandle&lt;CPointTemplate&gt;</c> and calling <c>PerformPrecache()</c>
    /// on each. The deferral itself is stock's and stays: precaching a template
    /// means constructing an entity out of its map data, and the caller is
    /// standing inside the map parse that is constructing them.
    /// </summary>
    private readonly List<(int Index, bool AllowNameFixup)> _pendingPrecache = new();

    /// <summary>Stock's <c>g_iCurrentTemplateInstance</c>.</summary>
    private int _instance;

    /// <summary>How many templates are stored.</summary>
    public int Count => _templates.Count;

    /// <summary>How many templates are waiting to be precached.</summary>
    public int PendingPrecacheCount => _pendingPrecache.Count;

    /// <summary>The instance number the next stamped copy will carry.</summary>
    public int CurrentInstance => _instance;

    //-------------------------------------------------------------------------
    // THE MAP WINDOW -- plan_entity.md step 2d.
    //
    // A point_template claims its sources between the map's entities being
    // created and being spawned. That window used to be native
    // (MapEntity_TemplateCapture, over the parse's own local arrays); it is
    // MapSpawner's now, because MapSpawner is what holds the map text.
    //
    // A DELEGATE AND NOT A REFERENCE TO MapSpawner, so this file does not
    // depend on the spawner and a test can open a window of its own with two
    // lines. Null means "no map is being spawned", which is what a template made
    // at runtime by ent_create sees -- and on that path Capture falls through to
    // the engine, which is exactly what it did before this existed.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Who answers a template capture while a map is being spawned, or null
    /// when no map is.
    /// </summary>
    public Func<string, bool, int, List<TemplatePlacement>>? MapWindow { get; set; }

    /// <summary>
    /// Turns every entity of a name into a template: the map window if one is
    /// open, and the engine otherwise.
    ///
    /// <para>
    /// The fall-through is not a fallback for a broken window — it is the
    /// runtime path. <c>ent_create point_template</c> makes a template with no
    /// map being parsed, and stock's answer there is "nothing", which is what
    /// the engine's own <c>TemplateCapture</c> gives.
    /// </para>
    /// </summary>
    public List<TemplatePlacement> Capture(IEngineScripted scripted, string sourceName,
                                           bool removeSources, int limit = 64)
        => MapWindow is { } window
            ? window(sourceName, removeSources, limit)
            : scripted.TemplateCapture(sourceName, removeSources, limit);

    /// <summary>
    /// Stores one entity's map text as a template and answers its index.
    ///
    /// <para>
    /// Called once per source claimed, from inside the map parse. Refuses an
    /// entity with no name — a template is looked up by targetname and one
    /// without a name could never be found — which is stock's <c>-1</c>.
    /// </para>
    /// </summary>
    public int Add(string? name, string? className, byte[] mapData)
    {
        if (string.IsNullOrEmpty(name))
        {
            log.Warning("[SourceSharp] template entity with no name, class " +
                        $"{className ?? "?"}\n");
            return -1;
        }

        ArgumentNullException.ThrowIfNull(mapData);

        // Every key in the text may grow by the fixup suffix, so the copy is
        // allocated with room for all of them to. Stock's `iExtraSpace`.
        int keys = MapEntityBlock.CountKeys(mapData);
        int length = mapData.Length + (FixupString.Length + 1) * keys + 1;

        var storage = new byte[length];
        Array.Copy(mapData, storage, mapData.Length);

        _templates.Add(new Entry
        {
            Name = name,
            MapData = new MapEntityBlock(storage, log, length),
            MapDataLength = length,
        });

        return _templates.Count - 1;
    }

    /// <summary>Whether this template's text has to be stamped with an instance
    /// number before it is parsed. Stock's
    /// <c>Templates_IndexRequiresEntityIOFixup</c>.</summary>
    public bool RequiresEntityIoFixup(int index) =>
        Valid(index) && _templates[index].NeedsEntityIoFixup;

    /// <summary>The stored text for a template. Stock's
    /// <c>Templates_FindByIndex</c>, without the pooled string — the caller
    /// hands these bytes straight to the native parser.</summary>
    public byte[] MapData(int index) =>
        Valid(index) ? _templates[index].MapData.Bytes : Array.Empty<byte>();

    /// <summary>The size of the buffer the text lives in. Stock's
    /// <c>Templates_GetStringSize</c>, and what the native parser is told the
    /// block is allowed to occupy.</summary>
    public int StringSize(int index) =>
        Valid(index) ? _templates[index].MapDataLength : 0;

    /// <summary>
    /// The first template with this targetname, case-insensitively. Stock's
    /// <c>Templates_FindByTargetName</c>.
    ///
    /// <para>
    /// Kept even though nothing in this build calls it: its two stock callers,
    /// <c>monstermaker.cpp</c> and <c>npc_combinedropship.cpp</c>, are out of
    /// the build with the HL2 NPCs and are the first thing an NPC port would
    /// want back. Its stock comment is worth carrying with it — <i>this can't
    /// handle multiple templates with the same targetname</i>, which is
    /// precisely why point_template uses indices instead.
    /// </para>
    /// </summary>
    public byte[] FindByTargetName(string name)
    {
        for (int i = 0; i < _templates.Count; i++)
        {
            if (string.Equals(_templates[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return MapData(i);
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Rewrites the entity I/O between a group's templates so that each
    /// instance wires up to its own copies rather than to the originals.
    ///
    /// <para>
    /// Stock's <c>Templates_ReconnectIOForGroup</c>. Without it, a group whose
    /// entities are wired to each other spawns copies still pointing at the
    /// sources — which have just been eaten — so the copies do nothing.
    /// </para>
    ///
    /// <para>
    /// The rule: for every key of every template in the group whose value names
    /// another template in the same group, append the fixup marker to that
    /// name, and mark BOTH templates as needing the stamp. A value may be an
    /// entity-I/O connection, in which case it reads
    /// <c>Target,Input,Parameter,Delay,Times</c> and only the leading target is
    /// a name — so the marker is inserted before the delimiter rather than
    /// appended to the whole thing.
    /// </para>
    /// </summary>
    public void ReconnectIoForGroup(IReadOnlyList<int> indices, bool allowNameFixup)
    {
        if (indices is null || indices.Count == 0)
            return;

        // The ORIGINAL targetnames, taken before anything is changed: the pass
        // renames as it goes, and matching against a name it has already
        // rewritten would append the marker twice.
        int count = indices.Count;
        var group = new GroupMember[count];

        for (int i = 0; i < count; i++)
        {
            if (!Valid(indices[i]))
                return;

            MapEntityBlock block = _templates[indices[i]].MapData;
            block.ExtractValue("targetname", out string targetName);
            group[i] = new GroupMember(indices[i], block, targetName);
        }

        if (!allowNameFixup)
            return;

        for (int i = 0; i < count; i++)
        {
            // Which instance of each key this is. A logic_relay has several
            // OnTrigger keys and they are told apart by position, not by name.
            //
            // CASE-INSENSITIVE, because stock's CUtlDict defaults to
            // k_eDictCompareTypeCaseInsensitive — and deliberately not the same
            // rule as the SetValue that consumes the count, which is strcmp.
            // Both halves are stock's and they disagree; reproduced.
            var keyInstances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            MapEntityBlock mapData = group[i].MapData;

            if (!mapData.GetFirstKey(out string keyName, out string value))
                continue;

            do
            {
                // A targetname is what everything else points AT; renaming it
                // here would be doing the last loop's job in the middle of this
                // one.
                if (string.Equals(keyName, "targetname", StringComparison.OrdinalIgnoreCase))
                    continue;

                keyInstances.TryGetValue(keyName, out int seen);
                keyInstances[keyName] = seen + 1;

                // An entity-I/O value is "Targetname<delim><data>", so the name
                // is whatever precedes the first delimiter. FIXME from stock,
                // kept because it is still true: any key whose value contains a
                // comma and is NOT entity I/O will not be found.
                char delimiter = value.Contains(IoParamDelimiter)
                    ? IoParamDelimiter
                    : ',';

                int split = value.IndexOf(delimiter);
                string named = split >= 0 ? value[..split] : value;

                for (int other = 0; other < count; other++)
                {
                    if (!string.Equals(group[other].Name, named,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    // -1 because the count above includes the one being changed.
                    int keyInstance = keyInstances[keyName] - 1;

                    string rewritten = split >= 0
                        ? named + FixupString + value[split..]
                        : named + FixupString;

                    mapData.SetValue(keyName, rewritten, keyInstance);

                    group[other].NeedsRename = true;

                    // BOTH ends are marked: the one holding the connection,
                    // because its text now carries a marker to stamp, and the
                    // one being pointed at, because its targetname is about to.
                    _templates[group[i].Index].NeedsEntityIoFixup = true;
                    _templates[group[other].Index].NeedsEntityIoFixup = true;
                }
            }
            while (mapData.GetNextKey(out keyName, out value));
        }

        // Only now, once nothing is matching against them any more.
        for (int i = 0; i < count; i++)
        {
            if (!group[i].NeedsRename)
                continue;

            MapEntityBlock mapData = group[i].MapData;
            mapData.ExtractValue("targetname", out string targetName);
            mapData.SetValue("targetname", targetName + FixupString);
        }
    }

    private sealed class GroupMember
    {
        public GroupMember(int index, MapEntityBlock mapData, string name)
        {
            Index = index;
            MapData = mapData;
            Name = name;
        }

        public int Index { get; }
        public MapEntityBlock MapData { get; }

        /// <summary>The targetname as it stood BEFORE this pass ran.</summary>
        public string Name { get; }

        public bool NeedsRename { get; set; }
    }

    /// <summary>
    /// Someone is about to instance a new group: burn an instance number so the
    /// copies wire up to each other and not to the previous group's copies.
    ///
    /// <para>
    /// Wraps at the number of digits the marker holds. Stock asserted and
    /// wrapped to 0 here with the comment <i>we won't hit this</i>; it wraps
    /// silently, because ten thousand template instances in one level is a map
    /// doing something unusual rather than a program in trouble, and an assert
    /// halts a debug server.
    /// </para>
    /// </summary>
    public void StartUniqueInstance()
    {
        _instance++;

        // -1 for the '&'.
        int max = (int)Math.Pow(10, FixupString.Length - 1);
        if (_instance >= max)
            _instance = 0;
    }

    /// <summary>
    /// The stored text with every fixup marker stamped with the current
    /// instance number. Stock's <c>Templates_GetEntityIOFixedMapData</c>.
    ///
    /// <para>
    /// The working copy is made once and stamped over every time, which is
    /// stock's arrangement and load-bearing: the marker and the stamp are the
    /// same length, so the copy can be rewritten in place for ever and the
    /// offsets never move.
    /// </para>
    /// </summary>
    public byte[] EntityIoFixedMapData(int index)
    {
        if (!Valid(index))
            return Array.Empty<byte>();

        Entry entry = _templates[index];

        if (entry.FixedMapData is null)
            entry.FixedMapData = entry.MapData.Bytes;

        byte[] data = entry.FixedMapData;
        byte[] stamp = Encoding.ASCII.GetBytes(
            $"{FixupString[0]}{_instance:D4}");

        for (int i = 0; i + stamp.Length <= data.Length; i++)
        {
            if (data[i] != FixupString[0])
                continue;

            // Only a marker: an ampersand followed by exactly the right number
            // of digits. Anything else in the map text is left alone.
            bool marker = true;
            for (int d = 1; d < stamp.Length; d++)
            {
                if (data[i + d] < (byte)'0' || data[i + d] > (byte)'9')
                {
                    marker = false;
                    break;
                }
            }

            if (!marker)
                continue;

            Array.Copy(stamp, 0, data, i, stamp.Length);

            // Stock advances past the stamp AND ONE MORE — `c += iFixupSize`
            // followed by the loop's own `c++`. So two markers written back to
            // back would leave the second unstamped. Reproduced: this pass and
            // the native one it replaced have to answer the same bytes, and
            // "the C# one is better here" is a divergence nobody asked for.
            i += stamp.Length;
        }

        return data;
    }

    /// <summary>Asks for these templates' contents to be precached once the
    /// level is ready for it. Stock's <c>Templates_SchedulePrecache</c>.</summary>
    public void SchedulePrecache(IReadOnlyList<int> indices, bool allowNameFixup)
    {
        if (indices is null)
            return;

        foreach (int index in indices)
            _pendingPrecache.Add((index, allowNameFixup));
    }

    /// <summary>
    /// What the level's precache step should hand to the parser, in the order
    /// it was scheduled, and empties the list.
    ///
    /// <para>
    /// An instance number is burned first, and that is stock's — the entities
    /// this pass builds are REAL for as long as it takes to precache them.
    /// Sharing an instance number with the first thing actually spawned would
    /// give two live entities the same fixed-up targetname, and the only
    /// symptom would be an output reaching one of them.
    /// </para>
    /// </summary>
    public List<TemplateSpawn> DrainPendingPrecaches()
    {
        var drained = new List<TemplateSpawn>(_pendingPrecache.Count);

        if (_pendingPrecache.Count == 0)
            return drained;

        StartUniqueInstance();

        foreach ((int index, bool allowNameFixup) in _pendingPrecache)
        {
            // The transform is the world origin and no facing, and that is not a
            // stand-in: MapEntity_PrecacheEntity builds the entity, precaches it
            // and removes it again without ever placing it. Stock passed no
            // position here either.
            drained.Add(new TemplateSpawn(InstanceMapData(index, allowNameFixup),
                                          StringSize(index),
                                          new Vector(0f, 0f, 0f),
                                          new QAngle(0f, 0f, 0f)));
        }

        _pendingPrecache.Clear();
        return drained;
    }

    /// <summary>
    /// The text one instance of a template should be parsed from — which copy
    /// depends on whether its group was rewritten, exactly as the precache
    /// drain decides it.
    /// </summary>
    public byte[] InstanceMapData(int index, bool allowNameFixup) =>
        allowNameFixup && RequiresEntityIoFixup(index)
            ? EntityIoFixedMapData(index)
            : MapData(index);

    /// <summary>
    /// Drops every template. Called on level shutdown, and on level init as
    /// well — a level that shut down between a schedule and its drain (a load
    /// cancelled, a map changed twice) would otherwise carry indices into a
    /// store that has been emptied into the next level.
    /// </summary>
    public void RemoveAll()
    {
        _templates.Clear();
        _pendingPrecache.Clear();
    }

    /// <summary>Empties the pending list without touching the store.</summary>
    public void ClearPendingPrecaches() => _pendingPrecache.Clear();

    private bool Valid(int index) => index >= 0 && index < _templates.Count;
}
