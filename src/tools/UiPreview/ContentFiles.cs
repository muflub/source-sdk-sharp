using SourceSharp.Abi;

namespace SourceSharp.UiPreview;

/// <summary>
/// The GAME paths a mod mounts, offline: the <c>game</c> search paths of its
/// gameinfo.txt that are relative to the file (<c>|gameinfo_path|</c>), in
/// its order, each a directory or a VPK. The engine's own paths
/// (<c>|all_source_engine_paths|</c>) and wildcards are not mounted: the
/// preview has no engine install, so a file only they hold reads as absent
/// and the panel draws its fallback.
/// </summary>
public sealed class ContentFiles : IEngineFiles
{
    private readonly List<object> _order = [];

    /// <summary>What was mounted, in order, for the preview's summary line.</summary>
    public IReadOnlyList<string> Mounted { get; }

    private ContentFiles(List<string> mounted) => Mounted = mounted;

    /// <summary>Mounts <paramref name="gameInfoPath"/>'s relative game paths. A path it names that is not there is skipped and listed as missing.</summary>
    public static ContentFiles FromGameInfo(string gameInfoPath)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(gameInfoPath))!;
        var mounted = new List<string>();
        var files = new ContentFiles(mounted);
        foreach (string raw in File.ReadAllLines(gameInfoPath))
        {
            string line = raw.Split("//")[0].Trim();
            string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 || !words[0].Split('+').Contains("game", StringComparer.OrdinalIgnoreCase))
                continue;
            const string relative = "|gameinfo_path|";
            if (!words[1].StartsWith(relative, StringComparison.OrdinalIgnoreCase) || words[1].Contains('*'))
                continue;
            string path = Path.GetFullPath(Path.Combine(root, words[1][relative.Length..]));
            files.Mount(path, mounted);
        }
        return files;
    }

    /// <summary>One directory, mounted alone (a --content run without a mod).</summary>
    public static ContentFiles FromDirectory(string dir)
    {
        var mounted = new List<string>();
        var files = new ContentFiles(mounted);
        files.Mount(Path.GetFullPath(dir), mounted);
        return files;
    }

    private void Mount(string path, List<string> mounted)
    {
        if (path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            // gameinfo names an archive by its directory file or by its bare name.
            string dir = path.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) ? path : path[..^".vpk".Length] + "_dir.vpk";
            string found = File.Exists(dir) ? dir : path;
            if (!File.Exists(found))
            {
                mounted.Add($"missing {path}");
                return;
            }
            var vpk = VpkDirectory.Open(found);
            _order.Add(vpk);
            mounted.Add($"{found} ({vpk.Count} files)");
        }
        else if (Directory.Exists(path))
        {
            _order.Add(path);
            mounted.Add(path);
        }
        else
        {
            mounted.Add($"missing {path}");
        }
    }

    public byte[]? ReadGameFileBytes(string path)
    {
        foreach (object source in _order)
        {
            if (source is VpkDirectory vpk)
            {
                if (vpk.Read(path) is { } bytes)
                    return bytes;
            }
            else if (Loose((string)source, path) is { } file)
            {
                return File.ReadAllBytes(file);
            }
        }
        return null;
    }

    public string? ReadGameFile(string path)
        => ReadGameFileBytes(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

    public byte[]? ReadFileOnPath(string path, string pathId)
        => string.Equals(pathId, "GAME", StringComparison.OrdinalIgnoreCase) ? ReadGameFileBytes(path) : null;

    // The engine's file system does not care about case; a Linux directory does.
    private static string? Loose(string dir, string path)
    {
        string exact = Path.Combine(dir, path.Replace('\\', '/'));
        if (File.Exists(exact))
            return exact;
        string lower = Path.Combine(dir, path.Replace('\\', '/').ToLowerInvariant());
        return File.Exists(lower) ? lower : null;
    }
}
