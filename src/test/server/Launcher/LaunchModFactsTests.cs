using System.Diagnostics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Makefile's launch seam (stage 0.2): `run`, `run-debug` and `headless`
/// take the mod from RUN_MOD_ID (default `test`), so a TF2 launch is
/// `make headless RUN_MOD_ID=tf2` and the snapshot follows the same variable.
/// Read from `make -n`, which expands without starting the game.
/// </summary>
public class LaunchModFactsTests
{
    private static string MakeDryRun(string target, string runModId)
    {
        var psi = new ProcessStartInfo("make",
            ["-s", "-n", target, $"RUN_MOD_ID={runModId}", "CONFIG=debug"])
        {
            WorkingDirectory = RepoLayout.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        Assert.True(p.ExitCode == 0,
            $"make -n {target} RUN_MOD_ID={runModId} exited {p.ExitCode}:\n{stderr[..Math.Min(400, stderr.Length)]}");
        return stdout;
    }

    /// <summary>The gpu-offload line of the headless launch recipe.</summary>
    private static string LaunchLine(string target, string runModId) =>
        Assert.Single(MakeDryRun(target, runModId).Split('\n'), l => l.Contains("gpu-offload"));

    [Theory]
    [InlineData("tf2")]
    [InlineData("test")]
    public void Headless_launches_the_named_mod(string mod) =>
        Assert.Contains($"-mod {mod} ", LaunchLine("headless", mod) + " ");

    [Fact]
    public void The_two_arms_differ()
    {
        // Before ≠ after: the stimulus reaches the recipe; if RUN_MOD_ID were
        // ignored, both arms would print the same line and this would fail.
        Assert.DoesNotContain("-mod tf2", LaunchLine("headless", "test"));
        Assert.DoesNotContain("-mod test", LaunchLine("headless", "tf2"));
    }

    [Fact]
    public void The_default_is_the_test_mod()
    {
        var psi = new ProcessStartInfo("make", ["-s", "-n", "headless", "CONFIG=debug"])
        {
            WorkingDirectory = RepoLayout.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        Assert.True(p.ExitCode == 0, "make -n headless exited " + p.ExitCode);
        string line = Assert.Single(stdout.Split('\n'), l => l.Contains("gpu-offload"));
        Assert.Contains("-mod test ", line);
    }

    [Fact]
    public void Run_snapshots_the_named_mod_directory()
    {
        // run-snapshot's source is $(MOD), which is built from RUN_MOD_ID: the
        // expanded recipe must copy from mods/tf2, not the hard-wired mods/test.
        Assert.Contains("/bin/debug/mods/tf2", MakeDryRun("run-snapshot", "tf2"));
    }

    /// <summary>
    /// The sibling mods each mod's gameinfo.txt mounts (<c>|gameinfo_path|../tf2</c>), read
    /// from the source tree: mod id (game/mod_&lt;id&gt;) to the names it climbs one level to.
    /// </summary>
    private static IEnumerable<(string Mod, string Sibling)> SiblingMounts()
    {
        foreach (string dir in Directory.GetDirectories(RepoLayout.Repo("game"), "mod_*"))
        {
            string info = Path.Combine(dir, "gameinfo.txt");
            if (!File.Exists(info))
                continue;
            string mod = Path.GetFileName(dir)["mod_".Length..];
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(info), @"\|gameinfo_path\|\.\./([A-Za-z0-9_]+)\s"))
                yield return (mod, m.Groups[1].Value);
        }
    }

    [Fact]
    public void Run_snapshot_links_every_sibling_mod_a_gameinfo_mounts()
    {
        // make run hands the engine a snapshot directory (tools/.run/<id>/mod) whose
        // gameinfo.txt is a link, and the engine resolves |gameinfo_path| against the
        // snapshot, so "../tf2" names tools/.run/<id>/tf2. Without that link
        // tf2_descent ran without the TF2 mod's files: its main menu drew
        // #TF2_MainMenu_* tokens (2026-11-15). Positive arm: tf2 mounts ../test.
        var mounts = SiblingMounts().ToList();
        Assert.Contains(("tf2", "test"), mounts);
        Assert.Contains(("tf2_descent", "tf2"), mounts);
        foreach (var group in mounts.GroupBy(m => m.Mod))
        {
            string recipe = MakeDryRun("run-snapshot", group.Key);
            foreach (var (mod, sibling) in group)
                Assert.True(
                    System.Text.RegularExpressions.Regex.IsMatch(recipe,
                        $@"ln -sfn \S+/mods/{sibling} ""?\S+/tools/\.run/[^/\s]+/{sibling}""?(\s|;|$)", System.Text.RegularExpressions.RegexOptions.Multiline),
                    $"run-snapshot for {mod} does not link ../{sibling}, which its gameinfo.txt mounts");
        }
    }
}
