using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.TempEnts;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Interop;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Client;

/// <summary>Client-side game logic. Runs inside ClientALC.</summary>
public sealed class ClientModule : ISideModule, IClientModule, IConVarHost, IClientRuntimeHost, IClientFxServices
{
    /// <summary>
    /// This side's game assembly, where every registry's scan starts: its own
    /// declarations and, compiled into it, the shared source's
    /// (plan_side_isolation P7). The scan keeps to this side
    /// (<see cref="LoadedAssemblies.All"/>), so what it finds is exactly the
    /// protocol this side speaks.
    /// </summary>
    // The particle manifests' file lists (C9 KeyValues K1), read in C# over
    // the engine's filesystem by search path.
    /// <summary>
    /// Fills the client's native ammo table (its CAmmoDef), in catalogue
    /// order. Client only: server.so keeps no ammo table, and the slot is the
    /// client's alone since plan_server_native_v2 T2. A weapon naming an
    /// unregistered type resolves to an ammo index of -1, so this runs before
    /// anything can be given.
    /// </summary>
    private void RegisterAmmo(AmmoCatalog catalog, IEngineWeapons weapons)
    {
        foreach (var ammo in catalog.All)
        {
            weapons.RegisterAmmoType(ammo.Name, ammo.DamageType, ammo.MaxCarry,
                                     ammo.TracerType, ammo.DamageForce, ammo.Damage);
        }

        Log.Msg($"[SourceSharp/Client] registered {catalog.Count} ammo type(s)\n");
    }

    private List<string>? ParticleManifests(bool map, string mapName)
    {
        var files = new ParticleManifestFiles(Log, _services.Require<IEngineFiles>().ReadFileOnPath);
        return map ? files.Map(mapName) : files.Game();
    }

    // A sprite model's material (C9 KeyValues K4): the .vmt read in C# over
    // the engine's filesystem, [$DECK] from this process' command line.
    private int SpriteMaterial(string spriteName, Span<byte> payload)
    {
        IReadOnlyList<string> args = _services.Require<ILocalCommandLine>().Args;
        IEngineFiles files = _services.Require<IEngineFiles>();
        // An #include or #base is read with no path ID (every search path);
        // the engine's read slot needs one, and GAME is the set a .vmt is on.
        var load = new KeyValuesLoad(Log, (path, id) => files.ReadFileOnPath(path, id ?? "GAME"), () => SpriteMaterialFiles.IsSteamDeck(args));
        (string Material, KeyValuesNode Vmt)? read = new SpriteMaterialFiles(Log, load).Read(spriteName);
        if (read == null) return -1;
        int bytes = KeyValuesBlock.Write(payload, read.Value.Material, read.Value.Vmt);
        return bytes < 0 ? -2 : bytes;
    }

    private static System.Reflection.Assembly Game => typeof(ClientModule).Assembly;

    // The engine this module is composed over (plan_abi_hotreload D8), handed
    // in by whoever creates it: the Bootstrap passes the side it loaded, and a
    // fact composing two modules passes two. It used to be looked up by side
    // through the static Engine, so two modules composed over one engine.
    private JitWarmup? _jitWarmup;
    private readonly ModuleEngine _engine;

    // Registers the adapters that call this side's shim areas: the real ones
    // in the game, fakes in a test that composes a module with no areas.
    private readonly IClientEngineAdapters _adapters;

    /// <summary>
    /// This composition's log: its engine's, over the client's console
    /// (statics to zero: it was the static Log).
    /// </summary>
    private ILog Log => _engine.Log;

    /// <summary>
    /// This composition's <c>IVEngineClient</c>, acquired through its own engine
    /// (plan_abi_hotreload D8). The services that call the engine client take
    /// this acquire rather than the engine (plan_shim_areas_split phase 8), so
    /// only the root names <see cref="ModuleEngine"/>.
    /// </summary>
    private IVtableEngineClient? AcquireEngineClient() => EngineClient.Acquire(_engine, out _);

    public ClientModule(ModuleEngine engine)
        : this(engine, new ClientEntityFetches())
    {
    }

    /// <summary>
    /// The same over <paramref name="adapters"/>, the registration of the
    /// engine adapters (the seams that call shim areas): a test composition
    /// hands fakes, so composing a module never needs attached areas.
    /// </summary>
    internal ClientModule(ModuleEngine engine, IClientEngineAdapters adapters)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(adapters);
        _engine = engine;
        _adapters = adapters;

        // What the module holds from construction, over its engine's log
        // (statics to zero).
        _layouts = new EntityLayoutRegistry(engine.Log);
        _tempEnts = new TempEntRegistry(engine.Log);
        _netStates = new NetStateCatalog(Game, engine.Log);
        _predictionErrors = new PredictionErrorRing(catalog: _netStates);
        _clock = engine.Clock();
        _standalones = new(_services);

        // The render policy grades a studio model by counts it reads from the
        // skeletons' studio headers, composed later (studio.cpp's eviction).
        _renderPolicy = new Standalone.ClientRenderPolicy(() => _skeletons?.Models);
    }

    /// <summary>
    /// The module's counters: frames, user commands and stages. Registered in
    /// the container for the commands that report them; they were statics.
    /// </summary>
    private readonly ClientFrameStats _stats = new();

    /// <summary>The module's counters, for the tests that drive <see cref="OnStage"/>.</summary>
    public IClientFrameStats Stats => _stats;

    // This side's per-client state that the stage entry drives and the report
    // commands read: registered in the container rather than held in statics.
    private readonly Interpolation _interpolation = new();
    private readonly Standalone.ClientRenderPolicy _renderPolicy;
    private readonly ConCommandRegistry _commands = new();
    private ClientArsenal _clientArsenal = null!;
    private ClientPlayerPrediction _prediction = null!;

    /// <summary>The viewer's world fog — plan_entity.md step 8e-render.</summary>
    private ClientWorldFog _worldFog = null!;
    private Standalone.ClientSkybox _skybox = null!;
    // Built in ComposeEngineAreas over this side's own networking area
    // (plan_abi_hotreload D8), so it registers with the client's engine.
    private UserMessageRegistry _userMessages = null!;
    // Built in Initialize, with the string tables; a composition that stops
    // at ComposeEngineAreas (the tests) has none.
    private UserMessageReceiver? _userMessageReceiver;
    // Built in ComposeEngineAreas over this side's own event area
    // (plan_abi_hotreload D8), so it subscribes and reads through the client's
    // engine and never whichever side is active.
    private GameEventRegistry _gameEvents = null!;

    // This side's movement runner, built in ComposeEngineAreas (D8).
    private MovementRunner _movement = null!;

    // The three movement decisions and the constants, resolved once after the
    // mods registered (phase 0.3), the client's copy of the same shared seam
    // the server resolves. The ABI getters answer from its frozen pack and
    // the runner hands the same pack to the replay's loop.
    private MovementRulesResolver _movementRules = null!;

    // Every [StringTable] declaration, registered on this side too and read-only
    // on it. Built in Initialize because it needs the container to ask for the
    // id-addressed engine area, which is internal on purpose.
    private StringTableRegistry _stringTables = null!;
    private readonly EntityLayoutRegistry _layouts;

    /// <summary>
    /// The temp entity declarations this side registered, and the classes that
    /// play them.
    ///
    /// <para>
    /// THE SAME <see cref="TempEntRegistry"/> THE SERVER HOLDS, over the same
    /// shared declarations — which is the whole of why the two tables pair. The
    /// only difference is which ABI slot answers <c>TempEntRegister</c>: a
    /// SendTable there, a RecvTable here.
    /// </para>
    /// </summary>
    private readonly TempEntRegistry _tempEnts;
    private TempEnts.ClientTempEnts _tempEntPlayers = null!;

    // The effect dispatch-by-name table (plan_client_novalve §2 cluster 4).
    // Built with the temp entity players, which need it; a native dispatch
    // reaches it through IManagedFxClient and IClientFxServices below.
    private Effects.ClientEffectDispatch? _effectDispatch;

    // IEffects' decisions (cluster 3), reached by the native thunk the same way.
    private Effects.ClientEffects? _clientEffects;

    // The client-side effect list (cluster 3): the view draws it through
    // IManagedFxClient, and the fork's effect TUs add to it.
    private Effects.ClientEffectListHost? _effectListHost;

    // The sprite renderer's planner: every C_SpriteRenderer::DrawSprite asks it
    // through IManagedFxClient::SpritePlan.
    private Effects.ClientSpriteHost? _spriteHost;

    // The env_spritetrail ribbons, ticked and drawn by native's renderable.
    private Effects.ClientSpriteTrails? _spriteTrails;

    // The client effect precache system's level decisions.
    private Effects.ClientEffectPrecache? _effectPrecache;

    // The smoke fog overlay's per-frame decision.
    private Effects.SmokeFogOverlay? _smokeFog;

    // The legacy tempents' decisions, which native's thunk asks.
    private TempEnts.ClientTempEntDecisions? _tempEntDecisions;

    /// <summary>
    /// The legacy temp entity pool — <c>c_te_legacytempents.cpp</c>'s
    /// <c>CTempEnts</c>, from the list up (plan_tmp.md Part 5).
    ///
    /// <para>
    /// HELD BY THE MODULE, like the dlights and the animation pump, and for the
    /// same reason: there is exactly one pass over it per frame and native
    /// drives that pass from where <c>cdll_client_int.cpp</c> always drove it.
    /// It is also in the container, so the temp entities that ask the pool for
    /// something get the one the frame runs.
    /// </para>
    /// </summary>
    private TempEnts.ClientTempEntPool _tempEntPool = null!;
    private TempEnts.ClientTempEntFrame _tempEntFrame = null!;
    private TempEnts.TempEntSpawns _tempEntSpawns = null!;
    // Built in Initialize, through the container: it takes the engine.
    private Hud _hud = null!;

    // cs_cl_draw_batch: whether a view's custom draws are prepared in one crossing (C7 item 7).
    private Render.DrawBatchConVar? _drawBatch;

    /// <summary>The UI host (plan_client_ui U2); null until built, which the binding tolerates.</summary>
    private IUiClientEvents? _ui;

    // The input system for the UI's key names, said once when it cannot be had.
    private bool _inputRefused;

    private IVtableInputSystem? AcquireInputSystem()
    {
        var input = InputSystem.Acquire(_engine, out string why);
        if (input is null && !_inputRefused)
        {
            _inputRefused = true;
            Log.Warning($"[SourceSharp/Ui] no input system for key names: {why}\n");
        }
        return input;
    }

    /// <summary>The centre print (C3): the engine's ICenterPrint lands here through the UI binding.</summary>
    private Vgui.CentrePrint? _centre;

    /// <summary>The loading and paused plaques (C3): the engine's ILoadingDisc lands here through the UI binding.</summary>
    private Vgui.LoadingDisc? _disc;

    /// <summary>Who is speaking (C3): the engine's VoiceStatus lands here through the UI binding.</summary>
    private Vgui.VoiceSpeakers? _voice;

    /// <summary>The voice manager (stock CVoiceStatus): who talks, the server's masks, the mutes.</summary>
    private Voice.VoiceStatus? _voiceStatus;

    /// <summary>The engine's VoiceStatus to both: the HUD's speakers and the voice manager.</summary>
    private Voice.VoiceStatusFanOut? _voiceEvents;

    /// <summary>The client mode's key decision (C3): the engine's key events land here through the UI binding.</summary>
    private Vgui.ClientKeyInput? _keys;

    /// <summary>The UI's own load context and the runtime in it (plan_client_ui U5).</summary>
    private Vgui.UiLoader? _uiLoader;

    /// <summary>
    /// The dlights being kept alive, and the colour corrections being weighted.
    ///
    /// Held by the module rather than by a static, because the module is what
    /// drives their per-frame pass and drops them at a level change — and
    /// because a static list outlives a managed reload that replaced everything
    /// holding it. The entities that put themselves in these ask the container
    /// for them.
    /// </summary>
    private Standalone.DynamicLights _lights = null!;
    private Effects.ClientWinds _winds = null!;
    private Effects.ClientParticleEffects _particleEffects = null!;
    private Effects.FleckSystems _flecks = null!;
    private Effects.SparkEffects _sparks = null!;
    private ClientSoundController _sounds = null!;

    // The client's sounds (sound_emitter.cpp's client emit path, moved): every
    // caller's emitter, bound in BuildManagedRoot, and the block its native
    // recipient filter is built in.
    private Sounds.ClientSounds _clientSounds = null!;
    private ManagedVtable? _recipientFilter;
    private Soundscapes.ClientSoundscapes _soundscapes = null!;
    private Soundscapes.ClientSoundscapeDriver _soundscapeDriver = null!;
    private Effects.ClientPrecipitations _precipitations = null!;

    // The client's physics (plan_client_novalve C6, cluster 9, and the client
    // leftovers of plan physics_bones_native_zero): the level's environment and
    // step (ClientPhysicsSystem), the impact sounds, and the tables the
    // callbacks arrive through.
    private Physics.ClientImpactSounds? _impactSounds;
    private Physics.ClientPhysicsSystem? _physicsSystem;
    private ManagedVtable? _physicsCollisionEvents;
    private Physics.ClientCollisionSolver? _collisionSolver;
    private Physics.ClientFluidEvents? _fluidEvents;
    private FrictionSounds? _frictionSounds;
    private Physics.ClientFriction? _friction;
    private Physics.ClientContactTouches? _contactTouches;
    private ManagedVtable? _physicsCollisionSolver;

    // The client's Steam (C6, Steam to managed): stock's CClientSteamContext
    // and the five STEAM_CALLBACKs it and CSecureLaunchSystem registered. Built
    // with the root, and torn down at Shutdown, which runs before a reload
    // unloads this context and before the final unload.
    private Steam.ClientSteamContext? _steamContext;
    private SteamCallbacks? _steamCallbacks;

    // The server-bounded convars' decision (C8): watched inputs, pushed values.
    private BoundedConVars? _boundedConVars;

    // The client's input (plan_client_novalve §2 cluster 13, lane c6-input):
    // native's IInput thunk set asks it through IManagedInputClient.
    private Input.ClientInputService? _inputService;
    private Standalone.ColorCorrections _corrections = null!;

    // The animation-event dispatcher and what it drives. Owned by the module
    // rather than by any entity: an animation keeps its own time whether or
    // not the entity playing it is visible, and there is exactly one pass.
    private Standalone.AnimationEventEffects _animEffects = null!;
    private Standalone.AnimationEventPump _animEvents = null!;

    /// <summary>
    /// The client's systems, as everything built afterwards reaches them.
    ///
    /// The same arrangement as the server's, and the client needs it more: a
    /// drawn entity had no route at all to anything the module owned. See
    /// <see cref="ServiceContainer"/>.
    /// </summary>
    private readonly ServiceContainer _services = new();

    // The side's console lines, kept from the console binding's build so the
    // frame and the unload can drain them (console-batch); null before it.
    private ConsoleLines? _consoleLines;

    /// <summary>
    /// What the client has been told about its standalone entities' dormancy
    /// and authored about their sequences (plan_crossings.md X2/X3). This
    /// composition's own (plan_abi_hotreload D7): registered in
    /// <see cref="_services"/> for the render binding and the gate commands,
    /// and handed to the push handlers that feed and clear it.
    /// </summary>
    private readonly StandaloneMirror _mirror = new();

    /// <summary>This composition's container, for a fact that composes two.</summary>
    internal IServiceLocator Services => _services;

    /// <summary>
    /// The engine areas, and the mirror the render area reads, in that order's
    /// reverse: the render binding takes the mirror in its constructor.
    /// Separate from <see cref="Initialize"/> so a fact can compose two of
    /// these without an engine behind them.
    /// </summary>
    internal int ComposeEngineAreas()
    {
        _services.AddInstance(_mirror);

        // This side's reload sections: the debug channel's resume data crosses
        // cs_cl_reload in them (WireDebugChannel, ReloadSave). Here, with the
        // mirror, because ReloadSave is reachable from this composition alone.
        _services.AddInstance<IReloadSections>(new ReloadSections(_engine.Reload));
        _reloadSections = _services.Require<IReloadSections>();

        // This side's model caches, its engine's own (plan_abi_hotreload D8):
        // the render binding reads them through that engine, and the
        // model-descriptor report is handed them from here. The runtime
        // binding clears them at level shutdown.
        _services.AddInstance(_engine.Models);
        _services.AddInstance(_engine.Breaks);

        // The mirror gate's independent oracle, over this side's own slots.
        _services.AddInstance(new StandaloneSlotReference(_engine, _skeletonRoute));

        // The client's three entity fetches and the other engine adapters,
        // built here and handed to the IEngineEntities binding the next line
        // builds (P1 run 3).
        _adapters.AddTo(_services, _engine);

        // The skeleton route the engine bindings ask through, registered before
        // they are built and filled when the client's skeletons answer.
        _services.AddInstance(_skeletonRoute);

        int areas = _engine.AddTo(_services);
        _convars = new ConVarRegistry(_services.Require<IEngineConsole>());

        // The server-bounded convars (C8): native objects the engine reads
        // through their vtable, answering values decided here. Started now so
        // the first frame already reads decided values; a reload starts it
        // again (the native watch ids and stored values survive).
        IEngineConsole boundedConsole = _services.Require<IEngineConsole>();
        _boundedConVars = new BoundedConVars(_services.Require<IClientConVarSlots>(), boundedConsole.FindConVarValue);
        _services.AddInstance(_boundedConVars);
        _boundedConVars.Start();
        _userMessages = new UserMessageRegistry(Log);
        // In the container for the dev API's received counts (ClientDebugBuild).
        _services.AddInstance(_userMessages);

        // THE CLIENT'S SOUNDS (sound_emitter.cpp's client emit path, moved):
        // one service for every caller, registered before the first of them
        // (the movement runner's world) and bound to its emitter in
        // BuildManagedRoot, where the native recipient filter's block is built.
        _clientSounds = new Sounds.ClientSounds(_services.Require<IEngineEntities>());
        _services.AddInstance(_clientSounds);
        _services.AddInstance<Sounds.IClientEntitySounds>(_clientSounds);
        _services.AddInstance<IFxGameSounds>(_clientSounds);
        _services.AddInstance<IClientImpactSoundEmitter>(_clientSounds);
        _services.AddInstance<IFxTracerSound>(_clientSounds);
        _services.AddInstance<ISoundscapeSounds>(_clientSounds);

        // This side's movement runner, over this module's engine
        // (plan_abi_hotreload D8): the native movement crossing and the
        // managed replay run the same loop through it.
        // The prediction's world carries stock's client move helper
        // (ClientPredictionMoveWorld, lane c6-input): the predicted swim
        // sound, and the helper's no-op answers taken here.
        _movement = new MovementRunner(_engine, world => new ClientPredictionMoveWorld(world,
            () => _prediction?.PredictingHost ?? EntityHandle.None,
            () => _prediction?.FirstRunOfFunctions ?? false,
            _clientSounds));
        _services.AddInstance(_movement);

        // And the module's engine itself, for what acquires an engine
        // interface through this side's own factory (EngineClient, D8).
        _services.AddInstance(_engine);

        // And its engine client, for what reports the engine's refusal
        // (cs_vtable_client) rather than naming the engine.
        _services.AddInstance<IVtableEngineClientSource>(new VtableEngineClientSource(_engine));

        // This side's build stamps (statics to zero): its SideState's, which
        // the HUD's corner stamp reads.
        _services.AddInstance(_engine.Build);

        // This side's declared networked states by name and type id, and its
        // prediction-error ring over them (statics to zero: three static maps
        // and the process-wide PredictionErrors.Ring).
        _services.AddInstance(_netStates);
        _services.AddInstance(_predictionErrors);

        _gameEvents = new GameEventRegistry(_services.Require<IEngineGameEvents>(), Log);
        // In the container for the dev API's received counts (ClientDebugBuild).
        _services.AddInstance(_gameEvents);

        return areas;
    }

    /// <summary>This side's declared networked states by name and type id (statics to zero).</summary>
    private readonly NetStateCatalog _netStates;

    /// <summary>Where the engine bindings find the client's skeletons (plan physics_bones_native_zero 1.2).</summary>
    private readonly StandaloneSkeletonRoute _skeletonRoute = new();

    /// <summary>The client's managed skeletons; null before Initialize.</summary>
    private ClientSkeletons? _skeletons;

    // The client's jointed bodies and the vphysics they are built in (plan
    // physics_bones_native_zero Phase 3); null when vphysics did not answer.
    private Ragdolls.ClientRagdolls? _ragdolls;
    private RagdollVPhysics? _ragdollPhysics;

    // physics_shared.cpp's client half (plan_physics_bones_native_zero 2.6):
    // the world build, the surface manifest and precache, and g_SolidSetup's
    // answers, which fork game/client/physics.cpp asks through the forwarders.
    private PhysicsWorldVPhysics? _physicsWorldVPhysics;
    private Physics.ClientPhysicsWorld? _physicsWorld;


    /// <summary>The client's managed skeletons, for the commands and the facts.</summary>
    internal ClientSkeletons? Skeletons => _skeletons;

    /// <summary>This composition's prediction-error ring, naming fields through <see cref="_netStates"/>.</summary>
    private readonly PredictionErrorRing _predictionErrors;

    /// <summary>The standalone push handler the root builds, fed into this composition's mirror.</summary>
    internal StandaloneClientBinding NewStandaloneBinding() => new(_engine.Crossings, Log, this, _mirror, _netStates);

    /// <summary>The runtime handler the root builds, which clears this composition's mirror at level shutdown.</summary>
    internal ClientRuntimeBinding NewRuntimeBinding() => new(_engine.Crossings, Log, this, _mirror, _engine.Models, _engine.Breaks);

    // Log's sink for this side while the module is composed (plan_abi_hotreload
    // D5): the module's own ILog, attached at composition and taken back in
    // Shutdown so Abi's Log keeps nothing of this load context across a reload.
    private IDisposable? _logAttachment;

    // The client's descriptor durations, read from the skeletons' studio
    // headers; taken back in Shutdown (see ServerModule's twin).
    private IDisposable? _descriptorAttachment;

    // plan_client_vtables CD: the managed-built interfaces native acquires from
    // the client's root, as the server's ServerModule holds them.
    private ManagedBindings? _bindings;
    private ManagedVtable? _managedRoot;

    // What the mods registered (SideServices.Register), disposed at Shutdown.
    private IReadOnlyList<Type> _modServices = [];

    private Scoreboard _scoreboard = null!;

    /// <summary>
    /// The debug channel, when this process was started with <c>-debugui</c>.
    ///
    /// <para>
    /// <b>NULL IS THE DEFAULT AND NULL IS THE WHOLE NEGATIVE PROOF.</b> Without
    /// the flag this field stays null, <c>DebugChannelInviteMessage.Accepted</c>
    /// is never installed, and the invitation handler returns on its first line
    /// — so there is no reachable path from a server's message to a socket, a
    /// dial, or a background task. Held by the module rather than by a static so
    /// a managed reload cannot leave one behind.
    /// </para>
    /// </summary>
    private IDebugApiClient? _debugChannel;

    /// <summary>
    /// This side's debug channel invitation handler, the container's instance,
    /// whose Accepted hook WireDebugChannel installs and every level shutdown
    /// clears. Null until Initialize has built the handlers.
    /// </summary>
    private DebugChannelInviteMessage _invites = null!;

    private IReloadSections _reloadSections = null!;

    // Built in Initialize, through the container.
    private FrameTimeWindow _frameTimes = null!;

    // Built in Initialize: the reload's service tier.
    private ClientReloadParticipants? _reloadParticipants;
    private Effects.ClientParticleSims? _particleSims;
    private ClientReloadLedger? _reloadLedger;

    /// <summary>Exposed so a report command and the tests can see whether one exists at all.</summary>
    public IDebugApiClient? DebugChannel => _debugChannel;

    /// <summary>
    /// Routes an arriving temp entity to the class that plays it. The blob is
    /// borrowed native memory, valid only for the duration of the call.
    /// </summary>
    public void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob)
        => _tempEntPlayers?.Dispatch(handle, blob);

    /// <summary>
    /// A native forward raised one locally: the id and the numbers arrive rather
    /// than a blob, because the effect is local and never has to become bytes.
    /// </summary>
    public bool ForwardTempEntity(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints)
    {
        TempEntState? state = TeForwardDispatch.Build((TeForward)id, floats, ints);

        return state is not null && (_tempEntPlayers?.PlayLocally(state) ?? false);
    }

    /// <summary>Exposed so the report command can read what has arrived.</summary>
    public TempEnts.ClientTempEnts TempEntities => _tempEntPlayers;

    /// <summary>
    /// The effect services a native call reaches (IManagedFxClient): null until
    /// built, and a call then does nothing, as stock's walk of an empty list ran
    /// nothing.
    /// </summary>
    IClientEffectDispatch? IClientFxServices.Dispatch => _effectDispatch;

    IClientEffects? IClientFxServices.Effects => _clientEffects;

    IClientEffectListHost? IClientFxServices.EffectList => _effectListHost;

    IClientSpriteHost? IClientFxServices.Sprites => _spriteHost;

    IClientSpriteTrailHost? IClientFxServices.SpriteTrails => _spriteTrails;

    IClientEffectPrecacheHost? IClientFxServices.Precache => _effectPrecache;

    IClientSmokeFogHost? IClientFxServices.SmokeFog => _smokeFog;

    IClientTempEntHost? IClientFxServices.TempEnts => _tempEntDecisions;

    /// <summary>Exposed for the same reason: the pool's counts are a report.</summary>
    public TempEnts.ClientTempEntPool TempEntPool => _tempEntPool;

    //-------------------------------------------------------------------------
    // THE LEGACY TEMP ENTITY POOL, driven from native (plan_tmp.md Part 5).
    //
    // Both arrive before Initialize has run at least once in a reload, and
    // both answer harmlessly then: an update or a clear over no pool is
    // nothing to do. (The pool's third callback, the native-originated
    // reservation, died with the exports it served; every spawn is
    // managed-originated now.)
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which effect family an impact is, from <see cref="ImpactChoicePolicy"/>.
    /// A pure function of its inputs, like ImpactEffectPolicy's blocks, so it needs nothing
    /// off this module and answers the same before the level has loaded as
    /// after it.
    /// </summary>
    public int ImpactChoice(ReadOnlySpan<int> input, Span<int> output,
                            Span<byte> particleName, Span<byte> decalName)
    {
        int written = ImpactChoicePolicy.Choose(input, output, out ImpactChoice choice);
        if (written < 0) return -1;

        // A name that does not fit is a REFUSAL, not a truncation: half a
        // particle-system name resolves to nothing and looks exactly like a
        // system that failed to load.
        if (!WriteAsciiZ(choice.ParticleName, particleName)) return -1;
        if (!WriteAsciiZ(choice.DecalName, decalName)) return -1;

        return written;
    }

    /// <summary>
    /// A NUL-terminated ASCII name into a native buffer. An EMPTY span is the
    /// caller saying it does not want this one, and succeeds without writing.
    /// </summary>
    private static bool WriteAsciiZ(string value, Span<byte> destination)
    {
        if (destination.IsEmpty) return true;
        if (value.Length + 1 > destination.Length) return false;

        for (int i = 0; i < value.Length; i++)
            destination[i] = (byte)value[i];
        destination[value.Length] = 0;

        return true;
    }

    /// <inheritdoc/>
    public void TempEntPoolUpdate(float frameTime) => _tempEntPool?.Update(frameTime);

    /// <inheritdoc/>
    public void TempEntPoolClear() => _tempEntPool?.Clear();


    // The three OnNetEntity* handlers went with the shell they routed for.
    // ClientStandaloneRegistry does the same job for every managed entity left.
    // Built in ComposeEngineAreas over this side's console (plan_abi_hotreload
    // D8), not over whichever side is active.
    private ConVarRegistry _convars = null!;

    public void Initialize(Side side)
    {
        Log.Msg("[SourceSharp/Client] managed client module initialized\n");
        SharedInfo.Announce(side, Log);

        // THE MANAGED-BUILT INTERFACES native acquires by name through the root
        // LoadSideV2 hands back (plan_client_vtables CD). The client's runtime
        // first; its areas register here as CE moves them.
        _bindings = new ManagedBindings();
        _services.AddInstance(_bindings);
        _bindings.Add("IManagedRuntimeClient", v => ClientInteropAreas.Runtime(v, NewRuntimeBinding()));
        _bindings.Add("IManagedCommandsClient",
                      v => ManagedAreaFactory.Create(v, new CommandsClientBinding(_engine.Crossings, this, this)));
        _bindings.Add("IManagedEventsClient",
                      v => ManagedAreaFactory.Create(v, new EventsClientBinding(_engine.Crossings, this)));
        _bindings.Add("IManagedUserMessagesClient",
                      v => ManagedAreaFactory.Create(v, new UserMessagesClientBinding(_engine.Crossings, _userMessageReceiver!)));
        _bindings.Add("IManagedStandaloneClient",
                      v => ManagedAreaFactory.Create(v, NewStandaloneBinding()));
        _bindings.Add("IManagedPredictionClient",
                      v => ManagedAreaFactory.Create(v, new PredictionClientBinding(_engine.Crossings, this, this, _movement)));
        _bindings.Add("IManagedHudClient", v => ManagedAreaFactory.Create(v, new HudClientBinding(_engine.Crossings, this)));
        _bindings.Add("IManagedUiClient", v => ManagedAreaFactory.Create(v, new UiClientBinding(_engine.Crossings, () => _ui, () => _centre, () => _disc, () => _voiceEvents, () => _keys, () => _voiceStatus)));
        // The client engine's console prints, forwarded in process by the
        // display func native installs on the client's ICvar at area acquire
        // (docs/todo.md's console-capture item): the mirror of the server's
        // registration, feeding the client side's ConsoleLines — which the
        // console window reads — resolved as a service (no EngineSide name
        // from the game assemblies, EngineAreaTests' audit).
        _bindings.Add("IManagedConsoleOutputClient",
                      v => ManagedAreaFactory.Create(v, new ConsoleDisplayClientBinding(_engine.Crossings, _consoleLines = _services.Require<ConsoleLines>())));
        _bindings.Add("IManagedTempEntsClient",
                      v => ManagedAreaFactory.Create(v, new TempEntsClientBinding(_engine.Crossings, Log, this)));
        _bindings.Add("IManagedBeamsClient", v => ManagedAreaFactory.Create(v, new BeamsClientBinding(() => _beams)));
        // The client state file's buffer and its DECALLIST / MUSICLIST blocks
        // (the client save codec): the engine allocator is asked at SaveInit,
        // and a block per decal is not narrated (its refusals are).
        _bindings.Add("IManagedSaveRestoreClient",
                      v => ManagedAreaFactory.Create(v, new SaveRestoreClientBinding(_engine.Crossings, Log, this,
                          new SaveBlockSession(Log, () => EngineClient.Acquire(_engine, out _) as ISaveMemory, traceBlocks: false))));
        _bindings.Add("IManagedDiagnosticsClient", v => ManagedAreaFactory.Create(v, new DiagnosticsClientBinding(_engine.Crossings, Log)));
        // The decal tables decals.cpp held (lane c2-sims, cluster 18): native's
        // decalsystem forwards here. The service is the container's (below),
        // resolved when native acquires the area, after composition.
        // The detail props (C7 cluster 10): native's client system asks here.
        // The service is the container's (below), resolved when native
        // acquires the area, after composition.
        _bindings.Add("IManagedDetailPropsClient",
                      v => ManagedAreaFactory.Create(v, new Detail.DetailPropsClientBinding(_engine.Crossings, Log,
                                                                                           _services.Require<Detail.IClientDetailProps>())));
        _bindings.Add("IManagedDecalsClient",
                      v => ManagedAreaFactory.Create(v, new DecalsClientBinding(_engine.Crossings, Log,
                                                                               _services.Require<IClientDecals>())));
        // The managed particle sims (lane c2-sims): the native draw executor
        // asks each owner's sim for stock's three steps.
        _bindings.Add("IManagedParticlesClient",
                      v => ManagedAreaFactory.Create(v, new ParticlesClientBinding(_engine.Crossings, Log,
                                                                                  _services.Require<Effects.ClientParticleManager>(),
                                                                                  _services.Require<IClientParticleQuery>(),
                                                                                  _services.Require<IClientImpactSprays>(),
                                                                                  _services.Require<IClientImpactSparks>(),
                                                                                  _services.Require<IClientWaterSplashes>(),
                                                                                  new Effects.RainTracerGeometry(),
                                                                                  ParticleManifests,
                                                                                  _services.Require<Effects.NewParticleManager>())));
        // point_worldtext's layout (lane c2-sims): the native text renderer asks
        // each owner for its draw's vertices.
        // The client's sound envelope controller (sound cluster 17, lane
        // c2-sims): the shim's patches ask the shared controller.
        _bindings.Add("IManagedInputClient",
                      v => ManagedAreaFactory.Create(v, new Input.InputClientBinding(_engine.Crossings, _inputService!)));
        _bindings.Add("IManagedSoundControllerClient",
                      v => ManagedAreaFactory.Create(v, new SoundControllerClientBinding(_engine.Crossings, _sounds)));
        // The client's sound emitter (sound_emitter.cpp's client emit path,
        // moved): the CSoundEmitterSystem row's stages and native's last
        // GameSound_* callers.
        _bindings.Add("IManagedSoundEmitterClient",
                      v => ManagedAreaFactory.Create(v, new SoundEmitterClientBinding(_engine.Crossings, _clientSounds)));
        // The client soundscape system (sound cluster 17): native's
        // client-system shell drives it.
        // The weather's render plan (lane c2-sims): DrawPrecipitation asks it.
        _bindings.Add("IManagedPrecipitationClient",
                      v => ManagedAreaFactory.Create(v, new PrecipitationClientBinding(_engine.Crossings, _precipitations)));
        _bindings.Add("IManagedSoundscapesClient",
                      v => ManagedAreaFactory.Create(v, new SoundscapesClientBinding(_engine.Crossings, _soundscapeDriver)));
        // A sprite model's material file (C9 KeyValues K4): CEngineSprite::Init
        // asks its material's name and .vmt, read in C# over the engine's
        // filesystem, and builds the procedural materials from them.
        _bindings.Add("IManagedMaterialFilesClient",
                      v => ManagedAreaFactory.Create(v, new MaterialFilesClientBinding(_engine.Crossings, Log, SpriteMaterial,
                                                                                     Render.ScreenshotEncoding.EncodeFile)));
        // The material proxies (lane c5-proxies, cluster 1): native's one
        // trampoline class asks the host each Init, bind and release.
        _bindings.Add("IManagedMaterialProxyClient",
                      v => ManagedAreaFactory.Create(v, new MaterialProxyClientBinding(_engine.Crossings, Log,
                                                                                     _services.Require<IClientMaterialProxies>())));
        // The render plan (C7): View_Render asks which path draws each frame.
        _bindings.Add("IManagedRenderClient",
                      v => ManagedAreaFactory.Create(v, new RenderClientBinding(_engine.Crossings, Log,
                                                                              _services.Require<IClientRenderPlan>())));
        // The client leaf system (C7 cluster 12 L2): libss_fwd_client's
        // IClientLeafSystem asks the registry and its decisions.
        _bindings.Add("IManagedLeafSystemClient",
                      v => ManagedAreaFactory.Create(v, new LeafSystemClientBinding(
                          _engine.Crossings, Log, _services.Require<IClientLeafSystemHost>())));
        // kClientSystems[] rows whose system is managed's (C7): each row's
        // adapter asks its system's stages by the row's name.
        _bindings.Add("IManagedClientSystemsClient",
                      v => ManagedAreaFactory.Create(v, new ClientSystemsClientBinding(_engine.Crossings, Log,
                                                                                    _services.Require<ClientSystems.ManagedClientSystems>())));
        // The plan's pixel visibility ops (C7): a view's end draws managed's
        // proxies, the scene's end frees what nobody asked about.
        _bindings.Add("IManagedPixelVisClient",
                      v => ManagedAreaFactory.Create(v, new PixelVisClientBinding(_engine.Crossings, Log,
                                                                               _services.Require<Render.PixelVisibility.PixelVisHost>())));
        // The plan's glow overlay ops (C7): the draw carries out managed's
        // commands, the sky update asks the sky overlays' obstruction.
        _bindings.Add("IManagedGlowClient",
                      v => ManagedAreaFactory.Create(v, new GlowClientBinding(_engine.Crossings, Log,
                                                                            _services.Require<Render.GlowOverlays.GlowHost>())));
        _bindings.Add("IManagedWorldTextClient",
                      v => ManagedAreaFactory.Create(v, new WorldTextClientBinding(_engine.Crossings, Log,
                                                                                  new Effects.ClientWorldTexts(_standalones))));
        _bindings.Add("IManagedFxClient", v => ManagedAreaFactory.Create(v, new FxClientBinding(_engine.Crossings, this)));
        // The engine's skeleton asks of a standalone entity (plan
        // physics_bones_native_zero 1.1, client half): resolved when native
        // acquires the area, after composition built the skeletons.
        _bindings.Add("IManagedBonesClient",
                      v => ManagedAreaFactory.Create(v, new BonesClientBinding(_engine.Crossings, Log,
                          _skeletons ?? throw new InvalidOperationException("the client's skeletons are not composed"))));
        _bindings.Add("IManagedPhysicsClient",
                      v => ManagedAreaFactory.Create(v, new PhysicsClientBinding(_engine.Crossings, Log, () => _physicsWorld, () => _physicsSystem)));

        // CONVARS FIRST. A command or a drawn entity may ask for one, by class
        // or by the name a player types, and the container has to be holding it
        // before anything that wants it is built. See the server's Initialize,
        // which orders these three passes the same way and for the same reason.
        // THE ENGINE FIRST, before even the convars. It is the deepest leaf in
        // the container — everything else can want it and it wants nothing —
        // and registering it here means no later line has to think about order.
        //
        // Twenty-two areas rather than one interface, so that a class says
        // which part of the engine it touches. A trigger taking IEngineDamage
        // has told a reader what it does to whoever walks in, before they open
        // the body.
        //
        // The mirror behind IsDormant and SequenceState (plan_crossings.md
        // X2/X3) goes in just before them: it is this composition's
        // (plan_abi_hotreload D7), the render binding takes it in its
        // constructor, and the push handlers above were handed the same
        // instance. A second mirror would be one nothing feeds, and
        // cs_cl_mirror_check would report every entity as diverged.
        int engineAreas = ComposeEngineAreas();

        // THE CORE SERVICES A MOD REACHES BY INJECTION on this side
        // (plan_host_sdk §2.8): what the server registers, less the server's own.
        // The side's ILog is its engine's (EngineServices.AddTo registered it
        // with the areas); this composition's plumbing prints through it too.
        var log = _services.Require<ILog>();
        _services.AddFactory<IHooks<IClientLevelHook>>(locator => new Hooks<IClientLevelHook>(locator, log));
        _logAttachment = _engine.AttachLog(log);
        _services.AddInstance<IProcessEnvironment>(new ProcessEnvironment());
        _services.AddInstance<IModInfo>(new ModInfo());
        _services.AddInstance(new ParticleReloadPolicy());
        _services.AddInstance(new ClientMapMaterials(log));
        // decals.cpp's tables (lane c2-sims); an IReloadParticipant, so its
        // parse and indices cross cs_cl_reload.
        _services.Add<IClientDecals, ClientDecals>();
        // The material proxies (lane c5-proxies); an IReloadParticipant, so every
        // bound proxy's Init answers and bind state cross cs_cl_reload.
        _services.Add<IClientMaterialProxies, MaterialProxies.MaterialProxyHost>();
        // The render plan (C7): View_Render's per-frame path and its recorder.
        // The engine's uniform random stream, for a service (or a mod) that draws
        // from it as stock's free RandomFloat does.
        _services.AddInstance<IUniformRandom>(new EngineRandom(() => UniformRandomStream.Acquire(_engine, out _), log));
        // The old-style particle manager (C7 particles phase 2.3): every
        // executor's binding, updated here; an IReloadParticipant, so the
        // bindings cross cs_cl_reload while the native executors stay.
        _services.AddFactory<Effects.ClientParticleManager>(locator => new Effects.ClientParticleManager(
            () => _particleSims ??= new Effects.ClientParticleSims(_standalones, _particleEffects!),
            locator.Require<IUniformRandom>(), locator.Require<ParticleViews>()));
        // The new-style effects' manager (C7 particles phase 3): every native
        // shell's decisions, made here, the library stepping the collections;
        // an IReloadParticipant, so the decisions' state crosses cs_cl_reload.
        _services.AddFactory<Effects.NewParticleManager>(locator => new Effects.NewParticleManager(locator.Require<IParticleCollections>()));
        _services.AddFactory<IClientParticleExecutor>(locator => new Effects.BoundParticleExecutor(
            locator.Require<INativeParticleExecutor>(), locator.Require<Effects.ClientParticleManager>()));
        // C7 post-processing: viewpostprocess.cpp's passes as a managed program,
        // and the hook a mod draws its own pass through.
        _services.AddFactory<IHooks<Render.IPostProcessContributor>>(locator => new Hooks<Render.IPostProcessContributor>(locator, log));
        _services.AddFactory<Render.PostProcessHost>(locator => new Render.PostProcessHost(
            locator.Require<IHooks<Render.IPostProcessContributor>>(),
            locator.Require<IFxRender>(), locator.Require<IEngineConsole>(), log, AcquireEngineClient));
        _services.AddFactory<MaterialProxies.IPostProxyValues>(locator => locator.Require<Render.PostProcessHost>());
        _services.AddFactory<Render.DemoViewCamera>(locator => new Render.DemoViewCamera(
            button => _inputService?.KeyState(button) ?? 0f, () => locator.Require<Render.ClDemoviewoverrideConVar>().Value));
        // ScreenSpaceEffects.cpp's manager (C7 screen-space effects), with
        // stock's one registration: run at the view's level start and end,
        // drawn into the plan; an IReloadParticipant, so each effect's flag
        // crosses cs_cl_reload.
        _services.AddFactory<Render.ScreenSpaceEffectManager>(locator => Render.ScreenSpaceEffectManager.WithStockEffects(
            w => Log.Warning(w), p => LocalCommandLine.Has(locator.Require<ILocalCommandLine>().Args, p)));
        _services.Add<Render.RenderPlanHost, Render.RenderPlanHost>();
        // The kClientSystems[] rows whose system is managed's (C7): the
        // viewport system (vgui_game_viewport.cpp's CViewportClientSystem).
        _services.AddFactory<ClientSystems.ManagedClientSystems>(locator =>
        {
            var systems = new ClientSystems.ManagedClientSystems();
            systems.Register(new ClientSystems.ViewportClientSystem(locator.Require<IClientModeLayout>()));
            systems.Register(new Render.PixelVisibility.PixelVisibilityClientSystem(
                locator.Require<Render.PixelVisibility.PixelVisibilitySystem>()));
            systems.Register(new Render.GlowOverlays.GlowOverlayClientSystem(
                locator.Require<Render.GlowOverlays.GlowOverlaySystem>()));
            return systems;
        });
        // The glow overlays (C7, glow_overlay.cpp and warp_overlay.cpp): the
        // list the explosions' and impacts' flashes join, drawn by the plan's
        // GlowDrawOverlays op; cl_sun_decay_rate is glow_overlay.cpp's, declared
        // in C# since that file left, and building_cubemaps the engine's,
        // watched. The flashes last a tenth of a second, so a managed reload
        // drops the few in flight rather than carry them.
        _services.AddFactory<Render.PixelVisibility.BuildingCubemapsWatch>(locator =>
        {
            var watch = new Render.PixelVisibility.BuildingCubemapsWatch(
                new Flashlight.WatchedConVarValues(locator.Require<IClientConVarSlots>(), locator.Require<IEngineConsole>().FindConVarValue));
            watch.Start();
            _buildingCubemaps = watch;
            return watch;
        });
        _services.AddFactory<Render.GlowOverlays.GlowSettings>(locator =>
        {
            var buildingCubemaps = locator.Require<Render.PixelVisibility.BuildingCubemapsWatch>();
            var sunDecayRate = _convars.Get<Render.GlowOverlays.SunDecayRateConVar>()
                               ?? throw new InvalidOperationException("cl_sun_decay_rate is not registered");
            return new Render.GlowOverlays.GlowSettings(() => buildingCubemaps.Value, () => sunDecayRate.Value);
        });
        _services.AddFactory<Render.GlowOverlays.GlowOverlaySystem>(locator => new Render.GlowOverlays.GlowOverlaySystem(
            locator.Require<IPixelVisibility>(),
            new Render.GlowOverlays.ClientGlowEngine(locator.Require<IGlowMaterials>(), locator.Require<IEngineWorldQuery>()),
            locator.Require<Render.GlowOverlays.GlowSettings>(),
            new Render.PixelVisibility.PixelVisFrame(locator.Require<IEngineEnvironment>())));
        _services.AddFactory<Render.GlowOverlays.GlowOverlayCreators>(locator =>
            new Render.GlowOverlays.GlowOverlayCreators(locator.Require<Render.GlowOverlays.GlowOverlaySystem>()));
        _services.AddFactory<IExplosionFireball>(locator => locator.Require<Render.GlowOverlays.GlowOverlayCreators>());
        _services.AddFactory<IFxImpactOverlay>(locator => locator.Require<Render.GlowOverlays.GlowOverlayCreators>());
        _services.AddFactory<Render.GlowOverlays.GlowHost>(locator => new Render.GlowOverlays.GlowHost(
            locator.Require<Render.GlowOverlays.GlowOverlaySystem>(), locator.Require<Render.GlowOverlays.GlowSettings>()));
        // The client's pixel visibility (C7, c_pixel_visibility.cpp): the
        // system every glow asks (sprites, particle glows, beam halos), its
        // engine over IShimViewClient's slots, and c_pixel_visibility.cpp's
        // ConVars, declared in C# since that file left (building_cubemaps is
        // the engine's, watched). An IReloadParticipant: a reload carries the
        // level's materials.
        _services.AddFactory<Render.PixelVisibility.PixelVisSettings>(locator =>
        {
            var partial = _convars.Get<Render.PixelVisibility.PixelVisPartialConVar>()
                          ?? throw new InvalidOperationException("r_pixelvisibility_partial is not registered");
            var enabled = _convars.Get<Render.PixelVisibility.PixelVisEnabledConVar>()
                          ?? throw new InvalidOperationException("r_dopixelvisibility is not registered");
            var spew = _convars.Get<Render.PixelVisibility.PixelVisSpewConVar>()
                       ?? throw new InvalidOperationException("r_pixelvisibility_spew is not registered");
            var buildingCubemaps = locator.Require<Render.PixelVisibility.BuildingCubemapsWatch>();
            return new Render.PixelVisibility.PixelVisSettings(() => partial.Value, () => enabled.Value, () => spew.Value,
                                                               () => buildingCubemaps.Value);
        });
        _services.AddFactory<Render.PixelVisibility.ClientPixelVisEngine>(locator => new Render.PixelVisibility.ClientPixelVisEngine(
            locator.Require<IPixelVisSlots>(), locator.Require<IFxRender>(), locator.Require<IEngineWorldQuery>(),
            locator.Require<IFlashlightSlots>().DXSupportLevel, Log, locator.Require<IEngineConsole>().FindConVarValue));
        _services.AddFactory<Render.PixelVisibility.PixelVisibilitySystem>(locator =>
        {
            var engine = locator.Require<Render.PixelVisibility.ClientPixelVisEngine>();
            var settings = locator.Require<Render.PixelVisibility.PixelVisSettings>();
            var system = new Render.PixelVisibility.PixelVisibilitySystem(engine, engine,
                new Render.PixelVisibility.PixelVisFrame(locator.Require<IEngineEnvironment>()), settings);
            // r_drawpixelvisibility's change callback; a system built after a
            // change (a managed reload) starts where stock's would stand.
            var draw = _convars.Get<Render.PixelVisibility.DrawPixelVisibilityConVar>()
                       ?? throw new InvalidOperationException("r_drawpixelvisibility is not registered");
            system.ShowQueries(draw.Value);
            draw.Changed += system.ShowQueries;
            return system;
        });
        _services.AddFactory<IPixelVisibility>(locator => locator.Require<Render.PixelVisibility.PixelVisibilitySystem>());
        _services.AddFactory<IParticlePixelVisibility>(locator => new Render.PixelVisibility.ParticlePixelVisibility(locator.Require<IPixelVisibility>()));
        _services.AddFactory<IFxSpriteGlow>(locator => new Render.PixelVisibility.PixelVisSpriteGlow(locator.Require<IPixelVisibility>()));
        _services.AddFactory<Render.PixelVisibility.PixelVisHost>(locator =>
            new Render.PixelVisibility.PixelVisHost(locator.Require<Render.PixelVisibility.PixelVisibilitySystem>()));
        // The leaf system's registry and its decisions over it (C7 cluster 12
        // L2); the registry is an IReloadParticipant, so every renderable,
        // leaf and shadow crosses cs_cl_reload. Its native batches are the
        // forwarder library's LeafSystem table.
        _services.AddFactory<ILeafNative>(_ => (ILeafNative?)NativeLeafCalls.Acquire(_engine, out string why)
                                               ?? throw new InvalidOperationException("the client leaf system's native table: " + why));
        _services.AddFactory<Render.LeafSystem.LeafRegistry>(locator => new Render.LeafSystem.LeafRegistry(locator.Require<ILeafNative>()));
        _services.AddFactory<Render.LeafSystem.LeafListConVars>(locator =>
        {
            var leafConVars = new Render.LeafSystem.LeafListConVars(_convars,
                new Flashlight.WatchedConVarValues(locator.Require<IClientConVarSlots>(), locator.Require<IEngineConsole>().FindConVarValue));
            leafConVars.Start();
            _leafConVars = leafConVars;
            return leafConVars;
        });
        _services.AddFactory<Render.LeafSystem.RenderableListBuilder>(locator => new Render.LeafSystem.RenderableListBuilder(
            locator.Require<Render.LeafSystem.LeafRegistry>(), locator.Require<Render.LeafSystem.LeafListConVars>()));
        // The leaf system's cost (L3, cs_cl_leaf_stats): its calls and time, and
        // the native batches it asks.
        _services.AddFactory<Render.LeafSystem.LeafSystemStats>(locator => new Render.LeafSystem.LeafSystemStats
        {
            Outbound = (locator.Require<ILeafNative>() as NativeLeafCalls)?.Calls,
        });
        _services.AddFactory<IClientLeafSystemHost>(locator => new Render.LeafSystem.ClientLeafSystem(
            locator.Require<Render.LeafSystem.LeafRegistry>(), locator.Require<Render.LeafSystem.RenderableListBuilder>(),
            locator.Require<Render.LeafSystem.LeafSystemStats>()));
        _services.AddFactory<IClientRenderPlan>(locator => locator.Require<Render.RenderPlanHost>());
        _services.AddFactory<MaterialProxies.IMaterialProxyReadings>(
            locator => (MaterialProxies.MaterialProxyHost)locator.Require<IClientMaterialProxies>());
        // The world's light at a point, which the particle sims tint by.
        _services.AddInstance<Effects.IClientWorldLight>(new Effects.ClientWorldLight(AcquireEngineClient));
        // The particle query particles.a calls back (lane c5-particles).
        _services.AddInstance<Effects.IParticleQueryEngine>(new Effects.ParticleQueryEngine(AcquireEngineClient));
        _services.Add<IClientParticleQuery, Effects.ParticleSystemQuery>();
        _services.AddInstance<IClientReconnect>(new EngineReconnect(AcquireEngineClient, log));
        _services.AddInstance<ISteamAuthTickets>(new SteamUserTickets(log));

        // The client's Steam, through the process's own libsteam_api.so (C6, Steam
        // to managed): the context, and the name filter every displayed player
        // name goes through (stock's g_PR->GetPlayerName, ONE source for the UI).
        var steamApi = new SteamClientFlat();
        _services.AddInstance<ISteamClientApi>(steamApi);
        _services.AddInstance<Vgui.IPlayerNames>(new Vgui.EnginePlayerNames(AcquireEngineClient,
                                                                            new Steam.SteamPlayerNameFilter(steamApi)));

        // Beside the engine rather than down with the game services: the clock
        // forwards to the engine's own and is one of the same leaves.
        _services.AddInstance<IGameClock>(_clock);

        // This side's seed source for entity-owned random streams, seeded once
        // from the engine's uniform stream. One per side's container.
        _services.AddInstance<IRandomSeeds>(RandomSeeds.FromEngine(_services.Require<IEngineHost>()));

        // The module's counters and the per-client state the stage entry
        // drives, for the commands that report them (cs_status_client,
        // cs_cl_interp, cs_cl_teleport_*, cs_cl_policy). All three were statics
        // so that a command could reach them without a module reference.
        _services.AddInstance<IClientFrameStats>(_stats);

        // THE PROCESS' OWN COMMAND LINE, read once: the debug channel's opt-in
        // and host. A service rather than the static cache it was.
        _services.AddInstance<ILocalCommandLine>(new ProcessCommandLine());
        _services.AddInstance(_interpolation);
        _services.AddInstance(_renderPolicy);

        // The game events' own objects (GameEvents.cs), this side's and no
        // other's, created here because the scoreboard takes its disconnect
        // event through its constructor. The native subscription stays where it
        // was (_gameEvents.RegisterAll, below). No engine call.
        GameEventRegistry.AddEventsTo(_services, Game, Log);

        // And the user message handlers, likewise this side's own, built by the
        // container so each can take what it needs: the layout check its
        // registry, the soundscape push the engine's sound area. The HUD takes
        // its four through its constructor below. The wire indices are still
        // fixed by _userMessages.RegisterAll, in the DLL-init window, which
        // reuses these instances. No engine call.
        _services.AddInstance(_layouts);

        // The client's own protocol-list hashes, which the layout check compares
        // with the server's (plan_side_isolation §2.1).
        _services.AddInstance(ProtocolManifest.Of(Game, Log));

        // plan_entity.md step 8e-sound -- WHERE A SOUNDSCAPE PUSH LANDS, handed
        // to the Soundscape handler's constructor. Registered from HERE, the
        // client assembly, because the client soundscape system is this side's
        // and the shared assembly the handler lives in must not lean on it.
        // Since sound cluster 17 that system is managed (ClientSoundscapes), so
        // the push lands without crossing; it is built below, after the convars.
        _services.AddInstance<SoundscapeMessage.SoundscapeSink>(
            (index, entIndex, bits, positions) => _soundscapes.Push(index, entIndex, bits, positions));

        // What the stock user-message hooks did to native client state (the
        // view effects, rumble, the viewport, the voice status), carried out
        // over client-only slots for the shared handlers.
        // view_effects.cpp's shakes and fades (C7): the Shake and Fade messages
        // reach them here; the native callers of vieweffects through
        // IManagedRenderClient. CalcShake's rumble angle goes to the mixer.
        _services.AddFactory<Render.ViewEffectsHost>(locator => new Render.ViewEffectsHost(
            locator.Require<IUniformRandom>(), locator.Require<IGameClock>(), locator.Require<Render.ShakeShowConVar>(),
            AcquireEngineClient, shake => _inputService?.RumbleScreenShake(shake, 0f)));
        _services.AddInstance<IClientMessageEffects>(new ClientMessageEffects(_services.Require<IEngineHud>(), () => _uiLoader?.Current, () => _voiceStatus,
                                                                             m => _inputService?.RumbleEffect(m.Waveform, m.Data, m.Flags),
                                                                             () => _services.Require<Render.ViewEffectsHost>(),
                                                                             _services.Require<IGameClock>()));

        UserMessageRegistry.AddHandlersTo(_services, Game, Log);
        _invites = _services.Require<DebugChannelInviteMessage>();

        // THE INTERPOLATION LIST AND THE CLOCK, TAKEN OUT OF THE CONTAINER ONCE
        // (plan_entity.md step 6a). Both are read twice a frame from OnStage,
        // which is a stage notification and not a place to resolve a service.
        _interpolationBlocks = _services.Require<IEngineEntities>().InterpolationBlocks;

        // AND THE RENDER-POLICY LIST (step 6b), for the same reason and out of
        // the same area.
        _renderPolicyBlocks = _services.Require<IEngineEntities>().RenderPolicyBlocks;

        // AND THE MODEL-CACHE WALK -- lane diag-visibility. Held rather than
        // resolved at the call, for the reason the two above are: a flush
        // arrives from the engine's own thread and a service lookup is not what
        // that moment is for. Settable by a test through the internal field, so
        // "a flush asks the walk exactly once" is a fact and not an inspection.
        _mdlCacheWalk = _services.Require<IEngineEntities>();
        // THE REPLAY'S THREE ASKS (plan_entity.md step 6f), out of the same
        // area and for the same reason: they are read once per replayed command
        // inside a prediction frame, which is not a place to resolve a service.
        // Registered so that ClientPlayerPrediction can take it in a
        // constructor and a test can hand it a double instead: by
        // ClientEntityFetches, before the areas, as the same object
        // IEngineEntities.ClientReplay answers.

        // The size tiers native registered. An instance rather than a static
        // cache, so two loads cannot share one table and no test can change it
        // for every other.
        _services.AddInstance<INetTiers>(_services.Create<NetTiers>());

        // What this side's publish path could not put on the wire.
        _services.AddInstance<INetTypeRefusals>(new NetTypeRefusals());

        // Built THROUGH the container, so they are handed the same engine every
        // drawn entity gets — and built HERE rather than with the registrations
        // below, because Subscribe is called on the HUD forty lines before that
        // point and found a null when this sat lower down.
        // The view the HUD's debug line reads and the prediction writes.
        _services.AddInstance(new ViewReadout());

        // The frame times over the last second, which Frame feeds and the dev
        // API's frame route reads (plan_client_novalve §4.4).
        _frameTimes = new FrameTimeWindow(TimeProvider.System);
        _services.AddInstance(_frameTimes);

        // What this composition serves its dev API, one service per group,
        // built by the container: WireDebugChannel hands the hooks over.
        _services.AddInstance(_engine.Crossings);
        _services.AddInstance<IDebugClientLog>(new ClientDebugLog(log));
        _services.Add<IDebugClientBuild, ClientDebugBuild>();
        _services.Add<IDebugClientPrediction, ClientDebugPrediction>();
        _services.Add<IDebugClientFrame, ClientDebugFrame>();
        _services.Add<IDebugClientEntities, ClientDebugEntities>();
        _services.AddInstance(new ClientNetSchemaTypes(_services.Require<INetTiers>(),
                                                       () => StandaloneNetStates.Discover(Game, Log)));
        _services.Add<IDebugClientNetTables, ClientDebugNetTables>();
        _services.Add<IDebugClientHooks, DebugClientHooks>();

        // The service tier of the client reload (plan_client_novalve §3.8):
        // every IReloadParticipant in this container, walked by ReloadSave and
        // ReloadRestore.
        _reloadParticipants = new ClientReloadParticipants(_services, _engine.Reload, log);
        _services.AddInstance(_reloadParticipants);

        // The reload's own record and the leak arm, and the dev API's request
        // for the next one (plan_client_novalve §3.8, §4.4).
        _reloadLedger = new ClientReloadLedger(_engine.Reload, _clock, log);
        _services.AddInstance(_reloadLedger);
        _services.AddInstance(new ClientReloadRequester(AcquireEngineClient));
        _services.Add<IDebugClientReload, ClientDebugReload>();
        _services.Add<IDebugClientNetGraph, ClientDebugNetGraph>();
        _services.Add<IDebugClientUi, Vgui.UiDebugForwarder>();
        _services.Add<IDebugClientParticles, ClientDebugParticles>();
        _services.Add<IDebugClientAbsences, ClientDebugAbsences>();
        _services.Add<IDebugClientProxies, ClientDebugProxies>();
        _services.Add<IDebugClientViewCamera, ClientDebugViewCamera>();
        _services.Add<IDebugClientRender, ClientDebugRender>();

        _hud = _services.Create<Hud>();
        _lights = _services.Create<Standalone.DynamicLights>();
        _winds = _services.Create<Effects.ClientWinds>();
        // The ownerless particle effects (lane c5-particles): the managed half of
        // CParticleMgr the fx ports start their emitters on; an IReloadParticipant.
        _services.AddInstance(new Effects.ParticleEffectKinds());
        _particleEffects = _services.Create<Effects.ClientParticleEffects>();
        _services.AddInstance(_particleEffects);
        _flecks = _services.Create<Effects.FleckSystems>();
        _services.AddInstance(_flecks);
        _sounds = _services.Create<ClientSoundController>();
        _corrections = _services.Create<Standalone.ColorCorrections>();

        // The round's tally. An instance the module owns rather than a static,
        // so two loads cannot share one board.
        _scoreboard = _services.Create<Scoreboard>();
        _services.AddInstance<IScoreboard>(_scoreboard);

        int v = _convars.RegisterAll(typeof(ClientModule).Assembly);
        _convars.PublishTo(_services);

        // THE DETAIL PROPS (C7 cluster 10): the lump's parse, the fades, the
        // sorts and the quads are managed; native's client system
        // (shell_detail_props.cpp) asks at stock's times
        // (IManagedDetailPropsClient) and carries the answers out. After the
        // convars, which the system reads and the level restores.
        _services.AddInstance<Detail.IClientDetailProps>(new Detail.DetailPropsHost(
            new Detail.DetailPropSystem(Detail.DetailPropsEngines.For(_engine, Log), _services.Create<Detail.DetailConVars>())));

        // THE SOUNDSCAPES (sound cluster 17, lane c2-sims): the system is
        // managed; native's client-system shell drives it at stock's times
        // (IManagedSoundscapesClient) and carries out its engine calls. After
        // the convars, for soundscape_fadetime.
        _soundscapes = _services.Create<Soundscapes.ClientSoundscapes>();
        _services.AddInstance(_soundscapes);
        _soundscapeDriver = _services.Create<Soundscapes.ClientSoundscapeDriver>();
        _services.AddInstance(_soundscapeDriver);

        // THE ROPES (plan_client_novalve §2 cluster 6): the simulation and the
        // build are managed, the native executor draws. After the convars,
        // which the system reads, and before the temp entity players, because
        // the effect dispatch asks for the managed client effects and
        // "ShakeRopes" is the ropes'.
        _services.AddInstance(_services.Create<Ropes.RopeConVars>());
        _ropes = _services.Create<Ropes.ClientRopeSystem>();
        _services.AddInstance<Ropes.IClientRopes>(_ropes);
        // The managed client effects (IClientEffectHandlers) are registered
        // with the effect dispatch, below, once the effect services they use
        // exist; ShakeRopes is the ropes'.

        // THE BEAMS (§2 cluster 5): the bookkeeping and the builders are
        // managed; native's `beams` forwards here (IManagedBeamsClient) and the
        // executor draws. Before the temp entity players, which create them.
        _beams = _services.Create<Beams.ClientBeamSystem>();
        _services.AddInstance(_beams);
        _services.AddInstance<Beams.IClientBeams>(_beams);

        // THE SECOND CAMERA (lane camproto): what a HUD panel shows, decided
        // here and drawn by the native executor at the frame's render.
        _viewCamera = _services.Create<ViewCamera.ViewCameraService>();
        _services.AddInstance(_viewCamera);
        _services.AddInstance<ViewCamera.IViewCameraService>(_viewCamera);
        _services.AddInstance<Ui.IViewCameraSource>(_viewCamera);

        // The HUD is built above, before any convar exists, so its corner stamp
        // is handed the toggle here rather than taking it in a constructor. The
        // delegate it replaces answers "draw it", so a wiring nobody did leaves
        // the stamp ON — the safe direction for something whose whole purpose is
        // to be on screen at the moment nobody thought to ask for it.
        var buildStamp = _convars.Get<BuildStampConVar>();
        if (buildStamp is not null)
            _hud.Build.Visible = () => buildStamp.Value;

        // cs_cl_player_predict, read once a frame by the replay. Held as a
        // reference rather than resolved per frame, and the prediction defaults
        // to ON when the convar is missing — the same "the safe direction is
        // the one a wiring nobody did leaves you in" the stamp above uses, and
        // here the safe direction is the game feeling like a game.
        _predictConVar = _convars.Get<PlayerPredictConVar>();

        // vcollide_wireframe, whose whole effect is its change callback: the
        // toggle draws nothing by itself, it makes every client shell
        // re-evaluate whether the leaf system should be showing it. Handed the
        // sweep here for the build stamp's reason -- a [ConVar] class is
        // constructed with no arguments, because the registry registers the
        // convars before the services exist -- and reaching the engine area
        // through the container rather than through a static is what makes the
        // convar testable at all.
        var wireframe = _convars.Get<VCollideWireframeConVar>();
        if (wireframe is not null)
        {
            var entities = _services.Require<IEngineEntities>();
            wireframe.Sweep = entities.UpdateVisibilityAll;
        }

        // The mod's services: every [SideService(Side.Client)] class and client
        // registrar in the loaded assemblies (mods.yaml lists them), after the
        // core's registrations so they can inject them and never replace one.
        // Then the handler pass again: the first one (above) skips a handler
        // whose constructor wants a mod service that did not exist yet, and
        // RegisterAll below hooks only handlers the container holds.
        _modServices = SideServices.Register(_services, Side.Client, LoadedAssemblies.Types(typeof(ClientModule).Assembly, Log));
        if (_modServices.Count > 0)
        {
            UserMessageRegistry.AddHandlersTo(_services, Game, Log);
            Log.Msg($"[SourceSharp] registered {_modServices.Count} mod service contract(s)\n");
        }

        // THIS SIDE's weapon tables, registered right after the mod services so
        // their lazy source-ask can see what the mods registered — and before
        // the native ammo/weapon registration below, which is what carries the
        // MOD's rows to the native fire-rate table. They must be built from the
        // SAME assembly set the server did: the networked WeaponTypeId is a
        // position in this list, and a row present on one side and not the
        // other is a mispredicted shot, not a refused give. The tables report
        // refused rows loudly through the log.
        _services.AddInstance(new WeaponCatalog(() => _services.ResolveAll<IWeaponTypeSource>(), Log));
        _services.AddInstance(new AmmoCatalog(() => _services.ResolveAll<IAmmoTypeSource>(), Log));
        _services.AddInstance(new WeaponBehaviours(() => _services.ResolveAll<IWeaponBehaviourSource>(), Log));

        // The animation layer chain's composition, the server's twin: the
        // mods' IAnimStateLayer services and the core's HL2 layer, so the
        // predicted body asks the layers the server's copy asked.
        _services.AddInstance(new SourceSharp.Shared.Anim.AnimStateLayers(
            () => _services.ResolveAll<SourceSharp.Shared.Anim.IAnimStateLayer>()));

        // The index a message travels under is its position in this list, so
        // both sides must derive the same sequence from the same metadata —
        // which is what naming the SHARED assembly here says. Discovery still
        // reads every loaded assembly, and warns about a message declared
        // anywhere this side alone can see. The client is the only side that
        // also hooks them for dispatch.
        _userMessages.RegisterAll(Game, _services);

        // plan_entity.md step 8e-sound: the server decides which soundscape a
        // player is in and sends the `Soundscape` message to that one player;
        // the handler hands the decoded numbers to ClientSoundscapes.Push (stock's
        // Soundscape_Update; one ABI slot away until sound cluster 17). That used to be a static delegate
        // installed here, because a handler was built with no constructor
        // arguments; the container builds it now (AddHandlersTo, above) over
        // the sink registered beside it. Never torn down: a soundscape push
        // arriving during a level load has to reach the same place a later one
        // does.

        // Game events, also from the shared assembly. Order carries no meaning
        // here — the engine dispatches by name — so this is a plain
        // subscription rather than a protocol like the user message list.
        _gameEvents.RegisterAll(Game, _services);

        // The SAME [StringTable] declarations the server registers, from the
        // same shared assembly, and WRITES:FALSE -- the owner's ruling: the
        // server only creates and the client only finds. There is no create
        // pass on this side and no moment one could run in; a table arrives
        // when it REPLICATES, so what is registered here is a name and every
        // use finds by it. That makes the client's find lazy by construction
        // rather than by a lazy wrapper, and a use before the table has
        // arrived is refused and says so once -- the next one succeeds.
        _stringTables = new StringTableRegistry(_services, writes: false);
        _stringTables.RegisterAll(Game, _services);

        // The client's user-message dispatch (plan_side_isolation §2.2): ids map
        // to keys through the server's SsUserMessages table, registered just
        // above, so it is built here; native first reaches it through the
        // IManagedUserMessagesClient binding, which is acquired after this load.
        _userMessageReceiver = new UserMessageReceiver(_userMessages, _services.Require<UserMessageTable>(), Log);

        // The same ammo table the server builds, from the same shared list. A
        // weapon's ammo type arrives as an index into it, so a client that
        // skipped this resolves every one of them to nothing.
        // The same tables the arsenal and the prediction machine read — which
        // is the point: the native fire-rate/ammo table must hold the MOD's
        // rows in the same index order, or a mod weapon fires at the shell's
        // default rate and its ammo resolves to nothing. Building them here is
        // what freezes the table; the first read wins (see WeaponCatalog).
        RegisterAmmo(_services.Require<AmmoCatalog>(), _services.Require<IEngineWeapons>());
        // The weapon catalogue's first read, for the same reason (its native
        // fire-rate table went in plan_server_native_v2 T2: nothing read it).
        _ = _services.Require<WeaponCatalog>().Count;

        // THE MOVEMENT CONSTANTS, resolved once now the mods are in (phase
        // 0.3), the same way the server asks: zero registrations answer the
        // core's stock table, one is that mod's, two is a startup failure
        // naming both. Prediction is the reason this is not merely tidy —
        // both sides must register the SAME parameters and answer the three
        // scales with the SAME rules, or the replay diverges from the server
        // with nothing but rubber-banding to show for it.
        _movementRules = _services.Create<MovementRulesResolver>();
        _services.AddInstance(_movementRules);
        Movement.Register("Client", _movementRules.Constants,
                          _services.Require<IEnginePlayerMovement>(), Log);

        // The replay's loop takes its decisions from the runner (see
        // MovementRunner.ClientLoop), so the client's own hook answers its
        // predicted commands too — frozen here, before any command runs.
        _movement.ReplayRules = _movementRules.Rules;

        // The collision matrix, the SAME one the server sends. ShouldCollide is
        // asked on this side too -- util_shared.cpp is shared and the trace
        // filter runs during prediction -- so if the client answered from the
        // native defaults while the server answered from C#, a change to
        // CollisionRules would desynchronise prediction with nothing to say so.
        //
        // It used not to arrive: the slot was server-only, so this call was a
        // silent no-op and the client's table was equal to the matrix by
        // coincidence alone. Now the log says which, on both sides.
        if (CollisionRules.Register(_services.Require<IEngineCollisionRules>()))
            Log.Msg($"[SourceSharp/Client] collision matrix delivered (rows 0x{CollisionRules.RowHash(CollisionRules.BuildMatrix()):X8})\n");
        else
            Log.Warning("[SourceSharp/Client] collision matrix NOT delivered: this side does not serve RegisterCollisionRules\n");

        int standalone = _standalones.RegisterAll(typeof(ClientModule).Assembly);
        Log.Msg($"[SourceSharp/Client] registered {standalone} standalone entity class(es)\n");

        // The same list the server publishes, from the same shared declaration
        // and in the same order. The two tables pair only because both come
        // from here — nothing on this side reads the server's.
        // A class with no client half draws nothing and decodes nothing, so it
        // has no use for a table here — and registering one the server has not
        // is the mismatch that makes a join hang with nothing in any log.
        int netStates = StandaloneNetStates.PublishAll(
            _services.Require<IEngineNetworking>(), "Client", Game, _standalones.IsRegistered,
            _standalones.ClassNames, _services.Require<INetTypeRefusals>(), Log);
        Log.Msg($"[SourceSharp/Client] {netStates} hierarchy class(es) network state of their own\n");

        // AND worldspawn, from the SAME declaration and in the SAME window, for
        // the same reason the server publishes it (plan_gamedll_after.md Part
        // B2). The two tables pair only because both come from the shared
        // assembly; nothing on this side reads the server's.
        WorldNet.Publish(_services.Require<IEngineNetworking>(), "Client", _services.Require<INetTypeRefusals>(), Log);

        // TEMP ENTITIES, from the SAME shared declarations the server registers.
        // Inside SourceSharp_ClientEarlyInit's window and therefore before
        // gHUD.Init(), which is the point of that call existing: a ClientClass
        // built after the engine has walked g_pClientClassHead is not in the
        // list the handshake negotiates, and the first event of that class then
        // resolves to nothing with no line anywhere (plan 2D).
        //
        // BEFORE the players below, because a player for a class native refused
        // is a player nothing can ever route to, and the refusal line has to be
        // on the console above it.
        //
        // THE TABLES ARE PLANNED HERE AND STORED BY NATIVE (the client half of
        // plan_server_native_v2 T4b): ClientTempEntEngine decides every
        // RecvProp, and IShimTempEntBackendsClient::TempEntBuildClient builds
        // the RecvTable, the ClientClass and the receiving shell from its block.
        var tempEntEngine = new TempEnts.ClientTempEntEngine(new ClientTempEntTables(_engine), Log);
        int tempEnts = _tempEnts.RegisterAll("Client", Game, tempEntEngine);
        _services.AddInstance(_tempEnts);

        // THE STANDALONE REGISTRY BEFORE THE POOL, and this is why it moved up
        // from where the other services are registered: the pool's collision
        // arm asks it what a trace HIT, by entity index, which is behaviour
        // ledger row C4's two dynamic_casts asked on this side. A pool built
        // before the registry was in the container would have got a null and
        // answered "unknown" for every entity in the map.
        //
        // Nothing is built out of the registry here -- an instance is not made
        // until a networked entity arrives, which is long after this -- so
        // moving it earlier costs nothing.
        _standalones.Services = _services;
        _services.AddInstance<IClientStandalones>(_standalones);

        // THE CLIENT'S SKELETONS (plan physics_bones_native_zero 1.2, client
        // half): posed in managed code from the engine's model cache, copied
        // as bytes. A reload participant (the merge sources, scales, poses,
        // layers and clocks C# set cross cs_cl_reload).
        IVtablePhysicsSurfaceProps? surfaceProps = PhysicsSurfaceProps.Acquire(_engine, out string surfaceWhy);
        if (surfaceProps is null)
            Log.Warning($"[SourceSharp/Client] no VPhysics surface table for the hitbox trace: {surfaceWhy}\n");
        _skeletons = new ClientSkeletons(new ClientStandaloneBlocks(_engine), new ClientSequenceStore(_engine),
                                         new SourceSharp.Shared.Bones.StudioModels(_engine.StudioMemory()),
                                         index => _standalones.GetByIndex(index)?.Handle,
                                         _services.Require<IEngineEnvironment>(), Log,
                                         _convars.Get<ClAnimThreeWayBlendConVar>(), surfaceProps,
                                         () => _ragdolls);
        _services.AddInstance(_skeletons);

        // The skeletons' batch table across cs_cl_reload: native may hold its
        // address past the reload, so the next build adopts it rather than
        // this one leaving it behind (a section of its own).
        _services.AddInstance(new ClientBoneBatchCarry(_skeletons));

        // The model descriptors are read from the same studio headers
        // (studio.cpp's eviction): sequences, events, attachments and each
        // sequence's default duration.
        _descriptorAttachment = _engine.Models.AttachSource(new SourceSharp.Shared.Bones.StudioModelDescriptor(_skeletons.Models));

        // THE CLIENT'S RAGDOLLS (plan physics_bones_native_zero Phase 3):
        // ragdoll_shared.cpp, CRagdoll and ragdoll_library.cpp's table in
        // managed code, calling vphysics through its vtables. A reload
        // participant (the bodies' objects outlive cs_cl_reload). Posed from the
        // skeleton above; its two key-handler tables are built with the root.
        _ragdollPhysics = RagdollVPhysics.Acquire(_engine, _services.Require<IClientPhysicsEngine>(), out string ragdollWhy);
        if (_ragdollPhysics is null)
        {
            Log.Warning($"[SourceSharp/Client] no vphysics for the ragdolls ({ragdollWhy}); no entity becomes a body\n");
            _services.AddInstance<Ragdolls.IClientRagdolls>(new Ragdolls.NoClientRagdolls());
        }
        else
        {
            var ragdollWorld = new Ragdolls.ClientRagdollWorld(_services.Require<IEngineWorldQuery>(),
                                                               _services.Require<IEngineEntities>(),
                                                               _services.Require<IEngineConsole>(),
                                                               _convars.Get<Ragdolls.RagdollSleepAfterTimeConVar>()
                                                               ?? throw new InvalidOperationException("ragdoll_sleepaftertime is not registered"));
            var ragdollBlocks = new ClientStandaloneBlocks(_engine);
            _ragdolls = new Ragdolls.ClientRagdolls(
                _ragdollPhysics, new Ragdolls.RagdollSolver(_ragdollPhysics, Log, () => ragdollWorld.Developer),
                new Ragdolls.ClientRagdollPoses(_skeletons, ragdollBlocks),
                new Ragdolls.ClientRagdollTails(ragdollBlocks, new StandaloneRagdollStore(_engine)),
                new Ragdolls.ClientRagdollClock(_services.Require<IEngineEnvironment>()), ragdollWorld,
                new Ragdolls.ClientRagdollNames(), Log);
            _services.AddInstance<Ragdolls.IClientRagdolls>(_ragdolls);
        }

        // An entity's skeleton and body go with it.
        _standalones.RemovalForget = entity =>
        {
            _skeletons.Forget(entity);
            _ragdolls?.Destroy(entity);
        };

        // THE FLIP: every bone question the engine bindings answer -- the
        // attachments, the matrices, the sequence and its clock, the layers,
        // the pose parameters, the merge source, the scale -- is the managed
        // skeleton's from here; native is asked only for the scale's and the
        // clock's stores, and asks IManagedBonesClient itself when the engine
        // calls SetupBones, TestHitboxes or an attachment.
        _skeletonRoute.Skeletons = _skeletons;

        // The client-side effect list, its factory and the tracer effects. The
        // list is a reload participant (its contract is its own type): live
        // effects cross a managed reload mid-life.
        var fxRender = _services.Require<IFxRender>();
        var fxConsole = _services.Require<IEngineConsole>();
        var tracerExtra = _convars.Get<TracerExtraConVar>();
        var drawTracers = _convars.Get<DrawTracersConVar>();
        var effectList = new Effects.ClientEffectList(fxRender, fxConsole) { TracerExtra = () => tracerExtra?.Value ?? true };
        _services.AddInstance(effectList);
        var effectFactory = new Effects.ClientSideEffectFactory(fxRender, _services.Require<IFxTracerSound>(), effectList,
                                                                _services.Require<IEngineHost>(),
                                                                () => tracerExtra?.Value ?? true);
        _services.AddInstance(effectFactory);
        var tracers = new Effects.TracerSupport(fxRender, fxConsole, () => drawTracers?.Value ?? true);
        _services.AddInstance(tracers);
        _effectListHost = new Effects.ClientEffectListHost(effectList, effectFactory);
        // fx_sparks.cpp's routines (lane c5-particles): IEffects' sparks, metal
        // sparks and energy splashes end here, on the ownerless effects.
        _services.AddInstance<Effects.IFxQuadSink>(effectFactory);
        _services.AddInstance<Effects.IDrawMetalSpark>(_convars.Get<DrawMetalSparkConVar>()
                                                       ?? throw new InvalidOperationException("fx_drawmetalspark is not registered"));
        _services.AddInstance<Effects.ISparkEnvironment>(_services.Create<Effects.SparkEnvironment>());
        _sparks = _services.Create<Effects.SparkEffects>();
        _services.AddInstance(_sparks);
        _services.AddInstance<Effects.IClientSparks>(_sparks);
        _services.AddInstance<IClientImpactSparks>(_sparks);
        // fx_water.cpp's routines (lane c5-particles): the splashes and the
        // ripple, behind the watersplash / gunshotsplash / waterripple effects.
        _services.AddInstance<Effects.ISplashEnvironment>(_services.Create<Effects.SplashEnvironment>());
        var waterSplashes = _services.Create<Effects.WaterSplashes>();
        _services.AddInstance(waterSplashes);
        _services.AddInstance<IClientWaterSplashes>(waterSplashes);
        // fx_blood.cpp's routines (lane c5-particles), behind the bloodspray,
        // BloodImpact and HunterDamage effects.
        _services.AddInstance<Effects.IBloodEnvironment>(_services.Create<Effects.BloodEnvironment>());
        var blood = _services.Create<Effects.BloodEffects>();
        _services.AddInstance(blood);
        // fx_explosion.cpp's routines (lane c5-particles), behind the explosion
        // temp entity and the Explosion / WaterSurfaceExplosion /
        // HelicopterMegaBomb effects.
        _services.AddInstance<Effects.IFxLineSink>(effectFactory);
        _services.AddInstance<Effects.IExplosionEnvironment>(_services.Create<Effects.ExplosionEnvironment>());
        var explosions = _services.Create<Effects.ExplosionEffects>();
        _services.AddInstance(explosions);
        _services.AddInstance<Effects.IClientExplosions>(explosions);
        // fx.cpp's routines (lane c5-particles): IEffects' muzzle flashes, smoke and
        // ricochet, the temp entities' attached flash, and the Smoke, GunshipImpact,
        // CommandPointer and MuzzleFlash effects. The flashes before the handlers.
        // The routines' sound and overlay, before the shards and the routines that use them.
        _services.AddInstance<Effects.IFxRoutineEnvironment>(_services.Create<Effects.FxRoutineEnvironment>());
        // The impact sprays (lane c5-particles): FX_DebrisFlecks (the flecks and their
        // dust), FX_DustImpact, and FX_Blood for the blood sprite temp entity.
        var sprays = _services.Create<Effects.ImpactSprays>();
        _services.AddInstance(sprays);
        _services.AddInstance<IClientImpactSprays>(sprays);
        _services.AddInstance<Effects.IImpactBlood>(sprays);
        // FX_Dust, the dust puff (lane c5-particles): IEffects::Dust and the dust temp entity.
        var puffs = _services.Create<Effects.ClientDustPuffs>();
        _services.AddInstance(puffs);
        _services.AddInstance<Effects.IClientDustPuff>(puffs);
        // CTEParticleRenderer's two effects, the blood stream and the debug line (lane c5-particles).
        var teParticles = _services.Create<Effects.ClientTeParticles>();
        _services.AddInstance(teParticles);
        _services.AddInstance<Effects.IClientTeParticles>(teParticles);
        // The large funnel temp entity's sprites (lane c5-particles).
        _services.AddInstance<Effects.ILargeFunnel>(_services.Create<Effects.LargeFunnel>());
        // The shards (lane c5-particles): FX_GlassImpact and the shatter temp entity on Simple3D.
        var shards = _services.Create<Effects.ShardEffects>();
        _services.AddInstance(shards);
        _services.AddInstance<TempEnts.IClientShatter>(shards);
        // The temp entities' muzzle-flash emitters on the managed executors (lane
        // c5-particles), before the routines and flashes that fill them.
        var poolEmitters = _services.Create<Effects.PoolEmitters>();
        _services.AddInstance(poolEmitters);
        _services.AddInstance<IFxEmitters>(poolEmitters);
        _services.AddInstance<Effects.IFxDiscreetLineSink>(effectFactory);
        var fxRoutines = _services.Create<Effects.FxEffects>();
        _services.AddInstance(fxRoutines);
        _services.AddInstance<Effects.IFxRoutines>(fxRoutines);
        _services.AddInstance<TempEnts.IMuzzleFlashLight>(_convars.Get<MuzzleFlashLightConVar>()
                                                           ?? throw new InvalidOperationException("muzzleflash_light is not registered"));
        var muzzleFlashes = _services.Create<TempEnts.MuzzleFlashes>();
        _services.AddInstance(muzzleFlashes);
        _services.AddInstance<TempEnts.ILegacyMuzzleFlashes>(muzzleFlashes);
        var spriteRenderer = _services.Create<Effects.SpriteRenderer>();
        _services.AddInstance(spriteRenderer);
        _spriteHost = new Effects.ClientSpriteHost(spriteRenderer, _services.Require<IFxSpriteGlow>(),
                                                   _services.Require<IFxSpriteDraw>());
        _services.AddInstance(_spriteHost);
        _services.AddInstance<Effects.IClientSpriteDraw>(_spriteHost);
        _spriteTrails = _services.Create<Effects.ClientSpriteTrails>();
        _services.AddInstance(_spriteTrails);
        _effectPrecache = _services.Create<Effects.ClientEffectPrecache>();
        _services.AddInstance(_effectPrecache);
        _smokeFog = new Effects.SmokeFogOverlay();
        _services.AddInstance(_smokeFog);

        // THE MANAGED CLIENT EFFECTS: a DECLARE_CLIENT_EFFECT that moved with
        // its subsystem, asked by the dispatch before the native registrations.
        var effectHandlers = new ClientEffectHandlers(
        [
            new Ropes.ShakeRopesEffect(_ropes),
            new Effects.TracerEffect(tracers, effectFactory),
            new Effects.ParticleTracerEffect(tracers, effectFactory, fxRender),
            new Effects.TracerSoundEffect(tracers, effectFactory),
            new Effects.PistolShellEjectEffect(fxRender),
            new Effects.RifleShellEjectEffect(fxRender),
            new Effects.ShotgunShellEjectEffect(fxRender),
            // The ParticleEffect / ParticleEffectStop client effects (lane
            // c5-particles): stock's c_particle_system.cpp handlers, absent until now.
            new Effects.ParticleEffectHandler(_services.Require<IClientParticleOneShots>(),
                                              _services.Require<IEngineEffects>(), _standalones),
            new Effects.ParticleEffectStopHandler(_services.Require<IEngineEffects>(), _standalones),
            // fx_water.cpp's three client effects (lane c5-particles).
            new Effects.WaterSplashEffect(waterSplashes, "watersplash"),
            new Effects.WaterSplashEffect(waterSplashes, "gunshotsplash"),
            new Effects.WaterRippleEffect(waterSplashes),
            // fx_blood.cpp's three client effects (lane c5-particles).
            new Effects.BloodSprayEffect(blood),
            new Effects.BloodImpactEffect(blood),
            new Effects.HunterDamageEffect(blood),
            // fx_explosion.cpp's three client effects (lane c5-particles).
            new Effects.ExplosionEffect(explosions, "Explosion", Effects.ExplosionKind.Base),
            new Effects.ExplosionEffect(explosions, "WaterSurfaceExplosion", Effects.ExplosionKind.Water),
            new Effects.ExplosionEffect(explosions, "HelicopterMegaBomb", Effects.ExplosionKind.MegaBomb),
            // fx.cpp's client effects (lane c5-particles); TeslaHitboxes and TeslaZap
            // draw nothing here, as their armed stock bodies did.
            new Effects.MuzzleFlashEffect(muzzleFlashes, _standalones),
            new Effects.SmokeEffect(fxRoutines),
            new Effects.GunshipImpactEffect(fxRoutines),
            new Effects.CommandPointerEffect(fxRoutines),
            // The RPG shot down's flash and sound (C7 glow overlays).
            new Render.GlowOverlays.RpgShotDownEffect(_services.Require<IFxGameSounds>(),
                                                      _services.Require<Render.GlowOverlays.GlowOverlayCreators>()),
            new Effects.EmptyEffect("TeslaHitboxes"),
            new Effects.EmptyEffect("TeslaZap"),
            // fx_impact.cpp's GlassImpact (lane c5-particles: ShardEffects).
            new Effects.GlassImpactEffect(shards),
            // fx_sparks.cpp's ManhackSparks (lane c5-particles: SparkEffects.SparkFan).
            new Effects.ManhackSparksEffect(_sparks),
        ]);
        _services.AddInstance<IClientEffectHandlers>(effectHandlers);

        // THE EFFECT DISPATCH TABLE BEFORE THE PLAYERS: CTEEffectDispatch's
        // player asks for it in its constructor.
        _effectDispatch = _services.Create<Effects.ClientEffectDispatch>();
        _services.AddInstance(_effectDispatch);
        _services.AddInstance<IClientEffectDispatch>(_effectDispatch);

        _clientEffects = _services.Create<Effects.ClientEffects>();
        _services.AddInstance(_clientEffects);
        _services.AddInstance<IClientEffects>(_clientEffects);

        // The bounce sounds before the frame, which plays them at a collision.
        var tempEntSounds = _services.Create<TempEnts.TempEntSounds>();
        _services.AddInstance(tempEntSounds);
        _services.AddInstance<TempEnts.ITempEntBounceSounds>(tempEntSounds);

        // C_LocalTempEntity::Frame, which is C# now. Before the pool, because
        // the pool holds it.
        _tempEntFrame = _services.Create<TempEnts.ClientTempEntFrame>();
        _services.AddInstance<TempEnts.IClientTempEntSimulation>(_tempEntFrame);

        // THE POOL BEFORE THE PLAYERS, because a player that drives it --
        // ClientTeKillPlayerAttachments is the first -- asks the container for
        // it while it is being built.
        _tempEntPool = _services.Create<TempEnts.ClientTempEntPool>();
        _services.AddInstance(_tempEntPool);

        // AND THE SPAWN HELPERS AFTER IT, for the same reason one line up: they
        // hold the pool, and seven of the ten C-Phase 5 players hold them.
        //
        // A separate object rather than more of ClientTempEntPool, because the
        // two are about different things -- the pool is slots, eviction and
        // expiry, and a spawn helper is what KIND of thing a slot holds. Stock
        // put both on CTempEnts and the file is four thousand lines;
        // docs/te-legacy-pool-survey.md's first finding was that only a third
        // of it is the pool.
        _tempEntSpawns = _services.Create<TempEnts.TempEntSpawns>();
        _services.AddInstance(_tempEntSpawns);

        // THE LEGACY TEMPENTS' DECISIONS (lane c2-fx): c_te_legacytempents.cpp
        // left client.so and its thunk asks these. The breakmodel groups hang
        // off the pool, which frees and clears them with their slots.
        var breakGroups = new TempEnts.TempEntBreakGroups();
        _tempEntPool.BreakGroups = breakGroups;
        _services.AddInstance(breakGroups);

        // The clientside physics props (CTEPhysicsProp's C_PhysPropClientside):
        // a pool slot each, moved by its vphysics object, made through the
        // ragdolls' vphysics calls. None without them; the player says so.
        if (_ragdollPhysics is not null)
        {
            var propEnvironment = _services.Require<IEngineEnvironment>();
            var physicsProps = new TempEnts.ClientPhysicsProps(
                _ragdollPhysics, new TempEnts.PoolPhysicsPropSlots(_tempEntPool), _services.Require<IClientModelNames>(),
                _services.Require<BreakModels>(), _services.Require<IGameClock>(),
                () => propEnvironment.Globals.IsAvailable ? propEnvironment.Globals.MaxClients : 1,
                _services.Require<IRandomSeeds>(), Log, _services.Create<TempEnts.PropBreakLimits>());
            _tempEntFrame.Driver = physicsProps;
            _services.AddInstance(physicsProps);
        }
        _services.AddInstance(_services.Create<TempEnts.TempEntRender>());
        _services.AddInstance(_services.Create<TempEnts.TempEntAttachments>());
        _tempEntDecisions = _services.Create<TempEnts.ClientTempEntDecisions>();
        _services.AddInstance(_tempEntDecisions);
        _services.AddInstance<TempEnts.ILegacyBrass>(_tempEntDecisions);
        _services.AddInstance<TempEnts.ITempModelSpawner>(_tempEntDecisions);

        // BoltImpact (c_stickybolt.cpp's client effect) spawns through the
        // decisions above, so it joins the effect list here. Its ragdoll pin
        // (CRagdollBoltEnumerator) traces the bodies' hitboxes and ties the
        // element hit to the level's world object, which the physics system
        // below makes.
        _services.AddInstance<Ragdolls.IRagdollBoltPin>(
            _ragdolls is not null && _ragdollPhysics is not null
                ? new Ragdolls.RagdollBoltPin(_ragdolls, new Ragdolls.ClientRagdollHitboxes(_skeletons), _ragdollPhysics,
                                              new Ragdolls.ClientWorldPhysObject(() => _physicsSystem?.World ?? 0),
                                              _services.Require<IFxGameSounds>())
                : new Ragdolls.NoRagdollBoltPin());
        effectHandlers.Add(_services.Create<Effects.CrossbowBoltEffect>());
        // The mods' effects (stage 0.9): every [SideService(Side.Client)]
        // IClientEffectHandler, after the core's, now the services they may
        // ask for exist.
        int modEffects = effectHandlers.AddFrom(_services, Log);
        if (modEffects > 0)
            Log.Msg($"[SourceSharp/Client] {modEffects} mod client effect(s)\n");

        // THE STANDALONE REGISTRY GOES IN BEFORE THE PLAYERS ARE BUILT, and the
        // ORDER is the whole of this comment. ClientTeExplosion asks for
        // IClientStandalones — behaviour ledger row C1's blast push walks the
        // live client entities looking for jointed bodies — and a player whose
        // constructor cannot be satisfied is SKIPPED with a warning and never
        // plays. Registering the registry thirty lines below RegisterAll, where
        // it used to be, would have taken the explosion off the client
        // altogether: the effect would simply stop drawing, and the only clue
        // would be one line at load among a hundred.
        //
        // Nothing is built here that was not already built: this moves the
        // AddInstance, and the registry's own Services and its entity creation
        // are untouched — an instance is not made until a networked entity
        // arrives, which is long after all of this.
        _standalones.Services = _services;
        _services.AddInstance<IClientStandalones>(_standalones);

        _tempEntPlayers = _services.Create<TempEnts.ClientTempEnts>();
        _services.AddInstance(_tempEntPlayers);

        int tePlayers = _tempEntPlayers.RegisterAll(typeof(ClientModule).Assembly, _services);
        Log.Msg($"[SourceSharp/Client] {tempEnts} temp entity table(s), " +
                $"{tePlayers} of them with a managed player\n");

        _hud.Subscribe();

        //---------------------------------------------------------------------
        // The services. Registered by contract, so a drawn entity that wants to
        // put something on screen asks for IHudMessages and gets exactly that —
        // not the HUD's paint pass, which is the module's and runs once a frame.
        //---------------------------------------------------------------------
        _services.AddInstance<IHud>(_hud);
        _services.AddInstance<IHudMessages>(_hud.Messages);
        _services.AddInstance<IHudHints>(_hud.Hints);
        _services.AddInstance<IDynamicLights>(_lights);
        _services.AddInstance<Effects.IClientWinds>(_winds);
        _services.AddInstance<IColorCorrections>(_corrections);

        // THE WEATHER (plan_client_novalve C2, lane c2-sims): stock's
        // CClient_Precipitation and CSnowFallManager, managed. After the winds,
        // which it blows by, and the convars, which it reads each frame.
        _services.AddInstance(_services.Create<Effects.PrecipitationConVars>());
        _precipitations = _services.Create<Effects.ClientPrecipitations>();
        _services.AddInstance(_precipitations);

        // The client's physics callbacks: stock's impact sounds, on managed's
        // own collision-event table (lane c6-input).
        var propOwners = _services.Resolve(typeof(TempEnts.ClientPhysicsProps)) as TempEnts.IClientPhysicsPropOwners;
        _impactSounds = new Physics.ClientImpactSounds(_services.Require<IClientImpactSoundWorld>(),
                                                       _services.Require<IClientImpactSoundEmitter>());
        _collisionSolver = new Physics.ClientCollisionSolver(
            new Physics.ClientCollisionFacts(_services.Require<IClientPhysicsObjectReads>(), _services.Require<IEngineConsole>(),
                                             propOwners));
        _services.AddInstance(_impactSounds);
        _services.AddInstance(_collisionSolver);
        // The splash draws from the engine's own random stream, as stock's
        // random->RandomFloat does; with none there is no splash.
        IUniformRandom? splashRandom = UniformRandomStream.Acquire(_engine, out string whyNoSplashRandom);
        if (splashRandom is null)
            Log.Warning($"[SourceSharp/Client] physics: no engine random stream ({whyNoSplashRandom}); no splash\n");
        _fluidEvents = new Physics.ClientFluidEvents(
            new Physics.ClientFluidWorld(_services.Require<IClientSplashCarryOut>(), _services.Require<IClientPhysicsObjectReads>(),
                                         new VPhysicsObjects(_engine.VPhysics), new VPhysicsFluids(_engine.VPhysics), _services.Require<IEngineEntities>(),
                                         propOwners),
            splashRandom, () => _clock.Now);
        _services.AddInstance(_fluidEvents);

        // The scrape (stock's client Friction and PhysFrictionSound): the shared
        // FrictionSounds over this side's sound controller and its own effects.
        // A clientside prop's scrape is the world's to play (ClientPhysicsProps.SoundSource).
        var scrapingProps = _services.Resolve(typeof(TempEnts.ClientPhysicsProps)) as TempEnts.ClientPhysicsProps;
        _frictionSounds = new FrictionSounds(_services.Require<IEngineSound>(), _sounds,
                                             new EnginePhysicsSurfaceTable(_services.Require<PhysicsSurfaces>()),
                                             new Physics.ClientFrictionEffects(() => _clientEffects),
                                             scrapingProps is null ? null : scrapingProps.SoundSource);
        _friction = new Physics.ClientFriction(new VPhysicsObjects(_engine.VPhysics), new Physics.FrictionSoundsScrapes(_frictionSounds),
                                               () => _clock.Now);
        _services.AddInstance(_frictionSounds);

        // The contact touches (stock's client StartTouch/EndTouch and
        // UpdateTouchEvents): the clientside props are what answers one, and
        // their HitSurface splats blood where the model asks for it.
        if (scrapingProps is not null)
        {
            var bloodConsole = _services.Require<IEngineConsole>();
            scrapingProps.HitSurfaces = new TempEnts.ClientPropHitSurfaces(
                _services.Require<IEngineEntityRender>().GetModelKeyValueText, _services.Require<IClientDecals>(),
                _services.Require<IEngineWorldQuery>(), _services.Require<IEngineEffects>(),
                () => !int.TryParse(bloodConsole.FindConVarValue("violence_hblood"), out int hblood) || hblood != 0);
            _contactTouches = new Physics.ClientContactTouches(
                new Physics.VPhysicsTouchWorld(new VPhysicsObjects(_engine.VPhysics)), scrapingProps);
            _services.AddInstance(_contactTouches);
        }

        // The level's environment and the step (stock's CPhysicsSystem): native's
        // client system row, PhysicsSimulate and PhysicsReset forward here.
        if (VPhysicsClientLevel.Acquire(_engine, out string whyNoLevel) is not { } level)
        {
            Log.Warning($"[SourceSharp/Client] physics: no vphysics for the level ({whyNoLevel}); nothing simulates\n");
        }
        else
        {
            _physicsSystem = new Physics.ClientPhysicsSystem(
                level,
                new Physics.ClientPhysicsClock(_services.Require<IEngineEnvironment>(), _services.Require<IEngineEffects>(),
                                               _convars.Get<Physics.ClientPhysTimescaleConVar>()
                                               ?? throw new InvalidOperationException("cl_phys_timescale is not registered")),
                _services.Require<ClientPhysicsEnvironment>(), () => _physicsWorld, () => _ragdolls,
                new Physics.ClientPhysicsStepTail(_impactSounds, _fluidEvents!, _frictionSounds, _clock, _contactTouches), Log);
            _services.AddInstance(_physicsSystem);
        }

        // The standalone registry is registered ABOVE, before the temp entity
        // players are built, because two of them ask for it: the explosion's
        // blast push needs the list the ragdolls are on (row C1), and the
        // pool's collision arm asks it what a trace hit (row C4). It used to be
        // here with the other services, and a player whose constructor cannot
        // be satisfied is skipped with one warning at load -- so the explosion
        // simply stopped drawing.

        // AFTER the registry is in the container, because the pump walks it.
        // The sink is built first and registered, so that cs_animevents can
        // ask the same instance the pump feeds rather than a second one that
        // has never seen an event.
        _services.AddInstance<Standalone.IViewEntity>(_services.Create<Standalone.LocalViewEntity>());
        _animEffects = _services.Create<Standalone.AnimationEventEffects>();
        _services.AddInstance(_animEffects);

        // AND under the interface, because that is what the pump's constructor
        // asks for. Registered under the concrete type as well so that
        // cs_animevents can read the counters off the same object — a command
        // resolving a second instance would report a dispatcher that had never
        // fired anything, which is the shape of report this project has twice
        // been misled by.
        _services.AddInstance<Standalone.IAnimationEventSink>(_animEffects);

        _animEvents = _services.Create<Standalone.AnimationEventPump>();
        _services.AddInstance(_animEvents);

        // The weapon machine's replay half (Phase 10): the same shared machine
        // the server runs, over working copies of the declared states, plus
        // the misprediction meter for the pair the player carries.
        _clientArsenal = _services.Create<ClientArsenal>();
        _services.AddInstance(_clientArsenal);
        _services.AddInstance<ILocalArsenal>(_clientArsenal);

        // THE UI (plan_client_ui §8 U2): the HUD as toolkit screens, drawn
        // through IShimUiClient when the HUD panel paints (IManagedUiClient).
        // After the arsenal and the HUD's message, hint and stamp state, which
        // its sources read.
        // THE CENTRE PRINT (stock CCenterStringLabel): the core's, for the same reason.
        _centre = new Vgui.CentrePrint(_clock, _convars.Get<Vgui.ScrCenterTimeConVar>());
        _services.AddInstance(_centre);
        _services.AddInstance<SourceSharp.Client.Ui.ICentrePrintSource>(_centre);
        _disc = new Vgui.LoadingDisc(_services.Require<IEngineHud>(), _convars.Get<Vgui.ClShowPausedImageConVar>());
        _services.AddInstance<SourceSharp.Client.Ui.ILoadingDiscSource>(_disc);
        _voice = new Vgui.VoiceSpeakers(new Vgui.VoicePlayers(_services.Require<IClientStandalones>(), _services.Require<Vgui.IPlayerNames>()));
        _services.AddInstance<SourceSharp.Client.Ui.IVoiceSource>(_voice);
        var modEnable = _convars.Get<Voice.VoiceModEnableConVar>();
        _voiceStatus = new Voice.VoiceStatus(new Voice.VoiceEngine(AcquireEngineClient, modEnable), _clock, Log,
                                             new Voice.VoiceBanFile(_services.Require<IModInfo>().GameDir, Log), modEnable,
                                             _convars.Get<Voice.VoiceClientDebugConVar>());
        _voiceEvents = new Voice.VoiceStatusFanOut(_voice, _voiceStatus);
        // THE SPECTATOR (stock CSpectatorGUI): the local player's networked
        // observer fields (PlayerNetState, written by the server's Observers).
        var observerHud = _services.Require<IEngineHud>();
        var observer = new Vgui.ObserverState(
            new Vgui.LocalObserverFields(_services.Require<IClientStandalones>(), () => observerHud.LocalPlayerIndex),
            new Vgui.SpectatedPlayers(_services.Require<IClientStandalones>(), _services.Require<Vgui.IPlayerNames>()),
            () => AcquireEngineClient()?.LevelName());
        _services.AddInstance<SourceSharp.Client.Ui.IObserverSource>(observer);
        _services.AddInstance<SourceSharp.Client.Ui.ISpectatorTargetSource>(observer);
        _services.AddInstance(new Vgui.SpectatorCommandRunner(new Vgui.SpectatorCommands(), observer, Log,
                                                              AcquireEngineClient));
        _services.AddInstance<SourceSharp.Client.Ui.IDebugOverlaySource>(
            new Vgui.DebugOverlayText(_services.Require<IClientDebugOverlay>(), _clock));
        // THE CHAT (stock CBaseHudChat): its feed and its input line, the core's.
        var chatFeed = new Vgui.ChatFeed(_clock, Log, _services.Require<IEngineHud>(),
                                         new Vgui.ChatPlayers(_services.Require<IClientStandalones>(), _services.Require<Vgui.IPlayerNames>()),
                                         _convars.Get<Vgui.HudSayTextTimeConVar>());
        chatFeed.Subscribe(_services.Require<SayTextMessage>(), _services.Require<SayText2Message>());
        _services.AddInstance<SourceSharp.Client.Ui.IChatSource>(chatFeed);
        var chatInput = new Vgui.ChatInputUi(AcquireEngineClient);
        _services.AddInstance(chatInput);
        _services.AddInstance<SourceSharp.Client.Ui.IChatInputSource>(chatInput);

        // TextMsg, as stock's chat handler printed it (hud_basechat.cpp): the centre, the console, the chat.
        var texts = new Vgui.TextMessages(_services.Require<IEngineHud>(), Log, _centre, _convars.Get<Vgui.ClShowTextMsgConVar>(), chatFeed);
        texts.Subscribe(_services.Require<TextMsgMessage>());

        // The game's notices (stock ClientModeShared::FireGameEvent): joins, leaves, teams, names, notify convars.
        new Vgui.ChatNotices(_services.Require<IEngineHud>(), Log, chatFeed).Subscribe(
            _services.Require<PlayerConnectClientEvent>(), _services.Require<PlayerDisconnectEvent>(),
            _services.Require<PlayerTeamEvent>(), _services.Require<PlayerChangeNameEvent>(),
            _services.Require<ServerCvarEvent>());

        // THE KILL FEED's queue (stock CHudDeathNotice): the core's, so a UI-only
        // reload keeps its lines and its event subscription pins nothing of the UI.
        var kills = new Vgui.KillFeed(_clock, Log,
            new Vgui.KillFeedPlayers(_services.Require<IClientStandalones>(), _services.Require<IEngineHud>(),
                                     _services.Require<Vgui.IPlayerNames>()),
            _convars.Get<Vgui.HudDeathNoticeTimeConVar>());
        kills.Subscribe(_services.Require<PlayerDeathEvent>());
        _services.AddInstance(kills);
        _services.AddInstance<SourceSharp.Client.Ui.IKillFeedSource>(kills);

        // Since U5 the toolkit, the screens and their composition load in the
        // UI's own collectible context (Vgui.UiLoader), which a UI-only deploy
        // swaps without this module; this module names none of their types.
        var hud = _hud;
        var arsenal = _clientArsenal;
        var uiServices = new Vgui.UiRuntimeServices(_services, Log, hud, arsenal, _convars.Get<Vgui.UiScaleConVar>(),
                                                    AcquireEngineClient,
                                                    AcquireInputSystem,
                                                    () => ClientStringTables.Acquire(_engine, out _));
        // The mod's own UI assemblies (mods.yaml client_ui, stage 0.9) join
        // the core's three in the UI's own context, so a mod HUD ships and
        // hot-reloads without the core ever naming a screen type.
        var mod = _services.Require<IModInfo>();
        _uiLoader = new Vgui.UiLoader(Log, _clock, mod.ManagedDir, () => uiServices,
                                      _convars.Get<Vgui.UiAutoReloadConVar>(), modAssemblies: mod.UiAssemblies)
        {
            DrawHud = _convars.Get<Vgui.ClDrawHudConVar>(),
        };
        _uiLoader.Load();
        _services.AddInstance(_uiLoader);
        _services.AddInstance(new Vgui.UiCarryReload(_uiLoader));
        // A new map hides the viewport's screens, as stock's viewport hid its panels on game_newmap.
        var newMapUi = _uiLoader;
        _services.Require<GameNewMapEvent>().Fired += _ => newMapUi.Current?.NewMap();
        _ui = _uiLoader;

        // THE CLIENT MODE'S KEY DECISION (stock ClientModeShared::KeyInput): the
        // chat keys open the chat, an observer's keys run spec_*.
        var chatOpener = new Vgui.ChatOpener(_uiLoader, chatInput);
        _services.AddInstance(chatOpener);
        _services.AddInstance(new Vgui.ViewportPanelCommand(_uiLoader));
        var uiLoader = _uiLoader;
        _services.AddInstance(new Vgui.ShowInfo(() => uiLoader.Current, Log));
        _keys = new Vgui.ClientKeyInput(chatOpener, observer, AcquireEngineClient);

        // THE PLAYER'S REPLAY (plan_entity.md step 6f). After the arsenal,
        // which it drives: the weapon machine's two phases are direct calls
        // from inside the prediction frame now that both halves are managed.
        _prediction = _services.Create<ClientPlayerPrediction>();

        // THE OBSERVER'S CAMERA (C7 item 5), after the standalones and the HUD
        // it reads exist: built earlier, its IClientStandalones did not resolve
        // and the container recursed until the stack ran out.
        _observerCamera = _services.Create<Render.ObserverCamera>();
        _services.AddInstance(_observerCamera);

        // THE NAMED RENDER TARGETS (C7, rendertexture.cpp's getters): which
        // texture each slot finds, and which references the material system's
        // release drops, declared to the native store before anything renders.
        // A reload declares the same rows, and the store keeps its references.
        var renderTargets = new Render.RenderTargetCatalog();
        _services.AddInstance(renderTargets);
        int renderTargetsDeclared = renderTargets.DeclareTo(_services.Require<IRenderTargetTable>());
        if (renderTargetsDeclared != (int)Render.RenderTargetSlot.Count)
            Log.Warning($"[SourceSharp/Client] the render target store took {renderTargetsDeclared} of {(int)Render.RenderTargetSlot.Count} slots\n");

        // THE LOCAL PLAYER'S FLASHLIGHT (C7 cluster 12): stock's
        // C_BasePlayer::UpdateFlashlight and CFlashlightEffect, run at
        // FRAME_RENDER_START. Its own ConVars are managed declarations; the
        // four it reads but does not declare come through the convar watch
        // (the module routes the watch's changes to them).
        IFlashlightSlots flashlightSlots = _services.Require<IFlashlightSlots>();
        var flashlightSettings = new Flashlight.FlashlightConVars(
            _convars,
            new Flashlight.WatchedConVarValues(_services.Require<IClientConVarSlots>(),
                                               _services.Require<IEngineConsole>().FindConVarValue),
            flashlightSlots.DXSupportLevel);
        flashlightSettings.Start();
        _flashlightSettings = flashlightSettings;
        _flashlight = new Flashlight.LocalPlayerFlashlight(
            new Flashlight.StandaloneFlashlightViewers(_services.Require<IClientStandalones>(),
                                                       _services.Require<IEngineHud>(),
                                                       new ClientStandaloneBlocks(_engine),
                                                       _services.Require<ViewReadout>()),
            new Flashlight.FlashlightWorld(flashlightSlots,
                                           _services.Require<IEngineVisibility>()),
            flashlightSettings);
        _services.AddInstance(_flashlight);

        // WHAT FOG THE VIEWER IS STANDING IN (plan_entity.md step 8e-render).
        // Driven from OnStage, which is why it is held rather than registered
        // behind a contract nothing else asks.
        _worldFog = _services.Create<ClientWorldFog>();
        _services.AddInstance(_worldFog);

        // AND WHAT 3D SKYBOX IT IS SHOWN (the defect-skybox lane). Held rather
        // than registered behind a contract nothing else asks, for the fog's
        // reason: OnStage is its only caller.
        _skybox = _services.Create<Standalone.ClientSkybox>();
        _services.AddInstance(_skybox);
        _services.AddInstance(_prediction);

        // THE INPUT (plan_client_novalve §2 cluster 13): stock's CInput, ported
        // (ClientInput), over the engine's own IVEngineClient and IInputSystem
        // and IShimInputClient's slots; its convars are the registry's above,
        // read with the engine's semantics, and its commands are registered
        // here from their table. The UI's menu layer, when it holds the input,
        // closes the gate.
        var inputCvars = new Input.InputCvars();
        int inputConVars = inputCvars.Bind(_convars.All);
        var inputHost = Input.ClientInputHost.For(_engine, _services.Require<IEngineConsole>());
        var inputDevices = Input.ClientInputDevices.For(_engine, Log);
        // The input mailbox: the sample times native stores and the no-op
        // answers native carries out, without a crossing either way.
        Input.IInputMailbox? inputMailbox = Input.InputMailbox.Open(_engine, out string whyNoMailbox);
        if (inputMailbox is null)
            Log.Warning($"[SourceSharp/Client] input: no input mailbox ({whyNoMailbox}); every input call crosses\n");
        var input = new Input.ClientInput(inputHost, inputDevices, inputCvars, new Input.UiMenuInputGate(() => _uiLoader?.Current),
                                          mailbox: inputMailbox);

        // The rumble mixer (c_rumble.cpp's CRumbleEffects), stepped once a frame
        // below; its random amplitude is the engine's own stream.
        IUniformRandom? rumbleRandom = UniformRandomStream.Acquire(_engine, out string whyNoRandom);
        if (rumbleRandom is null)
            Log.Warning($"[SourceSharp/Client] input: no engine random stream ({whyNoRandom}); no rumble mixer\n");
        var rumble = rumbleRandom is null ? null : new Input.ClientRumble(inputDevices, inputCvars, rumbleRandom, text => Log.Msg(text),
                                                                                 inputMailbox);
        IEngineHud hudArea = _services.Require<IEngineHud>();
        _inputService = new Input.ClientInputService(input, rumble,
                                                     localPlayerAlive: () => _standalones.GetByIndex(hudArea.LocalPlayerIndex) is ClientSsPlayer local
                                                                             && local.State.Alive,
                                                     afterCommand: (cmd, sampleTime, active) => OnCreateMove(ref cmd, sampleTime, active),
                                                     curTime: () => _clock.Now,
                                                     paused: () => inputHost.IsPaused);
        _services.AddInstance(_inputService);
        IEngineConsole inputConsole = _services.Require<IEngineConsole>();
        _services.AddInstance<IDebugClientInput>(new ClientDebugInput(_inputService, inputCvars, inputHost,
                                                                      () => inputConsole.FindConVarValue("sv_cheats")));
        int inputCommands = 0;
        foreach (Input.InputCommandSpec spec in Input.InputCommands.For(input, inputHost, inputCvars))
        {
            if (_commands.Register(spec.Name, spec.Help, spec.Flags, new Input.InputConCommand(spec.Run), _services.Require<IEngineConsole>()))
                inputCommands++;
        }
        Log.Msg($"[SourceSharp/Client] input: {inputConVars} convar(s) bound, {inputCommands} command(s) registered\n");

        // COMMANDS LAST, after everything they might ask for.
        int n = _commands.RegisterAll(typeof(ClientModule).Assembly, _services.Require<IEngineConsole>(), _services);
        _commands.PublishTo(_services);
        Log.Msg($"[SourceSharp/Client] registered {n} console command(s) by attribute\n");
        Log.Msg($"[SourceSharp/Client] {engineAreas} engine area(s) in the container\n");
        Log.Msg($"[SourceSharp/Client] {_services.ServiceTypes.Count} service(s), " +
                   $"{_services.Names.Count} console name(s) in the container\n");

        // Subscribers attach to the events the classes above raise — this
        // side's instances, from its container. This is where gameplay reacting
        // to events gets wired up, and it needs no knowledge of the registry or
        // the native event API.
        _scoreboard.Subscribe();

        // Nothing publishes on the generated shell here any more: it is
        // deleted, and every type this side publishes goes through
        // StandaloneNetStates. The two sides still agree only because both read
        // the same shared metadata.
        int layoutCount = _layouts.DiscoverAll(Game);
        Log.Msg($"[SourceSharp/Client] discovered {layoutCount} entity layout(s), " +
                   $"fingerprint 0x{_layouts.Fingerprint:X8}\n");

        // The layout-check message compares the server's fingerprint against
        // this registry when it arrives at join. It holds the registry itself
        // (its constructor took it from the container), so the discovery above
        // is what it will read.
        Log.Msg($"[SourceSharp/Client] registered {v} convar(s) by attribute\n");

        WireDebugChannel();

        // plan_map_load P5: compile this side's methods off the main thread
        // while the game starts, so the first map load does not JIT them. The
        // context stays collectible (hot reload); Shutdown stops this first.
        _jitWarmup = new JitWarmup(_services.Require<ILog>());
        _jitWarmup.Start("client", _jitWarmup.TypesOfContextOf(typeof(ClientModule).Assembly));
    }

    /// <summary>
    /// GATE ONE, ON THIS SIDE, AND IT IS THE FIRST OF THE TWO.
    ///
    /// <para>
    /// The debug channel exists at all only if the person who launched THIS
    /// PROCESS asked for it. Without <c>-debugui</c> nothing below runs:
    /// <c>DebugChannelInviteMessage.Accepted</c> is left null, so the handler
    /// for the server's invitation returns before it reads a byte of the
    /// payload, and no client is left holding a socket, a task, or an intention
    /// to dial. That is the negative the tests assert, because "we would not do
    /// anything with it" is a much weaker claim than "there is no code path".
    /// </para>
    ///
    /// <para>
    /// The message is still DECLARED unconditionally, and has to be: declaration
    /// order in the shared assembly is the wire protocol, and a message one side
    /// omits renumbers every message after it on the other.
    /// </para>
    /// </summary>
    private void WireDebugChannel()
    {
        // Whatever came before goes first. Called from Initialize and again from
        // every LevelShutdown, so this is also what stops two loads leaving two.
        TeardownDebugChannel();

        // THE PROCESS' OWN COMMAND LINE, as the server reads it too: the engine
        // has no command-line slot any more (see LocalCommandLine).
        var args = _services.Require<ILocalCommandLine>().Args;

        // The client's dev API, loaded by name (plan_side_isolation §2.6):
        // nothing here references its assembly.
        var channel = DebugApiClientLoader.LoadIfRequested(args, _services.Require<IDebugClientHooks>(), Log);
        if (channel is null)
            return;

        _debugChannel = channel;

        // THE ONLY PLACE THIS DELEGATE IS INSTALLED. It closes over THIS
        // channel rather than over the field, so a stale delegate that somehow
        // survived would attach a disposed channel rather than the live one —
        // which fails loudly instead of quietly working.
        _invites.Accepted = channel.Attach;

        // AFTER A cs_cl_RELOAD: the old module left the session's resume data
        // (ReloadSave), and the server sends no second invitation, so this
        // client redials by itself. Taken, so a level change finds none.
        if (_reloadSections.Take(IDebugApiClient.ResumeReloadSection) is { } resume)
            channel.Resume(resume);

        Log.Msg($"[SourceSharp/Client] {DebugApiClientLoader.EnableParam} given: this client will " +
                "accept a debug channel invitation from the server it joins\n");
    }

    /// <summary>
    /// Takes the debug channel down, socket, task and registration together.
    ///
    /// <para>
    /// <b>CALLED AT EVERY LEVEL SHUTDOWN, NOT ONLY AT PROCESS EXIT.</b> A socket,
    /// a handler registration and a background task that survive a level change
    /// are the exact family of defect this tree has already paid for — a
    /// <c>CWorld</c> that stopped being freed at level shutdown was invisible on
    /// the first map and took the process down on the second, and hid for six
    /// phases because every live gate loaded one map. A dial loop left running
    /// would also hold the client's collectible load context on its stack, so a
    /// managed reload would leak the build it replaced.
    /// </para>
    ///
    /// <para>
    /// The registration goes WITH the channel, deliberately: leaving the
    /// listener installed over a disposed channel would mean an invitation
    /// arriving during a load could re-open one, which is the same defect with
    /// more steps. <see cref="LevelInit"/> puts both back.
    /// </para>
    ///
    /// <para>
    /// <see cref="DebugChannelClient.Dispose"/> WAITS for the task rather than
    /// only signalling it. "Signalled" is not "gone".
    /// </para>
    /// </summary>
    private void TeardownDebugChannel()
    {
        if (_invites is not null)
            _invites.Accepted = null;

        _debugChannel?.Dispose();
        _debugChannel = null;
    }

    public void Frame(float frameTime)
    {
        _stats.CountFrame();

        // The voice manager tells the server its state once a second (stock CVoiceStatus::Frame).
        _voiceStatus?.Frame();
        _frameTimes.Add(frameTime);

        // The dev API's questions that read the game's state, answered here on
        // the game thread (a no-op without -debugui: the channel is null).
        _debugChannel?.Pump();

        // Dynamic lights are refreshed here rather than from OnDraw, and the
        // difference matters: OnDraw only runs for an entity the leaf system
        // thinks is visible, and a light does not have to be visible to matter
        // -- a lamp round a corner still lights the wall you can see.
        _lights.Frame();

        // The impact sounds, the fluid entries and the scrapes are the physics
        // step's (ClientPhysicsSystem.Simulate, at stock's place in the frame).

        // Colour corrections, here for the same reason and with the same
        // argument: the weight is a function of where the player is standing,
        // which moves without anybody deciding it, and a correction round a
        // corner still tints the wall you can see.
        _corrections.Frame();

        // Animation events, here for the third instance of the same argument.
        // A sequence's cycle advances on its own clock whether or not the leaf
        // system thinks the entity is visible, so an entity that went off
        // screen mid-reload and came back would otherwise return with its
        // cursor where it was left and fire the rest of the reload at once.
        //
        // AFTER the lights, and that ordering is load-bearing: a muzzle flash
        // fired here writes a dlight for this frame, and _lights.Frame()
        // refreshing afterwards would be a second pass over the same pool with
        // this frame's flash not yet in it.
        _animEvents.Frame();

        // The winds (lane c2-sims): each due wind thinks, then the head wind's
        // vector is published for native's GetWindspeedAtTime readers. Once a
        // frame, as the native CManagedWindThink it replaces.
        _winds.Frame();

        // The finished ownerless particle effects let their executors go, here
        // rather than inside the particle manager's own walk.
        _particleEffects.Frame();

        // The sound envelopes, once a frame: stock's client system Update.
        _sounds.SystemUpdate();

        // The weather after the winds it blows by: each volume's think and the
        // snowfall manager's (stock's ClientThink, every frame).
        _precipitations.Frame(frameTime);
        // The rumble mixer, once a frame (lane c6-input).
        _inputService?.RumbleFrame();

        // The ropes AFTER the winds: a rope reads this frame's published wind.
        _ropes?.Frame(frameTime);
    }

    /// <summary>
    /// <c>IBaseClientDLL::FrameStageNotify</c>, once per stage — the client's
    /// counterpart of the server's <c>RunFrame</c> (<c>plan_entity.md</c> §3.4).
    ///
    /// <para>
    /// <b>What it does today is remember the stage, and that is deliberate.</b>
    /// The per-frame client work this call exists to carry — interpolation,
    /// client think, the render tail — belongs to Phase 6 and is not written
    /// yet. Landing the entry now, with its crossing counted and its stage
    /// recorded, is what lets 6a and 6b hang work off a call that already
    /// arrives exactly once per stage rather than each inventing an ABI slot.
    /// </para>
    ///
    /// <para>
    /// <b>No allocation and no logging.</b> This runs seven times per client
    /// frame. An unknown stage is IGNORED rather than throwing: the value comes
    /// from the engine, and an engine that grew an eighth stage should reach a
    /// client that carries on rather than one that unwinds into its C++ frames.
    /// </para>
    /// </summary>
    // plan_client_vtables CD: the runtime's ClientStage slot, from SourceSharp.Client.Interop.
    void IClientRuntimeHost.OnStage(int stage) => OnStage((ClientFrameStage)stage);

    public void OnStage(ClientFrameStage stage)
    {
        _stats.NoteStage(stage);

        // The engine's console lines buffered since the last client frame
        // (console-batch): drained once per frame, at its top.
        if (stage == ClientFrameStage.Start)
            _consoleLines?.Drain();

        // INTERPOLATION, ON TWO OF THE SEVEN STAGES AND WITH NO SLOT OF ITS OWN
        // (plan_entity.md §3.9, step 6a). This is what the note above meant by
        // "6a and 6b hang work off a call that already arrives once per stage".
        //
        // The ORDER of the two is the whole design. A sample has to be taken
        // while the head still holds the value the decoder wrote, and the render
        // pass writes the drawn pose over it later in the same frame -- so the
        // sample is taken one stage after the decode finishes, and the pose is
        // computed at the stage stock computes it at.
        switch (stage)
        {
            case ClientFrameStage.NetUpdatePostDataUpdateEnd:
                _interpolation.SamplePass(_interpolationBlocks, _clock.Now);

                // RENDER POLICY, ON THE SAME STAGE AND WITH NO SLOT OF ITS OWN
                // (plan_entity.md §3.9, step 6b). Here rather than at
                // RenderStart because the shell's apply sweep runs at PreRender,
                // which is BETWEEN the two -- so a policy input a class set out
                // of OnDataChanged reaches the leaf system and the shadow
                // manager in the same frame it was set, and never one late.
                _renderPolicy.Pass(_renderPolicyBlocks);

                // THE VIEWER'S WORLD FOG, ON THE SAME STAGE AND FOR THE SAME
                // REASON (plan_entity.md step 8e-render). The decode has just
                // finished, so the fog the renderer draws with this frame is
                // the fog that arrived this frame. It reads the LOCAL player's
                // networked copy and hands it to native only when it moved.
                //
                // THIS CALL IS THE WHOLE OF WHAT MAKES THE FOG REACH THE
                // RENDERER, and if it went missing the game would run perfectly
                // with the fog off -- which is exactly what the tree looked like
                // for the whole time the defect was open. TheFogPassIsWiredInto-
                // TheNetUpdateStage asserts the call rather than the class.
                _worldFog?.Pass();

                // AND THE SKYBOX, at the same stage and for the same reason.
                // THIS CALL IS THE WHOLE OF WHAT MAKES THE 3D SKYBOX REACH THE
                // RENDERER: without it the game runs perfectly and no skybox
                // draws, which is exactly what the tree looked like for every
                // wave the defect was open.
                // TheSkyboxPassIsWiredIntoTheNetUpdateStage asserts the call
                // rather than the class.
                _skybox?.Pass();
                break;

            case ClientFrameStage.RenderStart:
                _interpolation.RenderPass(_interpolationBlocks, _clock.Now);

                // The local player's flashlight, where stock's
                // C_BasePlayer::Simulate ran it (OnRenderStart's SimulateEntities,
                // after the interpolation): the projected texture is handed to
                // the shadow manager before the frame's view renders.
                _flashlight?.Pass();

                // The observer's view, after the drawn poses it reads its
                // target off (C7 item 5).
                _observerCamera?.Pass();

                // The second camera, after the drawn poses: its view is queued
                // for this frame's render (lane camproto), into the render
                // plan's monitor pass when the plan draws the frame (C7 item 6).
                if (_viewCamera is { PlannedQueue: null } camera && _services.Resolve<Render.RenderPlanHost>() is { } planHost)
                    camera.PlannedQueue = planHost.QueueMonitor;
                _viewCamera?.Pass();
                break;
        }
    }

    /// <summary>
    /// The engine's interpolation list, held rather than fetched: the object
    /// carries the pass's block array between its <c>Begin</c> and its
    /// <c>Block</c> calls, so a fresh one per stage would answer
    /// <c>Block</c> out of a list nothing had fetched.
    /// </summary>
    // The ropes, stepped once a frame (Frame), never per view.
    private Ropes.ClientRopeSystem? _ropes;
    private Beams.ClientBeamSystem? _beams;
    private ViewCamera.ViewCameraService? _viewCamera;
    private Render.ObserverCamera? _observerCamera;
    private Flashlight.LocalPlayerFlashlight? _flashlight;
    private Flashlight.FlashlightConVars? _flashlightSettings;
    private Render.LeafSystem.LeafListConVars? _leafConVars;
    private Render.PixelVisibility.BuildingCubemapsWatch? _buildingCubemaps;

    private IInterpolationBlocks _interpolationBlocks = NoInterpolationBlocks.Instance;

    /// <summary>
    /// The engine's render-policy list, held rather than fetched, for the reason
    /// <see cref="_interpolationBlocks"/> gives.
    /// </summary>
    private IRenderPolicyBlocks _renderPolicyBlocks = NoRenderPolicyBlocks.Instance;
    /// <summary>cs_cl_player_predict, or null before the convars register.</summary>
    private PlayerPredictConVar? _predictConVar;

    /// <summary>The engine's clock, for the two interpolation stages.</summary>
    private readonly IGameClock _clock;

    public void LevelInit(string mapName)
    {
        Log.Msg($"[SourceSharp/Client] level init: {mapName}\n");
        // ResetToneMapping's managed half (shell_clientdll's LevelInitPreEntity
        // resets the engine's scale): the tonemap average starts over.
        _services.Require<Render.PostProcessHost>().Planner.ResetToneMapping();
        if (_voiceStatus is { } voice)
            voice.LevelInitialized = true;

        // RESET, and it was not being reset. Two readers call this "frames
        // since level init" (Commands.cs) and "level shutdown after N frames"
        // (LevelShutdown, below), and on the SECOND and every later level both
        // were reporting frames since the process started -- a diagnostic
        // quietly answering a different question than the one it is labelled
        // with, which is worse than no diagnostic.
        //
        // Found when a lane carried it as a level-init witness across three
        // arms and read 5161 / 12356 / 18238, each of which began with a level
        // init. The intent of both readers is per-level, so the missing reset
        // is the bug rather than the labels.
        _stats.ResetFrames();

        // AND THE PREDICTION'S RING, for the same reason one line down: it is
        // keyed by COMMAND NUMBER, and command numbers start again from the
        // beginning on every map, so a slot kept across a level load is judged
        // against a different world's acknowledgement (step 6f).
        _prediction?.LevelInit();

        // AND THE FOG, for the reason the ring is cleared: the push is compared
        // against what was last pushed, so a new map whose fog happens to equal
        // the old map's would never be pushed at all and the renderer would go
        // on holding the previous level's numbers.
        _worldFog?.LevelInit();

        // AND THE SKYBOX, for the same reason and with a sharper consequence:
        // the push is compared against what was last pushed, so a new map whose
        // sky camera happens to equal the old map's would never be pushed at
        // all -- and the renderer would go on setting the PREVIOUS level's
        // visleaf area bit, which is how a corner of another map ends up in the
        // sky.
        _skybox?.LevelInit();

        // EVERY RING, THROWN AWAY (plan_entity.md step 6a). The rings are keyed
        // by the entry index inside an entity's handle, and handles start again
        // from the beginning on every map -- so an entity on this map would
        // otherwise inherit the history of whatever held its slot on the last
        // one, and be drawn sliding in from wherever that thing was.
        _interpolation.Clear();

        // EVERY "ALREADY DECIDED" MARK, THROWN AWAY (step 6b), for the same
        // reason the rings are: the marks are keyed by the entry index inside a
        // handle, handles start again from the beginning on every map, and an
        // entity that inherited one would keep the render policy of whatever
        // held its slot on the last map until one of its own inputs moved.
        _renderPolicy.Clear();

        // The temp entity players that say something once per LEVEL — the
        // behaviour ledger's open client rows. See IClientTempEntLevel.
        _tempEntPlayers?.LevelInit();

        // This hook fires immediately AFTER the client gamesystems'
        // LevelInitPostEntity — the baseline entities and their blobs arrived
        // during signon, before it. That ordering is what the detail
        // controller needs: the detail props (DetailPropSystem) restore cl_detailfade and
        // cl_detaildist in its LevelInitPostEntity, wiping the clamp the
        // entity applied on arrival, so the clamp is said again from the one
        // managed moment on the right side of the restore.
        foreach (var entity in _standalones.Live())
        {
            if (entity is Standalone.ClientEnvDetailController controller)
                controller.Reclamp();
        }

        ReadTheModsTable();

        // Last, so a mod sees the level as the core has set it up.
        _services.Require<IHooks<IClientLevelHook>>()
            .Each(nameof(IClientLevelHook.OnLevelInit), h => h.OnLevelInit(mapName));

        // THE DEBUG CHANNEL IS DELIBERATELY NOT RE-ARMED HERE, and a live run is
        // why. The server's OnClientActive — which is what sends the invitation
        // — fires BEFORE this hook:
        //
        //     [Server] client 1 active            <- invitation sent
        //     [DebugChannel] a client attached    <- the client dialled back
        //     [Client] level init                 <- this line
        //
        // So a re-arm here tears down the channel the CURRENT level has already
        // established, six milliseconds after it attached, and nothing invites
        // again until the next map. The console said "a client attached" and
        // then "session ended" on every load, and the mirror never received a
        // record. Arming happens in Initialize and again at LevelShutdown, both
        // of which are on the right side of the invitation.
    }

    /// <summary>
    /// <b>The client half of Phase A8, demonstrated rather than asserted.</b>
    /// The mod's table is created and filled on the SERVER; what this reads is
    /// the replicated copy, through the client's own container, by NAME.
    ///
    /// <para>
    /// <b>It may legitimately find nothing here</b>, and that is the lazy find
    /// working rather than failing: the table arrives with the signon data and
    /// this hook is not a promise that it has. Nothing is cached, so a later
    /// read succeeds; what is reported is whichever answer the engine actually
    /// gave, because a line that said "read it" without saying what would be
    /// equally true of a table that never arrived.
    /// </para>
    /// </summary>
    private void ReadTheModsTable()
    {
        var table = _services.Require<ManagedStringTable>();

        Log.Msg($"[SourceSharp/Client] mod string table '{table.Name}' id {table.Id}: "
              + $"{table.Count} string(s), [0] = '{table.GetString(0) ?? "<not here yet>"}'\n");
    }

    public void LevelShutdown()
    {
        Log.Msg($"[SourceSharp/Client] level shutdown after {_stats.FrameCount} frames\n");
        _impactSounds?.Clear();
        _fluidEvents?.Clear();
        _contactTouches?.Clear();
        if (_voiceStatus is { } voice)
            voice.LevelInitialized = false;

        // The id map is the level's server's: the next one's table is read afresh.
        _userMessageReceiver?.Forget();

        // First, so a mod still sees the level's state as it was.
        _services.Require<IHooks<IClientLevelHook>>()
            .Each(nameof(IClientLevelHook.OnLevelShutdown), h => h.OnLevelShutdown());

        // Every skeleton and model image goes with the level.
        _skeletons?.OnLevelShutdown();

        // The flashlight's shadow goes with the shadow manager's level.
        _flashlight?.LevelShutdown();

        // Forgotten rather than released: the entities holding them are going
        // away with the level, and so is the engine's whole dlight pool.
        _lights.Reset();

        // The winds go with their entities.
        _winds.Reset();

        // The ownerless effects' executors go with the level (the executor's own
        // level shutdown releases them).
        _particleEffects.Reset();
        _flecks.Reset();

        // And every looping sound patch stops (stock's LevelShutdownPreEntity).
        _sounds.SystemReset();

        // The weather goes with the level's volumes.
        _precipitations.LevelShutdown();

        // Forgotten rather than released, for the same reason: the lookups
        // belong to the level's colour-correction manager and go with it.
        _corrections.Reset();

        // Every cursor names an entity of the level that is going. Kept, they
        // would meet the next level's handles — recycled numbers pointing at
        // different models — and read as animations that had been running all
        // along.
        _animEvents.Reset();
        _animEffects.Reset();

        // THE DEBUG CHANNEL GOES WITH THE LEVEL, socket, task and registration
        // together — and a FRESH one is armed in the same breath, because the
        // next level's invitation arrives BEFORE that level's client LevelInit.
        //
        // Nothing crosses the boundary: the object the previous level's session
        // ran on is disposed and dropped, its task is waited for, and the
        // registration installed here closes over a NEW object with a new socket
        // and no history. What is re-armed is the ABILITY to accept an
        // invitation, which is what -debugui grants once for the process.
        TeardownDebugChannel();
        WireDebugChannel();
    }

    public void ConCommandDispatch(int commandId, string args)
    {
        if (!_commands.Dispatch(commandId, args))
            Log.Warning($"[SourceSharp/Client] no command bound to id {commandId}\n");
    }

    //-------------------------------------------------------------------------
    // IClientModule
    //-------------------------------------------------------------------------

    /// <summary>
    /// Runs once per user command. This is where player intent becomes game
    /// state, so it is the first real input decision owned by C#.
    ///
    /// Allocation-free: it runs every tick and a GC pause here is a frame hitch.
    /// </summary>
    public void OnCreateMove(ref UserCmd cmd, float inputSampleTime, bool active)
    {
        int commands = _stats.CountCommand();

        // One-shot proof the hook is live, without needing player input.
        if (commands == 1)
            Log.Msg($"[SourceSharp/Client] C# received its first user command (tick {cmd.TickCount})\n");
        else if (commands == 500)
            Log.Msg($"[SourceSharp/Client] C# has processed 500 user commands\n");

        // Proof it is also mutable: log the first jump C# observes.
        if (!_stats.LoggedFirstJump && cmd.IsPressed(InputButtons.Jump))
        {
            _stats.LoggedFirstJump = true;
            Log.Msg($"[SourceSharp/Client] C# saw +jump (cmd {cmd.CommandNumber}, yaw {cmd.Yaw:F1})\n");
        }
    }

    public void OnConVarChanged(int id, string newValue, string oldValue)
    {
        // A watched engine convar (IShimConVarsClient): the bounded convars' inputs.
        if (_boundedConVars is { } bounded && bounded.OnChanged(id))
            return;

        // The flashlight's ConVars, read through the same watch.
        if (_flashlightSettings is { } flashlight && flashlight.OnChanged(id, newValue))
            return;

        // The leaf system's (C7 cluster 12 L2): r_portalsopenall, the engine's.
        if (_leafConVars is { } leafConVars && leafConVars.OnChanged(id, newValue))
            return;

        // building_cubemaps, the engine's, which the pixel visibility and the glow overlays read (C7).
        if (_buildingCubemaps is { } buildingCubemaps && buildingCubemaps.OnChanged(id, newValue))
            return;

        if (!_convars.NotifyChanged(id, newValue, oldValue))
            Log.Warning($"[SourceSharp/Client] no convar bound to id {id}\n");
    }

    /// <summary>Hot reload, the way out. See ScriptedEntityRegistry.SaveForReload.</summary>
    public int ReloadSave()
    {
        // The mirror's records describe entities that outlive this module; see
        // StandaloneMirror.Save for what a fresh one would answer without them.
        _engine.Reload.Put(StandaloneMirror.ReloadSection, _mirror.Save(), 1);

        // The debug channel's resume data, for the reloaded module to redial
        // with (WireDebugChannel). Null removes any stale section.
        _reloadSections.Put(IDebugApiClient.ResumeReloadSection, _debugChannel?.SaveResume());

        // The services, beside the entities (plan_client_novalve §3.8).
        var participants = _reloadParticipants?.Save();

        int entities = _standalones.SaveForReload(_engine.Reload);

        // LAST: the ledger lists every section the save left, its own aside.
        _reloadLedger?.Save(participants?.Participants ?? 0, entities);
        return entities;
    }

    /// <summary>Hot reload, the way back in.</summary>
    public int ReloadRestore()
    {
        // FIRST: the old build's ledger, before anything takes its section.
        _reloadLedger?.BeginRestore();

        // BEFORE the entities, so a sequence an entity's LoadState plays lands
        // on top of the carried record rather than under it.
        byte[]? mirror = _engine.Reload.Take(StandaloneMirror.ReloadSection);

        if (mirror is not null && _mirror.Restore(mirror) < 0)
            Log.Warning("[SourceSharp/Client] the carried standalone mirror was refused; dormancy " +
                        "and sequence state start empty and fill as the engine pushes\n");

        // The services, before the entities: an entity's LoadState may ask one.
        var participants = _reloadParticipants?.Restore();

        int entities = _standalones.RestoreAfterReload(_engine.Reload);

        // LAST: the counts, the caches the ABI dropped, and the leak arm.
        _reloadLedger?.FinishRestore(participants, entities);
        return entities;
    }

    /// <summary>
    /// Save, the way out — the client's standalone entities through
    /// <see cref="SavedEntityCodec"/>. Size-then-fill, and 0 for a world with
    /// nothing in it; see <c>ServerModule.SaveCollect</c>, which is the same
    /// method over the other registry because there is one record type and one
    /// format for both sides.
    /// </summary>
    public int SaveCollect(Span<byte> destination)
    {
        var records = _standalones.Collect();

        return records.Count == 0 ? 0 : SavedEntityCodec.Write(records, destination, Log);
    }

    /// <summary>
    /// Save, the way back in. A refused block restores NOTHING: the registry is
    /// not reached at all on a refusal. An empty block is not a refusal — it is
    /// what a client with no standalone entities wrote.
    /// </summary>
    public int SaveRestore(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return 0;

        if (!SavedEntityCodec.TryRead(source, out var entities, out string? refusal))
        {
            Log.Warning($"[SourceSharp/Client] the save's entity block was refused " +
                        $"({refusal}); NOTHING has been restored\n");
            return -1;
        }

        return _standalones.RestoreFrom(entities);
    }

    /// <summary>
    /// One standalone player command, replayed (Phase 9c4's crossing). Phase 2
    /// is the frame's basis reset; phase 1 runs the weapon machine over the
    /// working states — the same call the server made for the same command.
    /// </summary>
    public void OnStandalonePlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime)
        => _clientArsenal?.OnPlayerCommand(player, in cmd, phase, firstTime);

    /// <summary>
    /// One client prediction frame (step 6f): <c>IPrediction::Update</c>,
    /// forwarded whole. THE ONE CROSSING the client's prediction costs —
    /// everything under it is managed calling managed.
    /// </summary>
    public void OnClientPredictionUpdate(int startFrame, bool validFrame,
                                         int incomingAcknowledged, int outgoingCommand,
                                         bool worldUpdateArrived)
    {
        if (_prediction is null)
            return;

        _prediction.Enabled = _predictConVar?.Value ?? true;
        _prediction.Update(startFrame, validFrame, incomingAcknowledged, outgoingCommand,
                           worldUpdateArrived);
    }

    /// <summary>
    /// plan_client_vtables CD: the client's managed root, built in the side's
    /// registry (which the Bootstrap owns and keeps across reloads). Native
    /// accepts it through its Validate and acquires the runtime from it.
    /// </summary>
    public nint BuildManagedRoot(ManagedVtables vtables)
    {
        _managedRoot = new ManagedRoot(_bindings
            ?? throw new InvalidOperationException("the managed root was asked for before Initialize"), vtables).Bind();

        // The client's IPhysicsCollisionEvent: every slot answered in managed
        // (the impacts, the scrapes and the splash by their sinks, the rest as
        // stock's client answered them); the physics system puts it on each
        // level's environment.
        // The objects vphysics is handed as its callbacks are libss_fwd_client's
        // (plan_forwarders_everywhere.md phase 5), each built in its block.
        PhysicsCallbackObjects? callbackObjects = PhysicsCallbackObjects.Acquire(_engine, out string callbacksWhy);
        if (callbackObjects is null)
            Log.Warning($"[SourceSharp/Client] no vphysics callbacks: {callbacksWhy}\n");

        if (_impactSounds is { } impacts && _physicsSystem is { } callbacks && callbackObjects is not null)
        {
            _physicsCollisionEvents = ClientPhysicsCollisionEventBinding.Create(vtables,
                new ClientPhysicsCollisionEventBinding(_engine.Crossings, impacts,
                                                       _fluidEvents ?? throw new InvalidOperationException("the fluid slots were built before the fluid entries"),
                                                       _friction ?? throw new InvalidOperationException("the friction slot was built before the scrapes"),
                                                       _contactTouches));
            callbacks.CollisionEvents = callbackObjects.CollisionEvent(_physicsCollisionEvents);
        }

        // The ragdolls' IVPhysicsKeyHandler tables (plan physics_bones_native_zero
        // Phase 3): the custom blocks' router and the solids' defaults.
        if (callbackObjects is not null)
            _ragdollPhysics?.InstallHandlers(vtables, _engine.Crossings, callbackObjects);

        // physics_shared.cpp's client half (2.6), over the same key table: the
        // client has one IVPhysicsKeyHandler contract, and it answers as
        // CSolidSetDefaults while a solid's parse is in flight.
        if (_ragdollPhysics?.Keys is { } keys)
        {
            // The static props' objects are the client's to make (stock's client
            // PhysicsLevelInit asked the static prop manager after the world).
            IStaticPropPhysics? staticProps = StaticPropMgrClient.Acquire(_engine, out string staticPropsWhy);
            if (staticProps is null)
                Log.Warning($"[SourceSharp/Client] physics: no static prop manager ({staticPropsWhy}); the static props make no objects\n");
            _physicsWorldVPhysics = PhysicsWorldVPhysics.Acquire(_engine, _services.Require<IClientPhysicsEngine>().Environment,
                                                                 staticProps, keys, out string worldWhy);
            IVtablePhysicsSurfaceProps? surfaceProps = PhysicsSurfaceProps.Acquire(_engine, out string propsWhy);
            if (_physicsWorldVPhysics is null || surfaceProps is null)
            {
                Log.Warning($"[SourceSharp/Client] no world physics build: {worldWhy} {propsWhy}\n");
            }
            else
            {
                IVtableSoundEmitter? emitter = SoundEmitterBase.Acquire(_engine, out string emitterWhy);
                if (emitter is null)
                    Log.Warning($"[SourceSharp/Client] the surface sounds' handles stay invalid: {emitterWhy}\n");

                SurfacePropertyManifest manifest = _services.Create<SurfacePropertyManifest>();
                _physicsWorld = new Physics.ClientPhysicsWorld(_physicsWorldVPhysics, _physicsWorldVPhysics,
                    () =>
                    {
                        int parsed = manifest.Load(surfaceProps);
                        Log.Msg($"[SourceSharp/Client] surface properties: {parsed} file(s) parsed\n");
                        return parsed;
                    },
                    Physics.ClientPhysicsWorld.ClientPrecache(emitter is null ? null : emitter.SoundIndex), keys);
            }
        }

        // The client's IPhysicsCollisionSolver: every one of its five answers
        // is ported (ClientCollisionSolver), so it replaces g_Collisions as the
        // solver whole.
        if (_collisionSolver is { } solver && _physicsSystem is { } solverCallbacks && callbackObjects is not null)
        {
            _physicsCollisionSolver = ClientCollisionSolverBinding.Create(vtables,
                new ClientCollisionSolverBinding(_engine.Crossings, solver));
            solverCallbacks.CollisionSolver = callbackObjects.CollisionSolver(_physicsCollisionSolver);
        }

        BuildSounds(vtables);

        ActivateSteam(vtables);

        return _managedRoot.Address;
    }

    /// <summary>
    /// The client's sound emitter (sound_emitter.cpp's client emit path,
    /// moved): the script table, the client's engine sound and engine, the
    /// file system, and the native IRecipientFilter every managed emit hands
    /// the engine, built in its block here. Without any of them nothing plays
    /// and the CSoundEmitterSystem row's Init is refused, as the native row's
    /// false stopped the client.
    /// </summary>
    private void BuildSounds(ManagedVtables vtables)
    {
        IVtableSoundEmitter? scripts = SoundEmitterBase.Acquire(_engine, out string scriptsWhy);
        IVtableEngineSoundClient? sound = EngineSoundClient.Acquire(_engine, out string soundWhy);
        IVtableEngineClient? engineClient = EngineClient.Acquire(_engine, out string clientWhy);
        IVtableBaseFileSystem? files = EngineBaseFileSystem.Acquire(_engine, out string filesWhy);
        RecipientFilterObject? filters = RecipientFilterObject.Acquire(_engine, out string filtersWhy);
        if (scripts is null || sound is null || engineClient is null || files is null || filters is null)
        {
            Log.Warning("[SourceSharp/Client] no sound emitter: "
                        + (scripts is null ? scriptsWhy : sound is null ? soundWhy : engineClient is null ? clientWhy
                           : files is null ? filesWhy : filtersWhy) + "\n");
            return;
        }

        var sink = new RecipientFilterSink();
        _recipientFilter = RecipientFilterSink.Create(vtables, sink);
        var engine = new ClientSoundEmitterEngine(scripts, sound, engineClient, sink, filters.Build(_recipientFilter));

        IEngineConsole console = _services.Require<IEngineConsole>();
        IEngineEnvironment environment = _services.Require<IEngineEnvironment>();
        IClientStandalones standalones = _services.Require<IClientStandalones>();
        IEngineEntityRender render = _services.Require<IEngineEntityRender>();

        // The speaker: the client entity at the index; "" for one with no model.
        string? SpeakerModel(int index)
            => standalones.GetByIndex(index) is { } speaker ? render.GetModelName(speaker.Handle) ?? string.Empty : null;

        var context = new Sounds.ClientSoundContext(
            () => environment.Globals.IsAvailable ? environment.Globals.MaxClients : 1,
            () => float.TryParse(console.FindConVarValue("closecaption"), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out float on) && (int)on != 0,
            engineClient.LocalPlayerSlot,
            () => _convars.Get<SvSoundEmitterTraceConVar>()?.Value == true,
            SpeakerModel);

        var emitter = new Sounds.ClientSoundEmitter(engine, _services.Require<IGameClock>(), context,
                                                    new Sounds.NoCloseCaptionHud(), text => Log.Msg(text),
                                                    DeveloperMessage);
        _clientSounds.Bind(emitter, new Sounds.ClientSoundEmitterSystem(new ClientSoundSystemEngine(scripts, engineClient, files)),
                           engine);
    }

    /// <summary><c>DevMsg</c>: the console line when <c>developer</c> is 1 or more.</summary>
    private void DeveloperMessage(string text)
    {
        if (int.TryParse(NativeConsole.ValueOf(_services.Require<IEngineConsole>(), "developer"), out int level)
            && level >= 1)
            Log.Msg(text);
    }

    /// <summary>
    /// CClientSteamContext::Activate (stock's CHLClient::Init called it), then
    /// the callbacks, each a block in the side's registry that Steam calls from
    /// the engine's SteamAPI_RunCallbacks.
    /// </summary>
    private void ActivateSteam(ManagedVtables vtables)
    {
        var steam = _services.Require<ISteamClientApi>();
        _steamContext = new Steam.ClientSteamContext(steam, Log);
        _steamContext.Activate();

        IVtableEngineClient? engineClient = EngineClient.Acquire(_engine, out string why);
        if (engineClient is not ISteamLaunchCommands commands)
        {
            Log.Warning($"[SourceSharp/Steam] no Steam callbacks: the engine client did not bind ({why})\n");
            return;
        }

        _steamCallbacks = SteamCallbacks.Acquire(_engine, vtables, steam, Log, out why);
        if (_steamCallbacks is null)
        {
            Log.Warning($"[SourceSharp/Steam] no Steam callbacks: the callback table is not served ({why})\n");
            return;
        }

        IEngineConsole console = _services.Require<IEngineConsole>();
        new Steam.ClientSteamCallbacks(_steamCallbacks, _steamContext,
            new Steam.SecureLaunch(steam, commands, Log,
                () => int.TryParse(console.FindConVarValue("developer"), out int level) ? level : 0)).RegisterAll();
    }

    /// <summary>
    /// CClientSteamContext::Shutdown and the callbacks' destructors: Steam lets
    /// go of every block before it is freed, so nothing Steam holds outlives
    /// this load.
    /// </summary>
    private void ShutdownSteam()
    {
        _steamCallbacks?.Dispose();
        _steamCallbacks = null;
        _steamContext?.Shutdown();
        _steamContext = null;
    }

    public void Shutdown()
    {
        // First: a warm-up thread still inside this context's code would keep
        // the collectible context from unloading (plan_map_load P5).
        _jitWarmup?.Dispose();
        _jitWarmup = null;

        // This load's last engine lines go to this load's listeners.
        _consoleLines?.Drain();

        // The blocks are the side registry's (plan_managed_vtable_reload.md
        // L4): native keeps them across a reload; the Bootstrap detaches or
        // frees them after this returns.
        _managedRoot = null;

        // Again here as well as at level shutdown, because a module can be
        // unloaded without a level ending — cs_sv_reload does exactly that — and
        // a task still on the stack is what stops a collectible load context
        // from unloading at all.
        TeardownDebugChannel();

        ShutdownSteam();

        SideServices.DisposeBuilt(_services, _modServices, Log);

        // The UI's context is collectible and names this module's assemblies:
        // left loaded it would hold this context alive across a client reload.
        _uiLoader?.Dispose();
        _uiLoader = null;
        _ui = null;

        // The world build's scratch solid_t and fluid_t (its debug names stay:
        // vphysics may keep them past this load).
        _physicsWorld = null;
        _physicsWorldVPhysics?.Dispose();
        _physicsWorldVPhysics = null;

        Log.Msg("[SourceSharp/Client] managed client module shutting down\n");

        // Last, so the line above still printed through the module's own sink;
        // from here the bootstrap's console has the side again.
        _descriptorAttachment?.Dispose();
        _descriptorAttachment = null;

        _logAttachment?.Dispose();
        _logAttachment = null;

    }

    /// <summary>Routes a fired game event to its declared handlers.</summary>
    public void DispatchGameEvent(string eventName, IntPtr handle)
        => _gameEvents.Dispatch(eventName, handle);

    /// <summary>
    /// How much of a jump a player gets (ABI slot). The answer is the side's
    /// resolved <see cref="MovementRules"/> pack — the same object the replay's
    /// loop was frozen with, and resolved the same way the server resolves
    /// its own, which is what keeps the predicted scales and the server's one
    /// opinion. The rule itself is shared, because the client predicts
    /// movement with the same one.
    /// </summary>
    public float GetJumpScale(in JumpRequest request) => _movementRules.Rules.Jump(request);

    public float GetFrictionScale(int playerIndex, float friction, float speed,
                                  bool ducking, int groundMaterial)
        => _movementRules.Rules.Friction(playerIndex, friction, speed, ducking, groundMaterial);

    public float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
        => _movementRules.Rules.MaxSpeed(playerIndex, maxSpeed, ducking, groundMaterial);

    //-------------------------------------------------------------------------
    // The client half of the standalone entity hierarchy.
    //-------------------------------------------------------------------------
    // Built in the constructor over this module's container, which is where
    // this side's engine areas go (plan_abi_hotreload D8).
    private readonly Standalone.ClientStandaloneRegistry _standalones;

    /// <summary>
    /// The engine area whose <c>InvalidateMdlCacheAll</c> is the one native
    /// step of a model-cache flush. Null before <c>Initialize</c>, which is a
    /// state a flush cannot legitimately arrive in — and is answered with 0
    /// rather than a throw, because unwinding into the engine's C++ frames is
    /// worse than a diagnostic that says it reached nothing.
    /// </summary>
    internal IEngineEntities? _mdlCacheWalk;

    /// <summary>
    /// How many times the engine has flushed the model cache since this module
    /// was built — the client's mirror of <c>ServerGameDll</c>'s counter.
    /// </summary>
    public int MdlCacheInvalidations { get; private set; }

    /// <summary>
    /// How many entities the LAST flush actually reached.
    ///
    /// <para>
    /// THIS IS THE NUMBER THAT WAS ZERO, and no counter of flushes could say
    /// so: <c>CShellClientDLL::InvalidateMdlCache</c> walked
    /// <c>ClientEntityList().FirstBaseEntity()</c>, which answers
    /// <c>C_BaseEntity</c>, and no managed entity on this side is one.
    /// </para>
    /// </summary>
    public int MdlCacheEntitiesInvalidated { get; private set; }

    /// <inheritdoc/>
    public string? MapMaterialReloadSet(string nextBsp, string previousBsp)
        => _services.Require<ClientMapMaterials>().ReloadSet(nextBsp, previousBsp);

    /// <inheritdoc/>
    public bool ParticlesNeedFullReload(int previousMapParticleFiles)
        => _services.Require<ParticleReloadPolicy>().NeedsFullReload(previousMapParticleFiles);

    /// <inheritdoc/>
    public void InvalidateMdlCache()
    {
        MdlCacheInvalidations++;

        // THE WALK IS THE ONE NATIVE STEP, on the server's precedent and for
        // the server's reason: what the SDK half of it frees is
        // C_BaseAnimating::m_pStudioHdr, a native member freed by a native
        // inline, and the parallel half clears m_CachedBones, a CUtlVector of
        // matrix3x4_t. Neither has a managed expression. WHETHER, WHEN and what
        // to SAY are here.
        MdlCacheEntitiesInvalidated = _mdlCacheWalk?.InvalidateMdlCacheAll() ?? 0;

        // The managed skeletons' images were copied from the cache just flushed,
        // and the skeletons are the ones the flush reaches on this side.
        MdlCacheEntitiesInvalidated += _skeletons?.InvalidateMdlCache() ?? 0;

        // AND WHAT IT REACHED, BY CLASS -- which is the whole point of the lane
        // this arrived with. A diagnostic that silently omits managed entities
        // is how a lane concludes "nothing was created" and goes looking for a
        // bug that is not there, so this names the population rather than
        // asserting a count: the directory is the authority on which managed
        // client entities exist (plan_entity.md 3.5), and it is asked here.
        //
        // The classes are SORTED so two runs are diffable, and the whole line is
        // built once -- a flush is rare (two FCVAR_CHEAT commands and a model
        // reload), so this is a measurement and not per-frame work.
        var directory = _standalones.Directory;

        Log.Msg(DescribeMdlCacheFlush(MdlCacheInvalidations, MdlCacheEntitiesInvalidated,
                                      directory.Count, CensusOf(directory)));
    }

    /// <summary>
    /// Every class the directory currently holds, with how many are in it.
    ///
    /// <para>
    /// <c>ClassNames</c> deletes a bucket when it empties, so this is "what is
    /// here now" and never "what has ever been here" — which is the property
    /// that makes a zero in this list mean something.
    /// </para>
    ///
    /// <para>
    /// UNORDERED HERE ON PURPOSE: the order is part of what the line PROMISES,
    /// so it belongs to the thing that prints the line and is asserted there.
    /// A dictionary's key order is not a promise.
    /// </para>
    /// </summary>
    private static List<(string ClassName, int Count)> CensusOf(ClientEntityDirectory directory)
    {
        var census = new List<(string ClassName, int Count)>();

        foreach (var className in directory.ClassNames)
            census.Add((className, directory.OfClass(className).Count()));

        return census;
    }

    /// <summary>
    /// The line a model-cache flush prints — pure, so the thing this lane is
    /// about can be tested without a running client.
    ///
    /// <para>
    /// <b>WHAT IT HAS TO SAY, AND WHY EACH PART IS THERE.</b> The defect was
    /// that a flush reached ZERO entities while looking healthy, so a receipt
    /// ("flushed") is not enough and neither is a count of flushes: the line
    /// carries the number the walk REACHED, and the population the directory
    /// holds, and it NAMES the classes. A reader can tell "the walk found
    /// nothing because there is nothing" from "the walk found nothing and here
    /// are the forty-one entities it missed", which is the distinction the old
    /// silence made impossible.
    /// </para>
    /// </summary>
    internal static string DescribeMdlCacheFlush(int flushNumber, int invalidated, int held,
                                                 IReadOnlyList<(string ClassName, int Count)> census)
    {
        // SORTED HERE, so two runs of the same map are diffable and so the
        // ordering is a property of the line rather than of whatever container
        // the caller happened to walk.
        var ordered = new List<(string ClassName, int Count)>(census);
        ordered.Sort(static (a, b) => string.CompareOrdinal(a.ClassName, b.ClassName));

        var line = new System.Text.StringBuilder();

        line.Append("[SourceSharp/Client] model cache flushed (#").Append(flushNumber).Append("): ")
            .Append(invalidated).Append(" entit").Append(invalidated == 1 ? "y" : "ies")
            .Append(" invalidated; the directory holds ").Append(held)
            .Append(" managed entit").Append(held == 1 ? "y" : "ies")
            .Append(" in ").Append(census.Count).Append(" class(es)");

        for (int i = 0; i < ordered.Count; i++)
        {
            line.Append(i == 0 ? ": " : ", ")
                .Append(ordered[i].ClassName).Append('(').Append(ordered[i].Count).Append(')');
        }

        return line.Append('\n').ToString();
    }

    public bool StandaloneCreate(EntityHandle entity, string className)
        => _standalones.Create(entity, className);

    public void StandaloneSpawn(EntityHandle entity) => _standalones.Get(entity)?.OnSpawn();

    /// <summary>
    /// Draws an entity that asked to draw itself. Once per frame, per entity —
    /// so this resolves and dispatches and does nothing else.
    /// </summary>
    public int StandaloneDraw(EntityHandle entity) => _standalones.Get(entity)?.OnDraw() ?? 0;

    public void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results)
    {
        _drawBatch ??= _convars.Get<Render.DrawBatchConVar>();
        if (!(_drawBatch?.Value ?? true))
            return;
        for (int i = 0; i < handles.Length; i++)
            results[i] = _standalones.Get(new EntityHandle(handles[i]))?.OnDrawBatched() ?? -1;
    }

    public void StandaloneRemoved(EntityHandle entity)
    {
        // Before the registry forgets it, so the cursor cannot outlive the
        // entity: handles are recycled, and a cursor left behind would be
        // handed to whatever entity the number came back as.
        _animEvents?.Forget(entity);
        _standalones.Remove(entity);
    }

    /// <summary>
    /// Hands a hierarchy entity its own replicated state. The span is borrowed
    /// native memory and does not outlive this call.
    /// </summary>
    public void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType)
    {
        // FIRST, and unconditionally — before the class gets its state and
        // whether or not the registry knows this entity yet.
        //
        // This is the only moment managed can learn that the entity's MODEL may
        // have moved: the model index is networked into the shell's own member
        // and never reaches managed, so the memo of that model's event tables
        // is invalidated on the update rather than on the change it cannot see.
        // A model cannot change without a net update, and OnDataChanged issues
        // this push on every update. plan_crossings.md Phase X5.
        _animEvents?.NoteDataChanged(entity);

        _standalones.Get(entity)?.DispatchDataChanged(blob, (DataUpdateType)updateType);
    }

}
