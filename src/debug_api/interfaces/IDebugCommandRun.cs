namespace Debug.Api.Server.Interfaces;

/// <summary>
/// Runs a managed console command for the dev API's capture: a FRESH instance
/// of the declared class, built from the real container with its log and
/// engine console replaced by a sink, so the capture answers with exactly what
/// that run printed. All managed logic, no native code (user ruling
/// 2026-10-07): nothing here touches native code, and the answer is assembled
/// in-process rather than read back out of the console file.
/// </summary>
public interface IDebugCommandRun
{
    /// <summary>
    /// Builds and runs one fresh instance of the command <paramref name="name"/>
    /// names, and answers the lines it printed, in order, through its log and
    /// the engine console alike.
    ///
    /// <para>
    /// Null means this name is NOT owned managedly and the caller must use the
    /// engine path: an unregistered name, a native-bodied carrier (whose output
    /// is native code a sink would never see), or a declared class this build's
    /// container cannot build. Null answers must be cheap and side-effect-free,
    /// because the caller runs them before deciding which path to take.
    /// </para>
    ///
    /// <para>
    /// GAME THREAD ONLY. A command's body reads and writes live engine state,
    /// so the server calls this from inside its game-thread hand-off, never
    /// from the API's own thread.
    /// </para>
    /// </summary>
    /// <param name="name">The console name, matched case-insensitively.</param>
    /// <param name="args">
    /// The raw remainder of the command line after the name, handed to the
    /// instance verbatim — the same string the console would have passed.
    /// </param>
    IReadOnlyList<string>? TryRunManaged(string name, string args = "");
}
