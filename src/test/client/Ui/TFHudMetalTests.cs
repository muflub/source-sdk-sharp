using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The engineer's metal account (stock's <c>CHudAccountPanel</c>,
/// tf_hud_account.cpp): a living engineer only (<c>ShouldDraw</c>,
/// :274-294), the account number the pool names, and the stock art the
/// panel asks for (hudaccountpanel.res's teambg area and
/// <c>ico_metal</c>).
/// </summary>
public class TFHudMetalTests
{
    private const string FullAccount = "engineer-with-metal";

    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void A_living_engineer_counts_their_metal()
    {
        var (ctx, all) = Screen(FullAccount);
        int account = Array.FindIndex(all, e => NameOf(ctx, e) == "metal account");
        Assert.True(account >= 0, "no metal account");
        Assert.Equal("200", RunText(ctx, account));
    }

    [Fact]
    public void A_spent_account_counts_zero()
    {
        var (ctx, all) = Screen("engineer-out-of-metal");
        int account = Array.FindIndex(all, e => NameOf(ctx, e) == "metal account");
        Assert.True(account >= 0, "no metal account");
        Assert.Equal("0", RunText(ctx, account));
    }

    [Fact]
    public void A_soldier_or_a_dead_engineer_draws_no_account()
    {
        Assert.DoesNotContain(Names("healthy-soldier"), n => n == "metal account");
        Assert.DoesNotContain(Names("dead-soldier"), n => n == "metal account");
    }

    [Fact]
    public void The_panel_asks_the_stock_art()
    {
        // The team area (AccountBG teambg_2) and the icon key
        // (hudaccountpanel.res MetalIcon), plus the blue arm on BLU.
        var lib = new FakeTextureLibrary("hud/misc_ammo_area_red");
        Frame(Panel(new FakeLocalPlayer { ClassName = "Engineer", Team = 2, Health = 125 }, lib),
              UiTestKit.Screen(1920, 1080));
        Assert.Contains("hud/misc_ammo_area_red", lib.Asked);
        Assert.Contains("ico_metal", lib.Asked);
        Assert.DoesNotContain("hud/misc_ammo_area_blue", lib.Asked);

        var blu = new FakeTextureLibrary("hud/misc_ammo_area_blue");
        Frame(Panel(new FakeLocalPlayer { ClassName = "Engineer", Team = 3, Health = 125 }, blu),
              UiTestKit.Screen(1920, 1080));
        Assert.Contains("hud/misc_ammo_area_blue", blu.Asked);
    }

    [Fact]
    public void A_library_without_the_art_leaves_the_plate()
    {
        var ctx = Frame(Panel(new FakeLocalPlayer { ClassName = "Engineer", Health = 125 }, TFHudTests.NoArt.Library),
                        UiTestKit.Screen(1920, 1080));
        Assert.True(Has(ctx, "metal plate"), "no fallback plate");
    }

    private static MetalPanel Panel(FakeLocalPlayer player, IUiTextureLibrary lib) => new(player, lib);

    private static UiContext Frame(MetalPanel panel, in ScreenInfo screen)
    {
        var ctx = Context();
        UiTestKit.Frame(ctx, screen, (ref UiFrame ui) => panel.Describe(ref ui));
        return ctx;
    }

    private static bool Has(UiContext ctx, string key)
        => Elements(ctx).Any(e => NameOf(ctx, e) == key);

    private static (UiContext, UiElementInfo[]) Screen(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(TFHudTests.AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, Elements(ctx));
    }

    private static string[] Names(UiContext ctx)
        => Elements(ctx).Select(e => NameOf(ctx, e)).ToArray();

    private static string[] Names(string slug)
    {
        var (ctx, _) = Screen(slug);
        return Names(ctx);
    }
}
