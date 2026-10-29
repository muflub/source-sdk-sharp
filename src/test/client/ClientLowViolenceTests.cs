using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

namespace SourceSharp.Tests;

/// <summary>
/// UTIL_IsLowViolence (util_shared.cpp), decided in C#: any of the four legacy
/// violence_* convars off, else the engine's answer.
/// </summary>
public class ClientLowViolenceTests
{
    private static readonly string[] Four = ["violence_hblood", "violence_ablood", "violence_hgibs", "violence_agibs"];

    private sealed class Console(Dictionary<string, string> values) : IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private sealed class Engine(bool low) : IViolenceSettings
    {
        public int Asked { get; private set; }

        public bool EngineSaysLowViolence
        {
            get
            {
                Asked++;
                return low;
            }
        }
    }

    private static Dictionary<string, string> AllOn() => Four.ToDictionary(n => n, _ => "1");

    [Theory]
    [InlineData("violence_hblood")]
    [InlineData("violence_ablood")]
    [InlineData("violence_hgibs")]
    [InlineData("violence_agibs")]
    public void Any_convar_off_is_low_and_the_engine_is_not_asked(string off)
    {
        var values = AllOn();
        values[off] = "0";
        var engine = new Engine(low: false);

        Assert.True(new ClientLowViolence(new Console(values), engine).IsLowViolence);
        Assert.Equal(0, engine.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_all_four_on_the_engine_decides(bool engineLow)
    {
        var engine = new Engine(engineLow);

        Assert.Equal(engineLow, new ClientLowViolence(new Console(AllOn()), engine).IsLowViolence);
        Assert.Equal(1, engine.Asked);
    }

    [Fact]
    public void A_convar_the_console_does_not_answer_reads_as_off()
    {
        var values = AllOn();
        values.Remove("violence_agibs");

        Assert.True(new ClientLowViolence(new Console(values), new Engine(low: false)).IsLowViolence);
    }

    [Fact]
    public void A_fraction_truncates_as_GetInt_does()
    {
        var values = AllOn();
        values["violence_hblood"] = "0.5";

        Assert.True(new ClientLowViolence(new Console(values), new Engine(low: false)).IsLowViolence);
    }
}
