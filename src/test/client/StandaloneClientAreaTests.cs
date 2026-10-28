using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE5: the client's standalone entity hierarchy (create,
/// spawn, removal, data, draw, dormancy) and its net state layout asks reach
/// managed code through the managed-built IManagedStandaloneClient, not the
/// ManagedApi table. Read from the code (comments stripped), so a commented-out
/// call cannot stand in for a live one.
/// </summary>
public class StandaloneClientAreaTests
{
    private static readonly string[] Slots =
    [
        "OnStandaloneCreate", "OnStandaloneSpawn", "OnStandaloneRemoved",
        "OnStandaloneDataChanged", "OnStandaloneDraw", "OnStandaloneDrawBatch", "OnStandaloneDormancyChanged",
        "NetStateFieldOffset", "NetStateFieldBits",
    ];

    private static string Code(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(file);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static string Shell() => Code("src", "client", "native", "shell_standalone_client.cpp");

    private static string Ask() => Code("src", "shared", "native", "standalone_player_sync.h");

    [Fact]
    public void EveryShellCallSiteTakesTheClientsArea()
    {
        string shell = Shell();

        // One accessor read per function that crosses: removal, create, the
        // data update (spawn and data share it), dormancy, draw and the view's
        // draw batch (C7 item 7). The shell's two layout asks went with the
        // surface renderer's blob reads (lane M1-A); the player sync's are
        // TheClientsLayoutAskTakesTheClientsArea's.
        Assert.Equal(6, Regex.Matches(shell, @"IManagedStandaloneClient \*pStandalone = ManagedStandaloneClient\(\);").Count);
        foreach (string slot in Slots.Where(s => !s.StartsWith("NetState", StringComparison.Ordinal)))
            Assert.Single(Regex.Matches(shell, $@"pStandalone->{slot}\("));
        Assert.DoesNotContain("pStandalone->NetStateField", shell);
    }

    [Fact]
    public void TheClientsLayoutAskTakesTheClientsArea()
    {
        string ask = Ask();

        Assert.Single(Regex.Matches(ask, @"pStandalone->NetStateFieldOffset\( pClassName, pFieldName \)"));
        Assert.Single(Regex.Matches(ask, @"pStandalone->NetStateFieldBits\( pClassName, pFieldName \)"));
        Assert.Single(Regex.Matches(ask, @"pAsk->pfnFieldOffset = &SourceSharp_ClientNetStateFieldOffset;"));
        Assert.Single(Regex.Matches(ask, @"pAsk->pfnFieldBits   = &SourceSharp_ClientNetStateFieldBits;"));
    }

    [Fact]
    public void NoNativeCodeReadsTheTableSlots()
    {
        string shell = Shell();
        string ask = Ask();

        Assert.Contains("ManagedStandaloneClient()", shell);
        Assert.Contains("ManagedStandaloneClient()", ask);
        foreach (string code in new[] { shell, ask })
        {
            Assert.DoesNotContain("SourceSharp_GetManagedApi", code);
            foreach (string slot in Slots)
                Assert.DoesNotContain($"pApi->{slot}", code);
        }
    }

    [Fact]
    public void TheBootstrapFillsNoneOfTheSlots()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        foreach (string slot in Slots)
            Assert.DoesNotContain($"pOut->{slot}", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedStandaloneClient"",\s*v => ManagedAreaFactory\.Create\(v, NewStandaloneBinding\(\)\)\);"),
                       module);
    }

    [Fact]
    public void TheLayoutAsksAnswerMinusOneOnAFault()
    {
        // The generated catch answers 0, a real offset; each ask keeps its own.
        string binding = Code("src", "client", "interop", "StandaloneClientBinding.cs");

        Assert.Equal(2, Regex.Matches(binding, @"ManagedVtable\.Fail\(log, ""IManagedStandaloneClient\.NetStateField(Offset|Bits)"", e\);\s*return -1;").Count);
    }
}
