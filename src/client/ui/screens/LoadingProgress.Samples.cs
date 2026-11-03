using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>The loading dialog's samples: a load under way, and one whose first step has not named itself.</summary>
internal static class LoadingProgressSamples
{
    private sealed class Fake(float progress, string status) : ILoadingProgressSource
    {
        public bool Showing => true;
        public float Progress => progress;
        public string Status => status;
    }

    [UiSample("loading, halfway")]
    private static UiScreen Halfway(UiSampleServices s)
        => s.Add<ILoadingProgressSource>(new Fake(0.48f, "Connecting to server...")).Create<LoadingProgress>();

    [UiSample("loading, just started")]
    private static UiScreen Started(UiSampleServices s)
        => s.Add<ILoadingProgressSource>(new Fake(0f, "")).Create<LoadingProgress>();
}
