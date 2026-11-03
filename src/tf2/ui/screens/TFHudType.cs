using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The hud's type sizes: stock's own ladder, the client scheme's hud faces
/// times 2.25 into du — HudFontGiantBold 44, HudFontBig 36,
/// HudFontMediumBigBold/HudFontMedium 30/24, the hud's small numeral
/// HudFontMediumSmall 18 (health's HudClassHealth 16 is the nearest below it)
/// and HudFontSmall 14. Stock's exact numbers win over the 4 du grid. The hud
/// screens carry them per element (<see cref="HudSize"/>), so the theme's
/// roles keep the toolkit's defaults for every screen stock's HUD files do
/// not cover — the menus, the scoreboard, the dev overlay and the build stamp.
/// </summary>
public static class TFHudType
{
    /// <summary>Sizes the text by its role on the hud ladder.</summary>
    public static UiBox HudSize(this UiBox box) => box.TextSize(Size(box.Role));

    internal static float Size(Text role) => role switch
    {
        Text.Display => GiantBold,          // HudFontGiantBold 44
        Text.Number => Big,                 // HudFontBig 36
        Text.Title => MediumBigBold,        // HudFontMediumBigBold 30
        Text.Heading => Medium,             // HudFontMedium 24
        Text.Body => MediumSmall,           // HudFontMediumSmall 18
        Text.Label or Text.Caption => Small,// HudFontSmall 14
        _ => 0f,                            // not a hud face: the role's theme size
    };

    private const float GiantBold = 99f;    // 44 tall
    private const float Big = 81f;          // 36
    private const float MediumBigBold = 67.5f;  // 30
    private const float Medium = 54f;       // 24
    private const float MediumSmall = 40.5f;    // 18
    private const float Small = 31.5f;      // 14

    /// <summary>Off-ladder hud faces, the scheme's smaller stocks by name:
    /// the escort's CapNumPlayers count (objectivestatusescort.res:427,
    /// HudFontSmallest 11), the time panel's band labels and counted-down
    /// clock (hudobjectivetimepanel.res WaitingForPlayersLabel et al,
    /// ClockSubTextTiny 8 and ClockSubText 9), the cap icon's seconds
    /// (controlpointicon.res CPTimerLabel, ControlPointTimer 10) and the
    /// cap teardrop's reason (controlpointprogressbar.res ProgressText,
    /// DefaultSmall 12).</summary>
    public const float Smallest = 24.75f;         // 11 tall
    public const float ClockSubTextTiny = 18f;    // 8 tall
    public const float ClockSubText = 20.25f;     // 9 tall
    public const float ControlPointTimer = 24.75f;    // ControlPointTimer is 10 (22.5); HudFontSmallest
                                                      // 11 is the nearest face at the lint's 24 du
                                                      // large-text floor, under which the digits'
                                                      // TanLight misses 4.5:1 on the portrait stills
    public const float ProgressText = 27f;        // 12 tall
}
