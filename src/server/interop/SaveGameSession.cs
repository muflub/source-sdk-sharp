//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

//=============================================================================
// THE SERVER'S SAVE, on the engine's buffer (plan_server_native_v2 T4c).
//
// What shell_savegame.cpp did for server.so, decided here: the field blocks
// of the engine's own structs, which are SaveBlockSession's (shared interop,
// the client's too), and the one body block that carries the managed world,
// which is the server's alone. The engine still calls IServerGameDLL's save
// methods through server.so's vtable, and each of those is now one forward
// into IManagedSaveRestore.
//=============================================================================

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
/// IServerGameDLL's save cluster, decided in C#: SaveInit, SaveWriteFields,
/// SaveReadFields, and Save/Restore's one body block. Every refusal is loud,
/// because the engine abandons a save on a NULL SaveInit and prints nothing.
/// </summary>
public sealed unsafe class SaveGameSession
{
    /// <summary>The managed world's body block.</summary>
    public const string EntityBlock = "SSENTITY";

    private readonly ILog _log;
    private readonly ISaveMemory _memory;

    // The block session, made on first use over the session's allocator (a
    // constructor only stores, by the interop layer's rule).
    private SaveBlockSession? _blocks;

    private SaveBlockSession Blocks => _blocks ??= new SaveBlockSession(_log, _memory, traceBlocks: true);

    // NUL-terminated names the token table holds BY POINTER: the engine reads
    // them back when it writes the table into the .sav, after the call that
    // interned them returned. Native memory, never freed: a save's table can
    // name them for as long as the engine holds the block, and it is two
    // short strings per module.
    private byte* _entityBlock;
    private byte* _blobChunk;

    private byte* EntityBlockName => _entityBlock != null ? _entityBlock : _entityBlock = SaveBlockSession.NativeName(EntityBlock);

    private byte* BlobChunkName => _blobChunk != null ? _blobChunk : _blobChunk = SaveBlockSession.NativeName("BLOB");

    /// <summary>
    /// A session over the engine's allocator: the side's registered one, which
    /// asks the engine at its first call (at SaveInit), not when the module is built.
    /// </summary>
    public SaveGameSession(ILog log, ISaveMemory memory)
    {
        _log = log;
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>SaveInit (<see cref="SaveBlockSession.Init"/>).</summary>
    public void* Init(int size, float curTime) => Blocks.Init(size, curTime);

    /// <summary>SaveWriteFields (<see cref="SaveBlockSession.WriteFields"/>).</summary>
    public bool WriteFields(void* save, byte* name, byte* pBase, byte* fields, int stride, int count)
        => Blocks.WriteFields(save, name, pBase, fields, stride, count);

    /// <summary>SaveReadFields (<see cref="SaveBlockSession.ReadFields"/>).</summary>
    public bool ReadFields(void* save, byte* name, byte* pBase, byte* fields, int stride, int count)
        => Blocks.ReadFields(save, name, pBase, fields, stride, count);

    private void Trace(string text) => _log.Msg("[SourceSharp][save] " + text + "\n");

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
        int written = SaveBlobCodec.WriteBlobBlock(ref cursor, block.Symbols, EntityBlockName, BlobChunkName, payload);
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
    /// already spawned. The entities restored, <see cref="SaveBlobCodec.NoBlock"/>
    /// for a save without the block (the map's own state is all there is), -1
    /// for a block that is there and corrupt.
    /// </summary>
    public int ReadEntities(void* save, ISavedEntities entities)
    {
        var block = new SaveRestoreBlock(save);
        if (block.IsNull)
            return SaveBlobCodec.NoBlock;

        int before = block.Pos;
        var sizing = new SaveCursor(block.Cursor, block.Available);
        int payload = SaveBlobCodec.ReadBlobBlock(ref sizing, block.Symbols, EntityBlockName, Span<byte>.Empty,
                                                   out string? refusal);
        if (payload == SaveBlobCodec.NoBlock)
        {
            Trace("Restore -- this save carries no managed entity block; the map's own state is all there is");
            return SaveBlobCodec.NoBlock;
        }

        if (payload < 0)
        {
            _log.Warning($"[SourceSharp] load: REFUSED blob block '{EntityBlock}' -- {refusal ?? "no reason given"}. "
                         + $"Nothing was read (cursor {before}, {block.Available} bytes of save data left)\n");
            return -1;
        }

        var bytes = new byte[payload];
        var cursor = new SaveCursor(block.Cursor, block.Available);
        int read = SaveBlobCodec.ReadBlobBlock(ref cursor, block.Symbols, EntityBlockName, bytes, out _);
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
