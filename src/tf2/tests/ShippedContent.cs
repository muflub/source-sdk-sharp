using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using SourceSharp.Tests;

namespace TF2.Tests;

/// <summary>
/// The shipped TF2 content as the engine's filesystem hands it: the Steam
/// install linked under <c>bin/&lt;config&gt;/content/tf</c>, read through the
/// same GAME-path lookup (the packs first, then the loose directory). For the
/// facts that read real models (<see cref="NotOnCiFactAttribute"/>: CI has no
/// install); a model that is not there is a failure, never a pass.
/// </summary>
internal static class ShippedContent
{
    /// <summary>The <c>tf</c> content directory.</summary>
    public static string Dir => RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf");

    // GAME-path lookup, the way gameinfo mounts it: the loose directory
    // second, the archive packs first (models are packed; the loose tf/models
    // holds only props_movies). A part is a file beside the directory file.
    public static byte[]? ReadModel(string contentDir, string path)
    {
        string loose = Path.Combine(contentDir, path.Replace('\\', '/'));
        foreach (string pack in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string dir = Path.Combine(contentDir, pack);
            if (File.Exists(dir) && FromDirVpk(dir, path) is { } packed)
                return packed;
        }
        return File.Exists(loose) ? File.ReadAllBytes(loose) : null;
    }

    private static byte[]? FromDirVpk(string dirVpk, string path)
    {
        byte[] image = File.ReadAllBytes(dirVpk);
        if (image.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(image) != 0x55aa1234)
            return null;
        int version = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4));
        int dirSize = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8));
        int headerSize = version == 1 ? 12 : version == 2 ? 28 : 0;
        if (headerSize == 0 || dirSize < 0 || headerSize + (long)dirSize > image.Length)
            return null;

        ReadOnlySpan<byte> directory = image.AsSpan(headerSize, dirSize);
        string want = path.Replace('\\', '/').ToLowerInvariant();
        string baseName = dirVpk.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)
            ? dirVpk[..^"_dir.vpk".Length]
            : dirVpk[..^".vpk".Length];

        int off = 0;
        while (ReadString(directory, ref off) is { Length: > 0 } extension)
        {
            while (ReadString(directory, ref off) is { Length: > 0 } folder)
            {
                while (ReadString(directory, ref off) is { Length: > 0 } name)
                {
                    string leaf = extension == " " ? name : name + "." + extension;
                    string full = folder == " " ? leaf : folder + "/" + leaf;

                    Need(directory, off, 6);
                    int preloadSize = BinaryPrimitives.ReadUInt16LittleEndian(directory[(off + 4)..]);
                    off += 6;

                    var parts = new List<(int Archive, uint Offset, uint Length)>();
                    while (true)
                    {
                        Need(directory, off, 2);
                        ushort archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian(directory[off..]);
                        if (archiveIndex == 0xffff)
                        {
                            off += 2;
                            break;
                        }
                        Need(directory, off, 10);
                        parts.Add((archiveIndex,
                                   BinaryPrimitives.ReadUInt32LittleEndian(directory[(off + 2)..]),
                                   BinaryPrimitives.ReadUInt32LittleEndian(directory[(off + 6)..])));
                        off += 10;
                    }

                    Need(directory, off, preloadSize);
                    byte[] preload = directory.Slice(off, preloadSize).ToArray();
                    off += preloadSize;

                    if (full.ToLowerInvariant() != want)
                        continue;

                    long total = preload.Length;
                    foreach ((_, _, uint length) in parts)
                        total += length;
                    var bytes = new byte[total];
                    preload.CopyTo(bytes, 0);
                    int at = preload.Length;
                    foreach ((int archiveIndex, uint offset, uint length) in parts)
                    {
                        string file = archiveIndex == 0x7fff
                            ? dirVpk
                            : $"{baseName}_{archiveIndex.ToString("D3", CultureInfo.InvariantCulture)}.vpk";
                        long baseOffset = archiveIndex == 0x7fff ? headerSize + (long)dirSize : 0;
                        using var stream = File.OpenRead(file);
                        stream.Seek(baseOffset + offset, SeekOrigin.Begin);
                        stream.ReadExactly(bytes, at, (int)length);
                        at += (int)length;
                    }
                    return bytes;
                }
            }
        }
        return null;
    }

    private static string ReadString(ReadOnlySpan<byte> directory, ref int offset)
    {
        int end = directory[offset..].IndexOf((byte)0);
        if (end < 0)
            throw new InvalidDataException("a name runs to the end of the directory");
        string value = Encoding.Latin1.GetString(directory.Slice(offset, end));
        offset += end + 1;
        return value;
    }

    private static void Need(ReadOnlySpan<byte> directory, int offset, int bytes)
    {
        if (offset + bytes > directory.Length)
            throw new InvalidDataException($"entry at {offset} needs more bytes than the directory holds");
    }
}
