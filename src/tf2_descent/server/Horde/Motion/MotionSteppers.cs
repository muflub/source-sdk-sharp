using System.Diagnostics;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// Where the bodies' movement steps run (plan_descent_logic 3.7, E5): the main
/// thread hands each tick's commands in (<see cref="Submit"/>) and takes the
/// newest finished steps out (<see cref="TryTake"/>), and neither call ever
/// waits. A step is applied at most one tick after the tick that asked for it:
/// <see cref="TryTake"/> refuses an older one, and the bodies then hold.
/// </summary>
public interface IMotionStepper : IDisposable
{
    /// <summary>The steps run so far, and the CPU microseconds they took on the thread that ran them.</summary>
    long StepsRun { get; }

    double StepMicros { get; }

    /// <summary>Hands in the commands of tick <paramref name="tick"/>; false when they could not be taken without waiting (the bodies hold a tick).</summary>
    bool Submit(long tick, ReadOnlySpan<StepInput> inputs);

    /// <summary>
    /// The newest finished steps not yet taken, and the tick that asked for
    /// them; false when there are none, or they are more than one tick old at
    /// <paramref name="now"/> (they are dropped).
    /// </summary>
    bool TryTake(long now, out ReadOnlySpan<StepOutput> outputs, out long tick);
}

/// <summary>
/// The steps run on the main thread, inside <see cref="Submit"/>, and taken at
/// the next tick: the mode while the movement's world is the engine's, which
/// only the game thread may ask (HO3's ruling: the nav voxels a worker could
/// sweep live in assemblies Descent may not load).
/// </summary>
public sealed class InlineMotionStepper(IMotionStep step, int capacity, ThreadCpuClock clock) : IMotionStepper
{
    private readonly StepOutput[] _outputs = new StepOutput[capacity];
    private int _count;
    private long _tick = -1;

    public long StepsRun { get; private set; }

    public double StepMicros { get; private set; }

    public bool Submit(long tick, ReadOnlySpan<StepInput> inputs)
    {
        double c0 = clock.Micros();
        for (int i = 0; i < inputs.Length; i++)
            step.Run(in inputs[i], out _outputs[i]);
        StepMicros += clock.Micros() - c0;
        StepsRun += inputs.Length;
        _count = inputs.Length;
        _tick = tick;
        return true;
    }

    public bool TryTake(long now, out ReadOnlySpan<StepOutput> outputs, out long tick)
    {
        tick = _tick;
        outputs = _outputs.AsSpan(0, _count);
        bool fresh = _tick >= 0 && now - _tick <= 1;
        _count = 0;
        _tick = -1;
        return fresh;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// The steps run on a pool thread, off the main thread (E5 as HO0 measured
/// it): the main thread's <see cref="Submit"/> copies the commands into a
/// pending buffer and starts the worker if it is idle; a busy worker takes the
/// newest pending commands when it finishes, never a stale set. The worker
/// publishes each finished set; <see cref="TryTake"/> swaps it out. Every
/// buffer swap is under one lock the main thread only ever tries
/// (<see cref="Monitor.TryEnter(object)"/>): when the worker holds it, the
/// main thread does without this tick rather than wait, and the bodies hold.
///
/// <para>
/// The step's world must be safe to read off the main thread: the level's voxel
/// columns (<see cref="VoxelMotionWorld"/>), never the engine's. The horde's nav
/// hands the motion one once the level's voxels are ready (<c>HordeNav</c>).
/// </para>
/// </summary>
public sealed class WorkerMotionStepper : IMotionStepper, IThreadPoolWorkItem
{
    private readonly IMotionStep _step;
    private readonly ThreadCpuClock _clock;
    private readonly object _gate = new();
    private StepInput[] _pending, _working;
    private StepOutput[] _done, _ready, _taken;
    private int _pendingCount, _workingCount, _readyCount;
    private long _pendingTick = -1, _workingTick = -1, _readyTick = -1;
    private bool _hasPending, _hasReady, _busy, _disposed;
    private long _stepsRun;
    private double _stepMicros;

    public WorkerMotionStepper(IMotionStep step, int capacity, ThreadCpuClock clock)
    {
        _step = step;
        _clock = clock;
        _pending = new StepInput[capacity];
        _working = new StepInput[capacity];
        _done = new StepOutput[capacity];
        _ready = new StepOutput[capacity];
        _taken = new StepOutput[capacity];
    }

    public long StepsRun => Interlocked.Read(ref _stepsRun);

    public double StepMicros
    {
        get
        {
            lock (_gate)
                return _stepMicros;
        }
    }

    /// <summary>Submits refused because the worker held the lock.</summary>
    public long Contended { get; private set; }

    /// <summary>Whether a set handed over is still being run (or waits to be).</summary>
    public bool Busy => Volatile.Read(ref _busy);

    /// <summary>Finished sets dropped as more than a tick old.</summary>
    public long Stale { get; private set; }

    public bool Submit(long tick, ReadOnlySpan<StepInput> inputs)
    {
        if (!Monitor.TryEnter(_gate))
        {
            Contended++;
            return false;
        }

        bool start;
        try
        {
            if (_disposed)
                return false;
            inputs.CopyTo(_pending);
            _pendingCount = inputs.Length;
            _pendingTick = tick;
            _hasPending = true;
            start = !_busy;
            _busy = true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        if (start)
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        return true;
    }

    public bool TryTake(long now, out ReadOnlySpan<StepOutput> outputs, out long tick)
    {
        outputs = default;
        tick = -1;
        if (!Monitor.TryEnter(_gate))
        {
            Contended++;
            return false;
        }

        try
        {
            if (!_hasReady)
                return false;
            (_ready, _taken) = (_taken, _ready);
            _hasReady = false;
            tick = _readyTick;
            outputs = _taken.AsSpan(0, _readyCount);
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        if (now - tick <= 1)
            return true;

        Stale++;
        outputs = default;
        return false;
    }

    /// <summary>The worker: runs the newest pending set until none is left.</summary>
    void IThreadPoolWorkItem.Execute()
    {
        while (true)
        {
            lock (_gate)
            {
                if (!_hasPending || _disposed)
                {
                    _busy = false;
                    return;
                }

                (_pending, _working) = (_working, _pending);
                _workingCount = _pendingCount;
                _workingTick = _pendingTick;
                _hasPending = false;
            }

            double c0 = _clock.Micros();
            for (int i = 0; i < _workingCount; i++)
                _step.Run(in _working[i], out _done[i]);
            double spent = _clock.Micros() - c0;

            lock (_gate)
            {
                (_done, _ready) = (_ready, _done);
                _readyCount = _workingCount;
                _readyTick = _workingTick;
                _hasReady = true;
                _stepMicros += spent;
                _stepsRun += _workingCount;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
    }
}

/// <summary>The calling thread's CPU time, in microseconds (CLOCK_THREAD_CPUTIME_ID; 0 where there is none).</summary>
public sealed class ThreadCpuClock
{
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

#pragma warning disable SYSLIB1054 // LibraryImport needs unsafe code, which this project does not allow
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out TimeSpec time);
#pragma warning restore SYSLIB1054

    public double Micros() =>
        OperatingSystem.IsLinux() && ClockGetTime(3, out TimeSpec t) == 0 ? t.Seconds * 1e6 + t.Nanoseconds / 1e3 : 0;

    /// <summary>The stopwatch's microseconds since <paramref name="start"/>.</summary>
    public static double Since(long start) => (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
}
