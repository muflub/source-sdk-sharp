using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed save field codec (<see cref="SaveBlockCodec"/>, shared interop:
/// the client's DECALLIST and MUSICLIST, the server's engine headers) against
/// stock's CSave::WriteFields and CRestore::ReadFields, run offline from the
/// pristine saverestore.cpp by
/// src/test/oracle/native/savegame_codec_oracle_test.cpp
/// (<c>Fixtures/savegame-stock.tsv</c>, README beside it). Both ways, byte for
/// byte: every struct stock wrote is written here and must come out as stock's
/// bytes and token table; every block stock read is read here from stock's
/// bytes and must leave the struct as stock left it, at the same cursor.
/// </summary>
public sealed unsafe class SaveCodecOracleTests : IDisposable
{
    private const int TokenSlots = 0xfff;

    private readonly List<nint> _native = [];

    public void Dispose()
    {
        foreach (nint p in _native)
            NativeMemory.Free((void*)p);
        _native.Clear();
    }

    private byte* Alloc(int bytes)
    {
        byte* p = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(bytes, 1));
        _native.Add((nint)p);
        return p;
    }

    private byte* Name(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        byte* p = Alloc(bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    private byte* Bytes(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        byte* p = Alloc(bytes.Length);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    private static string Hex(byte* p, int n) => Convert.ToHexString(new ReadOnlySpan<byte>(p, n)).ToLowerInvariant();

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static float Bits(string hex) => BitConverter.Int32BitsToSingle(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private sealed record Map(int StructBytes, SaveFieldDesc[] Fields);

    /// <summary>A level as the row has it: the base time, and the landmark GetLandmark() answers.</summary>
    private readonly record struct Level(float Base, float X, float Y, float Z)
    {
        public static Level Parse(string baseHex, string use, string landmark)
        {
            string[] v = landmark.Split(',');
            return use == "1" ? new Level(Bits(baseHex), Bits(v[0]), Bits(v[1]), Bits(v[2]))
                              : new Level(Bits(baseHex), 0f, 0f, 0f);
        }

        public SaveLevel Codec => new(Base, X, Y, Z);
    }

    /// <summary>A fresh 0xfff-slot table, or one laid back down from a row's `slot:name` list.</summary>
    private SaveTokenTable Table(string? tokens = null)
    {
        byte** slots = (byte**)Alloc(TokenSlots * sizeof(nint));
        if (!string.IsNullOrEmpty(tokens))
        {
            foreach (string pair in tokens.Split(','))
            {
                int colon = pair.IndexOf(':');
                slots[I(pair[..colon])] = Name(pair[(colon + 1)..]);
            }
        }
        return new SaveTokenTable(slots, TokenSlots);
    }

    private static string Tokens(SaveTokenTable table)
    {
        var s = new StringBuilder();
        for (int i = 0; i < table.Count; i++)
        {
            byte* name = table.FromSymbol(i);
            if (name == null)
                continue;
            if (s.Length > 0)
                s.Append(',');
            s.Append(i.ToString(CultureInfo.InvariantCulture)).Append(':').Append(SaveBlockCodec.NameText(name));
        }
        return s.ToString();
    }

    private sealed class Replay
    {
        public readonly Dictionary<string, Map> Maps = new(StringComparer.Ordinal);
        public readonly List<string> WriteMisses = [];
        public readonly List<string> ReadMisses = [];
        public readonly List<string> StreamMisses = [];
        public int Writes, Reads, StreamWrites, StreamReads, Streams, Refused, LandmarkReads, OmittedZeroes;
    }

    private Map ParseMap(string fields, int structBytes)
    {
        var descs = new List<SaveFieldDesc>();
        foreach (string f in fields.Split(';'))
        {
            string[] p = f.Split(':');
            descs.Add(new SaveFieldDesc(Name(p[0]), I(p[1]), I(p[2]), I(p[3])));
        }
        return new Map(structBytes, [.. descs]);
    }

    private static void Miss(List<string> misses, int line, string what)
        => misses.Add("line " + line.ToString(CultureInfo.InvariantCulture) + ": " + what);

    private static string First(List<string> misses) => string.Join("\n", misses.Take(8));

    private Replay Run()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "savegame-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock save codec is missing: " + path);

        var r = new Replay();
        string lastWritten = "", lastTokens = "";
        int line = 0;

        // A stream's state between its rows.
        SaveTokenTable streamTable = default;
        byte* streamBuffer = null;
        var streamCursor = 0;
        Level streamWrite = default, streamRead = default;
        string streamBytes = "";

        foreach (string text in File.ReadLines(path))
        {
            line++;
            if (text.Length == 0 || text[0] == '#')
                continue;

            string[] c = text.Split('\t');
            switch (c[0])
            {
                case "M":
                    r.Maps[c[1]] = ParseMap(c[3], I(c[2]));
                    break;

                case "W":
                {
                    Map map = r.Maps[c[2]];
                    Level level = Level.Parse(c[4], c[5], c[6]);
                    byte* pStruct = Bytes(c[7]);
                    SaveTokenTable table = Table();
                    byte* buffer = Alloc(1 << 16);
                    var cursor = new SaveCursor(buffer, 1 << 16);
                    int written = SaveBlockCodec.WriteFieldBlock(ref cursor, table, Name(c[3]), pStruct, map.Fields,
                                                                 level.Codec, out _);
                    string got = written < 0 ? "<refused>" : Hex(buffer, cursor.Pos);
                    if (got != c[8])
                        Miss(r.WriteMisses, line, $"{c[1]}: stock wrote {c[8]}, the codec wrote {got}");
                    else if (Tokens(table) != c[9])
                        Miss(r.WriteMisses, line, $"{c[1]}: stock's table is {c[9]}, the codec's is {Tokens(table)}");

                    if (BitConverter.ToInt32(Convert.FromHexString(c[8]), 4) < map.Fields.Length)
                        r.OmittedZeroes++;
                    lastWritten = c[8];
                    lastTokens = c[9];
                    r.Writes++;
                    break;
                }

                case "R":
                {
                    Map map = r.Maps[c[2]];
                    Level level = Level.Parse(c[4], c[5], c[6]);
                    byte* pStruct = Bytes(c[7]);
                    int length = lastWritten.Length / 2;
                    var cursor = new SaveCursor(Bytes(lastWritten), length);
                    int read = SaveBlockCodec.ReadFieldBlock(ref cursor, Table(lastTokens), Name(c[3]), pStruct, map.Fields,
                                                             level.Codec, out string? refusal);
                    int ret = read < 0 ? 0 : 1;
                    string got = Hex(pStruct, map.StructBytes);
                    if (ret != I(c[8]) || cursor.Pos != I(c[9]) || got != c[10])
                        Miss(r.ReadMisses, line, $"{c[1]}: stock answered {c[8]} at {c[9]} with {c[10]}; the codec "
                                                 + $"answered {ret} ({refusal ?? "read"}) at {cursor.Pos} with {got}");
                    if (ret == 0)
                        r.Refused++;
                    if (c[5] == "1")
                        r.LandmarkReads++;
                    r.Reads++;
                    break;
                }

                case "S":
                    streamWrite = Level.Parse(c[2], c[3], c[4]);
                    streamRead = Level.Parse(c[5], c[6], c[7]);
                    streamTable = Table();
                    streamBuffer = Alloc(1 << 16);
                    streamCursor = 0;
                    break;

                case "SW":
                {
                    Map map = r.Maps[c[2]];
                    var cursor = new SaveCursor(streamBuffer + streamCursor, (1 << 16) - streamCursor);
                    int written = SaveBlockCodec.WriteFieldBlock(ref cursor, streamTable, Name(c[3]), Bytes(c[4]), map.Fields,
                                                                 streamWrite.Codec, out _);
                    if (written < 0)
                        Miss(r.StreamMisses, line, $"{c[1]}: the codec refused a block stock wrote");
                    else
                        streamCursor += written;
                    r.StreamWrites++;
                    break;
                }

                case "SO":
                {
                    string got = Hex(streamBuffer, streamCursor);
                    if (got != c[2])
                        Miss(r.StreamMisses, line, $"{c[1]}: stock's stream is {c[2].Length / 2} bytes, the codec's "
                                                   + $"{streamCursor}; they differ");
                    else if (Tokens(streamTable) != c[3])
                        Miss(r.StreamMisses, line, $"{c[1]}: the stream's token tables differ");

                    // The reads run over STOCK's bytes and table, as a load would.
                    streamBytes = c[2];
                    streamBuffer = Bytes(c[2]);
                    streamTable = Table(c[3]);
                    streamCursor = 0;
                    r.Streams++;
                    break;
                }

                case "SR":
                {
                    Map map = r.Maps[c[2]];
                    byte* pStruct = Alloc(map.StructBytes);
                    new Span<byte>(pStruct, map.StructBytes).Fill(0xcd);
                    var cursor = new SaveCursor(streamBuffer + streamCursor, streamBytes.Length / 2 - streamCursor);
                    int read = SaveBlockCodec.ReadFieldBlock(ref cursor, streamTable, Name(c[3]), pStruct, map.Fields,
                                                             streamRead.Codec, out string? refusal);
                    if (read > 0)
                        streamCursor += read;
                    string got = Hex(pStruct, map.StructBytes);
                    if ((read < 0 ? 0 : 1) != I(c[4]) || streamCursor != I(c[5]) || got != c[6])
                        Miss(r.StreamMisses, line, $"{c[1]} {c[2]}: stock answered {c[4]} at {c[5]} with {c[6]}; the "
                                                   + $"codec answered {read} ({refusal ?? "read"}) at {streamCursor} with {got}");
                    r.StreamReads++;
                    break;
                }

                default:
                    Assert.Fail($"line {line}: an unknown row tag '{c[0]}'");
                    break;
            }
        }

        return r;
    }

    [Fact]
    public void EveryStructStockWroteComesOutAsStocksBytesAndTable()
    {
        Replay r = Run();
        Assert.True(r.WriteMisses.Count == 0,
                    $"{r.WriteMisses.Count} of {r.Writes} writes differ from stock:\n" + First(r.WriteMisses));
    }

    [Fact]
    public void EveryBlockStockReadLeavesTheStructAsStockLeftIt()
    {
        Replay r = Run();
        Assert.True(r.ReadMisses.Count == 0,
                    $"{r.ReadMisses.Count} of {r.Reads} reads differ from stock:\n" + First(r.ReadMisses));
    }

    [Fact]
    public void TheEnginesStreamsComeOutAndBackAsStocksDid()
    {
        Replay r = Run();
        Assert.True(r.StreamMisses.Count == 0,
                    $"{r.StreamMisses.Count} stream rows differ from stock:\n" + First(r.StreamMisses));
    }

    // The positive arms: the replay compared rows of every kind, including the
    // ones that make the codec's decisions visible.
    [Fact]
    public void TheReplayReachesEveryArm()
    {
        Replay r = Run();
        Assert.Equal(13, r.Maps.Count);
        Assert.True(r.Writes > 300, $"{r.Writes} writes");
        Assert.True(r.Reads > 300, $"{r.Reads} reads");
        Assert.True(r.OmittedZeroes > 100, $"{r.OmittedZeroes} writes where stock omitted a zero field");
        Assert.True(r.LandmarkReads > 50, $"{r.LandmarkReads} reads under a used landmark");
        Assert.Equal(1, r.Refused);
        Assert.Equal(3, r.Streams);
        Assert.Equal(r.StreamWrites, r.StreamReads);
        Assert.True(r.StreamWrites > 20, $"{r.StreamWrites} stream blocks");
    }
}
