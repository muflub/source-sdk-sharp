using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The respawn countdown's arms (tf_spectatorgui.cpp:343-447): the
/// stalemate and next-round round states name themselves, a wave counts
/// down through the #TF2_Respawn_InSecs token's %s1, a due wave says
/// "Prepare to respawn", a single second takes its own sentence — and
/// the label is the dying player's only (:343-360).
/// </summary>
public class TFRespawnTimerArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void A_wave_counts_down_through_the_token()
    {
        var (ctx, _, _) = Render("respawn-in-thirty-seconds");
        Assert.True(Has(ctx, "respawn"));
        Assert.Equal("Respawn in: 30 seconds", RunText(ctx, Index(ctx, "respawn")));
    }

    [Fact]
    public void A_single_second_takes_its_own_sentence()
    {
        var (ctx, _, _) = Render("one-second-to-respawn");
        Assert.Equal("Respawn in: 1 second", RunText(ctx, Index(ctx, "respawn")));
    }

    [Fact]
    public void A_wave_due_now_says_prepare()
    {
        var (ctx, _, _) = Render("prepare-to-respawn");
        Assert.Equal("Prepare to respawn", RunText(ctx, Index(ctx, "respawn")));
    }

    [Fact]
    public void A_stalemate_names_sudden_death()
    {
        var (ctx, _, _) = Render("no-respawning-in-sudden-death");
        Assert.Equal("No respawning in Sudden Death", RunText(ctx, Index(ctx, "respawn")));
    }

    [Fact]
    public void A_team_win_waits_for_the_next_round()
    {
        var (ctx, _, _) = Render("wait-for-the-next-round");
        Assert.Equal("Respawn in: Wait for next round", RunText(ctx, Index(ctx, "respawn")));
    }

    /// <summary>The dying player's gate (tf_spectatorgui.cpp:343-360):
    /// alive on a team, the label refuses — even with a wave counting.</summary>
    [Fact]
    public void Alive_the_label_refuses()
    {
        var (_, screen, _) = Render("hidden-while-alive");
        Assert.False(screen.Visible);
    }

    /// <summary>The ReinforcementsLabel's .res geometry (spectator.res:
    /// 69-97): centred on the screen, 50 units from its top, 600 units
    /// wide — the "c-300" xpos puts the box's centre on the screen's.</summary>
    [Fact]
    public void The_label_sits_centred_50_units_from_the_top()
    {
        var (ctx, _, _) = Render("respawn-in-thirty-seconds");
        var label = Named(ctx, "respawn").Rect;
        Assert.Equal(960f, label.X + label.W / 2, 1f);
        Assert.Equal(50f * StockRes.Scale, label.Y, 1f);
        Assert.Equal(600f * StockRes.Scale, label.W, 1f);
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    /// <summary>The named element's index (RunText's handle).</summary>
    private static int Index(UiContext ctx, string name)
    {
        var all = UiTestKit.Elements(ctx);
        for (int i = 0; i < all.Length; i++)
            if (UiTestKit.NameOf(ctx, all[i]) == name)
                return i;
        throw new InvalidOperationException($"no element named {name}");
    }

    private static (UiContext Ctx, UiScreen Screen, TFClassMenuArtTests.RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2respawntimer" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var screen = sample.Create(lib);
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, screen, lib);
    }
}
