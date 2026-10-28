//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Which of its three modes a SLAM is in: stock's <c>SlamState_t</c>.</summary>
public enum SlamMode
{
    /// <summary><c>SLAM_TRIPMINE_READY</c>: facing a wall, the primary plants a tripmine.</summary>
    TripmineReady = 0,

    /// <summary><c>SLAM_SATCHEL_THROW</c>: the primary throws a satchel.</summary>
    SatchelThrow = 1,

    /// <summary>
    /// <c>SLAM_SATCHEL_ATTACH</c>: the primary sticks a satchel to the wall.
    /// Nothing in stock ever enters it (<c>SetSlamState</c> is only ever asked
    /// for the other two), and its arms are ported all the same.
    /// </summary>
    SatchelAttach = 2,
}

/// <summary>
/// The SLAM's state, packed into <see cref="WeaponNetState.Special"/>: the
/// nine networked and predicted fields of stock's <c>DT_Weapon_SLAM</c>, and
/// the activity the weapon last sent, which <c>StartSatchelDetonate</c> reads
/// back through <c>GetActivity()</c>. The wall-switch clock is
/// <see cref="WeaponNetState.SpecialTime"/>.
/// </summary>
public static class SlamBits
{
    private const int ModeMask = 0x3;

    public const int DetonatorArmed = 1 << 2;
    public const int NeedDetonatorDraw = 1 << 3;
    public const int NeedDetonatorHolster = 1 << 4;
    public const int NeedReload = 1 << 5;
    public const int ClearReload = 1 << 6;
    public const int ThrowSatchel = 1 << 7;
    public const int AttachSatchel = 1 << 8;
    public const int AttachTripmine = 1 << 9;

    /// <summary>
    /// Asked to go: out of charges with none left to detonate, where stock
    /// calls <c>Weapon_Drop( this )</c> and <c>UTIL_Remove( this )</c>. The
    /// owner of the inventory acts on it; the machine only says so.
    /// </summary>
    public const int Spent = 1 << 10;

    private const int ActivityShift = 16;
    private const int ActivityMask = 0xFF << ActivityShift;

    /// <summary>
    /// A new SLAM's word: <c>CWeapon_SLAM</c>'s constructor and <c>Spawn</c> —
    /// throw mode, and a reload owed, which the first <c>Deploy</c> clears.
    /// </summary>
    public const int Initial = (int)SlamMode.SatchelThrow | NeedReload;

    public static SlamMode Mode(int bits) => (SlamMode)(bits & ModeMask);

    public static int WithMode(int bits, SlamMode mode) => (bits & ~ModeMask) | ((int)mode & ModeMask);

    public static bool Has(int bits, int flag) => (bits & flag) != 0;

    public static int With(int bits, int flag, bool on) => on ? bits | flag : bits & ~flag;

    /// <summary>The activity last sent, or empty for none.</summary>
    public static string Activity(int bits)
    {
        int at = ((bits & ActivityMask) >> ActivityShift) - 1;
        return at >= 0 && at < SlamActivities.All.Length ? SlamActivities.All[at] : string.Empty;
    }

    public static int WithActivity(int bits, string activity)
    {
        int at = SlamActivities.All.IndexOf(activity) + 1;
        return (bits & ~ActivityMask) | ((at << ActivityShift) & ActivityMask);
    }
}

/// <summary>
/// The activities the SLAM sends, by stock's names. Not every one is on
/// v_slam.mdl — it carries twenty-eight sequences and stock asks for
/// thirty-six activities — and stock's <c>SetIdealActivity</c> answers a
/// missing one by changing nothing, which <see cref="SlamMachine"/> keeps.
/// </summary>
public static class SlamActivities
{
    public const string ThrowIdle = "ACT_SLAM_THROW_IDLE";
    public const string ThrowNdIdle = "ACT_SLAM_THROW_ND_IDLE";
    public const string ThrowThrow = "ACT_SLAM_THROW_THROW";
    public const string ThrowThrow2 = "ACT_SLAM_THROW_THROW2";
    public const string ThrowThrowNd = "ACT_SLAM_THROW_THROW_ND";
    public const string ThrowThrowNd2 = "ACT_SLAM_THROW_THROW_ND2";
    public const string ThrowDraw = "ACT_SLAM_THROW_DRAW";
    public const string ThrowNdDraw = "ACT_SLAM_THROW_ND_DRAW";
    public const string ThrowToTripmineNd = "ACT_SLAM_THROW_TO_TRIPMINE_ND";
    public const string ThrowToStickwall = "ACT_SLAM_THROW_TO_STICKWALL";
    public const string ThrowDetonate = "ACT_SLAM_THROW_DETONATE";
    public const string ThrowDetonatorHolster = "ACT_SLAM_THROW_DETONATOR_HOLSTER";
    public const string DetonatorDraw = "ACT_SLAM_DETONATOR_DRAW";
    public const string DetonatorIdle = "ACT_SLAM_DETONATOR_IDLE";
    public const string DetonatorDetonate = "ACT_SLAM_DETONATOR_DETONATE";
    public const string DetonatorThrowDraw = "ACT_SLAM_DETONATOR_THROW_DRAW";
    public const string DetonatorStickwallDraw = "ACT_SLAM_DETONATOR_STICKWALL_DRAW";
    public const string TripmineIdle = "ACT_SLAM_TRIPMINE_IDLE";
    public const string TripmineDraw = "ACT_SLAM_TRIPMINE_DRAW";
    public const string TripmineToThrowNd = "ACT_SLAM_TRIPMINE_TO_THROW_ND";
    public const string TripmineAttach = "ACT_SLAM_TRIPMINE_ATTACH";
    public const string TripmineAttach2 = "ACT_SLAM_TRIPMINE_ATTACH2";
    public const string StickwallIdle = "ACT_SLAM_STICKWALL_IDLE";
    public const string StickwallAttach = "ACT_SLAM_STICKWALL_ATTACH";
    public const string StickwallAttach2 = "ACT_SLAM_STICKWALL_ATTACH2";
    public const string StickwallNdAttach = "ACT_SLAM_STICKWALL_ND_ATTACH";
    public const string StickwallNdAttach2 = "ACT_SLAM_STICKWALL_ND_ATTACH2";
    public const string StickwallDraw = "ACT_SLAM_STICKWALL_DRAW";
    public const string StickwallNdDraw = "ACT_SLAM_STICKWALL_ND_DRAW";
    public const string StickwallDetonate = "ACT_SLAM_STICKWALL_DETONATE";
    public const string StickwallDetonatorHolster = "ACT_SLAM_STICKWALL_DETONATOR_HOLSTER";
    public const string StickwallToThrow = "ACT_SLAM_STICKWALL_TO_THROW";

    /// <summary>Every activity above, in a fixed order: an index into it is what the state word stores.</summary>
    public static readonly ImmutableArray<string> All =
    [
        ThrowIdle, ThrowNdIdle, ThrowThrow, ThrowThrow2, ThrowThrowNd, ThrowThrowNd2, ThrowDraw, ThrowNdDraw,
        ThrowToTripmineNd, ThrowToStickwall, ThrowDetonate, ThrowDetonatorHolster,
        DetonatorDraw, DetonatorIdle, DetonatorDetonate, DetonatorThrowDraw, DetonatorStickwallDraw,
        TripmineIdle, TripmineDraw, TripmineToThrowNd, TripmineAttach, TripmineAttach2,
        StickwallIdle, StickwallAttach, StickwallAttach2, StickwallNdAttach, StickwallNdAttach2,
        StickwallDraw, StickwallNdDraw, StickwallDetonate, StickwallDetonatorHolster, StickwallToThrow,
    ];
}

/// <summary>
/// <c>CWeapon_SLAM</c>'s command (weapon_slam.cpp): the satchel thrown or
/// stuck, the tripmine planted, the detonator, and the idle that carries the
/// throw and the plant out once their first animation has run.
///
/// <para>
/// Run by <see cref="WeaponLogic"/> for a <see cref="WeaponKind.Slam"/> row,
/// on the server and on the client's replay, over the weapon's declared state
/// only — the state word in <see cref="WeaponNetState.Special"/>, the clocks,
/// and the "slam" pool in <see cref="WeaponNetState.SecondaryReserve"/>. What
/// makes or sets off an entity goes through the arms while effects are live,
/// which on the client's replay is never.
/// </para>
/// </summary>
public sealed class SlamMachine
{
    private readonly IWeaponArms _arms;

    public SlamMachine(IWeaponArms arms) => _arms = arms;

    /// <summary><c>CanAttachSLAM</c>'s reach.</summary>
    public const float AttachReach = 42f;

    /// <summary>The reach of the attaches themselves.</summary>
    public const float PlaceReach = 128f;

    public const string ThrowSound = "Weapon_SLAM.SatchelThrow";
    public const string DetonateSound = "Weapon_SLAM.SatchelDetonate";

    /// <summary>
    /// Played by v_slam.mdl itself, an AE_CL_PLAYSOUND on its
    /// throw_to_tripmine_ND sequence; precached with the other two as stock's
    /// <c>Precache</c> does.
    /// </summary>
    public const string TripMineModeSound = "Weapon_SLAM.TripMineMode";

    /// <summary><c>CWeapon_SLAM::ItemPostFrame</c>, once per command.</summary>
    public void RunCommand(WeaponNetState w, ViewModelNetState vm, int buttons, float now, bool effectsLive,
                           in WeaponType type)
    {
        w.LastButtons = buttons;
        var held = (InputButtons)buttons;

        Think(w, vm, now);

        if (held.HasFlag(InputButtons.Attack2) && w.NextSecondaryAttack <= now)
        {
            if (Has(w, SlamBits.DetonatorArmed))
                StartSatchelDetonate(w, vm, now, effectsLive);
        }
        else if (!Has(w, SlamBits.NeedReload) && held.HasFlag(InputButtons.Attack) && w.NextPrimaryAttack <= now)
        {
            PrimaryAttack(w, vm, now);
        }
        else
        {
            WeaponIdle(w, vm, now, effectsLive, in type);
        }
    }

    /// <summary><c>CWeapon_SLAM::Deploy</c>: arm from what is still out, then pick the draw.</summary>
    public void Deploy(WeaponNetState w, ViewModelNetState vm, float now, in WeaponType type)
    {
        Set(w, SlamBits.Spent, false);
        Set(w, SlamBits.DetonatorArmed, _arms.ChargesOut());
        SetMode(w, SlamMode.SatchelThrow);
        Set(w, SlamBits.NeedReload, false);

        string activity;

        if (Has(w, SlamBits.DetonatorArmed))
        {
            if (w.SecondaryReserve <= 0)
            {
                activity = SlamActivities.DetonatorDraw;
                Set(w, SlamBits.NeedReload, true);
            }
            else if (CanAttach())
            {
                activity = SlamActivities.DetonatorStickwallDraw;
                SetSlamState(w, SlamMode.TripmineReady, now);
            }
            else
            {
                activity = SlamActivities.DetonatorThrowDraw;
                SetSlamState(w, SlamMode.SatchelThrow, now);
            }
        }
        else if (CanAttach())
        {
            activity = SlamActivities.TripmineDraw;
            SetSlamState(w, SlamMode.TripmineReady, now);
        }
        else
        {
            activity = SlamActivities.ThrowNdDraw;
            SetSlamState(w, SlamMode.SatchelThrow, now);
        }

        // DefaultDeploy.
        SendWeaponAnim(w, vm, activity, now);
        w.NextPrimaryAttack = now + CurrentDuration(vm);
        w.NextSecondaryAttack = now + CurrentDuration(vm);
    }

    //-------------------------------------------------------------------------
    // The triggers.
    //-------------------------------------------------------------------------

    private void PrimaryAttack(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (w.SecondaryReserve <= 0)
            return;

        switch (Mode(w))
        {
            case SlamMode.TripmineReady:
                if (CanAttach())
                    StartTripmineAttach(w, vm, now);
                break;
            case SlamMode.SatchelThrow:
                StartSatchelThrow(w, vm, now);
                break;
            case SlamMode.SatchelAttach:
                StartSatchelAttach(w, vm, now);
                break;
        }
    }

    private void StartSatchelDetonate(WeaponNetState w, ViewModelNetState vm, float now, bool effectsLive)
    {
        string current = SlamBits.Activity(w.Special);

        if (current != SlamActivities.DetonatorIdle && current != SlamActivities.ThrowIdle)
            return;

        if (Has(w, SlamBits.NeedReload))
            SendWeaponAnim(w, vm, SlamActivities.DetonatorDetonate, now);
        else if (Mode(w) == SlamMode.SatchelAttach)
            SendWeaponAnim(w, vm, SlamActivities.StickwallDetonate, now);
        else if (Mode(w) == SlamMode.SatchelThrow)
            SendWeaponAnim(w, vm, SlamActivities.ThrowDetonate, now);
        else
            return;

        // SatchelDetonate.
        if (effectsLive)
        {
            _arms.DetonateCharges();
            _arms.Sound(DetonateSound);
        }

        Set(w, SlamBits.DetonatorArmed, false);

        w.NextPrimaryAttack = now + CurrentDuration(vm);
        w.NextSecondaryAttack = now + CurrentDuration(vm);
    }

    private void StartTripmineAttach(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (_arms.ProbeWall(PlaceReach) != WallProbe.Nothing)
        {
            bool armed = Has(w, SlamBits.DetonatorArmed);

            SendWeaponAnim(w, vm, armed ? SlamActivities.StickwallAttach : SlamActivities.TripmineAttach, now);

            Set(w, SlamBits.NeedReload, true);
            Set(w, SlamBits.AttachTripmine, true);
            Set(w, SlamBits.NeedDetonatorDraw, armed);
        }

        w.NextPrimaryAttack = now + CurrentDuration(vm);
        w.NextSecondaryAttack = now + CurrentDuration(vm);
    }

    private void StartSatchelThrow(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (Has(w, SlamBits.DetonatorArmed))
        {
            SendWeaponAnim(w, vm, SlamActivities.ThrowThrow, now);
        }
        else
        {
            SendWeaponAnim(w, vm, SlamActivities.ThrowThrowNd, now);
            Set(w, SlamBits.DetonatorArmed, true);
            Set(w, SlamBits.NeedDetonatorDraw, true);
        }

        Set(w, SlamBits.NeedReload, true);
        Set(w, SlamBits.ThrowSatchel, true);

        w.NextPrimaryAttack = now + CurrentDuration(vm);
        w.NextSecondaryAttack = now + CurrentDuration(vm);
    }

    /// <summary>
    /// Stock's is all inside <c>#ifndef CLIENT_DLL</c>, so its client never
    /// predicted it; this one does, from the same probe. Stock traces along
    /// <c>BodyDirection2D</c> here rather than the aim, and the probe is the
    /// aim's: the mode it serves is never entered.
    /// </summary>
    private void StartSatchelAttach(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (_arms.ProbeWall(PlaceReach) == WallProbe.Nothing)
            return;

        if (Has(w, SlamBits.DetonatorArmed))
        {
            SendWeaponAnim(w, vm, SlamActivities.StickwallAttach, now);
        }
        else
        {
            SendWeaponAnim(w, vm, SlamActivities.StickwallNdAttach, now);
            Set(w, SlamBits.DetonatorArmed, true);
            Set(w, SlamBits.NeedDetonatorDraw, true);
        }

        Set(w, SlamBits.NeedReload, true);
        Set(w, SlamBits.AttachSatchel, true);

        w.NextPrimaryAttack = now + CurrentDuration(vm);
    }

    //-------------------------------------------------------------------------
    // What the idle carries out.
    //-------------------------------------------------------------------------

    private void SatchelThrow(WeaponNetState w, bool effectsLive, in WeaponType type)
    {
        Set(w, SlamBits.ThrowSatchel, false);

        if (effectsLive)
            _arms.ThrowCharge(in type);

        w.SecondaryReserve -= 1;

        if (effectsLive)
            _arms.Sound(ThrowSound);
    }

    private void SatchelAttach(WeaponNetState w, bool effectsLive, in WeaponType type)
    {
        Set(w, SlamBits.AttachSatchel, false);

        if (_arms.ProbeWall(PlaceReach) == WallProbe.Nothing)
            return;

        if (effectsLive)
            _arms.AttachCharge(in type);

        w.SecondaryReserve -= 1;
    }

    private void TripmineAttach(WeaponNetState w, bool effectsLive, in WeaponType type)
    {
        Set(w, SlamBits.AttachTripmine, false);

        if (_arms.ProbeWall(PlaceReach) == WallProbe.Nothing)
            return;

        if (effectsLive)
            _arms.AttachTripmine(in type);

        w.SecondaryReserve -= 1;
    }

    /// <summary><c>CWeapon_SLAM::SLAMThink</c>: swap modes as a wall comes into reach or leaves it.</summary>
    private void Think(WeaponNetState w, ViewModelNetState vm, float now)
    {
        if (w.SpecialTime > now)
            return;

        if (w.SecondaryReserve <= 0)
            return;

        bool armed = Has(w, SlamBits.DetonatorArmed);

        if (CanAttach())
        {
            if (Mode(w) == SlamMode.SatchelThrow)
            {
                SetSlamState(w, SlamMode.TripmineReady, now);
                SendWeaponAnim(w, vm, armed ? SlamActivities.ThrowToStickwall : SlamActivities.ThrowToTripmineNd, now);
                w.SpecialTime = now + CurrentDuration(vm);
                Set(w, SlamBits.NeedReload, false);
            }
        }
        else if (Mode(w) == SlamMode.TripmineReady)
        {
            SetSlamState(w, SlamMode.SatchelThrow, now);
            SendWeaponAnim(w, vm, armed ? SlamActivities.StickwallToThrow : SlamActivities.TripmineToThrowNd, now);
            w.SpecialTime = now + CurrentDuration(vm);
            Set(w, SlamBits.NeedReload, false);
        }
    }

    /// <summary><c>CWeapon_SLAM::WeaponIdle</c>.</summary>
    private void WeaponIdle(WeaponNetState w, ViewModelNetState vm, float now, bool effectsLive, in WeaponType type)
    {
        // HasWeaponIdleTimeElapsed: strictly after.
        if (!(now > w.TimeWeaponIdle))
            return;

        if (Has(w, SlamBits.ClearReload))
        {
            Set(w, SlamBits.NeedReload, false);
            Set(w, SlamBits.ClearReload, false);
        }

        string activity = string.Empty;
        bool armed() => Has(w, SlamBits.DetonatorArmed);
        bool needDraw() => Has(w, SlamBits.NeedDetonatorDraw);

        if (Has(w, SlamBits.ThrowSatchel))
        {
            SatchelThrow(w, effectsLive, in type);
            activity = armed() && !needDraw() ? SlamActivities.ThrowThrow2 : SlamActivities.ThrowThrowNd2;
        }
        else if (Has(w, SlamBits.AttachSatchel))
        {
            SatchelAttach(w, effectsLive, in type);
            activity = armed() && !needDraw() ? SlamActivities.StickwallAttach2 : SlamActivities.StickwallNdAttach2;
        }
        else if (Has(w, SlamBits.AttachTripmine))
        {
            TripmineAttach(w, effectsLive, in type);
            activity = needDraw() ? SlamActivities.StickwallAttach2 : SlamActivities.TripmineAttach2;
        }
        else if (Has(w, SlamBits.NeedReload))
        {
            if (w.SecondaryReserve > 0)
            {
                switch (Mode(w))
                {
                    case SlamMode.TripmineReady:
                        activity = needDraw() ? SlamActivities.StickwallDraw : SlamActivities.TripmineDraw;
                        break;

                    case SlamMode.SatchelAttach:
                        if (Has(w, SlamBits.NeedDetonatorHolster))
                        {
                            activity = SlamActivities.StickwallDetonatorHolster;
                            Set(w, SlamBits.NeedDetonatorHolster, false);
                        }
                        else if (armed())
                        {
                            activity = needDraw() ? SlamActivities.DetonatorStickwallDraw : SlamActivities.StickwallDraw;
                            Set(w, SlamBits.NeedDetonatorDraw, false);
                        }
                        else
                        {
                            activity = SlamActivities.StickwallNdDraw;
                        }
                        break;

                    case SlamMode.SatchelThrow:
                        if (Has(w, SlamBits.NeedDetonatorHolster))
                        {
                            activity = SlamActivities.ThrowDetonatorHolster;
                            Set(w, SlamBits.NeedDetonatorHolster, false);
                        }
                        else if (armed())
                        {
                            activity = needDraw() ? SlamActivities.DetonatorThrowDraw : SlamActivities.ThrowDraw;
                            Set(w, SlamBits.NeedDetonatorDraw, false);
                        }
                        else
                        {
                            activity = SlamActivities.ThrowNdDraw;
                        }
                        break;
                }

                Set(w, SlamBits.ClearReload, true);
            }
            else if (armed())
            {
                // No charges left and one still out: idle on the detonator alone.
                activity = needDraw() ? SlamActivities.DetonatorDraw : SlamActivities.DetonatorIdle;
                Set(w, SlamBits.NeedDetonatorDraw, false);
            }
            else
            {
                Set(w, SlamBits.Spent, true);
            }
        }
        else if (w.SecondaryReserve <= 0)
        {
            Set(w, SlamBits.Spent, true);
        }
        else
        {
            switch (Mode(w))
            {
                case SlamMode.TripmineReady:
                    activity = armed() ? SlamActivities.StickwallIdle : SlamActivities.TripmineIdle;
                    w.SpecialTime = 0;
                    break;

                case SlamMode.SatchelThrow:
                    if (Has(w, SlamBits.NeedDetonatorHolster))
                    {
                        activity = SlamActivities.ThrowDetonatorHolster;
                        Set(w, SlamBits.NeedDetonatorHolster, false);
                    }
                    else
                    {
                        activity = armed() ? SlamActivities.ThrowIdle : SlamActivities.ThrowNdIdle;
                        w.SpecialTime = 0;
                    }
                    break;

                case SlamMode.SatchelAttach:
                    if (Has(w, SlamBits.NeedDetonatorHolster))
                    {
                        activity = SlamActivities.StickwallDetonatorHolster;
                        Set(w, SlamBits.NeedDetonatorHolster, false);
                    }
                    else
                    {
                        activity = armed() ? SlamActivities.StickwallIdle : SlamActivities.TripmineIdle;
                        w.SpecialTime = 0;
                    }
                    break;
            }
        }

        // SendWeaponAnim( 0 ) for the arms that chose nothing, which stock's
        // SetIdealActivity answers by changing nothing.
        if (activity.Length > 0)
            SendWeaponAnim(w, vm, activity, now);
    }

    //-------------------------------------------------------------------------
    // The pieces.
    //-------------------------------------------------------------------------

    private bool CanAttach() => _arms.ProbeWall(AttachReach) == WallProbe.Surface;

    /// <summary>
    /// <c>SendWeaponAnim</c> through <c>SetIdealActivity</c>: an activity the
    /// model does not carry changes nothing; one it does becomes the weapon's
    /// activity and the viewmodel's sequence, and the idle is due when that
    /// sequence has run.
    /// </summary>
    private bool SendWeaponAnim(WeaponNetState w, ViewModelNetState vm, string activity, float now)
    {
        int sequence = _arms.LookupActivity(activity);

        if (sequence < 0)
            return false;

        w.Special = SlamBits.WithActivity(w.Special, activity);

        vm.Sequence = sequence;
        vm.AnimationParity = AnimationParity.Next(vm.AnimationParity);
        vm.PlaybackRate = 1f;

        w.TimeWeaponIdle = now + _arms.SequenceDuration(sequence);
        return true;
    }

    /// <summary><c>SequenceDuration()</c>: the sequence the weapon is playing now.</summary>
    private float CurrentDuration(ViewModelNetState vm) => _arms.SequenceDuration(vm.Sequence);

    /// <summary><c>SetSlamState</c>: the new mode, and the idle due at once so its animation follows.</summary>
    private static void SetSlamState(WeaponNetState w, SlamMode mode, float now)
    {
        SetMode(w, mode);
        w.TimeWeaponIdle = now;
    }

    private static SlamMode Mode(WeaponNetState w) => SlamBits.Mode(w.Special);

    private static void SetMode(WeaponNetState w, SlamMode mode) => w.Special = SlamBits.WithMode(w.Special, mode);

    private static bool Has(WeaponNetState w, int flag) => SlamBits.Has(w.Special, flag);

    private static void Set(WeaponNetState w, int flag, bool on) => w.Special = SlamBits.With(w.Special, flag, on);
}
