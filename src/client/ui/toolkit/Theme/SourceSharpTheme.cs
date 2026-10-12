using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The default theme (plan_client_ui §5.8.1), in Source's own vernacular:
/// translucent dark panels over the world, an amber accent, warm white text,
/// big condensed numerals, small upper-case labels. Team colours follow
/// HL2DM's: Combine blue, Rebels red. Mods swap the theme, not the screens.
///
/// <para>
/// Contrast: Text on Surface over a white wall is above 7:1, TextMuted above
/// 4.5:1 (the lint checks this against the world backdrops, not by assertion).
/// </para>
/// </summary>
public static class SourceSharpTheme
{
    public static UiTheme Create()
    {
        const string c = UiFontLibrary.Condensed, s = UiFontLibrary.Sans;
        const UiFontFlags aa = UiFontFlags.Antialias, shadow = UiFontFlags.Antialias | UiFontFlags.DropShadow;

        return new UiTheme("SourceSharp")
        {
            HudSurfaceOpacity = 1f,
            MinTargetDu = 32f,
        }
            .Set(Tone.None, default)
            .Set(Tone.Surface, new UiColor(13, 15, 19, 214))
            .Set(Tone.SurfaceRaised, new UiColor(44, 48, 56, 210))
            .Set(Tone.Line, new UiColor(255, 255, 255, 26))
            .Set(Tone.Text, new UiColor(242, 238, 230, 255))
            .Set(Tone.TextMuted, new UiColor(178, 174, 164, 255))
            .Set(Tone.Accent, new UiColor(255, 181, 46, 255))
            .Set(Tone.Positive, new UiColor(126, 214, 110, 255))
            .Set(Tone.Warning, new UiColor(255, 138, 61, 255))
            .Set(Tone.Danger, new UiColor(255, 78, 64, 255))
            .Set(Tone.TeamA, new UiColor(126, 186, 246, 255))
            .Set(Tone.TeamB, new UiColor(246, 112, 94, 255))
            .Set(Tone.Scrim, new UiColor(4, 5, 7, 150))
            .Set(Tone.OnAccent, new UiColor(26, 18, 4, 255))
            .Set(Tone.Window, new UiColor(17, 19, 23, 255))

            .Set(Text.Display, new UiTypeStyle(c, 72, 700, shadow, false))
            .Set(Text.Title, new UiTypeStyle(c, 30, 600, shadow, true))
            .Set(Text.Heading, new UiTypeStyle(c, 22, 600, aa, true))
            .Set(Text.Body, new UiTypeStyle(s, 18, 400, shadow, false))
            .Set(Text.Label, new UiTypeStyle(c, 14, 500, aa, true))
            .Set(Text.Number, new UiTypeStyle(c, 46, 700, shadow, false))
            .Set(Text.Caption, new UiTypeStyle(s, 14, 400, aa, false))
            .Set(Text.Code, new UiTypeStyle(UiFontLibrary.Mono, 15, 400, aa, false))

            .Set(Motion.Instant, 0f)
            .Set(Motion.Quick, 0.12f)
            .Set(Motion.Standard, 0.28f)
            .Set(Motion.Slow, 0.9f)

            .Set(Elevation.Flat, default)
            .Set(Elevation.Raised, new UiShadow(2, 3, 60))
            .Set(Elevation.Overlay, new UiShadow(6, 12, 110));
    }
}
