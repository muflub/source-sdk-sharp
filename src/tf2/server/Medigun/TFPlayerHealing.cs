//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Entities.Ctf;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Medigun;

/// <summary>
/// The player side of being healed: stock's <c>CTFPlayerShared::Heal</c> and
/// <c>StopHealing</c> (tf_player_shared.cpp:8775, 8825), the heal accumulation
/// and overheal decay of <c>CTFPlayerShared::ConditionGameRulesThink</c>
/// (:2392-2735), and the charge-effect half the same file keeps beside them,
/// <c>RecalculateChargeEffects</c>, <c>SetChargeEffect</c> and
/// <c>TestAndExpireChargeEffect</c> (:8854, :9017, :8915).
/// </summary>
public interface ITFPlayerHealing
{
    /// <summary>Stock's no-attribute <c>GetOverHealBonus</c>: <c>tf_max_health_boost</c> taken as 1.5.</summary>
    public const float StandardOverhealBonus = 1.5f;

    /// <summary>Stock's no-attribute <c>GetOverHealDecayMult</c>.</summary>
    public const float StandardOverhealDecayMult = 1.0f;

    /// <summary>
    /// <c>CTFPlayerShared::Heal</c>: this healer now heals this patient at
    /// <paramref name="healRate"/> health per second, up to
    /// <paramref name="overhealBonus"/> times max health, with
    /// <paramref name="decayMult"/> on the drain when healing stops. Re-healing
    /// by the same healer replaces the entry and carries its accumulated heal.
    /// Stock ends with <c>RecalculateChargeEffects</c> (tf_player_shared.cpp:8808).
    /// A dispenser healer passes <paramref name="dispenserHeal"/>: stock's
    /// <c>bDispenserHeal</c> — the constant-rate arm, the spy's cloak and the
    /// accumulation clamp — with the bonus 1 that never overheals
    /// (tf_obj_dispenser.cpp:846-861), and <paramref name="scorer"/> names the
    /// player who is credited the heal (stock's <c>pHealScorer</c>, the builder).
    /// </summary>
    void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
              float overhealBonus = StandardOverhealBonus, float decayMult = StandardOverhealDecayMult,
              bool dispenserHeal = false, int scorer = -1);

    /// <summary>
    /// <c>CTFPlayerShared::StopHealing</c>: the healer is gone; answers the health it
    /// had accumulated. Stock ends with <c>RecalculateChargeEffects</c>
    /// (tf_player_shared.cpp:8844), so a covered patient starts wearing their uber off.
    /// </summary>
    float StopHealing(EntityHandle patient, EntityHandle healer, float now);

    /// <summary><c>GetNumHealers</c>: how many healers keep this patient topped up.</summary>
    int NumHealers(int patientIndex);

    /// <summary>
    /// The entity indexes of everyone healing this patient now — the walk
    /// stock's <c>GetHealerByIndex</c> (tf_player_shared.cpp:8814) answers
    /// per index, here in one ask.
    /// </summary>
    IEnumerable<int> HealerIndexes(int patientIndex);

    /// <summary>
    /// <c>RecalculateChargeEffects</c> (:8854): recompute every medigun charge effect —
    /// the invulnerable, crit-boost and mega-heal rows of <c>g_MedigunEffects</c> — from
    /// this player's own and their healers' released charges, each with its provider.
    /// </summary>
    void RecalculateChargeEffects(EntityHandle player, float now);

    /// <summary>
    /// Which mediguns say they are releasing, and as what — the answer stock's
    /// <c>CTFPlayer::GetChargeEffectBeingProvided</c> (tf_player.cpp:20416) reads off the
    /// player's live active medigun, whose <c>GetChargeType</c> is the item's
    /// <c>set_charge_type</c> (tf_weapon_medigun.cpp:818-832). The port cannot ask the
    /// weapon (each would need the other built: the container's cycle rule), so the
    /// medigun reports in at the moments its release begins and ends, carrying the
    /// medigun_charge_types row it released, and this holds the answer
    /// (docs/divergences.md).
    /// </summary>
    void NoteReleasing(int playerIndex, bool releasing, int chargeType = 0);

    /// <summary>
    /// <c>CTFPlayerShared::GetMaxBuffedHealth</c> (:2235) for the standard player: the class max
    /// times <c>tf_max_health_boost</c>, floored to a multiple of five. The medigun watches this
    /// to decide when its meter stops filling fast.
    /// </summary>
    int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false);

    /// <summary>
    /// <c>CTFPlayer::CanRecieveMedigunChargeEffect</c>, the player arms this lane answers: a
    /// player carrying the intelligence flag receives no standard medigun charge effect.
    /// </summary>
    bool CanReceiveMedigunChargeEffect(EntityHandle player);

    /// <summary>The player's instant uber loss: stock's <c>RecalcEffectOnTarget(target, true)</c>.</summary>
    void RemoveChargeEffect(EntityHandle player);

    /// <summary>
    /// <c>CTFPlayerShared::TestAndExpireChargeEffect</c> (:8915), every row: a charge
    /// condition whose wear-off time has passed — the invulnerable row after
    /// <c>tf_invuln_time</c>, the others at once — is taken away.
    /// </summary>
    void TestAndExpireChargeEffect(EntityHandle player, float now);

    /// <summary>
    /// The per-player heal half of <c>CTFPlayerShared::ConditionGameRulesThink</c>: pay out the
    /// healers' accumulated health, cap it, report and event it, and drain the overheal when no
    /// healer is keeping the player up.
    /// </summary>
    void TickPatient(EntityHandle patient, float now, float frametime);

    /// <summary>
    /// The burn just went out on this patient: fire <c>player_extinguished</c>
    /// once per healer keeping them up, as stock's HEALTH_BUFF arm of the
    /// burning think does (tf_player_shared.cpp:2793-2884).
    /// </summary>
    void Extinguished(EntityHandle patient);
}

/// <summary>
/// The server's <see cref="ITFPlayerHealing"/>. The stock arms that have no
/// server here — the disguised player's fake health, the
/// Quick-Fix no-decay arm, the healing debuff reduction, the rune of vampirism,
/// the rage buffers, the exploit arm of the charge expiry and the charge kick
/// for AoE heals (that last one only fires for an overheal bonus of 1, which no
/// standard medigun entry has) — are the rows in docs/plans/tf2/deferred.tsv.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerHealing)])]
public sealed class TFPlayerHealing : ITFPlayerHealing, ITFConditionHook, IGameLifecycle, IClientLifecycle, IOrderedHook
{
    /// <summary>Stock's <c>SetChargeEffect</c> duration: <c>tf_medigun_chargerelease_rate</c> (8) plus one.</summary>
    public const float UberDuration = 9f;

    /// <summary>Stock's <c>INVALID_ITEM_DEF_INDEX</c> (shareddefs.h).</summary>
    public const int InvalidItemDefIndex = 65535;

    private readonly IEnginePlayers players;
    private readonly IEngineEntities entities;
    private readonly ITFPlayerConditions conditions;
    private readonly ITFPlayerResupply resupply;
    private readonly TFMaxHealthBoostConVar maxHealthBoost;
    private readonly TFBoostDrainTimeConVar boostDrainTime;
    private readonly TFInvulnTimeConVar invulnTime;
    private readonly IGameClock clock;
    private readonly ITFPlayerStats stats;
    private readonly TFDamageRecords records;
    private readonly IEngineGameEvents events;

    // The flag rules, asked as the leaf under them: the medigun link the weapon
    // owners took makes every constructor edge back to CtfRules a turn of the
    // cycle CtfRules -> EngineCtfPlayers -> TFMovementRules ->
    // TFServerWeaponOwners -> TFMedigunWeapon -> here, which the container
    // refuses at the load. The refusal needs only who carries a flag, so the
    // healing holds IFlagCarriers — the carried-flag ask cut free of the rules'
    // company — and the rules' own seam stays with the services that can pay
    // for it.
    private readonly IFlagCarriers flagCarriers;
    private readonly ITFPlayerData data;
    private readonly TFPlayerItems items;
    private readonly AttributeHooks hooks;
    private readonly IEngineSound sound;

    private readonly Dictionary<int, PatientHealing> patients = new();
    private readonly Dictionary<int, float[]> chargeOffTimes = new();
    private readonly Dictionary<int, int> releasers = new();

    // Stock's m_bChargeSoundEffectsOn (per row) and m_bChargeOffSounded (one per player,
    // tf_player_shared.cpp:9080-9098): which rows currently carry an on sound, and whether
    // this grant cycle has already sounded its off.
    private readonly Dictionary<int, bool[]> chargeSoundFlags = new();
    private readonly Dictionary<int, bool> chargeOffSounded = new();

    public TFPlayerHealing(IEnginePlayers players, IEngineEntities entities,
                           ITFPlayerConditions conditions, ITFPlayerResupply resupply,
                           TFMaxHealthBoostConVar maxHealthBoost, TFBoostDrainTimeConVar boostDrainTime,
                           TFInvulnTimeConVar invulnTime, IGameClock clock,
                           ITFPlayerStats stats, TFDamageRecords records,
                           IEngineGameEvents events,
                           IFlagCarriers flagCarriers, ITFPlayerData data,
                           TFPlayerItems items, AttributeHooks hooks, IEngineSound sound)
    {
        this.players = players;
        this.entities = entities;
        this.conditions = conditions;
        this.resupply = resupply;
        this.maxHealthBoost = maxHealthBoost;
        this.boostDrainTime = boostDrainTime;
        this.invulnTime = invulnTime;
        this.clock = clock;
        this.stats = stats;
        this.records = records;
        this.events = events;
        this.flagCarriers = flagCarriers;
        this.data = data;
        this.items = items;
        this.hooks = hooks;
        this.sound = sound;
    }

    public int HookOrder => TFMedigunOrders.Healing;

    public int LifecycleOrder => TFMedigunOrders.Healing;

    public int ClientLifecycleOrder => TFMedigunOrders.Healing;

    public void OnLevelShutdown()
    {
        patients.Clear();
        chargeOffTimes.Clear();
        releasers.Clear();
        chargeSoundFlags.Clear();
        chargeOffSounded.Clear();
    }

    public void OnClientDisconnected(int playerIndex)
    {
        patients.Remove(playerIndex);
        chargeOffTimes.Remove(playerIndex);
        releasers.Remove(playerIndex);
        chargeSoundFlags.Remove(playerIndex);
        chargeOffSounded.Remove(playerIndex);
    }

    // ---- CTFPlayerShared::Heal / StopHealing / the healers list ----------------

    public void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
                     float overhealBonus = ITFPlayerHealing.StandardOverhealBonus,
                     float decayMult = ITFPlayerHealing.StandardOverhealDecayMult,
                     bool dispenserHeal = false, int scorer = -1)
    {
        var state = State(patient.EntryIndex);
        // Stock replaces the same healer's entry, carrying its flHealAccum across (the old entry
        // leaves through StopHealing, which returns it).
        float carried = state.Healers.TryGetValue(healer.EntryIndex, out var old) ? old.HealAccum : 0f;
        state.Healers[healer.EntryIndex] = new HealerEntry
        {
            HealerIndex = healer.EntryIndex,
            Amount = healRate,
            OverhealBonus = overhealBonus,
            DecayMult = decayMult,
            HealAccum = carried,
            DispenserHeal = dispenserHeal,
            ScorerIndex = scorer,
        };
        conditions.Add(patient, TFCond.HealthBuff, provider: healer);
        RecalculateChargeEffects(patient, now);          // tf_player_shared.cpp:8808
    }

    public float StopHealing(EntityHandle patient, EntityHandle healer, float now)
    {
        if (!patients.TryGetValue(patient.EntryIndex, out var state))
            return 0f;
        float accum = 0f;
        if (state.Healers.Remove(healer.EntryIndex, out var entry))
            accum = entry.HealAccum;
        if (state.Healers.Count == 0)
            conditions.Remove(patient, TFCond.HealthBuff);
        RecalculateChargeEffects(patient, now);          // tf_player_shared.cpp:8844
        return accum;
    }

    public int NumHealers(int patientIndex) => patients.TryGetValue(patientIndex, out var state) ? state.Healers.Count : 0;

    /// <summary>The healers' indexes as kept (<see cref="Heal"/>'s keys, the walk's own order).</summary>
    public IEnumerable<int> HealerIndexes(int patientIndex)
        => patients.TryGetValue(patientIndex, out var state) ? state.Healers.Keys : [];

    // ---- the HEALTH_BUFF condition arms (tf_player_shared.cpp:1578-1584, 1935-1939) --

    /// <summary>
    /// <c>OnConditionAdded(TF_COND_HEALTH_BUFF)</c>: the accumulation starts from
    /// nothing and the second-window clock is wound a second ahead. Stock runs the
    /// arm on every add — a refresh (a healer arriving beside a live buff) zeroes
    /// the fraction too, losing its part-fraction, and both of these arms live on
    /// the same class as the state they reset, exactly as in stock.
    /// </summary>
    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.HealthBuff || !patients.TryGetValue(player.EntryIndex, out var state))
            return;
        state.HealFraction = 0f;
        state.HealedPerSecondTimer = clock.Now + 1f;
    }

    /// <summary>
    /// <c>OnConditionRemoved(TF_COND_HEALTH_BUFF)</c> GAME_DLL half: the last
    /// healer leaving leaves no carry-over behind — the next attach (or the item
    /// overheal's drain, which shares the fraction) starts clean.
    /// <c>m_flDisguiseHealFraction</c> has no port (the disguised healing is a
    /// deferred row).
    /// </summary>
    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.HealthBuff && patients.TryGetValue(player.EntryIndex, out var state))
            state.HealFraction = 0f;
    }

    // ---- the charge effects: the g_MedigunEffects rows a released medigun grants ------------

    /// <summary>
    /// One row of stock's <c>g_MedigunEffects</c> (tf_weapon_medigun.cpp:49-56), the slice
    /// <c>RecalculateChargeEffects</c> walks (tf_player_shared.cpp:8904-8906): the condition
    /// the charge grants, the wearing-off condition that rides its removal
    /// (<c>TF_COND_LAST</c> on the rows that have none), whether the wear-off takes
    /// <c>tf_invuln_time</c> seconds — stock passes 0.0 for every row but the invulnerable
    /// one, whose pending expiry the next tick clears — and the pair of script sounds the
    /// grant and the wear-off emit to the patient (:51-56; the Vaccinator's three rows share
    /// one pair).
    /// </summary>
    private readonly record struct ChargeRow(int Type, TFCond Condition, bool HasWearingOff, TFCond WearingOff, bool Timed,
                                             string ChargeOnSound, string ChargeOffSound);

    private static ChargeRow Row(int chargeType) => chargeType switch
    {
        1 => new(1, TFCond.Critboosted, false, default, false, "TFPlayer.CritBoostOn", "TFPlayer.CritBoostOff"),    // MEDIGUN_CHARGE_CRITICALBOOST
        2 => new(2, TFCond.Megaheal, false, default, false, "TFPlayer.QuickFixInvulnerableOn", "TFPlayer.MegaHealOff"),  // MEDIGUN_CHARGE_MEGAHEAL
        // The Vaccinator's three typed bubbles (tf_weapon_medigun.cpp:54-56), one sound pair between them.
        3 => new(3, TFCond.MedigunUberBulletResist, false, default, false, "WeaponMedigun_Vaccinator.InvulnerableOn", "WeaponMedigun_Vaccinator.InvulnerableOff"),     // MEDIGUN_CHARGE_BULLET_RESIST
        4 => new(4, TFCond.MedigunUberBlastResist, false, default, false, "WeaponMedigun_Vaccinator.InvulnerableOn", "WeaponMedigun_Vaccinator.InvulnerableOff"),      // MEDIGUN_CHARGE_BLAST_RESIST
        5 => new(5, TFCond.MedigunUberFireResist, false, default, false, "WeaponMedigun_Vaccinator.InvulnerableOn", "WeaponMedigun_Vaccinator.InvulnerableOff"),       // MEDIGUN_CHARGE_FIRE_RESIST
        _ => new(0, TFCond.Invulnerable, true, TFCond.InvulnerableWearingoff, true, "TFPlayer.InvulnerableOn", "TFPlayer.InvulnerableOff"),  // MEDIGUN_CHARGE_INVULN
    };

    private const int ChargeRowCount = 6;   // MEDIGUN_NUM_CHARGE_TYPES (tf_shareddefs.h:587-598): the plain three and the Vaccinator's three resists

    public void RecalculateChargeEffects(EntityHandle player, float now)
    {
        // CTFPlayerShared::RecalculateChargeEffects (tf_player_shared.cpp:8854): per row, the
        // player's own released medigun or a healer's, with that player as the provider.
        Span<(bool On, EntityHandle Provider)> charges = stackalloc (bool, EntityHandle)[ChargeRowCount];
        for (int i = 0; i < ChargeRowCount; i++)
            charges[i] = (false, default);

        if (releasers.TryGetValue(player.EntryIndex, out int ownType))
            charges[ownType] = (true, player);

        if (patients.TryGetValue(player.EntryIndex, out var state))
        {
            foreach (int healerIndex in state.Healers.Keys)
            {
                if (!releasers.TryGetValue(healerIndex, out int healerType))
                    continue;
                charges[healerType] = (true, players.GetHandle(healerIndex));
            }
        }

        // Stock's line after the loop (:8899-8902): the flag-carrier refusal takes only the
        // invulnerable row back off — a covered carrier keeps every other charge effect.
        if (!CanReceiveMedigunChargeEffect(player))
            charges[0].On = false;

        for (int i = 0; i < ChargeRowCount; i++)
            SetChargeEffect(player, Row(i), charges[i].On, charges[i].Provider, instantRemove: false, now);
    }

    /// <summary>
    /// The medigun's report of its release and of the <c>set_charge_type</c> row it rides:
    /// stock reads both off the live weapon in <c>GetChargeEffectBeingProvided</c>; the
    /// port's answer is pushed (see the interface's <c>NoteReleasing</c> and
    /// docs/divergences.md).
    /// </summary>
    public void NoteReleasing(int playerIndex, bool releasing, int chargeType = 0)
    {
        // Outside MEDIGUN_NUM_CHARGE_TYPES' six rows the plain charge stands.
        if (releasing)
            releasers[playerIndex] = chargeType is >= 0 and < ChargeRowCount ? chargeType : 0;
        else
            releasers.Remove(playerIndex);
    }

    /// <summary>
    /// <c>CTFPlayer::CanRecieveMedigunChargeEffect</c> player arm: a player carrying the
    /// intelligence flag (theirs or stolen) receives no standard medigun charge effect.
    /// With no carriers seam held there is no flag to carry. The MvM, passtime and
    /// resist arms are deferred.
    /// </summary>
    public bool CanReceiveMedigunChargeEffect(EntityHandle player)
        => flagCarriers.CarriedBy(player) is null;

    /// <summary><c>CTFPlayerShared::SetChargeEffect</c> (tf_player_shared.cpp:9017), one row of it.</summary>
    private void SetChargeEffect(EntityHandle player, ChargeRow row, bool on, EntityHandle provider,
                                 bool instantRemove, float now)
    {
        float[] offTimes = OffTimes(player.EntryIndex);
        bool[] flags = SoundFlags(player.EntryIndex);
        int slot = row.Type;
        bool active = conditions.InCond(player, row.Condition);
        if (active == on)
        {
            // Stock: coming back to the state already held clears a pending wear-off.
            if (on && offTimes[slot] != 0f)
            {
                offTimes[slot] = 0f;
                if (row.HasWearingOff)
                    conditions.Remove(player, row.WearingOff);
                SendNewInvulnGameEvent(player);
            }
            return;
        }
        // Stock: already in the process of turning it off — the pending expiry stands
        // (tf_player_shared.cpp:9112-9114).
        if (!on && !instantRemove && offTimes[slot] != 0f)
            return;

        if (on)
        {
            if (offTimes[slot] != 0f)
            {
                sound.EntityStopSound(player, row.ChargeOffSound);      // :9064 the pending off sound stops under the re-grant
                offTimes[slot] = 0f;
                if (row.HasWearingOff)
                    conditions.Remove(player, row.WearingOff);
            }
            conditions.Add(player, row.Condition, ChargeSeconds(provider), provider);
            SendNewInvulnGameEvent(player);
            sound.EntityEmitSound(player, row.ChargeOnSound);           // :9078-9079 stock's CSingleUserRecipientFilter; the sound area has no single-recipient emit, so neighbours hear it too (as HealthKit.Sound, docs/divergences.md)
            flags[slot] = true;                                         // m_bChargeSoundEffectsOn (:9081)
            chargeOffSounded[player.EntryIndex] = false;                // m_bChargeOffSounded (:9080)
            return;
        }

        if (flags[slot])                                                // :9085-9089: the on sound stops when the charge starts wearing off
        {
            sound.EntityStopSound(player, row.ChargeOnSound);
            flags[slot] = false;
        }

        if (offTimes[slot] == 0f && !chargeOffSounded.GetValueOrDefault(player.EntryIndex))   // :9091-9098, once per grant cycle
        {
            sound.EntityStopSound(player, row.ChargeOffSound);          // :9094 no duplicate Off sounds
            sound.EntityEmitSound(player, row.ChargeOffSound);
            chargeOffSounded[player.EntryIndex] = true;
        }

        if (instantRemove)
        {
            offTimes[slot] = 0f;
            conditions.Remove(player, row.Condition);
            if (row.HasWearingOff)
                conditions.Remove(player, row.WearingOff);
            return;
        }

        if (row.HasWearingOff)
            conditions.Add(player, row.WearingOff, provider: provider);
        offTimes[slot] = now + (row.Timed ? invulnTime.Value : 0f);
    }

    /// <summary>
    /// The seconds a granted charge condition carries, stock's <c>flNonBotMaxDuration</c>
    /// (tf_player_shared.cpp:9053-9055): the release rate, plus the provider's
    /// <c>add_uber_time</c>, plus one.
    /// </summary>
    private float ChargeSeconds(EntityHandle provider)
    {
        if (provider == default)
            return UberDuration;
        float added = hooks.Float(0f, "add_uber_time", items.ManagerOf(provider.EntryIndex));
        return TFMedigunBehaviour.ChargeReleaseRate + added + 1f;
    }

    /// <summary>
    /// <c>CTFPlayerShared::SendNewInvulnGameEvent</c> (tf_player_shared.cpp:8993): per healer
    /// of the player, a <c>player_invulned</c> naming the pair. Stock fires it from
    /// <c>SetChargeEffect</c> on every row's grant — the invulnerable one and the rest
    /// alike — so the event rides the release, not the pop.
    /// </summary>
    private void SendNewInvulnGameEvent(EntityHandle player)
    {
        if (!patients.TryGetValue(player.EntryIndex, out var state))
            return;
        foreach (int healerIndex in state.Healers.Keys)
        {
            if (!entities.IsPlayer(players.GetHandle(healerIndex)))
                continue;    // stock's ToTFPlayer skip: a dispenser heals but is no medic
            Fire("player_invulned", e =>
            {
                events.EventSetInt(e, "userid", players.GetUserId(player));
                events.EventSetInt(e, "medic_userid", players.GetUserId(players.GetHandle(healerIndex)));
            });
        }
    }

    /// <summary>
    /// <c>CTFPlayerShared::TestAndExpireChargeEffect</c> (:8915), every row, without the
    /// exploit arm (which needs the provider's user-command age and a forced melee).
    /// </summary>
    public void TestAndExpireChargeEffect(EntityHandle player, float now)
    {
        if (!chargeOffTimes.TryGetValue(player.EntryIndex, out var offTimes))
            return;
        bool[] flags = SoundFlags(player.EntryIndex);
        for (int i = 0; i < ChargeRowCount; i++)
        {
            var row = Row(i);
            if (conditions.InCond(player, row.Condition))
            {
                if (offTimes[i] != 0f && now > offTimes[i])
                {
                    offTimes[i] = 0f;
                    conditions.Remove(player, row.Condition);
                    if (row.HasWearingOff)
                        conditions.Remove(player, row.WearingOff);
                }
            }
            else if (flags[i])
            {
                // Stock's else-if (:8980-8987): the charge condition is gone but its on
                // sound is still riding — stop it once, on this tick. Stock guards the stop
                // on a non-empty name (:8982); every row of the table carries names.
                sound.EntityStopSound(player, row.ChargeOnSound);
                flags[i] = false;
            }
        }
    }

    /// <summary>The player's instant uber loss: stock's <c>RecalcEffectOnTarget(target, true)</c>.</summary>
    public void RemoveChargeEffect(EntityHandle player)
    {
        if (chargeOffTimes.TryGetValue(player.EntryIndex, out var offTimes))
            for (int i = 0; i < ChargeRowCount; i++)
                offTimes[i] = 0f;
        for (int i = 0; i < ChargeRowCount; i++)
        {
            var row = Row(i);
            conditions.Remove(player, row.Condition);
            if (row.HasWearingOff)
                conditions.Remove(player, row.WearingOff);
        }
    }

    private float[] OffTimes(int playerIndex)
    {
        if (chargeOffTimes.TryGetValue(playerIndex, out var offTimes))
            return offTimes;
        var fresh = new float[ChargeRowCount];
        chargeOffTimes[playerIndex] = fresh;
        return fresh;
    }

    /// <summary>The per-row sound flags, stock's <c>m_bChargeSoundEffectsOn</c> (:9081, :9088).</summary>
    private bool[] SoundFlags(int playerIndex)
    {
        if (chargeSoundFlags.TryGetValue(playerIndex, out var flags))
            return flags;
        var fresh = new bool[ChargeRowCount];
        chargeSoundFlags[playerIndex] = fresh;
        return fresh;
    }

    // ---- CTFPlayerShared::ConditionGameRulesThink: the heal, the cap, the drain --

    /// <summary>
    /// The per-player half of <c>ConditionGameRulesThink</c> that this lane owns
    /// (:2392-2735): accumulate each healing healer's rate into the shared fraction, pay
    /// it out capped to <see cref="MaxBuffedHealth"/>, report and event it, then, when no
    /// healer is keeping the player up, let the overheal drain back to max. The condition
    /// fast-expire half and the burning and bleeding reductions stay with the condition
    /// ticker that already has them.
    /// </summary>
    public void TickPatient(EntityHandle patient, float now, float frametime)
    {
        patients.TryGetValue(patient.EntryIndex, out var state);
        bool decayHealth = true;

        if (state is not null && state.Healers.Count > 0 && conditions.InCond(patient, TFCond.HealthBuff))
        {
            int max = resupply.MaxHealth(patient);
            if (max > 0)
            {
                int prevHealth = entities.GetHealth(patient);
                float flTimeSinceDamage = TimeSinceLastDamage(patient.EntryIndex, now);
                float scale = RemapValClamped(flTimeSinceDamage, 10f, 15f, 1f, 3f);

                // Stock's heal-rate attribute walk (:2403-2411): what the held
                // items say multiplies every healer's amount — mult_health_fromhealers
                // is asked on the patient's own manager, its penalty on the active
                // weapon (:2408), and the result rides both fraction arms (:2503, :2520).
                float attribModScale = hooks.Float(1f, "mult_health_fromhealers",
                                                    items.ManagerOf(patient.EntryIndex));   // :2406
                attribModScale = hooks.Float(attribModScale, "mult_health_fromhealers_penalty_active",
                                             ActiveItemOf(patient.EntryIndex));             // :2410

                // Stock's heal multiplier for the mega-healed patient (tf_player_shared.cpp:2486-2495):
                // the Quick-Fix charge triples what its healer banks toward the next heal.
                if (conditions.InCond(patient, TFCond.Megaheal))
                    scale *= 3f;

                // Stock divides by the class max and, past it, re-bases on the buffing max; for the
                // standard player the buffing max IS the class max, so both arms answer health/max.
                float curOverheal = (float)prevHealth / max;

                float totalAmount = 0f;
                List<(int Index, HealerEntry Entry)> healing = new(state.Healers.Count);
                foreach (var (index, entry) in state.Healers)
                {
                    // Stock's per-healer overheal walk (:2429-2434): once the patient is
                    // past the class max, the healer's own overheal_fill_rate rides their
                    // heal — asked on the healer's manager, and only for a player's heal,
                    // never the dispenser's.
                    float perHealerScale = 1f;
                    if (curOverheal > 1f && !entry.DispenserHeal)
                        perHealerScale = hooks.Float(1f, "overheal_fill_rate", items.ManagerOf(index));

                    // Stock's dispenser arm runs before the overheal check: the cloak
                    // refills even when the dispenser heals no health
                    // (tf_player_shared.cpp:2440-2443).
                    if (entry.DispenserHeal)
                        AddToSpyCloakMeter(patient, frametime * entry.Amount);

                    // Stock skips a healer whose overheal bonus is reached: that healer heals
                    // nothing, and (the Quick-Fix no-decay arm aside) leaves the drain on.
                    if (curOverheal >= entry.OverhealBonus)
                        continue;
                    decayHealth = false;

                    // Stock's per-healer walk (:2519): the patient's own
                    // mult_healing_from_medics rides the out-of-combat scale for
                    // every medigun heal — asked on the patient's manager, like
                    // the two walks above. The dispenser arm takes its rate whole,
                    // which is what the ternary below keeps (:2503).
                    if (!entry.DispenserHeal)
                        scale = hooks.Float(scale, "mult_healing_from_medics", items.ManagerOf(patient.EntryIndex));   // :2519

                    // Dispensers heal at a constant rate, ignoring the out-of-combat
                    // scale (tf_player_shared.cpp:2497-2513); a medigun's heal is
                    // scaled (:2514-2526). The attribute scale rides both arms
                    // (:2503, :2520); the per-healer overheal scale rides the medigun
                    // arm alone (:2520).
                    float fraction = frametime * entry.Amount * attribModScale
                                   * (entry.DispenserHeal ? 1f : scale * perHealerScale);
                    state.HealFraction += fraction;
                    if (entry.DispenserHeal)
                    {
                        // track how much this healer has actually done so far, capped at
                        // what still fits under the buff max (:2506-2507)
                        entry.HealAccum += Math.Clamp(fraction, 0f, MaxBuffedHealth(patient) - prevHealth);
                    }
                    totalAmount += entry.Amount;
                    if (state.BestDecayMult == -1f || entry.DecayMult < state.BestDecayMult)
                        state.BestDecayMult = entry.DecayMult;
                    healing.Add((index, entry));
                }

                int healthToAdd = (int)state.HealFraction;
                int taken = 0;
                if (healthToAdd > 0)
                {
                    state.HealFraction -= healthToAdd;
                    int boostMax = MaxBuffedHealth(patient);
                    healthToAdd = Math.Clamp(healthToAdd, 0, boostMax - prevHealth);
                    taken = healthToAdd > 0 ? resupply.TakeHealth(patient, healthToAdd, ignoreMaxHealth: true) : 0;
                    if (taken > 0 && totalAmount > 0f)
                        ReportHealing(patient, state, healing, totalAmount, taken, prevHealth, boostMax, now);
                }

                int healthAfter = prevHealth + taken;
                foreach (var (index, entry) in state.Healers)
                    TickHealedPerSecond(patient, state, index, entry, healthAfter, max, now);
            }
        }

        // The HEALTH_OVERHEALED arms and the drain (:2693-2735): stock runs these for the
        // player, not only for the healed — the fraction and the decay live on the shared,
        // so an item-driven overheal drains at multiplier 1 with no healer up at all.
        int maxHealth = resupply.MaxHealth(patient);
        if (maxHealth <= 0)
            return;
        int health = entities.GetHealth(patient);
        if (!conditions.InCond(patient, TFCond.HealthOverhealed) && health > maxHealth)
            conditions.Add(patient, TFCond.HealthOverhealed);
        else if (conditions.InCond(patient, TFCond.HealthOverhealed) && health <= maxHealth)
            conditions.Remove(patient, TFCond.HealthOverhealed);

        if (decayHealth && health > maxHealth)
        {
            // A player can sit over max with no healer at all (an item did it), and stock drains
            // that too at multiplier 1 (docs/plans/tf2/deferred.tsv: the item arms). The fraction
            // lives on the patient state, which that player has never made: make it.
            state ??= patients[patient.EntryIndex] = new PatientHealing();
            float mult = state.BestDecayMult == -1f ? 1f : state.BestDecayMult;
            float boostAmount = MaxBuffedHealth(patient, ignoreHealthOverMax: true) - maxHealth;
            state.HealFraction += frametime * (boostAmount / (boostDrainTime.Value * mult));   // stock shares m_flHealFraction with the heal
            int drain = (int)state.HealFraction;
            if (drain > 0)
            {
                state.HealFraction -= drain;
                entities.SetHealth(patient, health - drain);    // stock subtracts m_iHealth directly: no pain sound, no damage path
            }
        }
        else if (health <= maxHealth && state is not null && state.BestDecayMult != -1f)
        {
            state.BestDecayMult = -1f;
        }
    }

    /// <summary>
    /// The burning think's HEALTH_BUFF arm (tf_player_shared.cpp:2793-2884):
    /// one <c>player_extinguished</c> per healer of a patient whose burn just
    /// went out. The healer named is the player behind the entry — for a
    /// dispenser heal stock's <c>pHealScorer</c> (the builder), for anything
    /// else the healer itself (stock falls back through
    /// <c>!pHealer-&gt;IsPlayer()</c> the same way) — and an entry with no
    /// player behind it is skipped. The event's <c>itemdefindex</c> is the
    /// named healer's active weapon's item, stock's
    /// <c>INVALID_ITEM_DEF_INDEX</c> (65535) when there is none. Stock's
    /// involved-clients UserMessage and its log lines are not ported.
    /// </summary>
    public void Extinguished(EntityHandle patient)
    {
        if (!patients.TryGetValue(patient.EntryIndex, out var state) || state.Healers.Count == 0)
            return;
        if (!conditions.InCond(patient, TFCond.HealthBuff))
            return;

        foreach (var entry in state.Healers.Values)
        {
            int healerIndex = entry.HealerIndex;
            if (entry.DispenserHeal || !IsPlayerEntity(healerIndex))
                healerIndex = entry.ScorerIndex;
            if (healerIndex < 0)
                continue;

            Fire("player_extinguished", e =>
            {
                events.EventSetInt(e, "victim", patient.EntryIndex);
                events.EventSetInt(e, "healer", healerIndex);
                events.EventSetInt(e, "itemdefindex", ItemDefIndex(healerIndex));
            });
        }
    }

    /// <summary>Whether that entry index is a player, stock's <c>pHealer-&gt;IsPlayer()</c>.</summary>
    private bool IsPlayerEntity(int index) => index >= 0 && entities.IsPlayer(players.GetHandle(index));

    /// <summary>The active weapon's item (<see cref="TF2.Shared.ITFWeaponOwners.ActiveItemOf"/>'s
    /// read): the name the hands hold, the item behind it.</summary>
    private IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        string name = players.GetActiveWeapon(players.GetHandle(playerIndex));
        return name.Length > 0 ? items.Of(playerIndex, name) : null;
    }

    /// <summary>
    /// The named healer's active weapon's item definition index — stock's
    /// <c>GetActiveTFWeapon()-&gt;GetAttributeContainer()-&gt;GetItem()-&gt;
    /// GetDefinitionIndex()</c> (:2875-2879): the item the loadout handed the
    /// weapon, and the unset index when the healer has no weapon item.
    /// </summary>
    private int ItemDefIndex(int healerIndex)
        => items.Of(healerIndex, players.GetActiveWeapon(players.GetHandle(healerIndex)))
           is AttributeContainer { Item: { } item } ? item.Defindex : InvalidItemDefIndex;

    /// <summary>
    /// <c>CTFPlayerShared::GetMaxBuffedHealth</c> (tf_player_shared.cpp:2235) for the standard
    /// player: the class max times <c>tf_max_health_boost</c>, floored to a multiple of five,
    /// and — unless <c>ignoreHealthOverMax</c> (:2256-2264), as the drain asks (:2704) — never
    /// below the health the player already has. The disguise and rune arms are deferred.
    /// </summary>
    public int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false)
    {
        int max = resupply.MaxHealth(patient);
        if (max <= 0)
            return 0;
        int roundDown = (int)(max * maxHealthBoost.Value / 5f) * 5;
        return ignoreHealthOverMax ? roundDown : Math.Max(roundDown, Math.Max(max, entities.GetHealth(patient)));
    }

    /// <summary>
    /// The stat split and the second-window event, stock's healer loop over
    /// <c>flHealAmount = nHealthToAdd * (flAmount / fTotalHealAmount)</c>. Only the healers
    /// that healed share the taken health — a skipped healer's amount never joined the total.
    /// </summary>
    private void ReportHealing(EntityHandle patient, PatientHealing state, List<(int Index, HealerEntry Entry)> healing,
                               float totalAmount, int taken, int prevHealth, int boostMax, float now)
    {
        bool recentlyHurt = TimeSinceLastDamage(patient.EntryIndex, now) <= 1f;
        foreach (var (index, entry) in healing)
        {
            float amount = taken * (entry.Amount / totalAmount);
            // Stock credits pHealScorer — for a dispenser, the builder, not the
            // dispenser itself (tf_player_shared.cpp:2589-2597).
            int scorer = entry.ScorerIndex >= 0 ? entry.ScorerIndex : index;

            // CTF_GameStats.Event_PlayerHealedOther — refused while the patient rides near the
            // buff cap with no fresh damage. Stock sums floats; this seam counts, so the fraction
            // rides on the entry until it makes a whole.
            if (boostMax - prevHealth > 1 || recentlyHurt)
            {
                entry.StatAccum += amount;
                int whole = (int)entry.StatAccum;
                if (whole > 0)
                {
                    entry.StatAccum -= whole;
                    stats.Increment(players.GetHandle(scorer), TFStat.Healing, whole);
                }
            }
            entry.HealedLastSecond += amount;
            if (entry.ScorerIndex < 0)
                entry.HealAccum += amount;
        }
    }

    private void TickHealedPerSecond(EntityHandle patient, PatientHealing state, int healerIndex,
                                     HealerEntry entry, int health, int max, float now)
    {
        // Stock fires when (timer <= curtime || health >= max) && flHealedLastSecond > 1.
        if ((state.HealedPerSecondTimer > now && health < max) || entry.HealedLastSecond <= 1f)
            return;
        if (health - entry.HealedLastSecond < max)
        {
            // Not pure overheal: the non-overheal part goes out as this healer's player_healed.
            int overheal = health - max;
            if (overheal > 0)
                entry.HealedLastSecond -= overheal;
            int amount = (int)entry.HealedLastSecond;
            int scorer = entry.ScorerIndex >= 0 ? entry.ScorerIndex : healerIndex;
            int healerUserId = players.GetUserId(players.GetHandle(scorer));
            int patientUserId = players.GetUserId(patient);
            Fire("player_healed", e =>
            {
                events.EventSetInt(e, "priority", 1);    // HLTV event priority, not transmitted
                events.EventSetInt(e, "patient", patientUserId);
                events.EventSetInt(e, "healer", healerUserId);
                events.EventSetInt(e, "amount", amount);
            });
        }
        entry.HealedLastSecond = 0f;
        state.HealedPerSecondTimer = now + 1f;
    }

    /// <summary>
    /// <c>CTFPlayerShared::AddToSpyCloakMeter</c> (tf_player_shared.cpp:9725): the meter
    /// clamps to 0..100. Stock asks the player's inventory for the invis (and its
    /// no-regen attributes); the port's ask is the carrier's class — the spy is the
    /// class that owns one (docs/plans/tf2/deferred.tsv).
    /// </summary>
    private void AddToSpyCloakMeter(EntityHandle patient, float val)
    {
        if (data.Networked(patient.EntryIndex) is not { } carrier || carrier.Class() != TFClass.Spy)
            return;
        carrier.CloakMeter = Math.Clamp(carrier.CloakMeter + val, 0f, 100f);
    }

    private float TimeSinceLastDamage(int playerIndex, float now)
        => records.LastHit(playerIndex) is { } hit ? now - hit.Time : float.PositiveInfinity;

    private void Fire(string name, Action<IntPtr> fill)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        fill(e);
        events.EventFire(e);
    }

    private static float RemapValClamped(float value, float inMin, float inMax, float outMin, float outMax)
    {
        float t = Math.Clamp((value - inMin) / (inMax - inMin), 0f, 1f);
        return outMin + (outMax - outMin) * t;
    }

    private PatientHealing State(int playerIndex)
        => patients.TryGetValue(playerIndex, out var state) ? state : patients[playerIndex] = new PatientHealing();

    private sealed class HealerEntry
    {
        public int HealerIndex;
        public float Amount;
        public float OverhealBonus = ITFPlayerHealing.StandardOverhealBonus;
        public float DecayMult = ITFPlayerHealing.StandardOverhealDecayMult;
        public bool DispenserHeal;
        public int ScorerIndex = -1;
        public float HealAccum;
        public float StatAccum;
        public float HealedLastSecond;
    }

    private sealed class PatientHealing
    {
        public readonly Dictionary<int, HealerEntry> Healers = new();
        public float HealFraction;
        public float BestDecayMult = -1f;
        public float HealedPerSecondTimer;
    }
}
