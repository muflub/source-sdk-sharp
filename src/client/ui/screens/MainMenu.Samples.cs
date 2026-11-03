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

    private sealed class Commands : IMainMenuCommands
    {
        public List<string> Run { get; } = [];

        void IMainMenuCommands.Run(string command) => Run.Add(command);
    }

    [UiSample("in game: resume, disconnect, players, then the rest")]
    private static UiScreen Column(UiSampleServices s)
        => s.Add<IMainMenuSource>(new Fake()).Add<IMainMenuCommands>(new Commands()).Create<MainMenu>();
}
