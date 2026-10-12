using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The design lint (plan_client_ui §5.8.5). Every rule has a fact that plants
/// its defect and sees it reported, and a clean twin that is not; and every
/// sample of every screen is lint-clean.
/// </summary>
public class UiLintTests
{
    private static readonly UiFontLibrary Fonts = new();
    private static readonly Lazy<IReadOnlyList<IUiBackdrop>> Backdrops =
        new(() => SkiaBackdrop.Load(RepoLayout.Repo("src", "client", "ui", "backdrops")));

    internal static PreviewOptions Options() => new(RepoLayout.RepoRoot, Path.GetTempPath(), false, true, null);

    /// <summary>Lints one frame of <paramref name="describe"/> at 1920x1080 in the default theme.</summary>
    internal static List<UiLintFinding> Lint(TestScreen.Describer describe, UiLayerKind layer = UiLayerKind.Menu,
                                             int w = 1920, int h = 1080, UiTheme? theme = null, UiInsets safe = default)
    {
        theme ??= SourceSharpTheme.Create();
        var ctx = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
        UiTestKit.Frame(ctx, UiSamples.Screen(w, h) with { Safe = safe }, describe, layer: layer);
        return new UiLint(theme, Fonts, Backdrops.Value).Check(ctx, $"planted@{w}x{h}");
    }

    private static List<UiLintFinding> Of(List<UiLintFinding> all, string rule) => all.Where(f => f.Rule == rule).ToList();

    [Fact]
    public void Every_sample_of_every_screen_is_lint_clean()
    {
        var findings = LintRun.All(Options());
        Assert.True(findings.Count == 0, $"{findings.Count} lint finding(s):\n" + string.Join("\n", findings.Take(30)));
    }

    [Fact]
    public void Clipped_reports_text_that_overflows_or_is_cut_and_not_text_that_fits()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().Width(Size.Du(160)).At(UiAnchor.Centre))
                ui.Data("xXx_Headcrab_Slayer_From_Ravenholm_xXx").Name("long");
            using (ui.Box().At(UiAnchor.TopLeft).Inset(Space.XXL).Size(Size.Du(200), Size.Du(10)).Clip())
                ui.Data("Cut in half").Name("cut");
        });
        var clipped = Of(bad, "clipped");
        Assert.Contains(clipped, f => f.Element.Contains("long") && f.Message.Contains("overflows its box by"));
        Assert.Contains(clipped, f => f.Element.Contains("cut") && f.Message.Contains("enclosing clip"));

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().Width(Size.Du(160)).At(UiAnchor.Centre))
                ui.Data("xXx_Headcrab_Slayer_From_Ravenholm_xXx").Elide();
            using (ui.Box().At(UiAnchor.TopLeft).Inset(Space.XXL).Size(Size.Du(200), Size.Du(40)).Clip())
                ui.Data("Whole");
        });
        Assert.Empty(Of(good, "clipped"));

        // A row half scrolled out of a scroll region is scrolling, not clipping.
        var scrolled = Lint((ref UiFrame ui) =>
        {
            using (ui.Scroll("s").At(UiAnchor.TopLeft).Inset(Space.XXL).Size(Size.Du(300), Size.Du(60)))
                for (int i = 0; i < 4; i++)
                    ui.Data("Row text").Height(Size.Du(40)).Key(i);
        });
        Assert.Empty(Of(scrolled, "clipped"));
    }

    [Fact]
    public void Offscreen_reports_what_leaves_the_screen_and_what_enters_the_safe_area()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            ui.Box().At(UiAnchor.Right, UiAnchor.Left).Size(Size.Du(100), Size.Du(40)).Fill(Tone.Surface).Name("gone").Dispose();
            ui.Rect(Tone.Surface, Size.Du(2500), Size.Du(40)).Name("wide");   // 1024x768 is 1440 du wide
        }, w: 1024, h: 768);
        var off = Of(bad, "offscreen");
        Assert.Contains(off, f => f.Element.EndsWith("gone") && f.Message.Contains("off screen"));
        Assert.Contains(off, f => f.Element.EndsWith("wide"));

        // Pushed 30 du outward past a 40 px safe inset: on screen, but in the inset.
        var safe = Lint((ref UiFrame ui) =>
        {
            ui.Rect(Tone.Surface, Size.Du(100), Size.Du(40)).Inset(Raw.Du(-30), Space.None).Name("edge");
        }, safe: new UiInsets(40, 40, 40, 40));
        Assert.Contains(Of(safe, "offscreen"), f => f.Element.EndsWith("edge") && f.Message.Contains("enters the safe area by 30 du"));

        // Rows scrolled out of view are clipped away, not off screen.
        var scrolled = Lint((ref UiFrame ui) =>
        {
            using (ui.Scroll("s").At(UiAnchor.BottomLeft).Inset(Space.XXL).Size(Size.Du(300), Size.Du(100)))
                for (int i = 0; i < 60; i++)
                    ui.Rect(Tone.Surface, Size.Fill, Size.Du(40)).Key(i);
        });
        Assert.Empty(Of(scrolled, "offscreen"));

        var good = Lint((ref UiFrame ui) =>
        {
            ui.Box().At(UiAnchor.Right).Size(Size.Du(100), Size.Du(40)).Fill(Tone.Surface).Dispose();
            ui.Rect(Tone.Surface, Size.Percent(100), Size.Du(40));
        }, w: 1024, h: 768);
        Assert.Empty(Of(good, "offscreen"));
    }

    [Fact]
    public void Contrast_reports_text_the_world_shows_through_and_not_text_on_a_panel()
    {
        // Muted caption straight over the world: the bright still swallows it.
        var bad = Lint((ref UiFrame ui) =>
        {
            ui.Data("Floating caption", Text.Caption, Tone.TextMuted).At(UiAnchor.Centre).Name("floating");
        }, UiLayerKind.Hud);
        Assert.Contains(Of(bad, "contrast"), f => f.Element.Contains("floating") && f.Message.Contains("over the bright still"));

        // The same on a panel passes.
        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
                ui.Data("Panelled caption", Text.Caption, Tone.TextMuted);
        }, UiLayerKind.Hud);
        Assert.Empty(Of(good, "contrast"));

        // A fade in progress (a chat line going) is judged at full strength.
        var fading = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre).Opacity(0.3f))
                ui.Data("Fading line", Text.Body, Tone.Text);
        }, UiLayerKind.Hud);
        Assert.Empty(Of(fading, "contrast"));
    }

    [Fact]
    public void Contrast_is_the_wcag_ratio()
    {
        var black = new UiColor(0, 0, 0, 255);
        var white = new UiColor(255, 255, 255, 255);
        Assert.Equal(21f, UiContrast.Ratio(black, white), 2);
        Assert.Equal(1f, UiContrast.Ratio(white, white), 3);
        Assert.Equal(4.54f, UiContrast.Ratio(new UiColor(118, 118, 118, 255), white), 1);   // the canonical #767676
        Assert.Equal(new UiColor(128, 128, 128, 255), UiContrast.Over(new UiColor(255, 255, 255, 128), black));
    }

    [Fact]
    public void Alignment_reports_free_placed_edges_that_nearly_meet()
    {
        // Two HUD panels in separate sub-panel layers, bottoms 2 du apart.
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(200), Size.Du(60)).At(UiAnchor.BottomLeft).Inset(Space.XL).Name("health");
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(200), Size.Du(60)).At(UiAnchor.BottomRight).Inset(Space.XL, Raw.Du(26)).Name("ammo");
        }, UiLayerKind.Hud);
        Assert.Contains(Of(bad, "alignment"), f => f.Element.EndsWith("ammo") && f.Message.StartsWith("bottom edge is 2 du from"));

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(200), Size.Du(60)).At(UiAnchor.BottomLeft).Inset(Space.XL);
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(200), Size.Du(60)).At(UiAnchor.BottomRight).Inset(Space.XL);
        }, UiLayerKind.Hud);
        Assert.Empty(Of(good, "alignment"));
    }

    [Fact]
    public void Overlap_reports_siblings_on_top_of_each_other_unless_layered()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(300), Size.Du(80)).At(UiAnchor.BottomLeft).Inset(Space.XL).Name("health");
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(300), Size.Du(80)).At(UiAnchor.BottomLeft).Inset(Raw.Du(200), Space.XL).Name("armour");
        }, UiLayerKind.Hud);
        Assert.Contains(Of(bad, "overlap"), f => f.Element.EndsWith("armour") && f.Message.StartsWith("overlaps") && f.Message.Contains("124 x 80 du"));   // 24..324 against 200..500

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(300), Size.Du(80)).At(UiAnchor.BottomLeft).Inset(Space.XL);
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Rect(Tone.Surface, Size.Du(300), Size.Du(80)).At(UiAnchor.BottomLeft).Inset(Raw.Du(340), Space.XL);
            // A badge meant to sit over a corner says so, whichever is drawn first.
            using (ui.Box().At(UiAnchor.Centre).Size(Size.Du(200), Size.Du(100)))
            {
                ui.Rect(Tone.Surface, Size.Fill, Size.Fill);
                ui.Rect(Tone.Accent, Size.Du(20), Size.Du(20)).At(UiAnchor.TopRight).Layered();
            }
            using (ui.Box().At(UiAnchor.TopLeft).Inset(Raw.Du(300)).Size(Size.Du(200), Size.Du(100)))
            {
                ui.Rect(Tone.Accent, Size.Du(20), Size.Du(20)).At(UiAnchor.TopRight).Layered();
                ui.Rect(Tone.Surface, Size.Fill, Size.Fill);
            }
        }, UiLayerKind.Hud);
        Assert.Empty(Of(good, "overlap"));
    }

    [Fact]
    public void Budget_reports_more_than_four_type_roles_or_six_tones()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
            {
                ui.Data("Title", Text.Title, Tone.Text);
                ui.Data("Heading", Text.Heading, Tone.TextMuted);
                ui.Data("Body", Text.Body, Tone.Accent);
                ui.Data("Label", Text.Label, Tone.TeamA);
                ui.Data("Caption", Text.Caption, Tone.TeamB);
                ui.Rect(Tone.Positive, Size.Du(40), Size.Du(4));
                ui.Rect(Tone.Danger, Size.Du(40), Size.Du(4));
            }
        });
        var budget = Of(bad, "budget");
        Assert.Contains(budget, f => f.Message == "uses 5 type roles (Title, Heading, Body, Label, Caption); keep to 4");
        Assert.Contains(budget, f => f.Message.StartsWith("uses 7 tones"));

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
            {
                ui.Data("Title", Text.Title, Tone.Text);
                ui.Data("Heading", Text.Heading, Tone.TextMuted);
                ui.Data("Body", Text.Body, Tone.Accent);
                ui.Data("Label", Text.Label, Tone.TeamA);
                ui.Rect(Tone.TeamB, Size.Du(40), Size.Du(4));
                ui.Rect(Tone.Danger, Size.Du(40), Size.Du(4));
            }
        });
        Assert.Empty(Of(good, "budget"));
    }

    [Fact]
    public void Target_reports_click_targets_under_32_du()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Row(Space.M).At(UiAnchor.Centre))
            {
                ui.Interact(ui.Rect(Tone.Accent, Size.Du(80), Size.Du(20)).Key("thin").Name("thin"));
                ui.Button("ok", "#SS_UI_Cancel");
            }
        });
        var t = Assert.Single(Of(bad, "target"));
        Assert.EndsWith("thin", t.Element);
        Assert.Contains("80 x 20 du", t.Message);

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Row(Space.M).At(UiAnchor.Centre))
            {
                ui.Interact(ui.Rect(Tone.Accent, Size.Du(80), Size.Du(32)).Key("thick"));
                ui.Rect(Tone.Accent, Size.Du(80), Size.Du(4));   // not clickable: no rule
            }
        });
        Assert.Empty(Of(good, "target"));
    }

    [Fact]
    public void Localise_reports_literal_copy_and_missing_tokens_but_not_data()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
            {
                ui.Text("Choose your side", Text.Title);
                ui.Text("#SS_UI_NoSuchToken");
                // A token stranded inside an interpolation: it renders
                // verbatim and no language file is ever consulted (the
                // descent tooltip's compare row did exactly this).
                string target = "Back Scatter";
                ui.Text($"#Descent_Tip_Compare: {target}");
            }
        });
        var loc = Of(bad, "localise");
        Assert.Contains(loc, f => f.Element.Contains("CHOOSE YOUR SIDE") && f.Message.StartsWith("is a literal string"));   // a Title is upper-cased
        Assert.Contains(loc, f => f.Message.StartsWith("names #SS_UI_NoSuchToken"));
        Assert.Contains(loc, f => f.Message.Contains("interpolates text containing #"));
        Assert.Equal(3, loc.Count);   // three findings, no more

        int kills = 12;
        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
            {
                ui.Text("#SS_UI_TeamMenu_Title", Text.Title);
                ui.Data("Alyx");
                ui.Text($"{kills} / 30");
            }
        });
        Assert.Empty(Of(good, "localise"));
    }

    [Fact]
    public void Raw_reports_every_escape_from_the_tokens()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Column(Raw.Du(13)).At(UiAnchor.Centre).Name("gap"))
            {
                ui.Rect(Raw.Color(255, 0, 255), Size.Du(40), Size.Du(40)).Name("magenta");
                ui.Data("Ink").Color(Raw.Color(10, 200, 10)).Name("ink");
                ui.Rect(Tone.Accent, Size.Du(40), Size.Du(40)).Inset(Raw.Du(5), Space.None).Name("nudged");
            }
        });
        var raw = Of(bad, "raw").Select(f => f.Element).ToList();
        Assert.Contains(raw, e => e.EndsWith("gap"));
        Assert.Contains(raw, e => e.EndsWith("magenta"));
        Assert.Contains(raw, e => e.Contains("ink"));
        Assert.Contains(raw, e => e.EndsWith("nudged"));

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Column(Space.M).At(UiAnchor.Centre))
            {
                ui.Rect(Tone.Accent, Size.Du(40), Size.Du(40));
                ui.Data("Ink").Color(Tone.Text);
            }
        });
        Assert.Empty(Of(good, "raw"));
    }

    [Fact]
    public void Ids_reports_two_keyed_elements_sharing_an_id()
    {
        var bad = Lint((ref UiFrame ui) =>
        {
            using (ui.Row(Space.M).At(UiAnchor.Centre))
            {
                ui.Button("join", "#SS_UI_TeamMenu_Join");
                ui.Button("join", "#SS_UI_TeamMenu_Spectate");
            }
        });
        Assert.Contains(Of(bad, "ids"), f => f.Message.StartsWith("join and join share id"));

        var good = Lint((ref UiFrame ui) =>
        {
            using (ui.Row(Space.M).At(UiAnchor.Centre))
            {
                ui.Button("join", "#SS_UI_TeamMenu_Join");
                ui.Button("spectate", "#SS_UI_TeamMenu_Spectate");
            }
        });
        Assert.Empty(Of(good, "ids"));
    }

    [Fact]
    public void Every_rule_has_a_planted_defect_fact()
    {
        var facts = typeof(UiLintTests).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Length > 0)
            .Select(m => m.Name).ToList();
        Assert.Equal(10, UiLint.Rules.Count);
        foreach (var rule in UiLint.Rules)
        {
            string prefix = char.ToUpperInvariant(rule[0]) + rule[1..].Replace("-", "_") + "_reports";
            Assert.True(facts.Any(f => f.StartsWith(prefix, StringComparison.Ordinal)), $"rule {rule} has no {prefix}... fact");
        }
    }
}
