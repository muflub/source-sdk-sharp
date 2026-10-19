//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

//=============================================================================
// THE SERVER'S SAVE, on the engine's buffer (plan_server_native_v2 T4c).
//
// What shell_savegame.cpp did for server.so, decided here: SaveInit's buffer
// size and token count, the field blocks of the engine's own structs, and the
// one body block that carries the managed world. The engine still calls
// IServerGameDLL's save methods through server.so's vtable, and each of those
// is now one forward into IManagedSaveRestore; the engine still allocates the
// block and reads CSaveRestoreData by layout, so the layout stays declared in
// engineabi/saverestore.h (held to upstream by tools/abi-parity) and its
// offsets are SaveRestoreLayout's, each pinned by a fact here and a gtest
// there (save_restore_layout_test.cpp).
//=============================================================================

/// <summary>
/// The engine's save allocator (IVEngineServer::SaveAllocMemory /
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
/// The managed world's entity bytes: size, then fill (SaveCollect answers the
/// bytes the whole block needs and writes nothing unless all fit).
/// </summary>
public interface ISavedEntities
{
    /// <summary>The bytes needed (an empty destination sizes), -1 when they cannot be encoded.</summary>
    int Collect(Span<byte> destination);

    /// <summary>Entities restored from the bytes, -1 on a refusal.</summary>
    int Restore(ReadOnlySpan<byte> source);
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
/// IServerGameDLL's save cluster, decided in C#: SaveInit, SaveWriteFields,
/// SaveReadFields, and Save/Restore's one body block. Every refusal is loud,
/// because the engine abandons a save on a NULL SaveInit and prints nothing.
/// </summary>
public sealed unsafe class SaveGameSession
{
    /// <summary>
    /// The buffer when the engine passes 0, which it does at both SaveInit
    /// call sites (docs/managed-saverestore.md §4a): a measured save is a few
    /// KB, and the managed entity block grows with the world.
    /// </summary>
    public const int DefaultBufferBytes = 4 * 1024 * 1024;

    /// <summary>The token table's slots (stock's 0xfff).</summary>
    public const int TokenSlots = 0xfff;

    /// <summary>The managed world's body block.</summary>
    public const string EntityBlock = "SSENTITY";

    private readonly ILog _log;
    private readonly Func<ISaveMemory?> _memoryOf;

    // NUL-terminated names the token table holds BY POINTER: the engine reads
    // them back when it writes the table into the .sav, after the call that
    // interned them returned. Native memory, never freed: a save's table can
    // name them for as long as the engine holds the block, and it is three
    // short strings per module.
    private readonly byte* _entityBlock;
    private readonly byte* _blobChunk;
    private readonly byte* _unnamed;

    /// <summary>A session over the engine's allocator (null: every SaveInit refuses, loudly).</summary>
    public SaveGameSession(ILog log, ISaveMemory? memory)
        : this(log, () => memory)
    {
    }

    /// <summary>
    /// A session over the engine's allocator as it is when a save starts: the
    /// engine interface is asked at SaveInit, not when the module is built.
    /// </summary>
    public SaveGameSession(ILog log, Func<ISaveMemory?> memoryOf)
    {
        _log = log;
        _memoryOf = memoryOf;
        _entityBlock = NativeName(EntityBlock);
        _blobChunk = NativeName("BLOB");
        _unnamed = NativeName("<unnamed>");
    }

    private static byte* NativeName(string text)
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
        ISaveMemory? memory = _memoryOf();
        if (memory is null)
        {
            _log.Warning("[SourceSharp] SaveInit: no engine interface -- the save cannot be sized "
                         + "and will abort SILENTLY at the engine's call site\n");
            return null;
        }

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
            descs[i] = new SaveFieldDesc(name != null ? name : _unnamed,
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

        Trace($"SaveWriteFields  block='{SaveBlockCodec.NameText(name)}'  {count} field(s)  at cursor {block.Pos}");

        SaveFieldDesc[] descs = Fields(fields, stride, count);
        var cursor = new SaveCursor(block.Cursor, block.Available);
        int written = SaveBlockCodec.WriteFieldBlock(ref cursor, block.Symbols, name, pBase, descs, block.BaseTime,
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
                if (SaveFieldCodec.Bytes(field.Type, field.Size) >= 0)
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

        Trace($"SaveReadFields   block='{SaveBlockCodec.NameText(name)}'  {count} field(s)  at cursor {block.Pos}");

        var cursor = new SaveCursor(block.Cursor, block.Available);
        int read = SaveBlockCodec.ReadFieldBlock(ref cursor, block.Symbols, name, pBase, Fields(fields, stride, count),
                                                 block.BaseTime, out string? refusal);
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

    /// <summary>
    /// Save's one body block: the managed world, sized, filled and framed as
    /// <see cref="EntityBlock"/>. False refuses the block (named here); a
    /// world with nothing to save writes nothing and is true.
    /// </summary>
    public bool WriteEntities(void* save, ISavedEntities entities)
    {
        var block = new SaveRestoreBlock(save);
        if (block.IsNull)
        {
            Trace("Save  -- no save block; 0 entity bytes");
            return true;
        }

        int before = block.Pos;
        int bytes = entities.Collect(Span<byte>.Empty);
        if (bytes < 0)
        {
            // Refuses the WHOLE block rather than dropping one record: a save
            // that silently omits an entity is the failure that survives to the load.
            _log.Warning("[SourceSharp] save: the managed side refused to encode its entities. No entity block is "
                         + "written, and the .sav will restore no managed state.\n");
            return false;
        }

        if (bytes == 0)
        {
            Trace("Save  -- 0 managed entity bytes (nothing live)");
            return true;
        }

        var payload = new byte[bytes];
        int filled = entities.Collect(payload);
        if (filled != bytes)
        {
            _log.Warning($"[SourceSharp] save: SaveCollect sized {bytes} bytes and then wrote {filled}. "
                         + "Nothing is written -- the two calls must agree.\n");
            return false;
        }

        var cursor = new SaveCursor(block.Cursor, block.Available);
        int written = SaveBlockCodec.WriteBlobBlock(ref cursor, block.Symbols, _entityBlock, _blobChunk, payload);
        if (written < 0)
        {
            _log.Warning($"[SourceSharp] save: blob block '{EntityBlock}' ({bytes} payload bytes) did not fit -- "
                         + $"{block.Available} bytes left in a {block.BufferBytes}-byte buffer. The block is NOT in "
                         + "the save, and neither is anything after it.\n");
            return false;
        }

        block.Advance(written);
        Trace($"Save  -- managed entity block '{EntityBlock}': {bytes} payload bytes in {written} block bytes "
              + $"(cursor {before} -> {block.Pos})");
        return true;
    }

    /// <summary>
    /// Restore's body block: the managed world laid over the entities the map
    /// already spawned. The entities restored, <see cref="SaveBlockCodec.NoBlock"/>
    /// for a save without the block (the map's own state is all there is), -1
    /// for a block that is there and corrupt.
    /// </summary>
    public int ReadEntities(void* save, ISavedEntities entities)
    {
        var block = new SaveRestoreBlock(save);
        if (block.IsNull)
            return SaveBlockCodec.NoBlock;

        int before = block.Pos;
        var sizing = new SaveCursor(block.Cursor, block.Available);
        int payload = SaveBlockCodec.ReadBlobBlock(ref sizing, block.Symbols, _entityBlock, Span<byte>.Empty,
                                                   out string? refusal);
        if (payload == SaveBlockCodec.NoBlock)
        {
            Trace("Restore -- this save carries no managed entity block; the map's own state is all there is");
            return SaveBlockCodec.NoBlock;
        }

        if (payload < 0)
        {
            _log.Warning($"[SourceSharp] load: REFUSED blob block '{EntityBlock}' -- {refusal ?? "no reason given"}. "
                         + $"Nothing was read (cursor {before}, {block.Available} bytes of save data left)\n");
            return -1;
        }

        var bytes = new byte[payload];
        var cursor = new SaveCursor(block.Cursor, block.Available);
        int read = SaveBlockCodec.ReadBlobBlock(ref cursor, block.Symbols, _entityBlock, bytes, out _);
        if (read != payload)
        {
            _log.Warning($"[SourceSharp] load: the managed entity block sized {payload} bytes and read {read}. "
                         + "NOTHING has been restored.\n");
            return -1;
        }

        block.Advance(cursor.Pos);
        int restored = payload == 0 ? 0 : entities.Restore(bytes);
        Trace($"Restore -- {restored} managed entities restored (cursor {before} -> {block.Pos})");
        return restored;
    }
}
