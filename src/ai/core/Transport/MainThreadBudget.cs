namespace SourceSharp.Ai;

/// <summary>What a frame's budget is held to: read once a frame by the binding, from its convars and the engine's.</summary>
/// <param name="TargetUs">The frame the game aims for (<c>fps_max</c>'s period); 0 for none, so nothing is unused.</param>
/// <param name="IdleSpinUs">
/// How long the engine's wait for that frame spins on the CPU before the frame starts, which a CPU clock counts as
/// busy and is not work: on a listen server 1.5 ms on Linux (CEngine::Frame's <c>fBusyWaitMS</c>); on a dedicated one 0.
/// </param>
/// <param name="FloorUs">What the bots always get in a frame.</param>
/// <param name="BoundTicks">How many ticks a bot's turn may wait before it runs, whatever the budget says.</param>
/// <param name="Capped">Whether the cap applies: off, every turn runs and the frame is only measured.</param>
public readonly record struct BudgetFrame(double TargetUs, double IdleSpinUs, double FloorUs, int BoundTicks, bool Capped);

/// <summary>What a bot's turn does this frame.</summary>
public enum BudgetTurn : byte
{
    /// <summary>It fits: run.</summary>
    Run = 0,

    /// <summary>It does not fit, and may wait: a later frame runs it, with its own tick.</summary>
    Defer,

    /// <summary>It does not fit and has waited as long as it may: it runs regardless, over the cap, counted.</summary>
    Forced,
}

/// <summary>
/// The bots' share of the main thread (user ruling 2026-11-03: "cap bot logic
/// runtime on main thread, only burst into unused frame time"): each frame a
/// guaranteed floor, plus up to the frame's unused time. Pure over the clocks
/// it is handed, so its facts hold time still.
///
/// <para>
/// UNUSED TIME is the frame target less a recent estimate of the frame's work
/// that is not the bots'. On a listen server the client's frame runs on this
/// thread out of the server's sight, so the work is measured as the thread's
/// CPU time from one frame's start to the next, less what the bots charged,
/// less the engine's idle spin. Off-CPU time (the engine's sleep for
/// <c>fps_max</c>, a blocked present) is no work. The wall clock has the last
/// word: a frame that took longer than the target had nothing unused, whatever
/// its CPU time said, and for <see cref="OverrunHoldFrames"/> frames after one
/// the bots get the floor alone, since a present blocked on the GPU reads as
/// idle until a frame runs late.
/// </para>
///
/// <para>
/// THE SPIN. The engine's wait sleeps until under 2.5 ms remain, then spins. A
/// frame the thread slept in spun about <see cref="BudgetFrame.IdleSpinUs"/>,
/// which is taken off. A frame it did not sleep in may have spun up to 2.5 ms,
/// which cannot be told from work and is left in: the estimate errs towards
/// less for the bots, only in a frame already within 2.5 ms of full.
/// </para>
///
/// <para>
/// The estimate takes a new high at once and falls slowly, so one quiet frame
/// does not open the budget for the next heavy one. With no CPU clock (off
/// Linux) nothing can be told from idle, so nothing counts as unused.
/// </para>
/// </summary>
public sealed class MainThreadBudget(Func<double> wallUs, Func<double>? cpuUs)
{
    /// <summary>What a frame keeps in hand: the estimate is of a recent past, and the frame's end is not the bots' to cut close.</summary>
    public const double MarginUs = 1000d;

    /// <summary>How much of the way a lower frame takes the estimate down, a frame.</summary>
    public const double EstimateFall = 0.1d;

    /// <summary>Off-CPU time past this in a frame says the thread slept, so the engine's wait reached its spin.</summary>
    public const double SleptUs = 250d;

    /// <summary>A frame this far past the target ran late: the engine's timer does not land closer than a share of it.</summary>
    public const double OverrunShare = 1.05d, OverrunSlackUs = 500d;

    /// <summary>How many frames the bots get the floor alone after a frame ran late.</summary>
    public const int OverrunHoldFrames = 30;

    /// <summary>How many frames <see cref="FrameStats"/> reads over.</summary>
    public const int Window = 512;

    private readonly double[] _periods = new double[Window];
    private readonly double[] _used = new double[Window];
    private readonly double[] _allowances = new double[Window];
    private long _frames;
    private int _frame;
    private bool _started;
    private double _wallAt, _cpuAt, _usedNow, _estimate;
    private int _hold;
    private BudgetFrame _settings;

    /// <summary>What the bots may use this frame, in microseconds of the thread's CPU time.</summary>
    public double AllowanceUs { get; private set; }

    /// <summary>What they have used of it.</summary>
    public double UsedUs => _usedNow;

    /// <summary>The recent estimate of a frame's work that is not the bots'.</summary>
    public double NonBotEstimateUs => _estimate;

    /// <summary>The frame's target, as last read.</summary>
    public double TargetUs => _settings.TargetUs;

    /// <summary>Whether the cap applies this frame.</summary>
    public bool Capped => _settings.Capped;

    /// <summary>The bound, as last read.</summary>
    public int BoundTicks => _settings.BoundTicks;

    /// <summary>Turns put off to a later frame, a count of ticks waited: <c>ai.budget.deferrals</c>.</summary>
    public long Deferrals { get; private set; }

    /// <summary>Turns run past the budget because they had waited as long as they may: <c>ai.budget.forced</c>.</summary>
    public long Forced { get; private set; }

    /// <summary>Frames whose bots used more than they were allowed: the floor's last bot, or a forced one.</summary>
    public long FramesOver { get; private set; }

    /// <summary>Frames that took longer than the target.</summary>
    public long FramesLate { get; private set; }

    /// <summary>Frames seen.</summary>
    public long Frames => _frames;

    /// <summary>
    /// The clock turns are charged by: the thread's CPU time, the wall with no such
    /// clock. A thread descheduled in a bot's turn has not spent the bots' budget, and
    /// putting another bot off for it would save nothing.
    /// </summary>
    public double Now => cpuUs is { } cpu ? cpu() : wallUs();

    /// <summary>
    /// The start of a tick in host frame <paramref name="hostFrame"/>: a frame
    /// not seen before closes the last, updates the estimate and sets this one's
    /// allowance. A second tick in the same frame shares it. Host frames with no
    /// tick (a client running faster than the tick rate) are shared out evenly,
    /// so the estimate stays that of one frame.
    /// </summary>
    public void Begin(int hostFrame, in BudgetFrame frame)
    {
        if (_started && hostFrame == _frame)
            return;

        double wall = wallUs(), cpu = cpuUs?.Invoke() ?? 0d;
        _settings = frame;
        if (_started)
        {
            double frames = Math.Max(1, hostFrame - _frame);
            Close((wall - _wallAt) / frames, (cpu - _cpuAt) / frames, _usedNow / frames);
        }

        _started = true;
        _frame = hostFrame;
        _wallAt = wall;
        _cpuAt = cpu;
        _usedNow = 0d;
        bool idleKnown = cpuUs is not null && frame.TargetUs > 0d && _hold == 0;
        double unused = idleKnown ? Math.Max(0d, frame.TargetUs - MarginUs - _estimate) : 0d;
        AllowanceUs = Math.Max(frame.FloorUs, unused);
    }

    private void Close(double period, double cpu, double bots)
    {
        int at = (int)(_frames % Window);
        _periods[at] = period;
        _used[at] = bots;
        _allowances[at] = AllowanceUs;
        _frames++;
        if (_settings.Capped && bots > AllowanceUs)
            FramesOver++;

        double spin = period - cpu >= SleptUs ? _settings.IdleSpinUs : 0d;
        double nonBot = Math.Max(0d, cpu - Math.Min(bots, cpu) - spin);
        _estimate = nonBot >= _estimate ? nonBot : _estimate + (nonBot - _estimate) * EstimateFall;
        if (_hold > 0)
            _hold--;
        if (_settings.TargetUs > 0d && period > _settings.TargetUs * OverrunShare + OverrunSlackUs)
        {
            FramesLate++;
            _hold = OverrunHoldFrames;
        }
    }

    /// <summary>
    /// A turn expected to cost <paramref name="estimateUs"/>, of something that
    /// has already waited <paramref name="waitedTicks"/> ticks: while the floor
    /// is not used up it runs; past that if it fits the allowance; past that it
    /// waits, up to the bound.
    /// </summary>
    public BudgetTurn Turn(int waitedTicks, double estimateUs)
    {
        if (!_settings.Capped || _usedNow < _settings.FloorUs || _usedNow + estimateUs <= AllowanceUs)
            return BudgetTurn.Run;
        if (waitedTicks >= _settings.BoundTicks)
        {
            Forced++;
            return BudgetTurn.Forced;
        }

        Deferrals++;
        return BudgetTurn.Defer;
    }

    /// <summary>What is left of this frame's allowance.</summary>
    public double RemainingUs => Math.Max(0d, AllowanceUs - _usedNow);

    /// <summary>Charges <paramref name="us"/> of the bots' main-thread work to this frame.</summary>
    public void Charge(double us) => _usedNow += us;

    /// <summary>The last <see cref="Window"/> frames: each one's length, what the bots used and were allowed.</summary>
    public BudgetStats FrameStats()
    {
        int n = (int)Math.Min(_frames, Window);
        if (n == 0)
            return default;
        double[] periods = _periods[..n], used = _used[..n], allowed = _allowances[..n];
        double usedMean = used.Average(), allowedMean = allowed.Average();
        Array.Sort(periods);
        Array.Sort(used);
        return new BudgetStats(n, At(periods, 0.5), At(periods, 0.99), periods[^1], usedMean, At(used, 0.99), used[^1], allowedMean);

        static double At(double[] sorted, double share) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * share))];
    }
}

/// <summary>The last frames' numbers, in microseconds: their length and what the bots used of what they were allowed.</summary>
public readonly record struct BudgetStats(int Frames, double PeriodP50, double PeriodP99, double PeriodMax, double UsedMean, double UsedP99, double UsedMax, double AllowedMean);
