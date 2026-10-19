//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the bodies entity_shared_statics.h declares ========//
// SS_REASON f  a bit-level wire codec
//
// plan_entity.md Phase 4, step 4e (Utilities). This file exists because the
// header that declares these said, in prose, why it did not have one:
//
//     WHY THE BODIES STAY IN baseentity_shared.cpp. [...] Moving the bodies
//     would mean exporting that state, which is a bigger change than the lift
//     and buys nothing.
//
// It buys something now. Step 4e's gate is the shim's cross-edge count into
// game/shared/baseentity_shared.cpp, and four of the shim's edges into that
// file were these — three functions and a clamp constant that have nothing to
// do with an entity and were only ever addressed there because that is where
// the ConVar objects sat. So the ConVar objects move with them, which is the
// whole of "exporting that state".
//
// WHAT IS HERE, AND WHY EACH IS MECHANISM RATHER THAN POLICY (§6):
//
//   the four k_flMaxEntity* limits, CheckEntityVelocity and
//   CheckEmitReasonablePhysicsSpew   — arithmetic over a Vector against four
//   floats, plus a five-second throttle on a spew. They decide nothing about
//   what the game does; they say whether a number is a number.
//
//   EntitySim_IsSimulatingOnAlternateTicks — reads gpGlobals->maxClients and
//   one ConVar. The ConVar (sv_alternateticks) is declared here now.
//
//   EntityPrediction_SetSeedFromCommand — copies two ints out of a CUserCmd
//   into entity_prediction_seed.h's storage.
//
//   EntityTracer_ComputeStartPosition — where a tracer is drawn from.
//
// THE TRACER HELPER LOST ITS ENTITY PARAMETER, and that is the step's own rule
// rather than a tidy-up: step 4e's paragraph says every utility the shim still
// calls is *either managed or a shim mechanism function taking handles*. This
// one took a pointer to the shooter and did not read it — both arms that read
// it were compiled out for the whole life of this port, so the parameter was
// dead weight that could only be supplied by a caller holding a raw entity
// pointer, which is precisely what Phase 4 is removing. It takes no entity at
// all now: neither a pointer nor a handle, because it needs neither.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#endif
#include "entity_shared_statics.h"
#include "entity_prediction_seed.h"
#ifdef CLIENT_DLL
#include "baseentity_shared.h"
#include "usercmd.h"
#else
// SERVER.SO ON THE R5 SET (P5 lane E): gpGlobals in place of cbase.h.
#include "edict.h"
#include "ss_usercmd.h"   // the server's command record (plan_server_novalve.md P5)
#include "igamerules.h"
extern CGlobalVars *gpGlobals;   // shell_engineboundary.cpp
#endif
#include "worldsize.h"
#include "vphysics/performance.h"

#include "managed_convar_ref.h"


//-----------------------------------------------------------------------------
// The reasonableness limits. Set from the existing constants elsewhere; the
// two speed limits are bumped because the computed speeds downstream are
// legitimately above the original constants.
//-----------------------------------------------------------------------------
float k_flMaxEntityPosCoord = MAX_COORD_FLOAT;
float k_flMaxEntityEulerAngle = 360.0 * 1000.0f; // really should be restricted to +/-180, but some code doesn't adhere to this.  let's just trap NANs, etc
float k_flMaxEntitySpeed = k_flMaxVelocity * 2.0f;
float k_flMaxEntitySpinRate = k_flMaxAngularVelocity * 10.0f;

//-----------------------------------------------------------------------------
// Whether the server simulates entities on even-numbered ticks only.
//-----------------------------------------------------------------------------
static CManagedConVarRef sv_alternateticks( "sv_alternateticks" );

bool EntitySim_IsSimulatingOnAlternateTicks()
{
	if ( gpGlobals->maxClients != 1 )
	{
		return false;
	}

	return sv_alternateticks.GetBool();
}

//-----------------------------------------------------------------------------
// The prediction seeds carried by one usercmd, or -1 for "not inside a usercmd"
// when handed NULL.
//-----------------------------------------------------------------------------
void EntityPrediction_SetSeedFromCommand( const EntityPredictionCmd_t *cmd )
{
	if ( !cmd )
	{
		EntityPrediction_SetSeed( -1 );
#ifdef GAME_DLL
		EntityPrediction_SetServerSeed( -1 );
#endif

		return;
	}

	EntityPrediction_SetSeed( cmd->random_seed );
#ifdef GAME_DLL
	EntityPrediction_SetServerSeed( cmd->server_random_seed );
#endif
}
