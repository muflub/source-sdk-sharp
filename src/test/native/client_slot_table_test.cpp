//========= SourceSharp ========================================================//
//
// client_slot_table_test.cpp -- the CLIENT arm of plan_entity.md §3.3, and the
// three properties step 5a's gate rests on.
//
//   1. THE SERIAL-REUSE TEST, CLIENT SIDE. The client's resolver
//      (SourceSharp_ClientShellFromHandle, shell_cliententitylist.cpp) carries
//      the SECOND entry in tools/entity-gates-allowlist, and an allowlist entry
//      is a hole in gate 3. It is excused on exactly the same terms as the
//      server's: CSourceSharpSlotTable::Resolve compares the serial before the
//      cast ever happens, and these cases go red when that comparison goes.
//      Mutation-verified rather than assumed -- see the status file.
//
//      The client's own reason for wanting it is sharper than the server's.
//      Over here a handle is an INPUT: the server assigns it, the engine hands
//      it to the create function as a forced serial, and BindAt adopts it. So
//      the entry a client shell occupies is chosen by the other machine, and
//      "the entry was reused between snapshots" is a normal event rather than
//      an edge case.
//
//   2. THE COUNTS, which are the other half of §3.3's client sentence: "the
//      counts are maintained by bind and unbind". CClientEntityCounts is the
//      REAL class the client links -- client_entity_counts.h is engine-free so
//      that this file can drive it rather than a stand-in that would drift
//      (`fixture-must-be-checked-against-native`). GetHighestEntityIndex is the
//      one with teeth: 0e measured 1,184 calls to it, every one from engine.so,
//      every one a loop bound over the engine's own tables.
//
//   3. THE CLIENT HAS NO BIND ARM THAT CHOOSES A SERIAL, and that is a
//      COMPILE-TIME property. slot_table.h declares SourceSharp_SlotBind on the
//      server and deliberately declares no client twin: the two sides' handles
//      have to agree because a handle is what an EHANDLE prop puts on the wire,
//      so a client that issued its own serial would produce entities that
//      resolve on one machine and not the other. Nothing gtest can assert says
//      "this function does not exist" -- every assertion about an absent name
//      passes whether or not it is absent -- so the block at the bottom is
//      compiled again with -DCLIENT_SLOT_TABLE_NOCOMPILE=1 and required to be
//      REFUSED by the compiler.
//
// ENGINE-FREE, the same route slot_table_test.cpp takes:
// sourcesharp/test/entitylist_stub supplies the one-TU cbase.h and -Ipublic
// gets basehandle.h and const.h. Neither slot_table.h nor
// client_entity_counts.h takes an engine.
//
// WHAT THIS FILE IS NOT. It does not compile shell_cliententitylist.cpp, which
// is behind cbase.h and the whole client DLL. The nine IClientEntityList
// methods are covered by what they are BUILT OUT OF -- this file for the table
// and the counts, and the live gate for the rest, because "the engine gets the
// right answer" is a question only a running engine can be asked. The one
// property that is neither is called out at the bottom of the file.
//
//=============================================================================

#include <string.h>

#include "gtest/gtest.h"

#include "basehandle.h"
#include "ihandleentity.h"

#include "../game/shared/slot_table.h"
#include "../game/client/client_entity_counts.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for the client shell. The table stores a void * and never
// dereferences it, so the only thing that matters about this type is that two
// instances have two addresses -- which is what "did the old handle come back
// holding the new occupant?" reduces to.
//-----------------------------------------------------------------------------
struct FakeClientShell
{
	int nWhich;
};

//-----------------------------------------------------------------------------
// The client's bind, as shell_cliententitylist.cpp performs it: BindAt with a
// handle somebody else assigned.
//
// The entry and serial come from the engine's create call
// (CreateStandaloneTier( iTier, entnum, serialNum )), so this helper takes them
// the same way round and the tests read like the call site does.
//-----------------------------------------------------------------------------
bool ClientBind( CSourceSharpSlotTable &table, int nEntry, int nSerial, FakeClientShell *pShell )
{
	return table.BindAt( CBaseHandle( nEntry, nSerial ), pShell );
}

} // namespace

//=============================================================================
// 1. SERIAL REUSE -- the cover for the client's allowlist entry
//=============================================================================

//-----------------------------------------------------------------------------
// The core case, and the one the allowlist entry is excused by: an entry that
// changed occupants must not answer the dead entity's handle with the live
// entity's pointer.
//
// On the client this is the ORDINARY case rather than a corner: the server
// recycles an edict index the moment an entity dies, and the next snapshot
// carries a new occupant at the same index with a bumped serial.
//-----------------------------------------------------------------------------
TEST( ClientSerialReuse, TheOldHandleDoesNotResolveToTheNewOccupant )
{
	CSourceSharpSlotTable table;

	FakeClientShell first = { 1 };
	FakeClientShell second = { 2 };

	ASSERT_TRUE( ClientBind( table, 42, 7, &first ) );

	const CBaseHandle hOld( 42, 7 );
	ASSERT_EQ( &first, table.Resolve( hOld ) );

	// The entity dies. Unbind bumps the entry's serial, which is what makes
	// every handle to it stale AT THAT MOMENT rather than when something else
	// takes the entry.
	table.Unbind( 42 );
	EXPECT_TRUE( table.Resolve( hOld ) == NULL );

	// The server gives the index to somebody else, with the next serial.
	ASSERT_TRUE( ClientBind( table, 42, 8, &second ) );

	EXPECT_EQ( &second, table.Resolve( CBaseHandle( 42, 8 ) ) );

	// The whole point. Not &second, and not &first either.
	EXPECT_TRUE( table.Resolve( hOld ) == NULL );
}

//-----------------------------------------------------------------------------
// The same, in the engine-invisible half of the range (entries 2048..8191,
// §3.3). Both halves behave identically and that is asserted rather than
// assumed -- a resolver that special-cased the edict range would pass every
// test above and fail here.
//-----------------------------------------------------------------------------
TEST( ClientSerialReuse, TheRuleHoldsAboveMaxEdicts )
{
	CSourceSharpSlotTable table;

	FakeClientShell first = { 1 };
	FakeClientShell second = { 2 };

	const int nEntry = MAX_EDICTS + 100;

	ASSERT_TRUE( ClientBind( table, nEntry, 3, &first ) );

	const CBaseHandle hOld( nEntry, 3 );
	table.Unbind( nEntry );

	ASSERT_TRUE( ClientBind( table, nEntry, 4, &second ) );

	EXPECT_EQ( &second, table.Resolve( CBaseHandle( nEntry, 4 ) ) );
	EXPECT_TRUE( table.Resolve( hOld ) == NULL );
}

//-----------------------------------------------------------------------------
// A handle whose serial was never issued for that entry answers NULL.
//
// This is the arm that a resolver checking only "is anything bound here"
// would pass the previous tests and fail: entry 9 IS bound, and the handle
// still must not resolve.
//-----------------------------------------------------------------------------
TEST( ClientSerialReuse, ASerialThatWasNeverBoundThereResolvesToNull )
{
	CSourceSharpSlotTable table;

	FakeClientShell shell = { 1 };

	ASSERT_TRUE( ClientBind( table, 9, 5, &shell ) );

	ASSERT_EQ( &shell, table.Resolve( CBaseHandle( 9, 5 ) ) );

	EXPECT_TRUE( table.Resolve( CBaseHandle( 9, 4 ) ) == NULL );
	EXPECT_TRUE( table.Resolve( CBaseHandle( 9, 6 ) ) == NULL );
}

//-----------------------------------------------------------------------------
// THE SERVER'S SERIAL IS ADOPTED, NOT REPLACED -- the client-specific half of
// the rule, and the reason there is no Bind() on this side.
//
// If BindAt issued its own serial instead of taking the handle's, this entity
// would be unreachable through the very handle the server put on the wire.
//-----------------------------------------------------------------------------
TEST( ClientSerialAdoption, TheHandleTheServerAssignedIsTheHandleThatResolves )
{
	CSourceSharpSlotTable table;

	FakeClientShell shell = { 1 };

	// A fresh table's entries all carry serial 0. The server's serial is 31.
	ASSERT_EQ( 0, table.SerialAt( 12 ) );

	ASSERT_TRUE( ClientBind( table, 12, 31, &shell ) );

	EXPECT_EQ( 31, table.SerialAt( 12 ) );
	EXPECT_EQ( &shell, table.Resolve( CBaseHandle( 12, 31 ) ) );

	// And NOT through the serial the table would have issued itself.
	EXPECT_TRUE( table.Resolve( CBaseHandle( 12, 0 ) ) == NULL );
}

//-----------------------------------------------------------------------------
// The client never invents an entry either: a bind at an entry that is already
// occupied is REFUSED rather than overwriting, so two live shells can never
// share one handle. shell_cliententitylist.cpp turns the refusal into a
// Warning, because there is nothing to recover and something to report.
//-----------------------------------------------------------------------------
TEST( ClientSerialAdoption, ASecondBindAtALiveEntryIsRefusedAndLeavesTheFirst )
{
	CSourceSharpSlotTable table;

	FakeClientShell first = { 1 };
	FakeClientShell second = { 2 };

	ASSERT_TRUE( ClientBind( table, 3, 1, &first ) );

	EXPECT_FALSE( ClientBind( table, 3, 2, &second ) );

	EXPECT_EQ( &first, table.Resolve( CBaseHandle( 3, 1 ) ) );
	EXPECT_EQ( 1, table.BoundCount() );
	EXPECT_EQ( 1, table.Refused() );
}

//=============================================================================
// 2. THE COUNTS -- CClientEntityCounts, the real class the client links
//=============================================================================

//-----------------------------------------------------------------------------
// An empty world answers -1, which is stock's own answer
// (CClientEntityList::m_iMaxUsedServerIndex starts at -1) and is the
// "nothing present" answer the engine expects. Zero
// would name entity 0 -- the world -- as present on a client that has loaded
// nothing.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, AnEmptyWorldHasNoHighestIndex )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	EXPECT_EQ( -1, counts.HighestEntityIndex() );
	EXPECT_EQ( 0, counts.NumberOfEntities( false ) );
	EXPECT_EQ( 0, counts.NumberOfEntities( true ) );
}

//-----------------------------------------------------------------------------
// Bind and unbind maintain the count. §3.3's sentence, asserted.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, BindAndUnbindMaintainTheNetworkedCount )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };
	FakeClientShell b = { 2 };

	ASSERT_TRUE( ClientBind( table, 4, 0, &a ) );
	counts.OnBound( 4 );
	EXPECT_EQ( 1, counts.NumberOfEntities( false ) );

	ASSERT_TRUE( ClientBind( table, 5, 0, &b ) );
	counts.OnBound( 5 );
	EXPECT_EQ( 2, counts.NumberOfEntities( false ) );

	table.Unbind( 4 );
	counts.OnUnbound( 4 );
	EXPECT_EQ( 1, counts.NumberOfEntities( false ) );

	table.Unbind( 5 );
	counts.OnUnbound( 5 );
	EXPECT_EQ( 0, counts.NumberOfEntities( false ) );
}

//-----------------------------------------------------------------------------
// The highest index tracks the highest EDICT entry, and grows with a bind.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, TheHighestIndexRisesWithTheHighestBind )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };
	FakeClientShell b = { 2 };
	FakeClientShell c = { 3 };

	ASSERT_TRUE( ClientBind( table, 10, 0, &a ) );
	counts.OnBound( 10 );
	EXPECT_EQ( 10, counts.HighestEntityIndex() );

	// A LOWER entry does not lower it.
	ASSERT_TRUE( ClientBind( table, 3, 0, &b ) );
	counts.OnBound( 3 );
	EXPECT_EQ( 10, counts.HighestEntityIndex() );

	ASSERT_TRUE( ClientBind( table, 77, 0, &c ) );
	counts.OnBound( 77 );
	EXPECT_EQ( 77, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// THE CASE A DECREMENT WOULD GET WRONG. Removing the highest entity drops the
// number to the next occupied entry, which can be any distance below -- so it
// is recomputed, not decremented.
//
// The gap is deliberate: 3 and 77 are bound, 77 goes, and the answer is 3. A
// `--m_nHighestUsed` would have said 76, and the engine would then walk 74
// empty indices every frame while reporting nothing wrong.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, RemovingTheHighestFallsToTheNextOccupiedEntryAndNotToOneLess )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };
	FakeClientShell b = { 2 };

	ASSERT_TRUE( ClientBind( table, 3, 0, &a ) );
	counts.OnBound( 3 );
	ASSERT_TRUE( ClientBind( table, 77, 0, &b ) );
	counts.OnBound( 77 );
	ASSERT_EQ( 77, counts.HighestEntityIndex() );

	// The table first, then the counts -- the order the call site uses, and the
	// order that matters: RecomputeHighest reads the table.
	table.Unbind( 77 );
	counts.OnUnbound( 77 );

	EXPECT_EQ( 3, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// Removing something that is NOT the highest leaves the number alone.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, RemovingABelowTheTopEntityLeavesTheHighestAlone )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };
	FakeClientShell b = { 2 };

	ASSERT_TRUE( ClientBind( table, 3, 0, &a ) );
	counts.OnBound( 3 );
	ASSERT_TRUE( ClientBind( table, 77, 0, &b ) );
	counts.OnBound( 77 );

	table.Unbind( 3 );
	counts.OnUnbound( 3 );

	EXPECT_EQ( 77, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// Emptying the world takes it back to -1 rather than to 0.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, TheLastRemovalReturnsTheHighestToMinusOne )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };

	ASSERT_TRUE( ClientBind( table, 0, 0, &a ) );
	counts.OnBound( 0 );
	ASSERT_EQ( 0, counts.HighestEntityIndex() );

	table.Unbind( 0 );
	counts.OnUnbound( 0 );

	EXPECT_EQ( -1, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// ENTRY 0 COUNTS. The client world is entry 0 serial 0, and a counter that
// treated index zero as "nothing" would report an empty world on every map --
// the same defect entitylist_slot0_test.cpp exists to describe, one layer up.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, EntryZeroIsARealEntityForTheCountsToo )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell world = { 1 };

	ASSERT_TRUE( ClientBind( table, 0, 0, &world ) );
	counts.OnBound( 0 );

	EXPECT_EQ( 1, counts.NumberOfEntities( false ) );
	EXPECT_EQ( 0, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// THE ENGINE-INVISIBLE RANGE MUST NOT REACH GetHighestEntityIndex.
//
// This is the one that crashes rather than mis-draws. 0e: every call to
// GetHighestEntityIndex came from engine.so and the result is used as a loop
// bound over the ENGINE's own tables, which are sized MAX_EDICTS. An entity at
// entry 5000 -- a managed-only object §3.3 puts above MAX_EDICTS, which the
// engine has never heard of -- answering 5000 here sends that walk 2,952
// entries past the end of an array.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, AnEntityAboveMaxEdictsDoesNotRaiseTheHighestIndex )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell edict = { 1 };
	FakeClientShell invisible = { 2 };

	ASSERT_TRUE( ClientBind( table, 6, 0, &edict ) );
	counts.OnBound( 6 );

	const int nInvisible = MAX_EDICTS + 2952;
	ASSERT_TRUE( CSourceSharpSlotTable::IsEntry( nInvisible ) );
	ASSERT_FALSE( CSourceSharpSlotTable::IsEdictEntry( nInvisible ) );

	ASSERT_TRUE( ClientBind( table, nInvisible, 0, &invisible ) );
	counts.OnBound( nInvisible );

	EXPECT_EQ( 6, counts.HighestEntityIndex() );
}

//-----------------------------------------------------------------------------
// ...but it IS counted, by the arm that asks for it. bIncludeNonNetworkable is
// what the caller says when it means "everything", and stock's own split was
// the same one (m_iNumServerEnts + m_iNumClientNonNetworkable).
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, TheEngineInvisibleRangeCountsOnlyWhenAskedFor )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell edict = { 1 };
	FakeClientShell invisible = { 2 };

	ASSERT_TRUE( ClientBind( table, 6, 0, &edict ) );
	counts.OnBound( 6 );
	ASSERT_TRUE( ClientBind( table, MAX_EDICTS + 1, 0, &invisible ) );
	counts.OnBound( MAX_EDICTS + 1 );

	EXPECT_EQ( 1, counts.NumberOfEntities( false ) );
	EXPECT_EQ( 2, counts.NumberOfEntities( true ) );
}

//-----------------------------------------------------------------------------
// An entry outside the table moves nothing. Not defensive tidiness: a malformed
// handle's GetEntryIndex() is masked with sixteen bits while the table is
// thirteen wide (basehandle.h's "bug-submarining"), so a number outside the
// array is a value that really arrives.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, AnEntryOutsideTheTableMovesNothing )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	counts.OnBound( -1 );
	counts.OnBound( CSourceSharpSlotTable::EntryCount() );
	counts.OnUnbound( -1 );
	counts.OnUnbound( CSourceSharpSlotTable::EntryCount() );

	EXPECT_EQ( -1, counts.HighestEntityIndex() );
	EXPECT_EQ( 0, counts.NumberOfEntities( true ) );
}

//-----------------------------------------------------------------------------
// SetMaxEntities/GetMaxEntities is a store and a load, which is all stock did.
// The engine calls the setter once per level load, and a caller of the getter
// uses it as a loop bound (docs/engine-boundary.md).
//
// It is NOT the same number as NumberOfEntities and never derived from it --
// that is the mistake this pins. GetMaxEntities is the engine's declared
// capacity, and a loop over 0..GetMaxEntities()-1 runs to it regardless of how
// many entities exist.
//-----------------------------------------------------------------------------
TEST( ClientEntityCounts, MaxEntitiesIsWhatTheEngineSetAndNotHowManyExist )
{
	CSourceSharpSlotTable table;
	CClientEntityCounts counts( table );

	FakeClientShell a = { 1 };

	EXPECT_EQ( 0, counts.MaxEntities() );

	counts.SetMaxEntities( 1024 );
	EXPECT_EQ( 1024, counts.MaxEntities() );

	ASSERT_TRUE( ClientBind( table, 1, 0, &a ) );
	counts.OnBound( 1 );

	EXPECT_EQ( 1024, counts.MaxEntities() );
	EXPECT_EQ( 1, counts.NumberOfEntities( false ) );
}

//=============================================================================
// 3. THE CLIENT MAY NOT ISSUE ITS OWN SERIALS -- a compile-time property
//
// Compiled again by build_must_not_compile in src/buildcpptests with
// -DCLIENT_SLOT_TABLE_NOCOMPILE=1, and required to be REFUSED.
//
// WHY A COMPILE ERROR AND NOT AN ASSERTION. There is no assertion that says "no
// such function exists": every test written against an absent name passes
// whether or not the name is absent, which is the check that cannot fail. The
// only thing that can observe an absence is the compiler.
//
// WHAT IT WOULD COST TO GET WRONG. A client that chose its own serial for an
// entity would produce a handle the server never assigned. Handles travel: an
// EHANDLE prop puts one on the wire, and the receiving side looks it up in its
// own table. So the failure is an entity that resolves on one machine and not
// on the other, per entity, with no message anywhere -- the exact class of bug
// §3.3 gives BindAt's forced serial to prevent.
//
// THE CONTROL IS THE FILE ITSELF. Every test above calls BindAt through
// ClientBind and passes, so the refusal below cannot be a typo or a missing
// include refusing everything: the same translation unit compiles and runs
// sixteen facts with the block switched off.
//=============================================================================

#if defined( CLIENT_SLOT_TABLE_NOCOMPILE ) && CLIENT_SLOT_TABLE_NOCOMPILE == 1

// The server has SourceSharp_SlotBind( entry, shell, edict ): bind and let the
// table choose the serial. There is no client twin, and this asks for one.
void ClientSlotTableProbe_ChoosesItsOwnSerial( CSourceSharpSlotTable &table, FakeClientShell *pShell )
{
	// Expected: 'Bind' is protected/absent to the client's declared surface --
	// the table's own Bind exists, but the CLIENT WRAPPER that would let a shim
	// TU reach it does not, and slot_table.h declares no such name.
	SourceSharp_ClientSlotBind( 5, pShell );
	(void)table;
}

#endif // CLIENT_SLOT_TABLE_NOCOMPILE == 1
