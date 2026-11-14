using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// The controller's tick, timed (plan_descent_logic HO2's main-thread cost;
/// 3.11 row 7 is the spike's): a stopwatch and the game thread's CPU clock
/// around each tick while switched on, into arrays made once, so measuring
/// allocates nothing. Off by default; <c>tf2_descent_horde_time</c> drives it.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeTickTimer
{
    /// <summary>Samples kept at most.</summary>
    public const int Capacity = 100_000;

    private readonly double[] _wall = new double[Capacity];
    private readonly double[] _cpu = new double[Capacity];
    private readonly ThreadClock _clock = new();
    private long _startStamp;
    private double _startCpu;

    public bool On { get; private set; }

    public int Samples { get; private set; }

    public void Start()
    {
        Samples = 0;
        On = true;
    }

    public void Stop() => On = false;

    /// <summary>Before the tick.</summary>
    public void Enter()
    {
        if (!On)
            return;
        _startCpu = _clock.Micros();
        _startStamp = Stopwatch.GetTimestamp();
    }

    /// <summary>After the tick.</summary>
    public void Leave()
    {
        if (!On || Samples >= Capacity)
            return;
        long end = Stopwatch.GetTimestamp();
        _cpu[Samples] = _clock.Micros() - _startCpu;
        _wall[Samples] = (end - _startStamp) * 1e6 / Stopwatch.Frequency;
        Samples++;
    }

    /// <summary>The two distributions, one line each.</summary>
    public (string Wall, string Cpu) Report() => (Dist(_wall), Dist(_cpu));

    private string Dist(double[] samples)
    {
        int n = Samples;
        if (n == 0)
            return "n 0";

        double[] s = samples[..n];
        Array.Sort(s);
        return string.Create(CultureInfo.InvariantCulture,
            $"n {n} mean {s.Average():0.0} p50 {s[n / 2]:0.0} p95 {s[(int)(n * 0.95)]:0.0} p99 {s[(int)(n * 0.99)]:0.0} max {s[n - 1]:0.0} us");
    }

    /// <summary>The calling thread's CPU time (CLOCK_THREAD_CPUTIME_ID), as src/ai's ThreadCpuClock; 0 off Linux.</summary>
    private sealed class ThreadClock
    {
        private struct TimeSpec
        {
            public long Seconds;
            public long Nanoseconds;
        }

#pragma warning disable SYSLIB1054 // LibraryImport needs unsafe code, which this project does not allow
        [DllImport("libc", EntryPoint = "clock_gettime")]
        private static extern int ClockGetTime(int clock, out TimeSpec time);
#pragma warning restore SYSLIB1054

        public double Micros() =>
            OperatingSystem.IsLinux() && ClockGetTime(3, out TimeSpec t) == 0 ? t.Seconds * 1e6 + t.Nanoseconds / 1e3 : 0;
    }
}
