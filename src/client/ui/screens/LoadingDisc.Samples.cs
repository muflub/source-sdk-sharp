using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class LoadingDiscSamples
{
    private sealed record Disc(bool Loading, bool Paused, string LoadingText, string PausedText) : ILoadingDiscSource;

    [UiSample("loading")]
    private static UiScreen Loading(UiSampleServices s) => s.Add<ILoadingDiscSource>(new Disc(true, false, "LOADING", "")).Create<LoadingDisc>();

    [UiSample("paused with a timer")]
    private static UiScreen Paused(UiSampleServices s) => s.Add<ILoadingDiscSource>(new Disc(false, true, "", "PAUSED - 27")).Create<LoadingDisc>();
}
