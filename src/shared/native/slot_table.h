//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The slot table: per side, an array indexed by a handle's entry index holding the bound
// shell pointer and that binding's serial. It binds, unbinds and resolves; it does not
// iterate, and must-not-compile tests hold that. Entries 2048..8191 (NUM_ENT_ENTRIES)
// are for entities the engine never sees.
//
// Serials follow stock: bumped on unbind so stale handles fail at once, wrapping at
// fifteen bits. Serial 0 is real (the client world's), so validity is IsValid(), never a
// compare against zero; an invalid handle's entry index is 8191, a bindable entry.
#pragma once

#include <string.h>

#include "basehandle.h"

enum { SOURCESHARP_SLOT_SERIAL_MASK = ( 1 << ( NUM_SERIAL_NUM_BITS - 1 ) ) - 1 };

class CSourceSharpSlotTable
{
public:
	CSourceSharpSlotTable() { Reset(); }

	// Every binding gone and every serial back to zero. Constructor and tests only:
	// a handle from before can then compare equal to a new binding.
	void Reset( void )
	{
		memset( m_pShell, 0, sizeof( m_pShell ) );
		memset( m_nSerial, 0, sizeof( m_nSerial ) );

		m_nBound   = 0;
		m_nBinds   = 0;
		m_nUnbinds = 0;
		m_nRefused = 0;
	}

	// Every binding gone, serials kept, for level shutdown. The edict keeps its network
	// serial across a map load, and a repeated serial would make the engine pack the new
	// entity as a continuation of the old one. Unbind does the bump.
	void ClearBindings( void )
	{
		for ( int nEntry = 0; nEntry < NUM_ENT_ENTRIES; nEntry++ )
			Unbind( nEntry );
	}

	// GetEntryIndex masks with sixteen bits but the table has thirteen, so a malformed
	// handle can name an entry that does not exist.
	static bool IsEntry( int nEntry )
	{
		return nEntry >= 0 && nEntry < NUM_ENT_ENTRIES;
	}

	// Entries at or above MAX_EDICTS are managed-only; the engine never sees them.
	static bool IsEdictEntry( int nEntry )
	{
		return nEntry >= 0 && nEntry < MAX_EDICTS;
	}

	static int EntryCount( void ) { return NUM_ENT_ENTRIES; }

	// Binds at an entry with the entry's current serial. Returns an invalid handle when
	// the entry is out of range or already bound, or the shell is NULL.
	CBaseHandle Bind( int nEntry, void *pShell )
	{
		if ( !IsEntry( nEntry ) || !pShell || m_pShell[nEntry] )
		{
			++m_nRefused;
			return CBaseHandle();
		}

		m_pShell[nEntry] = pShell;
		++m_nBound;
		++m_nBinds;

		return CBaseHandle( nEntry, (int)m_nSerial[nEntry] );
	}

	// Binds at a handle assigned elsewhere, adopting its serial: the client is told its
	// serials by the server. Refuses an invalid handle first (its entry is 8191).
	bool BindAt( const CBaseHandle &handle, void *pShell )
	{
		const int nEntry = handle.IsValid() ? handle.GetEntryIndex() : -1;

		if ( !IsEntry( nEntry ) || !pShell || m_pShell[nEntry] )
		{
			++m_nRefused;
			return false;
		}

		// Stored exactly as given, not masked, or the entity would be unreachable
		// through the handle it was bound with.
		m_pShell[nEntry] = pShell;
		m_nSerial[nEntry] = (unsigned short)handle.GetSerialNumber();
		++m_nBound;
		++m_nBinds;

		return true;
	}

	// Makes every handle to this binding stale. Idempotent: a second unbind must not bump
	// the serial again. Takes the entry, so a stale handle cannot unbind the occupant.
	void Unbind( int nEntry )
	{
		if ( !IsEntry( nEntry ) || !m_pShell[nEntry] )
			return;

		m_pShell[nEntry] = NULL;
		m_nSerial[nEntry] = (unsigned short)( ( m_nSerial[nEntry] + 1 ) & SOURCESHARP_SLOT_SERIAL_MASK );

		--m_nBound;
		++m_nUnbinds;
	}

	// Answers NULL for an invalid handle, an empty entry or a serial mismatch. The
	// serial check is what keeps a stale handle from reaching a new occupant.
	void *Resolve( const CBaseHandle &handle ) const
	{
		if ( !handle.IsValid() )
			return NULL;

		const int nEntry = handle.GetEntryIndex();

		if ( !IsEntry( nEntry ) )
			return NULL;

		if ( (int)m_nSerial[nEntry] != handle.GetSerialNumber() )
			return NULL;

		return m_pShell[nEntry];
	}

	// Whoever is bound at the entry now. No serial: the engine's index APIs carry none.
	void *ResolveByIndex( int nEntry ) const
	{
		return IsEntry( nEntry ) ? m_pShell[nEntry] : NULL;
	}

	CBaseHandle HandleOf( int nEntry ) const
	{
		if ( !IsEntry( nEntry ) || !m_pShell[nEntry] )
			return CBaseHandle();

		return CBaseHandle( nEntry, (int)m_nSerial[nEntry] );
	}

	// The handle a bind at this entry would produce, without binding: the shell's
	// SetRefEHandle is the single binder, so issuing comes first. An occupied entry
	// answers an invalid handle.
	CBaseHandle IssueHandle( int nEntry ) const
	{
		if ( !IsEntry( nEntry ) || m_pShell[nEntry] )
			return CBaseHandle();

		return CBaseHandle( nEntry, (int)m_nSerial[nEntry] );
	}

	// The serial the entry's next binding gets, for the edict's m_NetworkSerialNumber mirror.
	int SerialAt( int nEntry ) const
	{
		return IsEntry( nEntry ) ? (int)m_nSerial[nEntry] : 0;
	}

	bool IsBound( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_pShell[nEntry] != NULL;
	}

	// How many bindings exist. Never which: there is deliberately no highest-index query,
	// which would turn ResolveByIndex into a walk.
	int BoundCount( void ) const { return m_nBound; }

	// Cumulative counters for the console dump.
	int Binds( void ) const   { return m_nBinds; }
	int Unbinds( void ) const { return m_nUnbinds; }
	int Refused( void ) const { return m_nRefused; }

private:
	CSourceSharpSlotTable( const CSourceSharpSlotTable & );
	CSourceSharpSlotTable &operator=( const CSourceSharpSlotTable & );

	// Parallel arrays, not a struct array, so there is no element type to hand out.
	void          *m_pShell[NUM_ENT_ENTRIES];
	unsigned short m_nSerial[NUM_ENT_ENTRIES];

	int m_nBound;
	int m_nBinds;
	int m_nUnbinds;
	int m_nRefused;
};

#ifndef CLIENT_DLL

class CStandaloneEntity;
struct edict_t;

CSourceSharpSlotTable &SourceSharp_SlotTable( void );

// The handle a shell at this entry will carry, issued without binding; invalid if the
// entry is occupied or out of range.
CBaseHandle SourceSharp_SlotIssue( int nEntry );

// The world is not in the table, so it carries its own serial under the table's wrap.
// Never zero: entry 0 with serial 0 packs to the INVALID_EHANDLE enumerator (0).
inline unsigned short SourceSharp_NextWorldSerial( unsigned short nPrevious )
{
	const unsigned short nNext =
		(unsigned short)( ( nPrevious + 1 ) & SOURCESHARP_SLOT_SERIAL_MASK );

	return nNext ? nNext : (unsigned short)1;
}

bool SourceSharp_SlotBindAt( const CBaseHandle &handle, CStandaloneEntity *pShell,
                             edict_t *pEdict );

void SourceSharp_SlotUnbind( int nEntry );

// The first of these carries the one permitted downcast.
CStandaloneEntity *SourceSharp_ShellFromHandle( const CBaseHandle &handle );
CStandaloneEntity *SourceSharp_ShellFromIndex( int nEntry );
CStandaloneEntity *SourceSharp_ShellFromHandleEntity( IHandleEntity *pHandleEntity );

// A handle to whatever holds it: the slot table, then the world, then the SDK's entity
// list. The three populations are disjoint.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle );

#endif // !CLIENT_DLL

#ifdef CLIENT_DLL

class C_StandaloneEntity;

// The client's table, defined in shell_cliententitylist.cpp. There is no client Bind():
// the server assigns serials, so the client only binds at the handle it is given.
CSourceSharpSlotTable &SourceSharp_ClientSlotTable( void );

// Binds at the handle the server assigned. False if the entry was bound or the handle invalid.
bool SourceSharp_ClientSlotBindAt( const CBaseHandle &handle, C_StandaloneEntity *pShell );

void SourceSharp_ClientSlotUnbind( int nEntry );

// The first carries the client's one permitted downcast; the others go through it.
C_StandaloneEntity *SourceSharp_ClientShellFromHandle( const CBaseHandle &handle );
C_StandaloneEntity *SourceSharp_ClientShellFromIndex( int nEntry );
C_StandaloneEntity *SourceSharp_ClientShellFromHandleEntity( IHandleEntity *pHandleEntity );

// A trace's m_pEnt to the shell it hit, or NULL (never a static prop).
C_StandaloneEntity *SourceSharp_ClientShellFromTraceHit( IHandleEntity *pHandleEntity );

#endif // CLIENT_DLL
