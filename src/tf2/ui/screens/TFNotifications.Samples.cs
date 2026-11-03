using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The notification banner from fakes (the ui-screen loop's sample step). The
// #TF2_Notify_* tokens live in game/mod_tf2/resource/tf2_english.txt.

public static class TFNotificationsSamples
{
    public static UiScreen Build(UiSampleServices s, FakeNotifications? source = null)
    {
        s.Add<ITFNotifications>(source ?? new FakeNotifications());
        return s.Create<TFNotifications>();
    }

    // The victims' banner: the enemy has the local team's flag
    // (HUD_NOTIFY_YOUR_FLAG_TAKEN).
    [UiSample("the enemy has our flag, red local")]
    private static UiScreen OurFlagTakenRed(UiSampleServices s) => Build(s,
        source: new FakeNotifications
        {
            Type = 0, LocalTeam = Red,
            Token = "#TF2_Notify_YourFlagTaken_Red",
        });

    // The takers' banner: the longest copy, on the other side's variant
    // (HUD_NOTIFY_ENEMY_FLAG_TAKEN).
    [UiSample("we have their flag, blue local")]
    private static UiScreen TheirFlagTakenBlue(UiSampleServices s) => Build(s,
        source: new FakeNotifications
        {
            Type = 4, LocalTeam = Blue,
            Token = "#TF2_Notify_EnemyFlagTaken_Blue",
        });

    // The capture pair, one per side (YOUR/ENEMY_FLAG_CAPTURED).
    [UiSample("our flag was captured, blue local")]
    private static UiScreen OurFlagCapturedBlue(UiSampleServices s) => Build(s,
        source: new FakeNotifications
        {
            Type = 3, LocalTeam = Blue,
            Token = "#TF2_Notify_YourFlagCaptured_Blue",
        });

    [UiSample("we captured theirs, red local")]
    private static UiScreen TheirFlagCapturedRed(UiSampleServices s) => Build(s,
        source: new FakeNotifications
        {
            Type = 7, LocalTeam = Red,
            Token = "#TF2_Notify_EnemyFlagCaptured_Red",
        });

    // The zone warning, the carrier's own line (TOUCHING_ENEMY_CTF_CAP).
    [UiSample("touching the enemy zone, red local")]
    private static UiScreen TouchingEnemyCap(UiSampleServices s) => Build(s,
        source: new FakeNotifications
        {
            Type = 8, LocalTeam = Red,
            Token = "#TF2_Notify_TouchingEnemyCtfCap_Red",
        });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}

/// <summary>The notification as the screen reads it; the fade time lives in
/// the adapter's facts.</summary>
public sealed class FakeNotifications : ITFNotifications
{
    public bool Present { get; init; } = true;

    public byte Type { get; init; }

    public float SecondsLeft { get; init; } = 3f;

    public int LocalTeam { get; init; }

    public string Token { get; init; } = "";
}
