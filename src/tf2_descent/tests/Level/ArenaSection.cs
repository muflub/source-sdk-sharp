using System.Buffers.Binary;
using System.Text;
using SourceSharp.Abi;
using TF2.Descent.Server.Level;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// A synthetic <c>ARNA</c> section, written the way the contract with the map session lays it out
/// (uint32 version; the spot as float32 x, y, z, yaw; uint32 track count, then x, y, z, uint32
/// flags a point; uint32 mouth count, then x, y, z, yaw, uint32 leg a mouth), and a <c>.map2d</c>
/// rewritten with it in its directory as the tools lay sections out (after the 96-byte header,
/// each on a four-byte boundary).
/// </summary>
internal static class ArenaSection
{
    public static byte[] Bytes(Vector spot, float yaw, IReadOnlyList<(Vector At, uint Flags)> track,
        IReadOnlyList<(Vector At, float Yaw, uint Leg)> mouths, uint version = ArenaLayout.Version)
    {
        var b = new byte[4 + 16 + 4 + 16 * track.Count + 4 + 20 * mouths.Count];
        int at = 0;
        void U(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v); at += 4; }
        void F(float v) { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), v); at += 4; }
        void V(Vector v) { F(v.X); F(v.Y); F(v.Z); }
        U(version);
        V(spot);
        F(yaw);
        U((uint)track.Count);
        foreach (var (p, flags) in track)
        {
            V(p);
            U(flags);
        }

        U((uint)mouths.Count);
        foreach (var (p, y, leg) in mouths)
        {
            V(p);
            F(y);
            U(leg);
        }

        return b;
    }

    /// <summary>The file's sections, in directory order.</summary>
    public static List<(string Tag, byte[] Bytes)> Sections(byte[] map2d)
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

    /// <summary><paramref name="map2d"/> laid out again with <paramref name="tag"/>'s bytes added to its sections.</summary>
    public static byte[] With(byte[] map2d, string tag, byte[] bytes)
    {
        var sections = Sections(map2d);
        sections.Add((tag, bytes));
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
        map2d.AsSpan(0, RoomLight.HeaderBytes).CopyTo(file);
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
}
