// The entity control block C# maps to validate its cached blob pointers.
//
//     nSerial   written only by native, when a slot is opened, closed or retired
//     nDirty    written only by C# when it writes a field, drained by native per frame
//
// One writer per word and everything on the game thread is what makes this lock-free.
// The serial is this block's own: the handle's serial wraps at 16 bits and does not
// change when the managed side reloads.
#pragma once

#include <stdint.h>
#include <string.h>

// The slot layout is the ABI contract C# indexes by stride.
#include "sourcesharp.h"

// NUM_ENT_ENTRIES, restated so this header needs no const.h.
enum { SOURCESHARP_MAX_ENTITY_SLOTS = 8192 };

// A serial no live slot carries: a closed slot and an untouched one are both zero.
enum { SOURCESHARP_SLOT_SERIAL_NONE = 0 };

// A compact list of open entries lets the drain skip the 8192 slots; removal swaps
// with the tail, so m_WhereOne records each entry's position.
class CSourceSharpEntitySlots
{
public:
	CSourceSharpEntitySlots() { Reset(); }

	// Everything back to closed, and the serial counter with it. Never between frames.
	void Reset( void )
	{
		memset( m_Slots, 0, sizeof( m_Slots ) );
		memset( m_Handles, 0, sizeof( m_Handles ) );
		memset( m_WhereOne, 0, sizeof( m_WhereOne ) );

		m_nActive     = 0;
		m_nNextSerial = SOURCESHARP_SLOT_SERIAL_NONE;
		m_nOpens      = 0;
		m_nCloses     = 0;
	}

	SourceSharpEntitySlot_t *Slots( void ) { return m_Slots; }
	int Count( void ) const { return SOURCESHARP_MAX_ENTITY_SLOTS; }

	static bool IsEntry( int nEntry )
	{
		return nEntry >= 0 && nEntry < SOURCESHARP_MAX_ENTITY_SLOTS;
	}

	// Opens the slot for an entry and gives it a fresh serial. Re-opening for the same
	// handle keeps the serial and the dirty word: a class with two state fields binds
	// twice, and a fresh serial would invalidate the first. False for an entry outside
	// the block, which a malformed handle (16-bit entry mask) can name.
	bool Open( int nEntry, uint32_t hEntity, uint32_t *pOutSerial )
	{
		if ( pOutSerial )
			*pOutSerial = SOURCESHARP_SLOT_SERIAL_NONE;

		if ( !IsEntry( nEntry ) )
			return false;

		// Already open for this entity: hand back its serial. Still counted as an open.
		if ( m_WhereOne[nEntry] && m_Handles[nEntry] == hEntity )
		{
			++m_nOpens;

			if ( pOutSerial )
				*pOutSerial = m_Slots[nEntry].nSerial;

			return true;
		}

		if ( !m_WhereOne[nEntry] )
		{
			m_Active[m_nActive++] = nEntry;
			m_WhereOne[nEntry] = m_nActive;
		}

		m_Slots[nEntry].nSerial = NextSerial();
		m_Slots[nEntry].nDirty  = 0;
		m_Handles[nEntry]       = hEntity;

		++m_nOpens;

		if ( pOutSerial )
			*pOutSerial = m_Slots[nEntry].nSerial;

		return true;
	}

	// Closes a slot, so every pointer C# holds into it reports stale. Idempotent:
	// destructors call it whether or not the entity was ever bound.
	void Close( int nEntry )
	{
		if ( !IsEntry( nEntry ) || !m_WhereOne[nEntry] )
			return;

		const int nAt = m_WhereOne[nEntry] - 1;
		const int nTail = m_Active[--m_nActive];

		m_Active[nAt]      = nTail;
		m_WhereOne[nTail]  = nAt + 1;
		m_WhereOne[nEntry] = 0;

		m_Slots[nEntry].nSerial = SOURCESHARP_SLOT_SERIAL_NONE;
		m_Slots[nEntry].nDirty  = 0;
		m_Handles[nEntry]       = 0;

		++m_nCloses;
	}

	// After a managed reload: gives every open slot a new serial so pointers from the
	// old assembly compare stale. Slots stay open and nDirty is kept, so the last
	// write the old assembly made is still drained.
	int Retire( void )
	{
		for ( int i = 0; i < m_nActive; ++i )
			m_Slots[ m_Active[i] ].nSerial = NextSerial();

		return m_nActive;
	}

	bool IsOpen( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_WhereOne[nEntry] != 0;
	}

	uint32_t SerialAt( int nEntry ) const
	{
		return IsEntry( nEntry ) ? m_Slots[nEntry].nSerial : (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE;
	}

	uint32_t HandleAt( int nEntry ) const { return IsEntry( nEntry ) ? m_Handles[nEntry] : 0u; }

	// C# owns this word; native writes it only here, for tests and the poke command.
	void MarkDirty( int nEntry )
	{
		if ( IsOpen( nEntry ) )
			m_Slots[nEntry].nDirty = 1;
	}

	bool IsDirty( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_Slots[nEntry].nDirty != 0;
	}

	// The open entries, for the drain. Order changes on close.
	int ActiveCount( void ) const { return m_nActive; }
	int ActiveAt( int i ) const { return ( i >= 0 && i < m_nActive ) ? m_Active[i] : -1; }

	// Reads and clears the dirty word in one step, so a drain cannot forget to clear it.
	bool TakeDirty( int nEntry )
	{
		if ( !IsEntry( nEntry ) || m_Slots[nEntry].nDirty == 0 )
			return false;

		m_Slots[nEntry].nDirty = 0;
		return true;
	}

	int Opens( void ) const { return m_nOpens; }
	int Closes( void ) const { return m_nCloses; }

private:
	// Monotonic across the session, skipping the sentinel on wrap.
	uint32_t NextSerial( void )
	{
		++m_nNextSerial;

		if ( m_nNextSerial == (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE )
			++m_nNextSerial;

		return m_nNextSerial;
	}

	SourceSharpEntitySlot_t m_Slots[SOURCESHARP_MAX_ENTITY_SLOTS];
	uint32_t                m_Handles[SOURCESHARP_MAX_ENTITY_SLOTS];

	int      m_Active[SOURCESHARP_MAX_ENTITY_SLOTS];
	// Position in m_Active plus one, so zero means absent and a fresh block is all
	// zero bytes (.bss rather than 160 KB of initialised data).
	int      m_WhereOne[SOURCESHARP_MAX_ENTITY_SLOTS];
	int      m_nActive;

	uint32_t m_nNextSerial;
	int      m_nOpens;
	int      m_nCloses;
};
