using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The timestamped console log (con_logfile + con_timestamp). A dedicated
/// server writes it beside -condebug's console.log; a CLIENT's +con_logfile
/// replaces console.log instead, which left every console.log reader on stale
/// lines after make run/headless (found by the engineapi lane, 2026-10-02).
/// </summary>
public class TimedConsoleLogTests
{
    private static string Makefile() => File.ReadAllText(RepoLayout.Repo("Makefile"));

    private static string Assignment(string make, string name)
    {
        var m = Regex.Match(make, $@"^{name}\s*\?=\s*((?:.*\\\n)*.*)$", RegexOptions.Multiline);
        Assert.True(m.Success, $"{name} is not assigned in the Makefile");
        return m.Groups[1].Value;
    }

    /// <summary>A variable's value with every $(VAR) it names expanded, once, from the Makefile.</summary>
    private static string Expanded(string make, string name)
        => Regex.Replace(Assignment(make, name), @"\$\(([A-Z_]+)\)",
                         m => Regex.IsMatch(make, $@"^{m.Groups[1].Value}\s*\?=", RegexOptions.Multiline)
                             ? Assignment(make, m.Groups[1].Value) : m.Value);

    [Fact]
    public void A_client_launch_never_moves_the_console_log()
    {
        string run = Expanded(Makefile(), "RUN_ARGS");

        Assert.Contains("-condebug", run);
        Assert.DoesNotContain("con_logfile", run);
    }

    [Fact]
    public void A_dedicated_launch_also_writes_a_timed_log_of_its_own()
    {
        string dedicated = Expanded(Makefile(), "DEDICATED_ARGS");

        Assert.Contains("-condebug", dedicated);
        Assert.Contains("+con_timestamp 1", dedicated);

        string file = Regex.Match(dedicated, @"\+con_logfile\s+(\S+)").Groups[1].Value;
        Assert.NotEqual("", file);
        Assert.NotEqual("console.log", file);
    }
}
