using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Director;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;
using static TF2.Descent.Tests.Horde.HordeNavDataTests;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The hulls' floors on depth 1 seed 1 (<see cref="NavHullReach"/>): a giant of scale 1.6 and up is
/// cut off in rooms 6, 16 and 17 (<see cref="HordeNavDataTests"/>), so the spawn director never
/// posts one there; a smaller robot is posted there as before.
/// </summary>
public sealed class NavHullReachTests
{
    private static AiVector Middle(int room)
    {
        var r = FixtureLevel.Rooms[room];
        foreach (var m in r.Markers)
            return m.Position;
        return (r.Min + r.Max) * 0.5f;
    }

    [Fact]
    public void A_giant_in_a_cut_off_room_does_not_join_and_one_at_the_arrival_does()
    {
        var reach = new NavHullReach(Nav);

        Assert.True(reach.Joins(FixtureLevel.RoomAt(Arrival), Arrival, 1.75));
        foreach (int room in new[] { 6, 16, 17 })
        {
            Assert.False(reach.Joins(room, Middle(room), 1.75), $"a giant of 1.75 joins from room {room}");
            Assert.False(reach.Joins(room, Middle(room), 1.9), $"a giant of 1.9 joins from room {room}");
            Assert.True(reach.Joins(room, Middle(room), 1.5), $"a giant of 1.5 does not join from room {room}");
            Assert.True(reach.Joins(room, Middle(room), 1.0), $"an ordinary robot does not join from room {room}");
        }
    }

    /// <summary>The level's rooms rolled over many seeds at depth 5: with the navigation's reach, no robot above 1.5 stands in rooms 6, 16 or 17; without it, some do.</summary>
    [Fact]
    public void The_director_over_depth_1s_rooms_posts_no_giant_where_its_floor_is_cut_off()
    {
        var reach = new NavHullReach(Nav);
        var director = new SpawnDirector(TheRules);
        int without = 0, smallThere = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            // Depth 1 rolls no giant: the same rooms and doors at depth 5, where giants roll.
            var level = HordeLevel.Create(seed, 5, FixtureLevel.Rooms, FixtureLevel.Doors);
            foreach (var r in director.Plan(level, 4).Packs.SelectMany(p => p.Robots))
                if (r.Room is 6 or 16 or 17 && r.Roll.Scale > 1.5)
                    without++;
            foreach (var r in director.Plan(level, 4, reach).Packs.SelectMany(p => p.Robots).Where(r => r.Room is 6 or 16 or 17))
            {
                Assert.True(r.Roll.Scale <= 1.5, $"seed {seed}: {r.Roll.Template} at {r.Roll.Scale} posted in room {r.Room}");
                smallThere++;
            }
        }

        Assert.True(without > 0, "the control: no robot above 1.5 was posted in rooms 6, 16 or 17 with no reach");
        Assert.True(smallThere > 100, $"{smallThere} robots in rooms 6, 16 and 17");
    }
}
