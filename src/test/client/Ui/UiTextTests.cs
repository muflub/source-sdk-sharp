using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Text (plan_client_ui U1 step 3): fonts measured from their TrueType files,
/// elision, alignment, and #Tokens from the mod's language file.
/// </summary>
public class UiTextTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void Every_embedded_face_reads_with_sane_metrics()
    {
        Assert.Equal(8, Fonts.Faces.Count);
        foreach (var f in Fonts.Faces)
        {
            Assert.Equal(1000, f.UnitsPerEm);
            Assert.InRange(f.Ascender, 800, 1200);
            Assert.InRange(f.Descender, -400, -150);
            Assert.True(f.Has('A') && f.Has('0') && f.Has('…'), $"{f.Family} {f.Weight} lacks a basic glyph");
            // Advances differ by glyph (the cmap and hmtx were really read), except in the mono face.
            if (f.Family == UiFontLibrary.Mono)
                Assert.Equal(f.AdvanceUnits('i'), f.AdvanceUnits('M'));
            else
                Assert.NotEqual(f.AdvanceUnits('i'), f.AdvanceUnits('M'));
        }
    }

    [Fact]
    public void Digits_are_tabular_so_changing_numbers_do_not_jitter()
    {
        var font = new UiFontSpec(UiFontLibrary.Condensed, 48, 700, UiFontFlags.None);
        float zero = Fonts.Advance(font, '0');
        for (char c = '1'; c <= '9'; c++)
            Assert.Equal(zero, Fonts.Advance(font, c));
    }

    [Fact]
    public void Weights_and_families_resolve_to_the_nearest_face()
    {
        Assert.Equal(700, Fonts.Face(new UiFontSpec(UiFontLibrary.Condensed, 20, 800, 0)).Weight);
        Assert.Equal(600, Fonts.Face(new UiFontSpec(UiFontLibrary.Condensed, 20, 620, 0)).Weight);
        Assert.Equal(UiFontLibrary.Sans, Fonts.Face(new UiFontSpec("Trebuchet MS", 20, 400, 0)).Family);
        // Condensed is narrower than the regular width at one size and weight.
        var c = new UiFontSpec(UiFontLibrary.Condensed, 20, 500, 0);
        var s = new UiFontSpec(UiFontLibrary.Sans, 20, 500, 0);
        Assert.True(Fonts.Advance(c, 'n') < Fonts.Advance(s, 'n'));
    }

    private static UiContext TtfContext(IUiLocaliser? loc = null)
        => new(PlainTheme(), Fonts, loc ?? new NoLocaliser(), []) { RecordNames = true };

    [Fact]
    public void Text_that_does_not_fit_is_elided_when_asked_and_reported_when_not()
    {
        var ctx = TtfContext();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column(Space.S).Width(Size.Du(120)))
            {
                ui.Data("xXx_TheLongestNameOnTheServer_xXx").Elide().Name("elided");
                ui.Data("xXx_TheLongestNameOnTheServer_xXx").Name("overflow");
                ui.Data("Short").Name("fits");
            }
        });

        var elements = Elements(ctx);
        int elided = Array.FindIndex(elements, e => NameOf(ctx, e) == "elided");
        int overflow = Array.FindIndex(elements, e => NameOf(ctx, e) == "overflow");
        int fits = Array.FindIndex(elements, e => NameOf(ctx, e) == "fits");

        string drawn = RunText(ctx, elided);
        Assert.EndsWith("…", drawn);
        Assert.StartsWith("xXx_", drawn);
        Assert.True((elements[elided].Flags & UiElementFlags.Elided) != 0);

        // The elided run fits its 120 du box.
        var font = ctx.List.AsDrawList().Fonts[0];
        float width = drawn.Sum(ch => Fonts.Advance(font, ch));
        Assert.True(width <= 120f + 0.5f, $"elided width {width}");

        Assert.True((elements[overflow].Flags & UiElementFlags.Overflowed) != 0);
        Assert.True(ctx.Report.OverflowOf(overflow) > 100f, $"overflow {ctx.Report.OverflowOf(overflow)}");
        Assert.Equal(UiElementFlags.None, elements[fits].Flags & (UiElementFlags.Elided | UiElementFlags.Overflowed));
        Assert.Equal("Short", RunText(ctx, fits));
    }

    [Fact]
    public void Text_aligns_within_its_box()
    {
        var ctx = TtfContext();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column().Width(Size.Du(400)))
            {
                ui.Data("Start").Name("s");
                ui.Data("End").TextAlign(Align.End).Name("e");
                ui.Data("Mid").TextAlign(Align.Centre).Name("m");
            }
        });

        var list = ctx.List.AsDrawList();
        var font = list.Fonts[0];
        float W(string t) => t.Sum(ch => Fonts.Advance(font, ch));
        var runs = list.GlyphRuns.ToArray();
        Assert.Equal(0, runs[0].X);
        Assert.Equal(MathF.Round(400 - W("End")), runs[1].X);
        Assert.Equal(MathF.Round((400 - W("Mid")) / 2), runs[2].X);
    }

    // The game's chain: mods/test's file, then the core's #SS_UI_ strings.
    private static UiLanguageFile Language()
        => UiLanguageFile.Chain([RepoLayout.Repo("game", "mod_test", "resource", "mod_sharp_english.txt"),
                                 RepoLayout.Repo(UiLanguageFile.CoreRepoPath.Split('/'))]);

    [Fact]
    public void Tokens_come_from_the_mods_language_file_and_the_cores()
    {
        var lang = Language();
        Assert.True(lang.Count > 40, $"{lang.Count} tokens: the file was not read");
        Assert.Equal("Health", lang.Find("#SS_UI_Health"));
        Assert.Equal("Health", lang.Find("#ss_ui_health"));         // keys match without case, as the engine's
        Assert.Equal("OVERWATCH STANDARD ISSUE\n(PULSE-RIFLE)", lang.Find("#HL2_Pulse_Rifle"));
        Assert.Equal("matches", lang.Find("#SS_UI_Console_Matches"));   // the last token: nothing above broke the parse
        Assert.Null(lang.Find("#SS_UI_NoSuchToken"));
        Assert.Null(lang.Find("SS_UI_Health"));                     // a token starts with #
    }

    [Fact]
    public void A_token_is_localised_and_a_label_upper_cased_and_a_missing_token_flagged()
    {
        var ctx = TtfContext(Language());
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column())
            {
                ui.Text("#SS_UI_Health", Text.Label).Name("label");
                ui.Text("#SS_UI_Missing").Name("missing");
                ui.Text("Literal copy").Name("literal");
                ui.Data("PlayerName").Name("data");
            }
        });

        var elements = Elements(ctx);
        int Find(string n) => Array.FindIndex(elements, e => NameOf(ctx, e) == n);
        Assert.Equal("HEALTH", RunText(ctx, Find("label")));
        Assert.Equal(0, (int)(elements[Find("label")].Flags & UiElementFlags.Unlocalised));
        Assert.Equal("#SS_UI_Missing", RunText(ctx, Find("missing")));
        Assert.NotEqual(0, (int)(elements[Find("missing")].Flags & UiElementFlags.Unlocalised));
        Assert.NotEqual(0, (int)(elements[Find("literal")].Flags & UiElementFlags.Unlocalised));
        Assert.Equal(0, (int)(elements[Find("data")].Flags & UiElementFlags.Unlocalised));

        // Game data keeps its case even in an upper-case role; copy does not.
        var ctx2 = TtfContext(Language());
        Frame(ctx2, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column())
            {
                ui.Data("xXx_Gordon", Text.Label).Name("name");   // Label is the test theme's upper-case role
                ui.Data("Alyx", Text.Body).Style(Text.Label).Name("restyled");
                ui.Text("#SS_UI_Health", Text.Label).Name("copy");
            }
        });
        var els2 = Elements(ctx2);
        Assert.Equal("xXx_Gordon", RunText(ctx2, Array.FindIndex(els2, e => NameOf(ctx2, e) == "name")));
        Assert.Equal("Alyx", RunText(ctx2, Array.FindIndex(els2, e => NameOf(ctx2, e) == "restyled")));
        Assert.Equal("HEALTH", RunText(ctx2, Array.FindIndex(els2, e => NameOf(ctx2, e) == "copy")));
    }

    [Fact]
    public void Interpolated_text_formats_numbers_into_the_frame()
    {
        var ctx = TtfContext();
        int clip = 18, reserve = 150;
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => { ui.Text($"{clip} / {reserve}", Text.Number).Name("ammo"); });
        Assert.Equal("18 / 150", RunText(ctx, Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == "ammo")));
    }
}
