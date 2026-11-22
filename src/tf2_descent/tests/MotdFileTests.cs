extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using Xunit;

using KeyAction = client::SourceSharp.Client.Input.KeyActionAttribute;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2 Descent's message of the day is its own (user 2026-11-15: "and updated
/// message of the day describing what descent is"), found ahead of TF2's by
/// the order the core's loader (<see cref="MessageOfTheDay"/>) looks and the
/// order the mod's gameinfo.txt mounts: the loader asks for
/// <c>cfg/motd.txt</c>, then <c>motd.txt</c>, then the shipped
/// <c>cfg/motd_default.txt</c>, each over the whole GAME path; TF2's is the
/// last of those (its content's <c>cfg/motd_default.txt</c>), and the mod's
/// own <c>motd.txt</c> sits in the first GAME directory.
/// </summary>
public sealed class MotdFileTests
{
    private const string TF2Motd = "Welcome to Team Fortress 2\r\n\r\nOur map rotation is:\r\n- 2Fort\r\n";

    /// <summary>The GAME path's loose directories, in gameinfo.txt's order:
    /// the repo's mod directories for the mod and its siblings, and
    /// <paramref name="tfContent"/> for TF2's content. The packs are skipped
    /// (TF2's and HL2's hold no motd file: read off their directory trees by
    /// hand, 2026-11-15), and so is the engine's install. A path this does not
    /// know fails the fact, so a new mount is looked at.</summary>
    private static List<string> GameDirectories(string tfContent)
    {
        var dirs = new List<string>();
        foreach (var (key, path) in GameInfoTests.SearchPaths("tf2_descent"))
        {
            if (!key.Split('+').Contains("game"))
                continue;

            string? dir = path switch
            {
                "|gameinfo_path|." => RepoLayout.Repo("game", "mod_tf2_descent"),
                "|gameinfo_path|pool" => RepoLayout.Repo("game", "mod_tf2_descent", "pool"),
                "|gameinfo_path|../tf2" => RepoLayout.Repo("game", "mod_tf2"),
                "|gameinfo_path|../test" => RepoLayout.Repo("game", "mod_test"),
                "|gameinfo_path|../../content/tf" => tfContent,
                "|gameinfo_path|custom/*" or "|gameinfo_path|download" => null,
                _ when path.EndsWith(".vpk", StringComparison.Ordinal) => null,
                _ when path.StartsWith("|all_source_engine_paths|", StringComparison.Ordinal) => null,
                _ => throw new Xunit.Sdk.XunitException($"gameinfo.txt mounts {key} {path}, which this fact does not place"),
            };
            if (dir is not null)
                dirs.Add(dir);
        }

        return dirs;
    }

    /// <summary>The engine's GAME lookup over loose directories: the first that holds the file answers.</summary>
    private sealed class SearchPathFiles(IReadOnlyList<string> dirs) : IEngineFiles
    {
        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;

        public byte[]? ReadGameFileBytes(string path)
        {
            foreach (string dir in dirs)
            {
                string file = Path.Combine(dir, path);
                if (File.Exists(file))
                    return File.ReadAllBytes(file);
            }

            return null;
        }
    }

    private sealed class Panel : IInfoPanel
    {
        public Dictionary<string, byte[]> Written { get; } = [];

        public bool Set(string name, ReadOnlySpan<byte> data)
        {
            Written[name] = data.ToArray();
            return true;
        }
    }

    /// <summary>The loader at its default names over <paramref name="dirs"/>: what the InfoPanel table holds after.</summary>
    private static Dictionary<string, byte[]> Load(IReadOnlyList<string> dirs)
    {
        var panel = new Panel();
        new MessageOfTheDay(new RecordingLog(), DeclaredConVars.Fresh<MotdFileConVar>(), DeclaredConVars.Fresh<MotdTextFileConVar>(),
                            new SearchPathFiles(dirs), panel).Load();
        return panel.Written;
    }

    /// <summary>A directory standing for TF2's content, holding what TF2 ships: its default motd files.</summary>
    private static DirectoryInfo TFContent()
    {
        var dir = Directory.CreateTempSubdirectory("descent-motd-tf-");
        Directory.CreateDirectory(Path.Combine(dir.FullName, "cfg"));
        File.WriteAllText(Path.Combine(dir.FullName, "cfg", "motd_default.txt"), TF2Motd);
        File.WriteAllText(Path.Combine(dir.FullName, "cfg", "motd_text_default.txt"), TF2Motd);
        return dir;
    }

    private static byte[] Terminated(string file) => [.. File.ReadAllBytes(file), 0];

    [Fact]
    public void The_motd_found_on_Descents_search_path_is_the_mods_own_and_not_TF2s()
    {
        var tf = TFContent();
        try
        {
            var written = Load(GameDirectories(tf.FullName));

            Assert.NotEqual([.. System.Text.Encoding.ASCII.GetBytes(TF2Motd), (byte)0], written["motd"]);
            Assert.Equal(Terminated(RepoLayout.Repo("game", "mod_tf2_descent", "motd.txt")), written["motd"]);
            Assert.Equal(Terminated(RepoLayout.Repo("game", "mod_tf2_descent", "motd_text.txt")), written["motd_text"]);
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <summary>The control: the same search path without the mod's own
    /// directory answers TF2's message, so the stand-in for TF2's content is
    /// reached and the fact above is a choice between the two.</summary>
    [Fact]
    public void Without_the_mods_directory_the_search_path_answers_TF2s_motd()
    {
        var tf = TFContent();
        try
        {
            var dirs = GameDirectories(tf.FullName).Where(d => d != RepoLayout.Repo("game", "mod_tf2_descent")).ToList();

            var written = Load(dirs);

            Assert.Equal([.. System.Text.Encoding.ASCII.GetBytes(TF2Motd), (byte)0], written["motd"]);
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <summary>Each letter key the message's KEYS list names is the
    /// mod's own key action on that key, under that action's label (the
    /// first word or words after the key's " - "), so the list cannot drift from the
    /// binds a fresh config gets.</summary>
    [Fact]
    public void The_motds_letter_keys_are_the_mods_key_actions()
    {
        var actions = typeof(client::TF2.Descent.Client.UseKey).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(KeyAction), false).Cast<KeyAction>())
            .ToArray();
        string[] lines = File.ReadAllLines(RepoLayout.Repo("game", "mod_tf2_descent", "motd.txt"));
        var rows = lines.SkipWhile(l => l != "KEYS").Skip(1)
            .Where(l => l.Length > 4 && char.IsLetter(l[0]) && l[1..4] == " - ")
            .Select(l => (Key: char.ToLowerInvariant(l[0]).ToString(), Text: l[4..]))
            .ToArray();

        // The stimulus: the list holds the use key and the window keys.
        Assert.Contains(rows, r => r.Key == "e");
        Assert.True(rows.Length >= 5, $"{rows.Length} letter rows");

        var wrong = rows.Where(r => !actions.Any(a => a.Keys.Contains(r.Key) && a.Label is { } label
                                                      && r.Text.StartsWith(label, StringComparison.Ordinal)))
                        .Select(r => $"{r.Key}: '{r.Text}' is no key action of the mod on that key")
                        .ToArray();
        Assert.Empty(wrong);
        Assert.True(File.ReadAllText(RepoLayout.Repo("game", "mod_tf2_descent", "motd.txt")).Length <= MessageOfTheDay.MaxBytes);
    }
}
