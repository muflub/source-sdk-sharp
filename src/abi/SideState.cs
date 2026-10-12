namespace SourceSharp.Abi;

/// <summary>
/// What one side keeps for as long as its side root lives, across every load
/// and reload of its game context (statics to zero, plan_abi_hotreload §0):
/// the managed-built registry, the inbound crossing counters, and the rest of
/// what used to be a static in this assembly's copy.
///
/// <para>
/// <b>Why an instance.</b> Each side root loads its own copy of the Abi
/// (plan_side_isolation P5), so a static here was one per side. Two game
/// instances of the SAME side in one process would still have shared it.
/// Now the side's entry creates one of these on its first load, the
/// Bootstrap keeps it as an opaque handle (the one <c>pVtables</c> slot it
/// has always kept), and every later load is handed the same one. A fact
/// that composes two instances creates two.
/// </para>
/// </summary>
public sealed class SideState
{
    private ManagedVtables? _vtables;

    public SideState(Side side)
    {
        Side = side;
    }

    /// <summary>The side this state belongs to.</summary>
    public Side Side { get; }

    /// <summary>
    /// The side's registry of managed-built blocks, created on first use: a
    /// state composed for a fact with no native caller never pins a handle.
    /// </summary>
    public ManagedVtables Vtables => _vtables ??= new ManagedVtables(side: Side);

    /// <summary>The side's inbound crossing counters (cs_sv_crossings / cs_cl_crossings).</summary>
    public Crossings Crossings { get; } = new();

    /// <summary>
    /// The side's build stamps: the native half taken once for the process,
    /// the managed half on every load (the HUD, the dev API and IGameBuild).
    /// </summary>
    public BuildInfo Build { get; } = new();

    /// <summary>
    /// The connection id stamps (<c>CEventAction::s_iNextIDStamp</c>): restored
    /// connections keep theirs across a reload (lifetime ruling b), so the
    /// counter outlives every load of the side.
    /// </summary>
    public EventIdStamps Stamps { get; } = new();

    /// <summary>The bytes a side's reload carries from its old load to its new one.</summary>
    public ReloadStore Reload => _reload ??= new ReloadStore(Side, this);

    private ReloadStore? _reload;

    /// <summary>
    /// Whether this state's crash handlers are installed
    /// (<see cref="ManagedDiagnostics.Install"/>): once per state, so a
    /// reload does not add a second set.
    /// </summary>
    public bool CrashHandlersInstalled { get; internal set; }

    /// <summary>
    /// The side's console for its current load (statics to zero: it was the
    /// static Log's per-copy sink): the shim console each load acquires, set by
    /// that load. What the Abi's own plumbing logs through, and the registry's
    /// thunks report faults through. Null before the first load has one.
    /// </summary>
    public ILog? Log
    {
        get => _log;
        set
        {
            _log = value;
            Vtables.Log = value;
        }
    }

    private ILog? _log;
}
