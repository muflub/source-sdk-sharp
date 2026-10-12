using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE4: the client's HUD paint and CreateMove reach managed
/// code through the managed-built IManagedHudClient, not the ManagedApi table.
/// Read from the code (comments stripped), so a commented-out call cannot stand
/// in for a live one.
/// </summary>
public class ClientHudAreaTests
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
    public void TheHudPaintTakesTheUisArea()
    {
        // plan_client_ui U2: the HUD panel paints the UI's HUD layer; the old
        // IManagedHudClient::OnHudPaint retired with the old HUD's drawing.
        string hud = Code("src", "client", "native", "shell_hud.cpp");

        Assert.Equal(4, Regex.Matches(hud, @"IManagedUiClient \*pUi = ManagedUiClient\(\)").Count);   // the HUD's Paint and OnScreenSizeChanged, the menu root's Paint and input
        Assert.Single(Regex.Matches(hud, @"pUi->Paint\( 0, nWide, nTall \);"));
        Assert.DoesNotContain("OnHudPaint", hud);
    }

    [Fact]
    public void CreateMoveTakesTheClientsArea()
    {
        // Lane c6-input: the whole command is ClientInput's now, through the
        // input thunk set's IManagedInputClient, and the HUD area's after-hook
        // (OnCreateMove) is called from managed, not from the shell.
        string input = Code("src", "client", "native", "shell_input.cpp");

        Assert.Single(Regex.Matches(input, @"p->InputCreateMove\( sequence_number, input_sample_frametime, active \? 1 : 0, &flat \);"));
        Assert.DoesNotContain("OnCreateMove", input);
    }

    [Fact]
    public void NeitherShellReadsTheTable()
    {
        foreach (var (name, area) in new[] { ("shell_hud.cpp", "ManagedUiClient()"), ("shell_input.cpp", "ManagedInputClient()") })
        {
            string shell = Code("src", "client", "native", name);

            Assert.Contains(area, shell);
            Assert.DoesNotContain("SourceSharp_GetManagedApi", shell);
            Assert.DoesNotContain("SourceSharpManagedApi", shell);
        }
    }

    [Fact]
    public void TheBootstrapFillsNeitherSlot()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->OnHudPaint", entry);
        Assert.DoesNotContain("pOut->OnCreateMove", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedHudClient"",\s*v => ManagedAreaFactory\.Create\(v, new HudClientBinding\(_engine\.Crossings, this\)\)\);"),
                       module);
    }
}
