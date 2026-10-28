using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE4: the client's HUD paint and CreateMove reach managed
/// code through managed-built areas, not the ManagedApi table. The HUD paint is
/// IManagedUiClient's; IManagedHudClient, whose last slot was the CreateMove
/// after-hook managed now calls itself, left in the serialised ABI pass
/// (2026-10-20). Read from the code (comments stripped), so a commented-out
/// call cannot stand in for a live one.
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
    public void TheBootstrapFillsNeitherSlot()
    {
        string entry = Code("src", "shared", "interop", "SideLoad.cs");

        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->OnHudPaint", entry);
        Assert.DoesNotContain("pOut->OnCreateMove", entry);
    }

    [Fact]
    public void TheClientRegistersNoHudArea()
    {
        // IManagedHudClient is gone: no binding is registered for it, while the
        // UI area the HUD paints through still is (the positive arm).
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.DoesNotContain("\"IManagedHudClient\"", module);
        Assert.DoesNotContain("HudClientBinding", module);
        Assert.Contains("_bindings.Add(\"IManagedUiClient\"", module);
    }
}
