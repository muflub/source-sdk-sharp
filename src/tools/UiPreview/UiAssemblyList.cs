namespace SourceSharp.UiPreview;

/// <summary>
/// tools/ui-preview-assemblies.txt: the game-side screen assemblies that join
/// the previews, the lint and the UI tests (docs/plans/descent_ui.md §1).
/// Lines are `AssemblyName` or `AssemblyName=game/&lt;moddir&gt;` (the mod whose
/// resource/*_english.txt chains into the preview's localiser). Comments are
/// `#` lines; a missing file is a failure, never a silent "no extra games".
/// </summary>
public static class UiAssemblyList
{
    public const string FileName = "tools/ui-preview-assemblies.txt";

    public static IReadOnlyList<(string Name, string? ModDir)> Load(string root)
    {
        string path = Path.Combine(root, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"ui-preview: {FileName} is missing; every game UI assembly it lists must be discovered");
        var list = new List<(string, string?)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) list.Add((line, null));
            else list.Add((line[..eq].Trim(), line[(eq + 1)..].Trim()));
        }
        return list;
    }
}
