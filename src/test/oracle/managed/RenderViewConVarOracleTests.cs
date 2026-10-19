extern alias client;
using System.Collections.Generic;
using System.Globalization;
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
/// view.cpp's, viewrender.cpp's and viewdebug.cpp's convars (plan_client_novalve
/// C7 step 3: the TUs left client.so). Parsed out of the pristine upstream files;
/// each the client declares is held to its name, default, help, flags and
/// bounds. Four stay native (fork TUs link them by object, or a change callback
/// is native), five are the engine's own, and pyro_dof is the TF2 client
/// library's.
/// </summary>
public class RenderViewConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help, string? Min, string? Max);

    internal static readonly string[] Native = ["mat_wireframe", "r_DrawDetailProps", "cl_drawshadowtexture", "cl_software_cursor"];

    // The engine defines these itself (stock's were children of them); the
    // managed registration refuses an alias, so the client declares none.
    internal static readonly string[] EngineOwned =
        ["r_drawtranslucentworld", "r_flashlightdrawdepth", "r_visocclusion", "r_waterforceexpensive", "r_waterforcereflectentities"];

    private static IEnumerable<string> Args(string text)
    {
        var parts = new List<string>();
        int depth = 0;
        bool quoted = false;
        var cur = new System.Text.StringBuilder();
        foreach (char ch in text)
        {
            if (ch == '"')
                quoted = !quoted;
            if (!quoted && ch == '(')
                depth++;
            if (!quoted && ch == ')')
                depth--;
            if (ch == ',' && !quoted && depth == 0)
            {
                parts.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(ch);
            }
        }
        parts.Add(cur.ToString().Trim());
        return parts;
    }

    // Each name's LAST declaration in file order: where stock declares one twice
    // (#ifdef TF_CLIENT_DLL ... #else ...), the #else -- this game's -- is the later.
    private static Dictionary<string, Stock> AllStock()
    {
        var found = new Dictionary<string, Stock>();
        foreach (string file in new[] { "view.cpp", "viewrender.cpp", "viewdebug.cpp" })
        {
            string text = ValveSdk.Read("src/game/client/" + file);
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//[^\n]*", "");
            foreach (Match m in Regex.Matches(text, @"\bConVar\s+\w+\s*\((.*?)\)\s*;", RegexOptions.Singleline))
            {
                var a = Args(m.Groups[1].Value).ToList();
                string help = a.Count > 3 && a[3].StartsWith('"') ? Regex.Replace(a[3], "\"\\s*\"", "")[1..^1] : "";
                string? min = a.Count >= 8 && a[4] == "true" ? a[5] : null;
                string? max = a.Count >= 8 && a[6] == "true" ? a[7] : null;
                string name = a[0].Trim('"');
                found[name] = new Stock(name, a[1].Trim('"'), a.Count > 2 ? Regex.Replace(a[2], @"\s+", "") : "", Regex.Unescape(help), min, max);
            }
        }
        return found;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.Render.RFarzConVar).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(SourceSharp.Tests.ClientDoubles.UnattachedEngine()));
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    private static float Bound(string expr) => expr.Trim() switch
    {
        "(1.0f / 640.0f)" => 1.0f / 640.0f,
        string s => float.Parse(s.TrimEnd('f'), CultureInfo.InvariantCulture),
    };

    [Fact]
    public void TheFilesStillDeclareTheSeventyFourThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(74, stock.Count);
        Assert.All(Native, n => Assert.True(stock.ContainsKey(n), n + " is not stock's"));
        Assert.True(stock.ContainsKey("pyro_dof"));
    }

    [Fact]
    public void EachTheClientDeclaresHasStocksDefaultHelpFlagsAndBounds()
    {
        var mine = Client();
        int held = 0;
        foreach (var (name, s) in AllStock().OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            if (name == "pyro_dof" || Native.Contains(name) || EngineOwned.Contains(name))
            {
                Assert.False(mine.ContainsKey(name), name + " is declared by the core client, which keeps no pyro convar and none native code links");
                continue;
            }
            Assert.True(mine.TryGetValue(name, out var c), name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            Assert.True(s.Flags.Contains("FCVAR_CHEAT") == c.Flags.HasFlag(ConVarFlags.Cheat), name + ": cheat");
            Assert.True(s.Flags.Contains("FCVAR_ARCHIVE") == c.Flags.HasFlag(ConVarFlags.Archive), name + ": archive");
            Assert.True(s.Flags.Contains("FCVAR_DONTRECORD") == c.Flags.HasFlag(ConVarFlags.DontRecord), name + ": dontrecord");
            Assert.True(s.Flags.Contains("FCVAR_NEVER_AS_STRING") == c.Flags.HasFlag(ConVarFlags.NeverAsString), name + ": never as string");
            Assert.True(s.Flags.Contains("FCVAR_ALLOWED_IN_COMPETITIVE") == c.Flags.HasFlag(ConVarFlags.AllowedInCompetitive), name + ": competitive");
            var attr = (ConVarAttribute)System.Attribute.GetCustomAttribute(c.GetType(), typeof(ConVarAttribute))!;
            Assert.True(s.Min is null ? float.IsNaN(attr.Min) : attr.Min == Bound(s.Min), name + ": min");
            Assert.True(s.Max is null ? float.IsNaN(attr.Max) : attr.Max == Bound(s.Max), name + ": max");
            held++;
        }
        Assert.Equal(74 - Native.Length - EngineOwned.Length - 1, held);
    }
}
