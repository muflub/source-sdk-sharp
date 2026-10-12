using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A dev API server takes down only the log sink it put up.
///
/// <para>
/// Dispose used to set <see cref="Log.Sink"/> to null whatever was there. In
/// the game that is the same thing, the sink being the server's own; in a
/// parallel test run it took away every other running test's log capture
/// whenever any test disposed a server it had never started.
/// </para>
/// </summary>
public class DebugApiLogSinkTests
{
    [Fact]
    public void DisposingAServerThatNeverStartedLeavesTheSinkInPlace()
    {
        var said = new List<string>();
        var lines = new ConsoleLines { Tap = (text, _) => said.Add(text) };

        DebugApiServer.ForTest(TestDebugHooks.Server(lines: lines)).Dispose();

        lines.Line("after the dispose\n", false);

        Assert.Contains("after the dispose\n", said);
    }
}
