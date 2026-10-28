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
}
