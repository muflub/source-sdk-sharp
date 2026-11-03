using System.Runtime.InteropServices;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The calling thread's CPU time (Linux's <c>CLOCK_THREAD_CPUTIME_ID</c>), for
/// <c>ai.brain.cpu.us</c>: a step on a worker is charged what it ran, not the
/// time its thread waited for a core. Null where the clock is not there.
/// </summary>
public static class ThreadCpuClock
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

    /// <summary>The clock, or null off Linux.</summary>
    public static Func<double>? Micros() => OperatingSystem.IsLinux() ? Read : null;

    private static double Read() => ClockGetTime(3, out TimeSpec t) == 0 ? t.Seconds * 1e6 + t.Nanoseconds / 1e3 : 0;
}
