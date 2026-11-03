using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// A screen that replaces another's element (<see cref="UiScreen.Takes"/>): a mod
/// HUD takes <c>hud.vitals</c> and <c>hud.ammo</c>, and the core HUD leaves its
/// own out while that screen is open and visible.
/// </summary>
public class UiTakesTests
{
    private sealed class Taker(params string[] takes) : UiScreen
    {
        private readonly string[] _takes = takes;
        public override string Name => "taker";
        public override UiLayerKind Layer => UiLayerKind.Hud;
        public override IReadOnlyList<string> Takes => _takes;
        public override void Describe(ref UiFrame ui) { }
    }

    private sealed class Asker : UiScreen
    {
        public bool? Saw;
        public override string Name => "asker";
        public override UiLayerKind Layer => UiLayerKind.Hud;
        public override void Describe(ref UiFrame ui) => Saw = ui.Taken("hud.vitals");
    }

    private static UiLayer Layer(UiContext ctx, params UiScreen[] screens)
        => new(screens, ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()));

    [Fact]
    public void Another_screen_sees_what_an_open_screen_takes_until_it_closes()
    {
        var asker = new Asker();
        var layer = Layer(Context(), new Taker("hud.vitals"), asker);

        layer.Frame(UiLayerKind.Hud, Screen(1920, 1080), 1);
        Assert.True(asker.Saw);

        layer.Close("taker");
        layer.Frame(UiLayerKind.Hud, Screen(1920, 1080), 2);
        Assert.False(asker.Saw);
    }

    [Fact]
    public void An_element_nobody_takes_is_not_taken()
    {
        var asker = new Asker();
        var layer = Layer(Context(), new Taker("hud.ammo"), asker);

        layer.Frame(UiLayerKind.Hud, Screen(1920, 1080), 1);
        Assert.False(asker.Saw);
    }

    [Fact]
    public void The_core_hud_leaves_its_vitals_and_ammo_to_a_screen_that_takes_them()
    {
        var sample = PreviewRun.AllSamples().Single(s => s.Group == "hud" && s.Slug == "full-health-smg");

        var alone = Context();
        Layer(alone, sample.Create()).Frame(UiLayerKind.Hud, Screen(1920, 1080), sample.Now);
        var aloneNames = Elements(alone).Select(e => NameOf(alone, e)).ToHashSet();
        Assert.Contains("VitalsPanel[vitals]", aloneNames);
        Assert.Contains("AmmoPanel[ammo]", aloneNames);

        var taken = Context();
        Layer(taken, sample.Create(), new Taker("hud.vitals", "hud.ammo")).Frame(UiLayerKind.Hud, Screen(1920, 1080), sample.Now);
        var takenNames = Elements(taken).Select(e => NameOf(taken, e)).ToHashSet();
        Assert.DoesNotContain("VitalsPanel[vitals]", takenNames);
        Assert.DoesNotContain("AmmoPanel[ammo]", takenNames);
        Assert.Contains("CrosshairPanel[crosshair]", takenNames);
    }

    [Fact]
    public void The_tf2_hud_takes_the_core_vitals_ammo_and_chat()
    {
        var tfhud = PreviewRun.AllSamples().First(s => s.Group == "tfhud").Create();

        Assert.Equal(["hud.vitals", "hud.ammo", "chat"], tfhud.Takes);
    }

    [Fact]
    public void The_core_chat_stands_down_under_the_screen_that_takes_it()
    {
        var chat = new SourceSharp.Client.Ui.Screens.Chat(
            new SourceSharp.Client.Ui.Screens.Samples.FakeChat
            {
                Lines = [new SourceSharp.Client.Ui.ChatLine("lodle", 2, "hello from the hud check", false, 1f)],
            });

        var alone = Context();
        Layer(alone, chat).Frame(UiLayerKind.Hud, Screen(1920, 1080), 1.0);
        Assert.Contains("row 0", Elements(alone).Select(e => NameOf(alone, e)));

        var taken = Context();
        Layer(taken, chat, new Taker("chat")).Frame(UiLayerKind.Hud, Screen(1920, 1080), 1.0);
        Assert.DoesNotContain("row 0", Elements(taken).Select(e => NameOf(taken, e)));
    }
}
