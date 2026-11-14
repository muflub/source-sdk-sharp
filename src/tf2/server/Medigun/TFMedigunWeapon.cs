//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Rooms;
using TF2.Server.Player;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Server.Medigun;

/// <summary>
/// The medigun's frame arms, which the per-player component drives: the watchdog
/// and the drain, the holster arms and the meter. <see cref="ITFHealEffects"/> is
/// the attack command's half; this is everything outside a command.
/// </summary>
public interface ITFMedigunWeapons
{
    /// <summary>The medic's <c>HealTargetThink</c> and <c>DrainCharge</c> for this frame.</summary>
    void TickMedic(int ownerIndex, float now, float frametime);

    /// <summary>The weapon went to its owner's offhand: beam off, uber off (drain goes on).</summary>
    void Holster(int ownerIndex, float now);

    /// <summary>The weapon is back in hand: a release still running covers its medic again,
    /// and the target traces run at once (<c>Deploy</c>).</summary>
    void Unholster(int ownerIndex, float now);

    /// <summary>The weapon was destroyed (death, class change): beam, release and uber all go.</summary>
    void RemoveMedigun(int ownerIndex, float now);

    /// <summary>The meter, 0..1 (<c>GetChargeLevel</c>).</summary>
    float ChargeLevel(int ownerIndex);

    /// <summary><c>SetChargeLevel</c>, stock's public meter setter.</summary>
    void SetChargeLevel(int ownerIndex, float level);

    /// <summary>
    /// <c>SubtractCharge</c> (tf_weapon_medigun.cpp:1067): the ask off the meter,
    /// floored at zero; false with no medigun state to move — the pomson's
    /// on-hit drain reads the answer as stock reads <c>bDidDrain</c>.
    /// </summary>
    bool SubtractCharge(int ownerIndex, float amount);

    /// <summary>
    /// <c>SetChargeLevelToPreserve</c> (tf_weapon_medigun.cpp:791): the spawn charge the
    /// meter keeps through the next deploy, capped by the owner's
    /// ubercharge_preserved_on_spawn_max (the Vita-Saw's organs).
    /// </summary>
    void PreserveCharge(int ownerIndex, float amount);

    /// <summary>The beam's patient, or -1 (<c>GetHealingTarget</c>).</summary>
    int HealingTargetIndex(int ownerIndex);

    /// <summary>Whether the meter is released (<c>IsReleasingCharge</c>).</summary>
    bool IsReleasing(int ownerIndex);
}

/// <summary>
/// The medigun itself: stock's <c>CWeaponMedigun</c> (tf_weapon_medigun.cpp) server arms —
/// target acquisition and the beam stick rules, the heal per second through
/// <see cref="ITFPlayerHealing"/>, the charge meter build (faster on a target that is
/// not fully healed, halved at the buffed-max floor, shared between healers,
/// tripled through a setup round), the
/// ÜberCharge release and its drain. The beam itself, the meter's networked state,
/// the sounds and the achievement arms are the rows in docs/plans/tf2/deferred.tsv.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFHealEffects), typeof(ITFMedigunWeapons)])]
public sealed class TFMedigunWeapon : ITFHealEffects, ITFMedigunWeapons, IGameLifecycle, IClientLifecycle, IOrderedHook
{
    // The release rate is TFMedigunChargeRate's twin (weapon_medigun_chargerelease_rate,
    // still a const — docs/divergences.md); the build rate is the convar itself.
    private const float StickRangeMult = 1.2f;     // GetStickRange: the beam holds this far past the script range
    private const float TargetCheckInterval = 1f;  // Maintain/FindNewTargetInSlot's gate
    private const float ThinkInterval = 0.2f;      // HealTargetThink's cadence
    private const float ClientTimeout = 5f;        // the watchdog's now − GetTimeBase bound
    internal const int ResistWeaponMode = 3;       // MEDIGUN_RESIST (tf_weapon_medigun.h:23-29): the Vaccinator's set_weapon_mode row
    internal const int ResistCount = 3;            // MEDIGUN_NUM_RESISTS (tf_weapon_medigun.h:31-37)
    internal const int MedigunChargeBulletResist = 3;   // MEDIGUN_CHARGE_BULLET_RESIST (tf_shareddefs.h:593), GetResistType's base
    internal const float ResistChunkSize = 0.25f;  // GetMinChargeAmount (:803-807): one quarter of the meter (weapon_medigun_resist_num_chunks defaults to 4, :78)
    internal const float VaccinatorResistSeconds = 3f;   // weapon_vaccinator_resist_duration (:89)
    internal const float VitaSawChargePerHit = 0.15f;  // VITASAW_CHARGE_PER_HIT (tf_weapon_bonesaw.cpp:19)

    /// <summary>ubercharge_preserved_on_spawn_max, the Vita-Saw's kept-charge cap.</summary>
    internal const string VitaSawPreserveAttrib = "ubercharge_preserved_on_spawn_max";

    private readonly IEnginePlayers players;
    private readonly IEngineEntities entities;
    private readonly IEngineWorldQuery world;
    private readonly ITFPlayerHealing healing;
    private readonly ITFPlayerConditions conditions;
    private readonly ITFPlayerResupply resupply;
    private readonly TFInvulnTimeConVar invulnTime;
    private readonly WeaponMedigunChargeRateConVar medigunChargeRate;
    private readonly ITFPlayerStats stats;
    private readonly IEngineGameEvents events;
    private readonly IConnectedClients clients;
    private readonly ITFPlayerData data;
    private readonly ITFRespawnRooms rooms;
    private readonly AttributeHooks hooks;
    private readonly TFPlayerItems items;
    private readonly ITFRoundSetup roundSetup;
    private readonly ITFRoundStateView rounds;
    private readonly TFMedigunLagCompConVar lagGate;
    private readonly ILagCompensation lagCompensation;

    private readonly Dictionary<int, MedicState> medics = new();

    // The charge kept through a death, per owner: recorded as the medigun dies
    // (RemoveMedigun), spent by the first state the respawn builds (State). Stock
    // holds it on the weapon's m_flChargeLevelToPreserve and clamps at WeaponReset
    // (:291-299), then zeroes the keep 100ms into the new life
    // (tf_player.cpp:2114-2124) — one death's keep, spent once.
    private readonly Dictionary<int, float> preserved = new();

    public TFMedigunWeapon(IEnginePlayers players, IEngineEntities entities, IEngineWorldQuery world,
                           ITFPlayerHealing healing, ITFPlayerConditions conditions, ITFPlayerResupply resupply,
                           TFInvulnTimeConVar invulnTime, WeaponMedigunChargeRateConVar medigunChargeRate,
                           ITFRespawnRooms rooms, AttributeHooks hooks,
                           TFPlayerItems items,
                           ITFPlayerStats stats, IEngineGameEvents events,
                           IConnectedClients clients, ITFPlayerData data,
                           ITFRoundSetup roundSetup, TFMedigunLagCompConVar lagGate,
                           ILagCompensation lagCompensation, ITFRoundStateView rounds)
    {
        this.players = players;
        this.entities = entities;
        this.world = world;
        this.healing = healing;
        this.conditions = conditions;
        this.resupply = resupply;
        this.invulnTime = invulnTime;
        this.medigunChargeRate = medigunChargeRate;
        this.rooms = rooms;
        this.hooks = hooks;
        this.items = items;
        this.stats = stats;
        this.events = events;
        this.clients = clients;
        this.data = data;
        this.roundSetup = roundSetup;
        this.lagGate = lagGate;
        this.lagCompensation = lagCompensation;
        this.rounds = rounds;
    }

    public int HookOrder => TFMedigunOrders.Weapon;

    public int LifecycleOrder => TFMedigunOrders.Weapon;

    public int ClientLifecycleOrder => TFMedigunOrders.Weapon;

    public void OnLevelShutdown()
    {
        medics.Clear();
        preserved.Clear();
    }

    public void OnClientDisconnected(int playerIndex)
    {
        medics.Remove(playerIndex);
        preserved.Remove(playerIndex);
    }

    // ---- the per-command beam: CWeaponMedigun::ItemPostFrame's attack arms -----------------

    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held)
    {
        int owner = attack.Command.OwnerIndex;
        float now = attack.Now;
        var state = State(owner);
        state.Holstered = false;    // the weapon answered to this command, so it is in hand (Deploy clears m_bHolstered)

        // The medigun's WeaponData block: Damage 24 (the heal rate), Range 450 (the beam length).
        var mode = script.Primary;

        // Stock reads the client's tf_medigun_autoheal per player and fails closed without it.
        bool autoHeal = clients.ClientConVar(owner, "tf_medigun_autoheal") == "1";
        bool press = held && !state.Attack1Down;
        state.Attack1Down = held;

        if (autoHeal)
        {
            // The toggle arm: a press drops the beam and re-aims in the same command — stock
            // removes on the press (tf_weapon_medigun.cpp:1563-1573) and reaches
            // PrimaryAttack through :1585 that frame. Between presses an existing beam goes on.
            if (press && state.Healing)
                RemoveHealingTarget(owner, state, now);

            // Stock :1575-1578: a medic who taunts drops the beam instead of holding it,
            // and the frame does not re-fire either. Stock's first disjunct (m_iState !=
            // WEAPON_IS_ACTIVE) is the holstered flag: Heal runs only after Deploy, and
            // Holster already drops the beam itself.
            if (state.Healing && (state.Holstered || conditions.InCond(players.GetHandle(owner), TFCond.Taunting)))
                RemoveHealingTarget(owner, state, now);
            else if (state.Healing || held)
                PrimaryAttack(owner, state, mode, now);
        }
        else if (held)
            PrimaryAttack(owner, state, mode, now);
        else if (state.Healing)
            RemoveHealingTarget(owner, state, now);

        // The secondary arm runs after the primary one, every frame IN_ATTACK2 is held
        // (:1605-1608); its own guards refuse (:1845-1861).
        if ((attack.Weapon.LastButtons & (int)WeaponButtons.Attack2) != 0)
            SecondaryAttack(owner, state, now);

        // The Vaccinator's resist cycle rides the reload press edge (:1618-1628): one step
        // per press, the down-flag let go when the button comes back up.
        if ((attack.Weapon.LastButtons & (int)WeaponButtons.Reload) != 0 && !state.ReloadDown)
        {
            CycleResistType(owner, state, now);
            state.ReloadDown = true;
        }
        else if ((attack.Weapon.LastButtons & (int)WeaponButtons.Reload) == 0 && state.ReloadDown)
            state.ReloadDown = false;

        // The meter's networked state rides the weapon (stock networks
        // m_flChargeLevel on DT_WeaponMedigun): the level, and the clocks the
        // client's replay of the same meter runs on. It is current at the next
        // command the gun answers — a dev-set level or a holstered drain waits
        // for one (docs/divergences.md).
        attack.Weapon.BehaviourMeter = state.Charge;
        attack.Weapon.BehaviourTimeA = state.LastHealCall;
        attack.Weapon.BehaviourTimeB = now;
        // The replay's own words: the check clock the pick gates on, and the
        // press edge its image of m_bCanChangeTarget reads back (:1555).
        attack.Weapon.BehaviourTimeC = state.NextTargetCheckTime;
        attack.Weapon.BehaviourMode = state.Attack1Down ? ITFMedigunPrediction.Attack1Down : 0;

        Publish(owner, state);
    }

    // ---- the Vaccinator's resist cycle: CWeaponMedigun::CycleResistType ---------------------

    /// <summary>
    /// <c>CWeaponMedigun::CycleResistType</c> (:838-901): the Vaccinator's three resists in
    /// bullet, blast, fire order, one step per reload press (:876-877). The attach's passive
    /// comes off the patient and the medic when this medic — or nobody — put it on
    /// (:855-871); the medic's own recalc runs either way (:883-886), and the new passive
    /// pair lands on patient and medic only under a patient (:889-895).
    /// </summary>
    private void CycleResistType(int owner, MedicState state, float now)
    {
        var ownerHandle = players.GetHandle(owner);

        // Resist medigun only (:841), and never mid-release (:844).
        if (hooks.Int(0, "set_weapon_mode", items.ManagerOf(owner)) != ResistWeaponMode || state.Releasing)
            return;

        var oldPassive = SmallResistCond(ResistType(owner, state));
        if (state.HealingTarget >= 0)
            RemoveIfOwnOrNoProvider(players.GetHandle(state.HealingTarget), oldPassive, ownerHandle);   // :853-862
        RemoveIfOwnOrNoProvider(ownerHandle, oldPassive, ownerHandle);                                   // :864-872

        state.ResistIndex = (state.ResistIndex + 1) % ResistCount;                                       // :876-877

        healing.RecalculateChargeEffects(ownerHandle, now);                                              // :883-886
        if (state.HealingTarget >= 0)
        {
            var patient = players.GetHandle(state.HealingTarget);
            healing.RecalculateChargeEffects(patient, now);                                              // :892
            var passive = SmallResistCond(ResistType(owner, state));
            conditions.Add(patient, passive, TFConditions.Permanent, ownerHandle);                      // :893
            conditions.Add(ownerHandle, passive, TFConditions.Permanent, ownerHandle);                  // :894
        }
    }

    /// <summary>
    /// <c>GetConditionProvider</c>'s remove gate (:855-861, :867-871): the old passive comes
    /// off only when this medic, or nobody, applied it; another medic's bubble stands.
    /// </summary>
    private void RemoveIfOwnOrNoProvider(EntityHandle player, TFCond cond, EntityHandle ownerHandle)
    {
        EntityHandle provider = conditions.Of(player.EntryIndex)?.Provider(cond) ?? default;
        if (provider == ownerHandle || provider == default)
            conditions.Remove(player, cond);
    }

    /// <summary>
    /// <c>GetChargeType</c> (:818-832) and <c>GetResistType</c> (:906-914): the item's charge
    /// row plus the cycles pressed, folded onto the three resists. A manager that names no
    /// charge row answers the plain base, as stock's hook default does.
    /// </summary>
    private int ResistType(int owner, MedicState state)
    {
        int chargeType = hooks.Int(0, "set_charge_type", items.ManagerOf(owner)) + state.ResistIndex;   // :829
        int resist = (chargeType - MedigunChargeBulletResist) % ResistCount;                               // :910-912
        return resist < 0 ? resist + ResistCount : resist;
    }

    /// <summary><c>g_MedigunResistConditions</c>' passive half (:66-71): the attach's small resist.</summary>
    private static TFCond SmallResistCond(int resistType) => resistType switch
    {
        1 => TFCond.MedigunSmallBlastResist,
        2 => TFCond.MedigunSmallFireResist,
        _ => TFCond.MedigunSmallBulletResist,
    };

    /// <summary><c>g_MedigunResistConditions</c>' deployed half (:66-71): the bubble's typed uber.</summary>
    private static TFCond UberResistCond(int resistType) => resistType switch
    {
        1 => TFCond.MedigunUberBlastResist,
        2 => TFCond.MedigunUberFireResist,
        _ => TFCond.MedigunUberBulletResist,
    };

    // ---- the charge, as stock's weapon keeps it on itself -----------------------------

    /// <summary>The meter, 0..1 (<c>GetChargeLevel</c>).</summary>
    public float ChargeLevel(int ownerIndex) => medics.TryGetValue(ownerIndex, out var s) ? s.Charge : 0f;

    /// <summary>Whether the meter is released (<c>IsReleasingCharge</c> minus the holster arm).</summary>
    public bool IsReleasing(int ownerIndex) => medics.TryGetValue(ownerIndex, out var s) && s.Releasing;

    /// <summary>The beam's patient, or -1 (<c>GetHealingTarget</c>). The component's tick reads it.</summary>
    public int HealingTargetIndex(int ownerIndex) => medics.TryGetValue(ownerIndex, out var s) ? s.HealingTarget : -1;

    /// <summary><c>SetChargeLevel</c>: the dev and HUD-side setter stock keeps public.</summary>
    public void SetChargeLevel(int ownerIndex, float level)
    {
        var state = State(ownerIndex);
        state.Charge = Math.Clamp(level, 0f, 1f);
        Publish(ownerIndex, state);
    }

    /// <summary>
    /// Stock's <c>SetChargeLevelToPreserve</c> (tf_weapon_medigun.cpp:791-798): the
    /// Vita-Saw's organs ride here, the amount clamped by the owner-walked
    /// ubercharge_preserved_on_spawn_max — stock's second walk of the same attribute,
    /// ON_OTHER the owner, beside the weapon walk that gated the caller.
    /// </summary>
    public void PreserveCharge(int ownerIndex, float amount)
    {
        float cap = hooks.Float(0f, VitaSawPreserveAttrib, items.ManagerOf(ownerIndex));
        State(ownerIndex).Preserve = MathF.Min(amount, cap);
    }

    /// <summary>
    /// Stock's <c>CWeaponMedigun::SubtractCharge</c> (tf_weapon_medigun.cpp:1067-1071):
    /// the ask, floored beneath zero, off the meter — the pomson's on-hit charge
    /// drain is the caller (<c>ApplyPostHitEffects</c>, tf_weaponbase.cpp:5582,
    /// whose own <c>IsReleasingCharge</c> gate stands there too). The deploy-state
    /// arm's resist chunks and release stop ride the release clock's own
    /// zero-crossing, which a refused drain never reaches; the meter's word on the
    /// weapon entity waits for the next command the gun answers, the recorded
    /// lag. No medigun state is stock's null <c>pMedigun</c> (:5571): nothing to
    /// drain, the ask answered.
    /// </summary>
    public bool SubtractCharge(int ownerIndex, float amount)
    {
        if (!medics.TryGetValue(ownerIndex, out var state))
            return false;

        state.Charge = MathF.Max(0f, state.Charge - MathF.Max(0f, amount));
        Publish(ownerIndex, state);
        return true;
    }

    /// <summary>
    /// The medigun's state onto its owner's carrier — the release and the beam's
    /// patient (the charge LEVEL rides the weapon's own state, written in
    /// <see cref="Heal"/>: stock networks it on DT_WeaponMedigun). Every entry
    /// point that moves the release or the beam ends here.
    /// </summary>
    private void Publish(int ownerIndex, MedicState state)
    {
        if (data.Networked(ownerIndex) is not { } carrier)
            return;
        carrier.ChargeReleasing = state.Releasing;
        carrier.HealTargetIndex = state.HealingTarget;
        carrier.MedicCharge = state.Charge;
    }

    /// <summary>
    /// Stock's <c>Deploy</c> (:390): the holster flag clears, a release still running
    /// recalculates the owner's effect back on non-instantly (:400-403), and the target
    /// traces run on the next command rather than waiting out the check window (:422).
    /// </summary>
    public void Unholster(int ownerIndex, float now)
    {
        var state = State(ownerIndex);
        state.Holstered = false;
        healing.NoteReleasing(ownerIndex, state.Releasing, state.ChargeType);   // IsReleasingCharge = releasing and not holstered
        if (state.Releasing)
            healing.RecalculateChargeEffects(players.GetHandle(ownerIndex), now);
        state.NextTargetCheckTime = now;
        Publish(ownerIndex, state);
    }

    /// <summary>
    /// Stock's <c>Holster</c> (:435): the beam drops, the holster is recorded (which takes the
    /// uber off while the drain keeps running, <c>IsReleasingCharge</c> being release and not
    /// holstered), and the owner's own effect recalculates instantly.
    /// </summary>
    public void Holster(int ownerIndex, float now)
    {
        if (!medics.TryGetValue(ownerIndex, out var state))
            return;
        RemoveHealingTarget(ownerIndex, state, now);
        state.Holstered = true;
        healing.NoteReleasing(ownerIndex, false, state.ChargeType);              // IsReleasingCharge includes not-holstered
        healing.RemoveChargeEffect(players.GetHandle(ownerIndex));
        Publish(ownerIndex, state);
    }

    /// <summary>
    /// Stock's <c>UpdateOnRemove</c> (:459): the beam drops, the release dies with the
    /// weapon, the medic's own uber goes instantly (<c>RecalcEffectOnTarget(pOwner, true)</c>)
    /// and the patient's wears off over <c>tf_invuln_time</c> — the <c>StopHealing</c>
    /// inside <c>RemoveHealingTarget</c> ends in the patient's recalc. The patients let go
    /// during a release keep the wear-off their detach started.
    /// </summary>
    public void RemoveMedigun(int ownerIndex, float now)
    {
        if (!medics.TryGetValue(ownerIndex, out var state))
            return;
        if (state.HealingTarget >= 0)
        {
            var patient = players.GetHandle(state.HealingTarget);
            healing.StopHealing(patient, players.GetHandle(ownerIndex), now);
        }
        state.Releasing = false;
        healing.NoteReleasing(ownerIndex, false, state.ChargeType);
        healing.RemoveChargeEffect(players.GetHandle(ownerIndex));
        Publish(ownerIndex, state);
        // The Vita-Saw's keep (WeaponReset's round-running arm,
        // tf_weapon_medigun.cpp:291-295): a death mid-round leaves the lesser of
        // the meter and the preserved cap for the next spawn; outside a round
        // stock's reset zeroes the meter outright, as a missing view does here.
        if (rounds.State == RoundState.RoundRunning)
            preserved[ownerIndex] = MathF.Min(state.Charge, state.Preserve);
        medics.Remove(ownerIndex);
    }

    // ---- CWeaponMedigun::PrimaryAttack / FindAndHealTargets ------------------------------------

    private void PrimaryAttack(int owner, MedicState state, TFWeaponModeData mode, float now)
    {
        // The medigun's session (tf_weapon_medigun.cpp:1774-1775, :1799-1801):
        // while tf_medigun_lagcomp is on, the target search sees the other
        // players where the medic saw them; stock reads the flag at both ends.
        EntityHandle shooter = players.GetHandle(owner);
        if (lagGate.Value)
            lagCompensation.Start(shooter);
        try
        {
            HealPrimary(owner, state, mode, now);
        }
        finally
        {
            if (lagGate.Value)
                lagCompensation.Finish(shooter);
        }
    }

    private void HealPrimary(int owner, MedicState state, TFWeaponModeData mode, float now)
    {
        int heldIndex = state.HealingTarget;    // :1232's capture, before maintain and find

        // FindAndHealTargets (:1237-1246): maintain the beam or find a new target — never both.
        if (state.HealingTarget >= 0)
            MaintainTargetInSlot(owner, state, mode, now);
        else
            FindNewTargetInSlot(owner, state, mode, now);

        state.Healing = state.HealingTarget >= 0;

        if (state.HealingTarget >= 0 && !state.Releasing)
        {
            // Stock's FindAndHealTargets charge build on its frametime; the seam has no frametime,
            // so the seconds since this weapon's last command pay in (docs/divergences.md).
            // A zero rate is stock's guard round the whole build (:1275): the meter stands.
            float dt = state.LastHealCall < 0f ? 0f : now - state.LastHealCall;
            float rate = medigunChargeRate.Value;
            float amount = rate > 0f ? dt / rate : 0f;

            // The setup round keeps the meter building: the crawl and the full-health
            // bonus arm only outside it (tf_weapon_medigun.cpp:1288, 1321 — stock's
            // !(InSetup() && GetActiveRoundTimer())).
            bool buildBonus = !roundSetup.InSetupWithRoundTimer;

            var patient = players.GetHandle(state.HealingTarget);
            if (buildBonus && entities.GetHealth(patient) >= (int)(healing.MaxBuffedHealth(patient) * 0.95f))
                amount *= 0.5f;                       // so close to the buffed max the meter crawls
            int healers = healing.NumHealers(state.HealingTarget);
            if (healers > 1)
                amount /= healers;                    // and healers share the one meter

            // The full-health bonus, then the meter's own rate — the medic's walk,
            // after the healer split and before the patient's bonus, as stock orders
            // them (tf_weapon_medigun.cpp:1327-1332).
            if (buildBonus && entities.GetHealth(patient) >= resupply.MaxHealth(patient))
                amount = hooks.Float(amount, "mult_medigun_overheal_uberchargerate", items.ManagerOf(owner));
            amount = hooks.Float(amount, "mult_medigun_uberchargerate", items.ManagerOf(owner));

            // The bonus the patient gives the healer (:1329-1340): the hook rides the
            // patient's own manager, and stock asks the rooms for patient and medic in
            // turn — refused the moment either stands in one (the || short-circuits).
            // Both asks carry the medic's world centre: stock's is the weapon entity's,
            // which our server medigun does not have (docs/divergences.md). The
            // patient is the entry capture, as stock's pTarget is: with our beam it is
            // the same player whenever this block runs.
            if (heldIndex >= 0)
            {
                var medic = players.GetHandle(owner);
                var medicCenter = entities.GetWorldSpaceCenter(medic);
                bool inRespawnRoom = rooms.PointInRespawnRoom(players.GetHandle(heldIndex), medicCenter, false)
                                  || rooms.PointInRespawnRoom(medic, medicCenter, false);
                if (!inRespawnRoom)
                    amount = hooks.Float(amount, "mult_uberchargerate_for_healer", items.ManagerOf(heldIndex));
            }

            // The setup round's triple, the last word before the meter takes it
            // (tf_weapon_medigun.cpp:1347-1350). The quick-build x4 beside it is
            // MvM's IsQuickBuildTime, still on the ledger.
            if (roundSetup.InSetupWithRoundTimer)
                amount *= 3f;

            state.Charge = Math.Min(1f, state.Charge + amount);
            state.LastHealCall = now;
        }
        else if (state.HealingTarget >= 0)
            state.LastHealCall = now;                 // released: the drain owns the meter now, the clock still moves
    }

    /// <summary>
    /// <c>CWeaponMedigun::MaintainTargetInSlot</c> (:656): the stick rule, then the three visibility
    /// traces once a second — the gate and its reset live here (:675-680), not around the call.
    /// </summary>
    private void MaintainTargetInSlot(int owner, MedicState state, TFWeaponModeData mode, float now)
    {
        var ownerHandle = players.GetHandle(owner);
        var target = players.GetHandle(state.HealingTarget);
        var eye = entities.GetEyePosition(ownerHandle);

        // The stick rule: the beam holds to 1.2× the script range and no further (:675).
        if ((eye - entities.GetWorldSpaceCenter(target)).Length >= mode.Range * StickRangeMult)
        {
            RemoveHealingTarget(owner, state, now);
            return;
        }
        if (now < state.NextTargetCheckTime)
            return;                                    // :677-678: between checks the beam holds untraced
        state.NextTargetCheckTime = now + TargetCheckInterval;    // :680

        var end = eye + MoveVec.AngleForward(entities.GetEyeAngles(ownerHandle)) * mode.Range;
        // trace 1: the aim line, hull — the target must still be what it hits.
        var aim = world.TraceLine(eye, end, TraceMask.ShotHull, ignore: ownerHandle);
        if (aim.Entity == target)
            return;
        // traces 2 and 3: to the target's center and its eyes — nothing but the target and enemies may stand in the way.
        if (TracePast(ownerHandle, target, eye, entities.GetWorldSpaceCenter(target))
            && TracePast(ownerHandle, target, eye, entities.GetEyePosition(target)))
            return;
        RemoveHealingTarget(owner, state, now);
    }

    /// <summary>
    /// <c>CWeaponMedigun::FindNewTargetForSlot</c> (:719): the lenient hull trace
    /// (<c>MASK_SHOT &amp; ~CONTENTS_HITBOX</c>) gives the candidate; the hitbox trace
    /// replaces it only with a different allowed entity (:739-755), so a refused nearest
    /// hitbox leaves the hull target standing.
    /// </summary>
    private void FindNewTargetInSlot(int owner, MedicState state, TFWeaponModeData mode, float now)
    {
        var ownerHandle = players.GetHandle(owner);
        var eye = entities.GetEyePosition(ownerHandle);
        var end = eye + MoveVec.AngleForward(entities.GetEyeAngles(ownerHandle)) * mode.Range;

        var loose = world.TraceLine(eye, end, TraceMask.ShotHull, ignore: ownerHandle);
        if (!loose.Hit)
            return;
        var candidate = loose.Entity;

        var hitbox = world.TraceLine(eye, end, TraceMask.Shot, ignore: ownerHandle);
        if (hitbox.Hit && hitbox.Entity != candidate && AllowedToHealTarget(ownerHandle, hitbox.Entity))
            candidate = hitbox.Entity;
        else if (!AllowedToHealTarget(ownerHandle, candidate))
            return;

        StartHealingTarget(owner, state, candidate.EntryIndex, mode, now);
        state.NextTargetCheckTime = now + TargetCheckInterval;
    }

    /// <summary><c>CWeaponMedigun::StartHealingTarget</c> (:1024): the patient takes on this healer at the script's rate.</summary>
    private void StartHealingTarget(int owner, MedicState state, int patientIndex, TFWeaponModeData mode, float now)
    {
        if (patientIndex == state.HealingTarget)
            return;
        var ownerHandle = players.GetHandle(owner);
        if (state.HealingTarget >= 0)
            healing.StopHealing(players.GetHandle(state.HealingTarget), ownerHandle, now);
        state.HealingTarget = patientIndex;
        if (patientIndex < 0)
            return;
        var patient = players.GetHandle(patientIndex);
        healing.Heal(patient, ownerHandle, mode.Damage, now,
                     OverHealBonus(owner, patientIndex), OverHealDecayMult(owner));
        healing.RecalculateChargeEffects(patient, now);

        // The Vaccinator's attach (:1032-1037): the small resist pair lands on patient and
        // medic with the beam, this medic its provider.
        if (hooks.Int(0, "set_weapon_mode", items.ManagerOf(owner)) == ResistWeaponMode)
        {
            var passive = SmallResistCond(ResistType(owner, state));
            conditions.Add(patient, passive, TFConditions.Permanent, ownerHandle);       // :1035
            conditions.Add(ownerHandle, passive, TFConditions.Permanent, ownerHandle);    // :1036
        }
    }

    /// <summary>
    /// <c>CWeaponMedigun::GetOverHealBonus</c> (:2664-2703): bonuses additive,
    /// penalties percentage — the cvar's base walked by the medic's own
    /// <c>mult_medigun_overheal_amount</c>, then the patient's
    /// <c>mult_patient_overheal_penalty</c> (:2671) and its active weapon's twin
    /// (:2672); the mod rides the running value, as stock's one float does. The
    /// upgrade and rune-carrier arms (:2689-2700) wait with the other reads on
    /// the deferred list. Stock's base reads <c>tf_max_health_boost</c> live;
    /// the seam holds it as the healing side's constant (docs/divergences.md),
    /// so the no-attribute walk answers exactly that constant back.
    /// </summary>
    private float OverHealBonus(int owner, int patientIndex)
    {
        float bonus = ITFPlayerHealing.StandardOverhealBonus - 1f;          // :2667

        float mod = hooks.Float(1f, "mult_medigun_overheal_amount", items.ManagerOf(owner));                                     // :2669
        mod = hooks.Float(mod, "mult_patient_overheal_penalty", items.ManagerOf(patientIndex));                                   // :2671
        mod = hooks.Float(mod, "mult_patient_overheal_penalty_active", ActiveItemOf(patientIndex));                              // :2672

        if (mod >= 1f)                       // :2673-2676: bonuses add to the base
            bonus += mod;
        else if (bonus > 0f)                 // :2677-2681: penalties scale the base, the +1 rebuilds the floor
            bonus = bonus * mod + 1f;

        return Math.Max(bonus, 1f);          // :2683-2687 safety net
    }

    /// <summary>
    /// <c>CWeaponMedigun::GetOverHealDecayMult</c> (:2708): the drain multiplier this heal
    /// carries — <c>mult_medigun_overheal_decay</c> walks the medic's items (:2711), and half
    /// the owner's <c>overheal_expert</c> rides on top (:2712, hooked on the owner at :2726).
    /// The patient keeps the lowest of its healers' multipliers, which is what the overheal
    /// drain divides by (<c>m_flBestOverhealDecayMult</c>, tf_player_shared.cpp:2531).
    /// </summary>
    private float OverHealDecayMult(int owner)
    {
        float mult = hooks.Float(1f, "mult_medigun_overheal_decay", items.ManagerOf(owner));     // :2711
        float expert = hooks.Float(0f, "overheal_expert", items.ManagerOf(owner));               // :2726
        return Math.Max(mult, mult + expert / 2f);                                               // :2712
    }

    /// <summary>
    /// <c>CWeaponMedigun::RemoveHealingTarget</c> (:1661): the beam drops; a released meter remembers
    /// the patient it let go of (the drain's surcharge), and the patient stops being healed by us.
    /// </summary>
    private void RemoveHealingTarget(int owner, MedicState state, float now)
    {
        if (state.HealingTarget < 0)
            return;
        if (state.Releasing)
        {
            // Already detached: the entry's time updates, it does not double (:1668-1686).
            int i = state.Detached.FindIndex(d => d.Target == state.HealingTarget);
            if (i >= 0)
                state.Detached[i] = (state.HealingTarget, now);
            else
                state.Detached.Add((state.HealingTarget, now));
        }
        healing.StopHealing(players.GetHandle(state.HealingTarget), players.GetHandle(owner), now);
        state.LastHealingTarget = state.HealingTarget;       // stock's m_hLastHealingTarget (:1726)
        state.HealingTarget = -1;
        state.Healing = false;
        state.LastHealCall = -1f;    // the meter builds on frames the beam is up, never a backlog for the span it was down
    }

    /// <summary>
    /// <c>CWeaponMedigun::AllowedToHealTarget</c> (:563): alive, visible (or the healer itself cloaked),
    /// own team — or an enemy under <c>TF_COND_DISGUISED</c> whose disguise team is the
    /// medic's (:590-599); the disguise team rides the target's carrier.
    /// </summary>
    private bool AllowedToHealTarget(EntityHandle owner, EntityHandle target)
    {
        if (!players.IsAlive(target))
            return false;
        if (conditions.InCond(target, TFCond.Stealthed) && !conditions.InCond(owner, TFCond.Stealthed))
            return false;
        int ownerTeam = players.GetTeam(owner);
        return players.GetTeam(target) == ownerTeam
            || (conditions.InCond(target, TFCond.Disguised) && DisguiseTeamOf(target) == ownerTeam);
    }

    /// <summary>The target's <c>m_nDisguiseTeam</c>, or unassigned when it has no carrier.</summary>
    private int DisguiseTeamOf(EntityHandle target)
        => data.Networked(target.EntryIndex)?.DisguiseTeam ?? (int)TFTeam.Unassigned;

    // ---- CWeaponMedigun::ItemPostFrame's secondary arm: the ÜberCharge release ------------------

    private void SecondaryAttack(int owner, MedicState state, float now)
    {
        var ownerHandle = players.GetHandle(owner);
        // Stock captures the patient whole (:1816-1820); without one the arms run solo.
        EntityHandle patientHandle = state.HealingTarget >= 0 ? players.GetHandle(state.HealingTarget) : default;
        int mode = hooks.Int(0, "set_weapon_mode", items.ManagerOf(owner));    // GetMedigunType (:781-785)

        // The Vaccinator's early outs (:1822-1834): a bubble already on the medic — and on
        // the patient too, when there is one — makes a held button answer nothing.
        if (mode == ResistWeaponMode)
        {
            var bubble = UberResistCond(ResistType(owner, state));
            if (conditions.InCond(ownerHandle, bubble)
                && (patientHandle == default || conditions.InCond(patientHandle, bubble)))
                return;
        }

        // The deny (:1839-1844): the plain gun wants the whole meter, the Vaccinator one
        // quarter (GetMinChargeAmount :803-813), and never a second release while one runs.
        float chunk = mode == ResistWeaponMode ? ResistChunkSize : 1f;
        if (state.Charge < chunk || state.Releasing)
            return;

        // Stock gates the release on the owner's CanRecieveMedigunChargeEffect only
        // (:1857): the patient may be NULL (:1822-1826), and the patient's own refusal
        // lives inside their recalc (tf_player_shared.cpp:8899).
        if (!healing.CanReceiveMedigunChargeEffect(ownerHandle))
            return;

        if (mode == ResistWeaponMode)
        {
            // The bubble arm (:1979-1991): the typed uber goes straight onto the medic,
            // then the patient, for the resist seconds plus the medic's add_uber_time;
            // the meter pays one chunk at once and no drain runs. The plain arm's stat
            // and recalc grants stand down (:1883, :1903-1915).
            float seconds = hooks.Float(VaccinatorResistSeconds, "add_uber_time", items.ManagerOf(owner));
            var bubble = UberResistCond(ResistType(owner, state));
            conditions.Add(ownerHandle, bubble, seconds, ownerHandle);                   // :1986
            state.Charge -= chunk;                                                       // :1987
            if (patientHandle != default)
                conditions.Add(patientHandle, bubble, seconds, ownerHandle);             // :1990
        }
        else
        {
            state.Releasing = true;
            // Stock's GetChargeType (:818-832): the item's set_charge_type attribute names
            // the charge effect — MEDIGUN_CHARGE_INVULN = 0 stays the plain über, rows 1-5
            // (tf_shareddefs.h:587-598) name their g_MedigunEffects rows
            // (tf_weapon_medigun.cpp:49-56).
            state.ChargeType = hooks.Int(0, "set_charge_type", items.ManagerOf(owner));
            healing.NoteReleasing(owner, true, state.ChargeType);

            // The player_invulned event is the recalc's grant arm (SendNewInvulnGameEvent,
            // TFPlayerHealing), not the pop's; Event_PlayerInvulnerable (:1897) is the stat above.
            stats.Increment(ownerHandle, TFStat.Invulns, 1);

            // The condition itself is nobody's to add here: stock's pop only recalculate the
            // affected shares (:1903-1915), whose SetChargeEffect grants the row to the
            // covered player — medic and patient alike — for the release rate plus the
            // provider's add_uber_time, plus one (tf_player_shared.cpp:9053-9055, :9074).
            // The release's length is the drain's, not this add's.
            healing.RecalculateChargeEffects(ownerHandle, now);
            if (state.HealingTarget >= 0)
                healing.RecalculateChargeEffects(players.GetHandle(state.HealingTarget), now);
        }

        // The deployed event rides every mode's pop (:1920-1929).
        Fire("player_chargedeployed", e =>
        {
            events!.EventSetInt(e, "userid", players.GetUserId(ownerHandle));
            if (state.HealingTarget >= 0)
                events.EventSetInt(e, "targetid", players.GetUserId(players.GetHandle(state.HealingTarget)));
        });
    }

    // ---- CWeaponMedigun::ItemPostFrame's frame arms: the think, the drain --------------------------

    /// <summary>
    /// The medic's frame: <c>HealTargetThink</c>'s watchdog (:946) on its 0.2s cadence and
    /// <c>DrainCharge</c>'s release (:1438). The component's per-player tick calls it; the drain
    /// goes on while holstered (stock's <c>ItemHolsterFrame</c>), the watchdog only while the
    /// beam is up.
    /// </summary>
    public void TickMedic(int ownerIndex, float now, float frametime)
    {
        if (!medics.TryGetValue(ownerIndex, out var state))
            return;
        var ownerHandle = players.GetHandle(ownerIndex);

        if (state.Healing && state.HealingTarget >= 0 && now >= state.NextThinkTime)
        {
            state.NextThinkTime = now + ThinkInterval;
            var target = players.GetHandle(state.HealingTarget);
            // Stock compares against the owner's GetTimeBase — the medigun's own last command answers here.
            bool goneStale = now - (state.LastHealCall < 0f ? now : state.LastHealCall) > ClientTimeout;
            if (!players.IsAlive(target) || goneStale || !AllowedToHealTarget(ownerHandle, target))
                RemoveHealingTarget(ownerIndex, state, now);

            // HealTargetThink's class arm (:960-975): the saved class (stock's m_nHealTargetClass,
            // written only here, ctor init at :313) holds what the patient had at the last think.
            // Changed mid-heal, on the same patient the gun last held (:971), stock's Lower() (:1636)
            // drops the beam so the re-attach re-evaluates the patient's attributes. Stock's
            // TeamFortress_SetSpeed beside it (:968) is the movement layer's own per-move read.
            if (state.Healing && entities.IsPlayer(target))
            {
                int patientClass = data.Networked(state.HealingTarget)?.PlayerClass ?? 0;
                if (patientClass != state.HealTargetClass)
                {
                    state.HealTargetClass = patientClass;
                    if (state.LastHealingTarget == state.HealingTarget)
                        RemoveHealingTarget(ownerIndex, state, now);    // Lower()'s beam drop (:1639-1642)
                }
            }
        }

        if (state.Releasing)
        {
            // The release's seconds ride the medic's walk, carried in an int as stock
            // carries it (tf_weapon_medigun.cpp:1447-1448) — the truncation is stock's.
            int releaseSeconds = (int)hooks.Float(TFMedigunBehaviour.ChargeReleaseRate, "add_uber_time",
                                                  items.ManagerOf(ownerIndex));
            float baseAmount = frametime / releaseSeconds;
            float amount = baseAmount;
            state.Detached.RemoveAll(d => now - d.Time > invulnTime.Value);
            foreach (var (target, _) in state.Detached)
                if (target != state.HealingTarget && players.IsAlive(players.GetHandle(target)))
                    amount += baseAmount * 0.5f;      // each patient let go during the release slows the drain by half its rate
            state.Charge -= amount;
            if (state.Charge <= 0f)
            {
                // CWeaponMedigun::SubtractChargeAndUpdateDeployState (:1472): at empty the release dies
                // and everyone it covered — the medic, the current patient, the ones let go during
                // the release — wears its uber off.
                state.Charge = 0f;
                state.Releasing = false;
                healing.NoteReleasing(ownerIndex, false, state.ChargeType);
                healing.RecalculateChargeEffects(ownerHandle, now);
                if (state.HealingTarget >= 0)
                    healing.RecalculateChargeEffects(players.GetHandle(state.HealingTarget), now);
                foreach (var (target, _) in state.Detached)
                    healing.RecalculateChargeEffects(players.GetHandle(target), now);
                state.Detached.Clear();
            }
        }

        Publish(ownerIndex, state);
    }

    // ---- the trace filter, emulated ------------------------------------------------------------

    /// <summary>
    /// Stock's <c>CMedigunFilter</c> (tf_weapon_medigun.cpp:629-648) answers false for
    /// own-team entities — the trace ignores them and walks through — and falls through to
    /// <c>CTraceFilterSimple</c> for everything else, so an enemy player, the world or a
    /// prop stops the beam. The seam traces once, so this re-launches the trace past each
    /// teammate it walks through (docs/divergences.md).
    /// </summary>
    private bool TracePast(EntityHandle owner, EntityHandle target, Vector from, Vector to)
    {
        int ownerTeam = players.GetTeam(owner);
        Vector start = from;
        for (int hop = 0; hop < 4; hop++)
        {
            var hit = world.TraceLine(start, to, TraceMask.Shot, ignore: owner);
            if (!hit.Hit || hit.Entity == target)
                return true;
            if (!entities.IsPlayer(hit.Entity) || players.GetTeam(hit.Entity) != ownerTeam)
                return false;           // an enemy, the world or a prop: the beam is blocked
            start = hit.End;            // a teammate: stock's filter ignores it, so we re-trace past them
        }
        return false;
    }

    /// <summary>The active weapon's item (<see cref="TF2.Shared.ITFWeaponOwners.ActiveItemOf"/>'s read): the
    /// name the engine holds, then the items table — the owners seam's shape without the seam, whose
    /// ctor cycle with <see cref="TFServerWeaponOwners"/>' medigun link it leaves.</summary>
    private IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        string name = players.GetActiveWeapon(players.GetHandle(playerIndex));
        return name.Length > 0 ? items.Of(playerIndex, name) : null;
    }

    private MedicState State(int ownerIndex)
    {
        if (medics.TryGetValue(ownerIndex, out var state))
            return state;
        state = new MedicState();
        // The kept charge spends once, on the first state the new life builds
        // (stock's WeaponReset clamp, then PostSpawnThink's zero at
        // tf_player.cpp:2114-2124).
        // A keep carried into a new round's setup is stock's zero (:296-299).
        if (preserved.Remove(ownerIndex, out float kept) && rounds.State == RoundState.RoundRunning)
            state.Charge = Math.Clamp(kept, 0f, 1f);
        return medics[ownerIndex] = state;
    }

    private void Fire(string name, Action<IntPtr> fill)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        fill(e);
        events.EventFire(e);
    }

    private sealed class MedicState
    {
        public float Charge;
        public float Preserve;               // m_flChargeLevelToPreserve: the spawn keep (the Vita-Saw's organs)
        public bool Releasing;
        public int ChargeType;              // the released medigun_charge_types row (GetChargeType at the pop)
        public bool Holstered;
        public int HealingTarget = -1;
        public bool Healing;
        public float NextTargetCheckTime;
        public float NextThinkTime;
        public float LastHealCall = -1f;
        public bool Attack1Down;
        public int LastHealingTarget = -1;   // stock's m_hLastHealingTarget: the patient the last detach left (:1726)
        public int HealTargetClass;          // stock's m_nHealTargetClass, the class the last think saved (ctor init 0, :313)
        public int ResistIndex;              // m_nChargeResistType: the Vaccinator's cycles (GetChargeType :829)
        public bool ReloadDown;              // m_bReloadDown: the reload press edge (ItemPostFrame :1618-1628)
        public readonly List<(int Target, float Time)> Detached = new();
    }
}
