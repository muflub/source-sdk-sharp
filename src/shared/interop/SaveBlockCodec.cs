//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================
// THE SAVE BLOCK CODEC, both sides' (plan_server_native_v2 T4c; the user's
// 2026-10-11 ruling: a managed save/restore codec).
//
// What shell_savegame.h was: the record layout of a field block over a byte
// cursor and the save's own token table. The layout is stock CSave's (a
// two-byte width, a two-byte token, the bytes). The server's engine blocks and
// the client's decal and music blocks are both encoded here; the server's
// managed-world blob block is SaveBlobCodec (Server.Interop), the one part only
// one side uses. docs/managed-saverestore.md is the argument for each field
// type, keyed by section.
//
// Everything here is pure: a cursor over bytes, names as NUL-terminated native
// strings, and a token table. The session is the part that knows where those
// live in the engine's CSaveRestoreData.
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
public static unsafe class SaveFieldCodec
{
    /// <summary>datamap.h FIELD_VOID: never saved.</summary>
    public const int FieldVoid = 0;

    /// <summary>datamap.h FIELD_VECTOR.</summary>
    public const int FieldVector = 3;

    /// <summary>datamap.h FIELD_INTEGER.</summary>
    public const int FieldInteger = 5;

    /// <summary>datamap.h FIELD_SHORT.</summary>
    public const int FieldShort = 7;

    /// <summary>datamap.h FIELD_CHARACTER.</summary>
    public const int FieldCharacter = 8;

    /// <summary>datamap.h FIELD_EMBEDDED: another datadesc, which this codec does not carry.</summary>
    public const int FieldEmbedded = 10;

    /// <summary>datamap.h FIELD_CUSTOM: its own save/restore ops, which this codec does not carry.</summary>
    public const int FieldCustom = 11;

    /// <summary>datamap.h FIELD_EHANDLE: empty is 0xFFFFFFFF, not zero.</summary>
    public const int FieldEHandle = 13;

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

    /// <summary>
    /// datamap.h's FIELD_SIZE: one element's bytes, per fieldtype_t, for the
    /// 64-bit build (FIELD_STRING, CLASSPTR, MODELNAME, SOUNDNAME are an intp,
    /// FIELD_FUNCTION a uint64 under POSIX; FIELD_EDICT is declared
    /// sizeof(int)). Zero for VOID, EMBEDDED, CUSTOM and an unknown type, as
    /// CDatamapFieldSizeDeducer's default is. Stock's ShouldSaveField and
    /// EmptyFields both walk fieldSize of these.
    /// </summary>
    public static int ElementBytes(int fieldType) => fieldType switch
    {
        1 => 4,          // FLOAT
        2 => 8,          // STRING
        3 => 12,         // VECTOR
        4 => 16,         // QUATERNION
        5 => 4,          // INTEGER
        6 => 1,          // BOOLEAN
        7 => 2,          // SHORT
        8 => 1,          // CHARACTER
        9 => 4,          // COLOR32
        12 => 8,         // CLASSPTR
        13 => 4,         // EHANDLE
        14 => 4,         // EDICT
        15 => 12,        // POSITION_VECTOR
        16 => 4,         // TIME
        17 => 4,         // TICK
        18 => 8,         // MODELNAME
        19 => 8,         // SOUNDNAME
        20 => 4,         // INPUT
        21 => 8,         // FUNCTION
        22 => 64,        // VMATRIX
        23 => 64,        // VMATRIX_WORLDSPACE
        24 => 48,        // MATRIX3X4_WORLDSPACE
        25 => 8,         // INTERVAL
        26 => 4,         // MODELINDEX
        27 => 4,         // MATERIALINDEX
        28 => 8,         // VECTOR2D
        _ => 0,
    };

    /// <summary>
    /// Stock's CSave::ShouldSaveField for a saved field: FIELD_VOID never has
    /// data, an EHANDLE has data when any handle is not 0xFFFFFFFF, and every
    /// other type when any of its fieldSize x <see cref="ElementBytes"/> bytes
    /// is not zero (DataEmpty). A field with no data is NOT WRITTEN, and reads
    /// back as zero because the reader empties every field first. EMBEDDED and
    /// CUSTOM ask their own description, which this codec does not carry, so
    /// they count as data.
    /// </summary>
    public static bool HasData(byte* pField, int fieldType, int fieldSize)
    {
        if (fieldType == FieldVoid)
            return false;

        if (fieldType is FieldEmbedded or FieldCustom)
            return true;

        if (fieldType == FieldEHandle)
        {
            for (int i = 0; i < fieldSize; i++)
            {
                if (Unsafe.ReadUnaligned<uint>(pField + i * 4) != 0xFFFFFFFFu)
                    return true;
            }
            return false;
        }

        int bytes = fieldSize * ElementBytes(fieldType);
        return bytes > 0 && new ReadOnlySpan<byte>(pField, bytes).ContainsAnyExcept((byte)0);
    }

    /// <summary>
    /// Stock's CRestore::EmptyFields for one saved field, before a block is
    /// read into the struct: fieldSize x <see cref="ElementBytes"/> bytes set
    /// to zero (0xFF for an EHANDLE). EMBEDDED and CUSTOM are emptied through
    /// their own description, which this codec does not carry, so they are
    /// left as they are.
    /// </summary>
    public static void Empty(byte* pField, int fieldType, int fieldSize)
    {
        int bytes = fieldSize * ElementBytes(fieldType);
        if (bytes > 0)
            new Span<byte>(pField, bytes).Fill(fieldType == FieldEHandle ? (byte)0xFF : (byte)0);
    }

    /// <summary>The bytes of one element as the codec reads it: what stock's ReadSimple divides by.</summary>
    private static int ReadElement(SaveFieldKind kind) => kind switch
    {
        SaveFieldKind.CharArray => 1,
        SaveFieldKind.Short => 2,
        SaveFieldKind.Integer or SaveFieldKind.Time => 4,
        _ => 12,
    };

    /// <summary>
    /// Stock's CRestore::ReadSimple over a record of <paramref name="available"/>
    /// bytes for a field wanting <paramref name="wanted"/>: the smaller of the
    /// two is read, the rest of the record skipped, and the elements read whole
    /// are what the time and position rebases apply to.
    /// </summary>
    public static int WholeElementsRead(SaveFieldKind kind, int wanted, int available, out int take)
    {
        take = Math.Min(wanted, available);
        return take / ReadElement(kind);
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
/// What a field block is written or read against: the save's base time
/// (CGameSaveRestoreInfo::GetBaseTime), which times are stored relative to,
/// and GetLandmark -- the level's landmark offset when it uses one, else the
/// origin -- which position vectors are.
/// </summary>
public readonly struct SaveLevel(float baseTime, float landmarkX, float landmarkY, float landmarkZ)
{
    /// <summary>A level with no landmark in use.</summary>
    public SaveLevel(float baseTime)
        : this(baseTime, 0f, 0f, 0f)
    {
    }

    /// <summary>GetBaseTime.</summary>
    public float BaseTime { get; } = baseTime;

    /// <summary>GetLandmark's x.</summary>
    public float LandmarkX { get; } = landmarkX;

    /// <summary>GetLandmark's y.</summary>
    public float LandmarkY { get; } = landmarkY;

    /// <summary>GetLandmark's z.</summary>
    public float LandmarkZ { get; } = landmarkZ;
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
/// The field block (stock CSave::WriteFields' record framing: a block opens
/// with a four-byte count record under the block's name, then one record per
/// field, each with its own width and name).
/// </summary>
public static unsafe class SaveBlockCodec
{
    /// <summary>A record header: the width short and the token short.</summary>
    public const int RecordHeader = 4;

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
    /// True for a field stock would write (it has data) and this build cannot
    /// encode: the one kind of field <see cref="WriteFieldBlock"/> omits.
    /// </summary>
    public static bool IsOmitted(byte* pBase, SaveFieldDesc field)
        => SaveFieldCodec.Bytes(field.Type, field.Size) < 0
           && SaveFieldCodec.HasData(pBase + field.Offset, field.Type, field.Size);

    /// <summary>
    /// Writes one field block of the struct at <paramref name="pBase"/>, as
    /// stock's CSave::WriteFields does: a field with no data is not written
    /// (ShouldSaveField), a time is stored relative to the base time and a
    /// position relative to the landmark. Answers the block's bytes, or -1 when
    /// it did not fit (the cursor is then poisoned and the caller must not
    /// advance). A field with data that this build cannot encode is OMITTED,
    /// counted and named rather than refusing the block: each record carries
    /// its own name and width, so the reader never sees it rather than reading
    /// at a wrong offset.
    /// </summary>
    public static int WriteFieldBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                      byte* pBase, ReadOnlySpan<SaveFieldDesc> fields, SaveLevel level,
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
        float* v = stackalloc float[3];     // a position vector, re-expressed against the landmark
        foreach (SaveFieldDesc field in fields)
        {
            byte* pField = pBase + field.Offset;
            if (!SaveFieldCodec.HasData(pField, field.Type, field.Size))
                continue;

            int bytes = SaveFieldCodec.Bytes(field.Type, field.Size);
            if (bytes < 0)
            {
                omitted++;
                continue;
            }

            SaveFieldKind kind = SaveFieldCodec.KindOf(field.Type);
            bool ok;
            if (kind == SaveFieldKind.Time)
            {
                float stored = SaveFieldCodec.EncodeTime(Unsafe.ReadUnaligned<float>(pField), level.BaseTime);
                ok = WriteRecord(ref cursor, symbols, field.Name, new ReadOnlySpan<byte>(&stored, 4));
            }
            else if (kind == SaveFieldKind.PositionVector)
            {
                new ReadOnlySpan<byte>(pField, 12).CopyTo(new Span<byte>(v, 12));
                (v[0], v[1], v[2]) = SaveFieldCodec.EncodePosition(v[0], v[1], v[2], level.LandmarkX, level.LandmarkY,
                                                                   level.LandmarkZ);
                ok = WriteRecord(ref cursor, symbols, field.Name, new ReadOnlySpan<byte>(v, 12));
            }
            else
            {
                ok = WriteRecord(ref cursor, symbols, field.Name, new ReadOnlySpan<byte>(pField, bytes));
            }

            if (!ok)
                return -1;

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

    internal static ReadOnlySpan<byte> BitConverterBytes(int value) => BitConverter.GetBytes(value);

    /// <summary>
    /// Reads one field block into the struct at <paramref name="pBase"/>, as
    /// stock's CRestore::ReadFields does, in two passes: walk the records to
    /// find the block's end (a short or corrupt block is refused before any
    /// field is touched), then empty the fields (EmptyFields) and fill the ones
    /// the file carries, matched by case-blind name. A record of an unknown
    /// name, or for a field this build cannot carry, is skipped by its own
    /// width; a record wider or narrower than its field fills what both have
    /// and skips the rest (ReadSimple). Times are rebased onto the base time and
    /// positions onto the landmark. Answers the bytes consumed, or -1 with the
    /// refusal.
    /// </summary>
    public static int ReadFieldBlock(ref SaveCursor cursor, ISaveSymbols symbols, byte* blockName,
                                     byte* pBase, ReadOnlySpan<SaveFieldDesc> fields, SaveLevel level,
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

        // Pass two: empty every declared field, then fill what the file has.
        foreach (SaveFieldDesc field in fields)
            SaveFieldCodec.Empty(pBase + field.Offset, field.Type, field.Size);

        float* v = stackalloc float[3];     // a position vector, rebased onto the landmark
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
            if (bytes < 0)
            {
                if (!cursor.Skip(recSize))
                {
                    refusal = "the save data ends inside a field record";
                    return -1;
                }
                continue;
            }

            byte* pField = pBase + fields[at].Offset;
            SaveFieldKind kind = SaveFieldCodec.KindOf(fields[at].Type);
            int whole = SaveFieldCodec.WholeElementsRead(kind, bytes, recSize, out int take);
            if (!cursor.Read(new Span<byte>(pField, take)) || !cursor.Skip(recSize - take))
            {
                refusal = "the save data ends inside a field";
                return -1;
            }

            if (whole < 1)
                continue;

            if (kind == SaveFieldKind.Time)
            {
                Unsafe.WriteUnaligned(pField, SaveFieldCodec.DecodeTime(Unsafe.ReadUnaligned<float>(pField), level.BaseTime));
            }
            else if (kind == SaveFieldKind.PositionVector)
            {
                new ReadOnlySpan<byte>(pField, 12).CopyTo(new Span<byte>(v, 12));
                (v[0], v[1], v[2]) = SaveFieldCodec.DecodePosition(v[0], v[1], v[2], level.LandmarkX, level.LandmarkY,
                                                                   level.LandmarkZ);
                new ReadOnlySpan<byte>(v, 12).CopyTo(new Span<byte>(pField, 12));
            }
        }

        return cursor.Pos - start;
    }
}
