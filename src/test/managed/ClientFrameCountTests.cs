extern alias client;
using System;
using System.IO;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's frame counter is per-LEVEL, held as a fact about the source
/// because <c>ClientModule</c>'s <c>Frame</c> and <c>LevelInit</c> cannot run
/// without an initialised module — the same reason
/// <c>DebugChannelLifecycleSiteTests</c> scans it rather than driving it. The
/// counter itself is <see cref="SourceSharp.Client.ClientFrameStats"/> (it was
/// the static <c>ClientModule.s_frameCount</c>), and that part is driven below.
///
/// <para>
/// <b>THE BUG THIS EXISTS FOR.</b> The counter was incremented in
/// <c>Frame</c> and never reset. Two readers describe it as per-level —
/// <c>Commands.cs</c> prints "frames since level init" and
/// <c>LevelShutdown</c> prints "level shutdown after N frames" — so on the
/// SECOND and every later level both were reporting frames since the PROCESS
/// started.
/// </para>
///
/// <para>
/// Found by a lane that carried it as a level-init witness across three arms
/// and read <b>5161 / 12356 / 18238</b>, each of which began with a level
/// init. It never looked broken: the numbers are plausible, they increase, and
/// nothing fails. That is the whole hazard — a diagnostic quietly answering a
/// different question from the one it is labelled with is worse than no
/// diagnostic, because it is believed.
/// </para>
///
/// <para>
/// The intent of both readers is per-level, so the missing reset is the bug
/// rather than the labels. These facts pin the reset AND the two labels
/// together: if someone later decides the counter should be
/// process-lifetime, they have to change the labels in the same edit, and
/// these facts are what will tell them.
/// </para>
/// </summary>
public class ClientFrameCountTests
{
    private static string Read(string relative)
        => File.ReadAllText(RepoLayout.Sdk(relative));

    private static string ClientModule()
        => Read("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs");

    /// <summary>
    /// One method's body, by brace matching from its SIGNATURE and not from its
    /// name — <c>LevelInit</c> alone also matches the call
    /// <c>_tempEntPlayers?.LevelInit()</c>, the word in a comment, and
    /// <c>LevelInitPostEntity</c>. The neighbouring scanner learned that the
    /// hard way and says so.
    /// </summary>
    private static string BodyOf(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the source declares {signature}");

        int open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"{signature} has a body");

        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"{signature} has no closing brace");
    }

    [Fact]
    public void TheFrameCounterIsResetAtLevelInit()
    {
        // THE REGRESSION. Without this line the counter is process-lifetime and
        // both of its readers are lying about what they print.
        Assert.Contains("_stats.ResetFrames();",
                        BodyOf(ClientModule(), "public void LevelInit(string mapName)"));
    }

    [Fact]
    public void TheCounterIsStillCountedInFrame()
    {
        // The positive control, and it is not decoration: a "reset" fact is
        // satisfied by a counter that is reset and never incremented, which is
        // a counter that always reads zero. Both halves have to be there for
        // either to mean anything.
        Assert.Contains("_stats.CountFrame();",
                        BodyOf(ClientModule(), "public void Frame(float frameTime)"));
    }

    [Fact]
    public void TheResetIsNotAlsoDoneInFrame()
    {
        // The failure the two facts above cannot see between them: a reset
        // placed in Frame satisfies "it is reset somewhere" and makes the
        // counter permanently zero.
        Assert.DoesNotContain("_stats.ResetFrames();",
                              BodyOf(ClientModule(), "public void Frame(float frameTime)"));
    }

    [Fact]
    public void TheCounterCountsFramesAndResetsToZero()
    {
        // The two calls the source facts above look for, driven.
        var stats = new client::SourceSharp.Client.ClientFrameStats();

        stats.CountFrame();
        stats.CountFrame();
        Assert.Equal(2, stats.FrameCount);

        stats.ResetFrames();
        Assert.Equal(0, stats.FrameCount);
    }

    [Fact]
    public void TheServerCountsItsOwnTicks()
    {
        // The server's counter, cs_status's source, likewise per module now.
        var mine = new SourceSharp.Server.ServerTicks();
        var theirs = new SourceSharp.Server.ServerTicks();

        theirs.Advance();
        theirs.Advance();

        Assert.Equal(2, theirs.Count);
        Assert.Equal(0, mine.Count);
    }

    [Fact]
    public void TwoModulesCountTheirOwnFrames()
    {
        var mine = new client::SourceSharp.Client.ClientFrameStats();
        var theirs = new client::SourceSharp.Client.ClientFrameStats();

        theirs.CountFrame();
        theirs.CountCommand();

        Assert.Equal(0, mine.FrameCount);
        Assert.Equal(0, mine.CommandCount);
    }

    [Fact]
    public void BothReadersStillDescribeItAsPerLevel()
    {
        // The labels are half the fact. The reset is only correct BECAUSE these
        // two say per-level; if either is ever reworded to mean process
        // lifetime, this fails and whoever did it has to decide deliberately
        // rather than leave the two halves disagreeing.
        Assert.Contains("frames since level init",
                        Read("src/sourcesharp/managed/SourceSharp.Client/Commands.cs"));

        Assert.Contains("level shutdown after ",
                        BodyOf(ClientModule(), "public void LevelShutdown()"));
    }
}
