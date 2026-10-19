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
    private readonly Side _side;
    private bool _ready;
    private nint _globals;
    private bool _globalsDecided;

    // plan_abi_hotreload D4 / todo sweep: the attach state lives on the side
    // object that chooses between the two area sets. The generated file only
    // builds the areas; the router class it used to emit is gone.
    private ShimAreas? _shims;
    private ClientShimAreas? _clientShims;
    private ShimRoot? _root;

    internal EngineSide(Side side) : this(side, new SideState(side)) { }

    internal EngineSide(Side side, SideState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Side != side)
            throw new ArgumentException($"a {side} engine was handed the {state.Side} side's state", nameof(state));

        _side = side;
        State = state;
        Models = new ModelDescriptors(this);
        // The precache is the server's: the client's slot is null, so it hands
        // none in and a missing model answers -1, as the null slot did.
        Breaks = new BreakModels(this, Models, side == Side.Server ? PrecacheModel : null);
        Surfaces = new PhysicsSurfaces(this);
    }

    /// <summary>
    /// This side's own interface factory (plan_abi_hotreload D8), over this
    /// side's <see cref="AcquireInterface"/>: what everything bound to this
    /// side acquires through. There is no process-wide factory any more; the
    /// one that picked a side by the thread's marker is gone.
    /// </summary>
    /// <summary>
    /// What this side keeps across its loads (statics to zero): the
    /// managed-built registry, the crossing counters and the rest, handed in by
    /// the side's load from the handle the Bootstrap keeps. A fresh one for an
    /// engine composed without a load, so no two engines share one by default.
    /// </summary>
    internal SideState State { get; }

    /// <summary>
    /// The engine interfaces this side's area facades were converted onto,
    /// acquired from this side's factory on first use and held for this
    /// engine's life (statics to zero: they were four process-wide slots).
    /// </summary>
    internal EngineBoundSet Bound { get; } = new();

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
    /// The entity's one-object vphysics calls (plan_physics_bones_native_zero,
    /// the last thin forwards): made here, through the object's vtable, and
    /// the velocity home and the impulse decided here rather than in native.
    /// </summary>
    internal EntityPhysicsMotion Motion => _motion ??= new EntityPhysicsMotion(new MotionHost(this), new VPhysicsMotionCalls(VPhysics));

    /// <summary>
    /// This side's vphysics forwarder tables (plan_engine_forwarders.md), bound
    /// from its own interface factory on first use: what every vphysics service
    /// of the side calls objects through.
    /// </summary>
    internal VPhysicsTables VPhysics => _vphysics ??= new VPhysicsTables(Interfaces);

    private VPhysicsTables? _vphysics;

    private EntityPhysicsMotion? _motion;

    /// <summary>
    /// The entity half of <see cref="Motion"/>: the held object pointer, and
    /// the shell's own velocity members through the slots that keep only
    /// them. Callers have checked the side is attached.
    /// </summary>
    private sealed class MotionHost(EngineSide side) : IEntityPhysicsMotionHost
    {
        public nint PhysicsObject(EntityHandle entity) => side.EntityPhysicsObject(entity);

        public Vector EntityVelocity(EntityHandle entity)
        {
            float* xyz = stackalloc float[3];
            side.ServerShims.EntityGetVelocity(entity.Value, xyz);
            return new Vector(xyz[0], xyz[1], xyz[2]);
        }

        public void SetEntityVelocity(EntityHandle entity, Vector velocity)
        {
            float* xyz = stackalloc float[3] { velocity.X, velocity.Y, velocity.Z };
            side.ServerShims.EntitySetVelocity(entity.Value, xyz);
        }

        public Vector EntityAngularVelocity(EntityHandle entity)
        {
            float* pyr = stackalloc float[3];
            side.ServerShims.EntityGetAngularVelocity(entity.Value, pyr);
            return new Vector(pyr[0], pyr[1], pyr[2]);
        }

        public void SetEntityAngularVelocity(EntityHandle entity, Vector degreesPerSecond)
        {
            float* pyr = stackalloc float[3] { degreesPerSecond.X, degreesPerSecond.Y, degreesPerSecond.Z };
            side.ServerShims.EntitySetAngularVelocity(entity.Value, pyr);
        }

        public Vector Angles(EntityHandle entity) => side.EntityGetAngles(entity);

        public void Warning(string message) => side.Warning(message);
    }

    internal NativeInterfaceFactory Interfaces => _interfaces ??= new NativeInterfaceFactory(AcquireInterface);

    private NativeInterfaceFactory? _interfaces;

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

    // plan_abi_hotreload D8: this side's model caches, its own instances rather
    // than one static pair chosen by the thread's active side. The render and
    // precache bindings read them through the side they are bound to; the
    // module hands them on (ModuleEngine) and clears them at level shutdown.

    /// <summary>This side's model descriptors, cached per model per level.</summary>
    internal ModelDescriptors Models { get; }

    /// <summary>This side's break-piece lists, parsed once per model per level.</summary>
    internal BreakModels Breaks { get; }

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

    /// <summary>The root the attached areas came from, or null before an attach.</summary>
    internal ShimRoot? Root => _root;

    /// <summary>Whether the server's areas are attached.</summary>
    internal bool ServerAttached => _shims is not null;

    /// <summary>Whether the client's areas are attached.</summary>
    internal bool ClientAttached => _clientShims is not null;

    /// <summary>The server's attached areas; throws before the attach.</summary>
    internal ShimAreas ServerShims => _shims ?? throw new InvalidOperationException(
        "a server-only engine slot was called before the shim areas were attached");

    /// <summary>The client's attached areas; throws before the attach.</summary>
    internal ClientShimAreas ClientShims => _clientShims ?? throw new InvalidOperationException(
        "an engine slot was called on the client before its shim areas were attached");

    /// <summary>The server's areas, or null when they are not attached.</summary>
    internal ShimAreas? ServerShimsOrNull => _shims;

    /// <summary>Binds every generated area; the server calls this from LoadSideV2.</summary>
    internal bool AttachShims(ShimRoot root, out string why)
    {
        if (!ShimAreas.TryAcquire(root, out var areas, out why))
            return false;
        _shims = areas;
        _root = root;
        return true;
    }

    /// <summary>Binds the client's shared bases; the client calls this from LoadSideV2.</summary>
    internal bool AttachClientShims(ShimRoot root, out string why)
    {
        if (!ClientShimAreas.TryAcquire(root, out var areas, out why))
            return false;
        _clientShims = areas;
        _root = root;
        return true;
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
    /// </summary>
    internal PhysicsSurfaces Surfaces { get; }

    internal Side Side => _side;

    /// <summary>
    /// Where this side's managed assemblies were loaded from, as the bootstrap
    /// knows it (plan_abi_hotreload D8: the static Engine's, per side now, and
    /// set by the bootstrap on the side it loads).
    ///
    /// <para>
    /// Told rather than discovered, because nothing loaded into the game can
    /// work it out for itself: the load context reads each assembly into memory
    /// so the files stay unlocked, which leaves Assembly.Location empty for
    /// every one of them. Anything that then asked the runtime where it came
    /// from got "" and fell back to a relative path — resolved against the
    /// engine's working directory, which is the Steam install and not this mod.
    /// </para>
    ///
    /// <para>Empty outside the game, where there is no bootstrap to say.</para>
    /// </summary>
    internal string ManagedDirectory { get; set; } = string.Empty;

    private bool s_Initialized => _ready;

    // plan_client_vtables CC-last: no struct; every slot is a shim-area method,
    // routed by ShimAreas.g.cs once the side's areas are attached.
    internal void Initialize()
    {
        _ready = true;
    }

    /// <summary>
    /// This side's CGlobalVars, or 0 until the engine has answered with one that
    /// passes <see cref="RefuseGlobalVars"/>. Asked once, lazily.
    /// </summary>
    internal nint GlobalVarsPointer
    {
        get
        {
            if (!_globalsDecided)
                BindGlobalVars(_side, AskForGlobalVars());

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


    public bool IsInitialized => s_Initialized;

    /// <summary>
    /// A new <see cref="EntityFactories"/> pointed at the native entity factory
    /// dictionary — the five slots the <c>sub-factory</c> lane requested and the
    /// <c>abi-tail</c> merge appended at ABI 242.
    ///
    /// <para>
    /// <b>ONLY WHEN THE SLOTS ARE REALLY THERE, and only on the server.</b> A
    /// null provider means "not asked"; providers wired to slots that answer
    /// zero would mean "the table is empty", and those are different answers
    /// with different consequences —
    /// <see cref="EntityFactories.OwnerOf"/> reads an unasked table as
    /// <c>Native</c>, which is the assumption this build already runs on, and an
    /// empty one as <c>Nothing</c>, which refuses <c>ent_create prop_physics</c>
    /// for a class that works. The client has no factory dictionary at all, so
    /// binding there would produce exactly that empty table.
    /// </para>
    ///
    /// <para>
    /// The refusal ledger is bound as a SET of three or not at all, because
    /// <see cref="EntityFactories.Refresh"/> skips the whole ledger without all
    /// three and a partial wiring would read as "nothing has been refused".
    /// </para>
    ///
    /// <para>
    /// A NEW INSTANCE PER CALL, which the server's composition asks for once
    /// (through <c>EngineServices.EntityFactories</c>) and registers in its own
    /// container — plan_abi_hotreload D7. The providers were five statics set
    /// from <c>Initialize</c>; two managed game instances would have shared them.
    /// </para>
    /// </summary>
    internal EntityFactories BindEntityFactories()
    {
        // The slots are the EntityLifecycle area's (the struct lost them in C6c),
        // so a server side without its shims attached has no factories to report.
        if (_side != Side.Server || !ServerAttached)
            return new EntityFactories();

        var factories = new EntityFactories
        {
            NativeCount = EntityFactoryCount,
            NativeNameAt = EntityFactoryNameAt,
            RefusalCount = EntityFactoryRefusalCount,
            RefusalNameAt = EntityFactoryRefusalNameAt,
            RefusalTimesAt = EntityFactoryRefusalTimesAt,
        };

        // PULLED ON THE WAY OUT. A cs_sv_reload builds a new composition, and so
        // a new table, with no map load after it to refresh one: the static
        // table this replaced simply survived. The dictionary is filled at
        // server.so's static init, so it is complete here; the map spawner
        // still pulls again per map, after SpawnWorld writes the refusals.
        factories.Refresh();
        return factories;
    }

    /// <summary>
    /// One crossing, for the whole process, per side. Zero when this side has no
    /// table yet or the shim is older than the slot.
    /// </summary>
    private nint AskForGlobalVars()
        => (s_Initialized && (ServerAttached || ClientAttached)) ? (ServerShimsOrNull is { } __att1 ? __att1.GetGlobalVarsPtr() : ClientShims.GetGlobalVarsPtr()) : 0;

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
        if (_side != side || !s_Initialized)
            return;

        var globals = new Globals(pointer);

        Msg($"[SourceSharp] {side} CGlobalVars bound at 0x{pointer:x}: IsClient={globals.IsClient}, "
          + $"maxClients={globals.MaxClients}, interval_per_tick={globals.IntervalPerTick}\n");

        int compared = 0;
        int disagreed = 0;
        var line = new System.Text.StringBuilder();

        if ((ServerAttached || ClientAttached))
            Compare("curtime", (ServerShimsOrNull is { } __att2 ? __att2.GetCurrentTime() : ClientShims.GetCurrentTime()).Equals(globals.CurTime), $"{(ServerShimsOrNull is { } __att3 ? __att3.GetCurrentTime() : ClientShims.GetCurrentTime())}/{globals.CurTime}");
        else
            line.Append("curtime no-slot ");

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
        if (!s_Initialized || !(ServerAttached || ClientAttached))
        {
            Lines.Line(message, isWarning: false);
            return;
        }

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
                if (ServerShimsOrNull is { } __att4) __att4.Msg((byte*)ptr);
                else ClientShims.Msg((byte*)ptr);
            });
        }
    }

    /// <summary>Prints a warning to the Source console.</summary>
    public void Warning(string message)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached))
        {
            Lines.Line(message, isWarning: true);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* p = bytes)
        {
            if (Lines.EngineFeedAttached)
                Lines.Line(message, isWarning: true);
            var ptr = (nint)p;
            Lines.SuppressEcho(message, () =>
            {
                if (ServerShimsOrNull is { } __att5) __att5.Warning((byte*)ptr);
                else ClientShims.Warning((byte*)ptr);
            }, isWarning: true);
        }
    }

    /// <summary>
    /// Registers a console command. Native owns the ConCommand object for the
    /// process lifetime; only the managed handler is rebound on reload, because
    /// UnregisterConCommands is all-or-nothing per DLL
    /// (src/tier1/convar.cpp:93). See plan.md Part 5, rule 2.
    /// </summary>
    /// <param name="flags">
    /// The FCVAR word, carried across and ORed with this DLL's own identifier
    /// flag natively — see the slot's comment in <c>sourcesharp.h</c>. A cheat
    /// command declared without <see cref="ConVarFlags.Cheat"/> is a command
    /// that runs with <c>sv_cheats 0</c>, which is a different command.
    /// </param>
    public int RegisterConCommand(string name, string help, ConVarFlags flags)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return -1;

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return (ServerShimsOrNull is { } __att6 ? __att6.RegisterConCommand(pName, pHelp, unchecked((int)flags)) : ClientShims.RegisterConCommand(pName, pHelp, unchecked((int)flags)));
        }
    }

    /// <summary>
    /// Registers a ConVar. Native owns it for the process lifetime.
    ///
    /// The bounds are optional and are the ENGINE's clamp: a convar with a
    /// minimum clamps every write on the way in, whatever the source, so a
    /// change callback never sees a value outside them.
    /// </summary>
    public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                     float? min = null, float? max = null)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return -1;

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var defBytes  = Encoding.UTF8.GetBytes(defaultValue + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pDef = defBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return (ServerShimsOrNull is { } __att7 ? __att7.RegisterConVar(pName, pDef, pHelp, flags,
                                      min.HasValue ? 1 : 0, min ?? 0f,
                                      max.HasValue ? 1 : 0, max ?? 0f) : ClientShims.RegisterConVar(pName, pDef, pHelp, flags,
                                      min.HasValue ? 1 : 0, min ?? 0f,
                                      max.HasValue ? 1 : 0, max ?? 0f));
        }
    }

    public void SetConVarValue(int id, string value)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
        {
            if (ServerShimsOrNull is { } __att8) __att8.SetConVarValue(id, p);
            else ClientShims.SetConVarValue(id, p);
        }
    }

    /// <summary>Reads the engine's current value. Prefer the cached ConVarBase accessors on hot paths.</summary>
    public string GetConVarValue(int id)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return string.Empty;

        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];
        if (ServerShimsOrNull is { } __att9) __att9.GetConVarValue(id, buf, bufSize);
        else ClientShims.GetConVarValue(id, buf, bufSize);
        return FromUtf8(buf);
    }

    //-------------------------------------------------------------------------
    // The engine's own console registry.
    //
    // Nothing above this line can see it: RegisterConVar and friends deal only
    // in convars C# declared, addressed by an id native handed back. These read
    // the list the engine already had — thousands of entries belonging to the
    // engine and both game DLLs — and they are the raw crossings. What renders
    // them is NativeConsole, which is where the reason for each of them is.
    //
    // Game thread only, and a snapshot must be drained before the next is taken.
    //-------------------------------------------------------------------------

    /// <summary>Walks the engine's registry; returns how many entries it saw.</summary>
    public int ConsoleSnapshot()
        => (s_Initialized && (ServerAttached || ClientAttached)) ? (ServerShimsOrNull is { } __att10 ? __att10.ConsoleSnapshot() : ClientShims.ConsoleSnapshot()) : 0;

    /// <summary>Copies one entry out of the last snapshot. False past the end.</summary>
    public bool ConsoleSnapshotEntry(int index, out NativeConsoleEntry entry)
    {
        entry = default;

        if (!s_Initialized || !(ServerAttached || ClientAttached))
            return false;

        ConsoleEntry raw;

        if ((ServerShimsOrNull is { } __att11 ? __att11.ConsoleSnapshotEntry(index, &raw) : ClientShims.ConsoleSnapshotEntry(index, &raw)) == 0)
            return false;

        // Every buffer is fixed and NUL-terminated native-side, so each of these
        // reads to the terminator rather than to the end of its buffer.
        entry = new NativeConsoleEntry(
            FromUtf8(raw.Name),
            FromUtf8(raw.Value),
            FromUtf8(raw.Default),
            FromUtf8(raw.Help),
            raw.Flags,
            raw.IsCommand != 0,
            raw.HasMin != 0 ? raw.Min : null,
            raw.HasMax != 0 ? raw.Max : null);

        return true;
    }

    /// <summary>
    /// One native convar's value by name. Null when the engine knows no such
    /// convar, and null for a command, which has no value to read.
    /// </summary>
    public string? FindConVarValue(string name)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* pName = nameBytes)
        {
            if ((ServerShimsOrNull is { } __att12 ? __att12.FindConVarValue(pName, buf, bufSize) : ClientShims.FindConVarValue(pName, buf, bufSize)) == 0)
                return null;
        }

        return FromUtf8(buf);
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

        if (_side == Side.Server && _liveness is { Rerouted: true } liveness &&
            liveness.TryLookup(h, out bool alive))
            return alive;

        return s_Initialized && (ServerAttached || ClientAttached) && (ServerShimsOrNull is { } __att13 ? __att13.EntityIsValid(h.Value) : ClientShims.EntityIsValid(h.Value)) != 0;
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
        => s_Initialized && (ServerAttached || ClientAttached) && (ServerShimsOrNull is { } __att14 ? __att14.EntityIsValid(h.Value) : ClientShims.EntityIsValid(h.Value)) != 0;

    /// <summary>
    /// The entity's edict index, or -1.
    /// </summary>
    /// <remarks>
    /// <b>-1 for a shell-less entity, and that is not a degradation</b> — step
    /// 2e. It is the same answer the shell gave: a class of kind
    /// <see cref="ScriptedEntityKind.Logical"/> was already server-only, so its
    /// <c>CStandaloneEntity</c> never claimed an edict and <c>entindex()</c>
    /// answered -1 for it. What changes is that there is no longer an object to
    /// ask.
    /// </remarks>
    public int EntityGetIndex(EntityHandle h)
    {
        if (ShellLess.Claims(h))
            return -1;

        return (s_Initialized && (ServerAttached || ClientAttached)) ? (ServerShimsOrNull is { } __att15 ? __att15.EntityGetIndex(h.Value) : ClientShims.EntityGetIndex(h.Value)) : -1;
    }

    public string EntityGetClassName(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.ClassName;

        if (!s_Initialized || !ServerAttached) return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        ServerShims.EntityGetClassName(h.Value, buf, bufSize);
        return FromUtf8(buf);
    }

    public int EntityGetHealth(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Health;

        return (s_Initialized && ServerAttached) ? ServerShims.EntityGetHealth(h.Value) : 0;
    }

    public void EntitySetHealth(EntityHandle h, int health)
    {
        if (ShellLess.State(h) is { } shellLess)
        {
            shellLess.Health = health;
            return;
        }

        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetHealth(h.Value, health);
    }

    public Vector EntityGetOrigin(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Origin;

        if (!s_Initialized || !(ServerAttached || ClientAttached)) return default;

        float* xyz = stackalloc float[3];
        if (ServerShimsOrNull is { } __att16) __att16.EntityGetOrigin(h.Value, xyz);
        else ClientShims.EntityGetOrigin(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public void EntitySetOrigin(EntityHandle h, Vector origin)
    {
        if (ShellLess.State(h) is { } shellLess)
        {
            shellLess.Origin = origin;
            return;
        }

        if (!s_Initialized || !ServerAttached) return;

        float* xyz = stackalloc float[3];
        xyz[0] = origin.X; xyz[1] = origin.Y; xyz[2] = origin.Z;
        ServerShims.EntitySetOrigin(h.Value, xyz);
    }

    /// <summary>
    /// Whether a handle names a player.
    ///
    /// Cheap enough for a touch path — it is a virtual call and a comparison —
    /// which matters, because the rule that uses it runs every tick something
    /// is standing in a trigger.
    /// </summary>
    public bool EntityIsPlayer(EntityHandle h)
    {
        // FALSE WITHOUT ASKING -- step 2e. A player is kind Player, which is
        // networked and has a shell; a shell-less entity can never be one.
        if (ShellLess.Claims(h))
            return false;

        return s_Initialized && ServerAttached && ServerShims.EntityIsPlayer(h.Value) != 0;
    }

    /// <summary>Handle for a player slot (1..maxplayers), or None if absent.</summary>
    public EntityHandle GetPlayerHandle(int playerIndex)
        => (s_Initialized && ServerAttached)
            ? new EntityHandle(ServerShims.GetPlayerHandle(playerIndex))
            : EntityHandle.None;

    /// <summary>
    /// The player whose console is running the ConCommand being dispatched
    /// right now, or <see cref="EntityHandle.None"/> when nobody issued it —
    /// the server console with no player on it, or a slot that has not spawned.
    ///
    /// <para>
    /// Stock's <c>UTIL_GetCommandClient</c>, which is the line every one of the
    /// ent_* development commands opened with. Only meaningful DURING a
    /// dispatch; the engine clears the command client between them, so nothing
    /// caches this.
    /// </para>
    /// </summary>
    public EntityHandle GetCommandClient()
        => (s_Initialized && ServerAttached)
            ? new EntityHandle(ServerShims.GetCommandClientHandle())
            : EntityHandle.None;

    /// <summary>
    /// The entity's native data blob, as a bounds-checked span.
    ///
    /// Shared memory, not a copy — writes land directly in the entity. The
    /// pointer is re-fetched every call so a stale handle yields an empty span
    /// rather than addressing freed memory. Do not store the returned span
    /// across frames; the compiler enforces that it cannot escape, since
    /// Span&lt;T&gt; is a ref struct.
    /// </summary>
    public Span<byte> GetEntityBlob(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached)
            return Span<byte>.Empty;

        int size;
        void* p = ServerShims.EntityGetDataBlob(h.Value, &size);
        if (p == null || size <= 0)
            return Span<byte>.Empty;

        return new Span<byte>(p, size);
    }

    /// <summary>
    /// Binds a state object to an entity's blob, once, in place of resolving the
    /// entity on every access.
    ///
    /// <para>
    /// A null pointer with zero bytes is a real answer, not a failure: most
    /// entities carry no blob. <see cref="NetState.BindTo"/> turns it into a named
    /// exception, because a state whose entity has no storage is a state that
    /// would replicate nothing and say nothing about why.
    /// </para>
    /// </summary>
    internal EntityBlobBinding BindEntityBlob(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached)
            return default;

        int size = 0;
        uint serial = 0;
        EntitySlot* slot = null;

        void* p = ServerShims.EntityBindBlob(h.Value, &size, &slot, &serial);

        if (p == null || size <= 0 || slot == null)
            return default;

        return new EntityBlobBinding((byte*)p, size, slot, serial);
    }

    /// <summary>
    /// Announces a blob write that did not go through a bound state.
    ///
    /// <para>
    /// Fetching a blob used to mark the entity changed by itself — on reads as
    /// well as writes, because native could not tell them apart. Part 9 deletes
    /// that, so the writes with no state object behind them say so themselves.
    /// </para>
    ///
    /// <para>
    /// IT HAS NO PRODUCTION CALLER ANY MORE. The one it had was
    /// <c>EntityData.TryWrite</c>, the older layout model's write path, which
    /// step 8a deleted — every write goes through a generated blob-backed
    /// property (<c>NetBlobView</c>) now, and that path marks the entity itself.
    /// Left in place rather than removed: this is a general engine-area member
    /// with an ABI slot behind it, and reaping it is a separate decision from
    /// 8a's. Recorded in <c>.claude/entity-port/requests/8a.md</c> §4 so it is
    /// not re-derived.
    /// </para>
    ///
    /// <para>
    /// Deliberately not called by any read. That asymmetry is the whole
    /// behaviour change of the step.
    /// </para>
    /// </summary>
    public void MarkEntityStateChanged(EntityHandle h)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntityMarkStateChanged(h.Value);
    }

    /// <summary>
    /// A MANAGED WRITE TO ONE ENGINE-BLOCK FIELD, announced by offset
    /// (plan_entity.md step 1b; <c>.claude/entity-port/abi-requests/1b.md</c> §1).
    ///
    /// <para>
    /// <see cref="MarkEntityStateChanged"/> above sets
    /// <c>FL_FULL_EDICT_CHANGED</c>, which makes the local network backdoor
    /// copy EVERY prop in the entity's table. That is the honest answer for a
    /// generic payload word, which can only say "one of hundreds of them
    /// moved". A block field is different: Origin, Angles and ModelIndex each
    /// have a TYPED SendProp at exactly their own offset, so a write to one can
    /// name it and the engine copies that prop alone.
    /// </para>
    ///
    /// <para>
    /// <paramref name="blockOffset"/> is measured INSIDE THE BLOCK — take it
    /// from <see cref="EngineBlockMarks.OffsetInBlockOf"/>. Native adds the
    /// block base, which is the one number managed code cannot know: it is
    /// <c>ALIGN_VALUE(sizeof the largest shell, 16)</c> and it moves with any
    /// edit to a shell.
    /// </para>
    ///
    /// <para>
    /// NO PRODUCTION CALLER YET, and that is stated rather than hidden. Nothing
    /// managed writes the block directly today — every managed origin write
    /// still goes through <c>EntitySetOrigin</c>, whose native body writes the
    /// block and marks the two offsets itself. This is the slot the block
    /// writer will use when it lands; it exists now because the offsets are the
    /// op table's and the op table is step 1f's.
    /// </para>
    /// </summary>
    public void MarkEntityBlockChanged(EntityHandle h, int blockOffset)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntityMarkBlockChanged(h.Value, blockOffset);
    }

    /// <summary>
    /// Whether the generic payload props on this entity's SendTable read the
    /// same bytes its typed props do (plan_nettable_generic.md Part 14 Step 3).
    ///
    /// <para>
    /// Asked of native rather than worked out here, and that is the point. Both
    /// prop sets read one blob on the server, so their VALUES cannot differ —
    /// what can differ is where the payload props point, and only native can
    /// read the payload back through the offsets the engine will use. A managed
    /// check could only compare the blob with itself and would pass forever.
    /// </para>
    ///
    /// <para>
    /// <see cref="PayloadAgreement.Words"/> is 0 and <see cref="PayloadAgreement.State"/>
    /// is <c>NoPayload</c> for anything that is not a runtime-networked entity,
    /// which is most of them and is a real answer rather than a failure.
    /// </para>
    /// </summary>
    public PayloadAgreement CheckPayloadAgreement(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached)
            return PayloadAgreement.NotSupplied;

        int words = 0;
        int firstBad = -1;
        int result = ServerShims.EntityPayloadAgreement(h.Value, &words, &firstBad);

        return new PayloadAgreement(
            result switch
            {
                1 => PayloadAgreementState.Agrees,
                0 => PayloadAgreementState.Disagrees,
                _ => PayloadAgreementState.NoPayload,
            },
            words, firstBad);
    }

    //-------------------------------------------------------------------------
    // TEMP ENTITIES (plan_tmp.md Part 3).
    //-------------------------------------------------------------------------

    /// <summary>
    /// Hands one temp entity's description to native, which builds the table.
    /// Answers the handle, or -1.
    ///
    /// <para>
    /// VALID ONLY DURING LoadSide. A ServerClass links itself into
    /// <c>g_pServerClassHead</c> when it is constructed and the engine walks
    /// that list exactly once; one built later is not in the list, nothing
    /// warns, and the first effect that claims it sends the engine into a table
    /// that was never flattened. Native refuses after the window rather than
    /// building one, so a late call answers -1 and says why.
    /// </para>
    ///
    /// <para>
    /// -1 is also what the CLIENT answers today: the slot is server-only until
    /// C-Phase 1 lands the receiving half. That is the ordinary shape of a slot
    /// one side implements, and a caller reads it as "this side does not send
    /// temp entities" rather than as a failure.
    /// </para>
    /// </summary>
    public unsafe int TempEntRegister(TeClass description)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached))
            return -1;

        // Every string is pinned for the duration of the call and freed after
        // it. Native COPIES what it is handed — a SendProp keeps the pointer it
        // was given for the life of the process and this memory is not that —
        // so nothing here has to outlive the call.
        var owned = new List<IntPtr>(description.Fields.Length + 4);

        try
        {
            var fields = new TeFieldDesc[description.Fields.Length == 0 ? 1 : description.Fields.Length];

            for (int i = 0; i < description.Fields.Length; i++)
            {
                TeField f = description.Fields[i];

                fields[i] = new TeFieldDesc
                {
                    Name       = Utf8(f.Name, owned),
                    Kind       = (int)f.Kind,
                    Offset     = f.Offset,
                    SizeBytes  = f.SizeBytes,
                    Bits       = f.Bits,
                    BitsFrom   = (int)f.BitsFrom,
                    Flags      = (int)f.Flags,
                    Low        = f.Low,
                    High       = f.High,
                    SubTable   = Utf8(f.SubTable, owned),
                    Elems      = f.Elems,
                    ElemStride = f.ElemStride,
                };
            }

            fixed (TeFieldDesc* pFields = fields)
            {
                var desc = new TeClassDesc
                {
                    ClassName     = Utf8(description.ClassName, owned),
                    TableName     = Utf8(description.TableName, owned),
                    BaseTable     = Utf8(description.BaseTable, owned),
                    BlobBytes     = description.BlobBytes,
                    RegisterClass = description.RegistersClass ? 1 : 0,
                    FieldCount    = description.Fields.Length,
                    Fields        = description.Fields.Length == 0 ? null : pFields,
                };

                return (ServerShimsOrNull is { } __att17 ? __att17.TempEntRegister(&desc) : ClientShims.TempEntRegister(&desc));
            }
        }
        finally
        {
            foreach (IntPtr p in owned)
                Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>
    /// Sends one. The blob is the packed fields, addressed by the offsets the
    /// description carries; native refuses a blob shorter than the declared size
    /// rather than sending whatever follows it.
    /// </summary>
    public unsafe void TempEntPlayback(int handle, ReadOnlySpan<byte> blob,
                                              TeFilter filter, float delay)
    {
        if (!s_Initialized || !ServerAttached || handle < 0 || blob.IsEmpty)
            return;

        var wire = new TeFilterDesc
        {
            Kind                 = (int)filter.Kind,
            PlayerIndex          = filter.PlayerIndex,
            Attenuation          = filter.Attenuation,
            IgnorePredictionCull = filter.IgnorePredictionCull ? 1 : 0,
        };

        wire.Origin[0] = filter.Origin.X;
        wire.Origin[1] = filter.Origin.Y;
        wire.Origin[2] = filter.Origin.Z;

        fixed (byte* pBlob = blob)
        {
            ServerShims.TempEntPlayback(handle, pBlob, blob.Length, &wire, delay);
        }
    }

    /// <summary>
    /// A round off an energy shield, where managed code decided one should be.
    ///
    /// <para>
    /// CLIENT ONLY, and it is the END of a ported effect rather than the
    /// beginning: the decode and the decision are C#'s, and this is
    /// <c>g_pEffects->EnergySplash</c> — the one line
    /// <c>C_TEEnergySplash::PostDataUpdate</c> was.
    /// </para>
    /// </summary>
    public unsafe void ClientEnergySplash(Vector position, Vector direction,
                                                 bool explosive)
    {
        if (!s_Initialized || !ClientAttached)
            return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X;
        pPos[1] = position.Y;
        pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X;
        pDir[1] = direction.Y;
        pDir[2] = direction.Z;

        ClientShims.ClientEnergySplash(pPos, pDir, explosive ? 1 : 0);
    }

    //-------------------------------------------------------------------------
    // C-PHASE 2 — the rest of Part 4's client backends.
    //
    // Fifteen drawing primitives. Each one is the LAST LINE of a c_te_*.cpp and
    // nothing else: the decode of the blob and the decision to draw are managed
    // code's, and what is behind these is the renderer this port never intended
    // to move.
    //
    // (The six beam creators left with the managed beam system: the temp
    // entity players call IClientBeams in C# directly.)
    //-------------------------------------------------------------------------

    /// <summary>
    /// Electric sparks. <c>g_pEffects-&gt;Sparks</c>, which is
    /// <c>FX_ElectricSpark</c>.
    ///
    /// <para>
    /// The direction is OPTIONAL and stays optional: no direction is a spark
    /// with no preferred one, which is a different effect from a spark aimed at
    /// the origin. Pass <c>null</c>, not <c>Vector.Zero</c>.
    /// </para>
    /// </summary>
    public unsafe void ClientSparks(Vector position, int magnitude, int trailLength,
                                           Vector? direction = null)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (direction is Vector dir)
        {
            float* pDir = stackalloc float[3];
            pDir[0] = dir.X; pDir[1] = dir.Y; pDir[2] = dir.Z;

            ClientShims.ClientSparks(pPos, magnitude, trailLength, pDir);
            return;
        }

        ClientShims.ClientSparks(pPos, magnitude, trailLength, null);
    }

    /// <summary>
    /// A ricochet: metal sparks AND the sound. <c>g_pEffects-&gt;Ricochet</c>.
    /// </summary>
    public unsafe void ClientRicochet(Vector position, Vector direction)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        ClientShims.ClientRicochet(pPos, pDir);
    }

    /// <summary>
    /// The same sparks with NO SOUND, which is the whole difference from
    /// <see cref="ClientRicochet"/> and is stock's own comment on the
    /// interface. <c>g_pEffects-&gt;MetalSparks</c>, which is
    /// <c>FX_MetalSpark</c>.
    /// </summary>
    public unsafe void ClientMetalSparks(Vector position, Vector direction)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        ClientShims.ClientMetalSparks(pPos, pDir);
    }

    /// <summary>
    /// Smoke sprites. <c>g_pEffects-&gt;Smoke</c>, which is <c>FX_Smoke</c>.
    ///
    /// <para>
    /// <paramref name="scale"/> is the TENFOLD one. Stock's two callers both
    /// pass <c>m_fScale * 10.0f</c> under a comment saying "the number passed
    /// down is 10 times smaller", and that multiply is part of the decode — so
    /// it belongs to the caller, not to this call.
    /// </para>
    /// </summary>
    public unsafe void ClientSmoke(Vector origin, int modelIndex,
                                          float scale, float frameRate)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        ClientShims.ClientSmoke(pOrigin, modelIndex, scale, frameRate);
    }

    /// <summary>
    /// A muzzle flash. <c>g_pEffects-&gt;MuzzleFlash</c>, which switches on the
    /// type over <c>FX_MuzzleEffect</c>, <c>FX_GunshipMuzzleEffect</c> and
    /// <c>FX_StriderMuzzleEffect</c> and prints a line for anything else —
    /// stock's own behaviour, left alone rather than turned into a silent
    /// default.
    /// </summary>
    public unsafe void ClientMuzzleFlash(Vector origin, Vector angles,
                                                float scale, int type)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        float* pAngles = stackalloc float[3];
        pAngles[0] = angles.X; pAngles[1] = angles.Y; pAngles[2] = angles.Z;

        ClientShims.ClientMuzzleFlash(pOrigin, pAngles, scale, type);
    }

    /// <summary>
    /// A decal on the world, a brush model or a static prop.
    /// <c>effects-&gt;DecalShoot</c>, the ENGINE's decal system.
    ///
    /// <para>
    /// The entity is an EDICT INDEX and is resolved on the native side, because
    /// the call wants the entity's <c>model_t*</c>, abs origin and abs angles
    /// and a <c>model_t*</c> is not a thing that crosses this boundary. Entity
    /// 0 is the world, which is what a world or BSP decal asks for.
    /// </para>
    ///
    /// <para>
    /// <paramref name="sAxis"/> is the decal's S axis — a footprint's facing.
    /// Omit it and the engine picks one, which is what the world and BSP decals
    /// do. <c>r_decals</c> is honoured natively, once, where stock honours it.
    /// </para>
    ///
    /// <para>
    /// This does NOT close ledger rows C2 or C3. Those are decals on ENTITIES
    /// and the world/brush arm of the PROJECTED decal, both of which go through
    /// <c>C_BaseEntity::AddDecal</c> — a call this hierarchy has no version of
    /// and this slot is not.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalShoot(int decalIndex, int entIndex, Vector position,
                                               Vector? sAxis = null, int flags = 0)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (sAxis is Vector axis)
        {
            float* pAxis = stackalloc float[3];
            pAxis[0] = axis.X; pAxis[1] = axis.Y; pAxis[2] = axis.Z;

            ClientShims.ClientDecalShoot(decalIndex, entIndex, pPos, pAxis, flags);
            return;
        }

        ClientShims.ClientDecalShoot(decalIndex, entIndex, pPos, null, flags);
    }

    /// <summary>
    /// A player's spray logo. <c>effects-&gt;PlayerDecalShoot</c>.
    ///
    /// <para>
    /// Three numbers here and everything else on the far side, which is not a
    /// thin wrapper being lazy: between the wire fields and the call sit a
    /// <c>player_info_t</c> out of the engine, a custom-file hash, a download
    /// check, a material lookup and a texture-variable swap — every one of them
    /// a pointer type that cannot cross this boundary.
    /// </para>
    /// </summary>
    public unsafe void ClientPlayerDecalShoot(Vector position, int player, int entIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        ClientShims.ClientPlayerDecalShoot(pPos, player, entIndex);
    }

    /// <summary>
    /// A decal's index from its name, or -1 for a name the engine does not
    /// know. <c>effects-&gt;Draw_DecalIndexFromName</c>.
    ///
    /// <para>
    /// The one backend in this family that ANSWERS rather than draws. A
    /// received temp entity never needs it — the wire carries the index — but a
    /// client making a decal for itself does.
    /// </para>
    /// </summary>
    public unsafe int ClientDecalIndexFromName(string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return ClientShims.ClientDecalIndexFromName(p); }
    }

    /// <summary>
    /// A decal on a STATIC PROP. <c>staticpropmgr-&gt;AddDecalToStaticProp</c>.
    ///
    /// <para>
    /// ONE call for two classes: <c>CTEDecal</c>'s world-with-a-hitbox arm and
    /// <c>CTEProjectedDecal</c>'s post-trace one. Stock hands both of them an
    /// uninitialised <c>trace_t</c> and the interface's own comment says the
    /// call "returns point of decal in trace_t", so nothing about the trace
    /// that chose the prop has to survive into the decal.
    /// </para>
    ///
    /// <para>
    /// <paramref name="staticPropIndex"/> is stock's <c>hitbox - 1</c>, applied
    /// by the caller. The engine numbers a prop's hitbox from one so that zero
    /// can mean "not a static prop", and unpacking that belongs beside the
    /// <c>hitbox != 0</c> test that reads it — which is a decision, and
    /// decisions are C#'s. A negative index is refused natively.
    /// </para>
    ///
    /// <para>
    /// This does NOT close ledger rows C2 or C3, and it is not the arm either
    /// of them names. Both rows are about <c>C_BaseEntity::AddDecal</c>; this
    /// is the static-prop arm, which never had an entity in it and has drawn
    /// all along.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalStaticProp(Vector start, Vector end,
                                                    int staticPropIndex, int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pStart = stackalloc float[3];
        pStart[0] = start.X; pStart[1] = start.Y; pStart[2] = start.Z;

        float* pEnd = stackalloc float[3];
        pEnd[0] = end.X; pEnd[1] = end.Y; pEnd[2] = end.Z;

        ClientShims.ClientDecalStaticProp(pStart, pEnd, staticPropIndex, decalIndex);
    }

    /// <summary>
    /// Whether this client is running with gore turned down.
    /// <c>UTIL_IsLowViolence</c>.
    ///
    /// <para>
    /// NOT FOUR CONVAR READS. <c>violence_hblood</c> and its three siblings are
    /// all reachable through <see cref="FindConVarValue"/>, and stock's own
    /// comment above them says they are legacy and that "the engine is the
    /// final arbiter of violence settings". The arbiter is
    /// <c>engine-&gt;IsLowViolence()</c>, which managed code cannot reach at
    /// all — so a C# copy of this rule would have been the four terms that do
    /// not decide anything and a missing fifth that does.
    /// </para>
    /// </summary>
    public unsafe bool ClientIsLowViolence()
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.ClientIsLowViolence() != 0;
    }

    /// <summary>
    /// A decal's NAME from its index, or the empty string for an index that
    /// names nothing. <c>decalsystem-&gt;GetDecalNameForIndex</c>.
    ///
    /// <para>
    /// NOT THE INVERSE of <see cref="ClientDecalIndexFromName"/>, and reading it
    /// as one is the trap. That asks the ENGINE's precached decal table
    /// (<c>effects-&gt;Draw_DecalIndexFromName</c>); this asks the GAME's decal
    /// emitter system, loaded from <c>scripts/decals_subrect.txt</c> and keyed
    /// on each entry's precache index. The two overlap and are not one table,
    /// and stock reads each in its own place —
    /// <c>c_te_worlddecal.cpp</c>'s low-violence test reads this one.
    /// </para>
    /// </summary>
    public unsafe string ClientDecalNameFromIndex(int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return string.Empty;

        // 260, which is what every other string slot on this ABI gives itself.
        // A decal name is a material path.
        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        return ClientShims.ClientDecalNameFromIndex(decalIndex, buf, bufSize) > 0 ? FromUtf8(buf) : string.Empty;
    }

    /// <summary>
    /// What a client entity's model IS, by entity index —
    /// <c>modelinfo-&gt;GetModelType</c> of whatever that index resolves to.
    /// <c>null</c> for no such entity.
    ///
    /// <para>
    /// NULL IS NOT <see cref="Abi.ModelType.Bad"/>, and folding the two together
    /// would delete stock's own line. <c>TE_Decal</c> reads
    /// <c>if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL ) return;</c>
    /// above the call, and <c>mod_bad</c> is a real answer about an entity that
    /// is really there — a caller that could not tell them apart would either
    /// warn about a missing decal path for entities that do not exist, or draw
    /// nothing for entities that do.
    /// </para>
    ///
    /// <para>
    /// NOT <see cref="ModelTypeOf"/> WITH ANOTHER NUMBER. That takes a PRECACHE
    /// index and answers about a file; this takes an ENTITY index and answers
    /// about what that entity is wearing. Managed code cannot bridge them: a
    /// <c>model_t*</c> does not cross this boundary, and this side's entity
    /// list holds entities C# never precached.
    /// </para>
    /// </summary>
    public unsafe ModelType? ClientEntityModelType(int entIndex)
    {
        if (!s_Initialized || !ClientAttached) return null;

        int answer = ClientShims.ClientEntityModelType(entIndex);

        return answer < 0 ? null : (ModelType)answer;
    }

    /// <summary>
    /// A decal on a STUDIO model — <c>C_BaseEntity::AddStudioDecal</c>, which
    /// is <c>modelrender-&gt;AddDecal</c> on the entity's own model instance.
    /// BEHAVIOUR LEDGER ROWS C2 AND C3's studio arm.
    ///
    /// <para>
    /// THE TWO POINTS AND NOT A RAY. Stock builds the <c>Ray_t</c> inside
    /// <c>AddDecal</c> and then widens its DELTA by a tenth
    /// (<c>ray.m_Delta *= 1.1f</c>, under its own "bloat a little bit so we get
    /// the intersection"). Both stay native: bloating the endpoint here and
    /// letting <c>Ray_t::Init</c> subtract would hand the engine
    /// <c>(start + d * 1.1f) - start</c>, which is not <c>d * 1.1f</c>.
    /// </para>
    ///
    /// <para>
    /// No hitbox: <c>AddStudioDecal</c> takes one and never reads it. No LOD
    /// limit: both of stock's call sites take <c>ADDDECAL_TO_ALL_LODS</c> by
    /// default. <c>r_drawmodeldecals</c> is checked natively, because that is
    /// where stock checks it and it is a renderer setting.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd,
                                                     int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pStart = stackalloc float[3];
        pStart[0] = rayStart.X; pStart[1] = rayStart.Y; pStart[2] = rayStart.Z;

        float* pEnd = stackalloc float[3];
        pEnd[0] = rayEnd.X; pEnd[1] = rayEnd.Y; pEnd[2] = rayEnd.Z;

        ClientShims.ClientDecalStudioModel(entIndex, pStart, pEnd, decalIndex);
    }

    /// <summary>
    /// A decal on a BRUSH model, the world included —
    /// <c>C_BaseEntity::AddBrushModelDecal</c>, which is one
    /// <c>effects-&gt;DecalShoot</c>. BEHAVIOUR LEDGER ROWS C2 AND C3's brush
    /// arm, and the one an ordinary <c>info_projecteddecal</c> takes.
    ///
    /// <para>
    /// NO RAY. With <c>doTrace</c> false the ray reaches nothing in that
    /// function; what places the decal is <paramref name="decalCenter"/>, which
    /// is stock's THIRD argument to <c>AddDecal</c> — <c>*pos</c> at
    /// <c>c_te_decal.cpp</c>'s call site and <c>endpos</c> at
    /// <c>c_te_projecteddecal.cpp</c>'s, and not the same vector as the
    /// segment's end in the second.
    /// </para>
    ///
    /// <para>
    /// NOT <see cref="ClientDecalShoot"/> WITH SHORTER ARGUMENTS, though both
    /// end in the same engine call: that one checks <c>r_decals</c> because all
    /// three of ITS stock callers check it on the line before, and neither of
    /// this one's does.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalBrushModel(int entIndex, Vector decalCenter,
                                                    int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pCenter = stackalloc float[3];
        pCenter[0] = decalCenter.X; pCenter[1] = decalCenter.Y; pCenter[2] = decalCenter.Z;

        ClientShims.ClientDecalBrushModel(entIndex, pCenter, decalIndex);
    }

    /// <summary>
    /// A CLIENT entity's MODEL bounds and its angles, BY INDEX. False when the
    /// index names no entity, or names one with no model.
    ///
    /// <para>
    /// BEHAVIOUR LEDGER ROW C6's native half, and the two reads are stock's:
    /// <c>CTempEnts::FizzEffect</c> asks
    /// <c>modelinfo-&gt;GetModelBounds( pent-&gt;GetModel(), … )</c> for the
    /// volume the bubbles are spread across and <c>GetLocalAngles()[1]</c> for
    /// the direction the current drags them, and nothing else about the entity.
    /// </para>
    ///
    /// <para>
    /// <b>THE MODEL'S BOUNDS, NOT THE COLLISION BOX.</b>
    /// <see cref="EntityGetBounds"/> answers the collideable's rectangle, which
    /// on <c>C_StandaloneEntity</c> is a symmetric ±16 cube set once in the
    /// constructor and never networked — so it would put every fizz in a
    /// 32-unit cube. The model's bounds are the slab the mapper drew, in the
    /// model's own frame, which for a brush model is world space already.
    /// </para>
    ///
    /// <para>
    /// BY INDEX, and the native arm resolves through
    /// <c>cl_entitylist-&gt;GetClientEntity</c>, which answers for BOTH
    /// hierarchies. Stock's <c>cl_entitylist-&gt;GetEnt</c> answered only the
    /// SDK one, which is why every fizz in this build was dropped in silence.
    /// </para>
    /// </summary>
    public unsafe bool ClientEntityModelBounds(int entityIndex,
                                                      out Vector mins, out Vector maxs,
                                                      out Vector angles)
    {
        mins = Vector.Zero;
        maxs = Vector.Zero;
        angles = Vector.Zero;

        if (!s_Initialized || !ClientAttached) return false;

        float* lo = stackalloc float[3];
        float* hi = stackalloc float[3];
        float* pyr = stackalloc float[3];

        if (ClientShims.ClientEntityModelBounds(entityIndex, lo, hi, pyr) == 0) return false;

        mins = new Vector(lo[0], lo[1], lo[2]);
        maxs = new Vector(hi[0], hi[1], hi[2]);
        angles = new Vector(pyr[0], pyr[1], pyr[2]);
        return true;
    }

    //-------------------------------------------------------------------------
    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP).
    //
    // Eight named primitives, not one ClientParticleTeSpawn(kind, params): the
    // seven TEs on DT_TEParticleSystem share a base TABLE and nothing else.
    // Sparks needs none of them — ClientSparks above already is its primitive,
    // which is why there are eight for seven.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The fireball's white-hot flash: one <c>CExplosionOverlay</c>, which
    /// grows and fades over a tenth of a second.
    /// </summary>
    public unsafe void ClientExplosionFireball(Vector origin)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        ClientShims.ClientExplosionFireball(pOrigin);
    }

    // ---- the legacy temp entity pool (plan_tmp.md Part 5). Client only ----
    //
    // c_te_legacytempents.cpp's CTempEnts, whose BOOKKEEPING is
    // ClientTempEntPool.cs: the active list, the free list, the priority
    // classes, the eviction, the lifetime and the update loop. These six reach
    // ONE pooled C_LocalTempEntity, addressed by SLOT.
    //
    // EVERY ONE ANSWERS FALSE FOR AN EMPTY SLOT rather than throwing, and that
    // is what the managed pool's own recovery is built on: a slot whose object
    // has gone -- because a reservation never got one, or because native tore
    // the level down under it -- is dropped from the list on the next pass
    // instead of being a fault.

    /// <summary>
    /// How many tempents this build's pool holds —
    /// <c>CTempEnts::MAX_TEMP_ENTITIES</c>.
    ///
    /// <para>
    /// ANSWERED RATHER THAN RESTATED. A C# constant beside a C++ enum is the
    /// stand-in that drifts silently, and the drift here would be a pool that
    /// overflows the native slot table or one that refuses at 500 while native
    /// still has room. Zero means the slot is not wired, which the pool treats
    /// as "no pool" rather than as a capacity.
    /// </para>
    /// </summary>
    public unsafe int TempEntPoolCapacity()
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.TempEntPoolCapacity();
    }

    /// <summary>
    /// Give one back: <c>RemoveFromLeafSystem</c>, <c>OnRemoveTempEntity</c>
    /// and the memory pool free, in stock <c>TempEntFree</c>'s order.
    /// </summary>
    public unsafe void TempEntPoolDestroy(int slot)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.TempEntPoolDestroy(slot);
    }

    /// <summary>
    /// Fill <paramref name="life"/> from the live object. False for an empty
    /// slot, and <paramref name="life"/> is then default.
    /// </summary>
    public unsafe bool TempEntPoolReadLife(int slot, out TempEntLife life)
    {
        life = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntLife* pLife = &life)
        {
            return ClientShims.TempEntPoolReadLife(slot, pLife) != 0;
        }
    }

    /// <summary>
    /// The transform C# owns while it simulates this tempent. Read ONCE, when
    /// the managed pool takes it over — everything after that is integrated on
    /// this side, so this is how C# learns what Prepare and the spawn helper
    /// left behind rather than a per-frame query.
    /// </summary>
    public unsafe bool TempEntPoolReadTransform(int slot, out TempEntTransform transform)
    {
        transform = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntTransform* p = &transform)
        {
            return ClientShims.TempEntPoolReadTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// Write the transform back, which is how a C#-simulated tempent gets
    /// drawn. ONCE per tempent per frame; see <see cref="TempEntTransform"/>
    /// for why this is not a set of per-field accessors.
    /// </summary>
    public unsafe bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntTransform* p = &transform)
        {
            return ClientShims.TempEntPoolWriteTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// The write-once state <c>Frame</c> reads and never writes — bounce
    /// factor, bounce sound, sprite animation bounds, the player-attachment
    /// offset, the owner to trace past and the flicker frame. Read ONCE when
    /// the managed pool takes a tempent over.
    ///
    /// <para>
    /// A THIRD STRUCT rather than a bigger transform, and asked of the OBJECT
    /// rather than remembered from the managed spawn record. See
    /// <see cref="TempEntStatics"/>: this build still has three NATIVE spawn
    /// routes, and a managed cache of what C# itself wrote would simulate them
    /// with <c>Prepare</c>'s defaults.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolReadStatics(int slot, out TempEntStatics statics)
    {
        statics = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntStatics* p = &statics)
        {
            return ClientShims.TempEntPoolReadStatics(slot, p) != 0;
        }
    }

    /// <summary>
    /// <c>GetWindspeedAtTime</c> — the client's current wind velocity, which is
    /// what <c>FTENT_WINDBLOWN</c> accelerates towards. The zero vector with no
    /// engine and on a map with no <c>env_wind</c>, which are the same answer
    /// the engine's own implementation gives.
    /// </summary>
    public unsafe Vector ClientWindSpeed(float time)
    {
        if (!s_Initialized || !ClientAttached) return Vector.Zero;

        float* xyz = stackalloc float[3] { 0f, 0f, 0f };

        ClientShims.ClientWindSpeed(time, xyz);

        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// <c>GetCurrentGravity()</c>. A slot rather than a constant because
    /// <c>sv_gravity</c> is a convar: a tempent falling at 600 while the world
    /// falls at 800 looks almost right, which is the worst kind of wrong for
    /// something nobody watches closely.
    /// </summary>
    public unsafe float CurrentGravity()
        => (!s_Initialized || !(ServerAttached || ClientAttached)) ? 600f : (ServerShimsOrNull is { } __att18 ? __att18.CurrentGravity() : ClientShims.CurrentGravity());

    /// <summary>
    /// A CLIENT entity's origin by INDEX. False when the index names no entity
    /// — the ordinary case for a player who has left, where stock simply does
    /// not move the tempent.
    /// </summary>
    public unsafe bool ClientEntityOrigin(int entityIndex, out Vector origin)
    {
        origin = Vector.Zero;

        if (!s_Initialized || !ClientAttached) return false;

        float* xyz = stackalloc float[3];

        if (ClientShims.ClientEntityOrigin(entityIndex, xyz) == 0) return false;

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    /// <summary>
    /// Write <paramref name="life"/> back into the live object. False for an
    /// empty slot.
    ///
    /// <para>
    /// EVERY FIELD IS WRITTEN, so a caller reads first and edits what it read.
    /// A partial write is not offered: the alternative is eight setters and a
    /// caller that forgets one, which is a tempent that fades at the wrong rate
    /// rather than a compile error.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolWriteLife(int slot, in TempEntLife life)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntLife* pLife = &life)
        {
            return ClientShims.TempEntPoolWriteLife(slot, pLife) != 0;
        }
    }

    /// <summary>
    /// One step of the physics — <c>C_LocalTempEntity::Frame</c>. False when
    /// the tempent asked to die, which is an animating sprite that ran off its
    /// last frame.
    ///
    /// <para>
    /// STILL NATIVE, and not because the pool is: the collision arm needs
    /// <c>UTIL_TraceLine</c> and a whole <c>trace_t</c>, and this build's client
    /// has no trace at all — the ABI's is wired <c>#ifndef CLIENT_DLL</c>. That
    /// is the same wall <c>docs/te-ledger.md</c> records for
    /// <c>CTEProjectedDecal</c>, and it is what holds ledger rows C4 and C5
    /// open.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.TempEntPoolSimulate(slot, frameTime, frameNumber) != 0;
    }

    /// <summary>
    /// <c>CTempEnts::AddVisibleTempEntity</c> — put it back in the leaf system
    /// for this frame. False when it cannot be drawn at all, which for stock
    /// means it has no model.
    /// </summary>
    public unsafe bool TempEntPoolAddVisible(int slot)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.TempEntPoolAddVisible(slot) != 0;
    }

    /// <summary>
    /// Construct a tempent into a slot the managed pool has already reserved:
    /// the memory-pool allocation, <c>Prepare( model, curtime )</c>, the
    /// priority, the origin and the leaf system — stock
    /// <c>CTempEnts::TempEntAlloc</c> from the list add down.
    ///
    /// <para>
    /// HOW EVERY MANAGED SPAWN BUILDS ITS OBJECT. The managed helper reserves
    /// from the pool it IS and calls this; there used to be a twin half where
    /// native entered <c>CTempEnts::TempEntAlloc</c> and called OUT to reserve
    /// through <c>OnTempEntPoolAlloc</c>, but that whole direction was deleted
    /// with the tempent pool sweep — the only code that entered it was the
    /// stock <c>c_te_legacytempents.cpp</c>, which compiles into no target. The
    /// list, the free list and the eviction rule live only on this side.
    /// </para>
    ///
    /// <para>
    /// False means nothing was constructed and the slot is still EMPTY — an
    /// out-of-range or occupied slot, a model index that resolves to nothing, or
    /// the memory pool refusing. The caller must give the reservation back;
    /// <see cref="SourceSharp.Client.TempEnts.ClientTempEntPool"/> does that in
    /// one place so no helper has to remember to.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
    {
        if (!s_Initialized || !ClientAttached) return false;

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        return ClientShims.TempEntPoolCreate(slot, xyz, modelIndex, priority) != 0;
    }

    /// <summary>
    /// Write a whole <see cref="TempEntSpawn"/> into the live object. False for
    /// an empty slot.
    ///
    /// <para>
    /// EVERY FIELD IS WRITTEN, for <c>TempEntPoolWriteLife</c>'s reason and one
    /// more of its own: the record is built from
    /// <see cref="TempEntSpawn.Prepared"/>, so "the caller did not set this" and
    /// "the caller set this to Prepare's value" are the same thing and a mask
    /// would distinguish two states that are one.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntSpawn* pSpawn = &spawn)
        {
            return ClientShims.TempEntPoolWriteSpawn(slot, pSpawn) != 0;
        }
    }

    /// <summary>
    /// An effect NAME, as the index the wire carries for it.
    ///
    /// <para>
    /// SERVER ONLY. The "EffectDispatch" network string table is a server-side
    /// engine interface; the client only ever turns an index back into a name
    /// to pick a callback. A client that asked gets 0 — the table's first
    /// entry — which is what every null-guarded slot in this file answers and
    /// is not a failure a caller can do anything about.
    /// </para>
    ///
    /// <para>
    /// THERE IS ONE NUMBERING AND THE ENGINE OWNS IT. Resolving a name in
    /// managed code instead would be a second numbering of a table the engine
    /// already keeps, and the symptom of the two disagreeing is not a missing
    /// effect but the WRONG one — which reads as a bug in that effect rather
    /// than in this port. Adding a name already present answers the index it
    /// already has, so this is idempotent for the life of the level.
    /// </para>
    /// </summary>
    public unsafe int EffectDispatchIndex(string name)
    {
        if (!s_Initialized || !ServerAttached || name.Length == 0)
            return 0;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");

        fixed (byte* p = bytes) { return ServerShims.EffectDispatchIndex(p); }
    }

    /// <summary>
    /// An effect INDEX, as the name the wire's number stands for. The mirror of
    /// <see cref="EffectDispatchIndex"/>.
    ///
    /// <para>
    /// CLIENT ONLY, and the reverse of that slot's reason: the client is the
    /// side that has to pick a callback, so it is the side that reads the
    /// "EffectDispatch" string table backwards. A server that asked gets the
    /// empty string.
    /// </para>
    ///
    /// <para>
    /// AN EMPTY ANSWER IS "the table does not hold that index", which is how
    /// stock's NULL return crosses a boundary that cannot carry one.
    /// <c>c_te_effect_dispatch.cpp</c> does nothing at all in that case, and
    /// neither does the managed player — a dispatch with no name is not a
    /// dispatch of the empty name.
    /// </para>
    ///
    /// <para>
    /// The buffer is 128 bytes because <c>MAX_EFFECT_DISPATCH_STRING_BITS</c>
    /// bounds how MANY names there are (1024) and nothing bounds how long one
    /// is; 128 is the same order as every other name buffer on this ABI and is
    /// far past the longest of the fifty-two registrations
    /// (<c>"ParticleEffectStop"</c>, eighteen).
    /// </para>
    /// </summary>
    public unsafe string EffectDispatchName(int index)
    {
        if (!s_Initialized || !ClientAttached || index < 0)
            return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        ClientShims.EffectDispatchName(index, buf, bufSize);
        return FromUtf8(buf);
    }

    //-------------------------------------------------------------------------
    // Game events.
    //-------------------------------------------------------------------------

    public void ListenForGameEvent(string name)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { if (ServerShimsOrNull is { } a) a.ListenForGameEvent(p); else ClientShims.ListenForGameEvent(p); }
    }

    public int EventGetInt(IntPtr handle, string key)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return 0;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (ServerShimsOrNull is { } __att20 ? __att20.EventGetInt(handle, p) : ClientShims.EventGetInt(handle, p)); }
    }

    public float EventGetFloat(IntPtr handle, string key)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return 0f;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (ServerShimsOrNull is { } __att21 ? __att21.EventGetFloat(handle, p) : ClientShims.EventGetFloat(handle, p)); }
    }

    public bool EventGetBool(IntPtr handle, string key)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return false;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (ServerShimsOrNull is { } __att22 ? __att22.EventGetBool(handle, p) : ClientShims.EventGetBool(handle, p)) != 0; }
    }

    public string EventGetString(IntPtr handle, string key)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return FromUtf8((ServerShimsOrNull is { } __att23 ? __att23.EventGetString(handle, p) : ClientShims.EventGetString(handle, p))); }
    }

    /// <summary>
    /// A new game event by name (the server's CreateEvent), or zero for a name
    /// the game's event files do not declare, or on the client, which fires none.
    /// Fire it (<see cref="EventFire"/>, which frees it) or free it.
    /// </summary>
    public IntPtr EventCreate(string name)
    {
        if (!s_Initialized || !ServerAttached) return IntPtr.Zero;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return ServerShims.EventCreate(p); }
    }

    /// <summary>Sets an int field of an event made by <see cref="EventCreate"/>.</summary>
    public void EventSetInt(IntPtr handle, string key, int value)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { ServerShims.EventSetInt(handle, p, value); }
    }

    /// <summary>Sets a float field.</summary>
    public void EventSetFloat(IntPtr handle, string key, float value)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { ServerShims.EventSetFloat(handle, p, value); }
    }

    /// <summary>Sets a bool field.</summary>
    public void EventSetBool(IntPtr handle, string key, bool value)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { ServerShims.EventSetBool(handle, p, value ? 1 : 0); }
    }

    /// <summary>Sets a string field.</summary>
    public void EventSetString(IntPtr handle, string key, string value)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return;

        var k = Encoding.UTF8.GetBytes(key + "\0");
        var v = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
        fixed (byte* pk = k)
        fixed (byte* pv = v) { ServerShims.EventSetString(handle, pk, pv); }
    }

    /// <summary>Fires an event (the engine's FireEvent, which frees it); whether the engine took it.</summary>
    public bool EventFire(IntPtr handle)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return false;
        return ServerShims.EventFire(handle) != 0;
    }

    /// <summary>Frees an event that was created and will not be fired.</summary>
    public void EventFree(IntPtr handle)
    {
        if (!s_Initialized || !ServerAttached || handle == IntPtr.Zero) return;
        ServerShims.EventFree(handle);
    }

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

            return (s_Initialized && (ServerAttached || ClientAttached)) ? (ServerShimsOrNull is { } __att24 ? __att24.GetCurrentTime() : ClientShims.GetCurrentTime()) : 0f;
        }
    }

    //-------------------------------------------------------------------------
    // World items. Enough engine support for C# to define a pickup; what the
    // thing is and what touching it does stays in managed code.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Creates an entity of a registered class. Returns
    /// <see cref="EntityHandle.None"/> if no such class exists.
    /// </summary>
    public EntityHandle CreateEntity(string className)
    {
        // A CLASS OF KIND Logical MAKES NO SHELL, SO IT DOES NOT CROSS TO MAKE
        // ONE -- plan_entity.md step 2e. This is the runtime creator's route
        // (ent_create, a point_template instantiating its sources, a class
        // spawning a helper); MapSpawnCreate below is the map's.
        if (ShellLess.ClassIsShellLess(className))
            return ShellLess.Mine!.Create(className, default);

        if (!s_Initialized || !ServerAttached) return EntityHandle.None;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { return new EntityHandle(ServerShims.CreateEntity(p)); }
    }

    /// <summary>
    /// Creates a map entity and gives the SHELL its map keys in the same
    /// crossing — plan_entity.md step 2d.
    ///
    /// <para>
    /// <b>What it replaces is 5709 crossings on dm_lockdown.</b> The map's text
    /// used to live only inside the native parse, so every key of every entity
    /// was handed up one at a time through the shell's <c>KeyValue</c>: 72% of
    /// the whole map-load entity budget, and the largest single item in it.
    /// <c>MapSpawner</c> parses the lump in managed memory and gives every key
    /// to <c>ScriptedEntityRegistry</c> with no crossing at all; this hands the
    /// same keys down to the shell's own arm, together.
    /// </para>
    ///
    /// <para>
    /// <paramref name="packedKeys"/> is <c>key\0value\0key\0value\0…</c>.
    /// EVERY key, in map order, filtered on neither side — a native filter would
    /// drop exactly the typo'd keys the near-miss report exists to catch, which
    /// is the route step 2a refuted.
    /// </para>
    /// </summary>
    public EntityHandle MapSpawnCreate(string className, ReadOnlySpan<byte> packedKeys)
    {
        // AND THE MAP'S ROUTE -- step 2e. The packed keys go to the same eight
        // arms CStandaloneEntity::KeyValueShellOnly claimed, in managed memory;
        // the caller then hands every key to the registry exactly as it does
        // for an entity that has a shell, so nothing about the managed half of
        // a map load changes.
        if (ShellLess.ClassIsShellLess(className))
            return ShellLess.Mine!.Create(className, packedKeys);

        if (!s_Initialized || !ServerAttached) return EntityHandle.None;

        var name = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* pName = name)
        fixed (byte* pKeys = packedKeys)
        {
            return new EntityHandle(ServerShims.MapSpawnCreate(pName, pKeys, packedKeys.Length));
        }
    }

    /// <summary>
    /// Spawns an entity that <see cref="CreateEntity"/> made.
    ///
    /// Two steps rather than one because what goes BETWEEN them matters: Source
    /// hands an entity its keyvalues and its position before it spawns, and
    /// spawning is what reads them. An entity created and never spawned has no
    /// model, no physics and no think — it sits where it was put, inert, and
    /// looks like one that spawned wrong rather than one that never spawned.
    /// </summary>
    public void SpawnEntity(EntityHandle entity)
    {
        // A shell-less entity spawns in managed code, in the same position of
        // the same pass -- step 2e. The crossing this replaces was outbound and
        // is therefore not in any Crossings counter; it is recorded in the
        // step's handover rather than left for the performance pass to find.
        if (ShellLess.Claims(entity))
        {
            ShellLess.Mine!.Spawn(entity);
            return;
        }

        if (s_Initialized && ServerAttached)
            ServerShims.EntitySpawn(entity.Value);
    }

    /// <summary>Precaches and applies a model.</summary>
    public void SetEntityModel(EntityHandle entity, string model)
    {
        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(model + "\0");
        fixed (byte* p = bytes) { ServerShims.EntitySetModel(entity.Value, p); }
    }

    /// <summary>
    /// Turns the entity into a touchable pickup with the given half-extents.
    /// A trigger, not a solid: the player walks through it rather than into it.
    /// </summary>
    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntityMakePickup(entity.Value, sizeX, sizeY, sizeZ);
    }

    /// <summary>
    /// Every entity of a class, as a lazy sequence.
    ///
    /// Wraps Source's start-after iteration idiom. Handle-based throughout, so
    /// an entity removed mid-iteration fails to resolve rather than dangling —
    /// which matters because a caller can quite reasonably remove or spawn
    /// things while walking the list.
    /// </summary>
    public IEnumerable<EntityHandle> FindEntitiesByClassname(string className)
    {
        // The pointer work lives in FindNextByClassname because an iterator's
        // generated state machine does not inherit the unsafe context.
        uint current = EntityHandle.None.Value;

        while (true)
        {
            uint next = FindNextByClassname(current, className);
            if (next == EntityHandle.None.Value)
                break;

            current = next;
            yield return new EntityHandle(next);
        }

        // ---- AND THE ENTITIES THE ENGINE CANNOT SEE -- step 2e -------------
        //
        // THIS IS THE CLAUSE THAT MAKES `debug-api ents "*"` REPORT THE SAME
        // CLASSNAMES. The native walk is over the engine's entity list, and a
        // shell-less entity is not in it — without this second half every
        // logic_relay, filter and math_counter in the map would simply vanish
        // from every find-by-class in the build, silently, and the first thing
        // to notice would be a map whose logic had stopped working.
        //
        // AFTER the native ones and not interleaved: stock's walk is in entity
        // creation order and this preserves that for everything that has a
        // shell, rather than inventing an order for the whole list.
        if (ShellLess.Mine is not { } side)
            yield break;

        var extra = new List<EntityHandle>();
        side.FindByClassName(className, extra);

        foreach (var handle in extra)
            yield return handle;
    }

    private uint FindNextByClassname(uint startAfter, string className)
    {
        if (!s_Initialized || !ServerAttached)
            return EntityHandle.None.Value;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { return ServerShims.FindEntityByClassname(startAfter, p); }
    }

    /// <summary>
    /// Traces from a player's eye along their aim. Returns what was hit, or
    /// <see cref="EntityHandle.None"/> for a miss.
    /// </summary>
    public EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint)
    {
        hitPoint = default;

        if (!s_Initialized || !ServerAttached)
            return EntityHandle.None;

        float* xyz = stackalloc float[3];
        uint hit = ServerShims.TracePlayerAim(player.Value, range, xyz);

        hitPoint = new Vector(xyz[0], xyz[1], xyz[2]);
        return new EntityHandle(hit);
    }

    /// <summary>
    /// Applies damage through the normal pipeline, so the gamerules damage
    /// hooks and the death event still run.
    /// </summary>
    public void ApplyDamage(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.ApplyDamage(victim.Value, attacker.Value, damage, (int)damageType);
    }

    /// <summary>
    /// The classname of the weapon a player is holding, server side. Empty when
    /// they are empty-handed, which is a real state here — the loadout can be
    /// empty, and a player between weapons has none for a tick.
    /// </summary>
    public string GetPlayerActiveWeapon(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached) return string.Empty;

        byte* buffer = stackalloc byte[64];
        ServerShims.GetPlayerActiveWeapon(player.Value, buffer, 64);
        return FromUtf8(buffer);
    }

    /// <summary>
    /// Where a player's shots start — their eye, which is what the stock weapon
    /// code fires from. Separate from the aim trace because a weapon firing more
    /// than one pellet has to aim each one itself.
    /// </summary>
    public Vector GetPlayerShootOrigin(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached) return default;

        float* xyz = stackalloc float[3];
        ServerShims.GetPlayerShootOrigin(player.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>Whether a player is alive. False covers both dead and spectating.</summary>
    /// <summary>
    /// The player's facts and the shadow body native holds for them
    /// (plan_physics_bones_native_zero 2.5): what PlayerShadowBodies reads
    /// before it reads vphysics itself. False when the handle names no player.
    /// INTERNAL, like the three below and Teleported: they are the server
    /// interop's player-shadow half (EnginePlayerShadowNative), not an engine
    /// area any game code asks.
    /// </summary>
    internal bool PlayerShadowFacts(EntityHandle player, out PlayerShadowFacts facts)
    {
        facts = default;
        if (!s_Initialized || !ServerAttached) return false;

        facts.StructSize = (uint)sizeof(PlayerShadowFacts);
        fixed (PlayerShadowFacts* p = &facts)
            return ServerShims.PlayerShadowFacts(player.Value, p) != 0;
    }

    /// <summary>Native holds the body managed built (stand, crouch, controller). False when the player is gone.</summary>
    internal bool PlayerShadowAdopt(EntityHandle player, nint stand, nint crouch, nint controller)
        => s_Initialized && ServerAttached && ServerShims.PlayerShadowAdopt(player.Value, stand, crouch, controller) != 0;

    /// <summary>Native tears the held body down (DestroyVCollision), or drops it if its environment has gone.</summary>
    internal void PlayerShadowRelease(EntityHandle player)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerShadowRelease(player.Value);
    }

    /// <summary>The hull in play; after a command, m_oldOrigin and the move's step height consumed.</summary>
    internal void PlayerShadowCommit(EntityHandle player, int state, bool afterCommand)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerShadowCommit(player.Value, state, afterCommand ? 1 : 0);
    }

    /// <summary>
    /// Told of every teleport after native has carried it out: the server
    /// composition hands a player's to its shadow (stock's TeleportEntity
    /// moves a player's physics object with them; plan_physics_bones_native_zero
    /// 2.5 made the shadow managed's). Null tells nobody.
    /// </summary>
    internal Action<EntityHandle, Vector, bool>? Teleported { get; set; }


    /// <summary>
    /// UTIL_TraceEntity: the entity's hull swept from start to end with the
    /// mask and collision group, ignoring the entity (physics stock parity B7).
    /// </summary>
    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs,
                                           uint mask, int collisionGroup)
    {
        if (!s_Initialized || !ServerAttached) return new EntityHullTrace(start, true, true);

        float* s0 = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e0 = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        float* outEnd = stackalloc float[3];
        int bits = ServerShims.EntityTraceHull(entity.Value, s0, e0, lo, hi, (int)mask, collisionGroup, outEnd);
        return new EntityHullTrace(new Vector(outEnd[0], outEnd[1], outEnd[2]), (bits & 1) != 0, (bits & 2) != 0);
    }

    public bool PlayerIsAlive(EntityHandle player)
        => s_Initialized && ServerAttached && ServerShims.PlayerIsAlive(player.Value) != 0;

    /// <summary>
    /// Puts a player into or out of spectating.
    /// <see cref="ObserverMode.None"/> stops them watching WITHOUT respawning
    /// them, which is a real state and not the same thing.
    /// </summary>
    public void PlayerSetObserverMode(EntityHandle player, ObserverMode mode)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetObserverMode(player.Value, (int)mode);
    }

    public ObserverMode PlayerGetObserverMode(EntityHandle player)
        => (s_Initialized && ServerAttached)
            ? (ObserverMode)ServerShims.PlayerGetObserverMode(player.Value)
            : ObserverMode.None;

    /// <summary>
    /// Puts a dead player back in the world, through the engine's own respawn —
    /// so the spawn point choice, the loadout and the spawn event all still
    /// happen.
    /// </summary>
    public void PlayerRespawn(EntityHandle player)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerRespawn(player.Value);
    }

    /// <summary>
    /// Throws the weapon in a player's hands into the world, where somebody else
    /// can pick it up. False when they were empty-handed, which is a real state
    /// rather than a failure.
    /// </summary>
    public bool PlayerDropActiveWeapon(EntityHandle player, Vector velocity)
    {
        if (!s_Initialized || !ServerAttached) return false;

        float* v = stackalloc float[3] { velocity.X, velocity.Y, velocity.Z };
        return ServerShims.PlayerDropActiveWeapon(player.Value, v) != 0;
    }

    /// <summary>
    /// Destroys everything a player carries, and the suit too if asked.
    ///
    /// Not the same as dropping: <see cref="PlayerDropActiveWeapon"/> hands one
    /// weapon to the world where somebody else can take it, while this leaves
    /// nothing behind.
    /// </summary>
    public void PlayerRemoveAllItems(EntityHandle player, bool stripSuit)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerRemoveAllItems(player.Value, stripSuit ? 1 : 0);
    }

    /// <summary>
    /// How fast one player's world runs: 1 is normal, 0.5 is half speed.
    ///
    /// Deliberately here rather than in <c>Movement</c>, which is where a speed
    /// rule otherwise belongs. Movement is predicted, so a per-player factor the
    /// client cannot see makes the two sides disagree about where the player is
    /// on every unacknowledged command — and the symptom is rubber-banding. The
    /// engine networks this value, so both halves scale by the same number.
    /// </summary>
    public void PlayerSetLaggedMovement(EntityHandle player, float value)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetLaggedMovement(player.Value, value);
    }

    /// <summary>
    /// Strikes buttons out of a player's commands before anything reads them, or
    /// stops doing so.
    ///
    /// Only the named bits are touched, so a caller clears exactly what it
    /// claimed and leaves whatever else is held down alone.
    /// </summary>
    public void PlayerSetButtonsDisabled(EntityHandle player, InputButtons buttons,
                                                bool disabled)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetButtonsDisabled(player.Value, (int)buttons, disabled ? 1 : 0);
    }

    /// <summary>
    /// Holds buttons down for a player whether or not they are pressing them.
    /// The mirror of <see cref="PlayerSetButtonsDisabled"/>: only the named bits
    /// move either way, so a caller gives back exactly what it took.
    /// </summary>
    public void PlayerForceButtons(EntityHandle player, InputButtons buttons, bool forced)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerForceButtons(player.Value, (int)buttons, forced ? 1 : 0);
    }

    /// <summary>
    /// Whether the engine may move a player who did not steer — the step up onto
    /// a stair, the slide along a wall, the ladder mount.
    ///
    /// An engine call rather than a Movement rule because it is predicted on
    /// both sides and networked, exactly as the lagged movement value is.
    /// </summary>
    public void PlayerSetAllowAutoMovement(EntityHandle player, bool allow)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetAllowAutoMovement(player.Value, allow ? 1 : 0);
    }

    /// <summary>
    /// Whether a player wades rather than walks, together with the flag that
    /// stops their own movement fighting whatever is pushing them.
    ///
    /// Stock mispredicts this: the underlying variable has no send prop, so the
    /// client never learns it is set. Setting it here behaves exactly as stock
    /// does, misprediction included.
    /// </summary>
    public void PlayerSetSlowMovement(EntityHandle player, bool slow)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetSlowMovement(player.Value, slow ? 1 : 0);
    }

    /// <summary>
    /// Snaps where the player is LOOKING, and tells the client to adopt it.
    ///
    /// <para>
    /// Not <see cref="EntitySetAngles"/>: an entity's angles are where its
    /// model points, and a player's view is <c>CPlayerState::v_angle</c>, which
    /// is separate state the engine networks for prediction. The native side
    /// sets <c>fixangle</c> with it, which is the half that makes the client
    /// adopt the value rather than keep predicting from its own — without it
    /// the next usercmd's stale viewangles overwrite the snap before anyone
    /// sees it.
    /// </para>
    ///
    /// <para>Pitch, yaw, roll, in that order. Roll is taken rather than
    /// assumed: <c>setang_exact</c>'s stock spelling accepts one.</para>
    /// </summary>
    public void PlayerSnapEyeAngles(EntityHandle player, Vector pitchYawRoll)
    {
        if (!s_Initialized || !ServerAttached)
            return;

        float* a = stackalloc float[3] { pitchYawRoll.X, pitchYawRoll.Y, pitchYawRoll.Z };
        ServerShims.PlayerSnapEyeAngles(player.Value, a);
    }

    /// <summary>
    /// Sets the player's move type from outside a movement frame, and answers
    /// the one it had BEFORE the write.
    ///
    /// <para>
    /// The previous value comes back so that a toggle — which is what
    /// <c>noclip</c> is — is one crossing rather than a read and a write with a
    /// frame's worth of movement possible in between, on the very state that
    /// decides what that movement does.
    /// </para>
    ///
    /// <para>
    /// <see cref="MoveType.None"/> when the handle is not a hierarchy player,
    /// or when the slot is not bound. No real player is ever in that move type,
    /// so the refusal is distinguishable from an answer.
    /// </para>
    /// </summary>
    public MoveType PlayerSetMoveType(EntityHandle player, MoveType moveType)
    {
        if (!s_Initialized || !ServerAttached)
            return MoveType.None;

        return (MoveType)ServerShims.PlayerSetMoveType(player.Value, (int)moveType);
    }

    /// <summary>
    /// Keeps a player within <paramref name="radius"/> of a point, by slowing
    /// them near the edge rather than by a wall.
    ///
    /// <paramref name="width"/> is the band over which the slowing comes on and
    /// <paramref name="speedFactor"/> is what the speed is multiplied by at the
    /// edge. A radius of 0 releases the player, which is how the engine itself
    /// spells "not constrained".
    /// </summary>
    public void PlayerSetMovementConstraint(EntityHandle player, Vector centre,
                                                   float radius, float width, float speedFactor)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* c = stackalloc float[3] { centre.X, centre.Y, centre.Z };
        ServerShims.PlayerSetMovementConstraint(player.Value, c, radius, width, speedFactor);
    }

    /// <summary>Releases a player from any movement constraint.</summary>
    public void PlayerClearMovementConstraint(EntityHandle player)
        => PlayerSetMovementConstraint(player, default, 0f, 0f, 0f);

    /// <summary>
    /// Renders a player's view from <paramref name="view"/> instead of their own
    /// eyes. An invalid handle gives the view back.
    ///
    /// The entity may be on either hierarchy. What it must be is TRANSMITTED —
    /// the client takes the origin and angles off its own copy, so a view
    /// entity it was never sent leaves the player looking out of their own head
    /// with nothing to say why. See <see cref="EntitySetTransmitAlways"/>.
    /// </summary>
    public void PlayerSetViewEntity(EntityHandle player, EntityHandle view)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetViewEntity(player.Value, EntityIsValid(view) ? view.Value : 0u);
    }

    /// <summary>
    /// Whether a player may act at all. Stock's EnableControl, inverted to read
    /// as what it does rather than what it permits.
    ///
    /// Frozen is not safe: a frozen player is still shot at, and still dies.
    /// Anything that freezes somebody for a cutscene wants
    /// <see cref="EntitySetTakeDamageMode"/> as well.
    /// </summary>
    public void PlayerSetFrozen(EntityHandle player, bool frozen)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.PlayerSetFrozen(player.Value, frozen ? 1 : 0);
    }

    /// <summary>
    /// The buttons a player is holding right now.
    ///
    /// The read behind <see cref="PlayerSetButtonsDisabled"/>. Wanted by
    /// anything waiting for the player to press ANY key, which no subscription
    /// to a particular one can express.
    /// </summary>
    public InputButtons PlayerGetButtons(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached) return 0;
        return (InputButtons)ServerShims.PlayerGetButtons(player.Value);
    }

    /// <summary>
    /// The weapon a player is holding, as a handle. Invalid when empty-handed.
    ///
    /// <see cref="GetPlayerActiveWeapon"/> answers the same question with a
    /// classname, which is what a rule about weapons wants. This is for acting
    /// ON the weapon entity — hiding the viewmodel, most of all.
    /// </summary>
    public EntityHandle PlayerGetActiveWeaponEntity(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached)
            return EntityHandle.None;

        return new EntityHandle(ServerShims.PlayerGetActiveWeaponEntity(player.Value));
    }

    /// <summary>
    /// One inventory slot, as an untyped handle. Invalid for an empty slot, a
    /// slot out of 0..47, or no such player.
    ///
    /// The managed half of m_hMyWeapons: the slots become addressable without
    /// a CBaseCombatWeapon anywhere in the question, which is what lets the
    /// inventory's readers survive the weapons leaving that class.
    /// </summary>
    public EntityHandle PlayerGetWeaponEntity(EntityHandle player, int slot)
    {
        if (!s_Initialized || !ServerAttached)
            return EntityHandle.None;

        return new EntityHandle(ServerShims.PlayerGetWeaponEntity(player.Value, slot));
    }

    /// <summary>
    /// The player's view model, as an entity handle. Invalid when the player
    /// has none — before spawn, or as an observer.
    /// </summary>
    public EntityHandle PlayerGetViewModelEntity(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached)
            return EntityHandle.None;

        return new EntityHandle(ServerShims.PlayerGetViewModelEntity(player.Value));
    }

    /// <summary>
    /// Whether an entity may be hurt, answering what it was before.
    ///
    /// The previous value comes back rather than being remembered here, because
    /// invulnerability nests badly: only whoever took it away knows when it is
    /// done, and two things taking it away in turn must give back what each
    /// found rather than a shared idea of "normal".
    /// </summary>
    public TakeDamageMode EntitySetTakeDamageMode(EntityHandle entity, TakeDamageMode mode)
    {
        if (!s_Initialized || !ServerAttached) return TakeDamageMode.Yes;
        return (TakeDamageMode)ServerShims.EntitySetTakeDamageMode(entity.Value, (int)mode);
    }

    /// <summary>
    /// Whether anything can bump into this entity.
    ///
    /// Not <see cref="EntitySetBrushSolidity"/>, which is a brush choosing what
    /// shape it presents. This is FSOLID_NOT_SOLID on an entity that already has
    /// one — a player being stepped through.
    /// </summary>
    public void EntitySetNotSolid(EntityHandle entity, bool notSolid)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetNotSolid(entity.Value, notSolid ? 1 : 0);
    }

    /// <summary>
    /// Whether this entity is sent to every client regardless of where they
    /// are, rather than only when it is in somebody's PVS.
    ///
    /// Wanted by exactly one kind of entity: one a client is meant to be looking
    /// THROUGH from a room it is not in. A PVS check answers "not visible" for
    /// that entity, so it is never sent, so the client has nothing to take a
    /// view from.
    /// </summary>
    public void EntitySetTransmitAlways(EntityHandle entity, bool always)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetTransmitAlways(entity.Value, always ? 1 : 0);
    }

    /// <summary>
    /// Hands an entity one of a map's own keys, after it has spawned.
    ///
    /// The general form rather than a setter per field, because a keyvalue is
    /// the one channel every entity in this build understands and it is the
    /// only one that reaches both kinds of storage. A stock entity resolves the
    /// key against its datadesc and writes the member bound to it; an entity on
    /// the parallel hierarchy checks the few keys the shell claims and then
    /// offers it to its managed class, where it lands in a [ScriptVars] key.
    /// Neither kind has to be identified by the caller.
    ///
    /// Returns whether anything claimed the key. A key nobody wanted is a
    /// mapper's typo, and only the caller is in a position to say so.
    /// </summary>
    public bool EntitySetKeyValue(EntityHandle entity, string key, string value)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.Claims(entity))
            return ShellLess.Mine!.KeyValue(entity, key, value);

        if (!s_Initialized || !ServerAttached) return false;

        var keyBytes = Encoding.UTF8.GetBytes(key + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* pKey = keyBytes)
        fixed (byte* pValue = valueBytes)
        {
            return ServerShims.EntitySetKeyValue(entity.Value, pKey, pValue) != 0;
        }
    }

    /// <summary>
    /// Puts the weapon in a player's hands away, remembering which it was, and
    /// hides the viewmodel with it. False when they were holding nothing.
    /// </summary>
    public bool PlayerHolsterActiveWeapon(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached) return false;
        return ServerShims.PlayerHolsterActiveWeapon(player.Value) != 0;
    }

    /// <summary>
    /// Takes the remembered weapon back out. False when nothing was remembered,
    /// which is what a player who never had one looks like.
    /// </summary>
    public bool PlayerDeployLastWeapon(EntityHandle player)
    {
        if (!s_Initialized || !ServerAttached) return false;
        return ServerShims.PlayerDeployLastWeapon(player.Value) != 0;
    }

    /// <summary>
    /// Local player health, or -1 when there is no local player — the case in
    /// the menu and between levels. Distinct from 0 so the HUD can tell "no
    /// player" from "dead".
    /// </summary>
    public int LocalPlayerHealth
        => (s_Initialized && ClientAttached) ? ClientShims.GetLocalPlayerHealth() : -1;

    /// <summary>
    /// Removes an entity from the world — deferred to the end of the frame, as
    /// <c>UTIL_Remove</c> is. The handle keeps resolving until then.
    ///
    /// <para>
    /// <b>THE DIRECTORY FIRST, AND IT COSTS NO CROSSING</b> —
    /// <c>plan_entity.md</c> step 2c. A managed entity is marked in its side's
    /// <see cref="EntityDirectory"/> through <see cref="EntityRemovalRoutes"/>
    /// and freed by that directory's end-of-frame pass, in a batch. Before this
    /// step every removal left managed code here, reached
    /// <c>CStandaloneEntity::Remove</c> and was appended to a native list.
    /// </para>
    ///
    /// <para>
    /// <b>The slot is still the fall-back and still has work to do.</b> A handle
    /// the directory does not hold — a stock <c>CBaseEntity</c>, which is what
    /// <c>gib.cpp</c>, <c>physics_prop_ragdoll.cpp</c> and
    /// <c>EntityDissolve.cpp</c> still make — is not the directory's to mark,
    /// and it goes out through <c>EntityRemove</c> to <c>UTIL_Remove</c>. That
    /// arm leaves with those classes in Phase 4.
    /// </para>
    /// </summary>
    public void RemoveEntity(EntityHandle entity)
    {
        if (Removal.Remove(entity))
            return;

        if (s_Initialized && ServerAttached)
            ServerShims.EntityRemove(entity.Value);
    }

    /// <summary>
    /// THE SHELLS BEHIND ONE FRAME'S REMOVALS, FREED — one call, whatever the
    /// size of the batch (<c>plan_entity.md</c> §3.4, step 2c).
    ///
    /// <para>
    /// The only caller is <see cref="EntityDirectory.FreeRemoved"/>, which owns
    /// WHEN this happens; what a shell is and how it is freed is native's.
    /// An empty batch does not cross at all.
    /// </para>
    /// </summary>
    public void DestroyShells(ReadOnlySpan<EntityHandle> batch)
    {
        if (batch.IsEmpty || !s_Initialized || !ServerAttached)
            return;

        // AN EntityHandle IS NOT ITS RAW HANDLE: it stores it inverted, so
        // default is the invalid handle (Entity.cs). The ABI asks for packed
        // uint32s, so they are copied out: on the stack for an ordinary
        // frame's removals, from the pool for a level's teardown.
        const int OnStack = 256;
        uint[]? rented = batch.Length > OnStack ? ArrayPool<uint>.Shared.Rent(batch.Length) : null;
        try
        {
            Span<uint> raw = rented is null ? stackalloc uint[batch.Length] : rented.AsSpan(0, batch.Length);
            EntityHandle.CopyRaw(batch, raw);

            fixed (uint* p = raw)
            {
                ServerShims.DestroyShell(p, batch.Length);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<uint>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// <b>THE SAFETY COUNTER FOR THE ENGINE-INVISIBLE RANGE</b> —
    /// <c>plan_entity.md</c> step 2e's third gate clause.
    ///
    /// <para>
    /// How many times a handle from entries 2048..8191 has arrived at
    /// <c>SourceSharp_ResolveEntity</c> and failed to resolve in the engine's
    /// own entity list — which a shell-less entity's handle always does,
    /// because it was never added to that list, and a live stock server-only
    /// <c>CBaseEntity</c>'s never does.
    /// </para>
    ///
    /// <para>
    /// <b>Read it beside <see cref="EntityDirectory.ShellLessCount"/>, never on
    /// its own.</b> A build that created no shell-less entities would leak none
    /// of their handles either, so the zero would be true and mean nothing.
    /// <c>status.logical</c> reports the pair.
    /// </para>
    ///
    /// <para>Zero with no engine, which is what a hermetic test sees.</para>
    /// </summary>
    /// <remarks>
    /// <b>INTERNAL, and not on an engine area.</b> It is instrumentation about
    /// the boundary rather than a capability game code asks for — its one
    /// caller is <see cref="ScriptedEntityRegistry.ShellLessNativeReach"/>, and
    /// putting it on <c>IEngineEntities</c> would publish a diagnostic counter
    /// to every consumer of the entity API.
    /// </remarks>
    internal int InvisibleRangeReach()
        => (s_Initialized && ServerAttached) ? ServerShims.InvisibleRangeReach() : 0;

    /// <summary>
    /// A handle for a "skip this entity" parameter, with a shell-less one
    /// turned into <see cref="EntityHandle.None"/> — <c>plan_entity.md</c>
    /// step 2e.
    ///
    /// <para>
    /// <b>The substitution changes nothing about the trace and that is the
    /// argument for it.</b> A shell-less entity is not in the partition and has
    /// no collision model, so it cannot be hit; "skip it" and "skip nothing"
    /// are the same trace. What differs is that the first spends a handle from
    /// the engine-invisible range on a native call, which is exactly what
    /// <see cref="InvisibleRangeReach"/> counts — 94 of them per
    /// <c>dm_lockdown</c> run before this, all from emitters excluding
    /// themselves.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="EntityHandle.None"/> (which <c>default</c> is) and never
    /// the raw handle 0.</b> The empty skip is <c>INVALID_EHANDLE_INDEX</c>;
    /// entry ZERO is worldspawn, and substituting it would ask the trace to
    /// ignore the world.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts a spawning player at <paramref name="origin"/> facing
    /// <paramref name="angles"/> — <c>plan_entity.md</c> step 2e.
    ///
    /// <para>
    /// <b>Only meaningful from inside <c>SelectSpawnPoint</c></b>, which is
    /// called from <c>CBasePlayer::Spawn</c> before the player is made solid.
    /// Called later it is the bug the ABI's own comment records: a player made
    /// solid at the world origin and teleported out of it afterwards arrives
    /// embedded in whatever was at the destination.
    /// </para>
    ///
    /// <para>
    /// <paramref name="origin"/> is final, stock's one-unit lift included;
    /// <paramref name="angles"/> is the spot's full facing, of which the entity
    /// takes yaw only and the player state takes all three.
    /// </para>
    /// </summary>
    public void PlayerPlaceAtSpawn(EntityHandle player, Vector origin, Vector angles)
    {
        if (!s_Initialized || !ServerAttached)
            return;

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* pyr = stackalloc float[3] { angles.X, angles.Y, angles.Z };

        ServerShims.PlayerPlaceAtSpawn(player.Value, xyz, pyr);
    }


    /// <summary>
    /// Ends the round and sends everyone to intermission.
    ///
    /// Routed through the gamerules so the base class runs its own end-of-round
    /// bookkeeping — scores, timers, the map change that follows — rather than
    /// the shell reimplementing it.
    /// </summary>
    public void EndRound()
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EndRound();
    }

    /// <summary>
    /// Team number for a player, or -1 if the handle is not a player.
    ///
    /// -1 rather than 0, because 0 is a real team a player can be on
    /// (unassigned) and conflating the two would make a rule unable to tell
    /// "not a player" from "no team yet".
    /// </summary>
    public int GetPlayerTeam(EntityHandle player)
        => (s_Initialized && ServerAttached) ? ServerShims.GetPlayerTeam(player.Value) : -1;

    /// <summary>
    /// The engine's userid for a player, or 0 for anything that is not one.
    ///
    /// The other half of the player's identity. Entity calls take a handle;
    /// game events name players by userid, and until this existed a rule
    /// reading a death event could not reach either player it described.
    /// </summary>
    public int GetPlayerUserId(EntityHandle player)
        => (s_Initialized && ServerAttached) ? ServerShims.GetPlayerUserId(player.Value) : 0;

    /// <summary>
    /// The player a userid names, or None if nobody on the server has it.
    ///
    /// Built from the property rather than asked for as its own engine call —
    /// see GetPlayerUserId in sourcesharp.h. The walk is over player slots
    /// only, so it is bounded by the server's player count and not by the
    /// entity list; it runs on a death, not on a tick.
    ///
    /// Userid 0 is the engine's way of saying "the world", so it resolves to
    /// None rather than to whichever slot happens to be empty.
    /// </summary>
    public EntityHandle FindPlayerByUserId(int userId)
    {
        if (userId == 0)
            return EntityHandle.None;

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            var player = GetPlayerHandle(slot);

            if (player != EntityHandle.None && GetPlayerUserId(player) == userId)
                return player;
        }

        return EntityHandle.None;
    }

    /// <summary>
    /// Makes a sound available. Must happen during level load.
    ///
    /// A sound emitted without this produces a warning and silence, which is
    /// the worst shape of failure: the entity works, the map works, and one
    /// thing makes no noise for a reason nothing states.
    /// </summary>
    public void PrecacheSound(string soundName)
    {
        if (!s_Initialized || !ServerAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) ServerShims.PrecacheSound(p);
    }

    /// <summary>
    /// Plays a sound on an entity, so it comes from where the entity is.
    ///
    /// <para>
    /// <b>A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES</b> —
    /// plan_entity.md step 2e's rule applied to step 8e-sound's first defect,
    /// and it is the same route <see cref="EntityGetOrigin"/> and
    /// <see cref="CreateEntity"/> already take.
    /// </para>
    ///
    /// <para>
    /// <b>The mechanism, because the failure it replaces was silent.</b>
    /// <c>SourceSharp_EntityEmitSound</c> resolves the handle through
    /// <c>g_pEntityList</c> so that native code can ask the entity where it is.
    /// A shell-less entity is never in that list by construction — no edict, no
    /// entity index, no C++ object of any kind — so the resolve failed for every
    /// one of them and the emit returned. Step 4b measured it on
    /// <c>dm_lockdown</c>, where 81 of 473 live entities are shell-less: three
    /// shelled <c>func_door</c>s gave three <c>EmitSound:</c> traces and one
    /// shell-less <c>info_target</c> gave zero, while step 2e's
    /// <c>logical.nativeReach</c> counter moved 24 → 25 for that one emit.
    /// </para>
    ///
    /// <para>
    /// <b>The position has to come from the side that owns it</b>, and this side
    /// owns it: <see cref="ShellLessEntity.Origin"/> is where a shell-less
    /// entity's position lives, and <see cref="EmitSoundAtPosition"/> already
    /// takes a point. Nothing new crosses and no ABI slot was added — which is
    /// also why <c>nativeReach</c> stays FLAT across the emit rather than
    /// counting one more reach, and the gate reads that number as the second,
    /// independent proof that this route was taken.
    /// </para>
    ///
    /// <para>
    /// <b>The divergence step 4b named does not exist, and it was checked in
    /// the code rather than reasoned about.</b> 4b's write-up
    /// said this routing was only equivalent once
    /// <c>SourceSharp_EmitSoundAtPosition</c> stopped writing
    /// <c>VOL_NORM</c>/<c>PITCH_NORM</c> over what the artist put in the
    /// soundscript. Two facts say it never did: <c>EmitSound_t</c>'s default
    /// constructor already sets <c>m_flVolume( VOL_NORM )</c> and
    /// <c>m_nPitch( PITCH_NORM )</c> (<c>game/shared/shareddefs.h:834</c>), so
    /// the two writes store the value that was already there; and
    /// <c>CSoundEmitterSystem::EmitSoundByHandle</c> reads neither field unless
    /// <c>SND_CHANGE_VOL</c> / <c>SND_CHANGE_PITCH</c> is set in
    /// <c>m_nFlags</c> (<c>sound_emitter.cpp:557-568</c>), and neither path sets
    /// any flag. The soundscript's own volume and pitch survive both routes.
    /// </para>
    ///
    /// <para>
    /// What DOES differ between the two routes is the recipient filter, and the
    /// difference is the right way round. The entity route builds a
    /// <c>CPASAttenuationFilter</c> around the source's CENTRE; the position
    /// route builds one around the point. For a shell-less entity those are the
    /// same point — it is of kind <c>Logical</c>, so it has no model, no OBB and
    /// therefore no centre distinct from its origin, which is what
    /// <c>shell_sound.cpp</c>'s own note about <c>SOUND_FROM_WORLD</c> already
    /// says from the native side.
    /// </para>
    /// </summary>
    public void EntityEmitSound(EntityHandle entity, string soundName)
    {
        if (soundName.Length == 0) return;

        if (ShellLess.State(entity) is { } shellLess)
        {
            // COUNTED BEFORE THE EMIT, and counted whether or not the engine
            // table is bound: this is the twin of logical.nativeReach, and what
            // it has to make observable is that the ROUTE was taken. Counting
            // after a call that returns early on an unbound table would report
            // zero for the very build a hermetic test runs in.
            ShellLess.NoteSoundPlacedHere();
            EmitSoundAtPosition(soundName, shellLess.Origin);
            return;
        }

        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) ServerShims.EntityEmitSound(entity.Value, p);
    }

    /// <summary>
    /// Stops a LOOPING sound. A one-shot ends by itself; a loop runs until the
    /// map changes.
    ///
    /// <para>
    /// <b>A shell-less entity's loop is stopped where its emit went</b>, which
    /// is <c>SOUND_FROM_WORLD</c>: the engine keys a playing sound by (entity
    /// index, name), so a stop that named a different index would leave the
    /// loop running forever and say nothing. The handle is still handed down —
    /// <c>SourceSharp_EntityStopSound</c> answers the world index for a handle
    /// it cannot resolve, and its own note says why that is this line's
    /// counterpart rather than a guess.
    /// </para>
    ///
    /// <para>
    /// No managed class of kind <c>Logical</c> loops a sound today: all eight
    /// callers of this method are brushes, an effect and a physics prop, every
    /// one of which has a shell. It is written down anyway because the emit
    /// above now works for a shell-less entity, and an emit that can start a
    /// loop nothing can stop is a worse defect than the one it closed.
    /// </para>
    /// </summary>
    public void EntityStopSound(EntityHandle entity, string soundName)
    {
        if (!s_Initialized || !ServerAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) ServerShims.EntityStopSound(entity.Value, p);
    }


    /// <summary>
    /// A soundscript from the world at <paramref name="origin"/>, on a CHAN_*
    /// channel, at <paramref name="volume"/> times the script's own: its
    /// parameters, a PAS attenuation filter at their level, and the emit
    /// (vphysics_sound.h's Play* body; plan_physics_bones_native_zero 2.3).
    /// False when the script has no parameters.
    /// </summary>
    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume)
    {
        if (!s_Initialized || !ServerAttached || soundName.Length == 0) return false;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = bytes) return ServerShims.EmitWorldSoundScript(p, xyz, channel, volume) != 0;
    }

    /// <summary>A noise with no entity to come from.</summary>
    public void EmitSoundAtPosition(string soundName, Vector origin)
    {
        if (!s_Initialized || !ServerAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = bytes) ServerShims.EmitSoundAtPosition(p, xyz);
    }

    /// <summary>
    /// Hands the movement parameters to the native loop. Called once per side
    /// at startup — see <see cref="MovementParams"/> for why both.
    /// </summary>
    public void RegisterMovementParams(MovementParams parameters)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        if (ServerShimsOrNull is { } __att25) __att25.RegisterMovementParams(&parameters);
        else ClientShims.RegisterMovementParams(&parameters);
    }

    /// <summary>
    /// Which kinds of damage behave which way. Stated once at startup, for the
    /// same reason the movement parameters are.
    ///
    /// The native side carries stock's values as its defaults, so a build where
    /// this is never called behaves like Half-Life rather than treating no
    /// damage as special — which matters because the table is read from damage
    /// paths that can run before the managed runtime is up on a listen server
    /// still loading.
    /// </summary>
    public void RegisterDamageRules(DamageRuleMasks masks)
    {
        if (!s_Initialized || !ServerAttached) return;

        ServerShims.RegisterDamageRules(&masks);
    }

    /// <summary>
    /// The rules' constants. Silently does nothing on the client, where the
    /// slot is left NULL -- there is no rules object there to read the table.
    /// </summary>
    public void RegisterGameRulesPolicy(GameRulesPolicyBlob policy)
    {
        if (!s_Initialized || !ServerAttached) return;

        ServerShims.RegisterGameRulesPolicy(&policy);
    }

    /// <summary>
    /// The collision matrix. Registered from both sides; answers whether this
    /// side's native received it, so a side that cannot serve the slot says so
    /// instead of keeping native's defaults in silence (which the client did
    /// until the slot moved into the shared IShimCollision base).
    /// </summary>
    public bool RegisterCollisionRules(uint[] rows)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return false;

        var blob = default(CollisionRulesBlob);
        blob.SetRows(rows);

        if (ServerShimsOrNull is { } __att26) __att26.RegisterCollisionRules(&blob);
        else ClientShims.RegisterCollisionRules(&blob);
        return true;
    }

    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry,
                                        int tracerType = 0, float damageForce = 0f,
                                        float damage = 0f)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            if (ServerShimsOrNull is { } __att27) __att27.RegisterAmmoType(p, (int)damageType, maxCarry, tracerType, damageForce, damage);
            else ClientShims.RegisterAmmoType(p, (int)damageType, maxCarry, tracerType, damageForce, damage);
        }
    }

    /// <summary>
    /// Registers what a weapon does when fired.
    ///
    /// Held natively because the stock attack path asks for these DURING a shot,
    /// and one generic shell serves every weapon name — so none of it can be
    /// inherited from a per-weapon C++ class the way stock does.
    /// </summary>
    public void RegisterWeaponType(string className, float fireRate,
                                          float spreadDegrees, int pellets)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { if (ServerShimsOrNull is { } a) a.RegisterWeaponType(p, fireRate, spreadDegrees, pellets); else ClientShims.RegisterWeaponType(p, fireRate, spreadDegrees, pellets); }
    }

    /// <summary>
    /// Claims an entity classname for the generic weapon shell.
    ///
    /// This is what makes a weapon EXIST as far as the engine is concerned: the
    /// name a map places, that <c>GiveNamedItem</c> creates by and that
    /// <c>ent_create</c> types. Without it the name resolves to no factory and
    /// the entity is dropped as unknown.
    ///
    /// <para>
    /// Server-only on the native side, and the reason is not entity handles for
    /// once — the entity factory dictionary belongs to the server, and a client
    /// builds a weapon from its network class rather than from a name. So the
    /// slot is null on the client and this call does nothing there, which is
    /// what lets one <c>WeaponTypes.Register</c> run unchanged on both sides.
    /// </para>
    /// </summary>
    public void RegisterWeaponEntity(string className)
    {
        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { ServerShims.RegisterWeaponEntity(p); }
    }

    //-------------------------------------------------------------------------
    // Weapons.
    //
    // The state machine that decides when a weapon fires and reloads is managed.
    // What is here is what IS the engine: assembling a bullet and tracing it,
    // playing a view-model animation, reading how long one lasts.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Everything the state machine reads. False when the handle is not a weapon
    /// or has no owner — a weapon lying on the ground has no machine to run.
    /// </summary>
    public bool WeaponGetState(EntityHandle weapon, out WeaponState state)
    {
        state = default;

        if (!s_Initialized || !ServerAttached) return false;

        fixed (WeaponState* p = &state)
            return ServerShims.WeaponGetState(weapon.Value, p) != 0;
    }

    public void WeaponSetNextPrimaryAttack(EntityHandle weapon, float time)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponSetNextPrimaryAttack(weapon.Value, time);
    }

    public void WeaponSetNextSecondaryAttack(EntityHandle weapon, float time)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponSetNextSecondaryAttack(weapon.Value, time);
    }

    public void WeaponSetClip(EntityHandle weapon, int clip)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponSetClip(weapon.Value, clip);
    }

    public void WeaponSetInReload(EntityHandle weapon, bool inReload)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponSetInReload(weapon.Value, inReload ? 1 : 0);
    }

    /// <summary>
    /// Takes from the owner's reserve for this weapon's ammo. Answers how many
    /// were actually taken, which is what makes a partial reload possible.
    /// </summary>
    public int WeaponTakeAmmo(EntityHandle weapon, int count)
        => (s_Initialized && ServerAttached) ? ServerShims.WeaponTakeAmmo(weapon.Value, count) : 0;

    /// <summary>Spends from the alt-fire pool, which is a different ammunition type.</summary>
    public int WeaponTakeSecondaryAmmo(EntityHandle weapon, int count)
        => (s_Initialized && ServerAttached)
             ? ServerShims.WeaponTakeSecondaryAmmo(weapon.Value, count) : 0;

    public void WeaponPlaySound(EntityHandle weapon, WeaponSound sound)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponPlaySound(weapon.Value, (int)sound);
    }

    public void WeaponSendAnim(EntityHandle weapon, WeaponAnim anim)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponSendAnim(weapon.Value, (int)anim);
    }

    /// <summary>
    /// How long an animation lasts, which is how long a reload takes. Read from
    /// the model rather than declared, so it cannot drift from the view model
    /// the weapon script names.
    /// </summary>
    public float WeaponAnimDuration(EntityHandle weapon, WeaponAnim anim)
        => (s_Initialized && ServerAttached)
            ? ServerShims.WeaponAnimDuration(weapon.Value, (int)anim) : 0f;

    /// <summary>
    /// Fires bullets: the trace, the damage the ammo carries, decals, blood,
    /// hitgroup scaling and physics force. The largest thing that stays native,
    /// and the right thing to keep — there is no decision in any of it.
    /// </summary>
    public void WeaponFireBullets(EntityHandle weapon, int shots, float spreadDegrees)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponFireBullets(weapon.Value, shots, spreadDegrees);
    }

    /// <summary>The muzzle flash and the owner's firing animation, which are one event.</summary>
    public void WeaponMuzzleFlash(EntityHandle weapon)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponMuzzleFlash(weapon.Value);
    }

    public void WeaponAddViewKick(EntityHandle weapon)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponAddViewKick(weapon.Value);
    }

    /// <summary>
    /// Lets the view model settle back to idle.
    ///
    /// Not cosmetic: a weapon whose idle is never sent stops animating after its
    /// deploy, and a view model with no live sequence stops being drawn — which
    /// reads as the gun vanishing from the player's hands.
    /// </summary>
    public void WeaponIdle(EntityHandle weapon)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.WeaponIdle(weapon.Value);
    }

    /// <summary>
    /// Gives a player ammo. Returns how much was actually added, which is less
    /// than asked for near the carry limit.
    /// </summary>
    public int GivePlayerAmmo(EntityHandle player, int count, string ammoName)
    {
        if (!s_Initialized || !ServerAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(ammoName + "\0");
        fixed (byte* p = bytes) { return ServerShims.GivePlayerAmmo(player.Value, count, p); }
    }

    /// <summary>
    /// Gives a player an item by entity class name. Returns false if no such
    /// class exists — a typo would otherwise look like a rules bug.
    /// </summary>
    public bool GivePlayerItem(EntityHandle player, string className)
    {
        if (!s_Initialized || !ServerAttached) return false;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { return ServerShims.GivePlayerItem(player.Value, p) != 0; }
    }

    //-------------------------------------------------------------------------
    // C#-declared entities.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Declares an entity class and the outputs mappers can connect to. The
    /// native side builds the entity description from this, which is why it has
    /// to happen before a map is parsed.
    /// </summary>
    public void RegisterScriptedEntity(string className, string[] outputNames, ScriptedEntityKind kind)
    {
        if (!s_Initialized || !ServerAttached)
            return;

        var nameBytes = Encoding.UTF8.GetBytes(className + "\0");

        // One pinned UTF-8 buffer per output name, plus the array of pointers
        // the native side reads. Both only need to survive the call — it copies
        // what it keeps.
        var outputBytes = new byte[outputNames.Length][];
        for (int i = 0; i < outputNames.Length; i++)
            outputBytes[i] = Encoding.UTF8.GetBytes(outputNames[i] + "\0");

        var handles = new System.Runtime.InteropServices.GCHandle[outputNames.Length];
        byte** pointers = stackalloc byte*[outputNames.Length == 0 ? 1 : outputNames.Length];

        try
        {
            for (int i = 0; i < outputNames.Length; i++)
            {
                handles[i] = System.Runtime.InteropServices.GCHandle.Alloc(
                    outputBytes[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                pointers[i] = (byte*)handles[i].AddrOfPinnedObject();
            }

            fixed (byte* pName = nameBytes)
            {
                ServerShims.RegisterScriptedEntity(pName, pointers, outputNames.Length, (int)kind);
            }
        }
        finally
        {
            for (int i = 0; i < handles.Length; i++)
                if (handles[i].IsAllocated)
                    handles[i].Free();
        }
    }

    /// <summary>
    /// WHICH OPS THIS CLASS OVERRIDES (plan_entity.md §3.4).
    ///
    /// <para>
    /// A bitmask over <see cref="EntityOp"/>: bit <c>1 &lt;&lt; op</c> set means
    /// "cross for this op". The shell stores it in the class descriptor and
    /// tests it before every dispatch, so an op a class does not override costs
    /// zero managed transitions.
    /// </para>
    ///
    /// <para>
    /// Called after <see cref="RegisterScriptedEntity"/> for the same name —
    /// that is what creates the class this is stored on. A mask for a name that
    /// was never registered is dropped by the far side, silently, the shape
    /// every other by-name slot has.
    /// </para>
    /// </summary>
    public void RegisterClassOps(string className, uint opMask)
    {
        if (!s_Initialized || !ServerAttached)
            return;

        var nameBytes = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* pName = nameBytes)
        {
            ServerShims.RegisterClassOps(pName, opMask);
        }
    }

    /// <summary>
    /// THE OUTBOUND HALF OF THE OP TABLE (plan_entity.md §3.4; step 1b's second
    /// ABI request).
    ///
    /// <para>
    /// <c>Dispatch</c> is native asking managed; this is managed telling native,
    /// through the same <see cref="EntityOp"/> vocabulary. One op uses it,
    /// <see cref="EntityOp.PositionChanged"/> — see
    /// <see cref="EntityPositionChanged"/>, which is the shape every caller
    /// should take rather than building an <see cref="OpArgs"/> by hand.
    /// </para>
    /// </summary>
    /// <remarks>
    /// INTERNAL, and deliberately: the only shape a caller should take is
    /// <see cref="EntityPositionChanged"/> and the ops that follow it. A public
    /// raw-pointer entry point would be an <see cref="OpArgs"/> built by hand
    /// at every call site, which is exactly the per-op argument list this table
    /// exists to have one of.
    /// </remarks>
    internal int EntityDispatch(EntityHandle h, EntityOp op, OpArgs* args)
    {
        if (!s_Initialized || !ServerAttached)
            return 0;

        return ServerShims.EntityDispatch(h.Value, (int)op, args);
    }

    /// <summary>
    /// MANAGED MOVED THE ENTITY; NATIVE HAS WORK TO DO ABOUT IT
    /// (<c>.claude/entity-port/abi-requests/1b.md</c> §2).
    ///
    /// <para>
    /// The four things the tail of <c>CStandaloneEntity::SetAbsTransform</c>
    /// does, in that order and for the reasons written out there: move it in
    /// the spatial partition (which is what a trace and a trigger overlap
    /// actually query), mark the origin and angle offsets, carry the children
    /// eagerly so parenting stays ordering-free, and touch the triggers LAST
    /// because the answer is about where the entity now is.
    /// </para>
    ///
    /// <para>
    /// <paramref name="previousOrigin"/> is what the trigger half needs and is
    /// the one thing native cannot read for itself once managed has written the
    /// new position. That is why it is carried rather than fetched.
    /// </para>
    /// </summary>
    public void EntityPositionChanged(EntityHandle h, Vector previousOrigin)
    {
        float* previous = stackalloc float[3];
        previous[0] = previousOrigin.X;
        previous[1] = previousOrigin.Y;
        previous[2] = previousOrigin.Z;

        var args = default(OpArgs);
        args.Vec0 = previous;

        EntityDispatch(h, EntityOp.PositionChanged, &args);
    }

    //-------------------------------------------------------------------------
    // Movement. What a trigger does to whoever is standing in it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Hands a broken surface the cracked edges it should draw.
    ///
    /// Pushed when the state changes, not asked for per frame: what stays native
    /// is the mesh building, which cannot come across at four interop calls a
    /// quad, and what stays here is deciding which edges there are.
    /// </summary>
    public unsafe void SurfaceSetEdges(EntityHandle entity, byte[] packed, int count)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (byte* p = packed)
        {
            ClientShims.SurfaceSetEdges(entity.Value, p, count);
        }
    }

    /// <summary>
    /// The right and up vectors of a surface facing a direction.
    ///
    /// The ENGINE's VectorAngles/AngleVectors rather than a port of them: the
    /// client places the panes it draws with the same two vectors, in native
    /// code, from the same normal. Two implementations agreeing to the last bit
    /// is not something to assume.
    /// </summary>
    public unsafe (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward)
    {
        if (!s_Initialized || !ServerAttached)
            return (default, default);

        float* f = stackalloc float[3] { forward.X, forward.Y, forward.Z };
        float* r = stackalloc float[3];
        float* u = stackalloc float[3];

        ServerShims.AngleVectorsFromForward(f, r, u);

        return (new Vector(r[0], r[1], r[2]), new Vector(u[0], u[1], u[2]));
    }

    /// <summary>Facing, as pitch/yaw/roll.</summary>
    public Vector EntityGetAngles(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Angles;

        if (!s_Initialized || !(ServerAttached || ClientAttached)) return default;

        float* pyr = stackalloc float[3];
        if (ServerShimsOrNull is { } __att29) __att29.EntityGetAngles(h.Value, pyr);
        else ClientShims.EntityGetAngles(h.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    public Vector EntityGetVelocity(EntityHandle h)
    {
        // ZERO, AND IT IS THE TRUE ANSWER -- step 2e. A shell-less entity is
        // not in any movement integrator and has nowhere to keep a velocity;
        // the shell's answer for a MOVETYPE_NONE server-only entity was zero
        // too.
        if (ShellLess.Claims(h))
            return default;

        if (!s_Initialized || !ServerAttached) return default;

        return Motion.Velocity(h);
    }

    /// <summary>Where something looks from. A player's origin is at their feet.</summary>
    public Vector EntityGetEyePosition(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached) return default;

        float* xyz = stackalloc float[3];
        ServerShims.EntityGetEyePosition(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>Which way something is looking. Not its body angles.</summary>
    public Vector EntityGetEyeAngles(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached) return default;

        float* pyr = stackalloc float[3];
        ServerShims.EntityGetEyeAngles(h.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>
    /// The middle of something's bounding box. A brush entity's origin is
    /// usually at the map's world origin rather than anywhere near the brush.
    /// </summary>
    public Vector EntityGetWorldSpaceCenter(EntityHandle h)
    {
        // THE ORIGIN, because a point entity's bounds are a point -- step 2e.
        // WorldSpaceCenter is the middle of the collision bounds, and a
        // shell-less entity has none: stock's CBaseEntity::WorldSpaceCenter
        // over a zero-extent OBB answers the origin exactly.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Origin;

        if (!s_Initialized || !ServerAttached) return default;

        float* xyz = stackalloc float[3];
        ServerShims.EntityGetWorldSpaceCenter(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public void EntitySetVelocity(EntityHandle h, Vector velocity)
    {
        if (!s_Initialized || !ServerAttached) return;

        Motion.SetVelocity(h, velocity);
    }

    /// <summary>
    /// Adds to velocity rather than replacing it. Clearing the ground matters
    /// more than it looks: something standing on the floor stays stuck to it, so
    /// an upward push does nothing unless the ground is let go of first.
    /// </summary>
    public void EntityApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround)
    {
        if (!s_Initialized || !ServerAttached) return;

        // clearGround is the non-simulated arm's, and that arm writes nothing
        // (EntityPhysicsMotion's remarks): the native forward ignored it too.
        Motion.ApplyVelocityImpulse(h, impulse);
    }

    /// <summary>
    /// Moves something somewhere, optionally facing a new way. One call rather
    /// than origin and angles separately, because the engine unlinks and relinks
    /// as one operation.
    ///
    /// <para>
    /// <b>AND IT SAYS SO, which is the half the client needs</b> — see
    /// <see cref="TeleportDiscontinuity"/>. Every teleport on this hierarchy
    /// arrives here, so bumping the counter on THIS side of the crossing covers
    /// <c>point_teleport</c>, <c>trigger_teleport</c>, the trains, the dev
    /// commands and any managed caller yet to be written, without a line of C++.
    /// Without it the new pose is just a pose a long way from the last one, and
    /// the client draws the entity travelling the whole distance.
    /// </para>
    ///
    /// <para>
    /// The bump is announced with <see cref="MarkEntityBlockChanged"/> rather
    /// than left to the origin's own mark: the two are different SendProps and
    /// <c>MapPropOffsetsToIndices</c> matches an offset EXACTLY, so a marked
    /// origin says nothing at all about the word beside it.
    /// </para>
    /// </summary>
    public void EntityTeleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity)
    {
        if (!s_Initialized || !ServerAttached) return;

        if (TeleportDiscontinuity.Bump(GetEntityBlock(h), out _))
            MarkEntityBlockChanged(h, TeleportDiscontinuity.FrameOffset);

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        if (angles is not Vector a)
        {
            ServerShims.EntityTeleport(h.Value, xyz, null, zeroVelocity ? 1 : 0);
        }
        else
        {
            float* pyr = stackalloc float[3] { a.X, a.Y, a.Z };
            ServerShims.EntityTeleport(h.Value, xyz, pyr, zeroVelocity ? 1 : 0);
        }

        // The solver half, after the entity has moved (it reads the facing
        // the entity now has when no angles were given).
        Motion.TeleportObject(h, origin, angles, zeroVelocity);

        Teleported?.Invoke(h, origin, zeroVelocity);
    }

    /// <summary>
    /// Makes <paramref name="parent"/> carry <paramref name="child"/>: the child
    /// keeps a fixed offset in the parent's frame and follows it from then on.
    ///
    /// <para>
    /// A default <paramref name="parent"/> unparents, the way the SDK's own
    /// SetParent reads a null parent. <paramref name="parentAttachment"/> is
    /// one-based, matching the skeleton's attachment lookup, and 0 means
    /// the parent's own origin rather than a frame on its model.
    /// </para>
    ///
    /// <para>
    /// Answers false when the link was refused — a cycle, an entity that has not
    /// spawned, or a child on the SDK's hierarchy, which is not supported. Each
    /// of those says so on the console; none of them asserts.
    /// </para>
    /// </summary>
    public bool EntitySetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0)
    {
        if (!s_Initialized || !ServerAttached) return false;

        return ServerShims.EntitySetParent(child.Value, parent.Value, parentAttachment) != 0;
    }

    /// <summary>
    /// What carries this entity, or <see cref="EntityHandle.None"/>. A handle rather than a
    /// pointer: the parent is resolved on every walk, so one whose entity has
    /// gone comes back as nothing rather than as a number that names a stranger.
    /// </summary>
    public EntityHandle EntityGetParent(EntityHandle child)
    {
        // EntityHandle.None: INVALID_EHANDLE_INDEX, which is what native
        // answers for "nothing carries this" (and default, since 2026-10-05).
        if (!s_Initialized || !ServerAttached) return EntityHandle.None;

        return new EntityHandle(ServerShims.EntityGetParent(child.Value));
    }

    /// <summary>
    /// Which frame on the parent this entity hangs from: one-based, 0 for the
    /// parent's own origin. Asked separately from <see cref="EntityGetParent"/>
    /// because "parented to the origin" and "not parented" are different answers
    /// and one number could not carry both.
    /// </summary>
    public int EntityGetParentAttachment(EntityHandle child)
    {
        if (!s_Initialized || !ServerAttached) return 0;

        return ServerShims.EntityGetParentAttachment(child.Value);
    }

    /// <summary>
    /// The offset this entity keeps in its parent's frame — what parenting
    /// actually is, rather than a position that happens to follow.
    ///
    /// <para>
    /// This is NOT a teleport, and the difference is the whole split the
    /// hierarchy is built on. Writing an ABSOLUTE position of a parented entity
    /// means "put it here, once, and relative to the parent from then on", so it
    /// re-derives the offset. This replaces the offset itself, which is what a
    /// child sliding along its parent wants. Each half left null keeps what the
    /// child already has.
    /// </para>
    /// </summary>
    public void EntitySetLocalTransform(EntityHandle child, Vector? origin, Vector? angles)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* xyz = null;
        float* pyr = null;

        if (origin is Vector o)
        {
            float* buf = stackalloc float[3] { o.X, o.Y, o.Z };
            xyz = buf;
        }

        if (angles is Vector a)
        {
            float* buf = stackalloc float[3] { a.X, a.Y, a.Z };
            pyr = buf;
        }

        ServerShims.EntitySetLocalTransform(child.Value, xyz, pyr);
    }

    /// <summary>
    /// That same offset, read back. Answers the entity's ABSOLUTE origin and
    /// angles when nothing is carrying it, so a caller can read a local frame
    /// without first asking whether the entity has a parent.
    /// </summary>
    public void EntityGetLocalTransform(EntityHandle child, out Vector origin, out Vector angles)
    {
        origin = default;
        angles = default;

        if (!s_Initialized || !ServerAttached) return;

        float* xyz = stackalloc float[3];
        float* pyr = stackalloc float[3];

        ServerShims.EntityGetLocalTransform(child.Value, xyz, pyr);

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        angles = new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>
    /// Who owns an entity, which for a projectile is who fired it.
    ///
    /// This is what stops a projectile hitting its own shooter, and it has to
    /// be the engine's business rather than the entity's: the trace that
    /// carries a projectile forward is what decides the flight ended, and a
    /// managed check on the touch happens after that decision.
    /// </summary>
    public void EntitySetOwner(EntityHandle h, EntityHandle owner)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetOwner(h.Value, owner.Value);
    }

    /// <summary>
    /// How many times a projectile reflects off what it hits before its flight
    /// ends. Zero — the default — stops it at the first thing it touches.
    ///
    /// This has to be the engine's business: the reflection is about the
    /// surface NORMAL, and the normal exists only inside the step trace that
    /// moves the projectile. A touch handler is told what was hit, never which
    /// way it faced. What stays here is what a bounce MEANS, which is nothing
    /// at all for a bolt and the whole character of an energy ball.
    /// </summary>
    public void EntitySetBounce(EntityHandle h, int bounces)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetBounce(h.Value, bounces);
    }

    public void EntitySetGravity(EntityHandle h, float gravity)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetGravity(h.Value, gravity);
    }

    public void EntitySetFriction(EntityHandle h, float friction)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetFriction(h.Value, friction);
    }

    //-------------------------------------------------------------------------
    // WHAT MANAGED PARENTING ASKS OF THE WORLD (step 2h, made live).
    //
    // Four calls, and the count is what says how much of parenting is managed
    // now: the registry, the composition, the cycle refusal, the deferred
    // parentname and the sweep are all in Hierarchy.cs, and these are the
    // questions it cannot answer for itself. Not one of them decides anything.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE ENGINE BLOCK, AND IT IS NOT <see cref="GetEntityBlob"/>.
    ///
    /// <para>
    /// <b>The two overlap and have different lengths, which is exactly the trap.</b>
    /// The block is the fixed 352-byte prefix of the entity's allocation and is
    /// always there — <c>CStandaloneEntity::operator new</c> floors every
    /// allocation at <c>SourceSharp_StandaloneMinBlobBytes()</c> for that reason.
    /// <see cref="GetEntityBlob"/> answers the NETWORKED payload, whose length is
    /// the entity's tier: eight words for a class that declared no state of its
    /// own, which is most of the map. Reading the block through that span sees
    /// thirty-two bytes of it and refuses everything past them — silently,
    /// because a short span is a legal span.
    /// </para>
    ///
    /// <para>
    /// So a composition asking "where is this parent" through the payload span
    /// gets a refusal for every ordinary prop in the map and leaves its children
    /// standing still. That is the bug this slot exists to remove, and it is the
    /// reason the block gets a getter of its own rather than a longer comment on
    /// the other one.
    /// </para>
    /// </summary>
    public Span<byte> GetEntityBlock(EntityHandle h)
    {
        if (!s_Initialized || !ServerAttached)
            return Span<byte>.Empty;

        int size;
        void* p = ServerShims.EntityGetEngineBlock(h.Value, &size);

        if (p == null || size <= 0)
            return Span<byte>.Empty;

        return new Span<byte>(p, size);
    }

    /// <summary>
    /// THE CHILD'S SOLVER OBJECT, DESTROYED — what making a parent link does
    /// besides arithmetic, and a stated divergence from stock.
    ///
    /// <para>
    /// Stock shadows a simulated child instead. There is no shadow support on
    /// this hierarchy, and leaving the object alone is strictly worse than
    /// destroying it: a static object cannot be moved by writing the entity's
    /// origin, so the model would follow its parent while an invisible hull
    /// stayed behind, stopping bullets and players in mid-air. A parented child
    /// stops being simulated and becomes a thing that follows.
    /// </para>
    ///
    /// <para>
    /// NOT UNDONE BY UNPARENTING. The object destroyed here was built from the
    /// model's compiled collision data by whoever asked for it, and only they
    /// know whether it should be static, asleep or debris.
    /// </para>
    /// </summary>
    public void EntityDestroyPhysics(EntityHandle entity)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntityDestroyPhysics(entity.Value);
    }

    /// <summary>
    /// Stock's <c>VPhysicsSwapObject</c>: the shell holds <paramref name="physicsObject"/>
    /// (0 for none) and <paramref name="previous"/> is the object it held
    /// before, which is NOT destroyed. False when the handle names no shell.
    /// INTERNAL: the server interop's half of the managed physics init
    /// (EntityPhysicsCreation), not an area game code asks.
    /// </summary>
    internal bool EntitySwapPhysicsObject(EntityHandle entity, nint physicsObject, out nint previous)
    {
        previous = 0;
        if (!s_Initialized || !ServerAttached)
            return false;

        void* held = null;
        int ok = ServerShims.EntitySwapPhysicsObject(entity.Value, physicsObject, &held);
        previous = (nint)held;
        return ok != 0;
    }

    /// <summary>
    /// THE FRAME'S OVERLAPPING PAIRS — one call, a span of them
    /// (<c>plan_entity.md</c> §3.8 and step 2b).
    ///
    /// <para>
    /// Fills <paramref name="into"/> and answers how many pairs there ARE,
    /// which may be more than it could hold. The list is built once per frame
    /// and answered as many times as it is asked for, so the caller grows its
    /// buffer and asks again — see <c>OverlapReader</c>, which is the
    /// only caller and does exactly that. Taking the truncated answer would
    /// read to the touch diff as every pair past the end having left, and a
    /// departure is something the game acts on.
    /// </para>
    ///
    /// <para>
    /// <b>ZERO WITH NO ENGINE</b>, which is what a hermetic test and a
    /// client-side directory both get. Zero pairs is a legitimate frame — an
    /// empty map has one — so this cannot be distinguished from a real answer,
    /// and it must not be: the diff's response to it is to report every link
    /// as having ended, which for a directory that never had one is nothing.
    /// </para>
    /// </summary>
    public int EntityOverlapReport(Span<OverlapPair> into)
    {
        if (!s_Initialized || !ServerAttached)
            return 0;

        // The empty span is a legal ASK: "how many are there". Native writes
        // nothing for it and answers the count, which is how the reader sizes
        // itself without a first call that throws away pairs.
        fixed (OverlapPair* p = into)
            return ServerShims.EntityOverlapReport((uint*)p, into.Length);
    }

    /// <summary>
    /// WHICH HIERARCHY HOLDS THIS ENTITY, AND WHETHER IT CAN POSE — three
    /// answers in one call.
    ///
    /// <para>
    /// One call rather than three because the three are asked together and only
    /// together: <see cref="ParentFrameRule.KindFor"/> takes all of them and
    /// answers which frame a child hangs from. Asking separately would also
    /// admit an answer no entity ever has — on both hierarchies at once —
    /// because the two questions could be asked a frame apart.
    /// </para>
    ///
    /// <para><see cref="ParentProbe.None"/> for a handle that resolves to nothing.</para>
    /// </summary>
    public ParentProbe EntityParentProbe(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached) return ParentProbe.None;

        return (ParentProbe)ServerShims.EntityParentProbe(entity.Value);
    }

    /// <summary>
    /// WHERE A ONE-BASED ATTACHMENT IS, IN THE WORLD, on an entity of either
    /// hierarchy.
    ///
    /// <para>
    /// THE ONE THING MANAGED PARENTING CANNOT DERIVE. An attachment frame is a
    /// posed skeleton, and both hierarchies pose on their own clock from the
    /// model's own bone data. Two answers to "where is this model's muzzle" is
    /// the shape of bug nobody finds by reading, so nothing here re-derives it.
    /// </para>
    ///
    /// <para>
    /// False means the model does not carry that attachment. The out values are
    /// written either way — native fills the entity's own place, which is what
    /// both sides' <c>GetAttachment</c> does — but a caller that composes
    /// against them anyway has silently parented a muzzle flash to an origin,
    /// so parenting reads the result.
    /// </para>
    /// </summary>
    public bool EntityGetAttachmentFrame(EntityHandle entity, int attachment,
                                                out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;

        if (!s_Initialized || !ServerAttached) return false;

        float* xyz = stackalloc float[3];
        float* pyr = stackalloc float[3];

        int got = ServerShims.EntityGetAttachmentFrame(entity.Value, attachment, xyz, pyr);

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        angles = new QAngle(pyr[0], pyr[1], pyr[2]);

        return got != 0;
    }

    /// <summary>
    /// An attachment NAME to a one-based index, on either hierarchy. 0 for a
    /// name the model does not carry — the engine's own spelling.
    ///
    /// <para>
    /// the skeleton's attachment lookup answers the same question of the
    /// parallel hierarchy alone, and that is not enough for parenting: a map's
    /// <c>parentattachment</c> is looked up on the PARENT's model, and the
    /// shipped content's parents are the map's own doors and lifts.
    /// </para>
    /// </summary>
    public int EntityLookupAttachmentAny(EntityHandle entity, string attachmentName)
    {
        if (!s_Initialized || !ServerAttached || attachmentName.Length == 0)
            return 0;

        var bytes = Encoding.UTF8.GetBytes(attachmentName + "\0");
        fixed (byte* p = bytes) { return ServerShims.EntityLookupAttachmentAny(entity.Value, p); }
    }

    /// <summary>
    /// THE SDK'S FIND-BY-NAME, which <see cref="FindEntityByName"/> is not: that
    /// one walks the shim's own vector and answers only the parallel hierarchy.
    /// Resolving a map's <c>parentname</c> has to reach both.
    /// </summary>
    public EntityHandle FindBaseEntityByName(string name)
    {
        if (!s_Initialized || !ServerAttached || name.Length == 0)
            return EntityHandle.None;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return new EntityHandle(ServerShims.FindBaseEntityByName(p)); }
    }

    //-------------------------------------------------------------------------
    // Host.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Runs a console command as if typed at the server console. Runs it now,
    /// not next frame — so it must be called on the game thread.
    /// </summary>
    public void ExecuteServerCommand(string command)
    {
        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(command + "\0");
        fixed (byte* p = bytes) { ServerShims.ExecuteServerCommand(p); }
    }

    /// <summary>
    /// Runs a console command on ONE client, as if that player had typed it.
    ///
    /// Whether it is obeyed is the client's business: a command it does not have,
    /// or one the client marks server-only, does nothing there and reports
    /// nothing back. So this cannot be used to ask a client a question — only to
    /// tell it something.
    /// </summary>
    public void SendClientCommand(EntityHandle player, string command)
    {
        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(command + "\0");
        fixed (byte* p = bytes) { ServerShims.SendClientCommand(player.Value, p); }
    }

    /// <summary>
    /// Whether this body, when it dies, becomes a ragdoll the server simulates
    /// rather than one the client makes for itself.
    ///
    /// Takes any entity and quietly does nothing unless it is a combat
    /// character, because "is this a combat character" is a native question:
    /// the flag lives on CBaseCombatCharacter and only its BecomeRagdoll reads
    /// it. A volume that sweeps everything walking through it would otherwise
    /// have to ask before every call and could not.
    /// </summary>
    public void EntitySetForceServerRagdoll(EntityHandle entity, bool force)
    {
        if (!s_Initialized || !ServerAttached) return;

        ServerShims.EntitySetForceServerRagdoll(entity.Value, force ? 1 : 0);
    }

    /// <summary>Adds to a player's score. Negative points are refused unless allowed.</summary>
    public void AddPlayerScore(EntityHandle player, int points, bool allowNegative)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.AddPlayerScore(player.Value, points, allowNegative ? 1 : 0);
    }

    /// <summary>Player armour, which the engine keeps separately from health.</summary>
    public int EntityGetArmor(EntityHandle entity)
        => (s_Initialized && ServerAttached) ? ServerShims.EntityGetArmor(entity.Value) : 0;

    public void EntitySetArmor(EntityHandle entity, int armor)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetArmor(entity.Value, armor);
    }

    /// <summary>Whether a brush is drawn.</summary>
    public void EntitySetVisible(EntityHandle entity, bool visible)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetVisible(entity.Value, visible ? 1 : 0);
    }

    /// <summary>Whether a brush can be walked through, walked into, or neither.</summary>
    public void EntitySetBrushSolidity(EntityHandle entity, BrushSolidity solidity)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetBrushSolidity(entity.Value, (int)solidity);
    }

    /// <summary>
    /// Shows or hides a pickup without destroying it, so an item that respawns
    /// keeps the entity a mapper may have wired outputs to.
    /// </summary>
    public void EntitySetPickupEnabled(EntityHandle entity, bool enabled)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetPickupEnabled(entity.Value, enabled ? 1 : 0);
    }

    //-------------------------------------------------------------------------
    // Brush movement. Doors, lifts, platforms.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Moves a brush to a position at a constant speed, reporting arrival
    /// through OnMoveDone. The stepping is native: it has to stop exactly on the
    /// destination, and a door that stops a fraction short never closes.
    /// </summary>
    public void EntityMoveToPosition(EntityHandle entity, Vector destination, float speed)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* xyz = stackalloc float[3] { destination.X, destination.Y, destination.Z };
        ServerShims.EntityMoveToPosition(entity.Value, xyz, speed);
    }

    /// <summary>Rotates a brush to an orientation at a constant speed.</summary>
    public void EntityMoveToAngles(EntityHandle entity, Vector destination, float speed)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* pyr = stackalloc float[3] { destination.X, destination.Y, destination.Z };
        ServerShims.EntityMoveToAngles(entity.Value, pyr, speed);
    }

    /// <summary>
    /// Spins an entity continuously. Not the same as rotating TO an angle: a fan
    /// never arrives anywhere, so it has no move-done and no destination.
    /// </summary>
    public void EntitySetAngularVelocity(EntityHandle entity, Vector degreesPerSecond)
    {
        if (!s_Initialized || !ServerAttached) return;

        Motion.SetAngularVelocity(entity, degreesPerSecond);
    }

    /// <summary>
    /// What a physics object is actually turning at, degrees per second.
    ///
    /// Read from the simulation rather than the entity: a prop's entity-level
    /// angular velocity is not refreshed every tick, so a sensor watching one
    /// through the entity would mostly read stale zeroes.
    /// </summary>
    public Vector EntityGetAngularVelocity(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached) return default;

        return Motion.AngularVelocity(entity);
    }

    /// <summary>
    /// Applies force at a point in the world rather than at the centre of mass.
    ///
    /// The only form that can spin something — the difference between pushing a
    /// crate and tipping it. <see cref="EntityApplyVelocityImpulse"/> always
    /// acts through the centre, so it cannot rotate anything at all.
    /// </summary>
    public void EntityApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint)
    {
        if (!s_Initialized || !ServerAttached) return;

        Motion.ApplyForceOffset(entity, force, worldPoint);
    }

    /// <summary>Spins a physics object about its own centre.</summary>
    public void EntityApplyTorque(EntityHandle entity, Vector torque)
    {
        if (!s_Initialized || !ServerAttached) return;

        Motion.ApplyTorque(entity, torque);
    }

    // Engine.EntityFireInput WAS HERE, AND IS DELETED WITH ITS SLOT --
    // plan_entity.md step 2a. Firing an input at an entity does not touch the
    // engine any more: the delivery is EntityDirectory's and the route is
    // EntityIoRoutes.FireInput, which is what IEngineEntities.FireInput calls.
    // A managed wrapper around a deleted slot would be a member EngineAreaTests
    // correctly reports as forwarded by nothing.

    /// <summary>
    /// Hurts everything in range, and shows nothing.
    ///
    /// <para>
    /// The counterpart to the explosion an entity makes (Explosions.Fire,
    /// managed) and not a quieter
    /// version of it: an explosion is an ENTITY the engine builds by classname,
    /// carrying a sprite, a sound and a scorch mark that its spawnflags only
    /// partly suppress. This is the damage traversal on its own, which is what
    /// something like point_hurt wants — invisible harm on a timer, where a bang
    /// every tenth of a second would be the bug.
    /// </para>
    ///
    /// <para>
    /// The falloff and the trace to each victim are the engine's, so a wall
    /// still stops it.
    /// </para>
    /// </summary>
    /// <param name="attacker">
    /// Who is credited with anything it kills. The default is
    /// <see cref="EntityHandle.None"/>, nobody: native resolves it to NULL and
    /// <c>CTakeDamageInfo</c> credits the kill to no one — what a map firing
    /// this from a timer means.
    /// </param>
    public void RadiusDamage(Vector origin, float damage, float radius,
                                    DamageType damageType, EntityHandle attacker = default)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        ServerShims.RadiusDamage(p, damage, radius, (int)damageType, attacker.Value);
    }

    /// <summary>
    /// The explosion temp entity: fireball, sound, particles, dynamic lights.
    ///
    /// <para>
    /// Presentation ONLY — this hurts nobody. <see cref="ExplosionDamage"/> is
    /// the other half, and env_explosion fires both because its spawnflags can
    /// suppress either one independently.
    /// </para>
    ///
    /// <para>
    /// <paramref name="flags"/> is the TE_EXPLFLAG_* set, NOT the entity's
    /// spawnflags. The recipient set is a PVS query made native-side from the
    /// origin: an explosion written to an empty filter is silently invisible.
    /// </para>
    /// </summary>
    public void ExplosionEffect(Vector origin, int modelIndex, float scale,
                                       int frameRate, int flags, int radius, int magnitude)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        ServerShims.ExplosionEffect(p, modelIndex, scale, frameRate, flags, radius, magnitude);
    }

    /// <summary>
    /// Radius damage as an explosion does it — with an inflictor, an ignored
    /// entity and a damage force, none of which <see cref="RadiusDamage"/> can
    /// carry.
    ///
    /// <para>
    /// <paramref name="inflictor"/> is what the blast's traces pass THROUGH,
    /// and an entity that explodes where it stands must name itself. Without it
    /// the first trace starts inside that entity's own hull, stops at fraction
    /// zero, and the falloff term is zero for every victim in range — they all
    /// take the full magnitude however far away they stand.
    /// </para>
    /// </summary>
    public void ExplosionDamage(Vector origin, float magnitude, float radius,
                                       DamageType damageType, EntityHandle attacker,
                                       EntityHandle inflictor, EntityHandle ignored,
                                       float damageForce)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        ServerShims.ExplosionDamage(p, magnitude, radius, (int)damageType, attacker.Value,
                            inflictor.Value, ignored.Value, damageForce);
    }

    /// <summary>
    /// The CONTENTS_* mask at a point. See <see cref="Contents"/>.
    /// </summary>
    public Contents PointContents(Vector origin)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return Contents.Empty;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        return (Contents)(ServerShimsOrNull is { } __att30 ? __att30.PointContents(p) : ClientShims.PointContents(p));
    }

    /// <summary>
    /// Kicks the view of every player in range.
    ///
    /// Not a shake: a shake rattles over a duration, this is one shove — what a
    /// mapper reaches for when something lands rather than when something
    /// rumbles. A radius of zero is everyone, as with the shake.
    /// </summary>
    public void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* p = stackalloc float[3] { punch.X, punch.Y, punch.Z };
        ServerShims.ViewPunch(o, p, radius, inAir ? 1 : 0);
    }

    /// <summary>How heavy a physics object is. Zero if it has no physics at all.</summary>
    public float EntityGetMass(EntityHandle entity)
        => (s_Initialized && ServerAttached) ? Motion.Mass(entity) : 0f;

    public void EntitySetMass(EntityHandle entity, float mass)
    {
        if (s_Initialized && ServerAttached)
            Motion.SetMass(entity, mass);
    }

    /// <summary>
    /// Whether the engine moves this at all. A prop with motion disabled is
    /// still solid and still collides; it simply does not respond.
    /// </summary>
    public void EntityEnableMotion(EntityHandle entity, bool enable)
    {
        if (s_Initialized && ServerAttached)
            Motion.EnableMotion(entity, enable);
    }

    /// <summary>
    /// Puts a settled physics object back into the simulation —
    /// <c>IPhysicsObject::Wake()</c>, which is
    /// <c>ensure_in_simulation()</c> and nothing else.
    ///
    /// <para>
    /// NOT <see cref="EntityEnableMotion"/> with true. Waking does not unpin a
    /// prop whose motion a map deliberately disabled, and stock's
    /// <c>CPhysicsProp::InputWake</c> (<c>props.cpp:2707</c>) does not either:
    /// a crate pinned by the MotionDisabled spawnflag stays pinned through a
    /// Wake input.
    /// </para>
    /// </summary>
    public void EntityPhysicsWake(EntityHandle entity)
    {
        if (s_Initialized && ServerAttached)
            Motion.Wake(entity);
    }

    /// <summary>
    /// Lets a physics object settle out of the simulation —
    /// <c>IPhysicsObject::Sleep()</c> and nothing else.
    ///
    /// <para>
    /// NOT <see cref="EntityEnableMotion"/> with false, which turns
    /// <c>IsMoveable()</c> false. A SLEPT object stays moveable, so any
    /// contact revives it and the player's shadow controller still shoves it;
    /// a PINNED one revives for nothing and reads to
    /// the player's shadow controller as a wall that clamps the
    /// player's velocity to zero along the contact normal.
    /// </para>
    /// </summary>
    public void EntityPhysicsSleep(EntityHandle entity)
    {
        if (s_Initialized && ServerAttached)
            Motion.Sleep(entity);
    }

    /// <summary>
    /// ORs physics game flags (FVPHYSICS_*, and SourceSharp's own such as
    /// force-collide-moveable) into the entity's physics object, as stock's
    /// PhysSetGameFlags. The collision rules read them. No object, no effect.
    /// </summary>
    public void EntityAddPhysicsGameFlags(EntityHandle entity, uint flags)
    {
        if (s_Initialized && ServerAttached)
            Motion.AddGameFlags(entity, flags);
    }


    /// <summary>
    /// The entity's <c>IPhysicsObject *</c>, or 0 for none: what managed calls
    /// vphysics through directly, by the generated <c>PhysicsObjectFwd</c>
    /// (plan_physics_managed). Valid until the entity's object is destroyed.
    /// </summary>
    public nint EntityPhysicsObject(EntityHandle entity)
        => s_Initialized && ServerAttached ? ServerShims.EntityPhysicsObject(entity.Value) : 0;

    /// <summary>
    /// The level's <c>IPhysicsEnvironment *</c>, or 0 between levels: managed
    /// installs its own vphysics callback tables on it, by the generated
    /// <c>PhysicsEnvironmentFwd</c> (plan_physics_managed B2).
    /// </summary>
    public nint PhysicsEnvironmentPointer()
        => s_Initialized && ServerAttached ? ServerShims.PhysicsEnvironmentPointer() : 0;


    /// <summary>
    /// <c>IPhysicsCollision::CollideGetExtent</c> for a physics object at its
    /// current pose: the farthest point of its collide along
    /// <paramref name="direction"/>. Zero for no object, no collide, or no
    /// collision table. Called through the vtables: a Vector by-value return
    /// is spelled (tools/vtable-layout's BY_VALUE_RETURNS).
    /// </summary>
    public unsafe Vector PhysicsCollideExtent(nint physicsObject, Vector direction)
    {
        if (!s_Initialized || !ServerAttached || physicsObject == 0 || !PhysicsCollisionTable(out void* collision, out PhysicsCollisionFwd* vt))
            return Vector.Zero;

        PhysicsObjectFwd* obj = VPhysics.PhysicsObject;
        void* collide = obj->GetCollide((void*)physicsObject);
        if (collide == null)
            return Vector.Zero;

        Vector origin;
        QAngle angles;
        obj->GetPosition((void*)physicsObject, &origin, &angles);
        return vt->CollideGetExtent(collision, collide, &origin, &angles, &direction);
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
    /// The level's world physics object (native's g_PhysWorldObject), which
    /// carries no game data, so the solver's answers know it by identity
    /// (plan_physics_bones_native_zero 2.1). 0 between levels.
    /// </summary>
    public nint PhysicsWorldObject()
        => s_Initialized && ServerAttached ? ServerShims.PhysicsWorldObject() : 0;

    /// <summary>
    /// Each active object's owner told the solver moved it, carried out by
    /// native (PhysicsOwner_t::VPhysicsUpdate), and the entities each object
    /// is the own object of written to <paramref name="owners"/>, in order
    /// (plan_physics_bones_native_zero 2.3). The count written. INTERNAL:
    /// the server interop's physics frame half (EnginePhysicsFrameNative).
    /// </summary>
    internal unsafe int PhysicsUpdateOwners(ReadOnlySpan<nint> objects, ReadOnlySpan<float> velocities, Span<uint> owners)
    {
        if (!s_Initialized || !ServerAttached || objects.IsEmpty)
            return 0;

        int count = Math.Min(Math.Min(objects.Length, owners.Length), velocities.Length / 3);
        fixed (nint* pObjects = objects)
        fixed (float* pVelocities = velocities)
        fixed (uint* pOwners = owners)
            return ServerShims.PhysicsUpdateOwners((void**)pObjects, pVelocities, count, pOwners);
    }

    /// <summary>
    /// <c>IPhysicsObject::GetInertia</c>, through the object's vtable (a Vector
    /// by-value return is spelled: tools/vtable-layout's BY_VALUE_RETURNS).
    /// Zero for no object.
    /// </summary>
    public unsafe Vector PhysicsInertia(nint physicsObject)
    {
        if (!s_Initialized || !ServerAttached || physicsObject == 0)
            return Vector.Zero;

        return (VPhysics.PhysicsObject)->GetInertia((void*)physicsObject);
    }

    /// <summary>
    /// Whether a prop is simulated or merely collided with. Must be set during
    /// spawn — afterwards the physics object already exists as the wrong kind.
    /// </summary>
    public void EntitySetPhysicsStatic(EntityHandle entity, bool isStatic)
    {
        if (s_Initialized && ServerAttached)
            PhysicsInit?.SetStatic(entity, isStatic);
    }

    /// <summary>
    /// Whether things pass through this entity's physics object and are
    /// reported, rather than colliding with it. A physics trigger is a separate
    /// kind of thing to the solver, not a solid that happens to be intangible.
    /// </summary>
    public void EntityBecomePhysicsTrigger(EntityHandle entity, bool isTrigger)
    {
        if (s_Initialized && ServerAttached)
            Motion.BecomeTrigger(entity, isTrigger);
    }

    /// <summary>
    /// ONE MODEL, ONE CROSSING: everything a model can be asked, packed.
    ///
    /// <para>
    /// The raw blob; <see cref="ModelDescriptor.Decode"/> reads it and
    /// <see cref="ModelDescriptors"/> keeps the answer for the level. Null when
    /// the slot is not bound, when the index names no model, or when the model
    /// has no studio data — a sprite, a brush model — all of which the eight
    /// per-call entry points this replaces also answered nothing for.
    /// </para>
    ///
    /// <para>
    /// TWO CALLS, SIZE THEN CONTENT, which is this ABI's usual buffer
    /// protocol. The size is not knowable without walking every string, and a
    /// blob written into a buffer that was too small would decode as a
    /// differently shaped model rather than as a failure — so native writes
    /// only when the whole thing fits, and this refuses a second answer that
    /// disagrees with the first rather than reading a short buffer.
    /// </para>
    /// </summary>
    public byte[]? ModelDescriptorBlob(int modelIndex)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        int needed = (ServerShimsOrNull is { } __att31 ? __att31.ModelDescriptor(modelIndex, null, 0) : ClientShims.ModelDescriptor(modelIndex, null, 0));

        if (needed <= 0) return null;

        var blob = new byte[needed];

        fixed (byte* p = blob)
        {
            int written = (ServerShimsOrNull is { } __att32 ? __att32.ModelDescriptor(modelIndex, p, needed) : ClientShims.ModelDescriptor(modelIndex, p, needed));

            // A SECOND ANSWER THAT DISAGREES IS A REFUSAL, not a short read.
            // The model could only have changed between the two calls if the
            // mdl cache moved under us, and decoding half a blob produces a
            // model with plausible-looking, wrong sequences.
            if (written != needed) return null;
        }

        return blob;
    }

    /// <summary>Plays a sequence from the start.</summary>
    public void EntitySetSequence(EntityHandle entity, int sequence)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetSequence(entity.Value, sequence);
    }

    /// <summary>
    /// The entity index the engine gave THIS client, 0 when there is no local
    /// player — the menu, or between levels. Client-side. The client half of
    /// "is this entity mine": the viewmodel and the HUD both decide locality
    /// by comparing an OwnerIndex against this.
    /// </summary>
    public int GetLocalPlayerIndex()
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.GetLocalPlayerIndex();
    }

    /// <summary>
    /// Puts a standalone entity into (or takes it out of) the renderer's
    /// VIEWMODEL pass, where it is drawn at the local player's eyes with the
    /// viewmodel FOV and the depth-range hack — exactly where a stock
    /// C_BaseViewModel draws. Client-side. Managed code decides WHICH entity
    /// (the one whose OwnerIndex is the local player); this is the mechanism.
    /// </summary>
    public void StandaloneSetViewModel(EntityHandle entity, bool enable)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetViewModel(entity.Value, enable ? 1 : 0);
    }

    public void EntityStopMoving(EntityHandle entity)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntityStopMoving(entity.Value);
    }

    /// <summary>Sets an entity's orientation.</summary>
    public void EntitySetAngles(EntityHandle entity, Vector angles)
    {
        if (ShellLess.State(entity) is { } shellLess)
        {
            shellLess.Angles = angles;
            return;
        }

        if (!s_Initialized || !ServerAttached) return;

        float* pyr = stackalloc float[3] { angles.X, angles.Y, angles.Z };
        ServerShims.EntitySetAngles(entity.Value, pyr);
    }

    /// <summary>The name a mapper gave an entity. Empty if it has none.</summary>
    public string EntityGetName(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(entity) is { } shellLess)
            return shellLess.Name;

        if (!s_Initialized || !ServerAttached) return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        ServerShims.EntityGetName(entity.Value, buf, bufSize);
        return FromUtf8(buf);
    }

    /// <summary>How big a brush is, as its collision bounds.</summary>
    public Vector EntityGetBoundsSize(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached) return default;

        float* xyz = stackalloc float[3];
        ServerShims.EntityGetBoundsSize(entity.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// Where the mapper drew the brush. Every door in Source is described as an
    /// offset from this rather than in world coordinates.
    /// </summary>
    public Vector EntityGetSpawnOrigin(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnOrigin;

        if (!s_Initialized || !ServerAttached) return default;

        float* xyz = stackalloc float[3];
        ServerShims.EntityGetSpawnOrigin(entity.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public Vector EntityGetSpawnAngles(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnAngles;

        if (!s_Initialized || !ServerAttached) return default;

        float* pyr = stackalloc float[3];
        ServerShims.EntityGetSpawnAngles(entity.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>Spawnflags the map set on an entity.</summary>
    public int EntityGetSpawnFlags(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnFlags;

        return (s_Initialized && ServerAttached) ? ServerShims.EntityGetSpawnFlags(entity.Value) : 0;
    }

    // Engine.FireScriptedOutput WAS HERE, AND IS DELETED WITH ITS SLOT --
    // plan_entity.md step 2a. Its one caller was Output.Fire, which now posts
    // straight into the directory's queue; there is nothing for a managed
    // wrapper around a deleted slot to wrap.

    /// <summary>
    /// Declares the parallel hierarchy's shared blob size, before any class on
    /// it is described. Returns the payload word count every class on the
    /// hierarchy will carry, -1 if native refused the size, and 0 when native
    /// does not supply the call.
    ///
    /// <para>
    /// The number is one for the whole hierarchy rather than one per class,
    /// because every entity on that hierarchy is allocated the same blob
    /// length — operator new is a static function with no idea which class the
    /// factory is about to make. Native cannot work it out early enough: it
    /// learns the size from the class descriptions as they arrive, so the first
    /// class described would get fewer payload words than the last.
    /// </para>
    ///
    /// <para>
    /// It also cannot be derived from the classes a side HOSTS. A state may be
    /// declared before one half of its entity has been ported, so the two sides
    /// publish different sets — and two sides deriving the number from what
    /// they publish derive two numbers. This is computed from every declaration
    /// in the shared assembly, which both sides read identically.
    /// </para>
    /// </summary>
    public int NetHierarchyDeclare(int blobBytes)
        => (s_Initialized && (ServerAttached || ClientAttached))
            ? (ServerShimsOrNull is { } __att38 ? __att38.NetHierarchyDeclare(blobBytes) : ClientShims.NetHierarchyDeclare(blobBytes)) : 0;

    /// <summary>
    /// Declares worldspawn's blob size to the native shim, which builds its
    /// table around the number (plan_gamedll_after.md Part B2).
    ///
    /// <para>
    /// SEPARATE FROM <see cref="NetHierarchyDeclare"/> BECAUSE THE WORLD IS NOT
    /// ON THE HIERARCHY. That number sizes every entity on the parallel
    /// hierarchy and is therefore the max over all of them; the world's shim is
    /// a <c>CBaseEntity</c> that allocates its own blob against its own base, so
    /// its size is its own and folding it into that max would put three hundred
    /// bytes on a hundred-odd entities that have no use for them.
    /// </para>
    ///
    /// <para>
    /// Zero with no engine, which is the test harness: nothing is declared, and
    /// the caller reports that it published nothing rather than treating the
    /// absence of a game as a refusal.
    /// </para>
    /// </summary>
    public int WorldNetDeclare(int blobBytes)
        => (s_Initialized && (ServerAttached || ClientAttached))
            ? (ServerShimsOrNull is { } __att39 ? __att39.WorldNetDeclare(blobBytes) : ClientShims.WorldNetDeclare(blobBytes)) : 0;

    /// <summary>
    /// Publishes one managed type and returns the tier native assigned it, or
    /// -1. See NetPublishType in sourcesharp.h.
    ///
    /// <para>
    /// This is the whole of what replaces the field-by-field description. What
    /// travels is a name, a layout fingerprint and a size — native has no
    /// opinion about what a field is any more.
    /// </para>
    ///
    /// <para>
    /// <paramref name="nativeTypeId"/> is the id NATIVE hashed from the name.
    /// The caller compares it against <see cref="NetSchema.TypeIdOf"/>, which
    /// turns "two implementations of FNV-1a in two languages" from something a
    /// test corpus covers into something every name in the build covers. -1
    /// with an id of 0 is what an absent engine answers, which is the test
    /// harness rather than a failure.
    /// </para>
    /// </summary>
    public int NetPublishType(string className, int shell, uint schemaHash,
                                     int layoutBytes, out uint nativeTypeId)
    {
        nativeTypeId = 0;

        if (!s_Initialized || !(ServerAttached || ClientAttached))
            return -1;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* p = bytes)
        fixed (uint* pId = &nativeTypeId)
        {
            return (ServerShimsOrNull is { } __att40 ? __att40.NetPublishType(p, shell, schemaHash, layoutBytes, pId) : ClientShims.NetPublishType(p, shell, schemaHash, layoutBytes, pId));
        }
    }

    /// <summary>
    /// One tier as native registered it, or null past the end of the list. See
    /// NetTierInfo in sourcesharp.h — this is what <see cref="NetTiers"/> reads
    /// instead of projecting the boundary rule a second time.
    /// </summary>
    public NetTierInfo? NetTierAt(int shell, int tier)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached))
            return null;

        byte* table = null;
        int bytes = 0, words = 0, props = 0, live = 0;

        if ((ServerShimsOrNull is { } __att41 ? __att41.NetTierInfo(shell, tier, &table, &bytes, &words, &props, &live) : ClientShims.NetTierInfo(shell, tier, &table, &bytes, &words, &props, &live)) == 0)
            return null;

        return new NetTierInfo(tier, FromUtf8(table), bytes, words, props, live != 0);
    }

    //-------------------------------------------------------------------------
    // Asking the world what is there.
    //
    // The primitives behind the emitter entities. Everything else in the entity
    // API is about a subject — this entity's health, that entity's velocity. A
    // radius query has no subject: it is a question about the world, and the
    // entity that asked is only where the question was asked from.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="into"/> with everything within a radius and returns
    /// how many were written.
    ///
    /// A result equal to the buffer's length means the answer was TRUNCATED, not
    /// that it happened to fit. Callers that care should notice and say so
    /// rather than treating a full buffer as a complete answer.
    ///
    /// Unfiltered on purpose. Each emitter discards a different subset — a push
    /// field ignores what it cannot move, an explosion ignores what cannot be
    /// hurt — so what is worth acting on is decided here, not natively.
    /// </summary>
    public int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into)
    {
        if (!s_Initialized || !ServerAttached || into.IsEmpty) return 0;

        float* c = stackalloc float[3] { center.X, center.Y, center.Z };

        // Native writes RAW handles into the span's storage, and an
        // EntityHandle stores its raw handle inverted (Entity.cs): the elements
        // it wrote are decoded in place before anyone reads them.
        int written;
        fixed (uint* p = EntityHandle.RawView(into))
            written = ServerShims.EntitiesInSphere(c, radius, p, into.Length);

        written = Math.Clamp(written, 0, into.Length);
        EntityHandle.DecodeRawInPlace(into[..written]);
        return written;
    }

    /// <summary>What an entity is, in one crossing rather than five.</summary>
    public EntityState EntityGetState(EntityHandle entity, out MoveType moveType)
    {
        moveType = MoveType.None;

        if (!s_Initialized || !ServerAttached) return EntityState.None;

        int move;
        var state = (EntityState)ServerShims.EntityGetState(entity.Value, &move);
        moveType = (MoveType)move;
        return state;
    }

    /// <summary>Traces a line through the world.</summary>
    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask,
                                        EntityHandle ignore = default)
    {
        if (!s_Initialized || !ServerAttached)
            return new TraceResult(false, 1f, end, Vector.Zero, EntityHandle.None);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        uint hit;

        int stopped = ServerShims.TraceLine(s, e, (int)mask, Skippable(ignore),
                                    &fraction, endPos, normal, &hit);

        return new TraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            new EntityHandle(hit));
    }

    /// <summary>
    /// Traces a line through the world and names the SURFACE that stopped it.
    ///
    /// <para>
    /// A slot of its own rather than a wider <see cref="TraceLine"/>. That one
    /// is the hottest in this API — 1.573 crossings per server frame at rest —
    /// and <c>ClientTraceLine</c>'s comment in <c>sourcesharp.h</c> records this
    /// ABI refusing to widen it once already, when the client wanted a hitbox.
    /// </para>
    ///
    /// <para>
    /// WHO WANTS IT: a bullet. <c>Arsenal.FireBullets</c> dispatches an impact
    /// effect per pellet, and the client chooses the decal and the impact sound
    /// from the surfaceprop the SERVER sent — that is what stock's
    /// <c>CBaseEntity::ImpactTrace</c> writes into the effect data. A server
    /// that names none leaves a wooden crate taking a concrete decal and cement
    /// flecks.
    /// </para>
    ///
    /// <para>
    /// A trace that hit nothing reports <see cref="SurfaceTraceResult.SurfaceProps"/>
    /// as -1, which is the ABI's answer and not a softening of one: index 0 is
    /// the real <c>default</c> surface.
    /// </para>
    /// </summary>
    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask,
                                                      EntityHandle ignore = default)
    {
        if (!s_Initialized || !ServerAttached)
            return new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        uint hit;
        int surfaceProps;
        int surfaceFlags;

        int stopped = ServerShims.TraceLineSurface(s, e, (int)mask, Skippable(ignore),
                                           &fraction, endPos, normal, &hit,
                                           &surfaceProps, &surfaceFlags);

        return new SurfaceTraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            new EntityHandle(hit),
            surfaceProps,
            surfaceFlags);
    }

    /// <summary>
    /// Traces a line through the CLIENT's world.
    ///
    /// <para>
    /// A SLOT OF ITS OWN rather than a client arm of <see cref="TraceLine"/>,
    /// and the reasons are in that order of weight. The server's trace answers
    /// no <see cref="ClientTraceResult.Hitbox"/>, and hitbox is the only field
    /// the first caller reads — a projected decal branches on it and nothing
    /// else. This side answers from the client's own copy of the BSP, which is
    /// the copy that decides what the client draws. And what it hit comes back
    /// as an entity INDEX, never an <see cref="EntityHandle"/>: reading a
    /// handle back through <c>CBaseHandle::Get()</c> on this side is behaviour
    /// ledger row C8, which segfaulted a live client.
    /// </para>
    ///
    /// <para>
    /// NO IGNORE ENTITY. The native side traces with
    /// <c>CTraceFilterHitAll</c>, which is stock's filter at the only place on
    /// this side that traces at all. Gaining an ignore would mean
    /// <c>CTraceFilterSimple</c>, which also brings collision groups and
    /// gamerules with it — so a caller passing "nothing to ignore" would get a
    /// different trace than one that passed nothing at all.
    /// </para>
    /// </summary>
    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask)
        => ClientTraceLine(start, end, mask, TraceFilter.HitAll);

    /// <summary>
    /// The same trace, choosing which entities it may hit. See
    /// <see cref="TraceFilter"/> for why this is an enumeration and not an
    /// ignore-entity parameter: a simple filter is not hit-all with one entity
    /// removed, because it also applies collision groups and the game rules.
    /// </summary>
    public unsafe ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                                           TraceFilter filter,
                                                           int passEntityIndex = -1)
    {
        if (!s_Initialized || !ClientAttached)
            return new ClientTraceResult(false, 1f, end, Vector.Zero, 0, -1, -1, 0);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        int hitbox;
        int entIndex;
        int surfaceProps;
        int surfaceFlags;

        int stopped = ClientShims.ClientTraceLine(s, e, (int)mask, (int)filter, passEntityIndex,
                                          &fraction, endPos, normal, &hitbox, &entIndex,
                                          &surfaceProps, &surfaceFlags);

        return new ClientTraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            hitbox,
            entIndex,
            surfaceProps,
            surfaceFlags);
    }

    /// <summary>
    /// A swept BOX, where <see cref="TraceLine"/> sweeps a ray.
    ///
    /// <para>
    /// The question a ray cannot answer is "would this fit here", which is what
    /// anything about to place an object in the world has to ask first.
    /// </para>
    /// </summary>
    /// <param name="mins">
    /// Corner offsets RELATIVE to the start point, which is how the engine wants
    /// a hull and how a caller holding an entity's bounds already has them.
    /// </param>
    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs,
                                       TraceMask mask, EntityHandle ignore = default)
    {
        if (!s_Initialized || !ServerAttached)
            return new HullResult(false, false, EntityHandle.None);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        uint hit;
        int startSolid;

        int blocked = ServerShims.TraceHull(s, e, lo, hi, (int)mask, Skippable(ignore), &hit, &startSolid);

        return new HullResult(blocked != 0, startSolid != 0, new EntityHandle(hit));
    }

    /// <summary>
    /// Turns every entity of a name into a template, and answers where each one
    /// stood.
    ///
    /// <para>
    /// Only meaningful DURING the map parse, in the window between the entities
    /// being read and the entities being spawned — which is the window a
    /// point_template's own Spawn runs in, and the whole reason any part of
    /// templating is still native. Outside it there is nothing to claim, and
    /// this answers with nothing.
    /// </para>
    /// </summary>
    /// <param name="removeSources">
    /// Whether the entities turned into templates are consumed. A template
    /// source is not usually meant to remain in the world; a map that wants it
    /// to says so with a spawnflag, and that decision is the caller's.
    /// </param>
    /// <returns>
    /// One entry per source found, each carrying the index the template store
    /// gave it and the place it stood in the world.
    /// </returns>
    public List<TemplatePlacement> TemplateCapture(string sourceName, bool removeSources,
                                                          int limit = 64)
    {
        var found = new List<TemplatePlacement>();

        if (!s_Initialized || !ServerAttached || sourceName.Length == 0)
            return found;

        var nameBytes = Encoding.UTF8.GetBytes(sourceName + "\0");
        int* indices = stackalloc int[limit];
        float* origins = stackalloc float[limit * 3];
        float* angles = stackalloc float[limit * 3];

        int total;
        fixed (byte* pName = nameBytes)
        {
            total = ServerShims.TemplateCapture(pName, removeSources ? 1 : 0, indices, origins, angles, limit);
        }

        for (int i = 0; i < Math.Min(total, limit); i++)
        {
            found.Add(new TemplatePlacement(
                indices[i],
                new Vector(origins[i * 3], origins[i * 3 + 1], origins[i * 3 + 2]),
                new QAngle(angles[i * 3], angles[i * 3 + 1], angles[i * 3 + 2])));
        }

        if (total > limit)
        {
            Warning($"[SourceSharp] '{sourceName}' named {total} entities and only {limit} became " +
                     "templates; the rest are gone from the map and in no template\n");
        }

        return found;
    }

    // TemplateReconnectIO AND TemplateSchedulePrecache STOOD HERE, and they are
    // gone rather than moved (plan_server_all.md Wave 1, docs/plan-audit.md
    // D11). Both were "pure text manipulation on the stored map data … native
    // because the text is", and the text is not native any more: the store is
    // SourceSharp.Server's TemplateStore, so a template asking for its own group
    // to be rewritten is one managed object calling another and needs no slot at
    // all. Two crossings per point_template per map load, removed by moving the
    // thing they crossed in order to reach.

    /// <summary>
    /// Spawns one copy of each template entity, each at the place the caller
    /// worked out for it — from pairs that arrive PARSED.
    ///
    /// <para>
    /// Nothing here decides WHERE: every transform arrives resolved. And since
    /// plan_server_novalve.md P5 nothing here PARSES either: the caller read
    /// each stored block with <c>MapEntityBlock</c>, so what crosses is, per
    /// entity, a classname and its <c>key\0value\0…</c> pack — the payload
    /// <c>MapSpawnCreate</c> already uses. Native creates, keys, places and
    /// spawns. The text-parsing slot this replaced, <c>TemplateSpawnInstance</c>,
    /// is gone: nothing called it once P5 parsed in managed (the typed-call walk
    /// in ShimAreaTypedCallsTests enforces that).
    /// </para>
    /// </summary>
    /// <returns>
    /// The entities created. Empty when a class failed to create, which is
    /// stock's all-or-nothing answer.
    /// </returns>
    public List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64)
    {
        var made = new List<EntityHandle>();

        if (!s_Initialized || !ServerAttached || spawns.Count == 0)
            return made;

        int count = spawns.Count;
        float[] origins = new float[count * 3];
        float[] angles = new float[count * 3];
        int[] packBytes = new int[count];

        // ONE pinned buffer per stream rather than an array of pointers: an
        // array of managed byte[] cannot be pinned as a unit.
        var names = new List<byte>(count * 16);
        var packs = new List<byte>(count * 128);

        for (int i = 0; i < count; i++)
        {
            names.AddRange(System.Text.Encoding.UTF8.GetBytes(spawns[i].ClassName));
            names.Add(0);

            int before = packs.Count;
            foreach (var kv in spawns[i].Keys)
            {
                packs.AddRange(System.Text.Encoding.UTF8.GetBytes(kv.Key));
                packs.Add(0);
                packs.AddRange(System.Text.Encoding.UTF8.GetBytes(kv.Value));
                packs.Add(0);
            }
            packBytes[i] = packs.Count - before;

            origins[i * 3] = spawns[i].Origin.X;
            origins[i * 3 + 1] = spawns[i].Origin.Y;
            origins[i * 3 + 2] = spawns[i].Origin.Z;
            angles[i * 3] = spawns[i].Angles.Pitch;
            angles[i * 3 + 1] = spawns[i].Angles.Yaw;
            angles[i * 3 + 2] = spawns[i].Angles.Roll;
        }

        byte[] nameBytes = names.ToArray();
        byte[] packBuffer = packs.Count > 0 ? packs.ToArray() : new byte[1];

        uint* handles = stackalloc uint[limit];
        int total;

        fixed (byte* pNames = nameBytes)
        fixed (byte* pPacks = packBuffer)
        fixed (int* pBytes = packBytes)
        fixed (float* pOrigins = origins)
        fixed (float* pAngles = angles)
        {
            total = ServerShims.TemplateSpawnKeyed(pNames, pPacks, pBytes, pOrigins, pAngles, count,
                                            handles, limit);
        }

        for (int i = 0; i < Math.Min(total, limit); i++)
        {
            if (handles[i] != 0)
                made.Add(new EntityHandle(handles[i]));
        }

        if (total > limit)
        {
            Warning($"[SourceSharp] a template made {total} entities and only {limit} were handed " +
                     "back; the rest exist but nothing here can address them\n");
        }

        return made;
    }

    //-------------------------------------------------------------------------
    // THE VTABLE BINDING'S FIVE NATIVE SLOTS.
    //
    // One to acquire an interface, and four proxies for calls a fixed unmanaged
    // signature cannot spell at all. See docs/vtable-binding.md and
    // sourcesharp/game/shared/shell_vtable_interfaces.cpp.
    //
    // The engine methods themselves are called through the forwarder
    // libraries' generated tables (plan_engine_forwarders.md), which are
    // acquired by name through this same slot.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The interface pointer for a factory kind and a version string, and, on
    /// failure, native's reason.
    ///
    /// <para>
    /// Null for a string this side does
    /// not serve. That is not an error: <c>IVEngineClient</c> exists only on the
    /// client and <c>IVEngineServer</c> only on the server.
    /// </para>
    ///
    /// <para>
    /// <b>The signature is the ABI slot's, not a convenience over it</b> —
    /// <see cref="NativeInterfaceFactory.AcquireFn"/> is this method's shape, so
    /// the production factory is this method and a test's factory is a delegate
    /// the test wrote. Anything friendlier here would be a second contract for
    /// the fake to disagree with.
    /// </para>
    ///
    /// <para>
    /// When the slot is missing — before <c>LoadSide</c>, or in a test process
    /// with no native half — the reason is written here rather than left to a
    /// null return with an empty buffer, because "nothing was reported" and
    /// "the boundary is not up" are different failures and only one of them is
    /// worth reading a stack trace for.
    /// </para>
    /// </summary>
    public unsafe void* AcquireInterface(
        SourceSharpFactoryType type, byte* interfaceVersion,
        byte* error, int errorBytes)
    {
        // Each side's shim root answers (plan_engineapi_vtables C5 for the
        // server, plan_client_vtables CB for the client); there is no struct.
        if (Root is { } root && interfaceVersion != null)
            return root.AcquireInterface((int)type, interfaceVersion, error, errorBytes);

        SayNoBoundary(error, errorBytes);
        return null;
    }

    /// <summary>
    /// <c>IVEngineClient::Con_NPrintf</c>, which is varargs and so cannot be
    /// issued through a fixed indirect signature at all.
    ///
    /// <para>
    /// The native side passes <paramref name="text"/> as an ARGUMENT —
    /// <c>Con_NPrintf( pos, "%s", text )</c> — and never as the format string.
    /// Caller-controlled text in the format position reads the stack on any
    /// <c>%s</c> that happens to be in a model name or a chat line.
    /// </para>
    /// </summary>
    public unsafe void ConNPrintf(void* engineClient, int position, string text)
    {
        if (!s_Initialized || !ClientAttached || engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);

        fixed (byte* p = bytes)
            ClientShims.ConNPrintf(engineClient, position, p);
    }

    /// <summary>
    /// <c>IVEngineClient::Con_NXPrintf</c>, with <c>con_nprint_s</c> flattened
    /// into its scalars.
    ///
    /// <para>
    /// The struct's layout stays native's business. <c>con_nprint.h</c> says of
    /// it that the "structure cannot be changed by mods", and a C# copy would be
    /// a second declaration of it with nothing holding the two in step.
    /// </para>
    /// </summary>
    public unsafe void ConNXPrintf(
        void* engineClient, int index, float timeToLive,
        float red, float green, float blue, bool fixedWidthFont, string text)
    {
        if (!s_Initialized || !ClientAttached || engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);
        float* colour = stackalloc float[3] { red, green, blue };

        fixed (byte* p = bytes)
            ClientShims.ConNXPrintf(engineClient, index, timeToLive, colour,
                            Vtable.Bit(fixedWidthFont), p);
    }

    /// <summary>
    /// <c>IVEngineClient::GetLightForPoint</c>, which returns <c>Vector</c> BY
    /// VALUE — three floats in <c>xmm0</c> and <c>xmm1</c> under the SysV
    /// classification, which a <c>delegate* unmanaged&lt;...&gt;</c> has no
    /// spelling for. The floats come back through a pointer instead.
    /// </summary>
    /// <returns>False when nothing answered, in which case nothing is written.</returns>
    public unsafe bool TryGetLightForPoint(
        void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(fast: false, engineClient, position, clamp, out light);

    /// <summary>
    /// <c>IVEngineClient::GetLightForPointFast</c>, the other half of the pair
    /// above and refused for the same reason.
    /// </summary>
    public unsafe bool TryGetLightForPointFast(
        void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(fast: true, engineClient, position, clamp, out light);

    // plan_client_vtables CC7: the two slots are routed (Slot_/Has_), so the
    // choice between them is a flag rather than a struct field passed along.
    private unsafe bool TryLightForPoint(
        bool fast, void* engineClient, in Vector position, bool clamp, out Vector light)
    {
        light = default;

        if (!s_Initialized || engineClient == null || !(fast ? ClientAttached : ClientAttached))
            return false;

        float* input = stackalloc float[3] { position.X, position.Y, position.Z };
        float* answer = stackalloc float[3];

        // NOT WRITTEN ON A MISS, deliberately: native leaves the buffer alone
        // when it refuses, so "the light here is black" and "there was no
        // engine" stay different answers.
        answer[0] = float.NaN;

        if (fast)
            ClientShims.GetLightForPointFast(engineClient, input, Vtable.Bit(clamp), answer);
        else
            ClientShims.GetLightForPoint(engineClient, input, Vtable.Bit(clamp), answer);

        if (float.IsNaN(answer[0]))
            return false;

        light = new Vector(answer[0], answer[1], answer[2]);
        return true;
    }

    /// <summary>
    /// Sets an entity alight for a number of seconds. Zero burns until
    /// something puts it out.
    /// </summary>
    public void EntityIgnite(EntityHandle entity, float lifetime)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.EntityIgnite(entity.Value, lifetime);
    }

    /// <summary>
    /// Dissolves an entity.
    /// </summary>
    /// <returns>
    /// False when there was nothing to dissolve. The effect is a shader over a
    /// STUDIO MODEL, so a brush or a logical entity has nothing for it to act
    /// on — worth reporting rather than swallowing, because a map that pointed
    /// one at a func_wall gets silence from stock.
    /// </returns>
    /// <summary>
    /// Damage carrying the physics force and the point it acts at, as stock's
    /// <c>CalculateBulletDamageForce</c> fills a <c>CTakeDamageInfo</c>: the
    /// victim's <c>OnTakeDamage</c> pushes its physics object with them
    /// (plan_physics_managed A30). The attacker resolves on either hierarchy.
    /// </summary>
    public void ApplyDamageForce(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType,
                                 Vector force, Vector position)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* f = stackalloc float[3] { force.X, force.Y, force.Z };
        float* p = stackalloc float[3] { position.X, position.Y, position.Z };
        ServerShims.ApplyDamageForce(victim.Value, attacker.Value, damage, (int)damageType, f, p);
    }

    public bool EntityDissolve(EntityHandle entity, DissolveType type = DissolveType.Normal,
                                      float magnitude = 250f, Vector? from = null)
    {
        if (!s_Initialized || !ServerAttached) return false;

        if (from is not Vector origin)
            return ServerShims.EntityDissolve(entity.Value, (int)type, magnitude, null) != 0;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        return ServerShims.EntityDissolve(entity.Value, (int)type, magnitude, p) != 0;
    }

    /// <summary>
    /// A shower of sparks at a point.
    /// </summary>
    /// <param name="direction">
    /// Null throws them in every direction, which is what a spark with no
    /// facing means — not a direction of zero length.
    /// </param>
    public void SparkEffect(Vector origin, int magnitude, int trailLength,
                                   bool makeSound = true, Vector? direction = null)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        if (direction is not Vector dir)
        {
            ServerShims.SparkEffect(o, magnitude, trailLength, makeSound ? 1 : 0, null);
            return;
        }

        float* d = stackalloc float[3] { dir.X, dir.Y, dir.Z };
        ServerShims.SparkEffect(o, magnitude, trailLength, makeSound ? 1 : 0, d);
    }

    /// <summary>
    /// Which frame of an animated material a brush is showing.
    ///
    /// <para>
    /// The frames belong to the material; which one is up belongs to the map.
    /// </para>
    /// </summary>
    public int EntityGetTextureFrame(EntityHandle entity)
        => (s_Initialized && ServerAttached)
            ? ServerShims.EntityGetTextureFrame(entity.Value) : 0;

    public void EntitySetTextureFrame(EntityHandle entity, int frame)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.EntitySetTextureFrame(entity.Value, frame);
    }

    /// <summary>
    /// Zooms a player's view to a field of view over <paramref name="rate"/>
    /// seconds.
    /// </summary>
    /// <param name="requester">
    /// Who is asking. The engine tracks the owner so two entities zooming the
    /// same player do not fight, and releasing must come from the same one.
    /// </param>
    /// <param name="fov">
    /// Zero RELEASES rather than setting a field of view of zero — it is how a
    /// requester says it has finished and the player returns to their own
    /// setting.
    /// </param>
    public void PlayerSetFov(EntityHandle player, EntityHandle requester, int fov, float rate = 0f)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.PlayerSetFov(player.Value, requester.Value, fov, rate);
    }

    /// <summary>
    /// Which skin of a model is drawn. Silently does nothing for an entity with
    /// no animating model, which is what stock does too.
    /// </summary>
    public void EntitySetSkin(EntityHandle entity, int skin)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;
        if (ServerShimsOrNull is { } __att42) __att42.EntitySetSkin(entity.Value, skin);
        else ClientShims.EntitySetSkin(entity.Value, skin);
    }

    /// <summary>
    /// Throws one piece of debris.
    ///
    /// <para>
    /// The loop around this — how many, how often, how widely scattered — is the
    /// caller's, and is the whole of what a shooter entity decides. What a gib
    /// IS stays native: a model, a physics object and a fade-out.
    /// </para>
    /// </summary>
    /// <returns>
    /// False when nothing was made, which is not always an error: gore can be
    /// switched off by a convar, and a model can fail to load.
    /// </returns>
    public bool ShootGib(string model, in GibShot shot)
    {
        if (!s_Initialized || !ServerAttached || model.Length == 0) return false;

        var modelBytes = Encoding.UTF8.GetBytes(model + "\0");

        fixed (byte* pModel = modelBytes)
        fixed (GibShot* pShot = &shot)
        {
            return ServerShims.ShootGib(pModel, pShot) != 0;
        }
    }


    /// <summary>
    /// Precaches a model or sprite and answers its INDEX.
    ///
    /// <para>
    /// The index is the useful half: both sides of the wire agree on model
    /// indices, so an entity that networks what it is drawn as sends a number
    /// rather than a path.
    /// </para>
    /// </summary>
    /// <returns>-1 if the model would not load. Do not network that — it is a
    /// lookup that fails every frame on the far side.</returns>
    public int PrecacheModel(string model)
    {
        if (!s_Initialized || !ServerAttached || model.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(model + "\0");
        fixed (byte* p = bytes) { return ServerShims.PrecacheModel(p); }
    }

    /// <summary>
    /// Makes, or updates, the sprite trail's renderable for a managed entity:
    /// the render mode (refused outside RenderMode_t, and then nothing is
    /// made) and whether it is switched on.
    ///
    /// <para>
    /// The ring itself, and every number it is drawn with, is managed
    /// (ClientSpriteTrails, lane c2-fx): the renderable ticks and draws it by
    /// handle through IManagedFxClient.
    /// </para>
    ///
    /// <para>
    /// The position is not passed. Native sweeps it in PreRender, because a
    /// trail is by definition something that FOLLOWS and a managed entity that
    /// only moved delivers no data change at all.
    /// </para>
    /// </summary>
    public void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.SpriteTrailUpdate(owner.Value, renderMode, visible ? 1 : 0);
    }

    /// <summary>The trail goes when its managed entity does.</summary>
    public void SpriteTrailDestroy(EntityHandle owner)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.SpriteTrailDestroy(owner.Value);
    }

    /// <summary>
    /// Creates the FLAME — the particle effect and glow a fire is drawn as.
    ///
    /// <para>
    /// What burns, how hot, for how long, what it sets alight and what it
    /// spreads to is decided in C#. This is only the thing you can see.
    /// </para>
    /// </summary>
    /// <param name="flags">
    /// The mapper's smoke/glow/visible-from-above choices, passed through as the
    /// spawnflags they came from rather than re-derived.
    /// </param>
    public EntityHandle FireEffectCreate(int type, Vector origin, float scale, int flags)
    {
        if (!s_Initialized || !ServerAttached) return EntityHandle.None;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        return new EntityHandle(ServerShims.FireEffectCreate(type, p, scale, flags));
    }

    /// <summary>
    /// How big the flame is drawn, as a fraction of full size, eased over
    /// <paramref name="time"/> seconds — a fire visibly taking hold or dying.
    /// </summary>
    public void FireEffectScale(EntityHandle effect, float scale, float time)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.FireEffectScale(effect.Value, scale, time);
    }

    /// <summary>
    /// Burning or not. Separate from creation because a fire that has gone out
    /// still exists and can be lit again.
    /// </summary>
    public void FireEffectEnable(EntityHandle effect, bool enable)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.FireEffectEnable(effect.Value, enable ? 1 : 0);
    }

    /// <summary>
    /// Declares that managed code draws this entity itself, and the box it
    /// occupies while doing it. Client-side.
    ///
    /// <para>
    /// Pushed rather than asked for: the bounds change only when the thing being
    /// drawn changes, while the engine asks for them every frame.
    /// </para>
    /// </summary>
    public void StandaloneSetCustomDraw(EntityHandle entity, Vector mins, Vector maxs)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        ClientShims.StandaloneSetCustomDraw(entity.Value, lo, hi);
    }

    /// <summary>
    /// How an entity that draws ITSELF is sorted, blended and depth-tested.
    ///
    /// <para>
    /// <see cref="StandaloneSetCustomDraw"/> says WHERE such an entity draws;
    /// this says HOW. Three answers a stock <c>C_BaseEntity</c> derives from its
    /// render mode and the parallel hierarchy has no member to derive:
    /// which of the renderer's two lists it belongs in, whether it is drawn
    /// depth-tested, and its constant alpha.
    /// </para>
    ///
    /// <para>
    /// <paramref name="translucent"/> is not cosmetic. A renderable in the
    /// opaque list is drawn by <c>DrawOpaqueRenderable</c>, which never sets a
    /// blend — an additive sprite left there is drawn at full strength and never
    /// reaches the depth split <paramref name="ignoreZ"/> selects.
    /// </para>
    /// </summary>
    public void StandaloneSetBlending(EntityHandle entity, bool translucent,
                                             bool ignoreZ, int alpha)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetBlending(entity.Value, translucent ? 1 : 0, ignoreZ ? 1 : 0, alpha);
    }

    /// <summary>
    /// Overrules the model about whether this entity casts a shadow.
    /// Client-side.
    ///
    /// <para>
    /// <see cref="ShadowCastChoice.Auto"/> is the stock rule and is right for
    /// nearly everything: a studio model casts, a brush model and a sprite do
    /// not. The two overrides are for the cases stock also has one for —
    /// EF_NOSHADOW and prop_dynamic's <c>disableshadows</c> are both
    /// <see cref="ShadowCastChoice.Never"/>, and
    /// <see cref="ShadowCastChoice.Simple"/> is the only way an entity that
    /// draws ITSELF can cast anything, since it has no studio header to be
    /// judged by.
    /// </para>
    ///
    /// <para>
    /// Neither override can make an entity cast that is not being drawn, nor
    /// make the viewmodel cast. Both are refused in
    /// <c>shadow_cast_policy.h</c> before the choice is read, and for the same
    /// reason: a shadow under something the viewer cannot see has no object to
    /// explain it.
    /// </para>
    /// </summary>
    public void StandaloneSetShadowCast(EntityHandle entity, ShadowCastChoice choice)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetShadowCast(entity.Value, (int)choice);
    }

    /// <summary>
    /// What this entity is casting right now, what it was asked to cast, and
    /// whether the client shadow manager is holding a handle for it at all.
    /// Client-side.
    ///
    /// <para>
    /// The third fact is the one Phase 5's gate needed and could not get. The
    /// shell's <c>ShadowCastType()</c> answered SHADOWS_SIMPLE for every drawn
    /// entity while <c>GetShadowHandle()</c> was hard-coded to invalid, so the
    /// manager held nothing and projected nothing — the entity reported that it
    /// cast a shadow and cast none, and the only thing in the game that could
    /// tell the difference was a photograph of the floor.
    /// </para>
    /// </summary>
    public unsafe ShadowCast StandaloneShadowCast(EntityHandle entity,
                                                         out ShadowCastChoice asked,
                                                         out bool hasShadow)
    {
        asked = ShadowCastChoice.Auto;
        hasShadow = false;

        if (!s_Initialized || !ClientAttached) return ShadowCast.Unknown;

        int choice = 0;
        int held = 0;
        int cast = ClientShims.StandaloneShadowCast(entity.Value, &choice, &held);

        asked = (ShadowCastChoice)choice;
        hasShadow = held != 0;

        return (ShadowCast)cast;
    }

    /// <summary>
    /// One sprite, this frame only. Answers 1 if anything was drawn.
    ///
    /// <para>
    /// The whole of what a sprite needs from native code: bind the material,
    /// work out three axes from the .vmt's own orientation, build one quad. Every
    /// value it takes is a networked field the managed half already holds
    /// decoded, which is why there is no client-side sprite OBJECT behind this
    /// the way there was a steam jet behind the old SteamJetUpdate slot.
    /// </para>
    ///
    /// <para>
    /// WHERE it is and WHICH WAY IT FACES are not arguments. Native reads both
    /// off the entity, and has to: what it holds is the INTERPOLATED place for
    /// the frame being drawn, and this side could not supply one anyway —
    /// <see cref="EntityGetOrigin"/> is a server-side slot and answers a silent
    /// zero vector on the client.
    /// </para>
    /// </summary>
    public int DrawSprite(EntityHandle owner, int modelIndex,
                                 float frame, int renderMode, int renderFx,
                                 int r, int g, int b, int a,
                                 float scale, bool worldSpaceScale,
                                 float hdrColorScale, float glowProxySize)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.DrawSprite(owner.Value, modelIndex, frame, renderMode, renderFx,
                              r, g, b, a, scale, worldSpaceScale ? 1 : 0,
                              hdrColorScale, glowProxySize);
    }

    /// <summary>
    /// One sprite, this frame only, a little away from its owner's origin.
    ///
    /// <para>
    /// <see cref="DrawSprite"/> with one argument added, and the argument is
    /// the whole reason it exists: a plasma flame is SEVEN sprites belonging to
    /// one entity — six in a ring whose radius follows the flame's scale, and a
    /// glow beneath them that rises as it grows. Stock gave each of those seven
    /// its own <c>C_Sprite</c> held as a member; a managed entity can hold none,
    /// and would not want seven client entities per fire to draw seven quads
    /// that depend on nothing but this frame's scale.
    /// </para>
    ///
    /// <para>
    /// The offset is in WORLD axes rather than the owner's, which is stock's
    /// choice too: the ring is laid out around the vertical and every flame
    /// points up, whatever the entity's angles say.
    /// </para>
    /// </summary>
    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex,
                                       float frame, int renderMode, int renderFx,
                                       int r, int g, int b, int a,
                                       float scale, bool worldSpaceScale,
                                       float hdrColorScale, float glowProxySize)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        float* o = stackalloc float[3] { offset.X, offset.Y, offset.Z };

        return ClientShims.DrawSpriteOffset(owner.Value, o, modelIndex, frame, renderMode, renderFx,
                                    r, g, b, a, scale, worldSpaceScale ? 1 : 0,
                                    hdrColorScale, glowProxySize);
    }

    /// <summary>
    /// Starts a named particle system on a managed entity, and keeps it at that
    /// entity's position for as long as it runs.
    ///
    /// <para>
    /// The counterpart of <see cref="SpriteTrailUpdate"/> rather than of
    /// <see cref="DrawSprite"/>: a particle system is a simulation that runs
    /// between frames, so something native has to own it. What differs from
    /// stock is that a managed entity cannot host one —
    /// <c>CParticleProperty</c> hangs off <c>CBaseEntity</c> and the parallel
    /// hierarchy has no such thing — so the effect is created ownerless and
    /// driven from the entity's origin by a per-frame sweep.
    /// </para>
    /// </summary>
    /// <returns>true if a particle system by that name was found and started.</returns>
    public bool ParticleSystemStart(EntityHandle owner, string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0) return false;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return ClientShims.ParticleSystemStart(owner.Value, p) != 0; }
    }

    /// <summary>
    /// Stops the particle system belonging to a managed entity.
    ///
    /// The particles already in the air finish rather than blinking away, which
    /// is a second or two of fire burning out and is what stock's
    /// <c>~C_FireSmoke</c> asks for.
    /// </summary>
    public void ParticleSystemStop(EntityHandle owner)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ParticleSystemStop(owner.Value);
    }

    /// <summary>
    /// Which of a running particle system's control points are kept at the
    /// entity that owns it, as a bitmask — bit 0 is control point 0.
    ///
    /// <para>
    /// <see cref="ParticleSystemStart"/> feeds control point 0 and nothing
    /// else, and for every system this build had started before the flame that
    /// was the whole truth: all eight <c>env_fire_*</c> systems reference
    /// control point 0 alone. <c>burning_character</c> reads control point 1 as
    /// well, through a "Position on Model Random" that falls back to the
    /// control point's POSITION when it has no model — and an unfed control
    /// point is <c>(0, 0, 0)</c>, so the whole flame emits at the centre of the
    /// map rather than at what is burning.
    /// </para>
    /// </summary>
    /// <returns>true if the entity had a running system to configure.</returns>
    public bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.ParticleSystemFollowPoints(owner.Value, pointMask) != 0;
    }

    /// <summary>
    /// The collision box as offsets from the entity's origin — what a hull trace
    /// wants.
    ///
    /// <para>
    /// Not interchangeable with <see cref="EntityGetBoundsSize"/>: a size can
    /// only be centred on the origin, and most prop models put their origin on
    /// the BASE. Halving a size therefore builds a hull that sinks through the
    /// floor, and a room check made with it is blocked everywhere.
    /// </para>
    /// </summary>
    public (Vector Mins, Vector Maxs) EntityGetBounds(EntityHandle entity)
    {
        // A POINT -- step 2e. SOLID_NONE with no model is what the kind table
        // installed for Logical, so the shell's own bounds were zero too.
        if (ShellLess.Claims(entity))
            return (Vector.Zero, Vector.Zero);

        if (!s_Initialized || !ServerAttached)
            return (Vector.Zero, Vector.Zero);

        float* lo = stackalloc float[3];
        float* hi = stackalloc float[3];

        ServerShims.EntityGetBounds(entity.Value, lo, hi);

        return (new Vector(lo[0], lo[1], lo[2]), new Vector(hi[0], hi[1], hi[2]));
    }

    //-------------------------------------------------------------------------
    // THE SIX MISSING DIRECTIONS, for the native half of a savegame.
    //
    // Each is the pair of a slot that already existed, and each exists because
    // SavedEntity's native store carries only fields the managed side can both
    // READ and WRITE -- which is what makes the field list and its apply order
    // testable against the engine double rather than only in a game. See
    // NativeField in SavedEntity.cs.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Sets the collision box, as offsets from the origin — the pair to
    /// <see cref="EntityGetBounds"/>.
    ///
    /// <para>
    /// The pair to THAT rather than to <see cref="EntityGetBoundsSize"/>, and
    /// the difference is not cosmetic: a size can only be centred on the
    /// origin, and most prop models put their origin on the base. A box
    /// rebuilt from a size sinks half through the floor.
    /// </para>
    /// </summary>
    public void EntitySetBounds(EntityHandle entity, Vector mins, Vector maxs)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        ServerShims.EntitySetBounds(entity.Value, lo, hi);
    }

    /// <summary>
    /// What SHAPE this entity presents to a trace — the half of solidity
    /// <see cref="EntityGetSolidFlags"/> qualifies but could not name.
    ///
    /// <para>
    /// <see cref="SolidType.None"/> where there is no engine or the handle
    /// resolves to nothing, which is the same zero every other entity getter
    /// answers.
    /// </para>
    /// </summary>
    public SolidType EntityGetSolidType(EntityHandle entity)
        => (s_Initialized && ServerAttached)
               ? (SolidType)ServerShims.EntityGetSolidType(entity.Value)
               : SolidType.None;

    /// <summary>
    /// Sets both halves of solidity, in ONE call.
    ///
    /// <para>
    /// One call and not two, because the two are one decision:
    /// <c>CStandaloneEntity::SetSolidity</c> writes the pair and then runs
    /// <c>UpdatePartition</c> once, which CREATES or DESTROYS the entity's
    /// spatial-partition handle from both values together. Set in pieces, an
    /// entity is briefly listed for a shape it does not have — the same reason
    /// <see cref="EntityTeleport"/> is one call rather than an origin and an
    /// angle.
    /// </para>
    /// </summary>
    public void EntitySetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetSolidity(entity.Value, (int)solidType, (int)solidFlags);
    }

    /// <summary>
    /// Sets the spawnflags — the pair to <see cref="EntityGetSpawnFlags"/>.
    ///
    /// <para>
    /// Replaces them wholesale rather than adding or removing bits, because
    /// what wants it is a restore putting back a value it read, and a
    /// read-modify-write against whatever the map lump just set would not be
    /// that value.
    /// </para>
    /// </summary>
    public void EntitySetSpawnFlags(EntityHandle entity, int spawnFlags)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetSpawnFlags(entity.Value, spawnFlags);
    }

    /// <summary>
    /// This entity's gravity scale — the pair to
    /// <see cref="EntitySetGravity"/>. A multiple of the world's, so 1 is no
    /// change.
    /// </summary>
    public float EntityGetGravity(EntityHandle entity)
        => (s_Initialized && ServerAttached)
               ? ServerShims.EntityGetGravity(entity.Value)
               : 0f;

    /// <summary>
    /// Sends <paramref name="payload"/> to the server this client is connected
    /// to, as a SourceSharp net message under <paramref name="key"/>
    /// (ss_client_message.h, reliable). Client-side. False when there is no
    /// channel, or the key (1-63 bytes) or payload (up to 4 KiB) does not fit.
    /// The server hands it to <c>IClientLifecycle.OnClientMessage</c>.
    /// </summary>
    public bool SendServerMessage(string key, ReadOnlySpan<byte> payload)
    {
        if (!s_Initialized || !ClientAttached || key.Length == 0) return false;

        var keyBytes = Encoding.UTF8.GetBytes(key + "\0");

        fixed (byte* pKey = keyBytes)
        fixed (byte* pData = payload)
        {
            return ClientShims.SendServerMessage(pKey, pData, payload.Length) != 0;
        }
    }

    /// <summary>
    /// Applies force through the centre of mass, so it moves without spinning.
    /// The counterpart to <see cref="EntityApplyForceOffset"/>.
    /// </summary>
    public void EntityApplyForceCenter(EntityHandle entity, Vector force)
    {
        if (!s_Initialized || !ServerAttached) return;

        Motion.ApplyForceCenter(entity, force);
    }

    /// <summary>
    /// Adds to base velocity — the engine's "carried along by something"
    /// channel, and the only way to shove a player that the movement code will
    /// not undo on the next tick.
    /// </summary>
    public void EntityAddBaseVelocity(EntityHandle entity, Vector velocity)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* v = stackalloc float[3] { velocity.X, velocity.Y, velocity.Z };
        ServerShims.EntityAddBaseVelocity(entity.Value, v);
    }

    /// <summary>
    /// Damage with a blast force behind it.
    ///
    /// <paramref name="direction"/> is a direction, not a force: the magnitude
    /// comes from the engine's own explosive-force calculation, which clamps it
    /// and jitters it per victim so a crowd caught in one blast does not fly
    /// apart in formation. Null applies no force at all.
    ///
    /// <paramref name="physicsOnly"/> takes the push without the health loss,
    /// which is what an explosion set to do no damage means.
    /// </summary>
    public void EntityTakeDamage(EntityHandle entity, EntityHandle attacker,
                                        float damage, DamageType damageType,
                                        Vector? direction = null, Vector forceOrigin = default,
                                        bool physicsOnly = false)
    {
        if (!s_Initialized || !ServerAttached) return;

        if (direction is not Vector dir)
        {
            ServerShims.EntityTakeDamage(entity.Value, attacker.Value, damage, (int)damageType,
                                 null, null, physicsOnly ? 1 : 0);
            return;
        }

        float* d = stackalloc float[3] { dir.X, dir.Y, dir.Z };
        float* o = stackalloc float[3] { forceOrigin.X, forceOrigin.Y, forceOrigin.Z };

        ServerShims.EntityTakeDamage(entity.Value, attacker.Value, damage, (int)damageType,
                             d, o, physicsOnly ? 1 : 0);
    }

    /// <summary>
    /// Makes an entity physically simulated.
    ///
    /// Needs a model to read collision data out of, which is the same ordering
    /// trap as prop data: a brush has one during OnSpawn, a prop does not until
    /// the tick after, because SetModel from C# only records a pending name.
    /// Returns whether the entity is simulated afterwards — including when it
    /// already was, so calling twice is safe.
    /// </summary>
    public bool EntityInitPhysics(EntityHandle entity, bool asleep = false, bool debris = false)
        => s_Initialized && ServerAttached
           && PhysicsInit?.Create(entity, isStatic: false, asleep, debris) == true;

    /// <summary>
    /// The server's managed physics init (EntityPhysicsCreation), which
    /// <see cref="EntityInitPhysics"/> and <see cref="EntitySetPhysicsStatic"/>
    /// reach. Set by the server composition once vphysics answers; null makes
    /// nothing. INTERNAL, like <see cref="Teleported"/>.
    /// </summary>
    internal IEntityPhysicsRoute? PhysicsInit { get; set; }

    /// <summary>
    /// Pushes one block of the client's world render state into the subsystem
    /// that reads it.
    ///
    /// <para>
    /// The three callers are the client halves of <c>shadow_control</c>,
    /// <c>water_lod_control</c> and <c>env_tonemap_controller</c>, and each
    /// calls this and nothing else: everything about what the numbers ARE is
    /// decided on the server, networked, and decoded before this is reached.
    /// See ClientSetWorldRenderState in sourcesharp.h for the block each
    /// selector expects.
    /// </para>
    ///
    /// <para>
    /// Client only. On the server the slot is null and this does nothing, which
    /// is the same degrade every other one-sided call here takes.
    /// </para>
    /// </summary>
    public void ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (float* p = values)
        {
            ClientShims.ClientSetWorldRenderState((int)which, p, values.Length);
        }
    }

    /// <summary>
    /// Where the local player is standing, or null when there is not one yet —
    /// which is every frame between connecting and spawning, and is why this
    /// answers nullable rather than a zero vector that reads as the map origin.
    /// </summary>
    public Vector? LocalPlayerOrigin
    {
        get
        {
            if (!s_Initialized || !ClientAttached)
                return null;

            float* xyz = stackalloc float[3];

            return ClientShims.GetLocalPlayerOrigin(xyz) == 0
                ? null
                : new Vector(xyz[0], xyz[1], xyz[2]);
        }
    }

    /// <summary>
    /// Loads a colour-correction lookup and answers a handle, or 0 if the file
    /// would not load.
    /// </summary>
    public int ColorCorrectionAdd(string lookupFile)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(lookupFile + "\0");
        fixed (byte* p = bytes)
        {
            return ClientShims.ClientColorCorrection((int)ColorCorrectionOp.Add, 0, p, 0f);
        }
    }

    /// <summary>
    /// How much of a loaded lookup to apply, 0..1. Pushed every frame while the
    /// entity lives, because the weight is a function of where the player is
    /// standing and that changes on its own.
    /// </summary>
    public void ColorCorrectionSetWeight(int handle, float weight)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ClientColorCorrection((int)ColorCorrectionOp.SetWeight, handle, null, weight);
    }

    /// <summary>
    /// Releases a lookup. Not optional: the manager holds it until told, and a
    /// colour correction whose entity is gone would tint the map forever.
    /// </summary>
    public void ColorCorrectionRemove(int handle)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ClientColorCorrection((int)ColorCorrectionOp.Remove, handle, null, 0f);
    }

    /// <summary>
    /// Sets a convar the ENGINE owns, by name.
    ///
    /// <para>
    /// The partner of <see cref="FindConVarValue"/>, and deliberately not the
    /// same call as <see cref="SetConVarValue"/>: that one addresses a convar
    /// this mod registered and holds an id for, and a renderer convar declared
    /// in native code has no id here to hold.
    /// </para>
    ///
    /// <para>
    /// Goes through ConVarRef rather than the console, which is what makes it
    /// usable at all — a cheat-flagged convar refuses a console change without
    /// sv_cheats, while every stock entity that writes one writes it from code
    /// where no such check applies.
    /// </para>
    ///
    /// <para>
    /// A name that resolves to nothing is ignored. That is the ordinary case
    /// for a client convar named from the server, not an error.
    /// </para>
    /// </summary>
    public void SetNamedConVarValue(string name, string value)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        var nameBytes  = Encoding.UTF8.GetBytes(name + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* n = nameBytes)
        fixed (byte* v = valueBytes)
        {
            if (ServerShimsOrNull is { } __att43) __att43.SetNamedConVarValue(n, v);
            else ClientShims.SetNamedConVarValue(n, v);
        }
    }

    //-------------------------------------------------------------------------
    // The small effects.
    //
    // Each is a temp entity written straight to the wire with no server-side
    // object behind it, which is why none of them answers anything: there is
    // nothing to hold on to afterwards. What the calling entity decides — when,
    // how much, which way, and from which of several positions — never appears
    // here.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Blood at a point, thrown along a direction.
    /// </summary>
    /// <param name="stream">
    /// The arterial spurt rather than the drips. A different injury rather than
    /// more of the same one, which is why it is a choice and not an amount.
    /// </param>
    /// <param name="sprayFlags">
    /// Source's newer cloud/drops/gore effects, as <see cref="BloodSpray"/>
    /// bits. <see cref="BloodSpray.None"/> is what a map predating them gets.
    /// </param>
    public void BloodEffect(Vector origin, Vector direction, BloodColor color,
                                   float amount, bool stream = false,
                                   BloodSpray sprayFlags = BloodSpray.None)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* d = stackalloc float[3] { direction.X, direction.Y, direction.Z };

        ServerShims.BloodEffect(o, d, (int)color, amount, stream ? 1 : 0, (int)sprayFlags);
    }

    /// <summary>
    /// The mark blood leaves on whatever the line hits, if it hits anything.
    ///
    /// <para>
    /// The trace runs natively rather than through <see cref="TraceLine"/> and
    /// then a decal call, because the engine's decal placement wants the whole
    /// trace — the surface, its plane and its texture — and
    /// <see cref="TraceResult"/> carries none of that.
    /// </para>
    /// </summary>
    /// <returns>Whether a decal was placed.</returns>
    public bool BloodDecal(Vector start, Vector end, BloodColor color)
    {
        if (!s_Initialized || !ServerAttached) return false;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        return ServerShims.BloodDecal(s, e, (int)color) != 0;
    }

    /// <summary>
    /// Claims a decal material during level load. Answers the index the engine
    /// will know it by, or -1 when the material would not load — which is a
    /// mapper's typo and the caller's to report, since the caller is the one
    /// that knows which entity wrote the key.
    /// </summary>
    public int PrecacheDecal(string name)
    {
        if (!s_Initialized || !ServerAttached) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            return ServerShims.PrecacheDecal(p);
        }
    }

    /// <summary>
    /// Paints one map decal. See PaintDecal in sourcesharp.h.
    ///
    /// <para>
    /// <paramref name="angles"/> picks the FORM: null traces a short line
    /// through <paramref name="origin"/> and stamps whatever it found, which is
    /// <c>infodecal</c>; a value throws the mark along that facing for
    /// <paramref name="projectDistance"/> units, which is
    /// <c>info_projecteddecal</c>.
    /// </para>
    ///
    /// <para>
    /// <paramref name="permanent"/> picks the DELIVERY, and it is the whole
    /// difference between a decal a map was built with and one it sprayed
    /// later: permanent goes into the signon data and reaches a client that
    /// joins an hour afterwards, temporary goes out once to whoever is
    /// connected now.
    /// </para>
    /// </summary>
    public bool PaintDecal(int texture, Vector origin, Vector? angles,
                                  float projectDistance, bool permanent, bool lowPriority)
    {
        if (!s_Initialized || !ServerAttached) return false;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[3];

        if (angles.HasValue)
        {
            a[0] = angles.Value.X;
            a[1] = angles.Value.Y;
            a[2] = angles.Value.Z;
        }

        return ServerShims.PaintDecal(texture, o, angles.HasValue ? a : null, projectDistance,
                              permanent ? 1 : 0, lowPriority ? 1 : 0) != 0;
    }

    /// <summary>
    /// A line of text drawn in the world at a position, through the engine's
    /// debug-overlay channel.
    ///
    /// <para>
    /// <paramref name="line"/> offsets it downward, so several calls at one
    /// position stack rather than overprint. <paramref name="duration"/> is a
    /// minimum in seconds — whatever is drawing has to keep drawing.
    /// </para>
    /// </summary>
    public void DrawWorldText(Vector origin, int line, string text, float duration,
                                     int r = 255, int g = 255, int b = 255, int a = 255)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        var bytes = Encoding.UTF8.GetBytes(text + "\0");

        fixed (byte* p = bytes)
        {
            ServerShims.DrawWorldText(o, line, p, duration, r, g, b, a);
        }
    }

    /// <summary>
    /// One line in the engine's debug overlay, between two world points.
    ///
    /// <para>
    /// <paramref name="duration"/> of 0 or less puts the line in the
    /// twenty-slot POOL — stock's <c>UTIL_AddDebugLine</c> lifetime, redrawn
    /// every frame until it is reused or <c>clear_debug_overlays</c> empties
    /// it. A positive duration is the engine's own timed overlay. Zero is not
    /// "forever" to the engine, it is one frame.
    /// </para>
    ///
    /// <para>
    /// The colour is the caller's: stock traced <c>MASK_BLOCKLOS</c> to pick
    /// white-or-red and that trace lives on this side now — see
    /// <see cref="TraceLine"/> with <see cref="TraceMask.BlockLos"/>.
    /// </para>
    ///
    /// <para>Server only. Does nothing on the client, where the slot is null.</para>
    /// </summary>
    public void AddLineOverlay(Vector start, Vector end, int r, int g, int b,
                                      bool noDepthTest = true, float duration = 0f)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        ServerShims.AddLineOverlay(s, e, r, g, b, noDepthTest ? 1 : 0, duration);
    }

    /// <summary>
    /// An entity's debug overlay, drawn FOR A HANDLE — <c>plan_entity.md</c>
    /// step 4c. Its collision box, and a line of text at its origin.
    ///
    /// <para>
    /// <b>A HANDLE AND NOT AN INDEX.</b> An index alone addresses whichever
    /// entity now occupies a reused slot; the handle carries the serial and
    /// native revalidates it, so a stale one draws nothing rather than drawing
    /// on a stranger.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="EntityHandle.None"/> IS THE EMPTY HANDLE</b> (and
    /// <c>default</c> is None since 2026-10-05). The raw handle 0 is entry 0
    /// serial 0, a REAL SLOT that can be worldspawn. This method refuses
    /// <c>None</c> and nothing else, because drawing on the world is a
    /// legitimate ask; the way to catch a wrong handle is to read the return
    /// value, which is what it is for.
    /// </para>
    ///
    /// <para>
    /// A null or empty <paramref name="text"/> makes the entity name itself:
    /// its targetname if it has one, else its classname.
    /// </para>
    /// </summary>
    /// <returns>True when something was drawn.</returns>
    public bool DrawEntityOverlay(EntityHandle entity, int line, string? text,
                                         float duration,
                                         int r = 255, int g = 255, int b = 255, int a = 255)
    {
        if (!s_Initialized || !ServerAttached) return false;
        if (entity == EntityHandle.None) return false;

        var bytes = Encoding.UTF8.GetBytes((text ?? string.Empty) + "\0");

        fixed (byte* p = bytes)
        {
            return ServerShims.DrawEntityOverlay(entity.Value, line, p, duration, r, g, b, a) != 0;
        }
    }

    /// <summary>
    /// The overlay readout — <c>plan_entity.md</c> step 4c. What the native
    /// overlay library has done since the last reset, and whether a viewer is
    /// established right now.
    ///
    /// <para>
    /// <b>WHY THE ZEROS ARE NOT ALL THE SAME ZERO.</b> Six of the native
    /// overlay entries were <c>return;</c> before 4c, and a build in that state
    /// is invisible to any assertion of the form "the call did not crash".
    /// <c>Drawn</c> is the work; <c>NoViewer</c>, <c>Clipped</c> and
    /// <c>NoOverlay</c> are the three honest reasons it could be zero. All four
    /// zero with <c>ViewerSet</c> false is "nobody asked"; <c>Drawn</c> zero
    /// with <c>NoViewer</c> climbing is the regression.
    /// </para>
    /// </summary>
    public (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet)
        OverlayReadout()
    {
        if (!s_Initialized || !ServerAttached) return (0, 0, 0, 0, false);

        uint drawn = 0, clipped = 0, noViewer = 0, noOverlay = 0;
        int viewerSet = 0;

        ServerShims.OverlayCounts(&drawn, &clipped, &noViewer, &noOverlay, &viewerSet);

        return (drawn, clipped, noViewer, noOverlay, viewerSet != 0);
    }

    /// <summary>
    /// Zeroes the four counters <see cref="OverlayReadout"/> reports, so a
    /// caller can read an interval rather than a running total.
    /// </summary>
    public void OverlayResetCounts()
    {
        if (!s_Initialized || !ServerAttached) return;

        ServerShims.OverlayResetCounts();
    }

    /// <summary>
    /// The player-shadow counters <c>cs_sv_playershadow</c> prints, copied into
    /// <paramref name="into"/> in native's order (enabled, built, updates,
    /// contacts, touches, snaps, live, outlived); how many were written. Zero
    /// with no engine.
    /// </summary>
    public int PlayerShadowCounts(Span<int> into)
    {
        if (!s_Initialized || !ServerAttached || into.IsEmpty) return 0;

        fixed (int* p = into) { return ServerShims.PlayerShadowCounts(p, into.Length); }
    }

    /// <summary>
    /// Installs native's exit recorders -- the SIGTERM/SIGINT/SIGHUP handlers
    /// that note the sender and then run the handler that was there, and the
    /// SDL quit/window-close watch -- and names the file a fatal stop writes
    /// its record to. Once per process; a second arm only replaces the path.
    /// What was installed, as native's kExitWatch* bits.
    /// </summary>
    public int ExitWatchArm(string recordPath)
    {
        if (!s_Initialized || !ServerAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(recordPath + "\0");
        fixed (byte* p = bytes) { return ServerShims.ExitWatchArm(p); }
    }

    /// <summary>
    /// The exit recorders' facts, in native's ExitWatchFact_t order; how many
    /// were written. Zero with no engine.
    /// </summary>
    public int ExitWatchRead(Span<int> into)
    {
        if (!s_Initialized || !ServerAttached || into.IsEmpty) return 0;

        fixed (int* p = into) { return ServerShims.ExitWatchRead(p, into.Length); }
    }

    /// <summary>
    /// Takes the exit recorders back out: the signal handlers are restored
    /// and SDL's quit/window-close watch is REMOVED, so the last engine
    /// frames after this assembly's module is unmapped can never call into
    /// freed code. The facts already recorded are kept. What was installed,
    /// as native's kExitWatch* bits (0 when nothing was armed).
    /// </summary>
    public int ExitWatchDisarm()
    {
        if (!s_Initialized || !ServerAttached) return 0;

        return ServerShims.ExitWatchDisarm();
    }

    /// <summary>One bullet streak, drawn between two points.</summary>
    public void TracerEffect(Vector start, Vector end)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        ServerShims.TracerEffect(s, e);
    }

    /// <summary>
    /// Bubbles rising out of an entity's volume.
    ///
    /// <para>
    /// Takes the entity rather than a pair of corners, because the volume is
    /// the brush the mapper drew and its world-space bounds are the engine's to
    /// read.
    /// </para>
    /// </summary>
    public void BubbleEffect(EntityHandle entity, int count, float speed)
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.BubbleEffect(entity.Value, count, speed);
    }

    /// <summary>
    /// The funnel of sprites that converges on a point, or flies out of one.
    /// </summary>
    /// <param name="spriteIndex">A precached sprite, as its model index.</param>
    /// <param name="reversed">Outward rather than inward.</param>
    public void FunnelEffect(Vector origin, int spriteIndex, bool reversed)
    {
        if (!s_Initialized || !ServerAttached || spriteIndex <= 0) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        ServerShims.FunnelEffect(o, spriteIndex, reversed ? 1 : 0);
    }

    /// <summary>
    /// A puff of dust, thrown along a direction.
    ///
    /// <para>
    /// The direction is normalised on the far side, because the engine asserts
    /// on one that is not a unit vector — a debug build stops on it rather than
    /// drawing something odd.
    /// </para>
    /// </summary>
    public void DustEffect(Vector origin, Vector direction, float size, float speed)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* d = stackalloc float[3] { direction.X, direction.Y, direction.Z };

        ServerShims.DustEffect(o, d, size, speed);
    }

    /// <summary>A splash of water at a point. Slime is the green one.</summary>
    public void SplashEffect(Vector origin, float scale, bool inSlime = false)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        ServerShims.SplashEffect(o, scale, inSlime ? 1 : 0);
    }

    /// <summary>
    /// A muzzle flash at a point, pointed somewhere.
    ///
    /// <para>
    /// A temp entity carrying a position, a set of angles and a size — and no
    /// entity index. That is the whole of why this one crosses: the rest of the
    /// particle and trail family delivers its effect by being a networked entity
    /// whose stock client class runs the emitter, and a managed entity never
    /// appears on the wire as that class.
    /// </para>
    /// </summary>
    /// <param name="angles">Pitch, yaw and roll, in the order an entity's angles are already held.</param>
    /// <param name="type">Stock's MUZZLEFLASH_TYPE_DEFAULT is 1, which is the only value stock's env_muzzleflash ever passes.</param>
    public void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[3] { angles.X, angles.Y, angles.Z };

        ServerShims.MuzzleFlashEffect(o, a, scale, type);
    }

    /// <summary>
    /// Where the water beneath a point is, and what kind it is.
    ///
    /// <para>
    /// Three answers from one call, because the trace has to run to give any of
    /// them: asking "am I under water" and "where is the surface" separately
    /// would run it twice and let the two answers disagree about the same
    /// water.
    /// </para>
    /// </summary>
    public WaterBelow WaterSurfaceBelow(Vector start)
    {
        if (!s_Initialized || !ServerAttached)
            return new WaterBelow(false, false, false, start);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* surface = stackalloc float[3];
        int slime;

        int answer = ServerShims.WaterSurfaceBelow(s, surface, &slime);

        return new WaterBelow(
            Submerged: answer < 0,
            Found: answer > 0,
            Slime: slime != 0,
            Surface: answer > 0 ? new Vector(surface[0], surface[1], surface[2]) : start);
    }

    /// <summary>
    /// Sets how a numbered lightstyle animates. The pattern is one letter per
    /// tenth of a second: 'a' is black, 'm' is normal brightness, 'z' is about
    /// double. It repeats forever.
    ///
    /// <para>
    /// A style number rather than an entity, because that is what the engine
    /// keeps — 64 slots shared by everything in the map. vbsp reserves 32..62
    /// for switchable lights and assigns one per NAMED light entity, so two
    /// lights the mapper named the same thing share a style and go out
    /// together. That is the reason this is not an entity property.
    /// </para>
    /// </summary>
    public void EngineLightStyle(int style, string pattern)
    {
        if (!s_Initialized || !ServerAttached) return;

        var bytes = Encoding.UTF8.GetBytes(pattern + "\0");
        fixed (byte* p = bytes) { ServerShims.EngineLightStyle(style, p); }
    }

    /// <summary>
    /// Precaches a material by name — "skybox/sky_day01_01rt", with no
    /// extension.
    ///
    /// <para>
    /// Not <see cref="PrecacheModel"/> with a .vmt, which is the neighbouring
    /// call and answers an INDEX. A model index is a number both sides of the
    /// wire agree on; a material is only ever a name the renderer resolves, and
    /// there is nothing to network.
    /// </para>
    /// </summary>
    public void PrecacheMaterial(string material)
    {
        if (!s_Initialized || !ServerAttached || material.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(material + "\0");
        fixed (byte* p = bytes) { ServerShims.PrecacheMaterial(p); }
    }

    /// <summary>
    /// A particle system, by name, so the client can build one.
    ///
    /// <para>
    /// The neighbour of <see cref="PrecacheModel"/> and
    /// <see cref="PrecacheMaterial"/> and interchangeable with neither: a
    /// particle system has its own string table, and a name that never reached
    /// it does not refuse at draw time — it is simply not there.
    /// </para>
    ///
    /// <para>
    /// Stock precaches the fire systems from <c>CFire::Precache</c>. That has
    /// not run since <c>env_fire</c> became a C# class, because
    /// <c>LINK_ENTITY_TO_CLASS( env_fire, CFire )</c> sits inside
    /// <c>#ifndef SOURCESHARP</c> and no <c>CFire</c> is ever constructed, and
    /// there was no other door to ask through.
    /// </para>
    /// </summary>
    public void PrecacheParticleSystem(string name)
    {
        if (!s_Initialized || !ServerAttached || name.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { ServerShims.PrecacheParticleSystem(p); }
    }

    /// <summary>
    /// States the 3D skybox every player is told about.
    ///
    /// <para>
    /// A push, not an answer to a question, and the rates are why: a map states
    /// its skybox once at level load, while the server reads it for every
    /// player every snapshot. The native side keeps the last thing pushed.
    /// </para>
    /// </summary>
    public void SetSkyboxParams(in SkyboxParams parameters)
    {
        if (!s_Initialized || !ServerAttached) return;

        fixed (SkyboxParams* p = &parameters) { ServerShims.SetSkyboxParams(p); }
    }

    /// <summary>
    /// Says there is no 3D skybox — a map with no sky_camera, or one that has
    /// just been unloaded. Players fall back to the engine's "nowhere" area.
    /// </summary>
    public void ClearSkyboxParams()
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.SetSkyboxParams(null);
    }

    /// <summary>
    /// States what fog the viewer is standing in — plan_entity.md step
    /// 8e-render. CLIENT ONLY; the slot is null on the server.
    ///
    /// <para>
    /// A push for the same reason <see cref="SetSkyboxParams"/> is one, from the
    /// other end: the values change when a mapper's entity says so, and
    /// <c>viewrender.cpp</c> asks for them several times per frame across four
    /// call sites. The native side keeps the last thing pushed and hands back a
    /// pointer to it.
    /// </para>
    /// </summary>
    public void SetViewerFogParams(in FogParams parameters)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (FogParams* p = &parameters) { ClientShims.SetViewerFogParams(p); }
    }

    /// <summary>
    /// Says this viewer has no fog controller, which is NOT the same as saying
    /// its fog is off.
    ///
    /// <para>
    /// <c>SourceSharp_ViewerFogParams</c> then answers NULL — the early-out
    /// stock takes for a player with no fog controller — rather than a zeroed
    /// <c>fogparams_t</c>, which every caller would read as a real "no fog".
    /// Step 7c's whole objection to a zeroed struct was that a screenshot cannot
    /// tell "this map has no fog" from "nobody asked"; this keeps the two apart.
    /// </para>
    /// </summary>
    public void ClearViewerFogParams()
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.SetViewerFogParams(null);
    }

    /// <summary>
    /// States what 3D skybox the viewer is shown — the <c>defect-skybox</c>
    /// lane. CLIENT ONLY; the slot is null on the server.
    ///
    /// <para>
    /// The other end of <see cref="SetSkyboxParams"/> and the same
    /// <see cref="SkyboxParams"/>: one map's sky camera making one round trip,
    /// pushed on the server and landing here. A push rather than an answer for
    /// the reason that one is: the values change when a mapper's entity says
    /// so, and <c>viewrender.cpp</c> asks for them several times a frame across
    /// eight call sites.
    /// </para>
    /// </summary>
    public void SetViewerSkyboxParams(in SkyboxParams parameters)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (SkyboxParams* p = &parameters) { ClientShims.SetViewerSkyboxParams(p); }
    }

    /// <summary>
    /// Says this viewer has no sky camera, which is NOT the same as saying its
    /// sky camera is in area 255.
    ///
    /// <para>
    /// <c>SourceSharp_ViewerSkyboxParams</c> then answers NULL — the early-out
    /// stock's <c>CSkyboxView</c> takes for a player with no sky camera —
    /// rather than a zeroed <c>sky3dparams_t</c>, whose area of 0 is a REAL
    /// visleaf area and would send the renderer at a camera that is not there.
    /// Area 255 is the different fact that a camera IS there and sits in the
    /// engine's "nowhere"; <c>PreRender3dSkyboxWorld</c> refuses that on its own
    /// line, one line after the NULL.
    /// </para>
    /// </summary>
    public void ClearViewerSkyboxParams()
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.SetViewerSkyboxParams(null);
    }

    /// <summary>
    /// The 3D-skybox area of one client, read off the SDK hierarchy ONLY.
    ///
    /// <para>
    /// plan_server.md Phase 8b, plan-audit C4. Half of a pair;
    /// <see cref="EntitiesStandalonePlayerSkyboxArea"/> is the other half, and
    /// the ORDER they are asked in is <c>ServerEntities.SkyboxAreaForClient</c>'s
    /// — which is the whole content of the crossing, because that order used to
    /// be an <c>#ifdef SOURCESHARP</c> block inside
    /// <c>CServerGameEnts::CheckTransmit</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>area</c> is untouched on a miss, not zeroed.</b> Area 0 is a real
    /// visleaf area, so "the world's first area" and "no such recipient" cannot
    /// share a value — CheckTransmit returns early for one and walks every
    /// edict for the other.
    /// </para>
    /// </summary>
    public bool EntitiesSdkPlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        if (!s_Initialized || !ServerAttached) return false;

        int value = 0;
        int found = ServerShims.EntitiesSdkPlayerSkyboxArea(edictIndex, &value);
        if (found == 0) return false;

        area = value;
        return true;
    }

    /// <summary>
    /// The 3D-skybox area of one client, read off the standalone hierarchy —
    /// the other half of the pair above.
    /// </summary>
    public bool EntitiesStandalonePlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        if (!s_Initialized || !ServerAttached) return false;

        int value = 0;
        int found = ServerShims.EntitiesStandalonePlayerSkyboxArea(edictIndex, &value);
        if (found == 0) return false;

        area = value;
        return true;
    }

    /// <summary>
    /// Where one client's ears are, read off the standalone hierarchy only.
    ///
    /// <para>
    /// plan_server.md Phase 7, plan-audit C5. Half of a pair;
    /// <see cref="ClientsSdkPlayerEarPosition"/> is the other half, and the
    /// ORDER they are asked in is <c>ServerClients.EarPositionForClient</c>'s —
    /// which is the whole content of the crossing, because that order used to
    /// be an <c>#ifdef SOURCESHARP</c> block inside
    /// <c>CServerGameClients::ClientEarPosition</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>ears</c> is untouched on a miss, not zeroed.</b> The world origin
    /// is a real position, so "the listener is at the origin" and "no such
    /// client" cannot share a value — the caller asserts before writing
    /// <c>vec3_origin</c> and has to know which case it is in.
    /// </para>
    /// </summary>
    public bool ClientsStandalonePlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        if (!s_Initialized || !ServerAttached) return false;

        float* xyz = stackalloc float[3];
        xyz[0] = 0.0f; xyz[1] = 0.0f; xyz[2] = 0.0f;
        int found = ServerShims.ClientsStandalonePlayerEarPosition(edictIndex, xyz);
        if (found == 0) return false;

        ears = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    /// <summary>
    /// Where one client's ears are, read off the SDK hierarchy — the other half
    /// of the pair above.
    /// </summary>
    public bool ClientsSdkPlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        if (!s_Initialized || !ServerAttached) return false;

        float* xyz = stackalloc float[3];
        xyz[0] = 0.0f; xyz[1] = 0.0f; xyz[2] = 0.0f;
        int found = ServerShims.ClientsSdkPlayerEarPosition(edictIndex, xyz);
        if (found == 0) return false;

        ears = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    /// <summary>
    /// Does this player see the world-space box — is it inside the PVS of
    /// the leaf cluster at their eyes? The engine's own read (stock builds
    /// the same cluster/PVS trio in its check-client machinery,
    /// triggers.cpp:1615); what a yes is worth is the caller's rule — stock's
    /// <c>CTRiggerHurt::RadiationThink</c> pings only the geiger of a player
    /// who can see the box.
    ///
    /// <para>
    /// <b>False when there is no engine, no attached shim, or the handle is
    /// not a player.</b> Failing closed means a player who cannot be asked
    /// hears nothing, which is the same outcome as an empty PVS and never a
    /// false ping.
    /// </para>
    /// </summary>
    public bool PlayerBoxInPvs(EntityHandle player, Vector mins, Vector maxs)
    {
        if (!s_Initialized || !ServerAttached) return false;

        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        return ServerShims.PlayerBoxInPvs(player.Value, lo, hi) != 0;
    }

    /// <summary>
    /// Tells one player what they are hearing.
    ///
    /// <para>
    /// <paramref name="entIndex"/> is an identity token rather than an entity
    /// index — the client only compares it with the last one to notice a
    /// change. Any distinct positive number per soundscape entity works, which
    /// is what lets an entity with no edict at all drive one. Zero is silence.
    /// </para>
    ///
    /// <para>
    /// <paramref name="localPositions"/> is up to eight places a soundscape's
    /// positioned sounds come from; <paramref name="localBits"/> says which of
    /// them are live. The rest are left as they were rather than zeroed, which
    /// is what stops a soundscape change networking eight stale vectors.
    /// </para>
    /// </summary>
    public void PlayerSetSoundscape(EntityHandle player, int entIndex, int soundscapeIndex,
                                           int localBits, ReadOnlySpan<Vector> localPositions)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* positions = stackalloc float[8 * 3];

        for (int i = 0; i < 8; i++)
        {
            Vector p = i < localPositions.Length ? localPositions[i] : default;
            positions[i * 3 + 0] = p.X;
            positions[i * 3 + 1] = p.Y;
            positions[i * 3 + 2] = p.Z;
        }

        ServerShims.PlayerSetSoundscape(player.Value, entIndex, soundscapeIndex, localBits, positions);
    }

    /// <summary>
    /// Places or moves one dynamic light. Client-side.
    ///
    /// <para>
    /// The engine keeps a small fixed pool keyed by <see cref="DynamicLight.Key"/>,
    /// and asking again with the same key returns the same light — which is how
    /// a light follows something instead of leaving one behind every frame.
    /// </para>
    /// </summary>
    public void DynamicLightUpdate(in DynamicLight light)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (DynamicLight* p = &light) { ClientShims.DynamicLightUpdate(p); }
    }

    /// <summary>
    /// Puts a light out now.
    ///
    /// <para>
    /// Not the same as ceasing to refresh it: a light burns for whatever its
    /// last <see cref="DynamicLight.LiveFor"/> said, and an entity that has been
    /// removed wants its light gone in the same frame.
    /// </para>
    /// </summary>
    public void DynamicLightRelease(int key)
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.DynamicLightRelease(key);
}

    /// Which of the engine's collision groups this entity is in.
    ///
    /// Not the same question as solidity. Solidity is the shape an entity
    /// presents to a trace; a collision group is one half of a PAIR the game
    /// rules answer about, which is the only way to say "solid to vehicles and
    /// to nothing else".
    /// </summary>
    public void EntitySetCollisionGroup(EntityHandle entity, CollisionGroup group)
    {
        if (s_Initialized && ServerAttached)
            ServerShims.EntitySetCollisionGroup(entity.Value, (int)group);
    }

    /// <summary>
    /// The same, read back. <see cref="CollisionGroup.None"/> for a handle that
    /// resolves to nothing, and for any engine group this mod has no name for.
    /// </summary>
    public CollisionGroup EntityGetCollisionGroup(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached)
            return CollisionGroup.None;

        return (CollisionGroup)ServerShims.EntityGetCollisionGroup(entity.Value);
    }

    /// <summary>
    /// What shape this entity presents to a trace, as the engine's own FSOLID_
    /// bits. Zero for a handle that resolves to nothing.
    ///
    /// <para>
    /// Read off the collideable, so both hierarchies answer: the thing a
    /// projectile has run into may be a CBaseEntity trigger volume or a managed
    /// brush, and the projectile cannot know which it was handed.
    /// </para>
    /// </summary>
    public SolidFlags EntityGetSolidFlags(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached)
            return SolidFlags.None;

        return (SolidFlags)ServerShims.EntityGetSolidFlags(entity.Value);
    }

    /// <summary>
    /// Joins the engine's area-portal list at the number vbsp assigned, so the
    /// per-client visibility pass has something to ask about this portal.
    /// Returns 0 if the number is not a portal.
    /// </summary>
    public uint AreaPortalCreate(int portalNumber, int portalVersion)
    {
        if (!s_Initialized || !ServerAttached)
            return 0;

        return ServerShims.AreaPortalCreate(portalNumber, portalVersion);
    }

    /// <summary>Leaves the list. The hole is left open behind it.</summary>
    public void AreaPortalDestroy(uint portal)
    {
        if (s_Initialized && ServerAttached && portal != 0)
            ServerShims.AreaPortalDestroy(portal);
    }

    /// <summary>Whether the hole is open.</summary>
    public void AreaPortalSetOpen(uint portal, bool open)
    {
        if (s_Initialized && ServerAttached && portal != 0)
            ServerShims.AreaPortalSetOpen(portal, open ? 1 : 0);
    }

    /// <summary>
    /// The distance past which a window portal shuts, measured from the volume
    /// of the entity given.
    /// </summary>
    public void AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance)
    {
        if (s_Initialized && ServerAttached && portal != 0)
            ServerShims.AreaPortalSetWindow(portal, entity.Value, fadeDistance);
    }

    /// <summary>Turns one of the map's compiled occluders on or off. Client-side.</summary>
    public void ActivateOccluder(int occluderIndex, bool active)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.ActivateOccluder(occluderIndex, active ? 1 : 0);
    }

    /// <summary>
    /// Which of stock's two distance-fade rules this entity follows, and over
    /// what band. Client-side.
    ///
    /// <para>
    /// Two rules rather than one because stock has two and they disagree about
    /// the curve AND about what the distance is measured to — see
    /// <see cref="FadeStyle"/>.
    /// </para>
    /// </summary>
    public void StandaloneSetFade(EntityHandle entity, FadeStyle style,
                                         float startDistance, float endDistance,
                                         float minimumAlpha = 0f)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.StandaloneSetFade(entity.Value, (int)style, startDistance, endDistance, minimumAlpha);
    }

    /// <summary>
    /// A second brush model drawn at this entity's transform, after its own and
    /// at full opacity. Client-side; a negative index clears it.
    /// </summary>
    public void StandaloneSetOverlayModel(EntityHandle entity, int modelIndex)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.StandaloneSetOverlayModel(entity.Value, modelIndex);
    }

    /// <summary>
    /// How big this instance wears its model, as a multiplier on the size the
    /// artist built it at. Answered by BOTH sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's copy is what the player sees. The server's is what its own
    /// skeleton is built with, so attachments on a scaled model land where the
    /// artist put them, and it is what the scaled-collision rule reads when it
    /// decides whether a trace may use the solver's scaled hull.
    /// </para>
    /// <para>
    /// Zero and below are REFUSED natively rather than clamped, and say so in
    /// the console: a scale of zero collapses every bone onto the entity
    /// origin, which is a thing that is still in the world and still solid to
    /// the entity list but draws as nothing and traces as nothing.
    /// </para>
    /// </remarks>
    public void StandaloneSetModelScale(EntityHandle entity, float scale)
    {
        if (s_Initialized && (ServerAttached || ClientAttached))
            if (ServerShimsOrNull is { } __att44) __att44.StandaloneSetModelScale(entity.Value, scale);
            else ClientShims.StandaloneSetModelScale(entity.Value, scale);
    }

    /// <summary>
    /// worldspawn's chaptertitle key — the world's one game-logic key. Empty
    /// when the map set no title, on the client (the slot is server-only), or
    /// before the world exists.
    /// </summary>
    public string WorldChapterTitle()
    {
        if (!s_Initialized || !ServerAttached)
            return string.Empty;

        return FromUtf8(ServerShims.WorldChapterTitle());
    }

    /// <summary>
    /// True when a client entity is DORMANT — out of the local player's PVS,
    /// so every value it holds is the last one that arrived rather than the
    /// current one. Client-side.
    ///
    /// <para>
    /// Ask this before believing any client-side reading, and print it in any
    /// report. A dormant entity answers a query without complaint, and that is
    /// not hypothetical: a viewmodel whose bounds sat below the floor (v_
    /// models are authored in CAMERA space) was culled, went dormant, and
    /// carried on answering plausible stale numbers to everything that asked.
    /// </para>
    /// </summary>
    public bool StandaloneIsDormant(EntityHandle entity)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.StandaloneIsDormant(entity.Value) != 0;
    }

    public void ClientEmitSound(EntityHandle entity, string soundName, Vector? origin = null)
    {
        if (!s_Initialized || !ClientAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");

        if (origin is Vector at)
        {
            float* xyz = stackalloc float[3];
            xyz[0] = at.X;
            xyz[1] = at.Y;
            xyz[2] = at.Z;

            fixed (byte* p = bytes) ClientShims.ClientEmitSound(entity.Value, p, xyz);
        }
        else
        {
            fixed (byte* p = bytes) ClientShims.ClientEmitSound(entity.Value, p, null);
        }
    }

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
        if (!s_Initialized || !(ServerAttached || ClientAttached))
            return string.Empty;

        byte* buffer = stackalloc byte[256];
        if (ServerShimsOrNull is { } __att48) __att48.EntityGetModelName(entity.Value, buffer, 256);
        else ClientShims.EntityGetModelName(entity.Value, buffer, 256);

        return FromUtf8(buffer);
    }














    /// <summary>
    /// Stock's <c>CBaseEntity::DetectInSkybox</c>: raises <c>EFL_IN_SKYBOX</c>
    /// if this entity stands inside a 3D skybox, and answers whether it does.
    ///
    /// <para>
    /// A query managed code cannot make — it walks the sky camera list and asks
    /// the engine whether two areas are connected. The flag it raises has one
    /// consequence, and it is the one an env_smokestack in a skybox depends on:
    /// the entity is sent to everyone instead of being PVS-checked, because
    /// nobody's PVS contains the 3D skybox.
    /// </para>
    /// </summary>
    public bool EntityDetectInSkybox(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached)
            return false;

        return ServerShims.EntityDetectInSkybox(entity.Value) != 0;
    }





    /// <summary>
    /// Retunes, restarts or stops an entity's AMBIENT channel.
    ///
    /// <para>
    /// Not another <see cref="EntityEmitSound"/>. The engine keys an ambient by
    /// (entity index, sound name), so a second call carrying
    /// <c>SND_CHANGE_PITCH</c> or <c>SND_CHANGE_VOL</c> retunes what is already
    /// playing instead of layering a second copy on top, and <c>SND_STOP</c>
    /// ends it. Every envelope, LFO and fade an ambient_generic has is built out
    /// of that one distinction.
    /// </para>
    ///
    /// <para>
    /// The index and the origin are handed over rather than an entity, because
    /// WHICH entity a sound comes from is the decision — a mapper's
    /// SourceEntityName key settles it — and because a stop must reach the same
    /// index the sound started on even after that entity has gone.
    /// </para>
    /// </summary>
    /// <param name="soundLevel">A soundlevel in dB, NOT an attenuation.</param>
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName,
                                        float volume, int soundLevel, int flags, int pitch)
    {
        if (!s_Initialized || !ServerAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        fixed (byte* p = bytes)
            ServerShims.EmitAmbientSound(entIndex, xyz, p, volume, soundLevel, flags, pitch);
    }


    /// <summary>
    /// Emit one sound patch at the given envelope values — plan_gamedll_after.md
    /// Phase A3, second pair.
    ///
    /// <para>
    /// <paramref name="volume01"/> is the ENVELOPE's value and not what reaches
    /// the engine: the shim multiplies it by the volume the sound script chose,
    /// which it read once at create time. A patch's ramp is a MULTIPLIER on a
    /// script's number, and only one side of this boundary has ever seen that
    /// number.
    /// </para>
    /// </summary>
    public void SoundPatchEmit(int patch, float volume01, float pitch, int flags,
                                      float startTime, bool emitCaption)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        if (ServerShimsOrNull is { } __att49) __att49.SoundPatchEmit(patch, volume01, pitch, flags, startTime, emitCaption ? 1 : 0);
        else ClientShims.SoundPatchEmit(patch, volume01, pitch, flags, startTime, emitCaption ? 1 : 0);
    }

    /// <summary>
    /// Stop one sound patch, if it is playing. Idempotent, because the shim
    /// tracks whether it ever started — stopping a sound that never played is a
    /// warning from the engine and a real one.
    /// </summary>
    public void SoundPatchStop(int patch)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        if (ServerShimsOrNull is { } __att50) __att50.SoundPatchStop(patch);
        else ClientShims.SoundPatchStop(patch);
    }

    /// <summary>
    /// The two questions the frame pass asks of every live patch, in one
    /// crossing.
    /// </summary>
    /// <param name="entIndex">
    /// The entity the patch rides, or negative when it has gone — which is what
    /// makes the caller drop it.
    /// </param>
    /// <param name="filterActive">
    /// Whether anybody is still listening. A patch with no recipients is KEPT
    /// and not emitted, which is stock's behaviour and not the same thing as
    /// having lost its entity.
    /// </param>
    public void SoundPatchState(int patch, out int entIndex, out bool filterActive)
    {
        entIndex = -1;
        filterActive = false;

        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        int index = -1;
        int active = 0;

        if (ServerShimsOrNull is { } __att51) __att51.SoundPatchState(patch, &index, &active);
        else ClientShims.SoundPatchState(patch, &index, &active);

        entIndex = index;
        filterActive = active != 0;
    }

    /// <summary>
    /// Free the native patch. The one direction <c>SoundDestroy</c> does not
    /// cover: a queued DESTROY command comes due inside the managed frame pass,
    /// so the decision is managed and the delete is not.
    /// </summary>
    public void SoundPatchFree(int patch)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return;

        if (ServerShimsOrNull is { } __att52) __att52.SoundPatchFree(patch);
        else ClientShims.SoundPatchFree(patch);
    }

    /// <summary>
    /// A sound script's parameters (GetParametersForSound): volume, pitch,
    /// pitch range and soundlevel. Null when the script has no such entry.
    /// </summary>
    public SoundScriptParams? SoundScriptParameters(string soundName)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached) || soundName.Length == 0) return null;

        float* outs = stackalloc float[5];
        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        int found;
        fixed (byte* p = bytes)
            found = ServerShimsOrNull is { } server ? server.SoundScriptParameters(p, outs) : ClientShims.SoundScriptParameters(p, outs);

        return found == 0
            ? null
            : new SoundScriptParams(outs[0], (int)outs[1], (int)outs[2], (int)outs[3], (int)outs[4]);
    }

    /// <summary>
    /// A sound patch on the entity, heard from where it stands
    /// (SoundCreate with a CPASAttenuationFilter): the handle the managed
    /// sound controller tracks it by, or 0. Played, ramped and destroyed
    /// through the controller like any other patch.
    /// </summary>
    public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached) || soundName.Length == 0) return 0;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes)
            return ServerShimsOrNull is { } server
                ? server.SoundPatchCreate(entity.Value, channel, p, soundLevel)
                : ClientShims.SoundPatchCreate(entity.Value, channel, p, soundLevel);
    }

    /// <summary>
    /// The surface's scrape sound, rough or smooth (surfacedata_t::sounds),
    /// or null when it has none.
    /// </summary>
    public string? SurfaceScrapeSound(int surfaceProps, bool smooth)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        byte* buffer = stackalloc byte[128];
        int length = ServerShimsOrNull is { } server
            ? server.SurfaceScrapeSound(surfaceProps, smooth ? 1 : 0, buffer, 128)
            : ClientShims.SurfaceScrapeSound(surfaceProps, smooth ? 1 : 0, buffer, 128);
        return length <= 0 ? null : Encoding.UTF8.GetString(buffer, Math.Min(length, 127));
    }

    // SoundEntQuery AND SoundEntInsert WERE HERE.
    //
    // plan_gamedll_after.md Phase A3, second pair, removed both slots and these
    // two wrappers with them. CSoundEnt's pool is SourceSharp.Server's SoundEnt,
    // so the two callers -- env_microphone and ai_sound -- resolve
    // IServerSoundEnt and call it. Nothing crosses the boundary for a list that
    // lives on this side of it.

    /// <summary>
    /// One titles.txt entry by name, or null when the level has no such entry.
    ///
    /// Client-side: the table is the client's, and it holds the placement and
    /// the timing as well as the words — a titles.txt message is a whole
    /// game_text, not a string.
    /// </summary>
    public TextMessage? HudTextMessageGet(string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0)
            return null;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        TextMessage message = default;

        fixed (byte* p = bytes)
        {
            if (ClientShims.HudTextMessageGet(p, &message) == 0)
                return null;
        }

        return message;
    }

    //-------------------------------------------------------------------------
    // What the stock user-message hooks did, carried out over values C# parsed
    // (the user-message payload model): the client's view effects, rumble,
    // viewport and voice status are native state. Client-side only.
    //-------------------------------------------------------------------------

    /// <summary>The two facts MsgFunc_VGUIMenu read off native state, for a decision made here.</summary>
    public ViewportFacts ClientViewportQuery(string panel)
    {
        if (!s_Initialized || !ClientAttached || panel.Length == 0)
            return ViewportFacts.None;

        var bytes = Encoding.UTF8.GetBytes(panel + "\0");
        fixed (byte* p = bytes)
            return (ViewportFacts)ClientShims.ClientViewportQuery(p);
    }

    /// <summary>
    /// MsgFunc_VGUIMenu's effects on the viewport, each one asked for in
    /// <paramref name="actions"/>: the data travels as name, NUL, value, NUL.
    /// </summary>
    public void ClientViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data,
                                   ViewportActions actions)
    {
        if (!s_Initialized || !ClientAttached || panel.Length == 0)
            return;

        var pairs = new List<byte>();
        foreach (var (name, value) in data)
        {
            pairs.AddRange(Encoding.UTF8.GetBytes(name));
            pairs.Add(0);
            pairs.AddRange(Encoding.UTF8.GetBytes(value));
            pairs.Add(0);
        }

        var nameBytes = Encoding.UTF8.GetBytes(panel + "\0");
        var pairBytes = pairs.ToArray();
        fixed (byte* n = nameBytes)
        fixed (byte* d = pairBytes)
            ClientShims.ClientViewportShow(n, show ? 1 : 0, d, pairBytes.Length, (int)actions);
    }

    /// <summary>
    /// The key this player has bound to a command, or an empty string when
    /// nothing is. Client-side — the server has no idea what anyone's keyboard
    /// looks like.
    /// </summary>
    public string HudLookupBinding(string binding)
    {
        if (!s_Initialized || !ClientAttached || binding.Length == 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(binding + "\0");
        byte* buffer = stackalloc byte[64];

        fixed (byte* p = bytes)
            ClientShims.HudLookupBinding(p, buffer, 64);

        return FromUtf8(buffer);
    }

    /// <summary>
    /// A "#Token" through the localisation tables. Answers with the token
    /// unchanged when there is no entry, so a caller can always draw the result.
    /// </summary>
    public string HudLocalise(string token)
    {
        if (!s_Initialized || !ClientAttached || token.Length == 0)
            return token;

        var bytes = Encoding.UTF8.GetBytes(token + "\0");
        byte* buffer = stackalloc byte[512];

        fixed (byte* p = bytes)
            ClientShims.HudLocalise(p, buffer, 512);

        return FromUtf8(buffer);
    }



    /// <summary>
    /// The surfaceprop an entity's own MODEL declares, or -1.
    ///
    /// <para>
    /// A TRACE answers with the surfaceprop of the face it struck, which is
    /// the right answer for a wall and the wrong one for a
    /// func_breakable_surf: vbsp refuses to compile that entity unless exactly
    /// ONE of its faces is drawn, so the other five carry nodraw's
    /// surfaceprop — 0, the default, concrete. Shot from behind, glass reports
    /// concrete.
    /// </para>
    ///
    /// <para>
    /// -1 IS THE COMMON ANSWER AND NOT A FAILURE. It means "nothing better
    /// than the face you struck", which is what a studio model (whose
    /// per-hitbox surfaceprop is finer than any whole-model answer), a brush
    /// drawing several materials (there is no single answer), and a side with
    /// no materials loaded all honestly are.
    /// </para>
    /// </summary>
    public int EntityModelSurfaceProp(EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached) return -1;

        return ServerShims.EntityModelSurfaceProp(entity.Value);
    }

    /// <summary>
    /// A surfaceprop by name — "glass", "tile", "metal" — or -1. What a map
    /// key that names a KIND of surface rather than a material resolves
    /// through.
    /// </summary>
    public int SurfacePropIndex(string name)
    {
        if (!s_Initialized || !ServerAttached || name.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");

        fixed (byte* p = bytes) { return ServerShims.SurfacePropIndex(p); }
    }

    /// <summary>
    /// What a surfaceprop is made of, as a game material.
    ///
    /// <see cref="SurfaceMaterial.Unknown"/> for an index that names no
    /// surface, which is what a negative index and an empty surfaceprops table
    /// both give.
    /// </summary>
    public SurfaceMaterial SurfacePropGameMaterial(int surfaceProp)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return SurfaceMaterial.Unknown;

        return (SurfaceMaterial)(ServerShimsOrNull is { } __att53 ? __att53.SurfacePropGameMaterial(surfaceProp) : ClientShims.SurfacePropGameMaterial(surfaceProp));
    }

    /// <summary>
    /// The engine's own bullet impact at a point: the decal, the particles and
    /// the sound, all of which it picks from the surfaceprop it is handed.
    ///
    /// <para>
    /// <paramref name="surfaceProp"/> is passed in rather than traced for,
    /// which is the whole point of the call: a caller that knows what the
    /// thing it hit is really made of can say so. Negative means "whatever the
    /// struck face says", which is what every caller with nothing better to
    /// offer passes — see <see cref="EntityModelSurfaceProp"/>.
    /// </para>
    /// </summary>
    public void ImpactEffect(Vector start, Vector end, int surfaceProp,
                                    DamageType damageType, EntityHandle entity)
    {
        if (!s_Initialized || !ServerAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        ServerShims.ImpactEffect(s, e, surfaceProp, (int)damageType, entity.Value);
    }

    // ---- plan_gamedll.md Part 4, Phase 6: LEVELINIT'S NINE NATIVE STEPS ----
    //
    // Nine wrappers and no tenth that runs them in order. The order is
    // ServerGameDll.MapLoadNewGame's, in C#, where it can be read and tested;
    // a Engine.LevelInit() here would be the same C++ sequence one language
    // further up.

    /// <summary>Clears the map-entity reference list. Before the parse.</summary>
    public void EntitiesPurgeMapRefs()
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.EntitiesPurgeMapRefs();
    }

    /// <summary>Reaps entities that removed themselves during a precache.</summary>
    public void EntitiesCleanupDeleteList()
    {
        if (!s_Initialized || !ServerAttached) return;
        ServerShims.EntitiesCleanupDeleteList();
    }

    // // ---- plan_gamedll.md Part 4, Phase 7: SERVERACTIVATE'S AND
    // 	// ---- LEVELSHUTDOWN'S NATIVE STEPS.
    // 	//
    // 	// The same rule as LevelInit's nine above: one slot per irreducible step,
    // 	// C# owns the order and native owns no part of it. LevelShutdown is the
    // 	// clearest case in the whole plan -- stock's body is nine calls and the
    // 	// SEQUENCE is the only thing in it, so a single LevelShutdown slot would
    // 	// have been a crossing that carried nothing but the thing worth reading.
    // 	//
    // 	// SERVER ONLY, all twelve, and NULL on the client for the same reason the
    // 	// nine above are.
    // 	//
    // 	// APPENDED AT THE TAIL: every field above has an offset the managed side
    // 	// has already compiled against.

    // // ---- plan_gamedll.md Part 4, Phase 7: GAMEFRAME'S NATIVE STEPS.
    // 	//
    // 	// Fifteen slots and no GameFrame slot, on the same rule as LevelInit's
    // 	// nine and LevelShutdown's twelve. The two per-frame passes below are the
    // 	// named list's, and THE MANAGED FRAME RUNS INSIDE THEM rather than around
    // 	// them -- SourceSharpManaged is a row of kServerSystems[] at the position
    // 	// CSourceSharpSystem held, so FramePreThink and Frame keep the same native
    // 	// neighbours stock gives them. Hoisting them out would move the managed
    // 	// frame ahead of CSoundEnt, CRagdollLRURetirement and CEntityListSystem,
    // 	// none of which is a no-op. See shell_frame.cpp.
    // 	//
    // 	// SERVER ONLY, all fifteen.
    // 	//
    // 	// APPENDED AT THE TAIL: every field above has an offset the managed side
    // 	// has already compiled against.

    //-------------------------------------------------------------------------
    // MOD-DECLARED NETWORK STRING TABLES — plan_gamedll_after.md Phase A8.
    //
    // ADDITIVE. The seven shared tables (Materials, ParticleEffectNames,
    // EffectDispatch, VguiScreen, InfoPanel, Scenes, ServerMapCycle) are
    // created natively and 141 native references read the globals behind them;
    // nothing here replaces any of that. These six are how a MOD declares a
    // table of its OWN, which is what the CreateNetworkStringTables slot was
    // reserved for.
    //
    // CREATION ONLY WORKS INSIDE IServerModule's CreateNetworkStringTables.
    // The engine opens a window for it there and refuses outside it — Create
    // answers -1 and says so rather than the caller finding out from an index
    // that does not work. Find works at any time, which is what a hot reload
    // needs: the managed side forgets its ids, the engine does not forget its
    // tables.
    //
    // EVERY MISS IS -1. Never INVALID_STRING_INDEX (0xFFFF), which crosses as
    // 65535 and reads as success to every `if (i >= 0)`.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Creates a table and answers its id, or answers the id of the table that
    /// already has that name. −1 on refusal — including a create attempted
    /// outside <c>CreateNetworkStringTables</c>, which the engine does not
    /// allow.
    /// </summary>
    public int StringTableCreate(string name, int maxEntries)
    {
        if (!s_Initialized || !ServerAttached) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            return ServerShims.StringTableCreate(p, maxEntries);
        }
    }

    /// <summary>The id of an existing table, or −1. Valid at any time.</summary>
    public int StringTableFind(string name)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            return (ServerShimsOrNull is { } __att54 ? __att54.StringTableFind(p) : ClientShims.StringTableFind(p));
        }
    }

    /// <summary>Adds a string; answers its index, or −1.</summary>
    public int StringTableAddString(int tableId, string value)
    {
        if (!s_Initialized || !ServerAttached) return -1;

        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
        {
            return ServerShims.StringTableAddString(tableId, p);
        }
    }

    /// <summary>The index of a string, or −1 when the table does not hold it.</summary>
    public int StringTableFindStringIndex(int tableId, string value)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return -1;

        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
        {
            return (ServerShimsOrNull is { } __att55 ? __att55.StringTableFindStringIndex(tableId, p) : ClientShims.StringTableFindStringIndex(tableId, p));
        }
    }

    /// <summary>
    /// One entry, by index. Null when there is none — and null rather than
    /// empty on purpose: a table may legitimately hold an empty string, and
    /// collapsing the two makes "there is no such entry" indistinguishable
    /// from "the entry is blank".
    /// </summary>
    public string? StringTableGetString(int tableId, int stringIndex)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        return (ServerShimsOrNull is { } __att56 ? __att56.StringTableGetString(tableId, stringIndex, buf, bufSize) : ClientShims.StringTableGetString(tableId, stringIndex, buf, bufSize)) < 0
             ? null
             : FromUtf8(buf);
    }

    /// <summary>How many strings a table holds, or −1 when there is no such table.</summary>
    public int StringTableGetNumStrings(int tableId)
        => (s_Initialized && (ServerAttached || ClientAttached))
         ? (ServerShimsOrNull is { } __att57 ? __att57.StringTableGetNumStrings(tableId) : ClientShims.StringTableGetNumStrings(tableId))
         : -1;
    // ---- plan_gamedll_after.md Phase A2 ------------------------------------

    // THE THREE STEAM GAME-SERVER SLOTS ARE RETIRED -- plan_server_novalve.md
    // P2.5. IEngineHost's Steam members are C#'s own (SteamGameServerFlat, the
    // flat API); server.so no longer links libsteam_api.so.

    /// <summary>
    /// Stock's InvalidateMdlCache walk: every live entity that owns a
    /// <c>CStudioHdr</c> drops it. Native because that cache is a native member
    /// of <c>CBaseAnimating</c> and no managed walk reaches it.
    ///
    /// <para>
    /// <b>BOTH SIDES, AND IT ANSWERS HOW MANY</b> (lane diag-visibility). The
    /// slot used to be bound on the server only, so this returned quietly on
    /// the client and the flush reached nothing there; and it used to answer
    /// nothing at all, which made an empty body and a working one identical
    /// from outside. Zero when there is no native side to ask, which is a true
    /// answer and not a degraded one: with no side bound there are no entities.
    /// </para>
    /// </summary>
    /// <returns>how many entities dropped a cache.</returns>
    public int EntitiesInvalidateMdlCacheAll()
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return 0;
        return (ServerShimsOrNull is { } __att58 ? __att58.EntitiesInvalidateMdlCacheAll() : ClientShims.EntitiesInvalidateMdlCacheAll());
    }

    /// <summary>
    /// Every client shell re-evaluates whether the leaf system should be
    /// showing it. Answers how many did.
    ///
    /// <para>
    /// <b>CLIENT ONLY</b>, and zero on the server, where the slot is null.
    /// Visibility there is the PVS's question and a different one.
    /// </para>
    ///
    /// <para>
    /// This is what lets a rendering toggle whose whole effect is its change
    /// callback live in managed code — <c>vcollide_wireframe</c>, which turns
    /// nothing on by itself and instead forces every renderable to re-evaluate.
    /// It answers a count for <see cref="EntitiesInvalidateMdlCacheAll"/>'s
    /// reason: the effect is on the NEXT frame, so nothing about the call
    /// itself is observable and a body that swept nothing would be
    /// indistinguishable from one that swept everything.
    /// </para>
    /// </summary>
    /// <returns>how many shells re-evaluated.</returns>
    public int EntitiesUpdateVisibilityAll()
    {
        if (!s_Initialized || !ClientAttached) return 0;
        return ClientShims.EntitiesUpdateVisibilityAll();
    }



    // ---- plan_gamedll_after.md Phase A3 ------------------------------------

    /// <summary>
    /// The whole text of a file on the engine's <c>GAME</c> search paths, or
    /// null when it is on none of them.
    ///
    /// <para>
    /// <b>The engine's filesystem and not <c>System.IO</c>, and that is the
    /// whole point of these two slots.</b> <c>scripts/decals_subrect.txt</c>
    /// and <c>scripts/propdata.txt</c> are in neither the mod directory nor any
    /// mounted VPK: they are loose files under the SDK's <c>hl2/scripts</c>,
    /// reachable only through the <c>game</c> search paths gameinfo.txt sets
    /// up. A <c>File.ReadAllText</c> over a mod-relative path finds nothing —
    /// and finds it silently.
    /// </para>
    /// </summary>
    public string? FilesReadGameFile(string path)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        // ITS OWN SIZE CALL, and it went from a wrapper call to this on the
        // commit that converted FilesGameFileSize -- plan_native_api.md Phase 3.
        //
        // WHY THIS ONE DID NOT CONVERT WITH IT. Appendix B rules
        // FilesReadGameFile "C -- converts now" on the ground that
        // Open/Size/Read/Close are all IBaseFileSystem methods and
        // VBaseFileSystem011 is bound. Three of the four are refused by a
        // decision this tree already took and recorded: EngineBaseFileSystem
        // offers "nothing that writes and no handles", because a FileHandle_t
        // handed to managed code is a lifetime managed code then owns across a
        // GC. The handle-free route the size slot uses -- FileExists then Size
        // -- has no read counterpart: IBaseFileSystem::ReadFile takes a
        // `CUtlBuffer &`, which is a class managed cannot build.
        //
        // So the row stays on the flat ABI and is not in this phase's orphan
        // list. Converting it means either lifting the no-handles rule, which
        // is the owner's call, or a shim-layer method under Phase 6, which is
        // where a handle-free read belongs anyway.
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        int size;

        {
            var sizeName = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* sp = sizeName)
                size = (ServerShimsOrNull is { } __att59 ? __att59.FilesGameFileSize(sp) : ClientShims.FilesGameFileSize(sp));
        }

        if (size < 0) return null;
        if (size == 0) return string.Empty;

        var buf = new byte[size];
        var bytes = Encoding.UTF8.GetBytes(path + "\0");

        fixed (byte* p = bytes)
        fixed (byte* b = buf)
        {
            int read = (ServerShimsOrNull is { } __att60 ? __att60.FilesReadGameFile(p, b, size) : ClientShims.FilesReadGameFile(p, b, size));
            if (read < 0) return null;

            // `read` and not `size`: a short read is a real answer and decoding
            // the untouched tail would append however many zero bytes the array
            // still holds to the document.
            return Encoding.UTF8.GetString(buf, 0, read);
        }
    }

    /// <summary>
    /// A game file's BYTES, exactly as the search path holds them, or null when
    /// it is on none of them. For content that is shipped on rather than read:
    /// the message of the day goes to clients byte for byte, and a decode and
    /// re-encode would change any byte that is not valid UTF-8.
    /// </summary>
    /// <summary>
    /// A whole file off one named search path (<c>"GAME"</c>, <c>"BSP"</c>,
    /// ...), as bytes: <c>null</c> when it is not there, an empty array for a
    /// real empty file, never a truncated read (IShimEngine::FilesSizeOnPath /
    /// FilesReadOnPath, both sides; C9).
    /// </summary>
    public byte[]? FilesReadOnPathBytes(string path, string pathId)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached) || path.Length == 0 || pathId.Length == 0)
            return null;

        var p = Encoding.UTF8.GetBytes(path + "\0");
        var id = Encoding.UTF8.GetBytes(pathId + "\0");
        fixed (byte* pp = p)
        fixed (byte* pi = id)
        {
            int size = ServerShimsOrNull is { } s ? s.FilesSizeOnPath(pp, pi) : ClientShims.FilesSizeOnPath(pp, pi);
            if (size < 0) return null;
            var buf = new byte[size];
            if (size == 0) return buf;
            fixed (byte* b = buf)
            {
                int read = ServerShimsOrNull is { } s2 ? s2.FilesReadOnPath(pp, pi, b, size) : ClientShims.FilesReadOnPath(pp, pi, b, size);
                return read == size ? buf : null;
            }
        }
    }

    public byte[]? FilesReadGameFileBytes(string path)
    {
        if (!s_Initialized || !(ServerAttached || ClientAttached)) return null;

        if (!(ServerAttached || ClientAttached)) return null;

        int size;
        var sizePath = Encoding.UTF8.GetBytes(path + "\0");
        fixed (byte* sp = sizePath)
            size = (ServerShimsOrNull is { } __att61 ? __att61.FilesGameFileSize(sp) : ClientShims.FilesGameFileSize(sp));

        if (size < 0) return null;
        if (size == 0) return Array.Empty<byte>();

        var buf = new byte[size];
        var bytes = Encoding.UTF8.GetBytes(path + "\0");

        fixed (byte* p = bytes)
        fixed (byte* b = buf)
        {
            int read = (ServerShimsOrNull is { } __att62 ? __att62.FilesReadGameFile(p, b, size) : ClientShims.FilesReadGameFile(p, b, size));
            if (read < 0) return null;
            return read == size ? buf : buf.AsSpan(0, read).ToArray();
        }
    }

    //-------------------------------------------------------------------------
    // THE ENTITY FACTORY DICTIONARY'S FIVE QUESTIONS -- the `sub-factory` lane,
    // appended at ABI 242.
    //
    // SERVER ONLY. The slots are NULL on the client, so these answer 0 and null
    // there, which is what keeps EntityFactories.Available false on that side.
    //
    // ONCE PER LEVEL AND ONCE PER cs_sv_reload, from EntityFactories.Refresh --
    // 2 + N crossings with N = 156 on this build, plus 2 + M for the refusal
    // ledger with M = 3. Never per frame and never per entity.
    //
    // INTERNAL, AND DELIBERATELY NOT PART OF THE ENGINE SURFACE GAME CODE SEES.
    // Every other Engine member is forwarded by an EngineBinding area, and
    // EngineAreaTests holds that. These five are NOT, because a second public
    // route to this data is a route round the one distinction that matters:
    // EntityFactories.Available answers "was the dictionary asked", and a caller
    // reading Engine.EntityFactoryCount() straight would get 0 from an unwired
    // build and read it as "the table is empty" -- the exact misreading that
    // refuses `ent_create prop_physics` for a class that works. EntityFactories
    // is the only consumer, and it lives in this assembly.
    //-------------------------------------------------------------------------

    /// <summary>How many classnames the native dictionary holds a factory for.</summary>
    internal int EntityFactoryCount()
        => (s_Initialized && ServerAttached) ? ServerShims.EntityFactoryCount() : 0;

    /// <summary>
    /// The i'th installed classname in install order, or null past the end.
    ///
    /// <para>
    /// NULL AND NOT EMPTY-STRING, which <see cref="FromUtf8"/> would have given:
    /// the contract in <c>sourcesharp.h</c> is that indices are dense and this
    /// answers NULL outside the range, and
    /// <see cref="EntityFactories.Refresh"/> ENDS its walk on that answer rather
    /// than skipping it — a null in the middle means the far side is not the
    /// table this was written against, and walking on would read whatever came
    /// after it.
    /// </para>
    /// </summary>
    internal string? EntityFactoryNameAt(int index)
    {
        if (!s_Initialized || !ServerAttached)
            return null;

        return Utf8OrNull(ServerShims.EntityFactoryNameAt(index));
    }

    /// <summary>How many distinct classnames have been asked for and refused.</summary>
    internal int EntityFactoryRefusalCount()
        => (s_Initialized && ServerAttached) ? ServerShims.EntityFactoryRefusalCount() : 0;

    /// <summary>The i'th refused classname, or null past the end.</summary>
    internal string? EntityFactoryRefusalNameAt(int index)
    {
        if (!s_Initialized || !ServerAttached)
            return null;

        return Utf8OrNull(ServerShims.EntityFactoryRefusalNameAt(index));
    }

    /// <summary>How many times the i'th refused classname was asked for.</summary>
    internal int EntityFactoryRefusalTimesAt(int index)
        => (s_Initialized && ServerAttached)
            ? ServerShims.EntityFactoryRefusalTimesAt(index)
            : 0;

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
    private static byte* Utf8(string? text, List<IntPtr> owned)
    {
        if (text is null)
            return null;

        IntPtr p = Marshal.StringToCoTaskMemUTF8(text);
        owned.Add(p);
        return (byte*)p;
    }

    // The one reason managed itself can produce, in the same buffer and under
    // the same rules native writes it: ASCII, truncating, terminated.
    private static void SayNoBoundary(byte* error, int errorBytes)
    {
        if (error == null || errorBytes <= 0)
            return;

        ReadOnlySpan<byte> text = "the native half of this side is not initialised"u8;
        int n = Math.Min(text.Length, errorBytes - 1);

        text[..n].CopyTo(new Span<byte>(error, n));
        error[n] = 0;
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
