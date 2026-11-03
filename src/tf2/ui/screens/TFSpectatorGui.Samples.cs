using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The spectator's bars from fakes (the ui-screen loop's sample step): every
// arm UpdateKeyLabels can put on screen, built through the same container
// the game uses. Every #TF2_Spectator_* token lives in
// game/mod_tf2/resource/tf2_english.txt; the player fake is
// FakeLocalPlayer (TFHud.Samples.cs).

public static class TFSpectatorGuiSamples
{
    public static UiScreen Build(UiSampleServices s, FakeObserver? observer = null,
                                 FakeLocalPlayer? local = null, IUiLocalisation? localise = null,
                                 SampleKeys? keys = null)
    {
        s.Add<IObserverSource>(observer ?? new FakeObserver());
        s.Add<ITFLocalPlayer>(local ?? new FakeLocalPlayer());
        s.Add<IUiLocalisation>(localise ?? new RespawnLocalise());
        s.Add<IUiKeyBindingStore>(keys ?? new SampleKeys()
            .With("F1", "changeclass").With("F2", "changeteam")
            .With("SPACE", "jump").With("MOUSE1", "attack").With("MOUSE2", "attack2"));
        return s.Create<TFSpectatorGui>();
    }

    // Chasing a player from the spectator team: the Change Team ask.
    [UiSample("spectating a map")]
    private static UiScreen Chasing(UiSampleServices s) => Build(s,
        observer: new FakeObserver { Mode = SpectatorMode.Chase, MapName = "ctf_2fort" },
        local: new FakeLocalPlayer { Team = 1 });

    // Dying on RED: the death cam rides the bars with the Change Class ask.
    [UiSample("dead on a team")]
    private static UiScreen DeadOnTeam(UiSampleServices s) => Build(s,
        observer: new FakeObserver { Mode = SpectatorMode.DeathCam, MapName = "ctf_2fort" },
        local: new FakeLocalPlayer { Team = 2 });

    // Playing: the bars draw nothing (the gate's refusing arm).
    [UiSample("playing draws nothing")]
    private static UiScreen Playing(UiSampleServices s) => Build(s,
        observer: new FakeObserver { Mode = SpectatorMode.None, MapName = "ctf_2fort" });
}

/// <summary>The observer as the bars read it: the mode and the map name
/// read when observing began (ObserverState's shape).</summary>
public sealed class FakeObserver : IObserverSource
{
    public SpectatorMode Mode { get; init; }
    public string MapName { get; init; } = "";
}
