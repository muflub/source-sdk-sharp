using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 class menu (stage 3.2): it answers the viewport's "class"
/// panel for TF2, its tiles bind their counts to the source, a click
/// records its choice once for the command sender, and the Cancel,
/// spectator-count, FULL and CURRENT arms follow stock's gates
/// (CTFClassMenu, tf_classmenu.cpp).
/// </summary>
public class TFClassMenuTests
{
    private static TFClassMenu Screen(FakeTFClassMenuSource source, FakeTFClassMenuActions? actions = null,
                                      IUiKeyBindingStore? keys = null)
        => new(source, new TFClassTile(new NoArt()), new NoArt(), actions ?? new FakeTFClassMenuActions(), keys);

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback tiles, not the VPK portraits (the art arms are
    /// TFClassMenuArtTests').</summary>
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

    private static void ClickTile(UiContext ctx, UiScreen screen, string subKey)
    {
        Click(ctx, Named(ctx, $"TFClassTile[{subKey}]").Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    /// <summary>The real screen, on a host (TF2 loaded): the panel's show
    /// opens the claimer, and hiding it closes it again.</summary>
    [Fact]
    public void The_class_panel_opens_the_tf2_screen()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        UiMenus m = host0.Menus;
        UiScreen[] menus = [Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 })];
        var host = UiHostKit.Build(surface, [], menus, m);

        Assert.False(host.Menu.IsOpen("tf2classmenu"));      // before: not open
        Assert.True(host.ViewportPanel("class", true));      // the panel TF2 claims
        Assert.True(host.Menu.IsOpen("tf2classmenu"));       // after: the claimer answers
    }

    [Fact]
    public void A_tile_click_chooses_its_class_once()
    {
        var actions = new FakeTFClassMenuActions();
        var screen = Screen(new FakeTFClassMenuSource { Team = 2 }, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Choices);                       // before: nothing chosen

        ClickTile(ctx, screen, "medic");
        Assert.Equal([5], actions.Choices);
        Assert.Equal(5, screen.Chosen);                      // and once: the hover's frame adds no second
    }

    [Fact]
    public void The_random_button_chooses_random()
    {
        var actions = new FakeTFClassMenuActions();
        var screen = Screen(new FakeTFClassMenuSource { Team = 2 }, actions);
        var ctx = Drawn(screen);

        ClickText(ctx, screen, "#TF2_ClassMenu_Random");
        Assert.Equal(1, actions.Randoms);
        Assert.Equal(12, screen.Chosen);                     // stock's TF_CLASS_RANDOM
        Assert.Empty(actions.Choices);                       // no class was chosen beside it
    }

    [Fact]
    public void The_tiles_bind_their_counts_to_the_source()
    {
        var few = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Players = [2, 1, 0, 1, 2, 3, 1, 0, 2] }));
        Assert.Equal("2", RunText(few, Count(few, "count-1")));
        Assert.Equal("3", RunText(few, Count(few, "count-6")));

        // The source moved; the tile follows (before ≠ after, same elements).
        var many = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Players = [7, 1, 0, 1, 2, 3, 1, 0, 2] }));
        Assert.Equal("7", RunText(many, Count(many, "count-1")));
    }

    /// <summary>Stock draws no counts for a spectator (:1446-1449).</summary>
    [Fact]
    public void A_spectator_sees_no_counts()
    {
        var on = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Players = [2, 1, 3, 1, 1, 2, 1, 0, 2] }));
        Assert.True(Count(on, "count-1") >= 0);              // before: a game team counts

        var off = Drawn(Screen(new FakeTFClassMenuSource { Team = 1, Players = [2, 1, 3, 1, 1, 2, 1, 0, 2] }));
        Assert.True(Count(off, "count-1") < 0);              // after: a spectator's are blank
    }

    /// <summary>Stock blanks a zero count (:1487-1489): an empty class's
    /// tile carries no number.</summary>
    [Fact]
    public void A_zero_count_is_blank()
    {
        var none = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Players = [0, 1, 0, 1, 2, 3, 1, 0, 2] }));
        Assert.True(Count(none, "count-1") < 0);             // before: scout has nobody

        var some = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Players = [2, 1, 0, 1, 2, 3, 1, 0, 2] }));
        Assert.True(Count(some, "count-1") >= 0);            // and the stimulus: one scout, one count
    }

    /// <summary>The tiles label their hotkeys (the buttons' labelText 1..9,
    /// south-west, classselection.res :140, :204, ...): the scout's tile
    /// carries 1 in its foot; the random's label is empty (:51).</summary>
    [Fact]
    public void A_tile_labels_its_hotkey_and_the_random_has_none()
    {
        var ctx = Drawn(Screen(new FakeTFClassMenuSource { Team = 2 }));

        var scout = Named(ctx, "TFClassTile[scout]").Rect;
        Assert.Contains(Elements(ctx), e => NameOf(ctx, e) == "hotkey"
                                        && e.Rect.X >= scout.X && e.Rect.X + e.Rect.W <= scout.X + scout.W
                                        && e.Rect.Y >= scout.Y && e.Rect.Y + e.Rect.H <= scout.Y + scout.H);
        Assert.Equal("1", RunText(ctx, Elements(ctx)
            .ToList()
            .FindIndex(e => NameOf(ctx, e) == "hotkey"
                         && e.Rect.X >= scout.X && e.Rect.X + e.Rect.W <= scout.X + scout.W)));

        var random = Named(ctx, "TFClassTile[random]").Rect;
        Assert.DoesNotContain(Elements(ctx), e => NameOf(ctx, e) == "hotkey"
                                                && e.Rect.X >= random.X && e.Rect.X + e.Rect.W <= random.X + random.W);
    }

    /// <summary>The hotkeys run in screen order, the .res's labelText per
    /// button — the soldier's tile reads 2 and the pyro's 3
    /// (classselection.res :204,:268) though their class numbers are 3
    /// and 7, and the sniper's 8 (:633) though its number is 2.</summary>
    [Fact]
    public void The_hotkeys_follow_the_screen_order()
    {
        var ctx = Drawn(Screen(new FakeTFClassMenuSource { Team = 2 }));

        Assert.Equal("2", HotkeyOn(ctx, "soldier"));
        Assert.Equal("3", HotkeyOn(ctx, "pyro"));
        Assert.Equal("8", HotkeyOn(ctx, "sniper"));
    }

    /// <summary>The hotkey the named tile's foot carries.</summary>
    private static string HotkeyOn(UiContext ctx, string tile)
    {
        var r = Named(ctx, $"TFClassTile[{tile}]").Rect;
        int i = Elements(ctx).ToList().FindIndex(e => NameOf(ctx, e) == "hotkey"
                                                   && e.Rect.X >= r.X && e.Rect.X + e.Rect.W <= r.X + r.W
                                                   && e.Rect.Y >= r.Y && e.Rect.Y + e.Rect.H <= r.Y + r.H);
        Assert.True(i >= 0, $"{tile}: no hotkey");
        return RunText(ctx, i);
    }

    /// <summary>Stock's rounded frame stands behind each class group: the
    /// outline spans its three portraits' union (the frame the stock
    /// screen shows; no .res element draws it).</summary>
    [Fact]
    public void Each_class_group_wears_a_frame_spanning_the_tiles()
    {
        var ctx = Drawn(Screen(new FakeTFClassMenuSource { Team = 2 }));

        var offense = Named(ctx, "offense frame").Rect;
        var scout = Named(ctx, "TFClassTile[scout]").Rect;
        var pyro = Named(ctx, "TFClassTile[pyro]").Rect;
        var medic = Named(ctx, "TFClassTile[medic]").Rect;
        var spy = Named(ctx, "TFClassTile[spy]").Rect;
        var support = Named(ctx, "support frame").Rect;
        Assert.Equal(scout.X, offense.X, 0.5f);              // the union's left ...
        Assert.Equal(pyro.X + pyro.W - offense.X, offense.W, 0.5f); // ... and its right
        Assert.Equal(medic.X, support.X, 0.5f);              // the next group's own union
        Assert.Equal(spy.X + spy.W - support.X, support.W, 0.5f);
        Assert.Equal(offense.W, support.W, 0.5f);            // three tiles apiece
    }

    /// <summary>The bottom band wears stock's button pair: the scheme's
    /// Button.BgColor TanDark face under the Button.TextColor TanLight
    /// label (clientscheme.res :252,:251 — the constants :99,:100 name),
    /// the dark plate and cream text the stock screen shows.</summary>
    [Fact]
    public void The_bottom_buttons_wear_the_scheme_colours()
    {
        var ctx = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 }));

        Assert.Equal(new UiColor(117, 107, 94, 255), FillOf(ctx, "edit loadout"));
        Assert.Equal(new UiColor(235, 226, 202, 255), InkOf(ctx, "#EditLoadout"));
        Assert.Equal(new UiColor(235, 226, 202, 255), InkOf(ctx, "#SS_UI_Cancel"));
        Assert.Equal(new UiColor(117, 107, 94, 255), FillOf(ctx, "cancel"));
    }

    /// <summary>The fill the named box drew.</summary>
    private static UiColor FillOf(UiContext ctx, string name)
    {
        int i = Count(ctx, name);
        Assert.True(i >= 0, $"no element named {name}");
        var list = ctx.List.AsDrawList();
        foreach (var op in list.Ops)
            if (op.Kind == UiPrimitiveKind.Quad && op.Element == i)
                return list.Quads[op.Index].Color;
        throw new InvalidOperationException($"{name}: no quad drew");
    }

    /// <summary>The ink the named text drew.</summary>
    private static UiColor InkOf(UiContext ctx, string token)
    {
        int i = TextIndex(ctx, token);
        Assert.True(i >= 0, $"no text {token}");
        var list = ctx.List.AsDrawList();
        foreach (var op in list.Ops)
            if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == i)
                return list.GlyphRuns[op.Index].Color;
        throw new InvalidOperationException($"{token}: no glyph run drew");
    }

    /// <summary>The boxes resolve on the frame's own aspect
    /// (<see cref="StockRes.Screen"/>), not the 640-wide authoring canvas:
    /// Edit Loadout's r180 xpos is 180 units from the true right edge — at
    /// 1920x1080 its left lands at 1515, where the fixed canvas put 1444
    /// (classselection.res:767, Panel.cpp ComputePos).</summary>
    [Fact]
    public void The_canvas_is_the_frames_aspect()
    {
        var ctx = Drawn(Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 }));
        Assert.Equal(1515f, Named(ctx, "edit loadout").Rect.X, 0.5f);
    }

    /// <summary>Stock's SetCancelButtonVisible shows the Select-a-Class
    /// label exactly while the Cancel is not (tf_classmenu.cpp:1035-1038):
    /// the first pick asks, a chosen class answers.</summary>
    [Fact]
    public void The_select_title_shows_only_while_the_cancel_does_not()
    {
        Assert.True(DrewToken("#TF2_ClassMenu_Select", new FakeTFClassMenuSource { Team = 2 }));
        Assert.False(DrewToken("#TF2_ClassMenu_Select", new FakeTFClassMenuSource { Team = 2, Current = 3 }));
    }

    /// <summary>Stock shows Cancel only once a class is chosen
    /// (:1044-1081): the forced first pick cannot be cancelled, and
    /// cancelling a chosen class records the Cancel without a choice.</summary>
    [Fact]
    public void Cancel_shows_only_with_a_class_chosen_and_cancels_without_choosing()
    {
        Assert.False(DrewToken("#SS_UI_Cancel", new FakeTFClassMenuSource { Team = 2 }));    // forced pick
        Assert.True(DrewToken("#SS_UI_Cancel", new FakeTFClassMenuSource { Team = 2, Current = 3 }));

        var actions = new FakeTFClassMenuActions();
        var screen = Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 }, actions);
        var ctx = Drawn(screen);
        ClickText(ctx, screen, "#SS_UI_Cancel");
        Assert.Equal(1, actions.Cancels);
        Assert.Empty(actions.Choices);
    }

    /// <summary>Edit Loadout and Reset stand where the .res puts them and
    /// record their asks (classselection.res:763-801): stock's
    /// openloadout opens the loadout for the class the menu is on and its
    /// resetclass resets it — and stock's OnTick keeps Reset hidden until
    /// a class is chosen (:1138-1142).</summary>
    [Fact]
    public void Edit_loadout_and_reset_record_their_asks()
    {
        Assert.False(DrewToken("#TF_ClassMenu_Reset", new FakeTFClassMenuSource { Team = 2 }));    // forced pick
        Assert.True(DrewToken("#TF_ClassMenu_Reset", new FakeTFClassMenuSource { Team = 2, Current = 3 }));

        var actions = new FakeTFClassMenuActions();
        var screen = Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 }, actions);
        var ctx = Drawn(screen);
        Assert.Equal(0, actions.EditLoadouts);               // before: nothing asked

        ClickText(ctx, screen, "#EditLoadout");
        Assert.Equal(1, actions.EditLoadouts);               // stock's openloadout
        ClickText(ctx, screen, "#TF_ClassMenu_Reset");
        Assert.Equal(1, actions.Resets);                     // the reset arm
        Assert.Empty(actions.Choices);                      // neither chose a class
    }

    /// <summary>The host rig for the named panels: one class screen on the
    /// menu layer over the host's own sources.</summary>
    private static (UiHost Host, UiFidelityTests.RecordingSurface Surface) NamedPanelHost(FakeTFClassMenuSource source,
                                                                                           FakeTFClassMenuActions? actions = null)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        UiMenus m = host0.Menus;
        UiScreen[] menus = [Screen(source, actions)];
        return (UiHostKit.Build(surface, [], menus, m), surface);
    }

    /// <summary>Every show of a claimed panel is stock's
    /// <c>SetVisible(true)</c> telling the server <c>menuclosed</c>' other
    /// half, <c>menuopen</c> (tf_classmenu.cpp:1180-1182): the host's
    /// viewport show raises the actions' Opened, the hide raises nothing.</summary>
    [Fact]
    public void A_panel_show_tells_the_actions_the_menu_opened()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2 }, actions);
        Assert.Equal(0, actions.Opens);                      // before: never shown

        host.ViewportPanel("class_red", true);
        Assert.Equal(1, actions.Opens);                      // the show's word, once

        host.ViewportPanel("class_red", false);
        Assert.Equal(1, actions.Opens);                      // and the hide raises none
    }

    /// <summary>Stock's class menu has no Escape arm
    /// (<c>OnKeyCodePressed</c>, tf_classmenu.cpp:874-972): Escape belongs
    /// to the game menu and the panel stays up, its close word unsaid —
    /// even on the frame the menu sees the key.</summary>
    [Fact]
    public void Escape_leaves_the_menu_open_and_tells_nothing()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2, Current = 3 }, actions);
        host.ViewportPanel("class_red", true);
        Assert.True(host.Menu.IsOpen("tf2classmenu"));       // before: up

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        host.Paint(1, 1920, 1080);
        Assert.True(host.Menu.IsOpen("tf2classmenu"));       // Esc is no close of the menu's
        Assert.Equal(0, actions.Closes);                     // and no close was told
    }

    /// <summary>The 0 key and the pad's are stock's close keys
    /// (tf_classmenu.cpp:896-908): with a class they ask the Cancel — the
    /// hide whose close tells the server, once, in the ui half's facts —
    /// and ask once, the press spent on its frame.</summary>
    [Fact]
    public void The_zero_key_asks_the_cancel_once()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2, Current = 3 }, actions);
        host.ViewportPanel("class_red", true);
        Assert.Equal(0, actions.Cancels);                    // before: no close asked

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Key0, 0, 0f);
        host.Paint(1, 1920, 1080);
        Assert.Equal(1, actions.Cancels);                    // the top-row 0 closes

        host.Paint(1, 1920, 1080);
        Assert.Equal(1, actions.Cancels);                    // and once: the press is spent

        var pad = new FakeTFClassMenuActions();
        var (padHost, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2, Current = 3 }, pad);
        padHost.ViewportPanel("class_red", true);
        padHost.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.KeyPad0, 0, 0f);
        padHost.Paint(1, 1920, 1080);
        Assert.Equal(1, pad.Cancels);                        // the keypad's 0 with it
    }

    /// <summary>Stock's gate holds the close keys for a player whose class
    /// stands (:903-908, <c>GetClassIndex() != TF_CLASS_UNDEFINED</c>):
    /// a forced first pick's 0 does nothing.</summary>
    [Fact]
    public void The_zero_key_is_nothing_without_a_class()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2 }, actions);
        host.ViewportPanel("class_red", true);

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Key0, 0, 0f);
        host.Paint(1, 1920, 1080);
        Assert.Equal(0, actions.Cancels);                    // no class: no close
        Assert.True(host.Menu.IsOpen("tf2classmenu"));       // and the menu stays
    }

    /// <summary>The <c>changeclass</c> bind is the third close key
    /// (stock's <c>m_iClassMenuKey</c>, tf_classmenu.cpp:669, :896): the
    /// key it names closes as the 0 does.</summary>
    [Fact]
    public void The_changeclass_bind_closes_too()
    {
        var actions = new FakeTFClassMenuActions();
        var screen = Screen(new FakeTFClassMenuSource { Team = 2, Current = 3 }, actions, new BoundKeys());
        var ctx = Drawn(screen);
        Assert.Equal(0, actions.Cancels);                    // before: no close asked

        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, KeyC, '\0', 0));
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Assert.Equal(1, actions.Cancels);                    // the bound key with the zeros
    }

    /// <summary>The C key's code (ButtonCode_t: the letters follow the
    /// digits) the bound-keys fake answers.</summary>
    private const int KeyC = 13;

    /// <summary>The bind store with one binding: C is changeclass, as
    /// stock's <c>GetButtonCodeForBind("changeclass")</c> answers.</summary>
    private sealed class BoundKeys : IUiKeyBindingStore
    {
        public string? KeyFor(string command) => command == "changeclass" ? "C" : null;

        public string? KeyName(int buttonCode) => buttonCode == KeyC ? "C" : null;

        public void Bind(string key, string command) { }

        public void Unbind(string key) { }
    }

    /// <summary>The named close — cs_ui_close's route, the viewport's
    /// hide, the reconnect sweep — tells the actions too, once per close
    /// by the host's own guard.</summary>
    [Fact]
    public void A_named_close_tells_the_actions_once()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2 }, actions);
        host.ViewportPanel("class_red", true);

        host.Close("tf2classmenu");
        Assert.Equal(1, actions.Closes);

        host.Close("tf2classmenu");                          // already closed: no second word
        Assert.Equal(1, actions.Closes);
    }

    /// <summary>A close that never went through the host — the layer's
    /// own, as a window's close button asks — tells the screen the same
    /// way: the layer is the one close chokepoint.</summary>
    [Fact]
    public void A_layer_close_tells_the_actions()
    {
        var actions = new FakeTFClassMenuActions();
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2 }, actions);
        host.ViewportPanel("class_red", true);

        host.Menu.Close("tf2classmenu");
        Assert.Equal(1, actions.Closes);
    }

    /// <summary>Another screen's close is not ours: the probe opens and
    /// closes beside the menu and the menu's actions hear nothing.</summary>
    [Fact]
    public void A_close_of_another_screen_tells_nothing()
    {
        var actions = new FakeTFClassMenuActions();
        var probe = new ProbeScreen();
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        UiMenus m = host0.Menus;
        var host = UiHostKit.Build(surface, [], [Screen(new FakeTFClassMenuSource { Team = 2 }, actions), probe], m);

        host.ViewportPanel("class_red", true);
        host.Open("probe");
        Assert.Equal(0, actions.Closes);                     // neither closed yet

        host.Close("probe");
        Assert.Equal(1, probe.Closes);                       // the probe's own word
        Assert.Equal(0, actions.Closes);                     // and none of the menu's
        Assert.True(host.Menu.IsOpen("tf2classmenu"));       // the menu stays up
    }

    /// <summary>A second screen on the layer, for the close facts.</summary>
    private sealed class ProbeScreen : UiScreen
    {
        public override string Name => "probe";

        public int Closes { get; private set; }

        public override void Closed() => Closes++;

        public override void Describe(ref UiFrame ui) { }
    }

    /// <summary>The colours the menu layer's last frame drew.</summary>
    private static HashSet<UiColor> Wore(UiHost host)
    {
        var colors = new HashSet<UiColor>();
        for (int i = 0; i < host.MenuLedger.Count; i++)
            colors.Add(host.MenuLedger[i].Color);
        return colors;
    }

    /// <summary>Stock's jointeam raises the class panel by name — class_red
    /// and class_blue (tf_player.cpp:6450-6455, tf_shareddefs.h:162-163) —
    /// so the one class menu answers every name it was raised under.</summary>
    [Fact]
    public void The_named_class_panels_open_the_tf2_screen()
    {
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2 });
        Assert.True(host.Answers("class_red"));                    // before: only plain "class" answered
        Assert.True(host.Answers("class_blue"));
        Assert.True(host.ViewportPanel("class_blue", true));       // and its show opens the claimer
        Assert.True(host.Menu.IsOpen("tf2classmenu"));
    }

    /// <summary>The named show renders the menu in the team the name names:
    /// stock colours and counts the tiles by the menu's iTeam, which the
    /// panel takes from the raised name (<c>UpdateNumClassLabels</c>,
    /// tf_classmenu.cpp:1452, :1510). The plain show and the name of the
    /// local team's side are one menu; the other name is another.</summary>
    [Fact]
    public void The_tiles_wear_the_team_the_panel_came_under()
    {
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 2, Players = [2, 1, 3, 1, 1, 2, 1, 0, 2] });

        host.ViewportPanel("class", true);                         // the plain claim: the local team's
        host.Paint(1, 1920, 1080);
        var plain = Wore(host);
        host.ViewportPanel("class", false);
        host.ViewportPanel("class_red", true);                     // the local team's own name
        host.Paint(1, 1920, 1080);
        var red = Wore(host);
        host.ViewportPanel("class_red", false);
        host.ViewportPanel("class_blue", true);                    // and the other side's
        host.Paint(1, 1920, 1080);
        var blue = Wore(host);

        Assert.True(plain.SetEquals(red));                         // the control: the name and the local team agree
        Assert.False(plain.SetEquals(blue));                       // before: both names followed the source
    }

    /// <summary>Stock's blank-count arm keys on the local player, not the
    /// panel's team (:1421, :1446-1449): a spectator raised as class_blue
    /// wears blue and still counts nobody.</summary>
    [Fact]
    public void A_spectator_raised_as_a_named_panel_wears_it_and_shows_no_counts()
    {
        var (host, _) = NamedPanelHost(new FakeTFClassMenuSource { Team = 1, Players = [2, 1, 3, 1, 1, 2, 1, 0, 2] });
        host.Menu.Context.RecordNames = true;

        host.ViewportPanel("class_blue", true);
        host.Paint(1, 1920, 1080);
        Assert.True(host.Menu.IsOpen("tf2classmenu"));             // before: nobody answered class_blue
        Assert.True(Count(host.Menu.Context, "count-1") < 0);      // and the spectator's counts stay blank
        var blue = Wore(host);

        host.ViewportPanel("class_blue", false);
        host.ViewportPanel("class_red", true);
        host.Paint(1, 1920, 1080);
        Assert.False(blue.SetEquals(Wore(host)));                  // before: both wore the spectator's neutral
    }

    /// <summary>Stock labels the class you are on CURRENT and a class at
    /// its limit FULL (:1499-1560); the first pick shows neither.</summary>
    [Fact]
    public void The_current_and_full_classes_are_labelled()
    {
        Assert.False(DrewToken("#TF2_ClassMenu_Current", new FakeTFClassMenuSource { Team = 2 }));
        Assert.False(DrewToken("#TF2_ClassMenu_Full", new FakeTFClassMenuSource { Team = 2 }));

        var chosen = new FakeTFClassMenuSource { Team = 2, Current = 3, Full = 5 };
        Assert.True(DrewLabel("#TF2_ClassMenu_Current", chosen));
        Assert.True(DrewLabel("#TF2_ClassMenu_Full", chosen));
    }

    /// <summary>Whether the token's text drew anywhere on a 1920x1080 frame.</summary>
    private static bool DrewToken(string token, FakeTFClassMenuSource source)
        => TextIndex(Drawn(Screen(source)), token) >= 0;

    /// <summary>Whether the Label role's text drew: the kit's Label style
    /// upper-cases, so its run holds the upper-cased token (the NoLocaliser
    /// leaves an unresolved token itself in the run).</summary>
    private static bool DrewLabel(string token, FakeTFClassMenuSource source)
    {
        var ctx = Drawn(Screen(source));
        var upper = token.ToUpperInvariant();
        for (int i = 0; i < Elements(ctx).Length; i++)
            if (RunText(ctx, i) == upper)
                return true;
        return false;
    }
}
