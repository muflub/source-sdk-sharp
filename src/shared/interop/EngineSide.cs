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
        // The precache is the server's (OwnModelPrecache, in each side's half):
        // the client hands none in and a missing model answers -1, as the null
        // slot did.
        Breaks = new BreakModels(this, Models, OwnModelPrecache);
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
    /// This side's vphysics forwarder tables (plan_engine_forwarders.md), bound
    /// from its own interface factory on first use: what every vphysics service
    /// of the side calls objects through.
    /// </summary>
    internal VPhysicsTables VPhysics => _vphysics ??= new VPhysicsTables(Interfaces);

    private VPhysicsTables? _vphysics;


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
    /// One crossing, for the whole process, per side. Zero when this side has no
    /// table yet or the shim is older than the slot.
    /// </summary>
    private nint AskForGlobalVars()
        => (s_Initialized && OwnShimsAttached) ? (OwnShims.EngineBase.GetGlobalVarsPtr()) : 0;

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

        if (OwnShimsAttached)
            Compare("curtime", (OwnShims.EngineBase.GetCurrentTime()).Equals(globals.CurTime), $"{(OwnShims.EngineBase.GetCurrentTime())}/{globals.CurTime}");
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
        if (!s_Initialized || !OwnShimsAttached)
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
                OwnShims.Console.Msg((byte*)ptr);
            });
        }
    }

    /// <summary>Prints a warning to the Source console.</summary>
    public void Warning(string message)
    {
        if (!s_Initialized || !OwnShimsAttached)
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
                OwnShims.Console.Warning((byte*)ptr);
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
        if (!s_Initialized || !OwnShimsAttached) return -1;

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return (OwnShims.Console.RegisterConCommand(pName, pHelp, unchecked((int)flags)));
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
        if (!s_Initialized || !OwnShimsAttached) return -1;

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var defBytes  = Encoding.UTF8.GetBytes(defaultValue + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pDef = defBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return (OwnShims.Console.RegisterConVar(pName, pDef, pHelp, flags,
                                      min.HasValue ? 1 : 0, min ?? 0f,
                                      max.HasValue ? 1 : 0, max ?? 0f));
        }
    }

    public void SetConVarValue(int id, string value)
    {
        if (!s_Initialized || !OwnShimsAttached) return;

        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
        {
            OwnShims.Console.SetConVarValue(id, p);
        }
    }

    /// <summary>Reads the engine's current value. Prefer the cached ConVarBase accessors on hot paths.</summary>
    public string GetConVarValue(int id)
    {
        if (!s_Initialized || !OwnShimsAttached) return string.Empty;

        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];
        OwnShims.Console.GetConVarValue(id, buf, bufSize);
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
        => (s_Initialized && OwnShimsAttached) ? (OwnShims.Console.ConsoleSnapshot()) : 0;

    /// <summary>Copies one entry out of the last snapshot. False past the end.</summary>
    public bool ConsoleSnapshotEntry(int index, out NativeConsoleEntry entry)
    {
        entry = default;

        if (!s_Initialized || !OwnShimsAttached)
            return false;

        ConsoleEntry raw;

        if ((OwnShims.Console.ConsoleSnapshotEntry(index, &raw)) == 0)
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
        if (!s_Initialized || !OwnShimsAttached) return null;

        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* pName = nameBytes)
        {
            if ((OwnShims.Console.FindConVarValue(pName, buf, bufSize)) == 0)
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

        return s_Initialized && OwnShimsAttached && (OwnShims.Entities.EntityIsValid(h.Value)) != 0;
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
        => s_Initialized && OwnShimsAttached && (OwnShims.Entities.EntityIsValid(h.Value)) != 0;

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

        return (s_Initialized && OwnShimsAttached) ? (OwnShims.Entities.EntityGetIndex(h.Value)) : -1;
    }

    public Vector EntityGetOrigin(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Origin;

        if (!s_Initialized || !OwnShimsAttached) return default;

        float* xyz = stackalloc float[3];
        OwnShims.Entities.EntityGetOrigin(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
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
        if (!s_Initialized || !OwnShimsAttached)
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

                return (OwnShims.Effects.TempEntRegister(&desc));
            }
        }
        finally
        {
            foreach (IntPtr p in owned)
                Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>
    /// <c>GetCurrentGravity()</c>. A slot rather than a constant because
    /// <c>sv_gravity</c> is a convar: a tempent falling at 600 while the world
    /// falls at 800 looks almost right, which is the worst kind of wrong for
    /// something nobody watches closely.
    /// </summary>
    public unsafe float CurrentGravity()
        => (!s_Initialized || !OwnShimsAttached) ? 600f : (OwnShims.Movement.CurrentGravity());

    //-------------------------------------------------------------------------
    // Game events.
    //-------------------------------------------------------------------------

    public void ListenForGameEvent(string name)
    {
        if (!s_Initialized || !OwnShimsAttached) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { OwnShims.Events.ListenForGameEvent(p); }
    }

    public int EventGetInt(IntPtr handle, string key)
    {
        if (!s_Initialized || !OwnShimsAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (OwnShims.Events.EventGetInt(handle, p)); }
    }

    public float EventGetFloat(IntPtr handle, string key)
    {
        if (!s_Initialized || !OwnShimsAttached) return 0f;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (OwnShims.Events.EventGetFloat(handle, p)); }
    }

    public bool EventGetBool(IntPtr handle, string key)
    {
        if (!s_Initialized || !OwnShimsAttached) return false;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return (OwnShims.Events.EventGetBool(handle, p)) != 0; }
    }

    public string EventGetString(IntPtr handle, string key)
    {
        if (!s_Initialized || !OwnShimsAttached) return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return FromUtf8((OwnShims.Events.EventGetString(handle, p))); }
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

            return (s_Initialized && OwnShimsAttached) ? (OwnShims.EngineBase.GetCurrentTime()) : 0f;
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

    /// <summary>
    /// Hands the movement parameters to the native loop. Called once per side
    /// at startup — see <see cref="MovementParams"/> for why both.
    /// </summary>
    public void RegisterMovementParams(MovementParams parameters)
    {
        if (!s_Initialized || !OwnShimsAttached) return;

        OwnShims.Movement.RegisterMovementParams(&parameters);
    }

    /// <summary>
    /// The collision matrix. Registered from both sides; answers whether this
    /// side's native received it, so a side that cannot serve the slot says so
    /// instead of keeping native's defaults in silence (which the client did
    /// until the slot moved into the shared IShimCollision base).
    /// </summary>
    public bool RegisterCollisionRules(uint[] rows)
    {
        if (!s_Initialized || !OwnShimsAttached) return false;

        var blob = default(CollisionRulesBlob);
        blob.SetRows(rows);

        OwnShims.Collision.RegisterCollisionRules(&blob);
        return true;
    }

    /// <summary>Facing, as pitch/yaw/roll.</summary>
    public Vector EntityGetAngles(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.Angles;

        if (!s_Initialized || !OwnShimsAttached) return default;

        float* pyr = stackalloc float[3];
        OwnShims.Entities.EntityGetAngles(h.Value, pyr);
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
        => (s_Initialized && OwnShimsAttached)
            ? (OwnShims.Network.NetHierarchyDeclare(blobBytes)) : 0;

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
        => (s_Initialized && OwnShimsAttached)
            ? (OwnShims.Network.WorldNetDeclare(blobBytes)) : 0;

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

        if (!s_Initialized || !OwnShimsAttached)
            return -1;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* p = bytes)
        fixed (uint* pId = &nativeTypeId)
        {
            return (OwnShims.Network.NetPublishType(p, shell, schemaHash, layoutBytes, pId));
        }
    }

    /// <summary>
    /// One tier as native registered it, or null past the end of the list. See
    /// NetTierInfo in sourcesharp.h — this is what <see cref="NetTiers"/> reads
    /// instead of projecting the boundary rule a second time.
    /// </summary>
    public NetTierInfo? NetTierAt(int shell, int tier)
    {
        if (!s_Initialized || !OwnShimsAttached)
            return null;

        byte* table = null;
        int bytes = 0, words = 0, props = 0, live = 0;

        if ((OwnShims.Network.NetTierInfo(shell, tier, &table, &bytes, &words, &props, &live)) == 0)
            return null;

        return new NetTierInfo(tier, FromUtf8(table), bytes, words, props, live != 0);
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
    /// The server's managed physics init (EntityPhysicsCreation), which
    /// <see cref="EntityInitPhysics"/> and <see cref="EntitySetPhysicsStatic"/>
    /// reach. Set by the server composition once vphysics answers; null makes
    /// nothing. INTERNAL, like <see cref="Teleported"/>.
    /// </summary>
    internal IEntityPhysicsRoute? PhysicsInit { get; set; }

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
        if (!s_Initialized || !OwnShimsAttached) return;

        var nameBytes  = Encoding.UTF8.GetBytes(name + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* n = nameBytes)
        fixed (byte* v = valueBytes)
        {
            OwnShims.Console.SetNamedConVarValue(n, v);
        }
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
        if (s_Initialized && OwnShimsAttached)
            OwnShims.Models.StandaloneSetModelScale(entity.Value, scale);
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
        if (!s_Initialized || !OwnShimsAttached)
            return string.Empty;

        byte* buffer = stackalloc byte[256];
        OwnShims.Models.EntityGetModelName(entity.Value, buffer, 256);

        return FromUtf8(buffer);
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
        if (!s_Initialized || !OwnShimsAttached) return;

        SidePatchEmit(patch, volume01, pitch, flags, startTime, emitCaption);
    }

    /// <summary>
    /// Stop one sound patch, if it is playing. Idempotent, because the shim
    /// tracks whether it ever started — stopping a sound that never played is a
    /// warning from the engine and a real one.
    /// </summary>
    public void SoundPatchStop(int patch)
    {
        if (!s_Initialized || !OwnShimsAttached) return;

        SidePatchStop(patch);
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

        if (!s_Initialized || !OwnShimsAttached) return;

        SidePatchState(patch, out entIndex, out filterActive);
    }

    /// <summary>
    /// Free the native patch. The one direction <c>SoundDestroy</c> does not
    /// cover: a queued DESTROY command comes due inside the managed frame pass,
    /// so the decision is managed and the delete is not.
    /// </summary>
    public void SoundPatchFree(int patch)
    {
        if (!s_Initialized || !OwnShimsAttached) return;

        SidePatchFree(patch);
    }


    /// <summary>
    /// A sound patch on the entity, heard from where it stands
    /// (SoundCreate with a CPASAttenuationFilter): the handle the managed
    /// sound controller tracks it by, or 0. Played, ramped and destroyed
    /// through the controller like any other patch.
    /// </summary>
    public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
    {
        if (!s_Initialized || !OwnShimsAttached || soundName.Length == 0) return 0;

        return SidePatchCreate(entity, channel, soundName, soundLevel);
    }

    // Each side's own patch arm (plan_server_native_v2 T4a): the server's
    // patches are the managed SoundEmitter's, the client's IShimSoundscapeClient's.
    private partial void SidePatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption);
    private partial void SidePatchStop(int patch);
    private partial void SidePatchState(int patch, out int entIndex, out bool filterActive);
    private partial void SidePatchFree(int patch);
    private partial int SidePatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel);



    // ---- plan_gamedll.md Part 4, Phase 6: LEVELINIT'S NINE NATIVE STEPS ----
    //
    // Nine wrappers and no tenth that runs them in order. The order is
    // ServerGameDll.MapLoadNewGame's, in C#, where it can be read and tested;
    // a Engine.LevelInit() here would be the same C++ sequence one language
    // further up.

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
        if (!s_Initialized || !OwnShimsAttached) return 0;
        return (OwnShims.Models.EntitiesInvalidateMdlCacheAll());
    }



    // ---- plan_gamedll_after.md Phase A3 ------------------------------------




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
