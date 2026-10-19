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
/// no Sixense controller, no map overview and no bonus maps, and rewrote
/// <c>entity_client_tools.cpp</c> as an <c>IClientTools</c> refusal stub. That
/// stub and <c>toolframework_client.cpp</c> left client.so on 2026-10-19 (user
/// ruling: "SFM is dead, remove it"); <c>shell_tools_off_client.cpp</c> answers
/// "tools are off" for the Valve TUs that still name the tool framework.
/// </para>
///
/// <para>
/// WHY THIS IS ASSERTED ON THE MANAGED SIDE AS WELL. The gtests in
/// <c>sourcesharp/test/shell_notshipped_test.cpp</c> run the LEDGER — that a
/// reach counts, that a declared name is found, that an undeclared one is not.
/// This file asserts the MEMBERSHIP: that the thirteen really left the client
/// build (<c>client_sourcesharp</c>, <c>src/client/native/CMakeLists.txt</c>;
/// they left <c>client_csharp.vpc</c> at step 7g, before VPC was replaced), and
/// that the tool framework left with them. A ledger that is perfectly correct about a list nobody removed
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
    /// <summary>The tool framework's two TUs, out of the build since 2026-10-19 (SFM is dead).</summary>
    public static readonly string[] ToolFramework =
    {
        "entity_client_tools.cpp",
        "toolframework_client.cpp",
    };

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

        // Proof of read, beside the absences: the tools-off stand-ins the client keeps.
        Assert.True(Compiles(client, "client/shell_tools_off_client.cpp"));

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
        Assert.True(File.Exists(Path.Combine(root, "src", "sourcesharp", "game", "client", "shell_clientdll.cpp")));
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
    public void TheToolFrameworkLeftTheClientBuild()
    {
        // SFM is dead (user ruling 2026-10-19): the refusal stub and the
        // framework bridge left, and the stand-ins took their names.
        var client = CMakeGraph.CompiledSources(CMakeGraph.Client);
        Assert.True(Compiles(client, "client/shell_tools_off_client.cpp"),
            "client_sourcesharp does not compile shell_tools_off_client.cpp, and clientshadowmgr.cpp still names what it defines");
        foreach (string gone in ToolFramework)
            Assert.False(Compiles(client, gone), gone + " is compiled into client_sourcesharp again");
    }

    [Fact]
    public void TheToolFrameworkIsGoneFromTheFarm()
    {
        // The farm holds only what the DLLs compile; rooted, as above.
        string root = RepoLayout.SdkTree;
        Assert.True(File.Exists(Path.Combine(root, "src", "sourcesharp", "game", "client", "shell_tools_off_client.cpp")));
        foreach (string gone in ToolFramework)
            Assert.False(File.Exists(Path.Combine(root, "src", "game", "client", gone)), gone + " is back in the farm");
    }

    [Fact]
    public void TheToolsOffStandInDefinesOnlyWhatClientShadowMgrNamesAndExportsNoInterface()
    {
        // Only a tool asks for VCLIENTTOOLS001, and no
        // binary of the install names it, so nothing exports it. `clienttools`
        // and ToolFramework_PostToolMessage left with flashlighteffect.cpp
        // (C7 cluster 12); clientshadowmgr.cpp names the two that stay.
        string text = File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_tools_off_client.cpp"));
        string code = System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", "");

        Assert.Contains("bool ToolsEnabled()\n{\n\treturn false;\n}", code);
        Assert.Contains("void ToolFramework_RecordMaterialParams( IMaterial * )\n{\n}", code);
        Assert.DoesNotContain("clienttools", code);
        Assert.DoesNotContain("IClientTools", code);
        Assert.DoesNotContain("ToolFramework_PostToolMessage", code);
        Assert.DoesNotContain("EXPOSE_SINGLE_INTERFACE", code);
        Assert.DoesNotContain("VCLIENTTOOLS_INTERFACE_VERSION", code);
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
        Assert.Contains("ConVar default_fov(", text);   // FilesSizeOnPath folded 2026-10-19
        Assert.DoesNotContain("COM_GetModDirectory()", text);
    }
}
