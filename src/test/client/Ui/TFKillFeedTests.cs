using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The death notices (tf_hud_deathnotice.cpp): the top-right line stack in
/// the stock layout (hudlayout.res:498-528), the names in team tones, the
/// inverted art for a line the local player is in and the skull fallback
/// (hud_basedeathnotice.cpp:891-918), the crit glyph behind the icon
/// (:290-294), and a bar nothing answers drawing nothing.
/// </summary>
public class TFKillFeedTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void A_kill_line_draws_the_killer_the_icon_and_the_victim()
    {
        Assert.Equal(2, Count("scout-rockets-a-soldier", "killer"));
        Assert.Equal(2, Count("scout-rockets-a-soldier", "victim"));
        Assert.Equal(2, Count("scout-rockets-a-soldier", "icon box"));
    }

    [Fact]
    public void The_crit_glyph_rides_behind_the_icon()
    {
        Assert.True(HasElement("crit-headshot", "crit"));
        Assert.True(HasElement("crit-headshot", "icon box"));
        Assert.False(HasElement("backstab", "crit"));   // no crit, no glyph
    }

    [Fact]
    public void A_suicide_draws_no_killer_and_still_draws_an_icon()
    {
        Assert.False(HasElement("suicide-falls-to-the-skull", "killer"));
        Assert.True(HasElement("suicide-falls-to-the-skull", "icon box"));
    }

    [Fact]
    public void The_local_players_line_asks_the_inverted_art_first()
    {
        var lib = new TFHudArtTests.RecordingLibrary { IconAnswer = Square };
        Render("the-local-player-kills-a-spy", lib);
        Assert.Contains("dneg_shotgun_primary", lib.Asked);
        Assert.DoesNotContain("d_shotgun_primary", lib.Asked);   // found inverted: standard not asked
    }

    [Fact]
    public void The_plain_line_asks_the_d_images_atlas_names()
    {
        var lib = new TFHudArtTests.RecordingLibrary();
        Render("scout-rockets-a-soldier", lib);
        Assert.Contains("d_tf_projectile_rocket", lib.Asked);
        Assert.Contains("d_scattergun", lib.Asked);
    }

    [Fact]
    public void The_crit_glyph_is_asked_by_its_own_name()
    {
        var lib = new TFHudArtTests.RecordingLibrary { IconAnswer = Square };
        Render("crit-headshot", lib);
        Assert.Contains("d_headshot", lib.Asked);
        Assert.Contains("d_crit", lib.Asked);
    }

    [Fact]
    public void The_suicide_falls_to_the_skull()
    {
        var lib = new TFHudArtTests.RecordingLibrary();
        Render("suicide-falls-to-the-skull", lib);
        Assert.Contains("d_skull_tf", lib.Asked);
        Assert.DoesNotContain("", lib.Asked);   // no weapon named: no empty ask
    }

    [Fact]
    public void Nothing_answered_no_icon_box_draws()
    {
        // GetIcon's last arm (:802-807) — the skull answered, or no icon at
        // all. With no art at all the box is simply absent.
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var feed = new TF2.Client.Ui.FakeKillFeed
        {
            Lines = [new("Soldier", 2, "Scout", 3, "d_missile", "", false, false, false)],
        };
        UiSamples.Render(ctx, new TF2.Client.Ui.TFKillFeed(feed, new TF2.Client.Ui.FakeTextureLibrary()),
                         UiSamples.Screen(1920, 1080), 100.0);
        Assert.DoesNotContain(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == "icon box");
    }

    [Fact]
    public void The_first_bar_sits_where_stock_paints_it()
    {
        // hudlayout.res ypos 18 + YRES(16) start + the y+1 polygon top, the
        // bar 14 tall (LineHeight − YRES(2)) — all x2.25 at 1080p.
        var box = Named("crit-headshot", "icon box");
        Assert.Equal(78.75f, box.Y, 0.5f);
        Assert.Equal(31.5f, box.H, 0.5f);
    }

    [Fact]
    public void The_icon_keeps_its_table_aspect_at_the_bars_height()
    {
        // EffectiveWidth/Height scaled to iLineTall − YRES(2)
        // (hud_basedeathnotice.cpp:179-189): a 96x32 d_ icon is three bars
        // wide.
        var box = Named("crit-headshot", "icon box",
                        new UiTextureIcon(0x51, new UiRect(0, 0, 1f, 1f), 96, 32));
        Assert.Equal(31.5f, box.H, 0.5f);
        Assert.Equal(94.5f, box.W, 0.5f);
    }

    [Fact]
    public void The_d_images_are_icon_table_names_not_materials()
    {
        // The d_ names are mod_textures.txt entries (CHud::GetIcon), not
        // material paths: a library that answers every material and no icon
        // draws no icon box.
        var ctx = Render("scout-rockets-a-soldier", new TFHudArtTests.RecordingLibrary());
        Assert.DoesNotContain(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == "icon box");
    }

    /// <summary>The named element's rect on a single-line sample.</summary>
    private static UiRect Named(string slug, string name, UiTextureIcon? icon = null)
    {
        var ctx = Render(slug, new TFHudArtTests.RecordingLibrary { IconAnswer = icon ?? Square });
        return UiTestKit.Named(ctx, name).Rect;
    }

    /// <summary>A square icon every ask answers, so the icon box layers draw.</summary>
    private static readonly UiTextureIcon Square = new(0x51, new UiRect(0, 0, 1f, 1f), 1, 1);

    private static UiContext Render(string slug, TFHudArtTests.RecordingLibrary lib)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfkillfeed" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return ctx;
    }

    /// <summary>Whether the named part drew on the sample's screen. The
    /// library answers every name, so the icon box layers draw.</summary>
    private static bool HasElement(string slug, string name)
    {
        var ctx = Render(slug, new TFHudArtTests.RecordingLibrary { IconAnswer = Square });
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }

    /// <summary>How many elements carry the name.</summary>
    private static int Count(string slug, string name)
    {
        var ctx = Render(slug, new TFHudArtTests.RecordingLibrary { IconAnswer = Square });
        return UiTestKit.Elements(ctx).Count(e => UiTestKit.NameOf(ctx, e) == name);
    }
}
