using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// physics_cannister against stock physics_cannister.cpp: the explosion breaks
/// the model into its gibs, the jet is sized, and unlit damage erodes integer
/// health the way stock's int -= float does.
/// </summary>
public class CannisterStockTests
{
    private static readonly EntityHandle Cannister = new(0x0001_00D0);
    private static readonly EntityHandle Jet = new(0x0001_00D1);
    private const string Model = "models/props_c17/canister01a.mdl";

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysicsCannister).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true);
        engine.Returns("EntityPhysics.GetMass", 10f);
        engine.Returns("MotionControllers.Create", 0x0002_0009u);
        engine.Returns("Entities.Create", Jet);
        engine.Returns("Entities.GetHealth", 25);
        engine.Returns("Precache.BreakPieceCount", 0);
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "model", Model);
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "thrust", "1000");
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "fuel", "4");
        registry.Spawn(Cannister, EntityNames.PhysicsCannister);
        return (registry, engine);
    }

    [Fact]
    public void Exploding_breaks_the_model_into_its_pieces_before_the_blast()
    {
        // physics_cannister.cpp:366-367: PropBreakableCreateAll, then
        // ExplosionCreate, then UTIL_Remove.
        var (registry, engine) = Spawned();
        int before = engine.Calls.Count;

        registry.Input(Cannister, "Explode", "", 0f, EntityHandle.None);

        var calls = engine.Calls.Skip(before).ToList();
        int pieces = calls.FindIndex(c => c.Member == "Precache.BreakPieceCount" && (string)c.Args[0]! == Model);
        int removed = calls.FindIndex(c => c.Member == "Entities.Remove" && c.ArgAs<EntityHandle>(0) == Cannister);
        Assert.True(pieces >= 0, "the explosion asked for the model's break pieces");
        Assert.True(removed < 0 || pieces < removed, "before the cannister went away");
    }

    [Fact]
    public void The_jet_is_sized_as_stock_sizes_it()
    {
        // physics_cannister.cpp:247-248: m_StartSize = 10, m_EndSize = 25.
        var (registry, engine) = Spawned();

        registry.Input(Cannister, "Activate", "", 0f, EntityHandle.None);

        var calls = engine.Calls.ToList();
        int spawn = calls.FindIndex(c => c.Member == "Entities.Spawn" && c.ArgAs<EntityHandle>(0) == Jet);
        int start = calls.FindIndex(c => c.Member == "Entities.SetKeyValue" && c.ArgAs<EntityHandle>(0) == Jet
                                         && (string)c.Args[1]! == "StartSize" && (string)c.Args[2]! == "10");
        int end = calls.FindIndex(c => c.Member == "Entities.SetKeyValue" && c.ArgAs<EntityHandle>(0) == Jet
                                       && (string)c.Args[1]! == "EndSize" && (string)c.Args[2]! == "25");
        Assert.True(spawn >= 0, "the control: the jet was spawned");
        Assert.True(start >= 0 && start < spawn, "StartSize 10 reaches the jet before it spawns");
        Assert.True(end >= 0 && end < spawn, "EndSize 25 reaches the jet before it spawns");
    }

    [Fact]
    public void The_thrust_is_computed_once_in_the_cannisters_own_frame()
    {
        // physics_cannister.cpp:225 CThrustController::CalcThrust at
        // CannisterActivate: CalculateVelocityOffset, then WorldToLocalVector,
        // answered as SIM_LOCAL_ACCELERATION every tick after. No re-issue per think.
        var (registry, engine) = Spawned();

        registry.Input(Cannister, "Activate", "", 0f, EntityHandle.None);
        for (int i = 0; i < 5; i++)
            registry.Think(Cannister);

        var thrust = Assert.Single(engine.Calls, c => c.Member == "MotionControllers.SetForceAtPoint");
        Assert.True(thrust.ArgAs<bool>(4), "in the object's frame, so it turns as the cannister tumbles");
        Assert.True(MathF.Abs(thrust.ArgAs<Vector>(1).Length - 1000f * 10f) < 0.5f,
                    $"thrust * mass, as CalcThrust's force ({thrust.ArgAs<Vector>(1)})");
    }

    [Fact]
    public void Unlit_fractional_damage_erodes_integer_health_as_stock_does()
    {
        // physics_cannister.cpp:167: m_iHealth -= info.GetDamage(), an int
        // minus a float truncated back: 25 - 0.5 = 24.5 -> 24.
        var (registry, _) = Spawned();
        var can = (PhysicsCannister)registry.Get(Cannister)!;

        Assert.Equal(1, can.OnDamaged(EntityHandle.None, 0.5f, DamageType.Bullet));
    }
}
