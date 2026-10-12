extern alias client;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The client's half of the ConVars two shared stock files declared on this
/// side: <c>game/shared/gamevars_shared.cpp</c> (mp_forcecamera,
/// mp_allowspectators, mp_friendlyfire, mp_fadetoblack, sv_hudhint_sound) and
/// <c>game/shared/sceneentity_shared.cpp</c> (scene_print, scene_clientflex).
/// plan_client_novalve C8: both TUs left client.so, their code having no
/// importer, and the declarations moved to SourceSharp.Client. Parsed out of the
/// pristine upstream files with the client's arms (no GAME_DLL, no CSTRIKE), and
/// each held to its name, default, help and flags.
/// </summary>
public class ClientSharedConVarOracleTests
{
    private readonly record struct Stock(string Name, string Default, string Flags, string Help);

    private static Dictionary<string, Stock> ParseStock(string relative)
    {
        var text = ValveSdk.Read(relative);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");

        // The client's arms: CSTRIKE is not this game (#else kept), and the
        // GAME_DLL callback is the server's.
        text = Regex.Replace(text, @"#ifdef\s+CSTRIKE\b.*?#else(?<keep>.*?)#endif", "${keep}", RegexOptions.Singleline);
        text = Regex.Replace(text, @"#ifdef\s+GAME_DLL\b.*?#endif", "", RegexOptions.Singleline);

        var found = new Dictionary<string, Stock>();
        foreach (Match m in Regex.Matches(text,
                     @"\bConVar\s+\w+\s*\(\s*""(?<name>[^""]+)""\s*,\s*""(?<def>[^""]*)""\s*,\s*(?<flags>[A-Za-z_0-9|\s]+?)\s*(,\s*""(?<help>(?:[^""\\]|\\.)*)""\s*)?\)",
                     RegexOptions.Singleline))
        {
            found[m.Groups["name"].Value] = new Stock(m.Groups["name"].Value, m.Groups["def"].Value,
                Regex.Replace(m.Groups["flags"].Value, @"\s+", ""), Regex.Unescape(m.Groups["help"].Value));
        }
        return found;
    }

    private static Dictionary<string, Stock> AllStock()
    {
        var all = ParseStock("src/game/shared/gamevars_shared.cpp");
        foreach (var (k, v) in ParseStock("src/game/shared/sceneentity_shared.cpp"))
            all.Add(k, v);
        return all;
    }

    private static Dictionary<string, ConVarBase> Client()
    {
        var assembly = typeof(client::SourceSharp.Client.ClMpChatTimeConVar).Assembly;
        var registry = new ConVarRegistry(SourceSharp.Tests.TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);
        return registry.All.Where(c => c.GetType().Assembly == assembly).ToDictionary(c => c.Name);
    }

    [Fact]
    public void TheTwoFilesAreStillTheShapeThisTestParses()
    {
        var stock = AllStock();
        Assert.Equal(new[] { "mp_allowspectators", "mp_fadetoblack", "mp_forcecamera", "mp_friendlyfire",
                             "scene_clientflex", "scene_print", "sv_hudhint_sound" },
                     stock.Keys.OrderBy(x => x, System.StringComparer.Ordinal));
    }

    [Fact]
    public void EachHasAClientDeclarationWithStocksDefaultHelpAndFlags()
    {
        var mine = Client();
        foreach (var (name, s) in AllStock().OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            Assert.True(mine.TryGetValue(name, out var c), name + " has no SourceSharp.Client declaration");
            Assert.True(c!.DefaultValue == s.Default, name + ": default \"" + c.DefaultValue + "\", stock \"" + s.Default + "\"");
            Assert.True(c.Help == s.Help, name + ": help \"" + c.Help + "\", stock \"" + s.Help + "\"");
            Assert.True(s.Flags.Contains("FCVAR_REPLICATED") == c.Flags.HasFlag(ConVarFlags.Replicated), name + ": replicated");
            Assert.True(s.Flags.Contains("FCVAR_NOTIFY") == c.Flags.HasFlag(ConVarFlags.Notify), name + ": notify");
        }
    }

    /// <summary>
    /// <c>r_FadeProps</c>, cdll_util.cpp's one ConVar, which stock declares only
    /// under <c>_DEBUG</c> (plan_client_novalve C8: the file left client.so with
    /// no importer). The client declares it in a Debug build, as stock did, and
    /// not otherwise. Its only stock reader is in cdll_util.cpp itself, so
    /// nothing reads it here either; it is kept for the K6 ConVar contract.
    /// </summary>
    [Fact]
    public void FadePropsIsDeclaredInDebugBuildsOnlyAsStockDeclaresIt()
    {
        var text = ValveSdk.Read("src/game/client/cdll_util.cpp");
        var m = Regex.Match(text, @"#ifdef\s+_DEBUG\s*ConVar\s+r_FadeProps\s*\(\s*""r_FadeProps""\s*,\s*""(?<def>[^""]*)""\s*\)\s*;\s*#endif");
        Assert.True(m.Success, "cdll_util.cpp no longer declares r_FadeProps under _DEBUG the way this test parses");

        var mine = Client();
#if DEBUG
        Assert.True(mine.TryGetValue("r_FadeProps", out var c), "a Debug client declares no r_FadeProps");
        Assert.Equal(m.Groups["def"].Value, c!.DefaultValue);
        Assert.Equal("", c.Help);
        Assert.False(c.Flags.HasFlag(ConVarFlags.Replicated));
#else
        Assert.False(mine.ContainsKey("r_FadeProps"), "a Release client declares r_FadeProps; stock's is _DEBUG only");
#endif
    }
}
