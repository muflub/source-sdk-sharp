using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Every launch line this tree owns is unattended: it refuses the engine's
/// modal dialogs and skips the intro video (-novid). A fatal
/// Error() -- a managed side that refused to start, for one -- otherwise parks
/// the game on a dialog nobody sees, and it ignores SIGTERM: the engine's
/// handler only asks the blocked main loop to quit. tools/debug-api's list lacked
/// the two flags the Makefile and the live gate carry, and a refused startup
/// left a game running with no dev API until it was killed with -9.
/// </summary>
public class LaunchDialogTests
{
    private const string LiveGate = "tools/live-gate";
    private const string MakeFile = "Makefile";
    private const string DebugApi = "debug-api";

    private static string Read(string relative)
    {
        string path = Path.Combine(RepoLayout.SdkTree, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "missing: " + path);
        return File.ReadAllText(path);
    }

    // Shell and Python comment lines removed, so prose naming the flags cannot
    // satisfy the check.
    private static string Uncommented(string text)
        => string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

    [Fact]
    public void TheMakefilesRunArgsRefuseDialogs()
    {
        // The whole assignment: every line ending in a backslash continues it.
        var args = Regex.Match(Uncommented(Read(MakeFile)), @"^RUN_ARGS\s*\?=(?:[^\n]*\\\n)*[^\n]*", RegexOptions.Multiline);
        Assert.True(args.Success, "RUN_ARGS not found in the Makefile");
        Assert.Contains("-nomessagebox", args.Value);
        Assert.Contains("-nocrashdialog", args.Value);
        Assert.Contains("-novid", args.Value);
    }

    [Fact]
    public void EveryLiveGateLaunchLineRefusesDialogs()
    {
        string gate = Uncommented(Read(LiveGate));
        // Three launch builders: each list names both flags.
        Assert.Equal(3, Regex.Matches(gate, "\"-nomessagebox\"").Count);
        Assert.Equal(3, Regex.Matches(gate, "\"-nocrashdialog\"").Count);
        Assert.Equal(3, Regex.Matches(gate, "\"-novid\"").Count);
    }

    [Fact]
    public void EveryLaunchLineThisTreeOwnsRefusesModalDialogs()
    {
        // The launch line that drifts: tools/debug-api keeps its own GAME_ARGS.
        string debugApi = Uncommented(Read(DebugApi));
        var gameArgs = Regex.Match(debugApi, @"GAME_ARGS=\((.*?)\)", RegexOptions.Singleline);
        Assert.True(gameArgs.Success, "GAME_ARGS not found in debug-api");
        Assert.Contains("-nomessagebox", gameArgs.Groups[1].Value);
        Assert.Contains("-nocrashdialog", gameArgs.Groups[1].Value);
        Assert.Contains("-novid", gameArgs.Groups[1].Value);
    }
}
