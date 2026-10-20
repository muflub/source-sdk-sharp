extern alias client;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

using ConCommandAttribute = client::SourceSharp.Abi.ConCommandAttribute;
using ConVarBase = client::SourceSharp.Abi.ConVarBase;
using ConVarFlags = client::SourceSharp.Abi.ConVarFlags;
using ConVarRegistry = client::SourceSharp.Abi.ConVarRegistry;

namespace SourceSharp.OracleTests;

/// <summary>
/// c_pixel_visibility.cpp's convars and its command (C7 pixel visibility: the
/// TU left client.so). Parsed out of the pristine upstream file; each is held
/// to its name, default, help and flags in the client's declarations
/// (SourceSharp.Client.Render.PixelVisibility). gl_can_query_fast is declared
/// under #ifdef OSX, which is not a target.
/// </summary>
public class PixelVisConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    private const string OsxOnly = "gl_can_query_fast";

    private static string StockText()
    {
        string text = ValveSdk.Read("src/game/client/c_pixel_visibility.cpp");
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    // ConVar name( "name", "default" [, flags [, "help" [, callback ]]] );
    private static List<Stock> AllStock()
    {
        var found = new List<Stock>();
        foreach (Match m in Regex.Matches(StockText(),
                     @"\bConVar\s+\w+\s*\(\s*""([^""]+)""\s*,\s*""([^""]*)""\s*(?:,\s*([^,)""]+?)\s*)?(?:,\s*""([^""]*)""\s*)?(?:,\s*\w+\s*)?\)\s*;"))
            found.Add(new Stock(m.Groups[1].Value, m.Groups[2].Value, Regex.Replace(m.Groups[3].Value, @"\s+", ""), m.Groups[4].Value));
        return found;
    }

    private static Assembly ClientAssembly => typeof(client::SourceSharp.Client.Render.PixelVisibility.PixelVisSettings).Assembly;

    private static Dictionary<string, ConVarBase> Client()
    {
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(ClientAssembly);
        return registry.All.Where(c => c.GetType().Assembly == ClientAssembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheFileStillDeclaresTheFiveThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(5, stock.Count);
        Assert.Contains(new Stock("r_pixelvisibility_partial", "1", "", ""), stock);
        Assert.Contains(new Stock("r_dopixelvisibility", "1", "FCVAR_ALLOWED_IN_COMPETITIVE", ""), stock);
        Assert.Contains(new Stock("r_drawpixelvisibility", "0", "0", "Show the occlusion proxies"), stock);
        Assert.Contains(new Stock("r_pixelvisibility_spew", "0", "", ""), stock);
        Assert.Contains(new Stock(OsxOnly, "0", "", ""), stock);
    }

    [Fact]
    public void EachHasStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        int held = 0;
        foreach (var s in AllStock())
        {
            if (s.Name == OsxOnly)
            {
                Assert.False(mine.ContainsKey(s.Name), s.Name + " is OSX-only in stock; the client declares none");
                continue;
            }
            Assert.True(mine.TryGetValue(s.Name, out var c), s.Name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, s.Name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, s.Name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            ConVarFlags expected = s.Flags switch
            {
                "FCVAR_ALLOWED_IN_COMPETITIVE" => ConVarFlags.AllowedInCompetitive,
                "" or "0" => ConVarFlags.None,
                _ => throw new Xunit.Sdk.XunitException(s.Name + ": stock's flags are " + s.Flags + ", which this test does not map"),
            };
            Assert.True(c.Flags == expected, s.Name + ": flags " + c.Flags + ", stock " + s.Flags);
            held++;
        }
        Assert.Equal(4, held);
    }

    [Fact]
    public void TheDebugCommandHasStocksNameAndHelp()
    {
        Match m = Regex.Match(StockText(), @"\bCON_COMMAND\s*\(\s*(\w+)\s*,\s*""([^""]*)""\s*\)");
        Assert.True(m.Success, "c_pixel_visibility.cpp no longer declares a CON_COMMAND this test parses");
        Assert.Equal("pixelvis_debug", m.Groups[1].Value);

        var mine = ClientAssembly.GetTypes()
            .Select(t => t.GetCustomAttribute<ConCommandAttribute>())
            .Where(a => a is not null && a.Name == m.Groups[1].Value)
            .ToList();
        Assert.Single(mine);
        Assert.Equal(m.Groups[2].Value, mine[0]!.Help);
    }
}
