using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// What a server's build of a map's navigation was made from and by
/// (plan_bots_teams_tf2 E.3 step 4), written beside the file as
/// <c>.nav3d.stamp</c>: the bsp's and the file's SHA-256, the builder's and
/// the reader's package versions, the models the build asked the content for
/// (a hash of the list, how many and how many the content lacked), how long it
/// took on how many threads, and the floor check when one ran.
/// </summary>
public sealed record NavBuildStamp(string BspSha256, string NavSha256, string MapTools, string MapFormats, string ModelsSha256, int Models,
                                   int MissingModels, double Seconds, int Threads, string Check = "not run")
{
    /// <summary>The stamp as the file holds it: one JSON object, keys sorted.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("bsp_sha256", BspSha256);
            w.WriteString("check", Check);
            w.WriteString("mapformats", MapFormats);
            w.WriteString("maptools", MapTools);
            w.WriteNumber("missing_models", MissingModels);
            w.WriteNumber("models", Models);
            w.WriteString("models_sha256", ModelsSha256);
            w.WriteString("nav_sha256", NavSha256);
            w.WriteNumber("seconds", Math.Round(Seconds, 2));
            w.WriteNumber("threads", Threads);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>The hash of a model list as the stamp names it: SHA-256 over the paths, one per line, in the order asked.</summary>
    public static string HashModels(IEnumerable<string> models)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', models))));
}

/// <summary>
/// The server's own builds of maps' navigation (plan_bots_teams_tf2 E.4):
/// <c>&lt;dir&gt;/&lt;map&gt;-&lt;sha256(bsp)[:16]&gt;.nav3d</c>, the derived
/// graph cache's directory (<c>&lt;GameDir&gt;/maps/ai-nav</c>), never beside
/// a shipped map. A changed bsp is another name, so another build. Each file
/// is written to a temporary name of its own (a GUID, so two games writing the
/// same key never share one) and renamed over the target, so a reader finds
/// the old file, the new one, or none, never half of one.
/// </summary>
public sealed class BuiltNavStore(string directory)
{
    public string Directory => directory;

    /// <summary>The hex SHA-256 of a map's bytes, the store's key.</summary>
    public static string Sha256(byte[] bsp) => Convert.ToHexStringLower(SHA256.HashData(bsp));

    /// <summary>The file's name in the store, for a line that names it.</summary>
    public string Name(string map, string bspSha256) => $"maps/ai-nav/{FileName(map, bspSha256)}";

    public string PathFor(string map, string bspSha256) => Path.Combine(directory, FileName(map, bspSha256));

    public string StampPathFor(string map, string bspSha256) => PathFor(map, bspSha256) + ".stamp";

    private static string FileName(string map, string bspSha256)
    {
        // A map name may carry a folder (a workshop map's); the store is one directory.
        var safe = new StringBuilder(map.Length);
        foreach (char c in map)
            safe.Append(c is '/' or '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return string.Create(CultureInfo.InvariantCulture, $"{safe}-{bspSha256[..16]}.nav3d");
    }

    /// <summary>The stored build's bytes, or null when there is none (or it cannot be read).</summary>
    public byte[]? TryRead(string map, string bspSha256)
    {
        try
        {
            return File.ReadAllBytes(PathFor(map, bspSha256));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The stored build's stamp, as written, or null.</summary>
    public string? TryReadStamp(string map, string bspSha256)
    {
        try
        {
            return File.ReadAllText(StampPathFor(map, bspSha256));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Stores a build: the file, then its stamp, each through its own temporary name and a rename.</summary>
    public void Write(string map, string bspSha256, byte[] nav, NavBuildStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(stamp);
        System.IO.Directory.CreateDirectory(directory);
        Replace(PathFor(map, bspSha256), nav);
        Replace(StampPathFor(map, bspSha256), Encoding.UTF8.GetBytes(stamp.ToJson()));
    }

    /// <summary>Writes <paramref name="bytes"/> to a GUID-named file beside <paramref name="path"/> and renames it over the target.</summary>
    public static void Replace(string path, byte[] bytes)
    {
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
