using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_tmp.md Part 8's <c>BloodEffect</c> row — WHY it is still native, as a
/// measurement rather than as a paragraph.
///
/// <para>
/// Lane S5 declined this row and wrote the reason down: <c>UTIL_BloodDrips</c>
/// "carries <c>UTIL_ShouldShowBlood</c> (two violence convars and
/// <c>engine-&gt;IsLowViolence</c>)". Lane SRV was sent to re-ask that question
/// because <c>ClientIsLowViolence</c> had landed since, and found the premise
/// is not what the code does: <b><c>UTIL_ShouldShowBlood</c> never calls
/// <c>UTIL_IsLowViolence</c> and never reaches <c>engine-&gt;IsLowViolence()</c>
/// at all.</b> The server's blood gate is two convar reads, and
/// <see cref="IEngineConsole.FindConVarValue"/> has been able to make them since
/// long before this port.
/// </para>
///
/// <para>
/// THE ROW WAS THEN BLOCKED ON ONE TERM neither S5 nor the roadmap named:
/// <c>g_pGameRules-&gt;IsMultiplayer()</c>, which scales the blood amount by
/// five. At the time there was no managed route to it.
/// </para>
///
/// <para>
/// <b>THAT BLOCKER IS GONE AND THIS SUMMARY HAD NOT CAUGHT UP.</b> See
/// <see cref="IsMultiplayerIsTrueAndIsNowDecidedByThePortRatherThanByAStockBase"/>
/// below, which has asserted the flip for some time: C# is the DECLARATION of
/// this answer. <c>GameRulesPolicy.Multiplayer</c> is pushed native through
/// <c>RegisterGameRulesPolicy</c> and <c>CSourceSharpGameRules::IsMultiplayer</c>
/// reads <c>s_Policy.bMultiplayer</c>, so a managed copy would read the single
/// source of the answer rather than hardcoding a base class's — which is what
/// Lane CD's rule (a stub that happens to be right turns a working capability
/// into an untracked gap) was actually about.
/// </para>
///
/// <para>
/// <b>WHAT BLOCKS IT NOW, measured by plan_shim.md Phase 2c, lane
/// 2-utilshared.</b> Two of <c>UTIL_BloodDrips</c>' four callees are still
/// native and belong to other lanes' Phase 3 steps: <c>UTIL_Smoke</c> is lane
/// S3's and <c>UTIL_BloodImpact</c> is lane H2's. A managed UTIL_BloodDrips
/// today would need one new ABI slot per callee and both lanes would retire
/// them again inside the phase, so the family crosses as a family, after its
/// callees. Phase 2c moved the body next to <c>UTIL_BloodImpact</c> in
/// <see cref="ViolenceHome"/> — server arm only, because the client has never
/// called it — and deleted <c>game/shared/util_shared.cpp</c>.
/// </para>
///
/// <para>
/// WHAT THIS FILE IS FOR. Every clause above is a claim about stock's own
/// <c>.cpp</c> and is asserted against that file <b>read off disk</b>, which is
/// the only kind of check the lane contract trusts for a claim about native
/// code — comparing a C# comment with a C# comment is comparing a computation
/// with itself. If any of it stops being true — most usefully, if a lane makes
/// <c>IsMultiplayer</c> reachable — a test here fails and names the row as now
/// portable.
/// </para>
/// </summary>
public class BloodEffectPortabilityTests
{
    /// <summary>
    /// Where the violence family lives since plan_entity.md step 7f. It used to
    /// be <c>src/game/shared/util_shared.cpp</c>; the four ConVars are file
    /// statics, so the predicates and the storage had to move together.
    /// </summary>
    private const string ViolenceHome = "src/sourcesharp/game/shared/util_shared_library.cpp";

    // UTIL_IsLowViolence's home since plan_server_native_v2 T2 (2026-10-18): it
    // was #ifdef CLIENT_DLL in ViolenceHome, so it moved to the client's utility
    // library with refs of its own to the same four convars.
    private const string LowViolenceHome = "src/sourcesharp/game/client/client_util_library.cpp";

    // -----------------------------------------------------------------------
    // The gate: what UTIL_ShouldShowBlood actually reads.
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>UTIL_ShouldShowBlood</c> is <b>two convar reads and nothing else</b>.
    /// This is the assertion that corrects S5's note and the roadmap's question
    /// in one: the deciding term is not <c>engine-&gt;IsLowViolence()</c>,
    /// because that call is in <c>UTIL_IsLowViolence</c>, which is a DIFFERENT
    /// function that the blood path never calls.
    /// </summary>
    [Fact]
    public void TheServersBloodGateIsTwoConvarsAndNeverReachesEngineIsLowViolence()
    {
        // REPOINTED at step 7f, and the repoint carries an extra assertion
        // rather than fewer: the violence family moved to
        // sourcesharp/game/shared/util_shared_library.cpp, and a move that
        // leaves a copy behind would pass a repointed read while the game ran
        // the copy. So the old address is asserted empty first.
        //
        // AND AT plan_shim.md PHASE 2c, lane 2-utilshared, THE OLD ADDRESS
        // STOPPED EXISTING: game/shared/util_shared.cpp was deleted, both vpc
        // rows with it. The claim is the same and is now stronger.
        AssertAbsent("src/game/shared/util_shared.cpp");

        string body = Body(ViolenceHome, "bool UTIL_ShouldShowBlood(");

        Assert.Contains("violence_hblood.GetBool()", body, StringComparison.Ordinal);
        Assert.Contains("violence_ablood.GetBool()", body, StringComparison.Ordinal);
        Assert.Contains("DONT_BLEED", body, StringComparison.Ordinal);

        Assert.DoesNotContain("IsLowViolence", body, StringComparison.Ordinal);

        // And the function that DOES reach the engine is a separate one, so the
        // absence above is a fact about the call graph rather than about how
        // this test happened to slice the file.
        string lowViolence = Body(LowViolenceHome, "bool UTIL_IsLowViolence(");
        Assert.DoesNotContain("bool UTIL_IsLowViolence(", Read(ViolenceHome), StringComparison.Ordinal);

        Assert.Contains("engine->IsLowViolence()", lowViolence, StringComparison.Ordinal);
        Assert.Contains("violence_hgibs", lowViolence, StringComparison.Ordinal);
        Assert.Contains("violence_agibs", lowViolence, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing on the server's blood path calls <c>UTIL_IsLowViolence</c> — not
    /// <c>UTIL_BloodDrips</c>, not <c>UTIL_BloodStream</c>, not
    /// <c>UTIL_BloodSpray</c>, and not the ABI shim that calls all three.
    /// </summary>
    [Fact]
    public void NoArmOfBloodEffectCallsUtilIsLowViolence()
    {
        var arms = new (string File, string Signature)[]
        {
            // plan_shim.md Phase 2c, lane 2-utilshared: UTIL_BloodDrips moved
            // out of game/shared/util_shared.cpp (deleted) into the shim
            // library, SERVER ARM ONLY -- the client never called it, and on
            // the client its UTIL_Smoke callee is stock's. A repoint, not a
            // relaxation: TheStockFileIsGoneEntirely below is the assertion
            // that the old address did not keep a copy.
            (ViolenceHome,                                           "void UTIL_BloodDrips("),
            // Phase 4 exit, lane p4-util: UTIL_BloodStream left game/server/util.cpp
            // for the shim with the other two temp-entity emitters. REPOINTED,
            // not relaxed. (TheBloodStreamArmIsNoLongerAlsoInUtilCpp, which read
            // util.cpp to prove the old address kept no copy, went when the
            // farm stopped linking util.cpp: neither DLL compiles it, so a copy
            // left there could not link anywhere.)
            ("src/sourcesharp/game/server/server_util_library.cpp",   "void UTIL_BloodStream("),
            // Phase 1b: shell_entity_orphans.cpp merged into shell_absent.cpp,
            // which is the one file the three absent-stub files became. The body
            // moved verbatim; this is a repoint, not a relaxation.
            ("src/sourcesharp/game/server/shell_absent.cpp",          "void UTIL_BloodSpray("),
            ("src/sourcesharp/game/server/shell_worldquery.cpp",      "void SourceSharp_BloodEffect("),
        };

        foreach (var (file, signature) in arms)
        {
            string body = Body(file, signature);

            Assert.False(body.Contains("UTIL_IsLowViolence", StringComparison.Ordinal),
                signature + " in " + file + " reaches UTIL_IsLowViolence, which would put "
              + "engine->IsLowViolence() back on the blood path and change this row's answer");
        }
    }

    // -----------------------------------------------------------------------
    // The four branches a faithful port would have to carry, and which one has
    // no managed route.
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>UTIL_BloodDrips</c>' whole rule, pinned term by term. Three of the
    /// four decisions are reachable from C# today; the fourth is the row.
    /// </summary>
    [Fact]
    public void UtilBloodDripsCarriesFourDecisionsAndOnlyOneHasNoManagedRoute()
    {
        string body = Body(ViolenceHome, "void UTIL_BloodDrips(");

        // 1 — the violence gate. Reachable: IEngineCore.FindConVarValue.
        Assert.Contains("UTIL_ShouldShowBlood( color )", body, StringComparison.Ordinal);

        // 2 — the German substitution. g_Language is a REPLICATED ConVar, and
        // it is sourcesharp/game/shared/shell_gamerules.cpp:80's now rather
        // than gamerules.cpp's -- which is half of why UTIL_BloodDrips could
        // move at all. It is a convar read either way and is reachable the same
        // way. Lane H1's Phase 3e step turns THIS site into a
        // ManagedConVarRef; the site is in the shim library now, not in
        // util_shared.cpp. plan_server_novalve.md P2.3b took that step: the
        // read is through s_Language, a by-name ref on the C# declaration.
        Assert.Contains("s_Language.GetInt() == LANGUAGE_GERMAN", body, StringComparison.Ordinal);

        // 3 — the one this file was built around, and the premise has changed.
        // See TheIsMultiplayerRouteRunsTheOtherWay below: C# is the DECLARATION
        // of this answer, not a reader that has no route to it.
        Assert.Contains("g_pGameRules->IsMultiplayer()", body, StringComparison.Ordinal);
        Assert.Contains("amount *= 5", body, StringComparison.Ordinal);

        // 4 — the mech branch: sparks and a coin-flip puff of smoke instead of
        // blood. Both halves are temp entities and both are reachable.
        Assert.Contains("color == BLOOD_COLOR_MECH", body, StringComparison.Ordinal);
        Assert.Contains("ss::Effects()->Sparks(origin)", body, StringComparison.Ordinal);
        Assert.Contains("UTIL_Smoke(origin", body, StringComparison.Ordinal);
        Assert.Contains("UTIL_BloodImpact( origin, direction, color, amount )", body,
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// The convar half of the gate really is convars, so "reachable through
    /// <see cref="IEngineConsole.FindConVarValue"/>" is a claim with something
    /// behind it rather than an assumption about names.
    /// </summary>
    [Fact]
    public void TheViolenceConvarsAndTheLanguageOneAreDeclaredAsConvars()
    {
        // REPOINTED AGAIN by lane policy-knobs, and once more since: the four
        // used to be FILE STATICS in util_shared_library.cpp, then managed
        // declarations, and are now declared by neither -- the engine defines
        // them (convar_ownership.h). What is left in the C++ is a by-name ref
        // into the engine's registry.
        //
        // The claim this paragraph defends is unchanged and is now checked at
        // three addresses instead of two: the setting must EXIST (declared in
        // managed code), the predicates must still READ it (a ref in the shim),
        // and no second native declaration may have come back — in the shim or
        // in stock's util_shared.cpp — because a convar declared twice is
        // neither a compile error nor a link error and nothing says which one
        // the game read.
        string violence = Read(ViolenceHome);
        AssertAbsent("src/game/shared/util_shared.cpp");
        string managed  = Read("src/sourcesharp/managed/SourceSharp.Server/ConVars.cs")
                        + Read("src/sourcesharp/managed/SourceSharp.Client/ConVars.cs");

        foreach (string name in new[] { "violence_hblood", "violence_ablood",
                                        "violence_hgibs", "violence_agibs" })
        {
            // Declared NOWHERE in managed code: the engine defines all four, and
            // a declaration would alias the engine's own (convar_ownership.h).
            Assert.Equal(0, CountOf(managed, "[ConVar(\"" + name + "\""));

            // Still read where the predicates are.
            Assert.Contains("CManagedConVarRef\t" + name + "( \"" + name + "\" )", violence,
                            StringComparison.Ordinal);

            // And declared natively nowhere.
            Assert.DoesNotContain("ConVar\t" + name + "( \"" + name + "\"", violence,
                                  StringComparison.Ordinal);
        }

        // REPOINTED by plan_shim.md Phase 2c, lane 2-utilshared, and the repoint
        // is the point rather than housekeeping. This used to read
        // game/shared/gamerules.cpp, which has a row in NEITHER vpc -- so it was
        // asserting about a file the game does not compile, and the declaration
        // the game actually registers is the shim's. Reading the live one is
        // what makes the sentence above ("this one is a convar read too") true
        // of the build rather than of the archive.
        //
        // It is also half of why UTIL_BloodDrips could move at all: the old
        // refusal said the body reaches g_Language in a stock file the client
        // compiles, and it does not, because shell_gamerules.cpp is in both
        // vpcs.
        //
        // plan_server_novalve.md P2.3b moved the declaration to C#: it is a
        // [ConVar] with stock's name, default and flag, the shim declares it
        // natively nowhere, and the blood predicates read it by name.
        Assert.Contains("[ConVar(\"g_Language\", \"0\", Flags = ConVarFlags.Replicated)]",
                        Read("src/sourcesharp/managed/SourceSharp.Server/ConVars.cs"),
                        StringComparison.Ordinal);
        Assert.DoesNotContain("ConVar g_Language(",
                              Read("src/sourcesharp/game/shared/shell_gamerules.cpp"),
                              StringComparison.Ordinal);
        Assert.Contains("static CManagedConVarRef s_Language( \"g_Language\" );", violence,
                        StringComparison.Ordinal);

        // And the managed side really can read one. Named by reflection rather
        // than by eye: a renamed member would otherwise leave this paragraph
        // asserting something that no longer exists.
        Assert.NotNull(typeof(IEngineConsole).GetMethod(nameof(IEngineConsole.FindConVarValue)));
    }

    // -----------------------------------------------------------------------
    // Why the missing term cannot simply be assumed.
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>IsMultiplayer()</c> is <c>return true</c> in this build, and BloodEffect
    /// still may not hardcode that — but the reason has changed, and the change
    /// is the interesting part.
    ///
    /// <para>
    /// <b>It used to be true because of a base class chosen somewhere else.</b>
    /// <c>CSourceSharpGameRules</c> inherited <c>CMultiplayRules</c>, whose
    /// <c>IsMultiplayer</c> is <c>return true</c>, and nothing about this mod
    /// said so — a file about deathmatch rules decided it, and swapping the base
    /// would have changed the answer with nothing to notice.
    /// </para>
    ///
    /// <para>
    /// <b>It is now decided in the shim</b>, by an override written for this
    /// mod, as one of the eight that took the last reachable answers off
    /// <c>CMultiplayRules</c>. That is strictly better and it does NOT make the
    /// value hardcodable in C#: the reason a managed BloodEffect cannot assume
    /// it WAS that <b>no ABI slot answered it</b>, and that is what the last
    /// assertion here used to guard. When a slot appears, the row can be
    /// ported.
    /// </para>
    ///
    /// <para>
    /// <b>That has now happened, and it went further than the promise.</b>
    /// SourceSharpGameRulesPolicy carries the value, but it is C# that declares
    /// it — <c>GameRulesPolicy.Multiplayer</c> — and native holds a copy pushed
    /// at startup. So a managed BloodEffect does not need an ABI read either:
    /// it reads a constant in its own assembly. The last assertion records that
    /// rather than guarding a blocker that is gone. The row itself is
    /// plan_tmp.md Part 8's to port.
    /// </para>
    /// </summary>
    [Fact]
    public void IsMultiplayerIsTrueAndIsNowDecidedByThePortRatherThanByAStockBase()
    {
        string shell = Read("src/sourcesharp/game/shared/shell_gamerules.h");
        string shellCpp = Read("src/sourcesharp/game/shared/shell_gamerules.cpp");

        Assert.Contains("IsMultiplayer( void ) OVERRIDE", shell, StringComparison.Ordinal);

        // ...and it answers true. Read from the shim's own body rather than
        // assumed from the fact that an override exists: an override that
        // returned false would satisfy the line above and change the game.
        string body = Body("src/sourcesharp/game/shared/shell_gamerules.cpp",
                           "bool CSourceSharpGameRules::IsMultiplayer(");
        Assert.Contains("return true;", body, StringComparison.Ordinal);

        // The stock answer it replaced (CMultiplayRules::IsMultiplayer returns
        // true) is no longer asserted here: multiplay_gamerules.cpp left the
        // farm with the sdk-links shrink, since neither DLL compiles it, and
        // this assertion's own note said it should go when the file did.

        // THE LOAD-BEARING ONE HAS FLIPPED, AND THIS RECORDS IT RATHER THAN
        // RELAXING IT.
        //
        // This assertion used to read "no ABI slot answers IsMultiplayer, so a
        // managed BloodEffect cannot read the value and may not assume it", and
        // the docstring above promised: "when a slot appears, the row can be
        // ported". A slot has appeared -- SourceSharpGameRulesPolicy carries
        // bMultiplayer -- and the value arrived somewhere better than an ABI
        // read: C# DECLARES it. GameRulesPolicy.Multiplayer is the authority
        // and native holds a pushed copy, so a managed BloodEffect reads a
        // constant in its own assembly and needs no crossing at all.
        //
        // SourceSharp.Server holds BOTH GameRulesPolicy and
        // EffectsOverTempEnts, so the visibility really is there; that is
        // asserted below rather than asserted about the header, because the
        // header is no longer the thing that decides it.
        //
        // THE PORT ITSELF IS NOT DONE HERE. plan_tmp.md Part 8 owns the
        // BloodEffect row and this change did not come from that lane. What
        // this file can honestly say is that the blocker it was written to
        // guard is gone.
        Assert.True(GameRulesPolicy.Multiplayer,
            "GameRulesPolicy.Multiplayer is the managed declaration of what "
          + "IsMultiplayer answers. If it stops being true, the shim override "
          + "checked above stops agreeing with it and one of the two is wrong.");

        Assert.True(typeof(GameRulesPolicy).Assembly == typeof(EffectsOverTempEnts).Assembly,
            "GameRulesPolicy and EffectsOverTempEnts are no longer in the same "
          + "assembly, so a managed BloodEffect cannot see the constant after "
          + "all and the blocker this file guards is back -- as a visibility "
          + "problem rather than an ABI one.");
    }

    // -----------------------------------------------------------------------
    // And the consequence: the row is still forwarded, deliberately.
    // -----------------------------------------------------------------------

    /// <summary>
    /// <see cref="EffectsOverTempEnts"/> does not override
    /// <c>BloodEffect</c>. Asserted by reflection on the declaring type, so it
    /// is a fact about the class rather than about a call that happened to
    /// reach the engine in one arrangement of arguments.
    /// </summary>
    [Fact]
    public void BloodEffectIsStillInheritedFromTheForwarderAndNotOverridden()
    {
        var member = typeof(EffectsOverTempEnts)
            .GetMethod(nameof(IEngineEffects.BloodEffect));

        Assert.NotNull(member);
        Assert.True(member!.DeclaringType == typeof(EngineEffectsForwarder),
            "BloodEffect is declared on " + member.DeclaringType?.Name + ". If it moved to "
          + "EffectsOverTempEnts, this file's whole argument has been overtaken and every "
          + "assertion in it should be re-read rather than deleted");

        // The eight that DID move, for contrast — the same reflection question,
        // answered the other way. A test that only ever asserts "not
        // overridden" would pass on a class that overrode nothing at all.
        foreach (string moved in new[] { "SparkEffect", "ExplosionEffect", "MuzzleFlashEffect",
                                         "FunnelEffect", "DustEffect", "ImpactEffect",
                                         "SplashEffect", "TracerEffect" })
        {
            Assert.True(typeof(EffectsOverTempEnts).GetMethod(moved)!.DeclaringType
                            == typeof(EffectsOverTempEnts),
                moved + " is no longer overridden by EffectsOverTempEnts");
        }
    }

    // -----------------------------------------------------------------------
    // Reading stock off disk.
    // -----------------------------------------------------------------------

    private static string Read(string relative)
    {
        string path = RepoLayout.Sdk(relative);

        Assert.True(File.Exists(path), "cannot find " + relative + " in " + RepoLayout.SdkTree);

        return File.ReadAllText(path);
    }

    /// <summary>
    /// One function's body, from the line its signature starts on to the brace
    /// that closes it. Braces are counted rather than a closing brace being
    /// looked for in column zero, because several of these end <c>}</c> followed
    /// by trailing whitespace.
    /// </summary>
    private static string Body(string relative, string signature)
    {
        string text = Read(relative);

        int start = text.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start >= 0, "no `" + signature + "` in " + relative
                              + " — the function was renamed or moved, and this test is now "
                              + "asserting nothing");

        int open = text.IndexOf('{', start);

        Assert.True(open >= 0, "no body for `" + signature + "` in " + relative);

        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[start..(i + 1)];
        }

        Assert.Fail("unterminated body for `" + signature + "` in " + relative);

        return string.Empty;
    }

    /// <summary>
    /// The file is not in the tree. Used where a path this file used to read was
    /// DELETED rather than repointed: an assertion that a needle is missing from
    /// a file that does not exist is vacuous, so the absence is taken of the
    /// path itself and <see cref="Read"/> still refuses a missing file
    /// everywhere else.
    ///
    /// <para>
    /// <b>ANCHORED TO ONE TREE, AND THAT IS THE WHOLE POINT OF IT.</b> The
    /// absence is read in the compat farm (<see cref="RepoLayout.SdkTree"/>),
    /// where an evicted file has no link — never by walking up from the test
    /// binary, which finds some other checkout's copy, and never in the fork,
    /// which still holds every Valve file. <see cref="ViolenceHome"/>, which
    /// every other fact in this class reads, must be there, so a missing farm
    /// cannot pass for an absent file.
    /// </para>
    /// </summary>
    private static void AssertAbsent(string relative)
    {
        Assert.True(File.Exists(RepoLayout.Sdk(ViolenceHome)),
            "cannot anchor the tree: " + ViolenceHome + " is not there either, so the "
          + "absence below would be a statement about a tree nobody found");

        Assert.False(File.Exists(RepoLayout.Sdk(relative)),
            relative + " is back in the tree at " + RepoLayout.SdkTree + ". plan_shim.md Phase 2c deleted it "
          + "after a zero-importer census over both link lines; a body there "
          + "again is a second definition of something this file reads at its "
          + "new address, and the linker picks between them silently.");
    }

    /// <summary>Occurrences of a needle, so "declared twice" can be said exactly.</summary>
    private static int CountOf(string hay, string needle)
    {
        int n = 0;
        for (int i = hay.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = hay.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
