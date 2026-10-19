extern alias client;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

// The client's declarations are of the client's copy of the shared interop
// (plan_shim_areas_split phase 3), so they are read with the client's registry
// and compared as the client's types.
using ConVarAttribute = client::SourceSharp.Abi.ConVarAttribute;
using ConVarBase = client::SourceSharp.Abi.ConVarBase;
using ConVarFlags = client::SourceSharp.Abi.ConVarFlags;
using ConVarRegistry = client::SourceSharp.Abi.ConVarRegistry;

namespace SourceSharp.OracleTests;

/// <summary>
/// flashlighteffect.cpp's convars (C7 cluster 12: the TU left client.so).
/// Parsed out of the pristine upstream file; each the client declares is held
/// to its name, default, help and flags. Two are the material system's and the
/// shader APIs' (stock's were children of theirs), so the client declares
/// neither and reads them by name.
/// </summary>
public class FlashlightConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    internal static readonly string[] EngineOwned = ["mat_slopescaledepthbias_shadowmap", "mat_depthbias_shadowmap"];

    private static List<Stock> AllStock()
    {
        string text = ValveSdk.Read("src/game/client/flashlighteffect.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        var found = new List<Stock>();
        // A definition, not an `extern ConVar x;`: a name and an argument list.
        foreach (Match m in Regex.Matches(text, @"\bConVar\s+\w+\s*\(\s*""([^""]+)""\s*,\s*""([^""]*)""\s*(?:,\s*([^,)]+?)\s*)?(?:,\s*""([^""]*)""\s*)?(?:,\s*\w+\s*)?\)\s*;"))
            found.Add(new Stock(m.Groups[1].Value, m.Groups[2].Value, Regex.Replace(m.Groups[3].Value, @"\s+", ""), m.Groups[4].Value));
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Flashlight.FlashlightConVars).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheEighteenThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(18, stock.Count);
        Assert.Equal(18, stock.Select(s => s.Name).Distinct().Count());
        Assert.All(EngineOwned, n => Assert.Contains(stock, s => s.Name == n));
        // The one with a callback still parses whole (its help and flags).
        Assert.Contains(new Stock("r_newflashlight", "1", "FCVAR_CHEAT", ""), stock);
    }

    [Fact]
    public void EachTheClientDeclaresHasStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        int held = 0;
        foreach (var s in AllStock())
        {
            if (EngineOwned.Contains(s.Name))
            {
                Assert.False(mine.ContainsKey(s.Name), s.Name + " is the material system's; the client declares no alias of it");
                continue;
            }
            Assert.True(mine.TryGetValue(s.Name, out var c), s.Name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, s.Name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, s.Name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            Assert.True(s.Flags == "FCVAR_CHEAT", s.Name + ": stock's flags are " + s.Flags + ", which this test does not map");
            Assert.True(c.Flags == ConVarFlags.Cheat, s.Name + ": flags " + c.Flags + ", stock FCVAR_CHEAT");
            held++;
        }
        Assert.Equal(16, held);
    }
}
