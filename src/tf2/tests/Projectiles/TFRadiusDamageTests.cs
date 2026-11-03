using SourceSharp.Abi;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// <see cref="TFRadiusDamage"/>: <c>CTFRadiusDamageInfo</c>'s falloff, a
/// victim's distance and the demoman's self share
/// (<c>tf_gamerules.cpp:5625-5892</c>), and <c>ApplyPushFromDamage</c>'s
/// push (<c>tf_player.cpp:10232-10475</c>).
/// </summary>
public sealed class TFRadiusDamageTests
{
    private const DamageType Blast = DamageType.Blast | TFDamageBits.HalfFalloff;

    private static readonly Vector Standing = new(48f, 48f, 82f);
    private static readonly Vector Crouched = new(48f, 48f, 62f);

    [Fact]
    public void HalfFalloffLeavesHalfAtTheEdge()
        => Assert.Equal(0.5f, TFRadiusDamage.Falloff(Blast, 90f, 146f));

    [Fact]
    public void RadiusMaxLeavesNothingAtTheEdge()
        => Assert.Equal(0f, TFRadiusDamage.Falloff(DamageType.Blast | TFDamageBits.RadiusMax | TFDamageBits.HalfFalloff, 90f, 146f));

    [Fact]
    public void PlainFalloffIsDamageOverRadius()
        => Assert.Equal(100f / 200f, TFRadiusDamage.Falloff(DamageType.Blast, 100f, 200f));

    [Fact]
    public void ZeroRadiusKeepsTheWholeDamage()
        => Assert.Equal(1f, TFRadiusDamage.Falloff(DamageType.Blast, 100f, 0f));

    [Fact]
    public void DirectHitTakesTheFullRocket()
    {
        var blast = TFBlast.Of(90f, TFProjectileDefs.RocketRadius, Blast);
        float distance = TFRadiusDamage.VictimDistance(default, directHit: true, isPlayer: true,
                                                       new Vector(0f, 0f, 41f), new Vector(0f, 0f, 0f), default);
        Assert.Equal(90f, TFRadiusDamage.VictimDamage(in blast, distance, self: false, TFWeaponId.Rocketlauncher));
    }

    [Fact]
    public void SplashFallsLinearlyToHalfAtTheRadius()
    {
        var blast = TFBlast.Of(90f, 146f, Blast);
        Assert.Equal(67.5f, TFRadiusDamage.VictimDamage(in blast, 73f, false, TFWeaponId.Rocketlauncher), 4);
        Assert.Equal(45f, TFRadiusDamage.VictimDamage(in blast, 146f, false, TFWeaponId.Rocketlauncher), 4);
    }

    [Fact]
    public void SplashPastTheRadiusStaysAtTheEdgeShare()
        => Assert.Equal(45f, TFRadiusDamage.VictimDamage(TFBlast.Of(90f, 146f, Blast), 500f, false, TFWeaponId.Rocketlauncher), 4);

    [Fact]
    public void PlayerIsScoredAtTheNearerOfCentreAndOrigin()
    {
        var source = new Vector(0f, 0f, 0f);
        float d = TFRadiusDamage.VictimDistance(source, false, isPlayer: true,
                                                worldSpaceCenter: new Vector(100f, 0f, 41f),
                                                origin: new Vector(100f, 0f, 0f), traceEnd: new Vector(5f, 0f, 0f));
        Assert.Equal(100f, d, 4);
    }

    [Fact]
    public void NonPlayerIsScoredAtTheTraceEnd()
    {
        float d = TFRadiusDamage.VictimDistance(default, false, isPlayer: false,
                                                new Vector(100f, 0f, 41f), new Vector(100f, 0f, 0f), new Vector(30f, 40f, 0f));
        Assert.Equal(50f, d, 4);
    }

    [Theory]
    [InlineData(TFWeaponId.Pipebomblauncher)]
    [InlineData(TFWeaponId.Grenadelauncher)]
    [InlineData(TFWeaponId.Cannon)]
    [InlineData(TFWeaponId.Stickbomb)]
    public void DemomanWeaponsHurtTheirOwnerAtThreeQuarters(TFWeaponId weapon)
        => Assert.Equal(75f, TFRadiusDamage.VictimDamage(TFBlast.Of(100f, 146f, Blast), 0f, self: true, weapon), 4);

    [Fact]
    public void RocketHurtsItsOwnerUnscaled()
        => Assert.Equal(100f, TFRadiusDamage.VictimDamage(TFBlast.Of(100f, 146f, Blast), 0f, self: true, TFWeaponId.Rocketlauncher), 4);

    [Fact]
    public void GrenadeHurtsAnEnemyUnscaled()
        => Assert.Equal(100f, TFRadiusDamage.VictimDamage(TFBlast.Of(100f, 146f, Blast), 0f, self: false, TFWeaponId.Grenadelauncher), 4);

    [Fact]
    public void SelfPassUsesTheJumpRadiusAndBaseDamageAndKeepsTheFalloff()
    {
        var blast = TFBlast.Of(112f, 200f, DamageType.Blast);
        var self = blast.SelfPass(TFProjectileDefs.RocketRadiusForRocketJumps, 90f);
        Assert.Equal((90f, 121f, 112f / 200f), (self.Damage, self.Radius, self.Falloff));
    }

    [Fact]
    public void RocketJumpRadiusIsStocks121()
        => Assert.Equal(121f, TFProjectileDefs.RocketRadiusForRocketJumps, 4);

    [Fact]
    public void ABoxWhoseFaceIsInsideTheRadiusIsReached()
        => Assert.True(TFRadiusDamage.Reaches(default, 146f, new Vector(140f, -24f, 0f), new Vector(188f, 24f, 82f)));

    [Fact]
    public void ABoxWhoseNearestCornerIsOutsideTheRadiusIsNot()
        => Assert.False(TFRadiusDamage.Reaches(default, 146f, new Vector(110f, 110f, 0f), new Vector(158f, 158f, 82f)));

    [Fact]
    public void DamageForceOfAStandingPlayerIsDamageTimesScale()
        => Assert.Equal(500f, TFRadiusDamage.DamageForce(Standing, 50f, 10f), 3);

    [Fact]
    public void DamageForceStopsAtAThousand()
        => Assert.Equal(1000f, TFRadiusDamage.DamageForce(Standing, 200f, 10f));

    [Theory]
    [InlineData(true, true, false, false, 10f)]
    [InlineData(true, true, true, false, 5f)]
    [InlineData(true, false, false, false, 0f)]
    [InlineData(false, true, false, false, 9f)]
    [InlineData(false, true, false, true, 8.5f)]
    public void SelfPushScaleIsStocks(bool soldier, bool blast, bool onGround, bool pyroFlareJump, float expected)
        => Assert.Equal(expected, TFRadiusDamage.SelfPushScale(soldier, blast, onGround, pyroFlareJump));

    [Fact]
    public void CrouchedSelfPushIsMeasured55High()
        => Assert.Equal(55f, TFRadiusDamage.SelfPushSize(Crouched, Crouched).Z);

    [Fact]
    public void StandingSelfPushKeepsItsHull()
        => Assert.Equal(82f, TFRadiusDamage.SelfPushSize(Standing, Crouched).Z);

    [Theory]
    [InlineData(true, true, false, false, 0.6f)]
    [InlineData(true, true, true, false, 1f)]
    [InlineData(true, true, false, true, 1f)]
    [InlineData(true, false, false, false, 1f)]
    [InlineData(false, true, false, false, 1f)]
    public void TheSelfDamageScaleIsTheSoldiersJumpAlone(bool soldier, bool blast, bool onGround, bool inWater, float scale)
        => Assert.Equal(scale, TFRadiusDamage.SelfDamageScale(soldier, blast, onGround, inWater));

    [Fact]
    public void RocketAtTheFeetThrowsAnAirborneSoldierStraightUp()
    {
        // The rocket at the soldier's feet, his centre 41 up: the push points
        // from 10 below the rocket to the centre, reversed, so straight up.
        var dir = TFRadiusDamage.PushDirection(new Vector(0f, 0f, 0f), new Vector(0f, 0f, 41f));
        float scale = TFRadiusDamage.SelfPushScale(soldier: true, blast: true, onGround: false, pyroFlareJump: false);
        var push = TFRadiusDamage.Push(dir, Standing, 60f, self: true, scale, heavy: false);
        Assert.Equal((0f, 0f, 600f), (push.X, push.Y, MathF.Round(push.Z, 3)));
    }

    [Fact]
    public void TheBlastIsTakenTenUnitsBelowTheInflictorSoASideBlastLiftsTheBody()
    {
        // Level with the victim's centre and 100 to the side: from 10 below,
        // (100, 0, -10) normalized, and the push is its reverse, a little up.
        var dir = TFRadiusDamage.PushDirection(new Vector(100f, 0f, 41f), new Vector(0f, 0f, 41f));
        Assert.Equal(-10f / MathF.Sqrt(100f * 100f + 10f * 10f), dir.Z, 5);
    }

    [Fact]
    public void AnEnemyBlastPushesAtSixAndAHeavyAtHalf()
    {
        var dir = new Vector(-1f, 0f, 0f);
        var other = TFRadiusDamage.Push(dir, Standing, 50f, self: false, 0f, heavy: false);
        var heavy = TFRadiusDamage.Push(dir, Standing, 50f, self: false, 0f, heavy: true);
        Assert.Equal((300f, 150f), (MathF.Round(other.X, 3), MathF.Round(heavy.X, 3)));
    }

    [Theory]
    [InlineData(0f, 900f)]
    [InlineData(2f, 1650f)]
    [InlineData(4f, 2400f)]
    [InlineData(6f, 2400f)]
    public void StickySpeedRampsWithTheCharge(float charge, float expected)
        => Assert.Equal(expected, TFProjectileDefs.StickySpeed(charge), 3);

    [Fact]
    public void RemapOfAnEmptyRangeAnswersTheFarEndAtOrPastIt()
        => Assert.Equal((1f, 2f), (TFRadiusDamage.RemapClamped(-1f, 0f, 0f, 1f, 2f), TFRadiusDamage.RemapClamped(0f, 0f, 0f, 1f, 2f)));
}
