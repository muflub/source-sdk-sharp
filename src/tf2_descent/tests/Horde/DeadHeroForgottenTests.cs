using SourceSharp.Ai;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The dead are forgotten for good (this lane; plan_descent_logic 3.3, 3.5): at a hero's death
/// nothing on the roster keeps his seat, so no robot carries him as a target into a new body, no
/// pack stays alerted to him, and a far pack converging on where he was walks home.
/// </summary>
public sealed class DeadHeroForgottenTests
{
    private const int Dead = 2, Alive = 3;

    private static RobotRoster Roster(HordeLevel? level = null) => RosterTests.Rolled(level ?? Row(4, markers: 2), 1);

    [Fact]
    public void A_row_targeting_the_dead_hero_forgets_him_where_and_when_and_its_alert()
    {
        var roster = Roster();
        roster.SetCarried(0, new CarriedMemory(Dead, Centre(2), 900, AlertLevel.Alerted, -1, 0));

        var (rows, _) = roster.ForgetSeat(Dead);

        Assert.Equal(1, rows);
        Assert.Equal(CarriedMemory.None, roster.Carried(0));
    }

    [Fact]
    public void A_row_targeting_another_hero_keeps_him()
    {
        var roster = Roster();
        var other = new CarriedMemory(Alive, Centre(2), 900, AlertLevel.Alerted, Dead, 40);
        roster.SetCarried(0, other);

        roster.ForgetSeat(Dead);

        Assert.Equal(other with { ThreatSeat = -1, Threat = 0 }, roster.Carried(0));   // only the dead hero's threat entry goes
    }

    [Fact]
    public void A_taunt_by_the_dead_hero_ends()
    {
        var roster = Roster();
        roster.Taunt(0, Dead, until: 5000);
        Assert.Equal(Dead, roster.TauntSeat(0, 1000));

        roster.ForgetSeat(Dead);

        Assert.Equal(-1, roster.TauntSeat(0, 1000));
    }

    [Fact]
    public void A_pack_alerted_to_the_dead_hero_calms_and_forgets_whose_contact_it_was()
    {
        var roster = Roster();
        roster.AlertPack(0, 1, Centre(1), 900, Dead);
        Assert.True(roster.PackAlertedToHero(0));

        var (_, packs) = roster.ForgetSeat(Dead);

        Assert.Equal(1, packs);
        Assert.Equal((AlertLevel.Calm, -1), (roster.PackAlert(0), roster.ContactSeat(0)));
    }

    [Fact]
    public void A_pack_alerted_by_something_nobody_made_is_not_his_to_calm()
    {
        var roster = Roster();
        roster.AlertPack(0, 1, Centre(1), 900, -1);

        roster.ForgetSeat(Dead);

        Assert.Equal(AlertLevel.Alerted, roster.PackAlert(0));
    }

    [Fact]
    public void No_seat_forgets_nothing()
    {
        var roster = Roster();
        roster.SetCarried(0, new CarriedMemory(-1, Centre(2), 900, AlertLevel.Suspicious, -1, 0));

        Assert.Equal((0, 0), roster.ForgetSeat(-1));
        Assert.Equal(AlertLevel.Suspicious, roster.Carried(0).Alert);
    }

    /// <summary>Six rooms in a row, the only pack in room 5; it heard the hero in room 4, the next room (the hearing's reach), and is walking there.</summary>
    private static (RobotRoster Roster, FarSimulation Sim, FakeSight Sight, Func<long> Tick) Converging()
    {
        var level = HordeLevel.Create(11, 4, Enumerable.Range(0, 6).Select(i => RowRoom(i, i == 5 ? 2 : 0)), Enumerable.Range(0, 5).Select(RowDoor));
        var roster = Roster(level);
        var sim = FarSimulation.For(TheRules.Content.Horde, level, RosterTests.TicksPerSecond);
        var sight = new FakeSight(level);
        long interval = FarSimulation.Interval(TheRules.Content.Horde, RosterTests.TicksPerSecond), now = 1000;
        long Tick()
        {
            now += interval;
            sim.Tick(roster, sight, [], now);
            return now;
        }

        now += interval;
        sim.Tick(roster, sight, [new SoundEvent(4, Centre(4), 5000, Dead)], now);
        for (int t = 0; t < 15; t++)
            Tick();
        return (roster, sim, sight, Tick);
    }

    [Fact]
    public void A_far_pack_converging_on_the_dead_heros_contact_turns_home()
    {
        var (roster, _, _, tick) = Converging();
        int first = roster.PackFirst(0);
        Assert.Equal(FarActivity.Converge, roster.Activity(first));   // the stimulus: on its way to him

        roster.ForgetSeat(Dead);
        tick();

        Assert.All(Enumerable.Range(first, roster.PackSize(0)), i => Assert.NotEqual(FarActivity.Converge, roster.Activity(i)));
    }

    [Fact]
    public void A_far_pack_that_forgot_him_walks_back_to_its_room()
    {
        var (roster, _, _, tick) = Converging();
        int first = roster.PackFirst(0);
        var pack = Enumerable.Range(first, roster.PackSize(0)).ToArray();
        Assert.Contains(pack, i => roster.Room(i) != 5);   // the control: the pack had left its room

        roster.ForgetSeat(Dead);
        for (int t = 0; t < 60; t++)
            tick();

        Assert.All(pack, i => Assert.Equal(5, roster.Room(i)));
    }
}
