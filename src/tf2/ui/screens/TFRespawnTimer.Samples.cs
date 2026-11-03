using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The respawn countdown from fakes (the ui-screen loop's sample step): every
// arm UpdateReinforcements can put on screen, built through the same
// container the game uses. Every #TF2_Respawn_* token lives in
// game/mod_tf2/resource/tf2_english.txt; the player fake is
// FakeLocalPlayer (TFHud.Samples.cs).

public static class TFRespawnTimerSamples
{
    public static UiScreen Build(UiSampleServices s, FakeLocalPlayer? local = null,
                                 FakeRespawnCountdown? countdown = null, IUiLocalisation? localise = null)
    {
        s.Add<ITFLocalPlayer>(local ?? new FakeLocalPlayer());
        s.Add<ITFRespawnCountdown>(countdown ?? new FakeRespawnCountdown());
        s.Add<IUiLocalisation>(localise ?? new RespawnLocalise());
        return s.Create<TFRespawnTimer>();
    }

    // The counting arm: 30 seconds of wave left, the local player dead
    // (Health 0 — the gate's death half, tf_spectatorgui.cpp:343-360).
    [UiSample("respawn in thirty seconds")]
    private static UiScreen Thirty(UiSampleServices s) => Build(s,
        countdown: new FakeRespawnCountdown { HasWave = true, WaitSeconds = 30 });

    // A single second takes its own sentence (stock's == 1 arm).
    [UiSample("one second to respawn")]
    private static UiScreen OneSecond(UiSampleServices s) => Build(s,
        countdown: new FakeRespawnCountdown { HasWave = true, WaitSeconds = 1 });

    // A wave due now says "Prepare to respawn" (the <= 0 arm).
    [UiSample("prepare to respawn")]
    private static UiScreen Now(UiSampleServices s) => Build(s,
        countdown: new FakeRespawnCountdown { HasWave = true, WaitSeconds = 0 });

    // Sudden death: no respawning at all (:363-372).
    [UiSample("no respawning in sudden death")]
    private static UiScreen Stalemate(UiSampleServices s) => Build(s,
        countdown: new FakeRespawnCountdown { Stalemate = true });

    // A team win: wait for the next round (:374-378).
    [UiSample("wait for the next round")]
    private static UiScreen TeamWin(UiSampleServices s) => Build(s,
        countdown: new FakeRespawnCountdown { TeamWin = true });

    // The gate's other half: alive, the label is stock's hidden arm — the
    // sample still draws (previews ignore Visible) for the fact to refuse.
    [UiSample("hidden while alive")]
    private static UiScreen Alive(UiSampleServices s) => Build(s,
        local: new FakeLocalPlayer { Health = 100 },
        countdown: new FakeRespawnCountdown { HasWave = true, WaitSeconds = 30 });
}

/// <summary>The respawn countdown as the sample arms it: at rest no wave
/// is up and no round state names itself.</summary>
public sealed class FakeRespawnCountdown : ITFRespawnCountdown
{
    public bool HasWave { get; init; }
    public int WaitSeconds { get; init; }
    public bool Stalemate { get; init; }
    public bool TeamWin { get; init; }
}

/// <summary>The mod's tokens the screens compose from; the #TF2_Respawn_*
/// and #TF2_Spectator_* labels resolve through the sample context's
/// language file (tf2_english.txt).</summary>
public sealed class RespawnLocalise : IUiLocalisation
{
    public string? Find(string token) => token switch
    {
        "#TF2_Respawn_InSecs" => "Respawn in: %s1 seconds",
        "#TF2_Spec_Map" => "map: %s1",
        "#TF2_Spectator_ChangeClass" => "Press [ %changeclass% ] to Change Class",
        "#TF2_Spectator_ChangeTeam" => "Press [ %changeteam% ] to Change Team",
        "#TF2_Spectator_SwitchCamModeKey" => "[%jump%]",
        "#TF2_Spectator_CycleTargetFwdKey" => "[%attack%]",
        "#TF2_Spectator_CycleTargetRevKey" => "[%attack2%]",
        _ => null,
    };
}
