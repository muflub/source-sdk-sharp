using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// TF2's player status corner (the class portrait and the health cross) as an
/// element another HUD may take (<see cref="TFHud.PlayerStatusElement"/>):
/// TF2Descent's life orb stands in its place (plan_descent_logic UI2), and
/// the rest of TF2's HUD stays.
/// </summary>
public class TFHudTakesTests
{
    private sealed class Taker(params string[] takes) : UiScreen
    {
        private readonly string[] _takes = takes;
        public override string Name => "taker";
        public override UiLayerKind Layer => UiLayerKind.Hud;
        public override IReadOnlyList<string> Takes => _takes;
        public override void Describe(ref UiFrame ui) { }
    }

    /// <summary>The names TF2's HUD drew, a soldier with a rocket launcher, beside <paramref name="others"/>.</summary>
    private static HashSet<string> Drawn(params UiScreen[] others) => Drawn("rocket-launcher-clip-and-reserve", others);

    /// <summary>The names TF2's HUD drew in sample <paramref name="slug"/>, beside <paramref name="others"/>.</summary>
    private static HashSet<string> Drawn(string slug, params UiScreen[] others)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiTestKit.Context();
        var layer = new UiLayer([.. others, sample.Create()], ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()));
        layer.Frame(UiLayerKind.Hud, UiTestKit.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Select(e => UiTestKit.NameOf(ctx, e)).ToHashSet();
    }

    [Fact]
    public void A_hud_that_takes_the_player_status_leaves_tf2s_portrait_and_cross_out()
    {
        Assert.Contains("player corner", Drawn());                                  // before: TF2's corner draws
        Assert.DoesNotContain("player corner", Drawn(new Taker(TFHud.PlayerStatusElement)));
    }

    [Fact]
    public void Taking_the_player_status_leaves_tf2s_ammo()
    {
        // The sub-panel's recorded name is its type and key: AmmoPanel[ammo corner].
        Assert.Contains(Drawn(new Taker(TFHud.PlayerStatusElement)), n => n.Contains("[ammo corner]", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_element_taken_leaves_the_player_status()
    {
        Assert.Contains("player corner", Drawn(new Taker("hud.vitals")));
    }

    /// <summary>The team status across the top centre (the match backdrop and the
    /// teams' player chips), as an element too: TF2Descent's XP bar stands there.</summary>
    [Fact]
    public void A_hud_that_takes_the_team_status_leaves_tf2s_top_centre_out()
    {
        Assert.Contains("TeamStatusPanel[team status]", Drawn("round-clock-in-setup"));            // before: drawn
        Assert.DoesNotContain("TeamStatusPanel[team status]", Drawn("round-clock-in-setup", new Taker(TFHud.TeamStatusElement)));
        Assert.Contains(Drawn("round-clock-in-setup", new Taker(TFHud.TeamStatusElement)),
                        n => n.Contains("[clock]", StringComparison.Ordinal));                     // the clock stays
    }
}
