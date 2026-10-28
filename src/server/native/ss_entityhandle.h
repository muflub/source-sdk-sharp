//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// ss::GameEntityFromEntityHandle and ss::TraceGameEntity: what util_shared.h's
// inlines of those names answer on the server, for code built on public/ headers (plan_server_novalve.md
// P5, lane E). The body is the server arm of that inline, unchanged.
//
// SAFE BESIDE util_shared.h: it is ss::, declares nothing global with a default
// argument, and needs only the static-prop manager and the IServerEntity chain.
// So a header included by legacy TUs as well as R5 ones (anyentity.h) can use
// it, which ss_util.h cannot offer.
//=============================================================================//

#pragma once

#include "iserverentity.h"
#include "engine/IStaticPropMgr.h"
#include "gametrace.h"

#include "igameentity_fwd.h"
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt

extern IStaticPropMgrServer *staticpropmgr;   // shell_engineboundary.cpp

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
	return ss::GameEntityFromEntityHandle( const_cast< IHandleEntity * >( pConstHandleEntity ) );
}

// The game entity a trace hit (util_shared.h's TraceGameEntity, server arm).
inline IGameEntity *TraceGameEntity( const trace_t &tr )
{
	return ss::GameEntityFromEntityHandle( ss::TraceHit( tr ) );
}

} // namespace ss
