using SourceSharp.Abi;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The launcher a simulated blast jump is fired from: what its shot is worth
/// and how it leaves the muzzle, every number the mod's own. The muzzle is
/// (forward, right, up) off the eye along the aim, <see cref="MuzzleUpDucked"/>
/// its up while the shooter crouches.
/// </summary>
public readonly record struct JumpLauncher(TFWeaponId Weapon, float Damage, float Speed, float Radius, float SelfRadius, DamageType Bits,
                                           float RefireSeconds, Vector Muzzle, float MuzzleUpDucked)
{
    /// <summary>
    /// The rocket launcher: the script's damage, refire time and speed (0 for
    /// the stock speed), with the rocket's constants (<see cref="TFProjectileDefs"/>)
    /// and the weapon's damage bits.
    /// </summary>
    public static JumpLauncher Rocket(float damage, float refireSeconds, float scriptSpeed = 0f)
        => new(TFWeaponId.Rocketlauncher, damage, TFProjectileDefs.Speed(scriptSpeed, TFProjectileDefs.RocketSpeed), TFProjectileDefs.RocketRadius,
               TFProjectileDefs.RocketRadiusForRocketJumps, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher), refireSeconds,
               new Vector(TFProjectileDefs.RocketMuzzleForward, TFProjectileDefs.RocketMuzzleRight, TFProjectileDefs.RocketMuzzleUp),
               TFProjectileDefs.RocketMuzzleUpDucked);

    /// <summary>The rocket launcher as its weapon script has it.</summary>
    public static JumpLauncher Rocket(TFWeaponScript script)
        => Rocket(script.Primary.Damage, script.Primary.TimeFireDelay, script.Primary.ProjectileSpeed);
}
