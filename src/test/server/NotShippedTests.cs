using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What this mod does NOT ship — plan_entity.md step 7g.
/// </summary>
/// <remarks>
/// <para>
/// The step removes thirteen stock client translation units on the grounds that
/// this mod ships no achievements, no replay, no HLTV camera, no rich presence,
/// no Sixense controller, no map overview and no bonus maps, and rewrites
/// <c>entity_client_tools.cpp</c> as an <c>IClientTools</c> refusal stub.
/// </para>
///
/// <para>
/// WHY THIS IS ASSERTED ON THE MANAGED SIDE AS WELL. The gtests in
/// <c>sourcesharp/test/shell_notshipped_test.cpp</c> run the LEDGER — that a
/// reach counts, that a declared name is found, that an undeclared one is not.
/// This file asserts the MEMBERSHIP: that the thirteen really left the client
/// build (<c>client_sourcesharp</c>, <c>src/client/native/CMakeLists.txt</c>;
/// they left <c>client_csharp.vpc</c> at step 7g, before VPC was replaced), and
/// that the three things a tool-free game still needs are still there. A ledger that is perfectly correct about a list nobody removed
/// anything from is the check that cannot fail.
/// </para>
///
/// <para>
/// READ OFF THE CMAKE BUILD. Membership is <see cref="CMakeGraph.CompiledSources"/>:
/// compile_commands.json, which must equal the target's CMakeLists list exactly,
/// and which throws on a missing, empty or stale build — so an absence here can
/// never pass over a list that was not read. The VPC-parser facts that used to
/// sit here (a <c>-$File</c> removal is not an addition; a continued
/// <c>$File</c> line is joined) went with the vpc: a CMake source list has
/// neither form.
/// </para>
/// </remarks>
public sealed class NotShippedTests
{
    /// <summary>The thirteen translation units step 7g took out of the build.</summary>
    public static readonly string[] Evicted =
    {
        "replay/cdll_replay.cpp",
        "replay/replaycamera.cpp",
        "basepresence.cpp",
        "basepresence_xbox.cpp",
        "hltvcamera.cpp",
        "achievement_saverestore.cpp",
        "achievementmgr.cpp",
        "achievements_hlx.cpp",
        "baseachievement.cpp",
        "point_bonusmaps_accessor.cpp",
        "sixense/in_sixense.cpp",
        "sixense/sixense_convars.cpp",
        "game_controls/MapOverview.cpp",
    };

    /// <summary>
    /// A target's compiled sources, farm-relative (<c>game/client/hltvcamera.cpp</c>).
    /// An evicted name is relative to <c>game/client</c> or <c>game/shared</c>,
    /// so the facts match it as a path SUFFIX, case-insensitively, as the vpc
    /// facts did.
    /// </summary>
    private static bool Compiles(IReadOnlySet<string> sources, string suffix) =>
        sources.Any(p => p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheThirteenNotShippedFilesAreOutOfTheClientBuild()
    {
        var client = CMakeGraph.CompiledSources(CMakeGraph.Client);

        // Proof of read, beside the absences: the tool bridge the client keeps.
        Assert.True(Compiles(client, "client/entity_client_tools.cpp"));

        foreach (string evicted in Evicted)
            Assert.False(Compiles(client, evicted), evicted + " is compiled into client_sourcesharp again");
    }

    [Fact]
    public void TheThirteenNotShippedFilesAreGoneFromTheTree()
    {
        // Out of the build AND deleted, which is the difference between "not
        // shipped" and "not currently compiled". gib.cpp went the same way at
        // step 4a and game/shared/gamestats.cpp before it.
        //
        // ROOTED, NOT SEARCHED, and the difference is not style. A search that
        // walks UP from the test binary until a path exists is right for
        // "find me this file" and catastrophically wrong for "prove this file is
        // gone": a lane worktree lives at <repo>/.claude/worktrees/<name>, so the
        // walk leaves the worktree and finds the MAIN tree's copy of a file the
        // lane deleted. This fact failed exactly that way when it was first
        // written. The root is the compat farm every other fact here reads,
        // where an evicted file has no link, so all of them answer about one tree.
        string root = RepoLayout.SdkTree;

        string[] dirs =
        {
            Path.Combine(root, "src", "game", "client"),
            Path.Combine(root, "src", "game", "shared"),
        };

        foreach (string evicted in Evicted)
        {
            foreach (string dir in dirs)
            {
                string path = Path.Combine(dir, evicted.Replace('/', Path.DirectorySeparatorChar));
                Assert.False(File.Exists(path), path + " is back in the tree");
            }
        }
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheRootIsThisTreeAndNotWhicheverAncestorHasASrcDirectory()
    {
        // The mutation check for the fact above: the root must be the tree that
        // owns the build the other facts read, and a file that IS still shipped
        // must be findable under it. A root that walked one level too far would
        // satisfy the second half too -- so the first pins the farm's own CMake
        // compile database, which only the owning tree has at that path. (It
        // pinned client_csharp.vpc until VPC was replaced.)
        string root = RepoLayout.SdkTree;

        Assert.True(File.Exists(Path.Combine(root, "build", "compile_commands.json")));
        Assert.True(File.Exists(Path.Combine(root, "src", "game", "client", "entity_client_tools.cpp")));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheServerBuildNeverHadThemAndStillDoesNot()
    {
        // The control. Step 7g touched the client project only, so a server
        // number moving would mean something else changed.
        var server = CMakeGraph.CompiledSources(CMakeGraph.Server);

        // Proof of read: a shared file the server does compile.
        Assert.Contains("sourcesharp/game/shared/shell_movement.cpp", server);

        foreach (string evicted in Evicted)
            Assert.False(Compiles(server, evicted), evicted + " is compiled into server_sourcesharp");
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheToolBridgeStaysInTheBuildAsARefusalStub()
    {
        // NOT evicted, and the exception is the point of the step's own wording
        // ("entity_client_tools.cpp (an IClientTools refusal stub)"). Twenty-two
        // in-game call sites dereference `clienttools`, and a tool that attaches
        // must be refused by name rather than told the export is missing.
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, "game/client/entity_client_tools.cpp"),
            "client_sourcesharp (src/client/native/CMakeLists.txt) no longer compiles game/client/entity_client_tools.cpp");
    }

    [Fact]
    public void TheToolStubStillExportsTheInterfaceAndTheGlobal()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "game", "client", "entity_client_tools.cpp"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);

        Assert.Contains("EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CClientTools, IClientTools, VCLIENTTOOLS_INTERFACE_VERSION", text);
        Assert.Contains("IClientTools *clienttools = &s_ClientTools;", text);
    }

    [Fact]
    public void TheToolStubKeepsTheTwoMethodsAGameWithoutToolsReaches()
    {
        // IsInRecordingMode and EnableRecordingMode are stock's own bodies and
        // must NOT refuse: c_te.cpp:537/:681 save the flag, clear it, play a
        // temp entity back and restore it, and refusing either half leaves that
        // sequence unbalanced.
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "game", "client", "entity_client_tools.cpp"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);

        Assert.Contains("m_bInRecordingMode = bEnable;", text);
        Assert.Contains("return m_bInRecordingMode;", text);
        Assert.DoesNotContain("SS_TOOLS_REFUSE( \"IsInRecordingMode\" )", text);
        Assert.DoesNotContain("SS_TOOLS_REFUSE( \"EnableRecordingMode\" )", text);
    }

    [Fact]
    public void TheToolStubRefusesEverythingElseByName()
    {
        // A count rather than a spot check, because the failure worth catching
        // is a method quietly losing its refusal and returning a plausible zero.
        // IClientTools declares 47 pure virtuals in
        // public/toolframework/itoolentity.h; two of them answer (above), so 45
        // refuse. The number is derived here rather than pinned so that adding a
        // method to the interface moves it honestly:
        string? iface = RepoLayout.FindSdkFile(Path.Combine("src", "public", "toolframework", "itoolentity.h"));
        Assert.NotNull(iface);

        string ifaceText = File.ReadAllText(iface!);
        int start = ifaceText.IndexOf("class IClientTools : public IBaseInterface", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = ifaceText.IndexOf("#define VCLIENTTOOLS_INTERFACE_VERSION", start, StringComparison.Ordinal);
        Assert.True(end > start);

        int pureVirtuals = System.Text.RegularExpressions.Regex
            .Matches(ifaceText[start..end], @"virtual[^;{}]*=\s*0\s*;")
            .Count;
        Assert.True(pureVirtuals > 40, $"IClientTools parsed as {pureVirtuals} pure virtuals");

        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "game", "client", "entity_client_tools.cpp"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);

        int refusals = System.Text.RegularExpressions.Regex
            .Matches(text, @"SS_TOOLS_REFUSE\( ""[A-Za-z]+"" \);")
            .Count;

        Assert.Equal(pureVirtuals - 2, refusals);
    }

    [Fact]
    public void TheOneInterfaceThatLeftTheExportSetIsDeclaredNotShipped()
    {
        // The gate's sentence: an interface the engine can ask for is answered
        // or REFUSED BY NAME. Replay is the only name that left client.so's
        // export set at 7g, and shell_notshipped.h is where the refusal and its
        // reason live.
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "game", "shared", "shell_notshipped.h"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);

        Assert.Contains("\"ClientReplay001\"", text);
        Assert.Contains("mod directory is tf or tf_beta", text);
    }

    [Fact]
    public void TheHltvBranchesRefuseByNameRatherThanVanishing()
    {
        // §7: a behaviour that is refused says so. Every site where the HLTV arm
        // used to do something the ordinary arm does not now calls
        // SourceSharp_HltvCameraAvailable(), which asks engine->IsHLTV() and
        // records the refusal when the answer would have mattered.
        var expected = new Dictionary<string, int>
        {
            // view.cpp's SetUpViews until C7 step 3; CShellViewRender's since.
            [Path.Combine("src", "sourcesharp", "game", "client", "shell_viewrender_client.cpp")] = 1,
        };
        // SpectatorGUI.cpp's six sites left with it (plan_client_novalve C3, lane ui):
        // the spec_* commands are managed (SpectatorCommandRunner) and forward to the
        // server for every client, which is the arm those sites always took.

        foreach ((string rel, int count) in expected)
        {
            string? path = RepoLayout.FindSdkFile(rel);
            Assert.NotNull(path);

            int found = File.ReadAllLines(path!)
                .Select(l => l.Trim())
                .Count(l => !l.StartsWith("//", StringComparison.Ordinal)
                            && l.Contains("SourceSharp_HltvCameraAvailable()", StringComparison.Ordinal));

            Assert.Equal(count, found);
        }
    }

    [Fact]
    public void TheHltvAnswerReallyAsksTheEngineBeforeRefusing()
    {
        // Answering a flat false without asking would make the ledger read zero
        // for a session that really was watching a broadcast — which is exactly
        // the reading that has to be trustworthy.
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "game", "shared", "shell_diagnostics.cpp"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);

        int at = text.IndexOf("bool SourceSharp_HltvCameraAvailable()", StringComparison.Ordinal);
        Assert.True(at >= 0);

        string body = text[at..(at + 600)];
        Assert.Contains("engine->IsHLTV()", body);
        Assert.Contains("SourceSharp_NotShipped(", body);
        Assert.Contains("return false;", body);
    }

    // COM_GetModDirectory's last client caller, viewrender.cpp's TF DX8
    // refusal, left with that file at C7 step 3; nm found no importer and the
    // definition went (docs/client_evictions.md). This holds that it stays gone.
    [Fact]
    public void TheModDirectoryReaderIsGoneWithItsLastCaller()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "game", "shared", "shell_shared.cpp"));
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);
        // The file the scan reads is the right one. (Anchored on
        // SourceSharp_WeaponPellets, then SourceSharp_RegisterWeaponType, until
        // plan_server_native_v2 T2 deleted each: no reader in either DLL.)
        Assert.Contains("int32_t SourceSharp_FilesSizeOnPath(", text);
        Assert.DoesNotContain("COM_GetModDirectory()", text);
    }
}
