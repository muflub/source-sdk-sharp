using System.Text.RegularExpressions;

namespace SourceSharp.OracleTests;

/// <summary>
/// The source files a VPC project script compiles: its <c>$File</c> entries
/// and those of the scripts it <c>$Include</c>s, a continued entry
/// (<c>$File "a" \</c> then <c>"b"</c>) included. An entry or include under
/// a <c>[condition]</c> is left out: the conditions in the server projects
/// are platform and build switches (<c>$WINDOWS</c>, <c>$BUILD_REPLAY</c>,
/// <c>$TF</c>), none of which the HL2MP game DLL's entities depend on. Only
/// scripts under <c>src/game</c> are followed; the platform scripts under
/// <c>vpc_scripts</c> list no game source. Keywords are matched as VPC does,
/// without regard to case.
/// </summary>
public static class VpcProject
{
    private static readonly Regex Include = new(@"^\s*\$Include\s+""(?<path>[^""]+)""\s*(?<cond>\[[^\]]*\])?",
                                                RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex Entry = new(@"""(?<path>[^""]+)""\s*(?<cond>\[[^\]]*\])?\s*(?<more>\\)?\s*$");

    /// <summary>
    /// Every unconditional file <paramref name="script"/> names, as full
    /// paths, <c>$SRCDIR</c> being <paramref name="srcDir"/> and a relative
    /// path the script's own directory's.
    /// </summary>
    public static IReadOnlyCollection<string> Files(string script, string srcDir)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        Read(Path.GetFullPath(script), Path.GetFullPath(srcDir), files, new HashSet<string>(StringComparer.Ordinal));
        return files;
    }

    private static void Read(string script, string srcDir, HashSet<string> files, HashSet<string> seen)
    {
        if (!seen.Add(script))
            return;

        string text = File.ReadAllText(script);
        string dir = Path.GetDirectoryName(script)!;
        string game = Path.Combine(srcDir, "game") + Path.DirectorySeparatorChar;

        foreach (Match m in Include.Matches(text))
        {
            string included = Resolve(m.Groups["path"].Value, dir, srcDir);
            if (!m.Groups["cond"].Success && included.StartsWith(game, StringComparison.Ordinal) && File.Exists(included))
                Read(included, srcDir, files, seen);
        }

        bool continued = false;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            // VPC's keywords are not case-sensitive: server_base.vpc says $file.
            bool isFile = trimmed.StartsWith("$File", StringComparison.OrdinalIgnoreCase);

            if (!isFile && !continued)
                continue;

            Match m = Entry.Match(trimmed);
            continued = m.Success && m.Groups["more"].Success;

            if (m.Success && !m.Groups["cond"].Success)
                files.Add(Resolve(m.Groups["path"].Value, dir, srcDir));
        }
    }

    private static string Resolve(string path, string dir, string srcDir)
    {
        string p = path.Replace("$SRCDIR", srcDir, StringComparison.Ordinal).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(dir, p));
    }
}
