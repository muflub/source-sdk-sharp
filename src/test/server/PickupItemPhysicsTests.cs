using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A map item is a physics object that falls and settles on its model's
/// hull, as stock's CItem::Spawn and CreateItemVPhysicsObject (item_world.cpp)
/// make it: COLLISION_GROUP_WEAPON (players pass through it), SOLID_VPHYSICS
/// with FSOLID_TRIGGER | FSOLID_NOT_STANDABLE, simulated and awake. Map
/// weapons (CBaseCombatWeapon::FallInit) do the same.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --map dm_lockdown,
/// 2026-10-25): items stood about a unit above stock's, and some 10-20 units,
/// because here they were trigger boxes that never fell.
/// </para>
/// </summary>
public class PickupItemPhysicsTests
{
    private static readonly EntityHandle Item = new(0x0004_0701u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(string className, bool physics = true)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", physics);
        registry.Spawn(Item, className);
        engine.Clear();
        ((PickupItem)registry.Get(Item)!).OnThink(PickupItem.PhysicsContext);
        return (registry, engine);
    }

    [Theory]
    [InlineData(EntityNames.ItemBattery)]
    [InlineData(EntityNames.ItemHealthkit)]
    public void ItBecomesAnAwakePhysicsObject(string className)
    {
        var (_, engine) = Spawned(className);

        var init = engine.Last("EntityPhysics.InitPhysics");
        Assert.True(init is not null, "no physics object was made");
        Assert.False((bool)init!.Value.Args[1]!, "created awake, so it falls");
        Assert.False((bool)init.Value.Args[2]!, "not debris");
    }

    [Fact]
    public void ItCollidesAsAWeaponSoPlayersPassThrough()
    {
        var (_, engine) = Spawned(EntityNames.ItemBattery);

        Assert.Equal(CollisionGroup.Weapon, (CollisionGroup)engine.Last("EntityPhysics.SetCollisionGroup")!.Value.Args[1]!);
    }

    [Fact]
    public void ItIsASolidPhysicsTriggerNothingStandsOn()
    {
        var (_, engine) = Spawned(EntityNames.ItemBattery);

        var call = engine.Last("Entities.SetSolidity");
        Assert.True(call is not null, "the solidity was never set");
        Assert.Equal(SolidType.VPhysics, (SolidType)call!.Value.Args[1]!);
        Assert.Equal(SolidFlags.Trigger | SolidFlags.NotStandable, (SolidFlags)call.Value.Args[2]!);
    }

    [Fact]
    public void TheSolidityAndGroupAreSetBeforeTheObjectIsMade()
    {
        // VPhysicsInitNormal reads them: the object is made as what the entity is.
        var (_, engine) = Spawned(EntityNames.ItemBattery);

        var order = engine.Calls.Select(c => c.Member).ToList();
        int init = order.IndexOf("EntityPhysics.InitPhysics");
        Assert.True(order.IndexOf("Entities.SetSolidity") < init && order.IndexOf("EntityPhysics.SetCollisionGroup") < init,
                    string.Join(", ", order));
    }

    [Theory]
    [InlineData(EntityNames.ItemHealthcharger)]
    [InlineData(EntityNames.ItemSuitcharger)]
    public void AWallChargerStaysOnTheWall(string className)
    {
        // CNewWallHealth / CNewRecharge are static wall fixtures, not CItems.
        var (_, engine) = Spawned(className);

        Assert.False(engine.Called("EntityPhysics.InitPhysics"), "a charger is not a falling item");
    }
}
