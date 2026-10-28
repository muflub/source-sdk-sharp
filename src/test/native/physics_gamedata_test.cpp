//========= SourceSharp ========================================================//
//
// physics_gamedata_test.cpp -- plan_entity.md Phase 1 step 1h, "physics game
// data is the handle" (§3.3), and the two properties the step's gate names:
//
//   1. A STALE HANDLE IN GAME DATA RESOLVES TO NULL. VPhysics holds an object's
//      game data for the whole life of the object, and physics.cpp's own
//      deferred event drain means an object outlives its entity by design --
//      the callbacks fire mid-solve and are answered after PostSimulationFrame.
//      So "the entity died and something else took its entry" is not a corner
//      case, it is the window this design exists to close. The old handle must
//      answer NOBODY, and above all must not answer with the new occupant.
//
//   2. THE +1 AND THE -1 ARE A PAIR, AND ZERO IS THE BOUNDARY. Game data of
//      NULL means "no owner" to every reader in the tree. A handle of zero --
//      entry 0, serial 0 -- is a REAL handle. Storing the packed handle raw
//      would give exactly one entity the same game data as nobody, and the
//      failure would be a silent skip. So the stored word is the handle plus
//      one, and these facts pin both directions of that add.
//
// A THIRD PROPERTY IS TESTED HERE THAT THE GATE DOES NOT NAME, because without
// it property 1 is not observable: A STALE HANDLE IS STILL READ AS A HANDLE.
// During Phases 1-4 a game-data word holds EITHER a packed handle or a real
// CBaseEntity pointer (the SDK still creates physics objects for prop_ragdoll,
// gib and the NPC solvers), so PhysicsOwnerOf has to know which arm a word
// belongs to. If a stale handle stopped looking like a handle, PhysicsOwnerOf
// would fall through to `static_cast<CBaseEntity *>( pGameData )` and hand back
// the handle reinterpreted as an entity pointer -- which is worse than
// returning the new occupant, not better. So the discriminator is checked
// separately from the resolve.
//
// ENGINE-FREE, exactly as slot_table_test.cpp is and for the same reason:
// physics_gamedata.h is arithmetic over a CBaseHandle and the slot table is an
// array, and neither takes an engine. The source-scanning facts at the bottom
// read the tree with <fstream>, which is how entity_gates_test.cpp and
// cpp_alias_lane1_test.cpp read theirs.
//
//=============================================================================

#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

#include <fstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "basehandle.h"
#include "ihandleentity.h"

#include "../game/shared/physics_gamedata.h"
#include "../game/shared/slot_table.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for the shell, borrowed from slot_table_test.cpp for its reason:
// the table stores a void * and never dereferences it, so all that matters
// about this type is that two instances have two addresses.
//-----------------------------------------------------------------------------
struct FakeShell
{
	int nWhich;
};

// The word behind a game-data pointer, so a fact can talk about the arithmetic
// rather than about the pointer.
uintptr_t WordOf( const void *pGameData )
{
	return (uintptr_t)pGameData;
}

// The tag stripped off, which is the half the bias lives in.
uintptr_t PayloadOf( const void *pGameData )
{
	return WordOf( pGameData ) & ~SOURCESHARP_PHYSICS_GAMEDATA_TAG;
}

//-----------------------------------------------------------------------------
// The handles worth trying, and why each one is here.
//
// Derived from const.h rather than typed in: the table is NUM_ENT_ENTRIES long
// and the serial wraps at SOURCESHARP_SLOT_SERIAL_MASK, so a literal 8191 or
// 32767 here would stop tracking the tree the day either moves.
//-----------------------------------------------------------------------------
struct Sample_t
{
	int         nEntry;
	int         nSerial;
	const char *pszWhy;
};

const Sample_t kSamples[] = {
	{ 0,                    0,                             "entry 0 serial 0 -- packs to the integer ZERO, which is the whole reason for the bias" },
	{ 0,                    1,                             "entry 0, a serial that is not" },
	{ 1,                    0,                             "a serial of zero on a non-zero entry" },
	{ MAX_EDICTS - 1,       7,                             "the last edict" },
	{ MAX_EDICTS,           7,                             "the first entry the engine cannot see (§3.3)" },
	{ NUM_ENT_ENTRIES - 1,  0,                             "the last entry in the table" },
	{ NUM_ENT_ENTRIES - 1,  SOURCESHARP_SLOT_SERIAL_MASK,  "the largest word a valid handle can pack to" },
};

const size_t kNumSamples = sizeof( kSamples ) / sizeof( kSamples[ 0 ] );

//-----------------------------------------------------------------------------
// Reading the tree, for the facts that are about what a FILE says rather than
// about what a function does. Same shape as cpp_alias_lane1_test.cpp's.
//-----------------------------------------------------------------------------
} // namespace


//=============================================================================
// THE BOUNDARY: PLUS ONE, AND WHY IT IS NOT DECORATION
//=============================================================================

//-----------------------------------------------------------------------------
// The fact the whole bias exists for. Handle (0,0) packs to the integer zero;
// a raw store would make its game data indistinguishable from "no owner".
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, AHandleOfZeroIsNotTheNullGameData )
{
	const CBaseHandle zero( 0, 0 );

	ASSERT_TRUE( zero.IsValid() );
	ASSERT_EQ( 0, zero.ToInt() ) << "the premise of this fact -- (0,0) must pack to zero";

	EXPECT_TRUE( PhysicsGameDataFromHandle( zero ) != NULL )
		<< "handle (0,0) stored as game data came out as NULL, which every "
		   "reader in the tree reads as 'no owner'";

	// AND THE PAYLOAD, NOT JUST THE WORD. The tag bit alone makes the word
	// non-NULL today, so the line above would still pass with the bias deleted
	// -- and would go on passing right up to the day Phase 4 removes the tag,
	// at which point exactly one entity per map would go silently unowned. The
	// bias has to carry this on its own, so the fact asks the half the bias
	// lives in.
	EXPECT_NE( (uintptr_t)0, PayloadOf( PhysicsGameDataFromHandle( zero ) ) )
		<< "with the tag stripped, handle (0,0)'s game data is the NULL that "
		   "means 'no owner' -- the +1 is what stops that and it is missing";
}

//-----------------------------------------------------------------------------
// The other end of the same boundary, and it is a different question: NULL is
// not a handle at all, so it decodes to nothing rather than to handle -1.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, TheNullGameDataIsNobodyRatherThanAHandle )
{
	EXPECT_EQ( PHYSICS_GAMEDATA_NONE, PhysicsGameDataKindOf( NULL ) );
	EXPECT_FALSE( PhysicsGameDataIsHandle( NULL ) );
	EXPECT_FALSE( PhysicsHandleFromGameData( NULL ).IsValid() );
}

//-----------------------------------------------------------------------------
// The bias is EXACTLY one, pinned as arithmetic rather than as a round trip.
//
// A round trip alone cannot see this: adding two and subtracting two round
// trips just as well, and so does adding nothing at all for every handle except
// (0,0). This fact reads the stored word and requires handle + 1.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, TheStoredWordIsThePackedHandlePlusExactlyOne )
{
	for ( size_t i = 0; i < kNumSamples; i++ )
	{
		const CBaseHandle handle( kSamples[ i ].nEntry, kSamples[ i ].nSerial );
		const uintptr_t   expected = (uintptr_t)(uint32)handle.ToInt() + 1;

		EXPECT_EQ( expected, PayloadOf( PhysicsGameDataFromHandle( handle ) ) )
			<< kSamples[ i ].pszWhy;
	}
}

//-----------------------------------------------------------------------------
// And the decode subtracts the same one. Built by hand rather than by calling
// the encoder, so that a bias that moved in BOTH directions together -- which a
// round-trip test cannot see -- is still red here.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, TheDecodeSubtractsExactlyOneFromAHandWrittenWord )
{
	for ( size_t i = 0; i < kNumSamples; i++ )
	{
		const CBaseHandle handle( kSamples[ i ].nEntry, kSamples[ i ].nSerial );

		const uintptr_t word = ( (uintptr_t)(uint32)handle.ToInt() + 1 )
		                     | SOURCESHARP_PHYSICS_GAMEDATA_TAG;

		const CBaseHandle back = PhysicsHandleFromGameData( (void *)word );

		EXPECT_EQ( kSamples[ i ].nEntry,  back.GetEntryIndex() )  << kSamples[ i ].pszWhy;
		EXPECT_EQ( kSamples[ i ].nSerial, back.GetSerialNumber() ) << kSamples[ i ].pszWhy;
	}
}

//-----------------------------------------------------------------------------
// The round trip itself, over the whole range a handle can occupy.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, EveryHandleComesBackAsItself )
{
	for ( size_t i = 0; i < kNumSamples; i++ )
	{
		const CBaseHandle handle( kSamples[ i ].nEntry, kSamples[ i ].nSerial );
		const CBaseHandle back = PhysicsHandleFromGameData( PhysicsGameDataFromHandle( handle ) );

		EXPECT_TRUE( back == handle ) << kSamples[ i ].pszWhy;
	}
}

//-----------------------------------------------------------------------------
// An entity with no handle stores nothing.
//
// Not a shortcut: ToInt() answers INVALID_EHANDLE_INDEX for an invalid handle,
// and that plus the bias is an ordinary tagged word that would decode back to a
// handle whose GetEntryIndex() is NUM_ENT_ENTRIES-1 by basehandle.h's own
// bug-submarining -- a real, bindable entry in the table.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataBias, AnEntityWithNoHandleStoresNoOwnerAndNotEntry8191 )
{
	CBaseHandle invalid;
	ASSERT_FALSE( invalid.IsValid() );

	EXPECT_TRUE( PhysicsGameDataFromHandle( invalid ) == NULL );
	EXPECT_EQ( PHYSICS_GAMEDATA_NONE, PhysicsGameDataKindOf( PhysicsGameDataFromHandle( invalid ) ) );
}


//=============================================================================
// THE DISCRIMINATOR: WHICH ARM OF PhysicsOwnerOf A WORD BELONGS TO
//=============================================================================

//-----------------------------------------------------------------------------
// Every handle this tree can pack reads back as a HANDLE, not as a pointer.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataKind, AStoredHandleReadsAsAHandle )
{
	for ( size_t i = 0; i < kNumSamples; i++ )
	{
		const CBaseHandle handle( kSamples[ i ].nEntry, kSamples[ i ].nSerial );
		void *pGameData = PhysicsGameDataFromHandle( handle );

		EXPECT_EQ( PHYSICS_GAMEDATA_HANDLE, PhysicsGameDataKindOf( pGameData ) )
			<< kSamples[ i ].pszWhy;
		EXPECT_TRUE( PhysicsGameDataIsHandle( pGameData ) ) << kSamples[ i ].pszWhy;
	}
}

//-----------------------------------------------------------------------------
// AND A REAL ALLOCATION NEVER DOES. This is the fact that says a range check
// would have been a guess and a tag is not: the addresses here come from the
// allocator this build actually uses, on the heap, on the stack and in static
// storage, and not one of them may carry the tag bit.
//
// Sixty-four allocations rather than one, because a single `new` proves very
// little about where an allocator puts things.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataKind, ARealEntityPointerNeverReadsAsAHandle )
{
	static FakeShell s_static = { 0 };
	FakeShell onStack = { 0 };

	EXPECT_EQ( PHYSICS_GAMEDATA_POINTER, PhysicsGameDataKindOf( &s_static ) )
		<< "a static-storage address carried the handle tag";
	EXPECT_EQ( PHYSICS_GAMEDATA_POINTER, PhysicsGameDataKindOf( &onStack ) )
		<< "a stack address carried the handle tag";

	std::vector< FakeShell * > allocated;
	for ( int i = 0; i < 64; i++ )
		allocated.push_back( new FakeShell );

	for ( size_t i = 0; i < allocated.size(); i++ )
	{
		EXPECT_EQ( PHYSICS_GAMEDATA_POINTER, PhysicsGameDataKindOf( allocated[ i ] ) )
			<< "heap allocation " << i << " at " << (void *)allocated[ i ]
			<< " carried the handle tag -- the discriminator is not exact";
		EXPECT_FALSE( PhysicsHandleFromGameData( allocated[ i ] ).IsValid() )
			<< "a heap pointer decoded to a handle";
	}

	for ( size_t i = 0; i < allocated.size(); i++ )
		delete allocated[ i ];
}

//-----------------------------------------------------------------------------
// The tag is the top bit of a pointer-sized word, which is what makes the claim
// above an ABI fact rather than an observation: x86-64 user space is the
// canonical LOW half, so bit 63 is never set in a user pointer.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataKind, TheTagIsTheTopBitOfAPointerSizedWord )
{
	EXPECT_EQ( (uintptr_t)1 << ( sizeof( void * ) * 8 - 1 ),
	           SOURCESHARP_PHYSICS_GAMEDATA_TAG );

	const CBaseHandle handle( 3, 4 );
	EXPECT_TRUE( ( WordOf( PhysicsGameDataFromHandle( handle ) )
	               & SOURCESHARP_PHYSICS_GAMEDATA_TAG ) != 0 );
}


//=============================================================================
// THE HEART OF THE STEP: A STALE HANDLE IN GAME DATA
//=============================================================================

//-----------------------------------------------------------------------------
// The control, so that the failures below are failures and not silence: a live
// handle in game data answers with its own shell.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, ALiveHandleInGameDataAnswersWithItsOwnShell )
{
	CSourceSharpSlotTable table;
	FakeShell crate = { 1 };

	const CBaseHandle handle = table.Bind( 100, &crate );
	ASSERT_TRUE( handle.IsValid() );

	void *pGameData = PhysicsGameDataFromHandle( handle );

	EXPECT_EQ( &crate, table.Resolve( PhysicsHandleFromGameData( pGameData ) ) );
}

//-----------------------------------------------------------------------------
// THE GATE. Bind an entity, put its handle in game data, unbind it, rebind the
// entry to a DIFFERENT entity, and the object's game data must resolve to NULL
// rather than to the new occupant.
//
// The object is not destroyed in between, deliberately: that is the real shape
// of the bug. VPhysics keeps the game data for the life of the OBJECT, and
// physics.cpp queues its callbacks and drains them after PostSimulationFrame,
// so an object whose entity is already gone is a state the solver produces on
// its own every time something is removed mid-step.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, AStaleHandleInGameDataResolvesToNullAndNotToTheNewOccupant )
{
	CSourceSharpSlotTable table;
	FakeShell dead = { 1 }, alive = { 2 };

	// The crate is created, simulated, and its object is handed its owner.
	const CBaseHandle hDead = table.Bind( 100, &dead );
	ASSERT_TRUE( hDead.IsValid() );

	void *pGameData = PhysicsGameDataFromHandle( hDead );
	ASSERT_EQ( &dead, table.Resolve( PhysicsHandleFromGameData( pGameData ) ) );

	// The crate is removed and something else takes its entry. The object --
	// and therefore the word in its game data -- has not moved.
	table.Unbind( 100 );
	const CBaseHandle hAlive = table.Bind( 100, &alive );
	ASSERT_TRUE( hAlive.IsValid() );
	ASSERT_EQ( &alive, table.Resolve( hAlive ) );

	void *pResolved = table.Resolve( PhysicsHandleFromGameData( pGameData ) );

	EXPECT_TRUE( pResolved == NULL )
		<< "a stale handle in game data resolved to something";
	EXPECT_NE( &alive, pResolved )
		<< "a stale handle in game data resolved to the entry's NEW occupant -- "
		   "a VPhysics callback about a dead entity would be delivered to a live one";
}

//-----------------------------------------------------------------------------
// THE SAME PROPERTY WITH THE ENTRY LEFT EMPTY, which is a different code path
// and is why it is a separate fact: a resolver that checked only the pointer
// passes this one and fails the one above.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, AHandleWhoseEntryIsNowEmptyResolvesToNull )
{
	CSourceSharpSlotTable table;
	FakeShell dead = { 1 };

	const CBaseHandle hDead = table.Bind( 101, &dead );
	void *pGameData = PhysicsGameDataFromHandle( hDead );

	table.Unbind( 101 );

	EXPECT_TRUE( table.Resolve( PhysicsHandleFromGameData( pGameData ) ) == NULL );
}

//-----------------------------------------------------------------------------
// AND A STALE HANDLE IS STILL A HANDLE.
//
// Without this, the fact above is not observable through PhysicsOwnerOf. A word
// that stopped reading as a handle would fall through to the SDK arm and be
// static_cast to CBaseEntity * -- so "resolves to null" would become "resolves
// to the packed handle wearing an entity's clothes", which is worse than the
// new occupant, not better.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, AStaleHandleIsStillReadAsAHandleAndNeverAsAPointer )
{
	CSourceSharpSlotTable table;
	FakeShell dead = { 1 }, alive = { 2 };

	const CBaseHandle hDead = table.Bind( 100, &dead );
	void *pGameData = PhysicsGameDataFromHandle( hDead );

	table.Unbind( 100 );
	table.Bind( 100, &alive );

	EXPECT_EQ( PHYSICS_GAMEDATA_HANDLE, PhysicsGameDataKindOf( pGameData ) )
		<< "a stale handle stopped looking like a handle, so PhysicsOwnerOf "
		   "would cast it to CBaseEntity * instead of answering 'no owner'";
}

//-----------------------------------------------------------------------------
// The stale rule holds in the engine-invisible half of the range too (§3.3),
// where the entities that have no shell at all live. Same table, same
// arithmetic, and a fact rather than an assumption because the halves are only
// identical while nothing special-cases MAX_EDICTS.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, TheStaleRuleHoldsAboveTheEdictRange )
{
	CSourceSharpSlotTable table;
	FakeShell dead = { 1 }, alive = { 2 };

	const CBaseHandle hDead = table.Bind( MAX_EDICTS + 17, &dead );
	ASSERT_TRUE( hDead.IsValid() );

	void *pGameData = PhysicsGameDataFromHandle( hDead );

	table.Unbind( MAX_EDICTS + 17 );
	table.Bind( MAX_EDICTS + 17, &alive );

	void *pResolved = table.Resolve( PhysicsHandleFromGameData( pGameData ) );
	EXPECT_TRUE( pResolved == NULL );
	EXPECT_NE( &alive, pResolved );
}

//-----------------------------------------------------------------------------
// Two live objects do not answer for each other. The trivial property, and the
// one that would be silently lost if the game data ever stopped carrying the
// entry as well as the serial.
//-----------------------------------------------------------------------------
TEST( PhysicsGameDataStale, TwoObjectsGameDataAnswerWithTwoDifferentShells )
{
	CSourceSharpSlotTable table;
	FakeShell first = { 1 }, second = { 2 };

	void *pFirst  = PhysicsGameDataFromHandle( table.Bind( 100, &first ) );
	void *pSecond = PhysicsGameDataFromHandle( table.Bind( 101, &second ) );

	ASSERT_TRUE( pFirst != NULL && pSecond != NULL );
	EXPECT_NE( pFirst, pSecond );

	EXPECT_EQ( &first,  table.Resolve( PhysicsHandleFromGameData( pFirst ) ) );
	EXPECT_EQ( &second, table.Resolve( PhysicsHandleFromGameData( pSecond ) ) );
}


//=============================================================================
// THE REGISTRY IS GONE, AND THE SHIM READS NO GAME DATA
//
// Source facts, because what they assert is the ABSENCE of code and no
// assertion about a symbol that does not exist can fail. Same mechanism, and
// the same reason, as entity_gates_test.cpp's allowlist facts.
//=============================================================================

//-----------------------------------------------------------------------------
// The scanner opened the right files. Two lanes have shipped a source-scanning
// test that was reading nothing; assert the reads before asserting on them.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE FILE-SCOPE REGISTRY IS DELETED. plan_entity.md step 1h says so in as many
// words, and plan.md §1a says nothing is a file-scope global; this is the fact
// that notices a replacement one appearing under a new name in the same place.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// And what replaced it: the object carries its owner's handle. Written by
// managed since the entity physics init moved (2026-10-12):
// EntityPhysicsCreation sets objectparams_t::pGameData to
// PhysicsGameData.FromHandle, physics_gamedata.h's encoding
// (EntityPhysicsCreationTests'
// TheObjectCarriesTheParsedParamsTheHandleAsGameDataAndTheClassnameAsItsName
// holds the value). The native shell makes no object any more and only holds
// one: the REMOVAL half is that no create is left in it, the RETENTION half
// that the hold and its teardown, which clears the game data, are.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE TWO LOOKUPS DO NOT READ GAME DATA AND DO NOT CAST.
//
// This is §3.16 family 4's own shape: the decode is arithmetic in a header, the
// resolvers take a CBaseHandle, and the only thing that turns a handle into an
// entity is the slot table's one allowlisted downcast. A resolver that took the
// object again would put a GetGameData call back into a shim TU, which is what
// the step's census gate measures.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// NO SERVER SHIM FILE THIS STEP OWNS READS GAME DATA ANY MORE.
//
// The step's gate is the census row, which counts the spelling over every
// compiled shim TU. This is the same claim for the three files the step owns,
// held here so that a regression is a red test in the native leg rather than a
// census diff nobody re-took.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// plan_physics_bones_native_zero 2.4: SourceSharp_EntitySetSolidToWorld was
// SourceSharp_EntitySetNotSolid inverted, so func_physbox's "notsolid" goes
// through the generic call (PhysboxNotSolidTests) and the body left. Since
// plan_server_native_v2 T4d the generic call is managed (EngineSide's
// EntitySetNotSolid over the solidity getters and EntitySetSolidity), so the
// native half that must stay is the one solidity writer.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SHELL BINDS ITSELF INTO THE SLOT TABLE, WHICH IS WHAT MAKES 1h LIVE.
//
// Step 1h's whole answer -- "the object carries its owner's handle and the slot
// table turns it back into the shell" -- is worth nothing if nothing is bound:
// every resolve answers NULL, every managed prop's object is unowned, and
// physics.cpp skips all of it. That is not a theory. The live gate caught it
// exactly once, with the bind absent: the crate fell 0.0 units under gravity,
// and with the bind it falls 133.
//
// The two calls are written out in .claude/entity-port/requests/1c.md §2a-2b
// and addressed to steps 1d and 1e; 1h landed them because 1h is the step that
// makes them load-bearing. This fact is here so that a merge which resolves
// shell_standalone_base.cpp in 1e's favour cannot quietly drop them.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// NOTHING ON THIS SIDE STILL CASTS A RAW GAME-DATA WORD TO AN ENTITY.
//
// The regression this fact is for, stated plainly because it is a crash and not
// a tidiness point: VPhysics hands game data straight to the collision
// callbacks as a `void *pGameData`, and game/server/physics.cpp's
// ShouldSolvePenetration used to open with
//
//     CBaseEntity *pEntity0 = static_cast<CBaseEntity *>(pGameData0);
//
// then guard on `if ( !pEntity0 || !pEntity1 ) return true;`. That guard was
// what kept an entity on the parallel hierarchy out of the SDK's penetration
// rules, and it worked only because such an object's game data was NULL. Once
// the object carries its owner's handle there the word is not NULL, the guard
// passes, and the very next line asks a packed handle for its collision group.
//
// physics.cpp is the ONE compiled server file VPhysics hands game data to, and
// every other read in it already goes through PhysicsOwnerOf -- so this is the
// whole of the claim, and it is a source fact because the callback cannot be
// reached without a solver.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// PhysicsOwnerOf'S TAGGED ARM RETURNS RATHER THAN FALLING THROUGH.
//
// The one line in physicsowner.h that the resolve tests above cannot reach
// without an engine, and the one whose absence would turn "a stale handle
// answers nobody" into "a stale handle answers as a CBaseEntity *".
//-----------------------------------------------------------------------------
