using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class DecideScheduleTests
{
    [Fact]
    public void SlotZeroDecidesEverySixthTick()
        => Assert.Equal([0, 6, 12, 18],
                        Enumerable.Range(0, 20).Where(t => DecideSchedule.IsDecideTick(new Tick(t), 0)).ToArray());

    [Fact]
    public void EachSlotDecidesOnceInAnySixTicks()
    {
        for (int slot = 1; slot <= 16; slot++)
            Assert.Equal(1, Enumerable.Range(100, 6).Count(t => DecideSchedule.IsDecideTick(new Tick(t), slot)));
    }

    [Fact]
    public void SixBotsInSixSlotsDecideOnSixDifferentTicks()
    {
        int[] ticks = Enumerable.Range(1, 6)
            .Select(slot => Enumerable.Range(0, 6).First(t => DecideSchedule.IsDecideTick(new Tick(t), slot)))
            .ToArray();
        Assert.Equal(6, ticks.Distinct().Count());
    }

    [Fact]
    public void TheRuleIsTickPlusSlotModPeriod()
        => Assert.True(DecideSchedule.IsDecideTick(new Tick(4), 2), "(4 + 2) % 6 == 0");
}
