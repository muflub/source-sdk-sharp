using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_vtables CE3: the client's game events reach managed code through
/// the managed-built IManagedEventsClient, not the ManagedApi table; its user
/// messages through IManagedUserMessagesClient since plan_side_isolation P2. Read from the code (comments stripped), so a commented-out
/// call cannot stand in for a live one. The game event listener's two arms are
/// pinned natively as well (gameevent_listener_lifetime_test).
/// </summary>
public class EventsClientAreaTests
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
        Assert.DoesNotContain("pOut->OnGameEvent", entry);
        Assert.DoesNotContain("pOut->OnUserMessage", entry);
    }

    [Fact]
    public void TheClientRegistersTheBinding()
    {
        string module = Code("src", "client", "managed", "ClientModule.cs");

        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedEventsClient"",\s*v => ManagedAreaFactory\.Create\(v, new EventsClientBinding\(_engine\.Crossings, this\)\)\);"),
                       module);
        Assert.Matches(new Regex(@"_bindings\.Add\(""IManagedUserMessagesClient"",\s*v => ManagedAreaFactory\.Create\(v, new UserMessagesClientBinding\(_engine\.Crossings, _userMessageReceiver!\)\)\);"),
                       module);
    }
}
