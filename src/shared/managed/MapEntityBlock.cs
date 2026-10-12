//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The map-entity-block parser: the tokeniser the BSP's entity lump is read
/// with, and the key/value cursor over one entity's block of it.
///
/// <para>
/// This is <c>game/shared/mapentities_shared.cpp</c>'s
/// <c>MapEntity_ParseToken</c>, <c>MapEntity_ExtractValue</c>,
/// <c>MapEntity_GetNumKeysInEntity</c> and <c>CEntityMapData</c>, ported. It is
/// the half of <c>TemplateEntities.cpp</c> that was never engine work — every
/// line of it is arithmetic over a byte buffer — and it is what stood between
/// the template store and C#.
/// </para>
///
/// <para>
/// <b>The buffer is bytes, not a string, and that is deliberate.</b> A template
/// is stored as the exact map text and handed back to the native parser to be
/// turned into an entity again; a round trip through UTF-16 would re-encode any
/// byte the map author put in a value, and the offsets <see cref="SetValue"/>
/// works in are byte offsets. So the cursor is an index into a
/// <see cref="byte"/> array, which is what the C++ pointer arithmetic was.
/// </para>
/// </summary>
public sealed class MapEntityBlock
{
    /// <summary><c>MAPKEY_MAXLENGTH</c>. A token longer than this is truncated,
    /// which is stock's behaviour and not a defect to fix here.</summary>
    public const int MaxKeyLength = 2048;

    /// <summary>
    /// The characters that are a token all by themselves. Stock's
    /// <c>s_BraceChars</c>, built into a 256-entry reverse map at first use;
    /// here it is a constant string, because a table lookup over five
    /// characters is not worth a cache and the cache was file-scope state.
    /// </summary>
    private const string BraceChars = "{}()'";

    /// <summary>The entity's text. Replaced wholesale by <see cref="SetValue"/>.</summary>
    private byte[] _data;

    /// <summary>
    /// How many bytes of storage the block may grow into.
    ///
    /// Stock's <c>m_nEntDataSize</c>, and stock's <c>-1</c> ("size unknown, so
    /// this may not be modified") is <c>null</c> here so that a caller cannot
    /// pass it by accident.
    /// </summary>
    private readonly int? _capacity;

    /// <summary>Stock's <c>m_pCurrentKey</c>, as an index rather than a pointer.</summary>
    private int _cursor;

    private readonly ILog _log;

    /// <param name="log">Where a malformed block is said (statics to zero).</param>
    public MapEntityBlock(byte[] entBlock, ILog log, int? capacity = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _data = entBlock ?? throw new ArgumentNullException(nameof(entBlock));
        _capacity = capacity;
        _cursor = 0;
    }

    /// <summary>The block's text as it stands now, up to its first NUL.</summary>
    public string Text => Encoding.UTF8.GetString(_data, 0, Terminator(_data));

    /// <summary>
    /// The block's bytes as they stand now, up to but not including the
    /// terminating NUL — what gets handed back to the native parser.
    /// </summary>
    public byte[] Bytes
    {
        get
        {
            int n = Terminator(_data);
            var copy = new byte[n];
            Array.Copy(_data, copy, n);
            return copy;
        }
    }

    /// <summary>Where the NUL is, or the end of storage if there is none.</summary>
    private static int Terminator(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0)
                return i;
        }

        return data.Length;
    }

    /// <summary>
    /// Reads one token starting at <paramref name="at"/>.
    ///
    /// <para>
    /// Stock returns a pointer to the character after the token and NULL at end
    /// of data; this returns the index after the token and <c>-1</c> for NULL,
    /// because 0 is a real position and would read as "the start of the buffer".
    /// </para>
    /// </summary>
    /// <returns>The index just past the token, or -1 at end of data.</returns>
    public static int ParseToken(byte[] data, int at, out string token)
    {
        token = string.Empty;

        if (data is null || at < 0)
            return -1;

        var sb = new StringBuilder();
        int len = 0;
        int c;

        // Skip whitespace, then a // comment, then whatever whitespace follows
        // it. Stock's `goto skipwhite`.
        while (true)
        {
            while (true)
            {
                if (at >= data.Length)
                    return -1;

                c = data[at];
                if (c == 0)
                    return -1;              // end of file
                if (c > ' ')
                    break;

                at++;
            }

            if (c == '/' && at + 1 < data.Length && data[at + 1] == '/')
            {
                while (at < data.Length && data[at] != 0 && data[at] != '\n')
                    at++;

                continue;
            }

            break;
        }

        // A quoted string runs to its closing quote and stops there, so this is
        // the only arm that can produce a token containing a space.
        if (c == '"')
        {
            at++;
            while (len < MaxKeyLength)
            {
                c = at < data.Length ? data[at] : 0;
                at++;

                if (c == '"' || c == 0)
                {
                    token = sb.ToString();
                    return at;
                }

                sb.Append((char)c);
                len++;
            }

            // A quoted token at the cap: stock drops its last character and then
            // FALLS THROUGH to the two tests below, with `c` still holding the
            // LAST CHARACTER IT READ rather than the quote it opened with — so
            // that character is examined as a brace and then appended a second
            // time by the regular-word loop. Reproduced rather than tidied:
            // this parser's answers are compared against native's byte for byte
            // and a tidier one would disagree on a pathological key.
            len--;
            sb.Length = len;
        }

        // The characters that are a whole token by themselves.
        if (BraceChars.IndexOf((char)c) >= 0)
        {
            token = ((char)c).ToString();
            return at + 1;
        }

        // A regular word: everything up to whitespace or a brace character.
        do
        {
            sb.Append((char)c);
            at++;
            len++;

            if (len >= MaxKeyLength)
            {
                len--;
                sb.Length = len;
            }

            c = at < data.Length ? data[at] : 0;
            if (BraceChars.IndexOf((char)c) >= 0)
                break;
        }
        while (c > 32);

        token = sb.ToString();
        return at;
    }

    /// <summary>
    /// <c>MapEntity_ExtractValue</c>: the first value for a key, by exact
    /// name — case-SENSITIVE, which is what stock's <c>strcmp</c> does, and
    /// deliberately NOT the case-insensitive rule the template store matches
    /// targetnames with.
    /// </summary>
    public static bool ExtractValue(byte[] data, string keyName, out string value)
    {
        value = string.Empty;
        int at = 0;

        while (at >= 0)
        {
            at = ParseToken(data, at, out string token);
            if (token.Length > 0 && token[0] == '}')
                break;

            if (token == keyName)
            {
                ParseToken(data, at, out value);
                return true;
            }

            if (at < 0)
                break;

            at = ParseToken(data, at, out _);       // skip over the value
        }

        return false;
    }

    /// <summary>
    /// <c>MapEntity_GetNumKeysInEntity</c>. What the template store sizes its
    /// spare room from: every key in a template may grow by the fixup suffix,
    /// so the stored copy is allocated with room for all of them to.
    /// </summary>
    public static int CountKeys(byte[] data)
    {
        int at = 0;
        int keys = 0;

        while (at >= 0)
        {
            at = ParseToken(data, at, out string token);
            if (token.Length > 0 && token[0] == '}')
                break;

            keys++;

            if (at < 0)
                break;

            at = ParseToken(data, at, out _);       // skip over the value
        }

        return keys;
    }

    /// <inheritdoc cref="ExtractValue(byte[], string, out string)"/>
    public bool ExtractValue(string keyName, out string value) =>
        ExtractValue(_data, keyName, out value);

    /// <summary>Rewinds the cursor and reads the first key. Stock's
    /// <c>GetFirstKey</c>.</summary>
    public bool GetFirstKey(out string keyName, out string value)
    {
        _cursor = 0;
        return GetNextKey(out keyName, out value);
    }

    /// <summary>
    /// Stock's <c>GetNextKey</c>: one key/value pair, advancing the cursor.
    ///
    /// <para>
    /// Returns false at the entity's closing brace, having stepped the cursor
    /// BACK to it — so a caller that keeps asking gets false for ever rather
    /// than walking off the end into whatever follows.
    /// </para>
    /// </summary>
    public bool GetNextKey(out string keyName, out string value)
    {
        keyName = string.Empty;
        value = string.Empty;

        int previous = _cursor;
        _cursor = ParseToken(_data, _cursor, out string token);

        if (token.Length > 0 && token[0] == '}')
        {
            _cursor = previous;                     // step back onto the brace
            return false;
        }

        if (_cursor < 0)
        {
            _log.Warning("[SourceSharp] map entity block: EOF without a closing brace\n");
            return false;
        }

        // Stock trims trailing spaces off a keyname, and only off a keyname.
        keyName = token.TrimEnd(' ');

        _cursor = ParseToken(_data, _cursor, out token);
        if (_cursor < 0)
        {
            _log.Warning("[SourceSharp] map entity block: EOF without a closing brace\n");
            return false;
        }

        if (token.Length > 0 && token[0] == '}')
        {
            _log.Warning("[SourceSharp] map entity block: closing brace without data\n");
            return false;
        }

        value = token;
        return true;
    }

    /// <summary>
    /// Stock's <c>SetValue</c>: replaces the <paramref name="keyInstance"/>'th
    /// value for a key, in place.
    ///
    /// <para>
    /// <b>The cursor adjustment is the subtle part and it is stock's.</b>
    /// <c>Templates_ReconnectIOForGroup</c> called this from INSIDE its own
    /// <c>GetNextKey</c> walk, so a replacement that changes the block's length
    /// would leave the walk mid-token. Stock moves the cursor by the length
    /// difference (<c>m_pCurrentKey += iPadding</c>); so does this.
    /// </para>
    ///
    /// <para>
    /// Refuses when the block was built without a capacity, which is stock's
    /// <c>m_nEntDataSize == -1</c> guard and for stock's reason: the buffer is
    /// someone else's and there is no room to grow into. Refuses too when the
    /// replacement would not fit, which stock did not check and could not —
    /// it wrote past the end and relied on the caller having reserved the room.
    /// </para>
    /// </summary>
    public bool SetValue(string keyName, string newValue, int keyInstance = 0)
    {
        if (_capacity is not int capacity)
            return false;

        int at = 0;
        int seen = 0;

        while (at >= 0)
        {
            at = ParseToken(_data, at, out string token);
            if (token.Length > 0 && token[0] == '}')
                break;

            if (token == keyName)
            {
                seen++;
                if (seen > keyInstance)
                {
                    // `prevData` in stock: where the key's token scan stopped,
                    // which is the space separating the key from its value.
                    int prevData = at;
                    int valueEnd = ParseToken(_data, at, out string oldValue);
                    if (valueEnd < 0)
                        return false;

                    // Insert quotes if the caller didn't.
                    string quoted = newValue.StartsWith('"')
                        ? newValue
                        : "\"" + newValue + "\"";

                    byte[] replacement = Encoding.UTF8.GetBytes(quoted);

                    // -2 for the quotes: the old token does not carry them and
                    // the new text does.
                    int padding = replacement.Length
                                  - Encoding.UTF8.GetByteCount(oldValue) - 2;

                    int writeAt = prevData + 1;     // step past that space
                    int tail = Terminator(_data);
                    int needed = writeAt + replacement.Length + (tail - valueEnd) + 1;

                    if (needed > capacity)
                        return false;

                    var rebuilt = new byte[Math.Max(_data.Length, needed)];
                    Array.Copy(_data, rebuilt, writeAt);
                    Array.Copy(replacement, 0, rebuilt, writeAt, replacement.Length);
                    Array.Copy(_data, valueEnd, rebuilt, writeAt + replacement.Length,
                               tail - valueEnd);
                    rebuilt[writeAt + replacement.Length + (tail - valueEnd)] = 0;
                    _data = rebuilt;

                    _cursor += padding;
                    return true;
                }
            }

            if (at < 0)
                break;

            at = ParseToken(_data, at, out _);      // skip over the value
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // THE LUMP, AND NOT ONE ENTITY OF IT -- plan_entity.md step 2d.
    //
    // Everything above this line reads ONE entity's block. A map load has to
    // find where each block starts and ends first, and until step 2d the only
    // thing that could was the native loop in MapEntity_ParseAllEntities: it
    // held the cursor, so every key in the map had to be handed UP one at a
    // time (5709 crossings on dm_lockdown, 72% of the map-load entity budget).
    //
    // These two are the rest of that loop's arithmetic, and they are the same
    // arithmetic: MapEntity_SkipToNextEntity counts braces, and the split is
    // ParseToken expecting a `{`.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>MapEntity_SkipToNextEntity</c>: from just inside an entity's opening
    /// brace, the index just past its matching closing brace.
    /// </summary>
    /// <remarks>
    /// Nested braces are counted, which is stock's — a value may contain one,
    /// and the parser makes `{` a token all by itself even in the middle of a
    /// word (see <see cref="BraceChars"/>). Answers -1 at end of data, which is
    /// stock's NULL, and for the same reason <see cref="ParseToken"/> does: 0 is
    /// a real position.
    /// </remarks>
    public static int SkipToNextEntity(byte[] data, int at)
    {
        if (data is null || at < 0)
            return -1;

        int openBraceCount = 1;

        while (at >= 0)
        {
            at = ParseToken(data, at, out string token);

            if (token == "{")
                openBraceCount++;
            else if (token == "}" && --openBraceCount == 0)
                return at;
        }

        return -1;
    }

    /// <summary>One entity's block in a lump: where its keys start, and where
    /// the block ends.</summary>
    /// <param name="KeysAt">
    /// The index just past the opening brace — where <see cref="GetFirstKey"/>
    /// would start, and what a <see cref="MapEntityBlock"/> over
    /// <see cref="Bytes"/> is built from.
    /// </param>
    /// <param name="End">The index just past the closing brace.</param>
    /// <param name="Bytes">
    /// The block's own text: from just past the opening brace through the
    /// closing brace, NUL-terminated.
    ///
    /// <para>
    /// <b>The opening brace is NOT in it, and that is stock's shape rather than
    /// a choice.</b> <c>MapEntity_ParseAllEntities</c> consumes the <c>{</c>
    /// before it records <c>pCurMapData</c>, and
    /// <c>MapEntity_RecordPendingNonBaseMapData</c> stores from there through
    /// the <c>}</c> — which is what a template holds and what
    /// <c>MapEntity_ParseEntity</c> is handed when a template is instanced. A
    /// copy that carried the brace would re-parse as an entity whose first key
    /// is <c>{</c>.
    /// </para>
    /// </param>
    public readonly record struct Block(int KeysAt, int End, byte[] Bytes);

    /// <summary>
    /// Splits an entity lump into its blocks, in map order.
    ///
    /// <para>
    /// This is the loop at the top of <c>MapEntity_ParseAllEntities</c>
    /// (game/server/mapentities.cpp:609-621) with the creation taken out of it:
    /// parse the opening brace, remember where the block began, skip to the
    /// matching close, and go round again. A token that is not <c>{</c> where
    /// one is expected ends the walk — stock calls <c>Error()</c> there and then
    /// <c>continue</c>s, which cannot both happen; ending is the half that is
    /// reachable.
    /// </para>
    /// </summary>
    public static List<Block> SplitBlocks(byte[] lump)
    {
        var blocks = new List<Block>();

        if (lump is null)
            return blocks;

        int at = 0;

        while (true)
        {
            at = ParseToken(lump, at, out string token);

            // End of data. Stock's `if (!pMapData) break;`.
            if (at < 0)
                break;

            if (token != "{")
                break;

            int keysAt = at;
            int end = SkipToNextEntity(lump, at);

            // AN UNTERMINATED FINAL BLOCK IS STILL A BLOCK, and that is stock's
            // answer rather than a lenient reading of it.
            // MapEntity_ParseAllEntities reads the classname and creates the
            // entity BEFORE MapEntity_SkipToNextEntity is asked for the next
            // one, so a `{` that never closes produces an entity built from
            // whatever text is left and then ends the walk. Pinned by the
            // `unterminated-final` row of map_entity_block_cases.txt, which is
            // recomputed by the real parser.
            bool truncated = end < 0;

            if (truncated)
                end = Terminator(lump);

            var bytes = new byte[end - keysAt + 1];
            Array.Copy(lump, keysAt, bytes, 0, end - keysAt);
            bytes[end - keysAt] = 0;

            blocks.Add(new Block(keysAt, end, bytes));

            if (truncated)
                break;

            at = end;
        }

        return blocks;
    }
}
