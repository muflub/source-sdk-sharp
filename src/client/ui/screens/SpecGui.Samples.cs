using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class SpecGuiSamples
{
    private sealed record Observer(SpectatorMode Mode, string MapName) : IObserverSource;

    [UiSample("chase camera")]
    private static UiScreen Chase(UiSampleServices s)
        => s.Add<IObserverSource>(new Observer(SpectatorMode.Chase, "dm_lockdown")).Create<SpecGui>();

    [UiSample("first person")]
    private static UiScreen InEye(UiSampleServices s)
        => s.Add<IObserverSource>(new Observer(SpectatorMode.InEye, "dm_overwatch")).Create<SpecGui>();

    [UiSample("free look, long map name")]
    private static UiScreen Roaming(UiSampleServices s)
        => s.Add<IObserverSource>(new Observer(SpectatorMode.Roaming, "ss_sandbox_with_a_very_long_workshop_map_name_v12")).Create<SpecGui>();

    [UiSample("death cam, map not known")]
    private static UiScreen DeathCam(UiSampleServices s)
        => s.Add<IObserverSource>(new Observer(SpectatorMode.DeathCam, "")).Create<SpecGui>();
}
