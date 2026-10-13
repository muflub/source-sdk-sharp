using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The platform skip (src/test/common/LinuxOnlyFactAttribute.cs): it skips off
/// Linux, says why, and runs on Linux.
/// </summary>
public sealed class LinuxOnlyFactAttributeTests
{
    [Fact]
    public void Off_linux_a_fact_skips_naming_its_reason()
    {
        var fact = new LinuxOnlyFactAttribute("reads /proc", onLinux: false);
        Assert.Equal("Linux only: reads /proc", fact.Skip);
    }

    [Fact]
    public void Off_linux_a_theory_skips_naming_its_reason()
    {
        var theory = new LinuxOnlyTheoryAttribute("reads the Linux build's graph", onLinux: false);
        Assert.Equal("Linux only: reads the Linux build's graph", theory.Skip);
    }

    [Fact]
    public void On_linux_the_fact_and_the_theory_run()
    {
        Assert.Null(new LinuxOnlyFactAttribute("r", onLinux: true).Skip);
        Assert.Null(new LinuxOnlyTheoryAttribute("r", onLinux: true).Skip);
    }

    [Fact]
    public void The_reason_is_kept_whether_or_not_it_skips()
    {
        Assert.Equal("why", new LinuxOnlyFactAttribute("why", onLinux: true).Reason);
        Assert.Equal("why", new LinuxOnlyTheoryAttribute("why", onLinux: false).Reason);
    }

    [Fact]
    public void The_public_constructor_follows_this_process_os()
        => Assert.Equal(OperatingSystem.IsLinux(), new LinuxOnlyFactAttribute("r").Skip is null);

    [Fact]
    public void A_debug_build_fact_skips_off_linux_as_a_linux_only_one()
    {
        var fact = new LinuxDebugBuildFactAttribute("images the game", onLinux: false, onCi: true, built: () => true);
        Assert.Equal("Linux only: images the game", fact.Skip);
    }

    [Fact]
    public void On_ci_before_the_native_build_a_debug_build_fact_skips_saying_so()
    {
        var fact = new LinuxDebugBuildFactAttribute("r", onLinux: true, onCi: true, built: () => false);
        Assert.Equal(LinuxDebugBuildFactAttribute.BeforeTheBuild, fact.Skip);
    }

    [Fact]
    public void On_ci_after_the_native_build_a_debug_build_fact_runs()
        => Assert.Null(new LinuxDebugBuildFactAttribute("r", onLinux: true, onCi: true, built: () => true).Skip);

    [Fact]
    public void Off_ci_a_missing_build_is_not_a_skip_and_is_not_even_looked_for()
    {
        // The fact runs and fails with its own "build debug first" message:
        // only CI's ordering excuses an absent build.
        bool asked = false;
        var fact = new LinuxDebugBuildFactAttribute("r", onLinux: true, onCi: false, built: () => asked = true);
        Assert.Null(fact.Skip);
        Assert.False(asked);
    }

    [Fact]
    public void The_later_ci_step_filters_on_the_trait_the_facts_carry()
    {
        string workflow = File.ReadAllText(RepoLayout.Repo(".github", "workflows", "ci.yml"));
        Assert.Contains(
            $"--filter \"{LinuxDebugBuildFactAttribute.Trait}={LinuxDebugBuildFactAttribute.TraitValue}\"", workflow);
        Assert.Contains("Test (managed facts that read the native build)", workflow);
        Assert.EndsWith(Path.Combine("bin", "linux64", "server.so"), LinuxDebugBuildFactAttribute.Witness);
    }
}
