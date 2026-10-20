using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Tests;

/// <summary>The server half of <see cref="TestServices"/>: the doubles and services only a project that sees the server can build.</summary>
public static partial class TestServices
{
    /// <summary>
    /// The server's game assembly, where a server registry's scan starts: its
    /// own declarations and, compiled into it, the shared source's
    /// (plan_side_isolation P7). What <c>typeof(SharedInfo).Assembly</c> named
    /// before the fold, when the shared declarations were an assembly of their own.
    /// </summary>
    public static System.Reflection.Assembly ServerGame => typeof(ServerModule).Assembly;

    /// <summary>A real impact-damage queue over <paramref name="engine"/>, for a player built by hand.</summary>
    public static SourceSharp.Server.PhysicsDamageQueue Impacts(RecordingEngine engine)
        => new(engine, engine, engine, new EntityDirectory(new RecordingLog()));

    /// <summary>A fluid set over surfaces lighter than water, for a player built by hand.</summary>
    public static SourceSharp.Server.FluidTouches Fluid() => new(new FixedSurfaceDensity(500f));

    /// <summary>
    /// A button source that reports nothing held.
    ///
    /// Players takes IPlayerButtons because the death cycle's "is this player
    /// asking to come back" rule moved onto it — see Players.WantsToRespawn. A
    /// test that does not exercise respawn intent wants the constructor
    /// satisfied and no held buttons, which is this.
    /// </summary>
    public static IPlayerButtons NoButtons => IdleButtons.Instance;

    private sealed class IdleButtons : IPlayerButtons
    {
        public static readonly IdleButtons Instance = new();
        public int Held(EntityHandle player) => 0;
    }

    /// <summary>
    /// The console of a server side no native module is attached to: what a
    /// <see cref="ConVarRegistry"/> registers through in a test that wants
    /// the engine's "no engine" answers (every registration refused, -1),
    /// from a side of its own rather than the process's (plan_abi_hotreload D8).
    /// </summary>
    public static IEngineConsole UnattachedConsole()
        => ScriptedEntityRegistry.UnattachedEngine().Require<IEngineConsole>();

    /// <summary>
    /// A restore's handle re-check over a side of the test's own with no
    /// native module attached: every handle comes back as written, the
    /// hermetic answer (plan_abi_hotreload D8).
    /// </summary>
    public static SavedHandles UnattachedHandles()
    {
        var engine = ScriptedEntityRegistry.UnattachedEngine();
        return new SavedHandles(engine.Require<IEngineHost>(), engine.Require<IEngineEntities>());
    }

    static partial void AddSideServices(ServiceContainer services, RecordingEngine engine)
    {
        // The constraint, spring and motion-controller tables
        // (plan_physics_bones_native_zero 2.4): the server's own now rather
        // than engine areas, recorded into the same double under the names
        // they had, so a joint an entity builds lands in the same ordered log.
        var joints = new RecordingJointTables(engine);
        services.AddInstance<IEngineConstraints>(joints);
        services.AddInstance<IEngineSprings>(joints);
        services.AddInstance<IEngineMotionControllers>(joints);

        services.AddInstance<SourceSharp.Server.IScreenEffects>(
            new SourceSharp.Server.ScreenEffects(engine, engine, engine, engine));

        // The world's noise record — plan_gamedll_after.md Phase A3, second
        // pair. Two map entities take it now (env_microphone reads it, ai_sound
        // writes it) where they used to take IEngineSound, because
        // game/server/soundent.cpp left server_csharp.vpc and the list is
        // managed.
        //
        // A REAL ONE, not a double, and that is the choice worth stating. It is
        // a pure data structure with two dependencies that are already doubles
        // here, so a test that spawns a microphone gets a microphone reading a
        // REAL empty list — which is the state a map starts in — rather than a
        // recorded call it then has to interpret. The alternative was a mock
        // whose Query always answers zero, which is the same answer with none
        // of the behaviour behind it.
        // WHAT AN ENTITY ASKED AN EXPLOSION FOR (shim wave, lane policy-rules).
        // A RECORDER and not a real BlastSweep, which is the opposite choice
        // from SoundEnt and EntityMotion above and is made for the reason those
        // two state in reverse: a real sweep would answer these tests with
        // whatever the engine double happens to say a sphere query found, so a
        // fact about what a ROCKET asks for would depend on the world. The
        // traversal's own arithmetic is pinned by BlastRulesTests, which needs
        // no engine at all.
        //
        // Reachable with services.Resolve( typeof( IBlastSweep ) ).
        services.AddInstance<IBlastSweep>(new RecordingBlastSweep());

        // The step's impact damage (physics stock parity C7): a REAL queue over
        // the double, for SoundEnt's reason above -- a prop or player a test
        // spawns queues into it, and the test delivers it.
        services.AddInstance(new SourceSharp.Server.PhysicsDamageQueue(engine, engine, engine,
                                                                       new EntityDirectory(new RecordingLog())));

        // Which entities touch fluid (physics stock parity I2), over surfaces
        // that are all lighter than water: a test that puts a prop in fluid
        // gets a floating prop.
        services.AddInstance(new SourceSharp.Server.FluidTouches(new FixedSurfaceDensity(500f)));

        // The queued impact and break sounds (plan_physics_bones_native_zero
        // 2.3), a REAL batch over a world that records what it is asked into
        // the double's log: a breaking prop is seen asking for its break source.
        services.AddInstance(new SourceSharp.Server.PhysicsSoundBatch(new RecordingPhysicsSoundWorld(engine)));

        // The scrape (D2), over the double and a controller that plays
        // nothing: a test that slides a prop reaches the policy, and
        // FrictionSoundsTests reads the controller's side itself.
        services.AddInstance(new SourceSharp.Shared.FrictionSounds(engine, new SilentSoundController(),
                                                                   new SourceSharp.Shared.EnginePhysicsSurfaceTable(
                                                                       EngineSide.Create(Side.Server).Surfaces),
                                                                   new SourceSharp.Server.TempEntFrictionEffects(engine,
                                                                       new SourceSharp.Server.TempEnts(new RecordingLog(), new TempEntRegistry(new RecordingLog()), engine))));

        // The players' shadows after the step (B7), over the double.
        // The physics shadows (plan_physics_bones_native_zero 2.5): a double
        // that logs into the engine's ordered log, so a spawn is seen asking
        // for a body ("Shadows.Init") and the recovery's probe is a stub
        // ("Shadows.Probe").
        var shadows = new RecordingPlayerShadows(engine);
        services.AddInstance<IPlayerShadows>(shadows);
        services.AddInstance(new SourceSharp.Server.PlayerShadowRecovery(engine, engine, engine, shadows,
                                                                         new EntityDirectory(new RecordingLog())));
        // The entity pairs the solver refuses (2.1), over a world where every
        // handle names a live entity with its own object: a test that disables
        // a pair forms one.
        var solverWorld = new OpenSolverWorld();
        var pairs = new SourceSharp.Server.EntityPairCollisions(solverWorld, new FakeSolverObjects());
        services.AddInstance(pairs);
        services.AddInstance(new SourceSharp.Server.PhysicsEntitySolver(
            new SourceSharp.Server.EngineEntitySolverWorld(engine, engine, new SourceSharp.Abi.VPhysicsObjects(TestVPhysics.None()), pairs), engine));

        // The decal system, for the same reason: what an explosion painted is
        // a group name asked for and an index arranged. Reachable with
        // services.Resolve( typeof( IServerDecals ) ).
        var decals = new RecordingDecals();
        services.AddInstance<IServerDecals>(decals);
        services.AddInstance(new BloodDecals(engine, decals, engine, engine));
        services.AddInstance(new SourceSharp.Server.Interop.SolverHeldOverlaps());

        services.AddInstance<IServerSoundEnt>(new SoundEnt(engine, engine, engine));

        // The side's seed source for entity-owned random streams (logic_case,
        // logic_timer, ambient_generic). A REAL one with a FIXED seed, so a
        // test whose entity picks at random picks the same way every run.
        services.AddInstance<IRandomSeeds>(new RandomSeeds(20260929));

        // propdata.txt's table (plan_server_novalve.md P2.4). A REAL one,
        // unloaded, for SoundEnt's reason: a prop's health is parsed from its
        // model's $keyvalues text, which a test stubs as
        // EntityRender.GetModelKeyValueText, and what the test then checks is
        // the real parse rather than a recorded answer.
        services.AddInstance<IServerPropData>(new PropData(new RecordingLog(), engine, engine, engine));

        // The movement integrators (plan_entity.md step 2f). A REAL one, for
        // SoundEnt's reason: every flying projectile takes it, a launch asks
        // the engine nothing, and what a test then wants to know is what the
        // projectile TOLD it — which a real object answers and a double would
        // only record.
        //
        // Its directory is its OWN and not the registry's, which the container
        // has no way to hand it here. That is enough for what these tests use
        // it for (a launch, and the state it records) and NOT enough to run a
        // frame through: EntityMotionTests builds its own with a directory it
        // binds entities into, which is what a frame needs.
        var motionDirectory = new EntityDirectory(new RecordingLog());
        services.AddInstance(new EntityMotion(new RecordingLog(), engine, engine, engine, engine, motionDirectory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, motionDirectory), new RecordingLog())));

        // The cross-level global state table (plan_entity.md Phase 4 exit).
        // A REAL one, for SoundEnt's reason and one more: two map entities take
        // it (logic_auto and multisource both gate their firing on a global) and
        // what a test wants to know is whether the GATE works. A double that
        // answered Off to everything would make both entities look correct while
        // never firing, and a double that answered On would make the gate
        // untestable -- the two failure modes this table's own silent loss had.
        //
        // It has no dependencies at all: a list, a dictionary and a bool.
        services.AddInstance<IServerGlobalState>(new GlobalStateTable(new RecordingLog()));

        // What the game sends as tempents, recorded rather than run. Every
        // entity that dispatches an effect (the crossbow bolt's stuck-bolt
        // "BoltImpact" is the first) is constructed through this container,
        // and a fact about a sent effect is only observable at this seam --
        // the same reason the blast sweep above is a recorder and not a real
        // sweep: a real TempEnts would answer with whatever the engine double
        // says, so a fact about WHAT was sent would depend on the world. The
        // recording is empty by default, which is the state a test that never
        // dispatches expects; the tests that care read the list
        // (BoltImpactTests). The dispatch half itself is pinned by
        // TempEntsTests, which drives the real TempEnts over its own engine.
        services.AddInstance<SourceSharp.Shared.ITempEnts>(new RecordingEffectDispatch());
    }
}

/// <summary>Every surface at one density, for the fluid touches the test services carry.</summary>
public sealed class FixedSurfaceDensity(float density) : SourceSharp.Server.ISurfaceDensity
{
    public float DensityOf(int surfaceProps) => density;
}

/// <summary>A sound controller that plays nothing, for the test services' FrictionSounds.</summary>
public sealed class SilentSoundController : IServerSoundController
{
    public void Create(int patch) { }
    public void Destroy(int patch) { }
    public void Play(int patch, float volume, float pitch, float startTime) { }
    public void ChangePitch(int patch, float target, float deltaTime) { }
    public void ChangeVolume(int patch, float target, float deltaTime) { }
    public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) { }
    public void Shutdown(int patch) { }
    public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) { }
    public void CommandClear(int patch) { }
    public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => 0f;
    public void SystemReset() { }
    public float GetPitch(int patch) => 0f;
    public float GetVolume(int patch) => 0f;
}

/// <summary>
/// A solver world where every handle but None names a live entity with
/// default facts, and its object is 0x1000 plus its entry. Facts and owners a
/// test sets win. The world object is <see cref="World"/>.
/// </summary>
public sealed class OpenSolverWorld : SourceSharp.Server.IPhysicsSolverWorld
{
    public const nint World = 0xF000;

    public readonly Dictionary<EntityHandle, SourceSharp.Server.SolverEntity> Facts = new();
    public readonly HashSet<EntityHandle> Gone = new();

    public nint WorldObject() => World;

    public bool TryEntity(EntityHandle entity, out SourceSharp.Server.SolverEntity facts)
    {
        if (entity == EntityHandle.None || Gone.Contains(entity))
        {
            facts = default;
            return false;
        }

        if (!Facts.TryGetValue(entity, out facts))
            facts = new SourceSharp.Server.SolverEntity(0, 6, 0, 6, false, EntityHandle.None, EntityHandle.None);
        return true;
    }

    public nint PhysicsObjectOf(EntityHandle entity)
        => TryEntity(entity, out _) ? ObjectOf(entity) : 0;

    public static nint ObjectOf(EntityHandle entity) => 0x1000 + entity.EntryIndex;
}

/// <summary>
/// The player shadows as the double sees them: each ask recorded in the
/// engine's ordered log and answered from its stubs.
/// </summary>
public sealed class RecordingPlayerShadows(RecordingEngine engine) : IPlayerShadows
{
    public bool Init(EntityHandle player, Vector standMins, Vector standMaxs, Vector crouchMins, Vector crouchMaxs,
                     float mass, float pushMassLimit, float pushSpeedLimit)
        => engine.RecordAnswer<bool>("Shadows.Init", player, standMins, standMaxs, crouchMins, crouchMaxs,
                                     mass, pushMassLimit, pushSpeedLimit);

    public void Teleport(EntityHandle player, Vector origin, bool zeroVelocity)
        => engine.RecordCall("Shadows.Teleport", player, origin, zeroVelocity);

    public PlayerShadowState? Probe(EntityHandle player) => engine.RecordAnswer<PlayerShadowState?>("Shadows.Probe", player);
}

/// <summary>
/// The physics sounds' world as the double sees it: every ask recorded into
/// the engine's ordered log, and no object, so nothing is queued.
/// </summary>
public sealed class RecordingPhysicsSoundWorld(RecordingEngine engine) : SourceSharp.Server.IPhysicsSoundWorld
{
    public Vector? ObjectPosition(EntityHandle entity)
    {
        engine.RecordCall("PhysicsSounds.ObjectPosition", entity);
        return null;
    }

    public (Vector Origin, int Material)? BreakSource(EntityHandle entity)
    {
        engine.RecordCall("PhysicsSounds.BreakSource", entity);
        return null;
    }

    public SourceSharp.Abi.SurfaceImpactSounds? Surface(int surfaceProps) => null;

    public bool Emit(string soundName, Vector origin, int channel, float volume)
    {
        engine.RecordCall("PhysicsSounds.Emit", soundName, origin, channel, volume);
        return true;
    }
}

/// <summary>
/// Physics objects as a test sets them: game data, flags and the rest by
/// object, and every call that changes one recorded.
/// </summary>
public sealed class FakeSolverObjects : SourceSharp.Server.Interop.IVPhysicsSolverObjects
{
    public sealed class Obj
    {
        public nint GameData;
        public uint GameFlags;
        public uint Contents = 0xFFFFFFFFu;
        public bool Moveable = true;
        public bool Static;
        public bool Trigger;
        public bool Shadow;
        public ushort CallbackFlags;
        public float Mass = 10f;
        public SourceSharp.Server.Interop.SnapshotContact[] Contacts = [];
    }

    public readonly Dictionary<nint, Obj> Objects = new();
    public readonly List<string> Calls = new();

    /// <summary>The object, made on first ask, owned by <paramref name="owner"/>.</summary>
    public Obj Owned(nint physicsObject, EntityHandle owner)
    {
        Obj o = At(physicsObject);
        o.GameData = unchecked((nint)(SourceSharp.Abi.PhysicsGameData.Tag
                                      | (owner.Value + SourceSharp.Abi.PhysicsGameData.Bias)));
        return o;
    }

    public Obj At(nint physicsObject)
    {
        if (!Objects.TryGetValue(physicsObject, out Obj? o))
            Objects[physicsObject] = o = new Obj();
        return o;
    }

    /// <summary>Every read of an object made through this double, GameData included: the cost a fact can count.</summary>
    public int Reads { get; private set; }

    private Obj? Get(nint o)
    {
        Reads++;
        return Objects.TryGetValue(o, out Obj? obj) ? obj : null;
    }

    public nint GameData(nint o) => Get(o)?.GameData ?? 0;
    public uint GameFlags(nint o) => Get(o)?.GameFlags ?? 0;
    public void SetGameFlags(nint o, uint flags) { At(o).GameFlags = flags; Calls.Add($"SetGameFlags {o:x} {flags:x}"); }
    public uint Contents(nint o) => Get(o)?.Contents ?? 0xFFFFFFFFu;
    public bool IsMoveable(nint o) => Get(o)?.Moveable ?? true;
    public bool IsStatic(nint o) => Get(o)?.Static ?? false;
    public bool IsTrigger(nint o) => Get(o)?.Trigger ?? false;
    public bool HasShadowController(nint o) => Get(o)?.Shadow ?? false;
    public ushort CallbackFlags(nint o) => Get(o)?.CallbackFlags ?? 0;
    public float Mass(nint o) => Get(o)?.Mass ?? 10f;
    public void RecheckCollisionFilter(nint o) => Calls.Add($"Recheck {o:x}");
    public void Sleep(nint o) => Calls.Add($"Sleep {o:x}");
    public void ClearVelocityAndStrain(nint o) => Calls.Add($"Clear {o:x}");

    public int Contacts(nint o, Span<SourceSharp.Server.Interop.SnapshotContact> into)
    {
        var all = Get(o)?.Contacts ?? [];
        for (int i = 0; i < all.Length && i < into.Length; i++)
            into[i] = all[i];
        return all.Length;
    }
}

