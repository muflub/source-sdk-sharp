//========= SourceSharp — the per-entity side table is keyed by nothing =======//
//
// `CEntityDataInstantiator<T>` is the hash table behind the eight data-object
// kinds that hang off an entity (`shareddefs.h`: GROUNDLINK .. VPHYSICSWATCHER).
// It was keyed on `const CBaseEntity *`. The client flip removes that class from
// the client link line, and the system that owns these tables had to move out of
// `game/shared/physics_main_shared.cpp` -- which leaves -- into
// `game/shared/physics_shared.cpp`, which stays.
//
// SO THE KEY IS A `const void *` NOW, AND STOCK'S OWN COMMENT SAID IT COULD BE:
//
//     // This is the hash key type, but it could just as easily be and int or void *
//
// This file is what makes that a checked claim rather than a plausible one. The
// retype is invisible to a compiler in the direction that matters -- every call
// site passed a pointer and still passes a pointer, so nothing fails to build if
// the table starts confusing keys -- and the failure mode is one entity reading
// another entity's touch links. That is silent, it is intermittent, and it is
// the exact shape of bug this port has lost days to before.
//
// It is a tier1 test rather than a header-only one on purpose: CUtlHash is the
// thing under test as much as the key type is, and a stand-in for it would be a
// test of the stand-in. (`fixture-must-be-checked-against-native`.)
//
//=============================================================================//

#include "gtest/gtest.h"
#include "entitydatainstantiator.h"

namespace
{

//-----------------------------------------------------------------------------
// A payload with a shape: a byte pattern the table must not touch, and enough
// of it that "wrong entry" and "right entry" cannot coincide.
//-----------------------------------------------------------------------------
struct Payload
{
	int nMark;
	int nOther;
};

//-----------------------------------------------------------------------------
// Keys that are NOT entities and never were. Three distinct addresses; what
// they point at is irrelevant, which is the whole point of the retype.
//-----------------------------------------------------------------------------
struct NotAnEntity
{
	char pad[64];
};

NotAnEntity g_a, g_b, g_c;

//-----------------------------------------------------------------------------
// Create then Get answers the SAME object for the SAME key.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, CreateThenGetAnswersTheSameObject )
{
	CEntityDataInstantiator<Payload> table;

	void *pCreated = table.CreateDataObject( &g_a );
	ASSERT_TRUE( pCreated != NULL );
	EXPECT_EQ( pCreated, table.GetDataObject( &g_a ) );

	table.DestroyDataObject( &g_a );
}

//-----------------------------------------------------------------------------
// TWO KEYS ARE TWO ENTRIES. This is the fact the retype could break and a
// compiler could not see: if the table collapsed distinct keys, one entity
// would read another's touch links, silently.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, DistinctKeysGetDistinctObjects )
{
	CEntityDataInstantiator<Payload> table;

	Payload *pA = (Payload *)table.CreateDataObject( &g_a );
	Payload *pB = (Payload *)table.CreateDataObject( &g_b );

	ASSERT_TRUE( pA != NULL );
	ASSERT_TRUE( pB != NULL );
	EXPECT_NE( pA, pB );

	pA->nMark = 0x1111;
	pB->nMark = 0x2222;

	EXPECT_EQ( 0x1111, ( (Payload *)table.GetDataObject( &g_a ) )->nMark );
	EXPECT_EQ( 0x2222, ( (Payload *)table.GetDataObject( &g_b ) )->nMark );

	table.DestroyDataObject( &g_a );
	table.DestroyDataObject( &g_b );
}

//-----------------------------------------------------------------------------
// A key that was never created answers NULL, and does not create one. Stock's
// GetDataObject is a lookup, not a get-or-add; the get-or-add is Create.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, AnUnknownKeyAnswersNull )
{
	CEntityDataInstantiator<Payload> table;

	table.CreateDataObject( &g_a );
	EXPECT_TRUE( table.GetDataObject( &g_c ) == NULL );

	table.DestroyDataObject( &g_a );
}

//-----------------------------------------------------------------------------
// Create is idempotent for one key. Stock returns the existing object rather
// than replacing it -- a replace would drop the touch links an entity is in the
// middle of using.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, CreateTwiceForOneKeyKeepsTheFirstObject )
{
	CEntityDataInstantiator<Payload> table;

	Payload *pFirst = (Payload *)table.CreateDataObject( &g_a );
	pFirst->nMark = 0x3333;

	Payload *pAgain = (Payload *)table.CreateDataObject( &g_a );
	EXPECT_EQ( pFirst, pAgain );
	EXPECT_EQ( 0x3333, pAgain->nMark );

	table.DestroyDataObject( &g_a );
}

//-----------------------------------------------------------------------------
// A created object is ZEROED. `CDataObjectAccessSystem` hands these straight to
// callers that read them without initialising -- stock memsets for exactly that
// and says so ("FIXME: We'll have to remove this if any objects we instance
// have vtables!!!").
//-----------------------------------------------------------------------------
TEST( DataObjectKey, ACreatedObjectIsZeroed )
{
	CEntityDataInstantiator<Payload> table;

	Payload *p = (Payload *)table.CreateDataObject( &g_a );
	ASSERT_TRUE( p != NULL );
	EXPECT_EQ( 0, p->nMark );
	EXPECT_EQ( 0, p->nOther );

	table.DestroyDataObject( &g_a );
}

//-----------------------------------------------------------------------------
// Destroy removes THAT key and leaves the others standing. The failure this
// guards is a level shutdown taking one entity's data object and every other
// entity's with it.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, DestroyRemovesOnlyThatKey )
{
	CEntityDataInstantiator<Payload> table;

	table.CreateDataObject( &g_a );
	Payload *pB = (Payload *)table.CreateDataObject( &g_b );
	pB->nMark = 0x4444;

	table.DestroyDataObject( &g_a );

	EXPECT_TRUE( table.GetDataObject( &g_a ) == NULL );
	ASSERT_TRUE( table.GetDataObject( &g_b ) != NULL );
	EXPECT_EQ( 0x4444, ( (Payload *)table.GetDataObject( &g_b ) )->nMark );

	table.DestroyDataObject( &g_b );
}

//-----------------------------------------------------------------------------
// Destroying a key that is not in the table is a no-op, not a crash. Stock's
// DestroyDataObject is reached from teardown paths that do not know whether the
// object was ever created.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, DestroyingAnUnknownKeyIsANoOp )
{
	CEntityDataInstantiator<Payload> table;

	table.CreateDataObject( &g_a );
	table.DestroyDataObject( &g_c );

	EXPECT_TRUE( table.GetDataObject( &g_a ) != NULL );

	table.DestroyDataObject( &g_a );
}

//-----------------------------------------------------------------------------
// A key can be re-created after it was destroyed, and the second object is
// fresh. An entity index is reused between maps; a stale payload surviving that
// is a map-reload bug of exactly the class this wave's reload clause exists for.
//-----------------------------------------------------------------------------
TEST( DataObjectKey, AKeyRecreatedAfterDestroyGetsAFreshObject )
{
	CEntityDataInstantiator<Payload> table;

	Payload *pFirst = (Payload *)table.CreateDataObject( &g_a );
	pFirst->nMark = 0x5555;
	table.DestroyDataObject( &g_a );

	Payload *pSecond = (Payload *)table.CreateDataObject( &g_a );
	ASSERT_TRUE( pSecond != NULL );
	EXPECT_EQ( 0, pSecond->nMark );

	table.DestroyDataObject( &g_a );
}

} // namespace
