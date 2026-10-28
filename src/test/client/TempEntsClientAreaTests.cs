using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE6: the client's temp entities, its temp entity pool,
/// and the impact and sprite rules reach managed code through the managed-built
/// IManagedTempEntsClient, not the ManagedApi table. Read from the code
/// (comments stripped), so a commented-out call cannot stand in for a live one.
/// </summary>
public class TempEntsClientAreaTests
{
    private static readonly string[] Slots =
    [
        "OnTempEntity", "OnNativeTempEntity", "OnImpactChoice",
        "OnTempEntPoolUpdate", "OnTempEntPoolClear", "SuppressLocalTempEnt",
    ];

    // Each native caller and the calls it makes on the area, with their counts.
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
            Assert.DoesNotContain($"pOut->{slot} ", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedTempEntsClient"",\s*v => ManagedAreaFactory\.Create\(v, new TempEntsClientBinding\(_engine\.Crossings, Log, this\)\)\);"),
                       module);
    }
}
