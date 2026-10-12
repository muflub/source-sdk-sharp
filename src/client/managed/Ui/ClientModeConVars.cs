//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Stock's cl_drawhud (clientmode_shared.cpp): off, the viewport and everything
/// in it was hidden. The UI paints neither layer while it is off (UiLoader.Paint).
/// </summary>
[ConVar("cl_drawhud", "1", Flags = ConVarFlags.Cheat, Help = "Enable the rendering of the hud")]
public sealed class ClDrawHudConVar : BoolConVar
{
}

/// <summary>Stock's hud_takesshots (clientmode_shared.cpp), read by name where the scoreboard's VGUIMenu arrives (UserMessages).</summary>
[ConVar("hud_takesshots", "0", Flags = ConVarFlags.Archive, Help = "Auto-save a scoreboard screenshot at the end of a map.")]
public sealed class HudTakesShotsConVar : BoolConVar
{
}

/// <summary>Stock's hud_freezecamhide (clientmode_shared.cpp), kept for the configs that set it: HL2MP has no freeze cam.</summary>
[ConVar("hud_freezecamhide", "0", Flags = ConVarFlags.Archive, Help = "Hide the HUD during freeze-cam")]
public sealed class HudFreezeCamHideConVar : BoolConVar
{
}
