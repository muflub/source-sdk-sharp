//========= SourceSharp — the vphysics callbacks, driven as vphysics drives them ==//
//
// plan_forwarders_everywhere.md phase 5. libss_fwd_<side> builds the objects
// vphysics is handed as its callbacks (fwd_physics.cpp), each deriving the Valve
// interface, in the managed block's reserved words. Each fact builds one in a
// block shaped as managed builds it -- word 0 the table, the reserved words
// after it -- over a recording managed side, calls it through the Valve
// interface's virtuals the way vphysics does, and holds what reached the table.
//
// The server's library (fwd_server.cpp, fwd_physics.cpp) is compiled into this
// binary, as fwd_engine_test compiles it.
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd.h"

#include "vphysics_interface.h"
#include "vcollide_parse.h"
#include "imanaged_physics_callbacks.h"

#include <stdint.h>

namespace
{

// The table's slots, as SSFwd_PhysicsCallbacks lays them out.
enum
{
	kSizeOf,
	kConstructObjectEvent,
	kConstructCollisionEvent,
	kConstructCollisionSolver,
	kConstructConstraintEvent,
	kConstructMotionEvent,
	kConstructKeyHandler,
	kPhysicsCallbacksSlots
};

const SSForwarderTable &Table()
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_PhysicsCallbacks" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

template <typename T>
using ConstructFn = void *(*)( void *, T * );

// What the recording managed side last heard.
struct Heard
{
	int nCalls = 0;
	void *pSelf = nullptr;
	void *pA = nullptr;
	void *pB = nullptr;
	void *pC = nullptr;
	float fl = 0.0f;
	int32_t n = 0;
	const char *psz = nullptr;
};

Heard g_Heard;

void Released( IManagedObject * ) {}

int32_t Validated( IManagedObject *, const uint32_t *, int32_t )
{
	return -1;
}

template <typename T>
void Hear( T *self, void *a = nullptr, void *b = nullptr, void *c = nullptr )
{
	g_Heard.nCalls++;
	g_Heard.pSelf = self;
	g_Heard.pA = a;
	g_Heard.pB = b;
	g_Heard.pC = c;
}

// A block as managed builds it: word 0 the table, then the reserved words the
// library builds its object in.
template <typename TTable>
struct Block
{
	const TTable *pTable;
	alignas( void * ) unsigned char reserved[kManagedPhysicsCallbackWords * sizeof( void * )];
};

} // namespace

TEST( FwdPhysics, TheTableServesSizeOfAndOneConstructPerCallback )
{
	const SSForwarderTable &table = Table();
	ASSERT_NE( nullptr, table.ppSlots );
	EXPECT_EQ( (int32_t)kPhysicsCallbacksSlots, table.nSlots );

	const int nBytes = reinterpret_cast<int ( * )()>( table.ppSlots[kSizeOf] )();
	EXPECT_EQ( int( kManagedPhysicsCallbackWords * sizeof( void * ) ), nBytes )
		<< "the object is a vptr and one pointer, in exactly the words a block reserves";
}

TEST( FwdPhysics, AConstructGivenNoMemoryOrNoTargetBuildsNothing )
{
	Block<SSManagedPhysicsConstraintEventTable> block = {};
	auto construct =
		reinterpret_cast<ConstructFn<IManagedPhysicsConstraintEvent>>( Table().ppSlots[kConstructConstraintEvent] );

	EXPECT_EQ( nullptr, construct( nullptr, reinterpret_cast<IManagedPhysicsConstraintEvent *>( &block ) ) );
	EXPECT_EQ( nullptr, construct( block.reserved, nullptr ) );
}

TEST( FwdPhysics, TheObjectEventReachesTheManagedSideWithTheObject )
{
	static const SSManagedPhysicsObjectEventTable kTable = {
		&Released,
		&Validated,
		[]( IManagedPhysicsObjectEvent *self, void *p )
		{
			Hear( self, p );
			g_Heard.n = 1;
		},
		[]( IManagedPhysicsObjectEvent *self, void *p )
		{
			Hear( self, p );
			g_Heard.n = 2;
		},
	};
	Block<SSManagedPhysicsObjectEventTable> block = { &kTable, {} };

	auto *pEvents = static_cast<IPhysicsObjectEvent *>(
		reinterpret_cast<ConstructFn<IManagedPhysicsObjectEvent>>( Table().ppSlots[kConstructObjectEvent] )(
			block.reserved, reinterpret_cast<IManagedPhysicsObjectEvent *>( &block ) ) );
	ASSERT_EQ( static_cast<void *>( block.reserved ), static_cast<void *>( pEvents ) );

	IPhysicsObject *pObject = reinterpret_cast<IPhysicsObject *>( 0x1234 );
	g_Heard = {};
	pEvents->ObjectWake( pObject );
	EXPECT_EQ( &block, g_Heard.pSelf ) << "the managed side is the block the object was built in";
	EXPECT_EQ( static_cast<void *>( pObject ), g_Heard.pA );
	EXPECT_EQ( 1, g_Heard.n );

	pEvents->ObjectSleep( pObject );
	EXPECT_EQ( 2, g_Heard.n );
}

TEST( FwdPhysics, TheCollisionEventsFrictionKeepsItsArgumentsInOrder )
{
	static const SSManagedPhysicsCollisionEventTable kTable = {
		&Released,
		&Validated,
		[]( IManagedPhysicsCollisionEvent *self, void *e )
		{
			Hear( self, e );
			g_Heard.n = 10;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *e )
		{
			Hear( self, e );
			g_Heard.n = 11;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *o, float fl, int32_t a, int32_t b, void *d )
		{
			Hear( self, o, d );
			g_Heard.fl = fl;
			g_Heard.n = a * 100 + b;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *a, void *b, void *d )
		{
			Hear( self, a, b, d );
			g_Heard.n = 13;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *a, void *b, void *d )
		{
			Hear( self, a, b, d );
			g_Heard.n = 14;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *o, void *f )
		{
			Hear( self, o, f );
			g_Heard.n = 15;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *o, void *f )
		{
			Hear( self, o, f );
			g_Heard.n = 16;
		},
		[]( IManagedPhysicsCollisionEvent *self )
		{
			Hear( self );
			g_Heard.n = 17;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *t, void *o )
		{
			Hear( self, t, o );
			g_Heard.n = 18;
		},
		[]( IManagedPhysicsCollisionEvent *self, void *t, void *o )
		{
			Hear( self, t, o );
			g_Heard.n = 19;
		},
	};
	Block<SSManagedPhysicsCollisionEventTable> block = { &kTable, {} };
	auto *pEvents = static_cast<IPhysicsCollisionEvent *>(
		reinterpret_cast<ConstructFn<IManagedPhysicsCollisionEvent>>( Table().ppSlots[kConstructCollisionEvent] )(
			block.reserved, reinterpret_cast<IManagedPhysicsCollisionEvent *>( &block ) ) );

	IPhysicsObject *pObject = reinterpret_cast<IPhysicsObject *>( 0x10 );
	IPhysicsCollisionData *pData = reinterpret_cast<IPhysicsCollisionData *>( 0x20 );

	g_Heard = {};
	pEvents->Friction( pObject, 2.5f, 7, 9, pData );
	EXPECT_EQ( &block, g_Heard.pSelf );
	EXPECT_EQ( static_cast<void *>( pObject ), g_Heard.pA );
	EXPECT_EQ( static_cast<void *>( pData ), g_Heard.pB );
	EXPECT_FLOAT_EQ( 2.5f, g_Heard.fl );
	EXPECT_EQ( 709, g_Heard.n );

	pEvents->PostSimulationFrame();
	EXPECT_EQ( 17, g_Heard.n );
	pEvents->ObjectLeaveTrigger( pObject, pObject );
	EXPECT_EQ( 19, g_Heard.n );
}

TEST( FwdPhysics, TheSolversAnswersComeBackAsVPhysicsAsksForThem )
{
	static const SSManagedPhysicsCollisionSolverTable kTable = {
		&Released,
		&Validated,
		[]( IManagedPhysicsCollisionSolver *, void *, void *, void *, void * ) -> int32_t { return 3; },
		[]( IManagedPhysicsCollisionSolver *, void *, void *, void *, void *, float dt ) -> int32_t
		{ return dt > 0.5f ? 1 : 0; },
		[]( IManagedPhysicsCollisionSolver *, void *p ) -> bool { return p != nullptr; },
		[]( IManagedPhysicsCollisionSolver *, int32_t n ) -> int32_t { return n + 1; },
		[]( IManagedPhysicsCollisionSolver *, void *, int32_t n ) -> bool { return n > 2; },
	};
	Block<SSManagedPhysicsCollisionSolverTable> block = { &kTable, {} };
	auto *pSolver = static_cast<IPhysicsCollisionSolver *>(
		reinterpret_cast<ConstructFn<IManagedPhysicsCollisionSolver>>( Table().ppSlots[kConstructCollisionSolver] )(
			block.reserved, reinterpret_cast<IManagedPhysicsCollisionSolver *>( &block ) ) );

	IPhysicsObject *pObject = reinterpret_cast<IPhysicsObject *>( 0x30 );
	EXPECT_EQ( 3, pSolver->ShouldCollide( pObject, pObject, nullptr, nullptr ) );
	EXPECT_EQ( 1, pSolver->ShouldSolvePenetration( pObject, pObject, nullptr, nullptr, 0.75f ) );
	EXPECT_TRUE( pSolver->ShouldFreezeObject( pObject ) );
	EXPECT_FALSE( pSolver->ShouldFreezeObject( nullptr ) );
	EXPECT_EQ( 6, pSolver->AdditionalCollisionChecksThisTick( 5 ) );
	EXPECT_TRUE( pSolver->ShouldFreezeContacts( &pObject, 3 ) );
	EXPECT_FALSE( pSolver->ShouldFreezeContacts( &pObject, 1 ) );
}

TEST( FwdPhysics, TheMotionEventAnswersASimresultAndWritesTheVectors )
{
	static const SSManagedMotionEventTable kTable = {
		&Released,
		&Validated,
		[]( IManagedMotionEvent *self, void *c, void *o, float dt, void *pLinear, void *pAngular ) -> int32_t
		{
			Hear( self, c, o );
			static_cast<float *>( pLinear )[0] = dt;
			static_cast<float *>( pAngular )[2] = 2.0f * dt;
			return IMotionEvent::SIM_GLOBAL_FORCE;
		},
	};
	Block<SSManagedMotionEventTable> block = { &kTable, {} };
	auto *pMotion = static_cast<IMotionEvent *>( reinterpret_cast<ConstructFn<IManagedMotionEvent>>(
		Table().ppSlots[kConstructMotionEvent] )( block.reserved, reinterpret_cast<IManagedMotionEvent *>( &block ) ) );

	Vector linear( 0, 0, 0 );
	AngularImpulse angular( 0, 0, 0 );
	IPhysicsMotionController *pController = reinterpret_cast<IPhysicsMotionController *>( 0x40 );
	g_Heard = {};
	EXPECT_EQ( IMotionEvent::SIM_GLOBAL_FORCE, pMotion->Simulate( pController, nullptr, 0.25f, linear, angular ) );
	EXPECT_EQ( static_cast<void *>( pController ), g_Heard.pA );
	EXPECT_FLOAT_EQ( 0.25f, linear.x );
	EXPECT_FLOAT_EQ( 0.5f, angular.z );
}

TEST( FwdPhysics, TheKeyHandlerAndTheConstraintEventForwardTheirArguments )
{
	static const SSManagedVPhysicsKeyHandlerTable kKeys = {
		&Released,
		&Validated,
		[]( IManagedVPhysicsKeyHandler *self, void *d, const char *k, const char * )
		{
			Hear( self, d );
			g_Heard.psz = k;
		},
		[]( IManagedVPhysicsKeyHandler *self, void *d )
		{
			Hear( self, d );
			g_Heard.psz = "defaults";
		},
	};
	Block<SSManagedVPhysicsKeyHandlerTable> keys = { &kKeys, {} };
	auto *pKeys = static_cast<IVPhysicsKeyHandler *>(
		reinterpret_cast<ConstructFn<IManagedVPhysicsKeyHandler>>( Table().ppSlots[kConstructKeyHandler] )(
			keys.reserved, reinterpret_cast<IManagedVPhysicsKeyHandler *>( &keys ) ) );

	int data = 0;
	g_Heard = {};
	pKeys->ParseKeyValue( &data, "contents", "8" );
	EXPECT_EQ( &keys, g_Heard.pSelf );
	EXPECT_EQ( static_cast<void *>( &data ), g_Heard.pA );
	EXPECT_STREQ( "contents", g_Heard.psz );
	pKeys->SetDefaults( &data );
	EXPECT_STREQ( "defaults", g_Heard.psz );

	static const SSManagedPhysicsConstraintEventTable kBroken = {
		&Released,
		&Validated,
		[]( IManagedPhysicsConstraintEvent *self, void *c ) { Hear( self, c ); },
	};
	Block<SSManagedPhysicsConstraintEventTable> broken = { &kBroken, {} };
	auto *pBroken = static_cast<IPhysicsConstraintEvent *>(
		reinterpret_cast<ConstructFn<IManagedPhysicsConstraintEvent>>( Table().ppSlots[kConstructConstraintEvent] )(
			broken.reserved, reinterpret_cast<IManagedPhysicsConstraintEvent *>( &broken ) ) );
	IPhysicsConstraint *pConstraint = reinterpret_cast<IPhysicsConstraint *>( 0x50 );
	pBroken->ConstraintBroken( pConstraint );
	EXPECT_EQ( &broken, g_Heard.pSelf );
	EXPECT_EQ( static_cast<void *>( pConstraint ), g_Heard.pA );
}
