//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Free functions for the shooting path that need no entity `this`.

#pragma once
#define SHELL_SHOT_PATH_H

#include "mathlib/vector.h"

namespace ss { class CTakeDamageInfo; }
class CGameTrace;
typedef CGameTrace trace_t;

// Included, not forward-declared: a forward declaration cannot coexist with `using CBaseEntity = IGameEntity`.
#include "igameentity.h"

// Tells every trigger volume along a shot's path that it was shot through.
void EntityShot_TraceAttackToTriggers( const ss::CTakeDamageInfo &info, const Vector &start,
                                       const Vector &end, const Vector &dir );

// The underwater bubble trail a bullet leaves. pEntity is the shooter and may be NULL.
void EntityShot_CreateBubbleTrail( IGameEntity *pEntity, const Vector &vecShotSrc,
                                   const Vector &vecShotEnd, const Vector &vecShotDir );

// NPC accuracy accounting; counts nothing in this build.
void EntityShot_UpdateStatistics( IGameEntity *pEntity, const trace_t &tr );
