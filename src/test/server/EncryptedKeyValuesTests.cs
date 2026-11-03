using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="EncryptedKeyValues"/>: stock's <c>ReadEncryptedKVFile</c>
/// (.txt first, else the .ctx through <c>UTIL_DecodeICE</c>) over the GAME
/// path. The sealed inputs are made with <see cref="IceKey.Encrypt"/>, which
/// <see cref="IceKeyTests"/> holds to published vectors.
/// </summary>
public class EncryptedKeyValuesTests
{
    private static readonly byte[] Key = "testkey!"u8.ToArray();

    // 3 whole blocks (24 bytes) and a 5-byte tail.
    private const string Script = "\"S\" { \"speed\" \"400\" }\n// tail";

    private sealed class Files : IEngineFiles
    {
        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
        public readonly List<string> Asked = new();

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
        {
            Asked.Add(path);
            return Bytes.TryGetValue(path, out var b) ? b : null;
        }

        public byte[]? ReadFileOnPath(string path, string pathId) => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
    }

    [Fact]
    public void A_ctx_alone_is_decrypted_and_parsed()
    {
        var files = new Files { Bytes = { ["scripts/x.ctx"] = Seal(Encoding.UTF8.GetBytes(Script)) } };

        var kv = new EncryptedKeyValues(files, Key).Read("scripts/x");

        Assert.Equal(400, kv.Find("S")!.GetInt("speed", 0));
        Assert.Equal(["scripts/x.txt", "scripts/x.ctx"], files.Asked);
    }

    [Fact]
    public void A_txt_is_preferred_over_the_ctx()
    {
        var files = new Files
        {
            Bytes =
            {
                ["scripts/x.txt"] = Encoding.UTF8.GetBytes("\"S\" { \"speed\" \"1\" }"),
                ["scripts/x.ctx"] = Seal(Encoding.UTF8.GetBytes(Script)),
            },
        };

        var kv = new EncryptedKeyValues(files, Key).Read("scripts/x");

        Assert.Equal(1, kv.Find("S")!.GetInt("speed", 0));
        Assert.Equal(["scripts/x.txt"], files.Asked);
    }

    [Fact]
    public void Missing_both_files_is_refused_naming_both()
    {
        var e = Assert.Throws<FileNotFoundException>(() => new EncryptedKeyValues(new Files(), Key).Read("scripts/x"));
        Assert.Contains("scripts/x.txt", e.Message);
        Assert.Contains("scripts/x.ctx", e.Message);
    }

    [Fact]
    public void DecodeIce_leaves_a_tail_shorter_than_a_block_as_read()
    {
        byte[] plain = Encoding.UTF8.GetBytes(Script);
        Assert.NotEqual(0, plain.Length % 8);
        int whole = plain.Length - plain.Length % 8;

        byte[] sealedBytes = Seal(plain);
        Assert.NotEqual(plain[..whole], sealedBytes[..whole]);

        var ice = new IceKey(0);
        ice.Set(Key);
        // Garble the tail: decoding must not touch it.
        sealedBytes[^1] = (byte)'#';
        EncryptedKeyValues.DecodeIce(ice, sealedBytes);

        Assert.Equal(plain[..whole], sealedBytes[..whole]);
        Assert.Equal((byte)'#', sealedBytes[^1]);
    }

    [Fact]
    public void The_read_buffer_is_not_decrypted_in_place()
    {
        byte[] sealedBytes = Seal(Encoding.UTF8.GetBytes(Script));
        byte[] before = sealedBytes.ToArray();
        var files = new Files { Bytes = { ["scripts/x.ctx"] = sealedBytes } };
        var reader = new EncryptedKeyValues(files, Key);

        reader.Read("scripts/x");

        Assert.Equal(before, sealedBytes);
        Assert.Equal(400, reader.Read("scripts/x").Find("S")!.GetInt("speed", 0));
    }

    [Fact]
    public void The_text_ends_at_the_first_NUL_as_stock_s_C_string_does()
    {
        byte[] withNul = [.. Encoding.UTF8.GetBytes("\"S\" { \"speed\" \"7\" }"), 0, .. "{{{ not kv"u8];
        var files = new Files { Bytes = { ["scripts/x.txt"] = withNul } };

        Assert.Equal(7, new EncryptedKeyValues(files, Key).Read("scripts/x").Find("S")!.GetInt("speed", 0));
    }

    [Fact]
    public void A_name_with_an_extension_is_refused()
        => Assert.Throws<ArgumentException>(() => new EncryptedKeyValues(new Files(), Key).Read("scripts/x.txt"));

    private static byte[] Seal(byte[] plain)
    {
        var ice = new IceKey(0);
        ice.Set(Key);
        byte[] sealedBytes = plain.ToArray();
        for (int at = 0; at + 8 <= sealedBytes.Length; at += 8)
            ice.Encrypt(plain.AsSpan(at, 8), sealedBytes.AsSpan(at, 8));
        return sealedBytes;
    }
}
