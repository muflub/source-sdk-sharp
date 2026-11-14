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
/// The item bars it leaves regen-finished are the kit's own
/// (<see cref="FillEffectBar"/>); the charge and energy meters are not on
/// the carrier yet (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerResupply)])]
public sealed class TFPlayerResupply(TFPlayerCarriers carriers, TFClassTable classes, IEnginePlayers players,
                                     IEngineEntities entities, ITFPlayerConditions conditions,
                                     TFLoadoutProvider loadout, IEngineGameEvents events,
                                     TFPlayerItems items, AttributeHooks attributes,
                                     TFWeaponScripts scripts, WeaponCatalog catalog,
                                     Arsenal arsenal, TFMaxHealthDrainHpMinConVar hpMin,
                                     TFMaxHealthDrainDeployCostConVar deployCost,
                                     IGameClock clock) : ITFPlayerResupply, IPlayerSpawnHook, IOrderedHook
{
    private readonly TFPlayerItems _items = items;
    private readonly AttributeHooks _attributes = attributes;
    private readonly TFWeaponScripts _scripts = scripts;
    private readonly WeaponCatalog _catalog = catalog;
    private readonly Arsenal _arsenal = arsenal;
    private readonly TFMaxHealthDrainHpMinConVar _hpMin = hpMin;
    private readonly TFMaxHealthDrainDeployCostConVar _deployCost = deployCost;
    private readonly IGameClock _clock = clock;

    /// <summary>The drain bookkeeping of one player, the five members of stock's
    /// <c>ResetMaxHealthDrain</c> (tf_player.cpp:14254-14261), held per index
    /// as the movement rules hold their stun trio.</summary>
    private sealed class DrainState
    {
        public int Bucket;
        public double LastUpdate = -1.0;
        public bool Refilling = true;
        public double DrainAccumulator;
        public double HealthAccumulator;
    }

    private readonly Dictionary<int, DrainState> _drain = [];

    /// <summary>The stock event a resupply fires.</summary>
    public const string RegenerateEvent = "player_regenerate";

    /// <summary>The spawn arm's order, the band's last value (<see cref="TFPlayerOrders.SpawnEffectBars"/>).</summary>
    public int HookOrder => TFPlayerOrders.SpawnEffectBars;

    /// <summary>
    /// The drain bookkeeping goes with the old life: stock's <c>Respawn</c>
    /// resets it when the bucket is not empty (tf_player.cpp:3938-3942), and
    /// the health then reads the full maximum — a heavy re-equipped into other
    /// draining gloves (the held item changing under one weapon name between
    /// the lives, so the refill arm never runs for it) comes back whole, not
    /// clamped to the old bucket's floor. The class and the strip are
    /// <see cref="TFPlayerSpawns"/>' arms, and this hook adds nothing else.
    /// </summary>
    public void OnSpawning(EntityHandle player, float now)
    {
        _drain.Remove(entities.GetIndex(player));
    }

    /// <summary>
    /// The spawned kit's bars, the regen's own (<see cref="FillEffectBar"/>):
    /// stock fills every pool to its maximum in the spawn's
    /// <c>InitClass</c> too (tf_player.cpp:4246-4250), not only under the
    /// resupply — the first command past a spawn reads a full count either
    /// way, and the hands keep the jar the player asked for.
    /// </summary>
    public void OnSpawned(EntityHandle player, float now)
    {
        foreach (var (name, _, _) in _arsenal.Holdings(player))
            FillEffectBar(player, name);
    }

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

        // ResetMaxHealthDrain (tf_player.cpp:4031), ahead of the recomputed maximum.
        _drain.Remove(entities.GetIndex(player));

        // "We may have been boosted over our max health": kept after InitClass.
        int oldMax = MaxHealth(player);
        int oldHealth = entities.GetHealth(player);
        bool boosted = (oldHealth > oldMax || !refillHealthAndAmmo) && oldMax > 0;

        // InitClass: the kit (GiveDefaultItems), then the health.
        if (refillHealthAndAmmo)
            GiveKit(player, cls);

        // SetHealth( GetMaxHealth() ), the kit's attributes counted (tf_player.cpp:14245-14269).
        int max = MaxHealth(player);
        entities.SetHealth(player, boosted ? Math.Max(oldHealth, max) : max);

        // Regenerate's meter trio: SetSpyCloakMeter, SetScoutEnergyDrinkMeter
        // and SetDemomanChargeMeter (tf_player.cpp:4096-4098), the refill
        // half's three setters filling their meters beside the kit.
        if (carriers.Get(player.EntryIndex) is { } state)
        {
            state.CloakMeter = 100f;
            state.EnergyDrinkMeter = 100f;
            state.ChargeMeter = 100f;
        }

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
            FillEffectBar(player, weapon);
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
        if (_scripts.Get(weapon) is not { } script
            || _catalog.For(weapon) is not { } type
            || type.ClipSize <= 0)
            return;

        // Stock reads the weapon's own attributes where the hooks do
        // (AutoFiresFullClip, tf_weaponbase.cpp:1871-1877, and GetMaxClip1's
        // mult_clipsize, :571-575 — the attribute arm of
        // TFAttributeWeaponHook): the item behind the named weapon, the
        // player's manager beside it.
        int index = entities.GetIndex(player);
        IAttributeProvider? item = _items.Of(index, weapon);

        int clip = script.Base.DefaultClip1 <= 0 || _attributes.Int(0, "auto_fires_full_clip", item) != 0
            ? 0
            : _attributes.Int(type.ClipSize, "mult_clipsize", item);

        _arsenal.SetClip(player, weapon, clip);
    }

    /// <summary>
    /// The kit's effect-bar weapons regen-finished: the spawn's own fill puts
    /// every pool at its maximum (<c>tf_player.cpp:4246-4250</c>, the jar's
    /// count among them), and a count at the maximum is the regen's finished
    /// state — <c>CheckEffectBarRegen</c> holds the timer at zero while the
    /// count is the maximum (<c>tf_weaponbase.cpp:6549-6552</c>), the jar in
    /// the hands from the first command. The family is the same switch that
    /// answers the recharge seconds (<see cref="TFItemMeter.EffectBarSeconds"/>);
    /// the bar's full is the gate the frame head reads
    /// (<see cref="TFItemMeter.RegenFinished"/>).
    /// </summary>
    private void FillEffectBar(EntityHandle player, string weapon)
    {
        if (_scripts.Get(weapon) is not { } script)
            return;

        if (TFItemMeter.EffectBarSeconds(script.Id) <= 0f)
            return;

        // The refill's denial (tf_player.cpp:4103-4112, and the respawn walk
        // of tf_player_shared.cpp:1006-1010): the item answering over zero on
        // item_meter_resupply_denied keeps the bar it carries — the locker
        // and the regen leave the charge where it stands. The hook is the
        // stock ON_OTHER root, the item behind the named weapon.
        int deny = _attributes.Int(0, "item_meter_resupply_denied",
                                   _items.Of(entities.GetIndex(player), weapon));
        if (deny > 0)
            return;

        _arsenal.SetItemMeter(player, weapon, 1f);
    }

    /// <summary>
    /// <c>CTFPlayer::GetMaxHealth</c> (tf_player.cpp:14241-14249) over
    /// <c>GetMaxHealthForBuffing</c>'s attribute arm (:14266-14269): the class
    /// maximum, then <c>add_maxhealth</c> and <c>add_maxhealth_nonbuffed</c>,
    /// both the player's own walk, floored at one. The active weapon's
    /// <c>GetMaxHealthMod</c> is zero for every weapon this SDK declares
    /// (tf_weaponbase.h:492, no overrides) and the runes have no counters yet
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public int MaxHealth(EntityHandle player)
    {
        if (ClassOf(player) is not { } cls)
            return 0;

        int index = entities.GetIndex(player);
        int max = _attributes.Int(classes[cls].MaxHealth, "add_maxhealth", _items.ManagerOf(index));
        // The demoman's heads (CTFPlayer::GetMaxHealthForBuffing,
        // tf_player.cpp:14276-14283): the owned sword's GetSwordHealthMod —
        // every collected head is +15 under the head cap, while the sword
        // decapitates at all (tf_weapon_sword.cpp:314-325).
        if (cls == TFClass.Demoman
            && _items.Of(index, TFSwordHook.SwordScript) is { } sword
            && _attributes.Int(0, "decapitate_type", sword) != 0)
            max += Math.Min(TFSwordHook.MaxHeads, carriers.Get(index)?.Decapitations ?? 0) * TFSwordHook.HealthPerHead;
        max = DrainMaxHealth(player, index, max);
        max = _attributes.Int(max, "add_maxhealth_nonbuffed", _items.ManagerOf(index));
        return Math.Max(max, 1);
    }

    /// <summary>
    /// The drain block of <c>GetMaxHealthForBuffing</c>
    /// (tf_player.cpp:14293-14441), right where stock runs it: after
    /// <c>add_maxhealth</c>, before the caller's
    /// <c>add_maxhealth_drain_nonbuffed</c> half of <c>GetMaxHealth</c>.
    /// While the held weapon's item carries <c>mod_maxhealth_drain_rate</c>
    /// the max health drains toward <c>tf_maxhealth_drain_hp_min</c> at the
    /// hook's per-second rate, once a
    /// <c>tf_maxhealth_drain_deploy_cost</c> on the refilling-to-deployed
    /// flip (:14306-14320); the health itself follows the same rate scaled
    /// by the health percentage (:14343-14352). With the attribute off the
    /// held item the drained bucket refills at the rate still reaching the
    /// player's own manager (:14360-14429), and a player who never drains
    /// resets the bookkeeping (:14431-14440). Every ask settles the
    /// elapsed time first, as stock's lazy-on-read does.
    /// </summary>
    private int DrainMaxHealth(EntityHandle player, int index, int iMax)
    {
        int originalMax = iMax;
        DrainState state = DrainFor(index);
        float now = _clock.Now;
        int floor = _hpMin.Value;

        float rate = _attributes.Float(0f, "mod_maxhealth_drain_rate",
                                       players.GetActiveWeapon(player) is { Length: > 0 } weapon
                                           ? _items.Of(index, weapon) : null);

        // Draining: the held Gloves are up (:14301-14359).
        if (rate > 0f)
        {
            int penalty = 0;
            if (state.Refilling)
            {
                penalty = _deployCost.Value;
                state.Refilling = false;
                if (state.DrainAccumulator < 0f)
                    state.DrainAccumulator = Math.Abs(state.DrainAccumulator);
                if (state.HealthAccumulator < 0f)
                    state.HealthAccumulator = Math.Abs(state.HealthAccumulator);
            }

            if (state.LastUpdate == -1.0)
                state.LastUpdate = now;

            if (originalMax - floor > state.Bucket)
            {
                int prevBucket = state.Bucket;
                double drainRate = (now - state.LastUpdate) * rate + penalty;
                state.DrainAccumulator += drainRate;
                int drain = (int)Math.Floor(state.DrainAccumulator);
                if (drain > 0)
                {
                    state.DrainAccumulator -= drain;
                    state.Bucket += drain;
                }

                int current = entities.GetHealth(player);
                float healthPerc = current / (float)Math.Max(iMax - prevBucket, floor);
                state.HealthAccumulator += drainRate * healthPerc;
                int healthDrain = (int)Math.Floor(state.HealthAccumulator);
                if (healthDrain > 0)
                {
                    state.HealthAccumulator -= healthDrain;
                    entities.SetHealth(player, Math.Max(current - healthDrain, 1));
                }
            }

            iMax = Math.Max(originalMax - state.Bucket, floor);
            state.LastUpdate = now;
            return iMax;
        }

        // Refilling: the attribute is off the held item (:14361-14430).
        if (state.Bucket > 0)
        {
            if (!state.Refilling)
            {
                if (state.DrainAccumulator > 0f)
                    state.DrainAccumulator = -state.DrainAccumulator;
                if (state.HealthAccumulator > 0f)
                    state.HealthAccumulator = -state.HealthAccumulator;
            }
            state.Refilling = true;

            // Stock's plain CALL_ATTRIB_HOOK_FLOAT (:14379): the rate as it
            // still reaches the player's own manager.
            float restoreRate = _attributes.Float(0f, "mod_maxhealth_drain_rate", _items.ManagerOf(index));
            if (restoreRate == 0f)
            {
                ResetDrain(state);
                return iMax;
            }

            int prev = state.Bucket;
            double restore = (now - state.LastUpdate) * restoreRate;
            state.DrainAccumulator += restore;
            int refill = (int)Math.Floor(state.DrainAccumulator);
            if (refill > 0)
            {
                state.DrainAccumulator -= refill;
                state.Bucket -= refill;
            }

            int alive = entities.GetHealth(player);
            float perc = alive / (float)Math.Max(iMax - prev, 1);
            state.HealthAccumulator += restore * perc;
            int restoreHealth = (int)Math.Floor(state.HealthAccumulator);
            if (restoreHealth > 0)
            {
                state.HealthAccumulator -= restoreHealth;
                // The refill's cap (:14406-14410): the overheal ceiling at
                // the stock default multiplier of one, else the nonbuffed
                // attribute's maximum — the greater of the two.
                int attrMax = _attributes.Int(originalMax, "add_maxhealth_nonbuffed", _items.ManagerOf(index));
                int cap = Math.Max(originalMax, attrMax);
                entities.SetHealth(player, Math.Min(alive + restoreHealth, cap));
            }

            state.LastUpdate = now;
            iMax = Math.Max(originalMax - state.Bucket, 1);
            if (iMax == originalMax)
            {
                // Back at the full maximum: nothing left over (:14419-14428).
                if (state.HealthAccumulator != 0f)
                    entities.SetHealth(player, entities.GetHealth(player) + 1);
                ResetDrain(state);
            }
            return iMax;
        }

        // Nothing draining and nothing drained: settle the bookkeeping
        // (:14431-14440), stock's guard against a stale first timestamp.
        if (state.LastUpdate != -1.0)
            ResetDrain(state);

        return iMax;
    }

    private DrainState DrainFor(int index)
    {
        if (!_drain.TryGetValue(index, out DrainState? state))
            _drain[index] = state = new DrainState();
        return state;
    }

    /// <summary><c>ResetMaxHealthDrain</c> (tf_player.cpp:14254-14261).</summary>
    private static void ResetDrain(DrainState state)
    {
        state.Bucket = 0;
        state.LastUpdate = -1.0;
        state.Refilling = true;
        state.DrainAccumulator = 0.0;
        state.HealthAccumulator = 0.0;
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
            && _items.Of(entities.GetIndex(player), weapon) is { } item)
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
        return TFMaxAmmoHooks.Apply(_attributes, _items.ManagerOf(entities.GetIndex(player)), ammo, classMax);
    }

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo)
    {
        if (count <= 0 || ClassOf(player) is null)
            return 0;

        // The pickups' metal toll (tf_player.cpp:14139-14147): a metal grant
        // from anything but the resupply is the player's
        // mult_metal_pickup times the count — the Eureka Effect's packs
        // weigh half. The locker's own fill (:169 and the kit's walk) rides
        // the raw core call and never sees this, stock's Resupply source
        // branch.
        if (ammo == TFAmmo.Metal)
            count = (int)(_attributes.Float(1f, "mult_metal_pickup",
                                            _items.ManagerOf(player.EntryIndex)) * count);

        // The grenades pools' denial (AddSecondaryAmmo, tf_player.cpp:14117-14136):
        // outside the resource-meter refill, a held item answering over zero on
        // grenades<N>_resupply_denied takes none of that pool from a pickup —
        // the stock call hooks the player themselves, the held item's manager.
        if (ammo is >= TFAmmo.Grenades1 and <= TFAmmo.Grenades3)
        {
            // The pool's name in the hook is the one-based stock ordinal
            // (grenades1..grenades3), the enum's four-on value minus the band.
            int deny = _attributes.Int(0,
                                       $"grenades{(int)ammo - (int)TFAmmo.Grenades1 + 1}_resupply_denied",
                                       ActiveItemOf(player.EntryIndex));
            if (deny > 0)
                return 0;
        }

        // The core's pool clamps to the carry rule, which is the class's AmmoMax.
        return players.GiveAmmo(player, count, TFAmmoNames.Name(ammo));
    }

    /// <summary>The active weapon's item (<see cref="TF2.Shared.ITFWeaponOwners.ActiveItemOf"/>'s
    /// read): the name the hands hold, the item behind it.</summary>
    private IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        string name = players.GetActiveWeapon(players.GetHandle(playerIndex));
        return name.Length > 0 ? _items.Of(playerIndex, name) : null;
    }

    /// <summary>The player's class when they have one (Scout..Engineer, or Civilian), else null.</summary>
    private TFClass? ClassOf(EntityHandle player)
    {
        TFClass cls = carriers.ClassOf(player);
        return cls is > TFClass.Undefined and < TFClass.CountAll ? cls : null;
    }
}
