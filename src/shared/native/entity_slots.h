//========= SourceSharp — the entity control block =============================//
//
// plan_nettable_generic.md Part 9: C# stops asking native for an entity's blob
// pointer on every property access and holds one instead. That trade is only
// safe if there is something cheap that says whether the pointer is still good,
// and something cheap that lets a write announce itself without a call. This is
// both, in eight bytes per entity:
//
//     nSerial   written by NATIVE when a slot is opened and when it is closed
//     nDirty    written by C# when it writes a field, drained by NATIVE per frame
//
// EACH WORD HAS EXACTLY ONE WRITER, and that is the whole of the safety
// argument for shared mutable state with no lock. Native never writes nDirty
// and C# never writes nSerial. Both run on the game thread — entity writes, the
// drain, and entity create/free are all game-thread work — so there is no
// cross-thread visibility question to answer. If managed work is ever moved off
// the game thread this block is the first thing that breaks, and it will break
// silently.
//
// WHY A SERIAL PER SLOT AND NOT A GLOBAL EPOCH. A global epoch invalidates every
// cached pointer whenever any entity dies, which on a map with a hundred physics
// props is constantly, and the optimisation evaporates. Per slot, a barrel dying
// costs the barrel's state object its pointer and nothing else.
//
// WHY NOT THE HANDLE'S OWN SERIAL, which is right there in the CBaseHandle. Two
// reasons. It is 16 bits and wraps; and it says the ENTITY is gone, which is not
// the same fact as "the blob a managed state was bound to is gone" — an entity
// whose managed side was reloaded has the same handle and a state object that
// must not carry on writing. A serial this file owns says exactly the thing the
// compare needs to mean.
//
// NO ENGINE HEADERS HERE, on purpose, and for the same reason generic_payload.h
// has none: the part that can be quietly wrong is the bookkeeping — a slot
// closed twice, a slot left in the active list, a serial reused — and every one
// of those is a dangling pointer in C# rather than an error anywhere. It is
// inline and tested without a running Source. See
// sourcesharp/test/entity_slots_test.cpp. The engine-facing half — resolving a
// handle, reading a blob, calling NetworkStateChanged — is in
// game/server/shell_entityslots.cpp.
//
//=============================================================================//
#ifndef SOURCESHARP_ENTITY_SLOTS_H
#define SOURCESHARP_ENTITY_SLOTS_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>
#include <string.h>

// SourceSharpEntitySlot_t itself, from the ABI header rather than restated
// here: C# maps the array and indexes it by stride, so the layout is the
// contract and two declarations of a contract is one too many. The host header
// has no Source dependency of its own, so including it keeps this file
// buildable with no engine.
#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// How many slots the block has. NUM_ENT_ENTRIES on the engine's side, restated
// here so this header needs no const.h; shell_entityslots.cpp static-asserts
// that the two agree, which is what stops this drifting.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_MAX_ENTITY_SLOTS = 8192 };

//-----------------------------------------------------------------------------
// A serial no live slot ever carries.
//
// Zero, and it is worth saying why rather than picking any sentinel: a closed
// slot is memset to zero, and an array that was never touched is zero as well.
// So "the slot behind this pointer was never opened" and "was opened and has
// since been closed" produce the same compare against any serial C# holds, and
// both are the answer the caller needs — the pointer is not good.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_SLOT_SERIAL_NONE = 0 };

//-----------------------------------------------------------------------------
// The block itself, plus the bookkeeping that makes the drain cheap.
//
// The drain must not walk 8192 slots looking for the handful that are dirty, so
// a compact list of open entries is kept beside them. Removal is a swap with the
// tail, which is why there is a second array saying where an entry sits in the
// first — an O(n) search on every entity free would be worse than the scan it is
// replacing.
//-----------------------------------------------------------------------------
class CSourceSharpEntitySlots
{
public:
	CSourceSharpEntitySlots() { Reset(); }

	//-------------------------------------------------------------------------
	// Everything back to closed, and the serial counter with it.
	//
	// Level change and test setup. NOT called between frames: a managed state
	// bound before a reset would find its slot closed, which is exactly what a
	// reset means and exactly what it must look like.
	//-------------------------------------------------------------------------
	void Reset( void )
	{
		memset( m_Slots, 0, sizeof( m_Slots ) );
		memset( m_Handles, 0, sizeof( m_Handles ) );

		for ( int i = 0; i < SOURCESHARP_MAX_ENTITY_SLOTS; ++i )
			m_Where[i] = -1;

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

	//-------------------------------------------------------------------------
	// Opens the slot for an entry and gives it a serial nothing else has.
	//
	// RE-OPENING FOR THE SAME ENTITY KEEPS THE SERIAL, and the comment that
	// used to be here said the opposite: that a fresh serial was "harmless for
	// the second state, which is handed the new number". It is not harmless,
	// and it was measured. A managed class with TWO [NetState] fields — which
	// is how one class serves two classnames, as func_dustmotes/func_dustcloud
	// and env_steam/env_steamjet both do — binds both to the same entity, and
	// the second bind invalidated the first. The FIRST state then threw
	// StaleEntityException on every write it attempted, with "expected serial
	// 33, control block says 34", from inside OnSpawn. The entity spawned, the
	// exception unwound the rest of its spawn, and nothing was networked.
	//
	// So the identity that matters is the HANDLE, not the entry. A different
	// handle in the same entry is slot reuse by a new entity and must get a
	// fresh serial — that is what makes every pointer the old entity's states
	// held report itself stale. The same handle is the same entity, and there
	// is nothing to invalidate.
	//
	// The dirty word follows the same rule, and for a stronger reason than
	// symmetry: clearing it on a same-entity rebind would DISCARD a pending
	// change. A state that had already been written and marked would be dropped
	// by the next state binding to the same entity, and the change would never
	// reach the wire. It is still cleared when the handle changes, because a
	// pending mark belongs to the entity that set it and that entity is gone.
	//
	// Returns false for an entry outside the block, which a real entity never
	// is: CBaseHandle::GetEntryIndex masks with ENT_ENTRY_MASK, which is 16 bits
	// wide while the entity list is 13, so a malformed handle can name a slot
	// that does not exist. Refusing is the only honest answer and the caller
	// reports it — the alternative is a write 224KB past the end of the block.
	//-------------------------------------------------------------------------
	bool Open( int nEntry, uint32_t hEntity, uint32_t *pOutSerial )
	{
		if ( pOutSerial )
			*pOutSerial = SOURCESHARP_SLOT_SERIAL_NONE;

		if ( !IsEntry( nEntry ) )
			return false;

		// Already open FOR THIS ENTITY: hand back what it already has. Counted
		// as an open, because it is one — the counter is how many binds native
		// served, not how many serials it burned.
		if ( m_Where[nEntry] >= 0 && m_Handles[nEntry] == hEntity )
		{
			++m_nOpens;

			if ( pOutSerial )
				*pOutSerial = m_Slots[nEntry].nSerial;

			return true;
		}

		if ( m_Where[nEntry] < 0 )
		{
			m_Where[nEntry] = m_nActive;
			m_Active[m_nActive++] = nEntry;
		}

		m_Slots[nEntry].nSerial = NextSerial();
		m_Slots[nEntry].nDirty  = 0;
		m_Handles[nEntry]       = hEntity;

		++m_nOpens;

		if ( pOutSerial )
			*pOutSerial = m_Slots[nEntry].nSerial;

		return true;
	}

	//-------------------------------------------------------------------------
	// Closes a slot, which is what makes every pointer C# holds into that
	// entity throw on its next access.
	//
	// Idempotent: an entity freed without ever having been bound closes a slot
	// that was never open, and that must be a no-op rather than a corruption of
	// the active list. The destructors that call this cannot know whether the
	// entity carried a managed state.
	//-------------------------------------------------------------------------
	void Close( int nEntry )
	{
		if ( !IsEntry( nEntry ) || m_Where[nEntry] < 0 )
			return;

		const int nAt = m_Where[nEntry];
		const int nTail = m_Active[--m_nActive];

		m_Active[nAt]   = nTail;
		m_Where[nTail]  = nAt;
		m_Where[nEntry] = -1;

		m_Slots[nEntry].nSerial = SOURCESHARP_SLOT_SERIAL_NONE;
		m_Slots[nEntry].nDirty  = 0;
		m_Handles[nEntry]       = 0;

		++m_nCloses;
	}

	//-------------------------------------------------------------------------
	// A MANAGED RELOAD HAPPENED: every serial handed out before it is void.
	// plan_nettable_generic.md Step 10.
	//
	// The header at the top of this file already says why the serial is this
	// block's and not the CBaseHandle's -- "an entity whose managed side was
	// reloaded has the same handle and a state object that must not carry on
	// writing". That was the reason the serial exists, and until there was a
	// reload path nothing ever made it true: a serial only ever moved when a
	// slot was OPENED for a different entity, so as built it answered entity
	// death and nothing else. This is the missing half.
	//
	// The slots STAY OPEN and the handles are untouched, so the new assembly's
	// Bind takes the "already open for this entity" path in Open above and is
	// handed the new number. Only a pointer that predates the reload compares
	// unequal, and every one of those belongs to an assembly that is gone.
	//
	// nDirty is deliberately left alone. It is C#'s word, a one in it means a
	// write really happened this frame, and the drain has not run yet -- so
	// clearing it here would silently drop the last change the old assembly
	// made. The write was real whoever made it.
	//
	// Walks the ACTIVE list rather than all 8192 slots, so the cost is the
	// number of entities carrying managed state and not the size of the block.
	//-------------------------------------------------------------------------
	int Retire( void )
	{
		for ( int i = 0; i < m_nActive; ++i )
			m_Slots[ m_Active[i] ].nSerial = NextSerial();

		return m_nActive;
	}

	bool IsOpen( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_Where[nEntry] >= 0;
	}

	uint32_t SerialAt( int nEntry ) const
	{
		return IsEntry( nEntry ) ? m_Slots[nEntry].nSerial : (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE;
	}

	uint32_t HandleAt( int nEntry ) const { return IsEntry( nEntry ) ? m_Handles[nEntry] : 0u; }

	// C# owns this word. Native reads it in the drain and clears it there, and
	// this is the only other place that writes it — for the tests, and for the
	// native poke command, which has to look like a managed write or it is
	// testing a different path.
	void MarkDirty( int nEntry )
	{
		if ( IsOpen( nEntry ) )
			m_Slots[nEntry].nDirty = 1;
	}

	bool IsDirty( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_Slots[nEntry].nDirty != 0;
	}

	//-------------------------------------------------------------------------
	// The open entries, for the drain to walk. Order is not stable across a
	// close — a swap with the tail moves one entry — and nothing may depend on
	// it, which is why the drain reads the dirty word rather than a position.
	//-------------------------------------------------------------------------
	int ActiveCount( void ) const { return m_nActive; }
	int ActiveAt( int i ) const { return ( i >= 0 && i < m_nActive ) ? m_Active[i] : -1; }

	//-------------------------------------------------------------------------
	// Takes the dirty word of an open entry and clears it in one step.
	//
	// One call rather than a read and a write, so a drain cannot read the flag,
	// act on it and forget to clear it — which would mark the entity every frame
	// for the rest of the map and look exactly like the dirty-on-read behaviour
	// this whole part exists to delete.
	//-------------------------------------------------------------------------
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
	//-------------------------------------------------------------------------
	// A serial nothing else holds, skipping the sentinel on wrap.
	//
	// 32 bits at one per bind: a map would have to bind four billion states to
	// wrap it, and the counter is monotonic across the whole session rather than
	// per slot, so a slot reused a thousand times still never repeats a number.
	//-------------------------------------------------------------------------
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
	int      m_Where[SOURCESHARP_MAX_ENTITY_SLOTS];
	int      m_nActive;

	uint32_t m_nNextSerial;
	int      m_nOpens;
	int      m_nCloses;
};

#endif // SOURCESHARP_ENTITY_SLOTS_H
