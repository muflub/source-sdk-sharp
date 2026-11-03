using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The engineer's destroy menu (CHudMenuEngyDestroy): each built slot
/// sends exactly stock's command for its g_kEngyBuildings row
/// (tf_hud_menu_engy_destroy.cpp:35-84, :264) and an unbuilt one sends
/// nothing and names #TF_NotBuilt (OnTick's arms, :288-341), and the
/// menu shows only with the destroy PDA equipped.
/// </summary>
public class TFDestroyMenuTests
{
    private static TFDestroyMenu Screen(FakeTFDestroyMenuSource source, FakeTFDestroyMenuActions? actions = null)
        => new(source, new TFDestroyTile(new NoArt()), new NoArt(), actions);

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback, not the VPK art (the art arms are TFDestroyMenuArtTests').</summary>
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

    private static void ClickSlot(UiContext ctx, UiScreen screen, int slot)
    {
        Click(ctx, Named(ctx, $"TFDestroyTile[active_item_{slot + 1}]").Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static int Count(UiContext ctx, string name) => Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == name);

    private static string At(UiContext ctx, string name) => RunText(ctx, Count(ctx, name));

    /// <summary>The four built slots send stock's own command for their
    /// g_kEngyBuildings rows — <c>destroy 2 0; lastinv</c> and kin
    /// (:35-84, :259-266).</summary>
    [Fact]
    public void Each_built_slot_sends_its_stock_command()
    {
        var actions = new FakeTFDestroyMenuActions();
        var screen = Screen(new FakeTFDestroyMenuSource { BuiltSlots = [0, 1, 2, 3] }, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Destroys);                     // before: nothing sent

        ClickSlot(ctx, screen, 0);
        ClickSlot(ctx, screen, 1);
        ClickSlot(ctx, screen, 2);
        ClickSlot(ctx, screen, 3);
        Assert.Equal([20, 0, 10, 11], actions.Destroys);     // OBJ_* + mode, in slot order
    }

    /// <summary>Stock's send arm only fires with the building standing
    /// (:259-266): an unbuilt slot sends nothing while a built one
    /// beside it still does.</summary>
    [Fact]
    public void An_unbuilt_slot_sends_nothing()
    {
        var actions = new FakeTFDestroyMenuActions();
        var screen = Screen(new FakeTFDestroyMenuSource { BuiltSlots = [0] }, actions);
        var ctx = Drawn(screen);

        ClickSlot(ctx, screen, 0);
        Assert.Equal([20], actions.Destroys);                // the sentry is up

        ClickSlot(ctx, screen, 1);
        Assert.Equal([20], actions.Destroys);                // the dispenser is not
    }

    /// <summary>The state arms follow the source (OnTick's :316-339): a
    /// built slot carries the demolish cross, an unbuilt one the
    /// #TF_NotBuilt reason.</summary>
    [Fact]
    public void The_state_arms_follow_the_source()
    {
        var built = Drawn(Screen(new FakeTFDestroyMenuSource { BuiltSlots = [0] }));
        Assert.True(Count(built, "not-built-1") < 0);        // the sentry is up
        Assert.Equal("#TF2_DestroyMenu_NotBuilt", At(built, "not-built-2"));

        var none = Drawn(Screen(new FakeTFDestroyMenuSource { }));
        Assert.True(Count(none, "not-built-4") >= 0);
    }

    /// <summary>Stock's show arm: the destroy PDA's activation gates the
    /// menu (the same m_bBuilderEquipped pair, tf_weapon_pda.cpp:157-180).</summary>
    [Fact]
    public void The_menu_shows_only_with_the_pda_equipped()
    {
        Assert.True(Screen(new FakeTFDestroyMenuSource()).Visible);
        Assert.False(Screen(new FakeTFDestroyMenuSource { DestroyerEquipped = false }).Visible);
    }

    /// <summary>The key chips number the slots 1..4 (the per-slot .res's
    /// NumberLabel, base_active.res:87-106).</summary>
    [Fact]
    public void The_key_chips_number_the_slots()
    {
        var ctx = Drawn(Screen(new FakeTFDestroyMenuSource { }));
        Assert.Equal("1", At(ctx, "number-1"));
        Assert.Equal("4", At(ctx, "number-4"));
    }

    /// <summary>The Cancel sentence is the source's resolved label
    /// (stock's dialog variable, tf_hud_menu_engy_destroy.cpp:360-367).</summary>
    [Fact]
    public void The_cancel_sentence_drew()
    {
        var ctx = Drawn(Screen(new FakeTFDestroyMenuSource { CancelLabel = "Hit 'q' to Cancel" }));
        Assert.Equal("Hit 'q' to Cancel", At(ctx, "cancel"));
    }
}
