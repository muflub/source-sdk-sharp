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
/// glow_overlay.cpp's convars (C7 glow overlays: the TU left client.so).
/// Parsed out of the pristine upstream file; each the client declares is held
/// to its name, default, help and flags (SourceSharp.Client.Render.GlowOverlays).
/// building_cubemaps is the engine's (stock's was a child of it): the managed
/// registration refuses an alias, so the client declares none and reads it by
/// name (BuildingCubemapsWatch).
/// </summary>
public class GlowOverlayConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    internal const string EngineOwned = "building_cubemaps";

    private static List<Stock> AllStock()
    {
        string text = ValveSdk.Read("src/game/client/glow_overlay.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        var found = new List<Stock>();
        foreach (Match m in Regex.Matches(text, @"\bConVar\s+\w+\s*\(\s*""([^""]+)""\s*,\s*""([^""]*)""\s*(?:,\s*([^,)""]+?)\s*)?(?:,\s*""([^""]*)""\s*)?\)\s*;"))
            found.Add(new Stock(m.Groups[1].Value, m.Groups[2].Value, Regex.Replace(m.Groups[3].Value, @"\s+", ""), m.Groups[4].Value));
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Render.GlowOverlays.GlowSettings).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheThreeThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(3, stock.Count);
        Assert.Contains(new Stock("cl_ShowSunVectors", "0", "0", ""), stock);
        Assert.Contains(new Stock("cl_sun_decay_rate", "0.05", "FCVAR_CHEAT", ""), stock);
        Assert.Contains(new Stock("building_cubemaps", "0", "", ""), stock);
    }

    [Fact]
    public void EachTheClientDeclaresHasStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        int held = 0;
        foreach (var s in AllStock())
        {
            if (s.Name == EngineOwned)
                continue;
            Assert.True(mine.TryGetValue(s.Name, out var c), s.Name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, s.Name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, s.Name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            ConVarFlags expected = s.Flags switch
            {
                "FCVAR_CHEAT" => ConVarFlags.Cheat,
                "" or "0" => ConVarFlags.None,
                _ => throw new Xunit.Sdk.XunitException(s.Name + ": stock's flags are " + s.Flags + ", which this test does not map"),
            };
            Assert.True(c.Flags == expected, s.Name + ": flags " + c.Flags + ", stock " + s.Flags);
            held++;
        }
        Assert.Equal(2, held);
    }

    [Fact]
    public void BuildingCubemapsIsTheEnginesAndIsReadByName()
    {
        Assert.False(Client().ContainsKey(EngineOwned), EngineOwned + " is declared in C#; the engine defines it and refuses the alias");
        Assert.Equal(EngineOwned, client::SourceSharp.Client.Render.PixelVisibility.BuildingCubemapsWatch.Name);
    }
}
