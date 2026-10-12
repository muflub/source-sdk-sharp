using System.Collections.Concurrent;

namespace TF2.Descent.Server;

/// <summary>
/// The thread the host SDK lives on (plan_host_sdk §2.9), with a
/// SynchronizationContext of its own: every <c>await</c> started here resumes
/// here, whether the awaited task completed inside the SDK's Pump or on a
/// thread-pool thread (StartAsync's connect does). So every SDK call, and
/// every continuation after one, runs on this one thread.
/// </summary>
public sealed class SdkThread : IDisposable
{
    private readonly LoopContext _context;
    private readonly Thread _thread;
    private readonly Action _tick;
    private readonly TimeSpan _interval;
    private volatile bool _stopping;

    /// <param name="tick">Called on the thread every <paramref name="interval"/> and after each wake: the pump.</param>
    public SdkThread(string name, TimeSpan interval, Action tick)
    {
        _context = new LoopContext();
        _tick = tick;
        _interval = interval;
        _thread = new Thread(Run) { Name = name, IsBackground = true };
    }

    public bool IsCurrent => Thread.CurrentThread == _thread;

    public int ManagedThreadId => _thread.ManagedThreadId;

    public void Start() => _thread.Start();

    /// <summary>Runs <paramref name="work"/> on the SDK thread; safe from any thread.</summary>
    public void Post(Action work) => _context.Post(_ => work(), null);

    /// <summary>Starts <paramref name="work"/> on the SDK thread, under its context.</summary>
    public void Post(Func<Task> work) => _context.Post(_ => _ = work(), null);

    /// <summary>
    /// Runs <paramref name="last"/> on the thread, then stops the loop once it
    /// has COMPLETED (an async step awaits across passes, so stopping when the
    /// queue is merely empty would cut it short), and waits up to
    /// <paramref name="timeout"/>.
    /// </summary>
    /// <returns>False when the thread did not finish in time.</returns>
    public bool Stop(Func<Task>? last, TimeSpan timeout)
    {
        if (!_thread.IsAlive)
            return true;

        if (last is null)
        {
            _stopping = true;
            _context.Wake();
        }
        else
        {
            Post(async () =>
            {
                try
                {
                    await last();
                }
                finally
                {
                    _stopping = true;
                    _context.Wake();
                }
            });
        }

        return _thread.Join(timeout);
    }

    public void Dispose() => Stop(null, TimeSpan.FromSeconds(5));

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(_context);

        while (true)
        {
            _context.RunQueued();
            if (_stopping && _context.IsEmpty)
                break;

            _tick();
            _context.WaitForWork(_interval);
        }
    }

    private sealed class LoopContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly AutoResetEvent _wake = new(false);

        public bool IsEmpty => _queue.IsEmpty;

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            _wake.Set();
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("the SDK thread is never waited on synchronously");

        public override SynchronizationContext CreateCopy() => this;

        public void Wake() => _wake.Set();

        public void WaitForWork(TimeSpan timeout) => _wake.WaitOne(timeout);

        public void RunQueued()
        {
            // Only what is queued now: work it queues runs on the next pass,
            // after a pump, so a continuation chain cannot starve the SDK.
            int n = _queue.Count;
            for (int i = 0; i < n && _queue.TryDequeue(out var item); i++)
                item.Callback(item.State);
        }
    }
}
