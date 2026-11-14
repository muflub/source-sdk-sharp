using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// The status rules over fixed slots (<see cref="StatusBook"/>, HO8): the same answers as
/// <see cref="StatusRules"/> for any sequence, a skill's own duration, the applier each slot
/// keeps, and a tick that allocates nothing. Ticks run at 10 a second.
/// </summary>
public sealed class StatusBookTests
{
    private static readonly TF2DescentRules Rules = new();
    private static StatusBook Book => Rules.StatusBook;
    private const double Rate = 10;

    private static StatusSlot[] Slots() => new StatusSlot[Book.Count];

    private static string Describe(RobotStatuses s) =>
        string.Join(";", s.Active.Select(a => $"{a.Id}x{a.Stacks}@{a.EndTick}/{a.NextDamageTick}"));

    [Fact]
    public void Any_sequence_of_applies_and_ticks_matches_the_status_rules()
    {
        var roll = new Random(8);
        string[] ids = [.. Rules.Content.Status.Rows.Select(r => r.Id)];
        int compared = 0, damaged = 0;
        for (int run = 0; run < 200; run++)
        {
            string? immunity = run % 5 == 0 ? "fire" : run % 7 == 0 ? "bullet" : null;
            var model = RobotStatuses.None;
            var slots = Slots();
            var due = new StatusDue[Book.Count];
            long now = 0, stamp = 0;
            for (int step = 0; step < 60; step++)
            {
                now += roll.Next(12);
                if (roll.Next(3) == 0)
                {
                    string id = ids[roll.Next(ids.Length)];
                    var (after, what) = Rules.Statuses.Apply(model, id, now, Rate, immunity);
                    model = after;
                    Assert.Equal(what, Book.Apply(slots, Book.RowOf(id), now, Rate, immunity, 0, 1, stamp++));
                }
                else
                {
                    var tick = Rules.Statuses.Tick(model, now, Rate);
                    model = tick.After;
                    int n = Book.Tick(slots, now, Rate, due);
                    foreach (var row in Rules.Content.Status.Rows)
                    {
                        double want = tick.Damage.Where(d => d.Status == row.Id).Sum(d => d.Amount);
                        double got = due[..n].ToArray().Where(d => Book.Row(d.Row).Id == row.Id).Sum(d => d.Amount);
                        Assert.Equal(want, got, 9);
                        if (want > 0) damaged++;
                    }
                }

                Assert.Equal(Describe(model), Describe(Book.View(slots)));
                Assert.Equal(Rules.Statuses.SpeedMultiplier(model, now), Book.SpeedMultiplier(slots, now), 12);
                Assert.Equal(Rules.Statuses.DamageTakenMultiplier(model, now), Book.DamageTakenMultiplier(slots, now), 12);
                Assert.Equal(Rules.Statuses.Stunned(model, now), Book.Stunned(slots, now));
                compared++;
            }
        }

        Assert.True(compared > 10_000 && damaged > 500, $"compared {compared}, damaged {damaged}");
    }

    [Fact]
    public void A_skill_names_its_own_duration()
    {
        var slots = Slots();
        int stun = Book.RowOf("stun");
        Assert.Equal(2, Book.Row(stun).DurationSeconds);

        Book.Apply(slots, stun, 100, Rate, null, 1.5, 3, 0);

        Assert.Equal(115, slots[stun].EndTick);
        Assert.True(Book.Stunned(slots, 114));
        Assert.False(Book.Stunned(slots, 115));
    }

    [Fact]
    public void No_duration_takes_the_rows()
    {
        var slots = Slots();
        int stun = Book.RowOf("stun");

        Book.Apply(slots, stun, 100, Rate, null, 0, 3, 0);

        Assert.Equal(120, slots[stun].EndTick);
    }

    [Fact]
    public void A_damage_tick_names_the_hero_who_applied_it_last()
    {
        var slots = Slots();
        var due = new StatusDue[Book.Count];
        int burn = Book.RowOf("burn");
        Book.Apply(slots, burn, 0, Rate, null, 0, 4, 0);
        Book.Apply(slots, burn, 2, Rate, null, 0, 7, 1);

        int n = Book.Tick(slots, 5, Rate, due);

        Assert.Equal(1, n);
        Assert.Equal(new StatusDue(burn, 4, 1, 7), due[0]);
    }

    [Fact]
    public void Two_damage_ticks_owed_at_once_are_one_entry_of_both()
    {
        var slots = Slots();
        var due = new StatusDue[Book.Count];
        int burn = Book.RowOf("burn");
        Book.Apply(slots, burn, 0, Rate, null, 0, 4, 0);

        int n = Book.Tick(slots, 10, Rate, due);

        Assert.Equal(1, n);
        Assert.Equal((8.0, 2), (due[0].Amount, due[0].Ticks));
    }

    [Fact]
    public void An_immune_robot_refuses_the_damaging_status_of_its_type_and_keeps_none()
    {
        var slots = Slots();

        Assert.Equal(StatusApplied.Immune, Book.Apply(slots, Book.RowOf("burn"), 0, Rate, "fire", 0, 1, 0));
        Assert.Equal(0, StatusBook.Bits(slots));
    }

    [Fact]
    public void Bits_carry_one_bit_a_carried_status_by_row()
    {
        var slots = Slots();
        Book.Apply(slots, Book.RowOf("slow"), 0, Rate, null, 0, 1, 0);
        Book.Apply(slots, Book.RowOf("burn"), 0, Rate, null, 0, 1, 1);

        Assert.Equal((1 << Book.RowOf("slow")) | (1 << Book.RowOf("burn")), StatusBook.Bits(slots));
    }

    [Fact]
    public void A_view_loads_back_into_the_same_slots_and_keeps_the_appliers()
    {
        var slots = Slots();
        Book.Apply(slots, Book.RowOf("marked"), 0, Rate, null, 0, 2, 5);
        Book.Apply(slots, Book.RowOf("bleed"), 1, Rate, null, 0, 3, 6);
        var view = Book.View(slots);

        long next = Book.Load(slots, view, 40);

        Assert.Equal(Describe(view), Describe(Book.View(slots)));
        Assert.Equal((2, 3), (slots[Book.RowOf("marked")].Applier, slots[Book.RowOf("bleed")].Applier));
        Assert.Equal(42, next);
    }

    [Fact]
    public void A_thousand_ticks_of_every_status_allocate_nothing()
    {
        var slots = new StatusSlot[48 * Book.Count];
        var due = new StatusDue[Book.Count];
        void ApplyAll(long now)
        {
            for (int robot = 0; robot < 48; robot++)
                for (int row = 0; row < Book.Count; row++)
                    Book.Apply(slots.AsSpan(robot * Book.Count, Book.Count), row, now, Rate, null, 0, 1, now);
        }

        int dealt = 0;
        void TickAll(long now)
        {
            for (int robot = 0; robot < 48; robot++)
            {
                var mine = slots.AsSpan(robot * Book.Count, Book.Count);
                dealt += Book.Tick(mine, now, Rate, due);
                _ = Book.SpeedMultiplier(mine, now) + Book.DamageTakenMultiplier(mine, now) + (Book.Stunned(mine, now) ? 1 : 0)
                    + StatusBook.Bits(mine) + Book.NextDue(mine);
            }
        }

        long now = 0;
        long Window()   // a thousand ticks; the bytes this thread allocated over them
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 1000; n++)
            {
                if (++now % 50 == 0) ApplyAll(now);
                TickAll(now);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        ApplyAll(0);
        Window();   // warm: the runtime's own first-run work is not the tick's
        long allocated = Window();

        Assert.True(dealt > 10_000, $"only {dealt} damage entries: the stimulus did not reach the tick");
        Assert.Equal(0, allocated);
    }
}
