using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>The admin menu's Kill entries (<c>tf2_descent_kill_robots</c>): the living robots of a room, or of the level.</summary>
public sealed class KillRobotsTests
{
    /// <summary>Two rooms: two soldiers in room 0, one medic in room 1, and a third soldier of room 0 dead.</summary>
    private static RobotRoster Roster()
    {
        var roster = RobotRoster.Create(TheRules, 66);
        roster.Load(Row(2, markers: 0), 1);
        roster.Add("soldier", 1.0, 0, new PackMarker(Centre(0), 0f), 9);
        roster.Add("soldier", 1.0, 0, new PackMarker(Centre(0), 0f), 10);
        roster.Add("medic", 1.0, 1, new PackMarker(Centre(1), 0f), 11);
        var dead = roster.Add("soldier", 1.0, 0, new PackMarker(Centre(0), 0f), 12);
        roster.Hurt(dead.Index, 1e6);
        return roster;
    }

    [Fact]
    public void A_room_names_its_living_robots_only()
    {
        var roster = Roster();

        var rows = KillRobotsCommand.Living(roster, 0);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, roster.Room(r)));
        Assert.All(rows, r => Assert.NotEqual(RobotState.Dead, roster.State(r)));
    }

    [Fact]
    public void The_level_names_every_living_robot()
    {
        var roster = Roster();

        Assert.Equal(3, KillRobotsCommand.Living(roster, null).Count);
    }
}
