extern alias client;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

using ConVarBase = client::SourceSharp.Abi.ConVarBase;
using ConVarFlags = client::SourceSharp.Abi.ConVarFlags;
using ConVarRegistry = client::SourceSharp.Abi.ConVarRegistry;

namespace SourceSharp.OracleTests;

/// <summary>
/// clientleafsystem.cpp's convars (C7 cluster 12 L2: the TU left client.so).
/// Parsed out of the pristine upstream file; each the client declares is held
/// to its name, default, help and flags. r_portalsopenall is the engine's too
/// (stock's client copy was a child of it), so the client
/// declares no alias of it and reads it by name.
/// </summary>
public class LeafSystemConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    internal static readonly string[] EngineOwned = ["r_portalsopenall"];

    private static List<Stock> AllStock()
    {
        string text = ValveSdk.Read("src/game/client/clientleafsystem.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        var found = new List<Stock>();
        foreach (Match m in Regex.Matches(text, @"\bConVar\s+\w+\s*\(\s*""([^""]+)""\s*,\s*""([^""]*)""\s*(?:,\s*([^,)""]+?)\s*)?(?:,\s*""([^""]*)""\s*)?\)\s*;"))
            found.Add(new Stock(m.Groups[1].Value, m.Groups[2].Value, Regex.Replace(m.Groups[3].Value, @"\s+", ""), m.Groups[4].Value));
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Render.LeafSystem.LeafListConVars).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheFourThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(4, stock.Count);
        Assert.Contains(new Stock("cl_drawleaf", "-1", "FCVAR_CHEAT", ""), stock);
        Assert.Contains(new Stock("r_PortalTestEnts", "1", "FCVAR_CHEAT", "Clip entities against portal frustums."), stock);
        Assert.Contains(new Stock("r_portalsopenall", "0", "FCVAR_CHEAT", "Open all portals"), stock);
        Assert.Contains(new Stock("cl_threaded_client_leaf_system", "0", "", ""), stock);
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
                Assert.False(mine.ContainsKey(s.Name), s.Name + " is the engine's; the client declares no alias of it");
                continue;
            }
            Assert.True(mine.TryGetValue(s.Name, out var c), s.Name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, s.Name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, s.Name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            ConVarFlags expected = s.Flags switch
            {
                "FCVAR_CHEAT" => ConVarFlags.Cheat,
                "" => ConVarFlags.None,
                _ => throw new Xunit.Sdk.XunitException(s.Name + ": stock's flags are " + s.Flags + ", which this test does not map"),
            };
            Assert.True(c.Flags == expected, s.Name + ": flags " + c.Flags + ", stock " + s.Flags);
            held++;
        }
        Assert.Equal(3, held);
    }
}
