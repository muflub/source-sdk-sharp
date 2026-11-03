using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;          // MotdUi: the host's motd data

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// TF2's text window (stage 3.2): it takes the viewport's "info" panel
/// from the core's motd, draws the server's title and text as the core
/// window does, and records stock's okay click (CTFTextWindow::OnCommand,
/// tf_textwindow.cpp:220-229) through <see cref="ITFTextWindowActions"/>
/// — the team panel opens in the deferred mapinfo's place
/// (docs/plans/tf2/deferred.tsv).
/// </summary>
public sealed class TFTextWindowTests
{
    private static UiContext Drawn(UiScreen screen)
    {
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static int TextIndex(UiContext ctx, string token)
    {
        var els = Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                return i;
        return -1;
    }

    private static void ClickText(UiContext ctx, UiScreen screen, string token)
    {
        int i = TextIndex(ctx, token);
        Assert.True(i >= 0, $"no element drew {token}");
        var r = Elements(ctx)[i].Rect;
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    /// <summary>One host's menu layer: the core's motd beside TF2's window,
    /// both reading the host's own motd data — the layer as TF2's load
    /// places it, where the two could still be told apart.</summary>
    private static (UiHost Host, UiMenus Menus) InfoHost(FakeTFTextWindowActions actions)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        UiMenus m = host0.Menus;
        UiScreen[] menus = [new Motd(m.Motd, m.Motd), new TFTextWindow(m.Motd, actions)];
        return (UiHostKit.Build(surface, [], menus, m), m);
    }

    /// <summary>The server's VGUIMenu "info" raises TF2's window, not the
    /// core's motd, and hiding it closes the claimer again.</summary>
    [Fact]
    public void The_info_panel_opens_the_tf2_text_window_and_not_the_core_s_motd()
    {
        var (host, _) = InfoHost(new FakeTFTextWindowActions());
        Assert.False(host.Menu.IsOpen("tf2textwindow"));      // before: neither is up
        Assert.False(host.Menu.IsOpen("motd"));

        Assert.True(host.ViewportPanel("info", true));         // the claimed name
        Assert.True(host.Menu.IsOpen("tf2textwindow"));        // the claimer answers
        Assert.False(host.Menu.IsOpen("motd"));                // and the core's does not

        host.ViewportPanel("info", false);                     // the hide arm
        Assert.False(host.Menu.IsOpen("tf2textwindow"));
    }

    /// <summary>The VGUIMenu's data is the one source both screens read
    /// (the host's TakePanel arm feeds it): the window draws the title and
    /// the text the server sent, stock's CTFTextWindow reading the same
    /// panel data.</summary>
    [Fact]
    public void The_info_vguimenu_data_reaches_the_text_window()
    {
        var (host, menus) = InfoHost(new FakeTFTextWindowActions());
        Assert.True(host.TakePanel("info", true,
            [new("title", "SERVER MESSAGE"), new("msg", "Welcome to 2fort.\nMind the sewers.")]));
        Assert.True(host.Menu.IsOpen("tf2textwindow"));
        Assert.Equal("SERVER MESSAGE", menus.Motd.Title);     // the data landed on the shared source

        host.Paint(1, 1920, 1080);
        var texts = new List<string>();
        var ctx = host.Menu.Context;
        for (int i = 0; i < Elements(ctx).Length; i++)
            texts.Add(RunText(ctx, i));
        Assert.Contains("SERVER MESSAGE", texts);             // and the window drew it
        Assert.Contains("Welcome to 2fort.", texts);
    }

    /// <summary>Stock's one button command: OK records the okay, once per
    /// click, and nothing else answers it.</summary>
    [Fact]
    public void The_OK_button_records_the_okay_once()
    {
        var actions = new FakeTFTextWindowActions();
        var source = new MotdUi();
        source.SetData([new("title", "SERVER MESSAGE"), new("msg", "hello")]);
        var screen = new TFTextWindow(source, actions);
        var ctx = Drawn(screen);
        Assert.Equal(0, actions.Okays);                       // before: unpressed

        ClickText(ctx, screen, "#SS_UI_Ok");
        Assert.Equal(1, actions.Okays);                       // after: once, the hover frame adding no second
    }
}
