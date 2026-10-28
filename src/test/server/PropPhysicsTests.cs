using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The one-off think a physics prop uses to finish spawning.
///
/// A prop cannot set its own mass or pin itself in place during OnSpawn: the
/// shell creates the physics object after OnSpawn returns, so for the whole of
/// it there is nothing to set them on. The work therefore happens on a think a
/// tick later, and that collides with the only subclass that wants a think of
/// its own — prop_physics_respawnable, which schedules one to come back.
///
/// If those were the same method, the settle tick would count as a respawn for
/// every respawnable prop in the map: it would unbreak, teleport back to its
/// spawn origin and re-enable motion, on the first tick, before anything had
/// happened to it. Which is why the settle is separate and why it is tested.
/// </summary>
public class PropPhysicsTests
{

    [Fact]
    public void SettleIsNotATimer()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Ignoring("Entities.GetOrigin");

        var handle = new EntityHandle(1);
        registry.Spawn(handle, EntityNames.PropPhysicsRespawnable);

        var prop = (PropPhysicsRespawnable)registry.Get(handle)!;

        // The first think after spawning is the settle, and it must not run the
        // subclass's timer.
        prop.OnThink();

        Assert.True(!prop.Respawned, "a respawnable prop's first think does not respawn it");

        // The second one is a real timer, which is what a respawn is.
        prop.OnThink();

        Assert.True(prop.Respawned, "the think after that does");
    }

    [Fact]
    public void SettleHappensOnce()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(2);
        registry.Spawn(handle, EntityNames.PropPhysics);

        var prop = (PropPhysics)registry.Get(handle)!;

        prop.OnThink();
        Assert.True(prop.HasSettled, "a plain prop settles on its first think");

        // A prop that settled twice would re-apply its mass scale to an already
        // scaled mass, so a scale of 2 would compound to 4.
        prop.OnThink();
        Assert.True(prop.SettleCount == 1, "and does not settle again");
    }

    //-------------------------------------------------------------------------
    // SF_PHYSPROP_START_ASLEEP (physics stock parity, B4). Stock's
    // CPhysicsProp::CreateVPhysics (props.cpp:2547) creates the object asleep
    // when the flag is set. The shell creates a prop's object after OnSpawn
    // (the model is applied then), so the prop is told the object exists
    // (OnPhysicsCreated, synchronously, before any simulation) and puts it to
    // sleep there.
    //-------------------------------------------------------------------------
    private static (ScriptedEntityRegistry Registry, EntityHandle Handle, RecordingEngine Engine) SpawnWith(int? spawnFlags)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;

        var handle = new EntityHandle(21);
        engine.Returns("Entities.GetSpawnFlags", spawnFlags ?? 0);
        // No model data: creation reads the prop's health from it.
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");
        registry.Spawn(handle, EntityNames.PropPhysics);
        return (registry, handle, engine);
    }

    [Fact]
    public void A_start_asleep_prop_puts_its_new_physics_object_to_sleep()
    {
        var (registry, handle, engine) = SpawnWith(1);

        registry.PhysicsCreated(handle);

        var sleeps = engine.Calls.Where(c => c.Member == "EntityPhysics.Sleep").ToList();
        Assert.True(sleeps.Count == 1 && Equals(sleeps[0].Args[0], handle),
            "a start-asleep prop sleeps its object the moment it is created: "
            + string.Join("; ", sleeps.Select(c => c.ToString())));
    }

    [Fact]
    public void A_prop_without_the_flag_leaves_its_new_object_awake()
    {
        var (registry, handle, engine) = SpawnWith(null);

        registry.PhysicsCreated(handle);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityPhysics.Sleep");
    }

    //-------------------------------------------------------------------------
    // Physics stock parity G: CCollisionEvent::ShouldFreezeObject crushes
    // frozen debris under a heavier moveable object (DMG_CRUSH, through the
    // physics damage queue). Stock's decision there: damage it if it can be
    // damaged; otherwise remove it if it is a prop gib (PropIsGib), since "it's
    // always safe to delete gibs"; otherwise an entity solver (deferred).
    //-------------------------------------------------------------------------
    private static (PropPhysics Prop, EntityHandle Handle, RecordingEngine Engine) Piece(bool gib)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;

        // No model data: health 0, so the prop cannot be damaged.
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");

        var handle = new EntityHandle(41);
        if (gib)
            registry.KeyValue(handle, EntityNames.PropPhysics, "gibfadetime", "5");
        registry.Spawn(handle, EntityNames.PropPhysics);
        return ((PropPhysics)registry.Get(handle)!, handle, engine);
    }

    [Fact]
    public void A_crushed_gib_that_cannot_be_damaged_is_removed()
    {
        var (prop, handle, engine) = Piece(gib: true);

        prop.OnDamaged(EntityHandle.None, 50f, DamageType.Crush);

        Assert.True(engine.Calls.Any(c => c.Member == "Entities.Remove" && Equals(c.Args[0], handle)),
            "stock removes a crushed prop gib that cannot be damaged");
    }

    [Fact]
    public void A_gib_that_is_shot_is_not_removed()
    {
        var (prop, _, engine) = Piece(gib: true);

        prop.OnDamaged(EntityHandle.None, 50f, DamageType.Bullet);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Remove");
    }

    [Fact]
    public void A_crushed_prop_that_is_not_a_gib_is_not_removed()
    {
        var (prop, _, engine) = Piece(gib: false);

        prop.OnDamaged(EntityHandle.None, 50f, DamageType.Crush);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Remove");
    }

    //-------------------------------------------------------------------------
    // L, the break sound. Stock's CBreakableProp::Break calls
    // PhysBreakSound( this, VPhysicsGetObject(), GetAbsOrigin() ) before the
    // explosion and the pieces (props.cpp:1673): the surfaceprop's break sound
    // queued into CPhysicsHook's batch.
    //-------------------------------------------------------------------------
    [Fact]
    public void A_breaking_prop_queues_its_break_sound()
    {
        var (prop, handle, engine) = Piece(gib: false);

        prop.OnKilled(EntityHandle.None);   // CBreakableProp::Event_Killed -> Break

        // Into managed's batch (plan_physics_bones_native_zero 2.3), which asks
        // for the prop's own object's origin and material.
        Assert.True(engine.Calls.Count(c => c.Member == "PhysicsSounds.BreakSource" && Equals(c.Args[0], handle)) == 1,
            "a breaking prop queues one break sound for its own object");
    }

    [Fact]
    public void A_prop_breaks_once_and_sounds_once()
    {
        var (prop, _, engine) = Piece(gib: false);

        prop.OnKilled(EntityHandle.None);   // CBreakableProp::Event_Killed -> Break
        prop.OnKilled(EntityHandle.None);   // CBreakableProp::Event_Killed -> Break

        Assert.Equal(1, engine.Calls.Count(c => c.Member == "PhysicsSounds.BreakSource"));
    }

    //-------------------------------------------------------------------------
    // 8d: CPhysicsProp::VPhysicsCollision (props.cpp:3026). Unless
    // SF_PHYSPROP_DONT_TAKE_PHYSICS_DAMAGE (0x2, cleared by prop data with
    // health), the prop takes CalculateDefaultPhysicsDamage with its
    // m_impactEnergyScale ("physdamagescale", default 0.1) and static damage
    // allowed, from the entity that hit it, through the damage pipeline.
    //-------------------------------------------------------------------------
    private static PhysicsEvent CrateAt400() => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = 41, Entity1 = 4,
        Mass0 = 10f, Mass1 = 50f,
        Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1,
        PreVelocity1X = 400f,
    };

    private static (PropPhysics Prop, EntityHandle Handle, RecordingEngine Engine, PhysicsDamageQueue Impacts) Struck(int spawnFlags = 0, string? damageScale = null)
        => Struck(out _, spawnFlags, damageScale);

    private static (PropPhysics Prop, EntityHandle Handle, RecordingEngine Engine, PhysicsDamageQueue Impacts) Struck(out FluidTouches fluid, int spawnFlags = 0, string? damageScale = null)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.IsPlayer", false);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);

        var handle = new EntityHandle(41);
        if (damageScale is not null)
            registry.KeyValue(handle, EntityNames.PropPhysics, "physdamagescale", damageScale);
        registry.Spawn(handle, EntityNames.PropPhysics);
        fluid = services.Require<FluidTouches>();
        return ((PropPhysics)registry.Get(handle)!, handle, engine, services.Require<PhysicsDamageQueue>());
    }

    [Fact]
    public void A_prop_hit_hard_takes_stocks_physics_damage()
    {
        // 8e6 x invMass 0.1 x energy 0.1 = 80,000 -> NPC table's 10.
        var (prop, handle, engine, impacts) = Struck();
        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(handle, hit.Args[0]);
        Assert.Equal(new EntityHandle(4), hit.Args[1]);
        Assert.Equal(10f, hit.Args[2]);
        Assert.Equal(DamageType.Crush, hit.Args[3]);
    }

    [Fact]
    public void Physdamagescale_scales_the_energy()
    {
        // energy 1.0: 800,000 -> 100.
        var (prop, _, engine, impacts) = Struck(damageScale: "1");
        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(100f, hit.Args[2]);
    }

    // A38: CBreakableProp's DEFINE_INPUT( m_impactEnergyScale, FIELD_FLOAT,
    // "physdamagescale" ) (props.cpp:775): a map can change the scale while the
    // prop lives, and the next impact uses it.
    [Fact]
    public void The_physdamagescale_input_sets_the_energy_scale()
    {
        var (prop, _, engine, impacts) = Struck();
        prop.OnPhysDamageScaleInput(1f);
        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(100f, hit.Args[2]);
    }

    [Fact]
    public void A_prop_that_does_not_take_physics_damage_takes_none()
    {
        var (prop, _, engine, impacts) = Struck(spawnFlags: 2);
        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void A_gentle_bump_does_no_damage()
    {
        // 30 in/s: 900 x 50 x 0.1 x 0.1 = 450 -> under the first step.
        var (prop, _, engine, impacts) = Struck();
        var ev = CrateAt400();
        ev.PreVelocity1X = 30f;
        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    //-------------------------------------------------------------------------
    // C8: forcetoenablemotion. Stock's CPhysicsProp starts a prop with the key
    // set pinned (CreateVPhysics), and in VPhysicsCollision lets it go once the
    // other object's mass x collision speed reaches the key -- unless the other
    // is a player and SF_PHYSPROP_PREVENT_PLAYER_TOUCH_ENABLE (0x400) is set --
    // pushing it with the other's momentum at the contact point
    // (ComputeEnablingImpulse) and enabling motion once.
    //-------------------------------------------------------------------------
    private const uint Other = 4;

    private static PhysicsEvent Knock(float speed = 100f) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = 41, Entity1 = Other,
        Mass0 = 20f, Mass1 = 50f,
        Flags = PhysicsEvent.FlagMoveable1 | PhysicsEvent.FlagCollision,
        Speed = speed,
        PreVelocity1X = 100f,
        PointX = 5f,
    };

    private static (PropPhysics Prop, RecordingEngine Engine) Pinned(float threshold = 1000f, int spawnFlags = 0,
                                                                     bool otherIsPlayer = false,
                                                                     params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.IsPlayer", otherIsPlayer);
        engine.Returns("Entities.GetWorldSpaceCenter", Vector.Zero);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);

        var handle = new EntityHandle(41);
        registry.KeyValue(handle, EntityNames.PropPhysics, "forcetoenablemotion",
                          threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.PropPhysics, key, value);
        registry.Spawn(handle, EntityNames.PropPhysics);
        return ((PropPhysics)registry.Get(handle)!, engine);
    }

    [Fact]
    public void TheGravityGunFreesAPinnedPropOnlyWhenItsMapSaysItMay()
    {
        var (pinned, pinnedEngine) = Pinned(spawnFlags: 8 | 0x40);
        var (bolted, boltedEngine) = Pinned(spawnFlags: 8);
        pinned.OnPhysicsCreated();
        bolted.OnPhysicsCreated();

        ((IPhysGunTarget)pinned).OnPhysGunPickup(new EntityHandle(1), PhysGunPickup.PickedUpByCannon);
        ((IPhysGunTarget)bolted).OnPhysGunPickup(new EntityHandle(1), PhysGunPickup.PickedUpByCannon);

        Assert.Equal((1, 0), (Enabled(pinnedEngine, true), Enabled(boltedEngine, true)));
        Assert.True(((IPhysGunTarget)pinned).EnablesOnPhyscannon && !((IPhysGunTarget)bolted).EnablesOnPhyscannon,
                    "SF_PHYSPROP_ENABLE_ON_PHYSCANNON is what the gun asks");
    }

    [Fact]
    public void ALaunchFromTheGunMarksThePropThrownAndADropDoesNot()
    {
        var (prop, engine) = Pinned();

        ((IPhysGunTarget)prop).OnPhysGunDrop(new EntityHandle(1), PhysGunDrop.DroppedByCannon);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityPhysics.AddGameFlags");

        ((IPhysGunTarget)prop).OnPhysGunDrop(new EntityHandle(1), PhysGunDrop.LaunchedByCannon);
        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.AddGameFlags" && c.ArgAs<uint>(1) == 0x0100u);
    }

    [Fact]
    public void APreventPickupPropRefusesTheGun()
    {
        var (prevented, _) = Pinned(spawnFlags: 0x200);
        var (plain, _) = Pinned(spawnFlags: 0);

        Assert.True(((IPhysGunTarget)prevented).PreventsPickup && !((IPhysGunTarget)plain).PreventsPickup,
                    "SF_PHYSPROP_PREVENT_PICKUP");
    }

    private static int Enabled(RecordingEngine engine, bool enable)
        => engine.Calls.Count(c => c.Member == "EntityPhysics.EnableMotion" && c.ArgAs<bool>(1) == enable);

    [Fact]
    public void A_prop_that_needs_a_force_to_move_starts_pinned()
    {
        var (prop, engine) = Pinned();

        prop.OnPhysicsCreated();   // the shell, once the object is made
        prop.OnThink();

        Assert.Equal(1, Enabled(engine, false));
    }

    /// <summary>
    /// A33: stock's CPhysicsProp::CreateVPhysics disables motion as it makes the
    /// object (props.cpp:2544-2622), so a pinned prop is never simulated. Ours
    /// did it in the first think, after one PhysFrame awake: a pinned crate
    /// placed slightly interpenetrating was shoved, or dropped, before it froze.
    /// </summary>
    [Fact]
    public void A_pinned_prop_is_pinned_as_its_object_is_made_not_a_think_later()
    {
        var (prop, engine) = Pinned(spawnFlags: 8);

        prop.OnPhysicsCreated();

        Assert.Equal(1, Enabled(engine, false));
    }

    //-------------------------------------------------------------------------
    // A38: the damage half of stock's prop motion rules. CBreakableProp::
    // OnTakeDamage refuses a blow under minhealthdmg before the base
    // (props.cpp:1045); CPhysicsProp::OnTakeDamage lets a damage FORCE past
    // forcetoenablemotion unpin it, and a blow that leaves health under
    // damagetoenablemotion unpin and push it (:3086-3152); CPhysicsProp::
    // CreateVPhysics pins a prop with damagetoenablemotion (:2585); and
    // CBreakableProp::Event_Killed unpins and pushes a pinned prop (:1170-1180).
    //-------------------------------------------------------------------------
    private static readonly Vector Hit = new(1f, 2f, 3f);

    private static int Pushes(RecordingEngine engine)
        => engine.Calls.Count(c => c.Member == "EntityPhysics.ApplyForceOffset");

    [Fact]
    public void A_blow_under_minhealthdmg_neither_pushes_nor_hurts()
    {
        var (prop, engine) = Pinned(threshold: 0f, keys: ("minhealthdmg", "50"));

        int taken = prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Hit, new Vector(500f, 0f, 0f));

        Assert.Equal(0, taken);
        Assert.Equal(0, Pushes(engine));
    }

    [Fact]
    public void A_damage_force_past_forcetoenablemotion_unpins_it()
    {
        var (prop, engine) = Pinned(threshold: 1000f);
        prop.OnPhysicsCreated();

        prop.OnDamagedAt(EntityHandle.None, 5f, DamageType.Bullet, Hit, new Vector(600f, 0f, 0f));
        Assert.Equal(0, Enabled(engine, true));   // the control: under the threshold

        prop.OnDamagedAt(EntityHandle.None, 5f, DamageType.Bullet, Hit, new Vector(1500f, 0f, 0f));
        Assert.Equal(1, Enabled(engine, true));
    }

    [Fact]
    public void Damagetoenablemotion_pins_the_prop_as_it_is_made()
    {
        var (prop, engine) = Pinned(threshold: 0f, keys: ("damagetoenablemotion", "50"));

        prop.OnPhysicsCreated();

        Assert.Equal(1, Enabled(engine, false));
    }

    [Fact]
    public void A_blow_that_leaves_health_under_damagetoenablemotion_unpins_and_pushes_again()
    {
        var (prop, engine) = Pinned(threshold: 0f, keys: new[] { ("damagetoenablemotion", "90"), ("health", "100") });
        engine.Returns("Entities.GetHealth", 100);
        prop.OnPhysicsCreated();

        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Hit, new Vector(100f, 0f, 0f));

        Assert.Equal(1, Enabled(engine, true));
        Assert.Equal(2, Pushes(engine));   // the base's, then VPhysicsTakeDamage after the unpin
    }

    [Fact]
    public void The_killing_blow_unpins_a_pinned_prop_and_pushes_it()
    {
        var (prop, engine) = Pinned(threshold: 0f, spawnFlags: 8);
        prop.OnPhysicsCreated();
        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Blast, Hit, new Vector(100f, 0f, 0f));
        int before = Pushes(engine);

        prop.OnKilled(EntityHandle.None);

        Assert.Equal(1, Enabled(engine, true));
        Assert.Equal(before + 1, Pushes(engine));
    }

    /// <summary>
    /// A crate's health is its model's prop_data, and stock's CBreakableProp
    /// spawns with it in m_iHealth. Ours put only a map "health" key into the
    /// engine's health, so a crate whose health came from its model spawned at
    /// 0 and native's TakeDamage (m_iHealth -= taken; <= 0 kills) broke it on
    /// the first point of damage: every breakable prop one-shot. Measured live
    /// on dm_lockdown at e041d922: 107 props at health 0, 47 broke from 5 damage.
    /// </summary>
    [Fact]
    public void A_prop_spawns_with_its_model_prop_data_health()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "models/props_junk/wood_crate001a.mdl");
        engine.Returns("EntityRender.GetModelKeyValueText", "mdlkeyvalue { prop_data { health 30 } }");

        registry.Spawn(new EntityHandle(43), EntityNames.PropPhysics);
        ((PropPhysics)registry.Get(new EntityHandle(43))!).OnPhysicsCreated();   // the shell, model applied

        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetHealth" && Equals(c.Args[1], 30));
    }

    [Fact]
    public void A_hard_enough_hit_knocks_it_loose_with_the_others_momentum()
    {
        // 100 in/s x 50 kg = 5000 against 1000.
        var (prop, engine) = Pinned();
        var ev = Knock();

        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(1, Enabled(engine, true));
        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.Wake");
        Assert.Equal(1, prop.OnMotionEnabled.FireCount);

        var push = Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.ApplyForceOffset");
        Assert.Equal(new Vector(5000f, 0f, 0f), push.Args[1]);
        Assert.Equal(new Vector(5f, 0f, 0f), push.Args[2]);
    }

    [Fact]
    public void A_soft_hit_leaves_it_pinned()
    {
        // 10 in/s x 50 kg = 500, short of 1000.
        var (prop, engine) = Pinned();
        var ev = Knock(speed: 10f);

        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(0, Enabled(engine, true));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityPhysics.ApplyForceOffset");
    }

    [Fact]
    public void It_is_knocked_loose_once()
    {
        var (prop, engine) = Pinned();
        var ev = Knock();

        prop.OnVPhysicsCollision(in ev, 0);
        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(1, Enabled(engine, true));
        Assert.Equal(1, prop.OnMotionEnabled.FireCount);
    }

    [Fact]
    public void A_player_cannot_knock_it_loose_when_the_map_says_so()
    {
        var (prop, engine) = Pinned(spawnFlags: 0x400, otherIsPlayer: true);
        var ev = Knock();

        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(0, Enabled(engine, true));
    }

    [Fact]
    public void A_player_can_knock_it_loose_otherwise()
    {
        var (prop, engine) = Pinned(otherIsPlayer: true);
        var ev = Knock();

        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(1, Enabled(engine, true));
    }

    [Fact]
    public void The_others_spin_adds_its_surface_speed_at_the_contact()
    {
        // 180 deg/s about local Z is pi rad/s; the contact is 5 units out along
        // +X from the other's centre, so w x r = (0, 5 pi, 0): the contact moves
        // at (100, 15.708, 0), times 50 kg.
        var (prop, engine) = Pinned();
        var ev = Knock();
        ev.PreAngularVelocity1Z = 180f;

        prop.OnVPhysicsCollision(in ev, 0);

        var push = Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.ApplyForceOffset").ArgAs<Vector>(1);
        Assert.True(MathF.Abs(push.X - 5000f) < 0.5f && MathF.Abs(push.Y - 785.398f) < 0.5f && MathF.Abs(push.Z) < 0.5f,
                    $"the contact point's momentum, spin included: {push}");
    }

    //-------------------------------------------------------------------------
    // I2: a floating prop feels an impact from anything but the world at three
    // times the energy (CalculatePhysicsImpactDamage's "extra oomph for
    // floating objects"). The test services' surfaces are all density 500,
    // lighter than water, so a crate touching fluid floats.
    //-------------------------------------------------------------------------
    [Fact]
    public void A_floating_crate_feels_a_hit_at_three_times_the_energy()
    {
        // 8e6 x invMass 0.1 x energy 3 = 2,400,000 -> the npc table's 1000^2 step, 500.
        var (prop, handle, engine, impacts) = Struck(out var fluid);
        fluid.Start(handle, 1);
        var ev = CrateAt400();

        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        Assert.Equal(500f, Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage").Args[2]);
    }

    [Fact]
    public void A_floating_crate_that_hits_the_world_gets_no_extra()
    {
        // The world as the other side: stock leaves the scale alone. Static
        // damage on: the crate's own loss, 300 in/s -> 9e4 x 10 x 0.1 x 0.1 = 9,000,
        // under the first step.
        var (prop, handle, engine, impacts) = Struck(out var fluid);
        fluid.Start(handle, 1);
        var ev = CrateAt400();
        ev.Entity1 = PhysicsEvent.NoEntity;
        ev.PreVelocity1X = 0f;
        ev.PreVelocity0X = 300f;

        prop.OnVPhysicsCollision(in ev, 0);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }
}
