//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "cmodel.h"

#include "iserverentity.h"
#include "globalvars_base.h"
#include "engine/ICollideable.h"
#include "iservernetworkable.h"

struct edict_t;

// Purpose: Defines the ways that a map can be loaded.
enum MapLoadType_t
{
	MapLoad_NewGame = 0,
	MapLoad_LoadGame,
	MapLoad_Transition,
	MapLoad_Background,
};

// Purpose: Global variables shared between the engine and the game .dll
class CGlobalVars : public CGlobalVarsBase
{
public:
	CGlobalVars( bool bIsClient );

public:
	// Current map
	string_t mapname;
	alignas( 8 ) unsigned char unused_mapversion[16];
	MapLoadType_t eLoadType; // How the current map was loaded
	alignas( 4 ) unsigned char unused_bMapLoadFailed[8];

	int serverCount;
};

inline CGlobalVars::CGlobalVars( bool bIsClient )
	: CGlobalVarsBase( bIsClient )
{
	serverCount = 0;
}

class CPlayerState;
class IServerNetworkable;
class IServerEntity;

#define FL_EDICT_CHANGED ( 1 << 0 ) // Game DLL sets this when the entity state changes
// Mutually exclusive with FL_EDICT_PARTIAL_CHANGE.

#define FL_EDICT_FREE ( 1 << 1 ) // this edict if free for reuse
#define FL_EDICT_FULL ( 1 << 2 ) // this is a full server entity

#define FL_EDICT_FULLCHECK ( 0 << 0 ) // call ShouldTransmit() each time, this is a fake flag
#define FL_EDICT_ALWAYS ( 1 << 3 ) // always transmit this entity
#define FL_EDICT_DONTSEND ( 1 << 4 ) // don't transmit this entity
#define FL_EDICT_PVSCHECK ( 1 << 5 ) // always transmit entity, but cull against PVS

// Used by local network backdoor.

// This is always set at the same time EFL_DIRTY_PVS_INFORMATION is set, but it
// gets cleared in a different place.

// This is used internally to edict_t to remember that it's carrying a
// "full change list" - all its properties might have changed their value.
#define FL_FULL_EDICT_CHANGED ( 1 << 8 )

// Max # of variable changes we'll track in an entity before we treat it
// like they all changed.
#define MAX_CHANGE_OFFSETS 19
#define MAX_EDICT_CHANGE_INFOS 100

class CEdictChangeInfo
{
public:
	// Edicts remember the offsets of properties that change
	unsigned short m_ChangeOffsets[MAX_CHANGE_OFFSETS];
	unsigned short m_nChangeOffsets;
};

// Shared between engine and game DLL.
class CSharedEdictChangeInfo
{
public:
	CSharedEdictChangeInfo() { m_iSerialNumber = 1; }

	// Matched against edict_t::m_iChangeInfoSerialNumber to determine if its
	// change info is valid.
	unsigned short m_iSerialNumber;

	CEdictChangeInfo m_ChangeInfos[MAX_EDICT_CHANGE_INFOS];
	unsigned short m_nChangeInfos; // How many are in use this frame.
};

// Not defined by the game DLL: it marks its edicts through SourceSharp_MarkEdictChanged
// (edict_change.h), so the StateChanged inlines below, which call this, are unused.
CSharedEdictChangeInfo *SharedEdictChangeInfo();

class IChangeInfoAccessor
{
public:
	inline void SetChangeInfo( unsigned short info ) { m_iChangeInfo = info; }

	inline void SetChangeInfoSerialNumber( unsigned short sn ) { m_iChangeInfoSerialNumber = sn; }

	inline unsigned short GetChangeInfo() const { return m_iChangeInfo; }

	inline unsigned short GetChangeInfoSerialNumber() const { return m_iChangeInfoSerialNumber; }

private:
	unsigned short m_iChangeInfo;
	unsigned short m_iChangeInfoSerialNumber;
};

// NOTE: YOU CAN'T CHANGE THE LAYOUT OR SIZE OF CBASEEDICT AND REMAIN COMPATIBLE WITH HL2_VC6!!!!!
class CBaseEdict
{
public:
	// Returns an IServerEntity if FL_FULLEDICT is set or NULL if this
	// is a lightweight networking entity.
	IServerEntity *GetIServerEntity();

	IServerUnknown *GetUnknown();

	// Set when initting an entity. If it's only a networkable, this is false.
	void SetEdict( IServerUnknown *pUnk, bool bFullEdict );

	bool IsFree() const;

	void StateChanged();
	void StateChanged( unsigned short offset );

	void ClearTransmitState();

	void SetChangeInfoSerialNumber( unsigned short sn );

public:
	// NOTE: this is in the edict instead of being accessed by a virtual because the engine needs fast access to it.
	// NOTE: YOU CAN'T CHANGE THE LAYOUT OR SIZE OF CBASEEDICT AND REMAIN COMPATIBLE WITH HL2_VC6!!!!!
	int m_fStateFlags;

	// NOTE: this is in the edict instead of being accessed by a virtual because the engine needs fast access to it.
	// int m_NetworkSerialNumber;

	// NOTE: m_EdictIndex is an optimization since computing the edict index
	// from a CBaseEdict* pointer otherwise requires divide-by-20. values for
	// m_NetworkSerialNumber all fit within a 16-bit integer range, so we're
	// repurposing the other 16 bits to cache off the index without changing
	// the overall layout or size of this struct. existing mods compiled with
	// a full 32-bit serial number field should still work. henryg 8/17/2011
#if VALVE_LITTLE_ENDIAN
	short m_NetworkSerialNumber;
	short m_EdictIndex;
#else
	short m_EdictIndex;
	short m_NetworkSerialNumber;
#endif

	// NOTE: this is in the edict instead of being accessed by a virtual because the engine needs fast access to it.
	IServerNetworkable *m_pNetworkable;

protected:
	IServerUnknown *m_pUnk;

public:
	IChangeInfoAccessor *GetChangeAccessor(); // The engine implements this and the game .dll implements as
	// as callback through to the engine!!!

	// NOTE: YOU CAN'T CHANGE THE LAYOUT OR SIZE OF CBASEEDICT AND REMAIN COMPATIBLE WITH HL2_VC6!!!!!
	// This breaks HL2_VC6!!!!!
	// References a CEdictChangeInfo with a list of modified network props.
	//unsigned short m_iChangeInfo;
	//unsigned short m_iChangeInfoSerialNumber;
};

// CBaseEdict inlines.
inline IServerEntity *CBaseEdict::GetIServerEntity()
{
	if ( m_fStateFlags & FL_EDICT_FULL )
		return (IServerEntity *)m_pUnk;
	else
		return 0;
}

inline bool CBaseEdict::IsFree() const
{
	return ( m_fStateFlags & FL_EDICT_FREE ) != 0;
}

inline void CBaseEdict::StateChanged()
{
	// Note: this should only happen for properties in data tables that used some
	// kind of pointer dereference. If the data is directly offsetable
	m_fStateFlags |= ( FL_EDICT_CHANGED | FL_FULL_EDICT_CHANGED );
	SetChangeInfoSerialNumber( 0 );
}

inline void CBaseEdict::StateChanged( unsigned short offset )
{
	if ( m_fStateFlags & FL_FULL_EDICT_CHANGED )
		return;

	m_fStateFlags |= FL_EDICT_CHANGED;

	IChangeInfoAccessor *accessor = GetChangeAccessor();
	CSharedEdictChangeInfo *pShared = SharedEdictChangeInfo();

	if ( accessor->GetChangeInfoSerialNumber() == pShared->m_iSerialNumber )
	{
		// Ok, I still own this one.
		CEdictChangeInfo *p = &pShared->m_ChangeInfos[accessor->GetChangeInfo()];

		// Now add this offset to our list of changed variables.
		for ( unsigned short i = 0; i < p->m_nChangeOffsets; i++ )
			if ( p->m_ChangeOffsets[i] == offset )
				return;

		if ( p->m_nChangeOffsets == MAX_CHANGE_OFFSETS )
		{
			// Invalidate our change info.
			accessor->SetChangeInfoSerialNumber( 0 );
			m_fStateFlags |= FL_FULL_EDICT_CHANGED; // So we don't get in here again.
		}
		else
		{
			p->m_ChangeOffsets[p->m_nChangeOffsets++] = offset;
		}
	}
	else
	{
		if ( pShared->m_nChangeInfos == MAX_EDICT_CHANGE_INFOS )
		{
			// Shucks.. have to mark the edict as fully changed because we don't have room to remember this change.
			accessor->SetChangeInfoSerialNumber( 0 );
			m_fStateFlags |= FL_FULL_EDICT_CHANGED;
		}
		else
		{
			// Get a new CEdictChangeInfo and fill it out.
			accessor->SetChangeInfo( pShared->m_nChangeInfos );
			pShared->m_nChangeInfos++;

			accessor->SetChangeInfoSerialNumber( pShared->m_iSerialNumber );

			CEdictChangeInfo *p = &pShared->m_ChangeInfos[accessor->GetChangeInfo()];
			p->m_ChangeOffsets[0] = offset;
			p->m_nChangeOffsets = 1;
		}
	}
}

inline void CBaseEdict::ClearTransmitState()
{
	m_fStateFlags &= ~( FL_EDICT_ALWAYS | FL_EDICT_PVSCHECK | FL_EDICT_DONTSEND );
}

inline IServerUnknown *CBaseEdict::GetUnknown()
{
	return m_pUnk;
}

inline void CBaseEdict::SetEdict( IServerUnknown *pUnk, bool bFullEdict )
{
	m_pUnk = pUnk;
	if ( ( pUnk != NULL ) && bFullEdict )
	{
		m_fStateFlags = FL_EDICT_FULL;
	}
	else
	{
		m_fStateFlags = 0;
	}
}

inline void CBaseEdict::SetChangeInfoSerialNumber( unsigned short sn )
{
	GetChangeAccessor()->SetChangeInfoSerialNumber( sn );
}

// Purpose: The engine's internal representation of an entity, including some
//  basic collision and position info and a pointer to the class wrapped on top
//  of the structure
struct edict_t : public CBaseEdict
{
public:
	ICollideable *GetCollideable();

	// The server timestampe at which the edict was freed (so we can try to use other edicts before reallocating this one)
	float freetime;
};

inline ICollideable *edict_t::GetCollideable()
{
	IServerEntity *pEnt = GetIServerEntity();
	if ( pEnt )
		return pEnt->GetCollideable();
	else
		return NULL;
}
