//========= SourceSharp — the shim layer's own interfaces, through forwarders =//
//
// plan_forwarders_everywhere.md phases 1 and 2. Managed calls IShimFrame through
// SSFwd_ShimFrame, a table of forwarders the server's library compiles over
// ishim_frame.h, never through the object's vtable. Each fact calls a slot
// the way managed does (ShimFrameFwd: slot N is field N, the header's order)
// on a recording IShimFrame, and holds what reached it.
//
// The server's library (fwd_server.cpp) is compiled into this binary, as
// fwd_engine_test does; the built library's export set is
// fwd_libraries_isolation_test's.
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_fwd.h"

#include "sourcesharp/game/server/ishim_frame.h"
#include "sourcesharp/game/server/ishim_level.h"
#include "sourcesharp/game/shared/ishim_areas.h"
#include "sourcesharp/game/shared/ishim_areas_client.h"
#include "sourcesharp/game/shared/ishim_console.h"
#include "sourcesharp/game/shared/ishim_root.h"
#include "sourcesharp/game/shared/vtable_hook.h"

#include <stdint.h>
#include <string.h>

namespace
{

// The table's slots, in ishim_frame.h's (and ShimFrameFwd's) order.
enum
{
	kIsSimulatingOnAlternateTicks,
	kTickCount,
	kGetFrameTime,
	kSetFrameTime,
	kSetServerCurTime,
	kPhysicsRunThinkFunctions,
	kServiceEventQueue,
	kUpdateAllClientData,
	kEndGameFrame,
	kFireNetworkPropertyEvents,
	kGamestatsUpdateConnection,
	kDrawAllDebugOverlays,
	kStandalonePlayerCommands,
	kStandaloneUpdate,
	kStandaloneFinish,
	kDeliverToSdkEntities,
	kDrainEntitySlots,
	kBeginModelCacheLock,
	kEndModelCacheLock,
	kShimFrameSlots
};

// Records which method ran last and what it was handed.
class RecordingFrame final : public IShimFrame
{
public:
	int32_t IsSimulatingOnAlternateTicks() override { return Ran( kIsSimulatingOnAlternateTicks ), 1; }
	int32_t TickCount() override { return Ran( kTickCount ), 4242; }
	float GetFrameTime() override { return Ran( kGetFrameTime ), 0.015f; }
	void SetFrameTime( float flFrameTime ) override { Ran( kSetFrameTime ); flSet = flFrameTime; }
	void SetServerCurTime() override { Ran( kSetServerCurTime ); }
	void PhysicsRunThinkFunctions( int32_t bSimulating ) override { Ran( kPhysicsRunThinkFunctions ); nSimulating = bSimulating; }
	void ServiceEventQueue() override { Ran( kServiceEventQueue ); }
	void UpdateAllClientData() override { Ran( kUpdateAllClientData ); }
	void EndGameFrame() override { Ran( kEndGameFrame ); }
	void FireNetworkPropertyEvents() override { Ran( kFireNetworkPropertyEvents ); }
	void GamestatsUpdateConnection() override { Ran( kGamestatsUpdateConnection ); }
	void DrawAllDebugOverlays() override { Ran( kDrawAllDebugOverlays ); }
	void StandalonePlayerCommands() override { Ran( kStandalonePlayerCommands ); }
	void StandaloneUpdate() override { Ran( kStandaloneUpdate ); }
	void StandaloneFinish() override { Ran( kStandaloneFinish ); }
	int32_t DeliverToSdkEntities( const char *pszTarget, const char *pszInput, const char *pszValue ) override
	{
		Ran( kDeliverToSdkEntities );
		pTarget = pszTarget;
		pInput = pszInput;
		pValue = pszValue;
		return 3;
	}
	void DrainEntitySlots() override { Ran( kDrainEntitySlots ); }
	void BeginModelCacheLock() override { Ran( kBeginModelCacheLock ); }
	void EndModelCacheLock() override { Ran( kEndModelCacheLock ); }

	int nLast = -1;
	int nCalls = 0;
	float flSet = 0.0f;
	int32_t nSimulating = -1;
	const char *pTarget = nullptr;
	const char *pInput = nullptr;
	const char *pValue = nullptr;

private:
	void Ran( int nSlot )
	{
		nLast = nSlot;
		nCalls++;
	}
};

const SSForwarderTable &ShimFrameTable()
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_ShimFrame" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

template < typename F >
F Slot( int nSlot )
{
	return reinterpret_cast< F >( ShimFrameTable().ppSlots[ nSlot ] );
}

// Every void, no-argument step, by slot.
const int kVoidSteps[] = {
	kSetServerCurTime, kServiceEventQueue,
	kUpdateAllClientData, kEndGameFrame, kFireNetworkPropertyEvents, kGamestatsUpdateConnection,
	kDrawAllDebugOverlays, kStandalonePlayerCommands,
	kStandaloneUpdate, kStandaloneFinish, kDrainEntitySlots, kBeginModelCacheLock,
	kEndModelCacheLock,
};

} // namespace

TEST( FwdShim, TheServerServesTheFrameTableWithOneSlotPerMethod )
{
	const SSForwarderTable &table = ShimFrameTable();
	ASSERT_NE( nullptr, table.ppSlots );
	EXPECT_EQ( (int32_t)kShimFrameSlots, table.nSlots );
}

TEST( FwdShim, EverySlotIsADistinctFunction )
{
	const SSForwarderTable &table = ShimFrameTable();
	ASSERT_EQ( (int32_t)kShimFrameSlots, table.nSlots );

	for ( int i = 0; i < table.nSlots; i++ )
	{
		EXPECT_NE( nullptr, table.ppSlots[ i ] ) << i;
		for ( int j = 0; j < i; j++ )
			EXPECT_NE( table.ppSlots[ i ], table.ppSlots[ j ] ) << i << " and " << j;
	}
}

TEST( FwdShim, TickCountAndGetFrameTimeAnswerFromTheirOwnMethods )
{
	RecordingFrame frame;

	EXPECT_EQ( 4242, Slot< int32_t ( * )( IShimFrame * ) >( kTickCount )( &frame ) );
	EXPECT_EQ( kTickCount, frame.nLast );

	EXPECT_FLOAT_EQ( 0.015f, Slot< float ( * )( IShimFrame * ) >( kGetFrameTime )( &frame ) );
	EXPECT_EQ( kGetFrameTime, frame.nLast );
}

TEST( FwdShim, SetFrameTimeAndPhysicsRunThinkFunctionsForwardTheirArgument )
{
	RecordingFrame frame;

	Slot< void ( * )( IShimFrame *, float ) >( kSetFrameTime )( &frame, 0.25f );
	EXPECT_EQ( kSetFrameTime, frame.nLast );
	EXPECT_FLOAT_EQ( 0.25f, frame.flSet );

	Slot< void ( * )( IShimFrame *, int32_t ) >( kPhysicsRunThinkFunctions )( &frame, 1 );
	EXPECT_EQ( kPhysicsRunThinkFunctions, frame.nLast );
	EXPECT_EQ( 1, frame.nSimulating );
}

TEST( FwdShim, EachVoidStepReachesItsOwnMethodOnce )
{
	for ( int nSlot : kVoidSteps )
	{
		RecordingFrame frame;
		Slot< void ( * )( IShimFrame * ) >( nSlot )( &frame );
		EXPECT_EQ( nSlot, frame.nLast ) << nSlot;
		EXPECT_EQ( 1, frame.nCalls ) << nSlot;
	}
}

TEST( FwdShim, DeliverToSdkEntitiesForwardsItsThreeStringsInOrder )
{
	RecordingFrame frame;
	const char *pszTarget = "door", *pszInput = "Open", *pszValue = "";

	EXPECT_EQ( 3, ( Slot< int32_t ( * )( IShimFrame *, const char *, const char *, const char * ) >(
		kDeliverToSdkEntities )( &frame, pszTarget, pszInput, pszValue ) ) );

	EXPECT_EQ( kDeliverToSdkEntities, frame.nLast );
	EXPECT_EQ( pszTarget, frame.pTarget );
	EXPECT_EQ( pszInput, frame.pInput );
	EXPECT_EQ( pszValue, frame.pValue );
}

TEST( FwdShim, TheInternalFactoryServesTheFrameTable )
{
	// SS_FWD_SERVES_SERVER is what server.so's link of the library defines:
	// the serve helper answers the table as its slot array and count, which is
	// how managed reaches it through the shim root's Acquire.
	void *pServed = SSFwd_Serve( "SSFwd_ShimFrame" );
	ASSERT_NE( nullptr, pServed );
	EXPECT_EQ( (int32_t)kShimFrameSlots, static_cast< const SSForwarderTable * >( pServed )->nSlots );
	EXPECT_EQ( ShimFrameTable().ppSlots, *static_cast< void *const *const * >( pServed ) );
}

//-----------------------------------------------------------------------------
// A DERIVED AREA (phase 2): IShimEventsServer extends IShimEvents, and its table
// is the whole chain, the base's five slots first, in declaration order, as
// ShimEventsServerFwd reads it.
//-----------------------------------------------------------------------------
namespace
{

class RecordingEvents final : public IShimEventsServer
{
public:
	void ListenForGameEvent( const char *pszName ) override { nLast = 0; pName = pszName; }
	int32_t EventGetInt( void *, const char * ) override { return nLast = 1, 0; }
	float EventGetFloat( void *, const char * ) override { return nLast = 2, 0.0f; }
	int32_t EventGetBool( void *, const char * ) override { return nLast = 3, 0; }
	const char *EventGetString( void *, const char * ) override { return nLast = 4, "four"; }
	void EventSetInt( void *, const char *, int32_t nValue ) override { nLast = 6; nSet = nValue; }
	void EventSetFloat( void *, const char *, float ) override { nLast = 7; }
	void EventSetBool( void *, const char *, int32_t ) override { nLast = 8; }
	void EventSetString( void *, const char *, const char *pszValue ) override { nLast = 9; pName = pszValue; }

	int nLast = -1;
	int32_t nSet = 0;
	const char *pName = nullptr;
};

const SSForwarderTable &EventsServerTable()
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_ShimEventsServer" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

} // namespace

TEST( FwdShim, ADerivedAreasTableIsTheBasesSlotsThenItsOwn )
{
	const SSForwarderTable &table = EventsServerTable();
	// IShimEvents' 5, then IShimEventsServer's 4 setters (EventCreate, EventFire
	// and EventFree went in plan_server_native_v2 T3: IGameEventManager2's
	// forwarders make those calls from managed).
	ASSERT_EQ( 9, table.nSlots );

	RecordingEvents events;
	const char *pszName = "player_death";

	reinterpret_cast< void ( * )( IShimEventsServer *, const char * ) >( table.ppSlots[ 0 ] )( &events, pszName );
	EXPECT_EQ( 0, events.nLast );
	EXPECT_EQ( pszName, events.pName );

	EXPECT_STREQ( "four", reinterpret_cast< const char *( * )( IShimEventsServer *, void *, const char * ) >(
		table.ppSlots[ 4 ] )( &events, nullptr, "k" ) );
	EXPECT_EQ( 4, events.nLast );

	reinterpret_cast< void ( * )( IShimEventsServer *, void *, const char *, int32_t ) >(
		table.ppSlots[ 5 ] )( &events, nullptr, "k", 7 );
	EXPECT_EQ( 6, events.nLast );
	EXPECT_EQ( 7, events.nSet );

	const char *pszValue = "v";
	reinterpret_cast< void ( * )( IShimEventsServer *, void *, const char *, const char * ) >(
		table.ppSlots[ 8 ] )( &events, nullptr, "k", pszValue );
	EXPECT_EQ( 9, events.nLast );
	EXPECT_EQ( pszValue, events.pName );
}

//-----------------------------------------------------------------------------
// THE ROOT (phase 3): native hands its table to managed at load, beside the
// root (ShimRoot_Get), so it is the table managed reaches everything else
// through. Acquire, AcquireInterface, Release, in ishim_root.h's order.
//-----------------------------------------------------------------------------
namespace
{

class RecordingRoot final : public IShimRoot
{
public:
	void *Acquire( const char *pszName ) override
	{
		nLast = 0;
		pName = pszName;
		return this;
	}

	void *AcquireInterface( int32_t nType, const char *pszVersion, char *, int32_t nErrorBytes ) override
	{
		nLast = 1;
		nTypeSaw = nType;
		pName = pszVersion;
		nErrorBytesSaw = nErrorBytes;
		return nullptr;
	}

	void Release() override { nLast = 2; }

	int nLast = -1;
	int32_t nTypeSaw = -1;
	int32_t nErrorBytesSaw = -1;
	const char *pName = nullptr;
};

} // namespace

TEST( FwdShim, TheRootsTableIsAcquireAcquireInterfaceAndRelease )
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_ShimRoot" );
	ASSERT_NE( nullptr, pTable );
	ASSERT_EQ( 3, pTable->nSlots );

	RecordingRoot root;
	const char *pszName = "SourceSharpConsole001";

	EXPECT_EQ( &root, ( reinterpret_cast< void *( * )( IShimRoot *, const char * ) >(
		pTable->ppSlots[ 0 ] )( &root, pszName ) ) );
	EXPECT_EQ( 0, root.nLast );
	EXPECT_EQ( pszName, root.pName );

	reinterpret_cast< void *( * )( IShimRoot *, int32_t, const char *, char *, int32_t ) >(
		pTable->ppSlots[ 1 ] )( &root, 3, pszName, nullptr, 64 );
	EXPECT_EQ( 1, root.nLast );
	EXPECT_EQ( 3, root.nTypeSaw );
	EXPECT_EQ( 64, root.nErrorBytesSaw );

	reinterpret_cast< void ( * )( IShimRoot * ) >( pTable->ppSlots[ 2 ] )( &root );
	EXPECT_EQ( 2, root.nLast );
}

//-----------------------------------------------------------------------------
// THE HAND-SHAPED TABLES' COUNTS: the root, the console, the frame and the
// level, each its forwarder table against the compiler's count for the
// interface. Read off the LAST member, whichever it is, so an append that the
// generated table missed shows as the two numbers parting company. None of the
// four declares a virtual destructor (each header says why), so slot 0 is the
// first method. These held the old generated vtable layouts until
// plan_forwarders_everywhere.md phase 6; the table is what managed reads now.
//-----------------------------------------------------------------------------
namespace
{

void ExpectServedCount( const char *pszTable, int nCompiler, int nExpected )
{
	EXPECT_EQ( nExpected, nCompiler ) << pszTable;
	const SSForwarderTable *pTable = SSFwdServer_Table( pszTable );
	ASSERT_NE( nullptr, pTable ) << pszTable;
	EXPECT_EQ( nCompiler, pTable->nSlots ) << pszTable;
}

} // namespace

TEST( FwdShimCounts, TheRootIsThreeSlots )
{
	// Acquire, AcquireInterface (plan_engineapi_vtables C5, before Release),
	// Release last.
	ExpectServedCount( "SSFwd_ShimRoot", VTableHook::MemberOf( &IShimRoot::Release ).slot + 1, 3 );
}

TEST( FwdShimCounts, TheConsoleIsTwelveSlots )
{
	// The ten console slots of the old engine struct, in its order, then
	// RevertNamedConVar, then FindConVarFlags (plan_server_native_v2 T4e).
	ExpectServedCount( "SSFwd_ShimConsole",
	                   VTableHook::MemberOf( &IShimConsole::FindConVarFlags ).slot + 1, 12 );
}

TEST( FwdShimCounts, TheFrameIsNineteenSlots )
{
	// Seventeen until StandaloneRunFrame split into StandaloneUpdate and
	// StandaloneFinish; eighteen until DeliverToSdkEntities was appended.
	// Nineteen again at plan_server_native_v2 T4e: the three system walks
	// (ServerSystemsPreThink, ServerSystemsPostThink,
	// ServerSystemsPreClientUpdate) retired, DrainEntitySlots,
	// BeginModelCacheLock and EndModelCacheLock appended.
	ExpectServedCount( "SSFwd_ShimFrame",
	                   VTableHook::MemberOf( &IShimFrame::EndModelCacheLock ).slot + 1, 19 );
}

TEST( FwdShimCounts, TheLevelIsThirtyOneSlots )
{
	// Twenty-two until LoadSaveGameState, twenty-three until FileSizeOnPath and
	// ReadFileOnPath, twenty-five until SpawnWorld, twenty-six until
	// IsPrecacheAllowed (plan_server_native_v2 T4a), twenty-seven until
	// SetLoadType (T4e): each appended. Then T4e's systems: the four walks
	// (InitPostEntitySystems, ShutdownPreClearSteamSystems,
	// ShutdownPreEntitySystems, ShutdownPostEntitySystems) retired and the
	// seven system stages appended, PhysicsInit to ResetEntitySlots.
	ExpectServedCount( "SSFwd_ShimLevel", VTableHook::MemberOf( &IShimLevel::ResetEntitySlots ).slot + 1, 31 );
}

//-----------------------------------------------------------------------------
// Every generated shim area's forwarder table, its count against the
// compiler's for the interface (tools/shim-areas; the tables are both sides',
// so the server's library serves the client's areas too).
//-----------------------------------------------------------------------------
#include "shim_areas_forwarders.inc"
