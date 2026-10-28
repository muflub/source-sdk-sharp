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
