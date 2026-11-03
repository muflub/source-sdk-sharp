using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The scoreboard's hud cover, stock's locked "global" render group
/// (tf_clientscoreboard.cpp:433-441 locked, :469 freed): the TF screens
/// that are stock hud elements stand down while it shows and describe again
/// when it closes (hud.cpp:244-246 registers the group in the base ctor;
/// ShouldDraw's locked arm, hud.cpp:288-303). The spectator bars and the
/// scope overlay are no hud elements, or unregistered in stock
/// (tf_hud_scope.cpp:336), and keep drawing.
/// </summary>
public class TFScoreboardCoverTests
{
    private sealed class Cover(bool locked) : ITFRenderGroups
    {
        public bool GlobalLocked => locked;
    }

    private static UiSampleServices Services(bool locked)
    {
        var s = new UiSampleServices();
        s.Add<ITFRenderGroups>(new Cover(locked));
        return s;
    }

    [Fact]
    public void The_always_on_hud_elements_stand_down_while_the_scoreboard_shows()
    {
        var hud = TFHudSamples.Build(Services(locked: true), new FakeLocalPlayer());
        var feed = TFKillFeedSamples.Build(Services(locked: true));

        Assert.False(hud.Visible);            // the cross, ammo, clock, goal hint: covered
        Assert.False(feed.Visible);           // the kill feed with them
    }

    [Fact]
    public void The_hud_elements_describe_again_once_the_scoreboard_closes()
    {
        var hud = TFHudSamples.Build(Services(locked: false), new FakeLocalPlayer());
        var feed = TFKillFeedSamples.Build(Services(locked: false));

        Assert.True(hud.Visible);
        Assert.True(feed.Visible);
    }

    [Fact]
    public void A_gated_hud_screen_stands_down_under_its_own_gate_too()
    {
        // The build menu is up while the PDA is the active weapon; the cover
        // hides it only in addition (tf_hud_menu_engy_build.cpp:98, a hud
        // element at depth 40).
        var s = Services(locked: true);
        s.Add<ITFBuildMenuSource>(new FakeTFBuildMenuSource());
        s.Add<ITFBuildMenuActions>(new FakeTFBuildMenuActions());
        var buildMenu = s.Create<TFBuildMenu>();
        Assert.False(buildMenu.Visible);      // builder up, scoreboard over it

        var s2 = Services(locked: false);
        s2.Add<ITFBuildMenuSource>(new FakeTFBuildMenuSource());
        s2.Add<ITFBuildMenuActions>(new FakeTFBuildMenuActions());
        Assert.True(s2.Create<TFBuildMenu>().Visible);

        var s3 = Services(locked: true);
        s3.Add<ITFBuildMenuSource>(new FakeTFBuildMenuSource { BuilderEquipped = false });
        s3.Add<ITFBuildMenuActions>(new FakeTFBuildMenuActions());
        Assert.False(s3.Create<TFBuildMenu>().Visible);
    }

    [Fact]
    public void The_spectator_bars_and_the_scope_keep_drawing_under_the_cover()
    {
        var s = Services(locked: true);
        var spec = TFSpectatorGuiSamples.Build(s, observer: new FakeObserver { Mode = SpectatorMode.Chase });
        var scope = TFScopeSamples.Build(Services(locked: true));

        Assert.True(spec.Visible);            // CSpectatorGUI: no hud element
        Assert.True(scope.Visible);           // tf_hud_scope.cpp:336 unregisters the group
    }
}
