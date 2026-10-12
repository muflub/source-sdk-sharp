using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The loading and paused plaques as stock's CLoadingDisc sets them
/// (vgui_loadingdiscpanel.cpp): the loading plaque's #GameUI_LoadingGame, the
/// paused plaque's #GameUI_Paused, or #GameUI_PausedTimer with the seconds left.
/// </summary>
public class LoadingDiscTests
{
    private static LoadingDisc Make(ClShowPausedImageConVar? show = null)
    {
        var engine = Fake();
        var tables = new Dictionary<string, string>
        {
            ["#GameUI_LoadingGame"] = "LOADING",
            ["#GameUI_Paused"] = "PAUSED",
            ["#GameUI_PausedTimer"] = "PAUSED - %s1",
        };
        engine.ReturnsEachCall("Hud.Localise", () => tables.GetValueOrDefault((string)engine.Calls[^1].Arg(0)!, ""));
        if (show is null)
        {
            show = new ClShowPausedImageConVar();
            show.NotifyChanged("1", "0");
        }
        return new LoadingDisc(engine, show);
    }

    private const PlaqueFacts Drawing = PlaqueFacts.DrawingLoadingImage, Demo = PlaqueFacts.PlayingDemo,
                              GameUi = PlaqueFacts.GameUiVisible, Pause = PlaqueFacts.Paused, Shot = PlaqueFacts.TakingScreenshot;

    [Fact]
    public void Loading_shows_while_the_engine_draws_its_loading_image()
    {
        var d = Make();
        d.State(Drawing, -1);
        Assert.Equal((true, false, "LOADING"), (d.Loading, d.Paused, d.LoadingText));
        d.State(PlaqueFacts.None, -1);
        Assert.False(d.Loading);
    }

    [Fact]
    public void A_demo_shows_neither_plaque()
    {
        var d = Make();
        d.State(Drawing | Demo, -1);
        d.State(Pause | Demo, -1);
        Assert.Equal((false, false), (d.Loading, d.Paused));
    }

    [Fact]
    public void A_pause_shows_paused_and_a_timed_one_its_seconds()
    {
        var d = Make();
        d.State(Pause, -1);
        Assert.Equal((true, "PAUSED"), (d.Paused, d.PausedText));
        d.State(Pause, 27);
        Assert.Equal("PAUSED - 27", d.PausedText);
        d.State(PlaqueFacts.None, -1);
        Assert.False(d.Paused);
    }

    [Fact]
    public void The_game_ui_or_a_screenshot_hides_the_paused_plaque()
    {
        var d = Make();
        d.State(Pause | GameUi, -1);
        Assert.False(d.Paused);
        d.State(Pause | Shot, -1);
        Assert.False(d.Paused);
        d.State(Pause, -1);
        Assert.True(d.Paused);
    }

    [Fact]
    public void Cl_showpausedimage_off_shows_no_paused_plaque()
    {
        var show = new ClShowPausedImageConVar();
        show.NotifyChanged("0", "1");
        var d = Make(show);
        d.State(Pause, -1);
        Assert.False(d.Paused);
        show.NotifyChanged("1", "0");
        d.State(Pause, -1);
        Assert.True(d.Paused);
    }

    [Fact]
    public void Nothing_set_shows_nothing()
    {
        var d = Make();
        Assert.Equal((false, false), (d.Loading, d.Paused));
    }
}
