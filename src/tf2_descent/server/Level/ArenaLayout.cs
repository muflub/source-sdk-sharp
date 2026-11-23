using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using SourceSharp.Abi;

namespace TF2.Descent.Server.Level;

/// <summary>A point of the Tank's track as the level ships it: where, and its flags (<see cref="ArenaLayout.StationFlag"/>, <see cref="ArenaLayout.FinalFlag"/>).</summary>
public readonly record struct ArenaTrackPoint(Vector Position, uint Flags)
{
    /// <summary>The Tank stops here and drills (bit 0).</summary>
    public bool Station => (Flags & ArenaLayout.StationFlag) != 0;

    /// <summary>The central pillar's station: the roof (bit 1).</summary>
    public bool Final => (Flags & ArenaLayout.FinalFlag) != 0;
}

/// <summary>A mouth adds come out of: where, the way it faces (degrees), and the boss's phase that opens it (1-based; leg 1 is open from the start).</summary>
public readonly record struct ArenaMouth(Vector Position, float Yaw, int Leg);

/// <summary>
/// A boss arena's layout as the level's <c>.map2d</c> carries it (the contract agreed with the
/// map session on 2026-11-16): the boss's spot and the way it faces at the intro, the Tank's
/// track in the file's order (the loop closes back to point 0), and the mouths its adds come out
/// of, by the phase that opens each. An empty track or no mouths is allowed: each piece the
/// layout leaves empty falls back to the arena's markers.
/// </summary>
public sealed record ArenaLayout(Vector Spot, float SpotYaw, ImmutableArray<ArenaTrackPoint> Track, ImmutableArray<ArenaMouth> Mouths)
{
    public const uint Version = 1;
    public const uint StationFlag = 1;
    public const uint FinalFlag = 2;

    /// <summary>A track point's bytes (three float32 and a uint32 of flags) and a mouth's (four float32 and a uint32 leg).</summary>
    public const int PointBytes = 16, MouthBytes = 20;

    /// <summary>The section's tag, as the directory spells it.</summary>
    public static ReadOnlySpan<byte> Tag => "ARNA"u8;

    /// <summary>The stations on the track, in file order.</summary>
    public int Stations => Track.Count(p => p.Station);

    /// <summary>
    /// The arena's layout from <paramref name="map2d"/>'s <c>ARNA</c> section: after the 96-byte
    /// header (its int32 at 12 the header's size, at 16 the section count), a directory of 12-byte
    /// entries (tag, uint32 offset, uint32 length); the section's bytes are uint32 version (1); the
    /// spot as float32 x, y, z, yaw; uint32 track count, then per point float32 x, y, z and uint32
    /// flags; uint32 mouth count, then per mouth float32 x, y, z, yaw and uint32 leg. False when the
    /// file has no <c>ARNA</c> section (<paramref name="why"/> null) or one this cannot read
    /// (<paramref name="why"/> says what is wrong). Pure: bytes in, layout out.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> map2d, out ArenaLayout? layout, out string? why)
    {
        layout = null;
        why = null;
        if (map2d.Length < RoomLight.HeaderBytes)
        {
            why = "the file is shorter than its header";
            return false;
        }

        int header = BinaryPrimitives.ReadInt32LittleEndian(map2d[12..]);
        int sections = BinaryPrimitives.ReadInt32LittleEndian(map2d[16..]);
        if (header < RoomLight.HeaderBytes || sections < 0 || (long)header + (long)sections * RoomLight.EntryBytes > map2d.Length)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"a header of {header} bytes and {sections} sections do not fit the file's {map2d.Length}");
            return false;
        }

        for (int s = 0; s < sections; s++)
        {
            ReadOnlySpan<byte> entry = map2d.Slice(header + s * RoomLight.EntryBytes, RoomLight.EntryBytes);
            if (!entry[..4].SequenceEqual(Tag))
                continue;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            if ((ulong)offset + length > (ulong)map2d.Length)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section ({length} bytes at {offset}) does not fit the file");
                return false;
            }

            return Parse(map2d.Slice((int)offset, (int)length), out layout, out why);
        }

        return false;
    }

    private static bool Parse(ReadOnlySpan<byte> b, out ArenaLayout? layout, out string? why)
    {
        layout = null;
        why = null;
        const int Head = 4 + 16 + 4;
        if (b.Length < Head)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section is {b.Length} bytes, short of its version, spot and track count");
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (version != Version)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section is version {version}; this reads {Version}");
            return false;
        }

        var spot = new Vector(F(b, 4), F(b, 8), F(b, 12));
        float yaw = F(b, 16);
        uint points = BinaryPrimitives.ReadUInt32LittleEndian(b[20..]);
        ulong mouthAt = Head + (ulong)points * PointBytes;
        if (mouthAt + 4 > (ulong)b.Length)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section's {points} track points do not fit its {b.Length} bytes");
            return false;
        }

        uint mouths = BinaryPrimitives.ReadUInt32LittleEndian(b[(int)mouthAt..]);
        ulong end = mouthAt + 4 + (ulong)mouths * MouthBytes;
        if (end != (ulong)b.Length)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section holds {points} track points and {mouths} mouths in {b.Length} bytes, not {end}");
            return false;
        }

        var track = ImmutableArray.CreateBuilder<ArenaTrackPoint>((int)points);
        for (int i = 0; i < (int)points; i++)
        {
            int at = Head + i * PointBytes;
            track.Add(new ArenaTrackPoint(new Vector(F(b, at), F(b, at + 4), F(b, at + 8)), BinaryPrimitives.ReadUInt32LittleEndian(b[(at + 12)..])));
        }

        var open = ImmutableArray.CreateBuilder<ArenaMouth>((int)mouths);
        for (int i = 0; i < (int)mouths; i++)
        {
            int at = (int)mouthAt + 4 + i * MouthBytes;
            uint leg = BinaryPrimitives.ReadUInt32LittleEndian(b[(at + 16)..]);
            if (leg is < 1 or > int.MaxValue)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"its ARNA section's mouth {i} opens on leg {leg}; legs count from 1");
                return false;
            }

            open.Add(new ArenaMouth(new Vector(F(b, at), F(b, at + 4), F(b, at + 8)), F(b, at + 12), (int)leg));
        }

        layout = new ArenaLayout(spot, yaw, track.MoveToImmutable(), open.MoveToImmutable());
        return true;
    }

    private static float F(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b[at..]);
}
