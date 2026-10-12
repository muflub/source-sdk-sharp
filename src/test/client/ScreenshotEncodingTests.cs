using System.Globalization;

using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed screenshot encoders (<see cref="ScreenshotEncoding"/>) against
/// pristine bitmap.a's TGAWriter and vtf.a's CVTFTexture, recorded by
/// src/test/oracle/native/image_encode_oracle_test.cpp as
/// <c>Fixtures/image-encode-stock.tsv</c>: the same file, byte for byte, or the
/// same refusal, for every recorded case.
/// </summary>
public class ScreenshotEncodingTests
{
    private sealed record Case(string Kind, int Width, int Height, byte[] Pixels, byte[]? File);

    private static byte[] Hex(string s)
    {
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = byte.Parse(s.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return b;
    }

    private static List<Case> Golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "image-encode-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock encodes are missing: " + path);
        var cases = new List<Case>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] f = line.Split('\t');
            cases.Add(new Case(f[0], int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture),
                               Hex(f[3]), f[4] == "REFUSED" ? null : Hex(f[4])));
        }
        return cases;
    }

    private static ScreenshotFileKind Kind(string kind) => kind switch
    {
        "tga-rgb" => ScreenshotFileKind.TgaRgb,
        "tga-rgba" => ScreenshotFileKind.TgaRgba,
        "vtf-rgb" => ScreenshotFileKind.VtfRgb,
        _ => throw new InvalidDataException(kind),
    };

    [Fact]
    public void EveryCaseIsStocksFileOrStocksRefusal()
    {
        List<Case> cases = Golden();
        Assert.Equal(18, cases.Count);
        Assert.Equal(2, cases.Count(c => c.File == null));
        foreach (Case c in cases)
        {
            ScreenshotFileKind kind = Kind(c.Kind);
            var file = new byte[c.Width * c.Height * 4 + 1024];
            int n = ScreenshotEncoding.Encode(kind, c.Width, c.Height, c.Pixels, file);
            string name = $"{c.Kind} {c.Width}x{c.Height}";
            if (c.File == null)
            {
                Assert.True(n == -1, $"{name}: stock refuses it, ours wrote {n}");
                continue;
            }
            Assert.True(n == c.File.Length, $"{name}: {n} bytes, stock wrote {c.File.Length}");
            Assert.Equal(n, ScreenshotEncoding.Size(kind, c.Width, c.Height));
            for (int i = 0; i < n; i++)
                Assert.True(file[i] == c.File[i], $"{name}: byte {i} is {file[i]:x2}, stock wrote {c.File[i]:x2}");
        }
    }

    [Fact]
    public void AShortFileOrTooFewPixelsIsMinusTwo()
    {
        var pixels = new byte[4 * 4 * 3];
        Assert.Equal(-2, ScreenshotEncoding.Encode(ScreenshotFileKind.TgaRgb, 4, 4, pixels, new byte[18 + 47]));
        Assert.Equal(-2, ScreenshotEncoding.Encode(ScreenshotFileKind.VtfRgb, 4, 4, pixels.AsSpan(1), new byte[1024]));
        Assert.Equal(-1, ScreenshotEncoding.Encode(ScreenshotFileKind.TgaRgb, 0, 4, pixels, new byte[1024]));
    }

    [Fact]
    public void PaddingPutsTheImageTopLeftOfAWhitePowerOfTwo()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]; // 3x2
        byte[] padded = ScreenshotEncoding.PadToPowerOfTwo(3, 2, pixels, out int w, out int h);
        Assert.Equal((4, 2), (w, h));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 255, 255, 10, 11, 12, 13, 14, 15, 16, 17, 18, 255, 255, 255 }, padded);
        ScreenshotEncoding.PadToPowerOfTwo(180, 100, new byte[180 * 100 * 3], out w, out h);
        Assert.Equal((256, 128), (w, h));
    }
}

/// <summary>
/// IManagedMaterialFilesClient::EncodeImage, the crossing the screenshot path
/// takes: the pixels arrive as one BitBlock, the file comes back in native's
/// buffer, and a null buffer asks for the size first.
/// </summary>
public class EncodeImageBindingTests
{
    private sealed class NullLog : SourceSharp.Abi.ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private static unsafe int Call(SourceSharp.Client.Interop.MaterialFilesClientBinding b, int kind, int w, int h, bool pad,
                                   byte[] block, byte[]? file)
    {
        fixed (byte* pb = block)
        fixed (byte* pf = file)
            return b.EncodeImage(kind, w, h, pad ? 1 : 0, pb, block.Length, file == null ? null : pf, file?.Length ?? 0);
    }

    private static byte[] Block(byte[] pixels)
    {
        var block = new byte[SourceSharp.Abi.BitBlock.HeaderBytes + pixels.Length];
        pixels.CopyTo(block, SourceSharp.Abi.BitBlock.HeaderBytes);
        SourceSharp.Abi.BitBlock.SetBits(block, pixels.Length * 8);
        return block;
    }

    [Fact]
    public void TheSizeComesFirstThenTheFileItSaid()
    {
        var binding = new SourceSharp.Client.Interop.MaterialFilesClientBinding(new SourceSharp.Abi.Crossings(), new NullLog(),
            (_, _) => -1, ScreenshotEncoding.EncodeFile);
        var pixels = new byte[180 * 100 * 3];
        new Random(7).NextBytes(pixels);
        byte[] block = Block(pixels);

        int need = Call(binding, 2, 180, 100, true, block, null);
        Assert.Equal(88 + 256 * 128 * 3, need);
        var file = new byte[need];
        Assert.Equal(need, Call(binding, 2, 180, 100, true, block, file));
        byte[] padded = ScreenshotEncoding.PadToPowerOfTwo(180, 100, pixels, out _, out _);
        Assert.Equal(padded, file[88..]);

        Assert.Equal(18 + 180 * 100 * 3, Call(binding, 0, 180, 100, false, block, null));
        Assert.Equal(-2, Call(binding, 0, 180, 100, false, block, new byte[100]));
        Assert.Equal(-2, Call(binding, 0, 181, 100, false, block, new byte[100000]));
        Assert.Equal(-1, Call(binding, 9, 4, 4, false, block, null));
        Assert.Equal(-1, Call(binding, 1, 4, 4, true, block, null));
    }
}
