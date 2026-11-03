//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>One HUD icon: the texture file its table entry's file key names
/// (the key's prefix is on the icon's name, hud.cpp:111-129) and the pixel
/// rectangle its <c>x</c>/<c>y</c>/<c>width</c>/<c>height</c> make, with
/// −1 meaning the whole texture (stock's GetInt defaults).</summary>
public readonly record struct HudIconEntry(string File, int X, int Y, int Width, int Height);

/// <summary>
/// The game's HUD icon tables (<c>scripts/hud_textures.txt</c>,
/// <c>scripts/mod_textures.txt</c>): a name to the texture file and pixel
/// rectangle a screen's icon key draws. Stock reads a file's
/// <c>TextureData</c> block, its <c>TextureFileRefs</c> block naming the
/// prefix each file key's icon is named under (TF2's
/// <c>"dfile" { "prefix" "d_" }</c> makes <c>"skull_tf" { "dfile" ... }</c>
/// the icon <c>d_skull_tf</c>), and skips <c>font</c> entries — those
/// are glyphs, not texture rectangles (CHud::LoadHudTextures,
/// hud.cpp:58-141). Chain is first-wins: hud_textures is loaded before
/// mod_textures (hud.cpp:464-465) and AddSearchableHudIconToList answers
/// the icon already in the list, so the first file to name an icon keeps
/// it (hud.cpp:772-787).
/// </summary>
public sealed class HudIconTable
{
    private readonly Dictionary<string, HudIconEntry> _icons = new(StringComparer.Ordinal);

    public HudIconEntry? Entry(string name)
        => _icons.TryGetValue(name, out HudIconEntry entry) ? entry : null;

    /// <summary>Several files as one table, first file wins on a shared name
    /// (hud_textures before mod_textures, hud.cpp:464-465/:772-787).</summary>
    public static HudIconTable Chain(params HudIconTable[] tables)
    {
        var merged = new HudIconTable();
        foreach (var table in tables)
            foreach (var (name, entry) in table._icons)
                merged._icons.TryAdd(name, entry);
        return merged;
    }

    public static HudIconTable Parse(string text)
    {
        var items = Tokenise(text);
        var table = new HudIconTable();
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(items, 0, items.Count, refs, table);
        return table;
    }

    /// <summary>Reads a table in whatever encoding its byte-order mark says,
    /// as <see cref="UiLanguageFile.Parse"/> decodes a language file: the
    /// game files can arrive UTF-16.</summary>
    public static HudIconTable Parse(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> le = [0xFF, 0xFE], be = [0xFE, 0xFF], utf8 = [0xEF, 0xBB, 0xBF];
        return Parse(bytes.StartsWith(le) ? Encoding.Unicode.GetString(bytes[2..])
                     : bytes.StartsWith(be) ? Encoding.BigEndianUnicode.GetString(bytes[2..])
                     : Encoding.UTF8.GetString(bytes.StartsWith(utf8) ? bytes[3..] : bytes));
    }

    /// <summary>Quoted and bare words plus braces — a KeyValues file's
    /// tokens. Unlike a language file's, an icon table's block names
    /// (<c>TextureData</c>, <c>font</c>) are written bare.</summary>
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
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}'))
                i++;
            items.Add(text[start..i]);
        }
        return items;
    }

    /// <summary>Index past the block's closing brace; the end when it never closes.</summary>
    private static int BlockEnd(List<string> items, int open)
    {
        int depth = 0;
        for (int i = open; i < items.Count; i++)
        {
            if (items[i] == "{") depth++;
            else if (items[i] == "}" && --depth == 0) return i + 1;
        }
        return items.Count;
    }

    /// <summary>Walks [start, end) for named blocks: a TextureFileRefs fills
    /// the prefixes, a TextureData the entries, anything else is descended
    /// into — both blocks sit under the file's root key (TF2's mod_textures
    /// roots at "sprites/640_hud").</summary>
    private static void Walk(List<string> items, int start, int end,
                             Dictionary<string, string> refs, HudIconTable table)
    {
        int i = start;
        while (i < end)
        {
            if (items[i] is "{" or "}" || i + 1 >= end || items[i + 1] != "{")
            {
                i++;
                continue;
            }
            int bodyEnd = BlockEnd(items, i + 1) - 1;   // the closing brace's index
            switch (items[i])
            {
                case "TextureFileRefs":
                    ReadRefs(items, i + 2, bodyEnd, refs);
                    break;
                case "TextureData":
                    ReadEntries(items, i + 2, bodyEnd, refs, table);
                    break;
                default:
                    Walk(items, i + 2, bodyEnd, refs, table);
                    break;
            }
            i = bodyEnd + 1;
        }
    }

    /// <summary>A TextureFileRefs block: one sub-block per file key, its
    /// <c>"prefix"</c> the name prefix that key's files are written under.</summary>
    private static void ReadRefs(List<string> items, int start, int end, Dictionary<string, string> refs)
    {
        int at = start;
        while (at < end)
        {
            if (items[at] is "{" or "}" || at + 1 >= end || items[at + 1] != "{")
            {
                at++;
                continue;
            }
            int bodyEnd = BlockEnd(items, at + 1) - 1;
            for (int i = at + 2; i + 1 < bodyEnd; i += 2)
                if (items[i] == "prefix")
                    refs[items[at]] = items[i + 1];
            at = bodyEnd + 1;
        }
    }

    private static void ReadEntries(List<string> items, int start, int end,
                                    Dictionary<string, string> refs, HudIconTable table)
    {
        int at = start;
        while (at < end)
        {
            if (items[at] is "{" or "}" || at + 1 >= end || items[at + 1] != "{")
            {
                at++;
                continue;
            }
            int subEnd = BlockEnd(items, at + 1);
            AddEntry(items, at + 2, subEnd - 1, items[at], refs, table);
            at = subEnd;
        }
    }

    /// <summary>One TextureData entry: each file key it carries is an icon of
    /// its own, named the key's TextureFileRefs prefix then the entry's name,
    /// on the key's file as written — <c>"file"</c> with no prefix
    /// (hud.cpp:69, :111-129); a <c>font</c> key makes it a font icon stock
    /// continues past (hud.cpp:96-110), and x/y/width/height default to −1 —
    /// the whole texture.</summary>
    private static void AddEntry(List<string> items, int start, int end, string name,
                                 Dictionary<string, string> refs, HudIconTable table)
    {
        for (int i = start; i + 1 < end; i += 2)
            if (items[i] == "font")
                return;

        int x = Int(items, start, end, "x"), y = Int(items, start, end, "y");
        int width = Int(items, start, end, "width"), height = Int(items, start, end, "height");
        for (int i = start; i + 1 < end; i += 2)
        {
            string key = items[i];
            string? prefix = key == "file" ? "" : refs.TryGetValue(key, out string? p) ? p : null;
            if (prefix is not null)
                table._icons.TryAdd(prefix + name, new HudIconEntry(items[i + 1], x, y, width, height));
        }
    }

    private static int Int(List<string> items, int start, int end, string key)
    {
        for (int i = start; i + 1 < end; i += 2)
            if (items[i] == key && int.TryParse(items[i + 1], CultureInfo.InvariantCulture, out int value))
                return value;
        return -1;
    }
}
