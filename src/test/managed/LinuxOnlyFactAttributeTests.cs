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
}
