namespace SourceSharp.Abi;

/// <summary>
/// The engine a side's module is composed over: one side's engine API, handed
/// to the module by whoever creates it (plan_abi_hotreload D8).
///
/// <para>
/// <b>Why it exists.</b> A module used to name its side
/// (<c>EngineServices.AddTo(services, Side.Server)</c>) and the static
/// <c>Engine</c> looked the side's engine up. That made "the server's engine"
/// one process-wide object, so two game instances in one process composed over
/// the same one. Now the module is GIVEN its engine: the side's own entry passes
/// the side it loaded, and a fact that composes two modules passes two engines,
/// and nothing either composes can reach the other's.
/// </para>
///
/// <para>
/// <b>Opaque on purpose.</b> The engine API behind it is internal, so game
/// code holding one of these can do exactly what composing a module needs —
/// register the engine areas, attach its liveness mirror, take its clock and
/// its entity factory table — and cannot call the engine past the area
/// interfaces.
/// </para>
/// </summary>
public sealed class ModuleEngine
{
    internal ModuleEngine(EngineSide api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Api = api;
    }

    /// <summary>The side's engine API this handle stands for.</summary>
    internal EngineSide Api { get; }

    /// <summary>Which side this engine is.</summary>
    public Side Side => Api.Side;

    /// <summary>
    /// This side's log, over its own console (statics to zero: it was the
    /// static Log). The composition registers it and hands it to what it builds.
    /// </summary>
    public ILog Log => Api.Log;

    /// <summary>
    /// This side's inbound crossing counters (statics to zero): its
    /// <see cref="SideState"/>'s, which outlive the module across reloads.
    /// The composition hands them to every binding that counts a crossing.
    /// </summary>
    public Crossings Crossings => Api.State.Crossings;

    /// <summary>This side's build stamps (statics to zero), its <see cref="SideState"/>'s.</summary>
    public BuildInfo Build => Api.State.Build;

    /// <summary>
    /// This side's reload store (statics to zero), its <see cref="SideState"/>'s:
    /// what a reload carries from the old module to the new one.
    /// </summary>
    public ReloadStore Reload => Api.State.Reload;

    /// <summary>
    /// Makes <paramref name="log"/> the console the side's own plumbing (its
    /// load, its registry's fault reports, its crash handlers) prints through
    /// until the returned attachment is disposed, which puts back what was
    /// there. The module attaches its composition's log at composition and
    /// disposes in Shutdown (plan_abi_hotreload D5; statics to zero: this was
    /// the static Log.Attach).
    /// </summary>
    public IDisposable AttachLog(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        SideState state = Api.State;
        ILog? previous = state.Log;
        state.Log = log;
        return new LogAttachment(state, log, previous);
    }

    private sealed class LogAttachment(SideState state, ILog log, ILog? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && ReferenceEquals(state.Log, log))
                state.Log = previous;
        }
    }

    /// <summary>
    /// Registers every engine area, bound to this engine, and this side's three
    /// routes up into its registry. Returns how many areas.
    /// </summary>
    public int AddTo(ServiceContainer services) => EngineServices.AddTo(services, Api);

    /// <summary>
    /// Has this engine's <c>EntityIsValid</c> answer from the composition's
    /// <paramref name="mirror"/> (plan_abi_hotreload D7) until the returned
    /// attachment is disposed. The module attaches at composition and disposes
    /// in Shutdown, as it does its log sink.
    /// </summary>
    public IDisposable AttachLiveness(LivenessMirror mirror) => Api.AttachLiveness(mirror);

    /// <summary>
    /// This engine's model descriptors (plan_abi_hotreload D8): the side's own
    /// cache, which its render binding reads. The module registers it for what
    /// else asks a model's table and clears it at level shutdown.
    /// </summary>
    public ModelDescriptors Models => Api.Models;

    /// <summary>
    /// This side's vphysics forwarder tables (plan_engine_forwarders.md): what a
    /// vphysics service is built over, so it calls objects through their
    /// forwarders rather than reading their vptrs. Bound on first use.
    /// </summary>
    public VPhysicsTables VPhysics => Api.VPhysics;

    /// <summary>This engine's break-piece lists, cleared with <see cref="Models"/>.</summary>
    public BreakModels Breaks => Api.Breaks;

    /// <summary>This engine's game clock (plan_engineapi_vtables B5).</summary>
    public IGameClock Clock() => new EngineClock(Api);

    /// <summary>
    /// This engine's model cache, read as bytes (plan physics_bones_native_zero
    /// step 1.2): what the managed skeleton builds its studio headers from.
    /// </summary>
    public IStudioMemory StudioMemory() => new EngineStudioMemory(Api);

    /// <summary>
    /// This engine's entity factory table, its providers bound to the native
    /// dictionary when the side has one (the server, with its shims attached)
    /// and unbound otherwise. A new instance per call: the server's composition
    /// asks once and registers it (plan_abi_hotreload D7).
    /// </summary>
    public EntityFactories EntityFactories() => Api.BindEntityFactories();
}
