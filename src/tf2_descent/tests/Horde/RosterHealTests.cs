using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>A medic's heal on the roster (HO6) and a robot put into the newest pack by hand.</summary>
public sealed class RosterHealTests
{
    private static (RobotRoster Roster, RobotId Id) One()
    {
        var roster = RobotRoster.Create(TheRules, 66);
        roster.Load(Row(2, markers: 0), 1);
        var id = roster.Add("soldier", 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(Centre(0), 0f), 9);
        return (roster, id);
    }

    [Fact]
    public void A_heal_gives_life_back_and_answers_what_it_gave()
    {
        var (roster, id) = One();
        double most = roster.Stats(id.Index).Life;
        roster.Hurt(id.Index, 60);

        double gained = roster.Heal(id.Index, 25);

        Assert.Equal(25, gained, 9);
        Assert.Equal(most - 35, roster.Life(id.Index), 9);
    }

    [Fact]
    public void A_heal_stops_at_the_robots_most_life()
    {
        var (roster, id) = One();
        double most = roster.Stats(id.Index).Life;
        roster.Hurt(id.Index, 10);

        double gained = roster.Heal(id.Index, 1000);

        Assert.Equal(10, gained, 9);
        Assert.Equal(most, roster.Life(id.Index), 9);
    }

    [Fact]
    public void A_dead_robot_is_not_healed()
    {
        var (roster, id) = One();
        roster.Hurt(id.Index, 1e6);

        Assert.Equal(0, roster.Heal(id.Index, 50));
        Assert.Equal(0, roster.Life(id.Index));
    }

    [Fact]
    public void A_robot_added_to_the_newest_pack_joins_it()
    {
        var (roster, id) = One();
        var mate = roster.Add("medic", 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(Centre(0), 0f), 10, joinLastPack: true);
        var alone = roster.Add("medic", 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(Centre(0), 0f), 11);

        Assert.Equal(roster.Pack(id.Index), roster.Pack(mate.Index));
        Assert.NotEqual(roster.Pack(id.Index), roster.Pack(alone.Index));
        Assert.Equal(2, roster.PackSize(roster.Pack(id.Index)));
    }
}
