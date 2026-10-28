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
/// <b>The map load, in managed code</b> — plan_entity.md §3.5 and step 2d.
///
/// <para>
/// "Map spawning is managed end to end: <c>LevelInit</c> hands the entity lump
/// to managed, which parses it, creates entities, applies keyvalues, resolves
/// parents and connections, and calls <c>Spawn</c> and <c>Activate</c> in stock
/// order." That is what this class is. Before it, the lump was walked by
/// <c>MapEntity_ParseAllEntities</c> and the cursor lived in C++ — so every key
/// of every entity had to be handed UP one at a time.
/// </para>
///
/// <para>
/// <b>THE NUMBER THIS CLASS EXISTS TO MOVE.</b> On <c>dm_lockdown</c> a map load
/// cost 7059 entity crossings, of which <c>KeyValue</c> was <b>5709</b> — 72%,
/// and the largest single item in the whole budget. The lump holds 5785 keys and
/// exactly 5709 of them are on an entity whose class the managed registry owns,
/// which is the identity that says the two numbers are the same thing.
/// The keys now go straight to <see cref="ScriptedEntityRegistry.KeyValue"/>
/// from managed memory, so that op reads <b>zero</b>.
/// </para>
///
/// <para>
/// <b>What this is NOT, because two other routes to the same number were
/// refuted and must not creep back in</b> (step 2a, and its evidence):
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Not a per-class <c>KeyValue</c> override bit.</b> The directory's name
/// index — what every output resolves its target against — is fed by the
/// <c>targetname</c> keyvalue and by nothing else, so a class whose
/// <c>KeyValue</c> was elided could never be reached by any connection in any
/// map, silently. 2a's fact <c>TargetnameFeedsTheDirectorysNameIndex</c> pins it.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Not a native key filter.</b> Publishing each class's key set to native so
/// the shell only crosses for a declared key destroys
/// <c>ScriptedEntityRegistry.ReportUnclaimed</c> / <c>IsNearMiss</c>, whose whole
/// job is catching a mapper who typed a key one character away from a declared
/// one — precisely the keys such a filter drops first. <b>Nothing here filters on
/// either side.</b> Every key of every entity reaches managed code, in map order,
/// and every key also goes down to the shell's own arm.
/// </description>
/// </item>
/// </list>
///
/// <para>
/// The route taken is the third one, which §3.5 already scheduled: parse in
/// managed code. It DELETES the per-key crossing rather than adding a native
/// buffer to carry it — the only C++ it adds is
/// <c>SourceSharp_MapSpawnCreate</c>, which creates the shell and walks a packed
/// pair list into <c>CStandaloneEntity::KeyValueShellOnly</c>. That if-chain is
/// not duplicated anywhere: managed code does not know which keys the shell
/// keeps and does not need to.
/// </para>
/// </summary>
public sealed class MapSpawner
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ScriptedEntityRegistry _registry;
    private readonly HierarchyDriver _hierarchy;
    private readonly TemplateStore _templates;
    private readonly IEngineEntities _entities;
    private readonly IEngineLevel _level;
    private readonly IServerMapSpawnOrder _spawnOrder;
    private readonly EntityFactories _factories;

    public MapSpawner(ILog log, ScriptedEntityRegistry registry, HierarchyDriver hierarchy,
                      TemplateStore templates, IEngineEntities entities, IEngineLevel level,
                      IServerMapSpawnOrder spawnOrder, EntityFactories factories)
    {

        _log = log;
        _factories = factories;
        _registry = registry;
        _hierarchy = hierarchy;
        _templates = templates;
        _entities = entities;
        _level = level;
        _spawnOrder = spawnOrder;
    }

    /// <summary>
    /// One map entity this class made: its handle, its class, and the map text
    /// it was made from.
    /// </summary>
    /// <remarks>
    /// The TEXT is kept and not just the keys, because a <c>point_template</c>
    /// claims an entity by copying its block verbatim and re-parsing it later —
    /// stock's <c>MapEntity_RecordPendingNonBaseMapData</c> kept the same bytes
    /// for the same reason.
    /// </remarks>
    public sealed class Spawned
    {
        public required EntityHandle Handle { get; init; }
        public required string ClassName { get; init; }
        public required byte[] MapData { get; init; }
        public required string TargetName { get; init; }
        public required Vector Origin { get; init; }
        public required QAngle Angles { get; init; }

        /// <summary>
        /// Taken by a <c>point_template</c>, so nothing spawns it. Stock's
        /// <c>MapEntity_ClaimPendingNonBaseEntity</c>.
        /// </summary>
        public bool Claimed { get; set; }

        /// <summary>Spawned already — a template spawns inside its own window.</summary>
        public bool HasSpawned { get; set; }
    }

    private readonly List<Spawned> _spawned = new();

    /// <summary>
    /// Open only between the create pass and the spawn pass, which is the window
    /// a <c>point_template</c> claims its sources in. Stock's three
    /// <c>g_pTemplateWindow*</c> statics, as one bool on an instance.
    /// </summary>
    private bool _windowOpen;

    /// <summary>The entities this class made on the current map, in map order.</summary>
    public IReadOnlyList<Spawned> Entities => _spawned;

    // ---- instrumentation, named so it has a removal owner --------------------
    //
    // Same standing as 1g's `status.think` and 2a's `status.io`: these are what
    // this step's crossing claim is READ OUT OF, so a build that stopped spawning
    // and a build that spawns without crossing can be told apart. The performance
    // pass owns dropping them.

    /// <summary>Blocks in the lump on the last load.</summary>
    public int LastBlocks { get; private set; }

    /// <summary>Blocks whose class the managed registry owns.</summary>
    public int LastManagedBlocks { get; private set; }

    /// <summary>Blocks handed to the native parse — the residue, worldspawn included.</summary>
    public int LastNativeBlocks { get; private set; }

    /// <summary>
    /// Keys delivered to <see cref="ScriptedEntityRegistry.KeyValue"/> with NO
    /// crossing. This is the number that used to be <c>crossings.entityOps.KeyValue</c>.
    /// </summary>
    public int LastManagedKeys { get; private set; }

    /// <summary>Outbound crossings spent creating: one per managed entity.</summary>
    public int LastCreateCrossings { get; private set; }

    /// <summary>Outbound crossings spent spawning: one per managed entity.</summary>
    public int LastSpawnCrossings { get; private set; }

    /// <summary>Classnames in the lump the managed registry does not own.</summary>
    public IReadOnlyList<string> LastResidueClasses => _residueClasses;

    /// <summary>
    /// The residue classnames that NOTHING in this build can create — the
    /// subset of <see cref="LastResidueClasses"/> with no native factory either.
    ///
    /// <para>
    /// Empty while the ABI slots <see cref="EntityFactories"/> reads through are
    /// unwired, and empty is then "not established" rather than "none": see that
    /// class for why an unasked table must not answer Nothing.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LastUnansweredClasses { get; private set; } =
        Array.Empty<string>();

    private readonly List<string> _residueClasses = new();

    /// <summary>
    /// <b>The order, and it is the whole of this class's contribution to a map
    /// load.</b> Stock's, with each position argued:
    ///
    /// <list type="number">
    /// <item><description>
    /// <b>The residue first.</b> Blocks whose class this side does not own go to
    /// the native parse as a lump of their own. <c>worldspawn</c> is one of them
    /// and is the map's first block, and everything downstream — the physics
    /// environment, the game rules, a trace — wants a world.
    /// </description></item>
    /// <item><description>
    /// <b>Create and key, in map order, before anything spawns.</b> Stock parses
    /// every entity before it spawns any, and the reason is the one a mapper
    /// relies on: an entity that refers to another by name wants the other to
    /// exist.
    /// </description></item>
    /// <item><description>
    /// <b>The templates, inside the window.</b> A <c>point_template</c> consumes
    /// the entities it names and they must not have spawned first. Each spawns
    /// AND finishes claiming before the next one starts, because two templates
    /// naming one entity is a first-come rule and interleaving would make it a
    /// race — stock's <c>MapEntity_SpawnMapTemplates</c>, same order.
    /// </description></item>
    /// <item><description>
    /// <b>Then everything else, in map order.</b>
    /// </description></item>
    /// <item><description>
    /// <b>Parents last.</b> A <c>parentname</c> is known from parse time, but the
    /// offset a child keeps is measured from where the two of them ENDED UP — and
    /// an entity does not have that until its own <c>Spawn</c> has run. Stock's
    /// third pass, and it has to be a third one.
    /// </description></item>
    /// </list>
    /// </summary>
    public void SpawnMap(ReadOnlySpan<byte> lump)
    {
        // COPIED ONCE, HERE. MapLoad.Entities is a borrowed span on a ref struct
        // — it is valid for exactly this call — and every block below is a slice
        // of it. A copy is the only way to hold one past the parse, which the
        // template window does.
        SpawnMapText(lump.ToArray());
    }

    /// <summary>
    /// <see cref="SpawnMap"/> over a lump this side already owns. The seam a
    /// test stands on: everything above it is the span copy.
    /// </summary>
    public void SpawnMapText(byte[] lump)
    {
        Reset();

        var blocks = MapEntityBlock.SplitBlocks(lump);
        LastBlocks = blocks.Count;

        // ---- 1. THE RESIDUE, TO THE NATIVE PARSE ---------------------------
        var residue = new List<MapEntityBlock.Block>();
        var mine = new List<(MapEntityBlock.Block Block, string ClassName)>();

        foreach (var block in blocks)
        {
            // ExtractValue, and case-SENSITIVE, because that is what
            // MapEntity_ParseEntity does: `entData.ExtractValue("classname", …)`
            // is a strcmp. A block with no classname is stock's Error(); it is a
            // corrupt lump rather than something to spawn, and it is reported and
            // skipped rather than taking the load down.
            if (!MapEntityBlock.ExtractValue(block.Bytes, "classname", out string className)
                || className.Length == 0)
            {
                _log.Warning("[SourceSharp] map spawn: a block has no classname; skipped\n");
                continue;
            }

            if (_registry.IsRegistered(className))
                mine.Add((block, className));
            else
                residue.Add(block);
        }

        LastManagedBlocks = mine.Count;
        LastNativeBlocks = residue.Count;

        foreach (var block in residue)
        {
            if (MapEntityBlock.ExtractValue(block.Bytes, "classname", out string name))
                _residueClasses.Add(name);
        }

        SpawnWorld(residue);

        // ---- 1b. AND SAY WHAT THE NATIVE PARSE COULD NOT MAKE --------------
        //
        // The residue is not "the rest of the map"; it is the blocks THIS side
        // does not own, and some of them nothing owns. On dm_lockdown that is
        // one classname over one block -- info_node, the map's AI node graph --
        // because its definer left server_csharp.vpc at some earlier step. (The
        // map's ladders, func_useableladder and info_ladder_dismount, were two
        // more until they became managed entities: FuncUseableLadder and
        // InfoLadderDismount.) p4-flip found that by grepping a console log no
        // gate reads, and plan_entity.md §7 asks for a DECLARED, PRINTED refusal
        // rather than a silent no-op.
        //
        // AFTER SpawnWorld, not before, because the native dictionary's
        // refusal ledger is written BY that call: a classname is only known to
        // answer to nothing once something has asked for it.
        //
        // Silent on a build whose ABI slots are unbound -- which was every build
        // before ABI 242, and is still the client. EntityFactories answers
        // Native, not Nothing, for every unknown name while it cannot ask, so
        // such a build prints nothing rather than declaring a refusal it has not
        // established. THAT IS ALSO WHY THE LINE APPEARING IS EVIDENCE: it
        // cannot be printed by a table that was not really pulled.
        // ONE PULL PER MAP LOAD, here and nowhere else. The table is a few
        // hundred strings and the refusal ledger three; the cost is two
        // crossings plus one per name, once, against 605 blocks. It is not on
        // any per-entity or per-frame path and must not become one.
        _factories.Refresh();

        LastUnansweredClasses = _factories
            .SplitResidue(_residueClasses, _registry.IsRegistered).Unanswered;

        string? declaration = EntityFactories.DeclareUnanswered(
            LastUnansweredClasses, residue.Count);

        if (declaration is not null)
            _log.Warning(declaration);

        // ---- 2. CREATE AND KEY, IN MAP ORDER -------------------------------
        foreach (var (block, className) in mine)
            Create(block, className);

        // ---- 3. THE TEMPLATE WINDOW ----------------------------------------
        //
        // OPENED AND SHUT AROUND THE TEMPLATES AND NOTHING ELSE, which is the
        // whole meaning of "window": outside it Capture answers nothing, and
        // that is the answer stock gives a point_template made by ent_create.
        _windowOpen = true;
        _templates.MapWindow = Capture;

        // Collected before any of them spawns: spawning claims entities out of
        // the list, and walking it while it changes underneath would skip
        // entries — and the entries being skipped would be other templates.
        var templates = new List<Spawned>();

        foreach (var each in _spawned)
        {
            if (string.Equals(each.ClassName, EntityNames.PointTemplate,
                              StringComparison.OrdinalIgnoreCase))
            {
                templates.Add(each);
            }
        }

        foreach (var each in templates)
        {
            each.Claimed = true;
            Spawn(each);
        }

        _windowOpen = false;
        _templates.MapWindow = null;

        // ---- 4. EVERYTHING ELSE, IN STOCK'S SPAWN-PRIORITY ORDER -----------
        //
        // THIS WAS "IN MAP ORDER" AND STOCK'S RULE HAD GONE QUIET -- 5e's shape,
        // in the map-load path. SortSpawnListByHierarchy builds a thirteen-
        // classname CStringRegistry and CompareSpawnOrder breaks its depth ties
        // with it; eleven of those thirteen are managed entities now, and a
        // managed entity is not in the list that tie-break sorts. It is here,
        // and here it was spawned in the order the mapper happened to place it.
        //
        // Measured from the BSP entity lumps, as pairs where an entity stock
        // spawns LATER appears earlier in the file:
        //
        //     dm_lockdown  605 blocks, 100 prop_physics (7) + phys_hinge and
        //                  phys_lengthconstraint (8)          -- 189 inverted
        //     ss_sandbox  1005 blocks,  49 prop_physics + 9 constraint-family
        //                  + 1 func_wall (10)                 -- 183 inverted
        //
        // THE SORT IS STABLE, so entities of equal priority -- which on a real
        // map is nearly all of them -- keep map order exactly as they did.
        // That is also stock's rule: CompareSpawnOrder answers 0 for an equal
        // pair and qsort leaves it where it was.
        //
        // NOT MOVED into the CREATE pass above. Stock parses every block before
        // it spawns any, and a mapper relies on that: an entity that refers to
        // another by name wants the other to exist. Only the SPAWN pass is
        // ordered, which is the pass stock orders.
        var pending = new List<Spawned>();
        foreach (var each in _spawned)
        {
            if (!each.Claimed)
                pending.Add(each);
        }

        var names = new string?[pending.Count];
        for (int i = 0; i < pending.Count; i++)
            names[i] = pending[i].ClassName;

        var order = new int[pending.Count];
        int written = _spawnOrder.Order(names, order);

        if (written == pending.Count)
        {
            for (int i = 0; i < written; i++)
                Spawn(pending[order[i]]);
        }
        else
        {
            // MAP ORDER, WHICH IS WHAT THIS PASS DID BEFORE THE RULE CAME BACK.
            // An entity spawned in the wrong order is a bug; an entity not
            // spawned at all is a missing entity, and that asymmetry decides
            // which way a refusal falls.
            _log.Warning($"[SourceSharp] map spawn: the spawn order answered {written} "
                        + $"of {pending.Count} entities; spawning in map order\n");

            foreach (var each in pending)
                Spawn(each);
        }

        // ---- 5. PARENTS ----------------------------------------------------
        //
        // THROUGH HierarchyDriver AND NOT A NATIVE CALL: step 2h-live made
        // parenting live in managed code and deleted the native policy, so the
        // deferred names this pass resolves were recorded managed-side too — the
        // shell's `parentname` arm forwards to HierarchySetParentName.
        _hierarchy.ResolveParentNames();
    }

    /// <summary>
    /// Creates one map entity and gives both sides its keys.
    ///
    /// <para>
    /// <b>The shell's keys go down together and the managed keys never go
    /// anywhere.</b> One crossing carries every key to
    /// <c>CStandaloneEntity::KeyValueShellOnly</c>; the same keys then go to
    /// <see cref="ScriptedEntityRegistry.KeyValue"/> in managed memory, in map
    /// order, at no cost at all.
    /// </para>
    ///
    /// <para>
    /// <b>A stated divergence: the shell's keys are all applied before any
    /// managed key is.</b> Native's <c>KeyValue</c> did the two arms per key, so
    /// they interleaved in map order. Nothing is lost — every key still reaches
    /// both sides, in map order within each side — and a managed
    /// <c>OnKeyValue</c> that reads a shell value now sees the FINAL one rather
    /// than a partly-applied one, which is more information and not less. It is
    /// written down because it is a real difference and not because anything is
    /// known to depend on it.
    /// </para>
    /// </summary>
    private void Create(MapEntityBlock.Block block, string className)
    {
        var keys = ReadKeys(block.Bytes, _log);

        var handle = _entities.CreateFromMap(className, Pack(keys));
        LastCreateCrossings++;

        // COMPARED AGAINST None RATHER THAN ASKED IsValid, and the difference is
        // a crossing per entity: EntityHandle.IsValid calls Engine.EntityIsValid,
        // which is exactly the kind of per-entity question this step exists to
        // stop asking. None is what the native side answers with
        // (INVALID_EHANDLE_INDEX) and a value compare is the whole test.
        //
        // (default(EntityHandle) is None too since 2026-10-05; the raw handle
        // 0 is entry ZERO, a REAL slot, and two lanes measured the wrong path
        // by treating it as nothing.)
        if (handle == EntityHandle.None)
        {
            _log.Warning($"[SourceSharp] map spawn: '{className}' could not be created\n");
            return;
        }

        string targetName = string.Empty;
        Vector origin = default;
        QAngle angles = default;

        foreach (var (key, value) in keys)
        {
            _registry.KeyValue(handle, className, key, value);
            LastManagedKeys++;

            if (string.Equals(key, "targetname", StringComparison.OrdinalIgnoreCase))
                targetName = value;
            else if (string.Equals(key, "origin", StringComparison.OrdinalIgnoreCase))
                origin = ParseVector(value);
            else if (string.Equals(key, "angles", StringComparison.OrdinalIgnoreCase))
                angles = ParseAngles(value);
        }

        _spawned.Add(new Spawned
        {
            Handle = handle,
            ClassName = className,
            MapData = block.Bytes,
            TargetName = targetName,
            Origin = origin,
            Angles = angles,
        });
    }

    private void Spawn(Spawned entity)
    {
        if (entity.HasSpawned)
            return;

        entity.HasSpawned = true;
        _entities.Spawn(entity.Handle);
        LastSpawnCrossings++;
    }

    /// <summary>
    /// <b>The template window, managed.</b> Stock's
    /// <c>MapEntity_TemplateCapture</c>: turns every entity of a name into a
    /// template and, if the template says so, consumes it.
    ///
    /// <para>
    /// Answers an empty list when the window is shut, which is what a
    /// <c>point_template</c> made at runtime by <c>ent_create</c> sees — stock's
    /// answer, from stock's reason: the map text a template is made of only
    /// exists between the parse and the spawn.
    /// </para>
    ///
    /// <para>
    /// <b>A stated divergence: the sources come back in MAP ORDER.</b> Stock
    /// walked its pending list BACKWARDS, because claiming removed from the list
    /// and a forward walk over a shrinking list skips entries — so a map's
    /// sources were captured in reverse. Nothing is claimed out of a list here
    /// (the entry is marked), so there is no such constraint, and reproducing an
    /// artefact of a list implementation was not worth it. Same call as 2a made
    /// about connection ordering, for the same reason.
    /// </para>
    ///
    /// <para>
    /// <b>What it cannot find: a CBaseEntity.</b> Stock swept both hierarchies.
    /// The blocks this class owns are the ones the managed registry owns, so a
    /// template naming one of the residue's classes finds nothing. On the two
    /// maps this port loads the residue is <c>worldspawn</c> and three ladder
    /// classes, none of which a mapper templates; it is a real limitation and it
    /// closes when the residue does.
    /// </para>
    /// </summary>
    public List<TemplatePlacement> Capture(string sourceName, bool removeSources, int limit)
    {
        var found = new List<TemplatePlacement>();

        if (!_windowOpen || string.IsNullOrEmpty(sourceName))
            return found;

        foreach (var each in _spawned)
        {
            // Case-INSENSITIVE, which is stock's Q_stricmp and the same rule the
            // template store matches targetnames with.
            if (each.Claimed || !string.Equals(each.TargetName, sourceName,
                                               StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int index = _templates.Add(each.TargetName, each.ClassName, each.MapData);
            if (index == -1)
                continue;

            // The COUNT keeps rising past the caller's buffer, which is stock's:
            // a caller with too small a buffer is told it missed some rather than
            // believing the template was small. Here the list simply stops
            // growing at the limit and the loop carries on claiming.
            if (found.Count < limit)
                found.Add(new TemplatePlacement(index, each.Origin, each.Angles));

            if (removeSources)
            {
                // Claimed so nothing spawns it, and REMOVED so it does not show
                // up in a find-by-name — stock does both, and doing only the
                // first would leave a live entity the template has already eaten.
                each.Claimed = true;
                _entities.Remove(each.Handle);
            }
        }

        return found;
    }

    /// <summary>
    /// <c>CServerGameDLL::ServerActivate</c>'s walk, for the entities this class
    /// made — plan_entity.md §3.5, "calls <c>Spawn</c> and <c>Activate</c> in
    /// stock order".
    ///
    /// <para>
    /// In spawn order, and after the native walk rather than instead of it: the
    /// two hierarchies are activated in the order their entities were made, which
    /// is the order stock's single <c>gEntList</c> walk gave them.
    /// </para>
    ///
    /// <para>
    /// A claimed source is NOT activated. It was eaten by a template and never
    /// spawned; activating it would run the second half of a lifecycle whose
    /// first half never happened.
    /// </para>
    /// </summary>
    public int ActivateAll()
    {
        int activated = 0;

        foreach (var each in _spawned)
        {
            if (each.Claimed || !each.HasSpawned)
                continue;

            if (_registry.Activate(each.Handle))
                activated++;
        }

        return activated;
    }

    /// <summary>Between maps. Every entity here belongs to the map that is going.</summary>
    public void Clear() => Reset();

    private void Reset()
    {
        _templates.MapWindow = null;
        _spawned.Clear();
        _residueClasses.Clear();
        LastUnansweredClasses = Array.Empty<string>();
        _windowOpen = false;
        LastBlocks = 0;
        LastManagedBlocks = 0;
        LastNativeBlocks = 0;
        LastManagedKeys = 0;
        LastCreateCrossings = 0;
        LastSpawnCrossings = 0;
    }

    /// <summary>
    /// Every key of one block, in map order — stock's
    /// <c>GetFirstKey</c>/<c>GetNextKey</c> walk, which is the same walk
    /// <c>MapEntity_ParseNonBaseEntity</c> made when this was native.
    /// </summary>
    internal static List<(string Key, string Value)> ReadKeys(byte[] block, ILog log)
    {
        var keys = new List<(string, string)>();
        var cursor = new MapEntityBlock(block, log);

        if (!cursor.GetFirstKey(out string key, out string value))
            return keys;

        do
        {
            keys.Add((key, value));
        }
        while (cursor.GetNextKey(out key, out value));

        return keys;
    }

    /// <summary>
    /// <c>key\0value\0key\0value\0…</c> — what the one crossing carries.
    ///
    /// <para>
    /// A NUL cannot appear in map text (the lump is a C string in the BSP), so
    /// the separator cannot collide with a value; the byte count on the far side
    /// is what ends the walk, so a trailing NUL is not needed and a value that
    /// ends the payload is not special.
    /// </para>
    /// </summary>
    internal static byte[] Pack(IReadOnlyList<(string Key, string Value)> keys)
    {
        var buffer = new List<byte>(256);

        foreach (var (key, value) in keys)
        {
            buffer.AddRange(System.Text.Encoding.UTF8.GetBytes(key));
            buffer.Add(0);
            buffer.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            buffer.Add(0);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The world, from the residue's <c>worldspawn</c> block — plan_server_novalve.md
    /// P5. Native used to be handed the residue as text and walk it for this one
    /// block; finding the block is a decision about the text, so it is made here
    /// over the blocks already split, and native is given the pairs.
    ///
    /// <para>
    /// The FIRST worldspawn block, as the native walk stopped at the first: there
    /// is one per map and it is the map's first block. Every pair goes, in block
    /// order and <c>classname</c> included, because that is what
    /// <c>CEntityMapData</c>'s walk handed <c>CWorld::KeyValue</c>.
    /// </para>
    ///
    /// <para>
    /// A residue with no world is a level this build cannot run, and without
    /// this warning the failure is a SIGSEGV inside the engine two hundred lines
    /// of console later; so it is said here, where it is still cheap to read.
    /// </para>
    /// </summary>
    private void SpawnWorld(IReadOnlyList<MapEntityBlock.Block> residue)
    {
        foreach (var block in residue)
        {
            if (!MapEntityBlock.ExtractValue(block.Bytes, "classname", out string name)
                || name != "worldspawn")
                continue;

            var keys = new List<KeyValuePair<string, string>>();
            var entData = new MapEntityBlock(block.Bytes, _log);
            if (entData.GetFirstKey(out string key, out string value))
            {
                do
                {
                    keys.Add(new(key, value));
                }
                while (entData.GetNextKey(out key, out value));
            }

            if (!_level.SpawnWorld(keys))
            {
                _log.Warning("[SourceSharp] the map's worldspawn block did not build a world. "
                          + "This level has NO WORLD: nothing precaches, and the first trace "
                          + "faults inside the engine.\n");
            }

            return;
        }

        _log.Warning("[SourceSharp] the map's entity residue carried no worldspawn. "
                  + "This level has NO WORLD: nothing precaches, and the first trace "
                  + "faults inside the engine.\n");
    }

    /// <summary>
    /// Three floats, or the origin. <c>sscanf( "%f %f %f" )</c>'s rule: all three
    /// or nothing, which is what the shell's own arm does with the same key.
    /// </summary>
    internal static Vector ParseVector(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
            return default;

        return new Vector(Number(parts[0]), Number(parts[1]), Number(parts[2]));
    }

    internal static QAngle ParseAngles(string value)
    {
        var v = ParseVector(value);
        return new QAngle(v.X, v.Y, v.Z);
    }

    private static float Number(string text)
        => float.TryParse(text, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float f)
            ? f
            : 0f;
}
