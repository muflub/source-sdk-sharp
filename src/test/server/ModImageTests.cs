using System.Diagnostics;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A game directory of the facts' own: bin/debug's entries as links, with a
/// real mods/tf2_descent (its cfg/ and maps/ real directories) so a fact can
/// plant run leftovers, a locked directory or a stand-in town .bsp without
/// touching the tree's bin/, where a game may be running and where an earlier
/// run's pool/, cache/ and cfg/config.cfg are left behind. tools/mod-image
/// reads it through --game. The .bsp is a stand-in: the tool only lists it.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class PlantedGame : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"mod-image-game-{Guid.NewGuid():N}");

    public PlantedGame()
    {
        string real = RepoLayout.Repo("bin", "debug");
        string realMod = Path.Combine(real, "mods", "tf2_descent");
        if (!Directory.Exists(realMod))
            throw new DirectoryNotFoundException($"{realMod} is missing: make setup and make debug first");
        Directory.CreateDirectory(Path.Combine(Root, "mods"));
        foreach (string e in Directory.EnumerateFileSystemEntries(real))
            if (Path.GetFileName(e) != "mods")
                File.CreateSymbolicLink(Path.Combine(Root, Path.GetFileName(e)), e);
        foreach (string m in Directory.EnumerateFileSystemEntries(Path.Combine(real, "mods")))
            if (Path.GetFileName(m) != "tf2_descent")
                File.CreateSymbolicLink(Path.Combine(Root, "mods", Path.GetFileName(m)), m);

        string mod = Mod();
        Directory.CreateDirectory(mod);
        foreach (string e in Directory.EnumerateFileSystemEntries(realMod))
        {
            string name = Path.GetFileName(e);
            if (name is "cfg" or "maps" or "pool" or "cache")
                continue;   // the real tree's run leftovers never reach the planted game
            File.CreateSymbolicLink(Path.Combine(mod, name), e);
        }
        Directory.CreateDirectory(Mod("cfg"));
        if (Directory.Exists(Path.Combine(realMod, "cfg")))
            foreach (string f in Directory.EnumerateFiles(Path.Combine(realMod, "cfg")))
                if (Path.GetFileName(f) != "config.cfg")
                    File.Copy(f, Mod("cfg", Path.GetFileName(f)));
        Directory.CreateDirectory(Mod("maps"));
        File.WriteAllText(Mod("maps", "tf2_descent_town.bsp"), "stand-in: tools/mod-image only lists it");
    }

    /// <summary>A path under the planted mods/tf2_descent.</summary>
    public string Mod(params string[] parts) =>
        Path.Combine(new[] { Root, "mods", "tf2_descent" }.Concat(parts).ToArray());

    public void Dispose()
    {
        // Re-open what a fact locked (pool/ at 0744, maps/locked at 0700), so
        // the delete can enter it. Only planted directories: never a link.
        foreach (string d in new[] { Mod("pool"), Mod("maps", "locked") })
            if (Directory.Exists(d) && new DirectoryInfo(d).LinkTarget is null)
                File.SetUnixFileMode(d, (UnixFileMode)0b111_111_101);
        Directory.Delete(Root, recursive: true);   // removes the links, never what they point at
    }
}

/// <summary>
/// tools/mod-image lays out what a hosted pod's init container copies to /game
/// (plan_host_sdk §2.5; the host's docs/mod-image.md).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]   // file modes: the tool and the pods are Linux
public sealed class ModImageTests
{
    private static (int Exit, string Output) DryRun(PlantedGame game, params string[] extra)
    {
        var psi = new ProcessStartInfo(RepoLayout.Repo("tools", "mod-image"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "--config", "debug", "--dry-run", "--game", game.Root }.Concat(extra))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [Fact]
    public void The_image_carries_the_mod_its_resolved_settings_and_the_core_and_nothing_mounted()
    {
        using var game = new PlantedGame();
        var (exit, output) = DryRun(game);
        var lines = output.Split('\n');

        Assert.True(exit == 0, output);
        foreach (var must in new[] { "game/mods.yaml", "game/bin/linux64", "game/bin/managed", "game/bin/dotnet",
                                     "game/mods/tf2_descent/gameinfo.txt", "game/mods/tf2_descent/mod.dedicated.yaml",
                                     "game/mods/test/gameinfo.txt", "game/mods/tf2/gameinfo.txt" })
            Assert.Contains(must, lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("game/content", StringComparison.Ordinal)
                                          || l.StartsWith("game/engines", StringComparison.Ordinal));
        Assert.Contains("fd_limit: 4096", lines);
        Assert.Contains("  - -console", lines);
    }

    /// <summary>
    /// Plants what a game run leaves in the mod directory (the engine's
    /// pool/ and cache/ at 0744, its rewritten cfg/config.cfg) in a planted
    /// game, and hands it to <paramref name="check"/>.
    /// </summary>
    private static void WithRunLeftovers(Action<PlantedGame> check)
    {
        using var game = new PlantedGame();
        string pool = game.Mod("pool"), cache = game.Mod("cache");
        Directory.CreateDirectory(Path.Combine(pool, "sound"));
        File.WriteAllText(Path.Combine(pool, "sound", "sound.cache"), "x");
        Directory.CreateDirectory(cache);
        File.WriteAllText(game.Mod("cfg", "config.cfg"), "// written by the engine\n");
        File.SetUnixFileMode(pool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                  | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        check(game);
    }

    [Fact]
    public void What_a_game_run_leaves_in_the_mod_directory_is_not_in_the_image()
    {
        WithRunLeftovers(game =>
        {
            var (exit, output) = DryRun(game);
            var lines = output.Split('\n');

            Assert.True(exit == 0, output);
            Assert.Contains("game/mods/tf2_descent/maps/tf2_descent_town.bsp", lines);   // the stimulus reached a real listing
            Assert.DoesNotContain("game/mods/tf2_descent/pool", lines);
            Assert.DoesNotContain("game/mods/tf2_descent/cache", lines);
        });
    }

    [Fact]
    public void The_images_cfg_is_the_repos_seed_not_the_engines_rewrite()
    {
        WithRunLeftovers(game =>
        {
            var (exit, output) = DryRun(game);
            var lines = output.Split('\n');

            Assert.True(exit == 0, output);
            Assert.Contains("game/mods/tf2_descent/cfg/server.cfg", lines);
            Assert.DoesNotContain("game/mods/tf2_descent/cfg/config.cfg", lines);
        });
    }

    [Fact]
    public void A_directory_a_non_root_game_cannot_enter_is_refused()
    {
        using var game = new PlantedGame();
        string locked = game.Mod("maps", "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var (exit, output) = DryRun(game);

        Assert.NotEqual(0, exit);
        Assert.Contains("game/mods/tf2_descent/maps/locked", output);
    }

    [Fact]
    public void An_unknown_mod_is_refused()
    {
        using var game = new PlantedGame();
        var (exit, output) = DryRun(game, "--mod", "no_such_mod");

        Assert.NotEqual(0, exit);
        Assert.Contains("no_such_mod", output);
    }
}
