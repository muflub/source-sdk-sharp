//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

/// <summary>A vector in game units.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized() => Length > 0 ? this * (1 / Length) : default;

    public static Vec3 operator *(Vec3 v, double k) => new(v.X * k, v.Y * k, v.Z * k);
}

/// <summary>Whose blast it is, as stock's push tells them apart: another's, or the robot's own (a Soldier's own rocket in the air, on the ground, or any other own blast).</summary>
public enum BlastOwner
{
    Other,
    SelfSoldierAirborne,
    SelfSoldierGrounded,
    SelfOther,
}

/// <summary>A blast as it reaches a robot: the direction from the blast to the robot, the damage it does for force, and whose it is.</summary>
public readonly record struct BlastForce(Vec3 AwayFromBlast, double Damage, BlastOwner Owner);

/// <summary>
/// The push a blast gives a robot (ruling L12: a robot is pushed by blasts as a player is, and
/// can blast jump). Stock CTFPlayer::ApplyPushFromDamage gives a player
/// <c>dir × DamageForce(hull, damage, scale)</c>, where DamageForce is
/// <c>damage × (48·48·82 ÷ the hull's volume) × scale</c>, capped at 1000, and the scale is 6
/// for another's blast, 10 for a Soldier's own rocket in the air, 5 on the ground and 9 for any
/// other own blast; a Heavy takes half of another's push, and another's push is reduced by the
/// victim's damage_force_reduction. Kept here: all of that, with the robot's hull being the
/// standing hull times its scale on every axis, and its knockback resistance (a share that
/// follows scale, R16) as damage_force_reduction. Not kept: the sentry, bow, flare pellet,
/// airblast and rage arms (no robot weapon does them yet), and MvM's rule that a miniboss is
/// never pushed (L12 replaces it).
/// </summary>
public sealed class BlastPushRules
{
    private readonly DescentContent _content;

    public BlastPushRules(DescentContent content) => _content = content;

    private PushSettings S => _content.RobotRules.Push;

    /// <summary>Stock's DamageForce for a hull <paramref name="scale"/> times the standing one: damage over the hull's volume, by the force scale, capped.</summary>
    public double Force(double damage, double scale, double forceScale)
    {
        double volume = S.HullWidth * scale * (S.HullDepth * scale) * (S.HullHeight * scale);
        double force = damage * (S.HullWidth * S.HullDepth * S.HullHeight / volume) * forceScale;
        return Math.Min(force, S.ForceCap);
    }

    /// <summary>The force scale stock uses for this owner.</summary>
    public double ForceScale(BlastOwner owner) => owner switch
    {
        BlastOwner.Other => S.OtherScale,
        BlastOwner.SelfSoldierAirborne => S.SelfSoldierAirborneScale,
        BlastOwner.SelfSoldierGrounded => S.SelfSoldierGroundedScale,
        BlastOwner.SelfOther => S.SelfScale,
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "not a blast owner"),
    };

    /// <summary>
    /// The velocity change a blast gives a robot of <paramref name="scale"/> with
    /// <paramref name="knockbackResistPct"/>. A robot's own blast ignores its resistance, as
    /// stock's own blast ignores damage_force_reduction; a Heavy takes half of another's.
    /// </summary>
    public Vec3 VelocityChange(BlastForce blast, double scale, double knockbackResistPct, bool heavy = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        double magnitude = Force(blast.Damage, scale, ForceScale(blast.Owner));
        if (blast.Owner == BlastOwner.Other)
        {
            if (heavy) magnitude *= S.HeavyOtherMult;
            magnitude *= 1 - Math.Clamp(knockbackResistPct, 0, 100) / 100;
        }
        return blast.AwayFromBlast.Normalized() * magnitude;
    }
}
