//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the AR2's ball tells its client half: stock's DT_PropCombineBall, for a ball
/// a weapon threw (never held, never launched by a spawner).
/// </summary>
[StandaloneNetState(EntityNames.PropCombineBall)]
public partial class CombineBallNetState
{
    /// <summary>m_bEmit: drawn until its end. After that it stays only to be heard.</summary>
    public partial bool Emit { get; set; }

    /// <summary>m_flRadius, in units.</summary>
    public partial float Radius { get; set; }
}
