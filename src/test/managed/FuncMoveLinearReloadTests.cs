using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A func_movelinear standing across a <c>cs_sv_reload</c> must still know
/// where its two ends are.
///
/// <para>
/// <b>THE MEASUREMENT</b> (live, dm_lockdown, 2026-10-05). A probe built with
/// <c>movedir "-90 0 0" movedistance 128 speed 64</c> at (-3843, 3034, 12.7)
/// opened 127 units straight up before the reload. After it, Open set the
/// brush moving at velocity (50.2, -39.7, 0.0): 64 units a second
/// horizontally, towards the WORLD ORIGIN. The ends are computed once, in
/// <c>OnSpawn</c>, into plain managed fields; the reload rebuilt the instance
/// without running <c>OnSpawn</c> and without carrying them, so both ends were
/// <c>Vector.Zero</c>. A sampler reading only z saw "never moves again".
/// </para>
/// </summary>
public class FuncMoveLinearReloadTests
{
    private readonly ReloadStore _reload = new(Side.Server);

    private static readonly Vector Drawn = new(-3843f, 3034f, 12.5f);

    private static ScriptedEntityRegistry NewRegistry(out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncMoveLinear).Assembly);
        registry.Services = Services(out engine);
        engine.Returns("Entities.GetSpawnOrigin", Drawn);
        return registry;
    }

    private static void Build(ScriptedEntityRegistry registry, EntityHandle handle)
    {
        registry.KeyValue(handle, EntityNames.FuncMovelinear, "movedir", "-90 0 0");
        registry.KeyValue(handle, EntityNames.FuncMovelinear, "movedistance", "128");
        registry.KeyValue(handle, EntityNames.FuncMovelinear, "speed", "64");
        registry.Spawn(handle, EntityNames.FuncMovelinear);
    }

    private static Vector LastDestination(RecordingEngine engine)
    {
        EngineCall? call = engine.Last("BrushMovement.MoveToPosition");
        Assert.True(call is not null, "the mover was never told to move");
        return (Vector)call!.Value.Args[1]!;
    }

    /// <summary>
    /// The control: without a reload, Open and Close name the two ends. Shows
    /// the stimulus reaches the subject, so the reload fact's red is the reload.
    /// </summary>
    [Fact]
    public void OpenAndCloseNameTheTwoEndsOfTheTravel()
    {
        var handle = new EntityHandle(0x0004_0041u);
        var registry = NewRegistry(out var engine);
        Build(registry, handle);

        registry.Input(handle, "Open", string.Empty, 0f, EntityHandle.None);
        Assert.Equal(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 128f), LastDestination(engine));

        registry.Input(handle, "Close", string.Empty, 0f, EntityHandle.None);
        Assert.Equal(Drawn, LastDestination(engine));
    }

    /// <summary>The fix: after a reload, Open still goes to the open end.</summary>
    [Fact]
    public void OpenAfterAHotReloadStillGoesToTheOpenEnd()
    {
        _reload.Clear();

        var handle = new EntityHandle(0x0004_0042u);
        var registry = NewRegistry(out _);
        Build(registry, handle);

        registry.SaveForReload(_reload);

        var reloaded = NewRegistry(out var after);
        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));

        reloaded.Input(handle, "Open", string.Empty, 0f, EntityHandle.None);

        Assert.Equal(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 128f), LastDestination(after));
    }

    /// <summary>And Close still goes to the closed end, from the open one.</summary>
    [Fact]
    public void CloseAfterAHotReloadStillGoesToTheClosedEnd()
    {
        _reload.Clear();

        var handle = new EntityHandle(0x0004_0044u);
        var registry = NewRegistry(out _);
        Build(registry, handle);

        registry.Input(handle, "Open", string.Empty, 0f, EntityHandle.None);
        registry.SaveForReload(_reload);

        var reloaded = NewRegistry(out var after);
        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));

        reloaded.Input(handle, "Close", string.Empty, 0f, EntityHandle.None);

        Assert.Equal(Drawn, LastDestination(after));
    }

    /// <summary>
    /// And where it was HEADING is carried too: SetPosition parks it part way,
    /// and the arrival after a reload reports by that target, not by a fresh
    /// zero that would read as "fully closed".
    /// </summary>
    [Fact]
    public void AnArrivalAfterAHotReloadReportsTheTargetSetBeforeIt()
    {
        _reload.Clear();

        var handle = new EntityHandle(0x0004_0043u);
        var registry = NewRegistry(out _);
        Build(registry, handle);

        registry.Input(handle, "SetPosition", "0.5", 0.5f, EntityHandle.None);
        registry.SaveForReload(_reload);

        var reloaded = NewRegistry(out _);
        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));

        var back = (FuncMoveLinear)reloaded.Get(handle)!;
        back.OnMoveDone();

        Assert.Equal(0, back.OnFullyClosed.FireCount);
        Assert.Equal(0, back.OnFullyOpen.FireCount);
    }
}
