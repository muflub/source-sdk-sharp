//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// Pyro vision's convars (viewpostprocess.cpp's pyro_* family), declared by the
// TF2 client library since that TU left client.so: names, defaults, flags and
// help as stock declared them (PyroVisionTests holds them). PyroVision reads
// them by name.

[ConVar("pyro_vignette", "2", Flags = ConVarFlags.Archive)]
public sealed class PyroVignetteConVar : FloatConVar { }

[ConVar("pyro_vignette_distortion", "1", Flags = ConVarFlags.Archive)]
public sealed class PyroVignetteDistortionConVar : FloatConVar { }

[ConVar("pyro_min_intensity", "0.1", Flags = ConVarFlags.Archive)]
public sealed class PyroMinIntensityConVar : FloatConVar { }

[ConVar("pyro_max_intensity", "0.35", Flags = ConVarFlags.Archive)]
public sealed class PyroMaxIntensityConVar : FloatConVar { }

[ConVar("pyro_min_rate", "0.05", Flags = ConVarFlags.Archive)]
public sealed class PyroMinRateConVar : FloatConVar { }

[ConVar("pyro_max_rate", "0.2", Flags = ConVarFlags.Archive)]
public sealed class PyroMaxRateConVar : FloatConVar { }

[ConVar("pyro_min_side_length", "0.3", Flags = ConVarFlags.Archive)]
public sealed class PyroMinSideLengthConVar : FloatConVar { }

[ConVar("pyro_max_side_length", "0.55", Flags = ConVarFlags.Archive)]
public sealed class PyroMaxSideLengthConVar : FloatConVar { }

[ConVar("pyro_min_side_width", "0.65", Flags = ConVarFlags.Archive)]
public sealed class PyroMinSideWidthConVar : FloatConVar { }

[ConVar("pyro_max_side_width", "0.95", Flags = ConVarFlags.Archive)]
public sealed class PyroMaxSideWidthConVar : FloatConVar { }

// viewrender.cpp's pyro_dof (the pyro vision depth of field switch), declared
// here since viewrender.cpp left client.so (C7 step 3).
[ConVar("pyro_dof", "1", Flags = ConVarFlags.Archive)]
public sealed class PyroDofConVar : FloatConVar { }
