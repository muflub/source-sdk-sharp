extern alias client;

using ClientHooks = client::SourceSharp.Abi.IHooks<client::TF2.Client.Ui.ITFMapInfoGate>;

using Xunit;

using Gate = client::TF2.Client.Ui.ITFMapInfoGate;
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Window = client::TF2.Client.Ui.TFTextWindowUi;

namespace TF2.Tests.Client;

/// <summary>
/// TF2's text window's game half (stage 3.2): stock's
/// <c>CTFTextWindow::OnCommand</c> hides the window and raises the map
/// info panel (<c>PANEL_MAPINFO</c>, tf_textwindow.cpp:220-229,
/// tf_shareddefs.h:164) — so the okay goes there, by the panel's name
/// as stock's viewport shows it. A registered map info gate may keep the
/// raise down (stock's custom server page arm, :224-228); TF2 registers none.
/// </summary>
public sealed class TFTextWindowUiTests
{
    [Fact]
    public void The_okay_closes_the_text_window_and_opens_the_map_info_panel()
    {
        var nav = new Recorder();
        var window = new Window(nav, new Gates());
        Assert.Empty(nav.Closed);                             // before the click: nothing moved
        Assert.Empty(nav.Panels);

        window.Okay();                                        // stock's okay: hide, then raise
        Assert.Equal(["tf2textwindow"], nav.Closed);
        Assert.Equal(["mapinfo"], nav.Panels);                // the named ask; the claim routes it
    }

    /// <summary>The ask is stock's <c>ShowPanel(PANEL_TEAM, true)</c> of
    /// mapinfo's CONTINUE: the navigation hears the panel's name, never a
    /// screen's — the claim, not a hard-wired screen, picks the team menu.</summary>
    [Fact]
    public void The_okay_names_the_panel_not_a_screen()
    {
        var nav = new Recorder();
        new Window(nav, new Gates()).Okay();
        Assert.Empty(nav.Opened);
    }

    [Fact]
    public void A_gate_that_refuses_closes_the_text_window_and_raises_nothing()
    {
        var nav = new Recorder();
        var window = new Window(nav, new Gates(new Verdict(true), new Verdict(false)));

        window.Okay();
        Assert.Equal(["tf2textwindow"], nav.Closed);
        Assert.Empty(nav.Panels);
        Assert.Empty(nav.Opened);
    }

    [Fact]
    public void Gates_that_all_allow_raise_the_map_info_panel()
    {
        var nav = new Recorder();
        new Window(nav, new Gates(new Verdict(true))).Okay();
        Assert.Equal(["mapinfo"], nav.Panels);
    }

    /// <summary>TF2's own join stays stock: neither TF2 side holds a map
    /// info gate or a motd gate, so its MOTD shows on every level and its OK
    /// leads on to the map info and the team menu.</summary>
    [Fact]
    public void TF2_keeps_no_gate_on_its_own_motd_or_map_info()
    {
        var clientTypes = typeof(Window).Assembly.GetTypes();
        var serverTypes = typeof(TF2.Server.Rules.TFWelcomeMenuCommands).Assembly.GetTypes();

        // The stimulus: the scans read the assemblies the text window and the welcome menu live in.
        Assert.Contains(typeof(Window), clientTypes);
        Assert.Contains(typeof(TF2.Server.Rules.TFWelcomeMenuCommands), serverTypes);

        Assert.DoesNotContain(clientTypes, t => t is { IsClass: true, IsAbstract: false } && typeof(Gate).IsAssignableFrom(t));
        Assert.DoesNotContain(serverTypes, t => t is { IsClass: true, IsAbstract: false } && typeof(SourceSharp.Server.IMotdGate).IsAssignableFrom(t));
    }

    private sealed class Verdict(bool raises) : Gate
    {
        public bool RaisesMapInfo() => raises;
    }

    private sealed class Gates(params Gate[] all) : ClientHooks
    {
        public IReadOnlyList<Gate> All => all;

        public void Each(string what, Action<Gate> call)
        {
            foreach (Gate gate in all)
                call(gate);
        }
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], Panels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => Panels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }
}
