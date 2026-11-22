using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The names the client's console holds, over the engine's registry: an
/// options script runs no line the engine would forward to the server as a
/// string command.
/// </summary>
public class EngineConsoleNamesTests
{
    private sealed class ListedConsole(params NativeConsoleEntry[] entries) : IEngineConsole
    {
        public void Msg(string message) { }
        public void ExecuteServerCommand(string command) { }
        public IReadOnlyList<string> CompleteCommandLine(string line) => [];
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => string.Empty;
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => 0;
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => entries.Length;

        public bool SnapshotEntry(int index, out NativeConsoleEntry entry)
        {
            entry = entries[index];
            return true;
        }

        public void Warning(string message) { }
    }

    [Fact]
    public void The_console_holds_its_convars_and_commands_in_any_case_and_nothing_else()
    {
        var console = new ListedConsole(new NativeConsoleEntry("voice_modenable", "1", "1", "", 0, false),
                                        new NativeConsoleEntry("maxplayers", "", "", "", 0, true));
        var held = new EngineConsoleNames(console).Held();

        Assert.Equal((true, true, true, false),
                     (held.Contains("voice_modenable"), held.Contains("maxplayers"), held.Contains("VOICE_ModEnable"),
                      held.Contains("tf_dingalingaling")));
    }
}
