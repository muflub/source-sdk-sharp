using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// HLTV, SourceTV AND REPLAY ARE DROPPED — plan_shim.md step 5c.
/// </summary>
/// <remarks>
/// <para>
/// This file holds the two halves of that, and the second one matters more
/// than the first.
/// </para>
///
/// <para>
/// <b>THE DELETIONS STAY DELETED</b> — an absence ratchet over the four shim
/// files 5c edited. Storage for a feature nobody ships comes back by being
/// copied out of a stock file in the next port step, which is how it got here
/// the first time; a ratchet is the only thing that notices.
/// </para>
///
/// <para>
/// <b>THE VTABLE SLOTS STAY, IN ORDER</b> — and this is the dangerous edit
/// this file exists to prevent. <c>IServerGameClients::GetReplayDelay</c>,
/// <c>IBaseClientDLL::ReplayInit</c>, <c>IBaseClientDLL::ReplayPostInit</c> and
/// <c>IServerGameDLL::ReplayInit</c> are engine interface slots. The engine
/// holds the object by pointer and calls into it BY VTABLE POSITION, computed
/// from its own copy of the header — so deleting a method in the middle shifts
/// every slot after it and the engine's call to one of those lands in its
/// neighbour's body, with the wrong arguments.
/// </para>
///
/// <para>
/// <b>What that failure would look like is the reason this is asserted.</b>
/// There is no link error, no missing symbol and no "replay is not available"
/// in the log. Some unrelated method starts misbehaving. Anyone reading
/// "HLTV is dropped, delete the replay methods" and acting on it produces
/// exactly that, and every other test in this tree keeps passing.
/// </para>
///
/// <para>
/// <b>WHICH TREE THESE FACTS READ.</b> Every one of them resolves the repo
/// root ONCE, at the FIRST ancestor of the test binary holding
/// <c>src/buildsourcesharp</c>, and reports that path in the failure message.
/// An upward walk that keeps going past the first hit escapes a git worktree
/// into the main checkout — where a file this branch deleted still exists —
/// and every absence fact here would then be answering about the wrong tree
/// while looking perfectly green.
/// </para>
/// </remarks>
public sealed class HltvDroppedTests
{
    private const string LiveGate = "tools/live-gate";
    private const string MakeFile = "Makefile";
    private const string DebugApi = "debug-api";

    /// <summary>The file's text, with the tree it came from named on failure.</summary>
    private static string Read(string relative)
    {
        string root = RepoLayout.SdkTree;
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            "missing: " + path + "\n(repo root resolved to " + root + ")");
        return File.ReadAllText(path);
    }

    /// <summary>Where a file resolved to, for a failure message.</summary>
    private static string Where(string relative) =>
        Path.Combine(RepoLayout.SdkTree, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The text with <c>//</c> and <c>/* */</c> comments removed, so that
    /// PROSE describing what was deleted cannot satisfy an absence check.
    /// Without this every fact below is the check that cannot fail: 5c's own
    /// comments quote the code they replaced.
    /// </summary>
    private static string Code(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join("\n", text.Split('\n')
            .Select(l => { int at = l.IndexOf("//", StringComparison.Ordinal); return at < 0 ? l : l[..at]; }));
    }

    //=========================================================================
    // 1. THE VTABLE SLOTS. These come first because they matter most.
    //=========================================================================

    // ThePlayerInfoHltvAndReplayFlagsAreStillAnswered went with
    // shell_playerinfo.cpp (plan_server_native_min P1, Decision 1): no engine
    // binary asks for PlayerInfoManager002, so the flags' reader is gone with
    // the interface. CServerGameClients::GetPlayerState still writes
    // player->hltv / player->replay (shell_gameclients.cpp).

    //=========================================================================
    // 3. THE REFUSAL IS ENFORCED, NOT INCIDENTAL.
    //=========================================================================

    [Fact]
    public void EveryLaunchLineThisTreeOwnsRefusesHltv()
    {
        // WHY THIS EXISTS AT ALL. Before 5c, HLTV failed to start only because
        // this mod happens to ship no director module — and the engine still
        // built a SourceTV client on the way to finding that out
        // (`maxplayers set to 5 (extra slot was added for SourceTV)`,
        // `ClientPutInServer (server): SourceTV`). "We dropped it" has to be
        // something the launch line says.
        string make = Code(Read(MakeFile).Replace("#", "//"));
        Assert.Contains("-nohltv", make);
        Assert.Contains("+tv_enable 0", make);

        string gate = Read(LiveGate);
        Assert.Contains("def no_hltv_flags():", gate);
        Assert.Contains("return [\"-nohltv\", \"+tv_enable\", \"0\"]", gate);

        // Called by all THREE launch lines, not merely defined. A helper
        // nobody calls is exactly how this comes back.
        Assert.Equal(3, Regex.Matches(gate, @"\+ no_hltv_flags\(\)").Count);

        // AND THE FOURTH LAUNCH LINE, which is the one that drifts. ./debug-api
        // run keeps its own GAME_ARGS; that list had already drifted from
        // RUN_ARGS once (the window size), and its own comment says so.
        string debugApi = Read(DebugApi);
        Assert.Contains("-nohltv +tv_enable 0", debugApi);
    }

    [Fact]
    public void TheGateSelfTestStillAssertsTheHltvFlagsByValue()
    {
        // A membership test would be satisfied by `+tv_enable 1`, which is the
        // setting that puts the SourceTV client straight back.
        string gate = Read(LiveGate);

        Assert.Contains("no_hltv_flags() == [\"-nohltv\", \"+tv_enable\", \"0\"]", gate);
        Assert.Contains("_line[_line.index(\"+tv_enable\") + 1] == \"0\"", gate);
    }

    [Fact]
    public void TheGateStatesWhatHltvCoverageIsNow()
    {
        // The claim in COVERAGE_HLTV has to keep naming the engine's SourceTV
        // and Replay servers, because the point of the row is that those
        // paths EXIST and are unreachable — not that they went away. We do
        // not edit the engine.
        string gate = Read(LiveGate);
        int at = gate.IndexOf("COVERAGE_HLTV = (", StringComparison.Ordinal);
        Assert.True(at >= 0, "COVERAGE_HLTV is gone from " + Where(LiveGate));

        string row = gate[at..(at + 1400)];
        Assert.Contains("SourceTV and Replay servers", row);
        Assert.Contains("IServerGameClients::GetPlayerState", row);
        Assert.Contains("-nohltv", row);
    }
}
