//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Ctf;

/// <summary>
/// <c>item_teamflag</c>'s networked state — the fields of stock's
/// <c>DT_CaptureFlag</c> (shared/tf/entity_capture_flag.cpp:119-146) the
/// client mirror reads: the status, the paper effect's name and the trail
/// switch. The rest of the send table has no reader in this build — the HUD's
/// flag row and its compass read the objective resource
/// (<see cref="TF2.Shared.Resources.ObjectiveResourceNetState"/>) and the
/// glow's outline shader is not ported — so it is not carried. The glow's wire halves (the enabled flag and the team key) are: the client mirror feeds them to the glow-object registry.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TFFlagNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "item_teamflag";

    /// <summary><c>m_nFlagStatus</c> (3 bits): home, stolen or dropped.</summary>
    [NetField(Bits = 3)] public partial byte FlagStatus { get; set; }

    /// <summary><c>m_nUseTrailEffect</c> (3 bits): the map's <c>trail_effect</c>, a <see cref="TFFlagEffects"/>.</summary>
    [NetField(Bits = 3)] public partial byte UseTrailEffect { get; set; }

    /// <summary><c>m_szPaperEffect</c>: the particle system a carried flag trails; empty takes stock's <see cref="TFFlagTrail.PaperEffect"/>.</summary>
    [NetString(128)] public partial string PaperEffect { get; set; }

    /// <summary><c>m_bGlowEnabled</c>: the flag outlines in its team colour while on; <c>ForceGlowDisabled</c> takes it off.</summary>
    [NetField(Bits = 1)] public partial byte GlowEnabled { get; set; }

    /// <summary><c>GetTeamNumber()</c>: whose flag this is (TF_TEAM_RED 2, TF_TEAM_BLUE 3); an invade flag takes its carrier's.</summary>
    [NetField(Bits = 3)] public partial byte Team { get; set; }
}
