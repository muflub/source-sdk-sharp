//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>The type a KeyValues load gives a key: tier1's <c>KeyValues::types_t</c> values.</summary>
public enum KeyValuesType : byte
{
    /// <summary>A block, or a key whose value was never read.</summary>
    None = 0,
    String = 1,
    Int = 2,
    Float = 3,
    UInt64 = 7,
}

/// <summary>
/// One key of a loaded KeyValues tree, as tier1 holds it: a name, a typed
/// value, the first child (<c>m_pSub</c>) and the next peer (<c>m_pPeer</c>).
/// Names and strings are the file's BYTES, as the C++ holds them: a load
/// never decodes text, and a byte a decoder would refuse is still a byte the
/// material system is handed.
/// </summary>
public sealed class KeyValuesNode
{
    internal KeyValuesNode(byte[] name) { Name = name; }

    public byte[] Name { get; internal set; }
    public KeyValuesType Type { get; internal set; }
    public byte[] StringValue { get; internal set; } = [];
    public int IntValue { get; internal set; }
    public float FloatValue { get; internal set; }
    public ulong UInt64Value { get; internal set; }
    public KeyValuesNode? FirstChild { get; internal set; }
    public KeyValuesNode? Next { get; internal set; }

    /// <summary>The children, in order.</summary>
    public IEnumerable<KeyValuesNode> Children
    {
        get
        {
            for (KeyValuesNode? c = FirstChild; c != null; c = c.Next)
                yield return c;
        }
    }

    /// <summary>The name as text (Latin-1, so every byte survives the round trip).</summary>
    public string NameText => System.Text.Encoding.Latin1.GetString(Name);

    internal KeyValuesNode CopyWithChildren()
    {
        var copy = new KeyValuesNode(Name)
        {
            Type = Type, StringValue = StringValue, IntValue = IntValue,
            FloatValue = FloatValue, UInt64Value = UInt64Value,
        };
        KeyValuesNode? last = null;
        foreach (KeyValuesNode child in Children)
        {
            KeyValuesNode c = child.CopyWithChildren();
            if (last == null) copy.FirstChild = c; else last.Next = c;
            last = c;
        }
        return copy;
    }
}

/// <summary>
/// tier1's KeyValues TEXT LOAD in C# (plan_client_novalve C9, KeyValues K4):
/// <c>LoadFromFile</c>, <c>LoadFromBuffer</c>, <c>RecursiveLoadFromBuffer</c>
/// and <c>ReadToken</c>, over a port of the four <c>CUtlBuffer</c> text
/// operations they read through, held to pristine KeyValues.cpp by the
/// keyvalues_load_oracle_test golden (KeyValuesLoadTests).
///
/// <para>
/// <b>What a load decides, and so what this keeps exactly.</b> The type each
/// value is given (<c>strtol</c> and <c>strtod</c> over the token: an int, a
/// float, a "0x" + 16-digit uint64, else a string; on LP64 the int overflow
/// test can never fire, so an over-long integer loads as its truncated int),
/// conditionals (<c>[$WIN32]</c> and the rest) on values, blocks and roots,
/// <c>//</c> comments, the 4095-byte token cap, and each error exit, which
/// stops the block it is in and keeps the key already made. Escape sequences
/// are off, as every LoadFromFile in this build has them; inside quotes a DEL
/// byte ends the string, as NoEsc's conversion makes it.
/// </para>
///
/// <para>
/// <b>Includes.</b> <c>#include</c> appends a file's roots after the last
/// root; <c>#base</c> merges a file's first root into the first root, a
/// matching child recursively and any other appended as a copy. Both are
/// read through the reader with no path ID, as stock's LoadFromFile hands
/// its own LoadFromBuffer none; with no reader they are read and ignored, as
/// stock ignores them with no filesystem. Stock frees all but the first
/// included file while they are still on the chain; here they stay.
/// </para>
///
/// <para>
/// <b>One divergence.</b> A file that starts with a UTF-16 byte order mark
/// is converted by stock as if it were 32-bit wchar_t, which is not what the
/// file holds on this platform; it loads here as an empty document, said.
/// </para>
/// </summary>
public sealed class KeyValuesLoad(ILog log, Func<string, string?, byte[]?>? readFile, Func<bool> isSteamDeck)
{
    private readonly ILog _log = log;
    private readonly Func<string, string?, byte[]?>? _readFile = readFile;
    private readonly Func<bool> _isSteamDeck = isSteamDeck;

    private const int TokenSize = 4096;
    private const int MaxErrorDepth = 100;

    /// <summary>
    /// KeyValues(rootName)->LoadFromFile(fs, resourceName, pathId): null when
    /// the file cannot be read, else the first root (its peers follow it).
    /// </summary>
    public KeyValuesNode? LoadFromFile(string rootName, string resourceName, string? pathId)
    {
        if (_readFile == null) return null;
        byte[]? bytes = _readFile(resourceName, pathId);
        if (bytes == null) return null;
        return LoadFromBuffer(rootName, resourceName, bytes);
    }

    /// <summary>KeyValues(rootName)->LoadFromBuffer(resourceName, text, fs): the first root.</summary>
    public KeyValuesNode LoadFromBuffer(string rootName, string resourceName, ReadOnlySpan<byte> text)
    {
        var root = new KeyValuesNode(Latin1(rootName));
        int len = text.IndexOf((byte)0);
        if (len < 0) len = text.Length;
        if (len > 2 && text[0] == 0xFF && text[1] == 0xFE)
        {
            _log.Warning($"[SourceSharp] KeyValues: {resourceName} is UTF-16, which this loader does not read\n");
            return root;
        }
        new Parse(this, resourceName, text[..len].ToArray()).Document(root);
        return root;
    }

    private static byte[] Latin1(string s) => System.Text.Encoding.Latin1.GetBytes(s);

    // The CUtlBuffer text reads ReadToken and the load use, over a read-only
    // buffer of the text's strlen: Get operations past the end set the
    // overflow (the buffer is then not valid); peeks never do; a seek sets or
    // CLEARS it by where it lands.
    private sealed class TextBuffer(byte[] data)
    {
        private readonly byte[] _d = data;
        public int Get;
        public bool Error;

        public bool IsValid => !Error;
        public int TellGet => Get;

        private bool CheckGet(int n)
        {
            if (Error) return false;
            if (_d.Length < Get + n) { Error = true; return false; }
            return true;
        }

        public bool CheckPeekGet(int offset, int n) => !Error && _d.Length >= Get + offset + n;

        public byte Peek(int offset) => _d[Get + offset];

        public static bool IsSpace(byte c) => c == ' ' || (c >= 9 && c <= 13);

        public void SeekHead(int at) { Get = at; Error = Get > _d.Length; }

        public void SeekCurrent(int by) => SeekHead(Get + by);

        // VaScanf's "%c": nothing on an error already set; the end sets it.
        public byte GetChar()
        {
            if (Error) return 0;
            if (Get >= _d.Length) { Error = true; return 0; }
            return _d[Get++];
        }

        public void EatWhiteSpace()
        {
            if (Error) return;
            while (CheckGet(1))
            {
                if (!IsSpace(_d[Get])) break;
                Get++;
            }
        }

        public bool EatCppComment()
        {
            if (Error) return false;
            if (!CheckPeekGet(0, 2) || _d[Get] != '/' || _d[Get + 1] != '/') return false;
            Get += 2;
            for (byte c = GetChar(); IsValid; c = GetChar())
            {
                if (c == '\n') break;
            }
            return true;
        }

        private bool PeekQuote() => CheckPeekGet(0, 1) && _d[Get] == '"';

        // GetDelimitedString with NoEsc's conversion (escape byte 0x7F, which
        // converts to nothing and so reads as a NUL), into a C string of at
        // most max - 1 bytes. Called only with a quote next.
        public byte[] GetDelimitedString(int max)
        {
            if (!IsValid) return [];
            EatWhiteSpace();
            if (!PeekQuote()) return [];
            SeekCurrent(1);
            var buf = new List<byte>();
            int nRead = 0;
            while (IsValid)
            {
                if (PeekQuote()) { SeekCurrent(1); break; }
                byte c = GetChar();
                if (c == 0x7F) c = 0;
                if (nRead < max) { buf.Add(c); nRead++; }
            }
            if (nRead >= max) nRead = max - 1;
            return CString(buf, nRead);
        }

        public static byte[] CString(List<byte> buf, int count)
        {
            int n = 0;
            while (n < count && buf[n] != 0) n++;
            return buf.GetRange(0, n).ToArray();
        }
    }

    private sealed class Parse(KeyValuesLoad owner, string resourceName, byte[] text)
    {
        private readonly TextBuffer _buf = new(text);
        private bool _wasQuoted;
        private bool _wasConditional;

        private void Error(string what) => owner._log.Warning($"KeyValues Error: {what} in file {resourceName}\n");

        // KeyValues::ReadToken.
        private byte[]? ReadToken()
        {
            _wasQuoted = false;
            _wasConditional = false;
            if (!_buf.IsValid) return null;
            while (true)
            {
                _buf.EatWhiteSpace();
                if (!_buf.IsValid) return null;
                if (!_buf.EatCppComment()) break;
            }
            if (!_buf.CheckPeekGet(0, 1)) return null;
            byte c = _buf.Peek(0);
            if (c == '"')
            {
                _wasQuoted = true;
                return _buf.GetDelimitedString(TokenSize);
            }
            if (c == '{' || c == '}')
            {
                _buf.SeekCurrent(1);
                return [c];
            }

            bool reported = false, conditionalStart = false;
            var token = new List<byte>();
            while (_buf.CheckPeekGet(0, 1))
            {
                c = _buf.Peek(0);
                if (c == 0) break;
                if (c == '"' || c == '{' || c == '}') break;
                if (c == '[') conditionalStart = true;
                if (c == ']' && conditionalStart) _wasConditional = true;
                if (TextBuffer.IsSpace(c)) break;
                if (token.Count < TokenSize - 1) token.Add(c);
                else if (!reported) { reported = true; Error(" ReadToken overflow"); }
                _buf.SeekCurrent(1);
            }
            return token.ToArray();
        }

        private static bool Is(byte[]? s, char c) => s != null && s.Length > 0 && s[0] == c;

        // KeyValues::LoadFromBuffer's loop, on the root it was called on.
        public void Document(KeyValuesNode self)
        {
            KeyValuesNode? previous = null;
            KeyValuesNode? current = self;
            var included = new List<KeyValuesNode>();
            var bases = new List<KeyValuesNode>();
            do
            {
                bool accepted = true;
                byte[]? s = ReadToken();
                if (!_buf.IsValid || s == null || s.Length == 0) break;

                if (Ascii.EqualsIgnoreCase(s, "#include"u8) || Ascii.EqualsIgnoreCase(s, "#base"u8))
                {
                    bool isBase = s.Length == 5;
                    s = ReadToken();
                    if (s == null || s.Length == 0) Error(isBase ? "#base is NULL " : "#include is NULL ");
                    else Include(s, isBase ? bases : included);
                    continue;
                }

                if (current == null)
                {
                    current = new KeyValuesNode(s);
                    if (previous != null) previous.Next = current;
                }
                else
                {
                    current.Name = s;
                }

                s = ReadToken();
                if (_wasConditional)
                {
                    accepted = owner.Evaluate(s);
                    s = ReadToken();
                }

                if (Is(s, '{') && !_wasQuoted) Block(current, 0);
                else Error("LoadFromBuffer: missing {");

                if (!accepted)
                {
                    if (previous != null) previous.Next = null;
                    current.FirstChild = null;
                    current.Type = KeyValuesType.None;
                }
                else
                {
                    previous = current;
                    current = null;
                }
            } while (_buf.IsValid);

            KeyValuesNode spot = self;
            foreach (KeyValuesNode kv in included)
            {
                while (spot.Next != null) spot = spot.Next;
                spot.Next = kv;
            }
            foreach (KeyValuesNode kv in bases)
                Merge(self, kv);
        }

        // KeyValues::ParseIncludedKeys: the file beside this one, loaded with
        // no path ID; nothing without a reader or a file.
        private void Include(byte[] file, List<KeyValuesNode> into)
        {
            if (owner._readFile == null) return;
            string dir = resourceName;
            int cut = dir.Length;
            while (cut > 0 && dir[cut - 1] != '/' && dir[cut - 1] != '\\') cut--;
            string full = dir[..cut] + System.Text.Encoding.Latin1.GetString(file);
            if (full.Length > 511) full = full[..511];
            KeyValuesNode? kv = owner.LoadFromFile(full, full, null);
            if (kv != null) into.Add(kv);
        }

        // KeyValues::RecursiveMergeKeyValues: names match as the symbol table
        // matches them, case-insensitively.
        private static void Merge(KeyValuesNode self, KeyValuesNode baseKv)
        {
            for (KeyValuesNode? baseChild = baseKv.FirstChild; baseChild != null; baseChild = baseChild.Next)
            {
                bool found = false;
                KeyValuesNode? last = null;
                for (KeyValuesNode? mine = self.FirstChild; mine != null; mine = mine.Next)
                {
                    last = mine;
                    if (Ascii.EqualsIgnoreCase(mine.Name, baseChild.Name))
                    {
                        Merge(mine, baseChild);
                        found = true;
                        break;
                    }
                }
                if (found) continue;
                while (last?.Next != null) last = last.Next;
                KeyValuesNode copy = baseChild.CopyWithChildren();
                if (last == null) self.FirstChild = copy; else last.Next = copy;
            }
        }

        // KeyValues::RecursiveLoadFromBuffer. errorLevel is the error stack's
        // depth on entry (two a block level: the block and its current key);
        // past 100 the block is not read.
        private void Block(KeyValuesNode self, int errorLevel)
        {
            if (errorLevel > MaxErrorDepth)
            {
                Error("RecursiveLoadFromBuffer:  recursion overflow");
                return;
            }
            KeyValuesNode? lastChild = self.FirstChild;
            while (lastChild?.Next != null) lastChild = lastChild.Next;

            while (true)
            {
                bool accepted = true;
                byte[]? name = ReadToken();
                if (name == null) { Error("RecursiveLoadFromBuffer:  got EOF instead of keyname"); break; }
                if (name.Length == 0) { Error("RecursiveLoadFromBuffer:  got empty keyname"); break; }
                if (name[0] == '}' && !_wasQuoted) break;

                var dat = new KeyValuesNode(name);
                if (lastChild == null) self.FirstChild = dat; else lastChild.Next = dat;

                byte[]? value = ReadToken();
                if (_wasConditional && value != null)
                {
                    accepted = owner.Evaluate(value);
                    value = ReadToken();
                }
                if (value == null) { Error("RecursiveLoadFromBuffer:  got NULL key"); break; }
                if (Is(value, '}') && !_wasQuoted) { Error("RecursiveLoadFromBuffer:  got } in key"); break; }

                if (Is(value, '{') && !_wasQuoted)
                {
                    Block(dat, errorLevel + 2);
                }
                else
                {
                    if (_wasConditional) { Error("RecursiveLoadFromBuffer:  got conditional between key and value"); break; }
                    SetValue(dat, value);

                    // Look ahead one token for a conditional tag.
                    int prev = _buf.TellGet;
                    byte[]? peek = ReadToken();
                    if (_wasConditional) accepted = owner.Evaluate(peek);
                    else _buf.SeekHead(prev);
                }

                if (accepted)
                {
                    lastChild = dat;
                }
                else
                {
                    if (lastChild == null) self.FirstChild = null; else lastChild.Next = null;
                }
            }
        }
    }

    // RecursiveLoadFromBuffer's typing of a value.
    private static void SetValue(KeyValuesNode dat, byte[] value)
    {
        int len = value.Length;
        long l = CNumbers.Strtol(value, out int iEnd);
        float f = CNumbers.Strtod(value, out int fEnd);
        if (len > 1 && (value[1] | 0x20) == 'x')
        {
            f = 0f;
            fEnd = 0;
        }

        if (len == 0)
        {
            dat.Type = KeyValuesType.String;
            dat.StringValue = [];
        }
        else if (len == 18 && value[0] == '0' && value[1] == 'x')
        {
            ulong v = 0;
            for (int i = 2; i < 18; i++)
            {
                sbyte digit = (sbyte)value[i];
                if (digit >= 'a') digit = (sbyte)(digit - ('a' - ('9' + 1)));
                else if (digit >= 'A') digit = (sbyte)(digit - ('A' - ('9' + 1)));
                v = unchecked(v * 16 + (ulong)(long)(digit - '0'));
            }
            dat.Type = KeyValuesType.UInt64;
            dat.UInt64Value = v;
        }
        else if (fEnd > iEnd && fEnd == len)
        {
            dat.Type = KeyValuesType.Float;
            dat.FloatValue = f;
        }
        else if (iEnd == len)
        {
            dat.Type = KeyValuesType.Int;
            dat.IntValue = unchecked((int)l);
        }
        else
        {
            dat.Type = KeyValuesType.String;
            dat.StringValue = value;
        }
    }

    // EvaluateConditional: this build is a PC, Linux and POSIX; never an
    // X360, Windows or OS X; a Steam Deck as the injected answer says.
    private bool Evaluate(byte[]? str)
    {
        if (str == null) return false;
        ReadOnlySpan<byte> s = str;
        if (s.Length > 0 && s[0] == '[') s = s[1..];
        bool not = s.Length > 0 && s[0] == '!';
        if (Contains(s, "$DECK"u8)) return _isSteamDeck() ^ not;
        if (Contains(s, "$X360"u8)) return false ^ not;
        if (Contains(s, "$WIN32"u8)) return true ^ not;
        if (Contains(s, "$WINDOWS"u8)) return false ^ not;
        if (Contains(s, "$OSX"u8)) return false ^ not;
        if (Contains(s, "$LINUX"u8)) return true ^ not;
        if (Contains(s, "$POSIX"u8)) return true ^ not;
        return false;
    }

    private static bool Contains(ReadOnlySpan<byte> hay, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            if (Ascii.EqualsIgnoreCase(hay.Slice(i, needle.Length), needle))
                return true;
        }
        return false;
    }

    private static class Ascii
    {
        public static bool EqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (Lower(a[i]) != Lower(b[i])) return false;
            }
            return true;
        }

        private static byte Lower(byte c) => c >= 'A' && c <= 'Z' ? (byte)(c + 32) : c;
    }
}

/// <summary>glibc's strtol (base 10) and strtod over a C string's bytes, with where each scan stopped.</summary>
internal static class CNumbers
{
    private static bool Space(byte c) => c == ' ' || (c >= 9 && c <= 13);

    private static bool Digit(byte c) => c >= '0' && c <= '9';

    private static int HexValue(byte c) =>
        c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    private static bool StartsIgnoreCase(byte[] s, int at, string word)
    {
        if (at + word.Length > s.Length) return false;
        for (int i = 0; i < word.Length; i++)
        {
            byte c = s[at + i];
            if (c >= 'A' && c <= 'Z') c = (byte)(c + 32);
            if (c != word[i]) return false;
        }
        return true;
    }

    /// <summary>strtol(s, &amp;end, 10): saturating at the long range; end 0 when nothing was read.</summary>
    public static long Strtol(byte[] s, out int end)
    {
        int i = 0;
        while (i < s.Length && Space(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        int first = i;
        System.Numerics.BigInteger acc = 0;
        while (i < s.Length && Digit(s[i])) { acc = acc * 10 + (s[i] - '0'); i++; }
        if (i == first) { end = 0; return 0; }
        end = i;
        if (neg) acc = -acc;
        if (acc > long.MaxValue) return long.MaxValue;
        if (acc < long.MinValue) return long.MinValue;
        return (long)acc;
    }

    /// <summary>(float)strtod(s, &amp;end): decimal, hexadecimal, inf/infinity and nan[(...)]; end 0 when nothing was read.</summary>
    public static float Strtod(byte[] s, out int end)
    {
        int i = 0;
        while (i < s.Length && Space(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }

        if (StartsIgnoreCase(s, i, "inf"))
        {
            end = StartsIgnoreCase(s, i, "infinity") ? i + 8 : i + 3;
            return neg ? float.NegativeInfinity : float.PositiveInfinity;
        }
        if (StartsIgnoreCase(s, i, "nan"))
        {
            end = i + 3;
            if (end < s.Length && s[end] == '(')
            {
                int j = end + 1;
                while (j < s.Length && (Digit(s[j]) || (s[j] | 0x20) >= 'a' && (s[j] | 0x20) <= 'z' || s[j] == '_')) j++;
                if (j < s.Length && s[j] == ')') end = j + 1;
            }
            return BitConverter.Int32BitsToSingle(neg ? unchecked((int)0xFFC00000) : 0x7FC00000);
        }

        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] | 0x20) == 'x')
        {
            int h = i + 2;
            double mant = 0;
            int nd = 0, exp = 0;
            while (h < s.Length && HexValue(s[h]) >= 0) { mant = mant * 16 + HexValue(s[h]); h++; nd++; }
            if (h < s.Length && s[h] == '.')
            {
                h++;
                while (h < s.Length && HexValue(s[h]) >= 0) { mant = mant * 16 + HexValue(s[h]); exp -= 4; h++; nd++; }
            }
            if (nd > 0)
            {
                if (h < s.Length && (s[h] | 0x20) == 'p')
                {
                    int e = h + 1;
                    bool eneg = false;
                    if (e < s.Length && (s[e] == '+' || s[e] == '-')) { eneg = s[e] == '-'; e++; }
                    int ed = e, ev = 0;
                    while (e < s.Length && Digit(s[e])) { ev = Math.Min(ev * 10 + (s[e] - '0'), 100000); e++; }
                    if (e > ed) { exp += eneg ? -ev : ev; h = e; }
                }
                end = h;
                double v = mant * Math.Pow(2, exp);
                return (float)(neg ? -v : v);
            }
            // "0x" with no hex digit reads as the 0 before it.
            end = i + 1;
            return neg ? -0f : 0f;
        }

        int mantStart = i, digits = 0;
        while (i < s.Length && Digit(s[i])) { i++; digits++; }
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && Digit(s[i])) { i++; digits++; }
        }
        if (digits == 0) { end = 0; return 0f; }
        if (i < s.Length && (s[i] | 0x20) == 'e')
        {
            int e = i + 1;
            if (e < s.Length && (s[e] == '+' || s[e] == '-')) e++;
            int ed = e;
            while (e < s.Length && Digit(s[e])) e++;
            if (e > ed) i = e;
        }
        end = i;
        string text = System.Text.Encoding.ASCII.GetString(s, mantStart, i - mantStart);
        double d = double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        return (float)(neg ? -d : d);
    }
}
