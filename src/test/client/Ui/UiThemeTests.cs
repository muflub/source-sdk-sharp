using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>The default theme, the theme file override, and Animate.</summary>
public class UiThemeTests
{
    [Fact]
    public void The_default_theme_defines_every_token()
    {
        var t = SourceSharpTheme.Create();
        foreach (var tone in Enum.GetValues<Tone>().Where(x => x != Tone.None))
            Assert.True(t.Color(tone).A > 0, $"{tone} is transparent");
        foreach (var role in Enum.GetValues<Text>())
            Assert.True(t.Type(role).SizeDu > 0 && t.Type(role).Family.Length > 0, $"{role} has no type");
        Assert.True(t.Duration(Motion.Quick) < t.Duration(Motion.Standard) && t.Duration(Motion.Standard) < t.Duration(Motion.Slow));
    }

    [Fact]
    public void The_theme_file_overrides_colours_sizes_and_motion()
    {
        var t = SourceSharpTheme.Create();
        UiThemeFile.Apply(t, """
            # a comment
            tone.accent: "#112233"
            tone.surface: '#00000080'   # with alpha
            type.number.size: 52
            type.label.upper: false
            motion.quick: 0.05
            hud.surface-opacity: 0.5
            scroll.step: 32
            """);
        Assert.Equal(new UiColor(0x11, 0x22, 0x33, 255), t.Color(Tone.Accent));
        Assert.Equal(new UiColor(0, 0, 0, 0x80), t.Color(Tone.Surface));
        Assert.Equal(52, t.Type(Text.Number).SizeDu);
        Assert.False(t.Type(Text.Label).Upper);
        Assert.Equal(0.05f, t.Duration(Motion.Quick));
        Assert.Equal(0.5f, t.HudSurfaceOpacity);
        Assert.Equal(32f, t.ScrollStepDu);
    }

    [Theory]
    [InlineData("tone.acent: \"#112233\"", "no Tone named 'acent'")]
    [InlineData("tone.accent: red", "not #rrggbb")]
    [InlineData("type.number.sise: 4", "unknown type property")]
    [InlineData("colour: 1", "unknown key")]
    public void A_bad_theme_line_is_an_error_naming_the_line(string line, string why)
    {
        var e = Assert.Throws<InvalidDataException>(() => UiThemeFile.Apply(SourceSharpTheme.Create(), "\n" + line));
        Assert.Contains("line 2", e.Message);
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void Animate_eases_to_its_target_with_the_themes_curve()
    {
        var ctx = Context();
        var values = new List<float>();
        float target = 0f;
        TestScreen.Describer d = (ref UiFrame ui) => values.Add(ui.Animate("v", target, Motion.Standard));

        Frame(ctx, Screen(1920, 1080), d, now: 0.0);       // first frame snaps
        target = 100f;
        for (int i = 1; i <= 30; i++)
            Frame(ctx, Screen(1920, 1080), d, now: i / 60.0);

        Assert.Equal(0f, values[0]);
        Assert.InRange(values[1], 1f, 30f);                 // moving, not jumped
        Assert.True(values.Zip(values.Skip(1)).All(p => p.Second >= p.First), "monotonic");
        // 0.5 s is 2× Standard (0.25 s in the test theme): within 1% of the target.
        Assert.InRange(values[^1], 99f, 100f);
    }
}
