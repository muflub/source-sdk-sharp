using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE8: the client's movement forwarder, prediction frame
/// and third-person camera reach managed code through the managed-built
/// IManagedPredictionClient, not the ManagedApi table. Read from the code
/// (comments stripped), so a commented-out call cannot stand in for a live one.
/// </summary>
public class ClientPredictionAreaTests
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
    public void TheBootstrapFillsNoneOfTheSlots()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->MoveProcessMovement", entry);
        Assert.DoesNotContain("pOut->ClientPredictionUpdate", entry);
        Assert.DoesNotContain("pOut->ThirdPersonCamera", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedPredictionClient"",\s*v => ManagedAreaFactory\.Create\(v, PredictionClientBinding\.For\(_engine\.Crossings, this, this, _movement\)\)\);"),
                       module);
    }
}
