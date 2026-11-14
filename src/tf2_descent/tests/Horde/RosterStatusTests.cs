using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Robots;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A roster row's statuses (plan_descent_logic 3.3, HO8): kept in slots with their end ticks
/// and appliers, run for a body's hit path or ticked far, a mark on what a robot takes, dropped
/// at death, carried across a body's return, the taunt and the debuff columns, and a steady
/// tick that allocates nothing.
/// </summary>
public sealed class RosterStatusTests
{
    private const double Tps = RosterTests.TicksPerSecond;

    private static RobotRoster Awake(int rooms = 3)
    {
        var roster = RosterTests.Rolled(Row(rooms));
        for (int r = 0; r < rooms; r++) roster.WakeRoom(r);
        return roster;
    }

    private static int RowOf(string id) => TheRules.StatusBook.RowOf(id);

    [Fact]
    public void A_status_keeps_the_hero_who_applied_it_and_the_skills_duration()
    {
        var roster = Awake();

        roster.ApplyStatus(0, RowOf("stun"), 100, 1.5, 3);

        var slot = roster.StatusSlot(0, RowOf("stun"));
        Assert.Equal((1, 100 + StatusRules.Ticks(1.5, Tps), 3), (slot.Stacks, slot.EndTick, slot.Applier));
        Assert.True(roster.Stunned(0));
    }

    [Fact]
    public void Running_statuses_hands_the_damage_owed_to_the_caller_and_takes_no_life()
    {
        var roster = Awake();
        double life = roster.Life(0);
        roster.ApplyStatus(0, RowOf("burn"), 0, 0, 5);
        var due = new StatusDue[TheRules.StatusBook.Count];

        int n = roster.RunStatuses(0, StatusRules.Ticks(0.5, Tps), due);

        Assert.Equal(1, n);
        Assert.Equal(new StatusDue(RowOf("burn"), 4, 1, 5), due[0]);
        Assert.Equal(life, roster.Life(0));
    }

    [Fact]
    public void Running_statuses_before_one_is_due_does_nothing()
    {
        var roster = Awake();
        roster.ApplyStatus(0, RowOf("burn"), 0, 0, 5);
        var due = new StatusDue[TheRules.StatusBook.Count];

        Assert.Equal(0, roster.RunStatuses(0, roster.StatusDue(0) - 1, due));
    }

    [Fact]
    public void A_status_ends_at_its_end_tick_and_its_slow_goes_with_it()
    {
        var roster = Awake();
        roster.ApplyStatus(0, RowOf("slow"), 0, 0, 1);
        long end = roster.StatusSlot(0, RowOf("slow")).EndTick;
        var due = new StatusDue[TheRules.StatusBook.Count];

        roster.RunStatuses(0, end - 1, due);
        Assert.True(roster.SpeedFactor(0) < 1f);
        roster.RunStatuses(0, end, due);

        Assert.Equal(1f, roster.SpeedFactor(0));
        Assert.Equal(0, roster.StatusBits(0));
    }

    [Fact]
    public void A_mark_raises_the_damage_taken_factor()
    {
        var roster = Awake();

        roster.ApplyStatus(0, RowOf("marked"), 0, 0, 1);

        Assert.Equal(1.35f, roster.DamageTakenFactor(0), 5);
    }

    [Fact]
    public void A_far_burn_on_a_marked_robot_takes_the_mark_too()
    {
        var roster = Awake();
        var stats = roster.Stats(0);
        double life = roster.Life(0);
        roster.ApplyStatus(0, RowOf("marked"), 0, 0, 1);
        roster.ApplyStatus(0, RowOf("burn"), 0, 0, 1);

        double dealt = roster.TickStatuses(0, StatusRules.Ticks(0.5, Tps));

        Assert.Equal(RobotRules.DamageTaken(stats, "fire", 4) * 1.35, dealt, 9);
        Assert.Equal(life - dealt, roster.Life(0), 9);
    }

    [Fact]
    public void A_dead_robot_carries_no_status_and_takes_none()
    {
        var roster = Awake();
        roster.ApplyStatus(0, RowOf("slow"), 0, 0, 1);
        roster.ApplyStatus(0, RowOf("burn"), 0, 0, 1);

        Assert.True(roster.Hurt(0, roster.Life(0)));

        Assert.Empty(roster.Statuses(0).Active);
        Assert.Equal((1f, 1f, false, long.MaxValue), (roster.SpeedFactor(0), roster.DamageTakenFactor(0), roster.Stunned(0), roster.StatusDue(0)));
        Assert.Equal(StatusApplied.Immune, roster.ApplyStatus(0, RowOf("stun"), 5, 0, 1));
        Assert.Empty(roster.Statuses(0).Active);
    }

    [Fact]
    public void A_status_and_its_applier_survive_a_return_and_a_new_lend()
    {
        var roster = Awake();
        var pool = BodyPool.For(TheRules.Content.Horde);
        roster.ApplyStatus(0, RowOf("bleed"), 10, 0, 6);
        var lease = pool.Lend(1, roster.Id(0));
        var start = roster.Promote(0, lease, 20);

        pool.Return(roster.Demote(0, start, 30));
        roster.Promote(0, pool.Lend(2, roster.Id(0)), 40);

        var slot = roster.StatusSlot(0, RowOf("bleed"));
        Assert.Equal((1, 6), (slot.Stacks, slot.Applier));
        Assert.Equal(10 + StatusRules.Ticks(TheRules.Statuses.Row("bleed").DurationSeconds, Tps), slot.EndTick);
    }

    [Fact]
    public void A_taunt_holds_its_hero_until_it_ends()
    {
        var roster = Awake();

        roster.Taunt(0, 2, 500);

        Assert.Equal(2, roster.TauntSeat(0, 499));
        Assert.Equal(-1, roster.TauntSeat(0, 500));
        Assert.Equal((2, AlertLevel.Alerted), (roster.Carried(0).TargetSeat, roster.Carried(0).Alert));
    }

    [Fact]
    public void A_debuff_on_what_a_robot_deals_holds_until_it_ends()
    {
        var roster = Awake();

        roster.Weaken(0, -20, 500, 100);
        roster.Weaken(0, -10, 600, 200);

        Assert.Equal(-20, roster.WeakenPct(0, 599));
        Assert.Equal(0, roster.WeakenPct(0, 600));
    }

    [Fact]
    public void A_new_load_starts_every_row_with_no_status_taunt_or_debuff()
    {
        var roster = Awake();
        roster.ApplyStatus(0, RowOf("stun"), 0, 0, 1);
        roster.Taunt(0, 2, 10_000);
        roster.Weaken(0, -20, 10_000, 0);

        roster.Load(Row(3), 1);

        Assert.Equal((0, -1, 0.0), (roster.StatusBits(0), roster.TauntSeat(0, 1), roster.WeakenPct(0, 1)));
        Assert.False(roster.Stunned(0));
    }

    [Fact]
    public void A_thousand_ticks_with_statuses_on_48_robots_allocate_nothing()
    {
        var roster = RosterTests.Rolled(Row(1, markers: 0));   // 48 robots of the fact's own, long-lived
        roster.WakeRoom(0);
        while (roster.Count < 48)
            roster.Add("giant_heavy", 1.0, 0, new PackMarker(Centre(0), 0), (ulong)roster.Count + 1);
        var due = new StatusDue[TheRules.StatusBook.Count];
        string[] ids = ["burn", "bleed", "slow", "marked", "stun"];
        void ApplyAll(long now)
        {
            for (int i = 0; i < 48; i++)
                foreach (string id in ids)
                    if (i % 2 == 0 || id is "burn" or "slow")   // the far half takes what it can live through for the fact
                        roster.ApplyStatus(i, RowOf(id), now, 0, 1 + i % 4);
        }

        int owed = 0;
        double dealt = 0;
        void TickAll(long now)
        {
            for (int i = 0; i < 48; i++)
            {
                if (i % 2 == 0)
                    owed += roster.RunStatuses(i, now, due);   // a body's: the caller deals it
                else
                    dealt += roster.TickStatuses(i, now);      // far: the row takes it
                _ = roster.StatusBits(i) + roster.SpeedFactor(i) + roster.DamageTakenFactor(i) + (roster.Stunned(i) ? 1 : 0);
            }
        }

        long now = 0;
        long Window()   // a thousand ticks; the bytes this thread allocated over them
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 1000; n++)
            {
                if (++now % 100 == 0) ApplyAll(now);
                TickAll(now);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        ApplyAll(0);
        Window();   // warm: the runtime's own first-run work is not the tick's
        owed = 0;
        dealt = 0;
        long allocated = Window();

        Assert.True(owed > 1000 && dealt > 0, $"owed {owed}, dealt {dealt}: the statuses did not tick");
        Assert.Equal(0, allocated);
    }
}
