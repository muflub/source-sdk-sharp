//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>gamebspfile.h's game lump ids and versions the detail props read.</summary>
public static class DetailGameLumps
{
    /// <summary><c>GAMELUMP_DETAIL_PROPS</c>, 'dprp'.</summary>
    public const int DetailProps = 0x64707270;

    /// <summary><c>GAMELUMP_DETAIL_PROP_LIGHTING</c>, 'dplt'.</summary>
    public const int DetailPropLighting = 0x64706c74;

    /// <summary><c>GAMELUMP_DETAIL_PROP_LIGHTING_HDR</c>, 'dplh'.</summary>
    public const int DetailPropLightingHdr = 0x64706c68;

    /// <summary><c>DETAIL_NAME_LENGTH</c>: a model dictionary entry's name.</summary>
    public const int NameLength = 128;

    /// <summary><c>sizeof( DetailSpriteDictLump_t )</c>.</summary>
    public const int SpriteDictSize = 32;

    /// <summary><c>sizeof( DetailObjectLump_t )</c>.</summary>
    public const int ObjectSize = 52;

    /// <summary><c>sizeof( DetailPropLightstylesLump_t )</c>.</summary>
    public const int LightstyleSize = 5;
}

/// <summary>gamebspfile.h's <c>DetailPropType_t</c>.</summary>
public enum DetailPropType : byte
{
    Model = 0,
    Sprite = 1,
    ShapeCross = 2,
    ShapeTri = 3,
}

/// <summary>mathlib.h's <c>ColorRGBExp32</c>: a lightmap colour and its power-of-two exponent.</summary>
public readonly record struct ColorRgbExp32(byte R, byte G, byte B, sbyte Exponent);

/// <summary><c>DetailSpriteDictLump_t</c>: a sprite's extents and its corners in the sheet.</summary>
public struct DetailSpriteDict
{
    public float UlX, UlY, LrX, LrY;
    public float TexUlX, TexUlY, TexLrX, TexLrY;
}

/// <summary><c>DetailObjectLump_t</c>, field for field.</summary>
public struct DetailObjectLump
{
    public Vector Origin;
    public Vector Angles;
    public ushort DetailModel;
    public ushort Leaf;
    public ColorRgbExp32 Lighting;
    public uint LightStyles;
    public byte LightStyleCount;
    public byte SwayAmount;
    public byte ShapeAngle;
    public byte ShapeSize;
    public byte Orientation;
    public byte Type;
    public float Scale;
}

/// <summary><c>DetailPropLightstylesLump_t</c>.</summary>
public readonly record struct DetailLightstyle(ColorRgbExp32 Lighting, byte Style);

/// <summary>
/// tier1's CUtlBuffer over a game lump, as the detail system reads one in
/// binary mode: little-endian ints, raw structs, and once a read runs past the
/// end every later read fails (GET_OVERFLOW). A failed GetInt answers 0. A
/// failed struct read leaves stock's stack garbage in its struct; here the
/// read answers <c>false</c> and the caller keeps zeros.
/// </summary>
public ref struct DetailLumpReader(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;
    private int _get;
    private bool _overflow;

    /// <summary><c>TellGet</c>.</summary>
    public readonly int Position => _get;

    /// <summary><c>SeekGet( SEEK_HEAD, offset )</c>. The overflow stays.</summary>
    public void Seek(int offset) => _get = offset;

    private bool CheckGet(int size)
    {
        if (_overflow)
            return false;
        if (_bytes.Length < _get + size)
        {
            _overflow = true;
            return false;
        }
        return true;
    }

    /// <summary><c>GetInt</c>.</summary>
    public int GetInt()
    {
        if (!CheckGet(4))
            return 0;
        int v = BinaryPrimitives.ReadInt32LittleEndian(_bytes.Slice(_get, 4));
        _get += 4;
        return v;
    }

    /// <summary><c>Get( pMem, size )</c>: the bytes, or false and nothing read.</summary>
    public bool Get(int size, out ReadOnlySpan<byte> data)
    {
        if (size <= 0 || !CheckGet(size))
        {
            data = default;
            return false;
        }
        data = _bytes.Slice(_get, size);
        _get += size;
        return true;
    }

    /// <summary>A <c>DetailObjectDictLump_t</c>: the model's name, up to its NUL.</summary>
    public string GetModelName()
    {
        if (!Get(DetailGameLumps.NameLength, out ReadOnlySpan<byte> raw))
            return string.Empty;
        int end = raw.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? raw : raw[..end]);
    }

    /// <summary>A <c>DetailSpriteDictLump_t</c>.</summary>
    public DetailSpriteDict GetSpriteDict()
    {
        DetailSpriteDict d = default;
        if (!Get(DetailGameLumps.SpriteDictSize, out ReadOnlySpan<byte> raw))
            return d;
        d.UlX = F(raw, 0);
        d.UlY = F(raw, 4);
        d.LrX = F(raw, 8);
        d.LrY = F(raw, 12);
        d.TexUlX = F(raw, 16);
        d.TexUlY = F(raw, 20);
        d.TexLrX = F(raw, 24);
        d.TexLrY = F(raw, 28);
        return d;
    }

    /// <summary>A <c>DetailObjectLump_t</c>.</summary>
    public DetailObjectLump GetObject()
    {
        DetailObjectLump o = default;
        if (!Get(DetailGameLumps.ObjectSize, out ReadOnlySpan<byte> raw))
            return o;
        o.Origin = new Vector(F(raw, 0), F(raw, 4), F(raw, 8));
        o.Angles = new Vector(F(raw, 12), F(raw, 16), F(raw, 20));
        o.DetailModel = BinaryPrimitives.ReadUInt16LittleEndian(raw[24..]);
        o.Leaf = BinaryPrimitives.ReadUInt16LittleEndian(raw[26..]);
        o.Lighting = new ColorRgbExp32(raw[28], raw[29], raw[30], unchecked((sbyte)raw[31]));
        o.LightStyles = BinaryPrimitives.ReadUInt32LittleEndian(raw[32..]);
        o.LightStyleCount = raw[36];
        o.SwayAmount = raw[37];
        o.ShapeAngle = raw[38];
        o.ShapeSize = raw[39];
        o.Orientation = raw[40];
        o.Type = raw[44];
        o.Scale = F(raw, 48);
        return o;
    }

    /// <summary>A <c>DetailPropLightstylesLump_t</c>.</summary>
    public DetailLightstyle GetLightstyle()
    {
        if (!Get(DetailGameLumps.LightstyleSize, out ReadOnlySpan<byte> raw))
            return default;
        return new DetailLightstyle(new ColorRgbExp32(raw[0], raw[1], raw[2], unchecked((sbyte)raw[3])), raw[4]);
    }

    private static float F(ReadOnlySpan<byte> raw, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(raw[offset..]);
}
