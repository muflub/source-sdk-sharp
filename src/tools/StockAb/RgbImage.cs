using System.IO.Compression;

namespace SourceSharp.StockAb;

/// <summary>An 8-bit RGB image, rows top to bottom.</summary>
public sealed class RgbImage
{
    public RgbImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height} is not an image");
        if (pixels.Length != width * height * 3)
            throw new ArgumentException($"{pixels.Length} bytes is not {width}x{height} RGB", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>R, G, B per pixel, row-major from the top-left.</summary>
    public byte[] Pixels { get; }

    public static RgbImage Blank(int width, int height) => new(width, height, new byte[width * height * 3]);

    /// <summary>
    /// A TGA the engine's <c>screenshot</c> command writes: uncompressed or
    /// run-length true colour (types 2 and 10), 24 or 32 bits, either origin.
    /// </summary>
    public static RgbImage ReadTga(byte[] file)
    {
        if (file.Length < 18)
            throw new InvalidDataException($"{file.Length} bytes is too short for a TGA header");

        int idLength = file[0];
        int colourMapType = file[1];
        int type = file[2];
        int width = file[12] | (file[13] << 8);
        int height = file[14] | (file[15] << 8);
        int bits = file[16];
        bool topDown = (file[17] & 0x20) != 0;

        if (colourMapType != 0 || (type != 2 && type != 10))
            throw new InvalidDataException($"TGA type {type} (colour map {colourMapType}) is not true colour");
        if (bits != 24 && bits != 32)
            throw new InvalidDataException($"TGA with {bits} bits per pixel; only 24 and 32 are read");

        int stride = bits / 8;
        var pixels = new byte[width * height * 3];
        int at = 18 + idLength;
        int count = width * height;

        void Put(int index, int from)
        {
            int row = index / width;
            int column = index % width;
            int y = topDown ? row : height - 1 - row;
            int to = ((y * width) + column) * 3;
            pixels[to] = file[from + 2];
            pixels[to + 1] = file[from + 1];
            pixels[to + 2] = file[from];
        }

        if (type == 2)
        {
            if (at + (count * stride) > file.Length)
                throw new InvalidDataException("TGA pixel data is truncated");
            for (int i = 0; i < count; i++)
                Put(i, at + (i * stride));
        }
        else
        {
            int i = 0;
            while (i < count)
            {
                if (at >= file.Length)
                    throw new InvalidDataException("TGA run-length data is truncated");
                int header = file[at++];
                int run = (header & 0x7F) + 1;
                if (i + run > count)
                    throw new InvalidDataException("a TGA run passes the end of the image");
                if ((header & 0x80) != 0)
                {
                    if (at + stride > file.Length)
                        throw new InvalidDataException("TGA run-length data is truncated");
                    for (int k = 0; k < run; k++)
                        Put(i++, at);
                    at += stride;
                }
                else
                {
                    if (at + (run * stride) > file.Length)
                        throw new InvalidDataException("TGA run-length data is truncated");
                    for (int k = 0; k < run; k++, at += stride)
                        Put(i++, at);
                }
            }
        }

        return new RgbImage(width, height, pixels);
    }

    /// <summary>This image as a PNG (8-bit RGB, no filter).</summary>
    public byte[] ToPng()
    {
        var raw = new byte[(Width * 3 + 1) * Height];
        for (int y = 0; y < Height; y++)
            Buffer.BlockCopy(Pixels, y * Width * 3, raw, (y * ((Width * 3) + 1)) + 1, Width * 3);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);

        var header = new byte[13];
        BigEndian(header, 0, (uint)Width);
        BigEndian(header, 4, (uint)Height);
        header[8] = 8;
        header[9] = 2;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        uint[] crcTable = CrcTable();
        Chunk(png, "IHDR", header, crcTable);
        Chunk(png, "IDAT", compressed.ToArray(), crcTable);
        Chunk(png, "IEND", [], crcTable);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data, uint[] crcTable)
    {
        var length = new byte[4];
        BigEndian(length, 0, (uint)data.Length);
        png.Write(length);

        byte[] tagged = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        png.Write(tagged);

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in tagged)
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        var tail = new byte[4];
        BigEndian(tail, 0, crc ^ 0xFFFFFFFFu);
        png.Write(tail);
    }

    private static uint[] CrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static void BigEndian(byte[] to, int at, uint value)
    {
        to[at] = (byte)(value >> 24);
        to[at + 1] = (byte)(value >> 16);
        to[at + 2] = (byte)(value >> 8);
        to[at + 3] = (byte)value;
    }
}
