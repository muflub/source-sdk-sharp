//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;

namespace SourceSharp.Client.Render;

/// <summary>What a screenshot file is written as.</summary>
public enum ScreenshotFileKind
{
    /// <summary>A TGA of RGB888 pixels (the save-game screenshot).</summary>
    TgaRgb = 0,
    /// <summary>A TGA of RGBA8888 pixels (the post-process dump).</summary>
    TgaRgba = 1,
    /// <summary>A one-frame RGB888 VTF, NOMIP | NOLOD | SRGB (the save-game screenshot's VTF form).</summary>
    VtfRgb = 2,
}

/// <summary>
/// The image files client.so wrote through bitmap.a's TGAWriter and vtf.a's
/// CVTFTexture (plan_client_novalve C9: both archives off client.so), held
/// byte for byte to those archives by image_encode_oracle_test's golden
/// (ScreenshotEncodingTests). Pure functions of the pixels: no state.
/// </summary>
public static class ScreenshotEncoding
{
    private const int TgaHeaderBytes = 18;
    private const int VtfHeaderBytes = 80;
    private const int VtfResourceBytes = 8;
    private const uint TextureFlagsSrgb = 0x40, TextureFlagsNoMip = 0x100, TextureFlagsNoLod = 0x200;
    private const int ImageFormatRgb888 = 2;
    private const uint VtfLegacyResourceImage = 0x30;

    /// <summary>The bytes the file will take, or -1 when stock refuses the size.</summary>
    public static int Size(ScreenshotFileKind kind, int width, int height)
    {
        if (width <= 0 || height <= 0) return -1;
        return kind switch
        {
            ScreenshotFileKind.TgaRgb => TgaHeaderBytes + width * height * 3,
            ScreenshotFileKind.TgaRgba => TgaHeaderBytes + width * height * 4,
            ScreenshotFileKind.VtfRgb => VtfSideOk(width) && VtfSideOk(height)
                ? VtfHeaderBytes + VtfResourceBytes + width * height * 3 : -1,
            _ => -1,
        };
    }

    /// <summary>
    /// The file for these pixels (RGB888, or RGBA8888 for <see cref="ScreenshotFileKind.TgaRgba"/>,
    /// rows top first): the bytes written, -1 when stock refuses the size, -2 when
    /// <paramref name="file"/> is too small or the pixels too few.
    /// </summary>
    public static int Encode(ScreenshotFileKind kind, int width, int height, ReadOnlySpan<byte> pixels, Span<byte> file)
    {
        int size = Size(kind, width, height);
        if (size < 0) return -1;
        int bpp = kind == ScreenshotFileKind.TgaRgba ? 4 : 3;
        if (pixels.Length < width * height * bpp || file.Length < size) return -2;
        file = file[..size];
        if (kind == ScreenshotFileKind.VtfRgb)
            WriteVtf(width, height, pixels, file);
        else
            WriteTga(width, height, bpp, pixels, file);
        return size;
    }

    // CVTFTexture::Init: a side of 1, 2 or a multiple of 4.
    private static bool VtfSideOk(int n) => n <= 2 || (n & 3) == 0;

    // TGAWriter::WriteToBuffer: an 18-byte header (uncompressed true colour,
    // origin top left), then the pixels with red and blue swapped.
    private static void WriteTga(int width, int height, int bpp, ReadOnlySpan<byte> pixels, Span<byte> file)
    {
        file[..TgaHeaderBytes].Clear();
        file[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(file[12..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file[14..], (ushort)height);
        file[16] = (byte)(bpp * 8);
        file[17] = 0x20;

        Span<byte> body = file[TgaHeaderBytes..];
        int count = width * height;
        for (int i = 0, o = 0; i < count; i++, o += bpp)
        {
            body[o] = pixels[o + 2];
            body[o + 1] = pixels[o + 1];
            body[o + 2] = pixels[o];
            if (bpp == 4) body[o + 3] = pixels[o + 3];
        }
    }

    // CVTFTexture::Serialize for one RGB888 frame and no mips: the 7.4 file
    // header (with its struct padding zero), one resource entry (the image,
    // at the end of the header), then the pixels as they are.
    private static void WriteVtf(int width, int height, ReadOnlySpan<byte> pixels, Span<byte> file)
    {
        int headerSize = VtfHeaderBytes + VtfResourceBytes;
        file[..headerSize].Clear();
        "VTF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file[4..], 7);
        BinaryPrimitives.WriteInt32LittleEndian(file[8..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(file[12..], headerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file[16..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file[18..], (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(file[20..], TextureFlagsSrgb | TextureFlagsNoMip | TextureFlagsNoLod);
        BinaryPrimitives.WriteUInt16LittleEndian(file[24..], 1);      // frames
        BinaryPrimitives.WriteUInt16LittleEndian(file[26..], 0);      // start frame
        BinaryPrimitives.WriteSingleLittleEndian(file[32..], 1f);     // reflectivity
        BinaryPrimitives.WriteSingleLittleEndian(file[36..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(file[40..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(file[48..], 1f);     // bump scale
        BinaryPrimitives.WriteInt32LittleEndian(file[52..], ImageFormatRgb888);
        file[56] = 1;                                                 // mip levels
        BinaryPrimitives.WriteInt32LittleEndian(file[57..], -1);      // low-res format: none
        BinaryPrimitives.WriteUInt16LittleEndian(file[63..], 1);      // depth
        BinaryPrimitives.WriteUInt32LittleEndian(file[68..], 1);      // resources
        BinaryPrimitives.WriteUInt32LittleEndian(file[VtfHeaderBytes..], VtfLegacyResourceImage);
        BinaryPrimitives.WriteUInt32LittleEndian(file[(VtfHeaderBytes + 4)..], (uint)headerSize);
        pixels[..(width * height * 3)].CopyTo(file[headerSize..]);
    }

    /// <summary>
    /// The crossing's answer (IManagedMaterialFilesClient::EncodeImage): the
    /// pixels padded to powers of two when asked (view.cpp's
    /// bCreatePowerOf2Padded), then encoded. The bytes written; the bytes
    /// needed when <paramref name="file"/> is empty; -1 refused; -2 short.
    /// </summary>
    public static int EncodeFile(int kind, int width, int height, bool padPowerOfTwo, ReadOnlySpan<byte> pixels, Span<byte> file)
    {
        if (kind is < 0 or > 2 || width <= 0 || height <= 0) return -1;
        var fileKind = (ScreenshotFileKind)kind;
        int bpp = fileKind == ScreenshotFileKind.TgaRgba ? 4 : 3;
        if (pixels.Length < width * height * bpp) return -2;
        if (padPowerOfTwo)
        {
            if (bpp != 3) return -1;
            byte[] padded = PadToPowerOfTwo(width, height, pixels, out int pw, out int ph);
            return file.IsEmpty ? Size(fileKind, pw, ph) : Encode(fileKind, pw, ph, padded, file);
        }
        return file.IsEmpty ? Size(fileKind, width, height) : Encode(fileKind, width, height, pixels, file);
    }

    /// <summary>
    /// view.cpp's power-of-two padding: the image in the top left of the smallest
    /// power-of-two rectangle that holds it, the rest white. RGB888.
    /// </summary>
    public static byte[] PadToPowerOfTwo(int width, int height, ReadOnlySpan<byte> pixels, out int paddedWidth, out int paddedHeight)
    {
        paddedWidth = SmallestPowerOfTwoAtLeast(width);
        paddedHeight = SmallestPowerOfTwoAtLeast(height);
        var padded = new byte[paddedWidth * paddedHeight * 3];
        padded.AsSpan().Fill(255);
        for (int row = 0; row < height; row++)
            pixels.Slice(row * width * 3, width * 3).CopyTo(padded.AsSpan(row * paddedWidth * 3));
        return padded;
    }

    // mathlib's SmallestPowerOfTwoGreaterOrEqual.
    private static int SmallestPowerOfTwoAtLeast(int x)
    {
        int p = 1;
        while (p < x) p <<= 1;
        return p;
    }
}
