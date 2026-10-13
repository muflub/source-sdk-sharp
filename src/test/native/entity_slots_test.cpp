//========= SourceSharp — the entity control block ============================//
//
// plan_nettable_generic.md Part 9, Step 5.
//
// The bookkeeping under a pointer C# holds for the life of an entity. Every way
// it can be wrong is a dangling pointer rather than an error anywhere: a slot
// closed twice corrupts the list the drain walks, a slot left open after its
// entity is freed lets a managed write land in memory the allocator has already
// reused, and a serial that repeats makes a stale state look live. None of those
// produces a message; they produce a value that is wrong or a crash somewhere
// else entirely. So the arithmetic lives in a header with no engine in it and is
// checked here.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/entity_slots.h"

#include <new>
#include <vector>

namespace
{

// A handle whose entry index is what we asked for.
//
// The serial half is not what the BLOCK numbers its slots by — it keeps its own,
// for the reason written in entity_slots.h — but Open does compare the whole
// handle, to tell "the same entity binding a second state" from "a new entity in
// a reused entry". So two handles differing only in this half are two entities.
uint32_t HandleFor( int nEntry, int nSerial = 3 )
{
	return (uint32_t)( nEntry | ( nSerial << 16 ) );
}

TEST( EntitySlots, StartsClosed )
{
	CSourceSharpEntitySlots slots;

	EXPECT_EQ( 0, slots.ActiveCount() );
	EXPECT_FALSE( slots.IsOpen( 0 ) );
	EXPECT_EQ( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, slots.SerialAt( 0 ) );
}

//-----------------------------------------------------------------------------
// The sentinel. A closed slot and a slot never opened must compare the same
// against any serial C# holds, because both mean the same thing: the pointer is
// not good.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ClosedLooksExactlyLikeNeverOpened )
{
	CSourceSharpEntitySlots slots;

	uint32_t nSerial = 0;
	ASSERT_TRUE( slots.Open( 12, HandleFor( 12 ), &nSerial ) );
	EXPECT_NE( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, nSerial );

	slots.Close( 12 );

	EXPECT_EQ( slots.SerialAt( 12 ), slots.SerialAt( 13 ) );
	EXPECT_NE( nSerial, slots.SerialAt( 12 ) );
}

//-----------------------------------------------------------------------------
// A serial nothing else holds, and specifically not one a reused slot has held
// before. This is the fact the whole compare rests on: if a slot could be closed
// and reopened at the same number, a state bound to the dead entity would find
// its serial matching and write into the new one's blob.
//-----------------------------------------------------------------------------
TEST( EntitySlots, SerialsAreNeverReused )
{
	CSourceSharpEntitySlots slots;

	uint32_t nFirst = 0, nSecond = 0, nOther = 0;

	slots.Open( 5, HandleFor( 5 ), &nFirst );
	slots.Close( 5 );
	slots.Open( 5, HandleFor( 5 ), &nSecond );
	slots.Open( 6, HandleFor( 6 ), &nOther );

	EXPECT_NE( nFirst, nSecond );
	EXPECT_NE( nSecond, nOther );
	EXPECT_NE( nFirst, nOther );
}

//-----------------------------------------------------------------------------
// Re-opening a slot that is already open is the normal case — a second state
// binding to the same entity, or one rebinding after a managed reload — and it
// must invalidate whatever held the old number rather than hand out the same
// one.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ReopeningForTheSameEntityKeepsTheSerial )
{
	CSourceSharpEntitySlots slots;

	uint32_t nOld = 0, nNew = 0;
	slots.Open( 9, HandleFor( 9 ), &nOld );
	slots.Open( 9, HandleFor( 9 ), &nNew );

	// THE CASE THAT WAS PINNED THE OTHER WAY UP. A managed class serving two
	// classnames has two [NetState] fields and binds both to one entity; when the
	// second bind burned a new serial, the first state threw on every write it
	// ever attempted -- from inside OnSpawn, so the rest of the spawn was
	// unwound and nothing reached the wire. Same entity, same serial.
	EXPECT_EQ( nOld, nNew );
	EXPECT_EQ( nNew, slots.SerialAt( 9 ) );

	// And it stays ONE entry in the list, not two: the drain walks that list, so
	// a duplicate would mark the same entity twice a frame forever.
	EXPECT_EQ( 1, slots.ActiveCount() );
}

//-----------------------------------------------------------------------------
// The half of re-opening that MUST still invalidate: a different entity in the
// same entry.
//
// That is slot reuse, and the stale-serial check exists for exactly it -- every
// pointer the old entity's states held has to start reporting itself stale. The
// discriminator is the HANDLE rather than the entry, because a handle carries a
// serial of its own and two entities never share one.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ReopeningForADifferentEntityInvalidatesTheOldBinding )
{
	CSourceSharpEntitySlots slots;

	uint32_t nOld = 0, nNew = 0;
	slots.Open( 9, HandleFor( 9 ), &nOld );
	slots.Open( 9, HandleFor( 9, 4 ), &nNew );

	EXPECT_NE( nOld, nNew );
	EXPECT_EQ( nNew, slots.SerialAt( 9 ) );
	EXPECT_EQ( 1, slots.ActiveCount() );
}

//-----------------------------------------------------------------------------
// A pending mark belongs to the binding that set it. Carrying it across a rebind
// would announce a change to an entity that may not be the entity that was
// written.
//-----------------------------------------------------------------------------
TEST( EntitySlots, OpeningForANewEntityClearsAPendingDirty )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 4, HandleFor( 4 ), NULL );
	slots.MarkDirty( 4 );
	ASSERT_TRUE( slots.IsDirty( 4 ) );

	slots.Open( 4, HandleFor( 4, 4 ), NULL );
	EXPECT_FALSE( slots.IsDirty( 4 ) );
}

//-----------------------------------------------------------------------------
// And the same entity KEEPS it, which is the stronger half.
//
// Clearing on a same-entity rebind does not merely lose an announcement: the
// change it announced was already written into the blob, so the entity would sit
// there holding state the wire never heard about. A class with two [NetState]
// fields writes through the first and binds the second in the same breath.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ReopeningForTheSameEntityKeepsAPendingDirty )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 4, HandleFor( 4 ), NULL );
	slots.MarkDirty( 4 );
	ASSERT_TRUE( slots.IsDirty( 4 ) );

	slots.Open( 4, HandleFor( 4 ), NULL );
	EXPECT_TRUE( slots.IsDirty( 4 ) );
}

//-----------------------------------------------------------------------------
// Closing a slot nobody opened is what happens on every entity that carries no
// managed state, which is most of them. It must be a no-op rather than a
// corruption of the list.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ClosingIsIdempotentAndSafeWhenNeverOpened )
{
	CSourceSharpEntitySlots slots;

	slots.Close( 100 );
	EXPECT_EQ( 0, slots.ActiveCount() );

	slots.Open( 100, HandleFor( 100 ), NULL );
	slots.Close( 100 );
	slots.Close( 100 );

	EXPECT_EQ( 0, slots.ActiveCount() );
	EXPECT_FALSE( slots.IsOpen( 100 ) );
}

//-----------------------------------------------------------------------------
// The active list survives removal from the middle, which is the case the
// swap-with-the-tail exists for and the one an ordinary erase would get wrong.
//-----------------------------------------------------------------------------
TEST( EntitySlots, TheActiveListSurvivesRemovalFromTheMiddle )
{
	CSourceSharpEntitySlots slots;

	for ( int i = 0; i < 5; ++i )
		slots.Open( i, HandleFor( i ), NULL );

	slots.Close( 2 );

	ASSERT_EQ( 4, slots.ActiveCount() );

	bool bSeen[5] = { false, false, false, false, false };

	for ( int i = 0; i < slots.ActiveCount(); ++i )
	{
		const int nEntry = slots.ActiveAt( i );
		ASSERT_GE( nEntry, 0 );
		ASSERT_LT( nEntry, 5 );
		EXPECT_FALSE( bSeen[nEntry] ) << "entry " << nEntry << " listed twice";
		bSeen[nEntry] = true;
	}

	EXPECT_TRUE( bSeen[0] && bSeen[1] && bSeen[3] && bSeen[4] );
	EXPECT_FALSE( bSeen[2] );
}

//-----------------------------------------------------------------------------
// Closing every entry in the order the drain walks them — backwards, because
// Close swaps the tail into the freed position and forwards would skip whatever
// landed there.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ClosingEveryEntryBackwardsEmptiesTheList )
{
	CSourceSharpEntitySlots slots;

	for ( int i = 0; i < 64; ++i )
		slots.Open( i * 3, HandleFor( i * 3 ), NULL );

	ASSERT_EQ( 64, slots.ActiveCount() );

	for ( int i = slots.ActiveCount() - 1; i >= 0; --i )
		slots.Close( slots.ActiveAt( i ) );

	EXPECT_EQ( 0, slots.ActiveCount() );
}

//-----------------------------------------------------------------------------
// Take-and-clear in one step. A drain that read the flag and forgot to clear it
// would mark its entity every frame for the rest of the map, which looks exactly
// like the dirty-on-read behaviour this step deletes.
//-----------------------------------------------------------------------------
TEST( EntitySlots, TakeDirtyClearsWhatItReports )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 7, HandleFor( 7 ), NULL );

	EXPECT_FALSE( slots.TakeDirty( 7 ) );

	slots.MarkDirty( 7 );
	EXPECT_TRUE( slots.TakeDirty( 7 ) );
	EXPECT_FALSE( slots.TakeDirty( 7 ) );
	EXPECT_FALSE( slots.IsDirty( 7 ) );
}

//-----------------------------------------------------------------------------
// Two drains in one frame, which is what the server does now: once in
// FrameUpdatePostEntityThink and once in CGameRules::EndGameFrame, so that a
// property written by the dev API's queue or by ServiceEventQueue — both of
// which run after every game system — is announced in the frame it was written
// rather than the next one.
//
// The second pass must find NOTHING when the first already drained the frame.
// If TakeDirty ever stopped clearing, the pair would mark every bound entity
// twice a frame forever and the doubled cost would look like a network problem
// rather than a drain problem.
//-----------------------------------------------------------------------------
static int DrainOnce( CSourceSharpEntitySlots &slots )
{
	int nTaken = 0;

	for ( int i = slots.ActiveCount() - 1; i >= 0; --i )
	{
		if ( slots.TakeDirty( slots.ActiveAt( i ) ) )
			++nTaken;
	}

	return nTaken;
}

TEST( EntitySlots, ASecondDrainInTheSameFrameFindsNothing )
{
	CSourceSharpEntitySlots slots;

	for ( int nEntry = 3; nEntry <= 5; ++nEntry )
	{
		slots.Open( nEntry, HandleFor( nEntry ), NULL );
		slots.MarkDirty( nEntry );
	}

	EXPECT_EQ( 3, DrainOnce( slots ) );
	EXPECT_EQ( 0, DrainOnce( slots ) );
	EXPECT_EQ( 0, DrainOnce( slots ) );
}

//-----------------------------------------------------------------------------
// And the case the second drain exists for: a write that lands BETWEEN the two.
// The late drain is the only thing that sees it, and seeing it is what keeps the
// edict marked for the snapshot this frame rather than the next.
//-----------------------------------------------------------------------------
TEST( EntitySlots, AWriteBetweenTheTwoDrainsIsCaughtByTheLateOne )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 11, HandleFor( 11 ), NULL );
	slots.MarkDirty( 11 );

	EXPECT_EQ( 1, DrainOnce( slots ) );

	// The dev API's queue, or an ent_fire: after the early drain, before the
	// snapshot.
	slots.MarkDirty( 11 );

	EXPECT_EQ( 1, DrainOnce( slots ) );
	EXPECT_FALSE( slots.IsDirty( 11 ) );
}

TEST( EntitySlots, MarkingAClosedSlotDoesNothing )
{
	CSourceSharpEntitySlots slots;

	slots.MarkDirty( 20 );
	EXPECT_FALSE( slots.IsDirty( 20 ) );

	slots.Open( 20, HandleFor( 20 ), NULL );
	slots.Close( 20 );
	slots.MarkDirty( 20 );
	EXPECT_FALSE( slots.IsDirty( 20 ) );
}

//-----------------------------------------------------------------------------
// The bound the header exists to enforce. CBaseHandle::GetEntryIndex masks with
// ENT_ENTRY_MASK, which is sixteen bits wide while the entity list is thirteen,
// so a malformed handle really can name a slot that does not exist. Refusing is
// the only honest answer; the alternative is a write far past the end of a
// static array.
//-----------------------------------------------------------------------------
TEST( EntitySlots, AnEntryOutsideTheBlockIsRefusedRatherThanWrapped )
{
	CSourceSharpEntitySlots slots;

	uint32_t nSerial = 12345;

	EXPECT_FALSE( slots.Open( SOURCESHARP_MAX_ENTITY_SLOTS, HandleFor( 0 ), &nSerial ) );
	EXPECT_EQ( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, nSerial );

	EXPECT_FALSE( slots.Open( 65535, HandleFor( 0 ), &nSerial ) );
	EXPECT_FALSE( slots.Open( -1, HandleFor( 0 ), &nSerial ) );

	EXPECT_EQ( 0, slots.ActiveCount() );
	EXPECT_FALSE( slots.IsOpen( SOURCESHARP_MAX_ENTITY_SLOTS ) );
}

TEST( EntitySlots, TheLastEntryInTheBlockIsUsable )
{
	CSourceSharpEntitySlots slots;

	uint32_t nSerial = 0;
	EXPECT_TRUE( slots.Open( SOURCESHARP_MAX_ENTITY_SLOTS - 1,
	                         HandleFor( SOURCESHARP_MAX_ENTITY_SLOTS - 1 ), &nSerial ) );
	EXPECT_NE( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, nSerial );
}

//-----------------------------------------------------------------------------
// The handle is what the drain resolves to find the entity to mark, so the block
// has to give back the one it was opened with rather than one derived from the
// entry.
//-----------------------------------------------------------------------------
TEST( EntitySlots, TheHandleComesBackAsItWentIn )
{
	CSourceSharpEntitySlots slots;

	const uint32_t hEntity = HandleFor( 31, 999 );
	slots.Open( 31, hEntity, NULL );

	EXPECT_EQ( hEntity, slots.HandleAt( 31 ) );

	slots.Close( 31 );
	EXPECT_EQ( 0u, slots.HandleAt( 31 ) );
}

//-----------------------------------------------------------------------------
// Reset is level shutdown. A slot that outlived the map would be handed to
// whatever takes its entry next, and the state holding it would carry on
// writing.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ResetClosesEverything )
{
	CSourceSharpEntitySlots slots;

	uint32_t nBefore = 0;
	slots.Open( 1, HandleFor( 1 ), &nBefore );
	slots.Open( 2, HandleFor( 2 ), NULL );
	slots.MarkDirty( 1 );

	slots.Reset();

	EXPECT_EQ( 0, slots.ActiveCount() );
	EXPECT_FALSE( slots.IsDirty( 1 ) );
	EXPECT_EQ( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, slots.SerialAt( 1 ) );
}

//-----------------------------------------------------------------------------
// The server's block is a static, and static storage is zero before its
// constructor runs. A constructor that leaves every byte zero lets the compiler
// emit the 160 KB object as .bss; one that writes anything else (the -1 that
// used to mean "not in the active list") made MSVC emit all of it as
// initialised data in server.dll. So: built over zeroed storage, as the static
// is, it leaves zero bytes, and a slot opened and closed again reads as never
// opened.
//-----------------------------------------------------------------------------
TEST( EntitySlots, ConstructionLeavesEveryByteZero )
{
	std::vector< unsigned char > storage( sizeof( CSourceSharpEntitySlots ), 0 );
	CSourceSharpEntitySlots *pSlots = new ( storage.data() ) CSourceSharpEntitySlots;
	size_t nNonZero = 0;
	for ( unsigned char b : storage )
		nNonZero += b != 0;
	EXPECT_EQ( 0u, nNonZero ) << "of " << storage.size() << " bytes";

	EXPECT_FALSE( pSlots->IsOpen( 0 ) );
	EXPECT_FALSE( pSlots->IsOpen( SOURCESHARP_MAX_ENTITY_SLOTS - 1 ) );
	ASSERT_TRUE( pSlots->Open( 0, HandleFor( 0 ), NULL ) );
	EXPECT_TRUE( pSlots->IsOpen( 0 ) );
	pSlots->Close( 0 );
	EXPECT_FALSE( pSlots->IsOpen( 0 ) );
	EXPECT_EQ( 0, pSlots->ActiveCount() );
	pSlots->~CSourceSharpEntitySlots();
}

//-----------------------------------------------------------------------------
// The layout IS the ABI: C# holds a pointer to one of these and indexes it by
// stride. A field added on one side and not the other reads the next entity's
// serial, which is a compare that passes when it should fail.
//-----------------------------------------------------------------------------
TEST( EntitySlots, TheSlotIsTwoWordsAndNothingElse )
{
	EXPECT_EQ( 8u, (unsigned)sizeof( SourceSharpEntitySlot_t ) );
	EXPECT_EQ( 0u, (unsigned)offsetof( SourceSharpEntitySlot_t, nSerial ) );
	EXPECT_EQ( 4u, (unsigned)offsetof( SourceSharpEntitySlot_t, nDirty ) );
}

//-----------------------------------------------------------------------------
// A MANAGED RELOAD VOIDS EVERY SERIAL AND CLOSES NOTHING.
// plan_nettable_generic.md Step 10.
//
// The distinction is the whole of what this call is for. Closing the slots
// would be wrong twice over: a closed slot reads serial 0, which is the answer
// "this entity is gone" and the entity is not gone; and the new assembly's Bind
// would then have to re-open, which numbers the slot again and is a second way
// to arrive at the same place with more moving parts.
//
// So the slot stays open, the handle is untouched, and only the number moves.
// A pointer held from before the reload compares unequal; a Bind made after it
// takes Open's "already open for this entity" path and is handed the new one.
//-----------------------------------------------------------------------------
TEST( EntitySlots, RetireVoidsEverySerialWithoutClosingASlot )
{
	CSourceSharpEntitySlots slots;

	uint32_t nA = 0, nB = 0;
	slots.Open( 10, HandleFor( 10 ), &nA );
	slots.Open( 20, HandleFor( 20 ), &nB );

	EXPECT_EQ( 2, slots.Retire() );

	// Still open, still the same entities.
	EXPECT_TRUE( slots.IsOpen( 10 ) );
	EXPECT_TRUE( slots.IsOpen( 20 ) );
	EXPECT_EQ( HandleFor( 10 ), slots.HandleAt( 10 ) );

	// And the serials a state was holding are no longer the serials the block
	// answers with, which is the compare in NetState.Blob going red.
	EXPECT_NE( nA, slots.SerialAt( 10 ) );
	EXPECT_NE( nB, slots.SerialAt( 20 ) );

	// Never the closed sentinel: "retired" and "gone" are different facts and
	// the exception message says which.
	EXPECT_NE( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, slots.SerialAt( 10 ) );
	EXPECT_NE( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, slots.SerialAt( 20 ) );

	// The rebind the new assembly makes gets the current number, so it works
	// where the old one now throws.
	uint32_t nRebound = 0;
	EXPECT_TRUE( slots.Open( 10, HandleFor( 10 ), &nRebound ) );
	EXPECT_EQ( slots.SerialAt( 10 ), nRebound );
}

//-----------------------------------------------------------------------------
// A CLOSED SLOT IS NOT RE-OPENED BY A RETIRE, and an entity that died during
// the frame the reload happened in must stay dead.
//-----------------------------------------------------------------------------
TEST( EntitySlots, RetireLeavesAClosedSlotClosed )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 10, HandleFor( 10 ), NULL );
	slots.Open( 20, HandleFor( 20 ), NULL );
	slots.Close( 20 );

	EXPECT_EQ( 1, slots.Retire() );

	EXPECT_FALSE( slots.IsOpen( 20 ) );
	EXPECT_EQ( (uint32_t)SOURCESHARP_SLOT_SERIAL_NONE, slots.SerialAt( 20 ) );
}

//-----------------------------------------------------------------------------
// A PENDING WRITE SURVIVES THE RELOAD.
//
// nDirty is C#'s word and a one in it means a property really was written this
// frame. The drain has not run yet at the moment a reload command executes, so
// clearing the word here would silently drop the last change the outgoing
// assembly made — a value correct in the blob, never announced, and nothing
// anywhere saying so. The write was real whoever made it.
//-----------------------------------------------------------------------------
TEST( EntitySlots, RetireDoesNotDiscardAPendingDirtyWord )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 10, HandleFor( 10 ), NULL );
	slots.MarkDirty( 10 );

	slots.Retire();

	EXPECT_TRUE( slots.IsDirty( 10 ) );
	EXPECT_TRUE( slots.TakeDirty( 10 ) );
}

//-----------------------------------------------------------------------------
// Retiring twice is not the same as retiring once, and it must not be: two
// reloads are two invalidations, and a state bound between them holds the
// number from the first.
//-----------------------------------------------------------------------------
TEST( EntitySlots, EveryRetireIsItsOwnInvalidation )
{
	CSourceSharpEntitySlots slots;

	slots.Open( 10, HandleFor( 10 ), NULL );

	slots.Retire();
	const uint32_t nAfterFirst = slots.SerialAt( 10 );

	slots.Retire();

	EXPECT_NE( nAfterFirst, slots.SerialAt( 10 ) );
}

//-----------------------------------------------------------------------------
// Nothing open, nothing to retire — and the cost is the number of entities
// carrying state, not the size of the block.
//-----------------------------------------------------------------------------
TEST( EntitySlots, RetiringAnEmptyBlockIsANoOp )
{
	CSourceSharpEntitySlots slots;

	EXPECT_EQ( 0, slots.Retire() );
}

//-----------------------------------------------------------------------------
// The slots C# is handed are the block's own, at the entry it was told. A
// pointer off by one slot reads the neighbour's serial, and the neighbour is
// usually closed — so the symptom would be a state that throws for no reason
// rather than one that corrupts, which is the better of the two failures and
// still worth not having.
//-----------------------------------------------------------------------------
TEST( EntitySlots, TheSlotPointerAddressesTheEntryItNames )
{
	CSourceSharpEntitySlots slots;

	uint32_t nSerial = 0;
	slots.Open( 40, HandleFor( 40 ), &nSerial );

	SourceSharpEntitySlot_t *pSlot = slots.Slots() + 40;

	EXPECT_EQ( nSerial, pSlot->nSerial );
	EXPECT_EQ( 0u, pSlot->nDirty );

	// And a store through it is what the block reads back, which is the C# half
	// of the contract: C# writes nDirty and nothing else.
	pSlot->nDirty = 1;
	EXPECT_TRUE( slots.IsDirty( 40 ) );
	EXPECT_TRUE( slots.TakeDirty( 40 ) );
	EXPECT_EQ( 0u, pSlot->nDirty );
}

} // namespace
