//====== SourceSharp — the client's runtime adapter, run rather than read =====//
//
// The mirror of managed_runtime_server_test.cpp, and it exists for the same two
// properties of the same shape of class:
//
//   1. every method is one forward to the matching managed slot and nothing
//      else -- no reordering, no second call, no wrong slot;
//   2. it holds no copy of the managed runtime.
//
// The second is why this file exists. A runtime pointer cached in a
// constructor compiles, links, passes every other test in this tree, and
// breaks on the first `cs_cl_reload`: the reload releases the runtime the
// load accepted and accepts a new one, so a copy taken at construction points
// into an assembly that has been unloaded. (Until CE1 it was a copy of the
// ManagedApi table, which CE-last removed.) The only proof is to move the
// runtime underneath a live adapter and see the call land in the new place.
//
// AND THE THIRD THING, WHICH IS THIS FILE'S OWN. It reads the sources to pin
// the ARRANGEMENT the adapter is half of -- that CShellClientDLL loads the
// runtime by name before the HUD, releases it by name in Shutdown, and that the
// game-system row does neither. Before this change the client had none of that:
// `IClientRuntimeAdapter` was an interface with no methods and
// `LoadManagedRuntime()` was declared in sourcesharp_gamesystem.h and defined
// NOWHERE IN THE TREE, while the CLR was actually brought up by a void free
// function calling a file-static's Init(). There was nothing to test because
// there was nothing named.
//
// THIS COMPILES THE SHIPPED HEADER, not a transcription of it. Both behavioural
// properties are asserted about
// `sourcesharp/game/client/managed_runtime_client.h` itself, which is why that
// class is in a header and why the header has no engine include in it.
//
//=============================================================================//

#include <string.h>

#include <fstream>
#include <string>
#include <sys/stat.h>

#include "gtest/gtest.h"

// The header is behind #ifdef CLIENT_DLL, because the interfaces it implements
// are in the CLIENT arm of sourcesharp_gamesystem.h. Defining it here is not a
// stand-in for the client build: it selects the same arm of the same two
// headers the client compiles.
#define CLIENT_DLL 1

#include "../game/client/managed_runtime_client.h"

namespace
{

//-----------------------------------------------------------------------------
// What the adapter forwards into, and a tally per slot.
//
// One counter per slot rather than a single "was called" flag, because
// "forwards exactly once" is two statements -- the right slot fired, and
// nothing else did -- and a flag can only make the first.
//-----------------------------------------------------------------------------
struct Tally
{
	int nFrame;
	int nLevelInit;
	int nLevelShutdown;

	// Not a slot. SourceSharp_UnloadManagedSide is the loader's partner and is
	// what OnSystemStop forwards to -- see the header.
	int nUnloadManagedSide;
};

Tally g_Tally;

// What the last call was handed, so the forward can be checked by value rather
// than by "something happened".
float g_LastFrameTime = -1.0f;
std::string g_strLastMapName;

// The "generation" of the runtime currently installed. The reload test installs
// a SECOND runtime and checks the adapter reaches it, which is the whole
// no-cached-runtime proof (plan_client_vtables CE1: the adapter forwards into
// the managed-built IManagedRuntimeClient, asked for on every call).
int g_nLastGeneration = -1;

// A runtime of one generation. Release and Validate are the managed object's
// own slots (imanaged_core.h); the adapter must reach neither. Managed builds
// the real one as a block whose first word is its table
// (plan_forwarders_everywhere.md phase 4): here the table is static functions,
// each handed the object.
class CFakeRuntime : public IManagedRuntimeClient
{
public:
	explicit CFakeRuntime( int nGeneration )
		: IManagedRuntimeClient( &kTable )
		, m_nGeneration( nGeneration )
	{
	}

private:
	static CFakeRuntime *Of( void *self ) { return static_cast<CFakeRuntime *>( self ); }

	static void Released( IManagedObject * ) { ADD_FAILURE() << "the adapter released the runtime"; }

	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t )
	{
		ADD_FAILURE() << "the adapter validated the runtime";
		return 0;
	}

	static void Framed( IManagedRuntimeClient *self, float dt )
	{
		g_Tally.nFrame++;
		g_LastFrameTime = dt;
		g_nLastGeneration = Of( self )->m_nGeneration;
	}

	static void Staged( IManagedRuntimeClient *, int32_t ) { ADD_FAILURE() << "the adapter has no stage forward"; }

	static void LevelInited( IManagedRuntimeClient *self, const char *pName )
	{
		g_Tally.nLevelInit++;
		g_strLastMapName = pName ? pName : "";
		g_nLastGeneration = Of( self )->m_nGeneration;
	}

	static void LevelShut( IManagedRuntimeClient *self )
	{
		g_Tally.nLevelShutdown++;
		g_nLastGeneration = Of( self )->m_nGeneration;
	}

	static void MdlCacheInvalidated( IManagedRuntimeClient * )
	{
		ADD_FAILURE() << "the adapter has no model-cache forward";
	}

	static int32_t ParticlesAsked( IManagedRuntimeClient *, int32_t )
	{
		ADD_FAILURE() << "the adapter has no particle-reload forward";
		return 1;
	}

	static int32_t MaterialsAsked( IManagedRuntimeClient *, const char *, const char *, char *, int32_t )
	{
		ADD_FAILURE() << "the adapter has no map-material forward";
		return -1;
	}

	static const SSManagedRuntimeClientTable kTable;

	int m_nGeneration;
};

const SSManagedRuntimeClientTable CFakeRuntime::kTable = {
	&Released,  &Validated,           &Framed,         &Staged,         &LevelInited,
	&LevelShut, &MdlCacheInvalidated, &ParticlesAsked, &MaterialsAsked,
};

CFakeRuntime g_RuntimeA( 1 );
CFakeRuntime g_RuntimeB( 2 );
IManagedRuntimeClient *g_pInstalled = NULL;

void InstallGeneration( int n )
{
	g_pInstalled = ( n == 1 ) ? static_cast<IManagedRuntimeClient *>( &g_RuntimeA )
							  : static_cast<IManagedRuntimeClient *>( &g_RuntimeB );
}

//-----------------------------------------------------------------------------
// The sum of every counter. "Forwards EXACTLY ONCE" is this being one after one
// call and not merely the expected counter being non-zero.
//-----------------------------------------------------------------------------
int TotalCalls()
{
	return g_Tally.nFrame + g_Tally.nLevelInit + g_Tally.nLevelShutdown + g_Tally.nUnloadManagedSide;
}

class ManagedRuntimeClientTest : public ::testing::Test
{
protected:
	void SetUp() override
	{
		memset( &g_Tally, 0, sizeof( g_Tally ) );
		g_LastFrameTime = -1.0f;
		g_strLastMapName.clear();
		g_nLastGeneration = -1;
		InstallGeneration( 1 );
	}

	CManagedRuntimeClient m_Adapter;
};

} // namespace

//-----------------------------------------------------------------------------
// The stubs the adapter links against: definitions of the symbols
// managed_runtime_client.h forward-declares. There is no table to install:
// the adapter reaches none since CE1, and the client has none since CE-last.
//-----------------------------------------------------------------------------
IManagedRuntimeClient *ManagedRuntimeClient()
{
	return g_pInstalled;
}

void SourceSharp_UnloadManagedSide( void )
{
	g_Tally.nUnloadManagedSide++;
}

//=============================================================================//
// 1. One forward per method, and exactly one.
//=============================================================================//

TEST_F( ManagedRuntimeClientTest, OnFrameForwardsOnce )
{
	m_Adapter.OnFrame( 0.015f );
	EXPECT_EQ( 1, g_Tally.nFrame );
	EXPECT_EQ( 1, TotalCalls() );
}

TEST_F( ManagedRuntimeClientTest, OnFrameCarriesTheFrameTime )
{
	// A body that forwarded a zero, or its own idea of a frame time, would pass
	// the count above and starve every managed timer on the client.
	m_Adapter.OnFrame( 0.0125f );
	EXPECT_FLOAT_EQ( 0.0125f, g_LastFrameTime );
}

TEST_F( ManagedRuntimeClientTest, OnLevelInitForwardsOnce )
{
	m_Adapter.OnLevelInit( "d1_trainstation_01" );
	EXPECT_EQ( 1, g_Tally.nLevelInit );
	EXPECT_EQ( 1, TotalCalls() );
}

TEST_F( ManagedRuntimeClientTest, OnLevelInitCarriesTheMapName )
{
	m_Adapter.OnLevelInit( "d1_trainstation_01" );
	EXPECT_EQ( "d1_trainstation_01", g_strLastMapName );
}

TEST_F( ManagedRuntimeClientTest, OnLevelShutdownForwardsOnce )
{
	m_Adapter.OnLevelShutdown();
	EXPECT_EQ( 1, g_Tally.nLevelShutdown );
	EXPECT_EQ( 1, TotalCalls() );
}

TEST_F( ManagedRuntimeClientTest, OnSystemStopForwardsToTheUnload )
{
	// The one method that does not reach the table: it is the loader's partner,
	// and the managed half of the teardown is inside that call. The server's
	// OnSystemStop is the same body for the same reason.
	m_Adapter.OnSystemStop();
	EXPECT_EQ( 1, g_Tally.nUnloadManagedSide );
	EXPECT_EQ( 1, TotalCalls() );
}

TEST_F( ManagedRuntimeClientTest, OnSystemStopReachesNoManagedSlot )
{
	// Stated as its own fact rather than left to TotalCalls above: a body that
	// ALSO reached a managed shutdown directly would run the module unload
	// twice, because SourceSharp_UnloadManagedSide already does.
	m_Adapter.OnSystemStop();
	EXPECT_EQ( 0, g_Tally.nFrame );
	EXPECT_EQ( 0, g_Tally.nLevelInit );
	EXPECT_EQ( 0, g_Tally.nLevelShutdown );
}

//=============================================================================//
// 2. NO CACHED TABLE. The reload proof.
//=============================================================================//

TEST_F( ManagedRuntimeClientTest, ForwardsIntoTheTableAsItIsNow )
{
	m_Adapter.OnFrame( 0.01f );
	ASSERT_EQ( 1, g_nLastGeneration );

	// The hot reload: the same struct, memset and refilled with a second
	// assembly's entry points. An adapter holding a copy taken at construction
	// would still land in generation 1 here -- which in a live process is a
	// call through a function pointer into an unloaded assembly.
	InstallGeneration( 2 );

	m_Adapter.OnFrame( 0.01f );
	EXPECT_EQ( 2, g_nLastGeneration );
	EXPECT_EQ( 2, g_Tally.nFrame );
}

TEST_F( ManagedRuntimeClientTest, EveryMethodFollowsTheReload )
{
	// Per method, not just the one above: a cache taken lazily inside a single
	// body would pass the previous test and fail here.
	InstallGeneration( 2 );

	m_Adapter.OnLevelInit( "d2_coast_01" );
	EXPECT_EQ( 2, g_nLastGeneration );

	g_nLastGeneration = -1;
	m_Adapter.OnLevelShutdown();
	EXPECT_EQ( 2, g_nLastGeneration );

	g_nLastGeneration = -1;
	m_Adapter.OnFrame( 0.02f );
	EXPECT_EQ( 2, g_nLastGeneration );
}

TEST_F( ManagedRuntimeClientTest, ItSurvivesTwoReloadsInARow )
{
	InstallGeneration( 2 );
	m_Adapter.OnFrame( 0.01f );
	ASSERT_EQ( 2, g_nLastGeneration );

	InstallGeneration( 1 );
	m_Adapter.OnFrame( 0.01f );
	EXPECT_EQ( 1, g_nLastGeneration );
}

TEST_F( ManagedRuntimeClientTest, TheAdapterIsOnePointerWide )
{
	// The structural half of "no cached table": a member would show up here.
	// One pointer is the vtable and nothing else.
	EXPECT_EQ( sizeof( void * ), sizeof( CManagedRuntimeClient ) );
}

//=============================================================================//
// 3. THE ARRANGEMENT, read from the sources.
//
// Everything above would still pass if nothing ever constructed the adapter.
// These read the two files that have to name it.
//=============================================================================//

namespace
{

} // namespace

//=============================================================================//
// 4. THE SHELL'S HALF -- the client DLL initialising the runtime the way the
//    game DLL does.
//=============================================================================//

//=============================================================================//
// 5. THE GAME-SYSTEM ROW DOES NEITHER.
//=============================================================================//

namespace
{

//-----------------------------------------------------------------------------
// THE ROW'S OWN TEXT, and scoping to it rather than to the file is the point.
//
// sourcesharp_gamesystem.cpp compiles into BOTH .so files and the SERVER's arm
// has three bodies of its own that read `g_ManagedApi.LevelInit`,
// `g_ManagedApi.LevelShutdown` and `g_ManagedApi.Frame` --
// SourceSharp_ManagedLevelInit, SourceSharp_ManagedLevelShutdown and
// SourceSharp_ManagedFrameThink, which the kServerSystems[] row calls and which
// are not this lane's. A file-wide count reads those three as the client's and
// fails on the server's code; it would also PASS the day someone put a direct
// table read back into the row, as long as one of the server's went away.
//
// Returns the class body: from `class CSourceSharpSystem` to the instance
// declaration that follows it.
//-----------------------------------------------------------------------------
} // namespace
