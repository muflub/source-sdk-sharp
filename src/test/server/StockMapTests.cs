using System.Text;

using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The stock copy of ss_sandbox (tools/stock-ab): which classnames change, and
/// that nothing but the entity lump does.
/// </summary>
public class StockMapTests
{
    private const string Lump =
        "{\n\"classname\" \"worldspawn\"\n}\n"
        + "{\n\"classname\" \"ss_barrel\"\n\"targetname\" \"ss_barrel\"\n}\n"
        + "{\n\"te\" \"ss_te_tester\"\n\"classname\" \"ss_te_tester\"\n}\n";

    [Fact]
    public void AClassnameValueIsRenamed()
    {
        string renamed = StockMap.Rename(Lump, StockMap.Renames, out int count);

        Assert.Contains("\"classname\" \"prop_physics\"", renamed);
        Assert.Contains("\"classname\" \"info_target\"", renamed);
        Assert.Equal(2, count);
    }

    [Fact]
    public void AnyOtherKeyHoldingTheSameStringIsLeftAlone()
    {
        string renamed = StockMap.Rename(Lump, StockMap.Renames, out _);

        Assert.Contains("\"targetname\" \"ss_barrel\"", renamed);
        Assert.Contains("\"te\" \"ss_te_tester\"", renamed);
    }

    [Fact]
    public void ALumpWithNothingToRenameComesBackUnchanged()
    {
        const string stock = "{\n\"classname\" \"prop_physics\"\n\"model\" \"x.mdl\"\n}\n";

        Assert.Equal(stock, StockMap.Rename(stock, StockMap.Renames, out int count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void TheNewLumpIsWhatTheReaderReadsBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stockmap-{Guid.NewGuid():N}.bsp");
        try
        {
            File.WriteAllBytes(path, StockMap.WithEntityLump(Bsp("{\n\"classname\" \"ss_barrel\"\n}\n"), Lump));

            Assert.Equal(Lump, BspEntities.ReadLumpText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryByteBeforeTheNewLumpIsTheInputsButLumpZerosDescriptor()
    {
        byte[] input = Bsp("{\n\"classname\" \"ss_barrel\"\n}\n");
        byte[] output = StockMap.WithEntityLump(input, Lump);

        for (int i = 0; i < input.Length; i++)
        {
            if (i is >= 8 and < 16)
                continue;
            Assert.True(input[i] == output[i], $"byte {i} changed");
        }

        Assert.NotEqual(input.AsSpan(8, 8).ToArray(), output.AsSpan(8, 8).ToArray());
    }

    [Fact]
    public void ACompressedEntityLumpIsRefused()
    {
        byte[] input = Bsp("{}");
        BitConverter.TryWriteBytes(input.AsSpan(8 + 12, 4), 1234);

        Assert.Throws<InvalidDataException>(() => StockMap.WithEntityLump(input, Lump));
    }

    /// <summary>A minimal VBSP: the header, one other lump's bytes, then lump 0's text.</summary>
    private static byte[] Bsp(string lump)
    {
        const int header = 8 + (64 * 16) + 4;
        byte[] other = [1, 2, 3, 4, 5, 6, 7];
        byte[] text = Encoding.Latin1.GetBytes(lump + "\0");
        var bytes = new byte[header + other.Length + text.Length];

        Encoding.ASCII.GetBytes("VBSP").CopyTo(bytes, 0);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), 20);
        BitConverter.TryWriteBytes(bytes.AsSpan(8 + 16, 4), header);
        BitConverter.TryWriteBytes(bytes.AsSpan(8 + 16 + 4, 4), other.Length);
        other.CopyTo(bytes, header);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), header + other.Length);
        BitConverter.TryWriteBytes(bytes.AsSpan(12, 4), text.Length);
        text.CopyTo(bytes, header + other.Length);
        return bytes;
    }
}
