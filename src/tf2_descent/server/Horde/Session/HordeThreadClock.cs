using System.Runtime.InteropServices;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>The calling thread's CPU time (CLOCK_THREAD_CPUTIME_ID), in microseconds, as HordeTickTimer reads it; 0 off Linux. A counter, never an input.</summary>
public static class HordeThreadClock
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

    public static double Micros() =>
        OperatingSystem.IsLinux() && ClockGetTime(3, out TimeSpec t) == 0 ? t.Seconds * 1e6 + t.Nanoseconds / 1e3 : 0;
}
