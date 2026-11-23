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
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        return (new MotdPanel(engine, DeclaredConVars.Holding<HostnameConVar>(hostname), convars.Declared<MotdUnloadOnDismissalConVar>()), engine, convars);
    }

    /// <summary>The rig with <paramref name="gates"/> registered as the message's vetoes.</summary>
    private static (MotdPanel Panel, RecordingEngine Engine) Gated(params IMotdGate[] gates)
    {
        var engine = Fake();
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        return (new MotdPanel(engine, DeclaredConVars.Holding<HostnameConVar>("x"), convars.Declared<MotdUnloadOnDismissalConVar>(),
                              new LagHooks<IMotdGate>(gates)), engine);
    }

    /// <summary>A gate with a fixed answer that counts who it was asked about.</summary>
    private sealed class Gate(bool shows) : IMotdGate
    {
        public List<int> Asked { get; } = [];

        public bool ShowsMotd(int entIndex)
        {
            Asked.Add(entIndex);
            return shows;
        }
    }

    private static int InfoPanels(RecordingEngine engine) =>
        engine.AllOf("UserMessages.Send").Count(c => (string)c.Args[0]! == "VGUIMenu");

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

    [Fact]
    public void A_gate_that_refuses_keeps_the_motd_from_the_player()
    {
        var (panel, engine) = Gated(new Gate(shows: true), new Gate(shows: false));

        panel.OnClientActive(3);

        Assert.Equal(0, InfoPanels(engine));
        Assert.Equal(0, panel.Sent);
    }

    [Fact]
    public void Gates_that_all_allow_show_the_motd()
    {
        var (panel, engine) = Gated(new Gate(shows: true), new Gate(shows: true));

        panel.OnClientActive(3);

        Assert.Equal(("info", 3), (Sent(engine).Menu.Name, Sent(engine).Slot));
    }

    [Fact]
    public void Every_gate_is_asked_about_each_activation_even_after_a_refusal()
    {
        var refuses = new Gate(shows: false);
        var counts = new Gate(shows: true);
        var (panel, _) = Gated(refuses, counts);

        panel.OnClientActive(3);
        panel.OnClientActive(3);

        Assert.Equal(new[] { 3, 3 }, counts.Asked);
    }
}
