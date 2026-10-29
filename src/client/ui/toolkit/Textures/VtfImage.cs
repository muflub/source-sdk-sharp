using System.Buffers.Binary;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>The VTF image formats this reader decodes, named after
/// public/bitmap/imageformat.h's <c>ImageFormat</c>.</summary>
public enum VtfPixelFormat
{
    Rgba8888,
    Abgr8888,
    Rgb888,
    Bgr888,
    I8,
    Ia88,
    A8,
    Bgra8888,
    Dxt1,
    Dxt3,
    Dxt5,
    Bgrx8888,
}

/// <summary>
/// A decoded VTF: mip 0, frame 0, face 0, as RGBA (the shape
/// <see cref="IUiSurface"/> takes in <c>TextureUpload</c>).
///
/// <para>
/// Original work over Valve's documented disk format, with
/// <c>../source-sdk-2013/src/public/vtf/vtf.h</c> for the header layout and
/// <c>src/public/bitmap/imageformat.h</c> for the format numbers. The header
/// runs: the 16-byte base (tag, version pair, headerSize), the 7.1 fields
/// (size, flags, frames, the 20 bytes the POSIX reflectivity occupies, bump
/// scale, image format, mip count, low-res format and size), depth from 7.2,
/// and from 7.3 the resource array (one 8-byte type/offset pair per entry)
/// after the structure's tail padding. The image data follows the low-res
/// thumbnail for 7.2 and earlier, and is found at the 0x30 resource entry for
/// 7.3 and later; either way the mips run smallest first, one chunk per frame
/// per face, so mip 0 is the last chunk. All of this is measured against the
/// shipped TF2 files as well as the header.
/// </para>
///
/// <para>
/// A file that is not a decodable VTF is refused with a reason: a truncated
/// or malformed file must never come out as garbage.
/// </para>
/// </summary>
public sealed record VtfImage(int Width, int Height, int MipCount, int FrameCount, VtfPixelFormat Format, byte[] Rgba)
{
    private const int TextureFlagEnvmap = 0x4000;

    /// <summary>Decodes <paramref name="data"/>; false with a reason when it is not a decodable VTF.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out VtfImage? image, out string why)
    {
        image = null;

        if (data.Length < 16)
            return Refuse(out why, $"the file is {data.Length} bytes, too short for a VTF header");
        if (data[0] != (byte)'V' || data[1] != (byte)'T' || data[2] != (byte)'F' || data[3] != 0)
            return Refuse(out why, "the file does not start with the VTF tag");
        int major = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        int minor = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        if (major != 7 || minor is < 0 or > 5)
            return Refuse(out why, $"the file is VTF version {major}.{minor}, not one of 7.0 to 7.5");
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        if (headerSize < 16 || headerSize > data.Length)
            return Refuse(out why, $"the header claims {headerSize} bytes, outside the file");

        int wide = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
        int tall = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
        int flags = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
        int frames = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
        int format = BinaryPrimitives.ReadInt32LittleEndian(data[52..]);
        int mips = data[56];
        int lowResFormat = BinaryPrimitives.ReadInt32LittleEndian(data[57..]);
        int lowResWide = data[61];
        int lowResTall = data[62];
        int depth = minor >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(data[63..]) : 1;
        if (wide < 1 || tall < 1)
            return Refuse(out why, $"the image is {wide}x{tall}");
        if (depth != 1)
            return Refuse(out why, $"the image has depth {depth}; this reader decodes flat textures only");
        if (mips < 1)
            return Refuse(out why, $"the image names {mips} mips");
        if ((long)wide * tall * 4 > int.MaxValue)
            return Refuse(out why, $"the image is {wide}x{tall}, too large to decode in memory");
        if (FormatOf(format) is not VtfPixelFormat fmt)
            return Refuse(out why, $"the image format {format} is not one this reader decodes");
        if (frames < 1)
            frames = 1;

        long imageOffset;
        if (minor >= 3)
        {
            int entries = BinaryPrimitives.ReadInt32LittleEndian(data[68..]);
            long entryStart = headerSize - 8L * entries;
            if (entries < 1)
                return Refuse(out why, "the file carries no image resource entry");
            if (entryStart < 72 || entryStart + 8L * entries > data.Length)
                return Refuse(out why, $"the {entries} resource entries do not fit the {headerSize}-byte header");
            imageOffset = -1;
            for (int i = 0; i < entries; i++)
            {
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)entryStart..]);
                uint at = BinaryPrimitives.ReadUInt32LittleEndian(data[((int)entryStart + 4)..]);
                if ((type & 0x00FFFFFF) == 0x30)        // the image resource
                {
                    imageOffset = at;
                    break;
                }
                entryStart += 8;
            }
            if (imageOffset < 0)
                return Refuse(out why, "the file carries no image resource entry");
        }
        else
        {
            // The low-res thumbnail sits between the header and the image; its
            // size follows from its own format and extents.
            imageOffset = headerSize + (lowResWide > 0 && lowResTall > 0
                ? MipSize(lowResFormat, lowResWide, lowResTall)
                : 0);
        }

        int faces = (flags & TextureFlagEnvmap) != 0 ? 6 : 1;
        long offset = imageOffset;
        for (int m = mips - 1; m >= 1; m--)
            offset += (long)MipSize(format, Math.Max(wide >> m, 1), Math.Max(tall >> m, 1)) * frames * faces;
        long top = (long)MipSize(format, wide, tall) * frames * faces;
        if (offset + top > data.Length)
            return Refuse(out why, $"the image data is truncated: {offset}+{top} exceeds the {data.Length}-byte file");

        var rgba = new byte[wide * tall * 4];
        ReadOnlySpan<byte> src = data[(int)offset..];
        switch (fmt)
        {
            case VtfPixelFormat.Dxt1:
                DecodeDxt1(src, rgba, wide, tall);
                break;
            case VtfPixelFormat.Dxt3:
                DecodeDxt3(src, rgba, wide, tall);
                break;
            case VtfPixelFormat.Dxt5:
                DecodeDxt5(src, rgba, wide, tall);
                break;
            default:
                DecodeUncompressed(fmt, src, rgba);
                break;
        }

        image = new VtfImage(wide, tall, mips, frames, fmt, rgba);
        why = "";
        return true;
    }

    private static bool Refuse(out string why, string reason)
    {
        why = reason;
        return false;
    }

    private static VtfPixelFormat? FormatOf(int format)
    {
        return format switch
        {
            0 => VtfPixelFormat.Rgba8888,
            1 => VtfPixelFormat.Abgr8888,
            2 => VtfPixelFormat.Rgb888,
            3 => VtfPixelFormat.Bgr888,
            5 => VtfPixelFormat.I8,
            6 => VtfPixelFormat.Ia88,
            8 => VtfPixelFormat.A8,
            12 => VtfPixelFormat.Bgra8888,
            13 => VtfPixelFormat.Dxt1,
            14 => VtfPixelFormat.Dxt3,
            15 => VtfPixelFormat.Dxt5,
            16 => VtfPixelFormat.Bgrx8888,
            _ => null,
        };
    }

    /// <summary>One mip's bytes per frame per face, at the format's bytes per pixel or block.</summary>
    private static int MipSize(int format, int wide, int tall)
    {
        return format switch
        {
            13 => Blocks(wide, tall) * 8,
            14 or 15 => Blocks(wide, tall) * 16,
            0 or 1 or 12 or 16 => wide * tall * 4,
            2 or 3 => wide * tall * 3,
            5 or 8 => wide * tall,
            4 or 6 => wide * tall * 2,
            _ => 0,
        };

        static int Blocks(int wide, int tall) => ((wide + 3) / 4) * ((tall + 3) / 4);
    }

    // ---- the block formats: one 4x4 block per 8 (DXT1) or 16 bytes, rows of
    // ceil(wide/4) blocks regardless of a partial edge column ----

    private static void DecodeDxt1(ReadOnlySpan<byte> src, byte[] rgba, int wide, int tall)
    {
        Span<byte> palette = stackalloc byte[16];
        for (int y = 0; y < tall; y += 4)
        {
            for (int x = 0; x < wide; x += 4)
            {
                ReadOnlySpan<byte> block = src[..8];
                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                Expand565(c0, palette);
                Expand565(c1, palette[4..]);
                if (c0 > c1)
                {
                    Blend565(c0, c1, 2, 1, palette[8..]);       // (2*c0 + c1) / 3
                    Blend565(c0, c1, 1, 2, palette[12..]);      // (c0 + 2*c1) / 3
                }
                else
                {
                    Midpoint565(c0, c1, palette[8..]);
                    palette[12..].Clear();                      // the 1-bit alpha hole
                }
                WriteBlock(rgba, wide, tall, x, y, palette, indices, 2);
                src = src[8..];
            }
        }
    }

    private static void DecodeDxt3(ReadOnlySpan<byte> src, byte[] rgba, int wide, int tall)
    {
        Span<byte> palette = stackalloc byte[16];
        Span<byte> alpha = stackalloc byte[16];
        for (int y = 0; y < tall; y += 4)
        {
            for (int x = 0; x < wide; x += 4)
            {
                ReadOnlySpan<byte> block = src[..16];
                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
                Expand565(c0, palette);
                Expand565(c1, palette[4..]);
                Blend565(c0, c1, 2, 1, palette[8..]);
                Blend565(c0, c1, 1, 2, palette[12..]);
                for (int i = 0; i < 16; i++)
                {
                    int nibble = (block[i >> 1] >> ((i & 1) * 4)) & 0xF;
                    alpha[i] = (byte)(nibble | (nibble << 4));
                }
                WriteBlock(rgba, wide, tall, x, y, palette, indices, 2, alpha);
                src = src[16..];
            }
        }
    }

    private static void DecodeDxt5(ReadOnlySpan<byte> src, byte[] rgba, int wide, int tall)
    {
        Span<byte> palette = stackalloc byte[16];
        Span<byte> alpha = stackalloc byte[8];
        for (int y = 0; y < tall; y += 4)
        {
            for (int x = 0; x < wide; x += 4)
            {
                ReadOnlySpan<byte> block = src[..16];
                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);

                byte a0 = block[0], a1 = block[1];
                alpha[0] = a0;
                alpha[1] = a1;
                if (a0 > a1)
                {
                    for (int i = 2; i < 8; i++)
                        alpha[i] = (byte)(((8 - i) * a0 + (i - 1) * a1) / 7);
                }
                else
                {
                    for (int i = 2; i < 6; i++)
                        alpha[i] = (byte)(((6 - i) * a0 + (i - 2) * a1) / 5);
                    alpha[6] = 0;
                    alpha[7] = 255;
                }
                ulong alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block[2..]);   // the 48 index bits, LSB first

                Expand565(c0, palette);
                Expand565(c1, palette[4..]);
                Blend565(c0, c1, 2, 1, palette[8..]);
                Blend565(c0, c1, 1, 2, palette[12..]);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
                for (int i = 0; i < 16; i++)
                {
                    int px = x + (i & 3), py = y + (i >> 2);
                    if (px >= wide || py >= tall)
                        continue;
                    int at = (py * wide + px) * 4;
                    rgba[at + 0] = palette[(int)((indices >> (2 * i)) & 3) * 4 + 0];
                    rgba[at + 1] = palette[(int)((indices >> (2 * i)) & 3) * 4 + 1];
                    rgba[at + 2] = palette[(int)((indices >> (2 * i)) & 3) * 4 + 2];
                    rgba[at + 3] = alpha[(int)((alphaBits >> (3 * i)) & 7)];
                }
                src = src[16..];
            }
        }
    }

    /// <summary>A block's 16 texels, 2 or 3 index bits each, onto the image.</summary>
    private static void WriteBlock(byte[] rgba, int wide, int tall, int x, int y,
                                   ReadOnlySpan<byte> palette, uint indices, int indexBits)
    {
        WriteBlock(rgba, wide, tall, x, y, palette, indices, indexBits, default(ReadOnlySpan<byte>));
    }

    private static void WriteBlock(byte[] rgba, int wide, int tall, int x, int y,
                                   ReadOnlySpan<byte> palette, uint indices, int indexBits,
                                   ReadOnlySpan<byte> alpha)
    {
        bool hasAlpha = !alpha.IsEmpty;
        for (int i = 0; i < 16; i++)
        {
            int px = x + (i & 3), py = y + (i >> 2);
            if (px >= wide || py >= tall)
                continue;
            int index = (int)((indices >> (indexBits * i)) & ((1 << indexBits) - 1));
            int at = (py * wide + px) * 4;
            rgba[at + 0] = palette[index * 4 + 0];
            rgba[at + 1] = palette[index * 4 + 1];
            rgba[at + 2] = palette[index * 4 + 2];
            rgba[at + 3] = hasAlpha ? alpha[i] : palette[index * 4 + 3];
        }
    }

    private static void Expand565(ushort v, Span<byte> rgba)
    {
        rgba[0] = Expand5((v >> 11) & 0x1F);
        rgba[1] = Expand6((v >> 5) & 0x3F);
        rgba[2] = Expand5(v & 0x1F);
        rgba[3] = 0xFF;
    }

    private static void Blend565(ushort a, ushort b, int weightA, int weightB, Span<byte> rgba)
    {
        rgba[0] = Expand5((weightA * ((a >> 11) & 0x1F) + weightB * ((b >> 11) & 0x1F)) / 3);
        rgba[1] = Expand6((weightA * ((a >> 5) & 0x3F) + weightB * ((b >> 5) & 0x3F)) / 3);
        rgba[2] = Expand5((weightA * (a & 0x1F) + weightB * (b & 0x1F)) / 3);
        rgba[3] = 0xFF;
    }

    private static void Midpoint565(ushort a, ushort b, Span<byte> rgba)
    {
        rgba[0] = Expand5((((a >> 11) & 0x1F) + ((b >> 11) & 0x1F)) / 2);
        rgba[1] = Expand6((((a >> 5) & 0x3F) + ((b >> 5) & 0x3F)) / 2);
        rgba[2] = Expand5(((a & 0x1F) + (b & 0x1F)) / 2);
        rgba[3] = 0xFF;
    }

    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));

    private static byte Expand6(int v) => (byte)((v << 2) | (v >> 4));

    // ---- the byte-per-pixel formats ----

    private static void DecodeUncompressed(VtfPixelFormat format, ReadOnlySpan<byte> src, byte[] rgba)
    {
        for (int i = 0; i < rgba.Length / 4; i++)
        {
            int at = i * 4;
            switch (format)
            {
                case VtfPixelFormat.Rgba8888:
                    rgba[at] = src[at];
                    rgba[at + 1] = src[at + 1];
                    rgba[at + 2] = src[at + 2];
                    rgba[at + 3] = src[at + 3];
                    break;
                case VtfPixelFormat.Abgr8888:           // A,B,G,R in memory
                    rgba[at] = src[at + 3];
                    rgba[at + 1] = src[at + 2];
                    rgba[at + 2] = src[at + 1];
                    rgba[at + 3] = src[at];
                    break;
                case VtfPixelFormat.Bgra8888:           // B,G,R,A in memory
                    rgba[at] = src[at + 2];
                    rgba[at + 1] = src[at + 1];
                    rgba[at + 2] = src[at];
                    rgba[at + 3] = src[at + 3];
                    break;
                case VtfPixelFormat.Bgrx8888:           // B,G,R,X in memory
                case VtfPixelFormat.Bgr888:             // B,G,R in memory (3 bytes)
                    int bgr = format == VtfPixelFormat.Bgr888 ? i * 3 : at;
                    rgba[at] = src[bgr + 2];
                    rgba[at + 1] = src[bgr + 1];
                    rgba[at + 2] = src[bgr];
                    rgba[at + 3] = 0xFF;
                    break;
                case VtfPixelFormat.Rgb888:             // R,G,B in memory (3 bytes)
                    rgba[at] = src[i * 3];
                    rgba[at + 1] = src[i * 3 + 1];
                    rgba[at + 2] = src[i * 3 + 2];
                    rgba[at + 3] = 0xFF;
                    break;
                case VtfPixelFormat.I8:
                    rgba[at] = rgba[at + 1] = rgba[at + 2] = src[i];
                    rgba[at + 3] = 0xFF;
                    break;
                case VtfPixelFormat.Ia88:               // I,A in memory
                    rgba[at] = rgba[at + 1] = rgba[at + 2] = src[i * 2];
                    rgba[at + 3] = src[i * 2 + 1];
                    break;
                case VtfPixelFormat.A8:
                    // A mask: the tint a screen draws with carries the colour.
                    rgba[at] = rgba[at + 1] = rgba[at + 2] = 0xFF;
                    rgba[at + 3] = src[i];
                    break;
            }
        }
    }
}
