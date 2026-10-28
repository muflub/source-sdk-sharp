using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What a player's +USE does with a physics object or an item:
/// CBasePlayer::CanPickupObject (player.cpp:2814) in the object caps of a prop
/// and a physbox, their Use (OnPlayerUse, then PickupObject), and HL2MP's
/// gate, hl2mp_allow_pickup, off in stock (hl2mp_player.cpp:407). Found
/// beside stock (2026-10-26): with use ported, dm_lockdown's plug must fire
/// OnPlayerUse when used, as its map wires it.
/// </summary>
public class PlayerPickupTests
{
    private static readonly EntityHandle Player = new(0x0001_0001);
    private static readonly EntityHandle Obj = new(0x0002_0041);

    private sealed class Bodies : IVPhysicsGrabCalls
    {
        public float MassValue = 10f;
        public ushort Flags;
        public bool Moveable = true;
        public bool Hinged;
        public (Vector Position, QAngle Angles) Position(nint o) => (Vector.Zero, default);
        public Vector ShadowPosition(nint o) => Vector.Zero;
        public float Mass(nint o) => MassValue;
        public void SetMass(nint o, float mass) { }
        public float RotDamping(nint o) => 0f;
        public void SetRotDamping(nint o, float rotDamping) { }
        public void EnableDrag(nint o, bool enable) { }
        public void Wake(nint o) { }
        public bool IsMoveable(nint o) => Moveable;
        public bool IsHinged(nint o) => Hinged;
        public ushort GameFlags(nint o) => Flags;
        public void SetGameFlags(nint o, ushort flags) { }
        public int MaterialIndex(nint o) => 0;
        public (Vector Linear, Vector Angular) Velocity(nint o) => (Vector.Zero, Vector.Zero);
        public void SetVelocity(nint o, Vector linear, Vector angular) { }
        public void AddVelocity(nint o, Vector linear, Vector angular) { }
        public void SetVelocityInstantaneous(nint o, Vector linear) { }
        public void ApplyForceCenter(nint o, Vector force) { }
        public void ApplyForceOffset(nint o, Vector force, Vector worldPoint) { }
        public float ComputeShadowControl(nint o, in ShadowControlParams p, float secondsToArrival, float dt) => 0f;
        public bool HasContact(nint o) => false;
        public int Contacts(nint o, Span<GrabContact> into) => 0;
        public void ClearContactFriction(nint o) { }
    }

    private sealed class Directory : IEntityDirectory
    {
        public readonly Dictionary<EntityHandle, ScriptedEntity> Entities = [];
        public ScriptedEntity? Get(EntityHandle handle) => Entities.GetValueOrDefault(handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>A body the rules can read: a VPHYSICS mover with an object and a box.</summary>
    private static RecordingEngine BodyEngine(MoveType moveType = MoveType.VPhysics, Vector? size = null)
    {
        var engine = Fake();
        engine.Strict = false;
        Vector half = (size ?? new Vector(20f, 20f, 20f)) * 0.5f;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetEngineBlock", new byte[EngineBlock.SizeBytes])
              .Returns("EntityPhysics.PhysicsObject", (nint)0x7700)
              .Returns("Entities.GetBounds", (half * -1f, half));
        EngineBlockMoveType.Write(engine, Obj, moveType);
        return engine;
    }

    private static PlayerPickupRules Rules(RecordingEngine engine, Bodies bodies, bool allow = false,
                                           IEntityDirectory? directory = null)
    {
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(PlayerPickupRules).Assembly);
        convars.Get<Hl2mpAllowPickupConVar>()!.Bind(-1, "hl2mp_allow_pickup", allow ? "1" : "0");
        return new PlayerPickupRules(engine, engine, bodies, directory ?? new Directory(), convars, new RecordingLog());
    }

    //-------------------------------------------------------------------------
    // CanPickupObject
    //-------------------------------------------------------------------------

    [Fact]
    public void A_light_small_free_body_may_be_picked_up()
        => Assert.True(Rules(BodyEngine(), new Bodies()).CanPickupObject(Obj));

    [Fact]
    public void A_world_hinges_body_may_not_be_picked_up()
        => Assert.False(Rules(BodyEngine(), new Bodies { Hinged = true }).CanPickupObject(Obj));

    [Fact]
    public void A_body_heavier_than_thirty_five_kilos_may_not()
    {
        Assert.True(Rules(BodyEngine(), new Bodies { MassValue = 35f }).CanPickupObject(Obj), "the control: the limit itself");
        Assert.False(Rules(BodyEngine(), new Bodies { MassValue = 35.5f }).CanPickupObject(Obj));
    }

    [Fact]
    public void A_body_longer_than_128_on_any_side_may_not()
    {
        Assert.True(Rules(BodyEngine(size: new Vector(128f, 10f, 10f)), new Bodies()).CanPickupObject(Obj));
        Assert.False(Rules(BodyEngine(size: new Vector(10f, 10f, 129f)), new Bodies()).CanPickupObject(Obj));
    }

    [Fact]
    public void What_does_not_move_with_physics_may_not()
        => Assert.False(Rules(BodyEngine(MoveType.Push), new Bodies()).CanPickupObject(Obj));

    [Fact]
    public void A_body_flagged_no_player_pickup_may_not()
        => Assert.False(Rules(BodyEngine(), new Bodies { Flags = 0x0080 }).CanPickupObject(Obj));

    private static PropPhysics PinnedProp(int spawnFlags)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var propEngine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        propEngine.Strict = false;
        propEngine.Returns("EntityRender.GetModelName", "")
                  .Returns("EntityRender.GetModelKeyValueText", "")
                  .Returns("Entities.GetSpawnFlags", spawnFlags);
        registry.Spawn(Obj, EntityNames.PropPhysics);
        return (PropPhysics)registry.Get(Obj)!;
    }

    [Fact]
    public void A_pinned_body_may_only_if_the_gravity_gun_may_free_it()
    {
        var engine = BodyEngine();
        var directory = new Directory();
        Assert.False(Rules(engine, new Bodies { Moveable = false }, directory: directory).CanPickupObject(Obj),
                     "nothing on this side says it may be freed");

        // Pinned (8) alone: the gun may not free it.
        directory.Entities[Obj] = PinnedProp(8);
        Assert.False(Rules(engine, new Bodies { Moveable = false }, directory: directory).CanPickupObject(Obj));

        // SF_PHYSPROP_ENABLE_ON_PHYSCANNON answers the gun's question yes.
        directory.Entities[Obj] = PinnedProp(8 | 0x40);
        Assert.True(Rules(engine, new Bodies { Moveable = false }, directory: directory).CanPickupObject(Obj));
    }

    //-------------------------------------------------------------------------
    // PickupObject: HL2MP's gate
    //-------------------------------------------------------------------------

    [Fact]
    public void Stock_hl2mp_carries_nothing_on_a_use()
        => Assert.False(Rules(BodyEngine(), new Bodies()).PickupObject(Player, Obj));

    [Fact]
    public void With_hl2mp_allow_pickup_a_pickup_within_the_limits_would_start()
    {
        Assert.True(Rules(BodyEngine(), new Bodies(), allow: true).PickupObject(Player, Obj));
        Assert.False(Rules(BodyEngine(), new Bodies { MassValue = 80f }, allow: true).PickupObject(Player, Obj));
    }

    //-------------------------------------------------------------------------
    // The caps and the Use of the things a player may use
    //-------------------------------------------------------------------------

    private static (ScriptedEntity Entity, ScriptedEntityRegistry Registry) Spawned(string className, int spawnFlags,
                                                                                   bool pickable = true)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                         engine, engine, registry, engine, new DamageHitGroups()));
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "")
              .Returns("EntityRender.GetModelKeyValueText", "")
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.IsPlayer", true)
              .Returns("EntityPhysics.InitPhysics", true)
              .Returns("EntityPhysics.PhysicsObject", (nint)0x7700)
              .Returns("Entities.GetBounds", (new Vector(-10f, -10f, -10f), new Vector(10f, 10f, 10f)));
        if (pickable)
        {
            engine.Returns("Entities.GetEngineBlock", new byte[EngineBlock.SizeBytes]);
            EngineBlockMoveType.Write(engine, Obj, MoveType.VPhysics);
        }
        registry.Spawn(Obj, className);
        return (registry.Get(Obj)!, registry);
    }

    [Fact]
    public void A_prop_a_player_could_lift_is_usable_and_one_they_could_not_is_not()
    {
        Assert.Equal(UseCaps.ImpulseUse, Spawned(EntityNames.PropPhysics, 0).Entity.ObjectCaps);
        Assert.Equal(UseCaps.None, Spawned(EntityNames.PropPhysics, 0, pickable: false).Entity.ObjectCaps);
    }

    [Fact]
    public void A_prop_with_the_use_output_is_usable_however_heavy_and_radius_pickup_adds_the_radius()
    {
        Assert.Equal(UseCaps.ImpulseUse, Spawned(EntityNames.PropPhysics, 0x100, pickable: false).Entity.ObjectCaps);
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, Spawned(EntityNames.PropPhysics, 0x4000).Entity.ObjectCaps);
    }

    [Fact]
    public void A_player_using_a_prop_with_the_use_output_fires_OnPlayerUse()
    {
        var (with, withRegistry) = Spawned(EntityNames.PropPhysics, 0x100);
        var (without, withoutRegistry) = Spawned(EntityNames.PropPhysics, 0);

        Assert.True(withRegistry.Input(Obj, "Use", "", 0f, Player), "Use is claimed");
        withoutRegistry.Input(Obj, "Use", "", 0f, Player);

        Assert.Equal(1, ((PropPhysics)with).OnPlayerUse.FireCount);
        Assert.Equal(0, ((PropPhysics)without).OnPlayerUse.FireCount);
    }

    [Fact]
    public void A_physbox_is_usable_for_its_use_output_or_its_lift_unless_it_ignores_use()
    {
        Assert.Equal(UseCaps.ImpulseUse, Spawned(EntityNames.FuncPhysbox, 0).Entity.ObjectCaps);
        Assert.Equal(UseCaps.None, Spawned(EntityNames.FuncPhysbox, 0x2000).Entity.ObjectCaps);
        Assert.Equal(UseCaps.ImpulseUse, Spawned(EntityNames.FuncPhysbox, 0x80000 | 0x2000, pickable: false).Entity.ObjectCaps);
    }

    [Fact]
    public void An_item_is_usable_a_weapon_is_not_and_a_charger_is_used_while_held()
    {
        Assert.Equal(UseCaps.ImpulseUse, Spawned(EntityNames.ItemHealthkit, 0).Entity.ObjectCaps);
        Assert.Equal(UseCaps.None, Spawned(EntityNames.WeaponShotgun, 0).Entity.ObjectCaps);
        Assert.Equal(UseCaps.ContinuousUse, Spawned(EntityNames.ItemHealthcharger, 0).Entity.ObjectCaps);
        Assert.Equal(UseCaps.ContinuousUse, Spawned(EntityNames.ItemSuitcharger, 0).Entity.ObjectCaps);
    }
}
