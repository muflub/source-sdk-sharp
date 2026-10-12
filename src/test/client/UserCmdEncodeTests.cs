using System.Globalization;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed usercmd encoder (<see cref="UserCmdCodec.EncodeToBlock"/>)
/// against pristine usercmd.cpp's WriteUsercmd, run offline by
/// src/test/oracle/native/usercmd_encode_oracle_test.cpp
/// (<c>Fixtures/usercmd-encode-stock.tsv</c>): 4,000 seeded command pairs, the
/// same bits and the same count, and the block's prefix says that count. The
/// decode half reads each back to the command it was written from.
/// </summary>
public class UserCmdEncodeTests
{
    private sealed record Row(int Case, DecodedUserCmd From, DecodedUserCmd To, int Bits, byte[] Payload);

    private static DecodedUserCmd Cmd(string[] f, int at)
    {
        int I(int k) => int.Parse(f[at + k], CultureInfo.InvariantCulture);
        float F(int k) => BitConverter.Int32BitsToSingle(int.Parse(f[at + k], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var c = new DecodedUserCmd();
        c.Cmd.CommandNumber = I(0);
        c.Cmd.TickCount = I(1);
        c.Cmd.Pitch = F(2);
        c.Cmd.Yaw = F(3);
        c.Cmd.Roll = F(4);
        c.Cmd.ForwardMove = F(5);
        c.Cmd.SideMove = F(6);
        c.Cmd.UpMove = F(7);
        c.Cmd.Buttons = I(8);
        c.Cmd.Impulse = I(9);
        c.Cmd.WeaponSelect = I(10);
        c.WeaponSubtype = I(11);
        c.Cmd.MouseDx = I(12);
        c.Cmd.MouseDy = I(13);
        return c;
    }

    private static List<Row> Golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "usercmd-encode-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock encodes are missing: " + path);
        var rows = new List<Row>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] f = line.Split('\t');
            string hex = f[31];
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            rows.Add(new Row(int.Parse(f[0], CultureInfo.InvariantCulture), Cmd(f, 1), Cmd(f, 15),
                             int.Parse(f[30], CultureInfo.InvariantCulture), bytes));
        }
        return rows;
    }

    [Fact]
    public void EveryCommandEncodesToStocksBits()
    {
        List<Row> rows = Golden();
        Assert.Equal(4000, rows.Count);

        Span<byte> block = stackalloc byte[BitBlock.HeaderBytes + 64];
        foreach (Row row in rows)
        {
            block.Fill(0xA5);
            int bits = UserCmdCodec.EncodeToBlock(row.To, row.From, block);
            Assert.True(bits == row.Bits, $"case {row.Case}: {bits} bits, stock wrote {row.Bits}");
            Assert.Equal(bits, BitBlock.Bits(block));

            // The written bits only: the last byte's spare bits are the
            // block's own business.
            ReadOnlySpan<byte> ours = BitBlock.Payload(block);
            for (int i = 0; i < bits; i++)
            {
                int a = (ours[i >> 3] >> (i & 7)) & 1, b = (row.Payload[i >> 3] >> (i & 7)) & 1;
                Assert.True(a == b, $"case {row.Case}: bit {i} is {a}, stock wrote {b}");
            }
        }
    }

    [Fact]
    public void EachEncodeDecodesBackAgainstTheZeroCommand()
    {
        int checkedRows = 0;
        Span<byte> block = stackalloc byte[BitBlock.HeaderBytes + 64];
        foreach (Row row in Golden())
        {
            DecodedUserCmd zero = default;
            int bits = UserCmdCodec.EncodeToBlock(row.To, zero, block);
            Assert.True(bits > 0);
            Assert.Equal(bits, UserCmdCodec.DecodeFromBlock(block, out DecodedUserCmd back));

            DecodedUserCmd want = row.To;
            want.Cmd.Impulse &= 0xFF;
            want.Cmd.MouseDx = (short)want.Cmd.MouseDx;
            want.Cmd.MouseDy = (short)want.Cmd.MouseDy;
            want.RandomSeed = UserCmdCodec.RandomSeedFor(want.Cmd.CommandNumber);
            if (want.Cmd.WeaponSelect == 0)
                want.WeaponSubtype = 0;   // the subtype rides inside the weapon's own presence bit
            Assert.Equal(want, back);
            checkedRows++;
        }
        Assert.Equal(4000, checkedRows);
    }

    [Fact]
    public void ABlockTooSmallAnswersMinusOne()
    {
        Span<byte> block = stackalloc byte[BitBlock.HeaderBytes + 2];
        var to = new DecodedUserCmd();
        to.Cmd.CommandNumber = 12345;
        Assert.Equal(-1, UserCmdCodec.EncodeToBlock(to, default, block));
        Assert.Equal(-1, BitBlock.Bits(block));
    }
}
