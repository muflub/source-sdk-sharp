//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The crosshair family stock's TF client declares (tf_hud_crosshair.cpp:23-29),
// declared by the TF2 client library since the bloom row: names, defaults and
// flags as stock wrote them (TFConVarsTests holds the rows). The file's is the
// custom-crosshair switch — ShouldDrawCrosshair's "return true" arm
// (tf_weaponbase.cpp:3640-3647) and CHudTFCrosshair::Paint's material path
// (tf_hud_crosshair.cpp:150-178); the colours and the scale ride both arms of
// the paint (tf_hud_crosshair.cpp:199-204, hud_crosshair.cpp:262-267).

/// <summary>Stock's crosshair red (tf_hud_crosshair.cpp:23): the red of the
/// crosshair's colour, both arms of the paint modulate by it.</summary>
[ConVar("cl_crosshair_red", "200", Flags = ConVarFlags.Archive)]
public sealed class ClCrosshairRedConVar : IntConVar { }

/// <summary>Stock's crosshair green (tf_hud_crosshair.cpp:24).</summary>
[ConVar("cl_crosshair_green", "200", Flags = ConVarFlags.Archive)]
public sealed class ClCrosshairGreenConVar : IntConVar { }

/// <summary>Stock's crosshair blue (tf_hud_crosshair.cpp:25).</summary>
[ConVar("cl_crosshair_blue", "200", Flags = ConVarFlags.Archive)]
public sealed class ClCrosshairBlueConVar : IntConVar { }

/// <summary>Stock's crosshair file (tf_hud_crosshair.cpp:27): the material
/// under <c>vgui/crosshairs/</c> the crosshair draws; empty is the default
/// crosshair — the script's <c>DrawCrosshair</c> word then rules
/// (<c>ShouldDrawCrosshair</c>, tf_weaponbase.cpp:3638-3648).</summary>
[ConVar("cl_crosshair_file", "", Flags = ConVarFlags.Archive)]
public sealed class ClCrosshairFileConVar : StringConVar { }

/// <summary>Stock's crosshair scale (tf_hud_crosshair.cpp:29): the player's
/// size of the 32-pixel crosshair, the paint's
/// <c>flPlayerScale = cl_crosshair_scale / 32</c>
/// (tf_hud_crosshair.cpp:201, hud_crosshair.cpp:264).</summary>
[ConVar("cl_crosshair_scale", "32.0", Flags = ConVarFlags.Archive)]
public sealed class ClCrosshairScaleConVar : FloatConVar { }
