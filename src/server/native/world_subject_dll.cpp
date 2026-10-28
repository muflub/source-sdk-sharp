//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer
//
// The server's answer to "is this the world", by pointer and by handle.

#include "gametrace.h"
#include "ss_world.h"
#include "world_subject.h"
#include "trace_hit.h"

// An identity compare against the world singleton (see world_subject.h). The cast runs
// up a single-inheritance chain.
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorldHandle(
		pHandleEntity, static_cast< const IHandleEntity * >( GetWorldEntity() ) );
}

// A projection of the bool above, so the two cannot disagree.
CWorld *SourceSharp_WorldFromEntityHandle( IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorld( pHandleEntity ) ? GetWorldEntity() : NULL;
}

// The world is not in the slot table, so its handle is compared directly. The packed
// compare checks the serial, which changes each map. Invalid handles are refused first
// so an unbound entity cannot match an unbound world.
CWorld *SourceSharp_WorldFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	CWorld *pWorld = GetWorldEntity();

	if ( !pWorld || pWorld->GetRefEHandle() != handle )
		return NULL;

	return pWorld;
}
