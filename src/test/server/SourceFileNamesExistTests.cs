using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE SUBJECT OF A PARITY FACT MUST STILL EXIST.
///
/// <para>
/// <b>WHY.</b> Most of this suite's parity facts read a native source file by
/// name and compare it against the managed mirror. They all share one shape:
/// look the file up (once by walking above the test binary, now in the compat
/// farm), and if it is not there, do not run. That shape is right — the suite has to run from the deployed
/// managed directory, where there is no <c>src/</c> — but it has a hole this
/// port walks into by design, because <b>a port deletes files</b>. When the
/// file a fact exists to read is deleted, the lookup returns null, the fact
/// takes its not-found arm, and it keeps reporting green forever while
/// examining nothing. Nothing else in the build says a word: the name is a
/// string, so the compiler never sees it.
/// </para>
///
/// <para>
/// It has already happened. <c>LivenessMirrorTests</c> had three facts reading
/// <c>shell_standalone_scripted.cpp</c>; step 1f deleted that file and the
/// three went on passing, asserting nothing, until they were found by accident.
/// This fact is the cheap mechanical check that would have caught it the moment
/// 1f landed: every source-file name the suite's CODE writes as a whole string
/// literal has to name a file that is really in the tree.
/// </para>
///
/// <para>
/// <b>WHAT IS AND IS NOT CHECKED, and both halves are deliberate.</b> Comments
/// are stripped first, so the many doc comments citing stock files this port
/// has already deleted (<c>gameinterface.cpp:1049</c> and friends) are prose
/// and stay prose. Only a literal that is a file name END TO END is checked —
/// <c>"shell_standalone.h"</c> yes, <c>"cdll_client_int.cpp:3\tFoo"</c> no —
/// because the whole-literal shape is the lookup shape, and an expected-value
/// string that merely mentions a path is not a lookup.
/// </para>
///
/// <para>
/// <b>THIS FILE EXCLUDES ITSELF</b> from the scan, because it necessarily
/// contains names that do not resolve: the fixtures below, and
/// <see cref="Fixtures"/> itself. That would be a hole big enough to hide a
/// real lookup in, so <see cref="EveryNameOnTheFixtureListIsGenuinelyAbsent"/>
/// closes it from the other side: a name may sit on the fixture list only for
/// as long as it names nothing. Add a real file to the list to silence a red
/// and that fact goes red instead.
/// </para>
/// </summary>
public sealed partial class SourceFileNamesExistTests
{
    /// <summary>
    /// Names that are LABELS, not lookups: text handed to an in-memory parser
    /// so its output can carry a plausible file name. No disk access is
    /// involved and there is no file for them to name.
    ///
    /// <para>
    /// Every entry needs a reason, and
    /// <see cref="EveryNameOnTheFixtureListIsGenuinelyAbsent"/> keeps the list
    /// from growing into an excuse.
    /// </para>
    /// </summary>
    internal static readonly string[] Fixtures =
    {
        // (UserMessageHookScanTests' "a.cpp", "kept.cpp", "notxbox.cpp" and
        // "posix.cpp" left this list at Phase 6b with the vpc $File scanner
        // facts that fed them in; NotShippedTests' "taken_out.cpp" and "b.cpp"
        // likewise.)

        // DebugOnlyAliasSitesTests: the same, for the debug-only site scanner.
        "c.cpp",
        "s.cpp",
        "x.cpp",

        // DebugOnlyAliasSitesTests.TheScannerFindsAnIfdefDebugSite quotes the
        // stock body its scanner was written for. The port deleted the stock
        // file (only gameinterface.h survives); the fixture is the text, and
        // the name is what the scanner is asked to print beside it.
        "gameinterface.cpp",

        // EntityMotionPushTests.TheStepperSTwoHeadersAreDeleted names these two
        // in order to assert that they are GONE -- plan_entity.md step 2g moved
        // the rider-or-blocker rule and the mover's velocity arithmetic into
        // SourceSharp.Abi/MoverMath.cs and deleted both headers with their
        // gtests. A fact whose whole subject is an absence is the one shape this
        // scan cannot tell from a lookup that has gone vacuous, so the two names
        // sit here -- and EveryNameOnTheFixtureListIsGenuinelyAbsent then keeps
        // them honest from the other side: put either header back and BOTH facts
        // go red, which is the right number.
        "standalone_riders.h",
        "shell_mover_velocity.h",

        // ClientPhysicsEvictionTests and PhysicsSharedEvictionTests name these in
        // order to assert that they are GONE (the client physics leftovers,
        // 2026-10-18): fork game/client/physics.cpp off client.so, and the files
        // only it called deleted. Put any back and both facts go red.
        "game/client/physics.cpp",
        "ragdoll_library.cpp",
        "ragdoll_library.h",
        "shell_physics_shared_client.cpp",
        // BoneLeafEvictionTests, for the client bone leaf library (2026-10-18).
        "shell_bone_leaf_library.cpp",
        "bone_leaf_library.h",

        // UserMessageRegistryOwnershipTests asserts the native user-message
        // registry and dispatch are GONE from both builds (plan_side_isolation
        // P2); put either back and those facts go red.
        "shell_usermessage_registry.cpp",
        "shell_usermessages.cpp",

        // SoundscapeLibraryTests.BothSidesLoadTheLibraryAndNeitherHasANativeRegistry
        // asserts the server's native soundscape registry is GONE
        // (plan_server_novalve.md P2.4) and the client's (sound cluster 17,
        // lane c2-sims); put either back and that fact goes red.
        "shell_soundscape.cpp",
        "shell_soundscape_client.cpp",

        // NotShippedTests: the same shape again, and the largest instance of it
        // so far. plan_entity.md step 7g took thirteen stock client translation
        // units out of the build AND out of the tree -- this mod ships no
        // achievements, no replay, no HLTV camera, no rich presence, no Sixense
        // controller, no map overview and no bonus maps -- and
        // NotShippedTests.Evicted names all thirteen in order to assert that
        // they are GONE, from the client build and from the disk. Put any one
        // of them back and TWO facts go red: that one and this list's own
        // EveryNameOnTheFixtureListIsGenuinelyAbsent, which is the right number.
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

        // NotShippedTests.ToolFramework names these two in order to assert that
        // they are GONE from client.so and the farm: SFM is dead (user ruling
        // 2026-10-19). Put either back and those facts go red.
        "entity_client_tools.cpp",
        "toolframework_client.cpp",

        // BloodEffectPortabilityTests names this one in order to assert that it
        // is GONE. plan_shim.md Phase 2c, lane 2-utilshared, deleted
        // game/shared/util_shared.cpp: 1,031 of its 1,242 lines had zero
        // importers on either link line, and the two definitions that did
        // (`hl2_episodic` and `UTIL_BloodDrips`) moved into
        // sourcesharp/game/shared/util_shared_library.cpp, where that file's
        // other facts already read them. Both vpc rows went with it.
        //
        // Same shape as the two deleted headers above: a fact whose whole
        // subject is an absence is what this scan cannot distinguish from a
        // lookup that has gone vacuous. EveryNameOnTheFixtureListIsGenuinelyAbsent
        // keeps it honest from the other side -- put the file back and BOTH
        // this list's fact and BloodEffectPortabilityTests' two AssertAbsent
        // calls go red, which is the right number.
        "src/game/shared/util_shared.cpp",

        // LeafMathConVarTests, plan_shim.md Phase 2d, and the two shapes are
        // different.
        //
        // stringregistry.cpp is the EntityMotionPushTests shape: a fact whose
        // whole subject is an absence. Phase 2d deleted it -- CStringRegistry
        // was a CUtlDict<int> behind an opaque pointer and its last two
        // consumers stopped using it -- and
        // AnAnchoredAbsenceCheckDoesNotEscapeIntoTheCheckoutAbove names it in
        // order to assert that it is gone HERE while it is still present in
        // the main checkout this worktree hangs off. That contrast is the
        // point: a test helper that walks UP looking for a file finds the
        // other tree's copy and answers "still there", which is how a managed
        // absence check reported on a repository nobody was editing. Put the
        // file back and TWO facts go red, which is the right number.
        "src/public/stringregistry.cpp",

        // The other two are a deliberate NEGATIVE CONTROL -- a name chosen to
        // resolve to nothing, so that LeafMathConVarTests' reader can be shown
        // to REFUSE a missing path and to print the resolved path when it
        // does. A control that named a real file would pass by finding it.
        "src/sourcesharp/public/this_file_does_not_exist.cpp",
        "this_file_does_not_exist.cpp",

        // THE sdk-links SHRINK (Phase 7): the farm now links only the fork files
        // client.so, server.so and the Valve static libs compile, so every
        // stock file neither DLL builds is absent from it. These eighteen names
        // name such files and none of them is a lookup -- each fact below reads
        // a file that IS in the tree, or none at all:
        //
        //   * ABSENCE FROM A COMPILED-SOURCE SET (CMakeGraph): GameRulesPolicyTests
        //     (gamerules.cpp, GameStats.cpp), MultiplayerConVarTests' two
        //     NoLongerCompiles facts, PolicyKnobDeclarationTests (baseentity.cpp),
        //     TempEntBaseTableTests (basetempentity.cpp, c_basetempentity.cpp),
        //     UserMessageHookParityTests (haptics/haptic_msgs.cpp) and
        //     UserMessageRegistryOwnershipTests (usermessages.cpp and the three
        //     per-game usermessages lists). A file out of the build is out of the
        //     farm too, and the absence is the claim.
        //   * NEEDLES searched for in files that are there: HltvDroppedTests
        //     (the replay headers and hltvdirector.h, in the shim's code) and
        //     ContractBaseline's lifted needle `ai_networkmanager.cpp` (in
        //     src/server/native/CMakeLists.txt).
        //   * LABELS: DebugOnlyAliasSitesTests hands its scanner in-memory text
        //     under the names cdll_client_int.cpp and client.cpp, and
        //     TempEntClientPool2Tests expects `props.cpp` inside a message.
        //
        //   * SUFFIXES: CMakeGraphTests and NativeCommandTableTests match the
        //     debug-only diagnostics by the suffix `_debug.cpp` (user ruling
        //     2026-10-01); no file is named that.
        //
        // EveryNameOnTheFixtureListIsGenuinelyAbsent keeps each honest: put the
        // file back in the farm and that fact goes red.
        "_debug.cpp",
        "ai_networkmanager.cpp",
        "cdll_client_int.cpp",
        "client.cpp",
        "game/shared/gamerules.cpp",
        "GameStats.cpp",
        "replay/ireplaysystem.h",
        "replay/iserverreplaycontext.h",
        "hltvdirector.h",
        "multiplay_gamerules.cpp",
        "baseentity.cpp",
        "c_basetempentity.cpp",
        "basetempentity.cpp",
        "props.cpp",
        "haptics/haptic_msgs.cpp",
        "game/shared/usermessages.cpp",
        "hl2_usermessages.cpp",
        "tf_usermessages.cpp",
        "sdk_usermessages.cpp",

        // ABSENCE FROM BOTH COMPILED-SOURCE SETS AND THE TREE: SkeletonEvictionTests'
        // BoneLibraryLeftTheServer / BoneLibraryLeftTheClient (plan
        // physics_bones_native_zero 1.2). The file was deleted when the client's
        // skeleton followed the server's into managed code; the absence is the claim.
        "bone_library.cpp",
        // PlayerShadowTests.The_players_step_and_stress_are_managed: the stress
        // sum is PhysicsObjectStress's since plan_physics_bones_native_zero 2.3,
        // and the header's absence is that fact's subject.
        "object_stress.h",

        // ABSENCE FROM THE CLIENT'S COMPILED-SOURCE SET, ITS sdk-links AND THE
        // FARM: RagdollEvictionTests (plan physics_bones_native_zero Phase 3).
        // The client's ragdolls are SourceSharp.Client's; the absence is the claim.
        "ragdoll.cpp",
        "src/game/client/ragdoll.cpp",
        // RagdollEvictionTests: src/shared/native/ragdoll_shared.cpp was deleted
        // with Phase 3; its absence from both targets and the disk is the claim.
        "ragdoll_shared.cpp",
        // PhysicsSharedEvictionTests (plan_physics_bones_native_zero 2.6):
        // src/shared/native/physics_shared.cpp was deleted; its absence from
        // both targets and the disk is the claim.
        "physics_shared.cpp",
        // RagdollEvictionTests: src/shared/native/bone_setup.cpp left with it.
        "bone_setup.cpp",
        // SkeletonEvictionTests: src/shared/native/studio_sequence_timing.cpp was
        // deleted 2026-10-12; its absence from both targets and the disk is the claim.
        "studio_sequence_timing.cpp",
        // StudioEvictionTests: src/shared/native/model_descriptor.cpp (and its
        // .h) went with the descriptor slot; the descriptor is managed's.
        "model_descriptor.cpp",
        "model_descriptor.h",
        // LeafMathConVarTests / StudioEvictionTests: src/shared/native/studio.cpp
        // (farm path src/sourcesharp/public/studio.cpp) was deleted 2026-10-18;
        // its absence is the claim.
        "src/sourcesharp/public/studio.cpp",
        "studio.cpp",
        // StudioEvictionTests: src/shared/native/studio_mdlcache.cpp went after it.
        "studio_mdlcache.cpp",
    };

    /// <summary>The extensions that mean "a file in this repository".</summary>
    private static readonly Regex NameLiteral = new(
        "\"([A-Za-z0-9_][A-Za-z0-9_./-]*\\.(?:cpp|h|hpp|inc|cs|vpc))\"",
        RegexOptions.Compiled);

    private const string SelfFileName = "SourceFileNamesExistTests.cs";

    // ------------------------------------------------------------------
    // The facts
    // ------------------------------------------------------------------

    [RepoSourceFact("src/sourcesharp/managed/SourceSharp.Server.Tests/" + SelfFileName)]
    public void EverySourceFileNameTheSuiteLooksUpIsAFileThatExists()
    {
        string root = RepoLayout.SdkTree;
        var index = BuildIndex(root);

        var orphans = new List<string>();

        foreach ((string name, string where) in ScanSuite(root))
        {
            if (Fixtures.Contains(name, StringComparer.Ordinal))
                continue;

            if (!Resolves(root, index, name))
                orphans.Add($"{where} names '{name}'");
        }

        Assert.True(orphans.Count == 0,
            "a fact that reads a source file by name goes silently vacuous the day that file is "
            + "deleted — it takes its not-found arm and reports a pass forever. These names are in "
            + "the suite's code and name nothing in the tree; either the fact should have moved "
            + "with its subject, or the subject should still be there:\n  "
            + string.Join("\n  ", orphans));
    }

    [RepoSourceFact("src/sourcesharp/managed/SourceSharp.Server.Tests/" + SelfFileName)]
    public void EveryNameOnTheFixtureListIsGenuinelyAbsent()
    {
        string root = RepoLayout.SdkTree;
        var index = BuildIndex(root);

        string[] real = Fixtures.Where(n => Resolves(root, index, n)).ToArray();

        Assert.True(real.Length == 0,
            "the fixture list is the one way past the check above, so it may only hold names that "
            + "really name nothing. These name a file that exists, which means either the scan is "
            + "now skipping a real lookup or the entry has outlived its reason: "
            + string.Join(", ", real));
    }

    [Fact]
    public void TheScannerReadsCodeAndNotComments()
    {
        const string text = """
            // ServerCode("in_a_line_comment.h")
            /* ServerCode("in_a_block_comment.h") */
            /// <summary>ServerCode("in_a_doc_comment.h")</summary>
            string? code = ServerCode("in_the_code.h");
            """;

        Assert.Equal(
            new[] { "in_the_code.h" },
            NamesIn(text).Select(n => n.Name).ToArray());
    }

    [Fact]
    public void TheScannerTakesOnlyALiteralThatIsANameEndToEnd()
    {
        // The left one is a lookup. The right one is an EXPECTED VALUE that
        // merely mentions a file; checking it would flag every stock path this
        // port has deleted and the check would be turned off inside a week.
        const string text = """
            Assert.Equal("cdll_client_int.cpp:3\tC_BaseEntity::Foo", Scan("shell_standalone.h"));
            """;

        Assert.Equal(
            new[] { "shell_standalone.h" },
            NamesIn(text).Select(n => n.Name).ToArray());
    }

    [Fact]
    public void TheScannerReportsTheLineTheNameIsOn()
    {
        const string text = """
            class X
            {
                string? a = ServerCode("shell_standalone.h");
            }
            """;

        Assert.Equal(3, NamesIn(text).Single().Line);
    }

    [Fact]
    public void APlantedNameThatResolvesToNothingIsReported()
    {
        // The check's own red path, without touching the tree: a name of the
        // lookup shape that no file answers to.
        string root = RepoLayout.SdkTree;
        var index = BuildIndex(root);

        Assert.False(Resolves(root, index, "no_such_shell_file_anywhere.cpp"),
            "a name nothing answers to must not resolve, or the check above can never go red");

        Assert.True(Resolves(root, index, "shell_standalone.h"),
            "and a name that IS in the tree must resolve, or the check above is red for everything");
    }

    [Fact]
    public void ANameWithADirectoryPartMustMatchThatDirectory()
    {
        string root = RepoLayout.SdkTree;
        var index = BuildIndex(root);

        Assert.True(Resolves(root, index, "src/game/server/shell_standalone.h") is false,
            "shell_standalone.h is not under src/game/server, so the qualified name must not "
            + "resolve off a bare basename match");

        Assert.True(Resolves(root, index, "src/sourcesharp/game/server/shell_standalone.h"),
            "and the real path does");
    }

    // ------------------------------------------------------------------
    // The ratchet
    // ------------------------------------------------------------------

    /// <summary>
    /// How many <c>Assert.True(true, …)</c> / <c>Assert.False(false, …)</c>
    /// sites the managed tree's CODE holds. Comments do not count and must not:
    /// several of these appear in doc comments written by the lanes that
    /// removed a real one and explained why, so a grep says the number went UP
    /// when a site was fixed.
    ///
    /// <para>
    /// <b>WHY A PIN AND NOT A BAN.</b> Not every one is a defect. The
    /// legitimate shape is terminal — a loop that <c>Assert.Fail</c>s on each
    /// violation, then one of these to record that it found none — and there
    /// are a handful of those. The defective shape is
    /// <c>if (input is null) { Assert.True(true, "…skipped…"); return; }</c>,
    /// which reports <b>Passed</b>, not Skipped: xUnit never prints the message
    /// of an assertion that held, so a run that examined nothing and a run
    /// whose tables agreed produce byte-identical output. A gate demanding zero
    /// today would be switched off today; a pinned number cannot be satisfied
    /// by ignoring it.
    /// </para>
    ///
    /// <para>
    /// Going UP is red: write the skip as an attribute
    /// (<see cref="RepoSourceFactAttribute"/>,
    /// <see cref="SharedSourceFactAttribute"/>,
    /// <see cref="SandboxMapFactAttribute"/>,
    /// <see cref="ClientShellFactAttribute"/>) so the runner prints SKIPPED with
    /// a reason. Going DOWN is red until somebody lowers this number, which is
    /// the moment the improvement is visible in the diff.
    /// </para>
    ///
    /// <para>
    /// 108 TO 102 IN STEP 6b. <c>StandaloneMirrorTests</c> carried seven copies
    /// of the guard — one per write-site census, all seven asking for the same
    /// file — and the step was rewriting two of those censuses anyway, so they
    /// became <see cref="ClientShellFactAttribute"/>. Seven sites out, and the
    /// step's own third census went in as one of them rather than as an eighth
    /// copy.
    /// </para>
    ///
    /// <para>
    /// 102 TO 101 AT THE LANE 3F MERGE. <c>AbiParityTests</c>'
    /// <c>CheckEveryEngineSlotIsWired</c> opened with a not-found arm that ran
    /// <c>Assert.True(true, ...)</c> and returned, so a broken walk-up to
    /// <c>sourcesharp_gamesystem.cpp</c> made the whole wiring check pass while
    /// checking nothing — one level up from the null-pointer slot the check
    /// exists to catch. The file is always in the tree, so not-found is now the
    /// assertion rather than the excuse.
    /// </para>
    ///
    /// <para>
    /// 101 TO 100 IN STEP 8a. <c>AbiBothHierarchiesTests</c> went with the probe
    /// it tested. Its one vacuous site was the header-not-found arm of the
    /// standalone-only door — <c>Assert.True(true, "the ABI header was found
    /// (skipped, not found)")</c> — the same shape as the lane 3f entry above and
    /// with the same consequence: a walk-up that stopped finding
    /// <c>sourcesharp.h</c> passed the door while reading nothing. The count fell
    /// because a FILE left, not because a site was repaired, so nobody should
    /// read this drop as an improvement to a check that still exists.
    /// </para>
    ///
    /// <para>
    /// 100 TO 95 IN plan_shim.md PHASE 1f. Five facts went with their subject:
    /// <c>TempEntBeamClientTests</c> (two), <c>TempEntClientCnTests</c> (one) and
    /// <c>TempEntClientDecalTests</c> (two) each read a per-lane client forwarder
    /// off disk, and all nine <c>shell_te_fwd_c*.cpp</c> were deleted when the
    /// client's 52 forwards collapsed to the two anything still reaches. Each of
    /// the five opened with a not-found arm, so leaving them would not have failed
    /// — it would have passed forever while reading nothing, which is the exact
    /// shape this pin exists to make visible. The count fell because FACTS left
    /// with the code they pinned, not because a site was repaired.
    /// </para>
    ///
    /// <para>
    /// 95 TO 91 IN plan_sharp_repo.md PHASE 5. <c>HotTraceAuditTests</c> had four
    /// not-found arms on its walk up from the test binary; the tests now read
    /// the compat farm through <c>RepoLayout</c>, which throws when the farm is
    /// missing, so the arms had nothing left to catch and went. This one IS a
    /// repair: the four facts now fail rather than pass on an absent tree.
    /// </para>
    ///
    /// <para>
    /// 91 TO 90 IN PHASE 6b. <c>TempEntEffectDataTests.NeitherHl2NorTfIsDefinedInThisBuild</c>
    /// had a "source tree is not beside the binary (SKIPPED)" arm over its vpc
    /// walk. It reads the CMake build's compile commands now, through
    /// <c>CMakeGraph</c>, which throws on a missing build, so the arm went. A
    /// repair: the fact fails rather than passes on an absent build.
    /// </para>
    ///
    /// <para>
    /// 90 TO 67 AT THE sdk-links SHRINK. Twenty-three not-found arms went with
    /// the oracle facts that carried them into SourceSharp.OracleTests: the
    /// stock send tables of TempEntBeam/Debris/DecalSchema/EffectData/Frame/
    /// Gore/Odds/Schema/Sprites and AnimationEventCatalogTests' eventlist.cpp.
    /// The farm stopped linking those uncompiled files, so every one of the
    /// arms was passing while reading nothing. There they read the pristine
    /// Valve SDK through ValveSdk, which throws on a missing file, so the arms
    /// are gone rather than moved: a repair, not a file leaving.
    /// </para>
    ///
    /// <para>
    /// 67 TO 66 AT plan_engineapi_vtables E11: the game-description route fact
    /// reads the runtime binding by Repo path and asserts it exists.
    /// </para>
    ///
    /// <para>
    /// 66 TO 64 AT plan_engineapi_vtables E17b: ShellSlotWiringTests' two
    /// required-list facts, each of which passed on a missing file, left with
    /// the list.
    /// </para>
    /// </summary>
    /// 64 TO 63 IN THE SERVER GC CULL (2026-10-01): TempEntEffectDataTests'
    /// sendproxy.cpp read, and with it its SKIPPED branch, moved to the oracle
    /// (TempEntSurfacePropOracleTests reads stock's own lines).
    /// 63 TO 60 AT plan_client_vtables CC-last (2c): the engine API struct went,
    /// and with it the skip branches of the two struct-order append facts
    /// (GlobalVarsBindingTests, StandaloneMirrorTests) and of TempEntFizzTests'
    /// header read, which now reads the slot table.
    /// 60 TO 57 AT plan_client_vtables CE-last: the ManagedApi struct went, and
    /// with it the skip arms of the struct's declaration and order facts
    /// (AbiParityTests.Covers, SuppressionAuditTests' marker parity).
    /// 57 TO 53 IN P1 RUN 3 (2026-10-02): the four skip arms that read a file
    /// Phase 1 moves out of Abi now fail on a missing file (SideAuditTests'
    /// chain and per-assembly audit, EngineAreaTests' binding read,
    /// LivenessMirrorTests' directory read). A skip there would hide the move.
    /// 53 TO 51 AT the C2 beam port: TempEntBeamClientTests' two off-disk reads
    /// of the native beam seams (view_beams.cpp's endpoint reads, the
    /// backends' refusal) went with the seams, and their SKIPPED arms with them.
    /// 51 TO 50 AT the client bones flip (physics_bones_native_zero 1.2):
    /// StandaloneMirrorTests' side-audit read, vacuous when off disk, went with
    /// the StandaloneSequenceInfo slot it pinned.
    /// 50 TO 49 AT bone_library.cpp's eviction: AnimationLayerTests' read of
    /// anim_layer_rule.h went with the header (the count is AnimLayerRule's).
    /// 49 TO 48 AT plan_server_native_v2 T2: AbiParityTests'
    /// TheGibShotStructMatchesTheHeader and its not-found arm went with the
    /// native SourceSharpGibShot.
    private const int VacuousAssertionSites = 48;

    private static readonly Regex Vacuous = new(
        @"\bAssert\s*\.\s*(?:True\s*\(\s*true|False\s*\(\s*false)\b",
        RegexOptions.Compiled);

    [RepoSourceFact("src/sourcesharp/managed/SourceSharp.Server.Tests/" + SelfFileName)]
    public void TheVacuousAssertionCountOnlyGoesDown()
    {
        string root = RepoLayout.SdkTree;
        List<string> sites = VacuousSites(root);

        Assert.True(sites.Count == VacuousAssertionSites,
            $"an assertion whose truth does not depend on anything the program did. Pinned at "
            + $"{VacuousAssertionSites}, found {sites.Count}. UP: use a Skip-setting attribute so "
            + "the runner prints SKIPPED instead of a pass nobody earned. DOWN: lower the pin in "
            + "SourceFileNamesExistTests, which is where the improvement becomes visible.\n  "
            + string.Join("\n  ", sites));
    }

    /// <summary>Every vacuous-assertion site in the managed tree's code.</summary>
    internal static List<string> VacuousSites(string root)
    {
        var sites = new List<string>();
        string managed = Path.Combine(root, "src", "sourcesharp", "managed");

        foreach (string path in Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(managed, path).Replace(Path.DirectorySeparatorChar, '/');

            // Build output holds generated and copied sources; a site counted
            // there is the same site counted twice.
            if (rel.Contains("/bin/", StringComparison.Ordinal)
             || rel.Contains("/obj/", StringComparison.Ordinal))
                continue;

            string code = StripCommentsAndLiterals(File.ReadAllText(path));

            foreach (Match m in Vacuous.Matches(code))
            {
                int line = 1;
                for (int i = 0; i < m.Index; i++)
                {
                    if (code[i] == '\n')
                        line++;
                }

                sites.Add($"{rel}:{line}");
            }
        }

        return sites;
    }

    [Fact]
    public void TheRatchetCountsCodeAndNotComments()
    {
        // The reason the count is not a grep: a lane that removes a site and
        // writes down why leaves the token behind in prose, and a grep reads
        // that as the number going UP.
        const string text = """
            /// Replaced the Assert.True(true, "skipped") this fact used to have.
            // Assert.False(false, "not this one either");
            Assert.True(true, "this one");
            """;

        Assert.Single(Vacuous.Matches(StripCommentsAndLiterals(text)));
    }

    // ------------------------------------------------------------------
    // The scanner
    // ------------------------------------------------------------------

    /// <summary>Every whole-literal source-file name in the suite's code.</summary>
    internal static IEnumerable<(string Name, string Where)> ScanSuite(string root)
    {
        // The suite is four directories since the side split (plan_side_isolation
        // P6): the cross-side project, each side's, and the helpers they link.
        foreach (string path in TestDirectories(root)
                                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                                .OrderBy(p => p, StringComparer.Ordinal))
        {
            string file = Path.GetFileName(path);

            if (file == SelfFileName)
                continue;

            foreach ((string name, int line) in NamesIn(File.ReadAllText(path)))
                yield return (name, $"{file}:{line}");
        }
    }

    /// <summary>The names in one C# source text, with their line numbers.</summary>
    internal static List<(string Name, int Line)> NamesIn(string text)
    {
        string code = StripComments(text);
        var found = new List<(string, int)>();

        foreach (Match m in NameLiteral.Matches(code))
        {
            int line = 1;
            for (int i = 0; i < m.Index; i++)
            {
                if (code[i] == '\n')
                    line++;
            }

            found.Add((m.Groups[1].Value, line));
        }

        return found;
    }

    // ------------------------------------------------------------------
    // Resolution
    // ------------------------------------------------------------------

    /// <summary>Basename to every repo-relative path carrying it, under src/ and tools/.</summary>
    internal static Dictionary<string, List<string>> BuildIndex(string root)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string top in new[] { "src", "tools" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
                continue;

            Walk(root, start, index);
        }

        return index;
    }

    private static void Walk(string root, string dir, Dictionary<string, List<string>> index)
    {
        foreach (string file in Directory.EnumerateFiles(dir))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

            if (!index.TryGetValue(Path.GetFileName(file), out List<string>? paths))
                index[Path.GetFileName(file)] = paths = new List<string>();

            paths.Add(rel);
        }

        foreach (string sub in Directory.EnumerateDirectories(dir))
        {
            string name = Path.GetFileName(sub);

            // Build output and version control: a name that resolves only
            // inside obj/ resolves to a copy, not to a source file.
            if (name is "bin" or "obj" or "node_modules")
                continue;

            // Every dot-directory: version control (.git, .vs) and scratch space
            // (tools/.npm-tmp, .npm-cache, .test-legs) -- nothing tracked lives in
            // one. Scratch is also written WHILE this walk runs: the ui leg's npm
            // creates and deletes tools/.npm-tmp/<random> in parallel, and a
            // directory that vanished mid-walk failed this test (2026-09-30).
            if (name.StartsWith('.'))
                continue;

            Walk(root, sub, index);
        }
    }

    /// <summary>
    /// A bare name resolves if anything in the tree carries it; a name with a
    /// directory part has to match that part too, so a qualified path cannot be
    /// answered by a same-named file somewhere else entirely.
    /// </summary>
    internal static bool Resolves(string root, Dictionary<string, List<string>> index, string name)
    {
        string basename = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;

        if (!index.TryGetValue(basename, out List<string>? paths))
            return false;

        if (!name.Contains('/'))
            return true;

        return paths.Any(p => p == name || p.EndsWith("/" + name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheIndexSkipsDotDirectoriesWhereScratchSpaceLives()
    {
        string root = Directory.CreateTempSubdirectory("ss-index-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tools", ".npm-tmp", "x"));
            File.WriteAllText(Path.Combine(root, "tools", ".npm-tmp", "x", "scratch.cpp"), "");
            File.WriteAllText(Path.Combine(root, "tools", "real.cpp"), "");

            Dictionary<string, List<string>> index = BuildIndex(root);

            Assert.True(index.ContainsKey("real.cpp"), "the walk found nothing: the control failed");
            Assert.False(index.ContainsKey("scratch.cpp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
