//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;
using System.Globalization;

using SourceSharp.Abi;

namespace TF2.Server.Voice;

/// <summary>
/// The scene image: every compiled choreography (.vcd) of the mounted game,
/// one file — <c>scenes/scenes.image</c>. This port plays no scenes, but a
/// scene's two cached answers are what a spoken line needs, and the image
/// carries both per scene without a scene parser: its duration, and the sound
/// names of its speak events.
///
/// <para>
/// The reader is the read half of the engine's scene file cache
/// (<c>scenefilecache/SceneFileCache.cpp</c> over the
/// <c>SceneImageHeader_t</c>/<c>SceneImageEntry_t</c>/<c>SceneImageSummary_t</c>
/// layouts, public/scenefilecache/SceneImageFile.h): the header's id and
/// version, the entry table at <c>nSceneEntryOffset</c> sorted by the CRC32 of
/// the scene's normalized name — lower case, backslashes, the <c>.vcd</c>
/// extension forced (<c>V_strlower</c>/<c>V_FixSlashes</c>/<c>V_SetExtension</c>,
/// <c>FindSceneInImage</c>) — binary-searched the same way, and per entry a
/// summary whose <c>msecs</c> is the duration and whose <c>soundStrings</c>
/// index the string table that follows the header
/// (<c>GetSceneCachedData</c>/<c>GetSceneCachedSound</c>/<c>GetSceneString</c>).
/// </para>
///
/// <para>
/// The one divergence is scope: the engine's cache also hands out compiled
/// scene bodies for playback, which no scene system here consumes; only the
/// summary is read, and the whole image is parsed once on first use.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFSceneImage
{
    /// <summary>The image, as the engine's <c>pSceneImageName</c> names it.</summary>
    public const string SceneImageFile = "scenes/scenes.image";

    /// <summary>SCENE_IMAGE_ID, 'VSIF'.</summary>
    public const int ImageId = ('V' << 0) | ('S' << 8) | ('I' << 16) | ('F' << 24);

    /// <summary>SCENE_IMAGE_VERSION.</summary>
    public const int ImageVersion = 2;

    private readonly IEngineFiles _files;

    private byte[]? _image;

    private int _entryOffset;

    private int _sceneCount;

    private uint[] _stringOffsets = [];

    public TFSceneImage(IEngineFiles files)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>How many scenes the loaded image holds; 0 before a load and for a missing one.</summary>
    public int SceneCount
    {
        get
        {
            Load();
            return _sceneCount;
        }
    }

    /// <summary>
    /// The scene's sounds, in the image's order — the summary's
    /// <c>soundStrings</c> through the string table. Empty when the image is
    /// absent, malformed, or names no such scene.
    /// </summary>
    public IReadOnlyList<string> SceneSounds(string sceneFile)
    {
        if (Locate(sceneFile) is not { } entry)
            return [];

        int summary = entry.SummaryOffset;
        if (summary + 8 > _image!.Length)
            return [];

        int count = BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(summary + 4, 4));
        if (count < 0 || summary + 8 + 2 * count > _image.Length)
            return [];

        var sounds = new string[count];
        for (int i = 0; i < count; i++)
        {
            short id = BinaryPrimitives.ReadInt16LittleEndian(_image.AsSpan(summary + 8 + 2 * i, 2));
            sounds[i] = String(id);
        }

        return sounds;
    }

    /// <summary>
    /// The scene's length, its summary's <c>msecs</c> in seconds — the engine's
    /// <c>GetSceneCachedData</c>. 0 when the image is absent, malformed, or
    /// names no such scene.
    /// </summary>
    public float SceneDuration(string sceneFile)
    {
        if (Locate(sceneFile) is not { } entry || entry.SummaryOffset + 8 > _image!.Length)
            return 0f;

        int msecs = BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(entry.SummaryOffset, 4));
        return Math.Max(msecs, 0) / 1000f;
    }

    /// <summary>
    /// The engine's <c>FindSceneInImage</c> binary search, whose lower bound
    /// starts at entry 1, not 0 — the engine's own loop, kept.
    /// </summary>
    private SceneEntry? Locate(string sceneFile)
    {
        Load();
        if (_image is null || _sceneCount == 0)
            return null;

        uint crc = Crc32.NormalizedSceneName(sceneFile);
        int lower = 1;
        int upper = _sceneCount;
        while (true)
        {
            if (upper < lower)
                return null;

            int middle = (lower + upper) / 2;
            int at = _entryOffset + 16 * (middle - 1);
            if (at + 16 > _image.Length)
                return null;

            uint probe = BinaryPrimitives.ReadUInt32LittleEndian(_image.AsSpan(at, 4));
            if (crc < probe)
            {
                upper = middle - 1;
            }
            else if (crc > probe)
            {
                lower = middle + 1;
            }
            else
            {
                int offset = BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(at + 4, 4));
                int summary = BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(at + 12, 4));
                return new SceneEntry(offset, summary);
            }
        }
    }

    /// <summary>The engine's <c>SceneImageHeader_t::String</c>.</summary>
    private string String(short id)
    {
        if (id < 0 || id >= _stringOffsets.Length || _image is null)
            return string.Empty;

        int at = (int)_stringOffsets[id];
        if (at < 0 || at >= _image.Length)
            return string.Empty;

        int end = at;
        while (end < _image.Length && _image[end] != 0)
            end++;

        return System.Text.Encoding.ASCII.GetString(_image, at, end - at);
    }

    private void Load()
    {
        if (_image is not null)
            return;

        byte[]? image = _files.ReadGameFileBytes(SceneImageFile);
        if (image is null || image.Length < 20)
            return;

        if (BinaryPrimitives.ReadInt32LittleEndian(image) != ImageId
            || BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4, 4)) != ImageVersion)
        {
            return;
        }

        int sceneCount = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8, 4));
        int stringCount = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(12, 4));
        int entryOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(16, 4));
        if (sceneCount < 0 || stringCount < 0 || entryOffset < 0 || entryOffset + 16 * sceneCount > image.Length)
            return;

        // The string table: stringCount offsets after the 20-byte header, each
        // an absolute offset into the image — the pool sits wherever the
        // compiler laid it, and the offset says where.
        int tableAt = 20;
        if (tableAt + 4 * stringCount > image.Length)
            return;

        var offsets = new uint[stringCount];
        for (int i = 0; i < stringCount; i++)
        {
            int at = tableAt + 4 * i;
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at, 4));
        }

        _image = image;
        _sceneCount = sceneCount;
        _entryOffset = entryOffset;
        _stringOffsets = offsets;
    }

    private readonly record struct SceneEntry(int DataOffset, int SummaryOffset);

    /// <summary>The CRC32 the image's entries are keyed by, over the normalized name.</summary>
    public static class Crc32
    {
        private const uint Polynomial = 0xEDB8_8320u;

        /// <summary>
        /// The entry key: lower case, backslashes, always ending <c>.vcd</c> —
        /// the engine's normalize before <c>CRC32_ProcessSingleBuffer</c>.
        /// ASCII, as a scene name is; anything wider folds as the engine's
        /// char copy does.
        /// </summary>
        public static uint NormalizedSceneName(string sceneFile)
        {
            Span<char> name = stackalloc char[260];
            int length = Math.Min(sceneFile.Length, name.Length - 5);
            for (int i = 0; i < length; i++)
            {
                char c = sceneFile[i];
                name[i] = c == '/' ? '\\' : char.ToLower(c, CultureInfo.InvariantCulture);
            }

            const string Extension = ".vcd";
            if (length < 4 || !name.Slice(length - 4, 4).SequenceEqual(Extension))
            {
                Extension.CopyTo(name[length..]);
                length += Extension.Length;
            }

            return Of(name[..length]);
        }

        /// <summary>CRC-32, bitwise: one lookup per voice command cannot carry a static table (SS0010).</summary>
        public static uint Of(ReadOnlySpan<char> text)
        {
            uint crc = 0xFFFF_FFFFu;
            for (int i = 0; i < text.Length; i++)
            {
                crc ^= text[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? Polynomial ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc ^ 0xFFFF_FFFFu;
        }
    }
}
