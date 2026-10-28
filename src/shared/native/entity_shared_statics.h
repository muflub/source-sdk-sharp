//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Free functions for what stock's baseentity_shared.cpp defined as CBaseEntity members
// taking no `this`, plus the precache flag's storage (one per DLL).

#pragma once
#define ENTITY_SHARED_STATICS_H

// The usercmd the prediction seeds are read from, per side.
#ifdef CLIENT_DLL
class CUserCmd;
typedef CUserCmd EntityPredictionCmd_t;
#else
namespace ss
{
struct CUserCmd;
}

typedef ss::CUserCmd EntityPredictionCmd_t;
#endif

// Not a forward declaration: `class CBaseEntity;` cannot coexist with the alias.
#include "igameentity.h"

// Whether precaching is legal right now. Callers bracket a block of precaching with it
// and put the old value back.
inline bool s_bAllowPrecache = false;

inline bool &EntityPrecache_AllowedRef()
{
	return s_bAllowPrecache;
}

inline bool EntityPrecache_IsAllowed()
{
	return EntityPrecache_AllowedRef();
}

inline void EntityPrecache_SetAllowed( bool bAllow )
{
	EntityPrecache_AllowedRef() = bAllow;
}

// Whether the server simulates entities on even-numbered ticks only.
bool EntitySim_IsSimulatingOnAlternateTicks();

// Stores one usercmd's prediction seeds, or -1 ("not inside a usercmd") for NULL.
void EntityPrediction_SetSeedFromCommand( const EntityPredictionCmd_t *cmd );

// Where a tracer for a shot from vecShotSrc is drawn from.
void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart );
