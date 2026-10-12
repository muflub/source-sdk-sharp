using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where a broken prop's pieces go and how hard they leave.
///
/// <para>
/// The arithmetic half of <c>PropBreakableCreateAll</c>, which is the half
/// nobody can check by looking: debris that erupts from a point twenty units
/// off, or comes out at half speed, still looks like debris. Every number here
/// is one a player sees and cannot name.
/// </para>
/// </summary>
public class PropBreakPlanTests
{
    private const float Tolerance = 0.001f;

    /// <summary>A prop standing at the origin, facing down the X axis, still.</summary>
    private static BreakContext Still(Vector origin = default, Vector angles = default)
        => new(Origin: origin,
               Angles: angles,
               Velocity: Vector.Zero,
               AngularVelocity: Vector.Zero,
               DefaultBurstScale: PropBreakPlan.DefaultBurstScale,
               DefaultCollisionGroup: CollisionGroup.Interactive,
               SpawnFlags: 0);

    private static BreakPiece Piece(Vector offset, float burst = 0f)
        => new()
        {
            Offset = offset,
            BurstScale = burst,
            CollisionGroup = (int)CollisionGroup.Debris,
            FadeTime = 20f,
            Health = 1f,
        };

    [Fact]
    public void APieceLandsAtItsOffsetFromAnUnrotatedProp()
    {
        var placement = PropBreakPlan.Place("models/plank.mdl",
                                            Piece(new Vector(10f, 0f, 4f)),
                                            Still(new Vector(100f, 200f, 300f)));

        Assert.True(MathF.Abs(placement.Origin.X - 110f) < Tolerance
                 && MathF.Abs(placement.Origin.Y - 200f) < Tolerance
                 && MathF.Abs(placement.Origin.Z - 304f) < Tolerance,
                    "a piece of an unrotated prop lands at the prop's origin plus its own offset");
    }

    /// <summary>
    /// The offset is ROTATED by the prop's facing, not merely added.
    ///
    /// <para>
    /// This is the difference between VectorTransform and a vector add, and it
    /// is invisible at yaw zero — which is the yaw a test written without
    /// thinking uses. A crate turned ninety degrees would come apart into planks
    /// arranged as though it had not been.
    /// </para>
    /// </summary>
    [Fact]
    public void TheOffsetTurnsWithTheProp()
    {
        var placement = PropBreakPlan.Place("models/plank.mdl",
                                            Piece(new Vector(10f, 0f, 0f)),
                                            Still(new Vector(100f, 200f, 300f),
                                                  new Vector(0f, 90f, 0f)));

        // Ninety degrees of yaw takes +X to +Y.
        Assert.True(MathF.Abs(placement.Origin.X - 100f) < Tolerance
                 && MathF.Abs(placement.Origin.Y - 210f) < Tolerance
                 && MathF.Abs(placement.Origin.Z - 300f) < Tolerance,
                    "a piece of a prop turned ninety degrees comes out ninety degrees round");
    }

    [Fact]
    public void EveryPieceWearsTheWholePropsFacing()
    {
        var angles = new Vector(15f, 90f, 45f);

        var placement = PropBreakPlan.Place("models/plank.mdl",
                                            Piece(new Vector(10f, 0f, 0f)),
                                            Still(Vector.Zero, angles));

        Assert.True(placement.Angles.Equals(angles),
                    "a piece is created facing the way the prop was, because that is how it was modelled");
    }

    /// <summary>
    /// The burst is a DIRECTION times a scale, not the offset times a scale.
    ///
    /// <para>
    /// Stock normalises. A version that did not would throw a piece sitting 100
    /// units out a hundred times harder than one sitting 1 unit out, so a large
    /// prop would fire its outer pieces across the map and drop its inner ones
    /// at its feet.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBurstIsNormalisedBeforeItIsScaled()
    {
        var near = PropBreakPlan.BurstImpulse(new Vector(1f, 0f, 0f), Vector.Zero, 100f);
        var far  = PropBreakPlan.BurstImpulse(new Vector(400f, 0f, 0f), Vector.Zero, 100f);

        Assert.True(MathF.Abs(near.X - 100f) < Tolerance, "a piece one unit out leaves at the burst scale");
        Assert.True(MathF.Abs(far.X - 100f) < Tolerance, "and so does one four hundred units out");
    }

    [Fact]
    public void TheBurstPointsAwayFromWhereThePropWas()
    {
        var impulse = PropBreakPlan.BurstImpulse(new Vector(0f, -3f, 4f),
                                                 Vector.Zero,
                                                 10f);

        Assert.True(MathF.Abs(impulse.Y + 6f) < Tolerance
                 && MathF.Abs(impulse.Z - 8f) < Tolerance,
                    "the burst runs from the break point out through the piece");
    }

    /// <summary>
    /// A piece exactly on the break point gets no burst rather than a
    /// direction nobody chose.
    ///
    /// <para>
    /// mathlib's VectorNormalize of a zero vector is a zero vector, and dividing
    /// by a zero length in C# is an infinity that becomes NaN the moment it is
    /// multiplied — which a physics object accepts and then never recovers from.
    /// </para>
    /// </summary>
    [Fact]
    public void APieceAtTheBreakPointIsNotThrownAnywhere()
    {
        var impulse = PropBreakPlan.BurstImpulse(Vector.Zero, Vector.Zero, 100f);

        Assert.True(impulse.LengthSquared == 0f,
                    "a piece with no direction to leave in leaves on the prop's velocity alone");
        Assert.True(!float.IsNaN(impulse.X) && !float.IsNaN(impulse.Y) && !float.IsNaN(impulse.Z),
                    "and gets a real vector rather than a NaN the solver would take");
    }

    [Fact]
    public void AZeroBurstScaleThrowsNothing()
    {
        var impulse = PropBreakPlan.BurstImpulse(new Vector(10f, 0f, 0f), Vector.Zero, 0f);

        Assert.True(impulse.LengthSquared == 0f,
                    "a block that asked for no burst gets none");
    }

    /// <summary>
    /// Stock's rule, verbatim: "don't automatically make anything
    /// COLLISION_GROUP_NONE or it will collide with debris being ejected by
    /// breaking".
    /// </summary>
    [Fact]
    public void APropInTheNeutralGroupHandsItsPiecesInteractive()
    {
        Assert.True(PropBreakPlan.GroupForPieces(CollisionGroup.None) == CollisionGroup.Interactive,
                    "a prop in the neutral group does not hand that group to its own debris");
    }

    [Fact]
    public void APropWithAGroupOfItsOwnKeepsIt()
    {
        Assert.True(PropBreakPlan.GroupForPieces(CollisionGroup.Debris) == CollisionGroup.Debris,
                    "a prop that is already debris breaks into debris");
    }

    /// <summary>
    /// The group comes from the BLOCK, which is where a modeller wrote it.
    ///
    /// <para>
    /// Native has already applied the caller's default to a block that wrote
    /// neither <c>debris</c> nor <c>fadetime</c>, so what arrives is the answer
    /// and not a hint. A planner that re-applied the default here would
    /// overwrite the modeller's choice with the prop's.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePiecesOwnGroupWinsOverThePropsDefault()
    {
        var piece = Piece(new Vector(1f, 0f, 0f));
        piece.CollisionGroup = (int)CollisionGroup.Debris;

        var context = Still() with { DefaultCollisionGroup = CollisionGroup.Interactive };

        var placement = PropBreakPlan.Place("models/plank.mdl", piece, context);

        Assert.True(placement.CollisionGroup == CollisionGroup.Debris,
                    "a block that asked for debris gets debris, whatever the prop is");
    }

    [Fact]
    public void ThePieceCarriesWhatThePropWasDoing()
    {
        var context = Still() with
        {
            Velocity = new Vector(0f, 0f, 250f),
            AngularVelocity = new Vector(90f, 0f, 0f),
        };

        var placement = PropBreakPlan.Place("models/plank.mdl", Piece(Vector.Zero), context);

        Assert.True(placement.Velocity.Equals(context.Velocity),
                    "a piece of a prop that was moving is moving");
        Assert.True(placement.AngularVelocity.Equals(context.AngularVelocity),
                    "and a piece of a prop that was spinning is spinning");
    }

    [Fact]
    public void TheBlocksOwnFlagsSurvive()
    {
        var piece = Piece(new Vector(1f, 0f, 0f));
        piece.IsMotionDisabled = 1;
        piece.IsRagdoll = 1;
        piece.FadeTime = 7.5f;

        var placement = PropBreakPlan.Place("models/plank.mdl", piece, Still());

        Assert.True(placement.MotionDisabled, "a piece the modeller pinned is pinned");
        Assert.True(placement.IsRagdoll, "a piece the modeller made a ragdoll says so");
        Assert.True(placement.FadeTime == 7.5f, "and lasts as long as the block said");
    }

    /// <summary>
    /// The burst is worked out from where the piece ENDS UP, not from its
    /// untransformed offset.
    ///
    /// <para>
    /// The two agree only for a prop at the world origin, which is where a
    /// careless test would put it. For a prop anywhere else, using the raw
    /// offset would point every burst back toward the map origin.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBurstIsMeasuredFromThePropAndNotFromTheWorld()
    {
        var placement = PropBreakPlan.Place("models/plank.mdl",
                                            Piece(new Vector(0f, 0f, 8f), burst: 100f),
                                            Still(new Vector(500f, -500f, 64f)));

        Assert.True(MathF.Abs(placement.BurstImpulse.Z - 100f) < Tolerance
                 && MathF.Abs(placement.BurstImpulse.X) < Tolerance
                 && MathF.Abs(placement.BurstImpulse.Y) < Tolerance,
                    "a piece eight units above a prop five hundred units out is thrown straight up");
    }
}
