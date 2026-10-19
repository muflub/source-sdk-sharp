using System;

namespace SourceSharp.Tests;

/// <summary>
/// "This call allocates nothing", measured so a one-off on the measuring thread
/// cannot fail it. A single measured call after one warm-up was charged
/// whatever landed inside it: a tiered-JIT tier-up or a call-counting stub's
/// install allocates on the calling thread, so under a full leg's load the
/// facts that measured once failed now and then (NameMatchesTests read 152 and
/// 688 bytes, PhysicsMotionControllersTests one call, each 0 when run alone).
/// A steady per-call allocation is in every call, so the fewest bytes across
/// several measured calls is still non-zero for it: the facts can still fail
/// for the thing they are about.
/// </summary>
public static class SteadyAllocation
{
    /// <summary>
    /// Runs <paramref name="call"/> <paramref name="warm"/> times, then
    /// answers the fewest bytes the current thread allocated across
    /// <paramref name="samples"/> further calls.
    /// </summary>
    public static long LeastAllocated(Action call, int warm = 3, int samples = 5)
    {
        for (int i = 0; i < warm; i++)
            call();

        long least = long.MaxValue;
        for (int i = 0; i < samples; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            call();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < least)
                least = allocated;
        }

        return least;
    }
}
