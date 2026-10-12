namespace SourceSharp.Abi;

/// <summary>
/// One composition's console log (statics to zero: it replaced the static
/// <c>Log</c> and its per-copy sink slot). Each side's composition registers
/// its own, over its own engine console, and hands it to what it builds by
/// constructor, so two game instances of one side log separately.
///
/// <para>
/// Two methods, so that what logs names neither the engine nor its console
/// contract (plan_abi_hotreload D5). The Abi's own plumbing logs through the
/// <see cref="ShimConsole"/> the side's load acquires, held by its
/// <see cref="SideState"/>.
/// </para>
/// </summary>
public interface ILog
{
    /// <summary>Prints a line to the console.</summary>
    void Msg(string message);

    /// <summary>Prints a warning to the console.</summary>
    void Warning(string message);
}

/// <summary>
/// The process's standard error, as a log: what something with no console of
/// its own writes to (a load that failed before it had one, a fact with no
/// engine). An instance per use; it holds nothing.
/// </summary>
public sealed class StandardErrorLog : ILog
{
    public void Msg(string message) => Console.Error.Write(message);

    public void Warning(string message) => Console.Error.Write(message);
}
