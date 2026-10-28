//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the slot table ======================================//
//
// plan_entity.md §3.3: "Identity is a handle; the slot table is the only native
// table". This is that table.
//
// WHAT IT IS. One array per side, indexed by a handle's entry index, holding
// { the concrete shell pointer, the serial that binding was given }. Four
// operations and no others: bind, unbind, resolve-by-handle (serial checked),
// resolve-by-index. It stores no entity — it stores the BINDING between an
// engine index and an object the engine already reaches through that index, and
// it exists so that turning an index or a handle back into the concrete type
// costs an array read instead of RTTI or a registry walk.
//
// WHAT IT IS NOT, AND THIS IS THE POINT OF THE FILE. It is not an entity list.
// It cannot iterate, it has no listeners, it knows no names, and it has no
// "find". Everything a list DOES — walk every entity, find by targetname, find
// by classname, notify on add and remove — lives in the managed EntityDirectory
// (§3.5). plan_entity.md's definition of done, clause 2, is that no native
// entity list survives the port; a table that could be walked would be one
// again inside a week, so the absence is checked rather than documented:
// slot_table_test.cpp compiles five ill-formed programs, each reaching for one
// spelling of iteration, and requires every one to be REFUSED BY THE COMPILER.
// See the block at the bottom of that file, and build_must_not_compile in
// src/buildcpptests.
//
// The honest boundary of that claim, said plainly rather than left to be
// discovered: a caller who already holds an index may ask about THAT index, and
// that is what ResolveByIndex is for — an edict index out of the engine, a
// handle entry out of a trace. What the table hands out is never a sequence: no
// begin/end, no First/Next, no ForEach, no ActiveCount/ActiveAt pair, and no
// pointer into its own storage. There is no way to learn WHICH entries are
// occupied without already knowing which entry to ask about, and nothing here
// will tell you the highest one. That last omission is deliberate and is
// discussed on BoundCount() below.
//
// NO ITERATION IS A BUILD-TIME PROPERTY, NOT A CONVENTION. Adding an ActiveAt()
// here to serve one caller turns four must-not-compile legs green, and green is
// the failure: build_must_not_compile reports a probe that COMPILED as an
// error, because a check that cannot fail is not a check.
//
// WHY void * AND NOT A TEMPLATE. A template over the shell type would store a
// typed pointer and the resolver would need no cast at all — which sounds
// strictly better and is not, for two reasons. The table is ONE object per
// side, reached by every shim TU on that side, and a template puts the shell's
// full class definition into every one of them: today that is shell_standalone.h
// and cbase.h, into files that §3.13 is trying to disconnect from both. And the
// two sides' shells are different types, so the shared declaration would be a
// template the shared code could never name. So the storage is type-erased and
// there is EXACTLY ONE place that un-erases it — the typed resolver below,
// which is the one downcast plan_entity.md permits on an entity path and the
// only entry in tools/entity-gates-allowlist.
//
// THE CAST IS SOUND ONLY BECAUSE OF WHAT WENT IN. static_cast from void * back
// to T * is well defined when, and only when, a T * is what was stored. That is
// not a comment anyone has to honour by hand: on each side the typed wrappers
// are the only route to Bind and the only route to Resolve, so the type that
// goes in and the type that comes out are the same type by construction. A
// caller cannot reach the void * layer without naming this class, and no shim
// TU does.
//
//=============================================================================
// SIZING, AND THE HALF OF THE RANGE THE ENGINE CANNOT SEE
//=============================================================================
//
// NUM_ENT_ENTRIES, not MAX_EDICTS. public/const.h:77-81 in THIS tree:
//
//     #define NUM_ENT_ENTRY_BITS  (MAX_EDICT_BITS + 2)   // 11 + 2
//     #define NUM_ENT_ENTRIES     (1 << NUM_ENT_ENTRY_BITS)
//     #define NUM_SERIAL_NUM_BITS 16
//
// so a handle addresses 8,192 entries with a 16-bit serial, while only the
// first 2,048 are edicts. Entries 2048..8191 are the ENGINE-INVISIBLE RANGE:
// plan_entity.md §3.3 gives them to entities that have no shell at all — a
// logic_relay, a filter, a math_counter, a point_template — so that an
// !activator or a targetname lookup works uniformly over things the engine
// packs and things it has never heard of. Sizing this array to MAX_EDICTS would
// make every one of those unaddressable, and the failure would be a silent NULL
// from a resolve rather than a refusal at bind. Both halves are here, both
// behave identically, and slot_table_test.cpp asserts that they do.
//
// THE COUNT IS NOT RESTATED HERE. It comes from this tree's const.h, through
// the array bound below, so there is no restated 8192 anywhere in this file to
// drift.
//
//=============================================================================
// SERIALS: WHY A REBIND MUST NOT RESURRECT AN OLD HANDLE
//=============================================================================
//
// A handle is entry + serial. The entry says where; the serial says WHICH
// OCCUPANT of that entry, and it is the only thing standing between a stale
// handle and the entity that took over its slot. Getting this wrong does not
// crash: it returns the wrong entity, confidently, to a caller that asked about
// a dead one.
//
// The rule is stock's, and it is BUMP ON UNBIND:
//
//     CBaseEntityList::RemoveEntityAtSlot  (game/shared/entitylist_base.cpp:251)
//         pInfo->m_SerialNumber = ( pInfo->m_SerialNumber + 1 ) & SERIAL_MASK;
//         // "Increment the serial # so ehandles go invalid."
//
// Bumping at unbind rather than at the next bind matters, and the difference is
// observable: it makes the old handle stale THE MOMENT THE ENTITY GOES, not the
// moment something else takes its entry. An entry that is merely empty already
// refuses the handle because there is no pointer in it; an entry that has been
// refilled refuses it because the serial moved. slot_table_test.cpp asserts
// both, separately, because a resolver that only checked the pointer would pass
// the first and hand back the new occupant on the second.
//
// SERIAL_MASK IS FIFTEEN BITS, not the sixteen the handle carries. Stock says
// so: entitylist_base.cpp:16 here computes ( 1 << ( NUM_SERIAL_NUM_BITS
// - 1 ) ) - 1, the max value of a serial number, which rolls back to 0 when it
// hits this limit. This table wraps where stock wraps. The spare top bit is
// what keeps every serial a legal argument to CBaseHandle::Init, whose assert
// admits < (1 << 16), and
// keeps a full serial clear of the 0xFFFF an invalid handle carries.
//
// SERIAL 0 IS A REAL SERIAL. It is the client world's, on every map load, and a
// resolver that treats handle-index-zero as "no entity" returns NULL for
// exactly one entity in the game and for no other — the defect
// entitylist_slot0_test.cpp exists to describe. So the guard here is
// CBaseHandle::IsValid(), which compares against INVALID_EHANDLE_INDEX
// (0xFFFFFFFF), and never a comparison against zero. It is not optional: an
// invalid handle's GetEntryIndex() answers NUM_ENT_ENTRIES-1 by design
// (basehandle.h:137-153 calls it "bug-submarining"), which is a real, bindable
// entry in this table — so an unguarded resolve of the invalid handle would
// address entry 8191 and answer with whatever lives there.
//
//=============================================================================
#pragma once

#include <string.h>

#include "basehandle.h"

//-----------------------------------------------------------------------------
// The serial wrap, taken from const.h rather than restated. See the header
// block: fifteen bits, agreed by two engine generations.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_SLOT_SERIAL_MASK = ( 1 << ( NUM_SERIAL_NUM_BITS - 1 ) ) - 1 };

//-----------------------------------------------------------------------------
// The table.
//
// Deliberately not copyable and deliberately not comparable: one per side, and
// the only reason to copy 80KB of bindings would be to walk the copy.
//-----------------------------------------------------------------------------
class CSourceSharpSlotTable
{
public:
	CSourceSharpSlotTable() { Reset(); }

	//-------------------------------------------------------------------------
	// Every binding gone, and every serial back to zero.
	//
	// LEVEL SHUTDOWN AND TEST SETUP ONLY, and it is the one operation here that
	// a handle held across it cannot survive: the serials go back to zero
	// rather than forward, so a handle from the previous map can compare EQUAL
	// to a binding on this one. That is safe only because nothing holds a
	// handle across a level change — every managed directory is rebuilt and
	// every shell is destroyed — and it is why this is not called between
	// frames. A per-map table that kept counting up would be the alternative;
	// it buys nothing, because the entities it would protect do not exist.
	//-------------------------------------------------------------------------
	void Reset( void )
	{
		memset( m_pShell, 0, sizeof( m_pShell ) );
		memset( m_nSerial, 0, sizeof( m_nSerial ) );

		m_nBound   = 0;
		m_nBinds   = 0;
		m_nUnbinds = 0;
		m_nRefused = 0;
	}

	//-------------------------------------------------------------------------
	// EVERY BINDING GONE, EVERY SERIAL KEPT. This is the LEVEL SHUTDOWN one;
	// Reset() above is the CONSTRUCTOR's and the tests'.
	//
	// THE DIFFERENCE IS THE ONLY THING THAT SURVIVES A LEVEL CHANGE, AND IT IS
	// NOT A HANDLE. Reset()'s own comment argues that rewinding the serials is
	// safe "because nothing holds a handle across a level change -- every
	// managed directory is rebuilt and every shell is destroyed". That was true
	// while CGlobalEntityList assigned the serials; it stopped being true the
	// moment this table did, because the EDICT holds one:
	// edict_t::m_NetworkSerialNumber is the mirror standalone_netserial.h
	// exists for, the engine keeps it across a map load, and SV_PackEntity
	// reuses the previous occupant's PackedEntity and ChangeFrameList whenever
	// the serial it snapshotted still matches.
	//
	// So a table that rewound to zero would hand map 2's entity at edict 5 the
	// SAME serial map 1's had, the mirror would not move, and the engine would
	// pack the new entity as a CONTINUATION of the dead one -- an abort when
	// their tiers' prop counts differ, a silently inherited delta baseline when
	// they do not. CGlobalEntityList never met this: it drew every serial once
	// at static construction and only ever bumped (entitylist_base.cpp:158),
	// so an edict's serial could never repeat.
	//
	// UNBIND IS WHAT DOES IT, one entry at a time, because Unbind is where the
	// bump lives and a second rule would be a second place to get it wrong. By
	// the time this runs every shell has usually already destroyed itself and
	// unbound its own entry -- the walk is what catches the ones that did not,
	// and Unbind is idempotent for the rest.
	//
	// THE WALK IS NOT AN ITERATION API. It hands out no sequence, no cursor and
	// no pointer; it is the same shape Reset()'s memset already is, over the
	// same storage, inside the class that owns it. Nothing outside can ask this
	// object which entries were occupied, before or after.
	//-------------------------------------------------------------------------
	void ClearBindings( void )
	{
		for ( int nEntry = 0; nEntry < NUM_ENT_ENTRIES; nEntry++ )
			Unbind( nEntry );
	}

	//-------------------------------------------------------------------------
	// Is this a real entry, and which half of the range is it in?
	//
	// IsEntry is the bound check every operation here opens with. It is not
	// belt and braces: CBaseHandle::GetEntryIndex masks with ENT_ENTRY_MASK,
	// which is SIXTEEN bits wide while this table is thirteen, so a malformed
	// handle can name an entry that does not exist. basehandle.h explains the
	// mask as bug-submarining that "a lot of code actually depends on"; the
	// only honest answer to an entry outside the array is a refusal, and the
	// alternative is a read 448KB past the end of it.
	//-------------------------------------------------------------------------
	static bool IsEntry( int nEntry )
	{
		return nEntry >= 0 && nEntry < NUM_ENT_ENTRIES;
	}

	// The engine can see an entry iff it is an edict. §3.3's split, and the
	// reason the table is 8192 long: everything at or above MAX_EDICTS is a
	// managed-only entity the engine will never pack, never trace against and
	// never be told about.
	static bool IsEdictEntry( int nEntry )
	{
		return nEntry >= 0 && nEntry < MAX_EDICTS;
	}

	static int EntryCount( void ) { return NUM_ENT_ENTRIES; }

	//-------------------------------------------------------------------------
	// BIND, with the table choosing the serial.
	//
	// Returns the handle the caller must now use for this shell, or an invalid
	// handle if the bind was refused. Three refusals, and each one is a state
	// that would otherwise be indistinguishable from success:
	//
	//   an entry outside the table   -- see IsEntry
	//   a null shell                 -- a bound entry holding NULL resolves the
	//                                   same as an unbound one, so "bound to
	//                                   nothing" is a state with no observable
	//                                   difference from a bind that never
	//                                   happened, and accepting it would let
	//                                   BoundCount drift from reality
	//   an entry already bound       -- an entry has ONE occupant. Stock asserts
	//                                   the same thing (entitylist_base.cpp:209,
	//                                   `Assert( pSlot->m_pEntity == NULL )`);
	//                                   here it is a refusal rather than an
	//                                   assert, because an assert compiles out
	//                                   of the shipping build and the silent
	//                                   overwrite it leaves behind would give
	//                                   two live entities the same handle.
	//
	// The serial is the entry's CURRENT one — the value Unbind left behind. It
	// is not advanced here; see the header block on why the bump belongs to
	// Unbind.
	//-------------------------------------------------------------------------
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

	//-------------------------------------------------------------------------
	// BIND at a handle somebody else assigned, adopting its serial.
	//
	// THE CLIENT CANNOT CHOOSE ITS OWN SERIALS and this is the arm that says
	// so. The server assigns a networked entity's handle and the client is told
	// what it is: stock's AddNetworkableEntity takes an iForcedSerialNum and
	// asserts it is only ever forced on the client (entitylist_base.cpp:213-221).
	// The two sides' handles must agree, because a handle is what an EHANDLE
	// prop puts on the wire, so on the client the serial is an input and not an
	// output. Phase 5a builds IClientEntityList on this call.
	//
	// It is also what lets the table stand beside the entity list during the
	// port rather than after it: while CGlobalEntityList still assigns server
	// handles, the shell binds the handle it was given instead of a second,
	// disagreeing one.
	//
	// Refuses an INVALID handle before anything else — see the header block:
	// GetEntryIndex() answers 8191 for it, which is a perfectly bindable entry,
	// so an unguarded BindAt would quietly bind the last slot in the table.
	//-------------------------------------------------------------------------
	bool BindAt( const CBaseHandle &handle, void *pShell )
	{
		const int nEntry = handle.IsValid() ? handle.GetEntryIndex() : -1;

		if ( !IsEntry( nEntry ) || !pShell || m_pShell[nEntry] )
		{
			++m_nRefused;
			return false;
		}

		// ADOPTED EXACTLY, AND NOT MASKED. Masking to SOURCESHARP_SLOT_SERIAL_MASK
		// here looks like symmetry with the wrap in Unbind and is a bug: a serial
		// above the mask would be stored as a different number than the handle
		// carries, and the entity would then be unreachable through the very
		// handle it was bound with -- a silent NULL from every resolve, for one
		// entity, with nothing to report it. The table's own wrap governs the
		// serials the table ISSUES; a serial it is HANDED is a fact about
		// somebody else's numbering and is stored as given. Sixteen bits is all a
		// CBaseHandle can carry and all m_nSerial holds, so nothing is lost.
		m_pShell[nEntry] = pShell;
		m_nSerial[nEntry] = (unsigned short)handle.GetSerialNumber();
		++m_nBound;
		++m_nBinds;

		return true;
	}

	//-------------------------------------------------------------------------
	// UNBIND, which is what makes every handle to this binding stale.
	//
	// Idempotent, and that is load-bearing rather than tidy: a shell is
	// destroyed on paths that cannot know whether it was ever bound (a
	// construction that failed halfway, a level shutdown that already reset the
	// table), and a second unbind that bumped the serial again would be a slow
	// leak of the serial space rather than a no-op.
	//
	// Takes the ENTRY and not the handle. A handle whose serial has already
	// moved is exactly the handle that must not be able to unbind the current
	// occupant, and offering an Unbind(handle) that silently did nothing for a
	// stale one would be a footgun with no diagnostic. The caller that has a
	// handle and means "unbind whatever is there" says so by passing
	// handle.GetEntryIndex(); the caller that means "unbind MY binding" checks
	// Resolve first.
	//-------------------------------------------------------------------------
	void Unbind( int nEntry )
	{
		if ( !IsEntry( nEntry ) || !m_pShell[nEntry] )
			return;

		m_pShell[nEntry] = NULL;
		m_nSerial[nEntry] = (unsigned short)( ( m_nSerial[nEntry] + 1 ) & SOURCESHARP_SLOT_SERIAL_MASK );

		--m_nBound;
		++m_nUnbinds;
	}

	//-------------------------------------------------------------------------
	// RESOLVE BY HANDLE. The serial is checked, and this is the check the whole
	// design rests on.
	//
	// THE ONE DOWNCAST plan_entity.md permits sits directly on top of this
	// function, in the typed resolver in the side's own TU, and it is
	// allowlisted in tools/entity-gates-allowlist by file and line. An
	// allowlist entry is a hole in gate 3; the only thing left guarding this
	// path is the serial-reuse test, which is why it is named in the allowlist
	// and why deleting the comparison below has to make that test go red. It
	// does — mutation-verified, and recorded in .claude/entity-port/status/1c.md.
	//
	// Three ways to miss, all answering NULL:
	//   the handle is INVALID       -- guarded first, or GetEntryIndex answers
	//                                  8191 and we resolve a real entry
	//   nothing is bound there      -- the entity died and nothing replaced it
	//   the serials disagree        -- the entity died and something DID
	//-------------------------------------------------------------------------
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

	//-------------------------------------------------------------------------
	// RESOLVE BY INDEX. No serial, because the caller has none.
	//
	// This is the edict-index route: the engine hands out an entity index in
	// IServerGameEnts, in a usercmd, in a user message, and on the client in
	// every IClientEntityList call the engine makes. There is no serial
	// anywhere in those calls to check, so this answers "whoever is bound at
	// that entry RIGHT NOW", which is the same thing the engine means by the
	// index.
	//
	// It is not iteration, and the difference is not a matter of taste: the
	// caller must already hold the index, and nothing here will produce one.
	// A loop over 0..N built on this is a caller inventing indices the table
	// never gave it, and the managed EntityDirectory (§3.5) is where that
	// question belongs.
	//-------------------------------------------------------------------------
	void *ResolveByIndex( int nEntry ) const
	{
		return IsEntry( nEntry ) ? m_pShell[nEntry] : NULL;
	}

	//-------------------------------------------------------------------------
	// The handle currently bound at an entry, or an invalid one.
	//
	// The inverse of ResolveByIndex, for the caller that has an index and needs
	// something it can hold: an index is only good until the entry is reused,
	// and a handle says which occupant it meant.
	//-------------------------------------------------------------------------
	CBaseHandle HandleOf( int nEntry ) const
	{
		if ( !IsEntry( nEntry ) || !m_pShell[nEntry] )
			return CBaseHandle();

		return CBaseHandle( nEntry, (int)m_nSerial[nEntry] );
	}

	//-------------------------------------------------------------------------
	// THE HANDLE A BIND AT THIS ENTRY WOULD PRODUCE, WITHOUT BINDING.
	//
	// HandleOf's complement, and the reason this exists rather than the caller
	// spelling CBaseHandle( nEntry, SerialAt( nEntry ) ) for itself: the
	// registration path needs the handle BEFORE it binds, because on this side
	// the shell's own SetRefEHandle is the single binder -- it unbinds the
	// entry the shell held, stores the new handle and binds it, in that order,
	// and a caller that had already bound would be REFUSED by BindAt for an
	// entry that is legitimately its own. So the issuer and the binder are two
	// steps, and this is the first of them.
	//
	// This is where CGlobalEntityList::AddEntityAtSlot's serial went. Stock
	// issues and binds in one call and then calls SetRefEHandle from INSIDE it
	// (entitylist_base.cpp:236), which is why the shim's
	// SetRefEHandle( <the entity list>->Add...( ... ) ) called SetRefEHandle twice
	// per entity: once from the list and once on the returned handle. Splitting
	// the two is what removes the second call rather than papering over it.
	//
	// AN OCCUPIED ENTRY ANSWERS AN INVALID HANDLE, not the occupant's. The
	// alternative -- handing back the live handle -- would let a second entity
	// take the first one's identity and would make Bind's "an entry has ONE
	// occupant" refusal arrive one step too late to name the caller.
	//
	// IT IS NOT ALLOCATION. It answers about the entry the caller names and
	// hands out no entry of its own; nothing here will tell you which entries
	// are free. Choosing the entry is the caller's, and on this side the answer
	// is always "the edict index the engine already gave the entity".
	//-------------------------------------------------------------------------
	CBaseHandle IssueHandle( int nEntry ) const
	{
		if ( !IsEntry( nEntry ) || m_pShell[nEntry] )
			return CBaseHandle();

		return CBaseHandle( nEntry, (int)m_nSerial[nEntry] );
	}

	//-------------------------------------------------------------------------
	// The serial an entry will hand to its NEXT binding.
	//
	// Exposed for one caller: the edict's m_NetworkSerialNumber mirror
	// (standalone_netserial.h). The engine keys "what did I last send for this
	// edict" on that field, and an edict that changes occupants without the
	// field moving is packed as a continuation of the dead entity — an abort
	// when the two entities' send tables differ in size, a silently inherited
	// delta baseline when they do not.
	//-------------------------------------------------------------------------
	int SerialAt( int nEntry ) const
	{
		return IsEntry( nEntry ) ? (int)m_nSerial[nEntry] : 0;
	}

	bool IsBound( int nEntry ) const
	{
		return IsEntry( nEntry ) && m_pShell[nEntry] != NULL;
	}

	//-------------------------------------------------------------------------
	// HOW MANY BINDINGS EXIST. A scalar, maintained by bind and unbind.
	//
	// A count is not an enumeration: it says how many, never which, and there
	// is no operation here that turns it into an index. §3.3 says the client's
	// IClientEntityList counts are "maintained by bind and unbind", and this is
	// the first of them.
	//
	// WHAT IS DELIBERATELY ABSENT IS GetHighestEntityIndex. Phase 5a needs it —
	// the engine calls it — and it is not here
	// because it is the one scalar that WOULD turn ResolveByIndex into a walk:
	// a caller holding the highest occupied entry can loop down to zero and has
	// iterated the table without ever naming an iteration API. It belongs with
	// the interface that needs it, maintained by bind and unbind there, and not
	// in the shared table where every shim TU in Phase 1 could reach it.
	//-------------------------------------------------------------------------
	int BoundCount( void ) const { return m_nBound; }

	// Cumulative, for the console dump. Not state anything reads back.
	int Binds( void ) const   { return m_nBinds; }
	int Unbinds( void ) const { return m_nUnbinds; }
	int Refused( void ) const { return m_nRefused; }

private:
	CSourceSharpSlotTable( const CSourceSharpSlotTable & );
	CSourceSharpSlotTable &operator=( const CSourceSharpSlotTable & );

	// TWO PARALLEL ARRAYS AND NOT AN ARRAY OF PAIRS, for one reason: a struct
	// would be a thing to hand out. `Slots()` returning a pointer to the first
	// of them is the shortest path from "the table cannot iterate" to a caller
	// walking it, and it is exactly the shape entity_slots.h has for its own
	// (different) block, where a drain genuinely does need the list. Here there
	// is nothing to hand out and no type to hand it out as.
	void          *m_pShell[NUM_ENT_ENTRIES];
	unsigned short m_nSerial[NUM_ENT_ENTRIES];

	int m_nBound;
	int m_nBinds;
	int m_nUnbinds;
	int m_nRefused;
};

//-----------------------------------------------------------------------------
// THE SIDE'S TABLE, AND THE TYPED WRAPPERS THAT ARE THE ONLY WAY IN.
//
// One table per side, owned by the side's game-system object rather than by
// this header: the definitions live in the side's own TU (the server's are in
// shell_entityslots.cpp; Phase 5a adds the client's), because the shell type
// they cast to is the side's and because plan.md §1a forbids file-scope state.
//
// Declared HERE rather than in a per-side header so that the shim TUs of steps
// 1d, 1e and 1h include one file to get the whole of §3.3.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL

class CStandaloneEntity;
struct edict_t;

CSourceSharpSlotTable &SourceSharp_SlotTable( void );


// THE HANDLE A SHELL AT THIS ENTRY WILL CARRY, ISSUED WITHOUT BINDING.
//
// This is what replaced CGlobalEntityList::AddNetworkableEntity on the
// registration path (plan_entity.md §3.3, lane entlist-server). The shell's own
// SetRefEHandle is this side's single binder, so registration is two steps --
// ask for the handle, then store it, and the store is what binds. An invalid
// handle back means the entry was occupied or out of range; see
// CSourceSharpSlotTable::IssueHandle for the two refusals.
CBaseHandle SourceSharp_SlotIssue( int nEntry );

//-----------------------------------------------------------------------------
// THE WORLD'S SERIAL, WHICH IS NOT THE TABLE'S -- and this is the rule, not the
// counter.
//
// worldspawn is the one entity §3.3's table does not hold: it is a CWorld, not
// a shell, and a second type behind the same void * would make the resolver's
// single static_cast answer about whichever object was there
// (world_subject.h). So the world carries its own handle and
// SourceSharp_WorldFromHandle compares against it -- which leaves exactly one
// thing the list used to contribute and this replaces: the serial.
//
// It obeys THIS table's wrap, because it has to interoperate with everything
// the table issues: a handle is a handle, CBaseHandle::Init asserts the serial
// is under 1 << 16, and the top bit stays clear of the 0xFFFF an invalid handle
// carries. Declared here rather than beside CWorld for exactly that reason --
// one wrap, stated once.
//
// AND IT NEVER ANSWERS ZERO. The world is entry 0, so a serial of 0 packs the
// whole handle to 0 -- which is the INVALID_EHANDLE ENUMERATOR (basehandle.h:25,
// value zero, not INVALID_EHANDLE_INDEX). Two readers have compared against it:
// CBaseEntityList::LookupEntity, fixed in this tree with
// entitylist_slot0_test.cpp standing over it, and any future one written the
// same way. Stock never met it because CBaseEntityList seeds every entry's
// serial with rand() (entitylist_base.cpp:158) and 0 for entry 0 is one draw in
// 32,768; a counter that starts at zero would meet it on the FIRST map, every
// time. The skip costs one comparison per map load and removes the whole class.
//
// A PURE FUNCTION OF THE PREVIOUS SERIAL, so it can be tested without a world:
// the storage is CWorld's (world_adapter.cpp) and the rule is here.
//-----------------------------------------------------------------------------
inline unsigned short SourceSharp_NextWorldSerial( unsigned short nPrevious )
{
	const unsigned short nNext =
		(unsigned short)( ( nPrevious + 1 ) & SOURCESHARP_SLOT_SERIAL_MASK );

	return nNext ? nNext : (unsigned short)1;
}

// Bind at a handle assigned elsewhere (CGlobalEntityList, during the port).
bool SourceSharp_SlotBindAt( const CBaseHandle &handle, CStandaloneEntity *pShell,
                             edict_t *pEdict );

void SourceSharp_SlotUnbind( int nEntry );

// THE RESOLVERS. The first of these carries the one permitted downcast.
CStandaloneEntity *SourceSharp_ShellFromHandle( const CBaseHandle &handle );
CStandaloneEntity *SourceSharp_ShellFromIndex( int nEntry );
CStandaloneEntity *SourceSharp_ShellFromHandleEntity( IHandleEntity *pHandleEntity );

//-----------------------------------------------------------------------------
// THE UNION RESOLVE: a handle to whatever holds it, as the face both
// hierarchies share.
//
// The resolvers above answer for THIS hierarchy and NULL for everything else,
// which is right for a caller that wants a shell and wrong for one that wants
// "the entity", whichever kind it is -- a trace filter comparing pointers, a
// player's ground entity, a damage target. Those callers spelled
// the entity list's LookupEntity( handle ), and that spelling is what this
// replaces: five sites in four files, each of which had to know that the
// entity list was the union of the two hierarchies.
//
// THREE ARMS, ASKED IN THIS ORDER, AND ONLY ONE OF THEM IS A LIST:
//
//   the slot table    -- every shell in this mod, serial checked
//   the world         -- one object at a fixed index, matched on its own
//                        handle. It is not a shell and is not in the table;
//                        world_subject.h has why the world is its own arm
//                        everywhere else too.
//   the SDK's list    -- CGlobalEntityList, for the entities of the hierarchy
//                        this port has not deleted yet. THE ONE PLACE THE SHIM
//                        STILL NAMES IT ON A RESOLUTION PATH, and it leaves
//                        with game/server/entitylist.cpp.
//
// A handle resolves in at most one of them, because the three populations are
// disjoint by construction: a shell binds its edict entry in the table and
// nowhere else, the world owns entry 0, and CBaseEntity::PostConstructor is
// what puts an SDK entity in the SDK's list.
//-----------------------------------------------------------------------------
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle );

#endif // !CLIENT_DLL

#ifdef CLIENT_DLL

class C_StandaloneEntity;

//-----------------------------------------------------------------------------
// THE CLIENT'S TABLE -- plan_entity.md Phase 5a.
//
// Same class, same rules, a different side's shell type and one fewer bind arm.
// The client has NO SourceSharp_ClientSlotBind( entry, ... ): §3.3 says the
// client cannot choose its own serials, and BindAt's own comment says why --
// the server assigns a networked entity's handle, the engine hands this side
// the forced serial with the create call, and a handle is what an EHANDLE prop
// puts on the wire. A Bind() over here would issue a SECOND, disagreeing serial
// for the same entity, and the disagreement would only show up as an EHANDLE
// that resolves on one side and not the other. So the arm that could do it is
// not declared, and the omission is the check.
//
// THE TABLE IS DEFINED IN shell_cliententitylist.cpp, beside the nine
// IClientEntityList methods that answer out of it, because on this side the
// table IS that interface's storage rather than something standing beside it.
//-----------------------------------------------------------------------------
CSourceSharpSlotTable &SourceSharp_ClientSlotTable( void );

// Bind at the handle the SERVER assigned -- entry from the create call, serial
// forced by the engine. False if the entry was already bound or the handle is
// invalid; see CSourceSharpSlotTable::BindAt for the three refusals.
bool SourceSharp_ClientSlotBindAt( const CBaseHandle &handle, C_StandaloneEntity *pShell );

void SourceSharp_ClientSlotUnbind( int nEntry );

// THE RESOLVERS. The first carries the client's one permitted downcast -- the
// SECOND entry in tools/entity-gates-allowlist, and the reason the file's
// header no longer says "exactly one". The other two go through it rather than
// casting again, exactly as the server's do.
C_StandaloneEntity *SourceSharp_ClientShellFromHandle( const CBaseHandle &handle );
C_StandaloneEntity *SourceSharp_ClientShellFromIndex( int nEntry );
C_StandaloneEntity *SourceSharp_ClientShellFromHandleEntity( IHandleEntity *pHandleEntity );

// A trace's m_pEnt to the shell it hit, or NULL: never a static prop, and only
// the object at that address (handle_cross.h's SourceSharp_CrossHandleToClientShell).
C_StandaloneEntity *SourceSharp_ClientShellFromTraceHit( IHandleEntity *pHandleEntity );

#endif // CLIENT_DLL
