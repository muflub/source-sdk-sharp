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
// This is what makes the comparison mean something. Every other entity in the
// game is a CBaseEntity, where the boundary sits at offset zero, both answers
// coincide and a cast is indistinguishable from a crossing.
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

class SdkBaseEntity;                                    // ~ CBaseEntity
class SdkGameEntity;                                    // ~ IGameEntity
class SdkWeapon;                                        // ~ CBaseCombatWeapon

class SdkServerUnknown : public SdkHandleEntity         // ~ IServerUnknown
{
public:
	virtual SdkBaseEntity *GetBaseEntity() = 0;
	virtual SdkGameEntity *GetGameEntity() { return 0; }
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

// --- the SDK's tree: the boundary is not implemented at all ------------------

class SdkBaseEntity : public SdkServerEntity
{
public:
	virtual SdkBaseEntity *GetBaseEntity() { return this; }

	// The single NULL the whole partition rests on, today. At Phase 13 step 5
	// this returns `this` instead, and the crossing follows it there without
	// being edited, because it asks.
	virtual SdkGameEntity *GetGameEntity() { return 0; }

	// CBaseEntity really does declare this, which is why the weapon crossing can
	// call it on GetBaseEntity()'s result without a cast.
	virtual SdkWeapon *MyCombatWeaponPointer() { return 0; }

	int m_nSdkPadding[ 6 ];
};

// --- the parallel hierarchy: four bases, the boundary last -------------------

class SdkNetworkable                                    // ~ IServerNetworkable
{
public:
	virtual ~SdkNetworkable() {}
	virtual SdkHandleEntity *GetEntityHandle() = 0;

	// IServerNetworkable really does declare this too, with the same signature
	// as IServerUnknown's and the comment "Only used by game code" -- so a
	// standalone entity's ONE override answers both, and resolving through the
	// networkable is exactly as exposed to step 5 as resolving through the
	// unknown. UTIL_Remove and the tools interface take this route.
	virtual SdkBaseEntity *GetBaseEntity() = 0;

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
	virtual SdkBaseEntity  *GetBaseEntity() { return 0; }
	virtual SdkGameEntity  *GetGameEntity() { return this; }
	virtual SdkHandleEntity *GetEntityHandle() { return this; }

	virtual int WhoAreYou() const { return 0x5A11D; }
};

// What the entity list holds and what CBaseHandle::Get() hands back: the
// IHandleEntity view, and nothing else.
static SdkHandleEntity *TableEntry( SdkStandalone *pEnt ) { return pEnt; }
static SdkHandleEntity *TableEntry( SdkBaseEntity *pEnt )  { return pEnt; }

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

TEST( HandleCrossGameEntity, SdkEntityComesBackNull )
{
	// The other arm of the partition. A CBaseEntity is not on the boundary and
	// says so with the same virtual, so the crossing needs no second rule — and
	// when step 5 changes that NULL to `this`, this test changes with the
	// hierarchy rather than the crossing.
	SdkBaseEntity base;
	EXPECT_EQ( ( SdkGameEntity * )0, CrossToGame( TableEntry( &base ) ) );
}

TEST( HandleCrossGameEntity, OnAnSdkEntityACastWouldHaveBeenRight )
{
	// Why the bug survived, stated as a test: for a hierarchy whose boundary is
	// at offset zero the two answers coincide, and this whole suite would be
	// vacuous if it were written against one. It is written against the
	// standalone above for exactly that reason.
	SdkBaseEntity base;
	EXPECT_EQ( ( const void * )TableEntry( &base ),
	           ( const void * )static_cast< SdkServerEntity * >( &base ) );
}

} // namespace

// =============================================================================
// The partition as a RESOLVER — SourceSharp_SDKEntityFromUnknown.
//
// The crossing above answers "where is this entity's game-entity view". This
// answers the opposite question — "is this entity in the SDK's tree, and if so
// where" — and it is the question 86 enumerated call sites ask by reading
// GetBaseEntity() and comparing the answer against NULL.
//
// WHAT MAKES THIS SUITE NON-VACUOUS. Today GetBaseEntity() already answers NULL
// for a standalone entity, so a resolver that asks the partition and one that
// does not agree on every input, and a suite built on today's hierarchy would
// pass with the partition line deleted. So the mock below is a standalone whose
// GetBaseEntity() returns its own IGameEntity subobject. On that object the two
// resolvers disagree, and the disagreement is the bug — printed as an address
// rather than left as a vtable nobody can inspect.
//
// NOTHING IN THE TREE ANSWERS THAT WAY ANY MORE, and that is exactly why the
// mock has to. `ss_step5_sim` used to make the live CStandaloneEntity do it and
// was retired at plan_entity.md step 4g; the shape it simulated is still what
// any future non-NULL GetBaseEntity would produce, and this suite is now the
// only thing that would notice a resolver going back to asking the wrong arm.
//
// PROVEN RED. Deleting the `GetGameEntity() != 0` test from
// SourceSharp_SDKEntityFromUnknown fails Step5StandaloneStillResolvesToNull,
// and the message names the address the caller would have dispatched through.
// =============================================================================

namespace
{

// A standalone entity AS IT WILL BE AT STEP 5: GetBaseEntity() no longer
// answers NULL, it answers the IGameEntity view of the same object. Nothing
// else about it changes — same bases, same order, same GetGameEntity().
class SdkStandaloneStep5 : public SdkServerEntity, public SdkNetworkable,
                           public SdkCollideable, public SdkGameEntity
{
public:
	virtual SdkBaseEntity *GetBaseEntity()
	{
		return reinterpret_cast< SdkBaseEntity * >( static_cast< SdkGameEntity * >( this ) );
	}
	virtual SdkGameEntity   *GetGameEntity()   { return this; }
	virtual SdkHandleEntity *GetEntityHandle() { return this; }

	virtual int WhoAreYou() const { return 0x5A11D; }
};

static SdkBaseEntity *Resolve( SdkServerUnknown *pUnknown )
{
	return SourceSharp_SDKEntityFromUnknown< SdkServerEntity >( pUnknown );
}

TEST( SdkEntityResolver, NullIn_NullOut )
{
	EXPECT_EQ( ( SdkBaseEntity * )0, Resolve( 0 ) );
}

TEST( SdkEntityResolver, AnSdkEntityResolvesToItself )
{
	// The answer that must not change. Every caller of GetContainingEntity,
	// CBaseEntity::Instance, gEntList.GetBaseEntity and EntityFromEntityHandle
	// depends on this staying bit-identical to the raw call for the SDK's tree.
	SdkBaseEntity base;
	EXPECT_EQ( &base, Resolve( &base ) );
	EXPECT_EQ( base.GetBaseEntity(), Resolve( &base ) );
}

TEST( SdkEntityResolver, TodaysStandaloneResolvesToNull )
{
	// True with or without the partition, because today's GetBaseEntity()
	// already answers NULL. Recorded so the suite is honest about which of its
	// cases can distinguish anything: this one cannot, and the next one can.
	SdkStandalone standalone;
	EXPECT_EQ( ( SdkBaseEntity * )0, Resolve( &standalone ) );
	EXPECT_EQ( ( SdkBaseEntity * )0, standalone.GetBaseEntity() );
}

TEST( SdkEntityResolver, Step5StandaloneStillResolvesToNull )
{
	// THE ONE THAT MATTERS. At step 5 the raw call answers non-NULL and the
	// resolver must still answer NULL, because a standalone entity is not in
	// the SDK's tree and no amount of step 5 makes it one.
	SdkStandaloneStep5 standalone;

	EXPECT_EQ( ( SdkBaseEntity * )0, Resolve( &standalone ) )
		<< "the resolver handed back " << ( const void * )Resolve( &standalone )
		<< " for an entity that is not in the SDK's tree; the caller's NULL "
		   "check will pass and it will dispatch through that address";
}

TEST( SdkEntityResolver, Step5RawGetBaseEntityIsTheHazard )
{
	// The premise, asserted rather than assumed: if this ever stopped being
	// true the test above would pass while proving nothing, exactly as a suite
	// written against today's hierarchy does.
	SdkStandaloneStep5 standalone;

	SdkBaseEntity *pRaw = standalone.GetBaseEntity();

	EXPECT_NE( ( SdkBaseEntity * )0, pRaw )
		<< "the step-5 mock still answers NULL, so this suite cannot tell a "
		   "resolver that asks the partition from one that does not";

	// And it is not merely non-NULL, it is a DIFFERENT ADDRESS from the one the
	// handle table holds — the 24-byte delta the live crash printed in rdi.
	EXPECT_NE( ( const void * )static_cast< SdkHandleEntity * >( &standalone ),
	           ( const void * )pRaw )
		<< "the mock's IGameEntity subobject is at offset zero, so the wrong "
		   "pointer would coincide with the right one";
}

// The networkable face of the same object, which is a SEPARATE base and so a
// separate function. If SourceSharp_SDKEntityFromNetworkable ever gets
// "simplified" into a static_cast to the partition, this is what catches it:
// the mock's IServerNetworkable and IServerEntity subobjects are at different
// offsets, exactly as the real ones are.
static SdkBaseEntity *ResolveNetworkable( SdkNetworkable *pNetworkable )
{
	return SourceSharp_SDKEntityFromNetworkable< SdkServerEntity >( pNetworkable );
}

// An SDK entity that is also networkable, so the resolver has a positive case
// to answer as well as a negative one.
class SdkNetworkedBaseEntity : public SdkBaseEntity, public SdkNetworkable
{
public:
	virtual SdkBaseEntity   *GetBaseEntity()   { return this; }
	virtual SdkGameEntity   *GetGameEntity()   { return 0; }
	virtual SdkHandleEntity *GetEntityHandle() { return this; }
};

TEST( SdkEntityResolver, NetworkableNullIn_NullOut )
{
	EXPECT_EQ( ( SdkBaseEntity * )0, ResolveNetworkable( 0 ) );
}

TEST( SdkEntityResolver, AnSdkEntityResolvesToItselfThroughTheNetworkable )
{
	SdkNetworkedBaseEntity ent;
	EXPECT_EQ( static_cast< SdkBaseEntity * >( &ent ),
		ResolveNetworkable( static_cast< SdkNetworkable * >( &ent ) ) );
}

TEST( SdkEntityResolver, Step5StandaloneResolvesToNullThroughTheNetworkable )
{
	SdkStandaloneStep5 standalone;

	// The premise: the two faces really are at different addresses, so the
	// route through GetEntityHandle() is doing work rather than being a
	// no-op that would pass either way.
	EXPECT_NE( ( const void * )static_cast< SdkNetworkable * >( &standalone ),
	           ( const void * )static_cast< SdkServerEntity * >( &standalone ) )
		<< "the mock's two faces coincide, so this case cannot distinguish "
		   "the crossing from a cast";

	EXPECT_EQ( ( SdkBaseEntity * )0, ResolveNetworkable( &standalone ) );
}

// --- the WEAPON crossing, which is itself on the step-5 list ------------------
//
// handle_cross.h:70 is the last entry in nullflip_verbose.txt's server column,
// and it belongs there: SourceSharp_CrossHandleToWeapon used to read
// GetBaseEntity() and NULL-check the answer, so at step 5 a managed weapon in a
// CHandle<CBaseCombatWeapon> slot would come back non-NULL and
// MyCombatWeaponPointer() would dispatch through the wrong subobject -- which
// is the exact bug the crossing exists to prevent, arriving through the
// crossing.
//
// The weapon mocks at the top of this file have no partition to ask (they are
// the CLIENT's shape), so this case is built on the step-5 standalone instead,
// which does.

class SdkWeapon : public SdkBaseEntity
{
public:
	virtual SdkWeapon *MyCombatWeaponPointer() { return this; }
};

// A standalone entity at step 5 that ALSO answers MyCombatWeaponPointer, which
// is what a managed weapon is: the pointer resolves, so only the partition can
// stop it.
class SdkStandaloneWeaponStep5 : public SdkStandaloneStep5
{
public:
	virtual SdkWeapon *MyCombatWeaponPointer() { return 0; }
};

template< class TPartition >
static SdkWeapon *CrossToWeapon( SdkHandleEntity *pHandleEntity )
{
	return SourceSharp_CrossHandleToWeapon<
		SdkWeapon, TPartition, SdkServerUnknown >( pHandleEntity );
}

TEST( SdkEntityResolver, TheWeaponCrossingAsksThePartitionToo )
{
	SdkStandaloneWeaponStep5 standalone;

	// Without the partition -- the client's shape, and what the server's used to
	// be -- the step-5 value comes back non-NULL and the crossing goes on to
	// call a virtual through it. Asserted so the case is known to be live.
	EXPECT_NE( ( SdkBaseEntity * )0, standalone.GetBaseEntity() )
		<< "the step-5 mock answers NULL, so this case proves nothing";

	// With it, NULL, which is the answer every weapon handle has always got for
	// an entity that is not on the SDK's tree.
	EXPECT_EQ( ( SdkWeapon * )0,
		CrossToWeapon< SdkServerEntity >( static_cast< SdkHandleEntity * >( &standalone ) ) );
}

TEST( SdkEntityResolver, TheWeaponCrossingStillFindsARealWeapon )
{
	// The answer that must not change. A partition that made everything NULL
	// would pass the test above and break every weapon in the game.
	SdkWeapon weapon;
	EXPECT_EQ( &weapon,
		CrossToWeapon< SdkServerEntity >( static_cast< SdkHandleEntity * >( &weapon ) ) );

	SdkBaseEntity notAWeapon;
	EXPECT_EQ( ( SdkWeapon * )0,
		CrossToWeapon< SdkServerEntity >( static_cast< SdkHandleEntity * >( &notAWeapon ) ) );
}

TEST( SdkEntityResolver, TheServerPartitionRecognisesAStandaloneAndOnlyThat )
{
	// This test used to assert the OPPOSITE of half of itself: that
	// SourceSharpNoPartition -- the tag the client passed when it had no
	// GetGameEntity() to ask -- answered false for a standalone entity, which
	// is the recorded absence of a check rather than a check. The tag is gone
	// (Phase 13's revised step 1), so what is left is the contrast that was
	// always the point: the partition says yes to exactly the objects that are
	// not on the SDK's tree, and no to the ones that are.
	SdkStandaloneWeaponStep5 standalone;
	SdkWeapon weapon;

	EXPECT_TRUE( SourceSharp_Partition< SdkServerEntity >::IsOutsideTheSdk(
		static_cast< SdkServerUnknown * >( &standalone ) ) )
		<< "the partition failed to recognise a standalone entity -- and this "
		   "one is a STEP-5 standalone, so GetBaseEntity() cannot be what "
		   "answered";

	EXPECT_FALSE( SourceSharp_Partition< SdkServerEntity >::IsOutsideTheSdk(
		static_cast< SdkServerUnknown * >( &weapon ) ) )
		<< "the partition called an SDK entity foreign; every weapon handle in "
		   "the game would come back NULL";
}

TEST( SdkEntityResolver, TheDeltaIsWhatTheLiveCrashSaw )
{
	// gameinterface.cpp:3328 ProcessUsercmds died with rdi = the ss_player's
	// IGameEntity view, PAST the handle table's address for the same object.
	// The number is the layout's, not this test's; what is asserted is that
	// there IS one, so the shape being reproduced is the shape that crashed.
	SdkStandaloneStep5 standalone;

	const long delta = ByteDelta( standalone.GetBaseEntity(),
	                              static_cast< SdkHandleEntity * >( &standalone ) );

	EXPECT_GT( delta, 0 )
		<< "the step-5 value coincides with the handle table's address; the "
		   "live hazard was a pointer PAST it";
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
//   ... SourceSharp_SDKEntityFromNetworkable         1 server /   0 client
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

	// And the one SHIM translation unit that names a crossing directly rather
	// than through a prelude header, so the pin does not rest entirely on
	// stock's headers.
	{ "SourceSharp_SDKEntityFromNetworkable", "sourcesharp/game/server/shell_mapparse.cpp",
	  "SourceSharp_SDKEntityFromNetworkable< IServerEntity >( pNetwork )" },
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

