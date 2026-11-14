//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

namespace TF2.Shared.Anim;

/// <summary>
/// The third-person gesture each TF weapon action shows: stock's
/// <c>DoAnimationEvent</c> asks, per family. Every gun family's shot is
/// <c>PLAYER_ATTACK1</c> — <c>CTFWeaponBaseGun::PrimaryAttack</c> fires through
/// <c>FireProjectile</c>, whose tail sends it (tf_weaponbase_gun.cpp:277-321),
/// and the minigun's firing loop is that same path per round
/// (tf_weapon_minigun.cpp:372). A melee swing says so on a hit and a miss
/// alike (CTFWeaponBaseMelee::Smash, tf_weaponbase_melee.cpp:348-351). A
/// reload is <c>PLAYER_RELOAD</c> once, at its start: the whole-clip
/// <c>CTFWeaponBase::DefaultReload</c> (tf_weaponbase.cpp:2219) and a
/// shell-by-shell loader's first round (<c>ReloadSingly</c>,
/// tf_weaponbase.cpp:2004); the loader's later rounds are the loop event
/// (:2008) and its finish the end (:2089) — <c>ReloadsSingly</c> weapons only,
/// the rest never send them.
/// </summary>
[SideService(Side.Server)]
public sealed class TFWeaponGestures(TFWeaponScripts scripts) : IWeaponGestureRule
{
    /// <inheritdoc/>
    public PlayerAnimEvent? Gesture(WeaponGestureTrigger trigger, string weaponClass)
    {
        TFWeaponId id = TFWeaponNames.IdOf(weaponClass);

        if (id == TFWeaponId.None || scripts.Get(weaponClass) is not { } script)
            return null;

        TFWeaponKind kind = TFWeaponKinds.Of(script);

        return trigger switch
        {
            WeaponGestureTrigger.Shot when kind is TFWeaponKind.Gun or TFWeaponKind.Shotgun
                or TFWeaponKind.Minigun or TFWeaponKind.SniperRifle or TFWeaponKind.Revolver
                or TFWeaponKind.Projectile or TFWeaponKind.Bow => PlayerAnimEvent.AttackPrimary,
            WeaponGestureTrigger.Swing when kind == TFWeaponKind.Melee => PlayerAnimEvent.AttackPrimary,
            WeaponGestureTrigger.WindUp when kind is TFWeaponKind.Minigun or TFWeaponKind.Flamethrower
                => PlayerAnimEvent.AttackPre,
            WeaponGestureTrigger.WindDown when kind is TFWeaponKind.Minigun or TFWeaponKind.Flamethrower
                => PlayerAnimEvent.AttackPost,
            WeaponGestureTrigger.SustainedFire when kind == TFWeaponKind.Flamethrower
                => PlayerAnimEvent.AttackPrimary,
            WeaponGestureTrigger.Secondary when kind == TFWeaponKind.Flamethrower
                => PlayerAnimEvent.AttackSecondary,
            WeaponGestureTrigger.ReloadStart => PlayerAnimEvent.Reload,
            WeaponGestureTrigger.ReloadShell when TFWeaponKinds.ReloadsSingly(id) => PlayerAnimEvent.ReloadLoop,
            WeaponGestureTrigger.ReloadEnd when TFWeaponKinds.ReloadsSingly(id) => PlayerAnimEvent.ReloadEnd,
            _ => null,
        };
    }
}
