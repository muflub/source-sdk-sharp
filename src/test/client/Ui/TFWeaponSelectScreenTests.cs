using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The weapon selection screen is stock's <c>CHudWeaponSelection</c> draw
/// (tf_hud_weaponselection.cpp): the right-edge column of cards at
/// <c>ComputeSlotLayout</c>'s places (:416-452), the border's own art per
/// card — the selection's team colour, the rest the plain frame
/// (:567-572, :591) — the number top right (:758-793) and the label at the
/// bottom; the selected card's weapon that cannot fire names it in red
/// (:745-752); and a selection-less frame draws nothing (:533, :664). The
/// screen takes the core hud's selection panel (Hud.cs's
/// <c>hud.weaponselection</c> gate), which stands down under it.
/// </summary>
public sealed class TFWeaponSelectScreenTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Painted(FakeWeaponSelect? select = null, IUiTextureLibrary? borders = null,
                                     UiTheme? theme = null)
    {
        var ctx = UiSamples.PreviewContext(theme ?? SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, TFWeaponSelectSamples.Build(new UiSampleServices { HudArt = borders }, select),
            UiSamples.Screen(1280, 720), 1.0);
        return ctx;
    }

    /// <summary>The theme as the game composes it: the core's, then the TF2
    /// mod's yaml over it — the faces the card's name and number draw in.</summary>
    private static UiTheme TF2Theme()
    {
        var theme = SourceSharpTheme.Create();
        UiThemeFile.Apply(theme, File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "resource", "ui-theme.yaml")));
        return theme;
    }

    private static string[] Names(UiContext ctx) => [.. UiTestKit.Elements(ctx).Select(e => UiTestKit.NameOf(ctx, e))];

    /// <summary>Every glyph run's text, element by element.</summary>
    private static string[] Texts(UiContext ctx)
        => [.. UiTestKit.Elements(ctx).Select((_, i) => UiTestKit.RunText(ctx, i)).Where(t => t.Length > 0)];

    private static readonly Regex TextRunRegex = new(
        "<text [^>]*font-family=\"(?<family>[^\"]+)\" font-size=\"(?<px>[^ \"]+)\"[^>]*>(?<text>[^<]*)</text>",
        RegexOptions.Compiled);

    /// <summary>The svg's text runs: the family and size each was drawn at.</summary>
    private static List<(string Text, string Family, float Px)> TextRuns(string svg)
    {
        List<(string, string, float)> runs = [];
        foreach (Match m in TextRunRegex.Matches(svg))
            runs.Add((m.Groups["text"].Value, m.Groups["family"].Value,
                float.Parse(m.Groups["px"].Value, CultureInfo.InvariantCulture)));
        return runs;
    }

    [Fact]
    public void A_closed_or_selectionless_frame_draws_nothing()
    {
        // The screen node itself is the only element left.
        Assert.Equal(["tfweaponselect"], Names(Painted(new FakeWeaponSelect { Showing = false })));
        Assert.Equal(["tfweaponselect"], Names(Painted(new FakeWeaponSelect { SelectedSlot = -1 })));
    }

    [Fact]
    public void The_column_is_one_border_art_per_held_slot()
    {
        var borders = new FakeTextureLibrary("hud/color_panel_brown", "hud/color_panel_red", "hud/color_panel_blu");
        var ctx = Painted(Open(), borders);

        // Three held slots, three cards: the plain frame's art, asked once
        // per card — the selected card on RED asks the red arm, never blue.
        Assert.Equal(3, Names(ctx).Count(name => name == "select border"));
        Assert.Contains("hud/color_panel_brown", borders.Asked);
        Assert.Contains("hud/color_panel_red", borders.Asked);
        Assert.DoesNotContain("hud/color_panel_blu", borders.Asked);
    }

    [Fact]
    public void The_column_stands_two_gaps_in_from_the_right_edge()
    {
        // hudlayout.res's RightMargin is 0 (its 32 is the _hidef arm), so
        // ComputeSlotLayout's boxes end two BoxGaps from the edge: 4 du at
        // the sample's 720 lines is 6 pixels.
        var borders = new FakeTextureLibrary("hud/color_panel_brown", "hud/color_panel_red", "hud/color_panel_blu");
        var ctx = Painted(Open(), borders);

        var cards = UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e) == "select border").ToArray();
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.Equal(1280f - 2 * 6f, card.Rect.X + card.Rect.W, 1f));
    }

    [Fact]
    public void The_selected_teams_border_is_its_own_colour()
    {
        var borders = new FakeTextureLibrary("hud/color_panel_brown", "hud/color_panel_red", "hud/color_panel_blu");
        _ = Painted(new FakeWeaponSelect { Team = 3 }, borders);

        Assert.Contains("hud/color_panel_blu", borders.Asked);
        Assert.DoesNotContain("hud/color_panel_red", borders.Asked);
    }

    [Fact]
    public void Every_card_carries_its_slot_number_and_label()
    {
        var texts = Texts(Painted(Open()));

        // The slots' numbers, '0'+i+1: the soldier's 1..3.
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);
        Assert.Contains("3", texts);

        // The labels, the printnames the tokens resolve to, read whole —
        // the name wraps, never elides (the rocket launcher's fits the
        // selected card's large tier on one line).
        Assert.Contains(texts, t => t.StartsWith("ROCKET", StringComparison.Ordinal));
        Assert.Contains("SHOTGUN", texts);
        Assert.Contains("FIRE AXE", texts);
    }

    [Fact]
    public void A_weapon_that_cannot_fire_names_it_in_the_note()
    {
        var select = new FakeWeaponSelect { SelectedSlot = 0 };
        select.Slots[0] = select.Slots[0] with { CanSelect = false };

        Assert.Contains("OUT OF AMMO", Texts(Painted(select)));
    }

    [Fact]
    public void The_screen_takes_the_core_huds_selection()
    {
        var screen = PreviewRun.AllSamples().First(s => s.Group == "tfweaponselect").Create();
        Assert.Equal(["hud.weaponselection"], screen.Takes);
    }

    [Fact]
    public void Card_names_draw_in_the_item_faces_face_not_the_label_face()
    {
        // Stock's card name is the item panel's label (itemmodelpanel.res
        // namelabel), the scheme's ItemFontName — TF2 Build — never the hud
        // label face the screen drew before.
        var runs = TextRuns(UiTestKit.Svg(Painted(Open(), theme: TF2Theme())));
        Assert.All(runs.Where(r => r.Text == "Rocket Launcher"), r => Assert.Equal("TF2 Build", r.Family));
        Assert.All(runs.Where(r => r.Text == "Shotgun"), r => Assert.Equal("TF2 Build", r.Family));

        // Whole, never an ellipsis: the name wraps (centerwrap), stock never
        // trims it to fit.
        Assert.All(runs.Where(r => r.Text.StartsWith("Rocket", StringComparison.Ordinal)),
            r => Assert.Equal("Rocket Launcher", r.Text));
    }

    [Fact]
    public void The_name_shrinks_to_the_largest_tier_that_fits_the_card()
    {
        // Stock's fit (item_model_panel.cpp:2127-2191): the large face while
        // the name is narrower than the card, a smaller one once it is not —
        // the same card, the longer name under the shorter one's tier.
        float Whole() => NamePx(new FakeWeaponSelect(), "Shotgun");
        var select = new FakeWeaponSelect();
        select.Slots[1] = select.Slots[1] with { Name = "Stickybomb Launcher" };
        float shrunk = NamePx(select, "Stickybomb Launcher");
        Assert.True(shrunk < Whole(), $"{shrunk} not under {Whole()}");
    }

    /// <summary>The px the named card name's largest run draws at.</summary>
    private static float NamePx(FakeWeaponSelect select, string name)
    {
        var runs = TextRuns(UiTestKit.Svg(Painted(select, theme: TF2Theme())));
        Assert.Contains(runs, r => r.Text == name);
        return runs.Where(r => r.Text == name).Max(r => r.Px);
    }

    [Fact]
    public void A_name_the_smallest_tier_cannot_hold_wraps_whole()
    {
        const string name = "The Extremely Long Named Prototype Healing Device";
        var select = new FakeWeaponSelect();
        select.Slots[1] = select.Slots[1] with { Name = name };
        var runs = TextRuns(UiTestKit.Svg(Painted(select, theme: TF2Theme())));

        // The wrap's lines, in draw order, spell the whole name — nothing
        // elided, nothing lost.
        var lines = runs.Where(r => r.Text.Length > 1 && name.Contains(r.Text)).ToList();
        Assert.True(lines.Count >= 2, $"one run: {string.Join("|", lines)}");
        Assert.Equal(name, string.Join(" ", lines.Select(r => r.Text.Trim())));
    }

    [Fact]
    public void The_core_hud_leaves_its_selection_to_the_screen_that_takes_it()
    {
        UiScreen core() => HudSamples.Build(new UiSampleServices(), new SourceSharp.Client.Ui.Screens.Samples.FakeLocalPlayer(),
            selection: new FakeWeaponSelection
            {
                Showing = true,
                Weapons = [(1, 0, "shotgun", "#TF_Weapon_Shotgun", true)],
                Selected = 0,
            });

        var alone = UiTestKit.Context(SourceSharpTheme.Create());
        Layer(alone, core()).Frame(UiLayerKind.Hud, UiTestKit.Screen(1920, 1080), 1.0);
        Assert.Contains("WeaponSelectionPanel[weaponselection]", Names(alone));

        var taken = UiTestKit.Context(SourceSharpTheme.Create());
        Layer(taken, core(), new Taker("hud.weaponselection")).Frame(UiLayerKind.Hud, UiTestKit.Screen(1920, 1080), 1.0);
        Assert.DoesNotContain("WeaponSelectionPanel[weaponselection]", Names(taken));
    }

    private static UiLayer Layer(UiContext ctx, params UiScreen[] screens)
        => new(screens, ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()));

    /// <summary>A stand-in screen that takes the named elements, the
    /// UiTakesTests's own.</summary>
    private sealed class Taker(params string[] takes) : UiScreen
    {
        private readonly string[] _takes = takes;

        public override string Name => "taker";

        public override UiLayerKind Layer => UiLayerKind.Hud;

        public override IReadOnlyList<string> Takes => _takes;

        public override void Describe(ref UiFrame ui)
        {
        }
    }

    private static FakeWeaponSelect Open() => new() { SelectedSlot = 1 };
}
