using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using SourceSharp.Tests;

namespace SourceSharp.OracleTests;

/// <summary>
/// The five replicated ConVars of <c>GameVars.cs</c>, held against
/// <c>game/shared/gamevars_shared.cpp</c> in the pristine upstream clone (an oracle:
/// neither DLL compiles that file since plan_client_novalve C8; the client's
/// halves are ClientSharedConVarOracleTests').
///
/// <para>
/// <b>Why the .cpp and not a table of expected strings.</b> These five exist
/// only to keep one end of an <c>FCVAR_REPLICATED</c> pair alive. A replicated
/// ConVar is a contract between two DLLs, and the other end is that .cpp, which
/// nothing in this port owns and which a Valve sync could change. A test that
/// spelled the expected values here would go on passing while the two
/// declarations drifted apart, which is the one failure this class is for.
/// </para>
///
/// <para>
/// So the .cpp is parsed and compared. That makes these tests read a file
/// outside the managed tree, and it makes them fail loudly if that file is ever
/// rewritten past the parser rather than passing vacuously — see
/// <see cref="TheCppIsStillTheShapeThisTestParses"/>, which is the guard on
/// everything below it.
/// </para>
/// </summary>
public class GameVarsOracleTests
{
    private const string CppRelative = "src/game/shared/gamevars_shared.cpp";

    /// <summary>What one <c>ConVar</c> declaration in the .cpp says.</summary>
    private readonly record struct StockConVar(
        string Name, string DefaultValue, string Flags, string Help);

    /// <summary>
    /// The .cpp with its comments removed and its <c>#ifdef CSTRIKE</c> arm
    /// dropped.
    ///
    /// <para>
    /// The CSTRIKE arm matters and is not cosmetic: <c>mp_forcecamera</c>'s
    /// default is written twice in that file, "0" under CSTRIKE and "1" under
    /// the <c>#else</c>. This port is not CSTRIKE, so the <c>#else</c> value is
    /// the one C# must match — and a parser that took the first string it found
    /// would silently assert the wrong one.
    /// </para>
    /// </summary>
    private static string StockSource(string repo)
    {
        var raw = File.ReadAllText(Path.Combine(repo, CppRelative));

        // Comments first, so a commented-out directive cannot steer the arm
        // selection below.
        raw = Regex.Replace(raw, @"/\*.*?\*/", "", RegexOptions.Singleline);
        raw = Regex.Replace(raw, @"//[^\n]*", "");

        // #ifdef CSTRIKE ... #else <keep> #endif  ->  <keep>
        raw = Regex.Replace(raw, @"#ifdef\s+CSTRIKE\b(.*?)#else(.*?)#endif", "$2",
                            RegexOptions.Singleline);

        // #ifdef GAME_DLL ... #endif -> kept: this IS the game DLL.
        raw = Regex.Replace(raw, @"#ifdef\s+GAME_DLL\b|#endif", "", RegexOptions.Singleline);

        return raw;
    }

    /// <summary>
    /// Every <c>ConVar name( "name", "default", FLAGS [, "help"] [, callback] )</c>
    /// in the stripped source, in declaration order.
    /// </summary>
    private static List<StockConVar> ParseStock(string repo)
    {
        var src = StockSource(repo);
        var found = new List<StockConVar>();

        foreach (Match m in Regex.Matches(src,
            @"ConVar\s+\w+\s*\(\s*""(?<name>[^""]+)""\s*,\s*""(?<def>[^""]*)""\s*,\s*(?<flags>[^,\)]+)(?<rest>.*?)\)\s*;",
            RegexOptions.Singleline))
        {
            var rest = m.Groups["rest"].Value;
            var help = Regex.Matches(rest, @"""(?<h>[^""]*)""")
                            .Select(x => x.Groups["h"].Value)
                            .FirstOrDefault() ?? "";

            found.Add(new StockConVar(
                m.Groups["name"].Value,
                m.Groups["def"].Value,
                Regex.Replace(m.Groups["flags"].Value, @"\s+", ""),
                help));
        }

        return found;
    }

    /// <summary>
    /// The declared ConVars of the SERVER assembly, by console name.
    ///
    /// <para>
    /// That one assembly and no other, deliberately. Registering the test
    /// assembly as well pulls in the client's declarations through the
    /// reference graph and lands two <c>cs_cl_log_input</c>s in the same
    /// dictionary — which is Source's own rule showing up in miniature: one
    /// name, one declaring DLL. <c>GameVars.cs</c> is in the server assembly,
    /// so that is the one to ask.
    /// </para>
    /// </summary>
    private static Dictionary<string, ConVarBase> Managed()
    {
        var assembly = typeof(ForceCameraConVar).Assembly;

        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);

        var byName = new Dictionary<string, ConVarBase>();
        foreach (var c in registry.All)
        {
            // DECLARED IN the server assembly, not merely reachable from it.
            //
            // RegisterAll walks out through an anchor's references and then its
            // load context. At runtime that gives the server exactly Server +
            // Shared + Abi -- server.so never loads SourceSharp.Client. In THIS
            // process both sides are loaded in the default context, so asking
            // the Server assembly also hands back the client's four replicated
            // convars and mp_chattime arrives twice.
            //
            // Filtered rather than "fixed" in the registry: the registry is
            // right for the process it is designed for, and the duplicate is
            // this test's environment rather than a defect in the port.
            if (c.GetType().Assembly != assembly)
                continue;

            Assert.True(byName.TryAdd(c.Name, c),
                "the server assembly declares the ConVar '" + c.Name
                + "' twice. Source refuses a duplicate name from one DLL, so "
                + "one of the two would simply not exist at runtime.");
        }
        return byName;
    }

    // ------------------------------------------------------------------
    // The guard on everything else. A parser that stops matching reports
    // "nothing to compare", and nothing-to-compare must not read as agreement.
    // ------------------------------------------------------------------

    [Fact]
    public void TheCppIsStillTheShapeThisTestParses()
    {
        var stock = ParseStock(ValveSdk.Root);

        Assert.True(stock.Count == 5,
            "parsed " + stock.Count + " ConVar declarations out of " + CppRelative
            + ", want 5. Either the file changed or the regex stopped matching it; "
            + "every comparison below would pass vacuously either way. Parsed: "
            + string.Join(", ", stock.Select(s => s.Name)));
    }

    [Fact]
    public void TheCstrikeArmIsDroppedSoTheDefaultIsThisPortsOne()
    {
        var stock = ParseStock(ValveSdk.Root);
        var forcecamera = stock.SingleOrDefault(s => s.Name == "mp_forcecamera");

        Assert.True(forcecamera.Name == "mp_forcecamera",
            "mp_forcecamera is no longer declared in " + CppRelative);

        // "0" is the CSTRIKE arm. Reading it here would mean the #ifdef
        // stripping stopped working and the whole comparison is against the
        // wrong game's default.
        Assert.True(forcecamera.DefaultValue == "1",
            "mp_forcecamera parsed as default \"" + forcecamera.DefaultValue
            + "\", want \"1\". \"0\" means the #ifdef CSTRIKE arm was taken.");
    }

    // ------------------------------------------------------------------
    // The five, one [Fact] each.
    // ------------------------------------------------------------------

    private static void AssertMatchesStock(string name, ConVarFlags wantFlags)
    {
        var stock = ParseStock(ValveSdk.Root).SingleOrDefault(s => s.Name == name);
        Assert.True(stock.Name == name, name + " is not declared in " + CppRelative
            + " any more. The C# declaration exists to be the other end of a "
            + "replicated pair; with no other end it is not preserving anything, "
            + "so re-decide rather than deleting this assertion.");

        var managed = Managed();
        Assert.True(managed.ContainsKey(name),
            "no C# ConVar named " + name + ". The client still declares it from "
            + CppRelative + ", and a replicated ConVar only one side declares "
            + "stops tracking the server.");

        var mine = managed[name];

        Assert.True(mine.DefaultValue == stock.DefaultValue,
            name + " default is \"" + mine.DefaultValue + "\" in C# and \""
            + stock.DefaultValue + "\" in " + CppRelative);

        Assert.True(mine.Help == stock.Help,
            name + " help is \"" + mine.Help + "\" in C# and \"" + stock.Help
            + "\" in " + CppRelative);

        // Flags are compared against what the .cpp SPELLS, since that is the
        // thing that can drift. FCVAR_REPLICATED must be on both or the pair
        // is not a pair.
        Assert.True(stock.Flags.Contains("FCVAR_REPLICATED"),
            name + " is no longer FCVAR_REPLICATED in " + CppRelative
            + ", so there is nothing left for the C# declaration to pair with.");
        Assert.True(mine.Flags.HasFlag(ConVarFlags.Replicated),
            name + " is not Replicated in C#.");

        Assert.True(stock.Flags.Contains("FCVAR_NOTIFY") == mine.Flags.HasFlag(ConVarFlags.Notify),
            name + " disagrees on NOTIFY: " + CppRelative + " says \""
            + stock.Flags + "\", C# says \"" + mine.Flags + "\"");

        Assert.True(mine.Flags == wantFlags,
            name + " declares " + mine.Flags + ", want " + wantFlags);
    }

    [Fact]
    public void ForceCameraMatchesStock()
        => AssertMatchesStock("mp_forcecamera", ConVarFlags.Replicated);

    [Fact]
    public void AllowSpectatorsMatchesStock()
        => AssertMatchesStock("mp_allowspectators", ConVarFlags.Replicated);

    [Fact]
    public void MpFriendlyFireMatchesStock()
        => AssertMatchesStock("mp_friendlyfire",
                              ConVarFlags.Replicated | ConVarFlags.Notify);

    [Fact]
    public void FadeToBlackMatchesStock()
        => AssertMatchesStock("mp_fadetoblack",
                              ConVarFlags.Replicated | ConVarFlags.Notify);

    [Fact]
    public void HudHintSoundMatchesStock()
        => AssertMatchesStock("sv_hudhint_sound", ConVarFlags.Replicated);

    // ------------------------------------------------------------------
    // mp_forcecamera keeps MPForceCameraCallback's clamp, which was the one
    // piece of BEHAVIOUR in gamevars_shared.cpp rather than declaration.
    // ------------------------------------------------------------------

    [Fact]
    public void TheStockClampIsStillInTheCppItWasPortedFrom()
    {
        var src = File.ReadAllText(Path.Combine(ValveSdk.Root, CppRelative));

        Assert.Contains("MPForceCameraCallback", src);
        Assert.Contains("OBS_ALLOW_TEAM", src);
    }

    [Fact]
    public void OutOfRangeForceCameraBecomesAllowTeamNotTheNearestBound()
    {
        var v = new ForceCameraConVar();

        // The engine is absent, so SetValue goes nowhere and the cached value
        // is whatever the callback was handed. What is asserted here is the
        // constants the clamp is written against — the arithmetic itself is
        // three comparisons and its inputs are what can be got wrong.
        Assert.True(ForceCameraConVar.AllowAll == 0, "OBS_ALLOW_ALL is 0");
        Assert.True(ForceCameraConVar.AllowTeam == 1, "OBS_ALLOW_TEAM is 1");
        Assert.True(ForceCameraConVar.AllowNone == 2, "OBS_ALLOW_NONE is 2");
        Assert.True(ForceCameraConVar.NumModes == 3, "OBS_ALLOW_NUM_MODES is 3");

        // ...and that they still are, in the header that defines them. Four
        // named constants copied into C# is four chances to be quietly wrong.
        var shared = File.ReadAllText(
            Path.Combine(ValveSdk.Root, "src/game/shared/shareddefs.h"));
        var block = Regex.Match(shared, @"OBS_ALLOW_ALL\s*=\s*0\s*,(?<rest>.*?)OBS_ALLOW_NUM_MODES",
                                RegexOptions.Singleline);
        Assert.True(block.Success,
            "shareddefs.h no longer declares the OBS_ALLOW_* enum in the shape "
            + "this test reads; re-check the four constants in ForceCameraConVar.");

        var order = Regex.Matches(block.Groups["rest"].Value, @"OBS_ALLOW_(\w+)")
                         .Select(m => m.Groups[1].Value).ToList();
        Assert.True(order.Count == 2 && order[0] == "TEAM" && order[1] == "NONE",
            "the OBS_ALLOW_* enum's order changed: after ALL=0 it now reads "
            + string.Join(",", order) + ". ForceCameraConVar's constants are "
            + "positional and are now wrong.");
    }
}
