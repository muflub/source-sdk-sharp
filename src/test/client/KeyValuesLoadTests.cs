using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed KeyValues text load (<see cref="KeyValuesLoad"/>) against
/// pristine KeyValues.cpp's LoadFromBuffer, run offline by
/// src/test/oracle/native/keyvalues_load_oracle_test.cpp over
/// <c>Fixtures/keyvalues-load-cases.tsv</c> into
/// <c>Fixtures/keyvalues-load-stock.tsv</c>: every key of every case, in
/// order, with stock's type and value. Names compare case-insensitively and
/// only that way: stock's names come from a process-wide case-insensitive
/// symbol table, so their casing is the first one that run used.
/// </summary>
public class KeyValuesLoadTests
{
    private sealed class Log : ILog
    {
        public readonly List<string> Warnings = new();
        public void Msg(string message) { }
        public void Warning(string message) => Warnings.Add(message);
    }

    private static string Fixture(string name)
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", name);
        Assert.True(File.Exists(path), "missing fixture: " + path);
        return path;
    }

    private static byte[] Unescape(string text)
    {
        var o = new List<byte>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\') { o.Add((byte)c); continue; }
            char e = text[++i];
            switch (e)
            {
                case 'n': o.Add(10); break;
                case 'r': o.Add(13); break;
                case 't': o.Add(9); break;
                case 'v': o.Add(11); break;
                case 'f': o.Add(12); break;
                case '\\': o.Add((byte)'\\'); break;
                case 'x': o.Add(byte.Parse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 2; break;
                default: throw new InvalidDataException("bad escape \\" + e);
            }
        }
        return o.ToArray();
    }

    private static string Escape(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            switch (b)
            {
                case 10: sb.Append("\\n"); break;
                case 13: sb.Append("\\r"); break;
                case 9: sb.Append("\\t"); break;
                case 11: sb.Append("\\v"); break;
                case 12: sb.Append("\\f"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default:
                    if (b < 0x20 || b >= 0x7f) sb.Append(CultureInfo.InvariantCulture, $"\\x{b:x2}");
                    else sb.Append((char)b);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void Dump(KeyValuesNode kv, int depth, string name, List<string[]> rows)
    {
        string value = kv.Type switch
        {
            KeyValuesType.String => Escape(kv.StringValue),
            KeyValuesType.Int => kv.IntValue.ToString(CultureInfo.InvariantCulture),
            KeyValuesType.Float => BitConverter.SingleToInt32Bits(kv.FloatValue).ToString("x8", CultureInfo.InvariantCulture),
            KeyValuesType.UInt64 => kv.UInt64Value.ToString("x16", CultureInfo.InvariantCulture),
            _ => "",
        };
        rows.Add([name, depth.ToString(CultureInfo.InvariantCulture), Escape(kv.Name), ((int)kv.Type).ToString(CultureInfo.InvariantCulture), value]);
        foreach (KeyValuesNode c in kv.Children)
            Dump(c, depth + 1, name, rows);
    }

    private static KeyValuesLoad Loader(Log log, Dictionary<string, string>? files = null, bool deck = false) =>
        new(log, files == null ? null : (path, _) => files.TryGetValue(path, out string? t) ? Encoding.Latin1.GetBytes(t) : null, () => deck);

    [Fact]
    public void EveryCaseLoadsAsStockLoadsIt()
    {
        var ours = new List<string[]>();
        int cases = 0;
        var loader = Loader(new Log());
        foreach (string line in File.ReadAllLines(Fixture("keyvalues-load-cases.tsv"), Encoding.Latin1))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            string name = line[..tab];
            for (KeyValuesNode? k = loader.LoadFromBuffer("vmt", "case.vmt", Unescape(line[(tab + 1)..])); k != null; k = k.Next)
                Dump(k, 0, name, ours);
            cases++;
        }
        Assert.True(cases >= 130, $"only {cases} cases read");

        var stock = File.ReadAllLines(Fixture("keyvalues-load-stock.tsv"), Encoding.Latin1)
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(stock.Count > 400, $"the golden read as {stock.Count} keys");

        int n = Math.Min(stock.Count, ours.Count);
        for (int i = 0; i < n; i++)
        {
            string[] s = stock[i], o = ours[i];
            bool same = s[0] == o[0] && s[1] == o[1] && string.Equals(s[2], o[2], StringComparison.OrdinalIgnoreCase) && s[3] == o[3] && s[4] == o[4];
            Assert.True(same, $"key {i}: stock [{string.Join(" | ", s)}], ours [{string.Join(" | ", o)}]");
        }
        Assert.Equal(stock.Count, ours.Count);
    }

    [Fact]
    public void AFileIsReadThroughTheReaderOnItsPath()
    {
        var asked = new List<(string, string?)>();
        var loader = new KeyValuesLoad(new Log(), (path, id) => { asked.Add((path, id)); return path == "materials/a.vmt" ? "\"Sprite\" { \"$x\" 1 }"u8.ToArray() : null; }, () => false);

        KeyValuesNode? kv = loader.LoadFromFile("vmt", "materials/a.vmt", "GAME");
        Assert.NotNull(kv);
        Assert.Equal("Sprite", kv!.NameText);
        Assert.Equal(KeyValuesType.Int, kv.FirstChild!.Type);
        Assert.Null(loader.LoadFromFile("vmt", "materials/missing.vmt", "GAME"));
        Assert.Equal([("materials/a.vmt", "GAME"), ("materials/missing.vmt", "GAME")], asked);
    }

    [Fact]
    public void BaseMergesIntoTheFirstRootAndIncludeAppendsARoot()
    {
        var files = new Dictionary<string, string>
        {
            ["materials/sub/base.vmt"] = "\"B\" { \"$a\" \"base\" \"$b\" \"2\" \"Proxies\" { \"P\" { \"x\" 1 } } }",
            ["materials/sub/inc.vmt"] = "\"Inc\" { \"$c\" \"3\" }",
        };
        var loader = Loader(new Log(), files);
        KeyValuesNode root = loader.LoadFromBuffer("vmt", "materials/sub/m.vmt",
            "#base \"base.vmt\"\n#include \"inc.vmt\"\n\"M\" { \"$A\" \"mine\" \"Proxies\" { \"Q\" { } } }"u8);

        // The first root keeps its own $a (a match, merged into: it has no
        // children to merge), gains $b, and its Proxies gains P after Q.
        Assert.Equal(["$A", "Proxies", "$b"], root.Children.Select(c => c.NameText));
        Assert.Equal("mine", Encoding.Latin1.GetString(root.FirstChild!.StringValue));
        Assert.Equal(["Q", "P"], root.Children.ElementAt(1).Children.Select(c => c.NameText));
        Assert.Equal("Inc", root.Next?.NameText);
        Assert.Null(root.Next!.Next);
    }

    [Fact]
    public void WithNoReaderIncludesAreReadAndIgnored()
    {
        KeyValuesNode root = Loader(new Log()).LoadFromBuffer("vmt", "m.vmt", "#base \"b.vmt\"\n\"M\" { \"$a\" 1 }"u8);
        Assert.Equal("M", root.NameText);
        Assert.Single(root.Children);
        Assert.Null(root.Next);
    }

    [Fact]
    public void DeckConditionalsFollowTheInjectedAnswer()
    {
        ReadOnlySpan<byte> text = "\"M\" { \"$a\" 1 [$DECK] \"$b\" 2 [!$DECK] }"u8;
        Assert.Equal(["$a"], Loader(new Log(), deck: true).LoadFromBuffer("vmt", "m", text).Children.Select(c => c.NameText));
        Assert.Equal(["$b"], Loader(new Log(), deck: false).LoadFromBuffer("vmt", "m", text).Children.Select(c => c.NameText));
    }

    [Fact]
    public void AUtf16FileLoadsEmptyAndIsSaid()
    {
        var log = new Log();
        KeyValuesNode root = Loader(log).LoadFromBuffer("vmt", "m.vmt", new byte[] { 0xFF, 0xFE, (byte)'"', 0, (byte)'M', 0 });
        Assert.Equal("vmt", root.NameText);
        Assert.Null(root.FirstChild);
        Assert.Contains(log.Warnings, w => w.Contains("UTF-16", StringComparison.Ordinal));
    }

    [Fact]
    public void AParseErrorIsSaidWithTheFile()
    {
        var log = new Log();
        Loader(log).LoadFromBuffer("vmt", "materials/bad.vmt", "\"M\" { \"a\" }"u8);
        Assert.Contains(log.Warnings, w => w == "KeyValues Error: RecursiveLoadFromBuffer:  got } in key in file materials/bad.vmt\n");
    }
}
