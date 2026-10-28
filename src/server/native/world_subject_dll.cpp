//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer
//
// The server's answer to "is this the world", by pointer and by handle.

#include "ss_world.h"
#include "world_subject.h"

// An identity compare against the world singleton (see world_subject.h). The cast runs
// up a single-inheritance chain.
bool SourceSharp_IsTheWorld( const IHandleEntity *pHandleEntity )
{
	return SourceSharp_IsTheWorldHandle( pHandleEntity, static_cast<const IHandleEntity *>( GetWorldEntity() ) );
}
