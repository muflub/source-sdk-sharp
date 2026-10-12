//========= SourceSharp — the model cache, as the bone drivers read it =========//
//
// plan physics_bones_native_zero step 1.2: the studio header handoff. The
// bone drivers ask IBoneModels for a StudioHdr by model index; this answers
// from the engine's model cache, copied as bytes (IStudioMemory), folded and
// validated once per model per level.
//
// THE FOLD OF THE ANIMATION BLOCKS. StudioHdr reads a block > 0 at
// `datastart + index` of the image that owns the animdesc, as if the image
// were the .ani file; the engine pages each block into a buffer of its own.
// StudioImageFold makes the two agree without teaching the reader a second
// image: the blocks are appended after the header and the copy's
// mstudioanimblock_t table is rebased onto them. A block the engine could not
// produce gets the entry (0, 0), which StudioHdr.PAnimBlock answers null for --
// the answer stock gets when GetAnimBlock returns NULL. Only the private copy
// is rewritten; the engine's header is never touched.
//
// INCLUDES are folded by StudioHdr.TryCreate itself through the resolver, each
// include's own blocks folded the same way. Images are cached by model index,
// includes by name; both go at Clear (level shutdown, model-cache flush). A
// model that answers no header is NOT remembered: the engine can be mid-load,
// and the next ask must ask again.
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Shared.Bones;

/// <summary>The animation-block fold (see the file header).</summary>
public static class StudioImageFold
{
    private const int LengthAt = 76;
    private const int NumAnimBlocksAt = 352;
    private const int AnimBlockIndexAt = 356;

    /// <summary>
    /// <paramref name="mdl"/> with every animation block appended and its
    /// block table rebased; <paramref name="mdl"/> itself when it declares no
    /// block beyond 0. <paramref name="block"/> answers one block's bytes, or
    /// null; an answer of the wrong length counts as null.
    /// </summary>
    public static byte[] Fold(byte[] mdl, Func<int, byte[]?> block)
    {
        ArgumentNullException.ThrowIfNull(mdl);
        ArgumentNullException.ThrowIfNull(block);

        if (mdl.Length < AnimBlockIndexAt + 4)
        {
            return mdl;
        }

        int length = ReadI32(mdl, LengthAt);
        int count = ReadI32(mdl, NumAnimBlocksAt);
        int table = ReadI32(mdl, AnimBlockIndexAt);

        if (count <= 1 || table <= 0 || length <= 0 || length > mdl.Length
            || (long)table + 8L * count > length)
        {
            return mdl;
        }

        var blocks = new byte[]?[count];
        long space = 0;

        for (int b = 1; b < count; b++)
        {
            int start = ReadI32(mdl, table + 8 * b);
            int end = ReadI32(mdl, table + 8 * b + 4);

            if (start < 0 || end <= start)
            {
                continue;
            }

            byte[]? bytes = block(b);

            if (bytes is null || bytes.Length != end - start)
            {
                continue;
            }

            blocks[b] = bytes;
            space = Math.Max(space, end);
        }

        if ((long)length + space > int.MaxValue)
        {
            return mdl;
        }

        var image = new byte[length + space];
        Buffer.BlockCopy(mdl, 0, image, 0, length);

        for (int b = 1; b < count; b++)
        {
            int at = table + 8 * b;

            if (blocks[b] is not { } bytes)
            {
                // Missing: the reader's "no block" answer, never a read of
                // whatever the header held at those offsets.
                WriteI32(image, at, 0);
                WriteI32(image, at + 4, 0);
                continue;
            }

            int start = ReadI32(mdl, at);
            int end = ReadI32(mdl, at + 4);

            Buffer.BlockCopy(bytes, 0, image, length + start, bytes.Length);
            WriteI32(image, at, length + start);
            WriteI32(image, at + 4, length + end);
        }

        return image;
    }

    private static int ReadI32(byte[] image, int at) => BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at, 4));

    private static void WriteI32(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at, 4), value);
}

/// <summary>
/// <see cref="IBoneModels"/> over a side's model cache: one folded, validated
/// <see cref="StudioHdr"/> per model index, kept until <see cref="Clear"/>.
/// </summary>
public sealed class StudioModels : IBoneModels
{
    private readonly IStudioMemory _memory;
    private readonly Dictionary<int, Entry> _models = [];
    private readonly Dictionary<string, byte[]?> _includes = new(StringComparer.Ordinal);

    private sealed class Entry(StudioHdr hdr, string name)
    {
        public StudioHdr Hdr { get; } = hdr;
        public string Name { get; } = name;
    }

    public StudioModels(IStudioMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>How many models are held.</summary>
    public int Count => _models.Count;

    /// <summary>How many images were read from the engine (models and includes),
    /// for the cost report.</summary>
    public int ImagesRead { get; private set; }

    /// <summary>How many bytes those images came to, folded.</summary>
    public long BytesRead { get; private set; }

    /// <inheritdoc/>
    public bool HasStudioModel(int modelIndex) => OpenStudioHdr(modelIndex) is not null;

    /// <inheritdoc/>
    public StudioHdr? OpenStudioHdr(int modelIndex) => Open(modelIndex)?.Hdr;

    /// <summary>
    /// <c>studiohdr_t::name</c> of the model at <paramref name="modelIndex"/>
    /// (the header's own name, not the path it was loaded from), or empty.
    /// </summary>
    public string StudioName(int modelIndex) => Open(modelIndex)?.Name ?? string.Empty;

    /// <summary>Every model and include goes: the level ended, or the engine
    /// flushed the studio data the images were copied from.</summary>
    public void Clear()
    {
        _models.Clear();
        _includes.Clear();
    }

    private Entry? Open(int modelIndex)
    {
        if (_models.TryGetValue(modelIndex, out Entry? held))
        {
            return held;
        }

        byte[]? mdl = _memory.ModelImage(modelIndex);

        if (mdl is null)
        {
            return null;
        }

        byte[] image = StudioImageFold.Fold(mdl, b => _memory.ModelAnimBlock(modelIndex, b));
        Note(image);

        if (!StudioHdr.TryCreate(image, name => Include(modelIndex, name), out StudioHdr? hdr) || hdr is null)
        {
            return null;
        }

        var entry = new Entry(hdr, HeaderName(mdl));
        _models.Add(modelIndex, entry);
        return entry;
    }

    private byte[]? Include(int modelIndex, string name)
    {
        if (_includes.TryGetValue(name, out byte[]? held))
        {
            return held;
        }

        byte[]? mdl = _memory.IncludeImage(modelIndex, name);
        byte[]? image = mdl is null
            ? null
            : StudioImageFold.Fold(mdl, b => _memory.IncludeAnimBlock(modelIndex, name, b));

        if (image is not null)
        {
            Note(image);
        }

        // A miss is remembered for the level: an include the engine cannot
        // find by name now will not be found by asking again this level, and
        // the model that names it is refused either way.
        _includes.Add(name, image);
        return image;
    }

    private void Note(byte[] image)
    {
        ImagesRead++;
        BytesRead += image.Length;
    }

    // studiohdr_t::name, char[64] at @12.
    private static string HeaderName(byte[] mdl)
    {
        ReadOnlySpan<byte> name = mdl.AsSpan(12, 64);
        int end = name.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? name : name[..end]);
    }
}
