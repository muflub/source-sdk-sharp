using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 mod's theme file (game/mod_tf2/resource/ui-theme.yaml): TF2's own
/// palette and its two fonts, over the toolkit's roles. Reads the file the
/// game ships, so a typo or a lost row is red here before it is in game.
/// </summary>
public class UiTF2ThemeTests
{
    private static UiTheme TF2Theme()
    {
        var theme = SourceSharpTheme.Create();
        UiThemeFile.Apply(theme, File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "resource", "ui-theme.yaml")));
        return theme;
    }

    [Fact]
    public void The_tf2_theme_names_its_two_fonts_by_game_path()
    {
        var fonts = TF2Theme().FontFiles;
        Assert.Equal(new UiThemeFontFile("TF2", "resource/TF2.ttf"), fonts[0]);
        Assert.Equal(new UiThemeFontFile("TF2 Build", "resource/TF2Build.ttf"), fonts[1]);
        Assert.Equal(new UiThemeFontFile("TF2 Secondary", "resource/TF2Secondary.ttf"), fonts[2]);
    }

    [Fact]
    public void The_tf2_theme_maps_the_scheme_onto_the_roles()
    {
        var t = TF2Theme();
        // TF2 for the number role (the scheme's HudFontBig, clientscheme.res
        // :848); TF2 Build for display, title and heading; TF2 Secondary for
        // body, label and caption — all weight 500.
        Assert.Equal("TF2", t.Type(Text.Number).Family);
        foreach (var role in new[] { Text.Display, Text.Title, Text.Heading })
        {
            Assert.Equal("TF2 Build", t.Type(role).Family);
            Assert.Equal(500, t.Type(role).Weight);
        }
        foreach (var role in new[] { Text.Body, Text.Label, Text.Caption })
        {
            Assert.Equal("TF2 Secondary", t.Type(role).Family);
            Assert.Equal(500, t.Type(role).Weight);
        }
    }

    [Fact]
    public void The_tf2_theme_sets_the_schemes_colours()
    {
        var t = TF2Theme();
        Assert.Equal(new UiColor(0x2e, 0x2b, 0x2a, 0xc8), t.Color(Tone.Surface));   // TanDarker
        Assert.Equal(new UiColor(0xeb, 0xe2, 0xca, 255), t.Color(Tone.Text));      // TanLight
        Assert.Equal(new UiColor(0xd4, 0x7f, 0x19, 255), t.Color(Tone.Accent));
        Assert.Equal(new UiColor(0xb4, 0x5c, 0x4d, 255), t.Color(Tone.TeamA));     // HUDRedTeamSolid; team 2 is RED
        Assert.Equal(new UiColor(0x68, 0x7c, 0x9b, 255), t.Color(Tone.TeamB));     // HUDBlueTeamSolid; team 3 is BLU
    }
}
