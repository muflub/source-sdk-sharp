using System;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE EXPLOSION FALLOFF CURVE, ASKED A QUESTION WITHOUT A RUNNING SOURCE.
///
/// <para>
/// Every fact here was impossible to write before <see cref="BlastRules"/>
/// existed: the curve was the middle of <c>CGameRules::RadiusDamage</c> in
/// <c>shell_gamerules.cpp</c>, reachable only by exploding something in a live
/// server. That is the whole measurable difference the move makes, and it is
/// why the gate for this lane is these facts and not a line count.
/// </para>
///
/// <para>
/// The reference numbers are the ones the native function's own comment
/// records: magnitude 120, radius 256, a victim a couple of hundred units away
/// on dm_lockdown taking 2.8 where the live defect delivered the whole 120.
/// </para>
/// </summary>
public class BlastRulesTests
{
    // The barrel the native comment describes.
    private const float Magnitude = 120f;
    private const float Radius = 256f;

    [Fact]
    public void FalloffIsDamagePerUnitOfRadius()
    {
        // 120/256. Stock: `falloff = info.GetDamage() / flRadius`.
        Assert.Equal(0.46875f, BlastRules.Falloff(Magnitude, Radius), 5);
    }

    [Fact]
    public void AZeroRadiusFallsOffAtOnePerUnitRatherThanDividingByIt()
    {
        // Stock's `else falloff = 1.0`. Not an arbitrary neutral: it is what
        // stops a missing radius key reading as "hurt the whole level".
        Assert.Equal(1f, BlastRules.Falloff(Magnitude, 0f));
    }

    [Fact]
    public void AtTheBlastItselfTheVictimTakesTheWholeMagnitude()
    {
        Assert.Equal(120f, BlastRules.AtDistance(Magnitude, Radius, 0f), 3);
    }

    [Fact]
    public void AtTheRadiusTheVictimTakesNothing()
    {
        Assert.Equal(0f, BlastRules.AtDistance(Magnitude, Radius, 256f), 3);
    }

    [Fact]
    public void TwoHundredAndFiftyUnitsOutIsTheTwoPointEightTheCommentRecords()
    {
        // The number in the native function's comment. It is the answer at
        // 250.0 units, not at the 252 the same sentence names -- see
        // BlastRules' class comment, which states the discrepancy rather than
        // repeating it.
        Assert.Equal(2.8f, BlastRules.AtDistance(Magnitude, Radius, 250f), 1);
    }

    [Fact]
    public void TwoHundredAndFiftyTwoUnitsOutIsUnderTwoHitPoints()
    {
        // 120 - 252*0.46875. The distance the comment names, on the curve the
        // comment describes.
        Assert.Equal(1.875f, BlastRules.AtDistance(Magnitude, Radius, 252f), 3);
    }

    [Fact]
    public void TwoHundredAndFiftyTwoUnitsOutIsNotTheWholeMagnitude()
    {
        // THE DEFECT, STATED AS A FACT. A blast whose first trace stopped at
        // fraction zero inside the exploding entity's own hull scored every
        // victim at distance zero, so this expression answered 120 out at the
        // edge of the radius and the RPG killed its own shooter through cover.
        Assert.True(BlastRules.AtDistance(Magnitude, Radius, 252f) < 5f,
            "a victim at 252 units of a 256-unit blast takes a couple of hit " +
            "points, not the whole magnitude");
    }

    [Fact]
    public void PastTheRadiusTheDamageIsNegativeSoTheSweepSparesTheVictim()
    {
        // NOT clamped to zero. Stock's caller reads `<= 0` as "skip this one
        // entirely", which is a different event from "hit for nothing": no
        // force, no trigger walk, no sound.
        Assert.True(BlastRules.AtDistance(Magnitude, Radius, 300f) < 0f);
    }

    [Fact]
    public void TheCurveIsLinearInDistanceAndNotInverseSquare()
    {
        // Halving the distance must halve the damage LOST, not quarter it.
        float lostAtFull = Magnitude - BlastRules.AtDistance(Magnitude, Radius, 200f);
        float lostAtHalf = Magnitude - BlastRules.AtDistance(Magnitude, Radius, 100f);

        Assert.Equal(lostAtFull / 2f, lostAtHalf, 3);
    }

    [Fact]
    public void AWiderBlastIsAHarderOneAtEveryDistance()
    {
        // The observation RocketVars.Radius' comment makes: the falloff is
        // magnitude/radius per unit, so widening the radius raises the damage
        // at every point inside the old one as well as reaching further.
        Assert.True(BlastRules.AtDistance(Magnitude, 300f, 100f)
                  > BlastRules.AtDistance(Magnitude, 200f, 100f));
    }

    //-------------------------------------------------------------------------
    // What stands in the way. blast_blocking.h's two predicates, restated over
    // managed handles.
    //-------------------------------------------------------------------------

    private static EntityHandle H(uint v) => new(v);

    [Fact]
    public void NothingInTheWayIsNotABlocker()
    {
        Assert.False(BlastRules.BlockerCounts(H(1), EntityHandle.None, EntityHandle.None));
    }

    [Fact]
    public void AVictimDoesNotShieldItself()
    {
        Assert.False(BlastRules.BlockerCounts(H(7), H(7), EntityHandle.None));
    }

    [Fact]
    public void AThingOwnedByTheVictimDoesNotShieldIt()
    {
        // Stock's "shouldn't let children block parents".
        Assert.False(BlastRules.BlockerCounts(H(7), H(9), H(7)));
    }

    [Fact]
    public void AnUnrelatedThingInTheWayIsABlocker()
    {
        Assert.True(BlastRules.BlockerCounts(H(7), H(9), H(11)));
    }

    [Fact]
    public void AHandleOfZeroIsNotReadAsNothing()
    {
        // The raw handle 0 is entry ZERO, which is a real entity -- the world.
        // EntityHandle.None is uint.MaxValue. A blocker of handle zero must
        // therefore still count, or worldspawn silently stops shielding.
        Assert.True(BlastRules.BlockerCounts(H(7), H(0), EntityHandle.None));
    }

    [Fact]
    public void SomethingWithNoPhysicsObjectKeepsAFlatQuarter()
    {
        Assert.Equal(0.25f, BlastRules.BlockedShare(hasPhysics: false, mass: 999f), 5);
    }

    [Fact]
    public void APhysicsBlockerKeepsItsMassOverThreeHundredAndFifty()
    {
        Assert.Equal(0.5f, BlastRules.BlockedShare(hasPhysics: true, mass: 175f), 5);
    }

    [Fact]
    public void ABlockerAtThreeHundredAndFiftyKilogrammesKeepsEverything()
    {
        Assert.True(BlastRules.BlockedShare(hasPhysics: true, mass: 350f) >= 1f);
    }

    //-------------------------------------------------------------------------
    // Water. Stock's "blasts don't travel into or out of water".
    //-------------------------------------------------------------------------

    [Fact]
    public void ABlastInWaterDoesNotReachSomethingDry()
    {
        Assert.False(BlastRules.ReachesThroughWater(blastInWater: true, victimWaterLevel: 0));
    }

    [Fact]
    public void ABlastInWaterReachesSomethingAnkleDeep()
    {
        Assert.True(BlastRules.ReachesThroughWater(blastInWater: true, victimWaterLevel: 1));
    }

    [Fact]
    public void ABlastInAirDoesNotReachSomethingFullySubmerged()
    {
        Assert.False(BlastRules.ReachesThroughWater(blastInWater: false, victimWaterLevel: 3));
    }

    [Fact]
    public void ABlastInAirReachesSomethingStandingInTheShallows()
    {
        Assert.True(BlastRules.ReachesThroughWater(blastInWater: false, victimWaterLevel: 2));
    }

    //-------------------------------------------------------------------------
    // Where the blast aims.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheBodyTargetOfAnUnrotatedBoxIsTheMiddleOfIt()
    {
        var at = Transform.FromOriginAngles(new Vector(10f, 20f, 30f), QAngle.Zero);

        Vector target = BlastRules.BodyTarget(at, new Vector(-16f, -16f, 0f),
                                                  new Vector(16f, 16f, 72f));

        Assert.Equal(10f, target.X, 3);
        Assert.Equal(20f, target.Y, 3);
        Assert.Equal(66f, target.Z, 3);
    }

    [Fact]
    public void TheBodyTargetOfARotatedOffCentreBoxIsNotTheAxisAlignedMidpoint()
    {
        // THE CHECK THAT COULD NOT FAIL IF THE TWO CENTRES WERE THE SAME
        // QUANTITY. anyentity.h's BodyTarget states that they part exactly
        // here, and this is the shape it names: a box whose middle is off the
        // entity's own origin, on an entity that is turned.
        var origin = new Vector(0f, 0f, 0f);
        var mins = new Vector(0f, -8f, 0f);
        var maxs = new Vector(64f, 8f, 16f);

        Vector unrotated = BlastRules.BodyTarget(
            Transform.FromOriginAngles(origin, QAngle.Zero), mins, maxs);
        Vector turned = BlastRules.BodyTarget(
            Transform.FromOriginAngles(origin, new QAngle(0f, 90f, 0f)), mins, maxs);

        Assert.Equal(32f, unrotated.X, 3);
        Assert.True(MathF.Abs(turned.X - unrotated.X) > 1f,
            "a quarter turn must move the OBB centre of an off-centre box");
    }
}
