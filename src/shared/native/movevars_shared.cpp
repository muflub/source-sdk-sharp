//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration

// The two sides have different cbase.h files, and some of these headers are not
// on the server include path.
#ifdef CLIENT_DLL
#include "client_class.h"
#include "movevars_shared.h"
#else
// The server half reads one managed convar.
#include "managed_convar_ref.h"
float GetCurrentGravity();
#endif

// some cvars used by player movement system
// Stock's non-HL2 default.
float GetCurrentGravity()
{
	// The server's sv_gravity is declared in C#.
	static CManagedConVarRef s_Gravity("sv_gravity");
	return s_Gravity.GetFloat();
}

// The movement area's slot, on both sides, so neither answers a plausible managed fallback.
float SourceSharp_CurrentGravity()
{
	return GetCurrentGravity();
}
