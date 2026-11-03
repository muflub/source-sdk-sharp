//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

/// <summary>
/// Which TF behaviour runs a weapon: the stock class hierarchy collapsed to
/// the behaviours this port ships (plan §2 point 4: behaviours, not classes).
/// A row's <see cref="SourceSharp.Shared.WeaponType.Behaviour"/> is the kind's key.
/// </summary>
public enum TFWeaponKind
{
    /// <summary>A hitscan gun that reloads its whole clip (pistol, SMG, revolver).</summary>
    Gun,
    /// <summary>A hitscan gun that reloads one shell at a time (scattergun, the shotguns).</summary>
    Shotgun,
    Minigun,
    SniperRifle,
    /// <summary>
    /// The spy's revolver (<c>CTFRevolver</c>): the gun with the accuracy
    /// clock — <c>PrimaryAttack</c> stamps it and <c>GetWeaponCrosshairScale</c>
    /// blooms the crosshair off its age under the item's <c>set_weapon_mode</c>
    /// 1 (<c>tf_weapon_revolver.cpp:157, 207-223</c>).
    /// </summary>
    Revolver,
    Melee,
    /// <summary>A launcher whose shot is a projectile entity (Stage 2.8): rockets, pipes, stickies, syringes.</summary>
    Projectile,
    Flamethrower,
    Medigun,
    /// <summary>A PDA (<c>WeaponType "pda"</c>): its trigger hands the owner back to the previous item.</summary>
    Pda,
    /// <summary>
    /// The engineer's builder (<c>TF_WEAPON_BUILDER</c>): the click-to-place
    /// half of build — the blueprint out at the crosshair, +attack placing it,
    /// +attack2 turning it (<c>CTFWeaponBuilder</c>,
    /// <c>tf_weapon_builder.cpp</c>). The sapper shares the stock class but
    /// runs its own use, so it stays <see cref="Inert"/>.
    /// </summary>
    Builder,
    /// <summary>A weapon with no attack of its own (the sapper, the invis watch).</summary>
    Inert,
}

/// <summary>The kind of each TF weapon, and the behaviour keys the rows name.</summary>
public static class TFWeaponKinds
{
    /// <summary>The <see cref="SourceSharp.Shared.WeaponType.Behaviour"/> key of a kind.</summary>
    public static string Key(TFWeaponKind kind) => kind switch
    {
        TFWeaponKind.Gun => "tf2.gun",
        TFWeaponKind.Shotgun => "tf2.shotgun",
        TFWeaponKind.Minigun => "tf2.minigun",
        TFWeaponKind.SniperRifle => "tf2.sniperrifle",
        TFWeaponKind.Revolver => "tf2.revolver",
        TFWeaponKind.Melee => "tf2.melee",
        TFWeaponKind.Projectile => "tf2.projectile",
        TFWeaponKind.Flamethrower => "tf2.flamethrower",
        TFWeaponKind.Medigun => "tf2.medigun",
        TFWeaponKind.Pda => "tf2.pda",
        TFWeaponKind.Builder => "tf2.builder",
        TFWeaponKind.Inert => "tf2.inert",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The kind of the weapon a script describes: by its weapon id where the
    /// stock class has behaviour of its own, else by the script's weapon type
    /// (a melee script is a melee weapon, a bullet gun is a gun).
    /// </summary>
    public static TFWeaponKind Of(TFWeaponScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        switch (script.Id)
        {
            case TFWeaponId.Scattergun:
            case TFWeaponId.ShotgunPrimary:
            case TFWeaponId.ShotgunSoldier:
            case TFWeaponId.ShotgunHwg:
            case TFWeaponId.ShotgunPyro:
                return TFWeaponKind.Shotgun;
            case TFWeaponId.Minigun:
                return TFWeaponKind.Minigun;
            case TFWeaponId.Sniperrifle:
                return TFWeaponKind.SniperRifle;
            case TFWeaponId.Revolver:
                return TFWeaponKind.Revolver;
            case TFWeaponId.Rocketlauncher:
            case TFWeaponId.Grenadelauncher:
            case TFWeaponId.Pipebomblauncher:
            case TFWeaponId.SyringegunMedic:
                return TFWeaponKind.Projectile;
            case TFWeaponId.Flamethrower:
                return TFWeaponKind.Flamethrower;
            case TFWeaponId.Medigun:
                return TFWeaponKind.Medigun;
            case TFWeaponId.Builder:
                return TFWeaponKind.Builder;
        }

        return script.Category switch
        {
            TFWeaponCategory.Melee => TFWeaponKind.Melee,
            TFWeaponCategory.Primary or TFWeaponCategory.Secondary
                when script.Primary.Projectile == TFProjectile.Bullet => TFWeaponKind.Gun,
            TFWeaponCategory.Pda => TFWeaponKind.Pda,
            _ => TFWeaponKind.Inert,
        };
    }

    /// <summary>
    /// Whether the weapon reloads one round at a time (<c>m_bReloadsSingly</c>,
    /// set by the shotgun, rocket, grenade and stickybomb launcher constructors:
    /// <c>tf_weapon_shotgun.cpp:68</c>, <c>tf_weapon_rocketlauncher.cpp:159</c>,
    /// <c>tf_weapon_grenadelauncher.cpp:100</c>, <c>tf_weapon_pipebomblauncher.cpp:86</c>).
    /// </summary>
    public static bool ReloadsSingly(TFWeaponId id) => id is TFWeaponId.Scattergun
        or TFWeaponId.ShotgunPrimary or TFWeaponId.ShotgunSoldier or TFWeaponId.ShotgunHwg or TFWeaponId.ShotgunPyro
        or TFWeaponId.Rocketlauncher or TFWeaponId.Grenadelauncher or TFWeaponId.Pipebomblauncher;
}
