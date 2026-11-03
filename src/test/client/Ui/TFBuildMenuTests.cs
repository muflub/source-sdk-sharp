using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The engineer's build menu (CHudMenuEngyBuild): each slot sends exactly
/// stock's command for its g_kEngyBuildings row (tf_hud_menu_engy_build.cpp:35-84,
/// :474-476) and only from an available one (:472), the state arms follow
/// the source (:561-580), the prices and chips bind to the source, and the
/// menu shows only with the build PDA equipped (tf_weapon_pda.cpp:157-180).
/// </summary>
public class TFBuildMenuTests
{
    private static TFBuildMenu Screen(FakeTFBuildMenuSource source, FakeTFBuildMenuActions? actions = null)
        => new(source, new TFBuildTile(new NoArt()), new NoArt(), actions);

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback, not the VPK art (the art arms are TFBuildMenuArtTests').</summary>
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
        Click(ctx, Named(ctx, $"TFBuildTile[active_item_{slot + 1}]").Rect);
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static int Count(UiContext ctx, string name) => Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == name);

    private static string At(UiContext ctx, string name) => RunText(ctx, Count(ctx, name));

    /// <summary>The four slots send stock's own command for their
    /// g_kEngyBuildings rows: sentry 2 0, dispenser 0 0, tele 1 0 and
    /// 1 1 (:35-84, :474-476).</summary>
    [Fact]
    public void Each_slot_sends_its_stock_command()
    {
        var actions = new FakeTFBuildMenuActions();
        var screen = Screen(new FakeTFBuildMenuSource { Metal = 400 }, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Builds);                       // before: nothing sent

        ClickSlot(ctx, screen, 0);
        ClickSlot(ctx, screen, 1);
        ClickSlot(ctx, screen, 2);
        ClickSlot(ctx, screen, 3);
        Assert.Equal([20, 0, 10, 11], actions.Builds);       // OBJ_* + mode*1, in slot order
    }

    /// <summary>Stock's send arm refuses a built slot and plays the deny
    /// sound instead (:472): the built slot sends nothing while an
    /// available one beside it still does.</summary>
    [Fact]
    public void A_built_slot_sends_nothing()
    {
        var actions = new FakeTFBuildMenuActions();
        var screen = Screen(new FakeTFBuildMenuSource { Metal = 400, BuiltSlots = [0] }, actions);
        var ctx = Drawn(screen);

        ClickSlot(ctx, screen, 0);
        Assert.Empty(actions.Builds);                       // the sentry is up

        ClickSlot(ctx, screen, 1);
        Assert.Equal([0], actions.Builds);                   // the dispenser beside it still sends
    }

    /// <summary>The unaffordable arm refuses too (:472): with 60 metal the
    /// sentry's 125 sends nothing and the teleporter's 50 sends.</summary>
    [Fact]
    public void An_unaffordable_slot_sends_nothing()
    {
        var actions = new FakeTFBuildMenuActions();
        var screen = Screen(new FakeTFBuildMenuSource { Metal = 60 }, actions);
        var ctx = Drawn(screen);

        ClickSlot(ctx, screen, 0);
        Assert.Empty(actions.Builds);

        ClickSlot(ctx, screen, 3);
        Assert.Equal([11], actions.Builds);
    }

    /// <summary>The state arms follow the source (OnTick's :561-580): a
    /// built slot names the already-built reason, a short account the
    /// cant-afford one, an available slot neither.</summary>
    [Fact]
    public void The_state_arms_follow_the_source()
    {
        var available = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400 }));
        Assert.True(Count(available, "reason-1") < 0);       // before: no reason anywhere

        var built = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400, BuiltSlots = [0] }));
        Assert.Equal("#TF2_BuildMenu_AlreadyBuilt", At(built, "reason-1"));

        var short_ = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 60 }));
        Assert.Equal("#TF2_BuildMenu_CantAfford", At(short_, "reason-1"));
    }

    /// <summary>Stock's show arm: the PDA's OnDataChanged gates the menu
    /// (tf_weapon_pda.cpp:157-180) — the Visible the HUD layer asks.</summary>
    [Fact]
    public void The_menu_shows_only_with_the_pda_equipped()
    {
        Assert.True(Screen(new FakeTFBuildMenuSource()).Visible);
        Assert.False(Screen(new FakeTFBuildMenuSource { BuilderEquipped = false }).Visible);
    }

    /// <summary>The slot prices bind to the source's Cost (OnTick's
    /// :558 CalculateObjectCost read).</summary>
    [Fact]
    public void The_slots_bind_their_prices_to_the_source()
    {
        var ctx = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400 }));
        Assert.Equal("125", At(ctx, "cost-1"));
        Assert.Equal("100", At(ctx, "cost-2"));
        Assert.Equal("50", At(ctx, "cost-3"));

        var dear = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400, Prices = [200, 100, 50, 50] }));
        Assert.Equal("200", At(dear, "cost-1"));
    }

    /// <summary>The key chips number the slots 1..4 (the per-slot .res's
    /// NumberLabel).</summary>
    [Fact]
    public void The_key_chips_number_the_slots()
    {
        var ctx = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400 }));
        Assert.Equal("1", At(ctx, "number-1"));
        Assert.Equal("4", At(ctx, "number-4"));
    }

    /// <summary>The Cancel sentence is the source's resolved label
    /// (stock's dialog variable, :612-619).</summary>
    [Fact]
    public void The_cancel_sentence_drew()
    {
        var ctx = Drawn(Screen(new FakeTFBuildMenuSource { CancelLabel = "Hit 'q' to Cancel" }));
        Assert.Equal("Hit 'q' to Cancel", At(ctx, "cancel"));
    }
}
