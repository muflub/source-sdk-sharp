using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The previews' fonts as the game measured them (plan_client_ui §5.4, U2).</summary>
public class UiRecordedFontMetricsTests
{
    private static readonly UiFontLibrary Ttf = new();

    private const string Two = "# a comment\nIBM Plex Sans\t20\t400\t1\t27\t21\t32\t" + "5,6,7\n";

    [Fact]
    public void A_recorded_font_measures_as_recorded()
    {
        var m = new UiRecordedFontMetrics(Ttf, Two);
        var f = new UiFontSpec("IBM Plex Sans", 20, 400, UiFontFlags.Antialias);

        Assert.Equal(new UiFontMetrics(21, 6, 27), m.Metrics(f));
        Assert.Equal(6f, m.Advance(f, '!'));
        Assert.Equal(5f, m.Advance(f, ' '));
    }

    [Fact]
    public void What_the_recording_does_not_hold_falls_to_the_ttf()
    {
        var m = new UiRecordedFontMetrics(Ttf, Two);
        var held = new UiFontSpec("IBM Plex Sans", 20, 400, UiFontFlags.None);
        var other = new UiFontSpec("IBM Plex Sans", 21, 400, UiFontFlags.None);

        Assert.Equal(Ttf.Advance(held, 'Q'), m.Advance(held, 'Q'));     // past the recorded range
        Assert.Equal(Ttf.Metrics(other), m.Metrics(other));
        Assert.Equal(Ttf.Advance(other, 'a'), m.Advance(other, 'a'));
    }

    [Fact]
    public void A_short_row_is_refused()
        => Assert.Throws<InvalidDataException>(() => new UiRecordedFontMetrics(Ttf, "IBM Plex Sans\t20\t400\n"));

    /// <summary>
    /// The fixture holds every theme role at every preview size, so the
    /// previews measure every string with the game's widths. A theme or size
    /// change that adds a font fails here until ui-fonts.tsv is re-recorded.
    /// </summary>
    [Fact]
    public void The_fixture_holds_every_role_at_every_preview_size()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "ui-fonts.tsv");
        Assert.True(File.Exists(path), "ui-fonts.tsv is missing; a scan of nothing would pass");
        var m = new UiRecordedFontMetrics(Ttf, File.ReadAllText(path));
        var theme = SourceSharpTheme.Create();
        var missing = new List<string>();
        int checkedSpecs = 0;
        foreach (var size in UiSamples.Sizes())
            foreach (var role in Enum.GetValues<Text>())
            {
                var t = theme.Type(role);
                var spec = new UiFontSpec(t.Family, Math.Max(1, (int)MathF.Round(t.SizeDu * size.Screen.Scale)), t.Weight, t.Flags);
                checkedSpecs++;
                if (!m.Holds(spec))
                    missing.Add($"{spec.Family} {spec.PxHeight}px {spec.Weight} ({role} @ {size.Name})");
            }
        Assert.True(checkedSpecs > 50, $"{checkedSpecs} specs");
        Assert.True(missing.Count == 0, "not recorded:\n" + string.Join("\n", missing.Distinct()));
    }
}
