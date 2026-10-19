using System.Text;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A Source language file (<c>resource/mod_sharp_english.txt</c>: UTF-16
/// KeyValues, "lang" { "Tokens" { "Key" "Text" } }) as a localiser: the same
/// file the game's localiser reads, so previews say what the game will.
/// Keys match without case, as the engine's do.
/// </summary>
public sealed class UiLanguageFile : IUiLocaliser
{
    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public UiLanguageFile(string text)
    {
        var items = Tokenise(text);
        // An all-whitespace file is nothing to say (the Chain base case); any
        // text that says something but has no Tokens block is a broken file.
        if (string.IsNullOrWhiteSpace(text)) return;
        int tokens = items.FindIndex(t => t.Equals("Tokens", StringComparison.OrdinalIgnoreCase));
        if (tokens < 0 || tokens + 1 >= items.Count || items[tokens + 1] != "{")
            throw new InvalidDataException("no \"Tokens\" block");

        for (int i = tokens + 2; i + 1 < items.Count && items[i] != "}"; i += 2)
            _tokens[items[i]] = items[i + 1];
    }

    /// <summary>Reads a language file in whatever encoding its byte-order mark says (UTF-16 as shipped).</summary>
    public static UiLanguageFile Load(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return new UiLanguageFile(reader.ReadToEnd());
    }

    /// <summary>
    /// A language file's bytes as a game file read hands them over (a file in
    /// a mounted VPK has no path on disk), decoded as <see cref="Load"/>
    /// decodes a file: the byte-order mark says the encoding, UTF-16 as
    /// shipped, and no mark is UTF-8.
    /// </summary>
    public static UiLanguageFile Parse(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> le = [0xFF, 0xFE], be = [0xFE, 0xFF], utf8 = [0xEF, 0xBB, 0xBF];
        return new UiLanguageFile(bytes.StartsWith(le) ? Encoding.Unicode.GetString(bytes[2..])
                                  : bytes.StartsWith(be) ? Encoding.BigEndianUnicode.GetString(bytes[2..])
                                  : Encoding.UTF8.GetString(bytes.StartsWith(utf8) ? bytes[3..] : bytes));
    }

    /// <summary>
    /// Several language files as one localiser, first file wins on a shared
    /// key (the preview's chain: the base mod's file, then each game mod's,
    /// as GameUiLocaliser asks the mod's own file before the engine's).
    /// </summary>
    public static UiLanguageFile Chain(IReadOnlyList<string> paths)
    {
        var merged = new UiLanguageFile("");
        foreach (string path in paths)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            foreach (var (key, value) in new UiLanguageFile(reader.ReadToEnd())._tokens)
                merged._tokens.TryAdd(key, value);
        }
        return merged;
    }

    /// <summary>The embedded core file's name, and its place in the repository (previews read it from disk).</summary>
    public const string CoreName = "resource/sourcesharp_ui_english.txt", CoreRepoPath = "src/client/ui/resource/sourcesharp_ui_english.txt";

    /// <summary>
    /// SourceSharp's own UI copy (the #SS_UI_ tokens), embedded in the toolkit:
    /// the core's strings, which no mod folder has to carry.
    /// </summary>
    public static UiLanguageFile Core()
    {
        using var stream = typeof(UiLanguageFile).Assembly.GetManifestResourceStream(CoreName)
            ?? throw new InvalidDataException($"the toolkit carries no {CoreName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return new UiLanguageFile(reader.ReadToEnd());
    }

    /// <summary>Several loaded files as one, first file wins on a shared key (a mod's file, then the core's).</summary>
    public static UiLanguageFile Chain(params UiLanguageFile[] files)
    {
        var merged = new UiLanguageFile("");
        foreach (var file in files)
            foreach (var (key, value) in file._tokens)
                merged._tokens.TryAdd(key, value);
        return merged;
    }

    public int Count => _tokens.Count;

    /// <summary>The token names it holds, without the '#' (for comparing two readings of a file).</summary>
    public IEnumerable<string> Keys => _tokens.Keys;

    /// <summary>Allocation-free: the key is looked up as a span of the token, not a substring.</summary>
    public string? Find(string token)
        => token.Length > 1 && token[0] == '#'
           && _tokens.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(token.AsSpan(1), out var text) ? text : null;

    /// <summary>Quoted strings and braces, with // comments dropped and \n, \", \\ unescaped.</summary>
    private static List<string> Tokenise(string text)
    {
        var items = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c is '{' or '}')
            {
                items.Add(c.ToString());
                i++;
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else
                        sb.Append(text[i]);
                    i++;
                }
                i++;
                items.Add(sb.ToString());
                // A conditional like [$WIN32] after a value is skipped.
                continue;
            }
            if (c == '[')
            {
                while (i < text.Length && text[i] != ']') i++;
                i++;
                continue;
            }
            i++;
        }
        return items;
    }
}
