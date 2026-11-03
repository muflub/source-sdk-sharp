using Xunit;

namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// The AI's worker pool (plan_hl2mp_ai §2.7, §2.5): work runs on its threads,
/// never the caller's; stopping joins them, so the process's thread count
/// comes back to where it was; a stopped pool takes nothing.
/// </summary>
public sealed class AiWorkerPoolTests
{
    private sealed class Note(ManualResetEventSlim done) : IAiWork
    {
        public int Thread { get; private set; } = -1;

        public string? Name { get; private set; }

        public void Run()
        {
            Thread = Environment.CurrentManagedThreadId;
            Name = System.Threading.Thread.CurrentThread.Name;
            done.Set();
        }
    }

    private sealed class Throws : IAiWork
    {
        public void Run() => throw new InvalidOperationException("boom");
    }

    /// <summary>This process's threads named <paramref name="prefix"/>…, as the kernel lists them (other facts' pools run beside this one).</summary>
    private static int ProcessThreads(string prefix)
        => Directory.GetDirectories("/proc/self/task").Count(t =>
        {
            try
            {
                return File.ReadAllText(Path.Combine(t, "comm")).StartsWith(prefix, StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;   // a thread that ended while listed
            }
        });

    [Fact]
    public void WorkRunsOnAWorkerNotTheCaller()
    {
        using var pool = new AiWorkerPool();
        pool.Start(2);
        using var done = new ManualResetEventSlim();
        var work = new Note(done);
        Assert.True(pool.Post(work));
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "the work ran");
        Assert.NotEqual(Environment.CurrentManagedThreadId, work.Thread);
        Assert.StartsWith("ai-worker-", work.Name);
    }

    [Fact]
    public void StoppingJoinsEveryWorkerAndTheThreadCountComesBack()
    {
        if (!OperatingSystem.IsLinux())
            return;
        using var pool = new AiWorkerPool("ai-join-fact");
        Assert.Equal(0, ProcessThreads("ai-join-fact"));
        pool.Start(4);
        Assert.Equal(4, pool.Alive);
        Assert.Equal(4, ProcessThreads("ai-join-fact"));
        pool.Stop();
        Assert.Equal(0, pool.Alive);
        // Joined, the kernel lets the thread go a moment later.
        Assert.True(SpinWait.SpinUntil(() => ProcessThreads("ai-join-fact") == 0, TimeSpan.FromSeconds(5)), "the workers' threads are gone");
    }

    [Fact]
    public void AStoppedPoolTakesNothing()
    {
        using var pool = new AiWorkerPool();
        using var done = new ManualResetEventSlim();
        Assert.False(pool.Post(new Note(done)));
        pool.Start(1);
        pool.Stop();
        Assert.False(pool.Post(new Note(done)));
        Assert.False(done.IsSet);
    }

    [Fact]
    public void AFaultIsCountedAndTheWorkerGoesOn()
    {
        using var pool = new AiWorkerPool();
        pool.Start(1);
        using var done = new ManualResetEventSlim();
        Assert.True(pool.Post(new Throws()));
        Assert.True(pool.Post(new Note(done)));
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "the work after the fault ran on the same worker");
        Assert.Equal(1, pool.Faults);
        Assert.Contains("boom", pool.LastFault);
    }

    [Fact]
    public void TheDefaultIsAtMostFourAndAtLeastOne()
        => Assert.InRange(AiWorkerPool.DefaultThreads, 1, Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1)));
}
