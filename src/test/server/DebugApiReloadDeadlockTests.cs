using System.Diagnostics;

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dev API's shutdown, and the deadlock that leaked a load context on every
/// hot reload.
///
/// <para>
/// <b>THE MEASUREMENT.</b> Two live <c>tools/live-gate</c> runs, six reloads
/// between them, and every one printed these two lines in this order:
/// </para>
///
/// <code>
/// [SourceSharp/DebugApi] the accept loop did not stop; this side's load context
///   cannot unload and a reload will leak it
/// [SourceSharp] ServerALC did NOT unload -- something still references it.
///   Reloading will leak.
/// </code>
///
/// <para>
/// <b>THE SHAPE.</b> <c>OnGameThread</c> queues work and blocks the LISTENER
/// thread until the GAME thread drains it in <c>Pump()</c>. A managed reload
/// runs on the game thread and its first act is <c>Dispose()</c>, which closes
/// the listener and JOINS its accept thread with a one-second budget. A request
/// in flight when the reload starts makes the two threads wait for each other:
/// the listener is inside a five-second wait for a pump that will never come,
/// and the game thread is inside the join. The join loses by four seconds, the
/// accept thread is still on the stack, and a live thread inside the assembly
/// is exactly what stops a collectible load context unloading.
/// </para>
///
/// <para>
/// <b>WHAT IS AND IS NOT CHECKED HERE.</b> Reproducing the deadlock needs a
/// game thread and a socket. What needs neither, and is the whole of the fix,
/// is that a request arriving after the shutdown has begun answers AT ONCE with
/// the body it would have got four seconds later, instead of waiting.
/// </para>
/// </summary>
public class DebugApiReloadDeadlockTests
{
    private const string Timeout = "{\"error\":\"the game is not ticking\"}";

    /// <summary>Before the shutdown, a queued request really does wait.</summary>
    [Fact]
    public void ARequestWaitsForTheGameThreadWhileTheServerIsRunning()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());

        Assert.False(server.IsShuttingDown);

        var clock = Stopwatch.StartNew();

        // Nothing will pump it, so this is the timeout path -- and the point is
        // that it TOOK the timeout rather than answering immediately, which is
        // what makes the fact below mean something.
        string answer = server.RunOnGameThreadForTest(
            () => "{}", Timeout, TimeSpan.FromMilliseconds(100));

        clock.Stop();

        Assert.Equal(Timeout, answer);
        Assert.True(clock.ElapsedMilliseconds >= 80,
                    $"a running server waits for the pump; this took {clock.ElapsedMilliseconds}ms");
    }

    /// <summary>Dispose says so.</summary>
    [Fact]
    public void DisposeMarksTheServerAsShuttingDown()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());
        server.Dispose();

        Assert.True(server.IsShuttingDown);
    }

    /// <summary>
    /// And after it, a request answers AT ONCE. This is the fact that breaks
    /// the deadlock: the listener thread goes back to its accept loop in time
    /// for the join, so the accept thread comes off the stack and the load
    /// context can unload.
    /// </summary>
    [Fact]
    public void ARequestMadeDuringShutdownAnswersAtOnceInsteadOfWaiting()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());
        server.Dispose();

        var clock = Stopwatch.StartNew();

        string answer = server.RunOnGameThreadForTest(
            () => "{}", Timeout, TimeSpan.FromSeconds(5));

        clock.Stop();

        Assert.Equal(Timeout, answer);
        Assert.True(clock.ElapsedMilliseconds < 250,
                    $"a shutting-down server must not wait for a pump that will never "
                  + $"come; this took {clock.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// AND THE REQUEST THAT WAS ALREADY WAITING IS WOKEN — which is the live
    /// case, and the one the two facts above cannot reach.
    ///
    /// <para>
    /// In the game it is always this shape: the listener thread is already
    /// inside its five-second wait when the reload calls <c>Dispose</c> on the
    /// game thread. A guard that only refused NEW requests would leave that one
    /// waiting the full five seconds, the join would still lose, and the load
    /// context would still leak — with every test above green. Found by
    /// mutation: removing the cancellation token from the wait left all four of
    /// the other facts passing.
    /// </para>
    /// </summary>
    [Fact]
    public void ARequestAlreadyWaitingIsWokenByDispose()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());

        var entered = new ManualResetEventSlim(false);
        string answer = "not set";
        long elapsedMs = -1;

        var caller = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            entered.Set();
            answer = server.RunOnGameThreadForTest(
                () => "{}", Timeout, TimeSpan.FromSeconds(30));
            clock.Stop();
            elapsedMs = clock.ElapsedMilliseconds;
        })
        { IsBackground = true };

        caller.Start();

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the caller thread started");

        // Give it a moment to be genuinely inside the wait rather than merely
        // past the flag, so this is the in-flight case and not the guard above.
        Thread.Sleep(100);

        server.Dispose();

        Assert.True(caller.Join(TimeSpan.FromSeconds(5)),
                    "the waiting request came back; without the cancellation it "
                  + "would sit for the full 30s and the accept thread would still "
                  + "be on the stack when the join gave up");

        Assert.Equal(Timeout, answer);
        Assert.True(elapsedMs < 5000,
                    $"it was woken rather than timing out; it took {elapsedMs}ms of 30000");
    }

    /// <summary>
    /// The work is not run either. A queued action that ran after the module
    /// said goodbye would be the same class of fault one layer down.
    /// </summary>
    [Fact]
    public void TheWorkOfARequestMadeDuringShutdownIsNeverRun()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());
        server.Dispose();

        bool ran = false;

        server.RunOnGameThreadForTest(
            () => { ran = true; return "{}"; }, Timeout, TimeSpan.FromMilliseconds(50));

        server.Pump();

        Assert.False(ran);
    }
}
