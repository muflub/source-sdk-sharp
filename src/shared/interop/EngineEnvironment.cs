namespace SourceSharp.Abi;

/// <summary>
/// The environment as the shell supplies it: a pointer to the engine's globals,
/// and a function that resolves an interface name.
///
/// <para>
/// <b>Both are handed in at construction and neither is cached.</b>
/// <see cref="Globals"/> rebuilds its eight-byte reader over the same pointer
/// each time it is asked, so nothing here holds a copy of anything the engine
/// writes. That is what the "live, not a snapshot" claim rests on, and it is a
/// fact rather than this paragraph.
/// </para>
///
/// <para>
/// <b>An engine AREA, like the other twenty-four, and that is not an accident
/// of the name.</b> EngineServices pairs <c>IEngineFoo</c> with <c>EngineFoo</c>
/// by reflection, so this interface enrols itself; and it should, because the
/// factory and the globals are exactly "a part of the engine a class touches".
/// That means the binding is <c>internal</c> — a class holding
/// <c>EngineEnvironment</c> rather than <see cref="IEngineEnvironment"/> would
/// compile, work, and be untestable — and it means <see cref="RecordingEngine"/>
/// answers for it in tests like it answers for every other area.
/// </para>
///
/// <para>
/// <b>The globals ARE fed now; the resolver still is not.</b>
/// plan_crossings.md Phase X1 added the <c>GetGlobalVarsPtr</c> ABI slot, so
/// the environment EngineServices registers reads the engine's live
/// <c>CGlobalVars</c> and <c>Globals.IsAvailable</c> is true in a live session.
/// <see cref="IEngineEnvironment.GetInterface"/> still answers zero: there is
/// no slot for the engine's <c>CreateInterfaceFn</c>, nothing in managed code
/// asks for one yet, and inventing a second unfed half to keep the two
/// symmetrical would be worse than saying which half is fed.
/// </para>
///
/// <para>
/// <b>The registered one takes the pointer from <c>Engine</c> rather
/// than holding one.</b> EngineServices builds every area binding with
/// <c>Activator.CreateInstance</c> — the parameterless constructor — and this
/// class is loaded once in the default ALC for BOTH game DLLs. An instance
/// that captured one side's pointer would answer the other side with it, and a
/// server reading the client's <c>curtime</c> is a plausible number rather than
/// a crash. So the default environment asks <c>Engine</c> for the
/// pointer belonging to the side whose call is running, which is the same
/// thread-local dispatch the engine API table itself is under. The
/// pointer-taking constructor stays for tests, which have no engine to ask.
/// </para>
/// </summary>
internal sealed class EngineEnvironment : IEngineEnvironment
{
    /// <summary>
    /// The engine's <c>CreateInterfaceFn</c>, as a managed callable. Null means
    /// the shell has not handed one across, and every lookup answers zero.
    /// </summary>
    private readonly Func<string, nint>? _resolve;

    /// <summary>
    /// The address of the engine's <c>CGlobalVars</c>, for an environment that
    /// was handed one. Held as a bare pointer value rather than as a
    /// <see cref="Abi.Globals"/> so that the reader is built fresh on every
    /// read — see the type summary.
    /// </summary>
    private readonly nint _globals;

    /// <summary>
    /// The side whose own globals this environment reads, live; null for one a
    /// test handed a pointer to, which reads <see cref="_globals"/> instead.
    /// </summary>
    private readonly EngineSide? _side;

    /// <summary>
    /// The environment as the game gets it: its own side's globals, read live
    /// (plan_abi_hotreload D8). Never the executing side's: two instances in
    /// one process each read their own.
    /// </summary>
    public EngineEnvironment(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _resolve = null;
        _globals = 0;
    }

    internal EngineEnvironment(Func<string, nint>? resolve, nint globals)
    {
        _resolve = resolve;
        _globals = globals;
    }

    /// <inheritdoc/>
    public nint GetInterface(string name)
    {
        if (_resolve is null || string.IsNullOrEmpty(name))
            return 0;

        return _resolve(name);
    }

    /// <inheritdoc/>
    public Globals Globals => new(_side is not null ? _side.GlobalVarsPointer : _globals);
}
