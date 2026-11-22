using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;

namespace TF2.Descent.Server.Level;

/// <summary>One room's light as the level's <c>.map2d</c> carries it: the light (level 1 = 100 on the brightness gate's scale; NaN unmeasured) and its flags.</summary>
public readonly record struct RoomLightRecord(float Light, uint Flags)
{
    /// <summary>The room holds no light of its own (flag 1).</summary>
    public bool DarkByDesign => (Flags & RoomLight.DarkByDesignFlag) != 0;

    /// <summary>The room's view was rendered (flag 2) and its light is a number.</summary>
    public bool Measured => (Flags & RoomLight.MeasuredFlag) != 0 && !float.IsNaN(Light);
}

/// <summary>
/// The <c>.map2d</c>'s <c>LITE</c> section (plan_descent_mine_levels 1.3e, p9; written by
/// tools/descent-room-light, which Map2dReader skips): after the 96-byte header (its int32 at 12 the
/// header's size, at 16 the section count), a directory of 12-byte entries (tag, uint32 offset,
/// uint32 length); the <c>LITE</c> entry's bytes are uint32 version (1), uint32 count (the room
/// count), then per room in the ROOM section's order a float32 light and a uint32 flags
/// (<see cref="DarkByDesignFlag"/>, <see cref="MeasuredFlag"/>). Pure: bytes in, records out.
/// </summary>
public static class RoomLight
{
    public const int HeaderBytes = 96;
    public const int EntryBytes = 12;
    public const uint Version = 1;
    public const uint DarkByDesignFlag = 1;
    public const uint MeasuredFlag = 2;

    /// <summary>The section's tag, as the directory spells it.</summary>
    public static ReadOnlySpan<byte> Tag => "LITE"u8;

    /// <summary>
    /// The rooms' light from <paramref name="map2d"/>, one record per room of <paramref name="roomCount"/>;
    /// false when the file has no <c>LITE</c> section (<paramref name="why"/> null) or one this cannot
    /// read (<paramref name="why"/> says what is wrong).
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> map2d, int roomCount, out ImmutableArray<RoomLightRecord> rooms, out string? why)
    {
        rooms = [];
        why = null;
        if (map2d.Length < HeaderBytes)
        {
            why = "the file is shorter than its header";
            return false;
        }

        int header = BinaryPrimitives.ReadInt32LittleEndian(map2d[12..]);
        int sections = BinaryPrimitives.ReadInt32LittleEndian(map2d[16..]);
        if (header < HeaderBytes || sections < 0 || (long)header + (long)sections * EntryBytes > map2d.Length)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"a header of {header} bytes and {sections} sections do not fit the file's {map2d.Length}");
            return false;
        }

        for (int s = 0; s < sections; s++)
        {
            ReadOnlySpan<byte> entry = map2d.Slice(header + s * EntryBytes, EntryBytes);
            if (!entry[..4].SequenceEqual(Tag))
                continue;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            if ((ulong)offset + length > (ulong)map2d.Length || length < 8)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"its LITE section ({length} bytes at {offset}) does not fit the file");
                return false;
            }

            ReadOnlySpan<byte> b = map2d.Slice((int)offset, (int)length);
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(b);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
            if (version != Version)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"its LITE section is version {version}; this reads {Version}");
                return false;
            }

            if (count != (uint)roomCount || length != 8 + 8 * (ulong)count)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"its LITE section holds {count} rooms in {length} bytes for the level's {roomCount} rooms");
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<RoomLightRecord>(roomCount);
            for (int i = 0; i < roomCount; i++)
                builder.Add(new RoomLightRecord(BinaryPrimitives.ReadSingleLittleEndian(b[(8 + 8 * i)..]),
                                                BinaryPrimitives.ReadUInt32LittleEndian(b[(12 + 8 * i)..])));
            rooms = builder.MoveToImmutable();
            return true;
        }

        return false;
    }
}
