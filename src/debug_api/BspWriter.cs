using System.Text;

namespace Debug.Api;

/// <summary>
/// Writes an edited entity list back into a compiled map.
///
/// A BSP is the output of a build, and editing one is not something a debugger
/// should do lightly -- so this does the least it possibly can. Only the entity
/// lump changes. Everything else in the file is copied byte for byte, and the
/// new lump is appended at the end with the header's offset and length pointed
/// at it, rather than the file being re-laid-out around it.
///
/// That is the same trick bspzip uses, and it is safe for the same reason: a
/// lump's position is whatever the header says it is, so nothing else has to
/// move and no other offset has to be recomputed. A single wrong offset in a
/// rebuilt header would produce a map that loads and is subtly wrong, which is
/// far worse than one that refuses to load at all.
///
/// The result is written to a temporary file and moved into place. On POSIX that
/// replaces the directory entry while the running engine keeps the inode it
/// already opened, so saving over the map that is currently loaded cannot
/// corrupt the session -- it simply has no effect until the map is reloaded.
/// </summary>
public static class BspWriter
{
    private const int LumpEntities = 0;
    private const int HeaderLumps = 64;
    private const int HeaderSize = 8 + HeaderLumps * 16;

    /// <summary>What a save did.</summary>
    public readonly record struct Report(string Path, string? BackupPath, int EntityCount, long Bytes);

    /// <summary>
    /// Rewrites <paramref name="sourcePath"/>'s entity lump and writes the
    /// result to <paramref name="destinationPath"/>.
    ///
    /// The two may be the same file, which is the ordinary case: a backup is
    /// taken first, and the write goes through a temporary file so an
    /// interrupted save cannot leave a half-written map where the real one was.
    /// </summary>
    public static Report Write(string sourcePath, string destinationPath,
                               IReadOnlyList<MapEntity> entities, bool backup = true)
    {
        if (entities.Count == 0)
            throw new ArgumentException("refusing to write a map with no entities", nameof(entities));

        // A map without a worldspawn is not a map. This is the one structural
        // rule worth enforcing here, because breaking it produces a file the
        // engine will not load at all and the mistake is easy to make from a
        // filtered list.
        if (!entities.Any(e => e.ClassName.Equals("worldspawn", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("refusing to write a map with no worldspawn", nameof(entities));

        byte[] original = File.ReadAllBytes(sourcePath);

        if (original.Length < HeaderSize
            || original[0] != (byte)'V' || original[1] != (byte)'B'
            || original[2] != (byte)'S' || original[3] != (byte)'P')
        {
            throw new InvalidDataException("not a BSP file");
        }

        byte[] lump = Encoding.UTF8.GetBytes(Serialize(entities));

        // Lumps are four-byte aligned. The engine does not mind a short one, but
        // every tool that walks the file assumes the alignment holds.
        int padding = (4 - (lump.Length % 4)) % 4;

        var rewritten = new byte[original.Length + lump.Length + padding];
        Array.Copy(original, rewritten, original.Length);
        Array.Copy(lump, 0, rewritten, original.Length, lump.Length);

        int at = 8 + LumpEntities * 16;
        BitConverter.GetBytes(original.Length).CopyTo(rewritten, at);
        BitConverter.GetBytes(lump.Length).CopyTo(rewritten, at + 4);

        // The lump version and fourCC are left as they were. A fourCC other than
        // zero would mean the lump was compressed, and having just written it
        // uncompressed that would be a lie.
        BitConverter.GetBytes(0).CopyTo(rewritten, at + 12);

        string? backupPath = backup && File.Exists(destinationPath)
            ? BackUp(destinationPath)
            : null;

        string temporary = destinationPath + ".writing";

        File.WriteAllBytes(temporary, rewritten);
        File.Move(temporary, destinationPath, overwrite: true);

        return new Report(destinationPath, backupPath, entities.Count, rewritten.Length);
    }

    /// <summary>Where copies of overwritten maps are kept, beside the maps themselves.</summary>
    public const string BackupDirectory = "debug-api-backups";

    /// <summary>
    /// Copies a map aside before it is overwritten.
    ///
    /// Every version is kept, numbered, in a directory of its own. A single
    /// backup file would be worse than none by the second save: the original
    /// would be replaced by a copy of the first edit, and the thing being backed
    /// up would be gone. A subdirectory rather than siblings keeps the maps
    /// directory readable and keeps the copies out of the map list, which scans
    /// for *.bsp beside it.
    /// </summary>
    private static string BackUp(string path)
    {
        string directory = Path.Combine(
            Path.GetDirectoryName(path) ?? ".", BackupDirectory);

        Directory.CreateDirectory(directory);

        string name = Path.GetFileNameWithoutExtension(path);

        // The first free number, so an existing copy is never overwritten. Four
        // digits sorts correctly in a listing for as long as anyone will care.
        for (int n = 1; n < 10000; n++)
        {
            string candidate = Path.Combine(directory, $"{name}.{n:D4}.bsp");

            if (File.Exists(candidate))
                continue;

            File.Copy(path, candidate);
            return candidate;
        }

        throw new IOException($"there are already 9999 backups of {name}; tidy {directory} up");
    }

    /// <summary>
    /// The entity lump, as the engine's parser expects it.
    ///
    /// Connections are written from the parsed list rather than from whatever
    /// keyvalues came in, and any keyvalue that reads as a connection is dropped
    /// on the way. Otherwise an edited entity would carry both its original
    /// connection keys and the edited ones, and every output would fire twice.
    /// </summary>
    internal static string Serialize(IReadOnlyList<MapEntity> entities)
    {
        var text = new StringBuilder();

        foreach (var entity in entities)
        {
            text.Append("{\n");

            foreach (var kv in entity.KeyValues)
            {
                if (BspFile.TryParseConnection(kv, out _))
                    continue;

                Append(text, kv.Key, kv.Value);
            }

            foreach (var connection in entity.Connections)
            {
                Append(text, connection.Output,
                    $"{connection.Target},{connection.Input},{connection.Parameter}," +
                    $"{connection.Delay.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{connection.TimesToFire}");
            }

            text.Append("}\n");
        }

        // The lump is null-terminated. The engine's parser stops there, and a
        // lump without one runs off into whatever follows it in the file.
        return text.Append('\0').ToString();
    }

    /// <summary>
    /// One key and value.
    ///
    /// Quotes inside either would end the token early and shift everything after
    /// it, so they are dropped rather than escaped -- the entity lump format has
    /// no escape for them, and a silently truncated map is worse than a value
    /// that lost a character.
    /// </summary>
    private static void Append(StringBuilder text, string key, string value)
        => text.Append('"').Append(Clean(key)).Append("\" \"").Append(Clean(value)).Append("\"\n");

    private static string Clean(string text)
    {
        if (!text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
            return text;

        return text.Replace("\"", "").Replace("\r", "").Replace("\n", " ");
    }
}
