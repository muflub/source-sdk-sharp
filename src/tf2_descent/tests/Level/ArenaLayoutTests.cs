using SourceSharp.Abi;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// The boss arena's layout from the level's <c>.map2d</c> (the <c>ARNA</c> section agreed with the
/// map session on 2026-11-16), over the committed depth 1 seed 1 fixture rewritten with a synthetic
/// section (<see cref="ArenaSection"/>): the spot and its yaw, the track in file order with its
/// flags, the mouths with their legs; a level without the section reads as before.
/// </summary>
public sealed class ArenaLayoutTests
{
    private static readonly LevelFixture Fixture = LevelFixture.Committed();

    public static readonly (Vector At, uint Flags)[] Track =
    [
        (new Vector(100, 200, 16), ArenaLayout.StationFlag),
        (new Vector(300, 200, 16), 0),
        (new Vector(500, 200, 16), ArenaLayout.StationFlag),
        (new Vector(400, 400, 16), ArenaLayout.StationFlag | ArenaLayout.FinalFlag),
    ];

    public static readonly (Vector At, float Yaw, uint Leg)[] Mouths =
    [
        (new Vector(-50, 0, 16), 90, 1),
        (new Vector(50, 0, 16), 270, 2),
        (new Vector(0, 50, 16), 180, 3),
    ];

    private static LevelData ReadWith(byte[] arna) =>
        LevelReader.Read(Fixture.Files() with { Map2d = ArenaSection.With(Fixture.Map2d, "ARNA", arna) });

    private static byte[] Standard(uint version = ArenaLayout.Version) =>
        ArenaSection.Bytes(new Vector(250, 300, 24), 135, Track, Mouths, version);

    [Fact]
    public void A_level_without_the_section_has_no_arena_layout_and_no_note()
    {
        LevelData level = Fixture.Read();

        Assert.DoesNotContain(ArenaSection.Sections(Fixture.Map2d), s => s.Tag == "ARNA");
        Assert.Null(level.Arena);
        Assert.Empty(level.Notes);
    }

    [Fact]
    public void The_section_gives_the_boss_s_spot_and_the_way_it_faces()
    {
        ArenaLayout layout = ReadWith(Standard()).Arena!;

        Assert.Equal(new Vector(250, 300, 24), layout.Spot);
        Assert.Equal(135f, layout.SpotYaw);
    }

    [Fact]
    public void The_track_keeps_the_file_s_order_and_flags()
    {
        ArenaLayout layout = ReadWith(Standard()).Arena!;

        Assert.Equal(Track.Select(t => t.At), layout.Track.Select(p => p.Position));
        Assert.Equal([true, false, true, true], layout.Track.Select(p => p.Station));
        Assert.Equal([false, false, false, true], layout.Track.Select(p => p.Final));
        Assert.Equal(3, layout.Stations);
    }

    [Fact]
    public void Each_mouth_keeps_its_place_facing_and_leg()
    {
        ArenaLayout layout = ReadWith(Standard()).Arena!;

        Assert.Equal(Mouths.Select(m => (m.At, m.Yaw, (int)m.Leg)), layout.Mouths.Select(m => (m.Position, m.Yaw, m.Leg)));
    }

    [Fact]
    public void Empty_counts_are_allowed()
    {
        LevelData level = ReadWith(ArenaSection.Bytes(new Vector(1, 2, 3), 0, [], []));

        Assert.NotNull(level.Arena);
        Assert.Empty(level.Arena!.Track);
        Assert.Empty(level.Arena.Mouths);
        Assert.Empty(level.Notes);
    }

    [Fact]
    public void A_section_of_another_version_is_refused_with_a_note()
    {
        LevelData level = ReadWith(Standard(version: 2));

        Assert.Null(level.Arena);
        Assert.Contains(level.Notes, n => n.Contains("arena layout is not read", StringComparison.Ordinal) && n.Contains("version 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_section_whose_counts_do_not_fit_its_bytes_is_refused_with_a_note()
    {
        byte[] bytes = Standard();
        LevelData level = ReadWith(bytes[..^4]);

        Assert.Null(level.Arena);
        Assert.Contains(level.Notes, n => n.Contains("arena layout is not read", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mouth_on_leg_zero_is_refused_with_a_note()
    {
        LevelData level = ReadWith(ArenaSection.Bytes(new Vector(1, 2, 3), 0, [], [(new Vector(0, 0, 0), 0, 0)]));

        Assert.Null(level.Arena);
        Assert.Contains(level.Notes, n => n.Contains("legs count from 1", StringComparison.Ordinal));
    }

    [Fact]
    public void The_load_line_names_the_layout_only_when_the_level_ships_one()
    {
        Assert.Contains("arena layout: boss at (250 300 24) yaw 135, 4 track points (3 stations), 3 mouths",
            LevelSummary.LoadLine(ReadWith(Standard()), TimeSpan.Zero), StringComparison.Ordinal);
        Assert.DoesNotContain("arena layout", LevelSummary.LoadLine(Fixture.Read(), TimeSpan.Zero), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rewritten_fixture_still_reads_its_rooms_doors_and_markers()
    {
        LevelData plain = Fixture.Read();
        LevelData with = ReadWith(Standard());

        Assert.Equal(plain.Rooms.Select(r => (r.Name, r.Min, r.Max)), with.Rooms.Select(r => (r.Name, r.Min, r.Max)));
        Assert.Equal(plain.Doors.Length, with.Doors.Length);
        Assert.Equal(plain.Markers.Select(m => m.Position), with.Markers.Select(m => m.Position));
    }
}
