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
/// <c>weaponN</c> the class scripts name (<see cref="TFClassTable"/>), the
/// engineer's builder, the spy's sapper and the item-carried weapons seeded
/// below, ordered by name so both sides number them alike (the index is the
/// wire format).
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

    /// <summary>The Gunslinger's entity class, the item's own row (items_working.tsv:58): the held wrench the mini-sentry attribute is asked of.</summary>
    public const string WrenchArm = "tf_weapon_robot_arm";

    /// <summary>
    /// The energy drink's entity class (<c>LINK_ENTITY_TO_CLASS(
    /// tf_weapon_lunchbox_drink, CTFLunchBox_Drink )</c>,
    /// tf_weapon_lunchbox.cpp:56): Bonk!, Crit-a-Cola and the festive Bonk are
    /// its rows. The class has no id of its own — stock's
    /// <c>CTFLunchBox::GetWeaponID</c> answers <c>TF_WEAPON_LUNCHBOX</c> for
    /// the whole family (tf_weapon_lunchbox.h:56) — so the drink carries the
    /// lunchbox kind; only the script name and the row are its own.
    /// </summary>
    public const string Drink = "tf_weapon_lunchbox_drink";

    /// <summary>
    /// The Half-Zatoichi's entity class (<c>LINK_ENTITY_TO_CLASS(
    /// tf_weapon_katana, CTFKatana )</c>, tf_weapon_sword.cpp:50): the
    /// soldier's and demoman's honourbound duel melee. Like the drink, the
    /// class has no id of its own — <c>CTFKatana</c> overrides no
    /// <c>GetWeaponID</c>, inheriting the decap base's <c>TF_WEAPON_SWORD</c>
    /// (tf_weapon_sword.h:45) — so the katana carries the sword kind; only
    /// the script name and the row are its own.
    /// </summary>
    public const string Katana = "tf_weapon_katana";

    private readonly TFWeaponScripts _scripts;
    private readonly TFClassTable _classes;
    private readonly ItemSchema _schema;

    public TFWeaponTypes(TFWeaponScripts scripts, TFClassTable classes, ItemSchema schema)
    {
        _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    /// <summary>
    /// The catalogued weapon names, ordered: each class's weapons, the builder
    /// and the sapper, the seeded item-carried weapons, and every class an item
    /// of the schema names by its <c>item_class</c> (stock gives an equipped
    /// item its own weapon entity — <c>ManageRegularWeapons</c> over
    /// <c>GetLoadoutItem</c>, tf_player.cpp:4496 — so the kit's give only hands
    /// the unlock out when the name has a row; a class no script backs still
    /// gets none, which <see cref="Rows"/>' script filter keeps).
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal)
        {
            // The Huntsman: no class script's weaponN (an unlock's loadout
            // item), but the bow's behaviour is ported, so it has a row.
            TFWeaponNames.Name(TFWeaponId.CompoundBow).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.Builder).ToLowerInvariant(),
            Sapper,
            // The flare gun is no class script's weaponN: the pyro holds it only
            // as an equipped item, whose class is the entity stock names
            // (tf_weapon_flaregun, the item's own row). The kit's spawn gate
            // reads this table, so the item-carried weapons seed here as they
            // land — the catalogue cannot learn them from the class scripts.
            TFWeaponNames.Name(TFWeaponId.Flaregun).ToLowerInvariant(),
            // The Manmelter: the pyro's vacuum, the flare gun's revenge shell
            // (CTFFlareGun_Revenge) — the same equipped-item row the flare gun
            // is, its behaviour the projectile's behind the beam.
            TFWeaponNames.Name(TFWeaponId.FlaregunRevenge).ToLowerInvariant(),
            // The Frontier Justice: the engineer's revenge shotgun
            // (CTFShotgun_Revenge), the shotgun shell's revenge row — the same
            // equipped-item seed the Manmelter is, its bookends the hands'.
            TFWeaponNames.Name(TFWeaponId.SentryRevenge).ToLowerInvariant(),
            // The Crusader's Crossbow: the medic's unlock, held only as an
            // equipped item — no class script's weaponN names it either, and
            // its behaviour is the projectile's, so it seeds beside the
            // flare gun.
            TFWeaponNames.Name(TFWeaponId.Crossbow).ToLowerInvariant(),
            // The Gunslinger: the engineer's unlock wrench, whose entity class
            // is the item's own row (items_working.tsv:58). No class script's
            // weaponN names it either, so it seeds beside the flare gun.
            WrenchArm,
            // The lunchbox the same way: the heavy holds it as the loadout's
            // secondaries slot (the sandvich), no class script's weaponN.
            TFWeaponNames.Name(TFWeaponId.Lunchbox).ToLowerInvariant(),
            // The soldier's buff items: Buff Banner, Battalion's Backup and the
            // Concheror all carry the tf_weapon_buff_item class
            // (items_working.tsv:51, :100, :128); the soldier class script's
            // weaponN's name the shovel, the shotgun and the launcher, so the
            // buff item is the third held-everything item and seeds here too.
            TFWeaponNames.Name(TFWeaponId.BuffItem).ToLowerInvariant(),
            // The jars: the sniper's and the thrown milk, held as equipped
            // items too (tf_weapon_jar, tf_weapon_jar_milk — the CTFJar pair
            // whose shot is the effect bar).
            TFWeaponNames.Name(TFWeaponId.Jar).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.JarMilk).ToLowerInvariant(),
            // The gas face of the jars: the pyro's Gas Passer
            // (items_working.tsv:330), the CTFJarGas equipped item whose
            // class is its own row, no class script's weaponN names it.
            TFWeaponNames.Name(TFWeaponId.JarGas).ToLowerInvariant(),
            // The Wrangler: the engineer's second unlock, held as the loadout
            // item whose triggers drive the sentry (tf_weapon_laser_pointer,
            // the item's own row).
            TFWeaponNames.Name(TFWeaponId.LaserPointer).ToLowerInvariant(),
            // The sniper's unlock rifles the same way: the Bazaar Bargain and
            // the Classic are equipped items whose classes are their own
            // entities (tf_weapon_sniperrifle_decap, tf_weapon_sniperrifle_
            // classic — items_working.tsv:133, :309), no class script's
            // weaponN names either.
            TFWeaponNames.Name(TFWeaponId.SniperrifleDecap).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.SniperrifleClassic).ToLowerInvariant(),
            // The Cleaner's Carbine: the sniper's unlock SMG, held as the
            // equipped item whose class is its own row
            // (tf_weapon_charged_smg, items_working.tsv:751) — no class
            // script's weaponN names it (the stock secondary is the plain
            // smg), so it seeds with its rifle cousins. Its SecondaryAttack
            // boost lives in TFWeaponBehaviour.
            TFWeaponNames.Name(TFWeaponId.ChargedSmg).ToLowerInvariant(),
            // The scout's energy drink, the lunchbox family's own script name:
            // Bonk! and cousins are equipped items, no class script's weaponN
            // names them either, so the drink seeds beside its plate.
            Drink,
            // The decapitating melee (the Eyelander and its skins, items_working
            // rows 132, 172, 266, 327, 404, 482, 1082): the demoman holds it as
            // an equipped item over the bottle, no class script's weaponN.
            TFWeaponNames.Name(TFWeaponId.Sword).ToLowerInvariant(),
            // The Dragon's Fury: the pyro's unlock, held as the equipped item
            // whose class is the entity stock binds the weapon to
            // (tf_weapon_dragons_fury.cpp:42) — the item's own row, whose id
            // answer rides the GetWeaponID override (tf_weapon_dragons_fury.h:45)
            // rather than the misspelt g_aWeaponNames spelling, so the entity
            // name is the seed, not the id's Name() key.
            "tf_weapon_rocketlauncher_fireball",
            // The Wrap Assassin: the scout's bat with a ball of its own (CTFBat_Giftwrap).
            TFWeaponNames.Name(TFWeaponId.BatGiftwrap).ToLowerInvariant(),
            // The Half-Zatoichi (items_working.tsv:357) the same way: the
            // soldier's and demoman's unlock, held as the loadout item whose
            // class is the katana's own row.
            Katana,
            // The energy family: the soldier's bison and Cow Mangler and the
            // engineer's pomson, held as equipped unlocks whose entity classes
            // are their own rows (items_working.tsv:441, :442, :588). No class
            // script's weaponN names them, so they seed beside the jars.
            TFWeaponNames.Name(TFWeaponId.Raygun).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.ParticleCannon).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.DrgPomson).ToLowerInvariant(),
            // The Air Strike: the soldier's airdrop primary
            // (items_working.tsv:315), held as the equipped item whose script
            // is its own row — stock's GetWeaponId knows no id for it
            // (tf_shareddefs.cpp:1206-1217), so it seeds by its script
            // spelling, the way the sapper and the Gunslinger do.
            "tf_weapon_rocketlauncher_airstrike",
            // The scout's hype pair (items_working.tsv:448, :772): the Soda
            // Popper and the Baby Face's Blaster, equipped items whose class
            // is their own row — no class script's weaponN names either.
            TFWeaponNames.Name(TFWeaponId.SodaPopper).ToLowerInvariant(),
            TFWeaponNames.Name(TFWeaponId.PepBrawlerBlaster).ToLowerInvariant(),
            // The Flying Guillotine and its Promo skin (items_working.tsv:222, :226):
            // the scout's thrown cleaver, held as the equipped item whose class is
            // its own row (tf_weapon_cleaver, stock's TF_WEAPON_CLEAVER) — no class
            // script's weaponN names it either.
            TFWeaponNames.Name(TFWeaponId.Cleaver).ToLowerInvariant(),
            // The Shortstop (items_working.tsv:220): the scout's four-pellet
            // unlock primary, held as the equipped item whose class is its own
            // row (CTFPistol_ScoutPrimary, tf_weapon_handgun_scout_primary) —
            // no class script's weaponN names it; the shove lives in
            // TFWeaponBehaviour's alt-fire arm.
            TFWeaponNames.Name(TFWeaponId.HandgunScoutPrimary).ToLowerInvariant(),
            // The Thermal Thruster: the soldier's backpack unlock, held as the
            // equipped item whose class is its own row
            // (items_working.tsv:329), no class script's weaponN names either.
            TFWeaponNames.Name(TFWeaponId.Rocketpack).ToLowerInvariant(),
            // The Short Circuit (items_working.tsv:164): the engineer's
            // unlock secondary, whose trigger is the shock attack the orb
            // entity answers (CTFMechanicalArm, tf_weapon_mechanical_arm) —
            // the equipped-item seed the Manmelter is.
            TFWeaponNames.Name(TFWeaponId.MechanicalArm).ToLowerInvariant(),
            // The Rescue Ranger (items_working.tsv:997): the engineer's shotgun
            // whose shot is the repair bolt (CTFShotgunBuildingRescue) — the
            // equipped item whose class is its own row, no class script's
            // weaponN naming it.
            TFWeaponNames.Name(TFWeaponId.ShotgunBuildingRescue).ToLowerInvariant(),
        };

        for (var c = TFClass.Scout; c < TFClass.CountAll; c++)
        {
            foreach (var id in _classes[c].Weapons)
            {
                if (id != TFWeaponId.None)
                    names.Add(TFWeaponNames.Name(id).ToLowerInvariant());
            }
        }

        // An equipped unlock is given by its own entity name: the kit hands out
        // each slot item's item_class, translated to the class's own spelling
        // (ManageRegularWeapons over GetLoadoutItem, tf_player.cpp:4496), so a
        // class the schema names is catalogued too — but only as a real weapon:
        // the schema also names the cosmetic classes (bundle, craft_item,
        // tf_wearable), classes no script backs, and classes whose script runs
        // no behaviour of its own (the Inert arm of TFWeaponKinds.Of). Each
        // row costs a bit of the carried-weapon mask
        // (PlayerNetState.WeaponMaskBits), so a name without a running weapon
        // behind it is refused here, read quiet: a miss is an absent row, not
        // a finding. What falls out of the catalogue falls back to the slot's
        // base item when equipped (TFLoadoutProvider.Weapons).
        foreach (var item in _schema.Items)
        {
            if (item.ItemClass is not { Length: > 0 } itemClass
                || !itemClass.StartsWith("tf_weapon_", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = itemClass.ToLowerInvariant();
            if (_scripts.GetQuiet(name) is { } script
                && TFWeaponKinds.Of(script) != TFWeaponKind.Inert)
                names.Add(name);
        }

        return names.ToArray();
    }

    public IEnumerable<WeaponType> Rows()
    {
        foreach (string name in Names())
        {
            // Quiet like the schema walk above: in the catalogue a miss is an
            // absent row, not a finding — the shipped scripts for every seed
            // name and class weapon are there, and a fixture without one of
            // them is no reason to raise a line per missing name.
            if (_scripts.GetQuiet(name) is { } script)
                yield return Row(script, BaseItemFor(name));
        }
    }

    /// <summary>The row a script makes, dressed in <paramref name="item"/>'s models when there is one.</summary>
    public static WeaponType Row(TFWeaponScript script, ItemDefinition? item)
    {
        ArgumentNullException.ThrowIfNull(script);

        var mode = script.Primary;
        bool melee = TFWeaponKinds.Of(script) == TFWeaponKind.Melee;

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
    /// the per-class spellings stock's loadout translates to). A class no
    /// base item wears at all — the later unlocks, plain items like the
    /// Huntsman's <c>tf_weapon_compound_bow</c> — takes its lowest defindex
    /// instead: the canonical look the base item is for the launch weapons
    /// (stock reads the equipped item per instance, <c>CTFWeaponBase::GetViewModel</c>,
    /// tf_weaponbase.cpp:651-676; the row is one dress for the class).
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

        if (best is null)
        {
            foreach (var item in ItemsByDefindex())
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
        }

        return best;
    }

    private ItemDefinition[]? _itemsByDefindex;

    /// <summary>Every definition by rising defindex: the fallback walk's order.</summary>
    private ItemDefinition[] ItemsByDefindex() =>
        _itemsByDefindex ??= _schema.Items.OrderBy(i => i.Defindex).ToArray();

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
