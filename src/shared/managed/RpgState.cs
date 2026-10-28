//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// The activity an RPG last sent (stock's GetActivity(), which is its ideal
/// activity too: every send goes through SetIdealActivity).
/// </summary>
public enum RpgActivity
{
    Other = 0,
    Draw,
    Idle,
    IdleLowered,
    Primary,
    Reload,
}

/// <summary>
/// The RPG's networked and predicted state, as bits of
/// <see cref="WeaponNetState.Special"/>: stock's DT_WeaponRPG (m_bGuiding,
/// m_bHideGuiding, m_bInitialStateUpdate, m_hMissile), the server's laser dot
/// (m_hLaserDot and its m_bIsOn), CBaseCombatWeapon's m_bFireOnEmpty, and the
/// activity. Kept on the weapon through a switch, as stock's are.
/// </summary>
public static class RpgState
{
    /// <summary>m_bGuiding.</summary>
    public const int Guiding = 1 << 0;

    /// <summary>m_bHideGuiding: suppressed while the weapon is lowered.</summary>
    public const int HideGuiding = 1 << 1;

    /// <summary>m_bInitialStateUpdate: set by Deploy, cleared once the draw is over.</summary>
    public const int InitialStateUpdate = 1 << 2;

    /// <summary>m_hMissile != NULL: a missile it fired is still flying.</summary>
    public const int MissileOut = 1 << 3;

    /// <summary>m_hLaserDot != NULL: the dot exists.</summary>
    public const int DotOut = 1 << 4;

    /// <summary>The dot's m_bIsOn: a missile steers for it.</summary>
    public const int DotOn = 1 << 5;

    /// <summary>CBaseCombatWeapon::m_bFireOnEmpty.</summary>
    public const int FireOnEmpty = 1 << 6;

    private const int ActivityShift = 8;
    private const int ActivityMask = 0xF << ActivityShift;

    public static bool Has(int bits, int flag) => (bits & flag) != 0;

    public static int Set(int bits, int flag, bool on) => on ? bits | flag : bits & ~flag;

    public static RpgActivity Activity(int bits) => (RpgActivity)((bits & ActivityMask) >> ActivityShift);

    public static int WithActivity(int bits, RpgActivity activity)
        => (bits & ~ActivityMask) | (((int)activity << ActivityShift) & ActivityMask);

    /// <summary>The ACT_VM_ name a kind is sent as.</summary>
    public static string ActivityName(RpgActivity activity) => activity switch
    {
        RpgActivity.Draw => "ACT_VM_DRAW",
        RpgActivity.Idle => "ACT_VM_IDLE",
        RpgActivity.IdleLowered => "ACT_VM_IDLE_LOWERED",
        RpgActivity.Primary => "ACT_VM_PRIMARYATTACK",
        RpgActivity.Reload => "ACT_VM_RELOAD",
        _ => "",
    };
}
