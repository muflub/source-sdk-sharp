//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// TF2's ammo rows: <c>GetAmmoDef</c> (<c>shared/tf/tf_gamerules.cpp:18369-18390</c>),
/// one <c>AddAmmoType</c> per <c>TF_AMMO_PRIMARY .. TF_AMMO_GRENADES3</c> under
/// its <c>g_aAmmoNames</c> spelling, every one <c>DMG_BULLET |
/// DMG_USEDISTANCEMOD | DMG_NOCLOSEDISTANCEMOD</c> with a line tracer and a
/// physics impulse of 2400, no damage of its own (a TF weapon's damage is its
/// script's) and the carry limit of the <c>ammo_max</c> convar's default, 5000
/// (<c>tf_gamerules.cpp:2039</c>). What a player really carries is the class's
/// <c>AmmoMax</c>, which <see cref="TFAmmoCarryRule"/> answers.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAmmoTypes : IAmmoTypeSource
{
    /// <summary>The <c>ammo_max</c> convar's default: the row's limit before the class's.</summary>
    public const int AmmoMax = 5000;

    /// <summary><c>AddAmmoType</c>'s physics force impulse.</summary>
    public const float PhysicsForceImpulse = 2400f;

    /// <summary><c>TRACER_LINE</c> (<c>shared/ammodef.h</c>).</summary>
    public const int TracerLine = 1;

    /// <summary>
    /// <c>DMG_BULLET | DMG_USEDISTANCEMOD | DMG_NOCLOSEDISTANCEMOD</c>: TF aliases
    /// the two modifiers onto <c>DMG_SLOWBURN</c> (1 &lt;&lt; 21) and <c>DMG_POISON</c>
    /// (<c>tf_shareddefs.h:1167-1168</c>, <c>shareddefs.h:463</c>).
    /// </summary>
    public const DamageType BulletDamage = DamageType.Bullet | (DamageType)(1 << 21) | DamageType.Poison;

    public IEnumerable<AmmoType> Rows()
    {
        for (var ammo = TFAmmo.Primary; ammo < TFAmmo.Count; ammo++)
        {
            yield return new AmmoType(TFAmmoNames.Name(ammo), BulletDamage, AmmoMax)
            {
                TracerType = TracerLine,
                DamageForce = PhysicsForceImpulse,
            };
        }
    }

    /// <summary>The TF ammo a row name is, or null for a row that is not TF's.</summary>
    public static TFAmmo? Of(string ammoName)
    {
        for (var ammo = TFAmmo.Primary; ammo < TFAmmo.Count; ammo++)
        {
            if (string.Equals(ammoName, TFAmmoNames.Name(ammo), StringComparison.Ordinal))
                return ammo;
        }
        return null;
    }
}

/// <summary>
/// <c>CTFPlayer::GetMaxAmmo</c>'s attribute-hook arm
/// (<c>shared/tf/tf_player_shared.cpp:13001-13016</c>): the four hook classes
/// (<c>mult_maxammo_primary/secondary/metal/grenades1</c>) the ammo index has,
/// and the walk of the player's own attribute manager over them. The
/// <c>GRENADES3</c> arm carries no hook (it is pinned to one), and the
/// <c>SECONDARY</c>..<c>GRENADES3</c> rows stock never hooks have none here.
///
/// <para>
/// Stock's <c>CALL_ATTRIB_HOOK_INT(iMax, hook)</c> passes <c>this</c> — the
/// <c>CTFPlayer</c>, whose <c>IHasAttributes</c> answers
/// <c>GetAttributeManager()</c> = <c>&amp;m_AttributeManager</c> and
/// <c>GetAttributeContainer()</c>/<c>GetAttributeOwner()</c> = <c>NULL</c>
/// (<c>server/tf/tf_player.h:637-642</c>): the walk is the player's own
/// manager, never the active weapon's item.
/// </para>
/// </summary>
public static class TFMaxAmmoHooks
{
    /// <summary>
    /// The hook class stock runs for this ammo index, or null for the rows it
    /// leaves unhooks (<c>Grenades3</c> and the <c>Dummy</c>/<c>Grenades2</c>
    /// rows stock never touches).
    /// </summary>
    public static string? Of(TFAmmo ammo) => ammo switch
    {
        TFAmmo.Primary => "mult_maxammo_primary",
        TFAmmo.Secondary => "mult_maxammo_secondary",
        TFAmmo.Metal => "mult_maxammo_metal",
        TFAmmo.Grenades1 => "mult_maxammo_grenades1",
        _ => null,
    };

    /// <summary>
    /// The class maximum run through the ammo's hook class over the player's
    /// own attribute manager (<see cref="ITFWeaponOwners.ManagerOf"/>); an
    /// unhooked row, or a player with no manager, keeps the class maximum.
    /// </summary>
    public static int Apply(AttributeHooks attributes, ITFWeaponOwners owners, int playerIndex,
                            TFAmmo ammo, int classMax)
        => Of(ammo) is { } hook
            ? attributes.Int(classMax, hook, owners.ManagerOf(playerIndex))
            : classMax;
}

/// <summary>
/// <c>CTFPlayer::GetMaxAmmo</c> (<c>shared/tf/tf_player_shared.cpp:12998-13028</c>)
/// as an <see cref="IAmmoCarryRule"/>: a TF ammo row's limit is the holder's
/// class <c>m_aAmmoMax</c>, except <c>TF_AMMO_GRENADES3</c>, which every class
/// carries one of, and the four hooked rows ride the player's own attribute
/// manager (<see cref="TFMaxAmmoHooks"/>). A player with no class (no carrier
/// yet, or between lives) and a row that is not TF's keep the limit handed in.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAmmoCarryRule(TFClassTable classes, ITFWeaponOwners? owners = null,
                                    AttributeHooks? attributes = null) : IAmmoCarryRule, IOrderedHook
{
    /// <summary>The rule's order: TF2's layer, below the mods' range (see <see cref="TFClassTable.LoadOrder"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 20;

    private readonly ITFWeaponOwners _owners = owners ?? new NoTFWeaponOwners();
    private readonly AttributeHooks _attributes = attributes ?? new AttributeHooks();

    public int HookOrder => Order;

    public int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry)
    {
        if (TFAmmoTypes.Of(ammo.Name) is not { } tfAmmo)
            return maxCarry;

        var playerClass = _owners.ClassOf(playerIndex);
        if (playerClass <= TFClass.Undefined || playerClass >= TFClass.CountAll)
            return maxCarry;

        int classMax = tfAmmo == TFAmmo.Grenades3 ? 1 : classes[playerClass].MaxAmmo(tfAmmo);
        return TFMaxAmmoHooks.Apply(_attributes, _owners, playerIndex, tfAmmo, classMax);
    }
}
