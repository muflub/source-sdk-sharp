using System.Collections.Concurrent;
using System.Diagnostics;

namespace SourceSharp.Ai;

/// <summary>
/// The AI's worker threads (plan_hl2mp_ai §2.7, ruling 2026-11-01: one or more
/// threads, never the main game thread). Work is posted from the main thread
/// without waiting and runs on whichever worker is free; what keeps the result
/// independent of the count is the work's own shape (a bot's steps run one
/// after another in its lane, nothing mutable is shared between brains). The
/// pool runs from <see cref="Start"/> to <see cref="Stop"/>, which joins every
/// thread, so a level's teardown leaves the process's thread count where it was.
/// </summary>
public sealed class AiWorkerPool(string name = "ai-worker") : IDisposable
{
    private readonly ConcurrentQueue<IAiWork> _queue = new();
    private readonly SemaphoreSlim _ready = new(0);
    private readonly List<Thread> _threads = new();
    private volatile bool _running;
    private long _busyTicks;
    private long _busyCpuNanos;
    private long _ran;
    private long _faults;
    private string? _lastFault;

    /// <summary>The default thread count: <c>min(4, processors − 1)</c>, at least one (§2.7's <c>ai_threads</c>).</summary>
    public static int DefaultThreads => Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));

    /// <summary>The threads the pool runs while started.</summary>
    public int Threads { get; private set; }

    /// <summary>Whether the workers run.</summary>
    public bool Running => _running;

    /// <summary>How many workers are alive now (0 once stopped and joined).</summary>
    public int Alive
    {
        get
        {
            lock (_threads)
                return _threads.Count(t => t.IsAlive);
        }
    }

    /// <summary>Work run so far.</summary>
    public long Ran => Interlocked.Read(ref _ran);

    /// <summary>Wall time the workers spent running work, in microseconds (<c>ai.pool.us</c>'s numerator).</summary>
    public double BusyMicros => Interlocked.Read(ref _busyTicks) * 1e6 / Stopwatch.Frequency;

    /// <summary>
    /// The calling thread's CPU clock in microseconds, or null: with one a
    /// worker's work is charged what it ran, not the time it waited for a core.
    /// </summary>
    public Func<double>? CpuClock { get; init; }

    /// <summary>
    /// CPU time the workers spent running work, in microseconds
    /// (<c>ai.pool.cpu.us</c>'s numerator); the wall time with no clock.
    /// </summary>
    public double BusyCpuMicros => CpuClock is null ? BusyMicros : Interlocked.Read(ref _busyCpuNanos) / 1e3;

    /// <summary>Work that threw; the worker goes on.</summary>
    public long Faults => Interlocked.Read(ref _faults);

    /// <summary>The last fault's message, or null.</summary>
    public string? LastFault => Volatile.Read(ref _lastFault);

    /// <summary>Work waiting for a worker.</summary>
    public int Queued => _queue.Count;

    /// <summary>Starts <paramref name="threads"/> workers (at least one). A started pool is stopped first.</summary>
    public void Start(int threads)
    {
        Stop();
        Threads = Math.Max(1, threads);
        _running = true;
        lock (_threads)
        {
            for (int i = 0; i < Threads; i++)
            {
                var t = new Thread(Work) { IsBackground = true, Name = $"{name}-{i}" };
                _threads.Add(t);
                t.Start();
            }
        }
    }

    /// <summary>
    /// Stops the workers and joins them: work still queued is discarded
    /// (answered by <see cref="Stop"/>'s count), work running finishes first.
    /// </summary>
    public int Stop()
    {
        Thread[] threads;
        lock (_threads)
        {
            threads = [.. _threads];
            _threads.Clear();
        }

        if (threads.Length == 0)
            return 0;
        _running = false;
        _ready.Release(threads.Length);
        foreach (Thread t in threads)
            t.Join();

        int discarded = 0;
        while (_queue.TryDequeue(out _))
            discarded++;
        while (_ready.CurrentCount > 0)
            _ready.Wait(0);
        return discarded;
    }

    /// <summary>Queues <paramref name="work"/> for a worker; false (nothing queued) when the pool is not running.</summary>
    public bool Post(IAiWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!_running)
            return false;
        _queue.Enqueue(work);
        _ready.Release();
        return true;
    }

    private void Work()
    {
        while (true)
        {
            _ready.Wait();
            if (!_running)
                return;
            if (!_queue.TryDequeue(out IAiWork? work))
                continue;
            long start = Stopwatch.GetTimestamp();
            double cpuStart = CpuClock?.Invoke() ?? 0d;
            try
            {
                work.Run();
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _faults);
                Volatile.Write(ref _lastFault, $"{work.GetType().Name}: {e.GetType().Name}: {e.Message}");
            }

            Interlocked.Add(ref _busyTicks, Stopwatch.GetTimestamp() - start);
            if (CpuClock is { } cpu)
                Interlocked.Add(ref _busyCpuNanos, (long)((cpu() - cpuStart) * 1e3));
            Interlocked.Increment(ref _ran);
        }
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
