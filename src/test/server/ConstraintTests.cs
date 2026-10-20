using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The three constraints whose joints are built from a frame native has to
/// measure — slide, pulley and ragdoll.
///
/// Almost none of the arithmetic is here: projecting the travel limits onto the
/// slide axis, measuring the rope already strung, and expressing a joint frame
/// relative to two poses all read where the objects currently ARE, so they stay
/// in the shell. What C# decides is which key means what, which spawnflag bit
/// means what, and how nine loose numbers group onto three axes — and every one
/// of those is wrong SILENTLY. A misspelt key leaves a field at zero, and a
/// joint with no limits looks exactly like a joint that was built correctly and
/// then leaned on.
/// </summary>
public class ConstraintTests
{

    [Fact]
    public void MapKeys()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysSlideConstraint).Assembly);
        registry.Services = Services();

        // The shared five come from PhysicsConstraintEntity, so their presence
        // is really a check that the base's keys reach a derived class at all.
        // breaksound since plan_physics_managed A23 (stock's m_breakSound).
        var shared = new[] { "attach1", "attach2", "forcelimit", "torquelimit", "breaksound" };

        Assert.True(HasExactly(registry, EntityNames.PhysSlideconstraint,
                shared.Concat(new[] { "slideaxis", "slidefriction", "systemloadscale" })), "the slide reads its own keys and the shared ones");

        Assert.True(HasExactly(registry, EntityNames.PhysPulleyconstraint,
                shared.Concat(new[] { "position2", "addlength", "gearratio" })), "the pulley reads its own keys and the shared ones");

        Assert.True(HasExactly(registry, EntityNames.PhysRagdollconstraint,
                shared.Concat(new[]
                {
                    "xmin", "xmax", "ymin", "ymax", "zmin", "zmax",
                    "xfriction", "yfriction", "zfriction",
                })), "the ragdoll reads its own keys and the shared ones");

        // SetVelocity is the one input any of the three adds. It is also the
        // only reason the linear motor exists, so a map firing it at a slide
        // that never declared it would drive nothing and say nothing.
        Assert.True(Inputs(registry, EntityNames.PhysSlideconstraint).Contains("SetVelocity") &&
            !Inputs(registry, EntityNames.PhysPulleyconstraint).Contains("SetVelocity") &&
            !Inputs(registry, EntityNames.PhysRagdollconstraint).Contains("SetVelocity"), "only the slide takes SetVelocity");

        Assert.True(new[]
            {
                EntityNames.PhysSlideconstraint,
                EntityNames.PhysPulleyconstraint,
                EntityNames.PhysRagdollconstraint,
            }.All(name =>
            {
                var inputs = Inputs(registry, name);
                return inputs.Contains("Break") && inputs.Contains("TurnOn") && inputs.Contains("TurnOff");
            }), "all three inherit the shared inputs");
    }

    [Fact]
    public void SpawnFlagBits()
    {
        // Bit 2 is spent on something different by every phys_ constraint, so
        // there is no shared answer to borrow and no way to notice a borrowed
        // one: a slide given the pulley's meaning simply never stops at its
        // ends. Through the static forms, because SpawnFlags asks the ENGINE
        // and a test has none to ask.
        //
        // What each entity does with the word is now said by the TYPE it takes,
        // so the raw ints below are cast the way the entity itself casts what
        // the engine hands it. The casts are the point of the test: they are
        // the one place a bit from the wrong entity could still get in, and
        // each check says what that bit is not.
        const PhysSlideConstraint.SpawnFlag slideBit2 = PhysSlideConstraint.SpawnFlag.LimitEnds;
        const PhysPulleyConstraint.SpawnFlag pulleyBit2 = PhysPulleyConstraint.SpawnFlag.Rigid;
        const PhysRagdollConstraint.SpawnFlag ragdollBit2 = PhysRagdollConstraint.SpawnFlag.FreeMovement;

        Assert.True((int)slideBit2 == 2 && (int)pulleyBit2 == 2 && (int)ragdollBit2 == 2, "all three spend bit 2, on three different meanings");

        Assert.True(PhysSlideConstraint.LimitsEndsFor(slideBit2), "bit 2 limits the slide to its ends");
        Assert.True(!PhysSlideConstraint.LimitsEndsFor(0), "a slide without it runs off the rail");

        Assert.True(PhysPulleyConstraint.RigidFor(pulleyBit2), "bit 2 makes the pulley's rope rigid");
        Assert.True(!PhysPulleyConstraint.RigidFor(0), "a pulley without it is a plain rope");

        Assert.True(PhysRagdollConstraint.LimitAnglesOnlyFor(ragdollBit2), "bit 2 frees the ragdoll's translation");
        Assert.True(!PhysRagdollConstraint.LimitAnglesOnlyFor(0), "a ragdoll without it holds its ends together");

        // Bit 1 is the shared no-collide, which none of these three honour. It
        // must not be mistaken for any of the above — that would turn a mapper
        // asking two objects not to collide into a joint with different physics.
        const int noCollide = (int)ConstraintSpawnFlag.DisableCollision;

        Assert.True(noCollide == 1, "the no-collide bit is the family's, not an entity's");
        Assert.True(!PhysSlideConstraint.LimitsEndsFor((PhysSlideConstraint.SpawnFlag)noCollide), "the no-collide bit is not the slide's");
        Assert.True(!PhysPulleyConstraint.RigidFor((PhysPulleyConstraint.SpawnFlag)noCollide), "the no-collide bit is not the pulley's");
        Assert.True(!PhysRagdollConstraint.LimitAnglesOnlyFor((PhysRagdollConstraint.SpawnFlag)noCollide), "the no-collide bit is not the ragdoll's");

        // Start-inactive is bit 4, belongs to the family rather than to the
        // ragdoll, and reads the other way round from the rest: the flag's
        // absence is what makes a joint active.
        Assert.True(PhysRagdollConstraint.StartsActiveFor(0), "a ragdoll starts active by default");
        Assert.True(!PhysRagdollConstraint.StartsActiveFor(ConstraintSpawnFlag.StartInactive), "bit 4 starts the ragdoll inactive");
        Assert.True(!PhysRagdollConstraint.StartsActiveFor((ConstraintSpawnFlag)(2 | 4)) &&
            PhysRagdollConstraint.LimitAnglesOnlyFor((PhysRagdollConstraint.SpawnFlag)(2 | 4)), "start-inactive survives the other flags");
    }

    [Fact]
    public void RagdollAxes()
    {
        // A map names nine numbers singly because Hammer has no field for a
        // pair of angles, and the joint wants them grouped the other way: per
        // axis, not per bound. A transposition here limits the wrong axis, and
        // the result is a joint that swings freely where it should stop and
        // jams where it should swing — which reads as a mapping error.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysRagdollConstraint).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(41);
        var keys = new (string Key, string Value)[]
        {
            ("xmin", "-30"), ("xmax", "30"),
            ("ymin", "-15"), ("ymax", "45"),
            ("zmin", "-5"),  ("zmax", "5"),
            ("xfriction", "1"), ("yfriction", "2"), ("zfriction", "3"),
        };

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.PhysRagdollconstraint, key, value);

        registry.Spawn(handle, EntityNames.PhysRagdollconstraint);
        var joint = (PhysRagdollConstraint)registry.Get(handle)!;

        Assert.True(joint.MinAngles.X == -30f && joint.MinAngles.Y == -15f && joint.MinAngles.Z == -5f, "the minima group by axis");

        Assert.True(joint.MaxAngles.X == 30f && joint.MaxAngles.Y == 45f && joint.MaxAngles.Z == 5f, "the maxima group by axis");

        // Separate from the limits and in different units from the hinge's,
        // which is Valve's own inconsistency and is kept: a mapper's number has
        // to mean here what it means there.
        Assert.True(joint.Friction.X == 1f && joint.Friction.Y == 2f && joint.Friction.Z == 3f, "friction groups by axis too");
    }

    private static IReadOnlyList<string> Inputs(ScriptedEntityRegistry registry, string className)
        => registry.DescribeClasses().Single(c => c.ClassName == className).Inputs;

    private static bool HasExactly(ScriptedEntityRegistry registry, string className,
                                   IEnumerable<string> expected)
    {
        // Case-insensitively, which is how the engine matches a map key and so
        // the only comparison that means anything here. A key's declared
        // spelling is its property's — "SlideAxis" — and the map writes
        // "slideaxis"; both are the same key and neither is more correct.
        var actual = registry.DescribeClasses()
            .Single(c => c.ClassName == className)
            .Vars.Select(v => v.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return actual.SequenceEqual(expected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                                    StringComparer.OrdinalIgnoreCase);
    }
}
