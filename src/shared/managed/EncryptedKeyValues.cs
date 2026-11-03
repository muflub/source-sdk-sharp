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
/// Stock's <c>ReadEncryptedKVFile</c> (<c>shared/weapon_parse.cpp</c>): a game
/// script named without its extension is read as <c>&lt;name&gt;.txt</c> if
/// that exists, else as <c>&lt;name&gt;.ctx</c> decrypted with ICE level 0
/// (<see cref="DecodeIce"/>, stock's <c>UTIL_DecodeICE</c>), then parsed as
/// KeyValues text. Shipped TF2 content has only the <c>.ctx</c> form of its
/// class and weapon scripts; a loose <c>.txt</c> overrides one, as in stock.
///
/// <para>
/// One reader per key: it builds its <see cref="IceKey"/> once and reuses it
/// for every file it reads. Files come through the GAME search path
/// (<see cref="IEngineFiles.ReadGameFileBytes"/>); stock reads MOD when it has
/// a key (docs/divergences.md).
/// </para>
/// </summary>
public sealed class EncryptedKeyValues
{
    private readonly IEngineFiles _files;
    private readonly IceKey _ice = new(0);

    /// <param name="files">This side's game file reads.</param>
    /// <param name="iceKey">The 8-byte ICE level 0 key the mod's <c>.ctx</c> files are sealed with.</param>
    public EncryptedKeyValues(IEngineFiles files, ReadOnlySpan<byte> iceKey)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _ice.Set(iceKey);
    }

    /// <summary>
    /// Reads and parses <paramref name="nameWithoutExtension"/>: the
    /// <c>.txt</c> first, then the decrypted <c>.ctx</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">Neither file is on the GAME path; the message names both.</exception>
    /// <exception cref="KeyValuesTextException">The text is not KeyValues.</exception>
    public KeyValuesText Read(string nameWithoutExtension)
    {
        if (string.IsNullOrEmpty(nameWithoutExtension) || Path.HasExtension(nameWithoutExtension))
            throw new ArgumentException(
                $"'{nameWithoutExtension}' must name a script without its extension", nameof(nameWithoutExtension));

        string txt = nameWithoutExtension + ".txt";
        if (_files.ReadGameFileBytes(txt) is { } plain)
            return KeyValuesText.Parse(Text(plain));

        string ctx = nameWithoutExtension + ".ctx";
        if (_files.ReadGameFileBytes(ctx) is { } sealedBytes)
        {
            // Decrypted into a copy: the read's array is not ours to change.
            byte[] opened = sealedBytes.AsSpan().ToArray();
            DecodeIce(_ice, opened);
            return KeyValuesText.Parse(Text(opened));
        }

        throw new FileNotFoundException($"neither {txt} nor {ctx} is on the GAME path", nameWithoutExtension);
    }

    /// <summary>
    /// <c>UTIL_DecodeICE</c> (<c>shared/util_shared.cpp</c>): decrypts every whole
    /// block of <paramref name="buffer"/> in place; a tail shorter than a block
    /// is left exactly as read.
    /// </summary>
    public static void DecodeIce(IceKey ice, Span<byte> buffer)
    {
        int block = ice.BlockSize;
        Span<byte> plain = stackalloc byte[block];

        for (int at = 0; at + block <= buffer.Length; at += block)
        {
            Span<byte> sealedBlock = buffer.Slice(at, block);
            ice.Decrypt(sealedBlock, plain);
            plain.CopyTo(sealedBlock);
        }
    }

    // Stock NUL-terminates the buffer and hands KeyValues a C string, so the
    // text ends at the first NUL byte.
    private static string Text(byte[] bytes)
    {
        int end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
