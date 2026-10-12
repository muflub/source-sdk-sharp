using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The seventeen ConVars of <c>GameDllConVars.cs</c>, held against
/// <c>game/server/game.cpp</c> — the file this port stopped compiling and
/// deliberately did not delete.
///
/// <para>
/// <b>Why the .cpp and not a table of expected strings here.</b> Same reason
/// <c>GameVarsOracleTests</c> gives: a test that spelled the defaults and help
/// text in this file would go on passing while the C# and the stock declaration
/// drifted apart, and drift is the only failure the move can have.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> Neither DLL compiles
/// game.cpp, so the farm no longer links it; these facts read it from the
/// pristine upstream clone through <see cref="ValveSdk"/>, where a missing file
/// is a failure rather than a skip.
/// </para>
///
/// <para>
/// <b>What these names are load-bearing for.</b> None of the seventeen has a
/// native symbol reader — measured on the debug link line, the 118 compiled
/// server objects import none of them — but seven are reached BY NAME, which no
/// census can see: <c>displaysoundlist</c> from <c>SoundEnt.cs</c>,
/// <c>mapcyclefile</c> from the engine itself, and five
/// <c>mp_*</c> names from <c>cfg/settings.scr</c>. Registering rather than
/// declaring is what keeps those working, and the name is the half that a
/// rename would break in silence.
/// </para>
/// </summary>
public class GameDllConVarsOracleTests
{
    private const string CppRelative = "src/game/server/game.cpp";

    /// <summary>What one <c>ConVar</c> declaration in the .cpp says.</summary>
    private readonly record struct StockConVar(
        string Symbol, string Name, string DefaultValue, string Flags, string Help);

    /// <summary>
    /// The .cpp with its comments removed and its <c>#ifdef CSTRIKE</c> arm
    /// dropped.
    ///
    /// <para>
    /// The CSTRIKE arm is not cosmetic: <c>mp_flashlight</c> is declared twice
    /// in that file, "1" under CSTRIKE and "0" under the <c>#else</c>. This port
    /// is not CSTRIKE, so the <c>#else</c> value is the one C# must match — and
    /// a parser that took the first string it found would assert the wrong one
    /// and call it agreement.
    /// </para>
    /// </summary>
    private static string StockSource()
    {
        var raw = ValveSdk.Read(CppRelative);

        raw = Regex.Replace(raw, @"/\*.*?\*/", "", RegexOptions.Singleline);
        raw = Regex.Replace(raw, @"//[^\n]*", "");

        // #ifdef CSTRIKE ... #else <keep> #endif  ->  <keep>
        raw = Regex.Replace(raw, @"#ifdef\s+CSTRIKE\b(.*?)#else(.*?)#endif", "$2",
                            RegexOptions.Singleline);

        return raw;
    }

    /// <summary>
    /// Every <c>ConVar symbol( "name", "default" [, FLAGS] [, "help"] [, callback] )</c>
    /// in the stripped source, in declaration order.
    /// </summary>
    private static List<StockConVar> ParseStock()
    {
        var src = StockSource();
        var found = new List<StockConVar>();

        foreach (Match m in Regex.Matches(src,
            @"ConVar\s+(?<sym>\w+)\s*\(\s*""(?<name>[^""]+)""\s*,\s*""(?<def>[^""]*)""(?<rest>[^;]*?)\)\s*;",
            RegexOptions.Singleline))
        {
            string rest = m.Groups["rest"].Value;

            // The help string is the next quoted run after the default, if any.
            string help = Regex.Matches(rest, @"""(?<h>[^""]*)""")
                               .Select(x => x.Groups["h"].Value)
                               .FirstOrDefault() ?? "";

            // The flags are whatever FCVAR_* tokens the declaration carries.
            string flags = string.Join("|",
                Regex.Matches(rest, @"FCVAR_\w+").Select(x => x.Value));

            found.Add(new StockConVar(m.Groups["sym"].Value, m.Groups["name"].Value,
                                      m.Groups["def"].Value, flags, help));
        }

        return found;
    }

    /// <summary>
    /// The declared ConVars of the SERVER assembly, by console name. That one
    /// assembly and no other, for <see cref="GameVarsOracleTests"/>'s reason: asking
    /// through the test assembly pulls the client's declarations in through the
    /// reference graph and lands two of some names in one dictionary.
    /// </summary>
    private static Dictionary<string, ConVarBase> Managed()
    {
        var assembly = typeof(MapCycleFileConVar).Assembly;

        var registry = new ConVarRegistry(SourceSharp.Tests.TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);

        var byName = new Dictionary<string, ConVarBase>();
        foreach (var c in registry.All)
        {
            if (c.GetType().Assembly != assembly)
                continue;

            Assert.True(byName.TryAdd(c.Name, c),
                "the server assembly declares the ConVar '" + c.Name
                + "' twice. Source refuses a duplicate name from one DLL, so one "
                + "of the two would simply not exist at runtime.");
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
        var stock = ParseStock();

        Assert.True(stock.Count == 17,
            "parsed " + stock.Count + " ConVar declarations out of " + CppRelative
            + ", want 17. Either the file changed or the regex stopped matching it; "
            + "every comparison below would pass vacuously either way. Parsed: "
            + string.Join(", ", stock.Select(s => s.Name)));
    }

    [Fact]
    public void TheCstrikeArmIsDroppedSoTheFlashlightDefaultIsThisPortsOne()
    {
        var stock = ParseStock();
        var flashlight = stock.SingleOrDefault(s => s.Name == "mp_flashlight");

        Assert.True(flashlight.Name == "mp_flashlight",
            "mp_flashlight is no longer declared in " + CppRelative);

        // "1" is the CSTRIKE arm. Reading it here would mean the #ifdef
        // stripping stopped working and the whole comparison is against the
        // wrong game's default.
        Assert.True(flashlight.DefaultValue == "0",
            "mp_flashlight parsed as default \"" + flashlight.DefaultValue
            + "\", want \"0\". \"1\" means the #ifdef CSTRIKE arm was taken.");
    }

    // ------------------------------------------------------------------
    // The seventeen, one [Fact] each.
    // ------------------------------------------------------------------

    private static void AssertMatchesStock(string name, ConVarFlags wantFlags)
    {
        var stock = ParseStock().SingleOrDefault(s => s.Name == name);
        Assert.True(stock.Name == name, name + " is not declared in " + CppRelative
            + " any more. The C# declaration is a port of that one; with no original "
            + "there is nothing to hold it against, so re-decide rather than deleting "
            + "this assertion.");

        var managed = Managed();
        Assert.True(managed.ContainsKey(name),
            "no C# ConVar named " + name + ". It is reached by NAME -- by managed "
            + "code, by the engine, or by cfg/settings.scr -- and an unregistered "
            + "name is not a link error and not a warning: the reader takes its own "
            + "default and nothing says so.");

        var mine = managed[name];

        Assert.True(mine.DefaultValue == stock.DefaultValue,
            name + " default is \"" + mine.DefaultValue + "\" in C# and \""
            + stock.DefaultValue + "\" in " + CppRelative);

        Assert.True(mine.Help == stock.Help,
            name + " help is \"" + mine.Help + "\" in C# and \"" + stock.Help
            + "\" in " + CppRelative);

        Assert.True(mine.Flags == wantFlags,
            name + " declares " + mine.Flags + ", want " + wantFlags);

        // AND THE FLAGS AGAINST THE .cpp, not just against this test's own
        // expectation -- otherwise a flag dropped in BOTH places agrees.
        Assert.True(stock.Flags.Contains("FCVAR_NOTIFY") == mine.Flags.HasFlag(ConVarFlags.Notify),
            name + " disagrees on NOTIFY: " + CppRelative + " says \"" + stock.Flags
            + "\", C# says \"" + mine.Flags + "\"");
        Assert.True(stock.Flags.Contains("FCVAR_ARCHIVE") == mine.Flags.HasFlag(ConVarFlags.Archive),
            name + " disagrees on ARCHIVE: " + CppRelative + " says \"" + stock.Flags
            + "\", C# says \"" + mine.Flags + "\"");
    }

    [Fact] public void DisplaySoundListMatchesStock()
        => AssertMatchesStock("displaysoundlist", ConVarFlags.None);

    [Fact] public void MapCycleFileMatchesStock()
        => AssertMatchesStock("mapcyclefile", ConVarFlags.None);

    [Fact] public void ServerCfgFileMatchesStock()
        => AssertMatchesStock("servercfgfile", ConVarFlags.None);

    [Fact] public void ListenServerCfgFileMatchesStock()
        => AssertMatchesStock("lservercfgfile", ConVarFlags.None);

    [Fact] public void TeamPlayMatchesStock()
        => AssertMatchesStock("mp_teamplay", ConVarFlags.Notify);

    [Fact] public void FallDamageMatchesStock()
        => AssertMatchesStock("mp_falldamage", ConVarFlags.Notify);

    [Fact] public void WeaponStayMatchesStock()
        => AssertMatchesStock("mp_weaponstay", ConVarFlags.Notify);

    [Fact] public void ForceRespawnMatchesStock()
        => AssertMatchesStock("mp_forcerespawn", ConVarFlags.Notify);

    [Fact] public void MpFootstepsMatchesStock()
        => AssertMatchesStock("mp_footsteps", ConVarFlags.Notify);

    [Fact] public void MpFlashlightMatchesStock()
        => AssertMatchesStock("mp_flashlight", ConVarFlags.Notify);

    [Fact] public void AutoCrosshairMatchesStock()
        => AssertMatchesStock("mp_autocrosshair", ConVarFlags.Notify);

    [Fact] public void DecalFrequencyMatchesStock()
        => AssertMatchesStock("decalfrequency", ConVarFlags.Notify);

    [Fact] public void TeamListMatchesStock()
        => AssertMatchesStock("mp_teamlist", ConVarFlags.Notify);

    [Fact] public void TeamOverrideMatchesStock()
        => AssertMatchesStock("mp_teamoverride", ConVarFlags.None);

    [Fact] public void DefaultTeamMatchesStock()
        => AssertMatchesStock("mp_defaultteam", ConVarFlags.None);

    [Fact] public void AllowNpcsMatchesStock()
        => AssertMatchesStock("mp_allowNPCs", ConVarFlags.Notify);

    [Fact] public void SuitVolumeMatchesStock()
        => AssertMatchesStock("suitvolume", ConVarFlags.Archive);

    [Fact]
    public void EverySeventeenthNameIsRegisteredAndNoMore()
    {
        // The count, both ways. A name this port invented and parked among
        // stock's seventeen would be a convar the SDK never had, in the class
        // whose whole claim is that it is stock's list.
        var stock = ParseStock().Select(s => s.Name).ToHashSet();
        var managed = Managed();

        var missing = stock.Where(n => !managed.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0,
            "declared in " + CppRelative + " and not in C#: " + string.Join(", ", missing));
    }
}
