//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Weapons;

/// <summary>
/// What env_sniperdot tells the client. Stock's <c>DT_SniperDot</c> networks
/// <c>m_flChargeStartTime</c> alone (shared/tf/tf_weapon_sniperrifle.cpp:
/// 1383-1389, a SendPropTime — the draw's ramp clock,
/// <c>DrawModel</c>'s <c>flLifeTime</c>, :1610); <c>m_vecSurfaceNormal</c> and
/// <c>m_hTargetEnt</c> are save-data only and cross no wire. The owner and its
/// team ride beside the clock as plain fields, the way the laser dot's do
/// (<see cref="SourceSharp.Shared.LaserDotNetState"/>): the draw traces from
/// the owner's view (<c>GetRenderingPositions</c>) and takes the sprite from
/// the team, stock's <c>OnDataChanged</c> <c>GetTeamNumber</c> arm
/// (:1705-1712) — <c>TF_TEAM_BLUE</c> the blue sprite, anything else the red.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class SniperDotNetState
{
    /// <summary>The entity's classname, stock's LINK_ENTITY_TO_CLASS (tf_weapon_sniperrifle.cpp:1391).</summary>
    public const string ClassName = "env_sniperdot";

    /// <summary>Entindex of the zoomed player the dot marks; 0 for nobody.</summary>
    public partial int OwnerIndex { get; set; }

    /// <summary>The owner's team, a <see cref="TFTeam"/>: which of the two sprites draws.</summary>
    [NetField(Bits = 2)] public partial byte Team { get; set; }

    /// <summary><c>m_flChargeStartTime</c>: when this charge began, on the server's clock.</summary>
    public partial float ChargeStartTime { get; set; }
}
