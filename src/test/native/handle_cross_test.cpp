//========= SourceSharp — tests for handle_cross.h ===========================//
//
// The crossing rule against mock hierarchies shaped like the real two. The
// mocks exist so the FAILURE is constructible: a parallel-hierarchy object the
// old (T*) cast mis-types into a pointer that is neither NULL nor the object's
// own address, laid out so the wrongness is a visible number rather than a
// vtable a test cannot inspect. Replace the crossing's virtual walk with a
// cast and ParallelHierarchyComesBackNull and OldPatternMistypes go red — that
// is the proof the guard can fail, per plan_baseentity.md Phase 13 step 1's
// requirement that the comparison happen where the two answers actually
// diverge.
//
// The second half of the file is Phase 13 step 1 proper: the crossing to the
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

namespace
{

// --- the shared interface both hierarchies implement -------------------------
//
// SHAPED LIKE THE CLIENT'S, and it is the client's partition these exercise:
// IHandleEntity -> IClientUnknown -> IClientEntity, with the game-entity
// boundary a SEPARATE base, and the partition question reached through
// GetIClientEntity() rather than a cast. That is not decoration -- it is the
// difference the client's crossing exists for, and the mocks below include an
// IClientUnknown that is NOT an entity so the difference is constructible.

class FakeHandleEntity
{
public:
	virtual ~FakeHandleEntity() {}
};

class FakeBase;        // the SDK tree's root      ~ C_BaseEntity
class FakeClientEntity;// the face that partitions ~ IClientEntity
class FakeGameEntity;  // the boundary             ~ IGameEntity

class FakeUnknown : public FakeHandleEntity            // ~ IClientUnknown
{
public:
	virtual FakeBase *GetBaseEntity() = 0;

	// The conversion the object performs for itself. NULL is the honest answer
	// from an IClientUnknown that is not an entity, and CDefaultClientRenderable,
	// CDetailModel, C_BaseTempEntity and the engine's static props all give it.
	virtual FakeClientEntity *GetIClientEntity() { return 0; }
};

class FakeGameEntity                                   // ~ IGameEntity
{
public:
	virtual ~FakeGameEntity() {}
	virtual int WhoAreYouOnTheClient() const = 0;
};

class FakeClientEntity : public FakeUnknown            // ~ IClientEntity
{
public:
	// Phase 13's revised step 1: appended HERE rather than on FakeUnknown,
	// exactly as the real one is appended to IClientEntity rather than
	// IClientUnknown, and for the reasons icliententity.h states.
	virtual FakeGameEntity *GetGameEntity() { return 0; }
};

// --- the SDK's hierarchy ------------------------------------------------------

class FakeWeapon;

class FakeBase : public FakeClientEntity
{
public:
	virtual FakeBase *GetBaseEntity() { return this; }
	virtual FakeClientEntity *GetIClientEntity() { return this; }
	virtual FakeWeapon *MyCombatWeaponPointer() { return 0; }
};

// A second base FIRST, so FakeHandleEntity is not at offset zero inside a
// weapon. The cast the old pattern performs is an ADJUSTING downcast — it
// subtracts the base's offset within FakeWeapon — which is exactly right when
// the object really is a weapon and manufactures an address pointing BEFORE
// the object when it is not. A zero offset would hide the second half of
// that: the mis-typed pointer would still equal the standalone's address and
// the wrongness would live only in the vtable, where a test cannot look
// without committing the bug itself.
class FakePredictable
{
public:
	virtual ~FakePredictable() {}
	int m_nPadding[ 4 ];
};

class FakeWeapon : public FakePredictable, public FakeBase
{
public:
	virtual FakeWeapon *MyCombatWeaponPointer() { return this; }
};

// --- the parallel hierarchy ---------------------------------------------------

class FakeStandalone : public FakeClientEntity, public FakeGameEntity
{
public:
	// The partition, BOTH arms. GetBaseEntity() answers NULL today and answers
	// the boundary view at step 5; GetGameEntity() answers the boundary view
	// before and after, which is why it is the one the crossing asks.
	virtual FakeBase *GetBaseEntity() { return 0; }
	virtual FakeClientEntity *GetIClientEntity() { return this; }
	virtual FakeGameEntity *GetGameEntity() { return this; }

	virtual int WhoAreYouOnTheClient() const { return 0xC11E7; }

	// Occupies the vtable slot a mis-typed reader would take for
	// MyCombatWeaponPointer. Never called by these tests — calling through a
	// wrong vtable is the bug, not a thing to demonstrate by doing — but its
	// existence is the shape of the hazard.
	virtual int StandaloneOnlyThing() { return 0xBEEF; }
};

// AN IClientUnknown THAT IS NOT AN ENTITY, which the server has no equivalent
// of and which is the whole reason the client's crossing is two virtuals rather
// than a cast. Shaped like CDetailModel: IClientUnknown plus a renderable face,
// and nothing below.
class FakeRenderableOnly
{
public:
	virtual ~FakeRenderableOnly() {}
	virtual int RenderableOnlyThing() { return 0xD37A11; }
};

class FakeDetailModel : public FakeUnknown, public FakeRenderableOnly
{
public:
	virtual FakeBase *GetBaseEntity() { return 0; }
	// GetIClientEntity() inherited: NULL, and it is the truth rather than a
	// hedge. This object has no FakeClientEntity subobject at all.
};

// The old pattern, spelled out: what CHandle<T>::Get() does without the
// specialization — one cast, no questions.
static FakeWeapon *OldPatternCast( FakeHandleEntity *pHandleEntity )
{
	return ( FakeWeapon * )pHandleEntity;
}

static FakeWeapon *Cross( FakeHandleEntity *pHandleEntity )
{
	// The client's shape, WITH its partition. This used to pass
	// SourceSharpNoPartition -- a tag that meant "the client cannot ask" and
	// made the crossing rest entirely on GetBaseEntity() answering NULL, which
	// is the answer step 5 takes away. Phase 13's revised step 1 gave the client
	// a real partition, so the tag is gone and this is the same crossing the
	// server does.
	return SourceSharp_CrossHandleToWeapon< FakeWeapon, SourceSharpClientPartition, FakeUnknown >( pHandleEntity );
}

// --- the crossing gives the right answer for every input ----------------------

TEST( HandleCross, NullIn_NullOut )
{
	EXPECT_EQ( ( FakeWeapon * )0, Cross( 0 ) );
}

TEST( HandleCross, WeaponComesBackAsItself )
{
	FakeWeapon weapon;
	FakeHandleEntity *pTableEntry = &weapon; // what the handle table holds

	EXPECT_EQ( &weapon, Cross( pTableEntry ) );
}

TEST( HandleCross, BaseEntityThatIsNotAWeaponComesBackNull )
{
	FakeBase base;
	EXPECT_EQ( ( FakeWeapon * )0, Cross( &base ) );
}

TEST( HandleCross, ParallelHierarchyComesBackNull )
{
	FakeStandalone standalone;
	EXPECT_EQ( ( FakeWeapon * )0, Cross( &standalone ) );
}

// --- and the old pattern demonstrably does not ---------------------------------

TEST( HandleCross, OldPatternMistypesTheParallelHierarchy )
{
	FakeStandalone standalone;

	// The cast answers non-NULL for an object that is not a weapon at all.
	// This is the wrong-vtable pointer shell_standalone_hierarchy.cpp scoped a
	// capability down to avoid; the crossing is what makes it NULL instead.
	FakeWeapon *pWrong = OldPatternCast( &standalone );

	// Non-NULL, for an object that is not a weapon at all — and not even the
	// standalone's own address, because the downcast adjusted by an offset
	// that belongs to a class this object never was.
	EXPECT_NE( ( FakeWeapon * )0, pWrong );
	EXPECT_NE( ( void * )&standalone, ( void * )pWrong );

	EXPECT_EQ( ( FakeWeapon * )0, Cross( &standalone ) );
}

TEST( HandleCross, CastAndCrossingAgreeOnARealWeapon )
{
	// The cast is NOT wrong for a genuine weapon — an adjusting downcast lands
	// on the right subobject — which is exactly why the bug survived: every
	// entity was a CBaseEntity, so the unchecked path was always right. The
	// crossing must keep that answer bit-identical or it is a regression.
	FakeWeapon weapon;
	FakeHandleEntity *pTableEntry = &weapon;

	EXPECT_EQ( OldPatternCast( pTableEntry ), Cross( pTableEntry ) );
	EXPECT_EQ( &weapon, Cross( pTableEntry ) );
}

} // namespace

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

class SdkHandleEntity                                   // ~ IHandleEntity
{
public:
	virtual ~SdkHandleEntity() {}
	virtual int RefEHandle() const { return -1; }
};

class SdkGameEntity;                                    // ~ IGameEntity, which IS CBaseEntity

// ONE ACCESSOR SINCE plan_server_native_v2 T1.3. Stock iserverunknown.h's
// GetBaseEntity() is typed by the entity face, and on the server that face is
// CBaseEntity with IGameEntity its alias (D1). The patch's second accessor,
// GetGameEntity(), is gone: the two partitioned the world identically once no
// SDK CBaseEntity was left on the server, and the SDK's tree this suite used to
// model beside the standalone one (SdkBaseEntity, SdkWeapon, the "step 5"
// standalone) went with it. What stays is what the crossing still does: adjust
// from the handle table's address to the face's, inside the object.
class SdkServerUnknown : public SdkHandleEntity         // ~ IServerUnknown
{
public:
	virtual SdkGameEntity *GetBaseEntity() = 0;
};

class SdkServerEntity : public SdkServerUnknown         // ~ IServerEntity
{
public:
	virtual int GetModelIndex() const { return 0; }
};

// --- the boundary, which is NOT in that chain --------------------------------

class SdkGameEntity                                     // ~ IGameEntity
{
public:
	virtual ~SdkGameEntity() {}

	// Answers with a value only this class knows, so a test can tell a correct
	// crossing from a plausible pointer by what comes back rather than by
	// inspecting a vtable it has no way to read.
	virtual int WhoAreYou() const = 0;
};

// --- the parallel hierarchy: four bases, the boundary last -------------------

class SdkNetworkable                                    // ~ IServerNetworkable
{
public:
	virtual ~SdkNetworkable() {}
	virtual SdkHandleEntity *GetEntityHandle() = 0;

	// IServerNetworkable declares this too, with the same signature as
	// IServerUnknown's, so a standalone entity's ONE override answers both.
	virtual SdkGameEntity *GetBaseEntity() = 0;

	int m_nNetPadding[ 3 ];
};

class SdkCollideable                                    // ~ ICollideable
{
public:
	virtual ~SdkCollideable() {}
	virtual int GetSolid() const { return 0; }
	int m_nCollidePadding[ 3 ];
};

class SdkStandalone : public SdkServerEntity, public SdkNetworkable,
                      public SdkCollideable, public SdkGameEntity
{
public:
	virtual SdkGameEntity   *GetBaseEntity()   { return this; }
	virtual SdkHandleEntity *GetEntityHandle() { return this; }

	virtual int WhoAreYou() const { return 0x5A11D; }
};

// The world: in the entity list as an IServerEntity, with no entity face at
// all. CWorld's GetBaseEntity() answers NULL, and that NULL is the partition's
// other arm.
class SdkWorld : public SdkServerEntity, public SdkNetworkable
{
public:
	virtual SdkGameEntity   *GetBaseEntity()   { return 0; }
	virtual SdkHandleEntity *GetEntityHandle() { return static_cast< SdkServerEntity * >( this ); }
};

// What the entity list holds and what CBaseHandle::Get() hands back: the
// IHandleEntity view, and nothing else.
static SdkHandleEntity *TableEntry( SdkStandalone *pEnt ) { return pEnt; }
static SdkHandleEntity *TableEntry( SdkWorld *pEnt )      { return static_cast< SdkServerEntity * >( pEnt ); }

// THE KNOWN-GOOD CROSSING, and it is known-good because the COMPILER performs
// it: from the concrete type, where the layout is a fact rather than an
// assumption, static_cast adjusts by whatever the base's offset really is.
// Nothing in the crossing under test is consulted to produce this.
static SdkGameEntity *KnownGood( SdkStandalone *pEnt )
{
	return static_cast< SdkGameEntity * >( pEnt );
}

// What CHandle<IGameEntity>::Get() would do WITHOUT the specialization.
static SdkGameEntity *OldPatternCastToGameEntity( SdkHandleEntity *pHandleEntity )
{
	return ( SdkGameEntity * )pHandleEntity;
}

static SdkGameEntity *CrossToGame( SdkHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToGameEntity< SdkGameEntity, SdkServerEntity >( pHandleEntity );
}

static long ByteDelta( const void *a, const void *b )
{
	return ( long )( ( const char * )a - ( const char * )b );
}

// --- the premise, asserted rather than assumed --------------------------------

TEST( HandleCrossGameEntity, StandaloneSubobjectAddressesActuallyDiffer )
{
	SdkStandalone standalone;

	// If this ever became false — a layout change, a reordered base list — every
	// other test in this suite would still pass while proving nothing, because
	// a cast and a crossing would agree by accident. So it is checked first and
	// on its own.
	EXPECT_NE( ( const void * )TableEntry( &standalone ),
	           ( const void * )KnownGood( &standalone ) )
		<< "the mock's IGameEntity subobject is at offset zero, so this suite "
		   "cannot distinguish a crossing from a cast";
}

// --- the crossing agrees with the compiler, on the object where it matters ----

TEST( HandleCrossGameEntity, StandaloneCrossesToItsOwnBoundaryView )
{
	SdkStandalone standalone;

	SdkGameEntity *pGood    = KnownGood( &standalone );
	SdkGameEntity *pCrossed = CrossToGame( TableEntry( &standalone ) );

	EXPECT_EQ( pGood, pCrossed )
		<< "the crossing and the compiler disagree about where this entity's "
		   "game-entity view is: compiler says " << ( const void * )pGood
		<< ", crossing says " << ( const void * )pCrossed
		<< ", off by " << ByteDelta( pCrossed, pGood ) << " bytes; the handle "
		   "table's address for it is " << ( const void * )TableEntry( &standalone );
}

// And the pointer works, which is the half an address comparison cannot show.
TEST( HandleCrossGameEntity, TheCrossedPointerDispatchesIntoTheRightClass )
{
	SdkStandalone standalone;

	SdkGameEntity *pCrossed = CrossToGame( TableEntry( &standalone ) );
	ASSERT_NE( ( SdkGameEntity * )0, pCrossed );

	EXPECT_EQ( 0x5A11D, pCrossed->WhoAreYou() )
		<< "a call through the crossed pointer landed somewhere else";
}

// --- and the cast does not -----------------------------------------------------

TEST( HandleCrossGameEntity, OldPatternMistypesTheStandalone )
{
	SdkStandalone standalone;

	SdkGameEntity *pGood  = KnownGood( &standalone );
	SdkGameEntity *pWrong = OldPatternCastToGameEntity( TableEntry( &standalone ) );

	// Non-NULL, resolves, and points at the FRONT of the object rather than at
	// the boundary subobject — so it is a pointer that looks entirely valid and
	// whose every virtual call goes into IServerEntity's slots instead. The
	// call is deliberately not made: dispatching through it is the bug, not a
	// thing to demonstrate by doing.
	EXPECT_NE( ( SdkGameEntity * )0, pWrong );
	EXPECT_NE( pGood, pWrong )
		<< "the cast happened to land on the boundary subobject, so this mock "
		   "no longer reproduces the hazard";

	EXPECT_EQ( pGood, CrossToGame( TableEntry( &standalone ) ) );
}

// --- everything else the crossing has to answer --------------------------------

TEST( HandleCrossGameEntity, NullIn_NullOut )
{
	EXPECT_EQ( ( SdkGameEntity * )0, CrossToGame( 0 ) );
}

TEST( HandleCrossGameEntity, TheWorldComesBackNull )
{
	// The other arm of the partition. The world has no entity face and says so
	// with the same virtual, so the crossing needs no second rule.
	SdkWorld world;
	EXPECT_EQ( ( SdkGameEntity * )0, CrossToGame( TableEntry( &world ) ) );
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
	return SourceSharp_SDKEntityFromNetworkable< SdkServerEntity >( pNetworkable );
}

TEST( SdkEntityResolver, NetworkableNullIn_NullOut )
{
	EXPECT_EQ( ( SdkGameEntity * )0, ResolveNetworkable( 0 ) );
}

TEST( SdkEntityResolver, AStandaloneIsNotAnSdkEntity )
{
	SdkStandalone standalone;

	// The premise: the raw accessor answers, and the two faces really are at
	// different addresses, so the route through GetEntityHandle() is doing
	// work rather than being a no-op that would pass either way.
	EXPECT_NE( ( SdkGameEntity * )0, standalone.GetBaseEntity() )
		<< "the standalone's GetBaseEntity() answers NULL, so this case cannot "
		   "tell a resolver that asks the partition from one that does not";
	EXPECT_NE( ( const void * )static_cast< SdkNetworkable * >( &standalone ),
	           ( const void * )static_cast< SdkServerEntity * >( &standalone ) )
		<< "the mock's two faces coincide, so this case cannot distinguish "
		   "the crossing from a cast";

	EXPECT_EQ( ( SdkGameEntity * )0, ResolveNetworkable( &standalone ) )
		<< "the resolver handed back " << ( const void * )ResolveNetworkable( &standalone )
		<< " for an entity that is not in the SDK's tree";
}

TEST( SdkEntityResolver, TheWorldIsNotAnSdkEntityEither )
{
	SdkWorld world;
	EXPECT_EQ( ( SdkGameEntity * )0, ResolveNetworkable( &world ) );
}

TEST( SdkEntityResolver, TheServerPartitionRecognisesAStandaloneAndNotTheWorld )
{
	SdkStandalone standalone;
	SdkWorld world;

	EXPECT_TRUE( SourceSharp_Partition< SdkServerEntity >::IsOutsideTheSdk(
		static_cast< SdkServerUnknown * >( &standalone ) ) )
		<< "the partition failed to recognise a standalone entity";

	EXPECT_FALSE( SourceSharp_Partition< SdkServerEntity >::IsOutsideTheSdk(
		static_cast< SdkServerUnknown * >( &world ) ) )
		<< "the partition called the world a standalone; it has no entity face";
}

TEST( SdkEntityResolver, TheDeltaIsWhatTheLiveCrashSaw )
{
	// gameinterface.cpp:3328 ProcessUsercmds died with rdi = the ss_player's
	// IGameEntity view, PAST the handle table's address for the same object.
	// The number is the layout's, not this test's; what is asserted is that
	// there IS one, so the shape being reproduced is the shape that crashed.
	SdkStandalone standalone;

	const long delta = ByteDelta( standalone.GetBaseEntity(),
	                              static_cast< SdkHandleEntity * >( &standalone ) );

	EXPECT_GT( delta, 0 )
		<< "the face coincides with the handle table's address; the live "
		   "hazard was a pointer PAST it";
}

} // namespace

// =============================================================================
// Phase 13 step 1, THE CLIENT HALF — SourceSharp_CrossHandleToClientGameEntity
// and SourceSharp_Partition< SourceSharpClientPartition >.
//
// The server's crossing gets from the handle table to the boundary with ONE
// structural cast and one virtual, because everything in the server's entity
// list is an IServerEntity and the three interfaces are a single chain.
//
// THE CLIENT'S TABLE IS WIDER THAN ITS ENTITY INTERFACE. CDefaultClientRenderable,
// CDetailModel and C_BaseTempEntity implement IClientUnknown and stop there, and
// the engine's static props are in the same table without being game objects at
// all. So the cast the server makes would be a LIE about three of them, and the
// GetGameEntity() call after it would land past the end of that object's primary
// vtable. FakeDetailModel above is that object, and it is what makes this suite
// able to fail in a way the server's cannot.
//
// THE KNOWN-GOOD ANSWER HERE IS dynamic_cast, i.e. the compiler's own runtime
// verdict on what an object really is. The crossing must agree with it on every
// input while never performing one -- and, crucially, must agree with it on the
// object where a static_cast disagrees.
//
// PROVEN RED. Two mutations, both in handle_cross.h, both reported in the
// commit message:
//
//   (a) SourceSharp_CrossHandleToClientGameEntity's body replaced with
//       `return (TGameEntity *)pHandleEntity;` -- the cast the specialisation
//       exists to replace.
//   (b) SourceSharp_Partition< SourceSharpClientPartition >::IsOutsideTheSdk
//       replaced with `return false;` -- the old SourceSharpNoPartition
//       behaviour, i.e. this commit reverted.
// =============================================================================

namespace
{

static FakeHandleEntity *ClientTableEntry( FakeStandalone *pEnt )   { return pEnt; }
static FakeHandleEntity *ClientTableEntry( FakeBase *pEnt )         { return pEnt; }
static FakeHandleEntity *ClientTableEntry( FakeDetailModel *pEnt )  { return pEnt; }

// The compiler's own crossing, from the concrete type, where the layout is a
// fact rather than an assumption. Nothing in the code under test is consulted.
static FakeGameEntity *ClientKnownGood( FakeStandalone *pEnt )
{
	return static_cast< FakeGameEntity * >( pEnt );
}

// What CHandle<IGameEntity>::Get() would do on the client WITHOUT the
// specialisation.
static FakeGameEntity *ClientOldPatternCast( FakeHandleEntity *pHandleEntity )
{
	return ( FakeGameEntity * )pHandleEntity;
}

static FakeGameEntity *ClientCrossToGame( FakeHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToClientGameEntity< FakeGameEntity, FakeUnknown >( pHandleEntity );
}

static long ClientByteDelta( const void *a, const void *b )
{
	return ( long )( ( const char * )a - ( const char * )b );
}

// --- the two premises, asserted first and on their own ------------------------

TEST( ClientHandleCross, StandaloneSubobjectAddressesActuallyDiffer )
{
	FakeStandalone standalone;

	// If this ever became false -- a reordered base list, a layout change --
	// every other test here would still pass while proving nothing, because a
	// cast and a crossing would agree by accident.
	EXPECT_NE( ( const void * )ClientTableEntry( &standalone ),
	           ( const void * )ClientKnownGood( &standalone ) )
		<< "the mock's boundary subobject is at offset zero, so this suite "
		   "cannot distinguish a crossing from a cast";
}

TEST( ClientHandleCross, ANonEntityUnknownIsGenuinelyNotAnEntity )
{
	// THE SECOND PREMISE, and the one the server's suite has no equivalent of.
	// A cast down to the entity face manufactures a pointer; the compiler's own
	// answer is NULL. Both are computed here, and they disagree -- which is
	// exactly the disagreement the crossing must not inherit.
	FakeDetailModel detail;
	FakeUnknown *pUnknown = &detail;

	FakeClientEntity *pTruth = dynamic_cast< FakeClientEntity * >( pUnknown );
	FakeClientEntity *pCast  = ( FakeClientEntity * )pUnknown;

	EXPECT_EQ( ( FakeClientEntity * )0, pTruth )
		<< "the mock's non-entity unknown really is an entity, so this suite "
		   "cannot show what a cast costs";
	EXPECT_NE( ( FakeClientEntity * )0, pCast )
		<< "the cast produced NULL, so it is not manufacturing anything and "
		   "the contrast below is empty";

	// And the object says so itself, which is the route the crossing takes.
	EXPECT_EQ( ( FakeClientEntity * )0, pUnknown->GetIClientEntity() );
}

// --- the crossing agrees with the compiler on every input ---------------------

TEST( ClientHandleCross, NullIn_NullOut )
{
	EXPECT_EQ( ( FakeGameEntity * )0, ClientCrossToGame( 0 ) );
}

TEST( ClientHandleCross, StandaloneCrossesToItsOwnBoundaryView )
{
	FakeStandalone standalone;

	FakeGameEntity *pGood    = ClientKnownGood( &standalone );
	FakeGameEntity *pCrossed = ClientCrossToGame( ClientTableEntry( &standalone ) );

	EXPECT_EQ( pGood, pCrossed )
		<< "the crossing and the compiler disagree about where this entity's "
		   "game-entity view is: compiler says " << ( const void * )pGood
		<< ", crossing says " << ( const void * )pCrossed
		<< ", off by " << ClientByteDelta( pCrossed, pGood ) << " bytes; the "
		   "handle table's address for it is "
		<< ( const void * )ClientTableEntry( &standalone );
}

TEST( ClientHandleCross, TheCrossedPointerDispatchesIntoTheRightClass )
{
	FakeStandalone standalone;

	FakeGameEntity *pCrossed = ClientCrossToGame( ClientTableEntry( &standalone ) );
	ASSERT_NE( ( FakeGameEntity * )0, pCrossed );

	EXPECT_EQ( 0xC11E7, pCrossed->WhoAreYouOnTheClient() )
		<< "a call through the crossed pointer landed somewhere else";
}

TEST( ClientHandleCross, AnSdkClientEntityComesBackNull )
{
	// The other arm of the partition. A C_BaseEntity is not on the boundary and
	// says so with the same virtual.
	FakeBase base;
	EXPECT_EQ( ( FakeGameEntity * )0, ClientCrossToGame( ClientTableEntry( &base ) ) );
}

TEST( ClientHandleCross, ANonEntityUnknownComesBackNullWithoutBeingCast )
{
	// THE ONE THE SERVER'S SUITE CANNOT HAVE. A static prop or a detail model in
	// the handle table is not an entity on either hierarchy, and the crossing
	// has to say so without ever pretending it is one.
	FakeDetailModel detail;

	EXPECT_EQ( ( FakeGameEntity * )0, ClientCrossToGame( ClientTableEntry( &detail ) ) );
}

// --- and the cast does not ------------------------------------------------------

TEST( ClientHandleCross, OldPatternMistypesTheStandalone )
{
	FakeStandalone standalone;

	FakeGameEntity *pGood  = ClientKnownGood( &standalone );
	FakeGameEntity *pWrong = ClientOldPatternCast( ClientTableEntry( &standalone ) );

	EXPECT_NE( ( FakeGameEntity * )0, pWrong );
	EXPECT_NE( pGood, pWrong )
		<< "the cast happened to land on the boundary subobject, so this mock "
		   "no longer reproduces the hazard";

	EXPECT_EQ( pGood, ClientCrossToGame( ClientTableEntry( &standalone ) ) );
}

// --- the partition as a resolver, on the client ---------------------------------

// A standalone entity whose GetBaseEntity() answers the boundary view instead of
// NULL — the shape the retired ss_step5_sim_client canary produced live, and the
// one a resolver must still refuse.
class FakeStandaloneStep5 : public FakeClientEntity, public FakeGameEntity
{
public:
	virtual FakeBase *GetBaseEntity()
	{
		return reinterpret_cast< FakeBase * >( static_cast< FakeGameEntity * >( this ) );
	}
	virtual FakeClientEntity *GetIClientEntity() { return this; }
	virtual FakeGameEntity *GetGameEntity() { return this; }
	virtual int WhoAreYouOnTheClient() const { return 0xC11E7; }
};

static FakeBase *ClientResolve( FakeUnknown *pUnknown )
{
	return SourceSharp_SDKEntityFromUnknown< SourceSharpClientPartition >( pUnknown );
}

TEST( ClientSdkEntityResolver, NullIn_NullOut )
{
	EXPECT_EQ( ( FakeBase * )0, ClientResolve( 0 ) );
}

TEST( ClientSdkEntityResolver, AnSdkEntityResolvesToItself )
{
	// The answer that must not change. Every one of the twenty-seven client
	// sites that moved onto SDKEntityFromUnknown depends on this staying
	// bit-identical to the raw call for the SDK's tree.
	FakeBase base;
	EXPECT_EQ( &base, ClientResolve( &base ) );
	EXPECT_EQ( base.GetBaseEntity(), ClientResolve( &base ) );
}

TEST( ClientSdkEntityResolver, ANonEntityResolvesToNull )
{
	FakeDetailModel detail;
	EXPECT_EQ( ( FakeBase * )0, ClientResolve( &detail ) );
}

TEST( ClientSdkEntityResolver, Step5StandaloneStillResolvesToNull )
{
	// THE ONE THAT MATTERS, and the one that was impossible before this commit.
	// At step 5 the raw call answers non-NULL and the resolver must still answer
	// NULL, because a standalone entity is not on the SDK's tree and no amount
	// of step 5 makes it one.
	FakeStandaloneStep5 standalone;

	EXPECT_EQ( ( FakeBase * )0, ClientResolve( &standalone ) )
		<< "the resolver handed back " << ( const void * )ClientResolve( &standalone )
		<< " for an entity that is not on the SDK's tree; the caller's NULL "
		   "check will pass and it will dispatch through that address";
}

TEST( ClientSdkEntityResolver, Step5RawGetBaseEntityIsTheHazard )
{
	// The premise, asserted rather than assumed: if this stopped being true the
	// test above would pass while proving nothing.
	FakeStandaloneStep5 standalone;

	FakeBase *pRaw = standalone.GetBaseEntity();

	EXPECT_NE( ( FakeBase * )0, pRaw )
		<< "the step-5 mock still answers NULL, so this suite cannot tell a "
		   "resolver that asks the partition from one that does not";

	EXPECT_NE( ( const void * )static_cast< FakeHandleEntity * >( &standalone ),
	           ( const void * )pRaw )
		<< "the mock's boundary subobject is at offset zero, so the wrong "
		   "pointer would coincide with the right one";
}

TEST( ClientSdkEntityResolver, TheWeaponCrossingAsksThePartitionToo )
{
	// A managed weapon at step 5: the pointer resolves, so only the partition
	// can stop it. This is the client's version of the case that put
	// handle_cross.h itself on the step-5 list.
	class FakeStandaloneWeaponStep5 : public FakeStandaloneStep5
	{
	public:
		virtual FakeWeapon *MyCombatWeaponPointer() { return 0; }
	};

	FakeStandaloneWeaponStep5 standalone;

	EXPECT_NE( ( FakeBase * )0, standalone.GetBaseEntity() )
		<< "the step-5 mock answers NULL, so this case proves nothing";

	EXPECT_EQ( ( FakeWeapon * )0,
		Cross( static_cast< FakeHandleEntity * >( &standalone ) ) );
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
//   ... SourceSharp_CrossHandleToClientGameEntity    0 server / 129 client
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

namespace {

std::string ReadSource( const char *pszPath )
{
	std::ifstream f( pszPath );
	EXPECT_TRUE( f.good() ) << pszPath << " did not open";
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

struct LiveCrossing_t
{
	const char *pszSymbol;
	const char *pszCallerFile;
	const char *pszCallText;
};

const LiveCrossing_t kLiveCrossings[] = {
	// The server's handle -> IGameEntity crossing. It was pinned in
	// baseentity.h's CHandle<IGameEntity>::Get, which was in every server TU's
	// prelude (102 translation units). That header is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6, so the row now reads
	// the crossing where the server still makes it: variant_t::Entity in our
	// shell_globals.cpp.
	{ "SourceSharp_CrossHandleToGameEntity", "sourcesharp/game/server/shell_globals.cpp",
	  "return SourceSharp_CrossHandleToGameEntity< IGameEntity, IServerEntity >( SourceSharp_EntityFromHandle( eVal ) );" },

	// The client's. A separate function rather than a template parameter,
	// because the server's cast would be wrong over there -- see the header.
	{ "SourceSharp_CrossHandleToClientGameEntity", "game/client/c_baseentity.h",
	  "return SourceSharp_CrossHandleToClientGameEntity< IGameEntity, IClientUnknown >( CBaseHandle::Get() );" },

	// (The partitioning crossing's row read game/server/entitylist.h, the SDK
	// entity list's resolver. That header is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6; the row is retired.)

	// (shell_mapparse.cpp's SourceSharp_SDKEntityFromNetworkable row went with
	// CreateEntityByName, its only caller, in plan_server_native_v2 T2: no
	// server object called that function, and the resolver answers NULL for
	// every server object anyway. The mock facts above still drive it.)
};

const size_t kNumLiveCrossings =
	sizeof( kLiveCrossings ) / sizeof( kLiveCrossings[ 0 ] );

} // namespace

TEST( HandleCrossConsumers, EveryLiveCrossingIsDefinedAndCalled )
{
	const std::string strHeader = ReadSource( "sourcesharp/game/shared/handle_cross.h" );
	ASSERT_FALSE( strHeader.empty() )
		<< "handle_cross.h did not load. If it has been DELETED on the strength "
		   "of plan_shim.md's ruling row, that row is wrong -- read the section "
		   "comment above for what was measured and with what.";

	for ( size_t i = 0; i < kNumLiveCrossings; i++ )
	{
		const LiveCrossing_t &c = kLiveCrossings[ i ];
		SCOPED_TRACE( c.pszSymbol );

		EXPECT_NE( strHeader.find( c.pszSymbol ), std::string::npos )
			<< c.pszSymbol << " is no longer defined in handle_cross.h, and "
			<< c.pszCallerFile << " still calls it.";

		const std::string strCaller = ReadSource( c.pszCallerFile );
		ASSERT_FALSE( strCaller.empty() ) << c.pszCallerFile;
		EXPECT_NE( strCaller.find( c.pszCallText ), std::string::npos )
			<< c.pszCallerFile << " no longer calls " << c.pszSymbol
			<< ". If the crossing genuinely went, this row goes WITH it in the "
			   "same commit -- but check first: a (T*) cast in its place is the "
			   "wrong-vtable bug this header exists to prevent, and it compiles.";
	}
}

TEST( HandleCrossConsumers, TheWeaponCrossingIsTheOneWithNoCallerYet )
{
	// The honest other half, so this section is not just a defence.
	// SourceSharp_CrossHandleToWeapon really does have 0 live TUs naming it on
	// either side -- it is the ONLY part of this header that does -- and it is
	// kept deliberately rather than overlooked.
	//
	// c_baseentity.h DECLARES the CHandle<C_BaseCombatWeapon>::Get()
	// specialisation and does not define it when armed, because a definition would instantiate this template eagerly in
	// every TU and ask the entity for MyCombatWeaponPointer -- a header error
	// everywhere. The body is Phase 13's. So the template is the mechanism
	// waiting for that body, and what makes that checkable rather than a claim
	// is that the declaration is present WITHOUT a definition.
	const std::string strHeader = ReadSource( "sourcesharp/game/shared/handle_cross.h" );
	ASSERT_FALSE( strHeader.empty() );
	EXPECT_NE( strHeader.find( "SourceSharp_CrossHandleToWeapon" ), std::string::npos );

	// The server side's declaration was in game/server/baseentity.h, which is
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in
	// P6; that half is retired with its subject.
	const std::string s = ReadSource( "game/client/c_baseentity.h" );
	ASSERT_FALSE( s.empty() );
	EXPECT_NE( s.find( "template<> C_BaseCombatWeapon *CHandle<C_BaseCombatWeapon>::Get() const;" ),
	           std::string::npos )
		<< "the weapon-handle specialisation is no longer DECLARED in "
		   "c_baseentity.h. If it has grown a definition, "
		   "SourceSharp_CrossHandleToWeapon is instantiated in every TU and this "
		   "test's premise has changed; if it has gone entirely, the template it "
		   "stands on can go too.";
}


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
class FakeShellFirstBase
{
public:
	virtual ~FakeShellFirstBase() {}
	int m_nPadding[ 6 ];
};

class FakeShell : public FakeShellFirstBase, public FakeClientEntity
{
public:
	virtual FakeBase *GetBaseEntity() { return 0; }
	virtual FakeClientEntity *GetIClientEntity() { return this; }
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
	return SourceSharp_CrossHandleToClientShell< FakeShell >( pHit,
		[ &table ]( FakeHandleEntity * ) { ++table.nAsked; return table.pBound; },
		[ bStaticProp ]( FakeHandleEntity * ) { return bStaticProp; } );
}

} // namespace

TEST( ClientShellCross, TheShellsHandleViewIsNotAtOffsetZero )
{
	// The premise: without it the identity check below cannot tell an adjusted
	// compare from a raw one.
	FakeShell shell;
	EXPECT_NE( ( const void * )&shell, ( const void * )static_cast< FakeHandleEntity * >( &shell ) );
}

TEST( ClientShellCross, ABoundShellComesBackAsItself )
{
	FakeShell shell;
	FakeShellTable table;
	table.pBound = &shell;

	FakeShell *pFound = CrossToShell( static_cast< FakeHandleEntity * >( &shell ), table );
	EXPECT_EQ( &shell, pFound )
		<< "a trace that hit a bound shell resolved to " << ( const void * )pFound
		<< "; the ground resolve and the impact control points read this answer";
	EXPECT_EQ( 1, table.nAsked );
}

TEST( ClientShellCross, NullIn_NullOut )
{
	FakeShellTable table;
	EXPECT_EQ( ( FakeShell * )0, CrossToShell( 0, table ) );
	EXPECT_EQ( 0, table.nAsked ) << "a NULL hit was looked up";
}

TEST( ClientShellCross, AnUnboundHandleComesBackNull )
{
	FakeShell shell;
	FakeShellTable table;   // nothing bound
	EXPECT_EQ( ( FakeShell * )0, CrossToShell( static_cast< FakeHandleEntity * >( &shell ), table ) );
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

	EXPECT_EQ( ( FakeShell * )0, CrossToShell( static_cast< FakeHandleEntity * >( &detail ), table ) );
	EXPECT_EQ( 1, table.nAsked );
}

TEST( ClientShellCross, AStaticPropIsNeverLookedUp )
{
	FakeShell shell;
	FakeShellTable table;
	table.pBound = &shell;

	EXPECT_EQ( ( FakeShell * )0, CrossToShell( static_cast< FakeHandleEntity * >( &shell ), table, true ) );
	EXPECT_EQ( 0, table.nAsked ) << "a static prop's handle was handed to the slot table";
}
