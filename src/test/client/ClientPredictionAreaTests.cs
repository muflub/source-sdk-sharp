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

    private static string Movement() => Code("src", "shared", "native", "shell_movement.cpp");
    private static string Prediction() => Code("src", "client", "native", "shell_prediction.cpp");
    private static string Camera() => Code("src", "client", "native", "shell_client.cpp");

    [Fact]
    public void TheMovementForwardersClientArmTakesTheClientsArea()
    {
        string movement = Movement();

        Assert.Matches(new Regex(@"#else\s*IManagedPredictionClient \*pCommands = ManagedPredictionClient\(\);\s*#endif"),
                       movement);
        Assert.Single(Regex.Matches(movement, @"pCommands->MoveProcessMovement\( pState \)"));
    }

    [Fact]
    public void ThePredictionFrameTakesTheClientsArea()
    {
        Assert.Matches(new Regex(@"if \( IManagedPredictionClient \*pPrediction = ManagedPredictionClient\(\) \)\s*\{\s*pPrediction->ClientPredictionUpdate\("),
                       Prediction());
    }

    [Fact]
    public void BothThirdPersonCameraPhasesTakeTheClientsArea()
    {
        string camera = Camera();

        Assert.Single(Regex.Matches(camera, @"IManagedPredictionClient \*pPrediction = ManagedPredictionClient\(\);"));
        Assert.Equal(2, Regex.Matches(camera, @"if \( !pPrediction->ThirdPersonCamera\( &io \) \)").Count);
    }

    [Fact]
    public void NoNativeCallSiteReadsTheTableSlots()
    {
        string[] files = [Movement(), Prediction(), Camera()];

        foreach (string code in files)
        {
            Assert.Contains("ManagedPredictionClient()", code);
            Assert.DoesNotContain("SourceSharp_GetManagedApi", code);
            Assert.DoesNotContain("pApi->MoveProcessMovement", code);
            Assert.DoesNotContain("pApi->ClientPredictionUpdate", code);
            Assert.DoesNotContain("pApi->ThirdPersonCamera", code);
        }
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
