//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's entity resolvers: an index or handle the caller already holds,
// answered with an engine interface. Never iteration or counts; "which entities
// exist" is the managed EntityDirectory's question. slot_table.h answers with
// the concrete shell instead.

#pragma once

#include "basehandle.h"
#include "const.h"
#include "icliententitylist.h"

class IClientEntity;
class IClientNetworkable;
class IClientUnknown;
class IClientRenderable;
class IClientThinkable;

// Not a bare forward declaration: C_BaseEntity may be an alias.
#include "igameentity.h"

// By index. An index naming no entity answers NULL.
IClientUnknown     *SourceSharp_ClientUnknownFromIndex( int nEntry );
IClientEntity      *SourceSharp_ClientEntityFromIndex( int nEntry );

// By handle. The serial is checked, so a dead entity's handle answers NULL.
IClientUnknown     *SourceSharp_ClientUnknownFromHandle( const CBaseHandle &hEnt );
IClientEntity      *SourceSharp_ClientEntityFromHandle( const CBaseHandle &hEnt );

IClientNetworkable *SourceSharp_ClientNetworkableFromHandle( const CBaseHandle &hEnt );

IClientRenderable  *SourceSharp_ClientRenderableFromHandle( const CBaseHandle &hEnt );

// The handle for an index, for a caller that must hold on to what it found.
CBaseHandle         SourceSharp_ClientHandleFromIndex( int nEntry );

// C_StandaloneEntity's constructor and destructor: the only places a shell
// enters or leaves the slot table. Takes the concrete type because
// slot_table.h static_casts back what it stores.
class C_StandaloneEntity;

void SourceSharp_ClientEntityBind( C_StandaloneEntity *pShell, int nEntNum, int nSerialNum );
void SourceSharp_ClientEntityUnbind( C_StandaloneEntity *pShell );

// The world is not in the slot table; these compare a handle or index with the
// handle the C_World singleton is bound to. The serial is compared, so the
// previous map's world handle does not resolve. An unbound world answers NULL.
// Defined in world_adapter_client.cpp.
IClientUnknown *SourceSharp_ClientWorldFromIndex( int nEntry );
IClientUnknown *SourceSharp_ClientWorldFromHandle( const CBaseHandle &hEnt );

// The world's bound handle, or an invalid handle when no level is loaded.
CBaseHandle     SourceSharp_ClientWorldHandle();

// Releases the world at level shutdown; false if no level was loaded.
bool            SourceSharp_ClientWorldLevelShutdown();

// The rules the world arm decides with, engine-free for tests. Each tests
// IsValid() first: GetEntryIndex() on an invalid handle answers NUM_ENT_ENTRIES-1.
inline bool SourceSharp_ClientWorldAnswersForIndex( const CBaseHandle &hWorld, int nEntry )
{
	return hWorld.IsValid() && hWorld.GetEntryIndex() == nEntry;
}

inline bool SourceSharp_ClientWorldAnswersForHandle( const CBaseHandle &hWorld,
                                                     const CBaseHandle &hAsked )
{
	// operator== compares entry and serial.
	return hWorld.IsValid() && hWorld == hAsked;
}

inline int SourceSharp_ClientHighestWithWorld( const CBaseHandle &hWorld, int nTableHighest )
{
	if ( !hWorld.IsValid() )
		return nTableHighest;

	const int nWorld = hWorld.GetEntryIndex();

	return nWorld > nTableHighest ? nWorld : nTableHighest;
}

inline int SourceSharp_ClientCountWithWorld( const CBaseHandle &hWorld, int nTableCount )
{
	return hWorld.IsValid() ? nTableCount + 1 : nTableCount;
}

// The index of what a trace hit: the entry when this side's table resolves the
// handle and it is networked, else -1.
inline int SourceSharp_ClientIndexOfHit( const CBaseHandle &hHit, bool bResolves )
{
	if ( !hHit.IsValid() || !bResolves )
		return -1;

	const int nEntry = hHit.GetEntryIndex();
	return nEntry < MAX_EDICTS ? nEntry : -1;
}

// What is left of the stock client entity list: the bind writes the handle,
// the rest answer nothing.
CBaseHandle     SourceSharp_LegacyListBind( IClientUnknown *pUnknown, int nEntNum, int nSerialNum );
void            SourceSharp_LegacyListUnbind( const CBaseHandle &hEnt );
IClientUnknown *SourceSharp_LegacyListUnknownFromIndex( int nEntry );
IClientUnknown *SourceSharp_LegacyListUnknownFromHandle( const CBaseHandle &hEnt );
