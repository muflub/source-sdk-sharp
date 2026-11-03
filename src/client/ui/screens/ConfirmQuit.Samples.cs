using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>Quit's question, asked.</summary>
internal static class ConfirmQuitSamples
{
    private sealed class Fake : IConfirmQuitSource
    {
        public bool Asking => true;
        public void Quit() { }
        public void Cancel() { }
    }

    [UiSample("asked")]
    private static UiScreen Asked(UiSampleServices s) => s.Add<IConfirmQuitSource>(new Fake()).Create<ConfirmQuit>();
}
