using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity C7 (8f): impact damage is queued and delivered after
/// the step, and an inflictor whose victim it killed, removed or made
/// non-solid gets its pre-impact velocity back, blended by the mass ratio --
/// stock's CCollisionEvent::AddDamageEvent / UpdateDamageEvents /
/// RestoreDamageInflictorState.
/// </summary>
public class PhysicsDamageQueueTests
{
    private const uint Crate = 41;
    private const uint Victim = 4;
    private const uint Victim2 = 5;

    private static PhysicsEvent Impact(uint victim = Victim, float victimMass = 30f, float crateMass = 30f) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = Crate, Entity1 = victim,
        Mass0 = crateMass, Mass1 = victimMass,
        Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1 | PhysicsEvent.FlagCollision,
        PreVelocity0X = 600f,
        PostVelocity0X = 100f,
        PointX = 1f, PointY = 2f, PointZ = 3f,
    };

    /// <summary>
    /// A queue over the double. <paramref name="kills"/>: after the first
    /// TakeDamage the victims read as non-solid, which is a change of the
    /// state stock watches (FSOLID_NOT_SOLID), as a breaking prop's would be.
    /// </summary>
    private static (PhysicsDamageQueue Queue, RecordingEngine Engine) Queue(bool kills = true)
        => Queue(new EntityDirectory(new RecordingLog()), kills);

    private static (PhysicsDamageQueue Queue, RecordingEngine Engine) Queue(EntityDirectory directory, bool kills = true)
    {
        Services(out var engine);
        engine.Returns("Entities.GetVelocity", new Vector(100f, 0f, 0f))
              .Returns("Entities.GetAngularVelocity", Vector.Zero)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.IsPlayer", false)
              .ReturnsEachCall("Entities.GetSolidFlags",
                  () => kills && engine.Calls.Any(c => c.Member == "Damage.TakeDamage")
                        ? SolidFlags.NotSolid : (SolidFlags)0);

        return (new PhysicsDamageQueue(engine, engine, engine, directory), engine);
    }

    private sealed class Bystander : ScriptedEntity;

    private static void Hurt(PhysicsDamageQueue queue, PhysicsEvent ev, uint victim = Victim)
        => queue.Add(new EntityHandle(victim), new EntityHandle(Crate), 30f, DamageType.Crush, in ev, 1);

    [Fact]
    public void Nothing_is_delivered_until_the_queue_is()
    {
        var (queue, engine) = Queue();

        Hurt(queue, Impact());
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");

        queue.Deliver();
        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(new EntityHandle(Victim), hit.Args[0]);
        Assert.Equal(new EntityHandle(Crate), hit.Args[1]);
        Assert.Equal(30f, hit.Args[2]);
    }

    /// <summary>
    /// Stock's AddDamageEvent opens with <c>if ( pEntity->IsMarkedForDeletion() )
    /// return;</c> (physics.cpp): a victim removed earlier in the step takes no
    /// hit, so it cannot flip the state bits that restore the inflictor's
    /// velocity either. The live victim beside it, in the same step, still does.
    /// </summary>
    [Fact]
    public void A_victim_already_marked_for_removal_queues_no_damage()
    {
        var directory = new EntityDirectory(new RecordingLog());
        Assert.True(directory.Bind(new EntityHandle(Victim), new Bystander()));
        Assert.True(directory.Bind(new EntityHandle(Victim2), new Bystander()));
        Assert.Equal(RemovalMark.Marked, directory.Remove(new EntityHandle(Victim)));
        var (queue, engine) = Queue(directory);

        Hurt(queue, Impact(Victim), Victim);
        Hurt(queue, Impact(Victim2), Victim2);

        Assert.Equal(1, queue.Count);
        queue.Deliver();
        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(new EntityHandle(Victim2), hit.Args[0]);
    }

    [Fact]
    public void A_queue_delivered_once_is_empty()
    {
        var (queue, engine) = Queue();
        Hurt(queue, Impact());

        queue.Deliver();
        queue.Deliver();

        Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void A_kill_puts_the_inflictors_velocity_back_blended_by_mass()
    {
        // Equal masses: ratio 1, blend 0.5 -> 600 x 0.5 + 100 x 0.5 = 350.
        var (queue, engine) = Queue();
        Hurt(queue, Impact());

        queue.Deliver();

        var set = Assert.Single(engine.Calls, c => c.Member == "Entities.SetVelocity");
        Assert.Equal(new EntityHandle(Crate), set.Args[0]);
        Assert.Equal(new Vector(350f, 0f, 0f), set.Args[1]);
    }

    [Fact]
    public void A_hit_that_changes_nothing_leaves_the_inflictor_alone()
    {
        var (queue, engine) = Queue(kills: false);
        Hurt(queue, Impact());

        queue.Deliver();

        Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    [Fact]
    public void A_heavy_inflictor_gets_all_its_velocity_back()
    {
        var (queue, engine) = Queue();
        Hurt(queue, Impact(crateMass: 500f));

        queue.Deliver();

        Assert.Equal(new Vector(600f, 0f, 0f),
                     Assert.Single(engine.Calls, c => c.Member == "Entities.SetVelocity").Args[1]);
    }

    [Fact]
    public void A_light_inflictor_gets_nothing_back()
    {
        // 3 against 300: ratio clamps to 0.1, blend 0, and stock sets nothing.
        var (queue, engine) = Queue();
        Hurt(queue, Impact(victimMass: 300f, crateMass: 3f));

        queue.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    [Fact]
    public void A_motion_disabled_inflictor_is_not_restored()
    {
        var (queue, engine) = Queue();
        var ev = Impact();
        ev.Flags &= ~PhysicsEvent.FlagMoveable0;
        Hurt(queue, ev);

        queue.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    [Fact]
    public void The_inflictor_is_restored_once_for_two_kills()
    {
        var (queue, engine) = Queue();
        Hurt(queue, Impact());
        Hurt(queue, Impact(victim: Victim2), Victim2);

        queue.Deliver();

        Assert.Equal(2, engine.Calls.Count(c => c.Member == "Damage.TakeDamage"));
        Assert.Single(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    [Fact]
    public void The_heaviest_victim_of_the_step_sets_the_ratio()
    {
        // The 300 kg victim queued second still counts for the first kill:
        // stock's otherMassMax is gathered while the step queues, before any
        // delivery. 30 against 300 clamps to 0.1, blend 0, nothing set.
        var (queue, engine) = Queue();
        Hurt(queue, Impact());
        Hurt(queue, Impact(victim: Victim2, victimMass: 300f), Victim2);

        queue.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    [Fact]
    public void The_angular_velocity_is_blended_in_the_objects_frame()
    {
        // The record's pre-impact angular velocity is local; turned 90 degrees
        // in yaw, local +X is world +Y. Blend 0.5 against a still object.
        var (queue, engine) = Queue();
        engine.Returns("Entities.GetAngles", new Vector(0f, 90f, 0f));
        var ev = Impact();
        ev.PreAngularVelocity0X = 90f;
        Hurt(queue, ev);

        queue.Deliver();

        var spin = Assert.Single(engine.Calls, c => c.Member == "Entities.SetAngularVelocity").ArgAs<Vector>(1);
        Assert.True(MathF.Abs(spin.X) < 0.01f && MathF.Abs(spin.Y - 45f) < 0.01f && MathF.Abs(spin.Z) < 0.01f,
                    $"local +X spin turned to world +Y and halved: {spin}");
    }

    [Theory]
    [InlineData(30f, 30f, false, 0.5f)]
    [InlineData(15f, 30f, false, 0.2222f)]
    [InlineData(60f, 30f, false, 0.5556f)]
    [InlineData(3f, 300f, false, 0f)]
    [InlineData(300f, 3f, false, 1f)]
    [InlineData(500f, 30f, false, 1f)]
    [InlineData(30f, 30f, true, 1f)]
    [InlineData(30f, 0f, false, 1f)]
    public void The_blend_is_stocks_mass_ratio_remap(float inflictorMass, float otherMassMax, bool slice, float expected)
        => Assert.True(MathF.Abs(PhysicsDamageQueue.VelocityBlend(inflictorMass, otherMassMax, slice) - expected) < 0.001f,
                       $"blend for {inflictorMass} against {otherMassMax}, slice {slice}");
}
