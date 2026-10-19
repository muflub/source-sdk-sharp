//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Interop;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>Server-side game logic. Runs inside ServerALC.</summary>
public sealed class ServerModule : ISideModule, IServerModule, IConVarHost, IHierarchyHost
{
    /// <summary>
    /// This side's game assembly, where every registry's scan starts: its own
    /// declarations and, compiled into it, the shared source's
    /// (plan_side_isolation P7). The scan keeps to this side
    /// (<see cref="LoadedAssemblies.All"/>), so what it finds is exactly the
    /// protocol this side speaks.
    /// </summary>
    private static System.Reflection.Assembly Game => typeof(ServerModule).Assembly;

    // The engine this module is composed over (plan_abi_hotreload D8), handed
    // in by whoever creates it: the Bootstrap passes the side it loaded, and a
    // fact composing two modules passes two. It used to be looked up by side
    // through the static Engine, so two modules composed over one engine.
    private JitWarmup? _jitWarmup;
    private readonly ModuleEngine _engine;

    /// <summary>
    /// This composition's log: its engine's, over the side's console (statics
    /// to zero: it was the static Log). Everything the module builds is handed
    /// it.
    /// </summary>
    private ILog Log => _engine.Log;

    public ServerModule(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        // What the module holds from construction, over its engine's log
        // (statics to zero).
        _tempEnts = new TempEntRegistry(engine.Log);
        _playerCommands = new PlayerCommandQueue(engine.Log);
        _netStates = new NetStateCatalog(Game, engine.Log);
        _layouts = new EntityLayoutRegistry(engine.Log);
    }

    private readonly ServerTicks _ticks = new();
    private readonly ConCommandRegistry _commands = new();
    // Built in ComposeEngineAreas over the side's own routes and this
    // composition's liveness mirror (plan_abi_hotreload D7); until then there is none.
    private ScriptedEntityRegistry _entities = null!;

    /// <summary>
    /// Which entity handles are alive (plan_crossings.md X5). This
    /// composition's own (plan_abi_hotreload D7): fed by the entity dispatch
    /// and the registry's removal, cleared at level shutdown, registered for
    /// the gate commands, and attached to the server's EntityIsValid while
    /// this module is composed.
    /// </summary>
    private readonly LivenessMirror _liveness = new();

    // The attachment of _liveness to the server's EntityIsValid, taken back in
    // Shutdown so a reloaded module's mirror replaces it rather than joins it.
    private IDisposable? _livenessAttachment;

    // The server's descriptor durations, read from the skeletons' studio
    // headers; taken back in Shutdown so the descriptors hold nothing of this
    // load context across a reload.
    private IDisposable? _descriptorAttachment;

    /// <summary>This composition's container, for a fact that composes two.</summary>
    internal IServiceLocator Services => _services;

    /// <summary>
    /// The engine areas, and the two mirrors before them: the render binding
    /// takes this side's <see cref="StandaloneMirror"/> in its constructor, and
    /// the gate commands take <see cref="_liveness"/>. Then
    /// <see cref="_liveness"/> is attached to the server's EntityIsValid until
    /// <see cref="Shutdown"/>. Separate from <see cref="Initialize"/> so a fact
    /// can compose two of these without an engine behind them.
    /// </summary>
    internal int ComposeEngineAreas()
    {
        // The server's own StandaloneMirror, never the client's: its render
        // binding neither records nor reads one (dormancy and the animation
        // cycle are client concepts; see EngineEntityRender.Recording), but two
        // sides are two instances and nothing is shared between them.
        _services.AddInstance(new StandaloneMirror());
        _services.AddInstance(_liveness);

        // Where the render and entity bindings find this side's managed
        // skeletons (plan physics_bones_native_zero step 1.2): registered
        // before the areas so their server constructors take it, and filled in
        // Initialize once the skeletons exist -- they read the engine through
        // those same areas, so they cannot be built first.
        _services.AddInstance(_skeletonRoute);

        // This side's model caches, its engine's own (plan_abi_hotreload D8):
        // the render binding reads them through that engine, and the
        // model-descriptor report and the game DLL's model precache are handed
        // them from here. The frame binding clears them at level shutdown.
        _services.AddInstance(_engine.Models);
        _services.AddInstance(_engine.Breaks);

        // The liveness gate's independent oracle, over this side's own slot.
        _services.AddInstance(new LivenessSlotReference(_engine));

        // This load's Steam game server, handed to the engine host binding the
        // next line builds (P1 run 3: an instance per load, not a static; it
        // binds on first use, so a cs_sv_reload needs no new activation).
        _services.AddInstance<ISteamGameServer>(new SteamGameServerFlat());

        int areas = _engine.AddTo(_services);

        _convars = new ConVarRegistry(_services.Require<IEngineConsole>());
        _userMessages = new UserMessageRegistry(Log, receives: false);

        // What sends a user message by key (plan_side_isolation §2.2a): this
        // side's registry for the id, IShimUserMessages for the engine.
        _services.AddInstance<IUserMessageSender>(new UserMessageSender(_userMessages, new UserMessageOutbox(_engine), Log));

        // The side's clock with its areas (plan_abi_hotreload D8): the registry
        // below stamps outputs, thinks and saves with it, and there is no
        // process-wide default behind this container any more.
        _services.AddInstance<IGameClock>(_engine.Clock());

        // The constraint, spring and motion-controller tables
        // (plan_physics_bones_native_zero 2.4): managed's own, calling vphysics
        // through its vtables. Not engine areas, so registered here beside
        // them, before anything that asks for one. Lifecycle participants
        // (level shutdown, the reload's carry).
        {
            // sv_gravity found once: ConVarRegistry.Get walks every convar, and
            // a medium asks for gravity on each object it acts on, each tick
            // (plan_physics_bones_perf B3: 600 ns of a medium's Simulate).
            SvGravityConVar? gravity = null;
            var jointWorld = new EnginePhysicsJointWorld(_services.Require<IEngineEntityPhysics>(),
                                                         _services.Require<IEngineEntities>(),
                                                         _services.Require<IEngineEnvironment>(),
                                                         _services.Require<IGameClock>(),
                                                         () => (gravity ??= _convars.Get<SvGravityConVar>())?.Value ?? 800f);
            var jointTable = new PhysicsJointTable();
            var vphysicsJoints = new VPhysicsJoints(_engine.VPhysics);
            _physicsConstraints = new PhysicsConstraints(jointTable, vphysicsJoints, jointWorld, Log,
                                                         ev => _physicsRouter?.Post(ev));
            _physicsMotion = new PhysicsMotionControllers(jointTable, vphysicsJoints, jointWorld, Log);
            _services.AddInstance(jointTable);
            _services.AddInstance(_physicsConstraints);
            _services.AddInstance<IEngineConstraints>(_physicsConstraints);
            _services.AddInstance<IEngineSprings>(_physicsConstraints);
            _services.AddInstance(_physicsMotion);
            _services.AddInstance<IEngineMotionControllers>(_physicsMotion);
        }

        // The mods' first look at a word a player's console typed (tf2 stage
        // 0.6). A CHAIN — several mods own different verbs — first true wins,
        // asked in HookOrder's order by OnClientCommand before any of the
        // core's own verbs. Registered HERE rather than beside the other
        // three hook factories in Initialize because its dispatcher is this
        // module itself, and the composition-only harnesses that drive that
        // dispatcher (the 0.6 facts) never reach Initialize.
        _services.AddFactory<IHooks<IClientCommandHook>>(
            locator => new Hooks<IClientCommandHook>(locator, locator.Require<ILog>()));

        // This side's movement runner, over this module's engine
        // (plan_abi_hotreload D8): the player-command binding runs the
        // movement crossing, the command stages and the shadow through it.
        _movement = new MovementRunner(_engine);
        _services.AddInstance(_movement);

        // And its player command queues and shadow run (statics to zero), for
        // the binding the root builds, the player's removal and the two
        // console commands that report them.
        _services.AddInstance(_playerCommands);
        _services.AddInstance(_playerCommands.Shadow);

        // This side's declared networked states by name (statics to zero: it
        // was three static maps every side's Discover wrote), for native's
        // layout asks through the net-state binding.
        _services.AddInstance(_netStates);

        // And the module's engine itself, for what acquires an engine
        // interface through this side's own factory (EngineClient, D8).
        _services.AddInstance(_engine);

        // And the engine interfaces cs_vtable reports, so the command takes
        // them rather than naming the engine (plan_shim_areas_split phase 8).
        _services.AddInstance<IServerEngineInterfaces>(new ServerEngineInterfaces(_engine));

        // Game events are CREATED, FIRED AND FREED through IGameEventManager2's
        // forwarders (plan_server_native_v2 T3), which took over three slots;
        // the rest of the area is the binding's. Without the manager an event
        // is never made, and that is said once here rather than per event.
        if (ServerGameEvents.Acquire(_engine, _services.Require<IEngineGameEvents>(), Log, out string eventsWhy) is { } serverEvents)
            _services.AddInstance(serverEvents);
        else
            Log.Warning($"[SourceSharp/Server] game events: no IGameEventManager2 ({eventsWhy}); no event will be fired\n");

        _gameEvents = new GameEventRegistry(_services.Require<IEngineGameEvents>(), Log);

        // THE ENTITY REGISTRY, over THIS side's three routes and this
        // composition's liveness mirror (plan_abi_hotreload D7): the routes were
        // static classes its constructor bound, so two game instances in one
        // process answered each other's entities. AddTo has just registered the
        // side's own; the registry binds into them, and the hierarchy, the frame
        // driver and the ABI bindings are handed the same.
        _entities = new ScriptedEntityRegistry(_liveness,
                                               _services.Require<ShellLessRoutes>(),
                                               _services.Require<EntityIoRoutes>(),
                                               _services.Require<EntityRemovalRoutes>(),
                                               _services, new ServerEntityEngine(_engine));

        _livenessAttachment = _engine.AttachLiveness(_liveness);

        return areas;
    }

    /// <summary>The registry, whose removal notify writes this composition's liveness mirror.</summary>
    internal ScriptedEntityRegistry Registry => _entities;

    /// <summary>The entity dispatch the root builds, which feeds this composition's liveness mirror.</summary>
    internal EntitiesBinding NewEntitiesBinding()
        => new(_engine.Crossings, Log, this, _liveness, _services.Require<EntityRemovalRoutes>(), _services.Require<ShellLessRoutes>());

    /// <summary>The frame handler the root builds, which clears this composition's liveness mirror at level shutdown.</summary>
    internal FrameBinding NewFrameBinding() => new(_engine.Crossings, Log, this, _liveness, _engine.Models, _engine.Breaks);

    /// <summary>
    /// The server's systems, as everything built afterwards reaches them.
    ///
    /// Filled during <see cref="Initialize"/> in one order and one order only —
    /// convars, then services, then the commands and entities that ask for
    /// either — and read-only from every consumer thereafter. See
    /// <see cref="ServiceContainer"/> for why nothing may add to it later.
    /// </summary>
    private readonly ServiceContainer _services = new();

    // The side's console lines, kept from the console binding's build so the
    // frame and the unload can drain them (console-batch); null before it.
    private ConsoleLines? _consoleLines;

    // What the mods registered (SideServices.Register), disposed at Shutdown.
    private IReadOnlyList<Type> _modServices = [];

    // The hosting services' state this module drives (plan_host_sdk §2.8):
    // the game-thread queue and frame stamp, the delays, the level, the
    // clients' hooks, and whether this level has announced itself ready.
    private readonly GameThread _gameThread = new();
    private readonly FrameScheduler _scheduler = new();
    private readonly LevelInfo _levelInfo = new();
    private ClientLifecycleDriver? _clients;
    private ILog? _log;

    // Log's sink for this side while the module is composed (plan_abi_hotreload
    // D5): the module's own ILog, attached at composition and taken back in
    // Shutdown so Abi's Log keeps nothing of this load context across a reload.
    private IDisposable? _logAttachment;
    private bool _levelReadyPending;

    /// <summary>
    /// Drives IGameLifecycle. Built after every service is registered, because
    /// it resolves its participants once -- see LifecycleDriver.
    /// </summary>
    private LifecycleDriver _lifecycle = null!;

    // The managed skeletons (plan physics_bones_native_zero step 1.2), the
    // route the engine bindings find them by, and the surface table their
    // hitbox trace reads.
    private readonly StandaloneSkeletonRoute _skeletonRoute = new();
    private ServerSkeletons _skeletons = null!;
    private IVtablePhysicsSurfaceProps? _surfaceProps;

    // The live entities of the three classes the engine asks about from
    // outside any one of them: fires (heat in a radius), ragdoll magnets (a
    // death's pull) and surface triggers (a player's footing). Registered in
    // the container for the entities that join them; each was a static list.
    private readonly EntityRoster<EnvFire> _fires = new();
    private readonly EntityRoster<PhysRagdollMagnet> _magnets = new();
    private readonly EntityRoster<EnvPlayerSurfaceTrigger> _surfaceTriggers = new();

    // Map decals waiting to paint (level init) or to be triggered by name.
    private readonly EntityRoster<MapDecal> _pendingDecals = new();
    private INetTrafficProfiler _netTraffic = null!;
    private FrameDriver _frames = null!;

    /// <summary>
    /// The game DLL's managed answer — <see cref="IServerLifetime"/>,
    /// <see cref="IServerLevel"/>, <see cref="IServerFrame"/> and
    /// <see cref="IServerListing"/> in one object. Built in
    /// <see cref="Initialize"/> and resolved once; see ServerGameDll.
    /// </summary>
    private ServerGameDll _gameDll = null!;

    private Arsenal _arsenal = null!;

    // The hitgroup of the hit being dealt (DamageHitGroups): opened by the
    // shooter, read here where the damage op reaches the victim.
    private DamageHitGroups _hitGroups = null!;

    private Flames _flames = null!;
    private ImpulseCheats _impulses = null!;
    private PlayerFlashlight _flashlight = null!;
    private DamageCheats _damageCheats = null!;
    private DevContext _devContext = null!;
    private Scoreboard _scoreboard = null!;

    //-------------------------------------------------------------------------
    // The engine, as this module uses it.
    //
    // Resolved out of its OWN container rather than called statically, so that
    // the rule holds without exception: nothing outside the ABI assembly names
    // Engine except to log. The module is the composition root and could
    // reasonably have been the one place allowed to, but an exception is a
    // thing every later reader has to be told about, and these cost one line
    // each.
    //-------------------------------------------------------------------------
    private IEngineConstraints _constraints = null!;
    private IEngineEntities _entityApi = null!;

    /// <summary>
    /// The player area, for the ONE call this module makes into it directly:
    /// the spawn placement in <see cref="SelectSpawnPoint"/> (step 2e).
    /// </summary>
    private IEnginePlayers _playerApi = null!;
    private IEngineMotionControllers _motion = null!;
    private IEngineNetworking _net = null!;
    private IUserMessageSender _send = null!;
    private IEngineSprings _springs = null!;
    private IEngineVisibility _visibility = null!;
    private IEngineWorldQuery _world = null!;

    /// <summary>worldspawn's chaptertitle key, read here and announced per player. See ChapterTitle.cs.</summary>
    private ChapterTitle _chapterTitle = null!;
    private IGameClock _clock = null!;


    // Null unless the process was started with -debugapi. A development tool that
    // was not asked for should not exist at all, let alone open a socket.
    private IDebugApi? _debugApi;

    /// <summary>The clients already in when this module was built (a cs_sv_reload), invited on its first frame.</summary>
    private DebugChannelReloadInvites _reloadInvites = null!;

    private IConnectedClients _connected = null!;

    // plan_engineapi_vtables D4: the root native acquires this side's
    // managed-built interfaces from, and the object it was handed as.
    private ManagedVtable? _managedRoot;

    /// <summary>
    /// vphysics' IPhysicsObjectEvent on managed's own table (plan_physics_managed
    /// B2.1), built from the side registry so a reload re-patches it in place
    /// while the environment still holds it; and what installs it per level.
    /// </summary>
    private ManagedVtables? _vtables;

    /// <summary>
    /// The managed sound emitter (plan_server_native_v2 T4a), built with the
    /// root because the one native IRecipientFilter every emit hands the
    /// engine lives in a managed block; and that block, caller-held as
    /// vphysics' are.
    /// </summary>
    private ServerSounds? _serverSounds;
    private SoundEmitterSystem? _soundSystem;
    private ManagedVtable? _recipientFilter;

    private ManagedVtable? _physicsObjectEvents;
    private ManagedVtable? _physicsCollisionEvents;
    private ManagedVtable? _physicsCollisionSolver;
    private PhysicsSolver? _physicsSolver;
    private PhysicsFluidEvents? _physicsFluidEvents;
    private PhysicsCallbacks? _physicsCallbacks;

    /// <summary>The constraint and spring table (plan_physics_bones_native_zero 2.4).</summary>
    private PhysicsConstraints? _physicsConstraints;

    /// <summary>The motion-controller table (2.4), whose IMotionEvent the root builds.</summary>
    private PhysicsMotionControllers? _physicsMotion;

    /// <summary>vphysics' IPhysicsConstraintEvent and IMotionEvent, managed-built (2.2), held by the environment and its controllers.</summary>
    private ManagedVtable? _physicsConstraintEvents;
    private ManagedVtable? _physicsMotionEvents;

    // physics_shared.cpp's server half (plan_physics_bones_native_zero 2.6):
    // the solid key table vphysics is handed (stock's g_SolidSetup), and the
    // world build, precache and solid parse CPhysicsHook asks for.
    private ManagedVtable? _solidKeys;
    private PhysicsWorldVPhysics? _physicsWorldVPhysics;
    private ServerPhysicsWorld? _physicsWorld;
    // An entity's physics init (InitPhysics to managed) and its scratch solid_t.
    private EntityPhysicsCreation? _entityPhysics;
    private VPhysicsEntityCalls? _entityPhysicsCalls;
    private ManagedBindings? _bindings;
    private string _currentMap = string.Empty;
    // Built in ComposeEngineAreas over this side's own networking area
    // (plan_abi_hotreload D8), so it registers with the server's engine.
    private UserMessageRegistry _userMessages = null!;
    // Built in ComposeEngineAreas over this side's own event area
    // (plan_abi_hotreload D8), so it subscribes and reads through the server's
    // engine and never whichever side is active.
    private GameEventRegistry _gameEvents = null!;

    // This side's movement runner, built in ComposeEngineAreas (D8).
    private MovementRunner _movement = null!;

    // The three movement decisions and the constants, resolved once after the
    // mods registered (phase 0.3). The ABI getters answer from its frozen
    // pack; the loop reaches the same pack through the binding's closure over
    // these getters, so server and client of a hooked mod see one answer.
    private MovementRulesResolver _movementRules = null!;

    // This side's entity payload reader, built over its own areas (D8).
    private NetPayloadReader _payloadReader = null!;

    // Every [StringTable] declaration. Built in Initialize rather than here
    // because it needs the container to ask for the id-addressed engine area --
    // which is internal on purpose, so that a public constructor naming it
    // would be the one place mod code could get hold of a TABLEID again.
    private StringTableRegistry _stringTables = null!;

    // Every temp entity declared in the shared assembly, discovered by
    // attribute. Held by the module because it is filled during Initialize and
    // then never changes: a ServerClass may only be built before the engine
    // collects the class list, so there is nothing to invalidate and nothing to
    // rebuild at a level change.
    private readonly TempEntRegistry _tempEnts;

    // The server's temp entity engine (plan_server_native_v2 T4b): the tables'
    // plans, built during the DLL-init window; its sender is attached with the
    // recipient filter, in BuildSounds.
    private ServerTempEntEngine? _tempEntEngine;

    /// <summary>
    /// Pack a native forward into the blob its class expects. The send stays
    /// native because the filter belongs to the caller, so this hands bytes back
    /// rather than playing anything.
    /// </summary>
    public int BuildForwardBlob(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints,
                                Span<byte> into, out int classHandle)
    {
        classHandle = -1;

        TempEntState? state = TeForwardDispatch.Build((TeForward)id, floats, ints);

        if (state is null)
            return -1;

        TempEntType? type = _tempEnts.Find(TempEntityAttribute.ClassNameOf(state.GetType()));

        if (type is null)
            return -1;

        byte[] blob = state.ToBlob(type);

        // REFUSED RATHER THAN TRUNCATED. A short write is a blob whose tail is
        // whatever native had there, decoded as fields -- which is worse than
        // nothing happening.
        if (blob.Length > into.Length)
            return -1;

        blob.AsSpan().CopyTo(into);
        classHandle = type.Handle;

        return blob.Length;
    }
    private GameRules? _rules;
    // Built in Initialize, through the container: it takes three engine areas.
    private Pickups _pickups = null!;
    private Corpses _corpses = null!;

    /// <summary>A death in order: the body, the record, then the mods' <see cref="IPlayerDeathHook"/>s.</summary>
    private PlayerDeaths _deaths = null!;

    // plan_gamedll_after.md Phase A3: the two "C" game systems, which are
    // IGameLifecycle participants like the four above AND the subjects two of
    // the shell's shims resolve. Built in Initialize through the container --
    // both take engine areas.
    private DecalEmitter _decals = null!;
    private PropData _propData = null!;
    private TemplateStore _templates = null!;
    private GlobalStateTable _globalState = null!;
    private MapSpawnOrder _mapSpawnOrder = null!;
    private SoundEnt _soundEnt = null!;
    private SoundController _soundController = null!;

    /// <summary>The viewer's world fog — plan_entity.md step 8e-render.</summary>
    private WorldFog _worldFog = null!;
    private WorldSkybox _worldSkybox = null!;
    private PlayerClientData _playerClientData = null!;

    // plan_server.md Phase 8b / plan-audit C4. IServerGameEnts' managed half:
    // which hierarchy answers for a client, and in which order. Built the same
    // way the four above are, because it takes an engine area.
    private ServerEntities _serverEntities = null!;
    private ServerClients _serverClients = null!;

    /// <summary>
    /// plan_gamedll_after.md Phase A3's fifth system — and, since step 2h went
    /// live, the one thing this module IS asked for.
    ///
    /// <para>
    /// It used to be held because every Create'd service is. Parenting changed
    /// that: the eleven <c>Hierarchy*</c> thunks in <c>Entry.cs</c> have to reach
    /// the level's registry, and <see cref="IHierarchyHost"/> is how — a cast on
    /// <c>s_ServerGame</c>, which is this object. So the field is now read.
    /// </para>
    /// </summary>
    private StandaloneHierarchy _standalone = null!;

    /// <summary>
    /// The managed map spawner — plan_entity.md step 2d. Held because
    /// <c>ServerGameDll</c> takes it and because <c>debug-api status</c> reads its
    /// counters.
    /// </summary>
    private MapSpawner _mapSpawner = null!;

    private EntityFactories _factories = null!;

    private PlayerShadowConVar? _playerShadow;

    /// <summary>
    /// This composition's player command queues, counters and shadow run
    /// (statics to zero): registered for the commands that read them and
    /// handed to the player-command binding.
    /// </summary>
    private readonly PlayerCommandQueue _playerCommands;

    /// <summary>This side's declared networked states by name and type id (statics to zero).</summary>
    private readonly NetStateCatalog _netStates;

    /// <summary>
    /// The movement integrators — plan_entity.md step 2f.
    ///
    /// <c>_entityMotion</c> and not <c>_motion</c>, which is already taken by
    /// <see cref="IEngineMotionControllers"/> — the SOLVER's motion
    /// controllers, an unrelated thing with a colliding name.
    /// </summary>
    private EntityMotion _entityMotion = null!;

    /// PARENTING, for the thunks. Null before <c>Start</c> has built the
    /// services, which is a state the parse can reach — see
    /// <see cref="IHierarchyHost"/>.
    /// </summary>
    HierarchyDriver? IHierarchyHost.Hierarchy => _standalone?.Parenting;

    /// <summary>
    /// Which soundscape each player is hearing.
    ///
    /// Held by the module rather than as a static, because the module is what
    /// drives its per-tick pass and drops it at a level change. The soundscape
    /// entities and the trigger volumes ask the container for it.
    /// </summary>
    // Built in Initialize rather than here, because it takes three engine areas
    // and the container that holds them does not exist at field-initialiser
    // time. Non-null from the first line of Initialize onwards.
    private Soundscapes _soundscapes = null!;
    private readonly EntityLayoutRegistry _layouts;

    /// <summary>This side's protocol-list hashes, sent with the layout check (plan_side_isolation §2.1). Built at the first send.</summary>
    private ProtocolManifest? _protocol;

    // What the dev API's schema view reports, built on first ask. Fixed after
    // DLL init -- a ServerClass may only be made before the engine collects the
    // class list -- so there is nothing to invalidate.
    private IReadOnlyList<NetSchemaType>? _netSchema;

    private NetTableProfileConVar? _netTableProfile;

    /// <summary>
    /// Class name to declared fields, for the traffic view's attribution.
    ///
    /// Built once off the schema this module already describes, rather than
    /// walking it per entity per frame: the profiler asks this for every entity
    /// on every sampled frame, and a linear search over two hundred types there
    /// would cost more than the diff it is annotating.
    /// </summary>
    private Dictionary<string, IReadOnlyList<NetSchemaField>>? _netFieldsByClass;
    // Built in ComposeEngineAreas over this side's console (plan_abi_hotreload
    // D8), not over whichever side is active.
    private ConVarRegistry _convars = null!;
    private GreetingConVar? _greeting;
    private VerboseJoinsConVar? _verboseJoinsVar;
    private DamageScaleConVar? _damageScale;
    private ArmourRatioConVar? _armourRatio;
    private ArmourBonusConVar? _armourBonus;
    private SpawnHealthConVar? _spawnHealth;
    private AllowFlashlightConVar? _allowFlashlight;
    private BannedNamesConVar? _bannedNames;
    private FriendlyFireConVar? _friendlyFire;


    public void Initialize(Side side)
    {
        Log.Msg("[SourceSharp/Server] managed server module initialized\n");
        SharedInfo.Announce(side, Log);

        // CONVARS FIRST, and the order here is now load-bearing rather than
        // incidental. A command or an entity may ask for a convar by class or
        // by name, and a container that does not hold one yet refuses the class
        // that wanted it — at load, with a message naming it, which is the
        // right end of the game to find that out at but only if the leaves went
        // in first.
        //
        // Every loaded assembly, like the rest of discovery — but see
        // ConVarRegistry before declaring one in Shared: both sides would find
        // it, register it from both DLLs, and Source rejects the second.
        // THE ENGINE FIRST, before even the convars. It is the deepest leaf in
        // the container — everything else can want it and it wants nothing —
        // and registering it here means no later line has to think about order.
        //
        // Twenty-two areas rather than one interface, so that a class says
        // which part of the engine it touches. A trigger taking IEngineDamage
        // has told a reader what it does to whoever walks in, before they open
        // the body.
        int engineAreas = ComposeEngineAreas();

        // THE PROCESS' OWN COMMAND LINE, read from /proc/self/cmdline: -tickrate
        // and -debugapi. It was two engine slots, retired for this reader.
        _services.AddInstance<ILocalCommandLine>(new ProcessCommandLine());

        // THE CORE SERVICES A MOD REACHES BY INJECTION (plan_host_sdk §2.8):
        // generic, and here rather than in a mod so every mod gets the same.
        // The side's ILog is its engine's (EngineServices.AddTo registered it
        // with the areas); this composition's plumbing prints through it too.
        var log = _services.Require<ILog>();
        _log = log;
        _services.AddFactory<IHooks<IPlayerDeathHook>>(locator => new Hooks<IPlayerDeathHook>(locator, log));
        _services.AddFactory<IHooks<IRespawnGate>>(locator => new Hooks<IRespawnGate>(locator, log));
        _services.AddFactory<IHooks<IRoundEndGate>>(locator => new Hooks<IRoundEndGate>(locator, log));
        // THE MOVEMENT RULES (phase 0.3) are a SINGLE-OWNER hook: a mod marks
        // its IMovementRules [SideService] like any other service, and the
        // MovementRulesResolver (asked once, below the mods' registration)
        // walks this container's registrations itself rather than through an
        // IHooks chain — a chain's order-uniqueness rule is about sequencing
        // a dispatch, and this hook has no dispatch: a second owner is
        // refused, never ordered.
        // tf2 stage 0.5: the join policy is single-owner and, like the
        // movement rules, NOT a chain: Teams resolves it from this container's
        // registrations when the module asks, below the mods' scan. The spawn
        // selector is a chain asked in HookOrder's order, lazy, so a hook may
        // take the services Teams itself needs.
        _services.AddFactory<IHooks<ISpawnSelector>>(locator => new Hooks<ISpawnSelector>(locator, log));
        // tf2 stage 0.7: the mode selector is a chain (first non-null answer
        // names the mode; the core's "null" answers last inside GameModes,
        // not as a registered selector). Lazy, so a selector may take the
        // services its own mode needs.
        _services.AddFactory<IHooks<IGameModeSelector>>(locator => new Hooks<IGameModeSelector>(locator, log));
        // The per-player composite's sources (tf2 stage 0.7 part B): every
        // mod's IPlayerComponentSource, TF2's and Descent's alike, asked once
        // per player and merged. A chain because that is the ruling — two
        // mods bolt components onto the same player.
        _services.AddFactory<IHooks<IPlayerComponentSource>>(locator => new Hooks<IPlayerComponentSource>(locator, log));
        // The mods' damage rules (tf2 stage 0.7 part C): a chain — immunity
        // zones, invulnerability windows and god-mode all refuse for their own
        // reason, first actual answer wins, and none of them replaces the
        // core's own two guards (the directions are argued on IDamageRule).
        _services.AddFactory<IHooks<IDamageRule>>(locator => new Hooks<IDamageRule>(locator, log));
        // The mods' spawn kit (tf2 stage 0.7 part C): a chain — the last
        // provider that names a column owns it, so a class mod and a fun-mod
        // override can coexist without editing each other (the directions
        // are argued on ILoadoutProvider).
        _services.AddFactory<IHooks<ILoadoutProvider>>(locator => new Hooks<ILoadoutProvider>(locator, log));
        _logAttachment = _engine.AttachLog(log);
        _services.AddInstance<IProcessEnvironment>(new ProcessEnvironment());
        _services.AddInstance<IModInfo>(new ModInfo());
        _services.AddInstance<IGameBuild>(new GameBuild(_engine.Build));
        _services.AddInstance<IGameThreadDispatcher>(_gameThread);
        _services.AddInstance<IGameThreadLiveness>(_gameThread);
        _services.AddInstance<IFrameScheduler>(_scheduler);
        _services.AddInstance<ILevelInfo>(_levelInfo);
        _services.AddInstance(EngineServices.ClientInfo(_engine));
        _services.Add<IConnectedClients, EngineClients>();
        _services.Add<IBoundPort, ProcBoundPort>();
        _services.Add<IServerConsole, ServerConsole>();
        _services.Add<IChat, Chat>();
        _services.Add<IScreenEffects, ScreenEffects>();
        _services.Add<ICloseCaptions, CloseCaptions>();

        // THE MANAGED-BUILT INTERFACES native acquires by name through the root
        // LoadSideV2 hands back (plan_engineapi_vtables D4). Areas register here.
        _bindings = new ManagedBindings();
        _services.AddInstance(_bindings);

        // D5: the shell game DLL's runtime, built from this module's contracts.
        _bindings.Add("IManagedRuntimeServer",
                      v => ServerInteropAreas.Runtime(v, _engine.Crossings, Log, this));
        _bindings.Add("IManagedPhysicsFrame",
                      v => ManagedAreaFactory.Create(v, new PhysicsFrameBinding(_engine.Crossings, () => _physicsFrame?.Frame(),
                                                                                () => _physicsWorld)));
        // The engine's console prints, forwarded in process by the display
        // func native installs on ICvar at area acquire (docs/todo.md's
        // console-capture item): the sink feeds the side's ConsoleLines,
        // resolved as a service — the module must not name EngineSide
        // (EngineAreaTests' audit).
        _bindings.Add("IManagedConsoleOutput",
                      v => ManagedAreaFactory.Create(v, new ConsoleDisplayBinding(_engine.Crossings, _consoleLines = _services.Require<ConsoleLines>())));
        _bindings.Add("IManagedResources",
                      v => ManagedAreaFactory.Create(v, new ResourcesBinding(_engine.Crossings, Log, this)));
        _bindings.Add("IManagedSaveRestore",
                      v => ManagedAreaFactory.Create(v, new SaveRestoreBinding(_engine.Crossings, Log, this,
                                                                               new SaveGameSession(Log, () => EngineServer.Acquire(_engine, out _) as ISaveMemory))));
        // The standalone player's move helper's two sounds (plan_server_native_v2
        // T4a), to the emitter BuildManagedRoot builds.
        _bindings.Add("IManagedSoundEmitter",
                      v => ManagedAreaFactory.Create(v, new SoundEmitterBinding(_engine.Crossings,
                                                                                new MoveSoundsOf(() => _serverSounds),
                                                                                new SoundSystemOf(() => _soundSystem, Log))));
        _bindings.Add("IManagedSoundEnt",
                      v => ManagedAreaFactory.Create(v, new SoundEntBinding(_engine.Crossings, this)));
        _bindings.Add("IManagedHierarchy",
                      v => ManagedAreaFactory.Create(v, new HierarchyBinding(_engine.Crossings, this)));
        _bindings.Add("IManagedEntities",
                      v => ManagedAreaFactory.Create(v, NewEntitiesBinding()));
        _bindings.Add("IManagedClients",
                      v => ManagedAreaFactory.Create(v, new ClientsBinding(_engine.Crossings, Log, this)));
        _bindings.Add("IManagedEvents",
                      v => ManagedAreaFactory.Create(v, new EventsBinding(_engine.Crossings, this)));
        _bindings.Add("IManagedUserMessages",
                      v => ManagedAreaFactory.Create(v, new UserMessagesBinding(_userMessages)));
        _bindings.Add("IManagedTempEnts",
                      v => ManagedAreaFactory.Create(v, new TempEntsBinding(_engine.Crossings, Log, this)));
        _bindings.Add("IManagedDiagnostics",
                      v => ManagedAreaFactory.Create(v, new DiagnosticsBinding(_engine.Crossings, Log)));
        _bindings.Add("IManagedCommands",
                      v => ManagedAreaFactory.Create(v, new CommandsBinding(_engine.Crossings, this, this, this)));
        _bindings.Add("IManagedNetState",
                      v => ManagedAreaFactory.Create(v, new NetStateLayoutBinding(_engine.Crossings, Log, _netStates)));
        _bindings.Add("IManagedPlayerCommands",
                      v => ManagedAreaFactory.Create(v, new PlayerCommandsBinding(_engine.Crossings, Log, this, this, _movement, _playerCommands,
                                                             () => _playerShadow?.Value ?? true)));
        _bindings.Add("IManagedFrame",
                      v => ManagedAreaFactory.Create(v, NewFrameBinding()));
        // The engine's hitbox traces against a standalone entity's skeleton
        // (plan physics_bones_native_zero step 1.1). Built when native
        // acquires the area, by which time the skeletons exist.
        _bindings.Add("IManagedBones",
                      v => ManagedAreaFactory.Create(v, new BonesBinding(_engine.Crossings, _skeletons)));

        // The clock is registered in ComposeEngineAreas, beside the engine
        // areas: it is one of the same leaves, and the registry built there
        // stamps with it (plan_abi_hotreload D8).

        // Where an entity's own random stream is seeded from: one source per
        // side, seeded once from the engine's uniform stream, so no two
        // entities replay the same pattern (logic_case, logic_timer,
        // ambient_generic).
        _services.AddInstance<IRandomSeeds>(RandomSeeds.FromEngine(_services.Require<IEngineHost>()));

        // The size tiers native registered. An instance rather than a static
        // cache, so two loads cannot share one table and no test can change it
        // for every other.
        _services.AddInstance<INetTiers>(_services.Create<NetTiers>());

        // The frame counter cs_status reads. The module's own; it was a static
        // so that a command could reach it without a module reference.
        _services.AddInstance<IServerTicks>(_ticks);

        // The rosters of live fires, ragdoll magnets and surface triggers.
        _services.AddInstance<IEntityRoster<EnvFire>>(_fires);
        _services.AddInstance<IEntityRoster<PhysRagdollMagnet>>(_magnets);
        _services.AddInstance<IEntityRoster<EnvPlayerSurfaceTrigger>>(_surfaceTriggers);
        _services.AddInstance<IEntityRoster<MapDecal>>(_pendingDecals);

        // Which point_viewcontrol holds each player's view: the cameras' shared
        // table, which was a static dictionary on TriggerCamera.
        _services.AddInstance<IViewControlHolders>(new ViewControlHolders());

        // The vote-index sequence every vote_controller draws from: it was a
        // static counter on the class.
        _services.AddInstance<IVoteIndices>(new VoteIndices());

        // What this side's publish path could not put on the wire, read by the
        // dev API's schema panel. The module's own ledger.
        _services.AddInstance<INetTypeRefusals>(new NetTypeRefusals());

        // The game events' own objects (GameEvents.cs), this module's and no
        // other's. Created here, beside the leaves, because their subscribers —
        // the scoreboard, the rules, the logic_eventlistener feed — take them
        // through their constructors and are built long before the native
        // subscription further down (_gameEvents.RegisterAll), which is where it
        // always was. No engine call.
        GameEventRegistry.AddEventsTo(_services, Game, Log);

        // The live logic_eventlistener entities and the events that reach them.
        _services.AddInstance<IGameEventFeed>(_services.Create<GameEventFeed>());

        // The inventory authority (Phase 10): who owns which weapons, with how
        // much ammunition, and the per-command machine that runs the active
        // one. Registered as an instance so the weapon pickups can ask for it,
        // and IEnginePlayers is re-registered DECORATED so the loadout, the
        // ammo crates and the death drop reach the managed inventory for
        // hierarchy players without knowing it exists.
        // The directory first: the Arsenal navigates from a handle to the
        // managed instance behind it, and is built two lines down.
        _services.AddInstance<IEntityDirectory>(_entities);

        // The registry's STORAGE, separately, because a different thing asks
        // for it: StandaloneHierarchy runs its think heap once a frame
        // (plan_entity.md §3.4, step 1g), and what it needs is the array and
        // the heap rather than the find-by-name face IEntityDirectory is.
        // §3.5 has them as one object and 2a merges them; today the registry
        // owns the one below and answers the interface above.
        _services.AddInstance(_entities.Directory);

        // AND THE SDK HIERARCHY'S HALF OF ONE I/O DELIVERY -- the sub-ioqueue
        // lane. The managed directory holds every entity this port makes; it
        // does not hold CEntityDissolve, CRagdollProp, CRagdollBoogie or
        // CPhysicsEntitySolver, which the compiled stock TUs still construct at
        // runtime and which CEventQueue::ServiceEvents used to be the only
        // thing that reached. The queue is managed now, so the walk is asked
        // from here -- ONLY when the directory answered nothing, so an input
        // cannot be delivered twice, and never on an idle frame.
        //
        // Bound here rather than in the registry's constructor because the
        // registry is in SourceSharp.Abi and has no engine area to ask; this
        // module is where an IEngineFrame is in scope.
        // NOTHING IS BOUND: IShimFrame.DeliverToSdkEntities is a retired slot.
        // The leaf existed to reach four stock classes the directory does not
        // hold -- the dissolver, the two ragdoll kinds and the physics solver --
        // and every one of their translation units is absent from the armed
        // build, so the hierarchy it walked has no members. The managed
        // directory is the whole population, and EntityIoRoutes already warns
        // once per distinct target and input that nothing took it, which is the
        // better message because it names the target.

        // The movement integrators (plan_entity.md step 2f, EntityMotion.cs).
        // After the directory, which it navigates to deliver an impact and to
        // notice an entity that has gone; before StandaloneHierarchy, which
        // runs it once a frame.
        // What a mover does to whatever is in its way (CPhysicsPushedEntities),
        // over the engine areas; the integrator below drives it.
        _services.AddInstance<IPushWorld>(_services.Create<EnginePushWorld>());
        _services.AddInstance(_services.Create<PhysicsPushedEntities>());
        _entityMotion = _services.Create<EntityMotion>();
        _services.AddInstance(_entityMotion);

        // AND IT IS THE BRUSH MOVER TOO -- plan_entity.md step 2g. Every mover
        // in the game reaches its stepper through ScriptedEntity.MoveTo /
        // RotateTo / Spin / StopMoving, which resolve IEngineBrushMovement;
        // registering the managed integrator for that contract puts it behind
        // all four without one of the two dozen brush classes changing a line.
        //
        // One member of the contract -- a breakable surface's pane edges -- is
        // geometry rather than movement, and its slot is CLIENT-only; the
        // adapter refuses it out loud rather than reaching for a slot this side
        // does not fill.
        _services.AddInstance<IEngineBrushMovement>(new ManagedBrushMovement(Log, _entityMotion));

        // THE BLAST TRAVERSAL (shim wave, lane policy-rules). Registered as an
        // instance so that every entity that can explode -- the rocket, the
        // grenade, the energy ball, the cannister, a breaking prop_physics,
        // env_explosion and point_hurt -- reaches ONE object rather than
        // carrying the five engine areas the sweep needs in its own
        // constructor. It replaces the outbound SourceSharp_RadiusDamage /
        // SourceSharp_ExplosionDamage pair: the falloff, the line of sight,
        // the blocking rule and the water rule are managed now. See
        // BlastSweep and BlastRules.
        // What the hit being dealt was inflicted by (a blast's grenade): read by
        // the player's Damage message, opened by the blast sweep.
        _services.AddInstance(new DamageInflictors());
        // Its hitgroup (a bullet's trace): opened by Arsenal.FireBullets, read
        // where the damage op reaches the victim (EntityOp.Damage below).
        _hitGroups = new DamageHitGroups();
        _services.AddInstance(_hitGroups);
        // UTIL_FindClientInPVS's check client, for the radiation triggers.
        _services.AddInstance(_services.Create<PvsCheckClient>());
        _services.AddInstance(_services.Create<DeathNotice>());
        _services.AddInstance(_services.Create<PlayerNames>());
        // Stock's observer mode, the dead CBasePlayer arm (the death cycle and spec_*).
        // Its convars are registered later in Initialize; it reads them when first asked.
        _movement.ObserverTargetPose = ObservedPose;
        _services.AddInstance(new Observers(Log, _entities, _services.Require<IEngineEntities>(), _services.Require<IEnginePlayers>(),
                                            _services.Require<IEnginePlayerMovement>(), _services.Require<IEngineEntityRender>(),
                                            _services.Require<IEngineDamage>(), _services.Require<IEngineWorldQuery>(),
                                            _services.Require<IEngineEntityPhysics>(), _convars,
                                            _services.Resolve<IConnectedClients>()));

        // The weapon and ammo tables THIS side reads, registered before the
        // arsenal is built so the container hands the same objects to it, to
        // anything else that resolves a row, and — after the mod assemblies
        // have registered their sources — to nothing else entirely: the tables
        // ask their sources lazily, on the first read, which is the same rule
        // Hooks<T> lives by. Wire indices are made against THIS list; a second
        // copy elsewhere would renumber the wire.
        _services.AddInstance(new WeaponCatalog(() => _services.ResolveAll<IWeaponTypeSource>(), Log));
        _services.AddInstance(new AmmoCatalog(() => _services.ResolveAll<IAmmoTypeSource>(), Log));
        _services.AddInstance(new WeaponBehaviours(() => _services.ResolveAll<IWeaponBehaviourSource>(), Log));

        // The animation layer chain's composition (phase 0.10): the mods'
        // IAnimStateLayer services, asked lazily on the first player's chain,
        // and the core's HL2 layer. The client registers the same.
        _services.AddInstance(new SourceSharp.Shared.Anim.AnimStateLayers(
            () => _services.ResolveAll<SourceSharp.Shared.Anim.IAnimStateLayer>()));

        _arsenal = _services.Create<Arsenal>();
        _services.AddInstance(_arsenal);
        _services.AddInstance<IWeaponGiver>(_arsenal);

        // Setting things alight (13e). Registered as an instance so that
        // env_entity_igniter can ask for one, and so that the ENGINE's own
        // ignites -- CBaseAnimating::Ignite and env_shooter's flaming gib,
        // which arrive through OnEntityIgnite -- reach the same object rather
        // than a second copy with a second view of what is already burning.
        // After the directory above, which it navigates.
        _flames = _services.Create<Flames>();
        _services.AddInstance(_flames);

        // The impulse cheats (stock's CheatImpulseCommands, gone with
        // player.cpp at the -ss_player flip). Built AFTER the arsenal it gives
        // through, and BEFORE the players decorator below on purpose: the
        // alive check wants the plain answer, not the arsenal's bookkeeping.
        _impulses = _services.Create<ImpulseCheats>();

        // The player's flashlight (C7 cluster 12): impulse 100's toggle, ahead
        // of the cheats as stock's ImpulseCommands is, and the death and
        // player_speedmod turn-offs. Before the players decorator, for the
        // impulse cheats' reason; cs_sv_allow_flashlight is wired to Allowed
        // once the ConVars are registered.
        _services.AddInstance<IEntityEffectBits>(_services.Create<EngineEntityEffectBits>());
        _flashlight = _services.Create<PlayerFlashlight>();
        _services.AddInstance<IPlayerFlashlight>(_flashlight);

        // The damage-rule cheats (stock's god and buddha, whose client.cpp
        // registrations no-op on a standalone player). The toggles live here;
        // the rule they set lives on SsPlayer, in OnDamaged.
        _damageCheats = _services.Create<DamageCheats>();

        // IN THE CONTAINER, because the console words that reach these toggles
        // are [ConCommand] classes now (plan_shim.md §5 3b, lane 3c-entityshell).
        // god and buddha were the last two stock spellings still registered in
        // C++, and their CON_COMMANDs did nothing but carry the word here
        // through the client-command crossing. GodCommand and BuddhaCommand ask
        // the container for this instance, so there is one set of toggles
        // whichever door the word came in by -- the registered name, or
        // OnClientCommand for a word typed at a client console.
        _services.AddInstance(_damageCheats);

        _services.AddInstance<IEnginePlayers>(
            new ArsenalPlayers(_services.Require<IEnginePlayers>(), _arsenal));

        // The round's tally. An instance the module owns rather than a static,
        // so two loads cannot share one board.
        _scoreboard = _services.Create<Scoreboard>();
        _services.AddInstance<IScoreboard>(_scoreboard);

        // Straight after the engine, because it takes three areas of it. Built
        // THROUGH the container rather than with new, so it is handed the same
        // engine every entity gets.
        // BEFORE the Create calls below, not with the other contract
        // registrations further down. Players takes IPlayerButtons now -- the
        // death cycle's "is this player asking to come back" rule moved onto it
        // -- and Create resolves constructor arguments EAGERLY, so a
        // registration that comes after the Create throws at server startup.
        // _buttons is a field initialiser, so it exists this early.
        _services.AddInstance<IPlayerButtons>(_buttons);

        _soundscapes = _services.Create<Soundscapes>();
        _pickups = _services.Create<Pickups>();
        _corpses = _services.Create<Corpses>();
        _players = _services.Create<Players>();
        _deaths = new PlayerDeaths(_log, _services.Require<IEngineEntities>(), _corpses, _players,
                                   _services.Require<IGameClock>(), _services.Require<IHooks<IPlayerDeathHook>>(),
                                   _services.Require<DeathNotice>(), _flashlight);

        // plan_gamedll_after.md Phase A3 -- the two "C" game systems that became
        // managed. Both are IGameLifecycle participants in the PRE-ENTITY half,
        // so both are found by the driver below rather than being called from
        // anywhere in this file; what makes them reachable at all is the
        // ServerMapLoadPreEntity slot, which had no counterpart before A3.
        //
        // Registered by CONTRACT as well as held, because the shell's two shims
        // -- decalsystem and CPropData::GetRandomChunkModel -- resolve them
        // through IServerModule and must not see the participant half.
        _decals = _services.Create<DecalEmitter>();
        _propData = _services.Create<PropData>();
        _services.AddInstance<IServerDecals>(_decals);
        _services.AddInstance(_services.Create<BloodDecals>());
        // The skin a server entity is drawn with, sent as m_nSkin (2026-10-19).
        _services.AddInstance<IServerSkins>(new ServerSkins(_engine));
        _services.AddInstance<IServerPropData>(_propData);

        // THE SURFACE PROPERTY MANIFEST, once, before any map -- plan_server_novalve.md
        // P2.4. It was PhysParseSurfaceData in CPhysicsHook::Init (tier1's
        // KeyValues over the manifest); the files it names still go to VPhysics'
        // own parser, from here.
        IVtablePhysicsSurfaceProps? surfaceProps = PhysicsSurfaceProps.Acquire(_engine, out string surfaceWhy);
        if (surfaceProps is null)
            Log.Warning($"[SourceSharp/Server] no VPhysics surface table to parse into: {surfaceWhy}\n");
        else
        {
            Log.Msg($"[SourceSharp/Server] surface properties: " +
                    $"{_services.Create<SurfacePropertyManifest>().Load(surfaceProps)} file(s) parsed\n");

            // The skeletons' hitbox trace names a hit's surfaceprop from it.
            _surfaceProps = surfaceProps;
        }

        // The same phase's second pair. CSoundEnt differs from those two in one
        // way that shows here: it has MANAGED callers as well as the shim's --
        // env_microphone reads the list and ai_sound writes it, and both used
        // to reach it through two IEngineSound slots that this phase deleted.
        // So the contract registration is not only for the shim; it is what
        // those two entities resolve.
        _soundEnt = _services.Create<SoundEnt>();
        _services.AddInstance<IServerSoundEnt>(_soundEnt);
        // After the ledger, which every blast announces itself to. Nothing
        // resolves the sweep before the entities that explode are spawned.
        _services.AddInstance<IBlastSweep>(_services.Create<BlastSweep>());

        // Sound envelopes. The shim asks it everything for the patches native
        // code builds; FrictionSounds is the one managed caller, through
        // IEngineSound.SoundPatchCreate (physics stock parity D2). Registered by
        // contract, so that each of the thirteen thunks resolves exactly one
        // subject.
        _soundController = _services.Create<SoundController>();
        _services.AddInstance<IServerSoundController>(_soundController);

        // WHAT FOG EACH VIEWER IS STANDING IN (plan_entity.md step 8e-render).
        //
        // Registered by its OWN TYPE and not behind a contract, because it has
        // exactly one caller and that caller is the frame driver: it implements
        // IFrameLifecycle and IGameLifecycle and nothing managed asks it
        // anything. `cs_sv_fog_report` reads it back for the gate.
        //
        // IT MUST BE REGISTERED FOR THE FOG TO REACH ANYONE, which is the whole
        // failure mode: a service with a lifecycle, a driver and its own tests
        // and no registration passes every check that exists and does nothing.
        // That is the state this tree has already found TWICE (OnLevelInitPre-
        // Entity, then OnPreClientUpdate itself), so
        // TheFogServiceIsRegisteredWithTheFrameDriver asserts the registration
        // rather than the class.
        _worldFog = _services.Create<WorldFog>();
        _services.AddInstance(_worldFog);

        // WHAT 3D SKYBOX EVERY PLAYER IS TOLD ABOUT (the defect-skybox lane).
        // The fog's twin, registered on the same terms and with the same
        // failure mode: a service with a lifecycle, a driver and its own tests
        // and NO REGISTRATION passes every check that exists and does nothing,
        // and this tree has already found that shape twice. So
        // TheSkyboxServiceIsRegisteredWithTheFrameDriver asserts the
        // registration rather than the class.
        _worldSkybox = _services.Create<WorldSkybox>();
        _services.AddInstance(_worldSkybox);

        // EACH PLAYER'S CLIENT-DATA UPDATE: the frame's Damage message (stock's
        // CHL2_Player::UpdateClientData). The fog's failure mode again: without
        // this registration hits are summed and never sent. The live gate's
        // damage-message step is what proves it registered: it reads the
        // client's received Damage count after a hurt.
        _playerClientData = _services.Create<PlayerClientData>();
        _services.AddInstance(_playerClientData);

        // IServerGameEnts' managed half (plan_server.md Phase 8b, plan-audit C4).
        // ONE kind of caller, like SoundController: the shell's CheckTransmit asks
        // it and nothing managed does. Registered by contract all the same, so the
        // thunk resolves a subject rather than reaching for a field.
        _serverEntities = _services.Create<ServerEntities>();
        _services.AddInstance<IServerEntities>(_serverEntities);

        // IServerGameClients' managed half (plan_server.md Phase 7, plan-audit
        // C5). Same arrangement and same reason as the line above it: the only
        // caller is the shell's ClientEarPosition, and registering it by
        // contract keeps the thunk resolving a subject rather than a field.
        _serverClients = _services.Create<ServerClients>();
        _services.AddInstance<IServerClients>(_serverClients);

        // The template store (plan_server_all.md Wave 1, plan-audit D11).
        // game/server/TemplateEntities.cpp has left server_csharp.vpc.
        //
        // Registered as an INSTANCE and by contract, and it needs both. The
        // instance is what PointTemplate and ServerGameDll take in their
        // constructors -- a template asking for its own group to be rewritten,
        // and the map load draining the deferred precaches -- and the contract
        // is what mapentities.cpp reaches through the TemplatesAddNamed thunk,
        // which is the one direction the store cannot arrange for itself.
        _templates = _services.Create<TemplateStore>();
        _services.AddInstance(_templates);
        _services.AddInstance<IServerTemplates>(_templates);

        // The cross-level global state table (plan_entity.md Phase 4 exit).
        // game/server/globalstate.cpp's CGlobalState, ported: the list, the
        // name lookup, the counters and -- the part that had stopped happening
        // -- the write window a level teardown shuts.
        //
        // Registered as an INSTANCE and by contract, for TemplateStore's
        // reasons. The contract is what the managed entities that read a
        // globalstate key take in their constructors (logic_auto, multisource);
        // the instance is what a test and the dump command reach. It is also an
        // IGameLifecycle participant, which is how the window is opened and
        // shut, and LifecycleDriver finds it by that interface.
        _globalState = _services.Create<GlobalStateTable>();
        _services.AddInstance(_globalState);
        _services.AddInstance<IServerGlobalState>(_globalState);

        // The map's spawn ORDER (plan_entity.md Phase 4 exit). Stock's
        // classname priority list, which had gone quiet: eleven of its thirteen
        // classnames are managed entities now and a managed entity is never in
        // the CBaseEntity spawn list the tie-break sorts.
        _mapSpawnOrder = _services.Create<MapSpawnOrder>();
        _services.AddInstance(_mapSpawnOrder);
        _services.AddInstance<IServerMapSpawnOrder>(_mapSpawnOrder);

        // The same phase's FIFTH and last system, and it is unlike the four
        // above: nothing resolves it. StandaloneHierarchy holds no logic and
        // answers no contract -- it is WHERE the parallel hierarchy runs in the
        // frame and WHEN it is torn down, and the only thing that has to find it
        // is the two drivers, which find it by the interfaces it implements.
        //
        // Registered by its CONCRETE TYPE for that reason. An IServerSomething
        // for it would be an interface with no asker, which is the shape this
        // module already refuses for Corpses.
        // vphysics' held trigger pairs (B2.2): the collision-event table writes
        // them, the hierarchy's overlap reader merges them in.
        _services.AddInstance(new SolverHeldOverlaps());
        {
            var worldEntities = _services.Require<IEngineEntities>();
            _services.AddInstance(new PhysicsWorldEntity(
                () => worldEntities.FindAllByClassname(EntityNames.Worldspawn).FirstOrDefault(EntityHandle.None),
                worldEntities.IsValid));
        }
        _standalone = _services.Create<StandaloneHierarchy>();
        _services.AddInstance(_standalone);

        // HOW A TOUCH EDGE REACHES AN ENTITY -- plan_entity.md step 2b.
        //
        // The directory computes WHICH pair, on WHICH frame and WHICH of the
        // three edges, and knows nothing about what a touch means. This is what
        // it means, and it is the same two lines the ENTITY_OP_START_TOUCH /
        // ENTITY_OP_TOUCH / ENTITY_OP_END_TOUCH arms of Dispatch ran when the
        // shell raised them: the map items own their own entity and are asked
        // first, and the runtime pickups only recognise handles they created
        // themselves, so nothing is ambiguous.
        //
        // HERE rather than in ScriptedEntityRegistry's constructor, where the
        // I/O route is set, because the pickup fall-through is this module's and
        // the registry is in SourceSharp.Abi. Set once, after _pickups exists.
        //
        // WHAT IS NO LONGER IN IT is the crossing. Each of these used to be one
        // inbound Dispatch per touching pair per frame; the whole diff now runs
        // inside the frame's one GameFrame crossing.
        _entities.Directory.TouchRouting = (receiver, toucher, edge) =>
        {
            if (edge == EntityDirectory.TouchEdge.End)
            {
                _entities.EndTouch(receiver, toucher);
                return;
            }

            if (!_entities.Touch(receiver, toucher))
                _pickups.OnTouched(receiver, toucher, _clock.Now);
        };

        // THE MAP SPAWNER -- plan_entity.md §3.5, step 2d. What
        // MapEntity_ParseAllEntities was, in C#.
        //
        // BUILT BY HAND rather than Create'd, and the parenting driver is why:
        // HierarchyDriver is not a registered service (see IHierarchyHost.
        // Hierarchy above -- it hangs off StandaloneHierarchy, which is the one
        // thing that runs it), so the container cannot resolve one. It is built
        // AFTER _standalone for exactly that reason, and before _gameDll, which
        // takes it in its constructor.
        // worldspawn's networked state, bound to the live world between the map
        // parse's keys. The module's own, handed to ServerGameDll below.
        // Each world's blob on this composition's side, bound by handle
        // (plan_abi_hotreload D8: it bound through the static Engine). Built
        // here because only the server binds a world blob.
        ModuleEngine worldEngine = _engine;
        _services.AddInstance<IWorldNetBinding>(new WorldNetBinding(world =>
        {
            var state = new WorldNetState();
            state.Bind(worldEngine, world);
            return state;
        }, Log));

        // THE ENTITY FACTORY TABLE, bound here and nowhere else (plan_abi_hotreload
        // D7): it was five static providers set from Engine.Initialize, so two
        // game instances in one process shared one table. This composition's own,
        // in its container, before the spawner and the dev console that read it.
        _factories = _engine.EntityFactories();
        _services.AddInstance(_factories);

        _mapSpawner = new MapSpawner(Log, _entities, _standalone.Parenting, _templates,
                                     _services.Require<IEngineEntities>(),
                                     _services.Require<IEngineLevel>(),
                                     _services.Require<IServerMapSpawnOrder>(),
                                     _factories);
        _services.AddInstance(_mapSpawner);

        // The development console (DevCommands.cs): the ent_* family, give,
        // fov and the rest of the forty-three registered ConCommands that
        // resolved their subject as a CBasePlayer and returned in silence.
        // Built after the soundscapes it flushes and after the arsenal it
        // gives through.
        //
        // The SHARED HALF only. Each command is its own [ConCommand] class and
        // is built by the same discovery pass that builds cs_hello, which is why
        // this has to be in the container before that pass runs — a command
        // asking for something not yet registered is refused at load.
        _devContext = _services.Create<DevContext>();
        _devContext.FlushSoundscape = _soundscapes.Forget;

        // ent_fire's whole body. THE QUEUE'S OWN Add, at stock's own position in
        // the chain: CC_Ent_Fire called g_EventQueue.AddEvent and delivered
        // nothing itself, and this is the same division with the managed queue
        // in the native one's place. The DELAY is turned into an absolute time
        // HERE, because the clock is this module's and the command has no
        // business knowing which clock the queue keeps.
        //
        // EntityHandle.None for the caller and 0 for the output id, which is
        // what a console line is: it is nobody's output, so !self and !caller
        // resolve to nothing for it and the SDK's I/O debugging has no output to
        // name. Stock passed NULL and 0 in the same two places.
        _devContext.PostInput = (target, input, value, delay, activator) =>
            _entities.Directory.Io.Add(_clock.Now + delay, target, EntityHandle.None,
                                       input, value, activator, EntityHandle.None, 0);
        _services.AddInstance(_devContext);

        // What ent_dump prints for an entity's net state, and it is the SAME
        // pair the development API's /nettable and /entity/<i>/payload routes
        // read — the schema list this module builds once, and the live blob
        // reader. Delegates rather than values because the list is built lazily
        // and a second copy could disagree with the one those routes serve,
        // which is exactly the drift the shared contract exists to prevent.
        //
        // Registered UNCONDITIONALLY: the console dump wants it whether or not
        // this process was started with -debugapi.
        //
        // The reader walks THIS side's entities through its own areas
        // (plan_abi_hotreload D8), never whichever side is active; the dev API
        // route further down reads through the same instance.
        _payloadReader = new NetPayloadReader(_services.Require<IEngineEntities>(),
                                              _services.Require<IEngineNetworking>());
        _services.AddInstance<INetState>(new LiveNetState(NetSchemaTypes, _payloadReader.Read));

        // The areas this module itself uses, straight after registering them.
        _constraints = _services.Require<IEngineConstraints>();
        _entityApi = _services.Require<IEngineEntities>();
        _playerApi = _services.Require<IEnginePlayers>();
        _motion = _services.Require<IEngineMotionControllers>();
        _net = _services.Require<IEngineNetworking>();
        _send = _services.Require<IUserMessageSender>();
        _springs = _services.Require<IEngineSprings>();
        _visibility = _services.Require<IEngineVisibility>();
        _world = _services.Require<IEngineWorldQuery>();
        _clock = _services.Require<IGameClock>();

        _chapterTitle = new ChapterTitle(Log, _send);

        int v = _convars.RegisterAll(typeof(ServerModule).Assembly);
        _convars.PublishTo(_services);

        // The registry itself, not just the convars in it. GameRules asks for
        // it by type so it can hold typed references rather than look each one
        // up by name on every read — and it is built through the container now,
        // so the container has to be able to answer for it.
        _services.AddInstance(_convars);

        // Registered on the server too, from the SAME shared declarations the
        // client uses. Dispatch is client-only, but the server resolves a
        // message name to an index when it SENDS one — and a miss is fatal.
        //
        // The shared assembly is named as the one both sides load, not as the
        // only one scanned: discovery reads them all, and a message found
        // outside this one is warned about because the other side has no copy.
        _userMessages.RegisterAll(Game);

        // Every [StringTable] declaration, registered under its own interface
        // so a consumer can inject it. NO ENGINE CALL happens here: the tables
        // themselves are made from IServerLevel.CreateNetworkStringTables,
        // which is the only window the engine allows a create in.
        //
        // From the SHARED assembly for the same reason user messages are: the
        // server creates a table and the client reads the replicated copy, so a
        // declaration only one side compiles against is a table only one side
        // can name.
        //
        // THIS RUNS AGAIN AFTER A cs_sv_reload AND CreateNetworkStringTables
        // DOES NOT -- that is per map load, not per managed reload. What is
        // registered is a NAME, so every declared table works the moment the
        // reloaded module is up, with nothing re-bound.
        // The server writes: its adds go through its own writer (P1 run 3).
        StringTableWriter.AddTo(_services);
        _stringTables = new StringTableRegistry(_services, writes: true);
        _stringTables.RegisterAll(Game, _services);

        // AFTER A cs_sv_reload the map's SsUserMessages table is already out
        // with every client (plan_side_isolation F5): the ids are its, a key it
        // lacks is refused until restart. On the first load there is no table
        // yet and nothing to adopt; CreateNetworkStringTables writes it.
        _userMessages.AdoptTable(_services.Require<UserMessageTable>());

        // The development API, if this process was started with -debugapi. Set up
        // after the convars so it can read them back, and after the entity
        // classes so /entities can see everything.
        _debugApi = DebugApiLoader.LoadIfRequested(_services.Require<ILocalCommandLine>(), DebugHooks(), Log);

        // This module's own record, empty: its first frame invites the clients
        // a previous module (before cs_sv_reload) had, whose sessions went with it.
        _reloadInvites = _services.Create<DebugChannelReloadInvites>();
        _connected = _services.Require<IConnectedClients>();

        if (_debugApi is not null)
        {
            // Every hook went in through DebugHooks(), so the first request
            // already sees a complete picture rather than a half-configured one.
            if (!_debugApi.Start())
                _debugApi = null;
        }

        // Map entity classes. This has to happen during init rather than at
        // level load: the engine builds each class's entity description from
        // what we declare here, and a map is parsed against those descriptions.
        int e = _entities.RegisterAll(typeof(ServerModule).Assembly);

        Log.Msg($"[SourceSharp/Server] registered {e} map entity class(es) by attribute\n");

        // AFTER the classes above, and that ordering is load-bearing: a table
        // is built for a CLASSNAME, and a name no factory answers to would put
        // a ServerClass on the wire that no entity can ever claim. Native
        // refuses such a description rather than building it.
        // Every [ScriptedEntity] is a candidate; native refuses the ones not on
        // the hierarchy, which is the only side that knows which those are.
        int netStates = StandaloneNetStates.PublishAll(
            _net, "Server", Game, _entities.IsRegistered, _entities.ClassNames,
            _services.Require<INetTypeRefusals>(), Log);
        Log.Msg($"[SourceSharp/Server] {netStates} hierarchy class(es) network state of their own\n");

        // AND worldspawn, which is NOT on that hierarchy and is published on its
        // own (plan_gamedll_after.md Part B2). Its shim is a CBaseEntity that
        // allocates its own blob against its own base, so its size is its own
        // and PublishAll's max deliberately skips it -- see
        // StandaloneNetStateAttribute.OnHierarchy.
        //
        // HERE, IN THE SAME WINDOW, for the same reason everything above is: a
        // ServerClass links itself into g_pServerClassHead from its own
        // constructor and the engine walks that list exactly once.
        WorldNet.Publish(_net, "Server", _services.Require<INetTypeRefusals>(), Log);

        // TEMP ENTITIES (plan_tmp.md Part 3), from the same shared declarations
        // the client will build its RecvTables from.
        //
        // INSIDE THE DLL-INIT WINDOW, and that is the whole of why it is here
        // rather than in a game system of its own. A ServerClass links itself
        // into g_pServerClassHead when it is constructed and the engine walks
        // that list exactly once, through CServerGameDLL::GetAllServerClasses;
        // one built later is simply not in the list and the first effect that
        // claims it sends the engine into a table that was never flattened.
        // Ordering between game systems is link order, so a second
        // CAutoGameSystem would be a coin toss (plan 2D).
        //
        // THE TABLES ARE PLANNED HERE AND STORED BY NATIVE (plan_server_native_v2
        // T4b): ServerTempEntEngine answers the registration, the playback and
        // the effect index; every other IEngineEffects member is the engine's.
        _tempEntEngine = new ServerTempEntEngine(_services.Require<IEngineEffects>(), new TempEntTables(_engine), Log,
                                                 EffectDispatchNames.For(_services));
        _tempEnts.RegisterAll("Server", Game, (ITempEntRegistrar)_tempEntEngine);

        // THE REGISTRY ITSELF, as the client already registers its own
        // (ClientModule). ITempEnts covers twenty-one of the forty-three tables
        // -- a method is added when its table gets a managed CALLER, and the
        // beams, the sprites and most of the gore reach the wire through native
        // forwards -- so anything that has to address a class by name needs the
        // registry rather than the interface. ss_te_tester and cs_te_fire are
        // the two, and both exist to fire ALL of them (plan 9.1).
        _services.AddInstance(_tempEnts);

        // The demonstration sender cs_te_fire and ss_te_tester share: this
        // module's registry, and the names it has already complained about.
        _services.AddInstance<ITeFire>(new TeFire(Log, _tempEnts, _tempEntEngine,
                                                 _services.Require<IEnginePrecache>()));

        // Registered by CONTRACT: what a caller wants is "make sparks happen",
        // and the class that answers it is a detail of which side is asking.
        // The client's own implementation of the same interface plays what
        // arrives instead of sending it.
        //
        // IT HOLDS THE REAL ENGINE, taken before the line below replaces the
        // registration. TempEnts needs TempEntPlayback and nothing else from
        // the engine; handing it the wrapper instead would work today and would
        // be a cycle waiting to be closed the first time a rewired member of
        // that wrapper wanted a temp entity.
        IEngineEffects nativeEffects = _tempEntEngine;
        var temps = new TempEnts(Log, _tempEnts, nativeEffects);

        _services.AddInstance<ITempEnts>(temps);

        // PLAN Part 8, THE REWIRING. IEngineEffects keeps every one of its call
        // sites; what changes is who answers eight of them. Sparks, the
        // explosion, the muzzle flash, the funnel, the dust, the impact, the
        // splash and the tracer are packed and sent by managed code over the
        // managed tables from here on; the other forty-seven members are the
        // engine's and EffectsOverTempEnts forwards them to the binding
        // untouched. See that class for the three rows of Part 8's table that
        // did NOT move and exactly why each is still native.
        //
        // A REPLACEMENT AND IT SAYS SO. ServiceContainer warns when a contract
        // is registered twice, and this is the case its comment names -- "a
        // module registering a better implementation over a default". The line
        // in the console is the port being visible rather than a mistake.
        _services.AddInstance<IEngineEffects>(new EffectsOverTempEnts(
            nativeEffects, temps,
            _services.Require<IEngineSound>(),
            _services.Require<IEngineEntities>()));

        // plan_entity.md §3.8: the physics event ring's consumer. AFTER the
        // IEngineEffects replacement above, deliberately -- dust from a physics
        // impact goes out over whatever effects implementation the module
        // settled on, not over the one it was about to replace.
        // The step's impact damage (physics stock parity C7): registered before
        // any map entity is made, because the props and the player take it.
        var impacts = new PhysicsDamageQueue(
            _services.Require<IEngineDamage>(),
            _services.Require<IEngineEntities>(),
            _services.Require<IEnginePlayers>(),
            _entities.Directory);
        _services.AddInstance(impacts);
        // The queued impact and break sounds (plan_physics_bones_native_zero
        // 2.3): CPhysicsHook's batch, managed, played at OnPreClientUpdate.
        // Before any map entity too: a breaking prop queues into it.
        var physicsSounds = new PhysicsSoundBatch(new EnginePhysicsSoundWorld(
            _services.Require<IEngineEntityPhysics>(), _services.Require<IEngineEntities>(), new VPhysicsInspect(_engine.VPhysics),
            _surfaceProps as IVPhysicsSurfaceSounds, _services.Require<IEngineSound>()));
        _services.AddInstance(physicsSounds);
        // Which entities touch fluid (I2): the props and the player read it.
        var fluid = new FluidTouches(new EngineSurfaceDensity(_services.Require<PhysicsSurfaces>()));
        _services.AddInstance(fluid);
        // The scrape a sliding object makes (D2), through the sound controller.
        var friction = new FrictionSounds(_services.Require<IEngineSound>(), _soundController,
                                          new EnginePhysicsSurfaceTable(_services.Require<PhysicsSurfaces>()),
                                          new TempEntFrictionEffects(_services.Require<IEngineEffects>(), _services.Require<ITempEnts>()));
        _services.AddInstance(friction);
        // cs_sv_playershadow_trace (A39): the shadow step's and the recovery's
        // per-frame record, read live from this side's convar.
        var shadowTraceOn = _convars.Get<PlayerShadowTraceConVar>()
            ?? throw new InvalidOperationException("cs_sv_playershadow_trace is not registered");
        var shadowTrace = new PlayerShadowTrace(Log, () => shadowTraceOn.Value);
        _playerCommands.Shadow.Trace = shadowTrace;
        // The players' physics shadows (plan_physics_bones_native_zero 2.5):
        // managed builds each body, reads and calls vphysics through its own
        // tables, and decides; native holds the pointers on the player. The
        // command loop steps them, the spawn asks for one, every teleport
        // carries one, and the recovery below reads them.
        IVPhysicsCollision? physicsCollision = PhysicsCollision.Acquire(_engine, out string collisionWhy);
        if (physicsCollision is null)
            Log.Warning($"[SourceSharp/Server] no VPhysics collision table: player shadows cannot be built: {collisionWhy}\n");
        if (_surfaceProps is null)
            Log.Warning("[SourceSharp/Server] no VPhysics surface table: player shadows use surfaceprop 0\n");
        var shadowBodies = PlayerShadowBodies.ForEngine(
            _engine, new VPhysicsShadowCalls(physicsCollision, _surfaceProps, _engine.VPhysics),
            () => _playerShadow?.Value ?? true, text => Log.Warning(text));
        _services.AddInstance(shadowBodies);
        _services.AddInstance<IPlayerShadows>(shadowBodies);
        _playerCommands.Shadow.Bodies = shadowBodies;
        // The players' shadows after the step (B7): VPhysicsShadowUpdate.
        var physTimescale = _convars.Get<PhysTimescaleConVar>()
            ?? throw new InvalidOperationException("phys_timescale is not registered");
        var shadows = new PlayerShadowRecovery(_services.Require<IEnginePlayers>(),
                                               _services.Require<IEngineEntities>(),
                                               _services.Require<IEngineWorldQuery>(),
                                               shadowBodies,
                                               _entities.Directory, shadowTrace, () => physTimescale.Value,
                                               h => _physicsSolver?.PenetratedByMover(h) ?? false,
                                               shadowBodies.Memory);
        _services.AddInstance(shadows);
        // vphysics' IPhysicsCollisionSolver answered by managed
        // (plan_physics_bones_native_zero 2.1), and the entity pairs the game
        // disabled, which its ShouldCollide reads.
        var solverObjects = new VPhysicsSolverObjects(_engine.VPhysics);
        var solverWorld = new EnginePhysicsSolverWorld(_services.Require<IEngineEntities>(),
                                                       _services.Require<IEngineNetworking>(),
                                                       _services.Require<IEngineEntityPhysics>());
        _services.AddInstance<IPhysicsSolverWorld>(solverWorld);
        var pairs = new EntityPairCollisions(solverWorld, solverObjects);
        _services.AddInstance(pairs);
        _physicsSolver = new PhysicsSolver(solverWorld, solverObjects, new PhysicsSolverRules(), pairs,
                                           new PhysicsPenetrationLedger(), _services.Require<IGameClock>(),
                                           ev => _physicsRouter?.Post(ev), impacts,
                                           () => _services.Require<IEngineFrame>().TickCount(), DeveloperMessage);
        _services.AddInstance(_physicsSolver);
        // The physics diagnostics' reads (plan_physics_bones_native_zero 2.3):
        // the SS-PHYSWORLD report, physics_budget and air_density, managed.
        var inspect = new VPhysicsInspect(_engine.VPhysics);
        _services.AddInstance<IVPhysicsInspect>(inspect);
        {
            var diagnostics = _services.Require<IEngineDiagnostics>();
            var reportOn = _convars.Get<PhysWorldReportConVar>()
                ?? throw new InvalidOperationException("sourcesharp_physworld_report is not registered");
            var entityPhysics = _services.Require<IEngineEntityPhysics>();
            _services.AddInstance(new PhysWorldReport(inspect, entityPhysics.EnvironmentPointer,
                entityPhysics.WorldObject, _physicsSolver,
                new PhysWorldReportSink(_services.Require<IEngineConsole>()),
                () =>
                {
                    Span<int> counts = stackalloc int[8];
                    return diagnostics.PlayerShadowCounts(counts) >= 8 ? (counts[6], counts[7]) : (0, 0);
                },
                () => reportOn.Value));
            // physics_debug.cpp's three SS-PHYSWORLD instruments (2026-10-18).
            // The terrain read is the world build's, made later in this init.
            var console = _services.Require<IEngineConsole>();
            _services.AddInstance(new PhysWorldProbes(inspect, entityPhysics.EnvironmentPointer,
                physicsCollision is null ? null : physicsCollision.Bounds,
                i => _physicsWorldVPhysics?.VirtualTerrainCollide(i) ?? 0,
                new PhysWorldReportSink(console), console.Msg));
        }
        // Stuck debris pulled apart (A9): stock's CPhysicsEntitySolver.
        var solver = new PhysicsEntitySolver(new EngineEntitySolverWorld(
            _services.Require<IEngineEntityPhysics>(), _services.Require<IEngineEntities>(), new VPhysicsObjects(_engine.VPhysics), pairs),
            _services.Require<IGameClock>());
        _services.AddInstance(solver);
        _physicsRouter = new PhysicsEventRouter(_entities, impacts, fluid, friction, shadows,
                                                _services.Require<IGameClock>(), solver);
        _physicsEvents = new PhysicsCollisionResponse(
            new EnginePhysicsCollisionWorld(
                _services.Require<IEngineEffects>(),
                _services.Require<IScreenEffects>(),
                physicsSounds,
                _services.Require<PhysicsSurfaces>()));
        _services.AddInstance(_physicsEvents);
        _services.AddInstance(_physicsRouter);

        // Game events, also from the shared assembly. Order carries no meaning
        // here — the engine dispatches by name — so this is a plain
        // subscription rather than a protocol like the user message list.
        _gameEvents.RegisterAll(Game, _services);

        // Subscribers attach to the events the classes above raise — this
        // module's instances, from its container. This is where gameplay
        // reacting to events gets wired up, and it needs no knowledge of the
        // registry or the native event API.
        _scoreboard.Subscribe();

        // The round's end (stock's GoToIntermission and ChangeLevel), before the
        // rules that end it and the game_end entities and command that ask.
        _services.AddInstance(_services.Create<Intermission>());
        _services.AddInstance(_services.Create<MotdPanel>());
        // Host_Say, behind say and say_team.
        _services.AddInstance(_services.Create<HostSay>());
        // The mode proxy (tf2 stage 0.7, ruling U1), BEFORE the rules: the
        // rules take it (every question they ask a mode goes through it), and
        // it takes the rules' machine as the default mode's — neither can be
        // the other's constructor parameter, so the proxy is wired to its
        // machine by Attach the moment both exist. Registered as a service
        // and as a lifecycle/frame participant (its pick lands before the
        // rules' reset, its warmup gate before their think).
        var modes = _services.Create<GameModes>();
        _services.AddInstance(modes);
        _rules = _services.Create<GameRules>();
        modes.Attach(_rules);
        // And the death cycle gets the proxy too (tf2 stage 0.7 part C): the
        // respawn wait and the wave release are the active mode's when it owns
        // them. Attach rather than a Players constructor parameter — the proxy
        // cannot be Create'd at the Players creation site above, which runs
        // before the ConVarRegistry it takes is registered.
        _players.AttachModes(modes);

        // NO NATIVE AMMO REGISTRATION ON THE SERVER (plan_server_novalve.md P5,
        // user ruling 2026-10-01). server.so's table was written here and read
        // by nothing once the server gc cull deleted CalculateBulletDamageForce;
        // the server's ammo answers come from AmmoTypes itself. The client still
        // registers natively (ClientModule), where the table is read.
        // The MOVEMENT registration and the eight movement ConVars' starting
        // values moved below the mods' registration (phase 0.3): both take the
        // RESOLVED movement constants, and a mod's IMovementRules is not
        // known until its assemblies have registered. The convars are still
        // server-only — every one is FCVAR_REPLICATED, and the engine refuses
        // a client that sets one (plan_server_novalve.md P2.3; the write was
        // shell_movement.cpp's SetEngineConVar). The weapon table's native
        // registration is below the mods' registration too: the catalogue
        // builds itself on its first read, and reading it before the mods are
        // in would freeze a table without their rows.

        // The usercmd decode's clamp (plan_server_novalve.md P2.2): read per
        // packet from this side's own ConVar, never by a crossing.
        var moveMagnitude = _convars.Get<MaxUsercmdMoveMagnitudeConVar>()
            ?? throw new InvalidOperationException("sv_max_usercmd_move_magnitude is not registered");
        _playerCommands.MaxMoveMagnitude = () => moveMagnitude.Value;

        // sourcesharp_player_shadow, this composition's own (plan_abi_hotreload
        // D7: it was the static PlayerShadow.Switch, which the convar's
        // constructor set). The run reads it live; before this line it answers
        // ON, the declared default.
        _playerShadow = _convars.Get<PlayerShadowConVar>()
            ?? throw new InvalidOperationException("sourcesharp_player_shadow is not registered");

        // Which kinds of damage behave which way. Beside the movement
        // parameters because it is the same kind of statement -- constants the
        // native side reads per hit -- and at the same moment, which is before
        // anything can be damaged.
        DamageRules.Register(_services.Require<IEngineDamage>());

        // The rules' constants, beside the damage masks and for the same
        // reason: both are policy a mod states once, and both are read on paths
        // that must not pay for a managed call to be told a startup value.
        GameRulesPolicy.Register(_services.Require<IEngineGameRulesPolicy>());

        // The collision matrix. Registered on BOTH sides -- the client asks the
        // same question during prediction, and two sides answering from
        // different tables shows up as jitter rather than as an error.
        if (CollisionRules.Register(_services.Require<IEngineCollisionRules>()))
            Log.Msg($"[SourceSharp/Server] collision matrix delivered (rows 0x{CollisionRules.RowHash(CollisionRules.BuildMatrix()):X8})\n");
        else
            Log.Warning("[SourceSharp/Server] collision matrix NOT delivered: this side does not serve RegisterCollisionRules\n");

            // Attribution for anything a weapon throws. The launch itself is in
            // the shared half, which cannot see the server's entity classes.
            _arsenal.Launched = (thrown, thrower, velocity, fuse) =>
            {
                switch (_entities.Get(thrown))
                {
                    case Grenade grenade:
                        // The gun's declared fuse wins; zero means the stock
                        // path declared none and the module's own rule stands.
                        grenade.ThrownBy(thrower, fuse > 0f ? fuse : GrenadeFuse);
                        break;

                    // An aimed projectile is not moving yet. The shell spawns it
                    // able to fly and this is what points it and lets it go.
                    case FlyingProjectile projectile:
                        projectile.FiredBy(thrower, velocity);
                        break;
                }
            };

        //---------------------------------------------------------------------
        // The services.
        //
        // Everything built after this line — every console command, every map
        // entity — can ask for any of these in its own constructor. Two static
        // back-references used to sit here instead: SpawnPickupCommand.Owner
        // and GameUi.ReadButtons, each a field the module had to remember to
        // assign, each reading as "no pickups" or "no keys held" rather than as
        // an error when it forgot. A missing service names the class that
        // wanted it.
        //
        // Registered by CONTRACT, not by concrete class. What an entity can see
        // of the rules is IGameRules and not GameRules, which is what keeps the
        // module's own lifecycle calls — Think, ResetRound, Reset — out of
        // reach of anything running inside a tick.
        //---------------------------------------------------------------------
        _services.AddInstance<IGameRules>(_rules);
        _services.AddInstance<IPlayerDirectory>(_players);
        _services.AddInstance<IPickupDirectory>(_pickups);
        // tf2 stage 0.5: the team roster, the join and the spawn ask, by
        // contract. Its observer half, clock, events and names are the
        // instances already registered above; the scoreboard (PlayerResource)
        // reads the same team through IEnginePlayers.GetTeam, so one store
        // answers both. Built HERE, before the mods' scan, so a mod can inject
        // ITeams — and so it resolves nothing at construction: its join policy
        // is asked for below the scan.
        var teams = _services.Create<Teams>();
        _services.AddInstance<ITeams>(teams);
        // IPlayerButtons is registered ABOVE, before the Create block, because
        // Players now takes it as a constructor argument.
        _services.AddInstance<ISoundscapes>(_soundscapes);

        // Corpses had no registration at all -- it was Create'd and held in a
        // field, which was enough while ServerModule called Reset on it by
        // hand. A participant the container does not know about is a
        // participant ResolveAll cannot find, so declaring IGameLifecycle on it
        // is only half the job.
        //
        // By its concrete type: nothing asks for a corpse registry by contract,
        // and inventing an ICorpses for one caller would be an interface with
        // no second implementation and no reader.
        _services.AddInstance(_corpses);

        //---------------------------------------------------------------------
        // ...and now that every service is registered, the thing that drives
        // their lifecycle.
        //
        // AFTER the registrations and not before: LifecycleDriver resolves its
        // participants ONCE, so anything registered later would never be
        // called and would fail silently -- the map would load, the service
        // would keep its stale handles, and nothing would say so.
        //---------------------------------------------------------------------
        // The payload traffic view: sampled by FrameUpdate, read by the dev API,
        // and a lifecycle participant because its snapshots are per level.
        // BEFORE the driver below, which collects its participants once.
        _netTraffic = _services.Create<NetTrafficProfiler>();
        _services.AddInstance<INetTrafficProfiler>(_netTraffic);

        // The mod's services: every [SideService(Side.Server)] class and server
        // registrar in the loaded assemblies (mods.yaml lists them), registered
        // LAST so they can inject anything above and can never replace it, and
        // BEFORE the drivers below, which collect their participants once.
        _modServices = SideServices.Register(_services, Side.Server, LoadedAssemblies.Types(typeof(ServerModule).Assembly, Log));
        if (_modServices.Count > 0)
            Log.Msg($"[SourceSharp] registered {_modServices.Count} mod service contract(s)\n");

        // THE JOIN POLICY, resolved once now the mods are in (tf2 stage 0.5),
        // for the movement rules' reason: a single-owner hook asked before the
        // scan sees no owner, forever. Two owners throw here, naming both,
        // before any player exists; the owner is said out loud either way.
        //
        // THE OTHER 0.5-0.7 HOOKS ARE SAFE AT THIS POINT BECAUSE NOTHING READS
        // THEM BEFORE IT: IHooks<T>.All collects on its first read and caches,
        // and ISpawnSelector, IClientCommandHook, IGameModeSelector,
        // IDamageRule, ILoadoutProvider, IRespawnGate, IPlayerDeathHook and
        // IPlayerComponentSource are first read by a spawn, a client command,
        // a level init, a hit, a death, a respawn or a client-active — all
        // after Initialize. The modes (GameModes.Registered) are walked fresh
        // on every ask; a mode's IRespawnPolicy and IScoring are read off the
        // active mode per call. Any new reader in Initialize above this line
        // must move below it.
        Log.Msg($"[SourceSharp/Teams] join policy: " +
                (teams.Policy is { } joinPolicy
                    ? $"{joinPolicy.GetType().FullName} ({joinPolicy.GetType().Assembly.GetName().Name})"
                    : "none (any real team is accepted; docs/divergences.md)") + "\n");

        // THE MODES' IDS, verified once the mods are in (tf2 stage 0.7): two
        // registrations answering one id are a startup error naming both —
        // the lookup takes the first, and the second would be unreachable.
        var modeIds = modes.VerifyRegistry();
        Log.Msg($"[SourceSharp/Modes] registered modes: " +
                (modeIds.Count > 0 ? string.Join(", ", modeIds) : "none") +
                $" (plus the core's '{GameModes.NullModeId}')\n");

        // THE MOVEMENT CONSTANTS, resolved once now the mods are in (phase
        // 0.3). Zero IMovementRules registrations answer the core's stock
        // table, so the test mod and tf2_descent register byte-identical
        // parameters to the ones they did before. Two mods claiming the hook
        // is a startup failure here, naming both, before any player exists.
        _movementRules = _services.Create<MovementRulesResolver>();
        // Published for a mod that wants to ask who owns movement, and for
        // the ABI getters below; Create built it over this container, so the
        // hook count already includes every assembly the scan found.
        _services.AddInstance(_movementRules);

        Movement.Register("Server", _movementRules.Constants,
                          _services.Require<IEnginePlayerMovement>(), Log);

        // The eight movement ConVars take the resolved constants as their
        // starting value (plan_server_novalve.md P2.3; this was
        // shell_movement.cpp's SetEngineConVar). The record of what was
        // written is the module's (it was a static on MovementTuning), and
        // each tuned convar is handed it as it is written.
        var movementTuning = new AppliedMovementTuning(Log);
        _services.AddInstance<IAppliedMovementTuning>(movementTuning);
        movementTuning.Apply(_convars, _movementRules.Constants);

        // The weapon catalogue's FIRST read, here, after the mods' registration:
        // the first read is what builds it, so the mods' rows are in it. (It
        // was the native fire-rate table's registration until
        // plan_server_native_v2 T2 deleted that table, which nothing read.)
        _ = _services.Require<WeaponCatalog>().Count;

        // vphysics' callbacks on managed's own tables (plan_physics_managed B2):
        // a lifecycle participant, so it is registered before the driver below.
        _physicsCallbacks = new PhysicsCallbacks(_services.Require<IEngineEntityPhysics>(), new VPhysicsEnvironment(_engine.VPhysics));
        _services.AddInstance(_physicsCallbacks);
        // The physics frame (plan_physics_bones_native_zero 2.3): whether and
        // how long to step, and what the step tells the game, decided here;
        // CPhysicsHook forwards its FrameUpdatePostEntityThink. A lifecycle
        // participant, so registered before the driver below.
        {
            var frameCalls = new VPhysicsFrameCalls(_engine.VPhysics);
            var speeds = _convars.Get<PhysSpeedsConVar>()
                ?? throw new InvalidOperationException("phys_speeds is not registered");
            var timescale = _convars.Get<PhysTimescaleConVar>()
                ?? throw new InvalidOperationException("phys_timescale is not registered");
            var directory = _players;
            var callbacks = _physicsCallbacks;
            _physicsFrame = new PhysicsFrame(frameCalls, EnginePhysicsFrameNative.ForEngine(_engine),
                new EnginePhysicsFrameClock(_services.Require<IEngineFrame>(), _services.Require<IEngineEnvironment>()),
                _services.Require<PlayerShadowBodies>(),
                new PlayerShadowStress(frameCalls, _services.Require<IPhysicsSolverWorld>()),
                () => directory.All.Select(p => p.Player), ev => _physicsRouter?.Post(ev),
                () => timescale.Value, () => speeds.Value, text => Log.Msg(text), callbacks.EnsureInstalled);
            _services.AddInstance(_physicsFrame);
        }

        // THE SERVER'S SKELETONS (plan physics_bones_native_zero step 1.2):
        // posed in managed code from the engine's model cache, copied as bytes.
        // A lifecycle participant (level shutdown drops every skeleton and
        // image), so registered before the driver below.
        _skeletons = new ServerSkeletons(new EngineStandaloneBlocks(_services.Require<IEngineEntities>()),
                                         new StudioModels(_engine.StudioMemory()),
                                         _services.Require<IGameClock>(), Log,
                                         _convars.Get<AnimThreeWayBlendConVar>(), _surfaceProps);
        _services.AddInstance(_skeletons);
        _entities.Directory.RemovalForget = _skeletons.Forget;

        // The model descriptors are read from the same studio headers
        // (studio.cpp's eviction): sequences, events, attachments and each
        // sequence's default duration.
        _descriptorAttachment = _engine.Models.AttachSource(new StudioModelDescriptor(_skeletons.Models));

        // THE FLIP: every bone question the engine bindings answer -- the
        // attachments, the cached count, the matrices, the sequence, the scale
        // -- is the managed skeleton's from here; native is asked only for the
        // model scale's store and by the engine's hitbox trace.
        _skeletonRoute.Skeletons = _skeletons;

        // The per-player composite (tf2 stage 0.7 part B, D9/D10), before the
        // three drivers below — they collect their participants at
        // construction, and this is a participant of all three (its
        // OnLevelShutdown detach, its frame tick, its client attach/detach).
        // Lazy like every service here: a mod's [SideService]
        // IPlayerComponentSource registered at its own stage is in the
        // container before the composite first resolves its chain, which
        // happens at the first player's client-active.
        _services.AddFactory<PlayerComposites>(locator => new PlayerComposites(
            log,
            locator.Require<IHooks<IPlayerComponentSource>>(),
            locator.Require<IEnginePlayers>(),
            locator.Require<IGameClock>()));
        _lifecycle = new LifecycleDriver(Log, _services);
        _frames = new FrameDriver(Log, _services);

        // The clients' hooks, collected once like the two drivers above; and
        // the sink a player entity's removal reports to.
        _clients = new ClientLifecycleDriver(_services, _log);
        _services.AddInstance<IPlayerRemovals>(_clients);

        //---------------------------------------------------------------------
        // The game DLL, and the environment it reads.
        //
        // RESOLVED ONCE, like the two drivers above and for the same reason: the
        // bootstrap's thunks hold these across the whole run, so a second
        // instance built later would be one the engine never calls.
        //
        // The environment is RESOLVED, not built here: IEngineEnvironment is an
        // engine area like the other twenty-four, so EngineServices.AddTo
        // already registered the binding at the top of this method.
        //
        // ITS GLOBALS ARE FED; ITS RESOLVER IS NOT, and which is which is stated
        // rather than hidden. plan_crossings.md Phase X1 added the
        // GetGlobalVarsPtr ABI slot, so Environment.Globals reads the engine's
        // live CGlobalVars for whichever side is executing and
        // Globals.IsAvailable is true in a live session. GetInterface still
        // answers zero: no slot crosses the engine's CreateInterfaceFn and
        // nothing in managed code asks for one yet.
        //
        // NOTHING IS CONSTRUCTED WITH A POINTER HERE, deliberately. This class
        // is shared by both game DLLs in the default ALC, so an instance holding
        // one side's pointer would answer the other side with it -- and a server
        // reading the client's curtime is a plausible number rather than a
        // crash. The binding asks Engine for the side whose call is running.
        //---------------------------------------------------------------------
        _gameDll = new ServerGameDll(Log, _services.Require<IEngineEnvironment>(),
                                     _convars,
                                     _services.Require<IEngineHost>(),
                                     _services.Require<ILocalCommandLine>(),
                                     _services.Require<IEngineLevel>(),
                                     _services.Require<IEngineEntities>(),
                                     _services.Require<IEngineConsole>(),
                                     _services.Require<IEngineFrame>(),
                                     _stringTables,
                                     _services.Require<ManagedStringTable>(),
                                     _lifecycle,
                                     _templates,
                                     _mapSpawner,
                                     _services.Require<EntityIoRoutes>(),
                                     _services.Require<IWorldNetBinding>(),
                                     new MessageOfTheDay(Log,
                                         _convars.Get<MotdFileConVar>()
                                             ?? throw new InvalidOperationException("motdfile is not registered"),
                                         _convars.Get<MotdTextFileConVar>()
                                             ?? throw new InvalidOperationException("motdfile_text is not registered"),
                                         _services.Require<IEngineFiles>(),
                                         EngineInfoPanel()));
        _gameDll.UserMessages = _userMessages;
        _gameDll.Skeletons = _skeletons;
        _gameDll.UserMessageIds = _services.Require<UserMessageTable>();
        _gameDll.ParticlePrecache = _services.Require<IEnginePrecache>();
        _gameDll.ModelText = _services.Require<IEngineEntityRender>();
        _gameDll.Models = _services.Require<ModelDescriptors>();
        _gameDll.SoundScripts = SoundEmitterBase.Acquire(_engine, out string soundWhy);
        if (_gameDll.SoundScripts is null)
            Log.Warning($"[SourceSharp/Server] no sound script table for model sounds: {soundWhy}\n");

        _services.AddInstance<IServerLifetime>(_gameDll);
        _services.AddInstance<IServerLevel>(_gameDll);
        _services.AddInstance<IServerFrame>(_gameDll);
        _services.AddInstance<IServerListing>(_gameDll);

        // THE FOURTH FRAME POINT'S PRODUCTION CALLER -- plan_gamedll_after.md
        // Phase A3, second pair, and until it nothing called that point at all.
        //
        // A DELEGATE rather than a constructor argument, and the ORDER above is
        // why: _frames resolves every IFrameLifecycle when it is built, so it
        // cannot exist until every participant is registered -- and _gameDll is
        // built after that, so it cannot be handed to _frames either. Each has
        // to exist before the other can take it, which is what a settable hook
        // is for. The same shape DevContext.FlushSoundscape already uses.
        //
        // SET HERE AND NOT BESIDE THE DRIVER, which is where it was first
        // written -- and _gameDll was null at that point, so the server threw a
        // NullReferenceException out of Initialize and the whole managed side
        // refused to load. The live gate is what caught it; every unit test in
        // the suite builds these objects by hand and none of them runs this
        // method.
        _gameDll.FrameParticipants = FramePreClientUpdate;

        Log.Msg($"[SourceSharp/Server] lifecycle: {_lifecycle.Participants.Count} " +
                $"participant(s) -- {string.Join(", ", _lifecycle.Participants.Select(p => p.GetType().Name))}\n");

        // The entity registry builds its instances from the container. Set
        // during init rather than at registration because an instance is not
        // made until a map places one, which is long after this.
        _entities.Services = _services;

        // COMMANDS LAST of the three discovery passes, because a command may
        // ask for a convar or for any service above, and neither existed when
        // the entity classes were registered. Registering the classes early is
        // not negotiable — the engine builds each class's entity description
        // from that pass — but nothing is instantiated by it.
        int n = _commands.RegisterAll(typeof(ServerModule).Assembly, _services.Require<IEngineConsole>(), _services);

        // The commands whose bodies are native: declared here, answered there
        // (NativeCommands, plan_server_novalve.md P2.3c).
        n += _commands.RegisterNativeBodied(NativeCommands.Server, _services.Require<IEngineConsole>());
#if DEBUG
        // The diagnostics: their native bodies are compiled into a debug
        // server.so only (server_sourcesharp_debug_sources).
        n += _commands.RegisterNativeBodied(NativeCommands.Debug, _services.Require<IEngineConsole>());
#endif
        _commands.PublishTo(_services);

        Log.Msg($"[SourceSharp/Server] registered {n} console command(s) by attribute\n");
        Log.Msg($"[SourceSharp/Server] registered {v} convar(s) by attribute\n");
        Log.Msg($"[SourceSharp/Server] {engineAreas} engine area(s) in the container\n");
        Log.Msg($"[SourceSharp/Server] {_services.ServiceTypes.Count} service(s), " +
                   $"{_services.Names.Count} console name(s) in the container\n");

        _netTableProfile = _convars.Get<NetTableProfileConVar>();
        _greeting        = _convars.Get<GreetingConVar>();
        _verboseJoinsVar = _convars.Get<VerboseJoinsConVar>();
        _damageScale     = _convars.Get<DamageScaleConVar>();
        _armourRatio     = _convars.Get<ArmourRatioConVar>();
        _armourBonus     = _convars.Get<ArmourBonusConVar>();
        _spawnHealth     = _convars.Get<SpawnHealthConVar>();
        _allowFlashlight = _convars.Get<AllowFlashlightConVar>();
        if (_allowFlashlight is { } allowFlashlight)
            _flashlight.Allowed = () => allowFlashlight.Value;
        _bannedNames     = _convars.Get<BannedNamesConVar>();

        // Nothing publishes on the generated shell here any more:
        // sourcesharp_netentity was its only type, and the shell is deleted.
        // Every type this side publishes goes through StandaloneNetStates.
        int layoutCount = _layouts.DiscoverAll(Game);
        Log.Msg($"[SourceSharp/Server] discovered {layoutCount} entity layout(s), " +
                   $"fingerprint 0x{_layouts.Fingerprint:X8}\n");
        _friendlyFire    = _convars.Get<FriendlyFireConVar>();

        // plan_map_load P5: compile this side's methods off the main thread
        // while the game starts, so the first map load does not JIT them. The
        // context stays collectible (hot reload); Shutdown stops this first.
        _jitWarmup = new JitWarmup(_services.Require<ILog>());
        _jitWarmup.Start("server", _jitWarmup.TypesOfContextOf(typeof(ServerModule).Assembly));
    }

    public void Frame(float frameTime)
    {
        // Deliberately allocation-free: this runs every tick, and a GC pause
        // here shows up as a frame hitch. See plan.md Part 1, risks.
        _ticks.Advance();

        // The penetration ledger's frame (stock's UpdatePenetrateEvents, after
        // the step), then the physics step's entity events, now that PhysFrame
        // has run (physics stock parity: stock's post-simulation delivery).
        _physicsSolver?.FrameUpdate();
        _physicsRouter?.Flush();

        // THE LEVEL'S FIRST FRAME: activation has run and the socket is bound,
        // so a client could connect. Once per level.
        if (_levelReadyPending)
        {
            _levelReadyPending = false;
            _levelInfo.IsReady = true;
            _lifecycle?.LevelReady(_currentMap);
        }

        // The payload traffic view (Part 13, Step 9). Off by default and a
        // single bool test when it is, which is what keeps this hook
        // allocation-free in the case that matters.
        //
        // NOT A PARTICIPANT, for the same reason as MapDecal in LevelShutdown:
        // it is a static, nothing registers or resolves it, and it needs
        // NetFieldsOf -- a method on this module, not on any service.
        //
        // It used to run BETWEEN the soundscape update and the arsenal update
        // and now runs before both, because the driver calls them as one
        // sequence and this cannot sit inside it. That is not a regression: it
        // samples network payloads, which are written during the entity thinks
        // that have already finished by the time this hook is called at all, so
        // sampling before the two managed writers sees the settled frame rather
        // than a half-updated one. It is also a diagnostic that does nothing
        // unless a convar turns it on.
        _netTraffic.Frame(_netTableProfile?.Value ?? false, NetFieldsOf);

        // Every IFrameLifecycle, in FrameOrder. This is the MIDDLE of the frame
        // -- FrameUpdatePostEntityThink -- and it is where the soundscape and
        // arsenal updates went, followed by the three that used to be reached
        // through CSourceSharpGameRules::Think.
        _frames.Think(frameTime);

        // Anything the development API was asked to do runs here, on the game
        // thread. Its HTTP handlers answer on thread-pool threads and the engine
        // is single-threaded, so this is the only place their work can happen.
        //
        // NOT A PARTICIPANT: the dev API is the module's own, is null unless it
        // was started, and has to run after everything else in the frame -- it
        // is the thing that observes the frame's results.
        _debugApi?.Pump();

        // After a cs_sv_reload: the clients already in had their sessions and
        // their resume data die with the old listener (DebugChannelReloadInvites).
        if (_debugApi is not null)
        {
            foreach (int slot in _reloadInvites.Due(s => _connected.UserId(s) >= 0))
                InviteToDebugChannel(slot);
        }
    }

    /// <summary>
    /// <c>IServerGameDLL::Think</c> — once per frame, level or no level.
    ///
    /// <para>
    /// <b>Housekeeping, and today that is exactly one thing.</b> The dev API's
    /// pump normally runs at the end of <see cref="Think"/>, which is the
    /// managed frame — and the managed frame is driven by
    /// <c>CSourceSharpSystem</c>, which nothing drives with
    /// <c>-shellgamedll</c> until plan_gamedll.md Phase 7. So in shell mode
    /// every <c>./debug-api</c> command answered <i>"timed out; is the game running
    /// frames?"</i>, including the entity count that is Phase 6's own gate.
    /// </para>
    ///
    /// <para>
    /// <b>Pumping twice in delegate mode is harmless and is the point.</b>
    /// <c>Pump</c> drains whatever is queued and returns; a second call in the
    /// same frame finds an empty queue. What it buys is that the dev API answers
    /// <i>at the main menu and between maps</i>, which it never has — the frame
    /// it used to depend on does not run there in either mode.
    /// </para>
    ///
    /// <para>
    /// <b>No lifecycle participant runs from here, and none may be added.</b>
    /// Nothing reached from this method may assume a level is loaded; that is
    /// the whole difference between this and <see cref="GameFrame"/>.
    /// </para>
    /// </summary>
    public void EngineThink()
    {
        // Every frame, level or no level: the frame stamp a hosting SDK's
        // stall watch reads, the work other threads posted, then the delays.
        _gameThread.RunFrame(_log!);
        _scheduler.Tick();
        _debugApi?.Pump();
    }

    /// <summary>
    /// Before any entity has thought this frame.
    /// CSourceSharpSystem::FrameUpdatePreEntityThink.
    /// </summary>
    public void FramePreThink(float frameTime) => _frames?.PreThink(frameTime);

    /// <summary>
    /// The end of the game DLL's frame, after the entity I/O queue has fired.
    /// Reached from CSourceSharpGameRules::EndGameFrame.
    /// </summary>
    public void FramePostThink(float frameTime) => _frames?.PostThink(frameTime);

    /// <summary>
    /// The fourth frame point — stock's <c>PreClientUpdate</c>.
    ///
    /// <para>
    /// <b>UNTIL plan_gamedll_after.md Phase A3's SECOND PAIR, NOTHING CALLED
    /// IT.</b> Part 3a added <c>IFrameLifecycle.OnPreClientUpdate</c> and
    /// <c>FrameDriver.PreClientUpdate</c> beside it, and no production caller
    /// was ever wired: <c>Entry.ServerPreClientUpdate</c> reaches
    /// <c>ServerGameDll.PreClientUpdate</c>, which is a different method on a
    /// different interface, and the driver's own point was driven only by two
    /// tests. That is the SECOND stage in this tree found in exactly that
    /// state — the first pair found <c>OnLevelInitPreEntity</c> the same way —
    /// and it is the same lesson: a stage with a driver, a dispatcher and its
    /// own tests, and no production caller, passes every check that exists and
    /// does nothing.
    /// </para>
    ///
    /// <para>
    /// <c>SoundController</c> is what needed it. Stock's
    /// <c>CSoundControllerImp</c> ran its whole per-frame pass from
    /// <c>PreClientUpdate</c> and from no other stage.
    /// </para>
    /// </summary>
    public void FramePreClientUpdate(float frameTime) => _frames?.PreClientUpdate(frameTime);

    /// <summary>
    /// One standalone player command, after the movement (phase 1) - where
    /// stock ran ItemPostFrame from PostThink. The machine writes only the
    /// declared blobs, which is what lets the client replay the same call.
    /// </summary>
    public void OnStandalonePlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime)
    {
        if (phase != 1)
            return;

        _arsenal.OnPlayerCommand(player, in cmd, firstTime);

        // The impulse cheats, AFTER the weapon frame — stock's order: player.cpp
        // ran ImpulseCommands at the end of ItemPostFrame. The sv_cheats gate is
        // inside Run, on the cheats ONLY: this crossing itself must never sit
        // behind it (the client-command gate got that wrong once already).
        // The flashlight's impulse first, and outside the cheats: stock's
        // ImpulseCommands answered 100 itself and handed every other number to
        // CheatImpulseCommands.
        if (firstTime && cmd.Impulse != 0 && !_flashlight.OnImpulse(player, cmd.Impulse))
            _impulses.Run(player, cmd.Impulse);
    }

    /// <summary>
    /// Every networked type this side describes, built once and kept.
    ///
    /// A method rather than the lambda it used to be, because the traffic view
    /// needs the same list and a route hook is write-only from out here — two
    /// callers of one lazy field want a name, not a closure each.
    /// </summary>
    private IReadOnlyList<NetSchemaType> NetSchemaTypes()
        => _netSchema ??= NetSchema.Describe(
            _services.Require<INetTiers>(),
            // NO GENERATED-SHELL LAYOUTS. The parameter stays — see
            // NetSchema.Describe — but nothing declares on that shell any more,
            // so every described type comes from the hierarchy's list below.
            Array.Empty<EntityLayout>(),
            StandaloneNetStates.Discover(Game, Log),
            _entities.IsRegistered);

    /// <summary>
    /// One class's declared fields, for attributing a changed word to a name.
    ///
    /// Null for a class with no schema on this side, which the sampler treats as
    /// "count the words, name no fields" — a class whose layout we do not have
    /// still costs what it costs, and reporting the total without the breakdown
    /// is better than dropping it.
    /// </summary>
    private IReadOnlyList<NetSchemaField>? NetFieldsOf(string className)
    {
        if (_netFieldsByClass is null)
        {
            var byClass = new Dictionary<string, IReadOnlyList<NetSchemaField>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var type in NetSchemaTypes())
                byClass[type.ClassName] = type.Fields;

            _netFieldsByClass = byClass;
        }

        return _netFieldsByClass.TryGetValue(className, out var fields) ? fields : null;
    }

    /// <summary>
    /// A map's name, from what the engine calls it.
    ///
    /// The engine says "maps/dm_lockdown.bsp", which is a path — and everything
    /// that takes a map name takes a name, refusing anything with a separator in
    /// it because a name from a browser must not be able to reach out of the
    /// maps directory. Passing the engine's version straight through meant the
    /// development API rejected its own current map.
    /// </summary>
    internal static string BareMapName(string mapName)
    {
        string name = mapName.Replace('\\', '/');

        int slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        return name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    public void LevelInit(string mapName)
    {
        _currentMap = BareMapName(mapName);
        _levelInfo.Map = _currentMap;
        _levelInfo.IsReady = false;
        _levelReadyPending = true;
        if (_clients is not null)
            _clients.LevelShuttingDown = false;

        Log.Msg($"[SourceSharp/Server] level init: {mapName}\n");

        // EVERY IGameLifecycle THE CONTAINER KNOWS ABOUT, IN SEQUENCE.
        //
        // This was six hand-written lines -- reset the round, the pickups, the
        // corpses, the players, the buttons, the arsenal -- and every one of
        // them was a service remembering to be remembered. A new service that
        // needs clearing on a map change joined that list or it did not, and
        // nothing said which. That is exactly the failure the [Precache]
        // attribute a few lines below was introduced to end, and it was still
        // here in the method that introduces it.
        //
        // Participation is declared now: a service implements IGameLifecycle
        // and ResolveAll finds it. The ORDER is the same order those six lines
        // ran in, carried by LifecycleOrder rather than by their position here,
        // because container registration order is not the order they need --
        // the arsenal registers first and has to run last.
        _lifecycle.LevelInit(_currentMap);

        // What a flame is drawn and heard with. Nothing else precaches it now:
        // PRECACHE_REGISTER(entityflame) and CEntityFlame::Precache left the
        // build with EntityFlame.cpp, and a flame is created when something
        // catches fire, which is far too late to precache anything.
        Flames.Precache(_services.Require<IEnginePrecache>());

        // EVERY [Precache] IN THE BUILD, declared on the class that needs it and
        // found across every loaded assembly that could carry one.
        //
        // NOT A NAMED LIST OF ASSEMBLIES. That was the first version and it had
        // already missed something: the temp entity models live in Shared beside
        // their declarations and env_spark's DoSpark lives in Server beside
        // EnvSpark, so a sweep over one assembly silently skipped the other's.
        // Naming them by hand just moves the "did anyone remember to add it"
        // failure up a level, which is the failure this attribute exists to end.
        //
        // It replaces four hand-written PrecacheModel lines and one
        // PrecacheSound buried in an OnSpawn that returns early for a spark
        // which starts off — so the sound was precached for exactly the sparks
        // that were already audible (behaviour ledger row S7). An attribute has
        // no early return.
        int precached = ServerPrecache.PrecacheAll(_services.Require<IEnginePrecache>(), Log);

        if (precached == 0)
        {
            Log.Warning(
                "[SourceSharp] the [Precache] sweep found nothing. Every asset a class "
                + "declares would then be absent at runtime, which reads as a missing effect "
                + "rather than as an error here.\n");
        }

        // worldspawn's one game-logic key, read from the native adapter. The
        // world has existed since long before LevelInit — it is the FIRST
        // thing the map spawns — so the key is already parsed by now.
        _chapterTitle.LevelInit(_world.WorldChapterTitle());

        // The map's decals, painted HERE and nowhere else.
        //
        // This is the parallel hierarchy's Activate: every map entity has
        // spawned and taken its position, so a decal's trace can hit a door
        // that only moved into place during its own Spawn, and nothing has
        // been sent to a client yet, so a permanent decal still reaches the
        // signon data. Stock says the same thing with CDecal::Activate, which
        // this hierarchy has no equivalent of.
        MapDecal.PaintPlaced(_pendingDecals, Log);

        CreatePlayerResource();
    }

    //-------------------------------------------------------------------------
    // The scoreboard singleton (lane U cluster 14).
    //
    // WHAT THIS REPLACES. CGameRules::CreateStandardEntities did exactly one
    // thing -- `CBaseEntity::Create( "player_manager", ... )` -- and
    // player_manager was CPlayerResource, a CBaseEntity with a SendTable of its
    // own. The entity is a managed one now, so the creation moved here, and
    // CSourceSharpGameRules::CreateStandardEntities overrides the stock version
    // to nothing so that nobody makes a second one.
    //
    // HERE RATHER THAN IN C++, and this is the point of the port: creating it
    // from the server module needs no native code at all. Nothing else about a
    // singleton wants a shell.
    //
    // AT LEVEL INIT, which is after the map's entities have spawned and before
    // any client has connected -- so the entity is in the signon data and a
    // client that joins on the first tick already has a scoreboard rather than
    // one that appears a fraction of a second later.
    //-------------------------------------------------------------------------
    private void CreatePlayerResource()
    {
        var resource = _entityApi.Create(EntityNames.PlayerManager);

        if (resource == EntityHandle.None)
        {
            // Loud, because everything downstream of it fails QUIETLY: the
            // client's ClientPlayerResource never finds the entity, so the
            // scoreboard, kill feed, chat and spectator bar read every row as
            // zero or empty, and nothing in any log says the entity was never
            // made. (The native IGameResources, g_PR, retired on the client.)
            Log.Warning("[SourceSharp/Server] could not create the "
                        + $"{EntityNames.PlayerManager} scoreboard singleton; "
                        + "every client's ClientPlayerResource will read nothing\n");
            return;
        }

        _entityApi.Spawn(resource);

        Log.Msg($"[SourceSharp/Server] {EntityNames.PlayerManager} up, "
                + $"{PlayerResourceNetState.Slots} slot(s), "
                + $"{PlayerResourceNetState.NetLayout.Size} byte(s) of scoreboard\n");
    }

    //-------------------------------------------------------------------------
    // Entity instances are dropped HERE, not at level init.
    //
    // Map entities spawn during the load, and LevelInit runs after that — so
    // clearing there threw away every instance the map had just bound, leaving
    // the entities alive natively with nothing behind them. Everything still
    // spawned, which is why it looked fine: what stopped working was every
    // input, touch and think after the load, silently.
    //-------------------------------------------------------------------------
    public void LevelShutdown()
    {
        Log.Msg($"[SourceSharp/Server] level shutdown after {_ticks.Count} ticks\n");
        _physicsRouter?.ForgetSleep();

        // Every player entity is removed below and its client stays: not a disconnect.
        if (_clients is not null)
            _clients.LevelShuttingDown = true;
        _levelInfo.IsReady = false;
        _levelReadyPending = false;
        _entities.Clear();
        _physicsRouter?.LevelShutdown();
        _physicsFluidEvents?.Clear();

        // Every debug channel invitation still outstanding. An offer made to
        // this map's client must not still be redeemable by the next map's —
        // this tree has already paid once for state that outlived its level.
        _debugApi?.LevelShutdown();

        // Every IGameLifecycle again, this time on the way out. The arsenal and
        // the soundscapes were the two SERVICES in this list and they declare
        // their own participation now.
        _lifecycle.LevelShutdown();

        // What threw during the frames, counted rather than logged per tick.
        // Here because this is the first moment printing it is not itself the
        // flood -- see FrameDriver.
        _frames.ReportFaults();

        // WHAT FOLLOWS IS MODULE STATE CLEARED HERE, IN THIS ORDER, rather than
        // in a lifecycle participant: the pending-decal roster (a static list on
        // MapDecal until the owner's 2026-09-29 rule that managed state comes
        // from the container) and _netFieldsByClass. (This comment used to name
        // NetTrafficProfiler too; it is a service now and clears itself in its
        // own OnLevelShutdown.)
        //
        // Any decal that never painted — a named one whose Activate never came.
        // Handles do not survive a level change, so a leftover row would name
        // an entity that no longer exists.
        _pendingDecals.Clear();


        // The schema is rebuilt per level with the entity list; the class map
        // derived from it has to go with it.
        _netFieldsByClass = null;

        // The 3D skybox is level data held on the NATIVE side, because that is
        // where the player's networked copy is read from. Nothing else drops it,
        // so a map with no sky_camera would otherwise render the last map's
        // miniature world. Here rather than at level init, because init runs
        // after entities have spawned and would wipe a fresh push.
        _visibility.ClearSkyboxParams();

        // LAST: the sound emitter system's level end (the overrides cleared,
        // the precache log closed), the CSoundEmitterSystem row's
        // LevelShutdownPostEntity. That row sat before SourceSharpManaged and
        // the pass walks backwards, so it ran after everything managed did
        // (plan_server_native_v2 T4a).
        _soundSystem?.LevelShutdownPostEntity();
    }

    public void ConCommandDispatch(int commandId, string args)
    {
        if (!_commands.Dispatch(commandId, args))
            Log.Warning($"[SourceSharp/Server] no command bound to id {commandId}\n");
    }

    public void OnConVarChanged(int id, string newValue, string oldValue)
    {
        if (!_convars.NotifyChanged(id, newValue, oldValue))
            Log.Warning($"[SourceSharp/Server] no convar bound to id {id}\n");
    }

    //-------------------------------------------------------------------------
    // Gameplay decisions. These are real rules, now decided in C# — the native
    // shell only applies what is returned here.
    //-------------------------------------------------------------------------

    public void OnPlayerSpawn(EntityHandle player)
    {
        // Apply the spawn health rule through the handle — proof C# both reads
        // and writes entity state safely.
        int health = _spawnHealth?.Value ?? 100;
        _entityApi.SetHealth(player, health);

        Log.Msg($"[SourceSharp/Server] {player} spawned at {_entityApi.GetOrigin(player)}, " +
                   $"health set to {_entityApi.GetHealth(player)} (damage x{_damageScale?.Value ?? 1f:F2})\n");

        // Run the handle-safety check here rather than from a console command:
        // spawn is the one moment a live player handle is guaranteed to exist.
        VerifyHandleSafety(player);
    }

    /// <summary>
    /// Which spawn point a player should enter the map on.
    ///
    /// This is a rule and it is written here, not in C++. The engine's own
    /// search — CBasePlayer::EntSelectSpawnPoint — walks gEntList for a
    /// CBaseEntity called info_player_deathmatch, and there is not one: the
    /// class is C#'s. Every branch of that search falls through and leaves the
    /// player at the world origin, silently, which reads as a broken map.
    ///
    /// <para>
    /// It ANSWERS rather than places, and that distinction was learnt the hard
    /// way. The first version teleported the player from OnPlayerSpawn, which
    /// runs after the shell has made them solid — so a player was made solid at
    /// the world origin and moved out of it afterwards, and arrived embedded in
    /// whatever was at the destination. Reported from a real playthrough as
    /// "the player is spawning in the wall". The engine asks this at the point
    /// in CBasePlayer::Spawn where it would have searched itself, before the
    /// hull and the physics shadow exist.
    /// </para>
    ///
    /// <para>
    /// One difference from stock, on purpose: when every point is occupied,
    /// stock kills whoever is standing on the first one. This picks a random
    /// occupied point instead. A mod that kills a player for standing still is
    /// a decision, and this one does not make it.
    /// </para>
    /// </summary>
    /// <summary>
    /// The ground under a player changed. Handed straight to the entities that
    /// asked to hear about it.
    ///
    /// <para>
    /// Nothing is filtered here on purpose. Whether a particular material is
    /// the one a trigger cares about is that trigger's question, and answering
    /// it here would mean this module knowing what each of them wants.
    /// </para>
    /// </summary>
    public void OnPlayerSurfaceChanged(EntityHandle player, int gameMaterial)
        => EnvPlayerSurfaceTrigger.PlayerSurfaceChanged(_surfaceTriggers, player, gameMaterial);

    public EntityHandle SelectSpawnPoint(EntityHandle player)
    {
        // The selectors first (tf2 stage 0.5/0.7): the active game mode's own
        // IGameMode.Spawns, then the registered ISpawnSelector chain in
        // HookOrder's order (ITeams.SelectSpawn), and the first named spot
        // wins. None named (no mode spawns and no selectors, or every one
        // falling through) is the core's walk below, unchanged: HL2DM is
        // team-blind today and stays so until a mod registers otherwise.
        if (_services.Resolve<ITeams>() is { } teams)
        {
            EntityHandle selected = teams.SelectSpawn(player);
            if (selected != EntityHandle.None)
                return selected;
        }

        var candidates = new List<EntityHandle>();

        // Deathmatch points first, and info_player_start only if there are
        // none. They are not peers: a map with both means the deathmatch ones,
        // and mixing them would drop a player at the single-player start now
        // and then for no stated reason.
        foreach (string className in Pickups.SpawnPointClasses)
        {
            foreach (var spot in _entityApi.FindAllByClassname(className))
            {
                // A spawn point left at the world origin is a mapping mistake
                // common enough that the engine skips it by hand too.
                var at = _entityApi.GetOrigin(spot);

                if (at.X != 0f || at.Y != 0f || at.Z != 0f)
                    candidates.Add(spot);
            }

            if (candidates.Count > 0)
                break;
        }

        // Nothing to choose from. The engine's own search runs instead, which
        // is the honest answer: a map with no C# spawn points may well have
        // CBaseEntity ones, and this must not make that map worse.
        if (candidates.Count == 0)
            return EntityHandle.None;

        // Preferring one nobody is standing in, and settling for a random one
        // when every point is taken.
        int first = _spawnRandom.Next(candidates.Count);
        EntityHandle chosen = candidates[first];

        for (int i = 0; i < candidates.Count; i++)
        {
            var spot = candidates[(first + i) % candidates.Count];

            if (!IsSpawnPointOccupied(spot, player))
            {
                chosen = spot;
                break;
            }
        }

        // ---- AND THE PLACEMENT, WHICH IS NOW THIS SIDE'S TOO -- step 2e ----
        //
        // WHAT BROKE, and it is the reason this is here rather than in C++.
        // CStandalonePlayer::Spawn used to resolve the handle this method
        // returns and read the SHELL's transform off it. Every spawn-point
        // class -- info_player_deathmatch, info_player_start -- is of kind
        // Logical, so as of this step it HAS no shell: the resolve answered
        // NULL, the whole `if ( pSpot )` block was skipped, and the player
        // spawned at the world origin. Measured on dm_lockdown: ss_player at
        // (-3893.09, 1413.9, 0.83) before, (0, 0, 0) after, with 24 of the 118
        // engine-invisible reaches in that run coming from that one resolve.
        //
        // The MOMENT is unchanged and that is the whole constraint the ABI's
        // own comment records: this method is called from inside
        // CBasePlayer::Spawn, before the player is made solid and before its
        // hull and physics shadow are built. Placing from OnPlayerSpawn instead
        // put players inside walls. PlayerPlaceAtSpawn runs here, inside that
        // same call, and does exactly what the native block did.
        //
        // The one-unit lift is stock's and is applied here because it is part
        // of the placement rule: a player put exactly on the point starts
        // intersecting the floor.
        var origin = _entityApi.GetOrigin(chosen);
        origin.Z += 1f;

        _playerApi.PlaceAtSpawn(player, origin, _entityApi.GetAngles(chosen));

        // NOTHING, so the native block does not place the player a second time.
        // A shelled spawn point would resolve there and be applied on top of
        // this; returning the handle would make which of the two won depend on
        // whether the class happened to have a shell.
        return EntityHandle.None;
    }

    /// <summary>
    /// Whether a player is already standing on a spawn point. The radius is
    /// stock's, because this replaces the engine's SEARCH and not its idea of
    /// what makes a point usable.
    /// </summary>
    private bool IsSpawnPointOccupied(EntityHandle spot, EntityHandle spawning)
    {
        Span<EntityHandle> nearby = stackalloc EntityHandle[16];
        int found = _world.EntitiesInSphere(_entityApi.GetOrigin(spot), 128f, nearby);

        for (int i = 0; i < found; i++)
        {
            if (nearby[i] != spawning && _entityApi.IsPlayer(nearby[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Which spawn point a player gets. Seeded rather than shared so the choice
    /// is independent of anything else drawing random numbers this frame.
    /// </summary>
    private readonly Random _spawnRandom = new();

    /// <summary>
    /// Checks the property the handle design exists for: a stale serial must not
    /// resolve. plan.md Part 6 group C calls slot reuse the most important case
    /// here, because a bare entity index cannot distinguish it.
    /// </summary>
    private void VerifyHandleSafety(EntityHandle live)
    {
        if (_verifiedHandles) return;
        _verifiedHandles = true;

        bool liveOk    = _entityApi.IsValid(live);
        bool staleOk   = !_entityApi.IsValid(new EntityHandle(live.Value ^ 0x8000_0000u));
        bool noneOk    = !_entityApi.IsValid(EntityHandle.None);

        Log.Msg($"[SourceSharp/Server] handle check: live={(liveOk ? "resolves" : "FAILED")}, " +
                   $"stale-serial={(staleOk ? "rejected" : "FAILED")}, " +
                   $"none={(noneOk ? "rejected" : "FAILED")}\n");
    }

    private bool _verifiedHandles;

    // VerifyEntityBlob went with the layout it used, and it had become a check
    // that could not fail: it round-tripped two fields through the PLAYER's
    // blob using the netentity shell's EntityLayout. That was real when the
    // player shared that shell's storage; since the player became a
    // CStandaloneEntity with its own PlayerNetState, the write and the read
    // landed at the same two offsets in a blob neither described, agreed with
    // each other, and reported "ok" forever.

    public void OnPlayerKilled(EntityHandle player, EntityHandle attacker) => _deaths.OnKilled(player, attacker);

    /// <summary>
    /// Admission control. Refuses names on the ban list ConVar — a real rule,
    /// decided entirely in C#.
    /// </summary>
    public string? OnClientConnect(string name, string address)
    {
        string banned = _bannedNames?.Value ?? string.Empty;

        if (banned.Length > 0)
        {
            foreach (var entry in banned.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(entry.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Msg($"[SourceSharp/Server] refusing '{name}' from {address}\n");
                    return $"The name '{name}' is not permitted on this server.";
                }
            }
        }

        if (_clients?.Connecting(name, address) is { } refusal)
        {
            Log.Msg($"[SourceSharp/Server] refusing '{name}' from {address}: {refusal}\n");
            return refusal;
        }

        Log.Msg($"[SourceSharp/Server] admitting '{name}' from {address}\n");
        return null;
    }

    /// <summary>The pose of whoever the observer at <paramref name="entIndex"/> watches (FullObserverMove's target).</summary>
    private (Vector Origin, Vector Angles, Vector Velocity)? ObservedPose(int entIndex)
    {
        var handle = _playerApi.GetHandle(entIndex);
        if (_entities.Get(handle) is not SsPlayer watcher || _services.Resolve<Observers>()?.TargetOf(watcher) is not { } target)
            return null;

        return (_entityApi.GetOrigin(target.Handle), _entityApi.GetAngles(target.Handle), _entityApi.GetVelocity(target.Handle));
    }

    /// <summary>First refusal on client commands.</summary>
    public CommandResult OnClientCommand(EntityHandle player, string command)
    {
        // Weapon switching (Phase 10). Console-shaped because the stock
        // selection UI lives in client files this build does not compile;
        // ss_use names a classname, ss_slot an inventory position, and
        // invnext / invprev / lastinv are the stock binds, forwarded by name
        // from shell_hud.cpp. Each of the five is its own verb: they were not,
        // and three of them stepping forward one slot is what "switching is
        // flaky" turned out to be.
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // THE MODS FIRST (tf2 stage 0.6): every registered IClientCommandHook
        // in HookOrder's order, and the first that says true owns the
        // command — nothing here runs after it, because the word was one
        // action and it happened. A hook that throws is named in the log and
        // skipped: the chain carries on, and an unclaimed word falls through
        // to the core's own verbs below, exactly as before this seam existed.
        if (parts.Length >= 1 && _services.Resolve<IHooks<IClientCommandHook>>() is { } hooks)
        {
            // The args once, as a view over the split: no copy per hook asked.
            ReadOnlySpan<string> args = parts.AsSpan(1);
            foreach (var hook in hooks.All)
            {
                bool claimed;
                try
                {
                    claimed = hook.Handle(player, parts[0], args);
                }
                catch (Exception e)
                {
                    // Hooks.Each logs this shape; the chain itself is not the thrower's.
                    Log.Warning($"[SourceSharp/Server] client command hook " +
                                $"{hook.GetType().FullName} threw on '{command}': {e}\n");
                    continue;
                }

                if (claimed)
                    return CommandResult.Handled;
            }
        }
        if (parts.Length >= 1)
        {
            if (parts[0].Equals("ss_use", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
                return Selected(player, command, _arsenal.Switch(player, parts[1]));

            if (parts[0].Equals("ss_slot", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2
                && int.TryParse(parts[1], out int slot))
                return Selected(player, command, _arsenal.SwitchSlot(player, slot));

            if (parts[0].Equals("invnext", StringComparison.OrdinalIgnoreCase))
                return Selected(player, command, _arsenal.SwitchNext(player));

            if (parts[0].Equals("invprev", StringComparison.OrdinalIgnoreCase))
                return Selected(player, command, _arsenal.SwitchPrev(player));

            if (parts[0].Equals("lastinv", StringComparison.OrdinalIgnoreCase))
                return Selected(player, command, _arsenal.SwitchLast(player));

            // The damage-rule cheats. Handled HERE, before the crossing's
            // cheat gate, because the refusal itself must be out loud —
            // DamageCheats gates itself and says so either way.
            if (parts[0].Equals("god", StringComparison.OrdinalIgnoreCase))
            {
                _damageCheats.ToggleGod(player);
                return CommandResult.Handled;
            }

            if (parts[0].Equals("buddha", StringComparison.OrdinalIgnoreCase))
            {
                _damageCheats.ToggleBuddha(player);
                return CommandResult.Handled;
            }

            // The observer commands (CBasePlayer::ClientCommand): spec_mode,
            // spec_next, spec_prev, spec_player. See Observers.
            if (_entities.Get(player) is SsPlayer watcher)
            {
                var observers = _services.Require<Observers>();
                // The container's clock, not the field: the field is set
                // later in Initialize, and this arm is also the one the
                // composition-only harnesses (and the 0.6 facts) drive.
                float now = _services.Require<IGameClock>().Now;
                switch (parts[0].ToLowerInvariant())
                {
                    case "spec_mode":
                        observers.SpecMode(watcher, parts.Length >= 2 ? parts[1] : null, now);
                        return CommandResult.Handled;
                    case "spec_next":
                        observers.SpecNext(watcher, reverse: false, now);
                        return CommandResult.Handled;
                    case "spec_prev":
                        observers.SpecNext(watcher, reverse: true, now);
                        return CommandResult.Handled;
                    case "spec_player":
                        if (parts.Length == 2)
                            observers.SpecPlayer(watcher, parts[1], now);
                        return CommandResult.Handled;
                    case "spectate":
                        // CHL2MP_Player's "spectate": HandleCommand_JoinTeam(
                        // TEAM_SPECTATOR) — the same route as `jointeam 1`, so
                        // the team is stored before the observer half runs.
                        _services.Require<ITeams>().Join(player, Observers.TeamSpectator);
                        return CommandResult.Handled;
                }
            }

            // The UI's team menu (plan_client_ui U3) sends stock's `jointeam N`.
            // Routed to the team service: its arms are stock's
            // (CHL2MP_Player::HandleCommand_JoinTeam) — the spectator team is
            // the observers' Spectate, an out-of-range number is their stock
            // warning, the single-owner IJoinTeamPolicy decides a playing
            // team BEFORE anything changes, and a playing team stops
            // observing (StopObserverMode, STATE_ACTIVE), sets the team
            // through the PlayerSetTeam slot and raises player_team.
            if (parts[0].Equals("jointeam", StringComparison.OrdinalIgnoreCase))
            {
                // Stock's `atoi( args[1] )`: the leading digits ("2x" is 2),
                // and no argument is team 0, which Observers' validity gate
                // refuses with the warning.
                int requested = Observers.Atoi(parts.Length > 1 ? parts[1] : null);
                _services.Require<ITeams>().Join(player, requested);
                return CommandResult.Handled;
            }

            // Stock's `use <weapon_name>` is ss_use by another name — its
            // client.cpp handler was one of the dead ones, calling SelectItem
            // on a CBasePlayer that is never there. Routed to the same arsenal
            // rather than reimplemented, so one selection rule serves both
            // spellings.
            if (parts[0].Equals("use", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    Log.Msg("usage: use <weapon classname>\n");
                    return CommandResult.Handled;
                }

                return Selected(player, command, _arsenal.Switch(player, parts[1]));
            }
        }

        if (!command.StartsWith("cs_say", StringComparison.OrdinalIgnoreCase))
            return CommandResult.NotHandled;

        Log.Msg($"[SourceSharp/Server] {player} said: {command}\n");
        return CommandResult.Handled;
    }

    /// <summary>
    /// A selection command, and what became of it.
    ///
    /// <para>
    /// Every refusal is said out loud, which is the point of this existing at
    /// all. The switch commands used to swallow their own answer — ss_slot and
    /// lastinv discarded a bool, and only ss_use ever said anything — so a
    /// switch that did nothing was indistinguishable, from outside the game,
    /// from a switch that worked. Driving weapons from a script against that is
    /// how "weapon switching is flaky" gets reported instead of "a dead player's
    /// switches are dropped", which is what it was.
    /// </para>
    ///
    /// <para>
    /// Dropping them is still the right answer — a dead player has no weapons
    /// after the death cleanup strips them, so there is nothing to switch to —
    /// but it is now a line in the console naming the player, the command and
    /// the reason, rather than silence.
    /// </para>
    ///
    /// <para>
    /// Handled either way: the command was for us and the client must not go
    /// looking for another handler because we said no.
    /// </para>
    /// </summary>
    private CommandResult Selected(EntityHandle player, string command, SwitchOutcome outcome)
    {
        if (outcome != SwitchOutcome.Switched)
            Log.Msg($"[SourceSharp/Arsenal] {player} '{command}' changed nothing: {Because(outcome)}\n");

        return CommandResult.Handled;
    }

    /// <summary>Why a switch did nothing, in words a player or a script can act on.</summary>
    private static string Because(SwitchOutcome outcome) => outcome switch
    {
        SwitchOutcome.AlreadyHeld     => "already holding it",
        SwitchOutcome.PlayerIsDead    => "the player is dead, and death strips the inventory",
        SwitchOutcome.NothingCarried  => "the player carries nothing",
        SwitchOutcome.NotArmed        => "no inventory for this entity",
        SwitchOutcome.NotOwned        => "the player does not own that weapon",
        SwitchOutcome.NoSuchSlot      => "no weapon in that slot",
        SwitchOutcome.NoHistory       => "nothing was held before this",
        _                             => outcome.ToString(),
    };

    /// <summary>Everyone is hostile unless the ConVar says otherwise.</summary>
    public PlayerRelationship GetPlayerRelationship(EntityHandle player, EntityHandle target)
        => _friendlyFire?.Value == false
            ? PlayerRelationship.Teammate
            : PlayerRelationship.UseStockRule;

    /// <summary>
    /// Hot reload, the way out: every live entity's state into the ABI, which is
    /// the only thing that outlives this assembly.
    /// </summary>
    public int ReloadSave()
    {
        // The liveness records describe entities that outlive this module; a
        // fresh mirror would send every handle from before the reload back to
        // the slot. See LivenessMirror.Save.
        ReloadStore reload = _engine.Reload;
        reload.Put(LivenessMirror.ReloadSection, _liveness.Save(), 1);

        int rows = _entities.SaveForReload(reload);

        // AND THE STATE THAT IS NOT AN ENTITY'S -- the third tier. Every
        // IGameLifecycle participant that implements ISaveCustom writes a record
        // of its own: the parent links, the touching pairs, the think bookings,
        // the movers mid-travel, the pickups this level init already placed.
        // None of that is a field on an entity, so no declaration on an entity
        // could have carried it, and all of it died with the module.
        //
        // AFTER the entity rows, because a participant's record may name
        // handles and the rows are what the report counts first. The ORDER of
        // the two saves does not matter; the order of the two RESTORES does,
        // and is argued at ReloadRestore.
        var parts = _lifecycle?.SaveState();

        if (parts is not null)
        {
            foreach (var (_, record) in parts)
                ReloadCarrier.PutPart(reload, record);
        }

        // MEASURED FROM THE STORE, NOT FROM WHAT THE DRIVER HANDED OVER. A
        // mutation that dropped every record inside PutPart left this report
        // still saying "2 subsystem record(s)", because it was counting the
        // offer rather than the deposit -- which is precisely the shape of
        // readout this whole tally exists to replace.
        reload.NoteSaved(SavedEntityCodec.Measure(
            _entities.LastCollected, ReloadCarrier.PeekParts(reload),
            ReloadCarrier.PeekIo(reload), ReloadCarrier.ShellLessCount(reload)));

        var tally = reload.Saved;

        Log.Msg($"[SourceSharp/Server] reload save: {tally.Entities} entit(ies) of " +
                $"{tally.Classes} class(es), {tally.Fields} field(s), {tally.Bytes} byte(s); " +
                $"{tally.Parts} subsystem record(s), {tally.PartFields} field(s), " +
                $"{tally.PartBytes} byte(s); {tally.IoEvents} pending output(s), " +
                $"{tally.IoConnections} connection(s), {tally.ShellLess} shell-less\n");

        return rows;
    }

    /// <summary>
    /// Hot reload, the way back in: the managed half of every entity that had
    /// one, rebuilt and refilled.
    /// </summary>
    public int ReloadRestore()
    {
        // The liveness records before anything else, as the client restores
        // its mirror: a record a restore writes lands on top of the carried one.
        ReloadStore reload = _engine.Reload;
        byte[]? liveness = reload.Take(LivenessMirror.ReloadSection);

        if (liveness is not null && _liveness.Restore(liveness) < 0)
            Log.Warning("[SourceSharp/Server] the carried liveness mirror was refused; every " +
                        "handle from before the reload crosses to the slot until it dies\n");

        // THE ENTITIES FIRST, AND THE ORDER IS THE POINT. A participant's record
        // names entities by handle, and a handle is only worth re-checking once
        // the managed instance behind it is standing again -- ISaveReader.
        // ReadHandle asks the engine, and an entity whose row has not been
        // restored yet answers the wrong thing about itself.
        int restored = _entities.RestoreAfterReload(reload);

        var parts = ReloadCarrier.TakeParts(reload);
        int reached = _lifecycle?.RestoreState(parts) ?? 0;

        reload.NoteRestored(SavedEntityCodec.Measure(
            _entities.LastRestored, parts, io: null, shellLess: 0));

        var tally = reload.Restored;

        Log.Msg($"[SourceSharp/Server] reload restore: {tally.Entities} entit(ies), " +
                $"{tally.Fields} field(s), {tally.Bytes} byte(s); {tally.Parts} subsystem " +
                $"record(s) into {reached} participant(s), {tally.PartFields} field(s)\n");

        return restored;
    }

    /// <summary>
    /// Save, the way out: every live entity's state through
    /// <see cref="SavedEntityCodec"/> into the engine's buffer.
    ///
    /// <para>
    /// SIZE-THEN-FILL, which is the codec's contract and not an extra one:
    /// <c>Write</c> answers the bytes the whole block needs and writes nothing
    /// unless every one of them fits, so a short buffer comes back as the
    /// required size rather than as a truncated save. An empty world answers 0
    /// rather than a 12-byte empty block, because 0 is what
    /// <see cref="ISideModule.SaveCollect"/> says "nothing to save" is.
    /// </para>
    /// </summary>
    public int SaveCollect(Span<byte> destination)
    {
        // A module whose Initialize has not run has no registry yet (it is
        // built over the side's routes there, plan_abi_hotreload D7), and so
        // nothing to save.
        if (_entities is null)
            return 0;

        var records = _entities.Collect();

        return records.Count == 0 ? 0 : SavedEntityCodec.Write(records, destination, Log);
    }

    /// <summary>
    /// Save, the way back in.
    ///
    /// <para>
    /// A REFUSED BLOCK RESTORES NOTHING — not the records that parsed before
    /// the bad one, not any of it. The registry is never reached on a refusal,
    /// which is the only arrangement that makes that true rather than intended:
    /// half a restored world is worse than none, because the player keeps
    /// playing in it.
    /// </para>
    ///
    /// <para>
    /// AN EMPTY BLOCK IS NOT A REFUSAL. It is what <see cref="SaveCollect"/>
    /// writes for a world with no managed entities, and a load that failed on
    /// one would fail every save taken before the first scripted entity spawned.
    /// </para>
    /// </summary>
    public int SaveRestore(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return 0;

        if (!SavedEntityCodec.TryRead(source, out var entities, out string? refusal))
        {
            Log.Warning($"[SourceSharp/Server] the save's entity block was refused " +
                        $"({refusal}); NOTHING has been restored\n");
            return -1;
        }

        // RestoreFromSave, NOT RestoreFrom, and the difference is the whole
        // correctness of this path. RestoreFrom re-identifies by handle, which
        // is right for a reload and worthless here: a load rebuilds the world
        // and every serial advances, so not one handle out of a .sav names
        // anything (measured: 0 of 470 on dm_lockdown).
        return _entities.RestoreFromSave(entities);
    }

    /// <inheritdoc/>
    public nint BuildManagedRoot(ManagedVtables vtables)
    {
        _vtables = vtables;
        _managedRoot = new ManagedRoot(_bindings
            ?? throw new InvalidOperationException("the managed root was asked for before Initialize"), vtables).Bind();

        BuildSounds(vtables);

        // The objects vphysics is handed as its callbacks are libss_fwd_server's
        // (plan_forwarders_everywhere.md phase 5), each built in its block.
        PhysicsCallbackObjects? callbackObjects = PhysicsCallbackObjects.Acquire(_engine, out string callbacksWhy);
        if (callbackObjects is null)
            Log.Warning($"[SourceSharp/Server] no vphysics callbacks: {callbacksWhy}\n");

        var objects = new VPhysicsObjects(_engine.VPhysics);
        _physicsObjectEvents = PhysicsObjectEventBinding.Create(vtables,
            new PhysicsObjectEventBinding(_engine.Crossings, objects.GameData, ev => _physicsRouter?.Post(ev)));
        // IPhysicsCollisionEvent (B2.2, 2.1, 2.3): every slot managed.
        {
            var entityPhysics = _services.Require<IEngineEntityPhysics>();
            _physicsFluidEvents = new PhysicsFluidEvents(objects, new VPhysicsFluids(_engine.VPhysics), new VPhysicsEnvironment(_engine.VPhysics),
                _services.Require<IEngineEntityPhysics>(), _services.Require<ITempEnts>(),
                _services.Require<IEngineWorldQuery>(), _services.Require<IEngineHost>(),
                _services.Require<IGameClock>(), ev => _physicsRouter?.Post(ev));
            _physicsCollisionEvents = PhysicsCollisionEventBinding.Create(vtables,
                new PhysicsCollisionEventBinding(_engine.Crossings, objects,
                                                 _services.Require<SolverHeldOverlaps>(), ev => _physicsRouter?.Post(ev),
                                                 _physicsFluidEvents,
                                                 new PhysicsCollisionImpacts(objects, _services.Require<IEngineEntityPhysics>(),
                                                                             _services.Require<IEngineNetworking>(),
                                                                             _physicsEvents
                                                                                 ?? throw new InvalidOperationException("the impact slots were built before the collision response"),
                                                                             ev => _physicsRouter?.Post(ev)),
                                                 _services.Require<PhysicsWorldEntity>()));
        }

        // IPhysicsCollisionSolver (2.1): every slot answered by managed.
        if (_physicsSolver is not null)
        {
            _physicsCollisionSolver = PhysicsCollisionSolverBinding.Create(vtables,
                new PhysicsCollisionSolverBinding(_engine.Crossings, _physicsSolver));
        }

        // IPhysicsConstraintEvent (2.2) and IMotionEvent (2.4): the constraint
        // table hears a break, the motion table answers every controller.
        if (_physicsConstraints is not null)
        {
            var constraints = _physicsConstraints;
            _physicsConstraintEvents = PhysicsConstraintEventBinding.Create(vtables,
                new PhysicsConstraintEventBinding(_engine.Crossings, constraints.Broken));
        }

        if (_physicsMotion is not null)
        {
            _physicsMotionEvents = MotionEventBinding.Create(vtables, new MotionEventBinding(_engine.Crossings, _physicsMotion));
            _physicsMotion.Handler = callbackObjects?.MotionEvent(_physicsMotionEvents) ?? 0;
        }

        // physics_shared.cpp's server half (2.6): the solid key table, and the
        // world build over it. "DEFAULT" is g_PhysDefaultObjectParams' name;
        // vphysics never keeps it (a solid's name is the caller's), and it is
        // allocated once per load and never freed, like the build's names.
        {
            var solidKeys = new SolidKeyHandlerBinding(_engine.Crossings,
                                                       System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("DEFAULT"));
            _solidKeys = callbackObjects is null ? null : SolidKeyHandlerBinding.Create(vtables, solidKeys, callbackObjects);
            var entityPhysics = _services.Require<IEngineEntityPhysics>();
            IStaticPropPhysics? staticProps = StaticPropMgrServer.Acquire(_engine, out string worldWhy);
            if (_solidKeys is null)
                worldWhy = $"no solid key handler: {callbacksWhy}";
            _physicsWorldVPhysics = staticProps is null || _solidKeys is null
                ? null
                : PhysicsWorldVPhysics.Acquire(_engine, entityPhysics.EnvironmentPointer, staticProps,
                                               solidKeys, out worldWhy);
            if (_physicsWorldVPhysics is null)
            {
                Log.Warning($"[SourceSharp/Server] no world physics build: {worldWhy}\n");
            }
            else
            {
                IVtableSoundEmitter? emitter = SoundEmitterBase.Acquire(_engine, out string emitterWhy);
                if (emitter is null)
                    Log.Warning($"[SourceSharp/Server] the surface sounds' handles stay invalid: {emitterWhy}\n");

                _physicsWorld = new ServerPhysicsWorld(_physicsWorldVPhysics, _physicsWorldVPhysics,
                    ServerPhysicsWorld.ServerPrecache(_services.Require<IEnginePrecache>().PrecacheSound,
                                                      emitter is null ? null : emitter.SoundIndex),
                    solidKeys.Solids.DefaultName);

                // An entity's physics object (InitPhysics to managed): made
                // here over the same model reads, held by the shell.
                _entityPhysicsCalls = new VPhysicsEntityCalls(_engine.VPhysics);
                var solidityFirst = _convars.Get<SolidityBeforePhysicsConVar>();
                _entityPhysics = new EntityPhysicsCreation(_physicsWorldVPhysics, _entityPhysicsCalls,
                    new EngineEntityPhysicsHost(_services.Require<IEngineEntities>(), entityPhysics,
                                                _services.Require<IEngineEntityRender>(),
                                                EngineEntityPhysicsNative.ForEngine(_engine), _entities,
                                                () => solidityFirst?.Value ?? true, text => Log.Warning(text)));
                _services.AddInstance(_entityPhysics);
                // phys_convert's conversion (stock's physobj.cpp), over the same
                // calls and the level's parenting registry.
                _services.AddInstance<IPhysicsConversion>(new PhysicsConversion(
                    new EnginePhysicsConversionWorld(_services.Require<IEngineEntities>(),
                                                     _services.Require<IEngineEntityRender>(), entityPhysics,
                                                     _services.Require<IEngineNetworking>(),
                                                     _services.Require<IEnginePrecache>(),
                                                     EngineEntityPhysicsNative.ForEngine(_engine),
                                                     () => _standalone?.Parenting.Model, text => Log.Msg(text)),
                    _entityPhysicsCalls, _entityPhysics));
                EngineEntityPhysicsNative.Route(_engine, _entityPhysics);
                _entities.SpawnPhysics = _entityPhysics;
            }
        }

        if (_physicsCallbacks is not null && callbackObjects is not null)
        {
            _physicsCallbacks.ObjectEvents = callbackObjects.ObjectEvent(_physicsObjectEvents);
            _physicsCallbacks.CollisionEvents = _physicsCollisionEvents is { } collisionEvents ? callbackObjects.CollisionEvent(collisionEvents) : 0;
            _physicsCallbacks.CollisionSolver = _physicsCollisionSolver is { } collisionSolver ? callbackObjects.CollisionSolver(collisionSolver) : 0;
            _physicsCallbacks.ConstraintEvents = _physicsConstraintEvents is { } constraintEvents ? callbackObjects.ConstraintEvent(constraintEvents) : 0;
        }

        return _managedRoot.Address;
    }

    /// <summary>
    /// The managed sound emitter (plan_server_native_v2 T4a): the script
    /// table, the server's engine sound and engine server through their
    /// forwarders, and the native IRecipientFilter libss_fwd_server builds in
    /// a managed block, which every emit points at its recipients. The side's
    /// sound members are routed to it. Without any of the four the server
    /// makes no sound, and says why.
    /// </summary>
    private void BuildSounds(ManagedVtables vtables)
    {
        IVtableSoundEmitter? scripts = SoundEmitterBase.Acquire(_engine, out string scriptsWhy);
        IVtableEngineSoundServer? sound = EngineSoundServer.Acquire(_engine, out string soundWhy);
        BuildSoundSystem(scripts, scriptsWhy, sound, soundWhy);
        IVtableEngineServer? server = EngineServer.Acquire(_engine, out string serverWhy);
        RecipientFilterObject? filters = RecipientFilterObject.Acquire(_engine, out string filtersWhy);
        if (scripts is null || sound is null || server is null || filters is null)
        {
            Log.Warning("[SourceSharp/Server] no sound emitter: "
                        + (scripts is null ? scriptsWhy : sound is null ? soundWhy : server is null ? serverWhy : filtersWhy)
                        + "\n");
            return;
        }

        var sink = new RecipientFilterSink();
        _recipientFilter = RecipientFilterSink.Create(vtables, sink);
        nint nativeFilter = filters.Build(_recipientFilter);
        var engine = new SoundEmitterEngine(scripts, sound, server, sink, nativeFilter);

        var players = _services.Require<IEnginePlayers>();
        var entities = _services.Require<IEngineEntities>();
        var render = _services.Require<IEngineEntityRender>();
        var recipients = new SoundRecipients(engine, new ServerSoundListeners(_services.Require<IEngineEnvironment>(),
                                                                                players, server));
        var trace = _convars.Get<SvSoundEmitterTraceConVar>();

        // The speaker: a player by its slot, anything else by the entry its
        // index names; null where nothing is bound, which has no gender.
        string? SpeakerModel(int index)
        {
            EntityHandle speaker = index >= 1 && index <= EngineLimits.MaxPlayerSlots ? players.GetHandle(index) : EntityHandle.None;
            if (speaker == EntityHandle.None)
                speaker = _entities.Directory.HandleOf(index);
            return speaker == EntityHandle.None ? null : render.GetModelName(speaker);
        }

        var emitter = new SoundEmitter(engine, recipients, _services.Require<ICloseCaptions>(),
                                       _services.Require<IGameClock>(), SpeakerModel, () => trace?.Value == true,
                                       text => Log.Msg(text), DeveloperMessage);
        _serverSounds = new ServerSounds(emitter, entities, handle => _soundController.Create(handle),
                                         text => Log.Warning(text), _soundSystem);
        ServerSoundRouting.Route(_engine, _serverSounds);

        // The temp entities' send (plan_server_native_v2 T4b): the same managed
        // recipient filter, the same recipient rules, PlaybackTempEntity
        // through the engine forwarder.
        _tempEntEngine?.Attach(new TempEntSender(new TempEntSendEngine(server, sink, nativeFilter)),
                               new TempEntRecipients(recipients));
    }

    /// <summary>
    /// The sound emitter SYSTEM (plan_server_native_v2 T4a, the precache path):
    /// the script table, the server's engine sound, the filesystem's two
    /// halves and the level's precache window. Its stages run where the
    /// CSoundEmitterSystem row's did: Init first in the SourceSharpManaged
    /// row's (IManagedSoundEmitter), the level start first in
    /// ServerGameDll.MapLoadPreEntity, the level's end last in LevelShutdown,
    /// its Shutdown last in the final unload's Shutdown.
    /// </summary>
    private void BuildSoundSystem(IVtableSoundEmitter? scripts, string scriptsWhy,
                                  IVtableEngineSoundServer? sound, string soundWhy)
    {
        IVtableBaseFileSystem? files = EngineBaseFileSystem.Acquire(_engine, out string filesWhy);
        IVtableFileSystem? dirs = EngineFileSystemServer.Acquire(_engine, out string dirsWhy);
        if (scripts is null || sound is null || files is null || dirs is null)
        {
            Log.Warning("[SourceSharp/Server] no sound emitter system, so no sound is precached: "
                        + (scripts is null ? scriptsWhy : sound is null ? soundWhy : files is null ? filesWhy : dirsWhy)
                        + "\n");
            return;
        }

        _soundSystem = new SoundEmitterSystem(
            new SoundPrecacheEngine(scripts, sound, files, dirs, _services.Require<IEngineLevel>()),
            LocalCommandLine.Has(_services.Require<ILocalCommandLine>().Args, "-makereslists"),
            () => _gameDll.CurrentMap, text => Log.Warning(text), DeveloperMessage);
        _gameDll.SoundSystem = _soundSystem;
    }

    /// <summary>The system's two native edges, to whichever system the module has built (none before the root).</summary>
    private sealed class SoundSystemOf(Func<SoundEmitterSystem?> system, ILog log) : ISoundSystemSink
    {
        public bool SystemInit()
        {
            if (system() is { } built)
                return built.Init();

            log.Warning("[SourceSharp/Server] the sound scripts cannot load: no sound emitter system\n");
            return false;
        }

        public void PrecacheScriptSound(string soundName) => system()?.PrecacheScriptSound(soundName);
    }

    /// <summary>The move helper's sounds, to whichever emitter the module has built (none before the root).</summary>
    private sealed class MoveSoundsOf(Func<IMoveSoundSink?> sink) : IMoveSoundSink
    {
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch)
            => sink()?.MoveSound(entIndex, origin, channel, sample, volume, soundLevel, flags, pitch);

        public void MoveSoundScript(int entIndex, Vector origin, string soundName)
            => sink()?.MoveSoundScript(entIndex, origin, soundName);
    }

    /// <summary>
    /// Stock's DevMsg( 1, ... ) / DevWarning for managed's physics solver: a
    /// line under <c>developer 1</c> and nothing otherwise. Read per print:
    /// the solver prints only for a large friction system or a blown collision
    /// budget, which are rare by construction.
    /// </summary>
    private void DeveloperMessage(string text)
    {
        if (int.TryParse(NativeConsole.ValueOf(_services.Require<IEngineConsole>(), "developer"), out int level)
            && level >= 1)
            Log.Msg(text);
    }

    public void Shutdown()
    {
        // First: a warm-up thread still inside this context's code would keep
        // the collectible context from unloading (plan_map_load P5).
        _jitWarmup?.Dispose();
        _jitWarmup = null;

        // The FINAL unload, rather than a reload: native releases the root
        // before it unloads the side for good, and keeps it across a reload.
        bool finalUnload = _vtables is null || !_vtables.Holds("IManagedRoot");

        Log.Msg("[SourceSharp/Server] managed server module shutting down\n");
        // This load's last engine lines go to this load's listeners; what is
        // printed after this reaches the next load from the same buffer.
        _consoleLines?.Drain();

        // vphysics' handlers never release their tables, so managed frees
        // them at the final unload (native has released the root by then);
        // a reload keeps both with the root.
        // The sound route goes first: nothing may emit through a filter whose
        // block is about to be freed.
        ServerSoundRouting.Route(_engine, null);
        _serverSounds = null;
        _vtables?.ReleaseCallerHeld(_managedRoot, _physicsObjectEvents, _physicsCollisionEvents, _physicsCollisionSolver,
                                   _physicsConstraintEvents, _physicsMotionEvents, _solidKeys, _recipientFilter);
        _recipientFilter = null;
        _solidKeys = null;
        _physicsWorld = null;
        if (_entityPhysics is not null)
        {
            EngineEntityPhysicsNative.Route(_engine, null);
            _entities.SpawnPhysics = null;
            _entityPhysics = null;
        }
        _entityPhysicsCalls?.Dispose();
        _entityPhysicsCalls = null;
        _physicsWorldVPhysics?.Dispose();
        _physicsWorldVPhysics = null;
        _physicsConstraintEvents = null;
        _physicsMotionEvents = null;
        _physicsObjectEvents = null;
        _physicsCollisionEvents = null;
        _physicsCollisionSolver = null;

        // The managed-built blocks are the side registry's, not this module's
        // (plan_managed_vtable_reload.md L4): native keeps them across a
        // reload, and the Bootstrap detaches or frees them after this returns.
        _managedRoot = null;

        // The participants first, while everything they might touch is still
        // up. _lifecycle is null if init never finished, which is exactly when
        // a shutdown is most likely to be the thing running.
        if (_clients is not null)
            _clients.LevelShuttingDown = true;

        _lifecycle?.Shutdown();

        // The mod's services, after the lifecycle's own shutdown pass so a
        // participant has had its last call, and before the context unloads.
        SideServices.DisposeBuilt(_services, _modServices, Log);

        // Closes the socket and detaches the log sink. Left running, the
        // listener would keep the port and the next launch could not bind it.
        _debugApi?.Dispose();
        _debugApi = null;

        // THE REGISTRY OUT OF THE SIDE'S ROUTES (plan_abi_hotreload D7). The
        // routes are the EngineSide's, and Engine holds both sides statically
        // until D8, so a registry left bound would keep this load context alive.
        // Only routes still bound to THIS registry are dropped. Null when init
        // never reached it.
        _entities?.Detach();

        // Last, so everything above still printed through the module's own
        // sink; from here the bootstrap's console has the side again.
        _descriptorAttachment?.Dispose();
        _descriptorAttachment = null;

        _livenessAttachment?.Dispose();
        _livenessAttachment = null;

        _logAttachment?.Dispose();
        _logAttachment = null;

        // LAST: the sound emitter system's Shutdown (its log closed, the
        // script table's ModShutdown), the CSoundEmitterSystem row's, which
        // the backward walk ran after SourceSharpManaged's; DLLShutdown unloads
        // the managed side before that walk, so this is the same place. Only
        // at the final unload: a reload keeps the table the native Init loaded,
        // and only closes this load's log.
        if (finalUnload)
            _soundSystem?.Shutdown();
        else
            _soundSystem?.CloseLog();
        _soundSystem = null;
    }

    //-------------------------------------------------------------------------
    // IServerModule — decisions server_base.vpc would normally force a mod to
    // answer in C++. They are answered here instead; native keeps only a shell.
    //-------------------------------------------------------------------------

    /// <summary>Shown in the server browser. Previously hardcoded in C++.</summary>
    /// <summary>
    /// The name in the server browser, and there is exactly ONE managed answer.
    ///
    /// <para>
    /// This method is the existing native route —
    /// <c>SourceSharp_GetGameDescription()</c> reaches it through
    /// <c>ServerGetGameDescription</c> — and it used to return a literal. The
    /// shell's own <c>GetGameDescription</c> needs the same answer, so this
    /// forwards to <see cref="IServerListing.GameDescription"/> rather than a
    /// second route being invented for the shell. Two routes would agree until
    /// the day somebody changed one of them.
    /// </para>
    /// </summary>
    public string GetGameDescription() => _gameDll.GameDescription;

    /// <inheritdoc/>
    public IServerLifetime Lifetime => _gameDll;

    /// <inheritdoc/>
    public IServerLevel Level => _gameDll;

    /// <inheritdoc/>
    public IServerFrame Frames => _gameDll;

    /// <inheritdoc />
    public void DrainConsole() => _consoleLines?.Drain();

    /// <inheritdoc/>
    public IServerListing Listing => _gameDll;

    /// <inheritdoc/>
    public IServerHosting Hosting => _gameDll;

    /// <inheritdoc/>
    public IServerDecals Decals => _decals;

    /// <inheritdoc/>
    public IServerPropData PropData => _propData;

    /// <inheritdoc/>
    public IServerSoundEnt SoundEnt => _soundEnt;

    /// <inheritdoc/>
    public IServerSoundController SoundController => _soundController;

    /// <summary>The viewer's world fog, for the report command and for tests.</summary>
    public WorldFog WorldFog => _worldFog;

    /// <summary>The viewer's 3D skybox, for the report command and for tests.</summary>
    public WorldSkybox WorldSkybox => _worldSkybox;

    /// <summary>Each player's client-data update, for the test that pins its registration.</summary>
    public PlayerClientData PlayerClientData => _playerClientData;

    /// <inheritdoc/>
    public IServerEntities Entities => _serverEntities;

    /// <inheritdoc/>
    public IServerClients Clients => _serverClients;

    /// <inheritdoc/>
    public IServerTemplates Templates => _templates;

    /// <inheritdoc/>
    public IServerGlobalState GlobalState => _globalState;

    public void OnClientPutInServer(int entIndex, string playerName)
    {
        _services.Resolve<PlayerNames>()?.PutInServer(entIndex, playerName);
        // Reads two C#-declared ConVars — proof they hold live engine values.
        if (_verboseJoinsVar?.Value != false)
            Log.Msg($"[SourceSharp/Server] {_greeting?.Value ?? "hello"}, {playerName} (ent {entIndex})\n");
    }

    public bool OnClientNetMessage(int entIndex, string key, ReadOnlySpan<byte> payload)
        => _clients?.Message(entIndex, key, payload) ?? false;

    public bool OnClientSettingsChanged(int entIndex)
        => _services.Resolve<PlayerNames>()?.Changed(entIndex) ?? false;

    public void OnClientActive(int entIndex, bool loadGame)
    {
        Log.Msg($"[SourceSharp/Server] client {entIndex} active (loadGame={loadGame})\n");
        _clients?.Active(entIndex);

        // THE SECOND HALF OF A SAVEGAME RESTORE, and this is the only moment
        // it can run.
        //
        // Restore ran inside engine->LoadGameState, inside LevelInit -- before
        // ServerActivate and before any client existed. Two groups of record
        // could not match anything at that instant and were held rather than
        // dropped: the entities the managed level init creates in
        // ServerActivate (10 ss_pickup, 9 ss_beam on dm_lockdown), and the
        // PLAYER with its viewmodel and weapons, which cannot exist before a
        // client is active because the edict they bind to is a client's.
        //
        // Both exist by now: ServerActivate has run, and the native shell has
        // created the standalone player for this edict (CServerGameClients::
        // ClientActive -- the engine skips ClientPutInServer on a load and
        // calls ClientActive with bLoadGame true).
        //
        // BEFORE THE LAYOUT CHECK AND THE INVITATION, so that the first
        // snapshot this client is sent carries the restored state rather than
        // the freshly spawned one.
        if (loadGame)
            _entities.ApplyDeferredRestore();

        // Hand the joining client BOTH fingerprints: the entity layouts, and
        // the engine block (plan_entity.md §3.2). Both sides generate their
        // datatables from the same metadata with nothing enforcing it, and both
        // sides declare the engine block twice — once in C# and once in C++ —
        // with nothing enforcing that either. This is the check that turns
        // either silent divergence into a message.
        SendLayoutCheck(entIndex);

        InviteToDebugChannel(entIndex);

        // Stock's FinishClientPutInServer: the message of the day.
        _services.Require<MotdPanel>().OnClientActive(entIndex);
    }

    /// <summary>
    /// Hands one joining client the two fingerprints, as a user message this
    /// side writes.
    ///
    /// <para>
    /// <b>THE PAYLOAD IS WRITTEN HERE AND WAS WRITTEN IN C++</b>
    /// (<c>plan_shim.md</c> §5 3e). <c>shell_usermessages.cpp</c> carried a
    /// hand-written <c>SourceSharp_SendLayoutCheck</c> that did three
    /// <c>WRITE_LONG</c>s, and the comment over it said exactly what was wrong
    /// with that: "the one place client and server hand-agree on a byte
    /// layout". They do not hand-agree now — the three ints go through
    /// <see cref="UserMessageWriter"/> and come back through
    /// <see cref="UserMessageReader"/>, which is one encoding agreed in one
    /// place, and the same round trip every other managed message takes.
    /// </para>
    ///
    /// <para>
    /// <b>The order is the wire protocol.</b>
    /// <c>LayoutCheckMessage.OnReceived</c> reads fingerprint, count, block
    /// fingerprint, and <c>LayoutHandshakeTests</c> holds the two orders
    /// against each other — so reversing either file fails rather than
    /// producing a handshake that parses and means something else.
    /// </para>
    ///
    /// <para>
    /// <b>Reliable, and aimed at one player.</b> A handshake that the network
    /// may drop is a client that silently never checks its layouts, which is
    /// the whole failure this message exists to make loud.
    /// </para>
    /// </summary>
    private void SendLayoutCheck(int entIndex)
    {
        _protocol ??= ProtocolManifest.Of(Game, Log);

        // 24 bytes, LayoutCheckMessage's declared Size: the engine drops a
        // fixed-size message of any other length, silently, and the generator
        // refuses a payload that disagrees with it (SSMSG009).
        _send.Send(new LayoutCheck
        {
            Fingerprint      = _layouts.Fingerprint,
            LayoutCount      = _layouts.Count,
            BlockFingerprint = _layouts.BlockFingerprint,
            StringTablesHash = _protocol.StringTables,
            TempEntsHash     = _protocol.TempEnts,
            NetStatesHash    = _protocol.NetStates,
        }, entIndex, reliable: true);

        Log.Msg($"[SourceSharp/Server] sent layout check to player {entIndex}: " +
                $"{_layouts.Count} layout(s), fingerprint 0x{_layouts.Fingerprint:X8}, " +
                $"engine block 0x{_layouts.BlockFingerprint:X8}\n");
    }

    /// <summary>
    /// What this composition serves its dev API (plan_devapi_interfaces §5.3):
    /// its own instances, each reading this module's live state through a
    /// closure rather than a snapshot. Built before the services and entity
    /// classes below it register, and a snapshot taken then would be empty.
    /// </summary>
    private DebugServerHooks DebugHooks() => new(
        new DebugLog(_services.Require<ILog>(), _services.Require<ConsoleLines>()),
        new DebugBuild(_engine.Build, _engine.Crossings),
        new DebugEntityEvents(_services.Require<IEngineEntities>(), _entities),

        // Listings, so the web UI can show what exists rather than only what
        // it was told to ask about; a convar reads back from the managed
        // registry first, then the engine's.
        DebugConsoleHook.Over(_services.Require<IEngineConsole>(), () => _convars.All, () => _commands.All,
                        name => _convars.ValueOf(name)),
        new DebugEntityCatalog(() => _entities.DescribeClasses(), () => _entities.DescribeInstances()),
        new DebugStatus(_engine.Reload, StatusCounters, StatusGame, () => _services.Describe(),
                      // Read off the API's own thread, which is safe because level
                      // load replaces this string rather than editing one in place.
                      () => _currentMap),

        // The traffic view. Reported from whatever the profiler has
        // accumulated; the route says so when nothing is being sampled,
        // rather than answering an empty list that reads like "no traffic".
        new DebugNetTrafficHook(
            () => new NetTrafficReport(_netTraffic.IsSampling, _netTraffic.Elapsed, _netTraffic.Tracked,
                                       _netTraffic.Report()),
            () => _netTraffic.Reset()),

        // The engine areas the dev API drives, out of this composition's own
        // container (plan_abi_hotreload D8): two compositions, two worlds.
        // With the touch links of the entity directory -- what a touch EDGE was
        // computed from, so a test can name the pair rather than infer it from
        // a total (physics parity E1).
        // And three facts only managed code holds: the last wake or sleep the
        // solver reported for an entity's object, what a player stands on,
        // and the server tick the dev API's waits count in.
        DebugWorld.From(_services, () => _entities.Directory.TouchingPairs(),
                        asleep: h => _physicsRouter?.IsAsleep(h),
                        groundIndex: GroundIndexOf,
                        tick: () => _ticks.Count),

        // The networked schema, and one entity's bytes read back through it.
        //
        // A closure and not a snapshot: the states are discovered from the
        // shared assembly and whether one is PUBLISHED depends on the entity
        // registry, which does not exist yet when this is built. NetSchemaTypes
        // caches on first ask, because the answer is fixed at DLL init.
        //
        // The two shells answer "was it published" differently, and since
        // Step 7 neither answer is "we described it" -- there is no
        // description. A hierarchy class is on the wire when an entity class
        // here claims its name, which is exactly the rule PublishAll applies.
        // The generated shell is on the wire when native put it on a TIER, and
        // -1 is what a refusal and an absent engine both answer. Tier ZERO is a
        // real tier, so the test is >= 0 and not > 0.
        new DebugNetSchema(NetSchemaTypes, () => _services.Require<INetTiers>(),
                         () => _services.Require<INetTypeRefusals>(), index => _payloadReader.Read(index)),

        // Counter groups as data: what cs_sv_physics_events prints, read from
        // the same objects, so a test asserts on numbers rather than on text.
        // Closures: the physics objects are built after this.
        new DebugCounterGroups(
        [
            ("physics", "cs_sv_physics_events",
             () => _physicsEvents is { } response && _physicsRouter is { } router
                 ? DebugCounterGroups.Physics(response, router) : null),
            // Managed's IPhysicsCollisionSolver (2.1).
            ("solver", "cs_sv_physics_solver",
             () => _physicsSolver is { } physicsSolver ? DebugCounterGroups.Solver(physicsSolver) : null),
            // The shadow bodies' counters, and native's two (live, outlived).
            ("playershadow", "cs_sv_playershadow",
             () => DebugCounterGroups.PlayerShadow(_services.Require<PlayerShadowBodies>(),
                                                   _services.Require<IEngineDiagnostics>())),
            // The think heap by class: what /status' think.pending is made of.
            ("think", "(no readout; /status think.pending is its total)",
             () => DebugCounterGroups.Thinks(_entities.Directory.Think.Bookings(),
                                             h => _services.Require<IEngineEntities>().GetClassName(h),
                                             _services.Require<IGameClock>().Now)),
        ]),

        // Who asks the process to end: native records, the dev API decides.
        new DebugProcess(_services.Require<IEngineDiagnostics>()),

        // The managed half of /command?capture=1: this module's own command
        // registry (the one RegisterAll filled) and container, so a capture
        // runs a fresh instance of the real class with its real dependencies
        // and only its print channels watched (user ruling 2026-10-07).
        new DebugCommandRun(_commands, () => _services, _services.Require<ILog>()));

    /// <summary>The counters /status reports (group G7); read off the game thread.</summary>
    private DebugCounters StatusCounters()
    {
        var directory = _entities.Directory;
        var overlay = _services.Require<IEngineHud>().OverlayReadout();

        return new DebugCounters(
            // What the think heap is carrying -- plan_entity.md step 1g. Read
            // beside crossings.entityOps, which is where the saving shows: the
            // ranLastFrame here is the N the old build paid N crossings for.
            new DebugThink(directory.Think.Pending, directory.Think.HeapDepth,
                         directory.ThinksRanLastFrame, directory.ThinksRunTotal),

            // And what the I/O queue is carrying -- plan_entity.md step 2a, on
            // the same terms: crossings.entityOps.Input reads zero both for a
            // build that delivers its I/O without crossing and for one that
            // stopped delivering, and deliveredTotal is what tells them apart.
            new DebugIo(directory.Io.Count, directory.ConnectionCount, directory.IoDeliveredLastFrame,
                      directory.Io.DeliveredTotal, directory.IoUnhandled, directory.Io.CyclesRefused,
                      directory.IoUnhandledDistinct, directory.Io.PostedAfterThePass,
                      directory.IoSdkAsks, directory.IoSdkTook),

            // And the touch link table -- plan_entity.md step 2b, on the same
            // terms again: crossings.entityOps.StartTouch/Touch/EndTouch read
            // zero both for a build that diffs its touches without crossing and
            // for one in which nothing touches anything, and edgesTotal beside
            // the frame count is what tells them apart. `refused` should be
            // zero; a non-zero one is a report that would not fit twice running
            // and a frame of stale links.
            new DebugTouch(_standalone.Overlaps.LastPairCount, directory.TouchLinkCount,
                         directory.TouchEdgesLastFrame, directory.TouchEdgesTotal,
                         _standalone.Overlaps.Capacity, _standalone.Overlaps.Regrows, _standalone.Overlaps.Refused),

            // And what the map spawner did on the last load -- plan_entity.md
            // step 2d, on the same terms again: crossings.entityOps.KeyValue
            // reads zero both for a build that applies its keys without crossing
            // and for one that stopped applying them, and managedKeys is the N
            // the old build paid N inbound crossings for.
            new DebugMapSpawn(_mapSpawner.LastBlocks, _mapSpawner.LastManagedBlocks, _mapSpawner.LastNativeBlocks,
                            _mapSpawner.LastManagedKeys, _mapSpawner.LastCreateCrossings,
                            _mapSpawner.LastSpawnCrossings),

            // AND WHAT THE NATIVE FACTORY DICTIONARY SAYS, as the managed side
            // sees it -- the `sub-factory` lane's table. `dumpentityfactories`
            // counts the same dictionary on the far side of the wire, so the two
            // are an AGREEMENT check rather than one number reported twice.
            //
            // `available` is reported and not inferred: false means the slots
            // are unbound and the dictionary has not been asked, which OwnerOf
            // reads as "assume native". An empty table would mean it answered
            // nothing, and that refuses classnames that work.
            new DebugFactories(_factories.Available, _factories.NativeNames.Count, _factories.Refusals.Count,
                             _factories.Refusals.Sum(r => r.Times), _mapSpawner.LastUnansweredClasses.Count),

            // AND THE SHELL-LESS ENTITIES -- plan_entity.md step 2e. The step's
            // claim is that no handle from the engine-invisible range reaches
            // native code, which is a zero; `live` beside it is what stops that
            // zero being trivially true of a build that made none. nativeReach
            // is counted in NATIVE code (SourceSharp_ResolveEntity) because a
            // managed counter could only count the routes managed code already
            // knows it takes.
            new DebugLogical(_entities.ShellLessClassCount, _entities.LiveCount,
                           _entities.AllocationTotals.Allocated, _entities.AllocationTotals.Released,
                           directory.ShellLessExhausted, _entities.ShellLessNativeReach),

            // AND WHAT THE OVERLAYS DREW -- plan_entity.md step 4c. `drawn` is
            // the work and `noViewer` / `clipped` / `noOverlay` are the three
            // honest reasons it could be zero. Counted in NATIVE code, for step
            // 2e's reason: most draws come from native callers.
            new DebugOverlayCounts(overlay.Drawn, overlay.Clipped, overlay.NoViewer, overlay.NoOverlay, overlay.ViewerSet));
    }

    /// <summary>
    /// The map, the tick count and the physics tables (group G7); game thread
    /// only. The physics tables are counted because they are the one part of
    /// the mod with no entity to look at: a constraint or a spring that
    /// outlives what made it is invisible from every other angle, and a count
    /// that climbs across level changes is what a leak looks like from outside.
    /// </summary>
    /// <summary>
    /// The entity index a player stands on, -1 for nothing, from the ground
    /// word its replicated state carries (GROUNDWIRE_NONE, else the index in
    /// the low eleven bits; game/shared/ground_handle_wire.h). Null for
    /// anything that is not a player: only a player keeps a ground here.
    /// </summary>
    private int? GroundIndexOf(EntityHandle entity)
    {
        if (_entities.Get(entity) is not SsPlayer player)
            return null;

        return player.GroundIndex;
    }

    private DebugGameStatus StatusGame()
        => new(_currentMap, _ticks.Count, _constraints.Count(), _springs.Count(), _motion.Count());

    /// <summary>
    /// Offers a joining client the debug channel, if this server has one.
    ///
    /// <para>
    /// <b>GATE TWO, AND IT IS TWO IFS.</b> <c>_debugApi</c> is null unless this
    /// process was started with <c>-debugapi</c>; <c>InviteDebugChannel</c> is null
    /// unless it was ALSO started with <c>-debuguiserve</c>. A server that did not
    /// opt in never sends this message, so a client's own opt-in has nothing to
    /// accept. The client's gate is separate again and comes first on its side.
    /// </para>
    ///
    /// <para>
    /// <b>WHAT TRAVELS IS A PORT AND A NONCE AND NOT AN ADDRESS.</b> Do not add
    /// a host field here or to <c>DebugChannelInvitation</c>. A server that could
    /// name the address a client dials could point that client's debug socket at
    /// any machine it liked, and the client would have no way to tell that from
    /// a legitimate offer. The client dials the server it already joined; that
    /// is an address it chose, and this end has no business supplying one.
    /// <c>DebugChannelInvitation.TryRead</c> refuses a payload with anything
    /// after the three declared fields, so an added field does not quietly work
    /// — it breaks the whole invitation, on purpose.
    /// </para>
    /// </summary>

    private void InviteToDebugChannel(int entIndex)
    {
        // Minted for this player's slot, so the session it opens answers
        // /client/{slot}/... (plan_side_isolation ruling 12).
        //
        // Opaque bytes (plan_devapi_interfaces §5.5): the dev API writes the
        // invitation, the game only carries it. The dev API logs the port.
        if (_debugApi is null)
            return;

        Span<byte> payload = stackalloc byte[DebugChannelInvite.MaxBytes];
        int written = _debugApi.WriteInvitation(entIndex, payload);

        if (written == 0)
            return;

        if (written < 0)
        {
            Log.Warning("[SourceSharp/Server] the debug channel invitation did not fit; not sent\n");
            return;
        }

        _send.Send(new DebugChannelInvite { Invitation = payload[..written].ToArray() },
                   entIndex, reliable: true);
        _reloadInvites.Invited(entIndex);
    }

    public void OnClientGamePrecache()
        => Log.Msg("[SourceSharp/Server] precache\n");

    public void OnRespawn(int entIndex, bool copyCorpse)
        => Log.Msg($"[SourceSharp/Server] respawn entity {entIndex}\n");

    // OnNetEntitySpawned went with the entity it populated. It wrote one
    // distinctive value per field type so the client could CHECK the round trip
    // — the only managed proof that a generated SendTable and its generated
    // RecvTable paired. What replaced it: cs_sv_payload_probe /
    // cs_cl_payload_probe over every word of a live entity's payload, and
    // ss_barrel's client half checking five decoded fields.

    /// <summary>Routes a fired game event to its declared handlers.</summary>
    public void DispatchGameEvent(string eventName, IntPtr handle)
        => _gameEvents.Dispatch(eventName, handle);

    //-------------------------------------------------------------------------
    // Gameplay policy. Delegated to GameRules so this class stays lifecycle.
    //-------------------------------------------------------------------------

    public Decision CanTakeDamage(EntityHandle player, EntityHandle attacker, float damage, int damageType)
        => _rules?.CanTakeDamage(player, attacker, damage, damageType, _clock.Now)
           ?? Decision.UseStockRule;

    public void OnPlayerSpawned(EntityHandle player)
    {
        _players.OnSpawned(player, _clock.Now);
        _rules?.OnPlayerSpawned(player, _clock.Now);

        // The chapter title, once per player per level, on their first spawn —
        // the multiplayer moment closest to stock's map-start announcement.
        _chapterTitle.OnPlayerSpawned(player, _entityApi.GetIndex(player));
    }

    /// <summary>
    /// How long a thrown grenade takes to go off.
    ///
    /// A game rule rather than a property of the entity: it is the number that
    /// decides whether grenades are a threat or a nuisance, and it belongs with
    /// the rules that decide the rest of the pacing.
    /// </summary>
    private const float GrenadeFuse = 2.5f;

    // Built in Initialize, through the container.
    private Players _players = null!;

    //-------------------------------------------------------------------------
    // Both a map item and a runtime pickup report through here. The map items
    // are asked first because they own their own entity; the runtime pickups
    // only recognise handles they created themselves, so nothing is ambiguous.
    //-------------------------------------------------------------------------
    public void OnEntityTouched(EntityHandle entity, EntityHandle other)
    {
        if (_entities.Touch(entity, other))
            return;

        _pickups.OnTouched(entity, other, _clock.Now);
    }

    /// <summary>
    /// A player's buttons, every tick.
    ///
    /// Nothing acts on them any more: attacking and reloading are the engine's
    /// weapon state machine again, and it reads the buttons itself. The hook
    /// stays because it is this mod's only window into player input, and the
    /// next thing that wants one will want it here.
    /// </summary>
    /// <summary>
    /// A player's buttons, every tick.
    ///
    /// Kept rather than acted on: the weapon reads its own buttons, and the one
    /// thing here that needs them is the death cycle asking whether a dead
    /// player is trying to come back.
    /// </summary>
    public void OnPlayerRunCommand(EntityHandle player, int buttons)
        => _buttons.Record(player, buttons);

    private readonly PlayerButtons _buttons = new();


    //-------------------------------------------------------------------------
    // C#-declared map entities. Straight pass-through to the registry, which
    // owns the handle-to-instance mapping.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // EVERY THING THAT HAPPENS TO AN ENTITY, THROUGH ONE CALL AND ONE LOOKUP
    // (plan_entity.md §3.4, step 1f).
    //
    // The twelve OnScripted* methods below are unchanged and are still what
    // this routes to -- and every one of them still works when called directly,
    // which is what the existing tests do. What is new is that the NATIVE side
    // arrives here instead, with an op number, and the resolution that each of
    // those methods used to do for itself happens once:
    //
    //   Resolve(entity, blob)  one dictionary lookup, wrapped in an EntityRef
    //   switch (op)            one jump
    //   instance.OnWhatever()  one virtual call
    //
    // against the previous shape of one lookup per callback per entity per
    // frame. §3.4's array-indexed directory is step 1g's; the point of
    // resolving HERE is that 1g changes one line and no call site.
    //-------------------------------------------------------------------------
    public int Dispatch(EntityOp op, EntityHandle entity, Span<byte> blob, in EntityOpArgs args)
    {
        // Spawn and KeyValue arrive BEFORE the managed instance exists -- they
        // are what creates it -- so they cannot be answered from a resolved
        // reference and take the handle and the classname instead. This is the
        // whole of why they are in EntityOpMask.Always: an override bit could
        // not turn them off without turning the entity off.
        switch (op)
        {
            case EntityOp.Spawn:
                _entities.Spawn(entity, args.ClassName);
                return 0;

            case EntityOp.KeyValue:
                return _entities.KeyValue(entity, args.ClassName, args.Text1, args.Text2) ? 1 : 0;

            case EntityOp.FilterPasses:
                // The one op whose SUBJECT is not the entity in hand: `entity`
                // is the filter being asked and args.Other is what it is being
                // asked about.
                return OnScriptedFilterPasses(entity, args.Other, args.Flags, args.IsDamageQuestion);
        }

        var it = _entities.Resolve(entity, blob);

        switch (op)
        {
            case EntityOp.Input:
                return _entities.Input(it, args.Text1, args.Text2, args.Number, args.Other) ? 1 : 0;

            case EntityOp.Think:
                _entities.Think(it);
                return 0;

            case EntityOp.Removed:
                _entities.Removed(it);
                return 0;

            // THE THREE TOUCH OPS AND THE IMPACT ARE ONE QUESTION HERE, which
            // is what the managed contract has always had: "somebody is in
            // me". Which of them a class gets is the shell's business -- an
            // item is told every tick, a brush on the edges only -- and the
            // fall-through to the pickup service is the same one
            // OnEntityTouched had: the map items own their own entity and are
            // asked first, and the runtime pickups only recognise handles they
            // created themselves, so nothing is ambiguous.
            case EntityOp.StartTouch:
            case EntityOp.Touch:
            case EntityOp.Impact:
                if (!_entities.Touch(it, args.Other))
                    _pickups.OnTouched(entity, args.Other, _clock.Now);
                return 0;

            case EntityOp.EndTouch:
                _entities.EndTouch(it, args.Other);
                return 0;

            case EntityOp.PhysicsCreated:
                _entities.PhysicsCreated(it);
                return 0;

            case EntityOp.MoveDone:
                _entities.MoveDone(it);
                return 0;

            case EntityOp.Blocked:
                _entities.Blocked(it, args.Other);
                return 0;

            case EntityOp.Damage:
                return _entities.Damaged(it, args.Other, args.Number, args.Flags,
                                         args.Position, args.Force, _hitGroups.Current);

            case EntityOp.Killed:
                _entities.Killed(it, args.Other);
                return 0;

            default:
                // An op this build has never heard of, which is what a native
                // side newer than the managed one looks like. Nothing, rather
                // than a throw: an exception here unwinds into the engine's C++
                // frames, and the honest answer to "I do not know that event"
                // is that nothing happened.
                return 0;
        }
    }

    public void OnScriptedSpawn(EntityHandle entity, string className)
        => _entities.Spawn(entity, className);

    public bool OnScriptedKeyValue(EntityHandle entity, string className, string key, string value)
        => _entities.KeyValue(entity, className, key, value);

    public bool OnScriptedInput(EntityHandle entity, string name, string value, float number, EntityHandle activator)
        => _entities.Input(entity, name, value, number, activator);

    public void OnScriptedThink(EntityHandle entity)
        => _entities.Think(entity);

    public int OnScriptedDamaged(EntityHandle entity, EntityHandle attacker, float damage,
                                 int damageType, Vector position, Vector force)
        => _entities.Damaged(entity, attacker, damage, damageType, position, force, _hitGroups.Current);

    public void OnScriptedKilled(EntityHandle entity, EntityHandle attacker)
        => _entities.Killed(entity, attacker);

    public void OnScriptedRemoved(EntityHandle entity)
        => _entities.Removed(entity);

    public void OnScriptedMoveDone(EntityHandle entity)
        => _entities.MoveDone(entity);

    public void OnScriptedBlocked(EntityHandle entity, EntityHandle other)
        => _entities.Blocked(entity, other);

    public void OnScriptedEndTouch(EntityHandle entity, EntityHandle other)
        => _entities.EndTouch(entity, other);

    private PhysicsCollisionResponse? _physicsEvents;

    /// <summary>The physics frame CPhysicsHook forwards to (plan_physics_bones_native_zero 2.3).</summary>
    private PhysicsFrame? _physicsFrame;

    /// <summary>The same events, to the entities they name (physics stock parity, step 3).</summary>
    private PhysicsEventRouter? _physicsRouter;

    //-------------------------------------------------------------------------
    // Weapons. These three crossings belonged to the C++ weapon shell, which
    // Phase 10 removed - the machine runs per player command now (see
    // OnStandalonePlayerCommand below), not per weapon entity frame. Declining
    // is safe either way: a caller that no longer exists cannot mind.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A filter, asked by an SDK entity that named it. -1 says the handle is not
    /// a filter, which is the answer for every other kind of entity a mapper can
    /// write into <c>filtername</c> — the registry is what knows, so nothing
    /// native has to keep a list of which classnames are filters.
    /// </summary>
    public int OnScriptedFilterPasses(EntityHandle filter, EntityHandle entity, int damageType, bool damage)
    {
        if (_entities.Get(filter) is not FilterBase asked)
            return -1;

        bool passed = damage
            ? asked.PassesDamage((DamageType)damageType, entity)
            : asked.Passes(entity);
        return passed ? 1 : 0;
    }

    /// <summary>
    /// A ragdoll magnet, asked by the SDK entity that has just died. The whole
    /// decision is here because the whole of a magnet is here: which magnets
    /// exist, which of them reaches this body, and how hard it pulls.
    /// </summary>
    public Vector? OnRagdollMagnetForce(EntityHandle victim, Vector centre)
    {
        PhysRagdollMagnet? magnet = PhysRagdollMagnet.Best(_magnets, victim, centre);

        return magnet?.PullOn(centre);
    }
    /// <summary>
    /// How much of a jump a player gets (ABI slot). The answer is the side's
    /// resolved <see cref="MovementRules"/> pack — the core's identity, or the
    /// one mod that registered <see cref="IMovementRules"/> — which is also
    /// what the client's binding closure reaches, because both sides resolve
    /// the same shared hook the same way. The rule itself is shared, because
    /// the client predicts movement with the same one.
    /// </summary>
    public float GetJumpScale(in JumpRequest request) => _movementRules.Rules.Jump(request);

    public float GetFrictionScale(int playerIndex, float friction, float speed,
                                  bool ducking, int groundMaterial)
        => _movementRules.Rules.Friction(playerIndex, friction, speed, ducking, groundMaterial);

    public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
        => _movementRules.Rules.MaxSpeed(playerIndex, maxSpeed, ducking, groundMaterial);

    public void AddFireHeatInRadius(Vector origin, float radius, float heat)
    {
        foreach (var fire in EnvFire.InRadius(_fires, origin, radius))
            fire.AddHeat(heat);
    }

    /// <summary>
    /// A native entity being set alight, asking for the flame it can no longer
    /// build. See OnEntityIgnite in sourcesharp.h.
    /// </summary>
    public void OnEntityIgnite(EntityHandle entity, float lifetime, float size)
        => _flames.Ignite(entity, lifetime, size);

    // The engine's InfoPanel table, through its own string-table interfaces. A
    // server whose container cannot be acquired still loads (the message of the
    // day is not worth a failed load), and says so when it would have written.
    private IInfoPanel EngineInfoPanel()
    {
        INetworkStringTableDirectory? tables = EngineStringTableDirectory.AcquireServer(_engine, out string why);

        return tables is null ? new UnavailableInfoPanel(why, Log) : new InfoPanel(tables);
    }

    private sealed class UnavailableInfoPanel(string why, ILog log) : IInfoPanel
    {
        public bool Set(string name, ReadOnlySpan<byte> data)
        {
            log.Warning($"[SourceSharp] message of the day '{name}' not written: {why}\n");
            return false;
        }
    }
}
