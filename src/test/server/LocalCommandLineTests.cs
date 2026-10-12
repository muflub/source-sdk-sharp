using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The process' own command line, read without the engine.
///
/// <para>
/// Both sides read it: the engine's command-line slots were filled on the
/// server only, answered a plausible <c>false</c> on the client, and are retired.
/// </para>
/// </summary>
public class LocalCommandLineTests
{
    private static readonly string[] Line =
        { "/opt/game/hl2_linux", "-game", "mod_sharp", "-debugui", "-debuguihost", "10.0.0.5" };

    [Fact]
    public void APresentParameterIsFound()
    {
        Assert.True(LocalCommandLine.Has(Line, "-debugui"), "the flag is on the line");
    }

    [Fact]
    public void AnAbsentParameterIsNotFound()
    {
        Assert.True(!LocalCommandLine.Has(Line, "-debugapi"), "this one is not");
    }

    /// <summary>
    /// A parameter must be a whole argument, not a prefix of one.
    ///
    /// <para>
    /// This tree has lost time to prefix matches — a needle for
    /// <c>/prediction</c> matching <c>/predictionXX</c>, a <c>"class CWorld"</c>
    /// anchor matching <c>CWorldCollideable</c>. Here it would mean a client
    /// launched with the SERVER's <c>-debuguiserve</c> reading itself as having
    /// asked for a channel, which is precisely the confusion between the two
    /// opt-ins that the design depends on not happening.
    /// </para>
    /// </summary>
    [Fact]
    public void AParameterIsAWholeArgumentAndNotAPrefix()
    {
        Assert.True(!LocalCommandLine.Has(new[] { "hl2", "-debuguiserve" }, "-debugui"),
            "-debugui is a prefix of -debuguiserve, and they are different sides' flags");
    }

    [Fact]
    public void MatchingIsCaseInsensitiveLikeTheEnginesOwn()
    {
        Assert.True(LocalCommandLine.Has(new[] { "hl2", "-DebugUI" }, "-debugui"),
            "ICommandLine::FindParm compares case-insensitively, and a managed reader that " +
            "did not would disagree with the engine about the same command line");
    }

    [Fact]
    public void AValueIsTheArgumentAfterItsParameter()
    {
        Assert.True(LocalCommandLine.Value(Line, "-debuguihost", "127.0.0.1") == "10.0.0.5",
            "which is how the engine reads one too");
    }

    [Fact]
    public void AnAbsentParameterGetsTheFallback()
    {
        Assert.True(LocalCommandLine.Value(Line, "-debuguiport", "27110") == "27110",
            "the fallback is the answer, not an empty string somebody has to check for");
    }

    [Fact]
    public void AParameterAtTheEndOfTheLineGetsTheFallbackRatherThanReadingPastIt()
    {
        Assert.True(LocalCommandLine.Value(new[] { "hl2", "-debuguihost" }, "-debuguihost", "127.0.0.1")
                    == "127.0.0.1",
            "there is no argument after it, and reading one would be the first out-of-range " +
            "access on this path");
    }

    [Fact]
    public void AParameterFollowedByAnotherFlagGetsTheFallback()
    {
        Assert.True(LocalCommandLine.Value(new[] { "hl2", "-debuguihost", "-debugui" },
                                           "-debuguihost", "127.0.0.1") == "127.0.0.1",
            "'-debuguihost -debugui' means somebody left the host off, not that the host is " +
            "called '-debugui'");
    }

    [Fact]
    public void AParameterFollowedByAConsoleCommandGetsTheFallback()
    {
        // Stock's ParmValue and the server's native reader (ss_command_line.cpp)
        // both refuse a value that starts with '+': '-tickrate +map x' left the
        // rate off, it did not ask for a rate of "+map".
        Assert.True(LocalCommandLine.Value(new[] { "hl2", "-tickrate", "+map", "dm_lockdown" },
                                           "-tickrate", "0") == "0",
            "'-tickrate +map' means the rate was left off");
    }

    [Fact]
    public void AReadingIsKeptByItsOwnerAndByNoOtherReader()
    {
        // Cached per reader, which is the service the client module registers:
        // the same reader answers the same list, and a second reader reads for
        // itself rather than sharing a process-wide cache.
        var line = new ProcessCommandLine();

        Assert.Same(line.Args, line.Args);
        Assert.NotSame(line.Args, new ProcessCommandLine().Args);
    }

    [Fact]
    public void TheProcessLineIsTheRunningProcessesOwn()
    {
        var line = new ProcessCommandLine();

        Assert.True(line.Args.Count > 0
                 && line.Args[0].Length > 0,
            "argv[0] is always there, which is what says this is reading the real process " +
            "rather than an empty list");
    }

    /// <summary>
    /// IT IS <c>/proc/self/cmdline</c> AND NOT <c>GetCommandLineArgs</c>, and
    /// that is the difference between working in the game and not.
    ///
    /// <para>
    /// The runtime this mod loads is not the process' entry point —
    /// <c>sourcesharp.cpp</c> starts CoreCLR as a COMPONENT of a game already
    /// running, and a component runtime is never handed a command line. A live
    /// run proved it: the server logged that it had offered the channel, the
    /// client logged nothing, and <c>-debugui</c> had been on the line all along.
    /// </para>
    ///
    /// <para>
    /// The test host here IS an entry point, so both readings agree on argv[0].
    /// What this holds is that the arguments come from the kernel's copy: it
    /// compares against <c>/proc/self/cmdline</c> read independently, so a
    /// silent fall back to the environment reading is a difference somewhere in
    /// the list rather than an agreement.
    /// </para>
    /// </summary>
    [Fact]
    public void TheProcessLineIsTheKernelsCopyAndNotTheRuntimesIdeaOfIt()
    {
        // NOT OPTIONAL, so this fails rather than skipping. /proc/self/cmdline
        // is not an incidental way of checking the answer -- it IS the
        // mechanism under test, the one LocalCommandLine reads because a
        // component runtime is never handed a command line. This tree builds
        // and runs on Linux only. If the file is absent, the production code
        // cannot work either, and Assert.True(true) would report PASSED for a
        // platform where the feature is broken.
        Assert.True(File.Exists("/proc/self/cmdline"),
            "/proc/self/cmdline is missing. It is what LocalCommandLine reads, so its " +
            "absence is a broken platform for this feature, not a test that does not apply.");

        string[] kernel = File.ReadAllText("/proc/self/cmdline")
                              .Split('\0', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(new ProcessCommandLine().Args.SequenceEqual(kernel),
            "argv as the kernel holds it, argument for argument — which is the reading that " +
            "is still true inside a natively-hosted runtime");
    }

    // SOURCESHARP_ARGS: the launcher moves SourceSharp's own flags off the
    // engine's command line (libtier0 caps it at 512 bytes) into this variable,
    // one argument per line. They read as if they followed argv.

    [Fact]
    public void LaunchArgsFollowArgv()
    {
        Assert.Equal(new[] { "hl2", "-novid", "-debugapi", "-debuguiport", "0" },
                     ProcessCommandLine.WithLaunchArgs(new[] { "hl2", "-novid" }, "-debugapi\n-debuguiport\n0"));
    }

    [Fact]
    public void NoLaunchArgsLeaveArgvAlone()
    {
        Assert.Equal(new[] { "hl2" }, ProcessCommandLine.WithLaunchArgs(new[] { "hl2" }, null));
        Assert.Equal(new[] { "hl2" }, ProcessCommandLine.WithLaunchArgs(new[] { "hl2" }, ""));
    }

    [Fact]
    public void EmptyLaunchArgLinesAreNotArguments()
    {
        Assert.Equal(new[] { "hl2", "-a", "-b" }, ProcessCommandLine.WithLaunchArgs(new[] { "hl2" }, "-a\n\n-b\n"));
    }

    [Fact]
    public void AFlagThatCameThroughTheLaunchArgsIsFound()
    {
        var line = ProcessCommandLine.WithLaunchArgs(new[] { "hl2" }, "-debugapi");
        Assert.True(LocalCommandLine.Has(line, "-debugapi"), "the moved flag is seen");
    }

    [Fact]
    public void TheLaunchArgsVariableIsTheLaunchersAndTheNativeReaders()
    {
        // src/launcher/Launcher.cs writes it; ss_command_line.cpp reads it too
        // (its gtest pins the same name).
        Assert.Equal("SOURCESHARP_ARGS", ProcessCommandLine.LaunchArgsVariable);
        Assert.Equal(ProcessCommandLine.LaunchArgsVariable, SourceSharp.Launcher.Launcher.LaunchArgsVariable);
    }
}
