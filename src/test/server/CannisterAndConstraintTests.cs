using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arithmetic in physics_cannister and point_playermoveconstraint, which is
/// the part of each that does not need an engine.
///
/// Both entities spend most of their time asking the engine for something, and
/// none of that is tested here — a motion controller is only meaningful against
/// a real physics object, and whether a player is actually slowed happens inside
/// movement. What IS here is the handful of decisions that are pure functions of
/// the map's numbers, and every one of them is a decision that would be wrong
/// SILENTLY: a jet rate off by twenty looks like a jet, and a boundary tested
/// the wrong way round looks like a constraint until somebody stands exactly on
/// the line.
/// </summary>
public class CannisterAndConstraintTests
{

    //-------------------------------------------------------------------------
    // point_playermoveconstraint.
    //-------------------------------------------------------------------------

    [Fact]
    public void ConstraintBoundary()
    {
        var centre = new Vector(100f, 200f, 300f);

        Assert.True(!PointPlayerMoveConstraint.Outside(centre, 128f, centre), "somebody at the centre is held");

        Assert.True(!PointPlayerMoveConstraint.Outside(centre, 128f, centre + new Vector(64f, 0f, 0f)), "somebody well inside is held");

        Assert.True(PointPlayerMoveConstraint.Outside(centre, 128f, centre + new Vector(256f, 0f, 0f)), "somebody well outside has got out");

        // The edge itself, and it is the whole reason this is one function: the
        // test is strictly-greater, so standing exactly on the radius is still
        // being held. Flip it and TurnOn grabs a player the very next think
        // releases, which fires OnConstraintBroken every tenth of a second at
        // somebody who has not moved.
        Assert.True(!PointPlayerMoveConstraint.Outside(centre, 128f, centre + new Vector(128f, 0f, 0f)), "somebody exactly on the radius is still held");

        // Distance is a distance, not a coordinate difference — a player who is
        // 128 out along two axes at once is 181 away and gone.
        Assert.True(PointPlayerMoveConstraint.Outside(centre, 128f, centre + new Vector(128f, 128f, 0f)), "the radius is a sphere, not a box");

        // A radius of zero is how a map writes an entity that constrains nobody.
        // It must not read as "everybody is already at the centre and therefore
        // inside", which is what a less-than-or-equal test would say.
        Assert.True(PointPlayerMoveConstraint.Outside(centre, 0f, centre + new Vector(1f, 0f, 0f)), "a zero radius lets everybody out");
    }

    //-------------------------------------------------------------------------
    // physics_cannister.
    //-------------------------------------------------------------------------

    [Fact]
    public void ThrustDirection()
    {
        // No offset: down the cannister's own up axis, and a unit vector,
        // because it is multiplied by the thrust afterwards.
        //
        // -Z rather than +X, and the difference is the whole entity: a cannister
        // is a barrel whose body runs along Z, so +X was a push through its side
        // and a cannister lying down shoved itself into the floor. It is also
        // literally what stock does -- GetAttachment falls back to the entity's
        // own transform for a model with no "nozzle" attachment, which is every
        // model this mod has been pointed at.
        var straight = PhysicsCannister.ThrustDirection(Vector.Zero);

        Assert.True(straight.Z == -1f, "an unperturbed cannister thrusts down its own up axis");
        Assert.True(straight.X == 0f && straight.Y == 0f, "and nowhere else");

        // The map's Activate offset, already brought into the cannister's frame.
        // The 5:1 weighting is what keeps it a nudge: the cannister still mostly
        // goes where the gas leaves it.
        var tipped = PhysicsCannister.ThrustDirection(new Vector(0f, 0.1f, -0.25f));

        Assert.True(tipped.Y > 0f, "an offset tips the thrust to one side");
        Assert.True(tipped.Z < -0.9f, "but the nozzle axis still dominates");

        Assert.True(MathF.Abs(tipped.Length - 1f) < 0.0001f, "the direction stays a unit vector");
    }

    [Fact]
    public void ThrustFollowsTheCannister()
    {
        // A cannister standing upright. Its own up is the world's, so the gas
        // goes into the ground and it presses itself down -- which is what stock
        // does with an upright cannister too, and why a map lays them on their
        // sides or hangs them.
        var upright = Angles.Vectors(Vector.Zero);
        var down = PhysicsCannister.Rotate(new Vector(0f, 0f, -1f), upright);

        Assert.True(MathF.Abs(down.Z + 1f) < 0.0001f && MathF.Abs(down.X) < 0.0001f, "an upright cannister pushes straight down");

        // The same cannister tipped onto its side. Pitch is inverted in Source --
        // a negative pitch looks UP -- so a pitch of -90 stands the model's nose
        // at the sky and lays its up axis along world -X, and the push comes back
        // down that axis to +X. This is the case that matters: a cannister that
        // kept pushing at the floor after it had rolled over would be the bug the
        // re-sampling exists to avoid.
        var onItsSide = Angles.Vectors(new Vector(-90f, 0f, 0f));
        var along = PhysicsCannister.Rotate(new Vector(0f, 0f, -1f), onItsSide);

        Assert.True(along.X > 0.999f, "a cannister on its side pushes along the ground");

        Assert.True(MathF.Abs(along.Z) < 0.0001f, "and no longer pushes at the floor");

        // Rotating a direction must not change how hard it pushes.
        Assert.True(MathF.Abs(PhysicsCannister.Rotate(new Vector(3f, 4f, 0f), onItsSide).Length - 5f) < 0.001f, "rotating preserves the magnitude");

        // ToLocal is the other direction, and the pair has to compose back to
        // where it started: the offset a shot arrives with is a world vector, so
        // it goes through ToLocal on the way in and Rotate on the way out, every
        // tick, for as long as the fuel lasts.
        var world = new Vector(0.3f, -0.5f, 0.81f);
        var round = PhysicsCannister.Rotate(PhysicsCannister.ToLocal(world, onItsSide), onItsSide);

        Assert.True(MathF.Abs(round.X - world.X) < 0.0001f
              && MathF.Abs(round.Y - world.Y) < 0.0001f
              && MathF.Abs(round.Z - world.Z) < 0.0001f, "world to local and back is where it started");
    }

    [Fact]
    public void JetSettings()
    {
        var weak = PhysicsCannister.JetSettings(1000f);
        var strong = PhysicsCannister.JetSettings(5000f);
        var absurd = PhysicsCannister.JetSettings(50000f);

        // The spread rides the thrust with no ceiling on it, which is what makes
        // a hard cannister vent wider rather than merely faster.
        Assert.True(strong.SpreadSpeed > weak.SpreadSpeed, "a harder cannister vents wider");
        Assert.True(absurd.SpreadSpeed > strong.SpreadSpeed, "and keeps widening past the saturation point");

        // The speed rides a fraction that saturates at 5000, so it does not.
        Assert.True(absurd.Speed == strong.Speed, "the plume speed saturates");
        Assert.True(strong.Speed == 228f, "at stock's ceiling");
        Assert.True(PhysicsCannister.JetSettings(0f).Speed == 128f, "and starts from stock's floor");

        // Stock casts the fraction to int BEFORE multiplying by 20, so the term
        // is 0 for every thrust below the saturation point and 20 only at or
        // above it. Kept deliberately — see JetSettings — and pinned here so a
        // later tidy-up cannot quietly "fix" every cannister in every map.
        Assert.True(weak.Rate == 52f, "the rate below saturation is stock's bare 52");
        Assert.True(strong.Rate == 72f, "and only reaches 72 at saturation");

        Assert.True(weak.Length == 64f && absurd.Length == 64f, "the jet length is fixed");
    }
}
