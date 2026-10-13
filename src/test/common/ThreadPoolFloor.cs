using System.Runtime.CompilerServices;

namespace SourceSharp.Tests;

/// <summary>
/// A thread-pool floor for the test assembly, set before any test runs.
///
/// <para>
/// xUnit runs up to <c>MaxParallelThreads</c> (16, AssemblyInfo.cs) test
/// classes at once, and many facts wait by blocking a thread (a
/// <c>Thread.Sleep</c> poll for a socket, a process or a frame). The pool
/// starts with one worker per core and injects more slowly, so on a 4-core CI
/// runner sixteen blocked tests left the async socket work they were waiting
/// for (DebugChannelClient's dial loop, the listener's sessions) with no thread
/// to run on: ClientProxyRouteTests waited 10 s for an attach that had nothing
/// to complete it. The floor is the parallel limit plus headroom for that
/// work, whatever the core count; on the 32-core box it changes nothing.
/// </para>
/// </summary>
internal static class ThreadPoolFloor
{
    private const int Floor = 64;

    [ModuleInitializer]
    internal static void Raise()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(Math.Max(workers, Floor), Math.Max(io, Floor));
    }
}
