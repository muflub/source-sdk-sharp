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
}
