// plan_engineapi_vtables Phase D3, reshaped by plan_managed_vtable_reload.md
// L1: native accepts a managed-built object only when the object's own
// Validate agrees with the slot hashes this binary was compiled with.

#include "gtest/gtest.h"

#include <string.h>

#include <type_traits>

#include "imanaged_root.h"
#include "managed_accept.h"
#include "shell_managed_areas.h"
#include "vtable_hook.h"

namespace
{

// A stand-in for a managed-built root: an object whose first word is its
// table (plan_forwarders_everywhere.md phase 4), as the block managed builds
// is. Validate answers as the generated managed thunk does
// (ManagedVtable.ValidateSlots) from the hashes it was "built" with, and
// counts its calls so a test sees the stimulus arrive.
class FakeRoot : public IManagedRoot
{
public:
	explicit FakeRoot( const uint32_t *pHashes, int32_t nHashes )
		: IManagedRoot( &kTable ), m_pHashes( pHashes ), m_nHashes( nHashes ) {}

	int m_nCalls = 0;

private:
	static FakeRoot *Of( void *self ) { return static_cast< FakeRoot * >( self ); }
	static void Released( IManagedObject * ) {}
	static void *Acquired( IManagedRoot *, const char * ) { return NULL; }

	static int32_t Validated( IManagedObject *self, const uint32_t *pSlotHashes, int32_t nSlots )
	{
		FakeRoot *root = Of( self );
		++root->m_nCalls;
		if ( !pSlotHashes || nSlots <= 0 )
			return 0;
		const int32_t nShared = nSlots < root->m_nHashes ? nSlots : root->m_nHashes;
		for ( int32_t i = 0; i < nShared; ++i )
		{
			if ( pSlotHashes[ i ] != root->m_pHashes[ i ] )
				return i;
		}
		return nSlots == root->m_nHashes ? -1 : nShared;
	}

	static const SSManagedRootTable kTable;

	const uint32_t *m_pHashes;
	int32_t m_nHashes;
};

const SSManagedRootTable FakeRoot::kTable = { &Released, &Validated, &Acquired };

} // namespace

TEST( ManagedAccept, TheTablePutsReleaseThenValidateFirst )
{
	// The control for everything below: the fixed slots the accept relies on,
	// in the table managed writes, whose order is the TSV's (no compiler
	// chooses it: plan_forwarders_everywhere.md phase 4).
	EXPECT_EQ( 0 * sizeof( void * ), offsetof( SSManagedRootTable, Release ) );
	EXPECT_EQ( 1 * sizeof( void * ), offsetof( SSManagedRootTable, Validate ) );
	EXPECT_EQ( 2 * sizeof( void * ), offsetof( SSManagedRootTable, Acquire ) );
	EXPECT_EQ( 3, kIManagedRootSlotCount );
}

TEST( ManagedAccept, MatchingHashesAreAccepted )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[ 128 ] = "unchanged";
	EXPECT_EQ( &root, ManagedAccept< IManagedRoot >( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_STREQ( "", why );
	EXPECT_EQ( 1, root.m_nCalls );
}

TEST( ManagedAccept, ARetypedSlotIsRefusedNamingIt )
{
	uint32_t built[ 3 ];
	memcpy( built, kIManagedRootSlotHashes, sizeof( built ) );
	built[ 2 ] ^= 1;
	FakeRoot root( built, 3 );

	char why[ 160 ] = {};
	EXPECT_EQ( nullptr, ManagedAccept< IManagedRoot >( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 2 differs" ) ) << why;
	EXPECT_NE( nullptr, strstr( why, "3 slot(s) compiled" ) ) << why;
}

TEST( ManagedAccept, AManagedTableShortByOneIsRefused )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount - 1 );
	char why[ 160 ] = {};
	EXPECT_EQ( nullptr, ManagedAccept< IManagedRoot >( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 2 differs" ) ) << why;
}

TEST( ManagedAccept, AManagedTableLongByOneIsRefused )
{
	const uint32_t built[] = { kIManagedRootSlotHashes[ 0 ], kIManagedRootSlotHashes[ 1 ], kIManagedRootSlotHashes[ 2 ], 0x1234 };
	FakeRoot root( built, 4 );
	char why[ 160 ] = {};
	EXPECT_EQ( nullptr, ManagedAccept< IManagedRoot >( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "managed's slot 3 differs" ) ) << why;
}

TEST( ManagedAccept, ANullObjectIsRefusedWithoutACall )
{
	char why[ 128 ] = {};
	EXPECT_EQ( nullptr, ManagedAccept< IManagedRoot >( nullptr, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "no object" ) ) << why;
}

TEST( ManagedAccept, NoCompiledHashesIsRefusedWithoutAskingTheObject )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[ 128 ] = {};
	EXPECT_EQ( nullptr, ManagedAccept< IManagedRoot >( &root, nullptr, 0, why, sizeof( why ) ) );
	EXPECT_NE( nullptr, strstr( why, "no compiled slot hashes" ) ) << why;
	EXPECT_EQ( 0, root.m_nCalls );
}

//-----------------------------------------------------------------------------
// plan_managed_vtable_reload.md L1b: every managed-built interface derives from
// IManagedObject, so native accepts and releases any of them through the base.
//-----------------------------------------------------------------------------
namespace
{

// Records what the holder held at the moment Release ran.
class ReleaseProbe : public IManagedRoot
{
public:
	ReleaseProbe() : IManagedRoot( &kTable ) {}

	IManagedRoot **m_ppHolder = NULL;
	int m_nReleases = 0;
	bool m_bHolderWasNullAtRelease = false;

private:
	static void Released( IManagedObject *self )
	{
		ReleaseProbe *probe = static_cast< ReleaseProbe * >( self );
		++probe->m_nReleases;
		probe->m_bHolderWasNullAtRelease = probe->m_ppHolder && *probe->m_ppHolder == NULL;
	}
	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t ) { return -1; }
	static void *Acquired( IManagedRoot *, const char * ) { return NULL; }

	static const SSManagedRootTable kTable;
};

const SSManagedRootTable ReleaseProbe::kTable = { &Released, &Validated, &Acquired };

} // namespace

TEST( ManagedAccept, EveryGeneratedInterfaceSharesTheBasesTwoSlots )
{
	static_assert( std::is_base_of< IManagedObject, IManagedRoot >::value, "the root derives from the base" );
	EXPECT_EQ( 0 * sizeof( void * ), offsetof( SSManagedObjectTable, Release ) );
	EXPECT_EQ( 1 * sizeof( void * ), offsetof( SSManagedObjectTable, Validate ) );
	// No virtual anywhere: the object is the table pointer and nothing else,
	// the first word of the block managed builds.
	static_assert( !std::is_polymorphic< IManagedRoot >::value, "a managed-built interface has no vtable" );
	EXPECT_EQ( sizeof( void * ), sizeof( IManagedRoot ) );
}

TEST( ManagedAccept, TheBaseAcceptAnswersTheSameObject )
{
	FakeRoot root( kIManagedRootSlotHashes, kIManagedRootSlotCount );
	char why[ 128 ] = "unchanged";
	IManagedObject *pObject = ManagedAcceptObject( &root, kIManagedRootSlotHashes, kIManagedRootSlotCount, why, sizeof( why ) );
	EXPECT_EQ( static_cast< IManagedObject * >( &root ), pObject );
	EXPECT_EQ( (void *)&root, (void *)pObject ) << "the base is not at offset 0";
	EXPECT_EQ( 1, root.m_nCalls );
}

TEST( ManagedAccept, ReleaseNullsTheHolderBeforeReleasing )
{
	ReleaseProbe probe;
	IManagedRoot *pHolder = &probe;
	probe.m_ppHolder = &pHolder;

	ManagedRelease( pHolder );

	EXPECT_EQ( nullptr, pHolder );
	EXPECT_EQ( 1, probe.m_nReleases );
	EXPECT_TRUE( probe.m_bHolderWasNullAtRelease ) << "Release ran while the holder still named the object";
}

TEST( ManagedAccept, ReleasingAnEmptyHolderCallsNothing )
{
	IManagedRoot *pHolder = NULL;
	ManagedRelease( pHolder );
	EXPECT_EQ( nullptr, pHolder );
}

//-----------------------------------------------------------------------------
// THE SHUTDOWN ORDER. Native releases the managed root and its areas BEFORE the
// Shutdown slot, because that slot unloads the module: released after it, the
// module's own safety net had already freed the handles ("N managed-built
// object(s) were still bound at shutdown"), native's release then found them
// released twice, and the handles kept the server's load context alive through
// its unload. Source-read: the function needs the engine to run.
//-----------------------------------------------------------------------------
#include <fstream>
#include <sstream>
#include <string>

TEST( ManagedAccept, TheFinalUnloadReleasesBeforeTheModuleUnloads )
{
	std::ifstream f( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_TRUE( f.is_open() ) << "run from the farm's src";
	std::stringstream ss;
	ss << f.rdbuf();
	const std::string src = ss.str();

	const size_t body = src.find( "void SourceSharp_UnloadManagedSide( void )" );
	ASSERT_NE( std::string::npos, body );
	const size_t end = src.find( "\n}\n", body );
	ASSERT_NE( std::string::npos, end );
	const std::string fn = src.substr( body, end - body );

	// Until plan_client_vtables CE-last the client's module unload was its
	// ManagedApi Shutdown slot; neither side has one now, and UnloadSide is
	// the unload on both.
	const size_t release = fn.find( "ManagedRoot_Release();" );
	const size_t unload = fn.find( "SourceSharp_UnloadMySide();" );
	ASSERT_NE( std::string::npos, release ) << "the final unload releases the managed root";
	ASSERT_NE( std::string::npos, unload ) << "the final unload unloads the side's module";
	EXPECT_LT( release, unload ) << "the root is released after UnloadSide unloaded the module";
	EXPECT_EQ( std::string::npos, fn.find( "Shutdown()" ) )
		<< "a managed Shutdown slot is called from the final unload again";
}

//-----------------------------------------------------------------------------
// plan_managed_vtable_reload.md L2: everything native holds from managed is one
// CManagedHolders on the game DLL object, and the object's destructor asserts
// it was emptied.
//-----------------------------------------------------------------------------
namespace
{

std::string ReadSource( const char *pszPath )
{
	std::ifstream f( pszPath );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

} // namespace

TEST( ManagedHolders, AZeroedSetIsEmpty )
{
	CManagedHolders holders = {};
	EXPECT_TRUE( ManagedHolders_Empty( holders ) );
}

TEST( ManagedHolders, EveryHolderAloneMakesTheSetNonEmpty )
{
	// Every field is a pointer, so the set is a run of words; setting each in
	// turn proves the check names every one, including areas added later.
	static_assert( sizeof( CManagedHolders ) % sizeof( void * ) == 0, "holders are pointers only" );
	const size_t nWords = sizeof( CManagedHolders ) / sizeof( void * );
	ASSERT_GE( nWords, 3u ) << "the root, the runtime and at least one area";

	int nChecked = 0;
	for ( size_t i = 0; i < nWords; ++i )
	{
		CManagedHolders holders = {};
		void *pWords[ sizeof( CManagedHolders ) / sizeof( void * ) ];
		memcpy( pWords, &holders, sizeof( holders ) );
		pWords[ i ] = &holders;
		memcpy( &holders, pWords, sizeof( holders ) );

		EXPECT_FALSE( ManagedHolders_Empty( holders ) ) << "word " << i << " is not checked";
		++nChecked;
	}
	EXPECT_EQ( (int)nWords, nChecked );
}

TEST( ManagedHolders, TheShimRootHoldsNothingFromManaged )
{
	// Two roots since plan_server_native_v2 T6: the client's, still a static in
	// shell_internalfactory.cpp, and the server's, a member of the game DLL
	// object declared in shell_shim_root_server.h. Neither holds anything
	// managed built.
	struct Root_t { const char *pszFile; const char *pszClass; const char *pszControl; };
	static const Root_t kRoots[] = {
		{ "sourcesharp/game/shared/shell_internalfactory.cpp", "class CShimRoot ", "CShimAreasClient m_ClientAreas;" },
		{ "sourcesharp/game/server/shell_shim_root_server.h", "class CShimRootServer ", "CShimAreas m_Areas;" },
	};
	for ( const Root_t &root : kRoots )
	{
		const std::string src = ReadSource( root.pszFile );
		ASSERT_FALSE( src.empty() ) << "run from the farm's src: " << root.pszFile;

		const size_t open = src.find( root.pszClass );
		ASSERT_NE( std::string::npos, open ) << root.pszFile;
		const size_t close = src.find( "\n};\n", open );
		ASSERT_NE( std::string::npos, close ) << root.pszFile;
		const std::string body = src.substr( open, close - open );

		EXPECT_NE( std::string::npos, body.find( root.pszControl ) )
			<< root.pszFile << ": the control: the walk reached the members";
		EXPECT_EQ( std::string::npos, body.find( "IManaged" ) )
			<< root.pszFile << ": the shim root holds a managed-built object again";
		EXPECT_EQ( std::string::npos, body.find( "CManagedAreas" ) )
			<< root.pszFile << ": the shim root holds the managed areas again";
	}
}

TEST( ManagedHolders, TheGameDllObjectOwnsThemAndChecksThemAtDestruction )
{
	const std::string src = ReadSource( "sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( src.empty() ) << "run from the farm's src";

	EXPECT_NE( std::string::npos, src.find( "    CManagedHolders m_Managed = {};\n" ) );
	EXPECT_NE( std::string::npos, src.find( "AssertMsg( ManagedHolders_Empty( m_Managed )," ) );
	EXPECT_NE( std::string::npos, src.find( "CManagedHolders &ShellGameDll_Managed()\n{\n    return s_gameShellDll.Managed();\n}" ) );
}

TEST( ManagedHolders, TheReloadIsCheckedBeforeAnythingIsSavedOrUnloaded )
{
	// plan_managed_vtable_reload.md L5: a refused reload must leave the running
	// build untouched, so the check comes before the save and the unload.
	const std::string src = ReadSource( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( src.empty() ) << "run from the farm's src";

	const size_t body = src.find( "static void SourceSharp_ReloadManagedSide( void )\n{" );
	ASSERT_NE( std::string::npos, body );
	const size_t end = src.find( "\n}\n", body );
	const std::string fn = src.substr( body, end - body );

	const size_t check = fn.find( "if ( SourceSharp_ReloadCheckMySide( szWhy, sizeof( szWhy ) ) != 0 )" );
	const size_t save = fn.find( "pSaveArea->ReloadSave()" );
	const size_t unload = fn.find( "SourceSharp_UnloadMySide();" );
	ASSERT_NE( std::string::npos, check ) << "the reload no longer checks the new build's hashes";
	ASSERT_NE( std::string::npos, save );
	ASSERT_NE( std::string::npos, unload );
	EXPECT_LT( check, save );
	EXPECT_LT( check, unload );
}

TEST( ManagedHolders, TheClientLoadsThroughV2WithItsShimRootAndNoEngineStruct )
{
	// plan_client_vtables CA: both sides load through LoadSideV2 with their
	// shim root. Since CC-last the client passes no engine struct, and since
	// CE-last no ManagedApi out-parameter.
	const std::string src = ReadSource( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( src.empty() ) << "run from the farm's src";

	const size_t body = src.find( "static int SourceSharp_LoadMySide()\n{" );
	ASSERT_NE( std::string::npos, body );
	const std::string fn = src.substr( body, src.find( "\n}\n", body ) - body );

	// CC-last (2c): LoadSideV2 has no engine-struct parameter at all. CD: both
	// sides share one load body, and both accept a managed root. CE-last: no
	// side hands it a ManagedApi either; the call is the side, the shim root
	// and the managed root's out-parameter, and nothing else.
	// plan_side_entries SE1: each side calls its own host export, so no side
	// travels as an argument. plan_forwarders_everywhere.md phase 3: the shim
	// root travels with its forwarder table, which replaced its slot count.
	EXPECT_NE( std::string::npos, fn.find( "const int rc = SourceSharp_LoadMySide_Host( pRoot, pRootTable, &pManagedRoot );" ) )
		<< "the load no longer goes through the side's own export with exactly the shim root, its table and the managed root";
	EXPECT_EQ( std::string::npos, fn.find( "SourceSharpManagedApi" ) )
		<< "the load hands LoadSideV2 a ManagedApi again";
	EXPECT_NE( std::string::npos, fn.find( "if ( !ManagedRoot_Accept( pManagedRoot, szWhy, sizeof( szWhy ) ) )" ) )
		<< "the load no longer accepts a managed root (both sides since CD)";
	EXPECT_EQ( std::string::npos, src.find( "g_EngineApi" ) ) << "the client's engine struct is back";
	EXPECT_EQ( std::string::npos, fn.find( "SourceSharp_LoadSide( SOURCESHARP_MY_SIDE" ) )
		<< "a side loads through LoadSide V1 again";
}

TEST( ManagedHolders, TheClientsShimRootServesTheConsole )
{
	// plan_client_vtables CA: the client's LoadSideV2 acquires the console
	// through its shim root, so the factory's console row must not be
	// server-only. Found by the live gate: "the shim root does not serve
	// SourceSharpConsole001" on the client.
	const std::string src = ReadSource( "sourcesharp/game/shared/shell_internalfactory.cpp" );
	ASSERT_FALSE( src.empty() ) << "run from the farm's src";

	const size_t row = src.find( "if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_CONSOLE_VERSION ) )" );
	ASSERT_NE( std::string::npos, row );

	// The nearest preprocessor guard above the row must be a closed one.
	const size_t open = src.rfind( "#ifndef CLIENT_DLL", row );
	const size_t close = src.rfind( "#endif", row );
	ASSERT_NE( std::string::npos, open ) << "the control: the file has server-only blocks";
	EXPECT_TRUE( close != std::string::npos && close > open )
		<< "the console row is inside an #ifndef CLIENT_DLL block: the client's root no longer serves it";
}

//-----------------------------------------------------------------------------
// plan_forwarders_everywhere.md phase 4: a managed-built interface has no
// virtual. Each method is an inline forwarder that calls its own slot of the
// table managed wrote, the object first and the arguments in order.
//-----------------------------------------------------------------------------
namespace
{

struct SoundEntCalls
{
	int nSlot = -1;
	void *pSelf = nullptr;
	int32_t nA = 0;
	float flB = 0.0f;
};

SoundEntCalls g_SoundEnt;

void SoundEntInit( IManagedSoundEnt *self, int32_t nMaxClients, float flCurTime )
{
	g_SoundEnt.nSlot = 2;
	g_SoundEnt.pSelf = self;
	g_SoundEnt.nA = nMaxClients;
	g_SoundEnt.flB = flCurTime;
}

void SoundEntShutdown( IManagedSoundEnt *self )
{
	g_SoundEnt.nSlot = 3;
	g_SoundEnt.pSelf = self;
}

void SoundEntReset( IManagedSoundEnt *self, int32_t nIndex )
{
	g_SoundEnt.nSlot = 4;
	g_SoundEnt.pSelf = self;
	g_SoundEnt.nA = nIndex;
}

void SoundEntReleased( IManagedObject *self )
{
	g_SoundEnt.nSlot = 0;
	g_SoundEnt.pSelf = self;
}

int32_t SoundEntValidated( IManagedObject *self, const uint32_t *, int32_t nSlots )
{
	g_SoundEnt.nSlot = 1;
	g_SoundEnt.pSelf = self;
	return nSlots;
}

const SSManagedSoundEntTable kSoundEntTable = {
	&SoundEntReleased, &SoundEntValidated, &SoundEntInit, &SoundEntShutdown, &SoundEntReset,
};

} // namespace

TEST( ManagedAccept, EachMethodCallsItsOwnSlotWithTheObjectAndItsArguments )
{
	IManagedSoundEnt area( &kSoundEntTable );

	area.ServerSoundEntInit( 24, 1.5f );
	EXPECT_EQ( 2, g_SoundEnt.nSlot );
	EXPECT_EQ( &area, g_SoundEnt.pSelf );
	EXPECT_EQ( 24, g_SoundEnt.nA );
	EXPECT_FLOAT_EQ( 1.5f, g_SoundEnt.flB );

	area.ServerSoundEntShutdown();
	EXPECT_EQ( 3, g_SoundEnt.nSlot );

	area.ServerSoundEntResetClientSound( 9 );
	EXPECT_EQ( 4, g_SoundEnt.nSlot );
	EXPECT_EQ( 9, g_SoundEnt.nA );

	// And the base's two, through the base, on the same table.
	IManagedObject *pObject = &area;
	EXPECT_EQ( kIManagedSoundEntSlotCount, pObject->Validate( kIManagedSoundEntSlotHashes, kIManagedSoundEntSlotCount ) );
	EXPECT_EQ( 1, g_SoundEnt.nSlot );
	pObject->Release();
	EXPECT_EQ( 0, g_SoundEnt.nSlot );
	EXPECT_EQ( static_cast< void * >( &area ), g_SoundEnt.pSelf );
}
