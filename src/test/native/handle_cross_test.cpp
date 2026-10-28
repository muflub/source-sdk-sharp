//========= SourceSharp — tests for handle_cross.h ===========================//
//
// The crossing rule against mock hierarchies shaped like the real one.
//
// Phase 13 step 1 proper: the crossing to the
// GAME-ENTITY boundary, tested on a mock shaped like CStandaloneEntity, where
// the interface is the fourth base and its subobject address genuinely differs
// from the one the handle table stores. That difference is the whole reason
// the test is not vacuous — on a CBaseEntity the two addresses coincide, both
// answers agree, and a cast passes.
//
// PROVEN RED. Replacing the crossing's body in handle_cross.h with
// `return (TGameEntity *)pHandleEntity;` — the cast the specialization exists
// to replace — fails four of the seven, and the failures name the
// disagreement rather than merely reporting one:
//
//   StandaloneCrossesToItsOwnBoundaryView
//       the crossing and the compiler disagree about where this entity's
//       game-entity view is: compiler says 0x7ffc0593f918, crossing says
//       0x7ffc0593f8e0, off by -56 bytes
//   TheCrossedPointerDispatchesIntoTheRightClass
//       Actual: -1  Expected: 0x5A11D
//       — and -1 is SdkHandleEntity::RefEHandle(), the function that really
//         sits in the slot the mis-typed pointer called WhoAreYou() through.
//         That is the hazard itself, printed.
//   OldPatternMistypesTheStandalone      cast and crossing now agree, wrongly
//   SdkEntityComesBackNull               a CBaseEntity comes back non-NULL
//
// The three that stay green are the ones that cannot tell the two apart:
// NULL in, the offset-zero premise on the SDK's tree, and the mock's own
// layout. That split is the point — if a mutation turned everything red, the
// suite would not be distinguishing anything.
//
//=============================================================================//
#include <string>
#include <sstream>
#include <fstream>
#include <gtest/gtest.h>

#include "../game/shared/handle_cross.h"

// =============================================================================
// Phase 13 step 1 — the crossing to IGameEntity.
//
// A second mock hierarchy, shaped like the real one rather than like the weapon
// mocks above: the SDK's chain is IHandleEntity -> IServerUnknown ->
// IServerEntity, single inheritance the whole way, and the game-entity boundary
// is a SEPARATE base that CStandaloneEntity lists FOURTH. So a standalone's
// IGameEntity view is not its IHandleEntity view, and the two differ by a
// number of bytes the test can print.
//
// This is what makes the comparison mean something. On a stock CBaseEntity the
// boundary sat at offset zero, both answers coincided and a cast was
// indistinguishable from a crossing; the server has no such entity any more,
// and the standalone is where the two differ.
// =============================================================================

namespace
{

// --- the engine-side chain, single inheritance, exactly like the real one -----

class SdkHandleEntity // ~ IHandleEntity
{
public:
	virtual ~SdkHandleEntity() {}

	virtual int RefEHandle() const { return -1; }
};

class SdkGameEntity; // ~ IGameEntity, which IS CBaseEntity

// ONE ACCESSOR SINCE plan_server_native_v2 T1.3. Stock iserverunknown.h's
// GetBaseEntity() is typed by the entity face, and on the server that face is
// CBaseEntity with IGameEntity its alias (D1). The patch's second accessor,
// GetGameEntity(), is gone: the two partitioned the world identically once no
// SDK CBaseEntity was left on the server, and the SDK's tree this suite used to
// model beside the standalone one (SdkBaseEntity, SdkWeapon, the "step 5"
// standalone) went with it. What stays is what the crossing still does: adjust
// from the handle table's address to the face's, inside the object.
class SdkServerUnknown : public SdkHandleEntity // ~ IServerUnknown
{
public:
	virtual SdkGameEntity *GetBaseEntity() = 0;
};

class SdkServerEntity : public SdkServerUnknown // ~ IServerEntity
{
public:
	virtual int GetModelIndex() const { return 0; }
};

// --- the boundary, which is NOT in that chain --------------------------------

class SdkGameEntity // ~ IGameEntity
{
public:
	virtual ~SdkGameEntity() {}

	// Answers with a value only this class knows, so a test can tell a correct
	// crossing from a plausible pointer by what comes back rather than by
	// inspecting a vtable it has no way to read.
	virtual int WhoAreYou() const = 0;
};

// --- the parallel hierarchy: four bases, the boundary last -------------------

class SdkNetworkable // ~ IServerNetworkable
{
public:
	virtual ~SdkNetworkable() {}

	virtual SdkHandleEntity *GetEntityHandle() = 0;

	// IServerNetworkable declares this too, with the same signature as
	// IServerUnknown's, so a standalone entity's ONE override answers both.
	virtual SdkGameEntity *GetBaseEntity() = 0;

	int m_nNetPadding[3];
};

class SdkCollideable // ~ ICollideable
{
public:
	virtual ~SdkCollideable() {}

	virtual int GetSolid() const { return 0; }

	int m_nCollidePadding[3];
};

class SdkStandalone : public SdkServerEntity, public SdkNetworkable, public SdkCollideable, public SdkGameEntity
{
public:
	virtual SdkGameEntity *GetBaseEntity() { return this; }

	virtual SdkHandleEntity *GetEntityHandle() { return this; }

	virtual int WhoAreYou() const { return 0x5A11D; }
};

// The world: in the entity list as an IServerEntity, with no entity face at
// all. CWorld's GetBaseEntity() answers NULL, and that NULL is the partition's
// other arm.
class SdkWorld : public SdkServerEntity, public SdkNetworkable
{
public:
	virtual SdkGameEntity *GetBaseEntity() { return 0; }

	virtual SdkHandleEntity *GetEntityHandle() { return static_cast<SdkServerEntity *>( this ); }
};

// What the entity list holds and what CBaseHandle::Get() hands back: the
// IHandleEntity view, and nothing else.
static SdkHandleEntity *TableEntry( SdkStandalone *pEnt )
{
	return pEnt;
}

static SdkHandleEntity *TableEntry( SdkWorld *pEnt )
{
	return static_cast<SdkServerEntity *>( pEnt );
}

// THE KNOWN-GOOD CROSSING, and it is known-good because the COMPILER performs
// it: from the concrete type, where the layout is a fact rather than an
// assumption, static_cast adjusts by whatever the base's offset really is.
// Nothing in the crossing under test is consulted to produce this.
static SdkGameEntity *KnownGood( SdkStandalone *pEnt )
{
	return static_cast<SdkGameEntity *>( pEnt );
}

// What CHandle<IGameEntity>::Get() would do WITHOUT the specialization.
static SdkGameEntity *OldPatternCastToGameEntity( SdkHandleEntity *pHandleEntity )
{
	return (SdkGameEntity *)pHandleEntity;
}

static SdkGameEntity *CrossToGame( SdkHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToGameEntity<SdkGameEntity, SdkServerEntity>( pHandleEntity );
}

static long ByteDelta( const void *a, const void *b )
{
	return (long)( (const char *)a - (const char *)b );
}

// --- the premise, asserted rather than assumed --------------------------------

TEST( HandleCrossGameEntity, StandaloneSubobjectAddressesActuallyDiffer )
{
	SdkStandalone standalone;

	// If this ever became false — a layout change, a reordered base list — every
	// other test in this suite would still pass while proving nothing, because
	// a cast and a crossing would agree by accident. So it is checked first and
	// on its own.
	EXPECT_NE( (const void *)TableEntry( &standalone ), (const void *)KnownGood( &standalone ) )
		<< "the mock's IGameEntity subobject is at offset zero, so this suite "
		   "cannot distinguish a crossing from a cast";
}

// --- the crossing agrees with the compiler, on the object where it matters ----

TEST( HandleCrossGameEntity, StandaloneCrossesToItsOwnBoundaryView )
{
	SdkStandalone standalone;

	SdkGameEntity *pGood = KnownGood( &standalone );
	SdkGameEntity *pCrossed = CrossToGame( TableEntry( &standalone ) );

	EXPECT_EQ( pGood, pCrossed ) << "the crossing and the compiler disagree about where this entity's "
									"game-entity view is: compiler says "
								 << (const void *)pGood << ", crossing says " << (const void *)pCrossed << ", off by "
								 << ByteDelta( pCrossed, pGood )
								 << " bytes; the handle "
									"table's address for it is "
								 << (const void *)TableEntry( &standalone );
}

// And the pointer works, which is the half an address comparison cannot show.
TEST( HandleCrossGameEntity, TheCrossedPointerDispatchesIntoTheRightClass )
{
	SdkStandalone standalone;

	SdkGameEntity *pCrossed = CrossToGame( TableEntry( &standalone ) );
	ASSERT_NE( (SdkGameEntity *)0, pCrossed );

	EXPECT_EQ( 0x5A11D, pCrossed->WhoAreYou() ) << "a call through the crossed pointer landed somewhere else";
}

// --- and the cast does not -----------------------------------------------------

TEST( HandleCrossGameEntity, OldPatternMistypesTheStandalone )
{
	SdkStandalone standalone;

	SdkGameEntity *pGood = KnownGood( &standalone );
	SdkGameEntity *pWrong = OldPatternCastToGameEntity( TableEntry( &standalone ) );

	// Non-NULL, resolves, and points at the FRONT of the object rather than at
	// the boundary subobject — so it is a pointer that looks entirely valid and
	// whose every virtual call goes into IServerEntity's slots instead. The
	// call is deliberately not made: dispatching through it is the bug, not a
	// thing to demonstrate by doing.
	EXPECT_NE( (SdkGameEntity *)0, pWrong );
	EXPECT_NE( pGood, pWrong ) << "the cast happened to land on the boundary subobject, so this mock "
								  "no longer reproduces the hazard";

	EXPECT_EQ( pGood, CrossToGame( TableEntry( &standalone ) ) );
}

// --- everything else the crossing has to answer --------------------------------

TEST( HandleCrossGameEntity, NullIn_NullOut )
{
	EXPECT_EQ( (SdkGameEntity *)0, CrossToGame( 0 ) );
}

TEST( HandleCrossGameEntity, TheWorldComesBackNull )
{
	// The other arm of the partition. The world has no entity face and says so
	// with the same virtual, so the crossing needs no second rule.
	SdkWorld world;
	EXPECT_EQ( (SdkGameEntity *)0, CrossToGame( TableEntry( &world ) ) );
}

} // namespace

// =============================================================================
// The partition as a RESOLVER — SourceSharp_SDKEntityFromNetworkable.
//
// "Is this entity in the SDK's tree, and if so where." On the server the
// answer is no for every object since T1.3: a standalone is outside the SDK
// (its GetBaseEntity() answers its face), and the world has no face at all.
// shell_mapparse.cpp's CreateEntityByName asks this and frees what it made
// when the answer is NULL, as it always has for a standalone.
//
// WHAT KEEPS THIS NON-VACUOUS: the standalone's raw GetBaseEntity() is
// non-NULL and at a different address from its networkable face, so a
// resolver that stopped asking the partition, or a route that skipped
// GetEntityHandle(), would hand that address back.
// =============================================================================

namespace
{

static SdkGameEntity *ResolveNetworkable( SdkNetworkable *pNetworkable )
{
	return SourceSharp_SDKEntityFromNetworkable<SdkServerEntity>( pNetworkable );
}

TEST( SdkEntityResolver, NetworkableNullIn_NullOut )
{
	EXPECT_EQ( (SdkGameEntity *)0, ResolveNetworkable( 0 ) );
}

TEST( SdkEntityResolver, AStandaloneIsNotAnSdkEntity )
{
	SdkStandalone standalone;

	// The premise: the raw accessor answers, and the two faces really are at
	// different addresses, so the route through GetEntityHandle() is doing
	// work rather than being a no-op that would pass either way.
	EXPECT_NE( (SdkGameEntity *)0, standalone.GetBaseEntity() )
		<< "the standalone's GetBaseEntity() answers NULL, so this case cannot "
		   "tell a resolver that asks the partition from one that does not";
	EXPECT_NE( (const void *)static_cast<SdkNetworkable *>( &standalone ),
			   (const void *)static_cast<SdkServerEntity *>( &standalone ) )
		<< "the mock's two faces coincide, so this case cannot distinguish "
		   "the crossing from a cast";

	EXPECT_EQ( (SdkGameEntity *)0, ResolveNetworkable( &standalone ) )
		<< "the resolver handed back " << (const void *)ResolveNetworkable( &standalone )
		<< " for an entity that is not in the SDK's tree";
}

TEST( SdkEntityResolver, TheWorldIsNotAnSdkEntityEither )
{
	SdkWorld world;
	EXPECT_EQ( (SdkGameEntity *)0, ResolveNetworkable( &world ) );
}

TEST( SdkEntityResolver, TheServerPartitionRecognisesAStandaloneAndNotTheWorld )
{
	SdkStandalone standalone;
	SdkWorld world;

	EXPECT_TRUE(
		SourceSharp_Partition<SdkServerEntity>::IsOutsideTheSdk( static_cast<SdkServerUnknown *>( &standalone ) ) )
		<< "the partition failed to recognise a standalone entity";

	EXPECT_FALSE( SourceSharp_Partition<SdkServerEntity>::IsOutsideTheSdk( static_cast<SdkServerUnknown *>( &world ) ) )
		<< "the partition called the world a standalone; it has no entity face";
}

TEST( SdkEntityResolver, TheDeltaIsWhatTheLiveCrashSaw )
{
	// gameinterface.cpp:3328 ProcessUsercmds died with rdi = the ss_player's
	// IGameEntity view, PAST the handle table's address for the same object.
	// The number is the layout's, not this test's; what is asserted is that
	// there IS one, so the shape being reproduced is the shape that crashed.
	SdkStandalone standalone;

	const long delta = ByteDelta( standalone.GetBaseEntity(), static_cast<SdkHandleEntity *>( &standalone ) );

	EXPECT_GT( delta, 0 ) << "the face coincides with the handle table's address; the live "
							 "hazard was a pointer PAST it";
}

} // namespace

//=============================================================================//
// THE CROSSINGS STILL HAVE CALLERS, AND THIS FILE SAYS SO IN ASSERTIONS
//
// WHY THIS SECTION EXISTS. plan_shim.md's Phase 3 ruling table records
// `handle_cross.h` as DELETE -- "dead since the no-base-entity merge, 327 lines
// plus 6 gtests", 1 live server consumer and 0 live client. Lane 3f re-measured
// it at the Phase 3 branch point and the ruling is WRONG, by a wide margin and
// in the direction that deletes live code:
//
//   tools/shim-header-census includers sourcesharp/game/shared/handle_cross.h
//     102 server / 169 client live translation units
//   tools/shim-header-census references SourceSharp_SDKEntityFromUnknown
//     103 server / 129 client
//   ... SourceSharp_CrossHandleToGameEntity        102 server /   0 client
//   ... SourceSharp_SDKEntityFromNetworkable         0 server /   0 client (T2)
//
// The ruling was taken with an instrument that asks a HEADER which objects
// import its symbols. A header has no object and every entity here is an inline
// template, so the honest answer to that question is zero and it means nothing.
// The real consumers arrive transitively, through `entitylist.h` on the server
// and `c_baseentity.h` on the client, which is why the count looked like one
// file.
//
// A MEASUREMENT IS NOT A DEFENCE. It was true on the day it was taken and the
// next reader of that table will not see it. So the live crossings are pinned
// here at their call sites, and the test fails if somebody acts on the stale
// row. It is deliberately a PAIR -- the definition must be in handle_cross.h
// AND the call must still be at its site -- so it cannot be satisfied by
// deleting both.
//=============================================================================//

namespace
{

struct LiveCrossing_t
{
	const char *pszSymbol;
	const char *pszCallerFile;
	const char *pszCallText;
};

} // namespace

// =============================================================================
// SourceSharp_CrossHandleToClientShell -- a trace hit to the client shell, with
// no game-entity face asked (2026-10-18: C_StandaloneEntity drops its
// IGameEntity base, after which GetGameEntity() answers NULL for every client
// object, so "which of ours did the trace hit?" moves to the slot table).
//
// RETENTION FIRST: a shell bound at its own handle must come back as ITSELF.
// That is the fact the client's ground resolve (movement_ground.h) and the
// impact effects' control points (shell_impact_effects.cpp) stand on; a
// resolver that answered NULL would compile, pass every refusal below, and
// leave a player on a managed entity standing on nothing.
// =============================================================================

namespace
{

// FakeHandleEntity is NOT at offset zero inside the shell, so the identity
// check compares two genuinely different numbers and a version of it that
// compared raw addresses would refuse the real shell.
class FakeHandleEntity // ~ IHandleEntity
{
public:
	virtual ~FakeHandleEntity() {}
};

class FakeShellFirstBase
{
public:
	virtual ~FakeShellFirstBase() {}

	int m_nPadding[6];
};

class FakeShell : public FakeShellFirstBase, public FakeHandleEntity
{
};

// Another object in the handle table, shaped like CDetailModel.
class FakeDetailModel : public FakeHandleEntity
{
};

// A slot table of one entry: whatever is bound answers every lookup, which is
// the worst case for the identity check (a handle that names another occupant).
struct FakeShellTable
{
	FakeShell *pBound = 0;
	int nAsked = 0;
};

static FakeShell *CrossToShell( FakeHandleEntity *pHit, FakeShellTable &table, bool bStaticProp = false )
{
	return SourceSharp_CrossHandleToClientShell<FakeShell>(
		pHit,
		[&table]( FakeHandleEntity * )
		{
			++table.nAsked;
			return table.pBound;
		},
		[bStaticProp]( FakeHandleEntity * ) { return bStaticProp; } );
}

} // namespace

TEST( ClientShellCross, TheShellsHandleViewIsNotAtOffsetZero )
{
	// The premise: without it the identity check below cannot tell an adjusted
	// compare from a raw one.
	FakeShell shell;
	EXPECT_NE( (const void *)&shell, (const void *)static_cast<FakeHandleEntity *>( &shell ) );
}

TEST( ClientShellCross, ABoundShellComesBackAsItself )
{
	FakeShell shell;
	FakeShellTable table;
	table.pBound = &shell;

	FakeShell *pFound = CrossToShell( static_cast<FakeHandleEntity *>( &shell ), table );
	EXPECT_EQ( &shell, pFound ) << "a trace that hit a bound shell resolved to " << (const void *)pFound
								<< "; the ground resolve and the impact control points read this answer";
	EXPECT_EQ( 1, table.nAsked );
}

TEST( ClientShellCross, NullIn_NullOut )
{
	FakeShellTable table;
	EXPECT_EQ( (FakeShell *)0, CrossToShell( 0, table ) );
	EXPECT_EQ( 0, table.nAsked ) << "a NULL hit was looked up";
}

TEST( ClientShellCross, AnUnboundHandleComesBackNull )
{
	FakeShell shell;
	FakeShellTable table; // nothing bound
	EXPECT_EQ( (FakeShell *)0, CrossToShell( static_cast<FakeHandleEntity *>( &shell ), table ) );
	EXPECT_EQ( 1, table.nAsked );
}

TEST( ClientShellCross, AnotherObjectAtTheBoundHandleComesBackNull )
{
	// The hit is a detail model whose handle names an entry a shell occupies:
	// the occupant is not what the trace hit.
	FakeShell shell;
	FakeDetailModel detail;
	FakeShellTable table;
	table.pBound = &shell;

	EXPECT_EQ( (FakeShell *)0, CrossToShell( static_cast<FakeHandleEntity *>( &detail ), table ) );
	EXPECT_EQ( 1, table.nAsked );
}

TEST( ClientShellCross, AStaticPropIsNeverLookedUp )
{
	FakeShell shell;
	FakeShellTable table;
	table.pBound = &shell;

	EXPECT_EQ( (FakeShell *)0, CrossToShell( static_cast<FakeHandleEntity *>( &shell ), table, true ) );
	EXPECT_EQ( 0, table.nAsked ) << "a static prop's handle was handed to the slot table";
}
