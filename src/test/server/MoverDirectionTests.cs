using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Which way a sliding brush moves and how far, as stock's CBaseDoor::Spawn
/// (doors.cpp) and CFuncMoveLinear::Spawn (func_movelinear.cpp) decide it.
///
/// <para>
/// The direction is the <c>movedir</c> key and nothing else, and a brush's
/// <c>angles</c> stay its rotation: ss_sandbox's doors station once wrote its
/// direction as <c>angles</c>, which this mod read as the direction and stock
/// drew as a brush turned on its side, out of view (tools/stock-ab, 2026-10-25).
/// With no explicit distance the travel is the brush's collision size less 2
/// (the engine grows a brush's bounds by 1 on every side) along the direction,
/// less the lip.
/// </para>
/// </summary>
public class MoverDirectionTests
{
    private static readonly Vector Drawn = new(100f, 200f, 8f);

    // A brush 128 x 64 x 128 as the collision bounds report it: one unit wider
    // on every side.
    private static readonly Vector Bounds = new(130f, 66f, 130f);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine, EntityHandle Handle) Spawn(
        string className, uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncDoor).Assembly);
        registry.Services = Services(out var engine);
        engine.Returns("Entities.GetSpawnOrigin", Drawn)
              .Returns("Entities.GetBoundsSize", Bounds)
              .Returns("Entities.GetSpawnAngles", new Vector(-90f, 0f, 0f));

        var handle = new EntityHandle(index);
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);
        registry.Spawn(handle, className);
        return (registry, engine, handle);
    }

    // Angles.Forward(-90 0 0) is (~1e-7, 0, 1), not (0, 0, 1).
    private static void Near(Vector expected, Vector actual)
        => Assert.True(MathF.Abs(expected.X - actual.X) < 0.01f && MathF.Abs(expected.Y - actual.Y) < 0.01f
                       && MathF.Abs(expected.Z - actual.Z) < 0.01f, $"expected {expected}, got {actual}");

    private static Vector OpenedTo(ScriptedEntityRegistry registry, RecordingEngine engine, EntityHandle handle)
    {
        registry.Input(handle, "Open", string.Empty, 0f, EntityHandle.None);
        EngineCall? call = engine.Last("BrushMovement.MoveToPosition");
        Assert.True(call is not null, "Open never moved the brush");
        return (Vector)call!.Value.Args[1]!;
    }

    [Fact]
    public void ADoorsAnglesAreNotItsDirection()
    {
        // angles -90 0 0 and no movedir: stock's movedir is 0 0 0, which is +x.
        var (registry, engine, handle) = Spawn(EntityNames.FuncDoor, 0x0004_0101u, ("lip", "8"));

        Near(new Vector(Drawn.X + 120f, Drawn.Y, Drawn.Z), OpenedTo(registry, engine, handle));
    }

    [Fact]
    public void ADoorKeepsItsAngles()
    {
        var (_, engine, _) = Spawn(EntityNames.FuncDoor, 0x0004_0102u, ("movedir", "-90 0 0"));

        Assert.False(engine.Called("Entities.SetAngles"), "a door's angles are its rotation; stock never zeroes them");
    }

    [Fact]
    public void ADoorTravelsItsDepthLessTwoAndTheLip()
    {
        var (registry, engine, handle) = Spawn(EntityNames.FuncDoor, 0x0004_0103u, ("movedir", "-90 0 0"), ("lip", "8"));

        Near(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 120f), OpenedTo(registry, engine, handle));
    }

    [Fact]
    public void ADoorHasNoMoveDistanceKey()
    {
        // func_door's travel is always its own depth: CBaseDoor declares no
        // movedistance, and CBaseToggle's "distance" is a rotating door's angle.
        var (registry, engine, handle) = Spawn(EntityNames.FuncDoor, 0x0004_0104u,
            ("movedir", "-90 0 0"), ("lip", "8"), ("movedistance", "50"));

        Near(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 120f), OpenedTo(registry, engine, handle));
    }

    [Fact]
    public void RisingWaterIsADoorWithTheSameRules()
    {
        var (registry, engine, handle) = Spawn(EntityNames.FuncWater, 0x0004_0105u, ("movedir", "-90 0 0"));

        Near(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 128f), OpenedTo(registry, engine, handle));
        Assert.False(engine.Called("Entities.SetAngles"), "func_water is a door: its angles stay");
    }

    [Fact]
    public void AMoveLinearsAnglesAreNotItsDirection()
    {
        var (registry, engine, handle) = Spawn(EntityNames.FuncMovelinear, 0x0004_0106u, ("MoveDistance", "96"));

        Near(new Vector(Drawn.X + 96f, Drawn.Y, Drawn.Z), OpenedTo(registry, engine, handle));
    }

    [Fact]
    public void AMoveLinearKeepsItsAngles()
    {
        var (_, engine, _) = Spawn(EntityNames.FuncMovelinear, 0x0004_0107u, ("movedir", "-90 0 0"), ("MoveDistance", "96"));

        Assert.False(engine.Called("Entities.SetAngles"), "a func_movelinear's angles are its rotation");
    }

    [Fact]
    public void AMoveLinearWithNoDistanceTravelsItsDepthLessTwoAndTheLip()
    {
        var (registry, engine, handle) = Spawn(EntityNames.FuncMovelinear, 0x0004_0108u, ("movedir", "-90 0 0"), ("lip", "8"));

        Near(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 120f), OpenedTo(registry, engine, handle));
    }

    [Fact]
    public void AMoveLinearsDistanceIsUsedWhenTheMapGivesOne()
    {
        // The control for the fact above: the same brush with a distance goes
        // that far, lip or not.
        var (registry, engine, handle) = Spawn(EntityNames.FuncMovelinear, 0x0004_0109u,
            ("movedir", "-90 0 0"), ("lip", "8"), ("MoveDistance", "40"));

        Near(new Vector(Drawn.X, Drawn.Y, Drawn.Z + 40f), OpenedTo(registry, engine, handle));
    }
}
