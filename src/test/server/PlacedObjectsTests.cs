using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// HL2MP's level-designer-placed objects against stock's hl2mp_gamerules.cpp
/// and item_world.cpp: an item's home is where it came to rest, a weapon's
/// where it first materialized; a taken item respawns at its home after
/// sv_hl2mp_item_respawn_time (30, where ours waited 20, from the map's
/// keyed origin); and ManageObjectRelocation sends one lying asleep more
/// than 64 units from home back to it, looking at each once per its respawn
/// time. Found beside stock (dm_lockdown census, 2026-10-27): none of it
/// existed here.
/// </summary>
public class PlacedObjectsTests
{
    private static readonly EntityHandle Thing = new(0x0004_0702u);

    private sealed class Rest : IPhysicsRest
    {
        public bool? Answer = true;
        public bool? Asleep(EntityHandle entity) => Answer;
    }

    private sealed class Placed(bool weapon = false) : IPlacedObject
    {
        public EntityHandle Handle => Thing;
        public bool IsPlacedWeapon => weapon;
        public Vector Origin { get; set; } = new(100f, 0f, 0f);
        public Vector HomeOrigin => Vector.Zero;
        public Vector HomeAngles => new(0f, 90f, 0f);
        public float NextResetCheck { get; set; }
    }

    private sealed class Rig
    {
        public required RecordingEngine Engine;
        public required PlacedObjects Objects;
        public required Rest Rest;
        public float Now = 10f;

        public int Teleports => Engine.AllOf("Entities.Teleport").Count();
    }

    private static Rig Made()
    {
        Services(out var engine);
        engine.Strict = false;
        var rest = new Rest();
        var rig = new Rig { Engine = engine, Rest = rest, Objects = new PlacedObjects(engine, rest, engine, engine, engine, engine) };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now);
        return rig;
    }

    [Fact]
    public void One_asleep_more_than_64_units_from_home_is_teleported_back_charged_and_woken()
    {
        var rig = Made();
        rig.Objects.Add(new Placed());

        rig.Objects.Relocate();

        var teleport = rig.Engine.Last("Entities.Teleport");
        Assert.True(teleport is not null, "it was not sent home");
        Assert.Equal((Thing, Vector.Zero, (Vector?)new Vector(0f, 90f, 0f), false),
                     (teleport!.Value.ArgAs<EntityHandle>(0), teleport.Value.ArgAs<Vector>(1),
                      teleport.Value.ArgAs<Vector?>(2), teleport.Value.ArgAs<bool>(3)));
        Assert.Equal(PlacedObjects.ChargeSound, rig.Engine.Last("Sound.EntityEmitSound")!.Value.ArgAs<string>(1));
        Assert.NotNull(rig.Engine.Last("EntityPhysics.Wake"));
    }

    [Fact]
    public void One_within_64_units_of_home_stays()
    {
        var rig = Made();
        rig.Objects.Add(new Placed { Origin = new Vector(64f, 0f, 0f) });

        rig.Objects.Relocate();

        Assert.Equal(0, rig.Teleports);
    }

    [Fact]
    public void One_still_moving_stays_and_is_not_looked_at_again_until_its_time_comes()
    {
        var rig = Made();
        rig.Objects.Add(new Placed());
        rig.Rest.Answer = false;

        rig.Objects.Relocate();
        rig.Rest.Answer = true;
        rig.Now = 39.9f;
        rig.Objects.Relocate();
        Assert.Equal(0, rig.Teleports);

        rig.Now = 40f;
        rig.Objects.Relocate();
        Assert.Equal(1, rig.Teleports);
    }

    [Fact]
    public void A_weapon_is_looked_at_on_the_weapons_time()
    {
        var rig = Made();
        var weapon = new Placed(weapon: true);
        var item = new Placed();
        rig.Objects.Add(weapon);
        rig.Objects.Add(item);

        rig.Objects.Relocate();

        Assert.Equal((30f, 40f), (weapon.NextResetCheck, item.NextResetCheck));
    }

    [Fact]
    public void An_object_with_no_physics_object_stays()
    {
        var rig = Made();
        rig.Objects.Add(new Placed());
        rig.Rest.Answer = null;

        rig.Objects.Relocate();

        Assert.Equal(0, rig.Teleports);
    }

    [Fact]
    public void An_object_joins_once_and_the_level_going_empties_the_list()
    {
        var rig = Made();
        var placed = new Placed();
        rig.Objects.Add(placed);
        rig.Objects.Add(placed);
        Assert.Equal(1, rig.Objects.Count);

        ((IGameLifecycle)rig.Objects).OnLevelShutdown();

        Assert.Equal(0, rig.Objects.Count);
    }

    [Fact]
    public void The_charge_is_precached_with_the_level()
    {
        var rig = Made();

        ((IGameLifecycle)rig.Objects).OnLevelInitPreEntity("dm_lockdown");

        Assert.Equal(PlacedObjects.ChargeSound, rig.Engine.Last("Precache.PrecacheSound")!.Value.ArgAs<string>(0));
    }

    // ---- the pickups ------------------------------------------------------

    private sealed class Pickup
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required PlacedObjects Objects;
        public required Rest Rest;
        public readonly ThinkScheduler Thinks = new();
        public float Now = 10f;
        public Vector Origin = new(5f, 6f, 7f);

        public PickupItem Entity => (PickupItem)Registry.Get(Thing)!;

        /// <summary>The thinks due by <paramref name="at"/>, run as the scheduler hands them over.</summary>
        public void Run(float at)
        {
            Now = at;
            var due = new List<ScheduledThink>();
            Thinks.TakeDue(at, due);
            foreach (var think in due)
            {
                if (think.Context is null)
                    Entity.OnThink();
                else
                    Entity.OnThink(think.Context);
            }
        }
    }

    private static readonly EntityHandle Player = new(0x0001_0001u);

    private static Pickup Spawned(string className, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PickupItem).Assembly);
        var services = Services(out var engine);
        services.AddInstance(new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                         engine, engine, registry, engine, new DamageHitGroups()));
        var rest = new Rest { Answer = false };
        var objects = new PlacedObjects(engine, rest, engine, engine, engine, engine);
        services.AddInstance(objects);
        registry.Services = services;
        engine.Strict = false;

        var p = new Pickup { Registry = registry, Engine = engine, Objects = objects, Rest = rest };
        engine.Returns("EntityPhysics.InitPhysics", true)
              .ReturnsEachCall("Clock.Now", () => p.Now)
              .ReturnsEachCall("Entities.GetOrigin", () => p.Origin)
              .Returns("Entities.GetAngles", new Vector(0f, 45f, 0f))
              .Returns("Entities.GetSpawnOrigin", new Vector(5f, 6f, 70f))
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.IsPlayer", true)
              .Returns("Players.IsAlive", true)
              .Returns("Entities.GetHealth", 50)
              // The floor a weapon's drop finds, and the taker's eye the
              // sight line reaches: one trace answers both.
              .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(5f, 6f, 7f), false, false, Player, 0.5f));
        registry.KeyValue(Player, EntityNames.Player, "targetname", "taker");
        registry.KeyValue(Thing, className, "origin", "5 6 70");
        registry.Get(Thing)!.BindThinks(p.Thinks);
        registry.Spawn(Thing, className);
        return p;
    }

    [Fact]
    public void An_item_watches_its_fall_a_tenth_of_a_second_at_a_time_until_it_sleeps()
    {
        var p = Spawned(EntityNames.ItemBattery);
        Assert.Equal(10.1f, p.Thinks.NextThinkTime(Thing, PickupItem.RestContext), 4);

        p.Run(10.1f);

        Assert.Equal(10.2f, p.Thinks.NextThinkTime(Thing, PickupItem.RestContext), 4);
        Assert.Equal(0, p.Objects.Count);
    }

    [Fact]
    public void An_item_asleep_takes_where_it_lies_as_its_home_and_joins_the_rules()
    {
        var p = Spawned(EntityNames.ItemBattery);
        p.Rest.Answer = true;

        p.Run(10.1f);

        Assert.Equal(1, p.Objects.Count);
        Assert.Equal((new Vector(5f, 6f, 7f), new Vector(0f, 45f, 0f)),
                     (((IPlacedObject)p.Entity).HomeOrigin, ((IPlacedObject)p.Entity).HomeAngles));
        Assert.False(p.Thinks.IsScheduled(Thing, PickupItem.RestContext));
    }

    [Fact]
    public void A_taken_item_waits_the_items_respawn_time_at_its_home_and_comes_back_charged()
    {
        var p = Spawned(EntityNames.ItemHealthkit);
        p.Rest.Answer = true;
        p.Run(10.1f);
        p.Origin = new Vector(50f, 60f, 7f);

        p.Entity.OnTouch(Player);

        Assert.Equal(new Vector(5f, 6f, 7f), p.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1));
        Assert.Equal(40.1f, p.Thinks.NextThinkTime(Thing, null), 4);
        Assert.Equal(1, p.Objects.Count);

        p.Run(40.1f);
        Assert.Equal(PlacedObjects.ChargeSound, p.Engine.Last("Sound.EntityEmitSound")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_weapon_takes_its_first_spot_on_the_floor_as_home_and_leaves_the_rules_when_taken()
    {
        var p = Spawned(EntityNames.WeaponSmg1);
        Assert.Equal(0, p.Objects.Count);

        p.Run(10.1f);
        Assert.Equal(1, p.Objects.Count);
        Assert.Equal(new Vector(5f, 6f, 7f), ((IPlacedObject)p.Entity).HomeOrigin);

        p.Entity.OnTouch(Player);
        Assert.Equal(0, p.Objects.Count);
        Assert.Equal(30.1f, p.Thinks.NextThinkTime(Thing, null), 4);

        p.Run(30.1f);
        Assert.Equal(1, p.Objects.Count);
    }

    /// <summary>CWeaponHL2MPBase::Materialize: one keyed SF_NORESPAWN falls as physics and joins nothing.</summary>
    [Fact]
    public void A_weapon_keyed_not_to_respawn_never_joins()
    {
        var p = Spawned(EntityNames.WeaponSmg1, WeaponPickup.NoRespawnFlag);

        p.Run(10.1f);

        Assert.NotNull(p.Engine.Last("EntityPhysics.InitPhysics"));
        Assert.Equal(0, p.Objects.Count);
    }

    [Fact]
    public void A_removed_pickup_leaves_the_rules()
    {
        var p = Spawned(EntityNames.ItemBattery);
        p.Rest.Answer = true;
        p.Run(10.1f);

        p.Entity.OnRemoved();

        Assert.Equal(0, p.Objects.Count);
    }
}
