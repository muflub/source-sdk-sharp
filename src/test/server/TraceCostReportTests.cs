using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: <c>cs_sv_trace_cost</c>, the instrument the
/// trace move is measured with: one row per kind of trace and the timer's
/// floor, and "reset" zeroes the meters after printing.
/// </summary>
public class TraceCostReportTests
{
    private sealed class FakeConsole : IEngineConsole
    {
        public List<string> Lines { get; } = new();

        public void Msg(string message) => Lines.Add(message);

        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => string.Empty;
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => 0;
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    [Fact]
    public void EachKindOfTraceAndTheTimersFloorGetsARow()
    {
        var console = new FakeConsole();
        new TraceCostReport(console, new ServerTraceCost()).Execute("");

        string all = string.Concat(console.Lines);
        foreach (string name in new[] { "trace.line", "trace.hull", "trace.entity_hull", "trace.water", "timer.empty" })
            Assert.Contains(name, all);
        Assert.Equal(5, console.Lines.Count);
    }

    [Fact]
    public void ResetZeroesTheMetersAfterPrintingThem()
    {
        var cost = new ServerTraceCost();
        cost.Line.Record(100, 1);
        cost.Water.Record(100, 1);

        new TraceCostReport(new FakeConsole(), cost).Execute("reset");

        Assert.All(cost.Meters(), m => Assert.Equal(0, m.Meter.Calls));
    }

    [Fact]
    public void WithoutResetTheMetersKeepCounting()
    {
        var cost = new ServerTraceCost();
        cost.Hull.Record(100, 1);

        new TraceCostReport(new FakeConsole(), cost).Execute("");

        Assert.Equal(1, cost.Hull.Calls);
    }
}
