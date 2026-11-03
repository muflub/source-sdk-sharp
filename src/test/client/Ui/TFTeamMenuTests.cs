using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 team menu (stage 3.2): it answers the viewport's "team" panel
/// for TF2, its cards bind their counts to the source, a click records its
/// choice for the command sender, and the Spectate and Cancel arms follow
/// stock's gates (CTFTeamMenu, tf_teammenu.cpp).
/// </summary>
public class TFTeamMenuTests
{
    private static TFTeamMenu Screen(FakeTFTeamMenuSource source, FakeTFTeamMenuActions? actions = null)
        => new(source, new TFTeamCard(), new NoArt(), actions ?? new FakeTFTeamMenuActions());

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback doors, not the shaded bar (the art arm is
    /// TFTeamMenuArtTests').</summary>
    private sealed class NoArt : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;

        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }

    private static UiContext Drawn(UiScreen screen)
    {
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static void Click(UiContext ctx, UiRect r)
    {
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
    }

    /// <summary>The index of the text element that drew the token (its runs
    /// joined), or -1: the button labels are the click targets to find.</summary>
    private static int TextIndex(UiContext ctx, string token)
    {
        var els = Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                return i;
        return -1;
    }

    private static int Count(UiContext ctx, string name) => Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == name);

    private static void ClickText(UiContext ctx, UiScreen screen, string token)
    {
        int i = TextIndex(ctx, token);
        Assert.True(i >= 0, $"no element drew {token}");
        Click(ctx, Elements(ctx)[i].Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static void ClickCard(UiContext ctx, UiScreen screen, string subKey)
    {
        Click(ctx, Named(ctx, $"TFTeamCard[{subKey}]").Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    /// <summary>The real screen, on a host with the core's team menu beside
    /// it (TF2 loaded): the panel's show opens the claimer and never the
    /// core's.</summary>
    [Fact]
    public void The_team_panel_opens_the_tf2_screen_and_never_the_core_s()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        UiMenus m = host0.Menus;
        UiScreen[] menus =
        [
            new TeamMenu(m.Teams, new TeamCard(), m.Teams),
            Screen(new FakeTFTeamMenuSource { RedPlayers = 2, BluePlayers = 1 }),
        ];
        var host = UiHostKit.Build(surface, [], menus, m);

        Assert.False(host.Menu.IsOpen("teammenu"));        // before: neither screen is open
        Assert.False(host.Menu.IsOpen("tf2teammenu"));
        Assert.True(host.ViewportPanel("team", true));     // the panel TF2 claims
        Assert.True(host.Menu.IsOpen("tf2teammenu"));      // after: the claimer answers
        Assert.False(host.Menu.IsOpen("teammenu"));        // the core's stays shut
    }

    [Fact]
    public void A_card_click_chooses_its_team_once()
    {
        var actions = new FakeTFTeamMenuActions();
        var screen = Screen(new FakeTFTeamMenuSource { RedPlayers = 2, BluePlayers = 1 }, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Choices);                     // before: nothing chosen

        ClickCard(ctx, screen, "red");
        Assert.Equal([TFTeamChoice.Red], actions.Choices);
        Assert.Equal(TFTeamChoice.Red, screen.Chosen);     // and once: the hover's frame adds no second
    }

    /// <summary>Spectate is a labelled button; the auto choice is the
    /// door click itself (stock's teambutton2, jointeam auto).</summary>
    [Fact]
    public void The_spectate_button_and_the_auto_door_choose_theirs()
    {
        var actions = new FakeTFTeamMenuActions();
        var screen = Screen(new FakeTFTeamMenuSource { RedPlayers = 2, BluePlayers = 1 }, actions);
        var ctx = Drawn(screen);

        ClickText(ctx, screen, "#TF2_TeamMenu_Spectate");
        Assert.Equal([TFTeamChoice.Spectate], actions.Choices);

        ClickCard(ctx, screen, "auto");
        Assert.Equal([TFTeamChoice.Spectate, TFTeamChoice.Auto], actions.Choices);
    }

    [Fact]
    public void The_cards_bind_their_counts_to_the_source()
    {
        var few = Drawn(Screen(new FakeTFTeamMenuSource { RedPlayers = 2, BluePlayers = 1 }));
        Assert.Equal("2", RunText(few, Count(few, "count-2")));
        Assert.Equal("1", RunText(few, Count(few, "count-3")));

        // The source moved; the card follows (before ≠ after, same elements).
        var many = Drawn(Screen(new FakeTFTeamMenuSource { RedPlayers = 7, BluePlayers = 4 }));
        Assert.Equal("7", RunText(many, Count(many, "count-2")));
        Assert.Equal("4", RunText(many, Count(many, "count-3")));
    }

    /// <summary>Stock adds the Spectate button only with
    /// mp_allowspectators (tf_teammenu.cpp:127-130).</summary>
    [Fact]
    public void Spectate_shows_only_when_the_server_allows_spectators()
    {
        Assert.True(DrewToken("#TF2_TeamMenu_Spectate", new FakeTFTeamMenuSource()));   // before: allowed
        Assert.False(DrewToken("#TF2_TeamMenu_Spectate",
                               new FakeTFTeamMenuSource { AllowsSpectators = false })); // after: gated off
    }

    /// <summary>Stock shows Cancel only once a team is chosen (:165): the
    /// forced first pick cannot be cancelled, and cancelling a chosen team
    /// records the Cancel without a choice.</summary>
    [Fact]
    public void Cancel_shows_only_with_a_team_chosen_and_cancels_without_choosing()
    {
        Assert.False(DrewToken("#SS_UI_Cancel", new FakeTFTeamMenuSource { CurrentTeam = 0 }));   // forced pick
        Assert.True(DrewToken("#SS_UI_Cancel", new FakeTFTeamMenuSource { CurrentTeam = 2 }));    // on a team

        var actions = new FakeTFTeamMenuActions();
        var screen = Screen(new FakeTFTeamMenuSource { CurrentTeam = 2 }, actions);
        var ctx = Drawn(screen);
        ClickText(ctx, screen, "#SS_UI_Cancel");
        Assert.Equal(1, actions.Cancels);
        Assert.Empty(actions.Choices);
    }

    /// <summary>Whether the token's text drew anywhere on a 1920x1080 frame.</summary>
    private static bool DrewToken(string token, FakeTFTeamMenuSource source)
        => TextIndex(Drawn(Screen(source)), token) >= 0;
}
