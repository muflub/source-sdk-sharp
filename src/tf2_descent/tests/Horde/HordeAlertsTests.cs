using SourceSharp.Ai;
using TF2.Descent.Ai.Senses;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>The horde's alert log (dev): each robot's first alert of a spell, every alert counted by cause.</summary>
public sealed class HordeAlertsTests
{
    private static AlertRecord At(long tick, int row, AlertCause cause = AlertCause.Sight) =>
        new(tick, row, 0, cause, SoundKind.Unknown, default, 0, default, 0, 1, true);

    private static HordeAlerts Log() => HordeAlerts.For(8, 1.0, 10);   // a spell of 10 ticks

    private static AlertRecord[] Drained(HordeAlerts log)
    {
        var into = new AlertRecord[HordeAlerts.Capacity];
        return into[..log.Drain(into)];
    }

    [Fact]
    public void A_robots_first_alert_is_recorded()
    {
        var log = Log();
        log.Add(At(100, 3));

        Assert.Equal([At(100, 3)], Drained(log));
    }

    [Fact]
    public void A_second_alert_within_the_spell_is_counted_not_recorded()
    {
        var log = Log();
        log.Add(At(100, 3));
        log.Add(At(105, 3, AlertCause.Sound));

        Assert.Single(Drained(log));
        Assert.Equal(1, log.Count(AlertCause.Sound));
    }

    [Fact]
    public void An_alert_after_a_quiet_spell_is_recorded_again()
    {
        var log = Log();
        log.Add(At(100, 3));
        log.Add(At(111, 3, AlertCause.Damage));

        Assert.Equal([At(100, 3), At(111, 3, AlertCause.Damage)], Drained(log));
    }

    [Fact]
    public void Two_robots_alerted_at_once_are_both_recorded()
    {
        var log = Log();
        log.Add(At(100, 3));
        log.Add(At(100, 4, AlertCause.PackCall));

        Assert.Equal(2, Drained(log).Length);
    }

    [Fact]
    public void A_full_ring_drops_the_oldest_and_counts_it()
    {
        var log = HordeAlerts.For(HordeAlerts.Capacity + 1, 1.0, 10);
        for (int row = 0; row <= HordeAlerts.Capacity; row++)
            log.Add(At(100, row));

        var drained = Drained(log);
        Assert.Equal(1, log.Overwritten);
        Assert.Equal(HordeAlerts.Capacity, drained.Length);
        Assert.Equal(1, drained[0].Row);
    }
}
