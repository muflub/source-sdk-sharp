using SourceSharp.Abi;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What a simulated blast jump fires: a rocket that goes off where it meets a solid, or a sticky that sticks there and goes off when detonated.</summary>
public enum JumpProjectile : byte
{
    Rocket = 0,

    /// <summary>A sticky bomb (round 2's S3): lobbed, stuck where it lands, detonated by the second trigger once armed.</summary>
    Sticky,
}

/// <summary>
/// The launcher a simulated blast jump is fired from: what its shot is worth
/// and how it leaves the muzzle, every number the mod's own. The muzzle is
/// (forward, right, up) off the eye along the aim, <see cref="MuzzleUpDucked"/>
/// its up while the shooter crouches. A sticky launcher's shot (<see cref="Kind"/>)
/// also climbs <see cref="LaunchUp"/>, falls at <see cref="GravityScale"/> of the
/// world's gravity and arms <see cref="ArmSeconds"/> after it is fired.
/// </summary>
public readonly record struct JumpLauncher(TFWeaponId Weapon, float Damage, float Speed, float Radius, float SelfRadius, DamageType Bits,
                                           float RefireSeconds, Vector Muzzle, float MuzzleUpDucked)
{
    /// <summary>A rocket, or a sticky.</summary>
    public JumpProjectile Kind { get; init; }

    /// <summary>What the shot adds upward along the shooter's up as it leaves (the pipes' 200: <c>FirePipeBomb</c>); 0 for a rocket.</summary>
    public float LaunchUp { get; init; }

    /// <summary>The share of the world's gravity the shot falls at (a sticky's 1: see <see cref="Sticky(float, float, float, float, float)"/>); 0 for a rocket, which flies straight.</summary>
    public float GravityScale { get; init; }

    /// <summary>How long after it is fired a sticky may be detonated (<c>tf_grenadelauncher_livetime</c>); 0 for a rocket.</summary>
    public float ArmSeconds { get; init; }

    /// <summary>
    /// The commands after the firing one before a detonation finds the sticky armed, at <paramref name="tickSeconds"/> a
    /// command: the game arms it by a think booked <see cref="ArmSeconds"/> after its spawn (TFStickybomb.OnSpawn), which runs
    /// on the first tick at or past that (the 54th at 0.015 s for 0.8 s) and may run after that tick's command, so the first
    /// sure one is the next (55). That also passes stock's compare, <c>curtime - m_flCreationTime &gt;= GetLiveTime()</c>
    /// (<c>DetonateRemotePipebombs</c>, tf_weapon_pipebomblauncher.cpp:551).
    /// </summary>
    public int ArmCommands(float tickSeconds) => (int)MathF.Ceiling((ArmSeconds / tickSeconds) - 1e-4f) + 1;

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

    /// <summary>
    /// The sticky launcher (round 2's S3). The shot is stock's: the script's damage, radius and refire time; a tap's launch
    /// speed (<see cref="TFProjectileDefs.StickySpeed"/> at <paramref name="charge"/> 0: <c>TF_PIPEBOMB_MIN_CHARGE_VEL</c>
    /// 900), the pipes' climb of 200 (<see cref="TFProjectileDefs.PipeLaunchUp"/>, its ±10 jitter left out: a simulation is
    /// the same every time), the arm time (<c>tf_grenadelauncher_livetime</c> 0.8, <see cref="TFProjectileDefs.StickyArmTime"/>),
    /// the owner's own pass at <c>TF_GRENADE_JUMP_RADIUS</c> (<see cref="TFProjectileDefs.GrenadeJumpRadius"/>, 146), the
    /// pipes' muzzle (16, 8, -6, no crouched arm: <c>FirePipeBomb</c> has none) and the launcher's damage bits.
    /// The fall is what this port's sticky flies: <c>TFStickybomb</c> is a <c>ScriptedEntityKind.Prop</c>, moved by the physics
    /// solver, and its <c>SetGravity(0.4)</c> (<see cref="TFProjectileDefs.PipebombGravity"/>, stock's own call) is the
    /// entity's gravity, which the physics solver does not read: it falls at the physics environment's, the world's 800
    /// (<see cref="GravityScale"/> <see cref="TFProjectileDefs.GrenadeFallScale"/>, 1), as stock's does (Valve's own estimate, <c>EstimateProjectileImpactPosition</c>, uses the
    /// full gravity). The solver's air drag is not simulated: a look-down tap flies some 0.07 s, where drag is nothing; a lob
    /// wants a live measure before a plan leans on it.
    /// </summary>
    public static JumpLauncher Sticky(float damage, float radius, float refireSeconds, float charge = 0f, float armSeconds = TFProjectileDefs.StickyArmTime)
        => new(TFWeaponId.Pipebomblauncher, damage, TFProjectileDefs.StickySpeed(charge), radius, TFProjectileDefs.GrenadeJumpRadius,
               TFWeaponDamageTypes.Of(TFWeaponId.Pipebomblauncher), refireSeconds,
               new Vector(TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight, TFProjectileDefs.PipeMuzzleUp), TFProjectileDefs.PipeMuzzleUp)
        {
            Kind = JumpProjectile.Sticky,
            LaunchUp = TFProjectileDefs.PipeLaunchUp,
            GravityScale = TFProjectileDefs.GrenadeFallScale,
            ArmSeconds = armSeconds,
        };

    /// <summary>The sticky launcher as its weapon script has it.</summary>
    public static JumpLauncher Sticky(TFWeaponScript script)
        => Sticky(script.Primary.Damage, script.DamageRadius, script.Primary.TimeFireDelay);
}
