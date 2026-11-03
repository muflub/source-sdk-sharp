//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The server's <see cref="ITFPlayerResupply"/>: <c>CTFPlayer::Regenerate</c>
/// (tf_player.cpp:4014-4142), <c>GetMaxHealth</c>, <c>TakeHealth</c>
/// (:8419-8471), <c>GetMaxAmmo</c> (tf_player_shared.cpp:12998-13028) and
/// <c>GiveAmmo</c> (:14077) over the carrier's class and the core's player.
///
/// <para>
/// Regenerate keeps the inventory, as stock's <c>InitClass</c> →
/// <c>ManageRegularWeapons</c> does: the weapons held stay (an owned row
/// becomes its default clip in the reserve), the missing ones are given, and
/// every ammo is filled to its class maximum. The kept weapon's clip is the
/// default again, as the bResetWeapons arm's <c>GiveDefaultAmmo</c> is
/// (tf_player.cpp:5209). The fresh-kit strip is the spawn's
/// (<c>TFPlayerSpawns.OnSpawning</c>, stock's <c>ForceRespawn</c>).
/// The meters it refills (cloak, energy drink, charge, item meters) are not
/// on the carrier yet (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerResupply)])]
public sealed class TFPlayerResupply(TFPlayerCarriers carriers, TFClassTable classes, IEnginePlayers players,
                                     IEngineEntities entities, ITFPlayerConditions conditions,
                                     TFLoadoutProvider loadout, IEngineGameEvents events,
                                     ITFWeaponOwners? owners = null, AttributeHooks? attributes = null,
                                     TFWeaponScripts? scripts = null, WeaponCatalog? catalog = null,
                                     Arsenal? arsenal = null) : ITFPlayerResupply
{
    private readonly ITFWeaponOwners _owners = owners ?? new NoTFWeaponOwners();
    private readonly AttributeHooks _attributes = attributes ?? new AttributeHooks();
    private readonly TFWeaponScripts? _scripts = scripts;
    private readonly WeaponCatalog? _catalog = catalog;
    private readonly Arsenal? _arsenal = arsenal;

    /// <summary>The stock event a resupply fires.</summary>
    public const string RegenerateEvent = "player_regenerate";

    /// <summary>What <c>Regenerate( true )</c> ends, in stock's order (:4045-4093).</summary>
    public IReadOnlyList<TFCond> EndedByResupply { get; } =
    [
        TFCond.Burning, TFCond.Urine, TFCond.MadMilk, TFCond.Gas, TFCond.Bleeding, TFCond.EnergyBuff,
        TFCond.Phase, TFCond.ParachuteActive, TFCond.Plague,
    ];

    public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true)
    {
        if (ClassOf(player) is not { } cls)
            return;

        // "We may have been boosted over our max health": kept after InitClass.
        int oldMax = MaxHealth(player);
        int oldHealth = entities.GetHealth(player);
        bool boosted = (oldHealth > oldMax || !refillHealthAndAmmo) && oldMax > 0;

        // InitClass: the kit (GiveDefaultItems), then the health.
        if (refillHealthAndAmmo)
            GiveKit(player, cls);

        int max = classes[cls].MaxHealth;
        entities.SetHealth(player, boosted ? Math.Max(oldHealth, max) : max);

        // Regenerate's SetSpyCloakMeter( 100.0f ) (tf_player.cpp:4096).
        if (carriers.Get(player.EntryIndex) is { } state)
            state.CloakMeter = 100f;

        if (refillHealthAndAmmo)
        {
            foreach (var cond in EndedByResupply)
            {
                if (!conditions.InCond(player, cond))
                    continue;

                conditions.Remove(player, cond);
                if (cond == TFCond.EnergyBuff && conditions.InCond(player, TFCond.CannotSwitchFromMelee))
                    conditions.Remove(player, TFCond.CannotSwitchFromMelee);
            }
        }

        IntPtr e = events.EventCreate(RegenerateEvent);
        if (e != IntPtr.Zero)
            events.EventFire(e);
    }

    /// <summary>
    /// The class's missing weapons and every ammo to its maximum, the
    /// inventory kept: a regen runs inside <c>m_bRegenerating</c>
    /// (tf_player.cpp:4033-4036), whose <c>InitClass</c> →
    /// <c>ManageRegularWeapons</c> keeps each weapon whose item still matches
    /// and gives only the missing ones (:4528-4662), resets it
    /// (<c>bResetWeapons</c>, :5205-5220), and the same guard skips the
    /// closing <c>Weapon_Switch</c> (:4700) — the locker draws nothing.
    /// The strip is the spawn's (<see cref="TFPlayerSpawns.OnSpawning"/>).
    /// </summary>
    internal void GiveKit(EntityHandle player, TFClass cls)
    {
        foreach (string weapon in loadout.Weapons(player))
        {
            players.GiveItem(player, weapon);
            GiveDefaultAmmo(player, weapon);
        }

        foreach (var (ammo, count) in loadout.Ammo(player))
            players.GiveAmmo(player, count, ammo);
    }

    /// <summary>
    /// The kept weapon's <c>GiveDefaultAmmo</c> (<c>bResetWeapons</c>' arm,
    /// tf_player.cpp:5209): the clip is the default again —
    /// <c>CBaseCombatWeapon::GiveDefaultAmmo</c>
    /// (basecombatweapon_shared.cpp:144-165) over <c>CTFWeaponBase</c>'s own
    /// <c>GetDefaultClip1</c> (tf_weaponbase.cpp:631-635) — written through
    /// the same seam the dropped-weapon pickup writes its restored clip by.
    /// The clipless arm's reserve default and the energy refill
    /// (tf_weaponbase.cpp:436-439) are the pool fill that follows and the
    /// port's no-energy-weapon state (TFDroppedWeapon's m_nClip note); no row
    /// here keeps a second clip. A weapon the kit just gave took the give's
    /// own default, so the reset only settles what a held life fired down.
    /// </summary>
    private void GiveDefaultAmmo(EntityHandle player, string weapon)
    {
        if (_scripts?.Get(weapon) is not { } script
            || _catalog?.For(weapon) is not { } type
            || _arsenal is null
            || type.ClipSize <= 0)
            return;

        // Stock reads the weapon's own attributes where the hooks do
        // (AutoFiresFullClip, tf_weaponbase.cpp:1871-1877, and GetMaxClip1's
        // mult_clipsize, :571-575 — the attribute arm of
        // TFAttributeWeaponHook): the item behind the named weapon, the
        // player's manager beside it.
        int index = entities.GetIndex(player);
        IAttributeProvider? item = _owners.ItemOf(index, weapon);

        int clip = script.Base.DefaultClip1 <= 0 || _attributes.Int(0, "auto_fires_full_clip", item) != 0
            ? 0
            : _attributes.Int(type.ClipSize, "mult_clipsize", item);

        _arsenal.SetClip(player, weapon, clip);
    }

    /// <summary>
    /// <c>CTFPlayer::GetMaxHealth</c> (tf_player.cpp:14241-14249) over
    /// <c>GetMaxHealthForBuffing</c>'s attribute arm (:14266-14269): the class
    /// maximum, then <c>add_maxhealth</c> and <c>add_maxhealth_nonbuffed</c>,
    /// both the player's own walk, floored at one. The active weapon's
    /// <c>GetMaxHealthMod</c> is zero for every weapon this SDK declares
    /// (tf_weaponbase.h:492, no overrides) and the sword's decapitations and
    /// the runes have no counters yet (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public int MaxHealth(EntityHandle player)
    {
        if (ClassOf(player) is not { } cls)
            return 0;

        int index = entities.GetIndex(player);
        int max = _attributes.Int(classes[cls].MaxHealth, "add_maxhealth", _owners.ManagerOf(index));
        max = _attributes.Int(max, "add_maxhealth_nonbuffed", _owners.ManagerOf(index));
        return Math.Max(max, 1);
    }

    public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false)
    {
        if (ClassOf(player) is null || conditions.InCond(player, TFCond.Nohealingdamagebuff))
            return 0;

        // The active weapon's mult_healing_received scales the heal before the
        // clamps (tf_player.cpp:8428-8434): the walk rooted at the weapon's
        // item, whose owner is the wearer's manager.
        float toHeal = health;
        if (players.GetActiveWeapon(player) is { Length: > 0 } weapon
            && _owners.ItemOf(entities.GetIndex(player), weapon) is { } item)
            toHeal = _attributes.Float(toHeal, "mult_healing_received", item);

        int current = entities.GetHealth(player);
        if (ignoreMaxHealth)
        {
            int added = (int)toHeal;
            entities.SetHealth(player, current + added);
            return added;
        }

        // CBaseEntity::TakeHealth under CTFPlayer's clamp to the class max.
        int max = MaxHealth(player);
        float toAdd = Math.Min(toHeal, max - current);
        if (toAdd <= 0f || current >= max)
            return 0;

        int healed = Math.Min(current + (int)toAdd, max);
        entities.SetHealth(player, healed);
        return healed - current;
    }

    public int MaxAmmo(EntityHandle player, TFAmmo ammo)
    {
        if (ClassOf(player) is not { } cls)
            return 0;

        // TFAmmoCarryRule's answer: every class carries one Grenades3, and the
        // four hooked rows ride the player's own attribute manager.
        int classMax = ammo == TFAmmo.Grenades3 ? 1 : classes[cls].MaxAmmo(ammo);
        return TFMaxAmmoHooks.Apply(_attributes, _owners, entities.GetIndex(player), ammo, classMax);
    }

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo)
    {
        if (count <= 0 || ClassOf(player) is null)
            return 0;

        // The core's pool clamps to the carry rule, which is the class's AmmoMax.
        return players.GiveAmmo(player, count, TFAmmoNames.Name(ammo));
    }

    /// <summary>The player's class when they have one (Scout..Engineer, or Civilian), else null.</summary>
    private TFClass? ClassOf(EntityHandle player)
    {
        TFClass cls = carriers.ClassOf(player);
        return cls is > TFClass.Undefined and < TFClass.CountAll ? cls : null;
    }
}
