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
#include "gamerules.h"
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
#include "ss_clock.h"


//-----------------------------------------------------------------------------
// The reasonableness limits. Set from the existing constants elsewhere; the
// two speed limits are bumped because the computed speeds downstream are
// legitimately above the original constants.
//-----------------------------------------------------------------------------
float k_flMaxEntityPosCoord = MAX_COORD_FLOAT;
float k_flMaxEntityEulerAngle = 360.0 * 1000.0f; // really should be restricted to +/-180, but some code doesn't adhere to this.  let's just trap NANs, etc
float k_flMaxEntitySpeed = k_flMaxVelocity * 2.0f;
float k_flMaxEntitySpinRate = k_flMaxAngularVelocity * 10.0f;

// Utility func to throttle rate at which the "reasonable position" spew goes out
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
static double s_LastEntityReasonableEmitTime;
bool CheckEmitReasonablePhysicsSpew()
{

	// Reported recently?
	double now = ss::SecondsSinceStart();
	if ( now >= s_LastEntityReasonableEmitTime && now < s_LastEntityReasonableEmitTime + 5.0 )
	{
		// Already reported recently
		return false;
	}

	// Not reported recently.  Report it now
	s_LastEntityReasonableEmitTime = now;
	return true;
}
#endif

//-----------------------------------------------------------------------------
// Returns:
//   -1 - velocity is really, REALLY bad and probably should be rejected.
//   0  - velocity was suspicious and clamped.
//   1  - velocity was OK and not modified
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
int CheckEntityVelocity( Vector &v )
{
	float r = k_flMaxEntitySpeed;
	if (
		v.x > -r && v.x < r &&
		v.y > -r && v.y < r &&
		v.z > -r && v.z < r)
	{
		// The usual case.  It's totally reasonable
		return 1;
	}
	float speed = v.Length();
	if ( speed < k_flMaxEntitySpeed * 100.0f )
	{
		// Sort of suspicious.  Clamp it
		v *= k_flMaxEntitySpeed / speed;
		return 0;
	}

	// A terrible, horrible, no good, very bad velocity.
	return -1;
}
#endif

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

//-----------------------------------------------------------------------------
// Where a tracer for a shot from vecShotSrc should be drawn from.
//
// Multiplayer answers a sentinel and not a position: the caller in
// baseentity_shared.cpp forces the attachment position in MakeTracer there, so
// the result is never read, and stock spelled that as (999,999,999) rather than
// as a branch at the call site. Kept verbatim.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart )
{
	// The `#ifndef HL2MP` this clause stood inside went with the move: neither
	// of this mod's link lines defines that name, so the guard was text and the
	// clause always ran. Deleted rather than ledgered, on tools/dead-conditionals'
	// own instruction.
	if ( g_pGameRules->IsMultiplayer() )
	{
		// NOTE: we do this because in MakeTracer, we force it to use the attachment position
		// in multiplayer, so the results from this function should never actually get used.
		pVecTracerStart->Init( 999, 999, 999 );
		return;
	}

	*pVecTracerStart = vecShotSrc;
}
#endif
