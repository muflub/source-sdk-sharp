//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server.Entities;

// The control-point convars stock declares in server-only code that neither
// the core nor TF2's 1.3 set carries. TFConVarsTests holds each row to the
// pristine declaration.

// ---- server/trigger_area_capture.cpp ----------------------------------------

[ConVar("mp_simulatemultiplecappers", "1", Flags = ConVarFlags.Cheat)]
public sealed class MpSimulateMultipleCappersConVar : IntConVar { }
