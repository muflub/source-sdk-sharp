//========= SourceSharp — one datadesc slot, two different casts =============//
//
// entity_funcslot.h split DEFINE_FUNCTION_RAW's single C-style cast in two:
//
//     the CLASS conversion   D::* -> ROOT::*      now a checked static probe
//     the SIGNATURE pun      (void) -> (Data &)   now an explicit reinterpret
//
// The reason that is worth doing is entirely invisible in review, because a
// C-style cast does not say which cast it chose. It chooses static_cast while
// the class relationship holds and reinterpret_cast the moment it stops
// holding, and the second one compiles. Phase 13 step 4 is the moment it stops
// holding, for 47 sites in the server that all look exactly the same before and
// after.
//
// So this file pins the three properties the split is standing on, in a model
// with the same shapes as the real one and no engine anywhere in it:
//
//   1. THE PUN IS LOSSLESS. What goes into the slot comes back out. Not just
//      the first word that saverestore.cpp's EXTRACT_INPUTFUNC_FUNCTIONPTR
//      compares -- the whole value, so that a member pointer put in as a Think
//      and taken back out as a Think still calls the right function on the
//      right object, for a non-virtual, a virtual and an inherited member.
//
//   2. THE PUN IS ONLY SAFE BECAUSE THE CLASS IS THE SAME. The representation
//      is { pointer, this-adjustment }, and the adjustment is measured from a
//      particular base. This asserts the hazard is REAL rather than
//      theoretical: for a class whose ROOT is a secondary base, the converted
//      member pointer carries a NON-ZERO adjustment, and a value punned without
//      the conversion carries zero -- two different values, one of which calls
//      through the wrong subobject. If that assertion ever passes trivially
//      (both zero) the split is protecting nothing and the test says so.
//
//   3. THE CHECK IS THE CONVERSION ITSELF. EntityFuncSlot's probe is not a
//      stand-in for the class conversion, it is that conversion applied to a
//      null member pointer, so anything that makes the pun meaningless makes
//      the probe ill-formed. The ill-formed half cannot be asserted from inside
//      a running test -- it is a compile error -- so buildcpptests compiles
//      this same file four more times with -DENTITY_FUNCSLOT_NOCOMPILE=<n> and
//      requires each one to FAIL. n=1 unrelated class, n=2 private base,
//      n=3 ambiguous base, n=4 virtual base.
//
//=============================================================================//
#include <string.h>
#include "gtest/gtest.h"

#include "../../public/entity_funcslot.h"

namespace
{

//-----------------------------------------------------------------------------
// The shapes datamap.h has: a root the slot is typed against, a slot signature
// nothing resembles, and entities deriving from the root.
//-----------------------------------------------------------------------------
struct Data_t { int value; };

class CRoot
{
public:
	virtual ~CRoot() {}
	virtual int Tag() const { return 1; }
	int m_iCalled;
	int m_iArg;
	CRoot() : m_iCalled( 0 ), m_iArg( 0 ) {}

	void RootThink( void ) { m_iCalled += 100; }
};

typedef void (CRoot::*Slot_t)( Data_t &data );   // stands for inputfunc_t
typedef void (CRoot::*Base_t)( void );           // stands for BASEPTR
typedef void (CRoot::*Touch_t)( CRoot *pOther ); // stands for ENTITYFUNCPTR

class CThing : public CRoot
{
public:
	virtual int Tag() const { return 2; }

	void Think( void )              { m_iCalled += 1; }
	virtual void VThink( void )     { m_iCalled += 2; }
	void TouchIt( CRoot *pOther )   { m_iCalled += 4; m_iArg = pOther ? pOther->Tag() : -1; }
};

class CDerivedThing : public CThing
{
public:
	virtual int Tag() const { return 3; }
	virtual void VThink( void ) { m_iCalled += 8; }
};

// A second, unrelated base placed FIRST, so CRoot is a secondary base at a
// non-zero offset. Nothing in the game tree looks like this -- that is the
// point: it is the shape the checked conversion exists to keep out.
class CUnrelatedFirst
{
public:
	virtual ~CUnrelatedFirst() {}
	virtual void Filler() {}
	int m_iPadding;
};

class CSecondaryRoot : public CUnrelatedFirst, public CRoot
{
public:
	void Think( void ) { m_iCalled += 16; }
};

// How saverestore.cpp reads the slot: the first machine word, nothing else.
void *FirstWord( const void *p )
{
	void *w = 0;
	memcpy( &w, p, sizeof( w ) );
	return w;
}

} // namespace


//-----------------------------------------------------------------------------
// 1. The pun is lossless -- non-virtual member.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, NonVirtualMemberSurvivesTheRoundTrip )
{
	Base_t pfn = static_cast< Base_t >( &CThing::Think );
	Slot_t slot = EntityFuncSlot< CRoot, Slot_t, CThing >( pfn );

	// what saverestore.cpp compares
	EXPECT_EQ( FirstWord( &pfn ), FirstWord( &slot ) );
	// and the whole value, which is what makes the read-back safe
	EXPECT_EQ( 0, memcmp( &pfn, &slot, sizeof( slot ) ) );

	Base_t back = reinterpret_cast< Base_t >( slot );
	CThing t;
	CRoot *pRoot = &t;
	( pRoot->*back )();
	EXPECT_EQ( 1, t.m_iCalled );
}

//-----------------------------------------------------------------------------
// 1b. ...a VIRTUAL member. This is the one a byte comparison alone would not
//     catch: the stored word is a vtable offset rather than an address, and it
//     has to still dispatch on the dynamic type after the round trip.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, VirtualMemberStillDispatchesAfterTheRoundTrip )
{
	Base_t pfn = static_cast< Base_t >( &CThing::VThink );
	Slot_t slot = EntityFuncSlot< CRoot, Slot_t, CThing >( pfn );
	Base_t back = reinterpret_cast< Base_t >( slot );

	CThing base;
	CDerivedThing derived;

	( static_cast< CRoot * >( &base )->*back )();
	EXPECT_EQ( 2, base.m_iCalled );

	// dispatched, not bound: the override runs
	( static_cast< CRoot * >( &derived )->*back )();
	EXPECT_EQ( 8, derived.m_iCalled );
}

//-----------------------------------------------------------------------------
// 1c. ...a member INHERITED from the root, which is what DEFINE_FUNCTION(
//     SUB_Remove ) is: the function is declared on CBaseEntity itself.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, InheritedMemberSurvivesTheRoundTrip )
{
	Base_t pfn = static_cast< Base_t >( &CThing::RootThink );
	Slot_t slot = EntityFuncSlot< CRoot, Slot_t, CThing >( pfn );
	Base_t back = reinterpret_cast< Base_t >( slot );

	CThing t;
	( static_cast< CRoot * >( &t )->*back )();
	EXPECT_EQ( 100, t.m_iCalled );
}

//-----------------------------------------------------------------------------
// 1d. ...and a signature that is nothing like the slot's, which is the whole
//     reason the slot is punned rather than converted.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, ForeignSignatureSurvivesTheRoundTrip )
{
	Touch_t pfn = static_cast< Touch_t >( &CThing::TouchIt );
	Slot_t slot = EntityFuncSlot< CRoot, Slot_t, CThing >( pfn );
	Touch_t back = reinterpret_cast< Touch_t >( slot );

	CThing t;
	CDerivedThing other;
	( static_cast< CRoot * >( &t )->*back )( &other );
	EXPECT_EQ( 4, t.m_iCalled );
	EXPECT_EQ( 3, t.m_iArg );
}

//-----------------------------------------------------------------------------
// 2. The hazard is real. A member pointer CONVERTED to the root differs from
//    the same one PUNNED to the root whenever the root is not at offset zero --
//    and the difference is the this-adjustment, which is what a wrong-class
//    reinterpret throws away.
//
//    If this ever starts passing with both adjustments zero, EXPECT_NE below
//    fails and the file stops claiming to protect anything.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, ConversionCarriesAnAdjustmentAPunWouldLose )
{
	typedef void (CSecondaryRoot::*Own_t)( void );
	Own_t own = &CSecondaryRoot::Think;

	// what EntityFuncSlot does: convert, then pun the signature
	Base_t converted = static_cast< Base_t >( own );
	Slot_t good = EntityFuncSlot< CRoot, Slot_t, CSecondaryRoot >( converted );

	// what a single C-style cast does once the class relationship is gone:
	// take the bytes and relabel them
	Slot_t bad;
	ASSERT_EQ( sizeof( bad ), sizeof( own ) );
	memcpy( &bad, &own, sizeof( bad ) );

	EXPECT_NE( 0, memcmp( &good, &bad, sizeof( good ) ) )
		<< "root is at offset zero here, so the pun and the conversion agree "
		   "and this test is not exercising the hazard any more";

	// and the converted one is the one that works
	Base_t back = reinterpret_cast< Base_t >( good );
	CSecondaryRoot s;
	CRoot *pRoot = &s;
	( pRoot->*back )();
	EXPECT_EQ( 16, s.m_iCalled );

	// the offset the pun would have dropped is genuinely non-zero
	EXPECT_NE( static_cast< void * >( pRoot ),
	           static_cast< void * >( static_cast< CUnrelatedFirst * >( &s ) ) );
}

//-----------------------------------------------------------------------------
// 3. Sizes. The static_assert inside EntityFuncSlot is the loud version of
//    this; asserting it here as well is what makes the failure readable when a
//    compiler sizes member pointers by inheritance model.
//-----------------------------------------------------------------------------
TEST( EntityFuncSlot, SlotAndHandlerAreTheSameShape )
{
	EXPECT_EQ( sizeof( Slot_t ), sizeof( Base_t ) );
	EXPECT_EQ( sizeof( Slot_t ), sizeof( Touch_t ) );
	EXPECT_EQ( sizeof( Slot_t ), sizeof( void (CSecondaryRoot::*)( void ) ) );
}


//-----------------------------------------------------------------------------
// The four shapes that MUST NOT COMPILE. buildcpptests compiles this file once
// per value and requires g++ to fail; a build that succeeds is reported as the
// failure it is.
//-----------------------------------------------------------------------------
#if defined( ENTITY_FUNCSLOT_NOCOMPILE )

namespace
{
	class CStranger                      { public: virtual ~CStranger() {} void Fn( void ) {} };
	class CPrivately : private CRoot     { public: void Fn( void ) {} };
	class CMiddleA : public CRoot        {};
	class CMiddleB : public CRoot        {};
	class CAmbiguous : public CMiddleA, public CMiddleB { public: void Fn( void ) {} };
	class CVirtually : public virtual CRoot { public: void Fn( void ) {} };
}

Slot_t NoCompileProbe()
{
#if ENTITY_FUNCSLOT_NOCOMPILE == 1
	// not a CRoot at all -- Phase 13 step 4's shape
	return EntityFuncSlot< CRoot, Slot_t, CStranger >(
		(Base_t)( &CStranger::Fn ) );
#elif ENTITY_FUNCSLOT_NOCOMPILE == 2
	// a CRoot, but privately: the conversion is not the caller's to make
	return EntityFuncSlot< CRoot, Slot_t, CPrivately >(
		(Base_t)( &CPrivately::Fn ) );
#elif ENTITY_FUNCSLOT_NOCOMPILE == 3
	// two CRoot subobjects: "the" adjustment does not exist.
	//
	// reinterpret_cast, not the macros' C-style cast, in this arm and the next:
	// for an ambiguous or virtual base the C-style cast is ITSELF an error, and
	// clang stops there without instantiating EntityFuncSlot -- so the refusal
	// would be this line's, not entity_funcslot.h's probe, and the
	// must-not-compile check (which requires the header's error) would be
	// evidence of nothing. gcc reports both. The relabelling reaches the probe
	// under either compiler.
	return EntityFuncSlot< CRoot, Slot_t, CAmbiguous >(
		reinterpret_cast< Base_t >( &CAmbiguous::Fn ) );
#elif ENTITY_FUNCSLOT_NOCOMPILE == 4
	// a virtual CRoot: the adjustment is not a constant and a member pointer
	// cannot carry it
	return EntityFuncSlot< CRoot, Slot_t, CVirtually >(
		reinterpret_cast< Base_t >( &CVirtually::Fn ) );
#else
#error "ENTITY_FUNCSLOT_NOCOMPILE must be 1..4"
#endif
}

#endif // ENTITY_FUNCSLOT_NOCOMPILE
