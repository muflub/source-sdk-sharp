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
	explicit CFakeRuntime( int nGeneration ) : IManagedRuntimeClient( &kTable ), m_nGeneration( nGeneration ) {}

private:
	static CFakeRuntime *Of( void *self ) { return static_cast< CFakeRuntime * >( self ); }

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
	static void MdlCacheInvalidated( IManagedRuntimeClient * ) { ADD_FAILURE() << "the adapter has no model-cache forward"; }
	static int32_t ParticlesAsked( IManagedRuntimeClient *, int32_t ) { ADD_FAILURE() << "the adapter has no particle-reload forward"; return 1; }
	static int32_t MaterialsAsked( IManagedRuntimeClient *, const char *, const char *, char *, int32_t ) { ADD_FAILURE() << "the adapter has no map-material forward"; return -1; }

	static const SSManagedRuntimeClientTable kTable;

	int m_nGeneration;
};

const SSManagedRuntimeClientTable CFakeRuntime::kTable = {
	&Released, &Validated, &Framed, &Staged, &LevelInited, &LevelShut,
	&MdlCacheInvalidated, &ParticlesAsked, &MaterialsAsked,
};

CFakeRuntime g_RuntimeA( 1 );
CFakeRuntime g_RuntimeB( 2 );
IManagedRuntimeClient *g_pInstalled = NULL;

void InstallGeneration( int n )
{
	g_pInstalled = ( n == 1 ) ? static_cast< IManagedRuntimeClient * >( &g_RuntimeA )
	                          : static_cast< IManagedRuntimeClient * >( &g_RuntimeB );
}

//-----------------------------------------------------------------------------
// The sum of every counter. "Forwards EXACTLY ONCE" is this being one after one
// call and not merely the expected counter being non-zero.
//-----------------------------------------------------------------------------
int TotalCalls()
{
	return g_Tally.nFrame + g_Tally.nLevelInit + g_Tally.nLevelShutdown
	     + g_Tally.nUnloadManagedSide;
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

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

// CODE ONLY. A source scan that reads comments as code can fail on prose and,
// worse, PASS on a commented-out line -- and every comment in the files below
// names the very calls these tests look for.
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string Read( const std::string &strPath )
{
	std::ifstream f( strPath.c_str(), std::ios::binary );
	if ( !f.is_open() )
		return std::string();
	return StripComments( std::string( ( std::istreambuf_iterator<char>( f ) ),
	                                   std::istreambuf_iterator<char>() ) );
}

int CountOf( const std::string &strHaystack, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHaystack.find( strNeedle ); at != std::string::npos;
	      at = strHaystack.find( strNeedle, at + 1 ) )
	{
		n++;
	}
	return n;
}

std::string ReadShell()
{
	const char *pRoot = SrcRoot();
	if ( pRoot == NULL )
		return std::string();
	return Read( std::string( pRoot ) + "/sourcesharp/game/client/shell_clientdll.cpp" );
}

const char *kSlots[] = { "Frame", "LevelInit", "LevelShutdown", "Shutdown" };
const int kSlotCount = (int)( sizeof( kSlots ) / sizeof( kSlots[ 0 ] ) );

} // namespace

TEST( ManagedRuntimeClientSources, EverySlotIsForwardedToExactlyOnce )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL ) << "source tree not found next to the test binary";

	const std::string strAdapter =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.h" );
	ASSERT_FALSE( strAdapter.empty() );

	// Three of the four, spelled as the call. `Shutdown` is deliberately NOT
	// forwarded from the adapter: SourceSharp_UnloadManagedSide runs the
	// module's unload (UnloadSide; the table's Shutdown slot went at CE-last),
	// and a second route would run managed shutdown twice.
	for ( int i = 0; i < kSlotCount - 1; i++ )
	{
		const std::string strCall = std::string( "->" ) + kSlots[ i ] + "(";
		EXPECT_EQ( 1, CountOf( strAdapter, strCall ) ) << kSlots[ i ];
	}

	EXPECT_EQ( 0, CountOf( strAdapter, std::string( "->Shutdown(" ) ) )
		<< "the adapter must not call the Shutdown slot: "
		   "SourceSharp_UnloadManagedSide already does";
}

TEST( ManagedRuntimeClientSources, TheLoaderHasNoTableCheck )
{
	// plan_client_vtables CE-last, the server's E-last on the client: there is
	// no ManagedApi table, so nothing may check one. A required-slot list or
	// the forced-null control left behind would name slots LoadSide fills for
	// no one; the accessor or the file-static back in the gamesystem would be
	// a second, unvalidated route into managed code beside the areas.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strLoader =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.cpp" );
	const std::string strSys =
		Read( std::string( pRoot ) + "/sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( strLoader.empty() );
	ASSERT_FALSE( strSys.empty() );

	// The controls: the same reads find what still holds in each file.
	EXPECT_EQ( 1, CountOf( strLoader, "SourceSharp_LoadManagedSide()" ) )
		<< "the control: the loader's one call into the shared load sequence";
	EXPECT_EQ( 1, CountOf( strSys, "static bool g_bSourceSharpLoaded = false;" ) )
		<< "the control: the gamesystem's surviving file-static beside where the table was";

	EXPECT_EQ( 0, CountOf( strLoader, "SOURCESHARP_NULL_SLOT" ) )
		<< "the forced-null control is back without a table for it to null";
	EXPECT_EQ( 0, CountOf( strLoader, "ValidateClientApi" ) )
		<< "a table validator is back in the client loader";
	EXPECT_EQ( 0, CountOf( strLoader, "SOURCESHARP_CLIENT_REQUIRED" ) )
		<< "a required-slot list is back in the client loader";
	EXPECT_EQ( 0, CountOf( strSys, "g_ManagedApi" ) )
		<< "the ManagedApi file-static is back in the gamesystem";
	EXPECT_EQ( 0, CountOf( strSys, "SourceSharp_GetManagedApi" ) )
		<< "the ManagedApi accessor is back in the gamesystem";
}

TEST( ManagedRuntimeClientSources, TheForwardsReachTheManagedBuiltRuntime )
{
	// Each of the three is spelled as a call on ManagedRuntimeClient(), and
	// none reaches the table.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strAdapter =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.h" );
	ASSERT_FALSE( strAdapter.empty() );

	for ( int i = 0; i < kSlotCount - 1; i++ )
	{
		const std::string strCall = std::string( "ManagedRuntimeClient()->" ) + kSlots[ i ] + "(";
		EXPECT_EQ( 1, CountOf( strAdapter, strCall ) ) << kSlots[ i ];
	}
	EXPECT_EQ( 0, CountOf( strAdapter, "SourceSharp_GetManagedApi" ) );
}

TEST( ManagedRuntimeClientSources, TheLoaderSeedsTheEngineAndTheGlobalsFromTheEnvironment )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strLoader =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.cpp" );
	ASSERT_FALSE( strLoader.empty() );

	// THE INVERSION, and it is the whole reason IManagedRuntimeEnvironment
	// exists on either side: the shell owns Init, so the shell owns the factory,
	// and the loader ASKS IT rather than fetching its own. A loader that called
	// a global factory here would compile, link, and quietly stop being the
	// thing the shell can substitute for.
	EXPECT_EQ( 1, CountOf( strLoader, "env->GetEngineInterface(" ) );
	EXPECT_EQ( 1, CountOf( strLoader, "env->GetGlobalVars()" ) );

	// And both land where the shared sequence reads them.
	EXPECT_EQ( 1, CountOf( strLoader, "engine = (IVEngineClient *)" ) );
	EXPECT_EQ( 1, CountOf( strLoader, "gpGlobals = &env->GetGlobalVars()" ) );
}

TEST( ManagedRuntimeClientSources, AFailedInitIsFatalAtThePointOfFailure )
{
	// THIS IS A MEASURED FACT AND NOT A STYLE RULE. With a required ManagedApi
	// slot forced null under -nogamedll (before CE-last took the table and its
	// check), a loader that answered NULL
	// and let CShellClientDLL::Init `return false` printed the slot name and
	// then "Stack overflow." and a core dump: the engine has no clean answer
	// for IBaseClientDLL::Init saying no, and the crash is what the reader sees
	// instead of the diagnostic. So every failure path here calls Error()
	// through the shared staged model first.
	//
	// managed_init_failure.h says the same thing in prose about the client, and
	// the server does NOT need it -- CShellGameDll::DLLInit returning false is
	// handled, which is why LoadManagedRuntime( IManagedRuntimeEnvironment * )
	// on the server arm still just returns NULL.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strLoader =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.cpp" );
	ASSERT_FALSE( strLoader.empty() );

	// One helper, and every one of the loader's failure paths goes through it.
	EXPECT_EQ( 1, CountOf( strLoader, "static void SourceSharp_FatalClientInitFailure(" ) );
	// Two since CE-last took the table check: a null environment's adapter
	// and a refused SourceSharp_LoadManagedSide.
	EXPECT_EQ( 2, CountOf( strLoader, "SourceSharp_FatalClientInitFailure(\n" ) );

	// THE SHARED MODEL, not an error path of its own -- which is the whole
	// reason managed_init_failure.h exists.
	EXPECT_EQ( 1, CountOf( strLoader, "SourceSharpInit_Format(" ) );
	EXPECT_EQ( 1, CountOf( strLoader, "kSourceSharpInitStage_" ) );

	// "%s" and not the message as the format: the detail carries a slot name.
	EXPECT_EQ( 1, CountOf( strLoader, "Error( \"%s\", szMessage )" ) );

	// AND EXACTLY ONE `return NULL` LEFT INSIDE THE LOADER: the null-environment
	// guard at the top, which is the CALLER passing nothing rather than a
	// managed init that failed. Every other exit is fatal, because a returned
	// NULL here is a `return false` out of IBaseClientDLL::Init and that is the
	// stack overflow.
	const size_t body = strLoader.find( "IClientRuntimeAdapter *LoadManagedRuntime(" );
	ASSERT_NE( std::string::npos, body );
	EXPECT_EQ( 1, CountOf( strLoader.substr( body ), "return NULL;" ) );
}

TEST( ManagedRuntimeClientSources, TheLoaderLoadsBeforeItHandsTheAdapterOut )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strLoader =
		Read( std::string( pRoot ) + "/sourcesharp/game/client/managed_runtime_client.cpp" );
	ASSERT_FALSE( strLoader.empty() );

	const size_t load    = strLoader.find( "SourceSharp_LoadManagedSide()" );
	const size_t handout = strLoader.find( "new CManagedRuntimeClient()" );

	ASSERT_NE( std::string::npos, load );
	ASSERT_NE( std::string::npos, handout );

	// The load accepts the managed root and every client area at their slot
	// hashes, and only then is the adapter handed out: handing out first would
	// let a caller reach a runtime nobody accepted. (Until CE-last a table
	// validator sat between the two; the acceptance inside the load is the
	// check now.)
	EXPECT_LT( load, handout );
}

//=============================================================================//
// 4. THE SHELL'S HALF -- the client DLL initialising the runtime the way the
//    game DLL does.
//=============================================================================//

TEST( ShellClientDllRuntime, TheShellIsTheEnvironment )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// CShellGameDll's second base, on the client. Without it the loader has
	// nothing to ask and the client is back to a free function reaching a
	// file-static.
	EXPECT_EQ( 1, CountOf( strShell,
		"class CShellClientDLL : public IBaseClientDLL, public IManagedRuntimeEnvironment" ) );

	EXPECT_EQ( 1, CountOf( strShell, "void *GetEngineInterface( const char *pName, int *pReturnCode ) override" ) );
	EXPECT_EQ( 1, CountOf( strShell, "CGlobalVarsBase &GetGlobalVars() override" ) );
}

TEST( ShellClientDllRuntime, TheShellLoadsTheRuntimeAndChecksTheAnswer )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// `SourceSharp_ClientEarlyInit()` was void and its answer was thrown away.
	// This one is not.
	EXPECT_EQ( 1, CountOf( strShell, "m_pManagedRuntime = LoadManagedRuntime( this );" ) );
	EXPECT_EQ( 1, CountOf( strShell, "if ( m_pManagedRuntime == NULL )" ) );

	// And nothing in the client reaches the old route any more.
	EXPECT_EQ( 0, CountOf( strShell, "SourceSharp_ClientEarlyInit" ) );
}

TEST( ShellClientDllRuntime, TheRuntimeComesUpBeforeTheHudHooksItsMessages )
{
	// THE ORDERING THE WHOLE ORIGINAL DIAGNOSIS RESTS ON, moved onto the file
	// that is actually compiled. gHUD.Init() hooks every user message the HUD
	// listens for; this mod's message list is declared in C#; hooking against an
	// empty list is one failed hook per message -- silent in release, fifteen
	// assertion dialogs in a debug build.
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	const size_t load = strShell.find( "m_pManagedRuntime = LoadManagedRuntime( this );" );
	const size_t hud  = strShell.find( "gHUD.Init();" );

	ASSERT_NE( std::string::npos, load );
	ASSERT_NE( std::string::npos, hud );
	EXPECT_LT( load, hud );
}

TEST( ShellClientDllRuntime, TheRuntimeComesUpBeforeTheClientSystemsWalk )
{
	// The server's one ordering constraint, on the client: LoadManagedRuntime
	// brings the CLR up, and only then is the systems table walked -- whose
	// SourceSharp row does no load of its own for exactly that reason.
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	const size_t load = strShell.find( "m_pManagedRuntime = LoadManagedRuntime( this );" );
	const size_t walk = strShell.find( "SourceSharp_ClientSystemsInit()" );

	ASSERT_NE( std::string::npos, load );
	ASSERT_NE( std::string::npos, walk );
	EXPECT_LT( load, walk );
}

TEST( ShellClientDllRuntime, TheRowIsHandedTheAdapter )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// Exactly once, and after the load. A row that was never handed the pointer
	// answers every per-frame and per-level forward by returning early, which is
	// a client with a live CLR and a dead mod -- and nothing logs it.
	EXPECT_EQ( 1, CountOf( strShell, "SourceSharp_SetClientRuntime( m_pManagedRuntime );" ) );

	const size_t load = strShell.find( "m_pManagedRuntime = LoadManagedRuntime( this );" );
	const size_t hand = strShell.find( "SourceSharp_SetClientRuntime( m_pManagedRuntime );" );
	ASSERT_NE( std::string::npos, load );
	ASSERT_NE( std::string::npos, hand );
	EXPECT_LT( load, hand );
}

TEST( ShellClientDllRuntime, TheRuntimeForwardInShutdownIsGuarded )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// Init returns false on many paths and the engine calls Shutdown after a
	// failed Init. An unguarded forward turns "the managed runtime would not
	// load" into a null dereference, which is the one outcome that hides the
	// diagnostic saying what was missing. CShellGameDll::DLLShutdown is guarded
	// for the same reason and shell_gamedll_refusals_test pins it there.
	const size_t guard = strShell.find( "if ( m_pManagedRuntime != NULL )" );
	const size_t stop  = strShell.find( "m_pManagedRuntime->OnSystemStop();" );

	ASSERT_NE( std::string::npos, guard );
	ASSERT_NE( std::string::npos, stop );
	EXPECT_LT( guard, stop );
}

TEST( ShellClientDllRuntime, ShutdownStopsThenWalksThenReleases )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// CShellGameDll::DLLShutdown's order exactly: stop the managed side, walk
	// the systems table, release the adapter. Releasing before the walk would
	// leave the row holding a freed object for the length of the walk.
	const size_t stop    = strShell.find( "m_pManagedRuntime->OnSystemStop();" );
	const size_t walk    = strShell.find( "SourceSharp_ClientSystemsShutdown();" );
	const size_t release = strShell.find( "UnloadManagedRuntime( m_pManagedRuntime );" );

	ASSERT_NE( std::string::npos, stop );
	ASSERT_NE( std::string::npos, walk );
	ASSERT_NE( std::string::npos, release );

	EXPECT_LT( stop, walk );
	EXPECT_LT( walk, release );
}

TEST( ShellClientDllRuntime, OneLoadAndOneReleasePerDll )
{
	const std::string strShell = ReadShell();
	ASSERT_FALSE( strShell.empty() );

	// THE OWNERSHIP RULE, and it is the thing a listen server can break. Both
	// DLLs run their own copy of this arrangement in one process; the CLR
	// refcount in libsourcesharp.so is the only place they meet. Two loads or
	// two releases from one DLL would unbalance a counter the other side is
	// relying on.
	EXPECT_EQ( 1, CountOf( strShell, "LoadManagedRuntime( this )" ) );
	EXPECT_EQ( 1, CountOf( strShell, "UnloadManagedRuntime( m_pManagedRuntime )" ) );
}

//=============================================================================//
// 5. THE GAME-SYSTEM ROW DOES NEITHER.
//=============================================================================//

namespace
{

std::string ReadGameSystem()
{
	const char *pRoot = SrcRoot();
	if ( pRoot == NULL )
		return std::string();
	return Read( std::string( pRoot ) + "/sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
}

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
std::string ReadClientRow()
{
	const std::string strSys = ReadGameSystem();
	const size_t at = strSys.find( "class CSourceSharpSystem" );
	if ( at == std::string::npos )
		return std::string();

	const size_t end = strSys.find( "static CSourceSharpSystem g_SourceSharpSystem;", at );
	if ( end == std::string::npos )
		return std::string();

	return strSys.substr( at, end - at );
}

} // namespace

TEST( ShellClientDllRuntime, TheRowDoesNotLoadOrUnloadTheRuntime )
{
	const std::string strSys = ReadGameSystem();
	ASSERT_FALSE( strSys.empty() );

	// SourceSharp_LoadManagedSide and SourceSharp_UnloadManagedSide are DEFINED
	// in this file -- both sides share them -- so the count that matters is one
	// definition and no call. A row that loaded as well as the shell would
	// Acquire twice from one DLL.
	EXPECT_EQ( 1, CountOf( strSys, "bool SourceSharp_LoadManagedSide( void )" ) );
	EXPECT_EQ( 0, CountOf( strSys, "return SourceSharp_LoadManagedSide();" ) );

	EXPECT_EQ( 1, CountOf( strSys, "void SourceSharp_UnloadManagedSide( void )" ) );
	EXPECT_EQ( 0, CountOf( strSys, "\t\tSourceSharp_UnloadManagedSide();" ) );

	// And the free function that used to be the client's only route is gone.
	EXPECT_EQ( 0, CountOf( strSys, "void SourceSharp_ClientEarlyInit()" ) );
}

TEST( ShellClientDllRuntime, TheRowForwardsThroughTheAdapterAndNotTheTable )
{
	const std::string strRow = ReadClientRow();
	ASSERT_FALSE( strRow.empty() )
		<< "CSourceSharpSystem's class body was not found in sourcesharp_gamesystem.cpp";

	// The three per-level and per-frame forwards go through the interface now.
	EXPECT_EQ( 1, CountOf( strRow, "m_pRuntime->OnLevelInit( szMapName )" ) );
	EXPECT_EQ( 1, CountOf( strRow, "m_pRuntime->OnLevelShutdown()" ) );
	EXPECT_EQ( 1, CountOf( strRow, "m_pRuntime->OnFrame( frametime )" ) );

	// AND NOT THE TABLE. Reading g_ManagedApi here would put the null-slot test
	// back at the call site -- `if ( g_bSourceSharpLoaded && g_ManagedApi.Frame )`
	// -- which is the silent failure the slot validators (and now the areas) exist to
	// replace: a client that runs at full speed and delivers no frame to managed
	// code, forever, with a clean log.
	EXPECT_EQ( 0, CountOf( strRow, "g_ManagedApi." ) );
}

TEST( ShellClientDllRuntime, TheRowStillTestsThatTheTableIsLive )
{
	// THE HALF THAT DID NOT MOVE, and it is a different question from the one
	// above. `g_ManagedApi.Frame != NULL` asked whether C# implemented a slot;
	// `g_bSourceSharpLoaded` asks whether the table is live AT ALL, which is a
	// real interval -- before the first load, after the last unload, and inside
	// a cs_cl_reload. Deleting it with the slot tests would forward into a
	// memset table during the reload window.
	const std::string strRow = ReadClientRow();
	ASSERT_FALSE( strRow.empty() );

	EXPECT_EQ( 3, CountOf( strRow, "g_bSourceSharpLoaded" ) );
}
