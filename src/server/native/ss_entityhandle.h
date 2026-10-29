//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// ss::GameEntityFromEntityHandle and ss::TraceGameEntity, the server arms of
// util_shared.h's inlines. Safe to include beside util_shared.h.

#pragma once

#include "iserverentity.h"
#include "engine/IStaticPropMgr.h"
#include "gametrace.h"

#include "igameentity_fwd.h"
#include "trace_hit.h"

namespace ss
{

// The game entity behind a handle known to be a server entity (an edict's, or what a
// factory made), or NULL for nothing or anything outside the game hierarchy.
inline IGameEntity *GameEntityFromServerEntity( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	// IServerEntity, IServerUnknown and IHandleEntity are a single-inheritance
	// chain, so all three views of an entity share an address.
	IServerEntity *pServerEntity = (IServerEntity *)pHandleEntity;
	return pServerEntity->GetBaseEntity();
}

// The game entity behind a handle the engine or the partition handed back, or
// NULL for nothing, a static prop, or anything outside the game hierarchy.
inline IGameEntity *GameEntityFromEntityHandle( IStaticPropMgrServer *pStaticProps, IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	// A static prop is not an entity at all and has no interface to ask.
	if ( pStaticProps->IsStaticProp( pHandleEntity ) )
		return NULL;

	return GameEntityFromServerEntity( pHandleEntity );
}

// The game entity a trace hit (util_shared.h's TraceGameEntity, server arm).
inline IGameEntity *TraceGameEntity( IStaticPropMgrServer *pStaticProps, const trace_t &tr )
{
	return ss::GameEntityFromEntityHandle( pStaticProps, ss::TraceHit( tr ) );
}

} // namespace ss
