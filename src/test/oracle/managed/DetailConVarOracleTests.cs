extern alias client;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// detailobjectsystem.cpp's convars (C7 cluster 10: the TU left client.so).
/// Parsed out of the pristine upstream file; each the client declares is held
/// to its name, default, help and flags. The four shape convars are under
/// USE_DETAIL_SHAPES (DOD and CS:S only) and mat_fullbright is the engine's
/// (stock's was a child of it), so the client declares none of them.
/// </summary>
public class DetailConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    internal static readonly string[] NotDeclared =
        ["cl_detail_max_sway", "cl_detail_avoid_radius", "cl_detail_avoid_force", "cl_detail_avoid_recover_speed", "mat_fullbright"];

    private static Dictionary<string, Stock> AllStock()
    {
        string text = ValveSdk.Read("src/game/client/detailobjectsystem.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        var found = new Dictionary<string, Stock>();
        foreach (Match m in Regex.Matches(text, @"\bConVar\s+\w+\s*\(\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*([^,)]*?)\s*(?:,\s*""([^""]*)""\s*)?\)\s*;"))
            found[m.Groups[1].Value] = new Stock(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Trim(), m.Groups[4].Value);
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Detail.DetailDistConVar).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheNineThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(9, stock.Count);
        Assert.All(NotDeclared, n => Assert.True(stock.ContainsKey(n), n + " is not stock's"));
    }

    [Fact]
    public void EachTheClientDeclaresHasStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        int held = 0;
        foreach (var (name, s) in AllStock().OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            if (NotDeclared.Contains(name))
            {
                Assert.False(mine.ContainsKey(name), name + " is declared by the client");
                continue;
            }
            Assert.True(mine.TryGetValue(name, out var c), name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            Assert.True(s.Flags.Contains("FCVAR_CHEAT") == c.Flags.HasFlag(ConVarFlags.Cheat), name + ": cheat");
            Assert.True(s.Flags.Contains("FCVAR_ARCHIVE") == c.Flags.HasFlag(ConVarFlags.Archive), name + ": archive");
            held++;
        }
        Assert.Equal(4, held);
    }
}
