namespace SourceSharp.Abi;

/// <summary>
/// What the engine handed the shell, asked for by name — the abstract
/// environment of plan_gamedll.md Part 0a #1.
///
/// <para>
/// <b>The inversion is the point, not the two methods.</b> Today every shell
/// file fetches its own engine interfaces and <c>CSourceSharpSystem::Init</c>
/// fetches another twenty. With the shell owning <c>DLLInit</c> it owns the
/// factory, and this is the first moment the port can have <i>one</i> place
/// where an engine interface is acquired. A class that wants one asks this
/// rather than reaching for a global.
/// </para>
/// </summary>
public interface IEngineEnvironment
{
    /// <summary>
    /// One engine interface by its versioned name — <c>"VEngineServer023"</c>
    /// and the like — as the <c>CreateInterfaceFn</c> the engine handed
    /// <c>DLLInit</c> answers it.
    /// </summary>
    /// <returns>
    /// Zero when the engine has no such interface. NOT an exception: a missing
    /// interface is a normal answer here — half the tree's acquisitions are
    /// optional, and a caller that must have one says so by checking.
    /// </returns>
    nint GetInterface(string name);

    /// <summary>
    /// The engine's <c>CGlobalVars</c>, read LIVE. See <see cref="Abi.Globals"/>:
    /// it is a pointer, not a copy, because a snapshot taken when the shell
    /// started would be one frame stale everywhere and wrong in a way nothing
    /// reports.
    /// </summary>
    Globals Globals { get; }
}
