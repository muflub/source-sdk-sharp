using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The target ID from fakes (the ui-screen loop's sample step). The name is
// ui.Text (it is copy, not game data); the class name rides ui.Data.

public static class TFTargetIdSamples
{
    public static UiScreen Build(UiSampleServices s, FakeTargetId? target = null, FakeTextureLibrary? textures = null)
    {
        s.Add<ITFTargetId>(target ?? new FakeTargetId());
        s.HudArt = textures ?? s.HudArt;
        return s.Create<TFTargetId>();
    }

    // The plain arms: a named teammate with the health cross full.
    [UiSample("aimed teammate")]
    private static UiScreen Teammate(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Alyx", Team = Red, ShowsHealth = true, Health = 80, MaxHealth = 125,
    });

    // The blue panel art (teambg_3) for a BLU teammate.
    [UiSample("blu teammate")]
    private static UiScreen BluTeammate(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Barney", Team = Blue, ShowsHealth = true, Health = 125, MaxHealth = 125,
    });

    // A wounded teammate: the cross clipped to the health fraction
    // (CTFHealthPanel::Paint).
    [UiSample("wounded teammate")]
    private static UiScreen Wounded(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Alyx", Team = Red, ShowsHealth = true, Health = 30, MaxHealth = 125,
    });

    // A dead teammate: the dead art, full box (:601-612).
    [UiSample("dead teammate")]
    private static UiScreen Dead(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Alyx", Team = Red, ShowsHealth = true, Health = 0, MaxHealth = 125,
    });

    // The friendly spy's reveal: "Disguised as Enemy Soldier"
    // (#TF_playerid_friendlyspy_disguise, c_tf_player.cpp:9768-9779).
    [UiSample("friendly spy disguised as enemy")]
    private static UiScreen SpyReveal(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Gordon", Team = Red, ShowsHealth = true, Health = 80, MaxHealth = 125,
        Line = new(TFTargetIdLineKind.Disguise, "Soldier", true, 0),
    });

    // The medic's charge line (#TF_playerid_mediccharge, :9794-9804).
    [UiSample("medic charge")]
    private static UiScreen Medic(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Alyx", Team = Red, ShowsHealth = true, Health = 150, MaxHealth = 150,
        Line = new(TFTargetIdLineKind.MedicCharge, "", false, 42),
    });

    // Without a resource row: the name shows, the cross hides (:835-838).
    [UiSample("no health row")]
    private static UiScreen NoHealthRow(UiSampleServices s) => Build(s, new FakeTargetId
    {
        Name = "Alyx", Team = Red,
    });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}

/// <summary>The target ID as the screen reads it; the ray and gates live in
/// the adapter's facts, so the samples fix one frame.</summary>
public sealed class FakeTargetId : ITFTargetId
{
    public bool Present { get; set; } = true;

    public string Name { get; set; } = "";

    public int Team { get; set; }

    public bool ShowsHealth { get; set; }

    public int Health { get; set; }

    public int MaxHealth { get; set; }

    public TFTargetIdLine Line { get; set; }
}
