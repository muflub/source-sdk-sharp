using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An entity's one-object vphysics calls, made by managed through the object's
/// vtable (plan_physics_bones_native_zero, the last thin forwards). The facts
/// are read off stock and off the native forwards they replace at 775ae7f52:
/// <list type="bullet">
/// <item>which home a velocity lives in: stock's <c>CBaseEntity::GetVelocity</c>
/// (baseentity.cpp:4743) asks the physics object when the entity is simulated
/// and the entity otherwise; on this hierarchy an object exists only because
/// its entity simulates (igameentity.h's GetMoveType note), so the object's
/// presence is the question (native's shell_velocity_home.h until 2026-10-12, now this);</item>
/// <item>an impulse: stock's <c>CBaseEntity::ApplyAbsVelocityImpulse</c>
/// (baseentity_shared.cpp:2426) skips a zero impulse, clamps or discards an
/// unreasonable one by <c>CheckEntityVelocity</c> (:1105), and adds it to the
/// object as a VELOCITY (<c>AddVelocity( &amp;vecImpulse, NULL )</c>) -- not as
/// a force scaled by mass, which is what the native forward did;</item>
/// <item>the rest are shell_scripted.cpp / shell_entityapi.cpp /
/// shell_worldquery.cpp's bodies, call for call.</item>
/// </list>
/// </summary>
public class EntityPhysicsMotionTests
{
    private static readonly EntityHandle Crate = new(0x0003_0007u);
    private const nint Object = 0x4000;

    /// <summary>vphysics' object, every call logged in order, with a velocity and a frame it answers.</summary>
    private sealed class FakeCalls : IVPhysicsMotionCalls
    {
        public readonly List<string> Log = [];
        public float MassValue = 10f;
        public ushort Flags;
        public Vector Linear = new(1f, 2f, 3f);
        public Vector AngularLocal = new(4f, 5f, 6f);

        // A frame that is not the identity: local x is world y, so a missed
        // conversion is a different vector rather than the same one.
        public static Vector ToWorld(Vector v) => new(-v.Y, v.X, v.Z);
        public static Vector ToLocal(Vector v) => new(v.Y, -v.X, v.Z);

        public float Mass(nint o) { Log.Add("GetMass"); return MassValue; }
        public void SetMass(nint o, float mass) => Log.Add($"SetMass {mass}");
        public void EnableMotion(nint o, bool enable) => Log.Add($"EnableMotion {enable}");
        public void Wake(nint o) => Log.Add("Wake");
        public void Sleep(nint o) => Log.Add("Sleep");
        public ushort GameFlags(nint o) { Log.Add("GetGameFlags"); return Flags; }
        public void SetGameFlags(nint o, ushort flags) { Log.Add($"SetGameFlags {flags:x}"); Flags = flags; }
        public void RecheckCollisionFilter(nint o) => Log.Add("RecheckCollisionFilter");
        public void ApplyForceCenter(nint o, Vector force) => Log.Add($"ApplyForceCenter {force}");
        public void ApplyForceOffset(nint o, Vector force, Vector worldPoint) => Log.Add($"ApplyForceOffset {force} {worldPoint}");
        public void ApplyTorqueCenter(nint o, Vector torque) => Log.Add($"ApplyTorqueCenter {torque}");
        public void BecomeTrigger(nint o) => Log.Add("BecomeTrigger");
        public void RemoveTrigger(nint o) => Log.Add("RemoveTrigger");
        public (Vector Linear, Vector Angular) Velocity(nint o) { Log.Add("GetVelocity"); return (Linear, AngularLocal); }
        public void SetVelocity(nint o, Vector linear, Vector angular) => Log.Add($"SetVelocity {linear} {angular}");
        public void AddVelocity(nint o, Vector linear) => Log.Add($"AddVelocity {linear}");
        public Vector LocalToWorldVector(nint o, Vector local) => ToWorld(local);
        public Vector WorldToLocalVector(nint o, Vector world) => ToLocal(world);
        public void SetPosition(nint o, Vector origin, QAngle angles, bool isTeleport)
            => Log.Add($"SetPosition {origin} {angles} {isTeleport}");
    }

    /// <summary>The entity: its object (0 for none), its own velocity members and its facing.</summary>
    private sealed class FakeHost : IEntityPhysicsMotionHost
    {
        public nint Held = Object;
        public Vector Velocity = new(7f, 8f, 9f);
        public Vector Angular = new(10f, 11f, 12f);
        public Vector Facing = new(0f, 90f, 0f);
        public readonly List<string> Log = [];

        public nint PhysicsObject(EntityHandle entity) => Held;
        public Vector EntityVelocity(EntityHandle entity) { Log.Add("EntityVelocity"); return Velocity; }
        public void SetEntityVelocity(EntityHandle entity, Vector velocity) { Log.Add($"SetEntityVelocity {velocity}"); Velocity = velocity; }
        public Vector EntityAngularVelocity(EntityHandle entity) { Log.Add("EntityAngularVelocity"); return Angular; }
        public void SetEntityAngularVelocity(EntityHandle entity, Vector degreesPerSecond) { Log.Add($"SetEntityAngularVelocity {degreesPerSecond}"); Angular = degreesPerSecond; }
        public Vector Angles(EntityHandle entity) { Log.Add("Angles"); return Facing; }
        public void Warning(string message) => Log.Add($"Warning {message}");
    }

    private static (EntityPhysicsMotion m, FakeCalls v, FakeHost host) Make()
    {
        var v = new FakeCalls();
        var host = new FakeHost();
        return (new EntityPhysicsMotion(host, v), v, host);
    }

    // ---- which home an impulse goes to (EngineSide's routing) --------------

    private static readonly EntityHandle Walker = new(0x0004_0001u);

    /// <summary>The player movement route, answering for <see cref="Walker"/> alone and logging what it took.</summary>
    private sealed class WalkerMoves : IServerPlayerMovementRoute
    {
        public readonly List<string> Log = [];

        public bool ApplyVelocityImpulse(EntityHandle entity, Vector impulse, bool clearGround)
        {
            if (entity != Walker)
                return false;

            Log.Add($"impulse {impulse} {clearGround}");
            return true;
        }

        public bool PlayerState(EntityHandle entity, out MoveType moveType, out bool onGround)
            => throw new InvalidOperationException("not asked");

        public MoveType SetMoveType(EntityHandle player, MoveType moveType) => throw new InvalidOperationException("not asked");
        public void SetSlowMovement(EntityHandle player, bool slow) => throw new InvalidOperationException("not asked");
        public void SetObserverMode(EntityHandle player, int mode) => throw new InvalidOperationException("not asked");
        public bool AddBaseVelocity(EntityHandle entity, Vector velocity) => throw new InvalidOperationException("not asked");
        public void ViewPunch(Vector center, Vector punch, float radius, bool inAir) => throw new InvalidOperationException("not asked");
    }

    [Fact]
    public void APlayersImpulseGoesToItsMovementRecordNotItsShadow()
    {
        // A player's object is its shadow, which the next move overwrites
        // (plan_server_native_v2 T4g): the impulse used to land there and
        // vanish, which is why trigger_push lifted nobody.
        var (m, v, _) = Make();
        var moves = new WalkerMoves();

        EngineSide.ApplyVelocityImpulse(moves, m, Walker, new Vector(0f, 0f, 200f), clearGround: true);

        Assert.Equal([$"impulse {new Vector(0f, 0f, 200f)} True"], moves.Log);
        Assert.DoesNotContain(v.Log, c => c.StartsWith("AddVelocity", StringComparison.Ordinal));
    }

    [Fact]
    public void APropsImpulseGoesToItsObject()
    {
        var (m, v, _) = Make();
        var moves = new WalkerMoves();

        EngineSide.ApplyVelocityImpulse(moves, m, Crate, new Vector(0f, 0f, 200f), clearGround: true);

        Assert.Equal((0, $"AddVelocity {new Vector(0f, 0f, 200f)}"), (moves.Log.Count, v.Log.Single()));
    }

    [Fact]
    public void WithNoRouteEveryImpulseGoesToTheObject()
    {
        var (m, v, _) = Make();

        EngineSide.ApplyVelocityImpulse(null, m, Walker, new Vector(0f, 0f, 200f), clearGround: false);

        Assert.Equal($"AddVelocity {new Vector(0f, 0f, 200f)}", v.Log.Single());
    }

    // ---- which home a velocity lives in (stock's GetVelocity) --------------

    [Fact]
    public void ASimulatedEntitysVelocityIsTheSolvers()
    {
        var (m, v, host) = Make();

        Assert.Equal(v.Linear, m.Velocity(Crate));
        Assert.DoesNotContain("EntityVelocity", host.Log);
    }

    [Fact]
    public void AnEntityWithNoObjectAnswersItsOwnVelocity()
    {
        var (m, v, host) = Make();
        host.Held = 0;

        Assert.Equal(host.Velocity, m.Velocity(Crate));
        Assert.Empty(v.Log);
    }

    [Fact]
    public void SettingASimulatedVelocityKeepsTheSpinAndWakesIt()
    {
        var (m, v, _) = Make();

        m.SetVelocity(Crate, new Vector(0f, 0f, 50f));

        Assert.Equal(["GetVelocity", $"SetVelocity {new Vector(0f, 0f, 50f)} {v.AngularLocal}", "Wake"], v.Log);
    }

    /// <summary>
    /// Stock's velocity set on a simulated entity is <c>Teleport( NULL, NULL,
    /// &amp;vel )</c>: TeleportEntity (baseentity.cpp:4947) writes
    /// <c>SetAbsVelocity</c> AND the object's <c>SetVelocity</c> in the one
    /// call, and the movement's ground read is the member
    /// (<c>ground-&gt;GetAbsVelocity()</c>, gamemovement.cpp:3847/3945), so a
    /// rider sees the new velocity the same tick, asleep or not. The entity's
    /// member here is also the held value that ground read answers
    /// (CStandaloneEntity::SetVelocity), so it is written with the object; the
    /// physics frame's hand-over was the only writer, a tick late for an object
    /// that slept through the set (docs/todo.md "Lane C leftovers").
    /// </summary>
    [Fact]
    public void SettingASimulatedVelocityHoldsItForTheGroundReadAtOnce()
    {
        var (m, v, host) = Make();

        m.SetVelocity(Crate, new Vector(0f, 0f, 50f));

        Assert.Equal(new Vector(0f, 0f, 50f), host.Velocity);
        Assert.Equal([$"SetEntityVelocity {new Vector(0f, 0f, 50f)}"], host.Log);
        Assert.Contains($"SetVelocity {new Vector(0f, 0f, 50f)} {v.AngularLocal}", v.Log);
    }

    [Fact]
    public void SettingTheVelocityOfAnEntityWithNoObjectWritesTheEntity()
    {
        var (m, v, host) = Make();
        host.Held = 0;

        m.SetVelocity(Crate, new Vector(0f, 0f, 50f));

        Assert.Equal(new Vector(0f, 0f, 50f), host.Velocity);
        Assert.Empty(v.Log);
    }

    [Fact]
    public void ASimulatedSpinIsTheSolversTurnedIntoWorldAxes()
    {
        var (m, v, host) = Make();

        Assert.Equal(FakeCalls.ToWorld(v.AngularLocal), m.AngularVelocity(Crate));
        Assert.DoesNotContain("EntityAngularVelocity", host.Log);
    }

    [Fact]
    public void AnEntityWithNoObjectAnswersItsOwnSpin()
    {
        var (m, _, host) = Make();
        host.Held = 0;

        Assert.Equal(host.Angular, m.AngularVelocity(Crate));
    }

    [Fact]
    public void SettingASimulatedSpinTurnsItIntoTheObjectsAxesKeepsTheTravelAndWakesIt()
    {
        var (m, v, host) = Make();
        var spin = new Vector(0f, 180f, 0f);

        m.SetAngularVelocity(Crate, spin);

        Assert.Equal(["GetVelocity", $"SetVelocity {v.Linear} {FakeCalls.ToLocal(spin)}", "Wake"], v.Log);
        Assert.Empty(host.Log);
    }

    [Fact]
    public void SettingTheSpinOfAnEntityWithNoObjectWritesTheEntity()
    {
        var (m, v, host) = Make();
        host.Held = 0;

        m.SetAngularVelocity(Crate, new Vector(0f, 180f, 0f));

        Assert.Equal(new Vector(0f, 180f, 0f), host.Angular);
        Assert.Empty(v.Log);
    }

    // ---- an impulse (stock's ApplyAbsVelocityImpulse) -----------------------

    [Fact]
    public void AnImpulseIsAddedAsAVelocityAndNotScaledByMass()
    {
        var (m, v, _) = Make();
        v.MassValue = 250f;

        m.ApplyVelocityImpulse(Crate, new Vector(0f, 0f, 100f));

        Assert.Equal([$"AddVelocity {new Vector(0f, 0f, 100f)}"], v.Log);
    }

    [Fact]
    public void AZeroImpulseTouchesNothing()
    {
        var (m, v, _) = Make();

        m.ApplyVelocityImpulse(Crate, Vector.Zero);

        Assert.Empty(v.Log);
    }

    [Fact]
    public void ASuspiciousImpulseIsClampedToTheMaximumEntitySpeed()
    {
        var (m, v, _) = Make();

        // Outside k_flMaxEntitySpeed (4000) on one axis, under 100 times it in length.
        m.ApplyVelocityImpulse(Crate, new Vector(8000f, 0f, 0f));

        Assert.Equal([$"AddVelocity {new Vector(EntityPhysicsMotion.MaxEntitySpeed, 0f, 0f)}"], v.Log);
    }

    [Fact]
    public void AnImpulseJustInsideTheBoxIsLeftAlone()
    {
        var (m, v, _) = Make();

        m.ApplyVelocityImpulse(Crate, new Vector(3999f, -3999f, 3999f));

        Assert.Equal([$"AddVelocity {new Vector(3999f, -3999f, 3999f)}"], v.Log);
    }

    [Fact]
    public void AnAbsurdImpulseIsDiscardedAndSaid()
    {
        var (m, v, host) = Make();

        m.ApplyVelocityImpulse(Crate, new Vector(0f, 0f, 400_000f));

        Assert.Empty(v.Log);
        Assert.Contains(host.Log, l => l.StartsWith("Warning Discarding ApplyAbsVelocityImpulse", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImpulseOnAnEntityWithNoObjectWritesNothing()
    {
        var (m, v, host) = Make();
        host.Held = 0;

        m.ApplyVelocityImpulse(Crate, new Vector(0f, 0f, 100f));

        Assert.Empty(v.Log);
        Assert.Empty(host.Log);
    }

    // ---- the plain forwards ---------------------------------------------------

    [Fact]
    public void MassIsTheObjectsAndZeroWithoutOne()
    {
        var (m, v, host) = Make();
        v.MassValue = 42f;
        Assert.Equal(42f, m.Mass(Crate));

        host.Held = 0;
        Assert.Equal(0f, m.Mass(Crate));
    }

    [Fact]
    public void APositiveMassIsSet()
    {
        var (m, v, _) = Make();

        m.SetMass(Crate, 5f);

        Assert.Equal(["SetMass 5"], v.Log);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void AMassThatIsNotPositiveIsRefused(float mass)
    {
        var (m, v, _) = Make();

        m.SetMass(Crate, mass);

        Assert.Empty(v.Log);
    }

    [Fact]
    public void EnablingMotionWakesIt()
    {
        var (m, v, _) = Make();

        m.EnableMotion(Crate, true);

        Assert.Equal(["EnableMotion True", "Wake"], v.Log);
    }

    [Fact]
    public void DisablingMotionLeavesItAsleepOrAwake()
    {
        var (m, v, _) = Make();

        m.EnableMotion(Crate, false);

        Assert.Equal(["EnableMotion False"], v.Log);
    }

    [Fact]
    public void WakeAndSleepAreOneCallEach()
    {
        var (m, v, _) = Make();

        m.Wake(Crate);
        m.Sleep(Crate);

        Assert.Equal(["Wake", "Sleep"], v.Log);
    }

    [Fact]
    public void GameFlagsAreOredInAndTheFilterRechecked()
    {
        var (m, v, _) = Make();
        v.Flags = 0x0001;

        m.AddGameFlags(Crate, 0x0400);

        Assert.Equal(["GetGameFlags", "SetGameFlags 401", "RecheckCollisionFilter"], v.Log);
    }

    [Fact]
    public void ForcesAndTorqueWakeTheObject()
    {
        var (m, v, _) = Make();
        var f = new Vector(1f, 0f, 0f);
        var p = new Vector(0f, 0f, 9f);

        m.ApplyForceCenter(Crate, f);
        m.ApplyForceOffset(Crate, f, p);
        m.ApplyTorque(Crate, f);

        Assert.Equal([$"ApplyForceCenter {f}", "Wake", $"ApplyForceOffset {f} {p}", "Wake", $"ApplyTorqueCenter {f}", "Wake"], v.Log);
    }

    [Fact]
    public void TriggerIsBecomeAndRemoved()
    {
        var (m, v, _) = Make();

        m.BecomeTrigger(Crate, true);
        m.BecomeTrigger(Crate, false);

        Assert.Equal(["BecomeTrigger", "RemoveTrigger"], v.Log);
    }

    [Fact]
    public void EveryCallOnAnEntityWithNoObjectIsANoOp()
    {
        var (m, v, host) = Make();
        host.Held = 0;

        m.SetMass(Crate, 5f);
        m.EnableMotion(Crate, true);
        m.Wake(Crate);
        m.Sleep(Crate);
        m.AddGameFlags(Crate, 1);
        m.ApplyForceCenter(Crate, new Vector(1f, 0f, 0f));
        m.ApplyForceOffset(Crate, new Vector(1f, 0f, 0f), Vector.Zero);
        m.ApplyTorque(Crate, new Vector(1f, 0f, 0f));
        m.BecomeTrigger(Crate, true);
        m.TeleportObject(Crate, Vector.Zero, null, zeroVelocity: true);

        Assert.Empty(v.Log);
    }

    // ---- a teleport's solver half (native's EntityTeleport) -----------------

    [Fact]
    public void ATeleportMovesTheObjectAsAJumpAndWakesIt()
    {
        var (m, v, host) = Make();
        var to = new Vector(100f, 0f, 0f);

        m.TeleportObject(Crate, to, new Vector(0f, 45f, 0f), zeroVelocity: false);

        Assert.Equal([$"SetPosition {to} {new QAngle(0f, 45f, 0f)} True", "Wake"], v.Log);
        Assert.DoesNotContain("Angles", host.Log);
    }

    [Fact]
    public void ATeleportWithoutAnglesKeepsTheFacingTheEntityHas()
    {
        var (m, v, host) = Make();
        var to = new Vector(100f, 0f, 0f);

        m.TeleportObject(Crate, to, null, zeroVelocity: false);

        Assert.Equal($"SetPosition {to} {QAngle.FromVector(host.Facing)} True", v.Log[0]);
    }

    [Fact]
    public void ATeleportThatStopsItZeroesBothVelocitiesBeforeWaking()
    {
        var (m, v, _) = Make();

        m.TeleportObject(Crate, Vector.Zero, Vector.Zero, zeroVelocity: true);

        Assert.Equal(["SetPosition", $"SetVelocity {Vector.Zero} {Vector.Zero}", "Wake"],
                     v.Log.Select(l => l.StartsWith("SetPosition", StringComparison.Ordinal) ? "SetPosition" : l));
    }
}
