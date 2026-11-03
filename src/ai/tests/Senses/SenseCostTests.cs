using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The Phase 4 gate's budget (plan_hl2mp_ai §9, §12.3): the senses' main-
/// thread cost for 4 bots × 3 others, the vision pass and the views, a
/// 300-tick mean, over the inline transport's session, within the 400 µs
/// `ai.sense.us` ceiling Phase 0 set. The engine is a fake (its traces cost
/// next to nothing), so this holds the AI's own work; the live counter holds
/// the engine's (§12.3).
/// </summary>
public sealed class SenseCostTests
{
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    private const int ClockThreadCpuTime = 3;   // CLOCK_THREAD_CPUTIME_ID (time.h)

#pragma warning disable SYSLIB1054 // LibraryImport needs unsafe code, which this test project does not allow
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out TimeSpec time);
#pragma warning restore SYSLIB1054

    /// <summary>This thread's CPU time in microseconds: what the senses cost, whatever else the machine runs.</summary>
    private static double ThreadMicroseconds()
    {
        if (!OperatingSystem.IsLinux())
            return Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency;
        if (ClockGetTime(ClockThreadCpuTime, out TimeSpec t) != 0)
            throw new InvalidOperationException("clock_gettime(CLOCK_THREAD_CPUTIME_ID) failed");
        return t.Seconds * 1e6 + t.Nanoseconds / 1e3;
    }

    private sealed class Hud : IBotHud
    {
        public SelfState Read(EntityHandle bot) => new(default, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive);
    }

    /// <summary>
    /// The least of up to five 300-tick means of the thread's own CPU time:
    /// a busy machine (make test-full's parallel legs) neither stretches it
    /// nor, by the least, a stray stall; the first trial within the budget
    /// ends it.
    /// </summary>
    [Fact]
    public void FourBotsAndThreeOthersSenseWithinTheBudget()
    {
        double best = double.MaxValue;
        for (int trial = 0; trial < 5 && best > 400; trial++)
            best = Math.Min(best, MeanOfATrial());
        Assert.True(best <= 400, $"ai.sense.us {best:F1} µs over 300 ticks, the least of five trials");
    }

    private static double MeanOfATrial()
    {
        var w = new SenseWorld();
        var bots = new List<SensedBot>();
        var eyes = new VisionParams(6000f, 90f, 0.3f, 0.5f);
        for (int s = 1; s <= 4; s++)
            bots.Add(new SensedBot(new ActorId(s, 1), w.Add(s, new AiVector(s * 64, 0, 0), yaw: 90f).Handle, eyes));
        for (int s = 5; s <= 7; s++)
            w.Add(s, new AiVector(s * 64, 900, 0));
        w.Occluder(new AiVector(300, 400, -10), new AiVector(316, 600, 300), SenseWorld.Solid);
        var pass = new VisionPass(w.Engine(), w.Entities(), w.Traces());
        var builder = new SnapshotBuilder(w.Engine(), WeaponCatalog.Stock(), new Hud());
        var session = new AiSession(new InlineTransport(), new NoRecording(), VisionPass.Slots, 64);
        foreach (SensedBot b in bots)
        {
            pass.Add(b.Id);
            session.AddBot(b.Id, 1, new byte[10]);
        }

        var events = new List<AiEvent>();
        SensedBot[] sensed = [.. bots];
        double total = 0;
        int seen = 0;
        for (int t = 1; t <= 330; t++)
        {
            events.Clear();
            double start = ThreadMicroseconds();
            pass.Run(new Tick(t), 0.015f, sensed, w.Living(sensed), events);
            AiSnapshot snap = session.Snapshots.BeginWrite(new Tick(t));
            builder.Build(snap, sensed, pass, _ => ActorActivity.None);
            double us = ThreadMicroseconds() - start;
            foreach (AiEvent e in events)
                session.Push(e);
            session.Publish(snap);
            if (t > 30)
            {
                total += us;
                seen += snap.ViewFor(1)!.SightedCount;
            }
        }

        Assert.True(seen > 0, "the control: the bots saw the others");
        return total / 300;
    }
}
