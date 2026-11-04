using Xunit;

namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// The bots' share of the main thread (user ruling 2026-11-03), with its clocks
/// held still: a floor always, the frame's unused time on top, and how a frame's
/// idle spin and off-CPU time are told from work.
/// </summary>
public sealed class MainThreadBudgetTests
{
    private const double Target = 16_667d, Floor = 500d, Spin = 1500d;

    private sealed class Clocks
    {
        public double Wall, Cpu;
    }

    private readonly Clocks _c = new();
    private readonly MainThreadBudget _b;
    private int _frame;

    public MainThreadBudgetTests() => _b = new MainThreadBudget(() => _c.Wall, () => _c.Cpu);

    private static BudgetFrame Settings(double target = Target, double spin = Spin, bool capped = true, int bound = 3)
        => new(target, spin, Floor, bound, capped);

    /// <summary>A frame of the game: work on the CPU, then the engine's wait, its sleep off the CPU and its spin on it.</summary>
    private void Frame(double workUs, double sleepUs = 0d, double spinUs = 0d, double? target = null, double? spin = null)
    {
        _b.Begin(_frame, Settings(target ?? Target, spin ?? Spin));
        _frame++;
        _c.Wall += workUs + sleepUs + spinUs;
        _c.Cpu += workUs + spinUs;
    }

    private void IdleFrame(double workUs) => Frame(workUs, Target - workUs - Spin, Spin);

    [Fact]
    public void WithAnEmptyFrameTheAllowanceIsTheFrameLessTheWorkAndTheMargin()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(5000d);
        _b.Begin(_frame, Settings());
        Assert.InRange(_b.NonBotEstimateUs, 4999d, 5100d);
        Assert.InRange(_b.AllowanceUs, Target - 5100d - MainThreadBudget.MarginUs, Target - 4999d - MainThreadBudget.MarginUs);
    }

    /// <summary>The engine's spin is on the CPU and is no work: taken as work it would cost the bots 1.5 ms of a frame.</summary>
    [Fact]
    public void TheEnginesIdleSpinIsNotWork()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(5000d);
        _b.Begin(_frame, Settings());
        Assert.True(_b.NonBotEstimateUs < 5000d + Spin / 2d, $"the estimate {_b.NonBotEstimateUs} took the spin for work");
    }

    /// <summary>The sleep is off the CPU: a wall clock would call it work and leave the bots nothing.</summary>
    [Fact]
    public void TheEnginesSleepIsNotWork()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(2000d);
        _b.Begin(_frame, Settings());
        Assert.True(_b.AllowanceUs > 10_000d, $"allowance {_b.AllowanceUs}");
    }

    /// <summary>A dedicated server's wait does not spin: nothing is taken off its work.</summary>
    [Fact]
    public void NoSpinIsTakenOffWhereTheWaitDoesNotSpin()
    {
        for (int i = 0; i < 40; i++)
            Frame(5000d, 10_000d, 0d, 15_000d, 0d);
        _b.Begin(_frame, Settings(15_000d, 0d));
        Assert.InRange(_b.NonBotEstimateUs, 4999d, 5001d);
    }

    /// <summary>A frame that never slept may have spun; it cannot be told from work, so it is left in, against the bots.</summary>
    [Fact]
    public void AFrameThatDidNotSleepKeepsItsSpinAsWork()
    {
        for (int i = 0; i < 40; i++)
            Frame(14_500d, 0d, 2000d);
        _b.Begin(_frame, Settings());
        Assert.InRange(_b.NonBotEstimateUs, 16_000d, 16_600d);
        Assert.Equal(Floor, _b.AllowanceUs);
    }

    /// <summary>What the bots charged is not the frame's other work: they do not crowd themselves out.</summary>
    [Fact]
    public void TheBotsOwnTimeIsNotCountedAsOtherWork()
    {
        for (int i = 0; i < 40; i++)
        {
            _b.Begin(_frame++, Settings());
            _b.Charge(3000d);
            const double work = 5000d + 3000d;
            _c.Wall += Target;
            _c.Cpu += work + Spin;
        }

        _b.Begin(_frame, Settings());
        Assert.InRange(_b.NonBotEstimateUs, 4999d, 5100d);
    }

    [Fact]
    public void WithNoFrameTargetTheBotsGetTheFloor()
    {
        for (int i = 0; i < 10; i++)
            Frame(1000d, target: 0d);
        _b.Begin(_frame, Settings(target: 0d));
        Assert.Equal(Floor, _b.AllowanceUs);
    }

    [Fact]
    public void WithNoUnusedTimeTheBotsGetTheFloor()
    {
        for (int i = 0; i < 10; i++)
            Frame(Target - 100d);
        _b.Begin(_frame, Settings());
        Assert.Equal(Floor, _b.AllowanceUs);
    }

    /// <summary>Off Linux there is no CPU clock: nothing can be told from idle, so nothing counts as unused.</summary>
    [Fact]
    public void WithNoCpuClockTheBotsGetTheFloor()
    {
        var b = new MainThreadBudget(() => _c.Wall, null);
        for (int i = 0; i < 10; i++)
        {
            b.Begin(i, Settings());
            _c.Wall += Target;
        }

        Assert.Equal(Floor, b.AllowanceUs);
    }

    [Fact]
    public void TheFloorAlwaysRuns()
    {
        for (int i = 0; i < 10; i++)
            Frame(Target * 2d);
        _b.Begin(_frame, Settings());
        Assert.Equal(BudgetTurn.Run, _b.Turn(0, 100_000d));   // the first turn, however dear
        _b.Charge(Floor - 1d);
        Assert.Equal(BudgetTurn.Run, _b.Turn(0, 100d));       // the floor not yet used up
        _b.Charge(1d);
        Assert.Equal(BudgetTurn.Defer, _b.Turn(0, 100d));     // used up, nothing unused
        Assert.Equal(1L, _b.Deferrals);
    }

    /// <summary>Unused time extends the allowance past the floor; a control at the same spend with none defers.</summary>
    [Fact]
    public void UnusedTimeExtendsTheAllowance()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(5000d);
        _b.Begin(_frame, Settings());
        _b.Charge(Floor * 4d);
        Assert.Equal(BudgetTurn.Run, _b.Turn(0, 100d));
        Assert.Equal(0L, _b.Deferrals);

        var clocks = new Clocks();
        var busy = new MainThreadBudget(() => clocks.Wall, () => clocks.Cpu);
        for (int i = 0; i < 40; i++)
        {
            busy.Begin(i, Settings());
            clocks.Wall += Target - 100d;
            clocks.Cpu += Target - 100d;
        }

        busy.Begin(40, Settings());
        busy.Charge(Floor * 4d);
        Assert.Equal(BudgetTurn.Defer, busy.Turn(0, 100d));
    }

    [Fact]
    public void ATurnThatWouldOverrunTheAllowanceWaits()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(5000d);
        _b.Begin(_frame, Settings());
        _b.Charge(_b.AllowanceUs - 50d);
        Assert.Equal(BudgetTurn.Defer, _b.Turn(0, 100d));
        Assert.Equal(BudgetTurn.Run, _b.Turn(0, 40d));
    }

    [Fact]
    public void TheBoundForcesATurnAndCountsIt()
    {
        for (int i = 0; i < 10; i++)
            Frame(Target * 2d);
        _b.Begin(_frame, Settings(bound: 3));
        _b.Charge(Floor);
        Assert.Equal(BudgetTurn.Defer, _b.Turn(2, 100d));
        Assert.Equal(0L, _b.Forced);
        Assert.Equal(BudgetTurn.Forced, _b.Turn(3, 100d));
        Assert.Equal(1L, _b.Forced);
    }

    [Fact]
    public void UncappedEveryTurnRunsAndTheFrameIsStillMeasured()
    {
        _b.Begin(0, Settings(capped: false));
        _b.Charge(1_000_000d);
        Assert.Equal(BudgetTurn.Run, _b.Turn(100, 1_000_000d));
        Assert.Equal((0L, 0L), (_b.Deferrals, _b.Forced));
        _c.Wall += 20_000d;
        _c.Cpu += 20_000d;
        _b.Begin(1, Settings(capped: false));
        Assert.Equal(1, _b.FrameStats().Frames);
        Assert.Equal(20_000d, _b.FrameStats().PeriodP50);
    }

    /// <summary>Two ticks of one host frame share its allowance: the second neither resets what the first used nor closes a frame.</summary>
    [Fact]
    public void TwoTicksInOneFrameShareItsAllowance()
    {
        _b.Begin(5, Settings());
        _b.Charge(400d);
        _c.Wall += 900d;
        _b.Begin(5, Settings());
        Assert.Equal(400d, _b.UsedUs);
        Assert.Equal(0L, _b.Frames);
        _b.Begin(6, Settings());
        Assert.Equal(0d, _b.UsedUs);
        Assert.Equal(1L, _b.Frames);
    }

    /// <summary>A client faster than the tick rate runs frames with no tick: a span of them is shared out, so the estimate is of one frame.</summary>
    [Fact]
    public void FramesWithNoTickAreSharedOut()
    {
        for (int i = 0; i < 40; i++)
        {
            _b.Begin(_frame, Settings(5000d, 0d));
            _frame += 3;
            _c.Wall += 15_000d;
            _c.Cpu += 6000d;
        }

        _b.Begin(_frame, Settings(5000d, 0d));
        Assert.InRange(_b.NonBotEstimateUs, 1999d, 2001d);
    }

    /// <summary>A frame that ran late had nothing unused, whatever its CPU time said (a present blocked on the GPU reads as idle): the floor alone for a while.</summary>
    [Fact]
    public void AFrameThatRanLateHoldsTheBotsToTheFloor()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(2000d);
        Assert.Equal(0L, _b.FramesLate);
        _b.Begin(_frame, Settings());
        Assert.True(_b.AllowanceUs > 10_000d, "the control: an idle frame's allowance");
        _frame++;
        _c.Wall += Target * 2d;
        _c.Cpu += 2000d;   // long, and hardly any of it on the CPU
        _b.Begin(_frame, Settings());
        Assert.Equal(1L, _b.FramesLate);
        Assert.Equal(Floor, _b.AllowanceUs);
        for (int i = 0; i < MainThreadBudget.OverrunHoldFrames - 1; i++)
        {
            IdleFrame(2000d);
            _b.Begin(_frame, Settings());
            Assert.Equal(Floor, _b.AllowanceUs);
        }

        IdleFrame(2000d);
        _b.Begin(_frame, Settings());
        Assert.True(_b.AllowanceUs > 10_000d, $"the hold over: {_b.AllowanceUs}");
    }

    /// <summary>A heavy frame raises the estimate at once; one quiet frame does not open the budget for the next heavy one.</summary>
    [Fact]
    public void TheEstimateRisesAtOnceAndFallsSlowly()
    {
        for (int i = 0; i < 40; i++)
            IdleFrame(2000d);
        IdleFrame(12_000d);
        _b.Begin(_frame, Settings());
        Assert.InRange(_b.NonBotEstimateUs, 11_999d, 12_100d);
        double high = _b.NonBotEstimateUs;
        IdleFrame(2000d);
        _b.Begin(_frame, Settings());
        Assert.True(_b.NonBotEstimateUs > high - (high - 2000d) * 0.2d, $"fell to {_b.NonBotEstimateUs}");
    }

    /// <summary>A thread descheduled in a bot's turn has not spent the budget: turns are charged by its CPU time, so a busy box puts off no bot.</summary>
    [Fact]
    public void ATurnIsChargedByTheThreadsCpuTimeNotTheWall()
    {
        _b.Begin(0, Settings());
        double from = _b.Now;
        _c.Cpu += 40d;
        _c.Wall += 4000d;
        Assert.Equal(40d, _b.Now - from);

        var wallOnly = new MainThreadBudget(() => _c.Wall, null);
        double wallFrom = wallOnly.Now;
        _c.Wall += 70d;
        Assert.Equal(70d, wallOnly.Now - wallFrom);   // with no CPU clock, the wall
    }

    [Fact]
    public void FramesOverTheAllowanceAreCounted()
    {
        for (int i = 0; i < 10; i++)
            Frame(Target * 2d);
        Assert.Equal(0L, _b.FramesOver);
        _b.Begin(_frame, Settings());
        _b.Charge(Floor + 1d);
        _frame++;
        _b.Begin(_frame, Settings());
        Assert.Equal(1L, _b.FramesOver);
    }
}
