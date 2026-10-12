using SourceSharp.Abi;
using Debug.Api;

namespace SourceSharp.Tests;

/// <summary>
/// A dev API server nothing pumps, for a test that reaches a game-thread wait on
/// purpose or in passing.
///
/// <para>
/// THE TIMEOUT IS STILL REAL, only shorter. Nothing drains the queue, so every
/// game-thread route and the status probe wait out their timeout and answer
/// what a wedged game answers. The game waits five seconds for a route and half
/// a second for status; a test that waited as long spent twenty seconds of the
/// suite watching timers run down. The defaults themselves are untouched and
/// only this server is shortened.
/// </para>
/// </summary>
internal static class NeverPumped
{
    /// <summary>How long a game-thread wait lasts on <see cref="Server"/>.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(20);

    public static DebugApiServer Server(Abi.Crossings? crossings = null, BuildInfo? build = null)
        => Server(TestDebugHooks.Server(crossings: crossings, build: build));

    public static DebugApiServer Server(SourceSharp.Server.Interface.IDebugServerHooks hooks)
        => DebugApiServer.ForTest(hooks, Wait);
}
