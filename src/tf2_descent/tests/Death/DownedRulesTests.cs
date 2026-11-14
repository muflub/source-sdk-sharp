using TF2.Descent.Rules;
using TF2.Descent.Rules.Death;
using Xunit;
using Ev = TF2.Descent.Rules.Death.DownedEvent;
using Out = TF2.Descent.Rules.Death.DownedOutcome;

namespace TF2.Descent.Tests.Death;

/// <summary>
/// The downed state (plan_descent_logic RU10; plan_tf2_rpg 2g, R26), one fact a rule. The
/// party runs at 10 ticks a second so the plan's seconds read as ticks: 20 s is 200.
/// </summary>
public sealed class DownedRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static DownedRules Downed => Rules.Downed;
    private const double Rate = 10;

    private static DownedParty Party(params string[] members) => DownedRules.NewParty(Rate, members);

    private static DownedStep Step(DownedParty p, DownedEvent e, long tick) => Downed.Step(p, e, tick);

    /// <summary>A party of two with "a" down from tick <paramref name="at"/>.</summary>
    private static DownedParty WithADown(long at = 0) => Step(Party("a", "b"), new Ev.LethalDamage("a"), at).Party;

    [Fact]
    public void Solo_lethal_damage_kills_with_no_downed_state()
    {
        var step = Step(Party("a"), new Ev.LethalDamage("a"), 5);

        Assert.Equal(Standing.Dead, step.Party.Member("a").Standing);
        Assert.Equal([new Out.Died("a", DeathCause.NoOneStanding)], step.Outcomes.ToArray());
    }

    [Fact]
    public void Lethal_damage_with_another_member_up_puts_the_hero_down_for_20_seconds()
    {
        var step = Step(Party("a", "b"), new Ev.LethalDamage("a"), 50);

        Assert.Equal(Standing.Down, step.Party.Member("a").Standing);
        Assert.Equal([new Out.Downed("a", 50 + 200)], step.Outcomes.ToArray());
    }

    [Fact]
    public void Going_down_is_not_dying()
    {
        var step = Step(Party("a", "b"), new Ev.LethalDamage("a"), 0);

        Assert.DoesNotContain(step.Outcomes, o => o is Out.Died);
    }

    [Fact]
    public void Each_further_down_on_the_level_shortens_the_window_by_5_seconds()
    {
        var p = Party("a", "b");
        var windows = new List<long>();
        long tick = 0;
        for (int i = 0; i < 5; i++)
        {
            var down = Step(p, new Ev.LethalDamage("a"), tick);
            var until = Assert.IsType<Out.Downed>(Assert.Single(down.Outcomes)).BleedOutTick;
            windows.Add(until - tick);
            p = Step(down.Party, new Ev.ReviveStart("b", "a", 10), tick).Party;
            tick += 30;   // 3 s of held use
            p = Step(p, new Ev.Tick(), tick).Party;
            tick += 20;   // past the grace
            Assert.Equal(Standing.Up, p.Member("a").Standing);
        }

        Assert.Equal([200L, 150, 100, 50, 50], windows);
    }

    [Fact]
    public void The_window_never_falls_below_its_5_second_floor()
    {
        Assert.Equal(20, Downed.WindowSeconds(0));
        Assert.Equal(5, Downed.WindowSeconds(3));
        Assert.Equal(5, Downed.WindowSeconds(40));
    }

    [Fact]
    public void A_new_level_forgets_the_downs_of_the_last()
    {
        var p = WithADown();
        p = Step(p, new Ev.ReviveStart("b", "a", 10), 0).Party;
        p = Step(p, new Ev.Tick(), 30).Party;
        p = Step(p, new Ev.LevelChanged(), 100).Party;

        var down = Step(p, new Ev.LethalDamage("a"), 100);

        Assert.Equal(0, p.Member("a").DownsThisLevel);
        Assert.Equal([new Out.Downed("a", 300)], down.Outcomes.ToArray());
    }

    [Fact]
    public void A_window_that_lapses_kills_the_downed_hero()
    {
        var p = WithADown(0);

        var before = Step(p, new Ev.Tick(), 199);
        var lapse = Step(before.Party, new Ev.Tick(), 200);

        Assert.Equal(Standing.Down, before.Party.Member("a").Standing);
        Assert.Equal([new Out.Died("a", DeathCause.BledOut)], lapse.Outcomes.ToArray());
    }

    [Fact]
    public void Three_seconds_of_held_use_in_range_revives_at_30_percent_life_with_2_seconds_of_grace()
    {
        var p = Step(WithADown(0), new Ev.ReviveStart("b", "a", 96), 10).Party;

        var early = Step(p, new Ev.Tick(), 39);
        var done = Step(early.Party, new Ev.Tick(), 40);

        Assert.Empty(early.Outcomes);
        Assert.Equal([new Out.Revived("a", "b", 30, 40 + 20)], done.Outcomes.ToArray());
        Assert.Equal(Standing.Up, done.Party.Member("a").Standing);
    }

    [Fact]
    public void A_revive_from_beyond_96_units_is_refused()
    {
        var step = Step(WithADown(), new Ev.ReviveStart("b", "a", 96.5), 0);

        Assert.Equal([new Out.Refused(DownedRefusal.OutOfRange)], step.Outcomes.ToArray());
        Assert.Null(step.Party.Member("b").Reviving);
    }

    [Fact]
    public void Moving_out_of_range_interrupts_the_revive()
    {
        var p = Step(WithADown(), new Ev.ReviveStart("b", "a", 50), 0).Party;
        var moved = Step(p, new Ev.ReviverMoved("b", 120), 10);

        var later = Step(moved.Party, new Ev.Tick(), 40);

        Assert.Equal([new Out.ReviveInterrupted("b", "a", ReviveBreak.MovedAway)], moved.Outcomes.ToArray());
        Assert.Equal(Standing.Down, later.Party.Member("a").Standing);
    }

    [Fact]
    public void Damage_to_the_reviver_interrupts_the_revive()
    {
        var p = Step(WithADown(), new Ev.ReviveStart("b", "a", 50), 0).Party;
        var hit = Step(p, new Ev.Damaged("b"), 20);

        var later = Step(hit.Party, new Ev.Tick(), 40);

        Assert.Equal([new Out.ReviveInterrupted("b", "a", ReviveBreak.Damaged)], hit.Outcomes.ToArray());
        Assert.Equal(Standing.Down, later.Party.Member("a").Standing);
    }

    [Fact]
    public void Letting_go_of_use_interrupts_the_revive()
    {
        var p = Step(WithADown(), new Ev.ReviveStart("b", "a", 50), 0).Party;
        var let = Step(p, new Ev.ReviveStop("b"), 29);

        var later = Step(let.Party, new Ev.Tick(), 40);

        Assert.Equal([new Out.ReviveInterrupted("b", "a", ReviveBreak.LetGo)], let.Outcomes.ToArray());
        Assert.Equal(Standing.Down, later.Party.Member("a").Standing);
    }

    [Fact]
    public void Lethal_damage_during_grace_does_not_down_the_hero()
    {
        var p = Step(WithADown(), new Ev.ReviveStart("b", "a", 50), 0).Party;
        p = Step(p, new Ev.Tick(), 30).Party;   // revived, grace to tick 50

        var hit = Step(p, new Ev.LethalDamage("a"), 49);

        Assert.Equal([new Out.Shielded("a")], hit.Outcomes.ToArray());
        Assert.Equal(Standing.Up, hit.Party.Member("a").Standing);
    }

    [Fact]
    public void Grace_ends_two_seconds_after_the_revive()
    {
        var p = Step(WithADown(), new Ev.ReviveStart("b", "a", 50), 0).Party;
        p = Step(p, new Ev.Tick(), 30).Party;

        var hit = Step(p, new Ev.LethalDamage("a"), 50);

        Assert.True(DownedRules.InGrace(p.Member("a"), 49));
        Assert.False(DownedRules.InGrace(p.Member("a"), 50));
        Assert.IsType<Out.Downed>(Assert.Single(hit.Outcomes));
    }

    [Fact]
    public void When_the_last_standing_member_goes_down_everyone_down_dies()
    {
        var p = Step(Party("a", "b", "c"), new Ev.LethalDamage("a"), 0).Party;
        p = Step(p, new Ev.LethalDamage("b"), 10).Party;   // c still up: b goes down

        var last = Step(p, new Ev.LethalDamage("c"), 20);

        Assert.Equal(Standing.Down, p.Member("b").Standing);
        Assert.All(last.Party.Members, m => Assert.Equal(Standing.Dead, m.Standing));
        Assert.Contains(new Out.Died("c", DeathCause.NoOneStanding), last.Outcomes);
        Assert.Contains(new Out.Died("a", DeathCause.PartyFell), last.Outcomes);
        Assert.Contains(new Out.Died("b", DeathCause.PartyFell), last.Outcomes);
    }

    [Fact]
    public void When_the_last_standing_member_leaves_the_instance_everyone_down_dies()
    {
        var left = Step(WithADown(), new Ev.Left("b"), 10);

        Assert.Equal([new Out.Died("a", DeathCause.PartyFell)], left.Outcomes.ToArray());
        Assert.Equal(Standing.Up, left.Party.Member("b").Standing);
    }

    [Fact]
    public void A_member_not_yet_in_the_instance_does_not_count_as_standing()
    {
        var p = Step(Party("a", "b"), new Ev.Left("b"), 0).Party;

        var hit = Step(p, new Ev.LethalDamage("a"), 1);
        var arrived = Step(Step(p, new Ev.Arrived("b"), 1).Party, new Ev.LethalDamage("a"), 2);

        Assert.Equal([new Out.Died("a", DeathCause.NoOneStanding)], hit.Outcomes.ToArray());
        Assert.IsType<Out.Downed>(Assert.Single(arrived.Outcomes));
    }

    [Fact]
    public void A_downed_hero_is_not_finished_off_by_more_damage()
    {
        var hit = Step(WithADown(), new Ev.LethalDamage("a"), 10);

        Assert.Equal([new Out.Refused(DownedRefusal.NotUp)], hit.Outcomes.ToArray());
        Assert.Equal(Standing.Down, hit.Party.Member("a").Standing);
    }

    [Fact]
    public void A_revive_that_completes_on_the_tick_the_window_lapses_saves_the_hero()
    {
        var p = Step(WithADown(0), new Ev.ReviveStart("b", "a", 10), 170).Party;

        var at = Step(p, new Ev.Tick(), 200);

        Assert.Equal(Standing.Up, at.Party.Member("a").Standing);
        Assert.IsType<Out.Revived>(Assert.Single(at.Outcomes));
    }

    [Fact]
    public void A_down_hero_who_leaves_the_instance_dies()
    {
        var left = Step(WithADown(), new Ev.Left("a"), 10);

        Assert.Equal([new Out.Died("a", DeathCause.LeftWhileDown)], left.Outcomes.ToArray());
    }

    [Fact]
    public void A_downed_hero_cannot_revive_anyone()
    {
        var p = Step(Party("a", "b", "c"), new Ev.LethalDamage("a"), 0).Party;
        p = Step(p, new Ev.LethalDamage("b"), 0).Party;

        var step = Step(p, new Ev.ReviveStart("a", "b", 10), 1);

        Assert.Equal([new Out.Refused(DownedRefusal.NotUp)], step.Outcomes.ToArray());
    }

    [Fact]
    public void Seconds_become_whole_ticks_rounded_up_at_the_engines_rate()
    {
        var p = DownedRules.NewParty(66.666, "a", "b");

        var step = Downed.Step(p, new Ev.LethalDamage("a"), 0);

        Assert.Equal([new Out.Downed("a", 1334)], step.Outcomes.ToArray());   // 20 s at 66.666/s is 1333.32 ticks
    }

    [Fact]
    public void A_member_who_joins_is_up_and_counts_as_standing()
    {
        var p = Step(Party("a"), new Ev.Joined("b"), 0).Party;

        var step = Step(p, new Ev.LethalDamage("a"), 1);

        Assert.Equal(Standing.Up, step.Party.Member("b").Standing);
        Assert.Equal([new Out.Downed("a", 201)], step.Outcomes.ToArray());
    }

    [Fact]
    public void A_member_who_joins_again_keeps_its_downs()
    {
        var p = Step(WithADown(), new Ev.Tick(), 200).Party;   // a bled out
        p = Step(p, new Ev.Joined("a"), 201).Party;

        Assert.Equal(1, p.Member("a").DownsThisLevel);
        Assert.Equal(2, p.Members.Length);
    }

    [Fact]
    public void A_dead_member_who_respawns_stands_up_with_its_downs_kept()
    {
        var p = Step(WithADown(), new Ev.Tick(), 200).Party;

        var step = Step(p, new Ev.Respawned("a"), 300);

        Assert.Equal(Standing.Up, step.Party.Member("a").Standing);
        Assert.Equal(1, step.Party.Member("a").DownsThisLevel);
        Assert.Empty(step.Outcomes);
    }

    [Fact]
    public void A_member_who_is_not_dead_cannot_respawn()
    {
        var step = Step(WithADown(), new Ev.Respawned("a"), 1);

        Assert.Equal(Standing.Down, step.Party.Member("a").Standing);
        Assert.Equal([new Out.Refused(DownedRefusal.NotDead)], step.Outcomes.ToArray());
    }

    [Fact]
    public void A_kill_the_machine_did_not_decide_kills_a_downed_hero_outright()
    {
        var step = Step(WithADown(), new Ev.Killed("a"), 5);

        Assert.Equal(Standing.Dead, step.Party.Member("a").Standing);
        Assert.Equal([new Out.Died("a", DeathCause.Outright)], step.Outcomes.ToArray());
    }

    [Fact]
    public void An_outright_kill_of_the_last_one_up_takes_the_downed_with_him()
    {
        var step = Step(WithADown(), new Ev.Killed("b"), 5);

        Assert.Equal([new Out.Died("b", DeathCause.Outright), new Out.Died("a", DeathCause.PartyFell)], step.Outcomes.ToArray());
    }

    [Fact]
    public void A_dead_member_is_not_killed_again()
    {
        var p = Step(WithADown(), new Ev.Killed("a"), 5).Party;

        var step = Step(p, new Ev.Killed("a"), 6);

        Assert.Equal([new Out.Refused(DownedRefusal.AlreadyDead)], step.Outcomes.ToArray());
    }

    [Fact]
    public void Time_does_not_run_back()
    {
        var p = Step(Party("a", "b"), new Ev.Tick(), 100).Party;

        Assert.Throws<ArgumentOutOfRangeException>(() => Downed.Step(p, new Ev.Tick(), 99));
    }
}
