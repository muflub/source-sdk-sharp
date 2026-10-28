//========= SourceSharp — the forwarder libraries, loaded as the game loads them //
//
// plan_engine_forwarders.md. The forwarder library links no runtime
// (NO_RUNTIME in src/fwd/native/CMakeLists.txt): no libc and no crt start
// files on Linux, no CRT and no entry point on Windows. So nothing runs
// when it loads but the loader's relocations, and a table the compiler chose
// to fill in a dynamic initialiser would load as nulls. fwd_engine_test cannot
// see that: it compiles fwd_server.cpp into an executable that has a CRT.
// This loads each built library itself (libss_fwd_server, libss_fwd_client)
// and reads its tables, on both platforms, and on Windows reads each file's
// import directory: the DLL imports nothing.
//=============================================================================//

#include "gtest/gtest.h"

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "ss_fwd.h"

#if defined( _WIN32 )
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace
{
using TableFn = const SSForwarderTable *(*)( const char * );

// Each library: its built file, its one export, a table it serves and a table
// only the other side calls (which it must not serve).
struct Library
{
	const char *pszPath;
	const char *pszExport;
	const char *pszTable;
	const char *pszOthersTable;
};

constexpr Library kLibraries[] = {
	{ SS_FWD_SERVER_LIB, "SSFwdServer_Table", "SSFwd_ModelInfo", "SSFwd_SteamCallback" },
	{ SS_FWD_CLIENT_LIB, "SSFwdClient_Table", "SSFwd_SteamCallback", "SSFwd_EngineServer" },
};

// The library's one export, from the built file; null with a message if the
// file does not load or does not export it.
TableFn LoadTableFunction( const Library &lib )
{
#if defined( _WIN32 )
	HMODULE hLib = LoadLibraryA( lib.pszPath );
	EXPECT_NE( nullptr, hLib ) << lib.pszPath << " did not load (error " << GetLastError() << ")";
	return hLib ? reinterpret_cast<TableFn>( GetProcAddress( hLib, lib.pszExport ) ) : nullptr;
#else
	void *pLib = dlopen( lib.pszPath, RTLD_NOW | RTLD_LOCAL );
	EXPECT_NE( nullptr, pLib ) << lib.pszPath << " did not load: " << dlerror();
	return pLib ? reinterpret_cast<TableFn>( dlsym( pLib, lib.pszExport ) ) : nullptr;
#endif
}

#if defined( _WIN32 )
std::vector<unsigned char> ReadAll( const char *pszPath )
{
	std::ifstream in( pszPath, std::ios::binary );
	return std::vector<unsigned char>( std::istreambuf_iterator<char>( in ), std::istreambuf_iterator<char>() );
}

template <typename T>
bool At( const std::vector<unsigned char> &b, uint64_t off, T &out )
{
	if ( off + sizeof( T ) > b.size() )
		return false;
	memcpy( &out, b.data() + off, sizeof( T ) );
	return true;
}

// Data directory N of a PE32+ image (0 exports, 1 imports, 13 delay
// imports): its size, or -1 when the file is not a PE32+ image.
int64_t DataDirectorySize( const std::vector<unsigned char> &b, int index )
{
	uint32_t peOffset = 0, signature = 0;
	uint16_t magic = 0;
	if ( !At( b, 0x3c, peOffset ) || !At( b, peOffset, signature ) || signature != 0x4550 )
		return -1;
	const uint64_t optional = peOffset + 4 + 20; // after "PE\0\0" and the COFF header
	if ( !At( b, optional, magic ) || magic != 0x20b )
		return -1;
	uint32_t size = 0;
	if ( !At( b, optional + 112 + index * 8 + 4, size ) )
		return -1;
	return size;
}
#endif
} // namespace

TEST( FwdLibrariesLoad, EachBuiltLibraryServesItsTableWithEverySlotSet )
{
	for ( const Library &lib : kLibraries )
	{
		TableFn table = LoadTableFunction( lib );
		ASSERT_NE( nullptr, table ) << lib.pszPath << " does not export " << lib.pszExport;

		const SSForwarderTable *pTable = table( lib.pszTable );
		ASSERT_NE( nullptr, pTable ) << lib.pszPath << " does not serve " << lib.pszTable;
		ASSERT_NE( nullptr, pTable->ppSlots );
		ASSERT_GT( pTable->nSlots, 1 ) << lib.pszTable;

		for ( int i = 0; i < pTable->nSlots; i++ )
		{
			EXPECT_NE( nullptr, pTable->ppSlots[i] ) << lib.pszTable << " slot " << i << " was not set at load";
			for ( int j = 0; j < i; j++ )
				EXPECT_NE( pTable->ppSlots[j], pTable->ppSlots[i] ) << lib.pszTable << " slots " << j << " and " << i;
		}
	}
}

TEST( FwdLibrariesLoad, EachBuiltLibraryMatchesNamesExactlyAndServesOnlyItsOwn )
{
	for ( const Library &lib : kLibraries )
	{
		TableFn table = LoadTableFunction( lib );
		ASSERT_NE( nullptr, table ) << lib.pszPath;

		const std::string name = lib.pszTable;
		EXPECT_NE( nullptr, table( name.c_str() ) );
		EXPECT_EQ( nullptr, table( name.substr( 0, name.size() - 1 ).c_str() ) ); // a prefix of a served name
		EXPECT_EQ( nullptr, table( ( name + "X" ).c_str() ) ); // a served name and more
		EXPECT_EQ( nullptr, table( "ssfwd_modelinfo" ) );
		EXPECT_EQ( nullptr, table( "" ) );
		EXPECT_EQ( nullptr, table( nullptr ) );

		// A table only the other side calls is the other library's to serve.
		if ( lib.pszOthersTable )
			EXPECT_EQ( nullptr, table( lib.pszOthersTable ) ) << lib.pszPath << " serves " << lib.pszOthersTable;
	}
}

#if defined( _WIN32 )
TEST( FwdLibrariesLoad, EachDllImportsNothing )
{
	for ( const Library &lib : kLibraries )
	{
		const std::vector<unsigned char> file = ReadAll( lib.pszPath );
		// Proof of read: the export directory holds the library's export.
		ASSERT_GT( DataDirectorySize( file, 0 ), 0 ) << lib.pszPath << " read as no PE32+ image with exports";

		EXPECT_EQ( 0, DataDirectorySize( file, 1 ) )
			<< lib.pszPath << " imports something: a CRT or system library came back";
		EXPECT_EQ( 0, DataDirectorySize( file, 13 ) ) << lib.pszPath << " delay-imports something";
	}
}
#endif
