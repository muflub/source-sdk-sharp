using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>A minimal VBSP: the header, an entity lump and one other lump whose bytes give the map its checksum.</summary>
internal static class TinyBsp
{
    public static byte[] Make(string entities, byte planes = 1)
    {
        const int header = 4 + 4 + 64 * 16 + 4;
        byte[] text = Encoding.UTF8.GetBytes(entities + "\0");
        byte[] other = [planes, 2, 3, 4, 5, 6, 7, 8];
        byte[] bsp = new byte[header + text.Length + other.Length];
        "VBSP"u8.CopyTo(bsp);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(4), 20);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(8), header);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(12), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(8 + 16), header + text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(8 + 16 + 4), other.Length);
        text.CopyTo(bsp, header);
        other.CopyTo(bsp, header + text.Length);
        return bsp;
    }

    public const string Stock = "{\n\"classname\" \"worldspawn\"\n}\n{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"40 40 17\"\n}\n";
}
