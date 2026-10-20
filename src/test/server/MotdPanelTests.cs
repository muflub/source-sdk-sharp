using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's FinishClientPutInServer (hl2mp_client.cpp), from ClientActive: the
/// joining player is shown the info panel -- title the hostname (else
/// "MESSAGE OF THE DAY"), type 1 (the string table entry), msg "motd",
/// unload sv_motd_unload_on_dismissal -- reliably, to them alone.
/// </summary>
public class MotdPanelTests
{
    private static (MotdPanel Panel, RecordingEngine Engine, ConVarRegistry ConVars) Rig(string? hostname)
    {
        var engine = Fake();
        engine.Returns("Console.FindConVarValue", hostname);
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        return (new MotdPanel(engine, engine, convars), engine, convars);
    }

    private static (VguiMenu Menu, int Slot, bool Reliable) Sent(RecordingEngine engine)
    {
        var send = Assert.Single(engine.AllOf("UserMessages.Send"), c => (string)c.Args[0]! == "VGUIMenu");
        var reader = new UserMessageReader((byte[])send.Args[4]!);
        return (VguiMenu.Read(ref reader), (int)send.Args[2]!, (bool)send.Args[3]!);
    }

    [Fact]
    public void A_joining_player_is_shown_the_motd_info_panel()
    {
        var (panel, engine, _) = Rig("My Server");

        panel.OnClientActive(3);

        var (menu, slot, reliable) = Sent(engine);
        Assert.Equal(("info", true, 3, true), (menu.Name, menu.Show, slot, reliable));
        Assert.Equal(new[] { "title", "type", "msg", "unload" }, menu.Keys);
        Assert.Equal(new[] { "My Server", "1", "motd", "0" }, menu.Values);
    }

    [Fact]
    public void With_no_hostname_the_title_is_stocks()
    {
        var (panel, engine, _) = Rig(null);
        panel.OnClientActive(2);
        Assert.Equal("MESSAGE OF THE DAY", Sent(engine).Menu.Values[0]);
    }

    [Fact]
    public void Unloading_on_dismissal_follows_the_convar()
    {
        var (panel, engine, convars) = Rig("x");
        convars.All.Single(c => c.Name == "sv_motd_unload_on_dismissal").Bind(-1, "sv_motd_unload_on_dismissal", "1");
        panel.OnClientActive(2);
        Assert.Equal("1", Sent(engine).Menu.Values[3]);
    }
}
