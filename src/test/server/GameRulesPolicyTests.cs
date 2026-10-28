using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The rules' constants: C# declares them, native holds a pushed copy.
///
/// <para>
/// <b>The failure this file exists for is drift.</b> There are two statements of
/// every value — the C# constant and the native default in <c>s_Policy</c> — and
/// they are not the same kind of thing: the C# one is the authority, the native
/// one is what gets used before C# has registered. Nothing in either language
/// makes them agree, which is exactly the complaint this port makes about stock
/// <c>CMultiplayRules</c> spelling every damage mask twice.
/// </para>
///
/// <para>
/// The table is two fields now. Fifteen went with the native methods that were
/// their only readers, so the second half of this file is about what must NOT
/// come back: a field with no reader, and a decision served from a table.
/// </para>
/// </summary>
public class GameRulesPolicyTests
{
    private const string Header = "src/sourcesharp/host/sourcesharp.h";

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative));

    // ------------------------------------------------------------------
    // The survivor agrees across the boundary.
    //
    // THERE WERE TWO AND THERE IS ONE. AllowDamageMatchesTheNativeDefault went
    // at plan_shim.md §5 3e with the field it was about: bAllowDamage's one
    // reader, CSourceSharpGameRules::AllowDamage, had no caller in any compiled
    // translation unit, because its single call site is in
    // game/server/baseentity.cpp and neither DLL compiles that file. The
    // assertion it made -- "the managed default and the native default are the
    // same value" -- is no longer a claim about anything, because neither
    // default exists.
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterPushesEveryValue()
    {
        var engine = new RecordingEngine();
        GameRulesPolicy.Register(engine);

        var call = engine.Calls.Single(c => c.Member == "GameRulesPolicy.RegisterPolicy");
        var blob = Assert.IsType<GameRulesPolicyBlob>(call.Arg(0));

        Assert.Equal(GameRulesPolicy.Multiplayer, blob.Multiplayer != 0);

        // AND THE BLOB HAS NOTHING ELSE IN IT, which is what makes the line
        // above a complete check rather than a sample. A field appended without
        // a Register() line is pushed as zero for ever and nothing else here
        // would notice.
        Assert.Single(typeof(GameRulesPolicyBlob).GetFields(
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance));
    }

    // ------------------------------------------------------------------
    // Both fields are read, and by the method they belong to.
    // ------------------------------------------------------------------

    /// <summary>
    /// The struct is the same shape on both sides, field for field and in
    /// order. It crosses as a blittable copy, so a field added to one side only
    /// silently reinterprets everything after it.
    /// </summary>
    [Fact]
    public void TheStructIsTheSameShapeOnBothSides()
    {
        var header = Read(Header);

        int start = header.IndexOf("typedef struct\n{\n\t// Whether this is a multiplayer game.",
                                   StringComparison.Ordinal);
        Assert.True(start >= 0, "cannot find the native policy struct");
        int end = header.IndexOf("} SourceSharpGameRulesPolicy;", start, StringComparison.Ordinal);

        var nativeFields = Regex.Matches(header[start..end], @"^\t(?:int32_t|float|char)\s+(\w+)",
                                         RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.TrimEnd('[', ']'))
            .ToArray();

        var managedFields = typeof(GameRulesPolicyBlob)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.Name)
            .ToArray();

        Assert.True(nativeFields.Length == managedFields.Length,
            "the struct has " + nativeFields.Length + " native field(s) and "
            + managedFields.Length + " managed. It crosses as a blittable copy, "
            + "so a field on one side only reinterprets everything after it.\n"
            + "native:  " + string.Join(", ", nativeFields) + "\n"
            + "managed: " + string.Join(", ", managedFields));

        for (int i = 0; i < nativeFields.Length; i++)
        {
            string native = Regex.Replace(nativeFields[i], @"^(b|n|fl|sz)", "");

            Assert.True(string.Equals(native, managedFields[i], StringComparison.OrdinalIgnoreCase),
                "field " + i + " is `" + nativeFields[i] + "` natively and `"
                + managedFields[i] + "` managed. Order is the layout.");
        }
    }

}

/// <summary>
/// THE GAME'S DIFFICULTY IS STILL DECLARED IN C++, AND THESE ARE THE FACTS THAT
/// SAY SO ON PURPOSE.
///
/// <para>
/// The shim audit named <c>skill</c> first among the fifteen gameplay ConVars.
/// The <c>policy-rules</c> lane moved it to <c>SourceSharp.Server.SkillConVar</c>,
/// ran the live gate, and moved it back: a managed ConVar of that NAME cannot be
/// set from the console, while an identically-shaped one called
/// <c>ss_skill_probe</c> can, and so could <c>cs_sv_teamplay</c> (deleted with
/// the teamplay rule, lane 6a-ruleslayer) and <c>cs_sv_fraglimit</c>. What is in the way is stock
/// <c>game/shared/gamerules.cpp</c>, then still in <c>client_csharp.vpc</c>, which
/// declared its own. The measurement is beside the declaration in
/// <c>shell_gamerules.cpp</c>.
/// </para>
///
/// <para>
/// These facts exist so the revert cannot be undone by accident: they pin the
/// ABSENCE of a declaration on both sides (the engine's `skill` is the one the
/// game reads, plan_server_novalve.md P2.3b), and the clamp that is the one
/// piece of the move worth keeping.
/// </para>
/// </summary>
public class SkillLevelTests
{
    [Fact]
    public void SkillIsNotDeclaredByManagedCode()
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(GameRules).Assembly);

        Assert.DoesNotContain(registry.All, c =>
            string.Equals(c.Name, "skill", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// THE BLOCKER IS LIFTED, AND THIS RECORDS THE DAY IT WENT.
    ///
    /// <para>
    /// This fact used to assert the opposite -- that the client still compiled
    /// the stock rules file -- and its own comment said "when this stops being
    /// true the reason has gone and skill can move". The armed flip is when it
    /// stopped being true: that file is not in the client's list any more.
    /// </para>
    ///
    /// <para>
    /// So the fact is INVERTED rather than deleted. Deleting it would have
    /// thrown away the only thing that was watching, and the point of the
    /// original was to notice this moment. The knob has NOT moved yet -- the
    /// sibling fact above still finds it declared natively -- and that is the
    /// work this now hands on, with the obstacle to it gone.
    /// </para>
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheClientNoLongerCompilesTheStockFileThatBlockedTheMove()
    {
        // Read off the CMake build: client_sourcesharp's compiled sources
        // (compile_commands.json, cross-checked against
        // src/client/native/CMakeLists.txt). A missing or stale build throws.
        //
        // The positive control, because an absence over a list that could not be
        // read passes for the wrong reason.
        //
        // IT NAMED takedamageinfo.cpp UNTIL plan_shim.md PHASE 1d, which
        // evicted that row from both vpcs as an empty translation unit -- so
        // the control failed while the assertion below was still true, which is
        // the control doing its job on itself. A control has to name something
        // the client build still has; usercmd.cpp is a shared file the armed
        // client compiles and the shim rulings mark KEEP. The assertion itself
        // is untouched.
        // usercmd.cpp left the client with its codec (plan_client_novalve C9):
        // tier_connect.cpp, which both DLLs compile by design, is the control.
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, "sourcesharp/game/shared/tier_connect.cpp"),
            "client_sourcesharp (src/client/native/CMakeLists.txt) no longer compiles "
          + "sourcesharp/game/shared/tier_connect.cpp -- the positive control needs a file the client still builds");

        Assert.False(CMakeGraph.Compiles(CMakeGraph.Client, "game/shared/gamerules.cpp"),
            "client_sourcesharp (src/client/native/CMakeLists.txt) compiles stock game/shared/gamerules.cpp again");
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void NoCompiledTranslationUnitReadsTheNativeSkill()
    {
        // The other half of the finding, and it stands whatever happens to the
        // declaration: the comment that argued for keeping skill in C++ said
        // GameStats.cpp read it through an `extern` and was still compiled. It
        // is compiled into NEITHER DLL (server_sourcesharp, client_sourcesharp).
        IReadOnlySet<string> built = CMakeGraph.GameSources();

        // Proof the lists were read: a file both DLLs do compile (tier_connect.cpp;
        // usercmd.cpp was until the client's codec moved to C#, plan_client_novalve C9).
        Assert.Contains("sourcesharp/game/shared/tier_connect.cpp", built);

        Assert.DoesNotContain(built, s => string.Equals(Path.GetFileName(s), "GameStats.cpp",
                                                        StringComparison.OrdinalIgnoreCase));
    }

}
