using System.Globalization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Level 2's track (the review of 2026-11-10): it runs to each door's cell
/// face where the door is joined, so two rooms' track meets with no gap; a
/// buffer stop ends it where the door is capped; a run has no gap inside it;
/// and only the tunnels and yards carry it.
/// </summary>
public sealed class DescentTrackTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Haulage => libs.Dressed(2, 0);

    private static readonly Dictionary<string, PropModel> Models = new[]
    {
        ActOne.Track, ActOne.Track128, ActOne.Track64, ActOne.TrackBend, ActOne.TrackStop,
    }.ToDictionary(m => m.Model);

    private static IEnumerable<RoomEntity> Rails(Room room) =>
        room.Entities.Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Rail));

    private static int Yaw(RoomEntity e) => int.Parse(e.Key("angles")!.Split(' ')[1], CultureInfo.InvariantCulture);

    /// <summary>A straight piece's extent along its own axis and where it stands across it.</summary>
    private static (bool AlongX, float From, float To, float Across) Span(RoomEntity e)
    {
        PropModel m = Models[e.Key("model")!];
        float half = (m.Maxs.Y - m.Mins.Y) / 2;
        Point o = e.Origin!.Value;
        bool alongX = Yaw(e) % 180 == 90;
        return alongX ? (true, o.X - half, o.X + half, o.Y) : (false, o.Y - half, o.Y + half, o.X);
    }

    private static bool Straight(RoomEntity e) => e.Key("model") is { } m && m.Contains("track_straight", StringComparison.Ordinal);

    private IEnumerable<Room> Tracked() =>
        Haulage.Select(r => r.Dressed).Where(r => ActOne.Haulage.Props.RailFamilies.Contains(RoomDresser.Family(r.Name)));

    [Fact]
    public void A_joined_doors_track_reaches_its_cell_face()
    {
        var rooms = Tracked().ToArray();
        Assert.NotEmpty(rooms);
        foreach (Room room in rooms)
        {
            // Every socket: a larger room's (the yard hall's) as a room of one cell's.
            float c = room.Extent;
            foreach (var (face, offset) in room.Sockets)
            {
                var doorway = Rails(room).Where(e => e.Key("room_needs") == "joined_" + room.SocketName(face, offset) && Straight(e)).Select(Span).ToArray();
                bool reaches = doorway.Any(s => face switch
                {
                    CellFace.West or CellFace.South => Math.Abs(s.From) < 0.01f,
                    _ => Math.Abs(s.To - c) < 0.01f,
                });
                Assert.True(reaches, $"{room.Name}: no track to its {face} cell face where the door is joined");
            }
        }
    }

    [Fact]
    public void A_capped_doors_track_ends_in_a_buffer_stop()
    {
        foreach (Room room in Tracked())
            foreach (var (face, offset) in room.Sockets)
                Assert.Contains(Rails(room), e => e.Key("model") == ActOne.TrackStop.Model && e.Key("room_needs") == "!joined_" + room.SocketName(face, offset));
    }

    [Fact]
    public void A_run_of_track_face_to_face_has_no_gap_but_at_a_buffer()
    {
        int runs = 0;
        // A room of one cell's line face to face; a yard hall's lines are half-runs kept per socket (DescentHallDressingTests).
        foreach (Room room in Tracked().Where(r => r.Cells == 1))
        {
            float c = room.Kit.Cell, m = c / 2;
            foreach (bool alongX in new[] { true, false })
            {
                bool through = alongX ? room.Doors.Contains(CellFace.West) && room.Doors.Contains(CellFace.East)
                                      : room.Doors.Contains(CellFace.South) && room.Doors.Contains(CellFace.North);
                if (!through)
                    continue;
                runs++;
                var spans = Rails(room).Where(Straight).Select(Span).Where(s => s.AlongX == alongX && Math.Abs(s.Across - m) < 1)
                    .OrderBy(s => s.From).ToArray();
                var stops = Rails(room).Where(e => e.Key("model") == ActOne.TrackStop.Model && e.Key("room_needs") is null)
                    .Select(e => alongX ? e.Origin!.Value.X : e.Origin!.Value.Y).ToArray();
                float reached = 0;
                foreach (var s in spans)
                {
                    if (s.From > reached + 8)
                        Assert.True(stops.Any(z => z > reached - 40 && z < s.From + 40),
                            $"{room.Name}: a gap from {reached} to {s.From} in its track with no buffer stop at its ends");
                    reached = Math.Max(reached, s.To);
                }

                Assert.True(reached >= c - 0.01f, $"{room.Name}: its track along {(alongX ? "x" : "y")} stops at {reached} of {c}");
            }
        }

        Assert.True(runs > 0);
    }

    [Fact]
    public void Only_the_tunnels_and_yards_carry_track()
    {
        PropSet props = ActOne.Haulage.Props;
        var untracked = Haulage.Select(r => r.Dressed).Where(r => !props.RailFamilies.Contains(RoomDresser.Family(r.Name))
                                                                  && !props.StubFamilies.Contains(RoomDresser.Family(r.Name))).ToArray();

        Assert.NotEmpty(untracked);
        Assert.All(untracked, r => Assert.Empty(Rails(r)));
    }

    [Fact]
    public void A_store_or_stope_has_track_from_one_door_only()
    {
        PropSet props = ActOne.Haulage.Props;
        var stubs = Haulage.Select(r => r.Dressed).Where(r => props.StubFamilies.Contains(RoomDresser.Family(r.Name)) && r.Doors.Count > 1).ToArray();

        Assert.NotEmpty(stubs);
        Assert.All(stubs, r => Assert.Single(Rails(r), e => e.Key("room_needs") is { } n && n.StartsWith("joined_", StringComparison.Ordinal)));
    }

    [Fact]
    public void Two_doors_at_a_corner_are_joined_by_a_curve()
    {
        var corners = Tracked().Where(r => r.Doors.Count == 2 && !(r.Doors.Contains(CellFace.West) && r.Doors.Contains(CellFace.East))
                                           && !(r.Doors.Contains(CellFace.South) && r.Doors.Contains(CellFace.North))).ToArray();

        Assert.NotEmpty(corners);
        Assert.Contains(corners, r => Rails(r).Any(e => e.Key("model") == ActOne.TrackBend.Model));
    }
}
