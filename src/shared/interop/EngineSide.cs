using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// ONE SIDE's engine API: what the static <c>Engine</c> was, as an instance
/// (plan_engineapi_vtables Phase B1). Each side's load builds its own
/// (plan_abi_hotreload Phase 1: SideLoad, held by the session; the static that
/// held one per side for the process is deleted), uninitialised until the
/// load fills it, so every member answers its defaults before then.
///
/// <para>
/// The static <c>Engine</c> keeps a forwarder per member, choosing the
/// instance by the thread's active side, so every existing caller still
/// compiles. Phase B moves callers onto an injected instance (or the contract
/// it implements) and deletes forwarders; the count of forwarders is the
/// ratchet, and it ends at zero.
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    // What this side's load attached to (docs/plans/plan_engineside_removal.md
    // Phase 1): the root, the areas, the bound interfaces and the factory are
    // the attachment's, and composition reads them there.
    private readonly SideAttachment _attachment;
    private nint _globals;
    private bool _globalsDecided;

    // The areas the side's OWN services read: the ones made before any
    // composition exists, with the side (its log, its globals and clock, its
    // model descriptors and break models, its entity liveness and the shell's
    // velocity host). Assigned at each attach, from the same registration, and
    // pinned by ShimAreaInjectionCensusTests so this does not grow back into a
    // container: an area a binding calls is that binding's.
    //
    // These, the registry and Surfaces are null only inside Create, which
    // hands them the detached areas before anything can hold the side.
    private IConsoleArea _consoleArea = null!;
    private IEngineArea _engineArea = null!;
    private IEntitiesArea _entitiesArea = null!;
    private IModelsArea _modelsArea = null!;

    // A constructor only saves what it is handed (phase 11 review): Over
    // builds the rest.
    private EngineSide(SideAttachment attachment)
    {
        _attachment = attachment;
    }

    /// <summary>
    /// The side's services and helpers over <paramref name="attachment"/>: its
    /// own areas taken now, from the attachment's detached or default ones,
    /// and again at each attach.
    /// </summary>
    internal static EngineSide Over(SideAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var engine = new EngineSide(attachment);
        engine.UseAreas(attachment.Areas);
        attachment.WhenAttached(() => engine.UseAreas(attachment.Areas));
        return engine;
    }

    /// <summary>What this side's load attached to (Phase 1 of plan_engineside_removal).</summary>
    internal SideAttachment Attachment => _attachment;


    /// <summary>
    /// Every line this side's console prints, for the dev API's tap and a
    /// console command's capture (statics to zero: it was the static Log's
    /// tap and LogCapture).
    /// </summary>
    internal ConsoleLines Lines { get; } = new();

    /// <summary>
    /// This side's log, over its own console (statics to zero: it was the
    /// static Log): what its composition registers as <see cref="ILog"/> and
    /// what this side's own caches warn through.
    /// </summary>
    internal ILog Log => _log ??= new SideLog(this);

    private ILog? _log;

    private sealed class SideLog(EngineSide side) : ILog
    {
        public void Msg(string message) => side.Msg(message);

        public void Warning(string message) => side.Warning(message);
    }

    /// <summary>
    /// This side's vphysics forwarder tables (plan_engine_forwarders.md), bound
    /// from its own interface factory on first use: what every vphysics service
    /// of the side calls objects through.
    /// </summary>
    internal VPhysicsTables VPhysics => _vphysics ??= new VPhysicsTables(Interfaces);

    private VPhysicsTables? _vphysics;


    /// <summary>The attachment's interface factory (until its users take it by constructor; plan Phase 2).</summary>
    internal NativeInterfaceFactory Interfaces => _attachment.Interfaces;

    // plan_abi_hotreload D7: the three routes up into the side's registry are
    // this side's own, not statics. EngineServices.AddTo registers them in the
    // side's container; the registry binds into them and the frame driver,
    // the hierarchy and the server's ABI bindings are handed them.

    /// <summary>Where this side's shell-less entities are answered from.</summary>
    internal ShellLessRoutes ShellLess { get; } = new();

    /// <summary>Where this side's entity inputs and I/O service pass go.</summary>
    internal EntityIoRoutes EntityIo { get; } = new();

    /// <summary>Where this side's removals are marked.</summary>
    internal EntityRemovalRoutes Removal { get; } = new();

    // plan_abi_hotreload D8: this side's model caches are its composition's
    // own (EngineServices.AddTo builds them over the side's model info, its
    // model names below and its OwnModelPrecache), and the bindings that read
    // them are handed them by constructor.

    /// <summary>
    /// The handle a trace may be told to skip: <see cref="EntityHandle.None"/>
    /// for one of this side's shell-less entities, which native has never
    /// heard of, and the handle itself otherwise.
    /// </summary>
    /// <remarks>
    /// <b>INTERNAL rather than private</b> so the substitution itself is a
    /// fact and not a comment: with no engine, <c>TraceLine</c> returns before
    /// it reaches this, so a test that went through the trace could not tell a
    /// working substitution from a missing one. On this side and not on the
    /// static <c>Engine</c> since plan_abi_hotreload D7, so a trace asks its
    /// OWN side's route.
    /// </remarks>
    internal uint Skippable(EntityHandle handle)
        => ShellLess.Claims(handle) ? EntityHandle.None.Value : handle.Value;

    /// <summary>
    /// Finds an entity by the name a mapper gave it. Returns an invalid handle
    /// if nothing has that name, which a caller must check — a map naming a
    /// target that does not exist is a common mistake.
    /// </summary>
    public EntityHandle FindEntityByName(string name)
    {
        // THE DIRECTORY ANSWERS -- entity names move to managed. Its one name
        // index holds shelled and shell-less entities alike, and FirstByName
        // gives the old two-walk order (shelled in creation order, then
        // shell-less by entry): 107 of 107 queries on dm_lockdown.
        //
        // The native SourceSharp_FindEntityByName walk is no longer asked: a
        // full live gate at 2684d5b (the directory first, the walk as a logged
        // fallback) found it answering nothing the directory did not.
        return ShellLess.Mine?.FindFirstNamed(name) ?? EntityHandle.None;
    }

    /// <summary>
    /// Every entity with a name, not just the first.
    ///
    /// Sharing a targetname is legal and deliberate in Source — a map fires one
    /// input at six lights by giving them all the same name — so anything acting
    /// on "the entity called X" is usually wrong about how many there are.
    ///
    /// Bounded, because the walk is only as safe as the list it walks: an entity
    /// that renames itself while being enumerated would otherwise loop forever.
    /// </summary>
    public List<EntityHandle> FindEntitiesByName(string name, int limit = 512)
    {
        var found = new List<EntityHandle>();

        if (name.Length == 0)
            return found;

        // The directory, in the old composition's order (AllByName).
        ShellLess.Mine?.FindAllNamed(name, found, limit);
        return found;
    }

    /// <summary>The side's own areas from <paramref name="areas"/>: the attachment's detached ones at Over, the root's at each attach.</summary>
    private void UseAreas(IServiceLocator areas)
    {
        _consoleArea = areas.Require<IConsoleArea>();
        _engineArea = areas.Require<IEngineArea>();
        _entitiesArea = areas.Require<IEntitiesArea>();
        _modelsArea = areas.Require<IModelsArea>();
        OwnAreas(areas);
    }

    // plan_abi_hotreload D7: the liveness mirror EntityIsValid answers from is
    // the server composition's, attached while it is composed and taken back
    // at its Shutdown. Null means every ask crosses to the slot.
    private LivenessMirror? _liveness;

    /// <summary>The mirror attached now, for a fact; null when none is.</summary>
    internal LivenessMirror? Liveness => _liveness;

    /// <summary>
    /// Has <see cref="EntityIsValid"/> answer from <paramref name="mirror"/>
    /// until the returned attachment is disposed, which puts back whatever was
    /// attached before, and only if this mirror is still the one attached.
    /// </summary>
    internal IDisposable AttachLiveness(LivenessMirror mirror)
    {
        var previous = _liveness;
        _liveness = mirror;
        return new LivenessAttachment(this, mirror, previous);
    }

    private sealed class LivenessAttachment(EngineSide side, LivenessMirror mirror, LivenessMirror? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (ReferenceEquals(side._liveness, mirror))
                side._liveness = previous;
        }
    }

    /// <summary>
    /// This side's surfaceprop table, over this side's routing. It replaced the
    /// two MoveSurface routers PhysicsSurfaces.Of reached through the static
    /// Engine.Current; EngineServices registers it in the side's container.
    /// Made again at the attach, over the attached movement area, which is
    /// before any composition registers it.
    /// </summary>

    internal Side Side => _attachment.Side;


    /// <summary>
    /// This side's CGlobalVars, or 0 until the engine has answered with one that
    /// passes <see cref="RefuseGlobalVars"/>. Asked once, lazily.
    /// </summary>
    internal nint GlobalVarsPointer
    {
        get
        {
            if (!_globalsDecided)
                BindGlobalVars(_attachment.Side, AskForGlobalVars());

            return _globals;
        }
    }

    internal void BindGlobalVars(Side side, nint pointer)
    {
        string? refusal = pointer != 0 ? RefuseGlobalVars(pointer, side) : null;
        nint accepted = refusal is null ? pointer : 0;

        _globals = accepted;
        _globalsDecided = pointer != 0;

        if (refusal is not null)
        {
            Warning($"[SourceSharp] the {side} side's CGlobalVars pointer was REFUSED: {refusal}. "
                  + "The clock stays on the ABI slot, which is correct and slower.\n");
            return;
        }

        if (accepted != 0)
            ReportAgreement(side, accepted);
    }


    /// <summary>
    /// One crossing, for the whole process, per side. Zero when this side has no
    /// table yet or the shim is older than the slot.
    /// </summary>
    private nint AskForGlobalVars()
        => _engineArea.GetGlobalVarsPtr();

    /// <summary>
    /// The one line that makes the whole phase checkable in a running game:
    /// the ABI slot's answer and the load's answer, taken back to back at the
    /// same instant, printed side by side.
    ///
    /// <para>
    /// <b>This is the safety argument, measured rather than asserted.</b> The
    /// claim is not "the load is fresh enough" — it is that the load and the
    /// slot are THE SAME MEMORY, so there is no version of the value that can
    /// be stale. If the mirror's offsets were wrong the two would disagree
    /// here, in the log, on the first frame, instead of handing the game a
    /// plausible number for the rest of the session. Once per side per process;
    /// this runs on the first clock read and never again.
    /// </para>
    ///
    /// <para>
    /// It reads the SLOTS directly rather than the public properties beside
    /// them, which now take the load path — comparing the load with itself
    /// would agree forever and prove nothing.
    /// </para>
    ///
    /// <para>
    /// <b>A slot this side does not have is "no slot", not a disagreement.</b>
    /// The first version of this reported DISAGREE on the client and said the
    /// mirror was wrong, and it was wrong itself: <c>FrameTickCount</c> and
    /// <c>FrameGetFrameTime</c> are two of the fifteen SERVER-ONLY frame steps,
    /// so on the client they are null and the sentinels this compared were its
    /// own. A diagnostic that cries wolf about the one thing it exists to
    /// detect is worse than none, so the verdict is taken over the rows that
    /// have both halves — on the client that is <c>curtime</c>, which is the
    /// row every crossing this phase removes was reading anyway.
    /// </para>
    /// </summary>
    private void ReportAgreement(Side side, nint pointer)
    {
        if (_attachment.Side != side)
            return;

        var globals = new Globals(pointer);

        Msg($"[SourceSharp] {side} CGlobalVars bound at 0x{pointer:x}: IsClient={globals.IsClient}, "
          + $"maxClients={globals.MaxClients}, interval_per_tick={globals.IntervalPerTick}\n");

        int compared = 0;
        int disagreed = 0;
        var line = new System.Text.StringBuilder();

        Compare("curtime", _engineArea.GetCurrentTime().Equals(globals.CurTime), $"{_engineArea.GetCurrentTime()}/{globals.CurTime}");

        // THE SECOND SOURCE FOR THESE TWO MOVED, AND THIS MOVED WITH IT.
        //
        // FrameTickCount and FrameGetFrameTime were flat ABI slots until
        // plan_native_api.md Phase 4 retired them. The NATIVE FUNCTIONS behind
        // them did not go anywhere -- shell_frame.cpp still reads
        // gpGlobals->tickcount and ->frametime -- and IShimFrame reaches exactly
        // those, so the comparison is against the same two loads it always was,
        // through its forwarders instead of through two fields.
        //
        // The whole point of this check is that the two answers come from two
        // ROUTES: the mirror reads the engine's struct at offsets this port
        // wrote down, and the shim asks the engine's own game DLL globals. If
        // the offsets are wrong, the numbers differ. Dropping it when the slots
        // went would have deleted the only thing that says the mirror reads the
        // right bytes.
        //
        // The client does not serve SourceSharpFrame001, so it takes the
        // no-slot arm and says so -- which is what that arm has always been
        // for.
        if (Forwarders.TryAcquire(
                Interfaces, SourceSharpFactoryType.Internal, "SourceSharpFrame001",
                out void* frameSelf, out ShimFrameFwd* frame, out _))
        {
            int shimTick = frame->TickCount(frameSelf);
            float shimFrameTime = frame->GetFrameTime(frameSelf);

            Compare("tickcount", shimTick == globals.TickCount,
                    $"{shimTick}/{globals.TickCount}");
            Compare("frametime", shimFrameTime.Equals(globals.FrameTime),
                    $"{shimFrameTime}/{globals.FrameTime}");
        }
        else
        {
            line.Append("tickcount no-slot frametime no-slot ");
        }

        Msg($"[SourceSharp] {side} globals slot-vs-load: {line}-> "
          + $"{(disagreed > 0 ? "DISAGREE" : compared > 0 ? "AGREE" : "NOTHING TO COMPARE")} "
          + $"({compared} row(s) compared)\n");

        if (disagreed > 0)
            Warning($"[SourceSharp] the {side} globals load DISAGREES with the ABI slot for the same "
                  + "tick. The mirror's offsets do not describe this engine's CGlobalVars.\n");
        else if (compared == 0)
            Warning($"[SourceSharp] the {side} globals could not be checked against any slot, so "
                  + "nothing here says the mirror is reading the right bytes.\n");

        void Compare(string what, bool same, string values)
        {
            compared++;
            if (!same) disagreed++;
            line.Append($"{what} {values}{(same ? "" : " MISMATCH")} ");
        }
    }

    public void Msg(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* p = bytes)
        {
            // One delivery either way (docs/todo.md's console capture): with
            // the display func attached this says the line and SuppressEcho
            // consumes the echo the engine hands back on this thread; without
            // one nothing echoes, so SuppressEcho says it itself.
            if (Lines.EngineFeedAttached)
                Lines.Line(message, isWarning: false);
            var ptr = (nint)p;
            Lines.SuppressEcho(message, () =>
            {
                _consoleArea.Msg((byte*)ptr);
            });
        }
    }

    /// <summary>Prints a warning to the Source console.</summary>
    public void Warning(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* p = bytes)
        {
            if (Lines.EngineFeedAttached)
                Lines.Line(message, isWarning: true);
            var ptr = (nint)p;
            Lines.SuppressEcho(message, () =>
            {
                _consoleArea.Warning((byte*)ptr);
            }, isWarning: true);
        }
    }

    //-------------------------------------------------------------------------
    // Entities. Every call revalidates the handle's serial number native-side,
    // so a stale handle fails closed rather than addressing a reused slot.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether a handle still names an entity.
    ///
    /// <para>
    /// <b>plan_crossings.md Phase X5: answered from <see cref="LivenessMirror"/>
    /// when it knows, and by the slot when it does not.</b> This was 38.578
    /// crossings a server frame at rest, the largest single row on that side,
    /// and 83.0% of them were on handles that had already died — which managed
    /// was told about at the time. See <see cref="LivenessMirror"/> for why a
    /// dead record is KEPT, why the world and handle 0 are refused, and why a
    /// handle the mirror has not heard of falls through here rather than being
    /// answered.
    /// </para>
    ///
    /// <para>
    /// <b>The side gate is not decoration.</b> The slot is wired only on the
    /// server (<c>sourcesharp_gamesystem.cpp:1676</c>, inside
    /// <c>#ifndef CLIENT_DLL</c>) and only the server pushes spawn and removal
    /// — but this is a listen server and both game DLLs share one
    /// <c>Engine</c> in the default ALC, while handle VALUES are per
    /// side. An ungated mirror would answer a client handle out of a server
    /// entity's record: the same number, a different entity, and a plausible
    /// answer rather than an error.
    /// </para>
    /// </summary>
    public bool EntityIsValid(EntityHandle h)
    {
        // BEFORE THE MIRROR, because the mirror is pushed from the native bind
        // sites and a shell-less entity has none -- step 2e. Reaching the
        // mirror first would answer "not alive" for every logical entity in the
        // map, which is exactly the silent shape this port keeps finding.
        if (ShellLess.Claims(h))
            return true;

        if (_attachment.Side == Side.Server && _liveness is { Rerouted: true } liveness &&
            liveness.TryLookup(h, out bool alive))
            return alive;

        return (_entitiesArea.EntityIsValid(h.Value)) != 0;
    }

    /// <summary>
    /// <c>EntityIsValid</c> as the ABI slot answers it, with the Phase X5
    /// mirror bypassed — for the divergence gate and for nothing else.
    ///
    /// <para>
    /// <b>The slot is KEPT, and this is why.</b> A push mirror's failure mode is
    /// not staleness, which push-on-change makes impossible; it is a lifetime
    /// path nobody pushed from, which does not go stale but diverges and then
    /// answers confidently forever with no log line and no crash. Holding that
    /// shut needs an INDEPENDENT oracle, and the slot is one — it reads the
    /// engine's own handle table through <c>SourceSharp_ResolveEntity</c>, which
    /// is what the trace filters and the event queue resolve through. A gate
    /// that compared the mirror against something managed had also written
    /// would agree with itself whatever the shells did.
    /// </para>
    /// </summary>
    public bool EntityIsValidBySlot(EntityHandle h)
        => (_entitiesArea.EntityIsValid(h.Value)) != 0;

    /// <summary>
    /// Server time in seconds. The same clock the shell computes respawn
    /// deadlines from, so policy and mechanism agree about when a delay has
    /// elapsed.
    ///
    /// <para>
    /// <b>A LOAD, NOT A CROSSING</b> — plan_crossings.md Phase X1. This is the
    /// same <c>gpGlobals-&gt;curtime</c> the <c>GetCurrentTime</c> slot returns,
    /// read out of the same engine memory at the same moment; it is the
    /// relocation of a load rather than a copy of a value, so there is no
    /// version of it that can be stale. The slot stays as the fallback for the
    /// two moments the pointer is not there — before the engine has set
    /// <c>gpGlobals</c>, and in a test process with no engine at all — and as
    /// the reference a live parity check compares against.
    /// </para>
    /// </summary>
    public float CurrentTime
    {
        get
        {
            nint globals = GlobalVarsPointer;

            if (globals != 0)
                return new Globals(globals).CurTime;

            return _engineArea.GetCurrentTime();
        }
    }

    /// <summary>
    /// Told of every teleport after native has carried it out: the server
    /// composition hands a player's to its shadow (stock's TeleportEntity
    /// moves a player's physics object with them; plan_physics_bones_native_zero
    /// 2.5 made the shadow managed's). Null tells nobody.
    /// </summary>
    internal Action<EntityHandle, Vector, bool>? Teleported { get; set; }

    /// <summary>
    /// Releases a player from any movement constraint. Nothing to release: the
    /// constraint sphere is not ported (plan_server_native_v2 T2 deleted the
    /// empty native setter; docs/todo.md).
    /// </summary>
    public void PlayerClearMovementConstraint(EntityHandle player) { }

    /// <summary>Facing, as pitch/yaw/roll.</summary>
    public Vector EntityGetAngles(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Angles;

        float* pyr = stackalloc float[3];
        _entitiesArea.EntityGetAngles(h.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    // vphysics' IPhysicsCollision, acquired on first use from this side's
    // factories; the pointer is the vphysics module's for the process.
    private void* _physicsCollision;
    private PhysicsCollisionFwd* _physicsCollisionFwd;
    private bool _physicsCollisionAsked;

    private unsafe bool PhysicsCollisionTable(out void* self, out PhysicsCollisionFwd* vt)
    {
        if (!_physicsCollisionAsked)
        {
            _physicsCollisionAsked = true;
            if (Forwarders.TryAcquire<PhysicsCollisionFwd>(Interfaces, SourceSharpFactoryType.Physics, "VPhysicsCollision007",
                                                           out void* collision, out PhysicsCollisionFwd* table, out _))
            {
                _physicsCollision = collision;
                _physicsCollisionFwd = table;
            }
        }

        self = _physicsCollision;
        vt = _physicsCollisionFwd;
        return self != null;
    }

    /// <summary>
    /// The server's managed physics init (EntityPhysicsCreation), which
    /// <see cref="EntityInitPhysics"/> and <see cref="EntitySetPhysicsStatic"/>
    /// reach. Set by the server composition once vphysics answers; null makes
    /// nothing. INTERNAL, like <see cref="Teleported"/>.
    /// </summary>
    internal IEntityPhysicsRoute? PhysicsInit { get; set; }

    /// <summary>
    /// The model this entity is using, as the name the map knows it by — "*23"
    /// for a brush model. Empty for an entity that has none.
    ///
    /// <para>
    /// A name rather than an index because that is what <see cref="SetEntityModel"/>
    /// takes and <see cref="PrecacheModel"/> turns it back into an index, so one
    /// accessor serves both the entity that wants to WEAR another's model and
    /// the one that wants to draw it as an extra.
    /// </para>
    /// </summary>
    public string EntityGetModelName(EntityHandle entity)
    {
        byte* buffer = stackalloc byte[256];
        _modelsArea.EntityGetModelName(entity.Value, buffer, 256);

        return FromUtf8(buffer);
    }

    /// <summary>
    /// The movement loop's hull trace (plan_server_native_v2 T4f): the server
    /// traces through the managed trace filter, skipping
    /// <paramref name="player"/> (SourceSharp_MoveTraceThrough); the client
    /// through its own slot's filter. The movement area is the caller's
    /// (the runner's world holds it): the side holds only the areas its own
    /// services read.
    /// </summary>
    internal void MoveTrace(IMovementArea movement, float* start, float* end, float* mins, float* maxs, uint mask,
                            int collisionGroup, EntityHandle player, MoveTraceBlob* blob)
        => SideMoveTrace(movement, start, end, mins, maxs, mask, collisionGroup, player, blob);

    /// <summary>The entity a movement trace skips: the player at that edict index on the server, none on the client.</summary>
    internal EntityHandle MovePass(int entIndex) => SideMovePass(entIndex);

    /// <summary>
    /// The entity's edict index, or -1: -1 for a shell-less entity too, which
    /// is the answer its shell gave (step 2e). Over the side's own entities
    /// area, so the binding and the server's movement classes ask the same.
    /// </summary>
    internal int EntityGetIndex(EntityHandle h)
        => ShellLess.Claims(h) ? -1 : _entitiesArea.EntityGetIndex(h.Value);

    /// <summary>
    /// The entity's origin. A shell-less entity is answered here and never
    /// crosses (step 2e).
    /// </summary>
    internal Vector EntityGetOrigin(EntityHandle h)
    {
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Origin;

        float* xyz = stackalloc float[3];
        _entitiesArea.EntityGetOrigin(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    private partial void SideMoveTrace(IMovementArea movement, float* start, float* end, float* mins, float* maxs,
                                       uint mask, int collisionGroup, EntityHandle player, MoveTraceBlob* blob);
    private partial EntityHandle SideMovePass(int entIndex);

    // ---- marshalling and the globals check, this side's own (plan_abi_hotreload D8) ----
    // They were the static Engine's and reached here through `using static`;
    // they are pure functions of their arguments, so they hold no state, and
    // they live where their only production caller is. Engine.cs keeps its
    // copies until the integrator deletes it with the forwarders.

    /// <summary>
    /// Why this pointer is not the side's <c>CGlobalVars</c>, or null when it is.
    ///
    /// <para>
    /// <b>A SWAPPED POINTER IS THE FAILURE THIS EXISTS FOR.</b> This is a listen
    /// server: one process, two game DLLs, two <c>CGlobalVars</c>. A server
    /// reading the client's globals does not crash - it reads a <c>curtime</c>
    /// that means something else (globalvars_base.h documents three different
    /// meanings for the client's) and every deadline computed from it is quietly
    /// wrong. So the side is not assumed from who called: it is read out of the
    /// struct.
    /// </para>
    ///
    /// <para>
    /// <b>And the constants come first, because <c>m_bClient</c> is only
    /// meaningful if the mirror is lined up at all.</b>
    /// <c>CGlobalVarsBase</c>'s constructor writes 100 and 32 into the two
    /// timestamp fields and nothing in the tree ever writes them again
    /// (globalvars_base.h:98-101). Reading those two values back through
    /// <see cref="CGlobalVarsRaw"/>'s offsets is the runtime half of what
    /// globalvars_layout.h asserts at compile time - evidence that this address
    /// really is a <c>CGlobalVars</c> and that the mirror agrees with it about
    /// where things are. A garbage pointer fails here rather than being read as
    /// a plausible clock.
    /// </para>
    /// </summary>
    internal static string? RefuseGlobalVars(nint pointer, Side side)
    {
        if (pointer == 0)
            return "it is null";

        var raw = (CGlobalVarsRaw*)pointer;

        if (raw->nTimestampNetworkingBase != 100 || raw->nTimestampRandomizeWindow != 32)
            return "the two constants CGlobalVarsBase's constructor writes read "
                 + $"{raw->nTimestampNetworkingBase}/{raw->nTimestampRandomizeWindow} rather than 100/32, "
                 + "so this is not a CGlobalVars or the mirror does not line up with it";

        bool isClient = new Globals(pointer).IsClient;

        if (isClient != (side == Side.Client))
            return $"m_bClient says these are the {(isClient ? "client" : "server")}'s globals "
                 + $"and they were asked for by the {side}";

        return null;
    }

    /// <summary>
    /// A NUL-terminated UTF-8 copy on the unmanaged heap, remembered so the
    /// caller can free it. Null in, null out — a null base table means NOBASE
    /// and native reads it that way.
    /// </summary>
    internal static byte* Utf8(string? text, List<IntPtr> owned)
    {
        if (text is null)
            return null;

        IntPtr p = Marshal.StringToCoTaskMemUTF8(text);
        owned.Add(p);
        return (byte*)p;
    }


    /// <summary>
    /// A native UTF-8 string, or NULL AS NULL.
    ///
    /// <para>
    /// <see cref="FromUtf8"/> is the house marshaller and it answers
    /// <c>string.Empty</c> for a null pointer, which is right for a name that is
    /// simply blank and wrong for the factory dictionary's two indexed reads:
    /// their contract in <c>sourcesharp.h</c> is that NULL means "past the end",
    /// and <see cref="EntityFactories.Refresh"/> ENDS its walk on that answer
    /// rather than skipping it — a null in the middle means the far side is not
    /// the table this was written against, and walking on would read whatever
    /// came after it.
    /// </para>
    ///
    /// <para>
    /// Split out from the two callers so it can be driven with a real pointer:
    /// with no engine bound, both wrappers return on their null-slot guard and
    /// never reach this line, so a fact about them could not tell the two
    /// marshallers apart.
    /// </para>
    /// </summary>
    internal static string? Utf8OrNull(byte* p)
        => p == null ? null : Marshal.PtrToStringUTF8((IntPtr)p);

    /// <summary>Marshals a native UTF-8 string. Null-safe.</summary>
    internal static string FromUtf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((IntPtr)p) ?? string.Empty);
}

/// <summary>
/// What a state object needs in order to stop calling across the boundary
/// on every property access (plan_nettable_generic.md Part 9). Moved here from
/// the deleted static <c>Engine</c>, whose nested type it was;
/// <see cref="EngineSide.BindEntityBlob"/> makes it.
///
/// <para>
/// All four together, because three of them are not safe on their own: the
/// pointer cannot be revalidated alone, the slot cannot say which binding it
/// belongs to without the serial, and the length is what makes a short blob
/// a refused bind rather than an overrun on first use.
/// </para>
/// </summary>
internal readonly unsafe struct EntityBlobBinding
{
    internal EntityBlobBinding(byte* pointer, int bytes, EntitySlot* slot, uint serial)
    {
        Pointer = pointer;
        Bytes   = bytes;
        Slot    = slot;
        Serial  = serial;
    }

    internal byte* Pointer { get; }
    internal int Bytes { get; }
    internal EntitySlot* Slot { get; }
    internal uint Serial { get; }
}
