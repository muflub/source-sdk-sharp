//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Buildings;

/// <summary>
/// The level 3 sentry's rocket (<c>CTFProjectile_SentryRocket</c>,
/// <c>tf_obj_sentrygun.cpp:2358-2397</c> over <c>CTFBaseRocket</c>): a flat
/// flyer in the sentry's rocket model, at the base rocket's 1100 u/s
/// (<c>tf_weaponbase_rocket.cpp:274</c>), whose touch is a blast of the
/// weapon's own bits — <c>DMG_BLAST</c> plain, no half falloff
/// (<c>g_aWeaponDamageTypes</c>) — at the 100 damage the pod armed
/// (<c>tf_obj_sentrygun.cpp:1362</c>) over the stock rocket radius.
///
/// <para>
/// The flight is the core's <see cref="FlyingProjectile"/>: it never hits its
/// own shooter — the builder here, not the turret — and is spent on the
/// first touch, as stock's <c>CTFBaseRocket::Explode</c> is.
/// </para>
/// </summary>
[ScriptedEntity(TFSentryRocket.MapClass, ScriptedEntityKind.Projectile)]
public sealed class TFSentryRocket(IEngineEntityPhysics physics, EntityMotion motion, TFBlastApplier blast)
    : FlyingProjectile(physics, motion)
{
    /// <summary><c>LINK_ENTITY_TO_CLASS(tf_projectile_sentryrocket, ...)</c> (tf_obj_sentrygun.cpp:2358).</summary>
    public const string MapClass = "tf_projectile_sentryrocket";

    /// <summary><c>CTFBaseRocket::Create</c>'s <c>flLaunchSpeed</c> (tf_weaponbase_rocket.cpp:274).</summary>
    public const float Speed = 1100f;

    /// <summary>The pod's <c>iDamage = 100</c> (tf_obj_sentrygun.cpp:1362).</summary>
    public const float Damage = 100f;

    private readonly TFBlastApplier Blast = blast;

    [SaveField]
    private EntityHandle _launcher;

    /// <summary>
    /// The turret that fired it (<c>CTFBaseRocket m_hLauncher</c>): the kill
    /// rules' owner arm — the rocket's engine owner is the builder here, not
    /// the turret, so the death rules ask the rocket itself.
    /// </summary>
    public EntityHandle Launcher => _launcher;

    /// <summary>Fired by the turret <paramref name="launcher"/> on the builder <paramref name="shooter"/>'s aim.</summary>
    public void FiredBy(EntityHandle launcher, EntityHandle shooter, Vector velocity)
    {
        _launcher = launcher;
        FiredBy(shooter, velocity);
    }

    /// <summary>What the damage layer reads the weapon's bits out of.</summary>
    public const TFWeaponId Weapon = TFWeaponId.SentryRocket;

    /// <summary>The weapon name the scopes name; the pod arms no item, so the layer answers its stock row.</summary>
    public const string WeaponName = "tf_projectile_sentryrocket";

    protected override float GravityScale => 0f;

    protected override string ProjectileModel => TFObjectModels.SentryRocketModel;

    protected override void Impact(EntityHandle hit)
    {
        Blast.Apply(Origin, TFBlast.Of(Damage, TFProjectileDefs.RocketRadius,
                                       TFWeaponDamageTypes.Of(Weapon)),
                    Attribution, Handle, ignored: Handle, Weapon, WeaponName, critical: false,
                    directVictim: hit);
    }
}
