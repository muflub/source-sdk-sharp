extern alias client;

using Xunit;

using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Window = client::TF2.Client.Ui.TFTextWindowUi;

namespace TF2.Tests.Client;

/// <summary>
/// TF2's text window's game half (stage 3.2): stock's
/// <c>CTFTextWindow::OnCommand</c> hides the window and raises the map
/// info panel (<c>PANEL_MAPINFO</c>, tf_textwindow.cpp:220-229,
/// tf_shareddefs.h:164) — so the okay goes there, by the panel's name
/// as stock's viewport shows it.
/// </summary>
public sealed class TFTextWindowUiTests
{
    [Fact]
    public void The_okay_closes_the_text_window_and_opens_the_map_info_panel()
    {
        var nav = new Recorder();
        var window = new Window(nav);
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
        new Window(nav).Okay();
        Assert.Empty(nav.Opened);
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
