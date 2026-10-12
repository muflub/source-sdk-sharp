using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE2: the client's console commands and convar changes
/// reach managed code through the managed-built IManagedCommandsClient, not the
/// ManagedApi table. Read from the code (comments stripped), so a commented-out
/// call cannot stand in for a live one.
/// </summary>
public class ClientCommandsAreaTests
{
    private static string Code(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        string text = File.ReadAllText(file);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    [Fact]
    public void BothNativeCallSitesTakeTheClientsArea()
    {
        string system = Code("src", "shared", "native", "sourcesharp_gamesystem.cpp");

        Assert.Equal(2, Regex.Matches(system, @"\bManagedCommandsClient\(\)").Count);
        Assert.Single(Regex.Matches(system, @"pCommands->ConCommandDispatch\("));
        Assert.Single(Regex.Matches(system, @"pCommands->ConVarChanged\("));
    }

    [Fact]
    public void NoNativeCodeReadsEitherTableSlot()
    {
        string system = Code("src", "shared", "native", "sourcesharp_gamesystem.cpp");

        Assert.Contains("ManagedCommandsClient()", system);
        Assert.DoesNotContain("g_ManagedApi.ConCommandDispatch", system);
        Assert.DoesNotContain("g_ManagedApi.ConVarChanged", system);
    }

    [Fact]
    public void TheBootstrapFillsNeitherSlot()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->ConCommandDispatch", entry);
        Assert.DoesNotContain("pOut->ConVarChanged", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedCommandsClient"",\s*v => ManagedAreaFactory\.Create\(v, new CommandsClientBinding\(_engine\.Crossings, this, this\)\)\);"),
                       module);
    }
}
