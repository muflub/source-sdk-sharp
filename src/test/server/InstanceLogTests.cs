using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Statics to zero, the log (the user's ruling, 2026-10-04: "Change logs
/// api"): each composition logs through its own <see cref="ILog"/>, over its own
/// side's console, and two compositions in one process never see each other's
/// lines. The static Log and its per-copy sink slot are gone (bar the one held
/// for the user-message lane).
/// </summary>
public class InstanceLogTests
{
    private static ServerModule Composed(EngineSide engine)
    {
        var module = new ServerModule(new ModuleEngine(engine));
        module.ComposeEngineAreas();
        return module;
    }

    /// <summary>
    /// Two server compositions in one process, each over an engine of its own:
    /// a line each logs reaches its own console's lines and never the other's,
    /// through the composition's ILog and through an entity its registry built.
    /// </summary>
    [Fact]
    public void TwoCompositionsLogToTheirOwnConsolesWithNoCrossover()
    {
        var firstEngine = EngineSide.Create(Side.Server);
        var secondEngine = EngineSide.Create(Side.Server);
        var first = Composed(firstEngine);
        var second = Composed(secondEngine);

        var firstLines = new List<string>();
        var secondLines = new List<string>();
        first.Services.Require<ConsoleLines>().Tap = (text, _) => firstLines.Add(text);
        second.Services.Require<ConsoleLines>().Tap = (text, _) => secondLines.Add(text);

        first.Services.Require<ILog>().Msg("first's line\n");
        second.Services.Require<ILog>().Warning("second's line\n");
        first.Registry.Log.Msg("first's registry\n");

        Assert.Equal(["first's line\n", "first's registry\n"], firstLines);   // the stimulus reached its own
        Assert.Equal(["second's line\n"], secondLines);
        Assert.NotSame(first.Services.Require<ILog>(), second.Services.Require<ILog>());
    }

    /// <summary>
    /// The side's plumbing (its load, its registry's fault reports) prints
    /// through the state's console; a module attaches its composition's log
    /// there and the attachment puts back what was there.
    /// </summary>
    [Fact]
    public void AttachingALogToTheSidePutsBackWhatWasThere()
    {
        var engine = EngineSide.Create(Side.Server);
        var shim = new RecordingLog();
        var mine = new RecordingLog();
        engine.State.Log = shim;

        using (new ModuleEngine(engine).AttachLog(mine))
        {
            Assert.Same(mine, engine.State.Log);
            Assert.Same(mine, engine.State.Vtables.Log);   // the registry's thunks report through it too
        }

        Assert.Same(shim, engine.State.Log);
    }

    /// <summary>
    /// A managed-built thunk's fault goes to its own registry's log, and to no
    /// other registry's.
    /// </summary>
    [Fact]
    public void AThunksFaultIsReportedOnItsOwnRegistrysLog()
    {
        using var mine = new ManagedVtables();
        using var other = new ManagedVtables();
        var mineLog = new RecordingLog();
        var otherLog = new RecordingLog();
        mine.Log = mineLog;
        other.Log = otherLog;

        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", _ => throw new InvalidOperationException("thrown on purpose"));
        var root = new ManagedRoot(bindings, mine).Bind();

        Assert.Equal(0, ManagedVtableTests.AcquireForTest(root, "SourceSharpProbe001"));

        Assert.Contains(mineLog.Lines, l => l.Text.Contains("EXCEPTION IN"));
        Assert.Empty(otherLog.Lines);
    }
}
