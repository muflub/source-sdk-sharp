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
/// viewpostprocess.cpp's convars (plan_client_novalve C7: the TU left
/// client.so; its passes are managed's PostProcessPlanner). Parsed out of the
/// pristine upstream file; each the client declares is held to its name,
/// default, help and flags. Ten are the engine's own (stock's were children of
/// them) and stay undeclared, and the pyro_* family is the TF2 client
/// library's (its facts hold those).
/// </summary>
public class ViewPostProcessConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    internal static readonly string[] EngineOwned =
    [
        "mat_accelerate_adjust_exposure_down", "mat_dynamic_tonemapping", "mat_force_tonemap_scale", "mat_fullbright",
        "mat_hdr_level", "mat_hdr_manual_tonemap_rate", "mat_hdr_tonemapscale", "mat_show_ab_hdr",
        "mat_tonemap_algorithm", "mat_tonemapping_occlusion_use_stencil",
    ];

    private static Dictionary<string, Stock> AllStock()
    {
        var text = ValveSdk.Read("src/game/client/viewpostprocess.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        var found = new Dictionary<string, Stock>();
        foreach (Match m in Regex.Matches(text,
                     @"\bConVar\s+\w+\s*\(\s*""(?<name>[^""]+)""\s*,\s*""(?<def>[^""]*)""\s*(,\s*(?<flags>[A-Za-z_0-9|\s]+?)\s*)?(,\s*""(?<help>(?:[^""\\]|\\.)*)""\s*)?\)",
                     RegexOptions.Singleline))
        {
            found[m.Groups["name"].Value] = new Stock(m.Groups["name"].Value, m.Groups["def"].Value,
                Regex.Replace(m.Groups["flags"].Value, @"\s+", ""), Regex.Unescape(m.Groups["help"].Value));
        }
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Render.MatBloomscaleConVar).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheFiftyNineThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(59, stock.Count);
        Assert.Equal(10, stock.Keys.Count(k => k.StartsWith("pyro_", System.StringComparison.Ordinal)));
        Assert.All(EngineOwned, n => Assert.True(stock.ContainsKey(n), n + " is not stock's"));
    }

    [Fact]
    public void EachTheClientDeclaresHasStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        int held = 0;
        foreach (var (name, s) in AllStock().OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            if (name.StartsWith("pyro_", System.StringComparison.Ordinal) || EngineOwned.Contains(name))
            {
                Assert.False(mine.ContainsKey(name), name + " is declared by the core client, which keeps no pyro or engine-owned convar");
                continue;
            }
            Assert.True(mine.TryGetValue(name, out var c), name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            Assert.True(s.Flags.Contains("FCVAR_CHEAT") == c.Flags.HasFlag(ConVarFlags.Cheat), name + ": cheat");
            Assert.True(s.Flags.Contains("FCVAR_ARCHIVE") == c.Flags.HasFlag(ConVarFlags.Archive), name + ": archive");
            held++;
        }
        Assert.Equal(59 - 10 - EngineOwned.Length, held);
    }
}
