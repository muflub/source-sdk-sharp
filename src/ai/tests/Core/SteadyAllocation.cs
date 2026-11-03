namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// What a steady step allocates on its own thread, measured so that a busy
/// test run cannot charge it with the runtime's own work.
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> also counts what the
/// runtime allocates on the thread for itself, once, at a moment other
/// threads decide (a process-wide cache grown on this thread's miss when the
/// other facts' threads have filled it): 3,720 bytes over 1,000 steps that
/// allocate nothing, in 2 of 11 busy `make ai-test` runs, never alone, and no
/// tiering or collection setting reproduces it alone. So the window is
/// repeated and the least is the answer, a window of zero ending it: a step
/// that allocates allocates in every window and still reads above zero.
/// </summary>
internal static class SteadyAllocation
{
    internal static long Measure(Action step, int steps, int windows = 5)
    {
        long least = long.MaxValue;
        for (int w = 0; w < windows && least > 0; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < steps; i++)
                step();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }
}
