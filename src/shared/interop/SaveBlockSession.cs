//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// THE SAVE BLOCK SESSION, both sides' (plan_server_native_v2 T4c for the
// server; the client's IBaseClientDLL cluster since the client save codec
// lane).
//
// What shell_savegame.cpp did, decided here: SaveInit's buffer size and token
// count, and the field blocks of the engine's own structs (the server's
// GameHeader, Save Header, ADJACENCY and LIGHTSTYLE; the client's DECALLIST
// and MUSICLIST). The engine still calls each side's save methods through its
// DLL's vtable, and each is one forward into that side's managed area; the
// engine still allocates the block and reads CSaveRestoreData by layout, so
// the layout stays declared in engineabi/saverestore.h (held to upstream by
// tools/abi-parity) and its offsets are SaveRestoreLayout's, each pinned by a
// fact here and a gtest there (save_restore_layout_test.cpp).
//=============================================================================

/// <summary>
/// The engine's save allocator (IVEngineServer and IVEngineClient::SaveAllocMemory /
/// SaveFreeMemory): the engine frees what it allocates when the save or load
/// is done, so SaveInit's one free is on its failure path.
/// </summary>
public unsafe interface ISaveMemory
{
    /// <summary>calloc-shaped: <paramref name="count"/> elements of <paramref name="size"/> bytes, or null.</summary>
    void* SaveAlloc(nuint count, nuint size);

    /// <summary>Frees what <see cref="SaveAlloc"/> answered.</summary>
    void SaveFree(void* block);
}

/// <summary>
/// Byte offsets of the engine structs the save is made of. The C++ is
/// engineabi/saverestore.h (CSaveRestoreData) and datamap.h
/// (typedescription_t); save_restore_layout_test.cpp pins each number against
/// offsetof, SaveGameSessionTests pins the same number here.
/// </summary>
public static class SaveRestoreLayout
{
    /// <summary>CSaveRestoreSegment::pBaseData.</summary>
    public const int BaseData = 0;

    /// <summary>CSaveRestoreSegment::pCurrentData.</summary>
    public const int CurrentData = 8;

    /// <summary>CSaveRestoreSegment::size (pCurrentData - pBaseData).</summary>
    public const int Size = 16;

    /// <summary>CSaveRestoreSegment::bufferSize.</summary>
    public const int BufferSize = 20;

    /// <summary>CSaveRestoreSegment::tokenCount.</summary>
    public const int TokenCount = 24;

    /// <summary>CSaveRestoreSegment::pTokens.</summary>
    public const int Tokens = 32;

    /// <summary>levelInfo.connectionCount (CGameSaveRestoreInfo, the second base).</summary>
    public const int ConnectionCount = 40;

    /// <summary>levelInfo.fUseLandmark.</summary>
    public const int UseLandmark = 1456;

    /// <summary>levelInfo.vecLandmarkOffset.</summary>
    public const int LandmarkOffset = 1480;

    /// <summary>levelInfo.time: GetBaseTime().</summary>
    public const int Time = 1492;

    /// <summary>sizeof(CSaveRestoreData).</summary>
    public const int SizeOf = 1616;

    /// <summary>typedescription_t::fieldType.</summary>
    public const int FieldType = 0;

    /// <summary>typedescription_t::fieldName.</summary>
    public const int FieldName = 8;

    /// <summary>typedescription_t::fieldOffset[TD_OFFSET_NORMAL].</summary>
    public const int FieldOffset = 16;

    /// <summary>typedescription_t::fieldSize (unsigned short).</summary>
    public const int FieldSize = 24;
}

/// <summary>A CSaveRestoreData the engine holds, read and moved by layout.</summary>
public readonly unsafe struct SaveRestoreBlock(void* block)
{
    private readonly byte* _at = (byte*)block;

    /// <summary>True when there is no block.</summary>
    public bool IsNull => _at == null;

    /// <summary>The next byte to read or write (AccessCurPos).</summary>
    public byte* Cursor => *(byte**)(_at + SaveRestoreLayout.CurrentData);

    /// <summary>Bytes consumed (GetCurPos).</summary>
    public int Pos => *(int*)(_at + SaveRestoreLayout.Size);

    /// <summary>The buffer's size (SizeBuffer).</summary>
    public int BufferBytes => *(int*)(_at + SaveRestoreLayout.BufferSize);

    /// <summary>BytesAvailable.</summary>
    public int Available => BufferBytes - Pos;

    /// <summary>GetBaseTime.</summary>
    public float BaseTime => *(float*)(_at + SaveRestoreLayout.Time);

    /// <summary>GetBaseTime and GetLandmark: the offset when levelInfo.fUseLandmark, else the origin.</summary>
    public SaveLevel Level
    {
        get
        {
            if (*(_at + SaveRestoreLayout.UseLandmark) == 0)
                return new SaveLevel(BaseTime);
            float* landmark = (float*)(_at + SaveRestoreLayout.LandmarkOffset);
            return new SaveLevel(BaseTime, landmark[0], landmark[1], landmark[2]);
        }
    }

    /// <summary>The token table (SizeSymbolTable slots).</summary>
    public SaveTokenTable Symbols => new(*(byte***)(_at + SaveRestoreLayout.Tokens),
                                         *(int*)(_at + SaveRestoreLayout.TokenCount));

    /// <summary>MoveCurPos.</summary>
    public void Advance(int bytes)
    {
        *(byte**)(_at + SaveRestoreLayout.CurrentData) += bytes;
        *(int*)(_at + SaveRestoreLayout.Size) += bytes;
    }

    /// <summary>
    /// Places a CSaveRestoreData at <paramref name="block"/>: the constructors
    /// zero every field, then Init, InitSymbolTable and the level info stamp.
    /// </summary>
    public static SaveRestoreBlock Place(void* block, int bufferBytes, byte** tokens, int tokenCount, float time)
    {
        byte* at = (byte*)block;
        new Span<byte>(at, SaveRestoreLayout.SizeOf).Clear();

        byte* data = at + SaveRestoreLayout.SizeOf;   // the buffer follows the structure
        *(byte**)(at + SaveRestoreLayout.BaseData) = data;
        *(byte**)(at + SaveRestoreLayout.CurrentData) = data;
        *(int*)(at + SaveRestoreLayout.BufferSize) = bufferBytes;

        new Span<byte>(tokens, tokenCount * sizeof(nint)).Clear();
        *(int*)(at + SaveRestoreLayout.TokenCount) = tokenCount;
        *(byte***)(at + SaveRestoreLayout.Tokens) = tokens;

        // levelInfo: the time stamp; no landmark, and NO connections (the
        // engine loops connectionCount ADJACENCY blocks with no cap of its own).
        *(float*)(at + SaveRestoreLayout.Time) = time;
        return new SaveRestoreBlock(block);
    }
}

/// <summary>
/// A save's field blocks, decided in C#: SaveInit, SaveWriteFields and
/// SaveReadFields, the three each side's engine cluster asks of its DLL.
/// Every refusal is loud, because the engine abandons a save on a NULL
/// SaveInit and prints nothing.
/// </summary>
public sealed unsafe class SaveBlockSession
{
    /// <summary>
    /// The buffer when the engine passes 0, which it does at every SaveInit
    /// call site (docs/managed-saverestore.md §4a): a measured save is a few
    /// KB, and the server's managed entity block grows with the world.
    /// </summary>
    public const int DefaultBufferBytes = 4 * 1024 * 1024;

    /// <summary>The token table's slots (stock's 0xfff).</summary>
    public const int TokenSlots = 0xfff;

    private readonly ILog _log;
    private readonly ISaveMemory _memory;
    private readonly bool _traceBlocks;

    // The name an unnamed field goes under. Native memory, never freed: the
    // token table holds it BY POINTER, and the engine reads it back when it
    // writes the table into the file, after the call that interned it.
    // The name a field with none is described by, made on first use (a
    // constructor only stores, by the interop layer's rule).
    private byte* _unnamed;

    private byte* Unnamed => _unnamed != null ? _unnamed : _unnamed = NativeName("<unnamed>");

    /// <summary>
    /// A session over the engine's allocator: the side's registered one,
    /// which the composition acquired with the engine client or server.
    /// <paramref name="traceBlocks"/> narrates every field block (the
    /// server's few dozen); SaveInit and every refusal are always said.
    /// </summary>
    public SaveBlockSession(ILog log, ISaveMemory memory, bool traceBlocks)
    {
        _log = log;
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _traceBlocks = traceBlocks;
    }

    /// <summary>A NUL-terminated copy of <paramref name="text"/> in native memory that is never freed.</summary>
    public static byte* NativeName(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        byte* p = (byte*)NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    private void Trace(string text) => _log.Msg("[SourceSharp][save] " + text + "\n");

    /// <summary>
    /// SaveInit: allocates the block through the engine and places it, or
    /// answers null having said which allocation failed.
    /// </summary>
    public void* Init(int size, float curTime)
    {
        ISaveMemory memory = _memory;

        // The engine passes 0 and we choose; a size it does pass is honoured.
        if (size <= 0)
            size = DefaultBufferBytes;

        void* buffer = memory.SaveAlloc((nuint)(SaveRestoreLayout.SizeOf + size), 1);
        if (buffer == null)
        {
            _log.Warning($"[SourceSharp] SaveInit: SaveAllocMemory refused {SaveRestoreLayout.SizeOf + size} bytes -- "
                         + "returning NULL, which aborts the whole save with no file and no further message\n");
            return null;
        }

        // Stock's ::SaveInit freed the wrong pointer here (the null the token
        // allocation answered); the buffer is held in its own variable.
        void* tokens = memory.SaveAlloc(TokenSlots, (nuint)sizeof(nint));
        if (tokens == null)
        {
            _log.Warning($"[SourceSharp] SaveInit: SaveAllocMemory refused the {TokenSlots}-entry token table -- "
                         + "returning NULL, which aborts the whole save with no file and no further message\n");
            memory.SaveFree(buffer);
            return null;
        }

        SaveRestoreBlock block = SaveRestoreBlock.Place(buffer, size, (byte**)tokens, TokenSlots, curTime);
        Trace($"SaveInit( size={size} ) -> {block.Available} byte buffer, {TokenSlots} symbol slots, "
              + "entity table EMPTY (tableCount == 0)");
        return buffer;
    }

    private SaveFieldDesc[] Fields(byte* fields, int stride, int count)
    {
        var descs = new SaveFieldDesc[count];
        for (int i = 0; i < count; i++)
        {
            byte* field = fields + (nint)i * stride;
            byte* name = *(byte**)(field + SaveRestoreLayout.FieldName);
            descs[i] = new SaveFieldDesc(name != null ? name : Unnamed,
                                         *(int*)(field + SaveRestoreLayout.FieldType),
                                         *(ushort*)(field + SaveRestoreLayout.FieldSize),
                                         *(int*)(field + SaveRestoreLayout.FieldOffset));
        }
        return descs;
    }

    /// <summary>
    /// SaveWriteFields: one field block of an engine struct (the save header,
    /// the adjacency list, the light styles), at the cursor.
    /// </summary>
    public bool WriteFields(void* save, byte* name, byte* pBase, byte* fields, int stride, int count)
    {
        var block = new SaveRestoreBlock(save);
        if (block.IsNull || name == null || pBase == null || fields == null || count < 0)
        {
            _log.Warning($"[SourceSharp] SaveWriteFields: refused '{SaveBlockCodec.NameText(name)}' -- a null argument\n");
            return false;
        }

        if (_traceBlocks)
            Trace($"SaveWriteFields  block='{SaveBlockCodec.NameText(name)}'  {count} field(s)  at cursor {block.Pos}");

        SaveFieldDesc[] descs = Fields(fields, stride, count);
        var cursor = new SaveCursor(block.Cursor, block.Available);
        int written = SaveBlockCodec.WriteFieldBlock(ref cursor, block.Symbols, name, pBase, descs, block.Level,
                                                     out int omitted);
        if (written < 0)
        {
            _log.Warning($"[SourceSharp] SaveWriteFields: block '{SaveBlockCodec.NameText(name)}' ({count} fields) did not fit -- "
                         + $"{block.Available} bytes left in a {block.BufferBytes}-byte buffer. The block is NOT in the save.\n");
            return false;
        }

        if (omitted > 0)
        {
            // Loud, and not a refusal of the block: each record carries its own
            // name and width, so an omitted field is one the reader never sees.
            // Every omitted field is named, not just the first.
            var names = new StringBuilder();
            foreach (SaveFieldDesc field in descs)
            {
                if (!SaveBlockCodec.IsOmitted(pBase, field))
                    continue;
                names.Append(names.Length > 0 ? ", " : "")
                     .Append(SaveBlockCodec.NameText(field.Name)).Append("(type ").Append(field.Type).Append(')');
            }
            _log.Warning($"[SourceSharp] SaveWriteFields: block '{SaveBlockCodec.NameText(name)}' is short {omitted} of {count} "
                         + $"field(s) -- {names}. This build does not encode those types (SaveBlockCodec); "
                         + "everything else in the block was written.\n");
        }

        block.Advance(written);
        return true;
    }

    /// <summary>SaveReadFields: the same block read back into the engine's struct, or refused whole.</summary>
    public bool ReadFields(void* save, byte* name, byte* pBase, byte* fields, int stride, int count)
    {
        var block = new SaveRestoreBlock(save);
        if (block.IsNull || name == null || pBase == null || fields == null || count < 0)
        {
            _log.Warning($"[SourceSharp] SaveReadFields: refused '{SaveBlockCodec.NameText(name)}' -- a null argument\n");
            return false;
        }

        if (_traceBlocks)
            Trace($"SaveReadFields   block='{SaveBlockCodec.NameText(name)}'  {count} field(s)  at cursor {block.Pos}");

        var cursor = new SaveCursor(block.Cursor, block.Available);
        int read = SaveBlockCodec.ReadFieldBlock(ref cursor, block.Symbols, name, pBase, Fields(fields, stride, count),
                                                 block.Level, out string? refusal);
        if (read < 0)
        {
            _log.Warning($"[SourceSharp] SaveReadFields: REFUSED block '{SaveBlockCodec.NameText(name)}' ({count} fields) -- "
                         + $"{refusal ?? "no reason given"}. The struct is left as the engine gave it rather than "
                         + $"half-filled (cursor {block.Pos}, {block.Available} bytes of save data left)\n");
            return false;
        }

        block.Advance(read);
        return true;
    }
}
