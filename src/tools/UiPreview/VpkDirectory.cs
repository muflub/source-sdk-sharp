//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace SourceSharp.UiPreview;

/// <summary>
/// A VPK (version 1 or 2) read by its directory file, for the offline preview:
/// the files a mod's gameinfo.txt mounts from an archive, as the game sees
/// them. The directory's parse is source-sdk-map-tools' VpkArchive, without
/// its file-system layer: the archive parts are files beside the directory.
/// </summary>
public sealed class VpkDirectory
{
    /// <summary>The four bytes that begin every VPK.</summary>
    public const uint Marker = 0x55aa1234;

    /// <summary>The archive index meaning "in the directory file itself".</summary>
    public const int EmbeddedArchiveIndex = 0x7fff;

    private const ushort PartListTerminator = 0xffff;
    private const string DirectorySuffix = "_dir.vpk";

    private readonly string _path;
    private readonly string _baseName;
    private readonly long _embeddedBase;
    private readonly Dictionary<string, Entry> _entries;

    private sealed record Part(int ArchiveIndex, uint Offset, uint Length);

    private sealed record Entry(byte[] Preload, List<Part> Parts);

    private VpkDirectory(string path, long embeddedBase, Dictionary<string, Entry> entries)
    {
        _path = path;
        _baseName = path.EndsWith(DirectorySuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^DirectorySuffix.Length]
            : path[..^".vpk".Length];
        _embeddedBase = embeddedBase;
        _entries = entries;
    }

    /// <summary>How many files the archive holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Opens a VPK by its directory file. A malformed one throws <see cref="InvalidDataException"/> naming why.</summary>
    public static VpkDirectory Open(string directoryPath) => Parse(directoryPath, File.ReadAllBytes(directoryPath));

    /// <summary>Whether the archive holds <paramref name="path"/> (forward slashes; case does not matter).</summary>
    public bool Contains(string path) => _entries.ContainsKey(Key(path));

    /// <summary>A file's bytes, or null when the archive does not hold it.</summary>
    public byte[]? Read(string path)
    {
        if (!_entries.TryGetValue(Key(path), out Entry? entry))
            return null;

        long total = entry.Preload.Length + entry.Parts.Sum(p => (long)p.Length);
        var bytes = new byte[total];
        entry.Preload.CopyTo(bytes, 0);
        int at = entry.Preload.Length;
        foreach (Part part in entry.Parts)
        {
            string file = part.ArchiveIndex == EmbeddedArchiveIndex
                ? _path
                : $"{_baseName}_{part.ArchiveIndex.ToString("D3", CultureInfo.InvariantCulture)}.vpk";
            long baseOffset = part.ArchiveIndex == EmbeddedArchiveIndex ? _embeddedBase : 0;
            using var stream = File.OpenRead(file);
            stream.Seek(baseOffset + part.Offset, SeekOrigin.Begin);
            try
            {
                stream.ReadExactly(bytes, at, (int)part.Length);
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException(
                    $"{_path}: {path} claims {part.Length} bytes at offset {part.Offset} of {file}, which came up short", error);
            }
            at += (int)part.Length;
        }
        return bytes;
    }

    private static string Key(string path) => path.Replace('\\', '/').ToLowerInvariant();

    internal static VpkDirectory Parse(string directoryPath, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            throw new InvalidDataException($"{directoryPath} is {bytes.Length} bytes, too short for a VPK header");

        uint marker = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (marker != Marker)
            throw new InvalidDataException($"{directoryPath}: expected the marker 0x{Marker:x8}, found 0x{marker:x8}");

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int directorySize = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int headerSize = version switch
        {
            1 => 12,
            2 => 28,
            _ => throw new InvalidDataException($"{directoryPath}: version {version} is not a VPK version"),
        };

        if (bytes.Length < headerSize)
            throw new InvalidDataException($"{directoryPath} is {bytes.Length} bytes, too short for a version {version} header");
        if (directorySize < 0 || headerSize + (long)directorySize > bytes.Length)
            throw new InvalidDataException(
                $"{directoryPath}: the directory claims {directorySize} bytes, which runs past the end of a {bytes.Length}-byte file");

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        ReadDirectory(directoryPath, bytes.Slice(headerSize, directorySize), entries);
        return new VpkDirectory(directoryPath, headerSize + (long)directorySize, entries);
    }

    // extension \0 { directory \0 { name \0 <fixed> } }, each level ended by an
    // empty string; the path is "<directory>/<name>.<extension>", the directory
    // spelled " " for the archive's root.
    private static void ReadDirectory(string directoryPath, ReadOnlySpan<byte> directory, Dictionary<string, Entry> entries)
    {
        int offset = 0;
        while (ReadString(directoryPath, directory, ref offset) is { Length: > 0 } extension)
        {
            while (ReadString(directoryPath, directory, ref offset) is { Length: > 0 } folder)
            {
                while (ReadString(directoryPath, directory, ref offset) is { Length: > 0 } name)
                {
                    string leaf = extension is " " ? name : name + "." + extension;
                    string path = folder is " " ? leaf : folder + "/" + leaf;
                    // Last spelling wins, as the engine overwrites a duplicate.
                    entries[Key(path)] = ReadEntry(directoryPath, directory, ref offset);
                }
            }
        }
    }

    private static Entry ReadEntry(string directoryPath, ReadOnlySpan<byte> directory, ref int offset)
    {
        Need(directoryPath, directory, offset, 6);
        int preloadSize = BinaryPrimitives.ReadUInt16LittleEndian(directory[(offset + 4)..]);
        offset += 6;

        var parts = new List<Part>();
        while (true)
        {
            Need(directoryPath, directory, offset, 2);
            ushort archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian(directory[offset..]);
            if (archiveIndex == PartListTerminator)
            {
                offset += 2;
                break;
            }

            Need(directoryPath, directory, offset, 10);
            uint partOffset = BinaryPrimitives.ReadUInt32LittleEndian(directory[(offset + 2)..]);
            uint partLength = BinaryPrimitives.ReadUInt32LittleEndian(directory[(offset + 6)..]);
            offset += 10;
            parts.Add(new Part(archiveIndex, partOffset, partLength));
        }

        Need(directoryPath, directory, offset, preloadSize);
        byte[] preload = directory.Slice(offset, preloadSize).ToArray();
        offset += preloadSize;
        return new Entry(preload, parts);
    }

    // Latin-1 rather than UTF-8: the engine compares these bytes with strcmp,
    // and decoding a non-UTF-8 byte to U+FFFD would merge two different names.
    private static string ReadString(string directoryPath, ReadOnlySpan<byte> directory, ref int offset)
    {
        int end = directory[offset..].IndexOf((byte)0);
        if (end < 0)
            throw new InvalidDataException(
                $"{directoryPath}: a name at offset {offset} runs to the end of the directory with no terminator");
        string value = Encoding.Latin1.GetString(directory.Slice(offset, end));
        offset += end + 1;
        return value;
    }

    private static void Need(string directoryPath, ReadOnlySpan<byte> directory, int offset, int bytes)
    {
        if (offset + bytes > directory.Length)
            throw new InvalidDataException(
                $"{directoryPath}: an entry at offset {offset} needs {bytes} bytes and the directory has {directory.Length - offset} left");
    }
}
