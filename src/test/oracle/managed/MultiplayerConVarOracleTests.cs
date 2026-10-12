extern alias client;
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
/// The fifteen ConVars <c>MultiplayerConVars.cs</c> took over from
/// <c>game/shared/multiplay_gamerules.cpp</c>, held against that file.
///
/// <para>
/// <b>Why the .cpp and not a table here.</b> The CLIENT used to compile it and
/// declare all fifteen, and four of them are <c>FCVAR_REPLICATED</c> —
/// a contract between two DLLs that only holds while both sides say the same
/// thing. A test that spelled the expected values in C# would compare my copy
/// with my copy and go on passing while the two declarations drifted.
/// </para>
///
/// <para>
/// The parser is deliberately strict about the <c>#if</c> arms: <c>nextlevel</c>
/// has two help strings chosen by <c>CSTRIKE_DLL || TF_DLL</c>, and the first
/// transcription took the wrong one. <c>MultiplayerConVarTests.NextLevelTakesTheNonCstrikeArm</c>
/// is the guard on that specific mistake.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> Neither DLL compiles
/// multiplay_gamerules.cpp any more, so the farm no longer links it; these
/// facts read it from the pristine upstream clone through <see cref="ValveSdk"/>.
/// </para>
/// </summary>
public class MultiplayerConVarOracleTests
{
    private const string StockCpp = "src/game/shared/multiplay_gamerules.cpp";

    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    /// <summary>
    /// Every <c>ConVar x( "name", "default", FLAGS, "help" ... )</c> in the .cpp,
    /// with comments removed and the CSTRIKE/TF arms dropped.
    /// </summary>
    private static Dictionary<string, Stock> ParseStock()
    {
        var text = ValveSdk.Read(StockCpp);

        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");

        // #if defined( CSTRIKE_DLL ) || defined( TF_DLL ) <A> #else <B> #endif -> B.
        // This build is neither, so the #else arm is the live one.
        text = Regex.Replace(text,
            @"#if\s+defined\(\s*CSTRIKE_DLL\s*\).*?#else(?<keep>.*?)#endif",
            "${keep}", RegexOptions.Singleline);

        // #ifdef GAME_DLL ... #endif is kept: this IS the game DLL.
        text = Regex.Replace(text, @"#ifdef\s+GAME_DLL\b|#endif\b", "");

        var found = new Dictionary<string, Stock>();

        foreach (Match m in Regex.Matches(text,
            @"\bConVar\s+\w+\s*\(\s*""(?<name>[^""]+)""\s*,\s*""(?<def>[^""]*)""\s*,\s*(?<flags>[A-Za-z_0-9|\s]+?)\s*,\s*""(?<help>(?:[^""\\]|\\.)*)""",
            RegexOptions.Singleline))
        {
            found[m.Groups["name"].Value] = new Stock(
                m.Groups["name"].Value,
                m.Groups["def"].Value,
                Regex.Replace(m.Groups["flags"].Value, @"\s+", ""),
                Regex.Unescape(m.Groups["help"].Value));
        }

        return found;
    }

    /// <summary>
    /// The ConVars DECLARED IN a given assembly, by console name.
    ///
    /// <para>
    /// The filter on the declaring assembly is load-bearing IN THIS PROCESS and
    /// nowhere else. <c>ConVarRegistry.RegisterAll</c> walks out through an
    /// anchor's references and then through its load context, which at runtime
    /// gives the server exactly Server + Shared + Abi — server.so never loads
    /// SourceSharp.Client. In the TEST process both sides are loaded in the
    /// default context, so asking the Server assembly hands back the client's
    /// four as well and <c>mp_chattime</c> arrives twice.
    /// </para>
    ///
    /// <para>
    /// So it is filtered here rather than "fixed" in the registry: the registry
    /// is behaving correctly for the process it is designed for, and a duplicate
    /// here is this test's environment rather than a defect in the port.
    /// </para>
    /// </summary>
    private static Dictionary<string, ConVarBase> DeclaredIn(System.Reflection.Assembly assembly)
    {
        var registry = new ConVarRegistry(SourceSharp.Tests.TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);

        var byName = new Dictionary<string, ConVarBase>();
        foreach (var c in registry.All)
        {
            if (c.GetType().Assembly != assembly)
                continue;

            Assert.True(byName.TryAdd(c.Name, c),
                assembly.GetName().Name + " declares '" + c.Name + "' twice; Source "
                + "refuses a duplicate name from one DLL, so one of them would not exist");
        }
        return byName;
    }

    private static Dictionary<string, ConVarBase> Managed() =>
        DeclaredIn(typeof(MpChatTimeConVar).Assembly);

    /// <summary>The guard: a parser that stops matching must not read as agreement.</summary>
    [Fact]
    public void TheCppIsStillTheShapeThisTestParses()
    {
        var stock = ParseStock();

        Assert.True(stock.Count == 15,
            "parsed " + stock.Count + " ConVar declarations out of " + StockCpp
            + ", want 15. Either that file changed or the regex stopped matching "
            + "it, and every comparison below would pass vacuously. Parsed: "
            + string.Join(", ", stock.Keys.OrderBy(x => x)));
    }

    [Fact]
    public void EveryStockConVarHasAManagedDeclaration()
    {
        var stock = ParseStock();
        var mine = Managed();

        var missing = stock.Keys.Where(n => !mine.ContainsKey(n)).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            missing.Count + " ConVar(s) declared by " + StockCpp + " have no managed "
            + "declaration: " + string.Join(", ", missing) + ". That file is out of "
            + "the SERVER build, so each of these no longer exists on this side — and "
            + "the client still declares them.");
    }

    [Fact]
    public void TheDefaultsAndHelpTextMatchStock()
    {
        var stock = ParseStock();
        var mine = Managed();

        foreach (var (name, s) in stock.OrderBy(kv => kv.Key))
        {
            if (!mine.TryGetValue(name, out var c))
                continue;   // reported by EveryStockConVarHasAManagedDeclaration

            Assert.True(c.DefaultValue == s.Default,
                name + " default is \"" + c.DefaultValue + "\" in C# and \""
                + s.Default + "\" in " + StockCpp);

            Assert.True(c.Help == s.Help,
                name + " help is:\n  C#:    \"" + c.Help + "\"\n  stock: \""
                + s.Help + "\"");
        }
    }

    /// <summary>
    /// The four replicated ones, which are the reason any of this is required
    /// rather than merely tidy.
    /// </summary>
    [Fact]
    public void EveryReplicatedConVarIsReplicatedOnBothSides()
    {
        var stock = ParseStock();
        var mine = Managed();

        var replicated = stock.Values
            .Where(s => s.Flags.Contains("FCVAR_REPLICATED"))
            .Select(s => s.Name)
            .OrderBy(x => x)
            .ToList();

        // FIVE, not four. mp_chattime is FCVAR_REPLICATED as well, and the
        // first version of this test asserted four from memory and was told so
        // by its own failure -- which is the whole reason it parses the .cpp
        // instead of listing names.
        Assert.True(replicated.Count == 5,
            StockCpp + " declares " + replicated.Count + " replicated ConVars, want 5: "
            + string.Join(", ", replicated));

        foreach (var name in replicated)
        {
            Assert.True(mine.TryGetValue(name, out var c),
                name + " is FCVAR_REPLICATED in " + StockCpp + " and has no managed "
                + "declaration. The client declares it; a replicated ConVar only one "
                + "side declares stops tracking the server.");

            Assert.True(c!.Flags.HasFlag(ConVarFlags.Replicated),
                name + " is FCVAR_REPLICATED in " + StockCpp + " and not Replicated in C#.");
        }
    }

    [Fact]
    public void NotifyIsCarriedWhereStockHasIt()
    {
        var stock = ParseStock();
        var mine = Managed();

        foreach (var (name, s) in stock)
        {
            if (!mine.TryGetValue(name, out var c))
                continue;

            Assert.True(s.Flags.Contains("FCVAR_NOTIFY") == c.Flags.HasFlag(ConVarFlags.Notify),
                name + " disagrees on NOTIFY: " + StockCpp + " says \"" + s.Flags
                + "\", C# says \"" + c.Flags + "\"");
        }
    }

    /// <summary>
    /// mp_chattime's bounds. Stock passes true,1,true,120 after the help — the
    /// one thing in this set the engine itself enforces, so losing it would let
    /// a value through that stock refuses.
    /// </summary>
    [Fact]
    public void ChatTimeKeepsTheBoundsStockEnforces()
    {
        var mine = Managed();
        Assert.True(mine.TryGetValue("mp_chattime", out var c), "mp_chattime is not declared");

        Assert.True(c!.MinValue == 1f, "mp_chattime min is " + c.MinValue + ", want 1");
        Assert.True(c.MaxValue == 120f, "mp_chattime max is " + c.MaxValue + ", want 120");

        // Read from the file rather than matched as one literal: stock spells
        // the bounds across two lines ("true, 1," then "true, 120 );"), so a
        // single-line needle finds nothing and the assertion would only ever
        // have been about this test's idea of the formatting.
        var stock = Regex.Replace(
            ValveSdk.Read(StockCpp), @"\s+", " ");
        Assert.Contains("true, 1, true, 120", stock, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The CLIENT half.
    // ------------------------------------------------------------------

    /// <summary>The ConVars the CLIENT assembly declares, by console name.</summary>
    private static Dictionary<string, ConVarBase> ManagedClient() =>
        DeclaredIn(typeof(client::SourceSharp.Client.ClMpChatTimeConVar).Assembly);

    /// <summary>
    /// Which of the fifteen the CLIENT ever compiled: the four that sit OUTSIDE
    /// <c>#ifdef GAME_DLL</c>. Parsed from the .cpp rather than listed, because
    /// which side of that directive a declaration falls on is exactly the thing
    /// a reader gets wrong.
    /// </summary>
    private static HashSet<string> StockClientSideNames()
    {
        var text = ValveSdk.Read(StockCpp);
        text = Regex.Replace(text, @"//[^\n]*", "");

        // Drop every #ifdef GAME_DLL ... #endif region, tracking DEPTH.
        //
        // A non-greedy regex was tried and is wrong: the GAME_DLL block contains
        // a nested `#ifndef TF_DLL ... #endif` around mp_waitingforplayers_time,
        // so the match ended at the inner #endif and nine names came back
        // instead of four. Preprocessor nesting is not a regex's job.
        var kept = new System.Text.StringBuilder();
        int depth = 0;          // how deep inside a GAME_DLL region
        int nested = 0;         // conditional depth once inside one

        foreach (var line in text.Split('\n'))
        {
            var t = line.TrimStart();

            if (depth == 0 && Regex.IsMatch(t, @"^#ifdef\s+GAME_DLL\b"))
            {
                depth = 1;
                nested = 0;
                continue;
            }

            if (depth > 0)
            {
                if (Regex.IsMatch(t, @"^#(if|ifdef|ifndef)\b"))
                    nested++;
                else if (Regex.IsMatch(t, @"^#endif\b"))
                {
                    if (nested == 0) { depth = 0; continue; }
                    nested--;
                }
                continue;
            }

            kept.Append(line).Append('\n');
        }

        return Regex.Matches(kept.ToString(), @"\bConVar\s+\w+\s*\(\s*""(?<name>[^""]+)""")
                    .Select(m => m.Groups["name"].Value)
                    .ToHashSet();
    }

    [Fact]
    public void TheClientDeclaresExactlyTheFourItUsedToCompile()
    {
        var clientSide = StockClientSideNames();

        Assert.True(clientSide.Count == 4,
            "parsed " + clientSide.Count + " client-side ConVars out of " + StockCpp
            + ", want 4 (" + string.Join(", ", clientSide.OrderBy(x => x))
            + "). Eleven of the fifteen are inside #ifdef GAME_DLL; if that count "
            + "moved, the client's half of this port is the wrong size.");

        var mine = ManagedClient();
        foreach (var name in clientSide.OrderBy(x => x))
        {
            Assert.True(mine.ContainsKey(name),
                "the client assembly does not declare " + name + ", which it used "
                + "to compile from " + StockCpp + ". All four are "
                + "FCVAR_REPLICATED, and a replicated ConVar only one side "
                + "declares stops tracking the server.");

            Assert.True(mine[name].Flags.HasFlag(ConVarFlags.Replicated),
                name + " is not Replicated on the client side.");
        }
    }

    [Fact]
    public void TheClientDoesNotGainTheElevenItNeverHad()
    {
        var clientSide = StockClientSideNames();
        var mine = ManagedClient();
        var stock = ParseStock();

        var gained = stock.Keys
            .Where(n => !clientSide.Contains(n) && mine.ContainsKey(n))
            .OrderBy(x => x)
            .ToList();

        Assert.True(gained.Count == 0,
            "the client assembly declares " + gained.Count + " ConVar(s) the client "
            + "never compiled: " + string.Join(", ", gained) + ". These are inside "
            + "#ifdef GAME_DLL in " + StockCpp + "; adding them to the client is "
            + "this port changing behaviour while claiming to preserve it.");
    }

    /// <summary>
    /// <c>mp_mapcycle_empty_timeout_seconds</c> is FCVAR_REPLICATED and sits
    /// inside <c>#ifdef GAME_DLL</c>, so stock never gives it a client half — it
    /// is replicated to nobody. Left exactly as it is, and asserted so that
    /// "fix" it looks like a decision rather than a tidy-up.
    /// </summary>
    [Fact]
    public void TheReplicatedConVarWithNoClientHalfStillHasNone()
    {
        var stock = ParseStock();
        Assert.True(stock.ContainsKey("mp_mapcycle_empty_timeout_seconds"));
        Assert.Contains("FCVAR_REPLICATED", stock["mp_mapcycle_empty_timeout_seconds"].Flags);

        Assert.DoesNotContain("mp_mapcycle_empty_timeout_seconds", StockClientSideNames());

        Assert.False(ManagedClient().ContainsKey("mp_mapcycle_empty_timeout_seconds"),
            "the client now declares mp_mapcycle_empty_timeout_seconds. Stock does "
            + "not: it is replicated to nobody, and giving it a client half is a "
            + "behaviour change, not a port.");
    }
}
