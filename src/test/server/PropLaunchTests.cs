using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A prop's first collision after the gravity gun launched it (CPhysicsProp::
/// VPhysicsCollision). An onfirstimpact-break prop breaks by damaging itself
/// for its health. The impact deals ten times its damage unless a player threw
/// it by hand. Then FVPHYSICS_WAS_THROWN comes off (ClearFlagsThink). Found
/// 2026-10-28: dm_lockdown's explosive barrels (onfirstimpact break) went
/// through a punt and the wall it hit intact, and a launched prop stayed
/// "thrown" for the rest of its life.
/// </summary>
public class PropLaunchTests
{
    private static readonly EntityHandle Prop = new(41);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const string BarrelText = "mdlkeyvalue { prop_data { health 20 } physgun_interactions { onfirstimpact break } }";

    private sealed class Flags : IVPhysicsGrabCalls
    {
        public ushort Value = 0x0100;
        public (Vector Position, QAngle Angles) Position(nint o) => (Vector.Zero, default);
        public Vector ShadowPosition(nint o) => Vector.Zero;
        public float Mass(nint o) => 10f;
        public void SetMass(nint o, float mass) { }
        public float RotDamping(nint o) => 0f;
        public void SetRotDamping(nint o, float rotDamping) { }
        public void EnableDrag(nint o, bool enable) { }
        public void Wake(nint o) { }
        public bool IsMoveable(nint o) => true;
        public bool IsHinged(nint o) => false;
        public ushort GameFlags(nint o) => Value;
        public void SetGameFlags(nint o, ushort flags) => Value = flags;
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

    private static PhysicsEvent CrateAt400() => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = 41, Entity1 = 4,
        Mass0 = 10f, Mass1 = 50f,
        Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1,
        PreVelocity1X = 400f,
    };

    private static (PropPhysics Prop, RecordingEngine Engine, PhysicsDamageQueue Impacts, Flags Flags) Spawned(string text = "")
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        var flags = new Flags();
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance<IVPhysicsGrabCalls>(flags);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelKeyValueText", text)
              .Returns("Entities.GetHealth", 20)
              .Returns("EntityPhysics.PhysicsObject", (nint)0x1234);

        registry.Spawn(Prop, EntityNames.PropPhysics);
        var prop = (PropPhysics)registry.Get(Prop)!;
        prop.OnPhysicsCreated();
        return (prop, engine, services.Require<PhysicsDamageQueue>(), flags);
    }

    private static float ImpactDamage(RecordingEngine engine, PhysicsDamageQueue impacts)
    {
        impacts.Deliver();
        return engine.AllOf("Damage.TakeDamage").Where(c => c.ArgAs<EntityHandle>(1) != Prop).Select(c => c.ArgAs<float>(2)).Single();
    }

    [Fact]
    public void A_first_impact_break_prop_breaks_on_its_first_collision_after_a_launch()
    {
        var (prop, engine, _, _) = Spawned(BarrelText);
        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);

        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);

        var self = Assert.Single(engine.AllOf("Damage.TakeDamage"), c => c.ArgAs<EntityHandle>(1) == Prop);
        Assert.Equal((Prop, 20f, DamageType.Generic), (self.ArgAs<EntityHandle>(0), self.ArgAs<float>(2), self.ArgAs<DamageType>(3)));
    }

    [Fact]
    public void Only_the_first_collision_and_only_after_a_launch()
    {
        var (dropped, engine, _, _) = Spawned(BarrelText);
        ((IPhysGunTarget)dropped).OnPhysGunDrop(Player, PhysGunDrop.DroppedByCannon);
        var ev = CrateAt400();
        dropped.OnVPhysicsCollision(in ev, 0);
        Assert.DoesNotContain(engine.AllOf("Damage.TakeDamage"), c => c.ArgAs<EntityHandle>(1) == Prop);

        var (launched, again, _, _) = Spawned(BarrelText);
        ((IPhysGunTarget)launched).OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);
        launched.OnVPhysicsCollision(in ev, 0);
        launched.OnVPhysicsCollision(in ev, 0);
        Assert.Single(again.AllOf("Damage.TakeDamage"), c => c.ArgAs<EntityHandle>(1) == Prop);
    }

    /// <summary>"Take extra damage after we're punted by the physcannon": ten times, 100 for the crate's 10.</summary>
    [Fact]
    public void A_launched_props_first_impact_deals_ten_times()
    {
        var (prop, engine, impacts, _) = Spawned();
        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);

        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(100f, ImpactDamage(engine, impacts));
        Assert.DoesNotContain(engine.AllOf("Damage.TakeDamage"), c => c.ArgAs<EntityHandle>(1) == Prop);
    }

    [Fact]
    public void Not_when_a_player_threw_it_by_hand_first()
    {
        var (prop, engine, impacts, _) = Spawned();
        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.ThrownByPlayer);
        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);

        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(10f, ImpactDamage(engine, impacts));
    }

    [Fact]
    public void The_thrown_flag_comes_off_after_the_first_collision()
    {
        var (prop, _, _, flags) = Spawned();
        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);

        var ev = CrateAt400();
        prop.OnVPhysicsCollision(in ev, 0);
        prop.RunThink("PROP_CLEARFLAGS");

        Assert.Equal(0, flags.Value & 0x0100);
    }
}
