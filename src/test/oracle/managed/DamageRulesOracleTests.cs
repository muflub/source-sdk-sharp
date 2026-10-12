using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The five damage masks, held against the two C++ files they came from rather
/// than against a table repeated here.
///
/// <para>
/// <b>Why read the .cpp.</b> These are transcriptions. A test that spelled the
/// expected flags in C# would be comparing my copy with my copy, and would go
/// on passing while <c>multiplay_gamerules.cpp</c> — which was in the
/// build and the authority until it left — said something else. So the
/// masks are parsed out of <c>CMultiplayRules</c>'s method bodies and the flag
/// VALUES out of <c>shareddefs.h</c>, and both are compared with what
/// <see cref="DamageRules"/> declares.
/// </para>
///
/// <para>
/// <b>The pairs are the point.</b> Stock answers each question twice — a
/// <c>Damage_Get*</c> returning the mask and a <c>Damage_Is*</c>/<c>Should*</c>
/// re-spelling the same flags inline — so there are two independent lists per
/// question in that file and nothing checks they agree. This reads BOTH and
/// requires all three (get, predicate, C#) to be the same set.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> Neither DLL compiles
/// multiplay_gamerules.cpp any more, so the farm no longer links it; these
/// facts read it, and shareddefs.h beside it, from the pristine upstream clone
/// through <see cref="ValveSdk"/>. The flag-value and composition facts stay in
/// DamageRulesTests.
/// </para>
/// </summary>
public class DamageRulesOracleTests
{
    private const string StockCpp = "src/game/shared/multiplay_gamerules.cpp";
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    private static string Read(string relative) => ValveSdk.Read(relative);

    /// <summary>Every <c>#define DMG_X (1 &lt;&lt; n)</c> in shareddefs.h, by name.</summary>
    private static Dictionary<string, int> DmgFlags()
    {
        var text = Read(SharedDefs);
        var flags = new Dictionary<string, int>();

        foreach (Match m in Regex.Matches(text,
            @"^#define\s+(?<name>DMG_\w+)\s+\(\s*1\s*<<\s*(?<bit>\d+)\s*\)", RegexOptions.Multiline))
        {
            flags[m.Groups["name"].Value] = 1 << int.Parse(m.Groups["bit"].Value);
        }

        return flags;
    }

    /// <summary>
    /// The DMG_* names a named <c>CMultiplayRules</c> method ORs together.
    ///
    /// Deliberately name-based rather than value-based: what is being checked
    /// is that the same SET of kinds is listed, and resolving the names through
    /// shareddefs.h separately is what catches a flag whose bit moved.
    /// </summary>
    private static HashSet<string> FlagsNamedIn(string body) =>
        Regex.Matches(body, @"DMG_\w+").Select(m => m.Value).ToHashSet();

    private static string BodyOf(string method)
    {
        var text = Read(StockCpp);
        var m = Regex.Match(text,
            @"CMultiplayRules::" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{(?<body>.*?)\n\}",
            RegexOptions.Singleline);

        Assert.True(m.Success,
            "could not find CMultiplayRules::" + method + " in " + StockCpp
            + ". If that file has left the build this test's authority is gone: "
            + "either point it at whatever replaced it, or delete it and say in "
            + "its place that DamageRules.cs is now the only statement of these "
            + "masks.");

        return m.Groups["body"].Value;
    }

    /// <summary>What a mask declared in C# names, as DMG_* names.</summary>
    private static HashSet<string> NamesOf(DamageType mask, Dictionary<string, int> flags)
    {
        var named = new HashSet<string>();
        foreach (var (name, value) in flags)
        {
            if (value != 0 && ((int)mask & value) == value)
                named.Add(name);
        }
        return named;
    }

    private static void AssertMaskMatchesStock(string getter, string predicate, DamageType mine)
    {
        var flags = DmgFlags();
        Assert.True(flags.Count > 20,
            "parsed only " + flags.Count + " DMG_* flags out of shareddefs.h; the "
            + "regex has stopped matching and every comparison below would be "
            + "against an empty table");

        var fromGetter = FlagsNamedIn(BodyOf(getter));
        var fromPred = FlagsNamedIn(BodyOf(predicate));
        var fromMine = NamesOf(mine, flags);

        // Stock's own two spellings must agree with each other first. If they
        // ever did not, the C# could only match one of them and which one would
        // be arbitrary.
        Assert.True(fromGetter.SetEquals(fromPred),
            "stock disagrees with ITSELF: " + getter + " lists ["
            + string.Join(",", fromGetter.OrderBy(x => x)) + "] and " + predicate
            + " lists [" + string.Join(",", fromPred.OrderBy(x => x))
            + "]. They are two independent spellings of one list in "
            + StockCpp + " and they have drifted.");

        Assert.True(fromMine.SetEquals(fromGetter),
            "DamageRules disagrees with " + getter + ".\n  C#:    ["
            + string.Join(",", fromMine.OrderBy(x => x)) + "]\n  stock: ["
            + string.Join(",", fromGetter.OrderBy(x => x)) + "]");
    }

    [Fact]
    public void TimeBasedMatchesStock() =>
        AssertMaskMatchesStock("Damage_GetTimeBased", "Damage_IsTimeBased", DamageRules.TimeBased);

    [Fact]
    public void ShouldGibCorpseMatchesStock() =>
        AssertMaskMatchesStock("Damage_GetShouldGibCorpse", "Damage_ShouldGibCorpse",
                               DamageRules.ShouldGibCorpse);

    [Fact]
    public void ShouldNotBleedMatchesStock() =>
        AssertMaskMatchesStock("Damage_GetShouldNotBleed", "Damage_ShouldNotBleed",
                               DamageRules.ShouldNotBleed);

    [Fact]
    public void ShowOnHudMatchesStock() =>
        AssertMaskMatchesStock("Damage_GetShowOnHud", "Damage_ShowOnHUD", DamageRules.ShowOnHud);

    /// <summary>
    /// NoPhysicsForce, which stock answers only as a predicate — there is no
    /// <c>Damage_GetNoPhysicsForce</c> body in <c>CMultiplayRules</c> to pair it
    /// with, so this one is compared against the single spelling that exists.
    /// </summary>
    [Fact]
    public void NoPhysicsForceMatchesStock()
    {
        var flags = DmgFlags();
        var fromStock = FlagsNamedIn(BodyOf("Damage_NoPhysicsForce"));

        // Its body says `int iTimeBasedDamage = Damage_GetTimeBased();` and then
        // ORs that variable in, so the time-based names are NOT spelled in it.
        // The C# composes the same way, so the comparison has to add them.
        fromStock.UnionWith(FlagsNamedIn(BodyOf("Damage_GetTimeBased")));

        var fromMine = NamesOf(DamageRules.NoPhysicsForce, flags);

        Assert.True(fromMine.SetEquals(fromStock),
            "DamageRules.NoPhysicsForce disagrees with stock.\n  C#:    ["
            + string.Join(",", fromMine.OrderBy(x => x)) + "]\n  stock: ["
            + string.Join(",", fromStock.OrderBy(x => x)) + "]");
    }
}
