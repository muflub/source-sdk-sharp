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
    private static readonly (string File, string Slot, int Calls)[] CallSites =
    [
        // The receiving shell's PostDataUpdate. The local path's Play was the
        // second caller and had no caller of its own (deleted, 2026-10-19).
        ("shell_tempents_client.cpp", "OnTempEntity", 1),
        ("shell_tempents_client.cpp", "OnNativeTempEntity", 1),
        ("shell_te_pool_client.cpp", "OnTempEntPoolUpdate", 1),
        ("shell_te_pool_client.cpp", "OnTempEntPoolClear", 1),
        // te->DispatchEffect's prediction filter (lane M1-C). OnImpactChoice has
        // no native caller since the impact is C# (BulletImpactEffect).
        ("shell_te_forwards_client.cpp", "SuppressLocalTempEnt", 1),
    ];

    private static readonly string[] Callers =
        ["shell_tempents_client.cpp", "shell_te_pool_client.cpp", "shell_te_forwards_client.cpp"];

    private static string Code(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(file);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static string Native(string file) => Code("src", "client", "native", file);

    [Fact]
    public void EveryNativeCallSiteTakesTheClientsArea()
    {
        foreach (var (file, slot, calls) in CallSites)
            Assert.True(calls == Regex.Matches(Native(file), $@"pTempEnts->{slot}\(").Count,
                        $"{file} calls {slot} on IManagedTempEntsClient {calls} time(s)");

        foreach (string file in Callers)
            Assert.True(Regex.IsMatch(Native(file), @"IManagedTempEntsClient \*pTempEnts = ManagedTempEntsClient\(\)"),
                        $"{file} takes the area from ManagedTempEntsClient()");
    }

    [Fact]
    public void NoNativeCallerReadsTheTable()
    {
        foreach (string file in Callers)
        {
            string code = Native(file);
            Assert.Contains("ManagedTempEntsClient()", code);
            Assert.DoesNotContain("SourceSharp_GetManagedApi", code);
            foreach (string slot in Slots)
            {
                Assert.DoesNotContain($"pApi->{slot}", code);
                Assert.DoesNotContain($"g_ManagedApi.{slot}", code);
            }
        }
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
