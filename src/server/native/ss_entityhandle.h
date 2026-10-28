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

extern IStaticPropMgrServer *staticpropmgr;

namespace ss
{

// The game entity behind a handle the engine or the partition handed back, or
// NULL for nothing, a static prop, or anything outside the game hierarchy.
inline IGameEntity *GameEntityFromEntityHandle( IHandleEntity *pHandleEntity )
{
	if ( !pHandleEntity )
		return NULL;

	// A static prop is not an entity at all and has no interface to ask.
	if ( staticpropmgr->IsStaticProp( pHandleEntity ) )
		return NULL;

	// IServerEntity, IServerUnknown and IHandleEntity are a single-inheritance
	// chain, so all three views of an entity share an address.
	IServerEntity *pServerEntity = (IServerEntity *)pHandleEntity;
	return pServerEntity->GetBaseEntity();
}

inline const IGameEntity *GameEntityFromEntityHandle( const IHandleEntity *pConstHandleEntity )
{
	// Qualified: unqualified, argument-dependent lookup would also find
	// util_shared.h's global overload in a TU that sees both.
	return ss::GameEntityFromEntityHandle( const_cast<IHandleEntity *>( pConstHandleEntity ) );
}

// The game entity a trace hit (util_shared.h's TraceGameEntity, server arm).
inline IGameEntity *TraceGameEntity( const trace_t &tr )
{
	return ss::GameEntityFromEntityHandle( ss::TraceHit( tr ) );
}

} // namespace ss
