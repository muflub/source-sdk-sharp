//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif

#ifdef CLIENT_DLL
#include "client_entity.h"

#else

#include "igamerules.h"
extern CGlobalVars *gpGlobals;
#endif

#include "vphysics/performance.h"

#include "managed_convar_ref.h"

// Whether the server simulates entities on even-numbered ticks only.
static CManagedConVarRef sv_alternateticks( "sv_alternateticks" );

bool EntitySim_IsSimulatingOnAlternateTicks()
{
	if ( gpGlobals->maxClients != 1 )
	{
		return false;
	}

	return sv_alternateticks.GetBool();
}
