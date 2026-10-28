using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// func_friction as stock's CFrictionModifier (triggers.cpp) is: the
/// <c>modifier</c> key as a percentage, set on whatever starts touching it
/// unless that is a player, and set back to 1 when it stops. Its StartTouch
/// replaces CBaseTrigger's, so no trigger spawnflag or filter is asked.
///
/// <para>
/// Found side by side with stock (tools/stock-ab, 2026-10-25): this mod read
/// a <c>modifytofriction</c> key no game's code or FGD has and slowed players.
/// </para>
/// </summary>
public class FuncFrictionTests
{
    private static readonly EntityHandle Volume = new(0x0004_0401u);
    private static readonly EntityHandle Crate = new(0x0004_0402u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(bool toucherIsPlayer, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncFriction).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Entities.IsPlayer", toucherIsPlayer)
              .Returns("Entities.IsValid", true);

        foreach (var (key, value) in keys)
            registry.KeyValue(Volume, EntityNames.FuncFriction, key, value);
        registry.Spawn(Volume, EntityNames.FuncFriction);
        return (registry, engine);
    }

    private static float? FrictionSet(RecordingEngine engine)
        => engine.Last("EntityPhysics.SetFriction") is { } call ? (float)call.Args[1]! : null;

    [Fact]
    public void TheModifierKeyIsAPercentage()
    {
        var (registry, engine) = Spawned(false, ("modifier", "15"));

        registry.Touch(Volume, Crate);

        Assert.Equal(0.15f, FrictionSet(engine)!.Value, 4);
    }

    [Fact]
    public void APlayerIsNotChanged()
    {
        var (registry, engine) = Spawned(true, ("modifier", "15"));

        registry.Touch(Volume, Crate);

        Assert.False(engine.Called("EntityPhysics.SetFriction"), "stock: if ( !pOther->IsPlayer() )");
    }

    [Fact]
    public void LeavingPutsItBack()
    {
        var (registry, engine) = Spawned(false, ("modifier", "15"));

        registry.Touch(Volume, Crate);
        registry.EndTouch(Volume, Crate);

        Assert.Equal(1f, FrictionSet(engine)!.Value);
    }

    [Fact]
    public void NoTriggerSpawnflagIsAsked()
    {
        // Clients only (1) on a CBaseTrigger would refuse a crate; this
        // StartTouch never asks.
        var (registry, engine) = Spawned(false, ("modifier", "15"), ("spawnflags", "1"));
        engine.Returns("Entities.GetSpawnFlags", 1);

        registry.Touch(Volume, Crate);

        Assert.Equal(0.15f, FrictionSet(engine)!.Value, 4);
    }

    [Fact]
    public void ModifyToFrictionIsNotAKey()
    {
        var (registry, engine) = Spawned(false, ("modifytofriction", "15"));

        registry.Touch(Volume, Crate);

        // Unset, the modifier is 0: stock's m_frictionFraction starts zeroed.
        Assert.Equal(0f, FrictionSet(engine)!.Value);
    }
}
