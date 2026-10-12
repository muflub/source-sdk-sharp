using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The crushes a solver queued, in order.</summary>
public sealed class RecordingCrushes : IPhysicsCrushes
{
    public readonly List<(EntityHandle Victim, EntityHandle Attacker, float Damage, Vector Force, Vector Point)> Queued = new();

    public void AddCrush(EntityHandle victim, EntityHandle attacker, float damage, Vector force, Vector point)
        => Queued.Add((victim, attacker, damage, force, point));
}

/// <summary>
/// plan_physics_bones_native_zero 2.1: ShouldFreezeObject and the crush,
/// managed. Hand-derived from native's CCollisionEvent::ShouldFreezeObject
/// (physics.cpp 1275-1310 at dcae1517d) and stock's (game/server/physics.cpp:
/// 628-711, FindMaxContact and ShouldFreezeObject); find_max_contact_test's
/// cases are PhysicsMaxContactTests', which this walk feeds.
/// </summary>
public class PhysicsSolverFreezeTests
{
    private readonly OpenSolverWorld _world = new();
    private readonly FakeSolverObjects _objects = new();
    private readonly RecordingCrushes _crushes = new();
    private readonly PhysicsSolver _solver;

    private static readonly EntityHandle Debris = new(0x0001_0010u);
    private static readonly EntityHandle Heavy = new(0x0001_0011u);

    private static nint Obj(EntityHandle e) => OpenSolverWorld.ObjectOf(e);

    public PhysicsSolverFreezeTests()
    {
        _solver = new PhysicsSolver(_world, _objects, new PhysicsSolverRules(), new EntityPairCollisions(_world, _objects),
                                    new PhysicsPenetrationLedger(), new SolverClock(), _ => { }, _crushes, () => 0, _ => { });
        _objects.Owned(Obj(Debris), Debris).Mass = 10f;
        _objects.Owned(Obj(Heavy), Heavy).Mass = 200f;
        _world.Facts[Debris] = Group((int)EngineCollisionGroup.Debris);
    }

    private static SolverEntity Group(int group, int moveType = 6)
        => new(group, 6, 0, moveType, false, EntityHandle.None, EntityHandle.None);

    // The heavy object pressing down on the debris with this normal force.
    private void Press(float normalForce, nint other = 0)
        => _objects.At(Obj(Debris)).Contacts =
           [new SnapshotContact(other == 0 ? Obj(Heavy) : other, normalForce, new Vector(1f, 2f, 3f), new Vector(0f, 0f, -1f))];

    [Fact]
    public void Debris_something_heavy_rests_on_is_crushed_by_it()
    {
        Press(150f);   // past 10 x 10 kg

        Assert.True(_solver.ShouldFreezeObject(Obj(Debris)));

        var crush = Assert.Single(_crushes.Queued);
        Assert.Equal(Debris, crush.Victim);
        Assert.Equal(Heavy, crush.Attacker);
        Assert.Equal(15f, crush.Damage, 4);                      // a tenth of the force
        Assert.Equal(new Vector(0f, 0f, -150f), crush.Force);    // the normal times the force
        Assert.Equal(new Vector(1f, 2f, 3f), crush.Point);
        Assert.Equal(1, _solver.FreezeCrushes);
    }

    [Fact]
    public void Under_ten_times_its_mass_nothing_is_crushed()
    {
        Press(100f);
        Assert.True(_solver.ShouldFreezeObject(Obj(Debris)));
        Assert.Empty(_crushes.Queued);
    }

    [Fact]
    public void A_lighter_or_pinned_presser_does_not_crush()
    {
        _objects.At(Obj(Heavy)).Mass = 5f;
        Press(500f);
        _solver.ShouldFreezeObject(Obj(Debris));

        _objects.At(Obj(Heavy)).Mass = 200f;
        _objects.At(Obj(Heavy)).Moveable = false;
        _solver.ShouldFreezeObject(Obj(Debris));

        Assert.Empty(_crushes.Queued);
    }

    [Fact]
    public void Something_that_is_not_debris_is_frozen_and_not_crushed()
    {
        _world.Facts[Debris] = Group((int)EngineCollisionGroup.None);
        Press(500f);
        Assert.True(_solver.ShouldFreezeObject(Obj(Debris)));
        Assert.Empty(_crushes.Queued);
    }

    [Fact]
    public void Interactive_debris_and_debris_triggers_are_debris_too()
    {
        Press(500f);
        _world.Facts[Debris] = Group((int)EngineCollisionGroup.InteractiveDebris);
        _solver.ShouldFreezeObject(Obj(Debris));
        _world.Facts[Debris] = Group((int)EngineCollisionGroup.DebrisTrigger);
        _solver.ShouldFreezeObject(Obj(Debris));

        Assert.Equal(2, _crushes.Queued.Count);
    }

    [Fact]
    public void An_ai_mover_is_never_frozen()
    {
        _world.Facts[Debris] = Group((int)EngineCollisionGroup.Debris, moveType: PhysicsSolverRules.MoveTypePush);
        Press(500f);
        Assert.False(_solver.ShouldFreezeObject(Obj(Debris)));
        Assert.Empty(_crushes.Queued);
    }

    [Fact]
    public void The_world_and_an_unowned_object_are_frozen_and_never_crushed()
    {
        Assert.True(_solver.ShouldFreezeObject(OpenSolverWorld.World));
        Assert.True(_solver.ShouldFreezeObject(0x0777));
        Assert.Empty(_crushes.Queued);
        Assert.Equal(2, _solver.FreezeAsks);
    }

    [Fact]
    public void A_presser_nobody_owns_crushes_with_no_attacker()
    {
        const nint stray = 0x0778;
        _objects.At(stray).Mass = 200f;
        Press(500f, stray);

        _solver.ShouldFreezeObject(Obj(Debris));

        Assert.Equal(EntityHandle.None, Assert.Single(_crushes.Queued).Attacker);
    }

    [Fact]
    public void Of_many_contacts_past_the_threshold_the_last_walked_wins()
    {
        // Stock's quirk, kept: the running maximum is never raised.
        var c = new EntityHandle(0x0001_0012u);
        _objects.Owned(Obj(c), c).Mass = 200f;
        _objects.At(Obj(Debris)).Contacts =
        [
            new SnapshotContact(Obj(Heavy), 900f, Vector.Zero, new Vector(0f, 0f, -1f)),
            new SnapshotContact(Obj(c), 150f, Vector.Zero, new Vector(0f, 0f, -1f)),
        ];

        _solver.ShouldFreezeObject(Obj(Debris));

        Assert.Equal(c, Assert.Single(_crushes.Queued).Attacker);
    }

    [Fact]
    public void A_snapshot_longer_than_the_stack_buffer_is_walked_whole()
    {
        var many = new SnapshotContact[40];
        for (int i = 0; i < many.Length; i++)
            many[i] = new SnapshotContact(Obj(Heavy), i == 39 ? 500f : 1f, Vector.Zero, new Vector(0f, 0f, -1f));
        _objects.At(Obj(Debris)).Contacts = many;

        _solver.ShouldFreezeObject(Obj(Debris));

        Assert.Single(_crushes.Queued);
    }
}

/// <summary>The freeze crush in the step's damage queue (physics stock parity G).</summary>
public class PhysicsDamageQueueCrushTests
{
    [Fact]
    public void A_crush_is_delivered_after_the_step_with_its_force_and_point()
    {
        TestServices.Services(out var engine);
        engine.Returns("Entities.IsPlayer", false)
              .Returns("Entities.GetSolidFlags", (SolidFlags)0);
        var queue = new PhysicsDamageQueue(engine, engine, engine, new EntityDirectory(new RecordingLog()));

        queue.AddCrush(new EntityHandle(4), new EntityHandle(41), 15f, new Vector(0f, 0f, -150f), new Vector(1f, 2f, 3f));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.ApplyAt");

        queue.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(new EntityHandle(4), hit.Args[0]);
        Assert.Equal(new EntityHandle(41), hit.Args[1]);
        Assert.Equal(15f, hit.Args[2]);
        Assert.Equal(DamageType.Crush, hit.Args[3]);
        Assert.Equal(new Vector(0f, 0f, -150f), hit.Args[4]);
        Assert.Equal(new Vector(1f, 2f, 3f), hit.Args[5]);
        Assert.Equal(1, queue.Crushes);
    }

    [Fact]
    public void A_victim_already_removed_is_not_crushed()
    {
        TestServices.Services(out var engine);
        var directory = new EntityDirectory(new RecordingLog());
        var queue = new PhysicsDamageQueue(engine, engine, engine, directory);
        var gone = new EntityHandle(4);
        Assert.True(directory.Bind(gone, new Bystander()));
        Assert.Equal(RemovalMark.Marked, directory.Remove(gone));

        queue.AddCrush(gone, new EntityHandle(41), 15f, Vector.Zero, Vector.Zero);

        Assert.Equal(0, queue.Count);
    }

    private sealed class Bystander : ScriptedEntity;
}
