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

        // An item is physical from its spawn; a weapon drops in its spawn and
        // becomes physical in its think (FallInit, FallThink).
        if (className.StartsWith("weapon_", StringComparison.Ordinal))
            ((PickupItem)registry.Get(Item)!).OnThink(PickupItem.PhysicsContext);

        return (registry, engine);
    }

    /// <summary>
    /// CItem::Spawn makes the object in the spawn itself, so it is there for
    /// the map's first physics step. Found beside stock (dm_lockdown,
    /// 2026-10-27, cs_sv_physics_watch): ours came a think later, and the
    /// crate keyed on a healthkit fell past it on the first step, slid, and
    /// knocked the stack above it to the floor; made at spawn, every object
    /// in the stack matches stock's step for step to its resting pose.
    /// </summary>
    [Fact]
    public void AnItemsObjectIsMadeInItsSpawnWithNoThinkToWaitFor()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", true);

        registry.Spawn(Item, EntityNames.ItemHealthkit);

        Assert.NotNull(engine.Last("EntityPhysics.InitPhysics"));
    }

    /// <summary>CBaseCombatWeapon::FallInit: a weapon's object waits for its fall's think.</summary>
    [Fact]
    public void AWeaponsObjectWaitsForItsThink()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        var services = Services(out var engine);
        services.AddInstance(new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                         engine, engine, registry, engine, new DamageHitGroups()));
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", true);

        registry.Spawn(Item, EntityNames.WeaponSmg1);

        Assert.Null(engine.Last("EntityPhysics.InitPhysics"));
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

    /// <summary>
    /// FallInit drops the weapon in its spawn, while the map is still
    /// spawning, so the drop meets only what spawned before it. Found beside
    /// stock (dm_lockdown, 2026-10-27): ours dropped from a think after the
    /// whole map had spawned, and the ar2 over its own ammo, and three frags
    /// beside frags that had already fallen, started solid and hung in the
    /// air (stock's ar2 rests at z 4.5, ours stayed at 17).
    /// </summary>
    [Fact]
    public void A_map_weapons_drop_runs_in_its_spawn_and_its_object_waits()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        var services = Services(out var engine);
        services.AddInstance(new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                         engine, engine, registry, engine, new DamageHitGroups()));
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", true).Returns("Clock.Now", 10f);
        AFragOverTheFloor(engine, new EntityHullTrace(Floor, false, false, Fraction: 0.4f));
        var thinks = new ThinkScheduler();
        registry.KeyValue(Item, EntityNames.WeaponFrag, "classname", EntityNames.WeaponFrag);
        ((PickupItem)registry.Get(Item)!).BindThinks(thinks);

        registry.Spawn(Item, EntityNames.WeaponFrag);

        Assert.True(engine.Called("WorldQuery.EntityTraceHull"), "dropped in the spawn");
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetOrigin" && ((Vector)c.Args[1]!).Equals(Floor));
        Assert.False(engine.Called("EntityPhysics.InitPhysics"), "physical only in FallThink");
        Assert.Equal(10.1f, thinks.NextThinkTime(Item, PickupItem.PhysicsContext), 4);
    }

    /// <summary>
    /// The think materializes on the spawn's answer and does not drop again:
    /// what spawned after the weapon (its ammo, a frag that has since fallen)
    /// is not in its way, as it was not in stock's.
    /// </summary>
    [Fact]
    public void The_think_makes_it_physical_on_the_spawns_drop_without_dropping_again()
    {
        int sweeps = 0;
        var (_, engine) = Spawned(EntityNames.WeaponFrag, arrange: e =>
        {
            AFragOverTheFloor(e, default);
            e.ReturnsEachCall("WorldQuery.EntityTraceHull", () => sweeps++ == 0
                ? new EntityHullTrace(Floor, false, false, Fraction: 0.4f)
                : new EntityHullTrace(Floor, true, true));
        });

        Assert.Equal(1, sweeps);
        Assert.True(engine.Called("EntityPhysics.InitPhysics"), "materialized on the spawn's drop");
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

    //-------------------------------------------------------------------------
    // The reach (CCollisionProperty::UseTriggerBounds) and the sight line
    // (UTIL_ItemCanBeTouchedByPlayer). Found by stock-ab's solidity census
    // (2026-10-26): stock's items and weapons carry FSOLID_USE_TRIGGER_BOUNDS,
    // so a player takes one 24 (a weapon 36) units before reaching its box.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Toucher = new(0x0001_0001u);

    private static (float Bloat, bool Uniform)[] Reach(RecordingEngine engine)
        => engine.AllOf("Entities.UseTriggerBounds").Where(c => c.ArgAs<bool>(1))
                 .Select(c => (c.ArgAs<float>(2), c.ArgAs<bool>(3))).ToArray();

    [Theory]
    [InlineData(EntityNames.ItemBattery, 24f)]
    [InlineData(EntityNames.ItemAmmoPistol, 24f)]
    [InlineData(EntityNames.WeaponPistol, 36f)]
    public void Its_trigger_reaches_past_its_box_by_stocks_bloat(string className, float bloat)
    {
        var (_, engine) = Spawned(className);

        Assert.Equal([(bloat, false)], Reach(engine));
    }

    [Fact]
    public void The_reach_is_kept_when_it_has_no_physics_and_stays_a_box()
    {
        var (_, engine) = Spawned(EntityNames.ItemBattery, physics: false);

        Assert.Equal([(24f, false)], Reach(engine));
    }

    [Fact]
    public void The_gravity_guns_carry_doubles_an_items_reach_and_the_drop_restores_it()
    {
        var (registry, engine) = Spawned(EntityNames.ItemBattery);
        var item = (PickupItem)registry.Get(Item)!;
        engine.Clear();

        ((IPhysGunTarget)item).OnPhysGunPickup(Toucher, PhysGunPickup.PickedUpByCannon);
        ((IPhysGunTarget)item).OnPhysGunDrop(Toucher, PhysGunDrop.DroppedByCannon);

        Assert.Equal([(48f, false), (24f, false)], Reach(engine));
        Assert.Equal(1, item.OnCacheInteraction.FireCount);
    }

    [Fact]
    public void A_weapons_reach_is_not_the_gravity_guns_business()
    {
        var (registry, engine) = Spawned(EntityNames.WeaponPistol);
        engine.Clear();

        ((IPhysGunTarget)registry.Get(Item)!).OnPhysGunPickup(Toucher, PhysGunPickup.PickedUpByCannon);

        Assert.Empty(Reach(engine));
    }

    private static (PickupItem Item, RecordingEngine Engine) Touched(string className, bool seen)
    {
        var (registry, engine) = Spawned(className, arrange: e =>
            e.Returns("Entities.IsPlayer", true)
             .Returns("Entities.GetHealth", 100)
             .Returns("Entities.IsValid", true)
             .Returns("WorldQuery.EntityTraceHull",
                      new EntityHullTrace(Vector.Zero, false, false, seen ? EntityHandle.None : new EntityHandle(0x0002_0099u),
                                          seen ? 1f : 0.4f)));
        var item = (PickupItem)registry.Get(Item)!;
        item.OnTouch(Toucher);
        return (item, engine);
    }

    [Fact]
    public void An_item_behind_a_wall_is_not_taken_and_says_nothing()
    {
        var (item, _) = Touched(EntityNames.ItemBattery, seen: false);

        Assert.Equal((0, 0), (item.OnCacheInteraction.FireCount, item.OnPlayerTouch.FireCount));
    }

    [Fact]
    public void An_item_in_sight_says_it_was_reached_even_with_nothing_to_give()
    {
        var (item, engine) = Touched(EntityNames.ItemBattery, seen: true);

        Assert.Equal(1, item.OnCacheInteraction.FireCount);
        var trace = Assert.Single(engine.AllOf("WorldQuery.EntityTraceHull"));
        Assert.Equal(GameMovement.CollisionGroupPlayerMovement, trace.ArgAs<int>(6));
    }

    [Fact]
    public void A_weapon_behind_a_wall_says_nothing_but_is_still_offered()
    {
        var (item, _) = Touched(EntityNames.WeaponPistol, seen: false);

        Assert.Equal(0, item.OnCacheInteraction.FireCount);
        // DefaultTouch's BumpWeapon runs whatever the sight line said.
        bool gates = (bool)typeof(WeaponPickup).GetProperty("SightGatesPickup",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(item)!;
        Assert.False(gates);
    }

    //-------------------------------------------------------------------------
    // A weapon's solidity (CWeaponHL2MPBase::Materialize): a box with a
    // physics object, a trigger, and not-standable only for the stunstick.
    // Found by stock-ab's solidity census (2026-10-26): here weapons were
    // SOLID_VPHYSICS hulls, all marked not-standable.
    //-------------------------------------------------------------------------

    private static (SolidType, SolidFlags) Settled(RecordingEngine engine)
    {
        var call = engine.Last("Entities.SetSolidity")!.Value;
        return ((SolidType)call.Args[1]!, (SolidFlags)call.Args[2]!);
    }

    [Fact]
    public void A_weapon_ends_as_a_box_trigger_after_its_object_is_made()
    {
        var (_, engine) = Spawned(EntityNames.WeaponPistol);

        Assert.Equal((SolidType.BoundingBox, SolidFlags.Trigger), Settled(engine));
        int made = engine.Calls.ToList().FindLastIndex(c => c.Member == "EntityPhysics.InitPhysics");
        int boxed = engine.Calls.ToList().FindLastIndex(c => c.Member == "Entities.SetSolidity");
        Assert.True(made >= 0 && boxed > made, $"the box ({boxed}) after the object ({made})");
    }

    [Fact]
    public void Only_the_stunstick_is_not_standable()
        => Assert.Equal((SolidType.BoundingBox, SolidFlags.Trigger | SolidFlags.NotStandable),
                        Settled(Spawned(EntityNames.WeaponStunstick).Engine));

    [Fact]
    public void An_item_stays_a_physics_hull_and_is_set_once()
    {
        var (_, engine) = Spawned(EntityNames.ItemBattery);

        Assert.Single(engine.AllOf("Entities.SetSolidity"));
        Assert.Equal((SolidType.VPhysics, SolidFlags.Trigger | SolidFlags.NotStandable), Settled(engine));
    }

    /// <summary>CItem::PhysicsSolidMaskForEntity adds player clips; a weapon is no CItem.</summary>
    [Theory]
    [InlineData(EntityNames.ItemHealthkit, true)]
    [InlineData(EntityNames.ItemBattery, true)]
    [InlineData(EntityNames.WeaponSmg1, false)]
    public void An_items_object_is_stopped_by_player_clips_and_a_weapons_is_not(string className, bool clipped)
    {
        var (registry, _) = Spawned(className);

        uint mask = registry.Get(Item)!.PhysicsSolidMask;

        Assert.Equal(EnginePushWorld.MaskSolid, mask & ~(uint)GameMovement.ContentsPlayerClip);
        Assert.Equal(clipped, (mask & (uint)GameMovement.ContentsPlayerClip) != 0);
    }
}
