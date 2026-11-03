using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The server browser's controls (plan_gameui_managed.md G3): the tabs, the
/// sortable header, a row, the filters, the buttons, the add-server row and
/// the password dialog each reach the browser's actions; the table orders by
/// the chosen column.
/// </summary>
public class ServerBrowserScreenTests
{
    private sealed class Rig
    {
        public readonly UiSampleServices Services = new();
        public readonly FakeServerBrowser Browser;
        public readonly ServerBrowser Screen;
        public readonly UiContext Ctx = UiTestKit.Context();

        public Rig(FakeServerBrowser browser)
        {
            Browser = browser;
            Screen = (ServerBrowser)ServerBrowserSamples.Build(Services, browser);
            Frame();
        }

        public void Frame() => UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => Screen.Describe(ref ui));

        // Case aside: a label's style upper-cases its run.
        public UiElementInfo Text(string token)
        {
            var els = UiTestKit.Elements(Ctx);
            int i = Array.FindIndex(els, e => string.Equals(UiTestKit.RunText(Ctx, Array.IndexOf(els, e)), token, StringComparison.OrdinalIgnoreCase));
            Assert.True(i >= 0, $"no element drew {token}");
            return els[i];
        }

        public bool Drew(string token)
        {
            var els = UiTestKit.Elements(Ctx);
            return Array.FindIndex(els, e => string.Equals(UiTestKit.RunText(Ctx, Array.IndexOf(els, e)), token, StringComparison.OrdinalIgnoreCase)) >= 0;
        }

        public void Click(UiElementInfo e)
        {
            float x = e.Rect.X + e.Rect.W / 2, y = e.Rect.Y + e.Rect.H / 2;
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Frame();
        }

        public void Click(string token) => Click(Text(token));
    }

    private static FakeServerBrowser Six() => new() { Servers = FakeServerBrowser.Many(6, seed: 4), Selected = 1 };

    [Fact]
    public void A_tab_clicked_selects_it()
    {
        var r = new Rig(Six());
        r.Click("#SS_UI_Browser_Lan");
        Assert.Contains("SelectTab Lan", r.Browser.Actions);
    }

    [Fact]
    public void A_header_clicked_sorts_by_its_column()
    {
        var r = new Rig(Six());
        r.Click("#SS_UI_Browser_Name");
        Assert.Contains("SortBy Name", r.Browser.Actions);
    }

    [Theory]
    [InlineData(ServerSort.Name, false)]
    [InlineData(ServerSort.Players, true)]
    [InlineData(ServerSort.Ping, false)]
    public void The_table_orders_by_the_chosen_column(ServerSort sort, bool descending)
    {
        var browser = Six();
        browser.Sort = sort;
        browser.Descending = descending;
        var r = new Rig(browser);
        var table = r.Services.Create<ServerTable>();
        var shown = Enumerable.Range(0, table.Shown).Select(i => browser.Servers[table.ShownIndex(i)]).ToArray();
        IEnumerable<ServerEntry> expected = sort switch
        {
            ServerSort.Name => shown.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
            ServerSort.Players => shown.OrderByDescending(s => s.Players),
            _ => shown.OrderBy(s => s.Ping),
        };
        Assert.Equal(6, shown.Length);
        Assert.Equal(expected.Select(s => sort == ServerSort.Players ? s.Players.ToString() : sort == ServerSort.Name ? s.Name : s.Ping.ToString()),
                     shown.Select(s => sort == ServerSort.Players ? s.Players.ToString() : sort == ServerSort.Name ? s.Name : s.Ping.ToString()));
    }

    [Fact]
    public void A_row_clicked_selects_its_server()
    {
        var browser = Six();
        var r = new Rig(browser);
        var table = r.Services.Create<ServerTable>();
        int first = table.ShownIndex(0);
        r.Click(browser.Servers[first].Name);
        Assert.Contains($"Select {first}", browser.Actions);
    }

    [Fact]
    public void A_filter_checkbox_clicked_turns_the_filter()
    {
        var r = new Rig(Six());
        r.Click("#SS_UI_Browser_NotFull");
        Assert.Contains("SetNotFull True", r.Browser.Actions);
    }

    [Fact]
    public void The_buttons_refresh_connect_and_add_the_favourite()
    {
        var r = new Rig(Six());
        r.Click("#SS_UI_Browser_Refresh");
        r.Click("#SS_UI_Browser_Connect");
        r.Click("#SS_UI_Browser_AddFavourite");
        Assert.Equal(["Refresh", "Connect", "ToggleFavourite"], r.Browser.Actions);
    }

    [Fact]
    public void On_favourites_the_button_removes_and_add_server_refuses_what_is_not_an_address()
    {
        var r = new Rig(new FakeServerBrowser { Tab = ServerTab.Favourites, Servers = FakeServerBrowser.Many(3), Selected = 0 });
        Assert.True(r.Drew("#SS_UI_Browser_RemoveFavourite"));
        r.Click("#SS_UI_Browser_AddServer");
        Assert.True(r.Screen.AddingServer);
        r.Click("#SS_UI_Browser_AddServer_Add");
        Assert.Contains("AddServer ", r.Browser.Actions);
        Assert.True(r.Drew("#SS_UI_Browser_AddServer_Bad"));
    }

    [Fact]
    public void The_password_dialog_joins_or_cancels()
    {
        var browser = Six();
        browser.AskingPassword = true;
        var r = new Rig(browser);
        Assert.True(r.Drew("#SS_UI_Browser_Password_Title"));
        r.Click(r.Text("#SS_UI_Cancel"));
        Assert.Contains("CancelPassword", browser.Actions);
    }

    [Fact]
    public void Without_steam_the_internet_tab_says_so()
    {
        var r = new Rig(new FakeServerBrowser { SteamAvailable = false });
        Assert.True(r.Drew("#SS_UI_Browser_Empty_NoSteam"));
    }
}
