//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the carried weapon networks — and, because [Predicted] rides the same
/// declaration, WHAT THE WEAPON PREDICTS. One declaration is the wire format,
/// the prediction schema and the tolerance table at once (Phase 9c5's rule,
/// inherited by Phase 10).
///
/// <para>
/// THIS IS WHERE STOCK'S THIRTEEN WENT. `basecombatweapon_shared.cpp` declared
/// 13 networked DEFINE_PRED_FIELDs, and the plan expected them here verbatim.
/// Nine survive as state; four dissolved on the way to the hierarchy, each for
/// a reason worth naming rather than a field worth keeping:
/// </para>
///
/// <para>
/// — <c>m_iViewModelIndex</c>, <c>m_iWorldModelIndex</c>: model indexes of the
///   v_ and w_ models, networked because stock's weapon SCRIPT lives server
///   side. The hierarchy weapon's models are a pure function of
///   <see cref="WeaponTypeId"/> through <see cref="WeaponTypes"/>, a table both
///   sides compile in the same assembly — networking them would be sending a
///   value the receiver can already compute.
/// — <c>m_nViewModelIndex</c>: WHICH viewmodel slot, for a design with several
///   (HL2's hands + camera). This mod has one viewmodel per player.
/// — <c>m_nNextThinkTick</c>: the native think clock. A hierarchy weapon has no
///   native thinks; the state machine runs once per player command.
/// — <c>m_iState</c>: holstered/active/dropped. Collapsed into existence — the
///   carried weapon entity IS the active weapon, and a weapon on the ground is
///   a pickup item, not a holstered weapon entity.
/// — <c>m_iPrimaryAmmoType</c>/<c>m_iSecondaryAmmoType</c>: runtime ammo
///   indexes, resolved from the script per instance. Ours come from the same
///   shared table as the models.
/// — <c>m_hOwner</c> survives as <see cref="OwnerIndex"/>, an ENTINDEX rather
///   than a handle: players occupy 1..maxplayers identically on both sides,
///   and an entindex needs no serial-number agreement across the wire.
/// </para>
///
/// <para>
/// And three JOINED that stock kept elsewhere: <see cref="Reserve"/> and
/// <see cref="SecondaryReserve"/> (stock's per-player m_iAmmo array, predicted
/// there) and <see cref="InReload"/> (stock predicts it as a private
/// DEFINE_FIELD). They are state the machine reads every command, so they ride
/// the same blob the ring copies.
/// </para>
///
/// <para>
/// Tolerances are stock's, quoted not invented: the three timers carry
/// predictioncopy.h's TD_MSECTOLERANCE (0.001), everything else compares
/// exactly, which is stock's default (predictioncopy.cpp:736).
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsWeapon)]
public partial class WeaponNetState
{
    /// <summary>Entindex of the player carrying this weapon; 0 for nobody.</summary>
    [Predicted]
    public partial int OwnerIndex { get; set; }

    /// <summary>
    /// Which weapon this is: an index into <see cref="WeaponTypes.All"/>, whose
    /// order is append-only for exactly this reason. -1 is empty-handed.
    /// Changes on a weapon switch, which is what makes one entity per player
    /// enough — the entity persists, the identity moves.
    /// </summary>
    [Predicted]
    public partial int WeaponTypeId { get; set; }

    /// <summary>Rounds loaded. Stock's m_iClip1; -1 for a weapon with no clip.</summary>
    [Predicted]
    public partial int Clip { get; set; }

    /// <summary>
    /// The primary pool behind the clip. A snapshot of the owner's pool for
    /// this weapon's ammo name, kept on the weapon so the client can predict a
    /// reload and the HUD can print it without a second table.
    /// </summary>
    [Predicted]
    public partial int Reserve { get; set; }

    /// <summary>The alt-fire's pool, where the row declares one.</summary>
    [Predicted]
    public partial int SecondaryReserve { get; set; }

    /// <summary>When the trigger next answers. Stock's m_flNextPrimaryAttack.</summary>
    [Predicted(0.001f)]
    public partial float NextPrimaryAttack { get; set; }

    /// <summary>When the other trigger next answers.</summary>
    [Predicted(0.001f)]
    public partial float NextSecondaryAttack { get; set; }

    /// <summary>When the viewmodel may settle back to idle.</summary>
    [Predicted(0.001f)]
    public partial float TimeWeaponIdle { get; set; }

    /// <summary>Mid-reload: the animation is playing and the clip fills when it ends.</summary>
    [Predicted]
    public partial bool InReload { get; set; }

    /// <summary>
    /// The buttons the machine saw last command, for the pressed edges. The
    /// machine's own copy rather than the player's OldButtons, because the
    /// player's is written by the MOVEMENT before phase 1 runs — by the time
    /// the weapon asks, the edge is already gone from it.
    /// </summary>
    [Predicted]
    public partial int LastButtons { get; set; }

    /// <summary>
    /// The earliest a click may refire: stock's CWeaponPistol::m_flSoonestPrimaryAttack
    /// (Hl2mpWeaponBehaviours). Zero for a weapon that does not use it.
    /// </summary>
    [Predicted(0.001f)]
    public partial float SoonestPrimaryAttack { get; set; }

    /// <summary>
    /// Stock's CWeaponPistol::m_flAccuracyPenalty: seconds of inaccuracy owed,
    /// 0.2 a shot, which widen the cone from one degree to six. Zero for a
    /// weapon that does not use it.
    /// </summary>
    [Predicted(0.001f)]
    public partial float AccuracyPenalty { get; set; }

    /// <summary>
    /// When <see cref="AccuracyPenalty"/> was last brought up to date: the
    /// decay since then is what stock's per-frame gpGlobals->frametime took off.
    /// </summary>
    [Predicted(0.001f)]
    public partial float AccuracyPenaltyAt { get; set; }

    /// <summary>
    /// When the primary trigger went down, 0 while it is up: stock's m_fFireDuration
    /// (basecombatweapon_shared.cpp) as a start time, what a machine gun's kick grows on.
    /// </summary>
    [Predicted(0.001f)]
    public partial float FireStartedAt { get; set; }

    /// <summary>
    /// When a charging alt-fire goes off, 0 for none: stock's m_bShotDelayed and
    /// m_flDelayedFire (weapon_ar2.cpp).
    /// </summary>
    [Predicted(0.001f)]
    public partial float ChargeFiresAt { get; set; }

    /// <summary>A shot was fired and the pump is owed: stock's CWeaponShotgun::m_bNeedPump.</summary>
    [Predicted]
    public partial bool NeedPump { get; set; }

    /// <summary>Fire the primary when the reload it interrupted lets it: m_bDelayedFire1.</summary>
    [Predicted]
    public partial bool DelayedFire1 { get; set; }

    /// <summary>Fire both barrels when the reload lets it: m_bDelayedFire2.</summary>
    [Predicted]
    public partial bool DelayedFire2 { get; set; }

    /// <summary>Reload once the owed pump is done: m_bDelayedReload.</summary>
    [Predicted]
    public partial bool DelayedReload { get; set; }

    /// <summary>Which trigger primed the grenade: CWeaponFrag::m_AttackPaused (0 none, 1 primary, 2 secondary).</summary>
    [Predicted]
    public partial int AttackPaused { get; set; }

    /// <summary>The pin is out and the throw waits on the release: m_fDrawbackFinished.</summary>
    [Predicted]
    public partial bool DrawbackFinished { get; set; }

    /// <summary>A grenade has gone and the next is drawn when the throw has played: m_bRedraw.</summary>
    [Predicted]
    public partial bool Redraw { get; set; }

    /// <summary>
    /// The model event the playing sequence fires next (its id; 0 for none),
    /// which stock's viewmodel dispatches to Operator_HandleAnimEvent.
    /// </summary>
    [Predicted]
    public partial int AnimEvent { get; set; }

    /// <summary>When <see cref="AnimEvent"/> fires: the sequence's start plus its cycle times its length.</summary>
    [Predicted(0.001f)]
    public partial float AnimEventAt { get; set; }

    /// <summary>When the last sequence a behaviour sent has played out: IsViewModelSequenceFinished.</summary>
    [Predicted(0.001f)]
    public partial float SequenceEndsAt { get; set; }

    /// A weapon's own mode word, for the one whose state is more than the
    /// fields above: the SLAM's mode, its eight flags and the activity it last
    /// sent (<see cref="SlamBits"/>), which stock networks and predicts as nine
    /// fields of its own (weapon_slam.cpp's DT_Weapon_SLAM). Zero for every
    /// other weapon. It follows the weapon through a switch the way the clip
    /// does, because stock's lives on the weapon entity.
    /// </summary>
    [Predicted]
    public partial int Special { get; set; }

    /// <summary>The mode word's clock: the SLAM's <c>m_flWallSwitchTime</c>.</summary>
    [Predicted(0.001f)]
    public partial float SpecialTime { get; set; }

    //-------------------------------------------------------------------------
    // The physcannon's (PhysCannonBehaviour): CWeaponPhysCannon's members.
    // m_bActive, m_hAttachedObject, m_EffectState and m_bOpen are networked
    // there, the last two predicted; the rest are its plain members, which the
    // replay must start from the server's word as it does every other field.
    // Unused and zero on every other weapon.
    //-------------------------------------------------------------------------

    /// <summary>m_bActive: an object is held.</summary>
    [Predicted]
    public partial bool CannonActive { get; set; }

    /// <summary>m_bOpen: the claws are open.</summary>
    [Predicted]
    public partial bool CannonOpen { get; set; }

    /// <summary>m_EffectState, a <see cref="PhysCannonEffect"/>.</summary>
    [Predicted]
    public partial int CannonEffect { get; set; }

    /// <summary>m_hAttachedObject, as an entindex: 0 for nothing.</summary>
    [Predicted]
    public partial int CannonHeld { get; set; }

    /// <summary>m_nChangeState, a <see cref="PhysCannonElements"/>.</summary>
    [Predicted]
    public partial int CannonChangeState { get; set; }

    /// <summary>m_flElementDebounce.</summary>
    [Predicted(0.001f)]
    public partial float CannonElementDebounce { get; set; }

    /// <summary>m_flCheckSuppressTime.</summary>
    [Predicted(0.001f)]
    public partial float CannonCheckSuppress { get; set; }

    /// <summary>m_nAttack2Debounce: the buttons held when the last pickup took.</summary>
    [Predicted]
    public partial int CannonAttack2Debounce { get; set; }

    /// <summary>m_flLastDenySoundPlayed (a bool in stock, despite the name).</summary>
    [Predicted]
    public partial bool CannonDenyPlayed { get; set; }

    /// <summary>m_flRepuntObjectTime.</summary>
    [Predicted(0.001f)]
    public partial float CannonRepuntTime { get; set; }

    /// <summary>m_hLastPuntedObject, as an entindex.</summary>
    [Predicted]
    public partial int CannonLastPunted { get; set; }

    /// <summary>A bolt has gone and the reload is owed once the shot has played: CWeaponCrossbow::m_bMustReload.</summary>
    [Predicted]
    public partial bool MustReload { get; set; }

    /// <summary>The crossbow's scope is up: m_bInZoom.</summary>
    [Predicted]
    public partial bool InZoom { get; set; }

    /// <summary>
    /// The Huntsman's arrow is lit: stock's <c>CTFCompoundBow::m_bArrowAlight</c>
    /// (<c>tf_weapon_compound_bow.cpp:36</c>). The flame's teammate touch sets it
    /// (<c>tf_weapon_flamethrower.cpp:3142</c>); the shot (<c>:169</c>), the
    /// holster (<c>:380</c>) and the weapon reset (<c>:92</c>, which this port's
    /// <see cref="ResetBehaviourState"/> carries) clear it.
    /// </summary>
    [Predicted]
    public partial bool ArrowAlight { get; set; }

    /// <summary>
    /// The owner's long-term random-crit rate, their weapon's: stock's
    /// <c>CTFWeaponBase::m_flObservedCritChance</c>
    /// (<c>shared/tf/tf_weaponbase.h:761</c>), which the server's
    /// <c>CanFireRandomCriticalShot</c> measures from the owner's round's
    /// ranged and random-crit damage and both sides' crit rolls compare
    /// against (<c>tf_weaponbase.cpp:4630-4664</c>). Stock nets it as a
    /// 16-bit <c>SendPropFloat</c>, NOSCALE, 0 to 100
    /// (<c>tf_weaponbase.cpp:181</c>) and zeroes it at the weapon's Init
    /// (:309), never at a deploy — a fresh state is already 0, so this
    /// field belongs beside the stock members, not the behaviour's four.
    /// </summary>
    [Predicted(0.001f)]
    public partial float ObservedCritChance { get; set; }

    //-------------------------------------------------------------------------
    // A BEHAVIOUR'S OWN STATE. The machine never reads these and writes them
    // only to zero them when a weapon is deployed: they are an
    // IWeaponBehaviour's, for the per-weapon state a mod's weapon keeps between
    // commands and must predict (a spin-up state, a charge meter, a crit
    // window, a reload's next shell). A behaviour object serves every player
    // on its side, so what it remembers about one weapon belongs here, where
    // the client's replay restores it with the rest of the weapon.
    //
    // Their meaning is the behaviour's: a mod documents its own use of them.
    // Seven: the minigun's state, its rapid-fire crit window and check, its
    // wind-up start, the spun drain's clock and accumulator
    // (TFMinigunBehaviour), and the kept clock below.
    //
    // Six of them are the deploy's to clear (see ResetBehaviourState); the
    // seventh, BehaviourKeptTime, a behaviour opts out of that clear for a
    // clock that must survive a deploy the way the stock member it mirrors
    // does.
    //-------------------------------------------------------------------------

    /// <summary>A behaviour's mode word (a state machine's state, flags).</summary>
    [Predicted]
    public partial int BehaviourMode { get; set; }

    /// <summary>A behaviour's first clock, on the machine's clock.</summary>
    [Predicted(0.001f)]
    public partial float BehaviourTimeA { get; set; }

    /// <summary>A behaviour's second clock, on the machine's clock.</summary>
    [Predicted(0.001f)]
    public partial float BehaviourTimeB { get; set; }

    /// <summary>A behaviour's third clock, on the machine's clock.</summary>
    [Predicted(0.001f)]
    public partial float BehaviourTimeC { get; set; }

    /// <summary>A behaviour's meter (a charge, an amount).</summary>
    [Predicted(0.001f)]
    public partial float BehaviourMeter { get; set; }

    /// <summary>A behaviour's second meter (an amount accrued across commands).</summary>
    [Predicted(0.001f)]
    public partial float BehaviourMeterB { get; set; }

    /// <summary>
    /// A behaviour's clock that survives a deploy: zero unless a behaviour
    /// writes it, and left standing by <see cref="ResetBehaviourState"/>, so
    /// it ages from the weapon's birth. The other six belong to the deploy;
    /// this one is for a behaviour whose stock member the constructor alone
    /// zeroes — holsters and redeploy must not erase it — predicted exact
    /// like the stock clock behind it (an unflagged
    /// <c>DEFINE_PRED_FIELD</c>, not a tolerance one). The tf2 assembly's
    /// TFRevolverBehaviour is the first user, riding it as the revolver's
    /// accuracy clock.
    /// </summary>
    [Predicted]
    public partial float BehaviourKeptTime { get; set; }

    /// <summary>
    /// The carried item's charge meter, 0..1 — stock's <c>m_flItemChargeMeter</c>
    /// (tf_player_shared.cpp:7832-7904; the networked array at
    /// tf_player_shared.h:1124, sent and predicted plain, so this field compares
    /// exact like stock's <c>DEFINE_PRED_ARRAY</c>). Stock keeps one meter per
    /// loadout slot, outside the held weapon and surviving holsters; ours rides
    /// the carrier's weapon blob and stands the same way — clear it on every
    /// deploy would refuse the item after a plain weapon switch, where stock's
    /// slot meter has charged on. The one float covers the one weapon the
    /// carrier holds; the behaviour that owns it is the item's lane, and the
    /// refill rule lives with it (the TF2 assembly's TFItemMeter).
    /// </summary>
    [Predicted]
    public partial float ItemMeter { get; set; }

    /// <summary>
    /// The clock the item charge meter ages on, kept beside
    /// <see cref="ItemMeter"/>: every command the machine's tail answers, the
    /// bar fills by the span since this stamp and the stamp moves to the
    /// command's own clock, so the server's run and the client's replay pay
    /// the same dt (the medigun's charge clocks do the same thing with
    /// <see cref="BehaviourTimeA"/>). It stands through deploys as the meter
    /// does — stock's slot meter ages on across a holster — and carries the
    /// clocks' millisecond tolerance like them.
    /// </summary>
    [Predicted(0.001f)]
    public partial float ItemMeterTime { get; set; }

    /// <summary>
    /// The server's deploy stamp: bumped by <c>Arsenal.Deploy</c>, and by the
    /// client's <c>MirrorDeploy</c> when a command's weaponselect answers for
    /// the same deploy, so a switch the client predicted carries the same
    /// stamp on both sides and stands fully judged. An arrival whose stamp
    /// stands away from the acked slot's is a (re)hand-out the client never
    /// predicted — the spawn kit's Deploy and the round-start respawn's run
    /// between two client commands, on no command at all — and the acked slot
    /// then has nothing to judge, exactly as a stock weapon recreated at
    /// respawn has nothing to judge: stock's <c>InitPredictable</c> seeds
    /// every predicted slot from the fresh entity's own arrival
    /// (<c>c_baseentity.cpp:4578-4583</c>, "so we don't get an error saying we
    /// mispredicted any values which are still at their initial values").
    /// Ours, not stock's: the ss pair persists where stock's entities are
    /// recreated each spawn, and the stamp is the event the recreation would
    /// have been. Compared exact, like the parity it stands beside.
    /// </summary>
    [Predicted]
    public partial int DeployEpoch { get; set; }

    /// <summary>
    /// When this weapon last came into the hands: stock's
    /// <c>m_flLastDeployTime</c> (<c>tf_weaponbase.cpp:1319</c>, written by the
    /// deploy tail), the next deploy's measure of how long the outgoing weapon
    /// was held — the <c>mult_switch_from_wep_deploy_time</c> arm compares it
    /// against the switch time (<c>:1262-1266</c>). Written beside the deploy
    /// stamp by <c>Arsenal.Deploy</c> and the client's <c>MirrorDeploy</c>, the
    /// same command clock both sides; tolerated like the timers beside it.
    /// </summary>
    [Predicted(0.001f)]
    public partial float LastDeployTime { get; set; }

    /// <summary>
    /// Zeroes the behaviour's own state, as a deploy does: the weapon now in
    /// the hands starts from nothing, the way stock's weapons reset their own
    /// on holster and deploy. The Huntsman's <see cref="ArrowAlight"/> rides
    /// the clear as stock's <c>WeaponReset</c> clears it
    /// (<c>tf_weapon_compound_bow.cpp:92</c>), and the holster's clear
    /// (:380) arrives through the next deploy's call.
    /// </summary>
    public void ResetBehaviourState()
    {
        BehaviourMode = 0;
        BehaviourTimeA = 0f;
        BehaviourTimeB = 0f;
        BehaviourTimeC = 0f;
        BehaviourMeter = 0f;
        BehaviourMeterB = 0f;
        ArrowAlight = false;
    }
}
