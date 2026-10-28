//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IClientEntityList's four scalars ===================//
//
// plan_entity.md Phase 5a. §3.3, on the client half of the slot table: "the
// nine methods in public/icliententitylist.h are index and handle lookups plus
// counts, and the counts are maintained by bind and unbind". The lookups are
// the table's. THIS IS THE COUNTS.
//
// WHY IT IS A SEPARATE HEADER AND NOT FOUR MEMBERS OF THE INTERFACE CLASS.
// Because the interface class lives behind cbase.h, and a fact that can only be
// checked inside a running game is a fact nobody checks. Three of these four
// scalars have a rule with an edge that is easy to get wrong and impossible to
// see wrong:
//
//   - GetHighestEntityIndex bounds a loop over the ENGINE's own tables (0e:
//     1,184 calls, every one from engine.so). Too small truncates the engine's
//     walk and entities silently stop being drawn.
//   - Removing the highest entity is the ONLY event that lowers it, and it can
//     lower it by any amount, so it cannot be decremented -- it has to be
//     recomputed.
//   - The engine-invisible range (entries 2048..8191, §3.3) must NOT count
//     towards it. Answering 8191 would send the engine off the end of an array
//     sized MAX_EDICTS.
//
// So the policy is here, header-only and engine-free -- basehandle.h and
// const.h and nothing else, through slot_table.h -- and
// client_slot_table_test.cpp drives THE REAL CLASS the client links, not a
// stand-in for it (`fixture-must-be-checked-against-native`).
//
// IT HOLDS NO ENTITY AND NO POINTER. Four ints and a reference to the table it
// asks about. It cannot iterate and it cannot resolve; every question it can
// answer is a number.
//
//===========================================================================//

#pragma once

#include "slot_table.h"

class CClientEntityCounts
{
public:
	//-------------------------------------------------------------------------
	// The table is a REFERENCE and not a copy: RecomputeHighest asks it which
	// entries are still bound, and a copy would answer about the world as it
	// was when the copy was taken.
	//-------------------------------------------------------------------------
	explicit CClientEntityCounts( const CSourceSharpSlotTable &table )
		: m_Table( table ), m_nMaxServerEnts( 0 ), m_nHighestUsed( -1 ),
		  m_nNetworkedBound( 0 ), m_nNonNetworkedBound( 0 )
	{
	}

	//-------------------------------------------------------------------------
	// A bind succeeded at this entry. Called from the ONE bind path, after the
	// table has accepted -- never from a refused bind, or the counters and the
	// table part company and nothing says which is right.
	//-------------------------------------------------------------------------
	void OnBound( int nEntry )
	{
		if ( !CSourceSharpSlotTable::IsEntry( nEntry ) )
			return;

		if ( CSourceSharpSlotTable::IsEdictEntry( nEntry ) )
		{
			++m_nNetworkedBound;

			if ( nEntry > m_nHighestUsed )
				m_nHighestUsed = nEntry;
		}
		else
		{
			// The engine-invisible range. Counted, because
			// NumberOfEntities( true ) asks about it -- and deliberately NOT
			// part of the highest index, which bounds an engine walk that stops
			// at MAX_EDICTS.
			++m_nNonNetworkedBound;
		}
	}

	//-------------------------------------------------------------------------
	// A binding was released. Call AFTER the table's Unbind, never before:
	// RecomputeHighest reads the table, and an entry that is still bound when
	// it looks makes the entity that just died the highest one for ever.
	//-------------------------------------------------------------------------
	void OnUnbound( int nEntry )
	{
		if ( !CSourceSharpSlotTable::IsEntry( nEntry ) )
			return;

		if ( CSourceSharpSlotTable::IsEdictEntry( nEntry ) )
		{
			--m_nNetworkedBound;

			// >= and not ==, so that an unbind of an entry that had somehow
			// been left above the recorded high-water mark still recomputes
			// rather than leaving a number nothing can bring down.
			if ( nEntry >= m_nHighestUsed )
				RecomputeHighest();
		}
		else
		{
			--m_nNonNetworkedBound;
		}
	}

	//-------------------------------------------------------------------------
	// -1 when nothing is bound, which is stock's answer for an empty list
	// (CClientEntityList::m_iMaxUsedServerIndex starts at -1) and is the
	// engine's own "no entities" answer from this getter.
	//-------------------------------------------------------------------------
	int HighestEntityIndex( void ) const { return m_nHighestUsed; }

	int NumberOfEntities( bool bIncludeNonNetworkable ) const
	{
		return bIncludeNonNetworkable
		     ? m_nNetworkedBound + m_nNonNetworkedBound
		     : m_nNetworkedBound;
	}

	void SetMaxEntities( int nMaxEnts ) { m_nMaxServerEnts = nMaxEnts; }
	int  MaxEntities( void ) const      { return m_nMaxServerEnts; }

private:
	//-------------------------------------------------------------------------
	// The walk down. Stock did the same thing for the same reason
	// (CClientEntityList::RecomputeHighestEntityUsed).
	//
	// THIS IS NOT THE ITERATION slot_table.h FORBIDS. Every index here was
	// INVENTED from a compile-time bound; the table produced none of them and
	// has no operation that could. What clause 2 forbids is the table handing
	// out a SEQUENCE -- a First/Next, an ActiveAt, a pointer into its storage --
	// because that is what lets a caller reach entities it did not already know
	// how to name. Asking "is entry 2047 bound?" is a question this class could
	// ask one at a time; asking it 2,048 times learns nothing new about the
	// table's interface.
	//-------------------------------------------------------------------------
	void RecomputeHighest( void )
	{
		m_nHighestUsed = -1;

		for ( int i = MAX_EDICTS - 1; i >= 0; i-- )
		{
			if ( m_Table.IsBound( i ) )
			{
				m_nHighestUsed = i;
				break;
			}
		}
	}

	CClientEntityCounts( const CClientEntityCounts & );
	CClientEntityCounts &operator=( const CClientEntityCounts & );

	const CSourceSharpSlotTable &m_Table;

	int m_nMaxServerEnts;
	int m_nHighestUsed;
	int m_nNetworkedBound;
	int m_nNonNetworkedBound;
};
