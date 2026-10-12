using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Golden files for the UI (plan_client_ui §3.2 "Goldens"): src/test/client/Ui/Golden/.
/// A change to what is drawn is a visible diff in the commit, regenerated on
/// purpose with UI_GOLDEN_WRITE=1, never on the fly. A missing golden fails.
/// </summary>
internal static class UiGolden
{
    public static string PathOf(string name) => RepoLayout.Repo("src", "test", "client", "Ui", "Golden", name);

    public static bool Writing => Environment.GetEnvironmentVariable("UI_GOLDEN_WRITE") == "1";

    public static void Text(string name, string actual)
    {
        string path = PathOf(name);
        if (Writing)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"no golden {path}: write it with UI_GOLDEN_WRITE=1 and review the diff");
        string expected = File.ReadAllText(path);
        if (expected == actual)
            return;

        // The first differing line, so the failure says where.
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        Assert.Fail($"{name} differs from its golden at line {i + 1}:\n  golden: {(i < e.Length ? e[i] : "<end>")}\n  actual: {(i < a.Length ? a[i] : "<end>")}");
    }
}
