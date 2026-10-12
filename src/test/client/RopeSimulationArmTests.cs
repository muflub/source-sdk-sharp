using SourceSharp.Abi;
using SourceSharp.Client.Ropes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The stock rope arms dm_lockdown's recording does not reach (its ropes do not
/// collide, are not constrained between their ends and hang between positions,
/// not entities). Each was ported rather than dropped; these pin what each does,
/// with the expectation worked from c_rope.cpp by hand.
/// </summary>
public class RopeSimulationArmTests
{
    private sealed class NoRandom : IRopeRandom
    {
        public float RandomFloat(float lo, float hi) => lo;
        public Vector RandomVector(float lo, float hi) => new(lo, lo, lo);
    }

    // A floor at z = 0: any hull trace that ends below it hits the plane.
    private sealed class Floor : IRopeWorld
    {
        public int Traces;

        public RopeTrace TraceHull(in Vector start, in Vector end, in Vector mins, in Vector maxs)
        {
            Traces++;
            if (end.Z - 2f >= 0f)
                return new RopeTrace(1f, false, false, default, 0f);
            if (start.Z - 2f < 0f)
                return new RopeTrace(0f, false, true, new Vector(0, 0, 1), 0f);
            return new RopeTrace(0.5f, false, false, new Vector(0, 0, 1), 0f);
        }

        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
        {
            boxColors.Clear();
            return new Vector(1, 1, 1);
        }
    }

    private sealed class Endpoints : IRopeEndpoints
    {
        public Vector Center = new(100, 0, 500);

        public bool Exists(EntityHandle entity) => true;

        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles)
        {
            origin = Center + new Vector(0, 0, attachment * 10);
            angles = default;
            return attachment == 2;
        }

        public Vector WorldSpaceCenter(EntityHandle entity) => Center;
        public Vector AbsAngles(EntityHandle entity) => default;
        public bool IsDormant(EntityHandle entity) => false;
        public int ModelIndex(EntityHandle entity) => 1;
    }

    private static RopeFrame Frame(float t) => new(1f / 60f, t, new Vector(0, 0, 10000), default);

    private static RopeSimulation Hung(RopeFlags extra, Vector start, Vector end, int length = 200, int slack = 300)
    {
        var sim = new RopeSimulation();
        sim.Create(1f, 5, RopeFlags.Simulate | extra, 1);
        sim.SetStartPos(start);
        sim.SetEndPos(end);
        sim.RopeLength = length;
        sim.Slack = slack;
        sim.RecomputeSprings();
        return sim;
    }

    [Fact]
    public void ACollidingRopeIsHeldAboveTheFloorAndCountsItsTouches()
    {
        // 500 units of cable between two points 10 above the floor: it would
        // sag far below z = 0 without the collision arm.
        var world = new Floor();
        RopeSimulation sim = Hung(RopeFlags.Collide, new Vector(0, 0, 10), new Vector(200, 0, 10));

        for (int i = 0; i < 60; i++)
            sim.Think(Frame(i / 60f), RopeSimSettings.Defaults, new NoRandom(), world);

        Assert.True(world.Traces > 0, "the collision arm never traced");
        for (int i = 0; i < sim.Physics.NodeCount; i++)
            Assert.True(sim.Physics.Node(i).Pos.Z > -0.5f, $"node {i} fell through the floor: {sim.Physics.Node(i).Pos.Z}");
        Assert.True(sim.LinksTouchingSomething > 0, "no link counted as touching the floor it rests on");
    }

    [Fact]
    public void WithoutTheCollideFlagTheSameRopeFallsThrough()
    {
        // The control for the fact above: rope_collide 1 traces only flagged ropes.
        var world = new Floor();
        RopeSimulation sim = Hung(RopeFlags.None, new Vector(0, 0, 10), new Vector(200, 0, 10));

        for (int i = 0; i < 60; i++)
            sim.Think(Frame(i / 60f), RopeSimSettings.Defaults, new NoRandom(), world);

        Assert.Equal(0, world.Traces);
        Assert.True(sim.Physics.Node(2).Pos.Z < -50f, $"an unflagged rope did not sag through: {sim.Physics.Node(2).Pos.Z}");
    }

    [Fact]
    public void RopeCollideTwoCollidesEveryRope()
    {
        var world = new Floor();
        RopeSimulation sim = Hung(RopeFlags.None, new Vector(0, 0, 10), new Vector(200, 0, 10));

        for (int i = 0; i < 10; i++)
            sim.Think(Frame(i / 60f), RopeSimSettings.Defaults with { Collide = 2 }, new NoRandom(), world);

        Assert.True(world.Traces > 0, "rope_collide 2 did not trace an unflagged rope");
    }

    [Fact]
    public void TheBetweenEndpointsConstraintKeepsNodesWithinTheSpanAlongItsChord()
    {
        // A slack rope strung VERTICALLY: gravity runs along the chord, so the
        // spare cable piles up below the lower end, past the span. The
        // constraint (ConstrainNode) clamps each node's projection on the chord
        // to half its length plus one; the loose control is the proof it bites.
        RopeSimulation loose = Hung(RopeFlags.None, new Vector(0, 0, 200), new Vector(0, 0, 0));
        RopeSimulation held = Hung(RopeFlags.None, new Vector(0, 0, 200), new Vector(0, 0, 0));
        held.ConstrainBetweenEndpoints = true;

        for (int i = 0; i < 30; i++)
        {
            foreach (RopeSimulation sim in new[] { loose, held })
            {
                sim.Think(Frame(i / 60f), RopeSimSettings.Defaults, new NoRandom(), new NoTraceWorld());
                sim.PrepareDraw(Frame(i / 60f), RopeSimSettings.Defaults, new NoRandom(), new NoTraceWorld());
            }
        }

        float worstHeld = 0, worstLoose = 0;
        for (int i = 1; i < 4; i++)
        {
            worstHeld = MathF.Max(worstHeld, MathF.Abs(held.Physics.Node(i).Pos.Z - 100f));
            worstLoose = MathF.Max(worstLoose, MathF.Abs(loose.Physics.Node(i).Pos.Z - 100f));
        }

        Assert.True(worstHeld <= 101.01f, $"a constrained node is {worstHeld} along the chord from the midpoint");
        Assert.True(worstLoose > 110f, $"the unconstrained control only reached {worstLoose}; the comparison proves nothing");
    }

    private sealed class NoTraceWorld : IRopeWorld
    {
        public RopeTrace TraceHull(in Vector start, in Vector end, in Vector mins, in Vector maxs)
            => throw new InvalidOperationException("this rope does not collide");

        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
        {
            boxColors.Clear();
            return new Vector(1, 1, 1);
        }
    }

    [Fact]
    public void AnEntityEndedRopeHangsFromTheEntityAndItsAttachment()
    {
        // CreateFromKeyValues' shape: both ends on one entity, two attachments.
        // Attachment 2 resolves; attachment 1 does not, and falls back to the
        // entity's centre as CalculateEndPointAttachment does.
        var ends = new Endpoints();
        var sim = new RopeSimulation();
        sim.CreateFromKeyValues(new EntityHandle(7), startAttachment: 1, endAttachment: 2, width: 0.5f, segments: 4,
                                gravity: false, length: 50, textureScale: null, textureHeight: 1);

        Assert.True((sim.Flags & RopeFlags.Simulate) != 0 && (sim.Flags & RopeFlags.NoGravity) != 0);
        Assert.Equal(RopeLock.StartPoint | RopeLock.EndPoint, sim.LockedPoints);
        Assert.Equal(0, sim.Slack);

        sim.Think(Frame(0), RopeSimSettings.Defaults, new NoRandom(), new NoTraceWorld(), ends);

        Assert.Equal(ends.Center, sim.Physics.Node(0).Pos);
        Assert.Equal(ends.Center + new Vector(0, 0, 20), sim.Physics.Node(3).Pos);
    }

    [Fact]
    public void ShakeRopeAddsAnUpwardImpulseThatFallsOffWithDistance()
    {
        RopeSimulation sim = Hung(RopeFlags.None, new Vector(0, 0, 0), new Vector(200, 0, 0));
        sim.Think(Frame(0), RopeSimSettings.Defaults, new NoRandom(), new NoTraceWorld());

        sim.ShakeRope(new Vector(0, 0, 0), 1000f, 100f);
        float near = sim.Impulse.Z;
        sim.Impulse = default;
        sim.ShakeRope(new Vector(5000, 0, 0), 1000f, 100f);

        Assert.True(near > 0f, "a shake at the rope gave it no impulse");
        Assert.Equal(0f, sim.Impulse.Z);
    }

    [Fact]
    public void TheSolidPassRemapsAlphaFromTheBackWidthAndClamps()
    {
        BeamSeg[] built = [new() { Width = 1f, Alpha = 0.7f }, new() { Width = 1f, Alpha = 0.7f }, new() { Width = 1f }];
        float[] back = [0.3f, 1.0f, 2.0f];
        var solid = new BeamSeg[3];

        Assert.True(RopeBuilder.SolidPass(built, back, 2.0f, 0.3f, 1f, 0f, 1f, solid));

        Assert.Equal(new[] { 0.3f, 1.0f, 2.0f }, solid.Select(s => s.Width));
        Assert.Equal(0f, solid[0].Alpha);
        Assert.Equal(1f, solid[1].Alpha);
        Assert.Equal(1f, solid[2].Alpha);   // RemapVal says 2.43; clamped.

        Assert.False(RopeBuilder.SolidPass(built, back, 0.3f, 0.3f, 1f, 0f, 1f, solid),
            "min alpha 0 and nothing wider than the min width is RenderSolidRopes' skip");
    }
}
