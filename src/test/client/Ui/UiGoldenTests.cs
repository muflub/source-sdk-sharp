using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Goldens for the exemplar screens (plan_client_ui §3.2 "Goldens", §5.8.4):
/// the SVG of each at 1920x1080 and 2560x1080 (a readable diff), and a pixel
/// golden of each drawn by SkiaBackend at 1280x720 over a flat colour (what it
/// looks like), compared with a small per-pixel tolerance. A change to either
/// is regenerated on purpose with UI_GOLDEN_WRITE=1 and reviewed.
/// </summary>
public class UiGoldenTests
{
    private static readonly UiFontLibrary Fonts = new();

    public static TheoryData<string, string> Exemplars() => new()
    {
        { "scoreboard", "12-players-two-teams" },
        { "hud", "full-health-smg" },
        { "hud", "low-health-hurt-from-the-left" },
        { "hud", "smg-2-of-3-grenades" },
        { "hud", "ar2-0-of-3-balls" },
        { "hud", "kill-feed-busy" },
        { "hud", "kill-feed-suicide-and-the-world" },
        { "console", "busy-log" },
        { "console", "autocomplete-open" },
        { "serverbrowser", "2-000-servers-by-ping" },
        { "serverbrowser", "lan-empty" },
        // The descent HUD joins the exemplars (docs/plans/descent_ui.md §6
        // step 3): its twin-orbs state is the reference picture of the lane.
        { "descenthud", "healthy-party-of-two-map-revealed" },
        // The TF2 HUD (docs/plans/tf2/stage3_client.md Phase 3.1), one state per
        // panel: the cross with the max beside it and the ammo row, the cross's
        // dying state, and the clock in setup.
        { "tfhud", "rocket-launcher-clip-and-reserve" },
        // The low-ammo warning (tf_hud_ammostatus.cpp:312-325): the red art
        // grown about its centre and pulsing under the panel's own bg.
        { "tfhud", "low-on-rockets" },
        { "tfhud", "critical-soldier" },
        { "tfhud", "round-clock-in-setup" },
        // The objective row: a cap in progress under the clock is the state
        // with the owner colour, the capper count and the swipe all live.
        { "tfhud", "red-caps-the-middle-point" },
        // The per-point countdowns (hud_controlpointicons.cpp:579-641,
        // :1843-1876): the timer art and digits on one point, the last-five
        // unlock countdown on another.
        { "tfhud", "point-timer-counts-down" },
        // The escort row: the cart mid-track with its pushers, the checkpoints
        // and the hill band over the rail.
        { "tfhud", "cart-rolls-down-the-track" },
        // The medic's meter: building with the beam out is the state with the
        // percent, the bar and the patient row all live.
        { "tfhud", "medic-builds-the-ubercharge" },
        // The spy's meters: disguised shows the cloak meter and the status
        // row with the imitated team's tone, both over the world.
        { "tfhud", "spy-disguised-as-a-red-soldier" },
        // The flag row: one briefcase carried, the other ticking back; and
        // both home under a caps target, the state with the playing-to
        // caption (stock's "Playing to: %rounds%", tf_english.txt
        // TF_PlayingTo) live between the capture counts.
        { "tfhud", "blue-flag-dropped" },
        { "tfhud", "both-flags-home" },
        // The charsheet joins as the plan's reference window screen (§6 step
        // 3: "charsheet (the reference screen; goldens first)"); mid-spend is
        // the state with numbers, buttons and the staged column all live.
        { "charsheet", "mid-spend-l18" },
        // The TF2 scoreboard (docs/plans/tf2/): the clock between the two
        // bands is its reference picture — bands, both lists, the spectator
        // line's absence, the shaded bar.
        { "scoreboard", "two-teams-and-the-clock" },
        // The TF2 team menu (stage 3.2): on RED is the state with the counts,
        // the your-team marker and the cancel arm all live.
        { "tf2teammenu", "on-red" },
        { "tf2classmenu", "on-red-as-soldier" },
        // The TF2 loadout (stage 3.2): the scout's slots with the equipped
        // icons, an empty slot's label and the class strip; and the item
        // selection over it — the owned grid, both dotted lines and cancel.
        { "tf2loadout", "scout-loadout" },
        { "tf2loadout", "picking-a-primary" },
        // The TF2 map info menu (stage 3.3): the briefing page is the
        // state with the photo, the briefing and both gates all live.
        { "tf2mapinfo", "the-briefing" },
        // The engineer's build menu: every slot open and affordable, then
        // the sentry already built and the dispenser past the metal count.
        { "tf2buildmenu", "all-available" },
        { "tf2buildmenu", "one-built-one-unaffordable" },
        // The engineer's destroy menu: nothing standing is the state
        // where all four not-built arms show, and the sentry built is
        // the one with the demolish cross over the half-alpha art.
        { "tf2destroymenu", "nothing-built" },
        { "tf2destroymenu", "sentry-built" },
        // The spy's disguise menu: the enemy team's tiles are the
        // default arm, and the toggle's is the other set.
        { "tf2disguisemenu", "showing-blue" },
        { "tf2disguisemenu", "showing-red" },
        // The TF2 kill feed (stage 3.1): the crit glyph behind the per-weapon
        // headshot art is the state with the override, the glyph and both
        // names live; the local line is the cream bar with the inverted art.
        { "tfkillfeed", "crit-headshot" },
        { "tfkillfeed", "the-local-player-kills-a-spy" },
        // The TF2 target ID (stage 3.1): the plain teammate is the state with
        // the health cross live; the spy reveal is the one with a data line.
        { "tftargetid", "aimed-teammate" },
        { "tftargetid", "friendly-spy-disguised-as-enemy" },
        // The TF2 building status (stage 3.1): the engineer's stack is the
        // state with the running sentry's shells and the dispenser's metal
        // live; sapped-and-wounded is the one with the alert tray. The
        // per-object rows: the pair charging shows the entrance's recharge
        // meter against the exit, level-three-sentry the rockets row over
        // the kill counts.
        { "tfbuildingstatus", "engineers-sentry-and-dispenser" },
        { "tfbuildingstatus", "sapped-and-wounded" },
        { "tfbuildingstatus", "teleporter-pair-charging" },
        { "tfbuildingstatus", "level-three-sentry" },
        // The TF2 damage indicator (stage 3.1): one fresh hit from the left,
        // and the spread — three hits around the circle plus the heavy.
        { "tfdamageindicator", "hit-from-the-left" },
        { "tfdamageindicator", "three-hits-and-a-heavy" },
        // The TF2 crosshair (stage 3.1): the drawn state — the hidden one is
        // an empty frame, the sample proves the gate.
        { "tfcrosshair", "drawn" },
        // The TF2 notification banner (stage 2.5): the enemy-has-our-flag
        // arrival on the plate, and the carrier's zone warning — the two
        // arms whose copy differs (the victims' and the actor's own line).
        { "tfnotifications", "the-enemy-has-our-flag-red-local" },
        { "tfnotifications", "touching-the-enemy-zone-red-local" },
    };

    private static UiContext Render(string group, string slug, int w, int h)
    {
        var sample = PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(ThemeOf(sample), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(w, h), sample.Now);
        return ctx;
    }

    /// <summary>
    /// The theme a golden draws under: the theme of the mod the sample's screen
    /// assembly belongs to (tools/ui-preview-assemblies.txt's `=game/&lt;moddir&gt;`),
    /// applied over the core theme exactly as `make ui-preview MOD=tf2` does
    /// (PreviewRun.Theme). A TF2 screen's goldens must show TF2's own RED/BLU —
    /// the core theme pairs TeamA with the other team — so a golden of a
    /// modded screen is recorded and compared under that mod's theme. A
    /// sample of an unlisted (core) assembly keeps the core theme.
    /// </summary>
    internal static UiTheme ThemeOf(UiSample sample)
    {
        var theme = SourceSharpTheme.Create();
        string? modDir = UiAssemblyList.Load(RepoLayout.RepoRoot)
            .FirstOrDefault(a => a.Name == sample.Method.DeclaringType!.Assembly.GetName().Name).ModDir;
        if (modDir is null)
            return theme;
        string path = Path.Combine(RepoLayout.RepoRoot, modDir, "resource", "ui-theme.yaml");
        if (File.Exists(path))
            UiThemeFile.Apply(theme, File.ReadAllText(path));
        return theme;
    }

    [Fact]
    public void The_theme_follows_the_screens_mod()
    {
        // The TF2 screens' samples live in TF2.Client.Ui (=game/mod_tf2): their
        // goldens draw under the tf2 mod's theme, whose TeamA is TF2's RED
        // (game/mod_tf2/resource/ui-theme.yaml, from clientscheme.res's Colors
        // block). A core screen's sample keeps the core theme.
        var tf2 = ThemeOf(PreviewRun.AllSamples().Single(s => s.Group == "tfhud" && s.Slug == "low-on-rockets"));
        Assert.Equal(new UiColor(0xB4, 0x5C, 0x4D, 0xFF), tf2.Color(Tone.TeamA));

        var core = ThemeOf(PreviewRun.AllSamples().Single(s => s.Group == "hud" && s.Slug == "full-health-smg"));
        Assert.Equal(SourceSharpTheme.Create().Color(Tone.TeamA), core.Color(Tone.TeamA));
    }

    [Theory]
    [MemberData(nameof(Exemplars))]
    public void The_svg_is_its_golden(string group, string slug)
    {
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1080) })
        {
            var svg = new SvgBackend(Fonts);
            svg.Draw(Render(group, slug, w, h).List.AsDrawList());
            UiGolden.Text($"{group}-{slug}-{w}x{h}.svg", svg.Svg);
        }
    }

    /// <summary>Pixels within this per-channel difference are the same pixel (antialiasing across Skia builds).</summary>
    private const int Tolerance = 12;

    /// <summary>At most this share of pixels may differ by more than the tolerance.</summary>
    private const double MaxDiffering = 0.002;

    [LinuxOnlyTheory("pixel goldens recorded with the Linux libSkiaSharp; the Windows build rasterises glyph edges differently (0.4-1.9% of pixels past tolerance)")]
    [MemberData(nameof(Exemplars))]
    public void The_pixels_are_their_golden(string group, string slug)
    {
        var ctx = Render(group, slug, 1280, 720);
        using var skia = new SkiaBackend(Fonts);
        skia.Draw(ctx.List.AsDrawList());
        string name = $"{group}-{slug}-1280x720.png";
        string path = UiGolden.PathOf(name);

        if (UiGolden.Writing)
        {
            File.WriteAllBytes(path, skia.Png());
            return;
        }

        Assert.True(File.Exists(path), $"no golden {path}: write it with UI_GOLDEN_WRITE=1 and look at it");
        using var golden = SKBitmap.Decode(path);
        Assert.NotNull(golden);
        var (differing, worst) = Compare(skia.Bitmap, golden);
        double share = differing / (double)(golden.Width * golden.Height);
        Assert.True(share <= MaxDiffering,
            $"{name}: {differing} pixels ({share:P2}) differ by more than {Tolerance} (worst {worst}); regenerate with UI_GOLDEN_WRITE=1 if the change is meant");
    }

    internal static (int Differing, int Worst) Compare(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(b.Width, a.Width);
        Assert.Equal(b.Height, a.Height);
        int differing = 0, worst = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                int d = Math.Max(Math.Max(Math.Abs(p.Red - q.Red), Math.Abs(p.Green - q.Green)), Math.Abs(p.Blue - q.Blue));
                worst = Math.Max(worst, d);
                if (d > Tolerance) differing++;
            }
        }
        return (differing, worst);
    }

    [Fact]
    public void The_pixel_comparison_sees_a_one_tone_change()
    {
        // The stimulus reaches the subject: the accent nudged by 30 in red moves
        // more than the tolerance allows on the scoreboard's local row and bars.
        var theme = SourceSharpTheme.Create();
        var a = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
        var sample = PreviewRun.AllSamples().Single(s => s.Group == "hud" && s.Slug == "full-health-smg");
        UiSamples.Render(a, sample.Create(), UiSamples.Screen(1280, 720), sample.Now);
        using var skiaA = new SkiaBackend(Fonts);
        skiaA.Draw(a.List.AsDrawList());

        theme.Set(Tone.Accent, theme.Color(Tone.Accent) with { R = 225 });
        var b = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
        UiSamples.Render(b, sample.Create(), UiSamples.Screen(1280, 720), sample.Now);
        using var skiaB = new SkiaBackend(Fonts);
        skiaB.Draw(b.List.AsDrawList());

        var (differing, _) = Compare(skiaA.Bitmap, skiaB.Bitmap);
        Assert.True(differing / (double)(1280 * 720) > MaxDiffering, $"only {differing} pixels moved");
    }
}
