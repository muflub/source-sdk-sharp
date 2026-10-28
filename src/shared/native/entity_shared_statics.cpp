//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec

#ifdef CLIENT_DLL
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif
#include "entity_shared_statics.h"
#include "entity_prediction_seed.h"
#ifdef CLIENT_DLL
#include "client_entity.h"
#include "usercmd.h"
#else
#include "edict.h"
#include "ss_usercmd.h"
#include "igamerules.h"
extern CGlobalVars *gpGlobals;
#endif
#include "worldsize.h"
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
