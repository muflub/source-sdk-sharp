using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE9a: the client's crossing report and user-command seed
/// reach managed code through the managed-built IManagedDiagnosticsClient, and
/// its model-cache flush through the slot appended to IManagedRuntimeClient,
/// not the ManagedApi table. Read from the code (comments stripped), so a
/// commented-out call cannot stand in for a live one.
/// </summary>
public class DiagnosticsClientAreaTests
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
    public void TheBootstrapFillsNoneOfTheThreeSlots()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->CrossingsReport", entry);
        Assert.DoesNotContain("pOut->Md5PseudoRandom", entry);
        Assert.DoesNotContain("pOut->ClientInvalidateMdlCache", entry);
    }

    [Fact]
    public void TheClientRegistersTheDiagnosticsBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedDiagnosticsClient"",\s*v => ManagedAreaFactory\.Create\(v, new DiagnosticsClientBinding\(_engine\.Crossings, Log\)\)\);"),
                       module);
    }

    [Fact]
    public void TheRuntimeBindingForwardsTheFlushToTheHost()
    {
        string binding = Code("src", "client", "interop", "ClientRuntimeBinding.cs");

        Assert.Matches(new Regex(@"public void ClientInvalidateMdlCache\(\)\s*\{\s*crossings\.Count\(CrossingOp\.ClientInvalidateMdlCache\);\s*host\.InvalidateMdlCache\(\);\s*\}"),
                       binding);
    }
}
