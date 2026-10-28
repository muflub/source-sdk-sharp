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
/// with FSOLID_TRIGGER | FSOLID_NOT_STANDABLE, simulated and awake. A map
/// weapon is dropped first: HL2MP's CWeaponHL2MPBase::FallInit sweeps the
/// model's box to the floor (UTIL_DropToFloor) and Materialize makes it
/// physical where it landed.
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

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(string className, bool physics = true,
                                                                                    Action<RecordingEngine>? arrange = null)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        var services = Services(out var engine);
        services.AddInstance(new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                         engine, engine, registry, engine, new DamageHitGroups()));
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", physics);
        arrange?.Invoke(engine);
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

    // dm_lockdown's frags, keyed 7.68 with the model's box (w_grenade.mdl's
    // hull, -4.68 to 4.84) 3 units above the floor: stock's rest standing on
    // their base, ours fell the 5.4 units as physics and toppled onto their
    // sides 1.6 units higher (tools/stock-ab --map dm_lockdown, 2026-10-26).
    private static readonly Vector Keyed = new(-3346.47f, 5866.9f, 7.68351f);
    private static readonly Vector HullMins = new(-2.089f, -2.089f, -4.6835f);
    private static readonly Vector HullMaxs = new(2.089f, 2.089f, 4.8395f);
    private static readonly Vector Floor = new(-3346.47f, 5866.9f, 4.6835f);

    private static void AFragOverTheFloor(RecordingEngine engine, EntityHullTrace drop)
        => engine.Returns("Entities.GetOrigin", Keyed)
                 .Returns("Entities.GetBounds", (HullMins, HullMaxs))
                 .Returns("WorldQuery.EntityTraceHull", drop);

    [Fact]
    public void A_map_weapon_drops_its_model_box_to_the_floor_before_it_becomes_physical()
    {
        var (_, engine) = Spawned(EntityNames.WeaponFrag,
                                  arrange: e => AFragOverTheFloor(e, new EntityHullTrace(Floor, false, false, Fraction: 0.4f)));

        var sweep = engine.Last("WorldQuery.EntityTraceHull");
        Assert.True(sweep is not null, "the weapon was never dropped");
        Assert.Equal(Keyed, (Vector)sweep!.Value.Args[1]!);
        Assert.Equal(Keyed - new Vector(0f, 0f, WeaponPickup.DropDistance), (Vector)sweep.Value.Args[2]!);
        Assert.Equal(HullMins, (Vector)sweep.Value.Args[3]!);
        Assert.Equal(HullMaxs, (Vector)sweep.Value.Args[4]!);
        Assert.Equal((int)CollisionGroup.None, (int)sweep.Value.Args[6]!);

        var order = engine.Calls.Select(c => c.Member).ToList();
        int placed = engine.Calls.ToList().FindIndex(c => c.Member == "Entities.SetOrigin" && ((Vector)c.Args[1]!).Equals(Floor));
        int init = order.IndexOf("EntityPhysics.InitPhysics");
        Assert.True(placed >= 0 && init > placed, $"set on the floor, then made physical: {string.Join(", ", order)}");
    }

    [Fact]
    public void A_map_weapon_with_no_floor_in_reach_stays_a_trigger_where_it_is()
    {
        var nothing = new EntityHullTrace(Keyed - new Vector(0f, 0f, WeaponPickup.DropDistance), false, false,
                                          Fraction: 1f);
        var (_, engine) = Spawned(EntityNames.WeaponFrag, arrange: e => AFragOverTheFloor(e, nothing));

        Assert.True(engine.Called("WorldQuery.EntityTraceHull"), "the control: the drop was tried");
        Assert.False(engine.Called("EntityPhysics.InitPhysics"), "no ground, so stock's FallThink never materializes it");
        Assert.False(engine.Called("Entities.SetOrigin"), "it stays where the map put it");
    }

    [Fact]
    public void A_map_weapon_inside_a_wall_stays_a_trigger_where_it_is()
    {
        var (_, engine) = Spawned(EntityNames.WeaponFrag,
                                  arrange: e => AFragOverTheFloor(e, new EntityHullTrace(Keyed, true, true)));

        Assert.True(engine.Called("WorldQuery.EntityTraceHull"), "the control: the drop was tried");
        Assert.False(engine.Called("EntityPhysics.InitPhysics"), "UTIL_DropToFloor's allsolid answers -1");
    }

    [Fact]
    public void An_item_falls_as_physics_from_where_the_map_put_it()
    {
        var (_, engine) = Spawned(EntityNames.ItemHealthkit,
                                  arrange: e => AFragOverTheFloor(e, new EntityHullTrace(Floor, false, false, Fraction: 0.4f)));

        Assert.True(engine.Called("EntityPhysics.InitPhysics"), "the control: it became physical");
        Assert.False(engine.Called("WorldQuery.EntityTraceHull"), "CItem::Spawn does not drop to the floor");
    }

    [Fact]
    public void A_weapon_keyed_not_to_respawn_falls_as_physics()
    {
        var (_, engine) = Spawned(EntityNames.WeaponFrag, arrange: e =>
        {
            AFragOverTheFloor(e, new EntityHullTrace(Floor, false, false, Fraction: 0.4f));
            e.Returns("Entities.GetSpawnFlags", WeaponPickup.NoRespawnFlag);
        });

        Assert.True(engine.Called("EntityPhysics.InitPhysics"), "the control: it became physical");
        Assert.False(engine.Called("WorldQuery.EntityTraceHull"), "FallInit's SF_NORESPAWN arm is VPhysicsInitNormal");
    }
}
