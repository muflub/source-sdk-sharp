using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
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
        => s.Add<IMainMenuSource>(new Fake()).Add<IMainMenuCommands>(new Commands())
            .Add<IBuildStampSource>(new FakeBuildStamp { DebugLine = "t 34.305  pos -2739.0 2563.0 192.8  ang 0.0 -150.0  fps 144" })
            .Create<MainMenu>();
}
