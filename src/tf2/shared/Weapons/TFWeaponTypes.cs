//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Anim;
using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// TF2's weapon rows (stage1 1.9): one <see cref="WeaponType"/> per milestone
/// weapon, read from its stock script (<see cref="TFWeaponScripts"/>) and
/// dressed from the item schema. The milestone weapons are every
/// <c>weaponN</c> the class scripts name (<see cref="TFClassTable"/>) plus the
/// engineer's builder and the spy's sapper, ordered by name so both sides
/// number them alike (the index is the wire format).
///
/// <para>
/// A row carries the script's primary mode in the core's terms: the fire
/// interval is <c>TimeFireDelay</c>, the pellets <c>BulletsPerShot</c>, the
/// spread the script's cone (a sine, <c>FireBulletsInfo_t::m_vecSpread</c>)
/// as the core's degrees, the clip <c>clip_size</c>, the ammo
/// <c>primary_ammo</c>, the deploy stock's 0.5 s (<c>CTFWeaponBase::Deploy</c>,
/// <c>tf_weaponbase.cpp:1255</c>), a melee weapon's reach
/// <c>GetSwingRange</c>'s 48 (<c>tf_weaponbase_melee.cpp:164-180</c>) and its
/// damage the script's. The TF numbers the core has no column for (rapid-fire
/// crits, reload times, the smack delay) the behaviour reads from the script.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponTypes : IWeaponTypeSource
{
    /// <summary><c>CTFWeaponBase::Deploy</c>'s <c>flWeaponSwitchTime</c>.</summary>
    public const float DeployTime = 0.5f;

    /// <summary><c>CTFWeaponBaseMelee::GetSwingRange</c>'s plain answer.</summary>
    public const float SwingRange = 48f;

    /// <summary>The spy's sapper: <c>LINK_ENTITY_TO_CLASS( tf_weapon_sapper, CTFWeaponSapper )</c>, a builder with its own script.</summary>
    public const string Sapper = "tf_weapon_sapper";

    private readonly TFWeaponScripts _scripts;
    private readonly TFClassTable _classes;
    private readonly ItemSchema _schema;

    public TFWeaponTypes(TFWeaponScripts scripts, TFClassTable classes, ItemSchema schema)
    {
        _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    /// <summary>The milestone weapon names, ordered: each class's weapons, the builder and the sapper.</summary>
    public IReadOnlyList<string> Names()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal)
        {
            // The Huntsman: no class script's weaponN (an unlock's loadout
            // item), but the bow's behaviour is ported, so it has a row.
            TFWeaponNames.Name(TFWeaponId.CompoundBow).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.Builder).ToLowerInvariant(),
            Sapper,
        };

        for (var c = TFClass.Scout; c < TFClass.CountAll; c++)
        {
            foreach (var id in _classes[c].Weapons)
            {
                if (id != TFWeaponId.None)
                    names.Add(TFWeaponNames.Name(id).ToLowerInvariant());
            }
        }

        return names.ToArray();
    }

    public IEnumerable<WeaponType> Rows()
    {
        foreach (string name in Names())
        {
            if (_scripts.Get(name) is { } script)
                yield return Row(script, BaseItemFor(name));
        }
    }

    /// <summary>The row a script makes, dressed in <paramref name="item"/>'s models when there is one.</summary>
    public static WeaponType Row(TFWeaponScript script, ItemDefinition? item)
    {
        ArgumentNullException.ThrowIfNull(script);

        var mode = script.Primary;
        bool melee = script.Category == TFWeaponCategory.Melee;

        return new WeaponType(script.Name, Interval: mode.TimeFireDelay,
                              Pellets: Math.Max(1, mode.BulletsPerShot),
                              Spread: SpreadDegrees(mode.Spread))
        {
            ClipSize = script.Base.MaxClip1,
            AmmoName = script.Base.Ammo1,
            ViewModel = item?.ModelPlayer ?? "",
            WorldModel = item?.ModelWorld ?? item?.ModelPlayer ?? "",
            AttachedModels = item?.AttachedModels.ToImmutableArray() ?? ImmutableArray<WeaponAttachedModel>.Empty,
            DeployTime = DeployTime,
            FireSound = script.Sound(WeaponScriptSound.Single),
            // The minigun never clicks: CTFMinigun::ItemPostFrame pushes its
            // empty-sound time a second ahead every frame (tf_weapon_minigun.cpp:193).
            EmptySound = script.Id == TFWeaponId.Minigun ? "" : script.Sound(WeaponScriptSound.Empty),
            ReloadSound = script.Sound(WeaponScriptSound.Reload),
            // The row carries the whole SoundData table for the precache
            // (CBaseCombatWeapon::Precache), not only the three the behaviours
            // play by field: the burst crit, a taunt, the rest.
            Sounds = script.Base.Sounds,
            MeleeRange = melee ? SwingRange : 0f,
            MeleeDamage = melee ? mode.Damage : 0f,
            Behaviour = TFWeaponKinds.Key(TFWeaponKinds.Of(script)),
            // A TF weapon dropped leaves NO ground entity: the row's name is
            // the item class, and TF2 registers no entity class under it
            // (dm_lockdown's rows are CBaseEntity classes; these are not).
            // Stock's death drop is the ammo pack — CTFPlayer::Event_Killed's
            // DropAmmoPack (tf_player.cpp:13029-13112), which TFDeathDrops
            // owns — never the weapon itself, and TF2 has no drop-the-weapon
            // action at all (stock's dropitem drops the carried intel). So the
            // core's create-the-row-name-by-pickup arm (Arsenal.DropActive)
            // must never fire for a TF row, whatever toggles its drop.
            RemovedOnDrop = true,
        };
    }

    /// <summary>
    /// A script's spread as the core's cone in degrees: the script's value is
    /// the sine of the half-angle (stock spreads a bullet by
    /// <c>x * spread * right + y * spread * up</c>), the core's
    /// <c>FireBullets</c> takes the angle whose sine it uses.
    /// </summary>
    public static float SpreadDegrees(float spread) =>
        spread <= 0f ? 0f : MathF.Asin(MathF.Min(spread, 1f)) * 180f / MathF.PI;

    /// <summary>
    /// The base item a weapon name wears: the schema's base item whose
    /// <c>item_class</c> is the name, else the longest whose class is the
    /// name's stem (<c>tf_weapon_shotgun</c> for <c>tf_weapon_shotgun_soldier</c>,
    /// the per-class spellings stock's loadout translates to).
    /// </summary>
    public ItemDefinition? BaseItemFor(string weaponName)
    {
        ItemDefinition? best = null;
        foreach (var item in _schema.BaseItems)
        {
            if (item.ItemClass is not { Length: > 0 } itemClass)
                continue;
            if (string.Equals(itemClass, weaponName, StringComparison.OrdinalIgnoreCase))
                return item;
            if (weaponName.Length > itemClass.Length && weaponName[itemClass.Length] == '_'
                && weaponName.StartsWith(itemClass, StringComparison.OrdinalIgnoreCase)
                && (best?.ItemClass is null || itemClass.Length > best.ItemClass.Length))
                best = item;
        }

        // The sapper's dress is the builder family's item (ManageBuilderWeapons
        // retypes the shell, SetSubType, tf_player.cpp:4627-4631): the builder
        // item the attachment-building class wears — the spy's
        // TF_WEAPON_BUILDER_SPY, c_sapper.
        if (string.Equals(weaponName, Sapper, StringComparison.OrdinalIgnoreCase))
        {
            string builder = TFWeaponNames.Name(TFWeaponId.Builder).ToLowerInvariant();
            foreach (var item in _schema.BaseItems)
            {
                if (string.Equals(item.ItemClass, builder, StringComparison.OrdinalIgnoreCase)
                    && item.UsedByClasses.Any(c => _classes[c].Buildables.Contains(TFObjectType.AttachmentSapper)))
                {
                    return item;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// <c>CTFWeaponBase::GetActivityWeaponRole</c> (tf_weaponbase.cpp:4185-4205)
    /// minus its cheat-convar arm: the script's type, overridden by the base
    /// item the name wears when its <c>anim_slot</c> names a row
    /// (<c>&gt;= 0</c> — <see cref="ItemDefinition.AnimationSlot"/>). Both
    /// consumers ask this role: the body tables through <c>ActivityList</c>
    /// (:4210) and the viewmodel translation, whose own re-derivation
    /// (:4523-4531) lands on the same answer.
    /// </summary>
    public TFWeaponRole ActivityWeaponRole(string weaponName, TFWeaponRole scriptRole)
    {
        int slot = BaseItemFor(weaponName)?.AnimationSlot ?? -1;
        return slot >= 0 ? (TFWeaponRole)slot : scriptRole;
    }
}
