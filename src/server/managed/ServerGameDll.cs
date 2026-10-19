//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.IO;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The game DLL's managed answer: one class implementing
/// <see cref="IServerLifetime"/>, <see cref="IServerLevel"/>,
/// <see cref="IServerFrame"/>, <see cref="IServerListing"/> and
/// <see cref="IServerHosting"/>.
///
/// <para>
/// <b>Four interfaces, one class, and that is decided rather than pending.</b>
/// The interfaces are the seam that matters — they are what the bootstrap's
/// thunks resolve and what a reader opens to find where a decision lives.
/// <i>Which object implements them</i> is a file-layout question, and splitting
/// one class into four before any of them has weight is a guess about where the
/// weight lands. plan_gamedll.md Part 13 splits it when a member belongs to one
/// interface and not the others.
/// </para>
///
/// <para>
/// <b>What is NOT here, and why the methods look empty.</b> The frame's three
/// managed points and the level's fan-out are still driven by
/// <c>CSourceSharpSystem</c> — <c>FrameUpdatePreEntityThink</c>,
/// <c>FrameUpdatePostEntityThink</c>, <c>LevelInitPostEntity</c>,
/// <c>LevelShutdownPostEntity</c> — which is a per-frame game system compiled
/// into <b>this same server binary</b>. The shell's <c>EXPOSE_</c> is commented
/// out until Phase 4, so nothing calls the methods below yet; the moment it is
/// restored, a body here that also drove <see cref="FrameDriver"/> would run
/// every participant twice a frame. plan_gamedll.md Phase 7 moves the drive
/// across, deleting the game system's copy in the same commit. Until then the
/// rule is <b>exactly one driver</b>, and it is the one that already works.
/// </para>
///
/// <para>
/// So what this class holds today is what has no second owner: the load a map
/// arrived by, the six answers Part 6 says a mod should own, and the tick
/// interval that is read once and never again.
/// </para>
/// </summary>
public sealed class ServerGameDll : IServerLifetime, IServerLevel, IServerFrame, IServerListing, IServerHosting
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// <c>DEFAULT_TICK_INTERVAL</c> (public/const.h:31). Fifteen milliseconds.
    ///
    /// <para>
    /// An ALIAS for <see cref="IServerLifetime.DefaultTickInterval"/> and not a
    /// second copy of the number: the bootstrap needs the same default for the
    /// no-module case and cannot see this assembly.
    /// </para>
    /// </summary>
    public const float DefaultTickInterval = IServerLifetime.DefaultTickInterval;

    private readonly IEngineEnvironment _environment;
    private readonly ConVarRegistry _convars;
    private readonly IEngineLevel _level;
    private readonly ParticleManifest _particles;
    private readonly IEngineEntities _entities;

    /// <summary>
    /// The template store, for the one step of a map load that drains it.
    ///
    /// <para>
    /// Held rather than reached for, and injected rather than resolved, because
    /// PrecachePointTemplates is a STEP: it has to be able to say what it is
    /// handing down, and a test has to be able to put something there.
    /// </para>
    /// </summary>
    private readonly TemplateStore _templates;

    /// <summary>
    /// The managed map spawner — plan_entity.md §3.5 and step 2d. What
    /// <c>MapEntity_ParseAllEntities</c> was, in C#.
    ///
    /// <para>
    /// Held for the same reason <see cref="_templates"/> is: the parse is a STEP
    /// in <see cref="SpawnTheMap"/>'s order and a test has to be able to put
    /// something there.
    /// </para>
    /// </summary>
    private readonly MapSpawner _spawner;

    // The side's I/O route, serviced once a frame (plan_abi_hotreload D7: it
    // was the static EntityIoRoutes).
    private readonly EntityIoRoutes _io;

    // A2: held rather than read-and-dropped. Until Phase A2 the constructor
    // used the host only to read -tickrate; the Steam pair needs it for the
    // life of the object, because the engine can activate Steam at any moment.
    private readonly IEngineHost _host;
    private readonly IEngineConsole _console;
    private readonly ServerTags _tags;
    private readonly IEngineFrame _frame;
    private readonly StringTableRegistry _stringTables;

    // A3: the lifecycle driver, so that the PRE-ENTITY half of level init has
    // somewhere to go. Held rather than resolved per call for the driver's own
    // reason -- it resolves its participants ONCE, so a second driver built
    // later would dispatch a different set in a different order.
    private readonly LifecycleDriver _lifecycle;

    private bool _systemStarted;
    private bool _gameStarted;

    private string _currentMap = string.Empty;
    private MapLoadType _loadType = MapLoadType.NewGame;
    private int _entityLumpBytes;
    private int _framesRun;

    /// <param name="environment">
    /// What the engine handed the shell. Held rather than fetched, which is the
    /// whole point of Part 0a #1: one place acquires an engine interface.
    /// </param>
    /// <param name="convars">
    /// Every declaration this side made. Held so that the shell's
    /// <c>RegisterConCommandBase</c> has somewhere to send its question — see
    /// <see cref="ConVarRegistry.TryGetCommandLinePolicy"/>.
    /// </param>
    /// <param name="host">
    /// The command line, for <c>-tickrate</c>. Read ONCE, here in the
    /// constructor, because stock's <c>GetTickInterval</c> is a <c>const</c>
    /// method the engine asks exactly once and a tick interval that changes
    /// mid-run is not a feature — every duration already computed against the
    /// old one silently means something else.
    /// </param>
    /// <param name="level">
    /// The irreducible native steps a map load is made of. plan_gamedll.md
    /// Part 4: this class owns the ORDER of them and native owns no part of it.
    /// </param>
    /// <param name="entities">
    /// Two of the same nine steps live on the entity list rather than on the
    /// load — see <see cref="IEngineEntities.PurgeMapRefs"/>.
    /// </param>
    /// <param name="stringTables">
    /// plan_gamedll_after.md Phase A8. Every <c>[StringTable]</c> declaration,
    /// already registered in the container; what this class does with it is run
    /// the CREATE pass from <see cref="CreateNetworkStringTables"/>, which is
    /// the one window the engine allows a table to be made in.
    /// </param>
    /// <param name="modTable">
    /// The mod's own declared table, injected like anything else. The class
    /// asks for the declaration by TYPE and never names the table.
    /// </param>
    /// <param name="lifecycle">
    /// plan_gamedll_after.md Phase A3. Where <see cref="MapLoadPreEntity"/>
    /// sends the pre-entity half — the half that had no caller before A3.
    ///
    /// <para>
    /// REQUIRED rather than optional, and the two test helpers were edited
    /// rather than defaulted: a participant that declares
    /// <c>OnLevelInitPreEntity</c> and is never reached is silent, which is the
    /// exact failure this argument exists to end. A default would let the next
    /// caller reproduce it.
    /// </para>
    /// </param>
    private readonly MessageOfTheDay? _motd;

    public ServerGameDll(ILog log, IEngineEnvironment environment, ConVarRegistry convars, IEngineHost host,
                         ILocalCommandLine commandLine, IEngineLevel level, IEngineEntities entities, IEngineConsole console,
                         IEngineFrame frame, StringTableRegistry stringTables,
                         ManagedStringTable modTable, LifecycleDriver lifecycle,
                         TemplateStore templates, MapSpawner spawner,
                         EntityIoRoutes io,
                         IWorldNetBinding? worldNet = null,
                         MessageOfTheDay? motd = null)
    {

        _log = log;
        _motd = motd;
        _lifecycle = lifecycle;
        _worldNet = worldNet ?? WorldNetBinding.Local(log);
        _templates = templates;
        _spawner = spawner;
        _io = io;
        _environment = environment;
        _convars = convars;
        _level = level;
        // The level area's native object answers the file reads too (they are
        // IShimLevel slots); a double that answers the steps and not the reads
        // is refused here rather than at the first map load.
        _particles = new ParticleManifest(log, level as ILevelFiles
            ?? throw new ArgumentException("the level area must also answer ILevelFiles", nameof(level)));
        _entities = entities;
        _host = host;
        _console = console;
        _frame = frame;
        _stringTables = stringTables;
        _modTable = modTable;
        TickInterval = TickIntervalFor(TickRateOn(commandLine));

        // The server-browser tags declared on convars: applied now, pushed on change.
        _tags = new ServerTags(_console, _convars);
    }

    /// <summary>
    /// What <c>-tickrate</c> names, or zero when it names nothing.
    ///
    /// <para>
    /// <b>Both halves of stock's read.</b> <c>CommandLine()-&gt;CheckParm</c>
    /// then <c>ParmValue</c> (gameinterface.cpp:918-923): the presence test
    /// first, because a bare <c>-tickrate</c> with no number after it must not
    /// be read as whatever the next argument happens to be.
    /// </para>
    ///
    /// <para>
    /// Invariant culture, because a command line is not localised and
    /// <c>float.Parse</c> under a comma-decimal locale reads "66.7" as 667.
    /// </para>
    /// </summary>
    private static float TickRateOn(ILocalCommandLine commandLine)
    {
        IReadOnlyList<string> args = commandLine.Args;
        if (!LocalCommandLine.Has(args, "-tickrate"))
            return 0f;

        return float.TryParse(LocalCommandLine.Value(args, "-tickrate", "0"),
                              System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out float rate)
            ? rate
            : 0f;
    }

    /// <summary>
    /// Stock's rule, in one place (gameinterface.cpp:900-927): the default
    /// unless <c>-tickrate</c> named something.
    ///
    /// <para>
    /// <b>Strictly greater than ten, which is stock's comparison and not a
    /// rounded version of it.</b> <c>-tickrate 10</c> is ignored and
    /// <c>-tickrate 11</c> is honoured. Stock's guard exists because the whole
    /// engine divides by this number, and a transliteration that relaxed it to
    /// <c>&gt;=</c> would change behaviour at exactly the value somebody typed
    /// on purpose.
    /// </para>
    /// </summary>
    public static float TickIntervalFor(float tickRate)
        => tickRate > 10f ? 1f / tickRate : DefaultTickInterval;

    // ---- IServerLifetime ---------------------------------------------------

    /// <inheritdoc/>
    public float TickInterval { get; }

    /// <summary>Whether <see cref="SystemStart"/> has run and <see cref="GameStop"/> has not.</summary>
    public bool IsSystemStarted => _systemStarted;

    /// <summary>Whether <see cref="GameStart"/> has run and <see cref="GameStop"/> has not.</summary>
    public bool IsGameStarted => _gameStarted;

    /// <inheritdoc/>
    public bool SystemStart()
    {
        _systemStarted = true;
        return true;
    }

    /// <inheritdoc/>
    public bool GameStart()
    {
        _gameStarted = true;
        return true;
    }

    /// <inheritdoc/>
    public void GamePostStart()
    {
        // Stock's PostInit: once per process, after the DLL's Init. It had no
        // participant until a mod's hosting service (plan_host_sdk P5), which
        // decides hosted or unhosted here; leaving it undispatched meant the
        // tf2_descent mod's first live run never started its SDK.
        _lifecycle.PostInit();
    }

    /// <inheritdoc/>
    public void GameStop()
    {
        _gameStarted = false;
        _systemStarted = false;
    }

    // ---- IServerLevel ------------------------------------------------------

    /// <summary>The map currently loaded, bare. Empty between maps.</summary>
    public string CurrentMap => _currentMap;

    /// <summary>How the current map arrived.</summary>
    public MapLoadType LoadType => _loadType;

    /// <summary>
    /// How many bytes the last load's entity lump ran to.
    ///
    /// <para>
    /// A LENGTH, deliberately, and not the lump. <see cref="MapLoad.Entities"/>
    /// is borrowed from engine memory that dies with the call, so this class
    /// could not keep it if it wanted to — <see cref="MapLoad"/> is a
    /// <c>ref struct</c> and the compiler refuses the field. The size is the
    /// part that is still true afterwards.
    /// </para>
    /// </summary>
    public int EntityLumpBytes => _entityLumpBytes;

    /// <summary>
    /// <inheritdoc cref="IServerLevel.MapLoadTransition"/>
    ///
    /// <para>
    /// <b>The save cluster wearing a different name (Part 7).</b> A transition is
    /// a save plus a restore, and this build's engine answers
    /// <c>Can't save multiplayer games</c> to every command that could produce
    /// one. The slot stays because the shape is right and costs nothing; what it
    /// does is <i>say so</i>, so that a transition arriving is a line in the log
    /// with the map named rather than a silent no-op. The shell counts it as
    /// well — that is the native half of the same evidence.
    /// </para>
    /// </summary>
    public bool MapLoadTransition(in MapLoad load)
    {
        _log.Warning($"[SourceSharp/Server] LevelInit took the TRANSITION arm for "
                  + $"'{load.MapName}' from '{load.OldLevel}'. plan_gamedll.md Part 7 "
                  + "refuses the save cluster, so nothing here saves or restores "
                  + "anything -- the map loads as if it were new.\n");

        return Loaded(in load, MapLoadType.Transition);
    }

    /// <summary>
    /// <inheritdoc cref="IServerLevel.MapLoadSaveGame"/>
    ///
    /// <para>
    /// <b>THE ORDER IS THE WHOLE OF THIS METHOD, and it is NOT stock's.</b>
    /// Stock does not spawn the map on this arm — its
    /// <c>Restore</c> re-creates every entity out of the save's entity table,
    /// so parsing the lump as well would build the world twice. This port
    /// writes NO entity table (<c>docs/managed-saverestore.md</c> §2d) and its
    /// records are the <i>managed half</i> of an entity rather than the entity:
    /// a <c>SavedEntity</c> carries map keys, <c>[NetState]</c> fields and
    /// output counters, and nothing that could bring a <c>prop_physics</c> into
    /// existence. So the native world has to come from the lump, and the save
    /// is laid back over it.
    /// </para>
    ///
    /// <para>
    /// <b>WHAT NOT SPAWNING COSTS, measured rather than reasoned.</b> With this
    /// arm doing nothing, a load left <b>0 entities</b> — and the reconnecting
    /// player was refused, because <c>CServerGameClients::ClientConnect</c>
    /// answers <c>false</c> on a null <c>g_pGameRules</c> and the game rules are
    /// installed by <c>CWorld::Spawn</c>. No world, no rules, no player. The
    /// refusal follows from the empty world and is not a separate fault.
    /// </para>
    ///
    /// <para>
    /// <b>THEN <see cref="IEngineLevel.LoadSaveGameState"/>, and it is the call
    /// stage 1 was missing.</b> <c>engine-&gt;LoadGameState</c> is what drives
    /// <c>ReadRestoreHeaders</c>, the <c>Save Header</c> and <c>LIGHTSTYLE</c>
    /// reads and then <c>Restore</c> — and the game DLL is the only thing that
    /// calls it. Stage 1 recorded those three never firing and guessed the
    /// engine had nothing to hand them; the truth is that nothing asked.
    /// </para>
    ///
    /// <para>
    /// A restore that fails leaves the freshly spawned map standing, which is
    /// the reason the spawn is FIRST: the alternative order would answer a
    /// failed load with a server that has no world in it.
    /// </para>
    /// </summary>
    public bool MapLoadSaveGame(in MapLoad load)
    {
        Loaded(in load, MapLoadType.LoadGame);

        // The world, from the lump. LastLoadSpawnedTheMap is true afterwards,
        // and on this arm that is the point rather than an accident.
        if (!SpawnTheMap(in load))
            return false;

        RestoredFromSave = _level.LoadSaveGameState(load.MapName, createPlayers: true);

        if (!RestoredFromSave)
        {
            // LOUD, AND NOT FATAL. The map is standing and playable; what is
            // missing is everything that had happened in it. A silent false
            // here is a load that looks like a load and gives back a new game.
            _log.Warning($"[SourceSharp/Server] the engine could not restore the saved "
                      + $"state for '{load.MapName}'. The map is loaded, but as a NEW "
                      + "one -- nothing that had happened in it has come back.\n");
        }

        return true;
    }

    /// <summary>
    /// Whether the last savegame arm actually got its state back out of the
    /// engine.
    ///
    /// <para>
    /// A separate fact from <see cref="LastLoadSpawnedTheMap"/>, and both are
    /// now true on this arm: the map spawned AND the save was read. Before
    /// stage 2 neither was.
    /// </para>
    /// </summary>
    public bool RestoredFromSave { get; private set; }

    /// <summary>
    /// Whether the last load actually spawned the map, or was one of the two
    /// refused save-cluster arms.
    /// </summary>
    public bool LastLoadSpawnedTheMap { get; private set; }


    /// <summary>
    /// A background map — the menu's. Identical to
    /// <see cref="MapLoadNewGame"/> in every step; the ONLY difference is
    /// <c>eLoadType</c>, and the shell has already assigned that before this is
    /// reached.
    /// </summary>
    public bool MapLoadBackground(in MapLoad load)
    {
        Loaded(in load, MapLoadType.Background);
        return SpawnTheMap(in load);
    }

    /// <summary>
    /// A fresh map. <c>CServerGameDLL::LevelInit</c>'s non-<c>loadGame</c> arm
    /// (gameinterface.cpp:1060-1195), as an ORDER rather than as a body.
    /// </summary>
    public bool MapLoadNewGame(in MapLoad load)
    {
        Loaded(in load, MapLoadType.NewGame);
        return SpawnTheMap(in load);
    }

    /// <summary>
    /// <b>The order, and it is the whole of this class's contribution to a map
    /// load.</b>
    ///
    /// <para>
    /// Every line is one native step that decides nothing (plan_gamedll.md
    /// Part 4). The sequence is stock's, and three positions in it are
    /// load-bearing rather than tidy:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><see cref="IEngineEntities.PurgeMapRefs"/> comes BEFORE the parse
    /// — the parse's filter fills that list as it goes, so purging afterwards
    /// throws away exactly what was just recorded.</item>
    /// <item><see cref="IEngineLevel.PrecachePointTemplates"/> comes AFTER it —
    /// a template's parameters cannot be read before the template entity
    /// exists.</item>
    /// <item><see cref="IEngineEntities.CleanupDeleteList"/> is LAST — anything
    /// above it can leave an entity that removed itself during its own
    /// precache.</item>
    /// </list>
    ///
    /// <para>
    /// The lump is passed straight through as the borrowed span. It is never
    /// copied and never kept: <see cref="MapLoad"/> is a <c>ref struct</c>, so
    /// keeping it is a compile error rather than a review note.
    /// </para>
    /// </summary>
    private bool SpawnTheMap(in MapLoad load)
    {
        _level.ResetServerTime();

        // The shell's own TODO at shell_gamedll.cpp:364, both halves of it.
        // THE PARTICLE MANIFESTS, IN C# -- plan_server_novalve.md P2.1. These
        // were two native steps into CParticleSystemMgr (IShimLevel's
        // ParseParticleManifest and ParseParticleManifestForMap, still in the
        // interface because it is append-only, and no longer called). The
        // always-precache set is precached here, right after the parse, where
        // stock did it from CWorld::Precache; ParticleManifest's header says
        // why only the string indices can differ.
        _particles.Clear();
        _particles.ParseGame();
        _particles.ParseMap(load.MapName);
        PrecacheParticleManifest();

        _entities.PurgeMapRefs();

        // THE MAP, AND IT IS MANAGED NOW -- plan_entity.md §3.5, step 2d.
        //
        // This line was `_level.ParseAllEntities(load.Entities)`, which handed
        // the whole lump to MapEntity_ParseAllEntities and kept the cursor in
        // C++. Every key of every entity then had to be handed back UP one at a
        // time: 5709 crossings on dm_lockdown, one per key, 72% of the whole
        // map-load entity budget and the largest single item in it. MapSpawner
        // parses the lump in managed memory and applies the keys where they are
        // already standing, so that op reads zero.
        //
        // THE LUMP IS COPIED INSIDE THIS CALL and never held: `load` is a ref
        // struct whose Entities is the ENGINE's own CM_EntityString() buffer,
        // valid for the length of LevelInit and not past it (the engine has a
        // CM_DiscardEntityString of its own). SpawnMap's first statement is the
        // copy, and the compiler forbids the span escaping.
        //
        // ALL THREE SPAWNING ARMS COME THROUGH HERE -- MapLoadNewGame,
        // MapLoadBackground and MapLoadSaveGame -- because this method is what
        // they share. The fourth, MapLoadTransition, spawns nothing at all
        // (Part 7 refuses the save cluster) and does not reach this line.
        _spawner.SpawnMap(load.Entities);

        // WHAT IS STILL NATIVE, AND IT IS A LIST RATHER THAN A CATEGORY:
        // MapSpawner hands the blocks whose class the managed registry does NOT
        // own back to the native parse, as a lump of their own. On dm_lockdown
        // that is 19 of 605 entities and four classnames -- worldspawn, plus
        // info_node, func_useableladder and info_ladder_dismount. When that list
        // is empty, mapentities.cpp can leave the vpc.

        // SETLOWVIOLENCE COMES FIRST, and it is stock's order rather than a
        // preference. gameinterface.cpp:1049 is
        // `g_RagdollLVManager.SetLowViolence( pMapName )` and :1053 is
        // `PrecachePointTemplates()`; these two lines ran the other way round
        // here from the day this method was written, under a doc comment saying
        // "the sequence is stock's" and a [Fact] called
        // AFreshMapLoadRunsStocksNineStepsInStocksOrder that pinned the
        // inversion. Found by tools/gate5 on its first real run, which is what
        // that gate exists for -- no census, no live gate and no managed test
        // could see it, because the test that would have was written from the
        // same wrong belief as the code.
        //
        // IT IS NOW INERT ON THE NATIVE SIDE BY CONSTRUCTION, not merely as
        // far as anything measured could tell. When this was written its three
        // readers of IsLowViolence() were the native ragdoll retirement, and a
        // precache could not reach them; plan_shim.md §4a item 3 then deleted
        // the retirement and the low-violence manager with it, because the LRU
        // they served had no writer on either link line. So
        // SourceSharp_LevelSetLowViolence has an empty body today.
        //
        // THE CALL STAYS ANYWAY, and for a stronger reason than before: its
        // POSITION is the fact. gameinterface.cpp:1049 sets low violence and
        // :1053 precaches point templates; these two lines ran the other way
        // round here until tools/gate5 found it, and
        // AFreshMapLoadRunsStocksNineStepsInStocksOrder pins the repair. An
        // empty step still holds its place in that sequence, and the decision
        // itself is made by Corpses.ReadViolencePolicy on the same load.
        _level.SetLowViolence(load.MapName);

        // THE NATIVE BODY IS EMPTY NOW AND THE CALL STAYS, which is deliberate
        // (lane 3b-enginebind). game/server/TemplateEntities.cpp left the vpc
        // and a managed template entity is precached by the managed spawner, so
        // the native loop precached nothing and is gone -- but this CALL is the
        // only marker the two orderings around it can be observed through, and
        // one of them was wrong here once: stock sets low violence
        // (gameinterface.cpp:1049) before this (:1053), and tools/gate5 found
        // this port running that pair inverted. A retired slot is not called,
        // so retiring it would delete the evidence.
        _level.PrecachePointTemplates(_templates.DrainPendingPrecaches());

        // The native step is the ordering marker (above); the load itself is
        // C#'s, at stock's position (gameinterface.cpp:1053).
        _level.LoadMessageOfTheDay();
        _motd?.Load();
        _entities.CleanupDeleteList();

        LastLoadSpawnedTheMap = true;
        return true;
    }

    /// <summary>
    /// What all four arms have in common. The arms stay four methods because the
    /// two save-cluster ones are refused separately (Part 7) and a single method
    /// with an enum would put that refusal inside a switch.
    /// </summary>
    private bool Loaded(in MapLoad load, MapLoadType how)
    {
        _currentMap = load.MapName;
        _loadType = how;
        LastLoadSpawnedTheMap = false;

        // The one measurement taken of the borrowed span, taken INSIDE the call
        // because that is the only place it is valid.
        _entityLumpBytes = load.Entities.Length;

        return true;
    }

    /// <summary>
    /// <b>The order LevelShutdown is made of, and it is the only thing in it.</b>
    ///
    /// <para>
    /// <c>CServerGameDLL::LevelShutdown</c> (gameinterface.cpp:1511-1559) is
    /// nine calls whose <i>sequence</i> is the entire content — three system
    /// passes with the steam-context clear, the sound entity and the entity list
    /// interleaved between them at exactly the points the passes are named for.
    /// So this is the clearest case in the whole plan for one native step per
    /// call: a single <c>LevelShutdown</c> crossing would have carried the one
    /// thing worth reading back into C++.
    /// </para>
    ///
    /// <para>
    /// The MANAGED level shutdown is not a line here. It is a row of
    /// <c>kServerSystems[]</c>, so it happens inside
    /// <see cref="IEngineLevel.ShutdownPostEntitySystems"/> at the position
    /// <c>CSourceSharpSystem</c> held — which is the position that keeps it
    /// after every other participant's teardown and before the entity list is
    /// gone.
    /// </para>
    ///
    /// <para>
    /// <b>Bookkeeping first.</b> The two fields below are this object's own and
    /// nothing native reads them, so they are cleared before the passes rather
    /// than after: a participant reached from one of those passes that asks
    /// <see cref="CurrentMap"/> should see "no map", not the one going away.
    /// </para>
    /// </summary>
    /// <summary>
    /// The PRE-ENTITY half of level init.
    ///
    /// <para>
    /// <b>Nothing native here, and that is the whole shape of Phase A3.</b> The
    /// two systems that moved — <c>CPropData</c> and
    /// <c>CDecalEmitterSystem</c> — are <see cref="IGameLifecycle"/>
    /// participants now, so what this method does is dispatch, exactly as
    /// <c>ServerModule.LevelInit</c> does for the PostEntity half. Whatever
    /// native step either of them still needs, each asks for itself.
    /// </para>
    ///
    /// <para>
    /// <b>The map name is NOT stored.</b> <see cref="CurrentMap"/> is set by the
    /// four <c>MapLoad*</c> arms, which run earlier in the same load and name
    /// the same map; writing it again here would be a second record of one fact.
    /// </para>
    /// </summary>
    public void MapLoadPreEntity(string mapName)
    {
        // FIRST, before any participant: the sound emitter system's level
        // start (the map's overrides, the preload), which was the
        // CSoundEmitterSystem row's LevelInitPreEntity, the row before the
        // SourceSharpManaged row this runs from (plan_server_native_v2 T4a).
        SoundSystem?.LevelInitPreEntity();

        _lifecycle.LevelInitPreEntity(mapName);
    }

    /// <summary>
    /// The PRE-ENTITY half of level shutdown.
    ///
    /// <para>
    /// <b>Nothing native here, and it is the mirror of
    /// <see cref="MapLoadPreEntity"/>.</b> What this method does is dispatch —
    /// the native pre-entity pass is still <c>_level.ShutdownPreEntitySystems()</c>
    /// inside <see cref="MapUnload"/>, and this runs from INSIDE it, because the
    /// <c>SourceSharpManaged</c> row is one of the rows that pass walks.
    /// </para>
    ///
    /// <para>
    /// <b>It is reached from the row and not called from <see cref="MapUnload"/>,
    /// and that is a position rather than a preference.</b> The pass walks
    /// <c>kServerSystems[]</c> BACKWARDS, so a dispatch written here would run
    /// either before every row or after every one — and the two participants
    /// that need it, <c>SoundController</c> and <c>StandaloneHierarchy</c>, both
    /// inherit their position from rows that sat in the middle of that walk.
    /// </para>
    /// </summary>
    public void MapUnloadPreEntity()
    {
        _lifecycle.LevelShutdownPreEntity();
    }

    public void MapUnload()
    {
        _currentMap = string.Empty;
        _entityLumpBytes = 0;

        // worldspawn's state was bound to an entity that is about to be freed
        // (plan_gamedll_after.md Part B2). Dropping it here rather than letting
        // the next map's first key notice the handle changed: a bound state
        // holds a pointer into the entity, and a state that reports itself
        // unbound is better than one that is merely never asked.
        _worldNet.Unbind();
        ChapterTitle = string.Empty;

        _level.ShutdownPreClearSteamSystems();

        // The Steam context clear is C#'s own (plan_server_novalve.md P2.5):
        // IShimLevel's ClearSteamAPIContext step is retired.
        _host.SteamGameServerClear();

        _level.ShutdownPreEntitySystems();

        // "YWB: This entity pointer is going away now and is corrupting memory
        // on level transitions/restarts" -- stock's comment, and the reason this
        // is between the pre-entity pass and the clear rather than in either.
        _level.ShutdownSoundEnt();
        _level.ClearEntityList();

        _level.ShutdownPostEntitySystems();

        // "In case we quit out during initial load" -- stock's comment. The
        // precache window is closed on the way out as well as at activate,
        // because a load that failed halfway never reached the activate.
        _level.SetPrecacheAllowed(false);

        // Stock uncached CParticleSystemMgr's definitions and rebuilt its
        // dictionary here; the definitions are ParticleManifest's now.
        _particles.Clear();
    }

    /// <summary>
    /// Where the always-precache set goes. Set by <see cref="ServerModule"/>;
    /// a load that has systems to precache and nowhere to send them is refused
    /// by name rather than dropped.
    /// </summary>
    internal IEnginePrecache? ParticlePrecache { get; set; }

    /// <summary>
    /// The managed sound emitter system, whose level start runs first in
    /// <see cref="MapLoadPreEntity"/> (plan_server_native_v2 T4a); null when
    /// the module could not build it, which it has said.
    /// </summary>
    internal SoundEmitterSystem? SoundSystem { get; set; }

    /// <summary>The model text and sequences ModelPrecacheComponents reads. Set by ServerModule.</summary>
    internal IEngineEntityRender? ModelText { get; set; }

    /// <summary>The server's model descriptors, whose sequences ModelPrecacheComponents reads. Set by ServerModule.</summary>
    internal ModelDescriptors? Models { get; set; }

    /// <summary>The sound script table ModelPrecacheComponents filters against. Set by ServerModule.</summary>
    internal IVtableSoundEmitter? SoundScripts { get; set; }

    /// <inheritdoc/>
    public void ModelPrecacheComponents(string modelName)
    {
        if (ModelText is null || ParticlePrecache is null || Models is null)
            return;

        bool snapshot = System.Environment.GetEnvironmentVariable("SOURCESHARP_NOVALVE_SNAPSHOT") != null;

        // Stock refused a model named without the .mdl extension here, before
        // anything else, and said so. (Its developer-2 note about rebuilding
        // modelsounds.cache has no cache to be about.)
        string extension = Path.GetExtension(modelName);
        if (extension.IndexOf("mdl", StringComparison.OrdinalIgnoreCase) < 0)
        {
            _log.Warning(extension.Length == 0
                ? $"Precache of {modelName} ambigious (no extension specified)\n"
                : $"Late precache of {modelName} (file missing?)\n");
            return;
        }

        IReadOnlyList<ModelSequence> sequences = Models.ForModel(modelName).Sequences;

        // 1. SOUNDS: the model sounds cache's list, rebuilt here every time --
        // each name the sound table holds, once, by the table's own spelling.
        if (SoundScripts is not null)
        {
            var seen = new HashSet<int>();
            var indices = new List<int>();
            foreach (string name in ModelComponents.SoundNames(sequences, modelName, w => _log.Warning(w + "\n")))
            {
                int index = SoundScripts.SoundIndex(name);
                if (index != -1 && seen.Add(index))
                    indices.Add(index);
            }

            foreach (int index in indices)
            {
                string? sound = SoundScripts.SoundName(index);
                if (sound is null)
                    continue;
                ParticlePrecache.PrecacheSound(sound);
                if (snapshot)
                    _log.Msg($"[novalve-snapshot:model-sound] {modelName} {sound}\n");
            }
        }

        // 2. THE $keyvalues "Particles" SYSTEMS.
        List<string> names = ModelParticles.Names(ModelText.GetModelKeyValueText(modelName));
        if (snapshot)
            _log.Msg($"[novalve-snapshot:model-particle-count] {modelName} {names.Count}\n");

        foreach (string name in names)
        {
            ParticlePrecache.PrecacheParticleSystem(name);
            if (snapshot)
                _log.Msg($"[novalve-snapshot:model-particle] {modelName} {name}\n");
        }

        // 3. THE ANIMATION-EVENT PARTICLE EFFECTS.
        foreach (string effect in ModelComponents.EventParticles(sequences))
        {
            ParticlePrecache.PrecacheParticleSystem(effect);
            if (snapshot)
                _log.Msg($"[novalve-snapshot:model-event-particle] {modelName} {effect}\n");
        }
    }

    private void PrecacheParticleManifest()
    {
        List<string> always = _particles.AlwaysPrecache();
        bool snapshot = System.Environment.GetEnvironmentVariable("SOURCESHARP_NOVALVE_SNAPSHOT") != null;

        if (always.Count > 0)
        {
            IEnginePrecache precache = ParticlePrecache
                ?? throw new InvalidOperationException("ServerGameDll.ParticlePrecache was never set; "
                                                       + always.Count + " particle system(s) to precache");
            foreach (string name in always)
            {
                precache.PrecacheParticleSystem(name);
                // The marker PrecacheStandardParticleSystems logged, same text,
                // so tools/novalve-snapshot --diff holds this list to the one
                // the native manager produced.
                if (snapshot)
                    _log.Msg($"[novalve-snapshot:particle-precache] {name}\n");
            }
        }

        if (snapshot)
            _log.Msg($"[novalve-snapshot:particle-precache-count] {always.Count}\n");
    }

    /// <summary>
    /// <b>What closes the 590-versus-473 gap.</b>
    ///
    /// <para>
    /// <c>CServerGameDLL::ServerActivate</c> (gameinterface.cpp:1220-1275).
    /// Phase 6 measured the shell path at 590 entities against the delegate's
    /// 473 and accounted for every one of the difference here: five map classes
    /// remove themselves during <c>Activate()</c> — decals, compile-time lights,
    /// a <c>logic_auto</c> that fires and goes — and four more are CREATED by
    /// the pass, which is the managed level-init's <c>ss_pickup</c>,
    /// <c>ss_beam</c>, the <c>spotlight_end</c> a live <c>point_spotlight</c>
    /// makes, and <c>player_manager</c>.
    /// </para>
    ///
    /// <para>
    /// <b>No <c>g_InRestore</c> gate.</b> Stock's first line returns early while
    /// a save is being restored. Nothing in a multiplayer build sets that flag —
    /// this build's engine answers <c>Can't save multiplayer games</c> to
    /// everything that could — so it is dead code that reads as live. Part 7's
    /// rule: skip the save cluster rather than carry an inert copy of it.
    /// </para>
    /// </summary>
    public void ServerActivate()
    {
        // STOCK'S DELETE-QUEUE CHECK IS NOT ASKED OF NATIVE ANY MORE.
        // IEngineLevel.ResetDeleteList is a retired slot (lane 3b-enginebind):
        // armed there is no SDK delete list for it to count, so it answered a
        // constant 0 and the warning below could never fire. The COUNT still
        // came back so the decision about what to say stayed here, which was
        // right -- and it is why the check moves rather than goes.
        //
        // THE WARNING GOES WITH IT RATHER THAN BEING REPOINTED, and that is a
        // deliberate stop rather than an oversight. The question is still worth
        // asking -- a non-empty delete queue at level start is a bug somewhere
        // upstream -- but the hierarchy that HAS a queue is the parallel one,
        // and its count is EntityDirectory.PendingRemovalCount, which this
        // class cannot reach: it holds IEngineEntities, not the directory.
        // Wiring a new dependency through the constructor to restore a warning
        // that has never once fired is a larger change than the one this lane
        // is making, so it is left for whoever owns that constructor.

        // STOCK'S ActivateAllEntities IS NOT CALLED. Its slot is retired: every
        // line of stock's walk (gameinterface.cpp:1233-1250) reaches the
        // hierarchy server_csharp.vpc evicts, so the native body had been empty
        // since that eviction. The parallel hierarchy's half, below, is the
        // whole of ServerActivate's activation pass -- and the ORDER is
        // unchanged, because the two hierarchies were already activated in the
        // order their entities were made.

        // AND THE PARALLEL HIERARCHY'S HALF OF THE SAME WALK -- plan_entity.md
        // §3.5, step 2d: "calls Spawn and Activate in stock order".
        //
        // AFTER the native walk rather than instead of it, and the position is
        // the same one MapSpawner's own spawn takes: the two hierarchies are
        // activated in the order their entities were made, which is the order
        // stock's single gEntList walk gave them.
        //
        // NO CROSSING. MapSpawner holds the entities it made and walks them in
        // managed memory.
        _spawner.ActivateAll();

        // The named list's LevelInitPostEntity pass, and the managed level init
        // is a ROW in it rather than a line here -- see the summary on
        // MapUnload for why that position is load-bearing.
        _level.InitPostEntitySystems();

        // "No more precaching after PostEntityAllSystems!!!" -- stock's own
        // shout, and the reason this is the step immediately after it.
        _level.SetPrecacheAllowed(false);

        ApplyThinkLimitPolicy();
        ApplyLocalTransferPolicy();
    }

    /// <summary>
    /// Turns off the engine's partial-change local transfer
    /// (<c>dt_UsePartialChangeEnts</c>, "(SP only)", default 1).
    ///
    /// <para>
    /// <b>The engine path is broken on 64-bit, for every entity.</b> With the
    /// local network backdoor on (a <c>-maxplayers 1</c> listen server, where
    /// <c>cl_LocalNetworkBackdoor</c> defaults to 1), an entity whose edict
    /// recorded change offsets is copied by LocalTransfer_TransferEntity's
    /// partial arm. That arm calls <c>CDatatableStack::Init(true)</c>, which
    /// fills the proxy slots with <c>memset(0xFF)</c>: a 64-bit -1. The lookup
    /// rebuilds a slot only when it equals <c>0xFFFFFFFF</c> (engine.so
    /// 0x4f9ad8 loads the sentinel into a 32-bit register). So every slot, the
    /// root included, reads as a struct base of -1, and the first send proxy
    /// dereferences <c>-1 + offset</c>: SIGSEGV in SendProxy_VectorToVector on
    /// m_vecOrigin (offset 1856), about 11 s after <c>+map</c>.
    /// </para>
    ///
    /// <para>
    /// With the switch off the engine takes its full-transfer arm for a changed
    /// entity, which sets the root base properly. Nothing else reads the
    /// convar (one reference in engine.so), so multiplayer packing is
    /// unaffected. Managed because it is a decision; the console area already
    /// sets engine convars by name.
    /// </para>
    /// </summary>
    private void ApplyLocalTransferPolicy()
        => _console.SetConVarValueByName("dt_UsePartialChangeEnts", "0");

    /// <summary>
    /// Stock's last two lines of <c>ServerActivate</c>: "only display the think
    /// limit when the game is run with developer mode set".
    ///
    /// <para>
    /// <b>Managed, and not a native step, because it is a CONDITION.</b> Part 4's
    /// rule is that a step is never a decision — so the shell exposes no
    /// think-limit step at all and this reads <c>developer</c> and writes
    /// <c>think_limit</c> through the console area, which already has both.
    /// </para>
    ///
    /// <para>
    /// A missing <c>developer</c> reads as zero, which is stock's behaviour for
    /// a convar whose <c>GetInt()</c> would be zero anyway.
    /// </para>
    /// </summary>
    private void ApplyThinkLimitPolicy()
    {
        string? developer = _console.FindConVarValue("developer");

        bool on = developer is not null
               && int.TryParse(developer, System.Globalization.NumberStyles.Integer,
                               System.Globalization.CultureInfo.InvariantCulture, out int level)
               && level != 0;

        if (!on)
            _console.SetConVarValueByName("think_limit", "0");
    }

    /// <summary>
    /// The mod's own table, injected.
    ///
    /// <para>
    /// <b>A field, and that is now safe.</b> What the container holds is one
    /// object per <c>[StringTable]</c> declaration carrying the table's NAME,
    /// so it answers about whichever table has that name on the map running
    /// now. There is no id to go stale and nothing to re-bind at a map change.
    /// </para>
    /// </summary>
    private readonly ManagedStringTable _modTable;

    /// <summary>The side's user-message registry, whose ids the SsUserMessages table carries.</summary>
    public UserMessageRegistry? UserMessages { get; set; }

    /// <summary>
    /// The managed server skeletons (plan physics_bones_native_zero step
    /// 1.2), told about a model-cache flush: their model images were copied
    /// from the studio data the flush frees, and every skeleton built from
    /// one is stale.
    /// </summary>
    public ServerSkeletons? Skeletons { get; set; }

    /// <summary>How many managed skeletons the last flush invalidated.</summary>
    public int MdlCacheSkeletonsInvalidated { get; private set; }

    /// <summary>The SsUserMessages table the registry writes its keys into (plan_side_isolation §2.2).</summary>
    public UserMessageTable? UserMessageIds { get; set; }

    /// <summary>
    /// <b>Part 8's managed half, and Phase A8's demonstration that it works.</b>
    ///
    /// <para>
    /// The seven shared tables — <c>Materials</c>, <c>ParticleEffectNames</c>,
    /// <c>EffectDispatch</c>, <c>VguiScreen</c>, <c>InfoPanel</c>,
    /// <c>Scenes</c>, <c>ServerMapCycle</c> — are already created by the time
    /// this runs, natively, and are deliberately <i>not</i> ported: 141 native
    /// references read the globals behind them. The shell calls this method
    /// <i>after</i> that, which is a contract rather than an ordering
    /// preference — see <see cref="IEngineStringTable"/> for the index-0
    /// invariant that depends on it.
    /// </para>
    ///
    /// <para>
    /// <b>This is the only moment a table may be created</b>, which is why the
    /// registry's create pass is driven from here and from nowhere else. The
    /// engine opens the window in this call and refuses outside it.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing in this method names a table.</b> The mod's own is declared
    /// by <c>[StringTable]</c> on <see cref="ManagedStringTable"/> and injected
    /// into this class; a second mod adding a second declaration changes
    /// nothing here. What is left is the read-back, which is Phase A8's own
    /// gate: <b>a read of the ENGINE and not of the string just written</b>,
    /// because comparing <see cref="ModTableEntry"/> with itself would pass against
    /// a <c>GetString</c> that returns its argument, against one that returns
    /// nothing, and against a table that was never filled.
    /// </para>
    /// </summary>
    public void CreateNetworkStringTables()
    {
        _stringTables.CreateAll();

        // The user-message ids (plan_side_isolation §2.2): the key of id i at
        // index i of the table just created, for every client to map through.
        if (UserMessages is { } messages && UserMessageIds is { } ids)
            messages.WriteTable(ids);

        int index = _modTable.AddString(ModTableEntry);

        string? readBack = index < 0 ? null : _modTable.GetString(index);

        _log.Msg($"[SourceSharp/Server] mod string table '{_modTable.Name}' id {_modTable.Id}: "
              + $"{_modTable.Count} string(s), "
              + $"[{index}] = '{readBack ?? "<nothing>"}'\n");
    }

    /// <summary>
    /// One of the map's worldspawn keys (plan_gamedll_after.md Part B2).
    ///
    /// <para>
    /// A one-line forward, because the decision about what a worldspawn key
    /// MEANS belongs with the declaration that holds the value —
    /// <see cref="WorldNet"/> beside <see cref="WorldNetState"/> in the shared
    /// assembly both sides compile against. This class is the seam the engine's
    /// question arrives at, and the seam is all it is.
    /// </para>
    /// </summary>
    /// <remarks>
    /// THREE KEYS ARE NOT VALUES WORLDSPAWN HOLDS, and they are answered here
    /// (plan_server_native_v2 T4d; CWorld::KeyValue answered them natively):
    /// "skyname" sets the ConVar the engine replicates, "newunit" talks to a
    /// save system with no reader of it left, and "chaptertitle" is a game-logic
    /// key the level announces per player and stock never networked. Matched as
    /// stock's FStrEq does, ignoring case.
    /// </remarks>
    public bool WorldKeyValue(EntityHandle world, string key, string value)
    {
        if (key.Equals("skyname", StringComparison.OrdinalIgnoreCase))
        {
            _console.SetConVarValueByName("sv_skyname", value);
            return true;
        }

        if (key.Equals("newunit", StringComparison.OrdinalIgnoreCase))
            return true;

        if (key.Equals("chaptertitle", StringComparison.OrdinalIgnoreCase))
        {
            ChapterTitle = value;
            return true;
        }

        return _worldNet.ApplyKey(world, key, value);
    }

    /// <summary>
    /// The map's "chaptertitle", or empty: what CWorld::m_iszChapterTitle held
    /// until plan_server_native_v2 T4d. Dropped with the world at
    /// <see cref="MapUnload"/>, as the member went with its entity.
    /// </summary>
    public string ChapterTitle { get; private set; } = string.Empty;

    /// <summary>
    /// worldspawn's bound networked state: the module's, kept between the map
    /// parse's keys and dropped at <see cref="MapUnload"/>. A fresh one when the
    /// caller supplied none, which is a test's.
    /// </summary>
    private readonly IWorldNetBinding _worldNet;

    /// <summary>
    /// What the mod's table carries. Its content is not the point: the gate is
    /// that the engine hands back what was written. (It carried the ABI version
    /// until the version was dropped, 2026-10-01.)
    /// </summary>
    public const string ModTableEntry = "sourcesharp";

    // ---- IServerFrame ------------------------------------------------------

    /// <summary>Frames this object has been handed. The frame's own work is Phase 7's.</summary>
    public int FramesRun => _framesRun;

    /// <summary>Whether the last frame was a simulating one.</summary>
    public bool WasSimulating { get; private set; }

    /// <summary>
    /// <b>The server frame, as an order.</b>
    /// <c>CServerGameDLL::GameFrame</c> (gameinterface.cpp:1319-1418), with
    /// every line that survives this build as one native step and the sequence
    /// here.
    ///
    /// <para>
    /// <b>The managed frame's first two points are NOT lines in this method,
    /// and Part 4's sketch says they are.</b> <c>FramePreThink</c> and the
    /// managed frame are game-system stages in stock — they run inside
    /// <c>IGameSystem</c>'s two per-frame passes at <c>CSourceSharpSystem</c>'s
    /// position in the list — so they are a ROW of <c>kServerSystems[]</c> and
    /// they happen inside <see cref="IEngineFrame.ServerSystemsPreThink"/> and
    /// <see cref="IEngineFrame.ServerSystemsPostThink"/>. Writing them inline
    /// here, as the sketch does, would move the managed frame ahead of
    /// <c>CSoundEnt</c> in the first pass and ahead of
    /// <c>CRagdollLRURetirement</c> and <c>CEntityListSystem</c> in the second.
    /// None of those three is a no-op. The third point,
    /// <c>FramePostThink</c>, is inside <see cref="IEngineFrame.EndGameFrame"/>
    /// — again, where stock reaches it from.
    /// </para>
    ///
    /// <para>
    /// <b>No <c>g_InRestore</c> gate.</b> Stock's first line returns early while
    /// a save restores, and nothing in a multiplayer build sets that flag, so it
    /// is dead code that reads as live (Part 11c #9).
    /// </para>
    /// </summary>
    public void GameFrame(bool simulating)
    {
        _framesRun++;
        WasSimulating = simulating;

        // ---- THE ALTERNATE-TICK ARM, TRANSLITERATED ON PURPOSE --------------
        //
        // Same condition, same doubling, same restore, in the same order as
        // gameinterface.cpp:1326-1337. THE LITERALNESS IS DELIBERATE: an
        // alternate-tick server is a configuration almost nobody runs locally,
        // so a cleverer version of this would be wrong in a place nobody looks.
        // Read it against stock line for line; that is what it is for.
        if (_frame.IsSimulatingOnAlternateTicks())
        {
            // "only run simulation on even numbered ticks"
            if ((_frame.TickCount() & 1) != 0)
            {
                _frame.UpdateAllClientData();
                return;
            }

            // "If we're skipping frames, then the frametime is 2x the normal tick"
            _frame.SetFrameTime(_frame.GetFrameTime() * 2.0f);
        }

        _frame.SetServerCurTime();
        float oldFrameTime = _frame.GetFrameTime();

        // "Delete anything that was marked for deletion outside of server
        // frameloop (e.g., in response to concommand)"
        _entities.CleanupDeleteList();

        // The named list's pre-entity-think pass. The managed FramePreThink is
        // a row in it.
        _frame.ServerSystemsPreThink();

        _frame.GamestatsUpdateConnection();

        // STOCK'S STEP 8 -- every entity's think function -- IS NOT CALLED.
        // IShimFrame.PhysicsRunThinkFunctions is a retired slot: physics_main.cpp
        // left the vpc, the SimThink list it walked is defined only there and in
        // entitylist.cpp, and under the entity alias no native entity object can
        // be on it. The thinks are managed and drain through
        // EntityDirectory.RunFrame from StandaloneHierarchy's hook inside the
        // very next statement (plan_entity.md 3.4, step 1g). The deferred-delete
        // cleanup that was step 8's other effect ran a step earlier, above.

        // The named list's post-entity-think pass. The managed frame — every
        // IFrameLifecycle.OnThink, the dev API's pump, the entity-slot drain —
        // is a row in it.
        _frame.ServerSystemsPostThink();

        // "service event queue, firing off any actions whos time has come"
        //
        // BOTH QUEUES, HERE, AND NOWHERE ELSE -- the sub-ioqueue lane.
        //
        // status/p4-base.md:156 recorded that this build has TWO entity I/O
        // queues serviced at TWO POINTS in one frame on two independent clocks
        // -- the managed one inside ServerSystemsPostThink, one step earlier,
        // and the native one here -- and that nobody had checked whether a map
        // mixing ent_fire with relay outputs was affected. It is. Both are here
        // now, one after the other with nothing between them, which is ONE
        // point, at stock's own position.
        //
        // THE NATIVE ONE FIRST, AND THAT ORDER IS THE FIX RATHER THAN A
        // PREFERENCE. Events cross the boundary in ONE direction: `ent_fire`
        // and CBaseEntityOutput::FireOutput post into g_EventQueue, whose
        // delivery reaches a managed entity, whose own outputs post into the
        // MANAGED queue -- and nothing managed ever posts back the other way.
        // Serviced managed-first, that hop lands after the managed pass has
        // already finished and waits a whole frame. Stock cannot do that at
        // all: ServiceEvents re-reads its head, so a zero-delay chain drains in
        // one pass however it started. Native-first, so does this one.
        // EntityIoQueue.PostedAfterThePass is the counter that reads non-zero
        // when it does not.
        //
        // WHAT IS STILL NOT STOCK, said plainly rather than left to be found:
        // stock has ONE list ordered by (time, posting order), and two events
        // due at the same instant with one in each queue are ordered here by
        // which queue holds them. The native queue holds console-originated
        // events and the SDK's own outputs; in a build whose map entities are
        // all managed that is `ent_fire`, one at a time, by hand.
        //
        // IT COSTS NO CROSSING. IEngineHost.CurrentTime is
        // `=> Engine.CurrentTime` (EngineBinding.cs:46), which LOADS curtime out
        // of the engine's own gpGlobals through the pointer the shim published;
        // and GameFrame is itself inside the one ServerGameFrame crossing, so
        // the pass that moved here brings nothing with it.
        // THE NATIVE QUEUE IS GONE, NOT DEFERRED. IShimFrame.ServiceEventQueue
        // is a retired slot -- cbase.cpp defined the queue and left the vpc, and
        // so did both producers (ent_fire in baseentity.cpp, the output-firing
        // path in entityoutput.cpp). No armed server TU can post an event, so
        // the ordering argument above now has one queue to order: this one, at
        // stock's own position.
        _io.Service(_host.CurrentTime);

        // "free all ents marked in think functions"
        _entities.CleanupDeleteList();

        _frame.UpdateAllClientData();

        // The rules' end of frame, which is where the THIRD managed point and
        // the late entity-slot drain both live.
        _frame.EndGameFrame();

        // "Any entities that detect network state changes on a timer do it here."
        // NO ENTITY DOES: IShimFrame.FireNetworkPropertyEvents is a retired slot.
        // g_NetworkPropertyEventMgr went with game/server/timedeventmgr.cpp at
        // plan_shim.md Phase 1d, and CServerNetworkProperty.SetUpdateInterval --
        // the only thing that could have registered an entity with it -- had no
        // caller in any compiled translation unit.

        _frame.SetFrameTime(oldFrameTime);
    }

    /// <summary>
    /// <c>CServerGameDLL::PreClientUpdate</c> (gameinterface.cpp:1424-1442).
    ///
    /// <para>
    /// The <c>!simulating</c> early return is stock's first line and it lives in
    /// the shell, where it already was — this method is only reached when the
    /// frame is simulating.
    /// </para>
    /// </summary>
    public void PreClientUpdate()
    {
        // NOT behind a debug guard, because stock's is not: its own comment
        // there is "allow this in release for now". This is what makes an
        // NDebugOverlay call from managed code visible at all.
        _frame.DrawAllDebugOverlays();

        _frame.ServerSystemsPreClientUpdate();

        // THE MANAGED PARTICIPANTS, AND THEY RUN AFTER THE NATIVE WALK.
        //
        // plan_gamedll_after.md Phase A3, second pair. Until it,
        // FrameDriver.PreClientUpdate had NO production caller at all -- the
        // point existed, the dispatcher existed, and the only two things that
        // had ever driven it were tests. SoundController is the first
        // participant, and stock's CSoundControllerImp ran its whole per-frame
        // pass from this stage and no other.
        //
        // AFTER rather than before, and that is an ordering decision rather than
        // a place to put a line. The only other row in kServerSystems[] that
        // declares PreClientUpdate is CPhysicsHook at index 1, and stock ran it
        // FIRST -- so dispatching the managed participants before the table
        // walk would have moved the sound controller ahead of the physics hook.
        //
        // A DELEGATE rather than a held FrameDriver, for a construction-order
        // reason: FrameDriver resolves every IFrameLifecycle in its constructor,
        // so it cannot exist until every participant is registered, and this
        // object is built before some of them. The same shape DevContext's
        // FlushSoundscape already uses.
        FrameParticipants?.Invoke(_frame.GetFrameTime());
    }

    /// <summary>
    /// Where <see cref="PreClientUpdate"/> sends the managed frame
    /// participants. Set by <c>ServerModule</c> once the driver exists; null
    /// until then, and null in a test that builds this object alone.
    /// </summary>
    public Action<float>? FrameParticipants { get; set; }

    // ---- IServerListing ----------------------------------------------------
    //
    // Part 6's six answers. Settable, because "the answers a mod should own" is
    // the whole point of the part: a mod sets one of these in its module and the
    // shell reports it, instead of editing a return statement in C++.

    /// <summary>
    /// <inheritdoc cref="IServerListing.GameDescription"/>
    ///
    /// <para>
    /// <b>This is the ONE managed answer, and the existing native route now ends
    /// here.</b> <c>SourceSharp_GetGameDescription()</c> already reached C#
    /// through <c>ServerModule.GetGameDescription()</c>, which returned a
    /// literal; that method forwards to this property now. Adding a second route
    /// for the shell to call would have given the server browser two answers
    /// that agree until somebody changes one.
    /// </para>
    /// </summary>
    public string GameDescription { get; set; } = "SourceSharp";

    /// <inheritdoc cref="IServerListing.ShouldHideServer"/>
    public bool ShouldHideServer { get; set; }

    /// <inheritdoc cref="IServerListing.BrowserMapOverride"/>
    public string? BrowserMapOverride { get; set; }

    /// <inheritdoc cref="IServerListing.BrowserGameData"/>
    public string? BrowserGameData { get; set; }

    /// <summary>
    /// Why a manual map change is refused, or null while it is allowed.
    ///
    /// <para>
    /// One nullable string rather than a bool and a string, so the two cannot
    /// disagree: there is no way to spell "refused, with no reason" or "allowed,
    /// but here is why not".
    /// </para>
    /// </summary>
    public string? ManualMapChangeRefusal { get; set; }

    /// <inheritdoc/>
    public bool IsManualMapChangeOkay(out string? reason)
    {
        reason = ManualMapChangeRefusal;
        return reason is null;
    }

    // ---- what the shell asks about a ConVar declaration --------------------

    /// <summary>
    /// The environment, for anything that needs an engine interface or the live
    /// globals. Exposed rather than reached for: Part 0a #1's whole claim is that
    /// there is one acquisition point.
    /// </summary>
    public IEngineEnvironment Environment => _environment;

    /// <summary>
    /// Part 0a #3's managed half, reached from the game DLL because that is what
    /// the shell holds. Answers what the shell's <c>RegisterConCommandBase</c>
    /// needs to know and cannot: whether a command-line value may set this
    /// declaration, and what its declared default is.
    /// </summary>
    public bool TryGetConVarCommandLinePolicy(string name, out ConVarCommandLinePolicy policy)
        => _convars.TryGetCommandLinePolicy(name, out policy);

    // ---- IServerHosting ----------------------------------------------------
    //
    // plan_gamedll.md Part 10's three loose ends. Two of them cannot be reached
    // in this build and the shell counts every call to both, so that stays a
    // measurement rather than a claim -- see docs/gamedll-ledger.md, Phase 8.

    /// <summary>
    /// The last cvar query answer the engine handed back, or null if none ever
    /// arrived.
    ///
    /// <para>
    /// KEPT rather than dropped, and it is the only reason this method has a
    /// body at all: nothing in this build calls <c>StartQueryCvarValue</c>, so
    /// the native refusal counter is the evidence that nothing arrives. This is
    /// the managed half of the same evidence, and it is what a mod that starts
    /// issuing queries reads first.
    /// </para>
    /// </summary>
    public QueriedCvar? LastQueriedCvar { get; private set; }

    /// <inheritdoc/>
    public void QueryCvarValueFinished(int cookie, int entIndex, QueryCvarStatus status,
                                       string cvarName, string? cvarValue)
    {
        LastQueriedCvar = new QueriedCvar(cookie, entIndex, status, cvarName, cvarValue);
    }

    /// <summary>
    /// Whether the server is currently hibernating.
    ///
    /// <para>
    /// A LISTEN SERVER NEVER SETS THIS. Stock's flag is read by a dedicated
    /// server idling with nobody connected. It is not the inverse of
    /// <see cref="ServerActivate"/> — see <see cref="IServerHosting"/>.
    /// </para>
    /// </summary>
    public bool IsHibernating { get; private set; }

    /// <inheritdoc/>
    public void SetHibernating(bool hibernating) => IsHibernating = hibernating;

    /// <summary>
    /// Extra lines a mod wants under the engine's <c>status</c>, beyond the ones
    /// this class produces itself. Empty by default.
    /// </summary>
    public IList<string> ExtraStatusLines { get; } = new List<string>();

    /// <inheritdoc/>
    public IEnumerable<string>? StatusLines
    {
        get
        {
            // The map is what a person typing `status` is most often checking,
            // and it is the one fact this class holds that the engine's own
            // status lines do not already print.
            var lines = new List<string>
            {
                _currentMap.Length == 0
                    ? "sourcesharp : no map loaded"
                    : $"sourcesharp : map {_currentMap} ({_loadType})",
            };

            lines.AddRange(ExtraStatusLines);
            return lines;
        }
    }

    // ---- plan_gamedll_after.md Phase A2: Part 11a2's three gaps ------------
    //
    // Three IServerGameDLL methods whose shell bodies were `{}` and which
    // carried no TODO, so nothing in the last plan was going to catch them.
    // They are implemented rather than refused because all three are REACHED BY
    // DESIGN -- which is also why not one of them is counted.

    /// <summary>
    /// How many times the engine has flushed the model cache since this object
    /// was built. Nothing in the game reads it; it is the only evidence a live
    /// instance can offer that the method is reached at all, which is exactly
    /// the question A2 had to answer and could not answer from an empty body.
    /// </summary>
    public int MdlCacheInvalidations { get; private set; }

    /// <inheritdoc/>
    public void InvalidateMdlCache()
    {
        MdlCacheInvalidations++;

        // IT SAYS SO, and that is the point rather than chatter. The whole of
        // Phase A2's difficulty was that an empty body and a working one are
        // indistinguishable from outside -- Part 7's rule, applied to a method
        // nobody could refuse. A flush is rare (an operator, or a model
        // reload), so one line each is a measurement and not noise.
        // THE WHOLE PORT, and it is one line. Stock's gEntList walk, kept
        // native because the CStudioHdr it frees is a native member of
        // CBaseAnimating -- see IServerHosting.InvalidateMdlCache for why Part
        // 11a2's managed IMdlCacheEvents walk is an addition and not this.
        MdlCacheEntitiesInvalidated = _entities.InvalidateMdlCacheAll();
        MdlCacheSkeletonsInvalidated = Skeletons?.InvalidateMdlCache() ?? 0;

        // IT SAYS SO, and that is the point rather than chatter. The whole of
        // Phase A2's difficulty was that an empty body and a working one are
        // indistinguishable from outside -- Part 7's rule, applied to a method
        // nobody could refuse. A flush is rare (an operator, or a model
        // reload), so one line each is a measurement and not noise.
        //
        // AND IT NOW CARRIES THE POPULATION (lane diag-visibility), which is
        // what makes the line a measurement rather than a receipt: the count is
        // the answer to "did this reach anything", and on the CLIENT the same
        // line was reporting a walk that reached ZERO. Printed AFTER the walk
        // for that reason -- there is nothing to report before it runs.
        _console.Msg($"[SourceSharp/Server] model cache flushed (#{MdlCacheInvalidations}): "
                     + $"{MdlCacheEntitiesInvalidated} entit{(MdlCacheEntitiesInvalidated == 1 ? "y" : "ies")} invalidated, "
                     + $"{MdlCacheSkeletonsInvalidated} skeleton(s)\n");
    }

    /// <summary>
    /// How many entities the last flush reached. Zero is the answer that says
    /// the walk found nothing, which is exactly the state this lane found the
    /// client half in and which no counter of flushes can distinguish.
    /// </summary>
    public int MdlCacheEntitiesInvalidated { get; private set; }

    /// <summary>
    /// Whether the Steam game-server interface answered at the last activation.
    /// False before the first one, and false on any server where Steam is not
    /// running — which is the ordinary state of a listen server started outside
    /// the client.
    /// </summary>
    public bool SteamGameServerAvailable { get; private set; }

    /// <summary>How many times Steam has been activated on this server.</summary>
    public int SteamActivations { get; private set; }

    /// <inheritdoc/>
    public void SteamGameServerActivated()
    {
        SteamActivations++;

        // NATIVE FIRST. Clear() then Init() -- gameinterface.cpp:1317 -- and
        // nothing managed may read the context before it has been re-acquired.
        SteamGameServerAvailable = _host.SteamGameServerReacquire();

        // STOCK'S ONE DECISION, AND IT IS THE ONLY REASON THIS IS NOT A NATIVE
        // STEP ON ITS OWN. gameinterface.cpp:1319-1322 asks Steam for gameplay
        // stats when the game server came back AND this is a dedicated server.
        // Both halves are questions with managed answers now, so the `if` reads
        // where the policy is rather than in C++.
        if (SteamGameServerAvailable && _host.IsDedicatedServer)
            _host.SteamGameServerRequestGameplayStats();

        // Same reason as the flush's line: on a server where Steam never comes
        // up this says so, once, rather than leaving "absent" and "never asked"
        // looking identical -- which is the state this method was in.
        _console.Msg($"[SourceSharp/Server] steam game server activated (#{SteamActivations}, "
                     + $"gameserver={(SteamGameServerAvailable ? "yes" : "no")}, "
                     + $"loggedon={(_host.SteamGameServerLoggedOn() ? "yes" : "no")}, "
                     + $"dedicated={(_host.IsDedicatedServer ? "yes" : "no")})\n");
    }

    /// <inheritdoc/>
    public void SteamGameServerShutdown()
    {
        // MANAGED FIRST, NATIVE LAST, which is the inverse of the pair above
        // and is the point rather than an oversight: anything a mod wants to
        // say to Steam has to be said before the context is dropped. Stock
        // spends an entire game-system stage on this ordering
        // (LevelShutdownPreClearSteamAPIContext), so it is not a subtlety.
        SteamGameServerAvailable = false;

        _console.Msg("[SourceSharp/Server] steam game server going away\n");

        _host.SteamGameServerClear();
    }
}

/// <summary>
/// One answer to a <c>StartQueryCvarValue</c>, as the engine handed it back.
/// </summary>
/// <param name="Cookie">The token the query returned.</param>
/// <param name="EntIndex">The player's entity index, or -1 for no edict.</param>
/// <param name="Status">Why there is or is not a value.</param>
/// <param name="Name">The cvar that was asked about.</param>
/// <param name="Value">
/// Its value, or null for every status but <see cref="QueryCvarStatus.ValueIntact"/>.
/// </param>
public readonly record struct QueriedCvar(int Cookie, int EntIndex,
                                          QueryCvarStatus Status,
                                          string Name, string? Value);
