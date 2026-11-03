using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The HUD-art arms of the TF2 player status and ammo row: the screens ask
/// the library for the stock material names (hudplayerclass.res,
/// hudplayerhealth.res, hudammoweapons.res, and CTFHealthPanel's
/// <c>hud/health_color</c> and <c>hud/health_dead</c>,
/// tf_hud_playerstatus.cpp:571-582), the team arms pick their .res images
/// (teambg_2/3), and a library that misses everything draws the fallback
/// plates instead of any texture layer.
/// </summary>
public class TFHudArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_player_status_asks_the_library_for_the_stock_materials()
    {
        var (ctx, lib) = Render("healthy-soldier");
        Assert.Contains("hud/character_red_bg", lib.Asked);        // PlayerStatusClassImageBG teambg_2
        Assert.Contains("hud/class_soldierred", lib.Asked);        // PlayerStatusClassImage (cpp:47-75)
        Assert.Contains("hud/health_bg", lib.Asked);               // PlayerStatusHealthImageBG
        Assert.Contains("hud/health_color", lib.Asked);            // CTFHealthPanel's fg (:571)
        Assert.DoesNotContain("hud/health_over_bg", lib.Asked);    // at max, no bonus is asked for

        // The bonus image only on an overhealed local (:769-799).
        Assert.Contains("hud/health_over_bg", Render("overhealed-heavy").Lib.Asked);

        // Dead: only the dead material (:748-759).
        var (dead, deadLib) = Render("dead-soldier");
        Assert.Contains("hud/health_dead", deadLib.Asked);         // (:578)
        Assert.DoesNotContain("hud/health_color", deadLib.Asked);
        Assert.DoesNotContain("hud/health_bg", deadLib.Asked);
    }

    [Fact]
    public void The_team_arm_picks_the_team_images()
    {
        var (ctx, lib) = Render("healthy-blu-soldier");
        Assert.Contains("hud/character_blue_bg", lib.Asked);       // teambg_3
        Assert.Contains("hud/class_soldierblue", lib.Asked);
        Assert.DoesNotContain("hud/character_red_bg", lib.Asked);
        Assert.DoesNotContain("hud/class_soldierred", lib.Asked);
    }

    [Fact]
    public void The_portrait_asks_stock_s_name_for_every_class_and_team()
    {
        // Stock's per-team image tables (g_szBlueClassImages /
        // g_szRedClassImages, tf_hud_playerstatus.cpp:47-75): all nine
        // classes, both arms — the engineer's materials are class_engiblue
        // and class_engired (tf2_misc_dir.vpk materials/hud/).
        var stock = new (string Class, string Blue, string Red)[]
        {
            ("Scout", "hud/class_scoutblue", "hud/class_scoutred"),
            ("Sniper", "hud/class_sniperblue", "hud/class_sniperred"),
            ("Soldier", "hud/class_soldierblue", "hud/class_soldierred"),
            ("Demoman", "hud/class_demoblue", "hud/class_demored"),
            ("Medic", "hud/class_medicblue", "hud/class_medicred"),
            ("Heavy", "hud/class_heavyblue", "hud/class_heavyred"),
            ("Pyro", "hud/class_pyroblue", "hud/class_pyrored"),
            ("Spy", "hud/class_spyblue", "hud/class_spyred"),
            ("Engineer", "hud/class_engiblue", "hud/class_engired"),
        };

        foreach (var (className, blue, red) in stock)
        {
            Assert.Equal(blue, AskedPortrait(className, 3));
            Assert.Equal(red, AskedPortrait(className, 2));
        }
    }

    /// <summary>The one class-image name the panel asks for a local of the
    /// named class and team.</summary>
    private static string AskedPortrait(string className, int team)
    {
        var lib = new RecordingLibrary();
        var panel = new HealthPanel(
            new FakeLocalPlayer
            {
                ClassName = className, Team = team,
                Health = 125, MaxHealth = 125, MaxBuffedHealth = 125,
            },
            lib);
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => panel.Describe(ref ui));
        return Assert.Single(lib.Asked, n => n.StartsWith("hud/class_"));
    }

    [Fact]
    public void The_cross_crops_from_the_bottom_as_health_drops()
    {
        // CTFHealthPanel::Paint crops the destination slice and the uv in
        // proportion, holding the bottom (:615-628): at 120 of 200 the top
        // edge descends by the missing two-fifths, uv t samples the same
        // slice from below — the art keeps its aspect instead of being
        // stretched into the whole box.
        var (wounded, full) = (CrossQuad(120), CrossQuad(200));
        Assert.True(MathF.Abs(wounded.Rect.H - 51f * 0.6f * StockRes.Scale) < 0.5f,
                    $"the wounded cross is {wounded.Rect.H} tall, not the cropped share");
        Assert.True(MathF.Abs(full.Rect.H - 51f * StockRes.Scale) < 0.5f,
                    $"the whole-health cross is {full.Rect.H} tall, not the whole box");
        Assert.True(MathF.Abs((full.Rect.Y + full.Rect.H) - (wounded.Rect.Y + wounded.Rect.H)) < 0.5f,
                    "the crop must hold the cross's bottom edge");

        // The uv slice: from the missing share of t to the foot of the sheet.
        Assert.True(MathF.Abs(wounded.Uv.X) < 1e-4f && MathF.Abs(wounded.Uv.W - 1f) < 1e-4f, "the slice runs the full width");
        Assert.True(MathF.Abs(wounded.Uv.Y - 0.4f) < 1e-3f, $"the slice starts at {wounded.Uv.Y}, not below the missing share");
        Assert.True(MathF.Abs(wounded.Uv.H - 0.6f) < 1e-3f, $"the slice is {wounded.Uv.H} tall, not the life fraction");
        Assert.True(MathF.Abs(full.Uv.Y) < 1e-4f && MathF.Abs(full.Uv.H - 1f) < 1e-4f, "whole health takes the whole sheet");
    }

    /// <summary>The one textured quad at the cross's key, over a local at
    /// the given health of a 200 max.</summary>
    private static UiTexturedQuad CrossQuad(int health)
    {
        var lib = new RecordingLibrary();
        var panel = new HealthPanel(
            new FakeLocalPlayer { ClassName = "Soldier", Health = health, MaxHealth = 200, MaxBuffedHealth = 300 },
            lib);
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => panel.Describe(ref ui));
        var ops = ctx.List.AsDrawList().Ops.ToArray();
        var quads = ctx.List.AsDrawList().TexturedQuads.ToArray();
        var elements = Elements(ctx);
        var names = elements.Select((e, i) => (i, Name: NameOf(ctx, e))).ToDictionary(p => p.i, p => p.Name);
        int key = names.Single(p => p.Value == "cross").Key;
        return quads[ops.Single(o => o.Element == key && o.Kind == UiPrimitiveKind.TexturedQuad).Index];
    }

    [Fact]
    public void The_ammo_row_asks_the_library_for_the_stock_ammo_images()
    {
        var (_, red) = Render("rocket-launcher-clip-and-reserve");
        Assert.Contains("hud/ammo_red_bg", red.Asked);             // HudWeaponAmmoBG teambg_2
        Assert.DoesNotContain("hud/ammo_blue_bg", red.Asked);

        var (_, blu) = Render("healthy-blu-soldier");
        Assert.Contains("hud/ammo_blue_bg", blu.Asked);            // teambg_3
        Assert.DoesNotContain("hud/ammo_red_bg", blu.Asked);
    }

    [Fact]
    public void The_flag_row_asks_the_library_for_the_stock_flag_images()
    {
        var (_, home) = Render("both-flags-home");
        Assert.Contains("hud/objectives_flagpanel_bg_left", home.Asked);    // LeftSideBG
        Assert.Contains("hud/objectives_flagpanel_bg_right", home.Asked);   // RightSideBG
        Assert.Contains("hud/objectives_flagpanel_bg_outline", home.Asked); // OutlineBG
        Assert.Contains("hud/objectives_flagpanel_briefcase", home.Asked);  // flagstatus.res Briefcase
        Assert.Contains("hud/objectives_flagpanel_ico_flag_home", home.Asked);
        Assert.Contains("hud/objectives_flagpanel_compass_red", home.Asked);

        // The carrying arm swaps the squares for the carried flag's image.
        Assert.Contains("hud/objectives_flagpanel_carried_red",
                        Render("carrying-the-red-flag").Lib.Asked);
    }

    [Fact]
    public void The_flag_compass_is_the_arrow_texture_turned_by_the_bearing()
    {
        // Stock paints one material per arrow panel — the with-arrow compass
        // art, the needle baked into the disc — turned by the bearing about
        // the panel's centre (CTFArrowPanel::Paint, tf_hud_flagstatus.cpp:
        // 212-260, GetAngleRotation :167-207): no separate shape needle, no
        // no-arrow disc (that arm is only the spectated carrier's).
        var (ctx, lib) = Render("both-flags-home");
        Assert.Contains("hud/objectives_flagpanel_compass_red", lib.Asked);
        Assert.Contains("hud/objectives_flagpanel_compass_blue", lib.Asked);
        Assert.DoesNotContain("hud/objectives_flagpanel_compass_red_noarrow", lib.Asked);
        Assert.DoesNotContain("hud/objectives_flagpanel_compass_blue_noarrow", lib.Asked);

        // The samples' bearings (red 135°, blue -60°) ride on the compass
        // leaves as radians about their centres; the disc's own leaves stay
        // unrotated.
        var ops = ctx.List.AsDrawList().Ops.ToArray();
        var quads = ctx.List.AsDrawList().TexturedQuads.ToArray();
        var turns = Elements(ctx).Select((e, i) => (e, i))
            .Where(p => NameOf(ctx, p.e) == "compass")
            .Select(p => quads[ops.Single(o => o.Element == p.i && o.Kind == UiPrimitiveKind.TexturedQuad).Index].Rotation)
            .OrderBy(r => r).ToArray();
        Assert.Equal(new[] { -60f * MathF.PI / 180f, 135f * MathF.PI / 180f }, turns);
    }

    [Fact]
    public void The_spectated_carriers_disc_is_the_no_arrow_art()
    {
        // Stock's observer arm (CTFArrowPanel::OnTick, tf_hud_flagstatus.cpp:
        // 127-159) swaps the material for the no-arrow disc while the local
        // player's in-eye target carries that flag; the other team's keeps
        // the needle, and the paint (the bearing's turn) is unchanged.
        var (_, lib) = Render("in-eye-of-the-red-carrier");
        Assert.Contains("hud/objectives_flagpanel_compass_red_noArrow", lib.Asked);
        Assert.Contains("hud/objectives_flagpanel_compass_blue", lib.Asked);
        Assert.DoesNotContain("hud/objectives_flagpanel_compass_red", lib.Asked);
        Assert.DoesNotContain("hud/objectives_flagpanel_compass_blue_noArrow", lib.Asked);
    }

    [Fact]
    public void The_compass_draws_under_the_state_s_mark()
    {
        // flagstatus.res lists the arrow panel (:3-15) before the briefcase
        // (:16-37) and the status icon (:38-56): the mark reads over the
        // disc, whose painted centre would otherwise cover it.
        var (ctx, _) = Render("both-flags-home");
        var ops = ctx.List.AsDrawList().Ops.ToArray();
        var names = Elements(ctx).Select((e, i) => (i, Name: NameOf(ctx, e)))
                                 .ToDictionary(p => p.i, p => p.Name);
        int At(string name) => ops.Select((o, pos) => (pos, Element: (int)o.Element))
                                  .Where(p => names.TryGetValue(p.Element, out var n) && n == name)
                                  .Select(p => p.pos).Min();
        Assert.True(At("compass") < At("status icon"),
                    $"the compass must draw before the status icon (compass {At("compass")}, status {At("status icon")})");
        Assert.True(At("compass") < At("briefcase"), "the compass must draw before the briefcase");
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback_plates()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "healthy-soldier");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the plates — the Tone.Surface quads at the
        // art's coordinates — stand in for it, and the numbers still draw.
        Assert.True(Has(ctx, "class plate"));
        Assert.True(Has(ctx, "cross plate"));
        Assert.False(Has(ctx, "class bg"));
        Assert.False(Has(ctx, "portrait"));
        Assert.False(Has(ctx, "cross"));
        Assert.True(Has(ctx, "health number"));
    }

    [Fact]
    public void A_dead_local_with_no_art_draws_nothing_over_the_class()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "dead-soldier");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        Assert.True(Has(ctx, "class plate"));                       // the class art still shows (its plate)
        Assert.False(Has(ctx, "dead cross"));
        Assert.False(Has(ctx, "cross plate"));
        Assert.False(Has(ctx, "health number"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var lib = new RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }

    /// <summary>Answers every name with a non-zero handle, recording each
    /// ask (the brief's fake: it records the Find calls).</summary>
    internal sealed class RecordingLibrary : IUiTextureLibrary
    {
        public readonly List<string> Asked = new();
        public int Find(string name) { Asked.Add(name); return 0x51; }
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) { Asked.Add(name); return null; }
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return IconAnswer; }

        /// <summary>What every icon ask answers: none by default; a fact
        /// that draws icon-table art sets one (its table size is the
        /// aspect stock keeps).</summary>
        public UiTextureIcon? IconAnswer { get; init; }
    }
}
