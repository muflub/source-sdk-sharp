//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Runtime.InteropServices;

namespace SourceSharp.Server.Interop;

//=============================================================================
// THE SAVE BLOCK CODEC, the server's, in C# (plan_server_native_v2 T4c; the
// user's 2026-10-11 ruling: a managed save/restore codec).
//
// What shell_savegame.h was for server.so: the record layout of a field block
// and a blob block over a byte cursor and the save's own token table. The
// layout is stock CSave's (a two-byte width, a two-byte token, the bytes), so a
// .sav written by the native codec reads here and the other way round. The
// client still builds the native header for its own decal and music blocks;
// docs/managed-saverestore.md is the argument for each field type, keyed by
// section.
//
// Everything here is pure: a cursor over bytes, names as NUL-terminated native
// strings, and a token table. SaveGameSession is the part that knows where
// those live in the engine's CSaveRestoreData.
//=============================================================================

/// <summary>
/// What a datamap field type is to the codec. The numbers are datamap.h's
/// <c>fieldtype_t</c>; <see cref="SaveFieldCodec.KindOf"/> maps them, and
/// everything it does not name is a refusal, never a guess.
/// </summary>
public enum SaveFieldKind
{
    Unsupported = 0,
    Integer,
    CharArray,
    Time,
    Short,
    Vector,
    PositionVector,
}

/// <summary>
/// The six field types this build carries, and their widths, times and
/// positions (stock CSave/CRestore's WriteTime/ReadTime and the position
/// vector's landmark).
/// </summary>
public static class SaveFieldCodec
{
    /// <summary>datamap.h FIELD_VECTOR.</summary>
    public const int FieldVector = 3;

    /// <summary>datamap.h FIELD_INTEGER.</summary>
    public const int FieldInteger = 5;

    /// <summary>datamap.h FIELD_SHORT.</summary>
    public const int FieldShort = 7;

    /// <summary>datamap.h FIELD_CHARACTER.</summary>
    public const int FieldCharacter = 8;

    /// <summary>datamap.h FIELD_POSITION_VECTOR.</summary>
    public const int FieldPositionVector = 15;

    /// <summary>datamap.h FIELD_TIME.</summary>
    public const int FieldTime = 16;

    /// <summary>Stock's ZERO_TIME: a time of exactly 0, which must not rebase.</summary>
    public const float ZeroTime = float.MaxValue * -0.5f;

    /// <summary>Stock's INVALID_TIME: carried through unchanged both ways.</summary>
    public const float InvalidTime = float.MaxValue * -1.0f;

    /// <summary>The codec's kind for a datamap field type.</summary>
    public static SaveFieldKind KindOf(int fieldType) => fieldType switch
    {
        FieldInteger => SaveFieldKind.Integer,
        FieldCharacter => SaveFieldKind.CharArray,
        FieldTime => SaveFieldKind.Time,
        FieldShort => SaveFieldKind.Short,
        FieldVector => SaveFieldKind.Vector,
        FieldPositionVector => SaveFieldKind.PositionVector,
        _ => SaveFieldKind.Unsupported,
    };

    /// <summary>
    /// The bytes one field occupies in a record, or -1 when this build cannot
    /// carry it: an unknown type, a count below one, or an ARRAY of a scalar
    /// type (whose element layout would be a guess).
    /// </summary>
    public static int Bytes(int fieldType, int fieldSize)
    {
        if (fieldSize < 1)
            return -1;

        return KindOf(fieldType) switch
        {
            SaveFieldKind.Integer or SaveFieldKind.Time => fieldSize == 1 ? 4 : -1,
            SaveFieldKind.Short => fieldSize == 1 ? 2 : -1,
            SaveFieldKind.Vector or SaveFieldKind.PositionVector => fieldSize == 1 ? 12 : -1,
            SaveFieldKind.CharArray => fieldSize,
            _ => -1,
        };
    }

    /// <summary>Stock's time transform on the way out: a delta from the base time.</summary>
    public static float EncodeTime(float value, float baseTime)
    {
        if (value == 0.0f)
            return ZeroTime;

        if (value == InvalidTime || value == float.MaxValue)
            return value;

        float delta = value - baseTime;

        // A real time AT the base must never store as 0: it would read back
        // as "no time", which is the zero sentinel's job.
        if (delta < 0.001f && delta > -0.001f)
            delta = 0.001f;

        return delta;
    }

    /// <summary>And on the way in: rebased onto the time the save is loaded at.</summary>
    public static float DecodeTime(float stored, float baseTime)
    {
        if (stored == ZeroTime)
            return 0.0f;

        if (stored == InvalidTime || stored == float.MaxValue)
            return stored;

        return stored + baseTime;
    }

    /// <summary>vec3_invalid: all three components FLT_MAX, never one.</summary>
    public static bool IsInvalidPosition(float x, float y, float z)
        => x == float.MaxValue && y == float.MaxValue && z == float.MaxValue;

    /// <summary>A position relative to the landmark, unless it is the invalid one.</summary>
    public static (float X, float Y, float Z) EncodePosition(float x, float y, float z,
                                                             float landmarkX, float landmarkY, float landmarkZ)
        => IsInvalidPosition(x, y, z) ? (x, y, z) : (x - landmarkX, y - landmarkY, z - landmarkZ);

    /// <summary>A position rebased onto the landmark it is loaded with.</summary>
    public static (float X, float Y, float Z) DecodePosition(float x, float y, float z,
                                                             float landmarkX, float landmarkY, float landmarkZ)
        => IsInvalidPosition(x, y, z) ? (x, y, z) : (x + landmarkX, y + landmarkY, z + landmarkZ);

    /// <summary>
    /// A block's encoded field bytes, or -1 when any one field cannot be
    /// carried: one unknown field refuses the sum rather than shortening it.
    /// </summary>
    public static int BlockBytes(ReadOnlySpan<SaveFieldDesc> fields)
    {
        int total = 0;
        foreach (SaveFieldDesc field in fields)
        {
            int bytes = Bytes(field.Type, field.Size);
            if (bytes < 0)
                return -1;
            total += bytes;
        }
        return total;
    }
}

/// <summary>
/// One field of a block: its name (a NUL-terminated native string, which goes
/// into the save's token table as a pointer, so it must outlive the save), its
/// datamap type and count, and its byte offset in the struct.
/// </summary>
public readonly unsafe struct SaveFieldDesc(byte* name, int type, int size, int offset)
{
    /// <summary>The field's name; never null in a block (SaveGameSession names an unnamed one).</summary>
    public byte* Name { get; } = name;

    /// <summary>datamap.h's fieldtype_t.</summary>
    public int Type { get; } = type;

    /// <summary>The element count (fieldSize).</summary>
    public int Size { get; } = size;

    /// <summary>The byte offset into the struct (fieldOffset[TD_OFFSET_NORMAL]).</summary>
    public int Offset { get; } = offset;
}

/// <summary>
/// The save's symbol table, as the codec uses it: a name to a two-byte token
/// on the way out, a token back to a name on the way in.
/// </summary>
public unsafe interface ISaveSymbols
{
    /// <summary>The name's token, interning the POINTER on first sight.</summary>
    ushort FindCreate(byte* name);

    /// <summary>The token's name, or null for a token off a file that names nothing.</summary>
    byte* FromSymbol(int token);
}

/// <summary>
/// CSaveRestoreSegment's token table (FindCreateSymbol / StringFromSymbol),
/// over the engine's own <c>char **</c> array: a string hash, linear probing,
/// and the caller's pointer stored rather than a copy, exactly as the engine
/// reads it back when it writes the table into the .sav.
///
/// <para>
/// ONE DIFFERENCE from stock: an out-of-range token answers null rather than
/// asserting, because the token came off a FILE and is hostile input.
/// </para>
/// </summary>
public readonly unsafe struct SaveTokenTable(byte** tokens, int count) : ISaveSymbols
{
    /// <summary>The slot count (SizeSymbolTable).</summary>
    public int Count { get; } = count;

    /// <summary>CSaveRestoreSegment::HashString: rotate right by four, xor the signed char.</summary>
    public static uint HashString(byte* token)
    {
        uint hash = 0;
        for (; *token != 0; token++)
        {
            hash = (hash >> 4) | (hash << 28);
            hash ^= (uint)(int)(sbyte)*token;
        }
        return hash;
    }

    /// <inheritdoc/>
    public ushort FindCreate(byte* name)
    {
        if (tokens == null || Count <= 0)
            return 0;

        uint start = HashString(name) % (uint)Count;
        for (int probe = 0; probe < Count; probe++)
        {
            int slot = (int)((start + (uint)probe) % (uint)Count);
            if (tokens[slot] == null || SaveBlockCodec.StrEquals(tokens[slot], name))
            {
                tokens[slot] = name;
                return (ushort)slot;
            }
        }
        return 0;
    }

    /// <inheritdoc/>
    public byte* FromSymbol(int token)
        => tokens == null || token < 0 || token >= Count ? null : tokens[token];
}

/// <summary>
/// A cursor over the save buffer. An overrun POISONS it: every later call
/// fails, so a block that did not fit is never followed by one that did.
/// </summary>
public unsafe ref struct SaveCursor
{
    private readonly Span<byte> _buffer;

    /// <summary>A cursor over <paramref name="bytes"/> bytes at <paramref name="pBase"/>; a null base is overflowed from the start.</summary>
    public SaveCursor(byte* pBase, int bytes)
    {
        _buffer = pBase == null ? Span<byte>.Empty : new Span<byte>(pBase, bytes < 0 ? 0 : bytes);
        Overflowed = pBase == null;
        Pos = 0;
    }

    /// <summary>Bytes consumed.</summary>
    public int Pos { get; private set; }

    /// <summary>Bytes left.</summary>
    public readonly int Available => _buffer.Length - Pos;

    /// <summary>True once anything overran.</summary>
    public bool Overflowed { get; private set; }

    private bool Fits(int bytes)
    {
        if (Overflowed || bytes < 0 || bytes > Available)
        {
            Overflowed = true;
            return false;
        }
        return true;
    }

    /// <summary>Copies all of <paramref name="data"/> in, or nothing.</summary>
    public bool Write(scoped ReadOnlySpan<byte> data)
    {
        if (!Fits(data.Length))
            return false;
        data.CopyTo(_buffer[Pos..]);
        Pos += data.Length;
        return true;
    }

    /// <summary>Fills all of <paramref name="data"/>, or nothing.</summary>
    public bool Read(scoped Span<byte> data)
    {
        if (!Fits(data.Length))
            return false;
        _buffer.Slice(Pos, data.Length).CopyTo(data);
        Pos += data.Length;
        return true;
    }

    /// <summary>Two bytes, little-endian as the engine lays a short.</summary>
    public bool WriteInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        MemoryMarshal.Write(bytes, in value);
        return Write(bytes);
    }

    /// <summary>Four bytes.</summary>
    public bool WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        MemoryMarshal.Write(bytes, in value);
        return Write(bytes);
    }

    /// <summary>Reads a short.</summary>
    public bool ReadInt16(out short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        bool ok = Read(bytes);
        value = ok ? MemoryMarshal.Read<short>(bytes) : (short)0;
        return ok;
    }

    /// <summary>Reads an int.</summary>
    public bool ReadInt32(out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        bool ok = Read(bytes);
        value = ok ? MemoryMarshal.Read<int>(bytes) : 0;
        return ok;
    }

    /// <summary>Moves past <paramref name="bytes"/> bytes, or poisons.</summary>
    public bool Skip(int bytes)
    {
        if (!Fits(bytes))
            return false;
        Pos += bytes;
        return true;
    }

    /// <summary>Moves back, never past the start. Does not clear a poisoning.</summary>
    public void Rewind(int bytes)
    {
        if (bytes > Pos)
            bytes = Pos;
        Pos -= bytes;
    }
}

/// <summary>
/// The field block and the blob block (stock CSave::WriteFields' record
/// framing: a block opens with a four-byte count record under the block's
/// name, then one record per field, each with its own width and name).
/// </summary>
public static unsafe class SaveBlockCodec
{
    /// <summary>A record header: the width short and the token short.</summary>
    public const int RecordHeader = 4;

    /// <summary>The largest payload one blob record carries.</summary>
    public const int BlobChunk = 30000;

    /// <summary>The answer of a blob read that found a different block, or none, at the cursor.</summary>
    public const int NoBlock = -2;

    /// <summary>strcmp == 0 over two NUL-terminated strings.</summary>
    public static bool StrEquals(byte* a, byte* b)
    {
        if (a == null || b == null)
            return a == b;
        return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(a)
                            .SequenceEqual(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(b));
    }

    /// <summary>
    /// CRestore's field match: case-blind over ASCII, and false when either
    /// side is null (a token that resolved to nothing matches no field).
    /// </summary>
    public static bool NamesMatch(byte* a, byte* b)
    {
        if (a == null || b == null)
            return false;

        for (; *a != 0 && *b != 0; a++, b++)
        {
            if (Lower(*a) != Lower(*b))
                return false;
        }
        return *a == *b;
    }

    private static byte Lower(byte c) => c is >= (byte)'A' and <= (byte)'Z' ? (byte)(c - 'A' + 'a') : c;

    /// <summary>The managed string of a native name, for messages.</summary>
    public static string NameText(byte* name)
        => name == null ? "<null>" : Marshal.PtrToStringUTF8((nint)name) ?? "<null>";

    /// <summary>One record: width, token, bytes.</summary>
    public static bool WriteRecord(ref SaveCursor cursor, ISaveSymbols symbols, byte* name, ReadOnlySpan<byte> data)
    {
        if (data.Length > short.MaxValue)
            return false;

        short token = (short)symbols.FindCreate(name);
        return cursor.WriteInt16((short)data.Length)
               && cursor.WriteInt16(token)
               && (data.Length == 0 || cursor.Write(data));
    }

    /// <summary>
    /// Writes one field block of the struct at <paramref name="pBase"/>. Answers
    /// the block's bytes, or -1 when it did not fit (the cursor is then
    /// poisoned and the caller must not advance). A field this build cannot
    /// encode is OMITTED, counted and named rather than refusing the block:
    /// each record carries its own name and width, so the reader never sees it
    /// rather than reading at a wrong offset.
    /// </summary>
    public static int WriteFieldBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                      byte* pBase, ReadOnlySpan<SaveFieldDesc> fields, float baseTime,
                                      out int omitted)
    {
        omitted = 0;

        if (blockName == null || pBase == null)
            return -1;

        int start = cursor.Pos;

        // The count record, written as -1 and patched once the count is known.
        if (!WriteRecord(ref cursor, symbols, blockName, BitConverterBytes(-1)))
            return -1;

        int written = 0;
        foreach (SaveFieldDesc field in fields)
        {
            int bytes = SaveFieldCodec.Bytes(field.Type, field.Size);
            if (bytes < 0)
            {
                omitted++;
                continue;
            }

            byte* pField = pBase + field.Offset;
            if (SaveFieldCodec.KindOf(field.Type) == SaveFieldKind.Time)
            {
                float stored = SaveFieldCodec.EncodeTime(*(float*)pField, baseTime);
                if (!WriteRecord(ref cursor, symbols, field.Name, new ReadOnlySpan<byte>(&stored, 4)))
                    return -1;
            }
            else if (!WriteRecord(ref cursor, symbols, field.Name, new ReadOnlySpan<byte>(pField, bytes)))
            {
                return -1;
            }

            written++;
        }

        int end = cursor.Pos;
        cursor.Rewind(end - start - RecordHeader);
        if (!cursor.WriteInt32(written))
            return -1;
        if (!cursor.Skip(end - cursor.Pos))
            return -1;

        return end - start;
    }

    private static ReadOnlySpan<byte> BitConverterBytes(int value) => BitConverter.GetBytes(value);

    /// <summary>
    /// Reads one field block into the struct at <paramref name="pBase"/>, in two
    /// passes: walk the records to find the block's end (a short or corrupt
    /// block is refused before any field is touched), then zero the fields and
    /// fill the ones the file carries, matched by case-blind name. A record of
    /// an unknown name, or of a width the field does not have, is skipped by
    /// its own width. Answers the bytes consumed, or -1 with the refusal.
    /// </summary>
    public static int ReadFieldBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                     byte* pBase, ReadOnlySpan<SaveFieldDesc> fields, float baseTime,
                                     out string? refusal)
    {
        refusal = null;

        if (blockName == null || pBase == null)
        {
            refusal = "a null buffer, struct or block name";
            return -1;
        }

        int start = cursor.Pos;

        if (!cursor.ReadInt16(out short size) || !cursor.ReadInt16(out short token))
        {
            refusal = "the save data ends inside a block header";
            return -1;
        }

        if (size != 4)
        {
            cursor.Rewind(cursor.Pos - start);
            refusal = "no block header here -- the first record is not an int";
            return -1;
        }

        if ((ushort)token != symbols.FindCreate(blockName))
        {
            byte* found = symbols.FromSymbol(token);
            cursor.Rewind(cursor.Pos - start);
            refusal = found != null ? NameText(found) : "a block written under another name";
            return -1;
        }

        if (!cursor.ReadInt32(out int saved))
        {
            refusal = "the save data ends inside a block's field count";
            return -1;
        }

        if (saved < 0)
        {
            cursor.Rewind(cursor.Pos - start);
            refusal = "a negative field count, which came off a file";
            return -1;
        }

        int afterHeader = cursor.Pos;

        // Pass one: the block's end, before any field is touched.
        for (int i = 0; i < saved; i++)
        {
            if (!cursor.ReadInt16(out short walkSize) || !cursor.ReadInt16(out _))
            {
                cursor.Rewind(cursor.Pos - start);
                refusal = "the save data ends inside a field record header";
                return -1;
            }

            if (walkSize < 0)
            {
                cursor.Rewind(cursor.Pos - start);
                refusal = "a negative field width, which came off a file";
                return -1;
            }

            if (!cursor.Skip(walkSize))
            {
                cursor.Rewind(cursor.Pos - start);
                refusal = "the save data ends inside a field record";
                return -1;
            }
        }

        int end = cursor.Pos;
        cursor.Rewind(end - afterHeader);

        // Pass two: zero what this build declares, then fill what the file has.
        foreach (SaveFieldDesc field in fields)
        {
            int clear = SaveFieldCodec.Bytes(field.Type, field.Size);
            if (clear > 0)
                new Span<byte>(pBase + field.Offset, clear).Clear();
        }

        for (int i = 0; i < saved; i++)
        {
            if (!cursor.ReadInt16(out short recSize) || !cursor.ReadInt16(out short recToken))
            {
                refusal = "the save data ends inside a field record header";
                return -1;
            }

            byte* recName = symbols.FromSymbol(recToken);
            int at = -1;
            for (int j = 0; j < fields.Length && at < 0; j++)
            {
                if (NamesMatch(fields[j].Name, recName))
                    at = j;
            }

            int bytes = at >= 0 ? SaveFieldCodec.Bytes(fields[at].Type, fields[at].Size) : -1;
            if (at < 0 || bytes != recSize)
            {
                if (!cursor.Skip(recSize))
                {
                    refusal = "the save data ends inside a field record";
                    return -1;
                }
                continue;
            }

            byte* pField = pBase + fields[at].Offset;
            if (SaveFieldCodec.KindOf(fields[at].Type) == SaveFieldKind.Time)
            {
                float stored = 0.0f;
                if (!cursor.Read(new Span<byte>(&stored, 4)))
                {
                    refusal = "the save data ends inside a time field";
                    return -1;
                }
                *(float*)pField = SaveFieldCodec.DecodeTime(stored, baseTime);
            }
            else if (!cursor.Read(new Span<byte>(pField, bytes)))
            {
                refusal = "the save data ends inside a field";
                return -1;
            }
        }

        return cursor.Pos - start;
    }

    /// <summary>
    /// Writes a blob block: a count record under <paramref name="blockName"/>
    /// then the payload in records of at most <see cref="BlobChunk"/> bytes,
    /// each under <paramref name="chunkName"/>. Answers the block's bytes, or -1.
    /// </summary>
    public static int WriteBlobBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                     byte* chunkName, ReadOnlySpan<byte> data)
    {
        if (blockName == null || chunkName == null)
            return -1;

        int start = cursor.Pos;
        int chunks = (data.Length + BlobChunk - 1) / BlobChunk;

        if (!WriteRecord(ref cursor, symbols, blockName, BitConverterBytes(chunks)))
            return -1;

        for (int at = 0; at < data.Length; at += BlobChunk)
        {
            int count = Math.Min(BlobChunk, data.Length - at);
            if (!WriteRecord(ref cursor, symbols, chunkName, data.Slice(at, count)))
                return -1;
        }

        return cursor.Pos - start;
    }

    /// <summary>
    /// Reads a blob block. SIZE, THEN FILL: when <paramref name="output"/> is
    /// shorter than the payload the cursor does not move and the payload's size
    /// is the answer; when it is long enough the payload is copied and the
    /// cursor moves past the block. <see cref="NoBlock"/> when the cursor is at
    /// another block or at the end (not an error: a save from before the
    /// block); -1 with the refusal when the block is there and corrupt.
    /// </summary>
    public static int ReadBlobBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                    Span<byte> output, out string? refusal)
    {
        refusal = null;

        if (blockName == null)
        {
            refusal = "a null buffer or a negative size";
            return -1;
        }

        int start = cursor.Pos;

        if (!cursor.ReadInt16(out short size) || !cursor.ReadInt16(out short token))
        {
            cursor.Rewind(cursor.Pos - start);
            return NoBlock;
        }

        if (size != 4 || (ushort)token != symbols.FindCreate(blockName))
        {
            cursor.Rewind(cursor.Pos - start);
            return NoBlock;
        }

        if (!cursor.ReadInt32(out int chunks))
        {
            cursor.Rewind(cursor.Pos - start);
            refusal = "the save data ends inside the block's chunk count";
            return -1;
        }

        if (chunks < 0)
        {
            cursor.Rewind(cursor.Pos - start);
            refusal = "a negative chunk count, which came off a file";
            return -1;
        }

        int afterHeader = cursor.Pos;
        int payload = 0;
        for (int i = 0; i < chunks; i++)
        {
            if (!cursor.ReadInt16(out short chunkSize) || !cursor.ReadInt16(out _))
            {
                cursor.Rewind(cursor.Pos - start);
                refusal = "the save data ends inside a chunk header";
                return -1;
            }

            if (chunkSize < 0 || !cursor.Skip(chunkSize))
            {
                cursor.Rewind(cursor.Pos - start);
                refusal = "a chunk runs past the end of the save data";
                return -1;
            }

            payload += chunkSize;
        }

        int end = cursor.Pos;
        if (output.Length < payload)
        {
            cursor.Rewind(end - start);
            return payload;
        }

        cursor.Rewind(end - afterHeader);
        int filled = 0;
        for (int i = 0; i < chunks; i++)
        {
            cursor.ReadInt16(out short chunkSize);
            cursor.ReadInt16(out _);
            if (chunkSize > 0)
                cursor.Read(output.Slice(filled, chunkSize));
            filled += chunkSize;
        }

        return payload;
    }
}
