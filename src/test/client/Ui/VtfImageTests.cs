using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The managed VTF reader, over hand-built files. Every file here is written
/// by <see cref="VtfFile"/> in code: no game content is committed, and the
/// bytes each fact expects come from the format's own rules (the header
/// layout of public/vtf/vtf.h, the format numbers of public/bitmap/imageformat.h).
/// </summary>
public sealed class VtfImageTests
{
    private static ushort Rgb565(byte r, byte g, byte b)
        => (ushort)((r >> 3) << 11 | (g >> 2) << 5 | (b >> 3));

    // Pure red and pure blue in RGB565, the two corners the DXT facts below.
    private const ushort Red565 = 0xF800, Blue565 = 0x001F;

    /// <summary>A 4x4 DXT1 block: 2x2 of red then 2x2 of blue per row (4-colour mode).</summary>
    private static byte[] RedBlueDxt1Block() =>
    [
        .. BitConverter.GetBytes(Red565), .. BitConverter.GetBytes(Blue565),
        // row0: 0 0 1 1; row1: same; rows 2-3: all 1.
        0x50, 0x50, 0x55, 0x55,
    ];

    /// <summary>A colour block of all-red texels: c0 = red, c1 = blue, every index 0.</summary>
    private static void AllRedColourBlock(byte[] block, int colourAt)
    {
        block[colourAt] = 0x00;
        block[colourAt + 1] = 0xF8;
        block[colourAt + 2] = 0x1F;
        block[colourAt + 3] = 0x00;
    }

    [Fact]
    public void A_version_7_3_image_sits_at_its_image_resource()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 13, mips: 1,
                                                image: [RedBlueDxt1Block()], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(4, image!.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal(1, image.MipCount);
        Assert.Equal(VtfPixelFormat.Dxt1, image.Format);
        // texel (0,0) red, (3,0) blue.
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[0..4]);
        Assert.Equal([0x00, 0x00, 0xFF, 0xFF], image.Rgba[((3 * 4 + 3) * 4)..((3 * 4 + 4) * 4)]);
    }

    [Fact]
    public void A_version_7_4_image_reads_like_a_7_3_one()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 4, wide: 4, tall: 4, format: 13, mips: 1,
                                                image: [RedBlueDxt1Block()], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(VtfPixelFormat.Dxt1, image!.Format);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[0..4]);
    }

    [Fact]
    public void A_version_7_2_image_sits_after_its_low_res_thumbnail()
    {
        // A 16x16 DXT1 thumbnail (128 bytes) followed by a 2x2 BGRA8888 image.
        byte[] lowres = new byte[128];
        byte[] image = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00];
        byte[] file = VtfFile.Build(new VtfFile(minor: 2, wide: 2, tall: 2, format: 12, mips: 1,
                                                image: [image], lowResBytes: lowres, lowResWide: 16, lowResTall: 16));
        Assert.True(VtfImage.TryDecode(file, out var decoded, out string why), why);
        Assert.Equal(2, decoded!.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(VtfPixelFormat.Bgra8888, decoded.Format);
        // BGRA8888 swizzles to RGBA.
        Assert.Equal([0x33, 0x22, 0x11, 0x44], decoded.Rgba[0..4]);
        Assert.Equal([0xFF, 0xEE, 0xDD, 0x00], decoded.Rgba[12..16]);
    }

    [Fact]
    public void A_version_7_1_image_without_a_thumbnail_sits_at_the_header()
    {
        byte[] image = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00];
        byte[] file = VtfFile.Build(new VtfFile(minor: 1, wide: 2, tall: 2, format: 12, mips: 1, image: [image]));
        Assert.True(VtfImage.TryDecode(file, out var decoded, out string why), why);
        Assert.Equal([0x30, 0x20, 0x10, 0x40], decoded!.Rgba[0..4]);
        Assert.Equal([0xF0, 0xE0, 0xD0, 0x00], decoded.Rgba[12..16]);
    }

    [Fact]
    public void Mip_zero_is_the_last_in_the_chain()
    {
        // Three mips of a 4x4 BGRA8888: the file stores 1x1, then 2x2, then 4x4;
        // the 4x4 (mip 0) is what decodes.
        byte[] mip0 = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++)
            mip0[i * 4] = (byte)i;                      // blue ramp, the rest 0
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 12, mips: 3,
                                                image: [new byte[4], new byte[16], mip0], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(3, image!.MipCount);
        Assert.Equal(16 * 4, image.Rgba.Length);
        Assert.Equal(15, image.Rgba[15 * 4 + 2]);   // the ramp is in the blue channel, through the BGRA swizzle
        Assert.Equal(0, image.Rgba[15 * 4 + 3]);
    }

    [Fact]
    public void Frame_zero_is_what_decodes()
    {
        byte[] frame0 = new byte[2 * 2 * 4], frame1 = new byte[2 * 2 * 4];
        Array.Fill(frame0, (byte)0xAA);
        Array.Fill(frame1, (byte)0xBB);
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 2, tall: 2, format: 12, mips: 1,
                                                image: [frame0, frame1], frames: 2, resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(2, image!.FrameCount);
        Assert.All(image.Rgba, b => Assert.Equal(0xAA, b));
    }

    [Fact]
    public void Dxt1_four_colour_mode_interpolates_the_two_middle_colours()
    {
        // c0 = red, c1 = blue; in the 565 palette space texel 2 is (2*c0+c1)/3
        // per channel and texel 3 is (c0+2*c1)/3.
        byte[] block = RedBlueDxt1Block();
        block[4] = 0xE4;    // row0 indices 0 1 2 3
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 13, mips: 1,
                                                image: [block], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image!.Rgba[0..4]);     // index 0 = c0
        Assert.Equal([0x00, 0x00, 0xFF, 0xFF], image.Rgba[4..8]);     // index 1 = c1
        Assert.Equal([165, 0, 82, 255], image.Rgba[8..12]);           // index 2 = (2*c0+c1)/3, 565-exact
        Assert.Equal([82, 0, 165, 255], image.Rgba[12..16]);          // index 3 = (c0+2*c1)/3
    }

    [Fact]
    public void Dxt1_one_bit_alpha_makes_index_three_transparent_in_three_colour_mode()
    {
        // c0 <= c1 is 3-colour mode: index 2 is the per-channel midpoint and
        // index 3 is the transparent black.
        byte[] block =
        [
            .. BitConverter.GetBytes(Blue565), .. BitConverter.GetBytes(Red565),
            0xE4, 0x55, 0x55, 0x55,
        ];
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 13, mips: 1,
                                                image: [block], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0x00, 0x00, 0xFF, 0xFF], image!.Rgba[0..4]);   // index 0 = c0
        Assert.Equal([123, 0, 123, 255], image.Rgba[8..12]);         // index 2 = the midpoint, 565-exact
        Assert.Equal([0, 0, 0, 0], image.Rgba[12..16]);              // index 3: the 1-bit alpha hole
    }

    [Fact]
    public void Dxt3_takes_alpha_from_its_explicit_four_bit_nibbles()
    {
        byte[] block = new byte[16];
        for (int i = 0; i < 8; i++)
            block[i] = 0xFF;                     // every nibble 0xF
        AllRedColourBlock(block, 8);             // colour from byte 8, all indices 0
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 14, mips: 1,
                                                image: [block], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(VtfPixelFormat.Dxt3, image!.Format);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[0..4]);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[(15 * 4)..(16 * 4)]);
    }

    [Fact]
    public void Dxt5_interpolates_alpha_between_its_two_corner_values()
    {
        byte[] block = new byte[16];
        block[0] = 0x00;   // a0
        block[1] = 0xFF;   // a1  (a0 <= a1: four interpolated + the 0 and 255 stops)
        // alpha indices, 3 bits each: texel 0 -> 0, texel 1 -> 1, texel 2 -> 6 (the 0 stop),
        // texel 3 -> 7 (the 255 stop).
        ulong indices = 0 | (1UL << 3) | (6UL << 6) | (7UL << 9);
        for (int i = 0; i < 6; i++)
            block[2 + i] = (byte)(indices >> (8 * i));
        AllRedColourBlock(block, 8);
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 15, mips: 1,
                                                image: [block], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0xFF, 0x00, 0x00, 0x00], image!.Rgba[0..4]);      // index 0 = a0
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[4..8]);       // index 1 = a1
        Assert.Equal([0xFF, 0x00, 0x00, 0x00], image.Rgba[8..12]);      // index 6 = the 0 stop
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], image.Rgba[12..16]);     // index 7 = the 255 stop
    }

    [Fact]
    public void The_uncompressed_formats_swizzle_to_rgba()
    {
        byte[] abgr = [0x10, 0x20, 0x30, 0x40];   // A,B,G,R -> R=0x40 G=0x30 B=0x20 A=0x10
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 1, mips: 1,
                                                image: [abgr], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(VtfPixelFormat.Abgr8888, image!.Format);
        Assert.Equal([0x40, 0x30, 0x20, 0x10], image.Rgba);
    }

    [Fact]
    public void Bgrx8888_leaves_the_x_byte_behind()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 16, mips: 1,
                                                image: [[0x10, 0x20, 0x30, 0xEE]], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0x30, 0x20, 0x10, 0xFF], image!.Rgba);
    }

    [Fact]
    public void Three_byte_formats_come_out_opaque()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 3, mips: 1,
                                                image: [[0x10, 0x20, 0x30]], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(VtfPixelFormat.Bgr888, image!.Format);
        Assert.Equal([0x30, 0x20, 0x10, 0xFF], image.Rgba);
    }

    [Fact]
    public void I8_and_ia88_carry_luminance_and_alpha()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 5, mips: 1,
                                                image: [[0x7F]], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0x7F, 0x7F, 0x7F, 0xFF], image!.Rgba);

        byte[] ia = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 6, mips: 1,
                                              image: [[0x7F, 0x40]], resources: true));
        Assert.True(VtfImage.TryDecode(ia, out var ia88, out string why2), why2);
        Assert.Equal([0x7F, 0x7F, 0x7F, 0x40], ia88!.Rgba);
    }

    [Fact]
    public void A8_comes_out_white_with_its_alpha()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 8, mips: 1,
                                                image: [[0xC0]], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xC0], image!.Rgba);
    }

    [Fact]
    public void Rgba8888_is_a_copy()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 0, mips: 1,
                                                image: [[0x10, 0x20, 0x30, 0x40]], resources: true));
        Assert.True(VtfImage.TryDecode(file, out var image, out string why), why);
        Assert.Equal(VtfPixelFormat.Rgba8888, image!.Format);
        Assert.Equal([0x10, 0x20, 0x30, 0x40], image.Rgba);
    }

    // ---- refusals: a bad file says why, never decodes garbage ----

    [Fact]
    public void A_file_shorter_than_a_header_is_refused()
    {
        Assert.False(VtfImage.TryDecode([0x56, 0x54, 0x46], out _, out string why));
        Assert.Contains("short", why);
    }

    [Fact]
    public void A_file_without_the_tag_is_refused()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 0, mips: 1,
                                                image: [new byte[4]], resources: true));
        file[0] = 0x58;
        Assert.False(VtfImage.TryDecode(file, out _, out string why));
        Assert.Contains("VTF", why);
    }

    [Fact]
    public void An_unknown_version_is_refused()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 1, tall: 1, format: 0, mips: 1,
                                                image: [new byte[4]], resources: true));
        file[4] = 8;
        Assert.False(VtfImage.TryDecode(file, out _, out string why));
        Assert.Contains("version", why);
    }

    [Fact]
    public void An_unknown_image_format_is_refused_naming_it()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 22, mips: 1,
                                                image: [new byte[16]], resources: true));
        Assert.False(VtfImage.TryDecode(file, out _, out string why));
        Assert.Contains("22", why);
    }

    [Fact]
    public void A_truncated_image_is_refused()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 0, mips: 1,
                                                image: [new byte[16]], resources: true));
        Assert.False(VtfImage.TryDecode(file[..^8], out _, out string why));
        Assert.Contains("truncat", why);
    }

    [Fact]
    public void A_volume_texture_is_refused()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 0, mips: 1,
                                                image: [new byte[16]], resources: true, depth: 2));
        Assert.False(VtfImage.TryDecode(file, out _, out string why));
        Assert.Contains("depth", why);
    }

    [Fact]
    public void A_version_7_3_file_without_an_image_resource_is_refused()
    {
        byte[] file = VtfFile.Build(new VtfFile(minor: 3, wide: 4, tall: 4, format: 0, mips: 1,
                                                image: [new byte[16]], resources: false));
        Assert.False(VtfImage.TryDecode(file, out _, out string why));
        Assert.Contains("image resource", why);
    }
}

/// <summary>The hand-built VTF files the facts decode. The layout is the one
/// public/vtf/vtf.h writes: a 16-byte base header, then the 7.1 fields, depth
/// from 7.2, and from 7.3 the resource array after the structure's padding;
/// the data itself is the low-res image (to 7.2) and then the mip chain,
/// smallest mip first, per mip one chunk per frame.</summary>
internal sealed record VtfFile(
    int minor,
    int wide,
    int tall,
    int format,
    int mips,
    byte[][] image,          // the whole chain, smallest mip first, frames within a mip
    int frames = 1,
    byte[]? lowResBytes = null,
    int lowResWide = 0,
    int lowResTall = 0,
    int depth = 1,
    bool resources = false)
{
    public static byte[] Build(in VtfFile f)
    {
        var body = new List<byte>(128 + f.image.Sum(b => b.Length));

        // The 16-byte base header.
        body.AddRange([0x56, 0x54, 0x46, 0x00]);         // "VTF\0"
        body.AddRange(BitConverter.GetBytes(7));
        body.AddRange(BitConverter.GetBytes(f.minor));

        if (f.minor >= 3)
        {
            int nres = f.resources ? 2 : 0;              // low-res + image, when present
            int headerSize = 80 + 8 * nres;
            body.AddRange(BitConverter.GetBytes(headerSize));
            WriteV71Header(body, f);
            if (f.minor >= 2)
                body.AddRange(BitConverter.GetBytes((ushort)f.depth));
            body.AddRange(new byte[3]);                  // the 7.3 pad
            body.AddRange(BitConverter.GetBytes(nres));
            body.AddRange(new byte[8]);                  // the structure's tail padding
            if (nres > 0)
            {
                int imageAt = headerSize + (f.lowResBytes?.Length ?? 0);
                body.AddRange(BitConverter.GetBytes(0x00000001u));   // the low-res image entry
                body.AddRange(BitConverter.GetBytes(headerSize));
                body.AddRange(BitConverter.GetBytes(0x00000030u));   // the image entry
                body.AddRange(BitConverter.GetBytes(imageAt));
                body.AddRange(f.lowResBytes ?? []);
            }
        }
        else
        {
            int headerSize = f.minor >= 2 ? 80 : 64;
            body.AddRange(BitConverter.GetBytes(headerSize));
            WriteV71Header(body, f);
            if (f.minor >= 2)
                body.AddRange(BitConverter.GetBytes((ushort)f.depth));
            body.AddRange(new byte[headerSize - body.Count]);
            body.AddRange(f.lowResBytes ?? []);
        }

        foreach (byte[] mip in f.image)
            body.AddRange(mip);
        return [.. body];
    }

    private static void WriteV71Header(List<byte> body, in VtfFile f)
    {
        body.AddRange(BitConverter.GetBytes((ushort)f.wide));
        body.AddRange(BitConverter.GetBytes((ushort)f.tall));
        body.AddRange(BitConverter.GetBytes(0u));                        // flags
        body.AddRange(BitConverter.GetBytes((ushort)Math.Max(f.frames, 1)));
        body.AddRange(BitConverter.GetBytes((ushort)0));                 // start frame
        body.AddRange(new byte[20]);                                     // the reflectivity's 20 bytes
        body.AddRange(BitConverter.GetBytes(1.0f));                      // bump scale
        body.AddRange(BitConverter.GetBytes(f.format));
        body.Add((byte)f.mips);
        body.AddRange(BitConverter.GetBytes(f.lowResBytes is null ? -1 : 13));  // low-res format
        body.Add((byte)f.lowResWide);
        body.Add((byte)f.lowResTall);
    }
}
