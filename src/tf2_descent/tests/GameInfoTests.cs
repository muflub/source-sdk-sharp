using SourceSharp.Tests;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2Descent runs the TF2 mod's game over TF2's content, so its GAME search
/// path must hold every content path the TF2 mod mounts. It once mounted three
/// single-file packs and not the loose TF2 directory, where the item schema
/// (<c>scripts/items/items_game.txt</c>) lives: the console said the schema was
/// on no GAME path, and a hero spawned with no TF kit.
/// </summary>
public sealed class GameInfoTests
{
    /// <summary>The prefix of every path into TF2's own content.</summary>
    private const string TFContent = "|gameinfo_path|../../content/tf";

    /// <summary>The prefix of every path into the engine's install.</summary>
    private const string EngineContent = "|all_source_engine_paths|";

    private static string GameInfo(string mod) => RepoLayout.Repo("game", "mod_" + mod, "gameinfo.txt");

    /// <summary>
    /// The SearchPaths block's entries, in order, as whole (key, path) pairs:
    /// comments dropped, every line inside the block that is not a brace.
    /// </summary>
    internal static List<(string Key, string Path)> SearchPaths(string mod)
    {
        var entries = new List<(string, string)>();
        bool inside = false;
        int depth = 0;
        foreach (var raw in File.ReadAllLines(GameInfo(mod)))
        {
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            string line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0)
                continue;

            if (!inside)
            {
                if (line == "SearchPaths")
                    inside = true;
                continue;
            }

            if (line == "{")
            {
                depth++;
                continue;
            }

            if (line == "}")
            {
                if (--depth == 0)
                    break;
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(parts.Length == 2, $"{GameInfo(mod)}: a search path is a key and a path: '{line}'");
            entries.Add((parts[0], parts[1]));
        }

        Assert.True(depth == 0 && inside, $"{GameInfo(mod)}: no closed SearchPaths block");
        return entries;
    }

    private static string[] Missing(Func<string, bool> which)
    {
        var descent = SearchPaths("tf2_descent");
        var tf2 = SearchPaths("tf2").Where(e => which(e.Path)).ToArray();

        // The stimulus: the TF2 mod has such paths to compare.
        Assert.NotEmpty(tf2);
        return tf2.Where(e => !descent.Contains(e)).Select(e => $"{e.Key} {e.Path}").ToArray();
    }

    [Fact]
    public void Descent_mounts_every_TF2_content_path_the_tf2_mod_mounts()
    {
        Assert.Empty(Missing(p => p.StartsWith(TFContent, StringComparison.Ordinal)));
    }

    [Fact]
    public void Descent_mounts_every_engine_pack_the_tf2_mod_mounts()
    {
        Assert.Empty(Missing(p => p.StartsWith(EngineContent, StringComparison.Ordinal)));
    }

    [Fact]
    public void Descent_s_own_levels_and_the_tf2_mod_come_before_TF2_s_content()
    {
        var paths = SearchPaths("tf2_descent").Select(e => e.Path).ToList();
        int firstContent = paths.FindIndex(p => p.StartsWith(TFContent, StringComparison.Ordinal));
        Assert.True(firstContent >= 0, "no TF2 content path at all");

        foreach (var own in new[] { "|gameinfo_path|.", "|gameinfo_path|pool", "|gameinfo_path|../tf2" })
        {
            int at = paths.IndexOf(own);
            Assert.True(at >= 0 && at < firstContent, $"{own} is at {at}, TF2's content starts at {firstContent}");
        }
    }

    /// <summary>
    /// The engine runs cfg/valve.rc instead of the command line's + commands
    /// when one is found on the MOD path, and only valve.rc's stuffcmds runs
    /// them. HL2MP's pack answered for Descent's until the mod stopped
    /// mounting it; then +map never ran and the game sat in its menu.
    /// </summary>
    [Fact]
    public void Descent_seeds_a_valve_rc_that_runs_the_command_line()
    {
        var lines = File.ReadAllLines(RepoLayout.Repo("game", "mod_tf2_descent", "cfg-seed", "valve.rc"))
            .Select(l => (l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l).Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        Assert.Equal("stuffcmds", lines.LastOrDefault());
    }

    [Fact]
    public void Descent_mounts_TF2_s_content_and_the_core_game_in_the_tf2_mod_s_order()
    {
        // TF2's packs, its loose directory and the core game behind them: the
        // core's HL2 scripts must not answer for TF2's.
        static bool Shared(string p) => p.StartsWith(TFContent, StringComparison.Ordinal) || p == "|gameinfo_path|../test";

        var tf2 = SearchPaths("tf2").Select(e => e.Path).Where(Shared).ToArray();
        var descent = SearchPaths("tf2_descent").Select(e => e.Path).Where(Shared).ToArray();
        Assert.True(tf2.Length >= 6, $"the TF2 mod's chain has {tf2.Length} such paths");
        Assert.Equal(tf2, descent);
    }
}
