using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The spy's disguise menu (CHudMenuSpyDisguise): a class tile sends
/// stock's command naming (tf_hud_menu_spy_disguise.cpp:543-564 — the
/// raw class and the shown team as the client's interception names it,
/// c_tf_player.cpp:9637-9679), the menu arms showing the enemy team and
/// the toggle flips it (:569-582, :607-629), and the menu shows only
/// with the disguise kit equipped.
/// </summary>
public class TFDisguiseMenuTests
{
    private static TFDisguiseMenu Screen(FakeTFDisguiseMenuSource source, FakeTFDisguiseMenuActions? actions = null)
        => new(source, new TFDisguiseTile(new NoArt()), new NoArt(), actions);

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback, not the VPK art (the art arms are TFDisguiseMenuArtTests').</summary>
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

    private static void ClickTile(UiContext ctx, UiScreen screen, string key)
    {
        Click(ctx, Named(ctx, $"TFDisguiseTile[{key}]").Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static int Count(UiContext ctx, string name) => Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == name);

    private static string At(UiContext ctx, string name) => RunText(ctx, Count(ctx, name));

    /// <summary>The nine tiles send stock's own command naming, the raw
    /// class and the shown team as the interception names it — the enemy
    /// team is -1 (iRemapKeyToClass :197-208, SelectDisguise :543-564,
    /// c_tf_player.cpp:9637-9679).</summary>
    [Fact]
    public void Each_tile_sends_its_class_and_the_shown_team()
    {
        var actions = new FakeTFDisguiseMenuActions();
        var screen = Screen(new FakeTFDisguiseMenuSource { Team = 2 }, actions);  // RED spy, enemy shown
        var ctx = Drawn(screen);
        Assert.Empty(actions.Disguises);                    // before: nothing sent

        for (int i = 1; i <= 9; i++)
            ClickTile(ctx, screen, $"class_item_blue_{i}");

        Assert.Equal(Enumerable.Range(1, 9).Select(c => (c, -1)), actions.Disguises);
    }

    /// <summary>The toggle flips the shown team and the send naming with
    /// it (:569-582): after the flip the tiles name the player's own
    /// team, -2 (:607-629 arms the enemy first).</summary>
    [Fact]
    public void The_toggle_flips_the_shown_team_and_the_naming()
    {
        var actions = new FakeTFDisguiseMenuActions();
        var screen = Screen(new FakeTFDisguiseMenuSource { Team = 2 }, actions);
        var ctx = Drawn(screen);
        Assert.True(Count(ctx, "TFDisguiseTile[class_item_red_1]") < 0);   // red is not shown yet

        screen.ToggleTeam();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Assert.True(Count(ctx, "TFDisguiseTile[class_item_red_1]") >= 0);

        ClickTile(ctx, screen, "class_item_red_1");
        Assert.Equal([(1, -2)], actions.Disguises);          // the spy's own team now
    }

    /// <summary>The menu arms showing the enemy team and draws only that
    /// team's tiles (spy_pda_reset's arm, :613-624).</summary>
    [Fact]
    public void The_menu_arms_showing_the_enemy()
    {
        var red = Drawn(Screen(new FakeTFDisguiseMenuSource { Team = 2 }));
        Assert.True(Count(red, "TFDisguiseTile[class_item_blue_1]") >= 0);
        Assert.True(Count(red, "TFDisguiseTile[class_item_red_1]") < 0);

        var blue = Drawn(Screen(new FakeTFDisguiseMenuSource { Team = 3 }));
        Assert.True(Count(blue, "TFDisguiseTile[class_item_red_9]") >= 0);
        Assert.True(Count(blue, "TFDisguiseTile[class_item_blue_9]") < 0);
    }

    /// <summary>Stock's show arm: the disguise kit's activation gates the
    /// menu (the PDA pair, tf_weapon_pda.cpp:157-180).</summary>
    [Fact]
    public void The_menu_shows_only_with_the_kit_equipped()
    {
        Assert.True(Screen(new FakeTFDisguiseMenuSource()).Visible);
        Assert.False(Screen(new FakeTFDisguiseMenuSource { DisguiseKitEquipped = false }).Visible);
    }

    /// <summary>The key chips number the tiles 1..9 (the per-class .res's
    /// NumberLabel, e.g. scout_red.res:75-94).</summary>
    [Fact]
    public void The_key_chips_number_the_tiles()
    {
        var ctx = Drawn(Screen(new FakeTFDisguiseMenuSource { Team = 2 }));
        Assert.Equal("1", At(ctx, "number-1"));
        Assert.Equal("9", At(ctx, "number-9"));
    }

    /// <summary>The toggle and cancel sentences are the source's resolved
    /// labels (stock's dialog variables, FindToggleBinding :453-476).</summary>
    [Fact]
    public void The_toggle_and_cancel_sentences_drew()
    {
        var ctx = Drawn(Screen(new FakeTFDisguiseMenuSource
        {
            Team = 2,
            ToggleLabel = "Hit 'x' to Toggle Team",
            CancelLabel = "Hit 'q' to Cancel",
        }));
        Assert.Equal("Hit 'x' to Toggle Team", At(ctx, "toggle"));
        Assert.Equal("Hit 'q' to Cancel", At(ctx, "cancel"));
    }
}
