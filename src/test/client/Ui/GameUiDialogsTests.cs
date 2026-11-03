using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The GameUI's load dialog, its disconnect form and Quit's question
/// (plan_gameui_managed.md G1), as the stock module's loading dialog and quit
/// confirmation behaved: the dialog only for a load that asked for it, a
/// repaint only when the bar moves a step or the step's text changes, the
/// failure's reason with its extended reason set in, the Steam login failures
/// while the dialog is up, and the engine's gameui_hide when a load ends.
/// </summary>
public class GameUiDialogsTests
{
    private sealed class Strings(Dictionary<string, string> table) : IUiLocalisation
    {
        public string? Find(string token) => table.GetValueOrDefault(token);
    }

    private static (GameUiLoading Loading, List<string> Sent) Loading(Dictionary<string, string>? table = null)
    {
        var sent = new List<string>();
        return (new GameUiLoading(new Strings(table ?? []), new ChatFeedTests.RecordingEngineClient(sent)), sent);
    }

    [Fact]
    public void A_load_that_asks_for_no_dialog_shows_none_and_asks_no_repaint()
    {
        var (loading, _) = Loading();
        loading.Started(false);
        Assert.False(loading.Showing);
        Assert.False(loading.Update(0.5f, "#LoadingProgress_Connecting"));
    }

    [Fact]
    public void A_repaint_is_asked_when_the_bar_moves_a_step_or_the_step_changes()
    {
        var (loading, _) = Loading(new() { ["#LoadingProgress_Connecting"] = "Connecting to server..." });
        loading.Started(true);
        Assert.True(loading.Showing);
        Assert.False(loading.Update(0.01f, null));                          // under a step of 25
        Assert.True(loading.Update(0.05f, null));                           // one step
        Assert.False(loading.Update(0.06f, null));                          // still that step
        Assert.True(loading.Update(0.06f, "#LoadingProgress_Connecting"));  // the step's text
        Assert.Equal("Connecting to server...", loading.Status);
        Assert.False(loading.Update(0.06f, "#loadingprogress_connecting")); // the same text, any case
        Assert.Equal(0.06f, loading.Progress);
    }

    [Fact]
    public void A_finished_load_hides_the_dialog_and_runs_gameui_hide()
    {
        var (loading, sent) = Loading();
        loading.Started(true);
        loading.Finished(false, "", "");
        Assert.False(loading.Showing);
        Assert.False(((IDisconnectMessageSource)loading).Showing);
        Assert.Equal(["gameui_hide"], sent);
    }

    [Fact]
    public void A_failed_load_shows_its_reason_with_the_extended_reason_set_in()
    {
        var (loading, _) = Loading(new() { ["#Fail_Format"] = "Could not load: %s1", ["#Fail_Detail"] = "map missing" });
        loading.Started(true);
        loading.Finished(true, "#Fail_Format", "#Fail_Detail");
        var message = (IDisconnectMessageSource)loading;
        Assert.True(message.Showing);
        Assert.Equal("Could not load: map missing", message.Body);
        message.Close();
        Assert.False(message.Showing);
    }

    [Fact]
    public void A_plain_reason_with_no_extended_reason_is_shown_as_given()
    {
        var (loading, _) = Loading();
        loading.Finished(true, "Server shutting down", null);
        Assert.Equal("Server shutting down", ((IDisconnectMessageSource)loading).Body);
    }

    [Fact]
    public void A_steam_login_failure_shows_only_while_the_dialog_is_up()
    {
        var (loading, _) = Loading(new() { ["#VAC_LoggedInElsewhereReason"] = "Logged in elsewhere." });
        loading.Disconnected(4);
        Assert.False(((IDisconnectMessageSource)loading).Showing);         // no dialog up: nothing, as stock
        loading.Started(true);
        loading.Disconnected(0);
        loading.Disconnected(1);
        Assert.False(((IDisconnectMessageSource)loading).Showing);         // no failure stock displays
        loading.Disconnected(4);
        Assert.Equal("Logged in elsewhere.", ((IDisconnectMessageSource)loading).Body);
        Assert.False(loading.Showing);
    }

    [Fact]
    public void Quit_asked_quits_on_quit_and_withdraws_on_cancel()
    {
        var sent = new List<string>();
        var quit = new ConfirmQuitUi(new ChatFeedTests.RecordingEngineClient(sent));
        quit.Ask();
        Assert.True(quit.Asking);
        quit.Cancel();
        Assert.False(quit.Asking);
        Assert.Empty(sent);
        quit.Ask();
        quit.Quit();
        Assert.False(quit.Asking);
        Assert.Equal(["quit"], sent);
    }

    // ---- the screens ----

    private static void ClickText(UiContext ctx, UiScreen screen, string token)
    {
        var els = Elements(ctx);
        int i = Array.FindIndex(els, e => RunText(ctx, Array.IndexOf(els, e)) == token);
        Assert.True(i >= 0, $"no element drew {token}");
        var r = els[i].Rect;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, r.X + r.W / 2, r.Y + r.H / 2, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, r.X + r.W / 2, r.Y + r.H / 2, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static UiContext Drawn(UiScreen screen)
    {
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    [Fact]
    public void The_disconnect_dialogs_close_closes_it()
    {
        var (loading, _) = Loading();
        loading.Finished(true, "Server shutting down", null);
        var screen = new DisconnectMessage(loading);
        var ctx = Drawn(screen);
        ClickText(ctx, screen, "#SS_UI_Close");
        Assert.False(((IDisconnectMessageSource)loading).Showing);
    }

    [Fact]
    public void The_quit_dialogs_buttons_cancel_and_quit()
    {
        var sent = new List<string>();
        var quit = new ConfirmQuitUi(new ChatFeedTests.RecordingEngineClient(sent));
        var screen = new ConfirmQuit(quit);
        quit.Ask();
        var ctx = Drawn(screen);
        ClickText(ctx, screen, "#SS_UI_Cancel");
        Assert.False(quit.Asking);
        quit.Ask();
        ctx = Drawn(screen);
        ClickText(ctx, screen, "#SS_UI_Quit_Ok");
        Assert.Equal(["quit"], sent);
    }

    [Fact]
    public void The_loading_dialog_draws_only_while_showing()
    {
        var (loading, _) = Loading();
        var screen = new LoadingProgress(loading);
        var idle = Drawn(screen);
        Assert.DoesNotContain(Elements(idle), e => RunText(idle, Array.IndexOf(Elements(idle), e)) == "#SS_UI_Loading");
        loading.Started(true);
        var ctx = Drawn(screen);
        Assert.Contains(Elements(ctx), e => RunText(ctx, Array.IndexOf(Elements(ctx), e)) == "#SS_UI_Loading");
    }
}
