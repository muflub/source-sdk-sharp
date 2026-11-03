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

    private readonly IEnginePlayers players;
    private readonly IEngineEntities entities;
    private readonly IEngineWorldQuery world;
    private readonly ITFPlayerHealing healing;
    private readonly ITFPlayerConditions conditions;
    private readonly ITFPlayerResupply resupply;
    private readonly TFInvulnTimeConVar invulnTime;
    private readonly WeaponMedigunChargeRateConVar medigunChargeRate;
    private readonly ITFPlayerStats? stats;
    private readonly IEngineGameEvents? events;
    private readonly IConnectedClients? clients;
    private readonly ITFPlayerData? data;
    private readonly ITFRespawnRooms rooms;
    private readonly AttributeHooks hooks;
    private readonly ITFWeaponOwners owners;
    private readonly ITFRoundSetup? roundSetup;
    private readonly TFMedigunLagCompConVar? lagGate;
    private readonly ILagCompensation? lagCompensation;

    private readonly Dictionary<int, MedicState> medics = new();

    public TFMedigunWeapon(IEnginePlayers players, IEngineEntities entities, IEngineWorldQuery world,
                           ITFPlayerHealing healing, ITFPlayerConditions conditions, ITFPlayerResupply resupply,
                           TFInvulnTimeConVar invulnTime, WeaponMedigunChargeRateConVar medigunChargeRate,
                           ITFRespawnRooms rooms, AttributeHooks hooks,
                           ITFWeaponOwners owners,
                           ITFPlayerStats? stats = null, IEngineGameEvents? events = null,
                           IConnectedClients? clients = null, ITFPlayerData? data = null,
                           ITFRoundSetup? roundSetup = null, TFMedigunLagCompConVar? lagGate = null,
                           ILagCompensation? lagCompensation = null)
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
        this.owners = owners;
        this.stats = stats;
        this.events = events;
        this.clients = clients;
        this.data = data;
        this.roundSetup = roundSetup;
        this.lagGate = lagGate;
        this.lagCompensation = lagCompensation;
    }

    public int HookOrder => TFMedigunOrders.Weapon;

    public int LifecycleOrder => TFMedigunOrders.Weapon;

    public int ClientLifecycleOrder => TFMedigunOrders.Weapon;

    public void OnLevelShutdown() => medics.Clear();

    public void OnClientDisconnected(int playerIndex) => medics.Remove(playerIndex);

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
        bool autoHeal = clients?.ClientConVar(owner, "tf_medigun_autoheal") == "1";
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
    /// The medigun's state onto its owner's carrier — the release and the beam's
    /// patient (the charge LEVEL rides the weapon's own state, written in
    /// <see cref="Heal"/>: stock networks it on DT_WeaponMedigun). Every entry
    /// point that moves the release or the beam ends here.
    /// </summary>
    private void Publish(int ownerIndex, MedicState state)
    {
        if (data?.Networked(ownerIndex) is not { } carrier)
            return;
        carrier.ChargeReleasing = state.Releasing;
        carrier.HealTargetIndex = state.HealingTarget;
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
        healing.NoteReleasing(ownerIndex, state.Releasing);   // IsReleasingCharge = releasing and not holstered
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
        healing.NoteReleasing(ownerIndex, false);              // IsReleasingCharge includes not-holstered
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
        healing.NoteReleasing(ownerIndex, false);
        healing.RemoveChargeEffect(players.GetHandle(ownerIndex));
        Publish(ownerIndex, state);
        medics.Remove(ownerIndex);
    }

    // ---- CWeaponMedigun::PrimaryAttack / FindAndHealTargets ------------------------------------

    private void PrimaryAttack(int owner, MedicState state, TFWeaponModeData mode, float now)
    {
        // The medigun's session (tf_weapon_medigun.cpp:1774-1775, :1799-1801):
        // while tf_medigun_lagcomp is on, the target search sees the other
        // players where the medic saw them; stock reads the flag at both ends.
        EntityHandle shooter = players.GetHandle(owner);
        if (lagGate?.Value ?? true)
            lagCompensation?.Start(shooter);
        try
        {
            HealPrimary(owner, state, mode, now);
        }
        finally
        {
            if (lagGate?.Value ?? true)
                lagCompensation?.Finish(shooter);
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
            bool buildBonus = roundSetup is not { InSetupWithRoundTimer: true };

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
                amount = hooks.Float(amount, "mult_medigun_overheal_uberchargerate", owners.ManagerOf(owner));
            amount = hooks.Float(amount, "mult_medigun_uberchargerate", owners.ManagerOf(owner));

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
                    amount = hooks.Float(amount, "mult_uberchargerate_for_healer", owners.ManagerOf(heldIndex));
            }

            // The setup round's triple, the last word before the meter takes it
            // (tf_weapon_medigun.cpp:1347-1350). The quick-build x4 beside it is
            // MvM's IsQuickBuildTime, still on the ledger.
            if (roundSetup is { InSetupWithRoundTimer: true })
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
                     ITFPlayerHealing.StandardOverhealBonus, ITFPlayerHealing.StandardOverhealDecayMult);
        healing.RecalculateChargeEffects(patient, now);
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
        => data?.Networked(target.EntryIndex)?.DisguiseTeam ?? (int)TFTeam.Unassigned;

    // ---- CWeaponMedigun::ItemPostFrame's secondary arm: the ÜberCharge release ------------------

    private void SecondaryAttack(int owner, MedicState state, float now)
    {
        if (state.Charge < 1f || state.Releasing)
            return;

        var ownerHandle = players.GetHandle(owner);
        // Stock gates the release on the owner's CanRecieveMedigunChargeEffect only
        // (tf_weapon_medigun.cpp:1863): the patient may be NULL (:1822-1826), and the
        // patient's own refusal lives inside their recalc (tf_player_shared.cpp:8899).
        if (!healing.CanReceiveMedigunChargeEffect(ownerHandle))
            return;

        state.Releasing = true;
        healing.NoteReleasing(owner, true);

        Fire("player_invulnerable", e => events!.EventSetInt(e, "userid", players.GetUserId(ownerHandle)));
        stats?.Increment(ownerHandle, TFStat.Invulns, 1);
        healing.RecalculateChargeEffects(ownerHandle, now);
        if (state.HealingTarget >= 0)
            healing.RecalculateChargeEffects(players.GetHandle(state.HealingTarget), now);
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
                int patientClass = data?.Networked(state.HealingTarget)?.PlayerClass ?? 0;
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
                                                  owners.ManagerOf(ownerIndex));
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
                healing.NoteReleasing(ownerIndex, false);
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

    private MedicState State(int ownerIndex)
        => medics.TryGetValue(ownerIndex, out var state) ? state : medics[ownerIndex] = new MedicState();

    private void Fire(string name, Action<IntPtr> fill)
    {
        if (events is null)
            return;
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        fill(e);
        events.EventFire(e);
    }

    private sealed class MedicState
    {
        public float Charge;
        public bool Releasing;
        public bool Holstered;
        public int HealingTarget = -1;
        public bool Healing;
        public float NextTargetCheckTime;
        public float NextThinkTime;
        public float LastHealCall = -1f;
        public bool Attack1Down;
        public int LastHealingTarget = -1;   // stock's m_hLastHealingTarget: the patient the last detach left (:1726)
        public int HealTargetClass;          // stock's m_nHealTargetClass, the class the last think saved (ctor init 0, :313)
        public readonly List<(int Target, float Time)> Detached = new();
    }
}
