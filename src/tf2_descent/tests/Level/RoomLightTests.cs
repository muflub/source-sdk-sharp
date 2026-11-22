using System.Buffers.Binary;
using System.Text;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// Each room's light from the level's <c>.map2d</c> (the <c>LITE</c> section tools/descent-room-light
/// writes; plan_descent_mine_levels 1.3e), over the committed depth 1 seed 1 fixture rewritten with
/// a section as the tool lays one out: the directory after the 96-byte header, each section on a
/// four-byte boundary.
/// </summary>
public sealed class RoomLightTests
{
    private static readonly LevelFixture Fixture = LevelFixture.Committed();

    /// <summary>The fixture's sections, in directory order.</summary>
    private static List<(string Tag, byte[] Bytes)> Sections(byte[] map2d)
    {
        int header = BinaryPrimitives.ReadInt32LittleEndian(map2d.AsSpan(12));
        int count = BinaryPrimitives.ReadInt32LittleEndian(map2d.AsSpan(16));
        var list = new List<(string, byte[])>();
        for (int s = 0; s < count; s++)
        {
            var e = map2d.AsSpan(header + s * RoomLight.EntryBytes);
            int off = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[4..]), len = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
            list.Add((Encoding.ASCII.GetString(e[..4]), map2d.AsSpan(off, len).ToArray()));
        }

        return list;
    }

    /// <summary>The file laid out again with <paramref name="sections"/>, as tools/descent-room-light writes it.</summary>
    private static byte[] Write(byte[] original, List<(string Tag, byte[] Bytes)> sections)
    {
        static int Align(int n) => (n + 3) & ~3;
        int at = RoomLight.HeaderBytes + sections.Count * RoomLight.EntryBytes;
        var offsets = new List<int>();
        foreach (var (_, b) in sections)
        {
            at = Align(at);
            offsets.Add(at);
            at += b.Length;
        }

        var file = new byte[at];
        original.AsSpan(0, RoomLight.HeaderBytes).CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), sections.Count);
        for (int s = 0; s < sections.Count; s++)
        {
            var e = file.AsSpan(RoomLight.HeaderBytes + s * RoomLight.EntryBytes);
            Encoding.ASCII.GetBytes(sections[s].Tag).CopyTo(e);
            BinaryPrimitives.WriteUInt32LittleEndian(e[4..], (uint)offsets[s]);
            BinaryPrimitives.WriteUInt32LittleEndian(e[8..], (uint)sections[s].Bytes.Length);
            sections[s].Bytes.CopyTo(file.AsSpan(offsets[s]));
        }

        return file;
    }

    private static byte[] Lite(uint version, IReadOnlyList<(float Light, uint Flags)> rooms, int? count = null)
    {
        var b = new byte[8 + 8 * rooms.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(b, version);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)(count ?? rooms.Count));
        for (int i = 0; i < rooms.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8 + 8 * i), rooms[i].Light);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12 + 8 * i), rooms[i].Flags);
        }

        return b;
    }

    /// <summary>The fixture with a LITE section of <paramref name="lite"/> appended to its directory.</summary>
    private static LevelData ReadWith(byte[] lite)
    {
        var sections = Sections(Fixture.Map2d);
        sections.Add(("LITE", lite));
        return LevelReader.Read(Fixture.Files() with { Map2d = Write(Fixture.Map2d, sections) });
    }

    /// <summary>Room i's light: 50 + 7 i, measured; room 2 unmeasured (NaN); room 3 dark by design.</summary>
    private static List<(float, uint)> Rooms(int n) =>
        [.. Enumerable.Range(0, n).Select(i => i == 2 ? (float.NaN, 0u) : (50f + 7f * i, RoomLight.MeasuredFlag | (i == 3 ? RoomLight.DarkByDesignFlag : 0u)))];

    [Fact]
    public void The_fixture_carries_no_light_and_reads_as_before()
    {
        LevelData level = Fixture.Read();

        Assert.DoesNotContain(Sections(Fixture.Map2d), s => s.Tag == "LITE");
        Assert.All(level.Rooms, r => Assert.Null(r.Light));
        Assert.All(level.Rooms, r => Assert.False(r.DarkByDesign));
        Assert.Empty(level.Notes);
    }

    [Fact]
    public void A_LITE_section_gives_each_room_its_light_in_room_order()
    {
        int n = Fixture.Read().Rooms.Length;
        LevelData level = ReadWith(Lite(1, Rooms(n)));

        Assert.True(n > 4);
        Assert.Empty(level.Notes);
        for (int i = 0; i < n; i++)
            if (i != 2)
                Assert.Equal(50f + 7f * i, level.Rooms[i].Light);
    }

    [Fact]
    public void An_unmeasured_room_keeps_no_light()
    {
        LevelData level = ReadWith(Lite(1, Rooms(Fixture.Read().Rooms.Length)));

        Assert.Null(level.Rooms[2].Light);
        Assert.NotNull(level.Rooms[1].Light);
    }

    [Fact]
    public void A_room_flagged_dark_by_design_is_dark_by_design()
    {
        LevelData level = ReadWith(Lite(1, Rooms(Fixture.Read().Rooms.Length)));

        Assert.True(level.Rooms[3].DarkByDesign);
        Assert.Single(level.Rooms, r => r.DarkByDesign);
    }

    [Fact]
    public void A_section_of_another_version_is_refused_with_a_note()
    {
        LevelData level = ReadWith(Lite(2, Rooms(Fixture.Read().Rooms.Length)));

        Assert.All(level.Rooms, r => Assert.Null(r.Light));
        Assert.Contains(level.Notes, n => n.Contains("version 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_section_for_another_room_count_is_refused_with_a_note()
    {
        int n = Fixture.Read().Rooms.Length;
        LevelData level = ReadWith(Lite(1, Rooms(n - 1)));

        Assert.All(level.Rooms, r => Assert.Null(r.Light));
        Assert.Contains(level.Notes, note => note.Contains("room light is not read", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rewritten_fixture_still_reads_its_rooms_doors_and_markers()
    {
        LevelData plain = Fixture.Read();
        LevelData lit = ReadWith(Lite(1, Rooms(plain.Rooms.Length)));

        Assert.Equal(plain.Rooms.Select(r => (r.Name, r.Min, r.Max)), lit.Rooms.Select(r => (r.Name, r.Min, r.Max)));
        Assert.Equal(plain.Doors.Length, lit.Doors.Length);
        Assert.Equal(plain.Markers.Length, lit.Markers.Length);
    }
}
