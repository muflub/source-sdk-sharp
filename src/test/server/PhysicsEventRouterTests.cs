using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The physics event ring delivered to the entities it names (physics stock
/// parity, step 3), and what stock's entities do with each event.
///
/// <para>
/// Written before the router routed anything, from stock's
/// CPhysicsProp::VPhysicsUpdate (props.cpp:2911-2935): a prop spawned with
/// SF_PHYSPROP_START_ASLEEP fires OnAwakened the first time its object is
/// awake, then clears the flag so it never fires again; a prop without the
/// flag never fires it.
/// </para>
/// </summary>
public class PhysicsEventRouterTests
{
    private const int StartAsleep = 1;

    // The step's events are queued by Route (inside the solver's
    // PostSimulationFrame) and delivered by Flush (the managed frame, after
    // PhysFrame) -- stock's post-simulation delivery.
    private static void Deliver(PhysicsEventRouter router, ReadOnlySpan<PhysicsEvent> events)
    {
        router.Route(events);
        router.Flush();
    }

    [Fact]
    public void Nothing_reaches_an_entity_until_the_flush()
    {
        var (registry, router, flags) = World();
        var handle = new EntityHandle(60);
        var prop = Prop(registry, flags, handle, StartAsleep);

        router.Route(new[] { Event(PhysicsEventKind.Wake, handle) });
        Assert.True(prop.OnAwakened.FireCount == 0, "the drain runs inside the solver step: it only queues");

        router.Flush();
        Assert.True(prop.OnAwakened.FireCount == 1, "the frame after PhysFrame delivers");
    }

    // Spawnflags live in the engine block. The stub answers per entity with the
    // last SetSpawnFlags written for it, so a flag the prop clears stays
    // cleared, as stock's RemoveSpawnFlags leaves it.
    private static (ScriptedEntityRegistry Registry, PhysicsEventRouter Router, Dictionary<EntityHandle, int> Flags) World()
        => World(out _);

    private static (ScriptedEntityRegistry Registry, PhysicsEventRouter Router, Dictionary<EntityHandle, int> Flags) World(out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        var recording = engine;

        // An awake object is updated every drain, and the update reads where
        // it is and how fast: in the world and at rest unless a fact says not.
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetVelocity", Vector.Zero);

        var flags = new Dictionary<EntityHandle, int>();
        // The recording engine logs a call before it asks the factory, so the
        // last call is this GetSpawnFlags and names the entity asking.
        engine.ReturnsEachCall("Entities.GetSpawnFlags", () =>
        {
            var asking = (EntityHandle)recording.Calls[^1].Args[0]!;
            var written = recording.Calls.LastOrDefault(c => c.Member == "Entities.SetSpawnFlags" && Equals(c.Args[0], asking));
            return written.Member is not null ? written.Args[1] : flags.GetValueOrDefault(asking);
        });
        return (registry, new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(), services.Require<FluidTouches>(), services.Require<FrictionSounds>(), services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>()), flags);
    }

    private static PropPhysics Prop(ScriptedEntityRegistry registry, Dictionary<EntityHandle, int> flags,
                                    EntityHandle handle, int spawnFlags)
    {
        flags[handle] = spawnFlags;
        registry.Spawn(handle, EntityNames.PropPhysics);
        return (PropPhysics)registry.Get(handle)!;
    }

    private static PhysicsEvent Event(PhysicsEventKind kind, EntityHandle entity) => new()
    {
        KindRaw = (uint)kind,
        Entity0 = entity.Value,
        Entity1 = PhysicsEvent.NoEntity,
    };

    [Fact]
    public void A_start_asleep_prop_fires_OnAwakened_when_its_object_wakes()
    {
        var (registry, router, flags) = World();
        var handle = new EntityHandle(11);
        var prop = Prop(registry, flags, handle, StartAsleep);

        Deliver(router, new[] { Event(PhysicsEventKind.Wake, handle) });

        Assert.True(prop.OnAwakened.FireCount == 1,
            $"a start-asleep prop fires OnAwakened when the solver wakes it (fired {prop.OnAwakened.FireCount})");
    }

    [Fact]
    public void OnAwakened_fires_only_on_the_first_wake()
    {
        var (registry, router, flags) = World();
        var handle = new EntityHandle(12);
        var prop = Prop(registry, flags, handle, StartAsleep);

        Deliver(router, new[]
        {
            Event(PhysicsEventKind.Wake, handle),
            Event(PhysicsEventKind.Sleep, handle),
            Event(PhysicsEventKind.Wake, handle),
        });

        Assert.True(prop.OnAwakened.FireCount == 1,
            $"stock clears SF_PHYSPROP_START_ASLEEP after the first OnAwakened (fired {prop.OnAwakened.FireCount})");
    }

    [Fact]
    public void A_prop_that_did_not_start_asleep_never_fires_OnAwakened()
    {
        var (registry, router, flags) = World();
        var handle = new EntityHandle(13);
        var prop = Prop(registry, flags, handle, 0);

        Deliver(router, new[] { Event(PhysicsEventKind.Wake, handle) });

        Assert.True(prop.OnAwakened.FireCount == 0, "OnAwakened belongs to start-asleep props only");
    }

    [Fact]
    public void A_wake_reaches_only_the_entity_it_names()
    {
        var (registry, router, flags) = World();
        var woken = new EntityHandle(14);
        var other = new EntityHandle(15);
        var wokenProp = Prop(registry, flags, woken, StartAsleep);
        var otherProp = Prop(registry, flags, other, StartAsleep);

        Deliver(router, new[] { Event(PhysicsEventKind.Wake, woken) });

        Assert.True(wokenProp.OnAwakened.FireCount == 1 && otherProp.OnAwakened.FireCount == 0,
            $"the wake goes to the entity the event names (woken {wokenProp.OnAwakened.FireCount}, other {otherProp.OnAwakened.FireCount})");
    }

    [Fact]
    public void An_event_with_no_entity_reaches_nobody()
    {
        var (registry, router, flags) = World();
        var handle = new EntityHandle(16);
        var prop = Prop(registry, flags, handle, StartAsleep);

        Deliver(router, new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake,
                                                Entity0 = PhysicsEvent.NoEntity,
                                                Entity1 = PhysicsEvent.NoEntity } });

        Assert.True(prop.OnAwakened.FireCount == 0, "an object the solver made for itself wakes nobody");
    }

    //-------------------------------------------------------------------------
    // B4b: OnOutOfWorld. Stock's CPhysicsProp::VPhysicsUpdate fires it when
    // CBaseEntity::IsInWorld fails (props.cpp:2932): any origin axis at or
    // past +/-16384 (MAX_COORD_INTEGER), or any velocity axis at or past
    // +/-2000. VPhysicsUpdate is called by PhysFrame for each object on the
    // solver's active list, every frame; native records one Update event per
    // active managed object into the ring, and the router delivers each.
    //-------------------------------------------------------------------------
    private static (PropPhysics Prop, EntityHandle Handle, PhysicsEventRouter Router)
        PropAt(Vector origin, Vector velocity, int index)
    {
        var (registry, router, flags) = World(out var engine);
        engine.Returns("Entities.GetOrigin", origin);
        engine.Returns("Entities.GetVelocity", velocity);
        var handle = new EntityHandle((uint)index);
        return (Prop(registry, flags, handle, 0), handle, router);
    }

    [Theory]
    [InlineData(16384f, 0f, 0f)]
    [InlineData(0f, -16384f, 0f)]
    [InlineData(0f, 0f, 20000f)]
    public void An_update_outside_the_world_fires_OnOutOfWorld(float x, float y, float z)
    {
        var (prop, handle, router) = PropAt(new Vector(x, y, z), Vector.Zero, 31);

        Deliver(router, new[] { Event(PhysicsEventKind.Update, handle) });

        Assert.True(prop.OnOutOfWorld.FireCount == 1,
            $"a prop updated at ({x}, {y}, {z}) is out of the world (fired {prop.OnOutOfWorld.FireCount})");
    }

    [Fact]
    public void An_update_faster_than_2000_on_an_axis_fires_OnOutOfWorld()
    {
        var (prop, handle, router) = PropAt(Vector.Zero, new Vector(0f, 0f, -2000f), 32);

        Deliver(router, new[] { Event(PhysicsEventKind.Update, handle) });

        Assert.True(prop.OnOutOfWorld.FireCount == 1,
            $"stock's IsInWorld fails at 2000 units/s on any axis (fired {prop.OnOutOfWorld.FireCount})");
    }

    [Fact]
    public void An_update_in_the_world_and_slower_does_not_fire_OnOutOfWorld()
    {
        var (prop, handle, router) = PropAt(new Vector(16383f, -16383f, 0f), new Vector(1999f, 0f, -1999f), 33);

        Deliver(router, new[] { Event(PhysicsEventKind.Update, handle) });

        Assert.True(prop.OnOutOfWorld.FireCount == 0, "inside the bounds and under the speed it is in the world");
    }

    [Fact]
    public void Every_update_out_of_the_world_fires_again()
    {
        var (prop, handle, router) = PropAt(new Vector(0f, 0f, 20000f), Vector.Zero, 34);

        Deliver(router, new[] { Event(PhysicsEventKind.Update, handle), Event(PhysicsEventKind.Update, handle) });

        Assert.True(prop.OnOutOfWorld.FireCount == 2 && router.UpdatesDelivered == 2,
            $"stock checks on every VPhysicsUpdate (fired {prop.OnOutOfWorld.FireCount}, delivered {router.UpdatesDelivered})");
    }

    [Fact]
    public void Wake_and_sleep_are_not_updates()
    {
        var (prop, handle, router) = PropAt(new Vector(0f, 0f, 20000f), Vector.Zero, 35);

        Deliver(router, new[] { Event(PhysicsEventKind.Wake, handle), Event(PhysicsEventKind.Sleep, handle) });

        Assert.True(prop.OnOutOfWorld.FireCount == 0 && router.UpdatesDelivered == 0,
            "only the solver's active-object update runs VPhysicsUpdate's checks");
    }

    //-------------------------------------------------------------------------
    // 8c: VPhysicsCollision per side. Stock's PostCollision calls
    // pEntities[i]->VPhysicsCollision( i, &event ) for each side; the world
    // side here is no managed entity and is called on nobody.
    //-------------------------------------------------------------------------
    private static PhysicsEvent Collision(uint entity0, uint entity1,
                                          uint flags = PhysicsEvent.FlagCollision) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = entity0, Entity1 = entity1,
        Speed = 250f, PointX = 1f, PointY = 2f, PointZ = 3f,
        Flags = flags,
    };

    private static (ScriptedEntityRegistry Registry, PhysicsEventRouter Router) ProbeWorld()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysicsEventRouterTests).Assembly);
        var services = Services();
        registry.Services = services;
        return (registry, new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(), services.Require<FluidTouches>(), services.Require<FrictionSounds>(), services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>()));
    }

    [Fact]
    public void A_collision_reaches_each_side_with_its_own_index()
    {
        var (registry, router) = ProbeWorld();
        registry.Spawn(new EntityHandle(51), "test_collision_probe");
        registry.Spawn(new EntityHandle(52), "test_collision_probe");
        var a = (CollisionProbe)registry.Get(new EntityHandle(51))!;
        var b = (CollisionProbe)registry.Get(new EntityHandle(52))!;

        Deliver(router, new[] { Collision(51, 52) });

        Assert.Equal(new[] { 0 }, a.Indices);
        Assert.Equal(new[] { 1 }, b.Indices);
    }

    [Fact]
    public void The_world_side_of_a_collision_reaches_nobody()
    {
        var (registry, router) = ProbeWorld();
        registry.Spawn(new EntityHandle(53), "test_collision_probe");
        var a = (CollisionProbe)registry.Get(new EntityHandle(53))!;

        Deliver(router, new[] { Collision(53, PhysicsEvent.NoEntity) });

        Assert.Equal(new[] { 0 }, a.Indices);
    }

    //-------------------------------------------------------------------------
    // 8e: stock's dispatch rule, whole. PostCollision calls VPhysicsCollision
    // only when the solver says it was a collision (pEvent->isCollision), never
    // for two shadows meeting, and VPhysicsShadowCollision on a side that is a
    // shadow when it says it was a shadow collision (isShadowCollision). An
    // entity hitting itself gets one non-shadow call.
    //-------------------------------------------------------------------------
    private static (ShadowProbe A, ShadowProbe B, PhysicsEventRouter Router) Shadows()
    {
        var (registry, router) = ProbeWorld();
        registry.Spawn(new EntityHandle(61), "test_shadow_probe");
        registry.Spawn(new EntityHandle(62), "test_shadow_probe");
        return ((ShadowProbe)registry.Get(new EntityHandle(61))!,
                (ShadowProbe)registry.Get(new EntityHandle(62))!, router);
    }

    // B5: CBasePlayer::VPhysicsUpdate's stress reaches the entity whose hull it is.
    [Fact]
    public void A_stress_event_reaches_its_entity()
    {
        var (a, b, router) = Shadows();

        Deliver(router, new[] { new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.Stress,
            Entity0 = 62, Entity1 = PhysicsEvent.NoEntity,
            Speed = 1234f,
        } });

        Assert.Empty(a.Stresses);
        Assert.Equal(new[] { 1234f }, b.Stresses);
    }

    // B5's live reading: how many hull stresses had a moving pusher in them.
    [Fact]
    public void Only_a_stress_with_a_moving_pusher_counts_as_felt()
    {
        var (_, b, router) = Shadows();

        Deliver(router, new[]
        {
            new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Stress, Entity0 = 62, Entity1 = PhysicsEvent.NoEntity },
            new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Stress, Entity0 = 62, Entity1 = PhysicsEvent.NoEntity,
                               Flags = PhysicsEvent.StressNonStatic, Speed = 30f },
        });

        Assert.Equal(2, b.Stresses.Count);
        Assert.Equal(1, router.StressFelt);
    }

    [Fact]
    public void A_contact_the_solver_did_not_call_a_collision_reaches_nobody()
    {
        var (a, b, router) = Shadows();

        Deliver(router, new[] { Collision(61, 62, flags: 0) });

        Assert.Empty(a.Collisions);
        Assert.Empty(b.Collisions);
    }

    [Fact]
    public void Two_shadows_meeting_is_no_collision()
    {
        var (a, b, router) = Shadows();
        const uint both = PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadow0 | PhysicsEvent.FlagShadow1;

        Deliver(router, new[] { Collision(61, 62, both) });

        Assert.Empty(a.Collisions);
        Assert.Empty(b.Collisions);
    }

    [Fact]
    public void A_shadow_collision_reaches_the_shadow_side_only()
    {
        var (a, b, router) = Shadows();
        const uint flags = PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadowCollision
                         | PhysicsEvent.FlagShadow1;

        Deliver(router, new[] { Collision(61, 62, flags) });

        Assert.Empty(a.ShadowCollisions);
        Assert.Equal(new[] { 1 }, b.ShadowCollisions);

        // ...and the impact itself still reaches both sides.
        Assert.Equal(new[] { 0 }, a.Collisions);
        Assert.Equal(new[] { 1 }, b.Collisions);
    }

    [Fact]
    public void A_shadow_side_hears_no_shadow_collision_the_solver_did_not_call_one()
    {
        var (_, b, router) = Shadows();

        Deliver(router, new[] { Collision(61, 62, PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadow1) });

        Assert.Empty(b.ShadowCollisions);
        Assert.Equal(new[] { 1 }, b.Collisions);
    }

    [Fact]
    public void An_entity_hitting_itself_gets_one_non_shadow_call()
    {
        var (a, _, router) = Shadows();
        const uint flags = PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadowCollision
                         | PhysicsEvent.FlagShadow0 | PhysicsEvent.FlagShadow1;

        Deliver(router, new[] { Collision(61, 61, flags & ~(PhysicsEvent.FlagShadow0 | PhysicsEvent.FlagShadow1)) });
        Deliver(router, new[] { Collision(61, 61, PhysicsEvent.FlagShadowCollision | PhysicsEvent.FlagShadow0) });

        Assert.Equal(new[] { 0 }, a.Collisions);
        Assert.Empty(a.ShadowCollisions);
    }

    [Fact]
    public void The_record_flag_bits_are_the_ones_the_native_enum_had()
    {
        // Pinned to physics_shell.h's SS_PHYSEVENT_FLAG_* until
        // plan_physics_bones_native_zero 2.3 deleted the ring; the values are
        // that enum's, hand-copied at bc21e4d46, so a record built before and
        // after reads the same.
        Assert.Equal(1u << 0, PhysicsEvent.FlagMoveable0);
        Assert.Equal(1u << 1, PhysicsEvent.FlagMoveable1);
        Assert.Equal(1u << 2, PhysicsEvent.FlagShadow0);
        Assert.Equal(1u << 3, PhysicsEvent.FlagShadow1);
        Assert.Equal(1u << 4, PhysicsEvent.FlagCollision);
        Assert.Equal(1u << 5, PhysicsEvent.FlagShadowCollision);
    }

    [Fact]
    public void A_flush_delivers_the_steps_impact_damage_after_the_collisions()
    {
        // UpdateDamageEvents runs once the step's collisions are all heard: a
        // hit queued while the drain routes is not delivered until the flush.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        registry.Services = services;
        engine.Returns("Entities.IsPlayer", false).Returns("Entities.GetSolidFlags", (SolidFlags)0);
        var impacts = services.Require<PhysicsDamageQueue>();
        var router = new PhysicsEventRouter(registry, impacts, services.Require<FluidTouches>(), services.Require<FrictionSounds>(), services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());

        var ev = Collision(70, 71);
        impacts.Add(new EntityHandle(71), new EntityHandle(70), 5f, DamageType.Crush, in ev, 1);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");

        router.Flush();

        Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(0, impacts.Count);
    }

    [Fact]
    public void Fluid_entry_and_exit_reach_the_fluid_touches()
    {
        // I2: FluidStart carries the object's material index; FluidEnd clears.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services();
        registry.Services = services;
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(),
                                            services.Require<FluidTouches>(), services.Require<FrictionSounds>(), services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());
        var fluid = services.Require<FluidTouches>();

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.FluidStart,
                                                Entity0 = 80, Entity1 = PhysicsEvent.NoEntity, SurfaceProps0 = 3 } });
        router.Flush();
        Assert.True(fluid.IsTouching(new EntityHandle(80)), "FluidStart marks the entity");

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.FluidEnd,
                                                Entity0 = 80, Entity1 = PhysicsEvent.NoEntity } });
        router.Flush();
        Assert.False(fluid.IsTouching(new EntityHandle(80)), "FluidEnd clears it");
    }

    [Fact]
    public void Friction_reaches_the_scrape_policy()
    {
        // D2: a Friction record is VPhysicsFriction's report for its object;
        // the policy asks for the surface's scrape sound when it is loud enough.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        registry.Services = services;
        engine.Returns("Sound.SurfaceScrapeSound", (string?)null);
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(),
                                            services.Require<FluidTouches>(), services.Require<FrictionSounds>(),
                                            services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Friction,
                                                Entity0 = 81, Entity1 = PhysicsEvent.NoEntity,
                                                Energy = 7750f, SurfaceProps0 = 1, SurfaceProps1 = 2 } });
        router.Flush();

        Assert.Contains(engine.Calls, c => c.Member == "Sound.SurfaceScrapeSound" && c.ArgAs<int>(0) == 1);
    }

    [Fact]
    public void A_shadow_update_asks_the_player_s_shadow_after_the_step()
    {
        // B7: SHADOW_UPDATE is PhysFrame's walk of the shadow entities; the
        // answer starts by probing the shadow the solver just moved.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        registry.Services = services;
        engine.Returns("Shadows.Probe", (PlayerShadowState?)null);
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(),
                                            services.Require<FluidTouches>(), services.Require<FrictionSounds>(),
                                            services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.ShadowUpdate,
                                                Entity0 = 1, Entity1 = PhysicsEvent.NoEntity } });
        router.Flush();

        Assert.Equal(new EntityHandle(1),
                     Assert.Single(engine.Calls, c => c.Member == "Shadows.Probe").Args[0]);
    }

    [Fact]
    public void A_broken_constraint_reaches_its_owner_even_with_a_world_end()
    {
        // A23: CPhysConstraintEvents::ConstraintBroken. The record names the
        // constraint (Object0) and its two ends; a world end has no entity, and
        // the owner must hear it all the same.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        registry.Services = services;
        registry.RegisterAll(typeof(ConstraintOwnerProbe).Assembly);
        registry.Spawn(new EntityHandle(9), "test_constraint_owner_probe");
        var owner = (ConstraintOwnerProbe)registry.Get(new EntityHandle(9))!;
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(),
                                            services.Require<FluidTouches>(), services.Require<FrictionSounds>(),
                                            services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.ConstraintBroken,
                                                Entity0 = PhysicsEvent.NoEntity, Entity1 = 2, Object0 = 0x0001_0003u } });
        router.Flush();

        Assert.Equal(new PhysicsId(0x0001_0003u), Assert.Single(owner.Heard));
    }

    [Fact]
    public void An_entity_solver_event_makes_a_solver_for_the_pair()
    {
        // A9: UpdatePenetrateEvents handed this pair to EntityPhysics_CreateSolver;
        // the first thing the solver does is take the pair out of collision.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        registry.Services = services;
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(),
                                            services.Require<FluidTouches>(), services.Require<FrictionSounds>(),
                                            services.Require<PlayerShadowRecovery>(), services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());
        engine.Returns("EntityPhysics.GetCollisionGroup", CollisionGroup.Debris);
        engine.Returns("EntityPhysics.PhysicsObject", (nint)0);
        var pairs = services.Require<EntityPairCollisions>();
        Assert.False(pairs.AreDisabled(new EntityHandle(1), new EntityHandle(2)));

        router.Route(new[] { new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.EntitySolver,
                                                Entity0 = 1, Entity1 = 2, Flags = PhysicsEvent.SolverDebris1 } });
        router.Flush();

        // The managed pair set (2.1), not a native hash, holds the pair now.
        Assert.True(pairs.AreDisabled(new EntityHandle(1), new EntityHandle(2)));
        Assert.Equal(1, router.Solver.Active);

        router.LevelShutdown();
        Assert.Equal(0, router.Solver.Active);
    }

    [Fact]
    public void A_class_with_only_the_old_hook_still_hears_the_impact()
    {
        // PhysMagnet's attach-on-impact overrides OnPhysicsCollision(other,
        // contact, speed, ...), which had no caller: the default
        // OnVPhysicsCollision forwards to it.
        var (registry, router) = ProbeWorld();
        registry.Spawn(new EntityHandle(54), "test_collision_legacy");
        registry.Spawn(new EntityHandle(55), "test_collision_legacy");
        var a = (LegacyCollisionProbe)registry.Get(new EntityHandle(54))!;

        Deliver(router, new[] { Collision(54, 55) });

        var heard = Assert.Single(a.Heard);
        Assert.Equal(new EntityHandle(55), heard.Other);
        Assert.Equal(250f, heard.Speed);
        Assert.Equal(new Vector(1f, 2f, 3f), heard.Contact);
    }

    // The dev API's "is it asleep" (debug-api lane, item 4): recorded from the
    // wake and sleep events the router delivers, never inferred.
    private static PhysicsEvent StateChange(PhysicsEventKind kind, uint entity)
        => new() { KindRaw = (uint)kind, Entity0 = entity, Entity1 = PhysicsEvent.NoEntity };

    [Fact]
    public void An_object_that_never_changed_state_reads_as_unknown()
    {
        var (_, router) = ProbeWorld();
        Assert.Null(router.IsAsleep(new EntityHandle(90)));
    }

    [Fact]
    public void A_delivered_sleep_reads_as_asleep()
    {
        var (_, router) = ProbeWorld();
        router.Route([StateChange(PhysicsEventKind.Sleep, 90)]);
        router.Flush();
        Assert.True(router.IsAsleep(new EntityHandle(90)));
    }

    [Fact]
    public void A_wake_after_a_sleep_reads_as_awake()
    {
        var (_, router) = ProbeWorld();
        router.Route([StateChange(PhysicsEventKind.Sleep, 90), StateChange(PhysicsEventKind.Wake, 90)]);
        router.Flush();
        Assert.False(router.IsAsleep(new EntityHandle(90)));
    }

    [Fact]
    public void Forgetting_sleep_states_makes_them_unknown_again()
    {
        var (_, router) = ProbeWorld();
        router.Route([StateChange(PhysicsEventKind.Sleep, 90)]);
        router.Flush();
        router.ForgetSleep();
        Assert.Null(router.IsAsleep(new EntityHandle(90)));
    }
}

[ScriptedEntity("test_collision_probe")]
public sealed class CollisionProbe : ScriptedEntity
{
    public List<int> Indices { get; } = new();
    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index) => Indices.Add(index);
}

[ScriptedEntity("test_shadow_probe")]
public sealed class ShadowProbe : ScriptedEntity
{
    public List<int> Collisions { get; } = new();
    public List<int> ShadowCollisions { get; } = new();
    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index) => Collisions.Add(index);
    public override void OnVPhysicsShadowCollision(in PhysicsEvent ev, int index) => ShadowCollisions.Add(index);
    public List<float> Stresses { get; } = new();
    public override void OnVPhysicsStress(in PhysicsEvent ev) => Stresses.Add(ev.Speed);
}

[ScriptedEntity("test_collision_legacy")]
public sealed class LegacyCollisionProbe : ScriptedEntity
{
    public List<(EntityHandle Other, Vector Contact, float Speed)> Heard { get; } = new();
    public override void OnPhysicsCollision(EntityHandle other, Vector contact, float speed,
                                            SurfaceMaterial surfaceMaterial, SurfaceMaterial modelMaterial)
        => Heard.Add((other, contact, speed));
}

[ScriptedEntity("test_constraint_owner_probe")]
public sealed class ConstraintOwnerProbe : ScriptedEntity
{
    public List<PhysicsId> Heard { get; } = new();
    public override bool OnPhysicsConstraintBroken(PhysicsId constraint)
    {
        Heard.Add(constraint);
        return true;
    }
}
