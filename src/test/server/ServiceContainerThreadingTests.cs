using System.Collections.Concurrent;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A container's constructor-plan cache, filled from several threads at once.
///
/// <para>
/// Three containers are static and shared by every registry that was handed
/// none, so tests running in parallel fill one cache from many threads. An
/// unguarded Dictionary written that way corrupts itself; the parallel suite
/// found it as "Operations that change non-concurrent collections must have
/// exclusive access" out of <c>ServiceContainer.ActivatorFor</c>.
/// </para>
/// </summary>
public class ServiceContainerThreadingTests
{
    /// <summary>A type per type argument, each needing its own plan.</summary>
    public sealed class Probe<T>
    {
    }

    private static Type[] ManyProbeTypes() =>
        typeof(object).Assembly.GetExportedTypes()
            .Where(t => !t.IsGenericTypeDefinition && !t.ContainsGenericParameters
                     && !t.IsByRefLike && !t.IsPointer && !t.IsByRef && t != typeof(void))
            .Take(600)
            .Select(t => typeof(Probe<>).MakeGenericType(t))
            .ToArray();

    [Fact]
    public void ThreadsCreatingThroughOneContainerEachGetAnInstanceAndNothingThrows()
    {
        Type[] types = ManyProbeTypes();
        const int threads = 8;
        const int rounds = 30;

        var failures = new ConcurrentBag<Exception>();
        int made = 0;

        // Many fresh containers rather than one: the damage is done while the
        // cache grows, and it only grows while it is new.
        for (int round = 0; round < rounds && failures.IsEmpty; round++)
            made += Race(new ServiceContainer(), types, threads, failures);

        Assert.True(types.Length >= 300, $"enough types to contend on: {types.Length}");
        Assert.True(failures.IsEmpty,
            $"{failures.Count} thread(s) threw: {failures.FirstOrDefault()?.GetType().Name}: " +
            $"{failures.FirstOrDefault()?.Message}");
        Assert.Equal(rounds * threads * types.Length, made);
    }

    /// <summary>Every thread creates every type once; how many came back right.</summary>
    private static int Race(ServiceContainer container, Type[] types, int threads,
                            ConcurrentBag<Exception> failures)
    {
        var start = new Barrier(threads);
        int made = 0;

        var workers = Enumerable.Range(0, threads).Select(k => new Thread(() =>
        {
            start.SignalAndWait();

            try
            {
                // Each thread walks the types from a different offset, so the
                // first request for a type lands on different threads.
                for (int i = 0; i < types.Length; i++)
                {
                    var type = types[(i + k * types.Length / threads) % types.Length];

                    if (container.Create(type).GetType() == type)
                        Interlocked.Increment(ref made);
                }
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        })).ToArray();

        foreach (var w in workers) w.Start();
        foreach (var w in workers) w.Join();

        return made;
    }
}
