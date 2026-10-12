//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// Valve's KeyValues TEXT format, read in C# — plan_gamedll_after.md Phase A3.
///
/// <para>
/// <b>Why this exists.</b> Part 9a's ownership test sends a KeyValues parse
/// into a lookup table to managed code: it is game-DLL logic with no engine
/// tie, and it is the thing C# is better at than C++. The two systems that
/// moved in A3 — <c>CDecalEmitterSystem</c> and half of <c>CPropData</c> — both
/// begin with one, so the parse had to come across with them.
/// </para>
///
/// <para>
/// <b>What it is NOT.</b> This is not a replacement for the engine's
/// <c>KeyValues</c> and does not try to be. It reads the subset the two game
/// scripts in question use, which is the subset every KV1 game script uses:
/// quoted and bare tokens, brace blocks, <c>//</c> comments, and a document
/// that may hold SEVERAL roots — which <c>decals_subrect.txt</c> does, and
/// which is why stock's own loader walks <c>GetNextKey()</c> after
/// <c>LoadFromFile</c>.
/// </para>
///
/// <para>
/// <b>Three things it deliberately does not do, each because doing it would be
/// a guess.</b> <c>#base</c> and <c>#include</c> are not followed — neither
/// file uses them, and following one would need the engine's search paths a
/// second time. Escape sequences are not interpreted, which is stock's own
/// default for a game script (<c>LoadFromBuffer</c> is called with escape
/// handling off). Conditional suffixes (<c>[$WIN32]</c>) are not evaluated;
/// they are read as part of the value, which is what makes their presence
/// visible rather than silently dropped.
/// </para>
///
/// <para>
/// <b>A malformed document throws.</b> Not "returns what it managed to read":
/// this project's recorded failure is a table that came up empty with nothing
/// said, and half a parse is that failure wearing a result.
/// </para>
/// </summary>
public sealed class KeyValuesText
{
    private readonly List<KeyValuesText> _children = new();

    private KeyValuesText(string name, string value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The key, as written.</summary>
    public string Name { get; }

    /// <summary>
    /// The value for a leaf, or the empty string for a block. A block and a
    /// leaf whose value is empty are told apart by <see cref="IsBlock"/> rather
    /// than by this, because <c>"X" ""</c> is a real and meaningful line in
    /// decals_subrect.txt — it says "do not decal this surface".
    /// </summary>
    public string Value { get; }

    /// <summary>Whether this key was written as a <c>{ }</c> block.</summary>
    public bool IsBlock { get; private set; }

    /// <summary>The block's children, in document order.</summary>
    public IReadOnlyList<KeyValuesText> Children => _children;

    /// <summary>
    /// The first child with this name, case-insensitively — the engine's
    /// <c>FindKey</c> is case-insensitive and a lookup that is not would find
    /// nothing on a file somebody re-cased.
    /// </summary>
    public KeyValuesText? Find(string name)
    {
        foreach (var child in _children)
        {
            if (string.Equals(child.Name, name, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    /// <summary>
    /// <c>KeyValues::FindKey</c>: a child by name, case-insensitively, where a
    /// <c>/</c> in the name descends one block per segment.
    /// </summary>
    public KeyValuesText? FindPath(string path)
    {
        KeyValuesText? at = this;
        foreach (string segment in path.Split('/'))
        {
            at = at.Find(segment);
            if (at is null)
                return null;
        }
        return at;
    }

    //-------------------------------------------------------------------------
    // tier1's TYPED READS -- plan_server_novalve.md P2.4. KeyValues types a
    // value when it parses it (RecursiveLoadFromBuffer): an empty value is a
    // string; "0x" plus sixteen hex digits a uint64; a value strtod consumes
    // whole, further than strtol, a float; a value strtol consumes whole an
    // int; anything else a string. GetInt/GetFloat/GetString then convert from
    // that type. A block reads as 0, 0f and the default string. Reproduced
    // rather than tidied: prop health is read through GetInt, and "30.5" is 30
    // there while "30abc" is 30 by atoi.
    //-------------------------------------------------------------------------

    private enum StockType { None, String, Int, Float, UInt64 }

    private StockType TypeOf(out int intValue, out float floatValue)
    {
        intValue = 0;
        floatValue = 0f;

        if (IsBlock)
            return StockType.None;

        string v = Value;
        if (v.Length == 0)
            return StockType.String;

        if (v.Length == 18 && v[0] == '0' && v[1] == 'x')
            return StockType.UInt64;

        long l = CStrtol(v, out int iEnd);
        double d = CStrtod(v, out int fEnd);

        // POSIX strtod reads hex; KeyValues undoes that.
        if (v.Length > 1 && char.ToLowerInvariant(v[1]) == 'x')
        {
            d = 0;
            fEnd = 0;
        }

        if (fEnd > iEnd && fEnd == v.Length)
        {
            floatValue = (float)d;
            return StockType.Float;
        }

        if (iEnd == v.Length)
        {
            intValue = unchecked((int)l);
            return StockType.Int;
        }

        return StockType.String;
    }

    /// <summary><c>KeyValues::GetInt( key, default )</c>.</summary>
    public int GetInt(string key, int defaultValue)
    {
        KeyValuesText? dat = FindPath(key);
        return dat is null ? defaultValue : dat.AsInt();
    }

    /// <summary><c>KeyValues::GetFloat( key, default )</c>.</summary>
    public float GetFloat(string key, float defaultValue)
    {
        KeyValuesText? dat = FindPath(key);
        return dat is null ? defaultValue : dat.AsFloat();
    }

    /// <summary><c>KeyValues::GetString( key, default )</c>.</summary>
    public string? GetString(string key, string? defaultValue)
    {
        KeyValuesText? dat = FindPath(key);
        return dat is null ? defaultValue : dat.AsString(defaultValue);
    }

    /// <summary><c>KeyValues::GetInt()</c> with no key: this node's own value.</summary>
    public int AsInt()
    {
        KeyValuesText dat = this;
        switch (dat.TypeOf(out int i, out float f))
        {
            case StockType.String: return unchecked((int)CStrtol(dat.Value, out _));
            case StockType.Float: return (int)f;
            case StockType.UInt64: return 0;
            case StockType.Int: return i;
            default: return 0;
        }
    }

    /// <summary><c>KeyValues::GetFloat()</c> with no key: this node's own value.</summary>
    public float AsFloat()
    {
        KeyValuesText dat = this;
        switch (dat.TypeOf(out int i, out float f))
        {
            case StockType.String: return (float)CStrtod(dat.Value, out _);
            case StockType.Float: return f;
            case StockType.Int: return i;
            case StockType.UInt64: return (float)ParseUInt64(dat.Value);
            default: return 0f;
        }
    }

    /// <summary>
    /// <c>KeyValues::GetString( NULL, default )</c>: this node's own value. A
    /// block answers <paramref name="defaultValue"/>, as stock's does.
    /// </summary>
    public string? AsString(string? defaultValue = "")
    {
        KeyValuesText dat = this;
        switch (dat.TypeOf(out int i, out float f))
        {
            case StockType.String: return dat.Value;
            case StockType.Int: return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case StockType.Float: return ((double)f).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            case StockType.UInt64: return ParseUInt64(dat.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            default: return defaultValue;
        }
    }

    private static ulong ParseUInt64(string v)
    {
        // RecursiveLoadFromBuffer's own digit loop, not a validating parse.
        ulong r = 0;
        for (int i = 2; i < 18; i++)
        {
            int c = v[i];
            if (c >= 'a') c -= 'a' - ('9' + 1);
            else if (c >= 'A') c -= 'A' - ('9' + 1);
            r = unchecked(r * 16 + (ulong)(c - '0'));
        }
        return r;
    }

    private static bool CSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    /// <summary>C's strtol, base 10: the value and where the scan stopped (0 when nothing converted).</summary>
    internal static long CStrtol(string s, out int end)
    {
        int i = 0;
        while (i < s.Length && CSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        int digits = i;
        decimal acc = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            if (acc <= long.MaxValue) acc = acc * 10 + (s[i] - '0');
            i++;
        }
        if (i == digits) { end = 0; return 0; }
        end = i;
        acc = neg ? -acc : acc;
        if (acc > long.MaxValue) return long.MaxValue;
        if (acc < long.MinValue) return long.MinValue;
        return (long)acc;
    }

    /// <summary>C's strtod for decimal text, inf and nan: the value and where the scan stopped.</summary>
    internal static double CStrtod(string s, out int end)
    {
        int i = 0;
        while (i < s.Length && CSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }

        string rest = s.Substring(i);
        if (rest.StartsWith("infinity", System.StringComparison.OrdinalIgnoreCase)) { end = i + 8; return neg ? double.NegativeInfinity : double.PositiveInfinity; }
        if (rest.StartsWith("inf", System.StringComparison.OrdinalIgnoreCase)) { end = i + 3; return neg ? double.NegativeInfinity : double.PositiveInfinity; }
        if (rest.StartsWith("nan", System.StringComparison.OrdinalIgnoreCase)) { end = i + 3; return double.NaN; }

        int mant = i;
        int nd = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; nd++; }
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; nd++; }
        }
        if (nd == 0) { end = 0; return 0; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int e = i + 1;
            if (e < s.Length && (s[e] == '+' || s[e] == '-')) e++;
            int ed = e;
            while (e < s.Length && s[e] >= '0' && s[e] <= '9') e++;
            if (e > ed) i = e;
        }
        end = i;
        double v = double.Parse(s.Substring(mant, i - mant), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture);
        return neg ? -v : v;
    }

    /// <summary>
    /// Parses a whole document. The result is a synthetic root whose CHILDREN
    /// are the document's top-level keys — plural on purpose, because a KV1
    /// document may hold several and decals_subrect.txt does.
    /// </summary>
    public static KeyValuesText Parse(string text)
    {
        var root = new KeyValuesText(string.Empty, string.Empty) { IsBlock = true };
        int i = 0;

        while (true)
        {
            SkipTrivia(text, ref i);
            if (i >= text.Length) break;

            if (text[i] == '}')
                throw new KeyValuesTextException($"unmatched '}}' at offset {i}");

            root._children.Add(ParseKey(text, ref i));
        }

        return root;
    }

    private static KeyValuesText ParseKey(string text, ref int i)
    {
        string name = ReadToken(text, ref i);

        SkipTrivia(text, ref i);

        if (i >= text.Length)
            throw new KeyValuesTextException($"key '{name}' has neither a value nor a block");

        if (text[i] == '{')
        {
            i++;
            var block = new KeyValuesText(name, string.Empty) { IsBlock = true };

            while (true)
            {
                SkipTrivia(text, ref i);

                if (i >= text.Length)
                    throw new KeyValuesTextException($"block '{name}' is never closed");

                if (text[i] == '}')
                {
                    i++;
                    return block;
                }

                block._children.Add(ParseKey(text, ref i));
            }
        }

        if (text[i] == '}')
            throw new KeyValuesTextException($"key '{name}' has no value");

        return new KeyValuesText(name, ReadToken(text, ref i));
    }

    private static string ReadToken(string text, ref int i)
    {
        if (text[i] == '"')
        {
            int start = ++i;

            while (i < text.Length && text[i] != '"')
            {
                // A quoted token may not span a line. Stock's tokeniser reads to
                // the next quote wherever it is, which turns one missing quote
                // into a token that swallows the rest of the file and a table
                // that is quietly two entries long. Refusing says so instead.
                if (text[i] == '\n')
                    throw new KeyValuesTextException($"unterminated quoted token at offset {start}");

                i++;
            }

            if (i >= text.Length)
                throw new KeyValuesTextException($"unterminated quoted token at offset {start}");

            string s = text[start..i];
            i++;
            return s;
        }

        int begin = i;

        while (i < text.Length && !char.IsWhiteSpace(text[i]) &&
               text[i] != '{' && text[i] != '}' && text[i] != '"')
        {
            i++;
        }

        if (i == begin)
            throw new KeyValuesTextException($"expected a token at offset {i}");

        return text[begin..i];
    }

    private static void SkipTrivia(string text, ref int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            // Only `//`. A single slash is a legal character in a model path
            // and treating it as a comment would truncate every one of them.
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;

                continue;
            }

            return;
        }
    }
}

/// <summary>What <see cref="KeyValuesText.Parse"/> throws. A malformed document
/// is an error rather than a short table.</summary>
public sealed class KeyValuesTextException : System.Exception
{
    public KeyValuesTextException(string message) : base(message) { }
}
