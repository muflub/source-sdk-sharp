namespace SourceSharp.UiPreview;

/// <summary>The repo and configuration this tool was built in, found from its own location.</summary>
public static class RepoRoot
{
    public static string Find(string from)
    {
        for (var d = new DirectoryInfo(from); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SourceSharp.slnx")))
                return d.FullName;
        throw new DirectoryNotFoundException($"no SourceSharp.slnx above {from}");
    }

    /// <summary>bin/&lt;config&gt;/tools/ui-preview → config.</summary>
    public static string Config(string from)
    {
        var parts = Path.GetFullPath(from).TrimEnd('/').Split('/');
        int bin = Array.LastIndexOf(parts, "bin");
        return bin >= 0 && bin + 1 < parts.Length ? parts[bin + 1] : "debug";
    }
}
