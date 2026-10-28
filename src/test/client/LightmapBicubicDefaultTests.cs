using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;

namespace SourceSharp.Tests;

/// <summary>
/// The once-per-install bicubic lightmap default, stock CHLClient::PostInit
/// (cdll_client_int.cpp:1187-1195), decided in C# since lane M1-B.
/// </summary>
public class LightmapBicubicDefaultTests
{
    private sealed class Console(Dictionary<string, string> values) : IEngineConsole
    {
        public List<(string Name, string Value)> Set { get; } = [];
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => Set.Add((name, value));
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    [Theory]
    [InlineData(95, false, true)]
    [InlineData(94, false, false)]
    [InlineData(90, true, true)]
    [InlineData(89, true, false)]
    [InlineData(90, false, false)]
    public void Bicubic_suits_dx95_anywhere_and_dx90_on_linux(int maxDx, bool linux, bool expected)
        => Assert.Equal(expected, LightmapBicubicDefault.Wanted(maxDx, linux));

    [Fact]
    public void A_fresh_install_gets_the_default_and_the_marker()
    {
        var console = new Console(new() { [LightmapBicubicDefault.MarkerName] = "0" });

        Assert.True(new LightmapBicubicDefault(console, linux: true).PostInit(90));
        Assert.Equal([("r_lightmap_bicubic", "1"), ("r_lightmap_bicubic_set", "1")], console.Set);
    }

    [Fact]
    public void An_old_adapter_gets_bicubic_off_and_the_marker()
    {
        var console = new Console(new() { [LightmapBicubicDefault.MarkerName] = "0" });

        Assert.False(new LightmapBicubicDefault(console, linux: false).PostInit(90));
        Assert.Equal([("r_lightmap_bicubic", "0"), ("r_lightmap_bicubic_set", "1")], console.Set);
    }

    [Fact]
    public void A_set_marker_leaves_the_players_choice_alone()
    {
        var console = new Console(new() { [LightmapBicubicDefault.MarkerName] = "1" });

        Assert.Null(new LightmapBicubicDefault(console, linux: true).PostInit(95));
        Assert.Empty(console.Set);
    }

    [Fact]
    public void No_marker_registered_decides_nothing()
    {
        var console = new Console([]);

        Assert.Null(new LightmapBicubicDefault(console, linux: true).PostInit(95));
        Assert.Empty(console.Set);
    }

    [Fact]
    public void No_material_system_decides_nothing()
    {
        var console = new Console(new() { [LightmapBicubicDefault.MarkerName] = "0" });

        Assert.Null(new LightmapBicubicDefault(console, linux: true).PostInit(-1));
        Assert.Empty(console.Set);
    }
}
