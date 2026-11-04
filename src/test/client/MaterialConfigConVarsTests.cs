using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A reload-only config change during queued rendering ends the main thread's render context and leaves a mutex
/// held (<see cref="MaterialConfigConVars"/>). These names each killed a live game in dm_lockdown on 2026-11-03:
/// mat_reducefillrate alone, the others after r_waterforceexpensive 1.
/// </summary>
public class MaterialConfigConVarsTests
{
    private static readonly string[] KilledAGame =
        ["mat_reducefillrate", "mat_motion_blur_enabled", "mat_monitorgamma", "mat_vsync"];

    [Fact]
    public void ComposingTheClientMarksTheConVarsThatKilledAGameForTheMaterialThread()
    {
        var adapters = new FakeClientEngineAdapters();
        new ClientModule(new ClientModuleEngine(TestSides.Create(Side.Client)), adapters).ComposeEngineAreas();

        foreach (string name in KilledAGame)
            Assert.Contains((name, ConVarFlags.MaterialSystemThread), adapters.ConVarFlags.Marked);
    }

    [Fact]
    public void EveryMarkIsTheMaterialThreadFlagAlone()
    {
        var adapters = new FakeClientEngineAdapters();
        new MaterialConfigConVars(adapters.ConVarFlags, new Log()).Mark();

        Assert.Equal(MaterialConfigConVars.Names.Count, adapters.ConVarFlags.Marked.Count);
        Assert.All(adapters.ConVarFlags.Marked, m => Assert.Equal(ConVarFlags.MaterialSystemThread, m.Flags));
    }

    [Fact]
    public void AConVarTheEngineDoesNotHaveIsSaidAndTheRestAreStillMarked()
    {
        var flags = new FakeClientEngineAdapters.RecordingConVarFlags();
        flags.Missing.Add("mat_trilinear");
        var log = new Log();

        new MaterialConfigConVars(flags, log).Mark();

        Assert.Contains(log.Lines, l => l.Contains("mat_trilinear", StringComparison.Ordinal));
        Assert.Equal(MaterialConfigConVars.Names.Count, flags.Marked.Count);
    }

    private sealed class Log : ILog
    {
        public List<string> Lines { get; } = [];

        public void Msg(string message) => Lines.Add(message);

        public void Warning(string message) => Lines.Add(message);
    }
}
