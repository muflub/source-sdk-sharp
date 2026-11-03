//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The viewmodel bob's convars, stock's client declares beside the helper that
// reads them (tf_weaponbase.cpp:4707): TFViewmodelBob reads them by name as
// CalcViewModelBobHelper does. No help strings stock never wrote.

/// <summary>The bob cycle's period, seconds (tf_weaponbase.cpp:4707).</summary>
[ConVar("cl_bobcycle", "0.8", Flags = ConVarFlags.Cheat)]
public sealed class ClBobCycleConVar : FloatConVar { }

/// <summary>The cycle's peak fraction, where the sine flips (tf_weaponbase.cpp:4708).</summary>
[ConVar("cl_bobup", "0.5", Flags = ConVarFlags.Cheat)]
public sealed class ClBobUpConVar : FloatConVar { }

// The min mode's pair, stock's client declares in the shared viewmodel
// (tf_viewmodel.cpp:125, :127), beside the calc that reads them.

/// <summary>The minimized viewmodels, the item's viewmodel offset held to the eye (tf_viewmodel.cpp:125).</summary>
[ConVar("tf_use_min_viewmodels", "0", Flags = ConVarFlags.Archive, Help = "Use minimized viewmodels.")]
public sealed class TFUseMinViewmodelsConVar : BoolConVar { }

/// <summary>An x y z overriding every viewmodel's offset (tf_viewmodel.cpp:127).</summary>
[ConVar("tf_viewmodels_offset_override", "", Flags = ConVarFlags.Cheat,
        Help = "If set, this will override the position of all viewmodels. Usage 'x y z'")]
public sealed class TFViewmodelsOffsetOverrideConVar : StringConVar { }
