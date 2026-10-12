using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A sprite model's material, read in C# (C9 KeyValues K4): the name rule
/// spritemodel.cpp applied, the .vmt read on GAME through the KeyValues load,
/// stock's warning for a missing file, the block native rebuilds the tree
/// from, and the binding's answers.
/// </summary>
public class SpriteMaterialFilesTests
{
    private sealed class Log : ILog
    {
        public readonly List<string> Warnings = new();
        public void Msg(string message) { }
        public void Warning(string message) => Warnings.Add(message);
    }

    [Theory]
    [InlineData("sprites/glow01.spr", "materials/sprites/glow01")]
    [InlineData("Sprites\\Light_Glow02.vmt", "materials/sprites/light_glow02")]
    [InlineData("sprites/a.b/c", "materials/sprites/a.b/c")]
    [InlineData("sprites/noext", "materials/sprites/noext")]
    [InlineData(".spr", "materials/.spr")]
    [InlineData("//server/share/x.spr", "//server/share/x")]
    [InlineData("///notunc.spr", "materials////notunc")]
    public void TheMaterialNameIsSpritemodelsRule(string sprite, string material)
        => Assert.Equal(material, SpriteMaterialFiles.MaterialName(sprite));

    [Fact]
    public void TheVmtIsReadOnGameAndAMissingOneIsStocksWarning()
    {
        var log = new Log();
        var asked = new List<(string, string?)>();
        var load = new KeyValuesLoad(log, (p, id) => { asked.Add((p, id)); return p == "materials/sprites/a.vmt" ? "\"Sprite\" { \"$x\" 1 }"u8.ToArray() : null; }, () => false);
        var files = new SpriteMaterialFiles(log, load);

        var read = files.Read("sprites/a.spr");
        Assert.NotNull(read);
        Assert.Equal("materials/sprites/a", read!.Value.Material);
        Assert.Equal("Sprite", read.Value.Vmt.NameText);
        Assert.Empty(log.Warnings);

        Assert.Null(files.Read("sprites/b.spr"));
        Assert.Equal(["Unable to load sprite material materials/sprites/b.vmt!\n"], log.Warnings);
        Assert.Equal([("materials/sprites/a.vmt", "GAME"), ("materials/sprites/b.vmt", "GAME")], asked);
    }

    [Fact]
    public void TheBlockIsTheNameThenTheTreeInPreorder()
    {
        var load = new KeyValuesLoad(new Log(), null, () => false);
        KeyValuesNode root = load.LoadFromBuffer("vmt", "m", "\"S\" { \"$s\" \"t\" \"$i\" 7 \"$f\" 1.5 \"$u\" 0x0000000000000010 \"P\" { } }"u8);

        byte[] payload = new byte[256];
        int n = KeyValuesBlock.Write(payload, "materials/x", root);

        var expect = new List<byte>();
        void Str(string s) { expect.AddRange(Encoding.ASCII.GetBytes(s)); expect.Add(0); }
        void U32(uint v) => expect.AddRange(BitConverter.GetBytes(v));
        Str("materials/x");
        expect.Add(0); Str("S"); U32(5);
        expect.Add(1); Str("$s"); Str("t"); U32(0);
        expect.Add(2); Str("$i"); expect.AddRange(BitConverter.GetBytes(7)); U32(0);
        expect.Add(3); Str("$f"); expect.AddRange(BitConverter.GetBytes(1.5f)); U32(0);
        expect.Add(7); Str("$u"); expect.AddRange(BitConverter.GetBytes(16UL)); U32(0);
        expect.Add(0); Str("P"); U32(0);

        Assert.Equal(expect.Count, n);
        Assert.Equal(expect.ToArray(), payload[..n]);
        Assert.Equal(-1, KeyValuesBlock.Write(new byte[n - 1], "materials/x", root));
    }

    [Fact]
    public unsafe void TheBindingAnswersTheBlockOrWhyNot()
    {
        string? asked = null;
        var binding = new MaterialFilesClientBinding(new Crossings(), new Log(), (name, payload) =>
        {
            asked = name;
            if (name == "none") return -1;
            if (payload.Length < 3) return -2;
            payload[0] = 1; payload[1] = 2; payload[2] = 3;
            return 3;
        }, (_, _, _, _, _, _) => -1);

        byte[] block = new byte[BitBlock.HeaderBytes + 16];
        byte[] name = Encoding.ASCII.GetBytes("sprites/a.spr\0");
        byte[] none = Encoding.ASCII.GetBytes("none\0");
        fixed (byte* b = block)
        fixed (byte* n = name)
        fixed (byte* x = none)
        {
            Assert.Equal(3, binding.SpriteMaterial(n, b, block.Length));
            Assert.Equal("sprites/a.spr", asked);
            Assert.Equal(24, BitBlock.Bits(block));
            Assert.Equal(new byte[] { 1, 2, 3 }, block[BitBlock.HeaderBytes..(BitBlock.HeaderBytes + 3)]);

            Assert.Equal(-1, binding.SpriteMaterial(x, b, block.Length));
            Assert.Equal(-2, binding.SpriteMaterial(n, b, BitBlock.HeaderBytes + 2));
            Assert.Equal(-2, binding.SpriteMaterial(n, b, 2));
        }
    }

    [Fact]
    public void DeckFollowsTheCommandLineThenTheEnvironment()
    {
        Assert.False(SpriteMaterialFiles.IsSteamDeck(["game", "-nogamepadui", "-gamepadui"]));
        Assert.True(SpriteMaterialFiles.IsSteamDeck(["game", "-gamepadui"]));
    }
}
