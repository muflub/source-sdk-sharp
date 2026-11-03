using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>The main menu's sample: the button column, with what the buttons do recorded.</summary>
internal static class MainMenuSamples
{
    private sealed class Fake : IMainMenuSource
    {
        public List<string> Clicked { get; } = [];

        public bool InLevel => true;

        public bool CanPlay => true;

        public void Play() => Clicked.Add("play");

        public void Resume() => Clicked.Add("resume");

        public void OpenOptions() => Clicked.Add("options");

        public void Quit() => Clicked.Add("quit");
    }

    [UiSample("play, options and quit; resume in game")]
    private static UiScreen Column(UiSampleServices s)
        => s.Add<IMainMenuSource>(new Fake()).Create<MainMenu>();
}
