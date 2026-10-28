//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CServerEntityResolver: a handle to the entity it names, through the DLL tier's slot
// table and the level's world, as stock's CGlobalEntityList::GetBaseEntity resolves a
// CBaseHandle. One object, the DLL tier's, handed to what resolves handles.

#pragma once

#include <cstdint>

#include "ss_util.h"
#include "igameentity.h"
#include "shell_resolve.h"

class CBaseHandle;
class CServerEntityContext;
class CSourceSharpSlotTable;
class CStandalonePlayer;

class CServerEntityResolver
{
public:
	CServerEntityResolver( const CSourceSharpSlotTable &slots, const CServerEntityContext &entities,
						   int32_t &nInvisibleRangeReach )
		: m_Slots( slots )
		, m_Entities( entities )
		, m_nInvisibleRangeReach( nInvisibleRangeReach )
	{
	}

	CServerEntityResolver( const CServerEntityResolver & ) = delete;
	CServerEntityResolver &operator=( const CServerEntityResolver & ) = delete;

	// A packed handle, serial revalidated. At most one arm is set. A handle in the
	// engine-invisible entry range that resolves to nothing is counted.
	SourceSharpEntity_t Resolve( uint32_t hEntity );
	CStandaloneEntity *Standalone( uint32_t hEntity );
	CStandalonePlayer *Player( uint32_t hEntity );

	// The shell bound to a handle or entry, or NULL.
	CStandaloneEntity *ShellFromHandle( const CBaseHandle &handle ) const;
	CStandaloneEntity *ShellFromIndex( int nEntry ) const;
	CStandaloneEntity *ShellFromHandleEntity( IHandleEntity *pHandleEntity ) const;

	// A handle to whatever holds it: the slot table's shells, then the world.
	IHandleEntity *EntityFromHandle( const CBaseHandle &handle ) const;

	// The level's world, or NULL between levels; and by handle or pointer identity.
	CWorld *World() const;
	CWorld *WorldFromHandle( const CBaseHandle &handle ) const;
	CWorld *WorldFromEntityHandle( IHandleEntity *pHandleEntity ) const;

private:
	const CSourceSharpSlotTable &m_Slots;
	const CServerEntityContext &m_Entities;

	// The DLL tier's count of unresolved handles from the engine-invisible range.
	int32_t &m_nInvisibleRangeReach;
};
