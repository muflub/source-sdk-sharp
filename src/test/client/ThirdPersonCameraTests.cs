using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where a third-person camera sits — plan_entity.md §3.10, the policy-player
/// lane.
///
/// <para>
/// These are <c>src/sourcesharp/test/thirdperson_camera_test.cpp</c>'s eighteen
/// gtests, ported one behaviour per <c>[Fact]</c> when the rule moved out of
/// <c>thirdperson_camera.h</c>. Every expectation below is the same number the
/// gtest asserted; nothing was re-derived from the port.
/// </para>
///
/// <para>
/// <b>Why the rule has a test at all, rather than three lines at the call
/// site</b>: a camera in front of the eye, a camera scaled by zero, and a camera
/// that never moved all produce the SAME PICTURE — a frame with no player in it
/// — so a screenshot cannot say which of the three happened.
/// </para>
/// </summary>
public class ThirdPersonCameraTests
{
    // The basis of a player looking down +X at yaw 0, which is what
    // AngleVectors gives for angles (0, 0, 0): forward +X, right -Y, up +Z.
    private static readonly Angles.Basis Facing = new(
        new Vector(1f, 0f, 0f), new Vector(0f, -1f, 0f), new Vector(0f, 0f, 1f));

    // ---- Displacement ------------------------------------------------------

    /// <summary>
    /// Looking down +X, the camera belongs at -X. A camera at +150 X sees the
    /// room the player is facing and no player, which reads as "the model did
    /// not draw".
    /// </summary>
    [Fact]
    public void BackIsBehindTheEyeAndNotInFrontOfIt()
    {
        Vector d = ThirdPersonCamera.Displacement(Facing, 150.0f, 0.0f, 0.0f);

        Assert.Equal(-150.0f, d.X);
        Assert.Equal(0.0f, d.Y);
        Assert.Equal(0.0f, d.Z);
    }

    /// <summary>Right and up follow their own axes — right is -Y at yaw 0.</summary>
    [Fact]
    public void RightAndUpFollowTheirOwnAxes()
    {
        Vector d = ThirdPersonCamera.Displacement(Facing, 0.0f, 20.0f, 40.0f);

        Assert.Equal(0.0f, d.X);
        Assert.Equal(-20.0f, d.Y);
        Assert.Equal(40.0f, d.Z);
    }

    /// <summary>And the three offsets add rather than replacing one another.</summary>
    [Fact]
    public void TheThreeOffsetsAdd()
    {
        Vector d = ThirdPersonCamera.Displacement(Facing, 100.0f, 10.0f, 5.0f);

        Assert.Equal(-100.0f, d.X);
        Assert.Equal(-10.0f, d.Y);
        Assert.Equal(5.0f, d.Z);
    }

    /// <summary>A zero wish is the eye itself, and not a stale value.</summary>
    [Fact]
    public void AZeroWishIsTheEyeItself()
    {
        Vector d = ThirdPersonCamera.Displacement(Facing, 0.0f, 0.0f, 0.0f);

        Assert.Equal(0.0f, d.X);
        Assert.Equal(0.0f, d.Y);
        Assert.Equal(0.0f, d.Z);
    }

    // ---- WishLength, which the fraction rule measures its floor against -----

    /// <summary>
    /// The published wish length measures the whole displacement and not one
    /// axis of it. Through the blob, because that is the number the rule hands
    /// the caller and the number the floor below is compared against.
    /// </summary>
    [Theory]
    [InlineData(150.0f, 0.0f, 0.0f, 150.0f)]
    [InlineData(0.0f, 0.0f, 0.0f, 0.0f)]
    public void TheWishLengthMeasuresTheWholeDisplacement(float back, float right, float up,
                                                          float expected)
    {
        var io = new ThirdPersonCameraBlob
        {
            Phase = ThirdPersonCamera.Place,
            ViewAngles = Vector.Zero,
            IdealDist = back,
            IdealDistRight = right,
            IdealDistUp = up,
        };

        Assert.Equal(1, ThirdPersonCamera.Answer(ref io));
        Assert.Equal(expected, io.WishLength, 3);
    }

    /// <summary>
    /// A three-axis displacement measures the diagonal — 2, 3 and 6 make 7.
    /// This is the fact that separates a length from "the longest axis".
    /// </summary>
    [Fact]
    public void TheWishLengthIsTheDiagonalAndNotAnAxis()
    {
        var io = new ThirdPersonCameraBlob
        {
            Phase = ThirdPersonCamera.Place,
            ViewAngles = Vector.Zero,
            IdealDist = 2f,          // -X
            IdealDistRight = 3f,     // -Y
            IdealDistUp = 6f,        // +Z
        };

        Assert.Equal(1, ThirdPersonCamera.Answer(ref io));
        Assert.Equal(7.0f, io.WishLength, 3);
    }

    // ---- SurvivingFraction -------------------------------------------------

    /// <summary>A clear sweep keeps the whole wish.</summary>
    [Fact]
    public void AClearSweepKeepsTheWholeWish()
        => Assert.Equal(1.0f, ThirdPersonCamera.SurvivingFraction(1.0f, 150.0f));

    /// <summary>0.6 of 150 is 90 units, comfortably past the floor, so it is kept.</summary>
    [Fact]
    public void ABlockedSweepPullsTheCameraIn()
        => Assert.Equal(0.6f, ThirdPersonCamera.SurvivingFraction(0.6f, 150.0f));

    /// <summary>
    /// 0.1 of 150 is 15 units: inside the player's own model, where the near
    /// plane cuts the chest open and the frame is unreadable. The answer is the
    /// eye, not 15 units.
    /// </summary>
    [Fact]
    public void ACameraPulledInsideTheOwnerGoesBackToTheEye()
        => Assert.Equal(0.0f, ThirdPersonCamera.SurvivingFraction(0.1f, 150.0f));

    /// <summary>
    /// And the floor is measured in UNITS, not in fraction: the same 0.1 on a
    /// much longer wish is 60 units and is perfectly usable.
    /// </summary>
    [Fact]
    public void TheFloorIsMeasuredInUnitsAndNotInFraction()
        => Assert.Equal(0.1f, ThirdPersonCamera.SurvivingFraction(0.1f, 600.0f));

    /// <summary>30 units of 150 is 0.2 exactly — at the floor, which is kept.</summary>
    [Fact]
    public void TheFloorIsExactlyMinDistance()
        => Assert.Equal(0.2f, ThirdPersonCamera.SurvivingFraction(0.2f, 150.0f));

    /// <summary>A hair under it is not.</summary>
    [Fact]
    public void AHairUnderTheFloorGoesBackToTheEye()
        => Assert.Equal(0.0f, ThirdPersonCamera.SurvivingFraction(0.19f, 150.0f));

    /// <summary>A flush wall gives nothing rather than a negative.</summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(-0.5f)]
    public void AFlushWallGivesNothingRatherThanANegative(float fraction)
        => Assert.Equal(0.0f, ThirdPersonCamera.SurvivingFraction(fraction, 150.0f));

    // ---- SeesTheOwner ------------------------------------------------------

    /// <summary>A camera behind the player sees them.</summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.5f)]
    public void ACameraBehindThePlayerSeesThem(float surviving)
        => Assert.True(ThirdPersonCamera.SeesTheOwner(surviving, 150.0f));

    /// <summary>
    /// A camera collapsed onto the eye does not. This is the pair that keeps
    /// the model from filling the screen when the player backs into a wall: the
    /// fraction rule sends the camera home, and the same numbers say the model
    /// must stop drawing.
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.1f)]
    public void ACameraCollapsedOntoTheEyeDoesNot(float surviving)
        => Assert.False(ThirdPersonCamera.SeesTheOwner(surviving, 150.0f));

    /// <summary>
    /// <c>cam_idealdist 0</c> is a legal thing to type, and it is first person
    /// with extra steps. The model must not draw for it.
    /// </summary>
    [Fact]
    public void AThirdPersonModeWithNoDistanceIsFirstPerson()
        => Assert.False(ThirdPersonCamera.SeesTheOwner(1.0f, 0.0f));

    // ---- CameraAngles ------------------------------------------------------------

    /// <summary>
    /// With no orbit the camera looks where the player looks — and the yaw is
    /// folded into (-180, 180] on the way, so 250 reads as -110.
    /// </summary>
    [Fact]
    public void WithNoOrbitTheCameraLooksWhereThePlayerLooks()
    {
        (float pitch, float yaw) = ThirdPersonCamera.CameraAngles(12.0f, 250.0f, 0.0f, 0.0f);

        Assert.Equal(12.0f, pitch);
        Assert.Equal(-110.0f, yaw);
    }

    /// <summary>The orbit adds.</summary>
    [Fact]
    public void TheOrbitAdds()
    {
        (float pitch, float yaw) = ThirdPersonCamera.CameraAngles(0.0f, 0.0f, 20.0f, 90.0f);

        Assert.Equal(20.0f, pitch);
        Assert.Equal(90.0f, yaw);
    }

    /// <summary>
    /// A player looking straight down with a downward orbit would pitch past
    /// vertical, and <c>AngleVectors</c> hands back a flipped up vector for it —
    /// the whole frame rolls over. So pitch clamps short of vertical, both ways.
    /// </summary>
    [Theory]
    [InlineData(89.0f, 30.0f, 89.0f)]
    [InlineData(-89.0f, -30.0f, -89.0f)]
    public void PitchIsClampedShortOfVertical(float viewPitch, float idealPitch, float expected)
    {
        (float pitch, _) = ThirdPersonCamera.CameraAngles(viewPitch, 0.0f, idealPitch, 0.0f);

        Assert.Equal(expected, pitch);
    }

    /// <summary>
    /// Yaw has no ends to run into: it must come back round. A clamp here would
    /// stick the camera at 180 and refuse to orbit past the player's back.
    /// </summary>
    [Theory]
    [InlineData(170.0f, 30.0f, -160.0f)]
    [InlineData(-170.0f, -30.0f, 160.0f)]
    public void YawFoldsRatherThanClamps(float viewYaw, float idealYaw, float expected)
    {
        (_, float yaw) = ThirdPersonCamera.CameraAngles(0.0f, viewYaw, 0.0f, idealYaw);

        Assert.Equal(expected, yaw, 3);
    }

    // ---- the crossing ------------------------------------------------------

    /// <summary>
    /// ROLL IS ZERO AND NOT THE VIEW'S. A third-person camera that inherited the
    /// player's roll would tilt the whole world when they took a punch, which is
    /// a view effect meant for the eye. The native side used to write this line
    /// itself (<c>angCamera[ROLL] = 0.0f</c>); it is part of the answer now.
    /// </summary>
    [Fact]
    public void ThePlacedCameraHasNoRoll()
    {
        var io = new ThirdPersonCameraBlob
        {
            Phase = ThirdPersonCamera.Place,
            ViewAngles = new Vector(0f, 0f, 45f),
            IdealDist = 150f,
        };

        Assert.Equal(1, ThirdPersonCamera.Answer(ref io));
        Assert.Equal(0f, io.CameraAngles.Z);
    }

    /// <summary>
    /// RESOLVE answers the fraction AND whether the model draws, from one set
    /// of numbers — the two must never be computed from different ones.
    /// </summary>
    [Fact]
    public void ResolveAnswersTheFractionAndTheModelTogether()
    {
        var io = new ThirdPersonCameraBlob
        {
            Phase = ThirdPersonCamera.Resolve,
            WishLength = 150f,
            TraceFraction = 0.1f,
        };

        Assert.Equal(1, ThirdPersonCamera.Answer(ref io));
        Assert.Equal(0f, io.SurvivingFraction);
        Assert.Equal(0, io.SeesOwner);
    }

    /// <summary>
    /// A phase the caller has never heard of is REFUSED rather than answered
    /// with zeroes — a zero surviving fraction is a real answer meaning "go back
    /// to the eye", so an unknown phase that returned one would place the camera
    /// rather than reporting that it could not.
    /// </summary>
    [Fact]
    public void AnUnknownPhaseIsRefused()
    {
        var io = new ThirdPersonCameraBlob { Phase = 7 };

        Assert.Equal(0, ThirdPersonCamera.Answer(ref io));
    }
}
