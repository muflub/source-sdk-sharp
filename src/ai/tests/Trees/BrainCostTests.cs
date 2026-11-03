using System.Diagnostics;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// §9 Phase 5's budget: a brain step with the HL2MP trees, its memory's
/// beliefs spreading over a 64 × 64 graph for three lost opponents and one
/// in sight, costs at most 2 ms a decide (the mean of this thread's CPU time
/// over 300 decides, the least of five trials). The live number is the
/// counter <c>ai.brain.us</c> (§12.3).
/// </summary>
public sealed class BrainCostTests
{
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

#pragma warning disable SYSLIB1054 // LibraryImport needs unsafe code, which this test project does not allow
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out TimeSpec time);
#pragma warning restore SYSLIB1054

    private static double ThreadMicroseconds()
    {
        if (!OperatingSystem.IsLinux())
            return Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency;
        if (ClockGetTime(3, out TimeSpec t) != 0)
            throw new InvalidOperationException("clock_gettime(CLOCK_THREAD_CPUTIME_ID) failed");
        return t.Seconds * 1e6 + t.Nanoseconds / 1e3;
    }

    [Fact]
    public void ADecideCostsAtMostTwoMilliseconds()
    {
        double best = double.MaxValue;
        for (int trial = 0; trial < 5 && best > 2000; trial++)
            best = Math.Min(best, MeanOfATrial());
        Assert.True(best <= 2000, $"ai.brain.us {best:F1} µs a decide, the least of five 300-decide means");
    }

    private static double MeanOfATrial()
    {
        string[] rows = [.. Enumerable.Repeat(new string('.', 64), 64)];
        var (g, _) = Grids.Parse(rows);
        var d = new Duel(Duel.Preset("hard"), g,
                         new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(100, 100, 0), 30f),
                         new ItemSpawn(1, "weapon_ar2", ItemKind.Weapon, new AiVector(1900, 1900, 0), 20f, Duel.Smg1));
        var near = new AiVector(400f, 300f, 0f);
        ActorId[] lost = [new(5, 0x4000_0005), new(6, 0x4000_0006), new(7, 0x4000_0007)];
        var seen = new List<AiEvent>(d.SeeAt(Duel.Opponent, near));
        foreach (ActorId a in lost)
            seen.AddRange(d.SeeAt(a, new AiVector(1000f + a.Slot * 100f, 1000f, 0f)));
        d.Step(d.Self(), [(Duel.Opponent, near)], [.. seen]);
        d.Step(d.Self(), [(Duel.Opponent, near)], [.. lost.Select(a => AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), a, new AiVector(1000f + a.Slot * 100f, 1000f, 0f), new AiVector(190f, 0f, 0f)))]);
        (ActorId, AiVector)[] view = [(Duel.Opponent, near)];
        double total = 0;
        for (int i = 0; i < 330; i++)
        {
            double start = ThreadMicroseconds();
            d.Step(d.Self(), view);
            if (i >= 30)
                total += ThreadMicroseconds() - start;
        }

        return total / 300;
    }
}
