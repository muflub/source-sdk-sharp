//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Econ;

/// <summary>
/// <c>TranslateWeaponEntForClass</c> (declared tf_shareddefs.h:1820, called
/// tf_player.cpp:4387 and :5544): the weapon entity an item's
/// <c>item_class</c> becomes for a class. One item serves several classes —
/// the base shotgun (<c>tf_weapon_shotgun</c>) is the soldier's, the heavy's,
/// the pyro's and the engineer's secondary or primary, the base pistol
/// (<c>tf_weapon_pistol</c>) the scout's and the engineer's — and each class
/// fires its own entity with its own script.
///
/// <para>
/// THE SDK HAS NO BODY for it (the table is in a library the SDK does not
/// ship), so the rule here is read from the class scripts, which name each
/// class's entities (<c>weapon1..N</c>, <see cref="TFClassData.Weapons"/>):
/// an item class the class's script names itself is that entity; else the
/// class's entity named <c>&lt;item class&gt;_&lt;suffix&gt;</c>
/// (<c>tf_weapon_shotgun</c> → <c>tf_weapon_shotgun_soldier</c>,
/// <c>_hwg</c>, <c>_pyro</c>, <c>_primary</c>; <c>tf_weapon_pistol</c> →
/// <c>tf_weapon_pistol_scout</c>); else the item class unchanged. Every base
/// item of the milestone translates to a weapon row this way. The
/// multi-class unlocks whose entity no class script names (the saxxy, the
/// shovel and bottle swap, the parachute, the engineer's revolver, the
/// throwables) keep their item class: a deferred row
/// (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
public static class TFItemClassTranslation
{
    /// <summary>The weapon entity <paramref name="itemClass"/> is for <paramref name="playerClass"/>.</summary>
    public static string Translate(string itemClass, TFClassData playerClass)
    {
        ArgumentNullException.ThrowIfNull(itemClass);
        ArgumentNullException.ThrowIfNull(playerClass);

        string? suffixed = null;
        foreach (var id in playerClass.Weapons)
        {
            if (id == TFWeaponId.None)
                continue;

            string name = TFWeaponNames.Name(id).ToLowerInvariant();
            if (string.Equals(name, itemClass, StringComparison.OrdinalIgnoreCase))
                return name;

            if (suffixed is null && name.Length > itemClass.Length + 1
                && name.StartsWith(itemClass, StringComparison.OrdinalIgnoreCase) && name[itemClass.Length] == '_')
            {
                suffixed = name;
            }
        }

        // The builder family's retarget (ManageBuilderWeapons's SetSubType,
        // tf_player.cpp:4309-4316, :4627-4631): the builder item in a class
        // whose buildable is an attachment is the sapper row — the shell that
        // carries the class's buildable, its place the sapper's.
        if (suffixed is null
            && string.Equals(itemClass, TFWeaponNames.Name(TFWeaponId.Builder).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            && playerClass.Buildables.Contains(TFObjectType.AttachmentSapper))
        {
            return TFWeaponTypes.Sapper;
        }

        return suffixed ?? itemClass;
    }
}
