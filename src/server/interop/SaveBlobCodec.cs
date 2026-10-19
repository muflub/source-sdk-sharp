//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

//=============================================================================
// THE BLOB BLOCK, the server's alone (plan_server_native_v2 T4c): the managed
// world's entity bytes, framed as stock frames a block -- a count record under
// the block's name, then the payload in records of at most BlobChunk bytes --
// so the engine's own walk over the .sav passes it like any other block. The
// field block it shares the records with is SaveBlockCodec (shared interop).
//=============================================================================

/// <summary>The managed world's blob block, over <see cref="SaveBlockCodec"/>'s records.</summary>
public static unsafe class SaveBlobCodec
{
    /// <summary>The largest payload one blob record carries.</summary>
    public const int BlobChunk = 30000;

    /// <summary>The answer of a blob read that found a different block, or none, at the cursor.</summary>
    public const int NoBlock = -2;

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

        if (!SaveBlockCodec.WriteRecord(ref cursor, symbols, blockName, SaveBlockCodec.BitConverterBytes(chunks)))
            return -1;

        for (int at = 0; at < data.Length; at += BlobChunk)
        {
            int count = Math.Min(BlobChunk, data.Length - at);
            if (!SaveBlockCodec.WriteRecord(ref cursor, symbols, chunkName, data.Slice(at, count)))
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
