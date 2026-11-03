using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The building status from fakes (the ui-screen loop's sample step). The
// labels are copy; the bars ride the line's numbers.

public static class TFBuildingStatusSamples
{
    public static UiScreen Build(UiSampleServices s, FakeBuildingStatus? buildings = null)
    {
        s.Add<ITFBuildingStatus>(buildings ?? new FakeBuildingStatus());
        s.Add<IUiLocalisation>(new SampleLocalise());
        return s.Create<TFBuildingStatus>();
    }

    // The built pair: a level-1 sentry with full health and shells over its
    // metal, the dispenser carrying metal.
    [UiSample("engineers-sentry-and-dispenser")]
    private static UiScreen Built(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.Sentry, 1, Red, false, false, false, 100, 150, 150, 90, 150, 0,
                12, 4, -1, 0, 0, 0),
            new(TFBuildingLineKind.Dispenser, 1, Red, false, false, false, 100, 125, 125, 0, 0, 200,
                0, 0, -1, 0, 0, 0),
        ],
    });

    // The construction switch: the sentry mid-build with its progress bar,
    // the dispenser not yet started (the not-built label).
    [UiSample("sentry-under-construction")]
    private static UiScreen Building(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.Sentry, 1, Red, false, true, false, 40, 45, 150, 0, 0, 0,
                0, 0, -1, 0, 0, 0),
            new(TFBuildingLineKind.Dispenser, 1, Red, true, false, false, 0, 0, 0, 0, 0, 0,
                0, 0, -1, 0, 0, 0),
        ],
    });

    // The alert tray: a sapper on the sentry (the sapper icon), the
    // dispenser wounded low enough for the wrench.
    [UiSample("sapped-and-wounded")]
    private static UiScreen Alerts(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.Sentry, 1, Red, false, false, true, 100, 150, 150, 20, 150, 0,
                12, 4, -1, 0, 0, 0),
            new(TFBuildingLineKind.Dispenser, 1, Red, false, false, false, 100, 30, 125, 0, 0, 200,
                0, 0, -1, 0, 0, 0),
        ],
    });

    // The spy's arm: the enemy's sentry carrying the sapper.
    [UiSample("spys-sapped-enemy")]
    private static UiScreen SpyView(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.Sentry, 2, Blue, false, false, true, 100, 150, 150, 90, 150, 0,
                12, 4, -1, 0, 0, 0),
        ],
    });

    // The recharge state: the entrance mid-charge against the exit's
    // level-1 metal, both teleporter ends of one pair.
    [UiSample("teleporter-pair-charging")]
    private static UiScreen Charging(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.TeleporterEntrance, 1, Red, false, false, false, 100, 150, 150, 0, 0, 0,
                0, 0, 40, 3, 0, 0),
            new(TFBuildingLineKind.TeleporterExit, 1, Red, false, false, false, 100, 150, 150, 0, 0, 0,
                0, 0, -1, 3, 0, 0),
        ],
    });

    // The level 3's rockets row over the kill counts.
    [UiSample("level-three-sentry")]
    private static UiScreen LevelThree(UiSampleServices s) => Build(s, new FakeBuildingStatus
    {
        Lines =
        [
            new(TFBuildingLineKind.Sentry, 3, Red, false, false, false, 100, 216, 216, 150, 150, 0,
                57, 21, -1, 0, 12, 20),
        ],
    });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}

/// <summary>The building status as the screen reads it; the row walk lives
/// in the adapter's facts, so the samples fix one frame.</summary>
public sealed class FakeBuildingStatus : ITFBuildingStatus
{
    public TFBuildingStatusLine[] Lines { get; set; } = [];

    public int Count => Lines.Length;

    public TFBuildingStatusLine Building(int i) => Lines[i];
}

/// <summary>The words the mod's table localises, as tf2_english.txt does
/// (the not-built label's one token over its two lines, the HUD's uber
/// label and its healing line).</summary>
public sealed class SampleLocalise : IUiLocalisation
{
    public string? Find(string token) => token switch
    {
        "#TF2_Hud_UberCharge" => "ÜBERCHARGE:",
        "#TF2_Hud_Healing" => "Healing",
        "#TF2_Building_Building" => "Building...",
        "#TF2_Building_NotBuilt_Sentry" => "Sentry\nNot Built",
        "#TF2_Building_NotBuilt_Dispenser" => "Dispenser\nNot Built",
        "#TF2_Building_NotBuilt_Entrance" => "Entrance\nNot Built",
        "#TF2_Building_NotBuilt_Exit" => "Exit\nNot Built",
        "#TF2_Building_Kills" => "Kills:",
        "#TF2_Building_Assists" => "Assists:",
        _ => null,
    };
}
