using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The commands the stock GameUI module registered, registered by managed
/// since that module is never called (plan_gameui_managed.md G4):
/// gamemenucommand through the game menu's table, and condump writing the
/// console window's text to the first unused condumpNNN.txt.
/// </summary>
public class GameUiCommandsTests
{
    private sealed class Screens : IGameUiNavigation
    {
        public List<string> Calls { get; } = [];
        public void ShowPauseMenu() => Calls.Add("show-pause");
        public void HidePauseMenu() => Calls.Add("hide-pause");
        public bool MainMenuOpen => false;
        public long MenuFrames => 0;
        public void Open(string screen) => Calls.Add("open " + screen);
        public void Close(string screen) => Calls.Add("close " + screen);
        public bool IsOpen(string screen) => false;
    }

    [Fact]
    public void Gamemenucommand_runs_the_named_command_through_the_table()
    {
        var screens = new Screens();
        var engine = new ChatFeedTests.RecordingEngineClient([]);
        var services = new ServiceContainer().AddInstance(new GameMenuCommands(screens, new ConfirmQuitUi(engine), engine));
        new GameMenuCommandCommand(services).RunLine(" OpenServerBrowser ");
        Assert.Equal(["open serverbrowser"], screens.Calls);
    }

    [Fact]
    public void Gamemenucommand_with_no_ui_does_nothing()
        => new GameMenuCommandCommand(new ServiceContainer()).Execute("OpenServerBrowser");

    [Fact]
    public void Gamemenucommand_completes_stocks_names()
        => Assert.Equal(["gamemenucommand OpenServerBrowser"], new GameMenuCommandCommand(new ServiceContainer()).SuggestFor("openserver"));

    private sealed class Files : IFwdBaseFileSystem
    {
        public HashSet<string> Existing { get; } = [];
        public Dictionary<string, string> Written { get; } = [];
        private string? _open;

        public byte[]? ReadAll(string fileName, string pathId) => null;
        public uint FileSize(string fileName, string pathId) => 0;
        public long FileTime(string fileName, string pathId) => 0;
        public bool FileExists(string fileName, string? pathId) => Existing.Contains(fileName);
        public nint Open(string fileName, string options, string pathId)
        {
            _open = fileName;
            Written[fileName] = "";
            return 1;
        }
        public int Write(ReadOnlySpan<byte> bytes, nint handle)
        {
            Written[_open!] += Encoding.UTF8.GetString(bytes);
            return bytes.Length;
        }
        public void Close(nint handle) => _open = null;
    }

    private sealed class Console(params string[] lines) : IConsoleLogSource
    {
        public int LineCount => lines.Length;
        public ConsoleLine Line(int i) => new(lines[i], ConsoleSeverity.Message);
    }

    [Fact]
    public void Condump_writes_the_console_to_the_first_unused_name()
    {
        var files = new Files();
        files.Existing.Add("condump000.txt");
        files.Existing.Add("condump001.txt");
        Assert.Equal("condump002.txt", new ConsoleDump(files).Write(new Console("] status", "hostname: test")));
        Assert.Equal("] status\nhostname: test\n", files.Written["condump002.txt"]);
    }

    [Fact]
    public void Condump_with_every_name_taken_writes_nothing_and_says_so()
    {
        var files = new Files();
        for (int i = 0; i < ConsoleDump.MaxFiles; i++)
            files.Existing.Add($"condump{i:000}.txt");
        var log = new RecordingLog();
        var services = new ServiceContainer().AddInstance<IConsoleLogSource>(new Console("x"));
        new CondumpCommand(services, files, log).Execute();
        Assert.Empty(files.Written);
        Assert.Contains("Can't condump!", log.Text);
    }
}
