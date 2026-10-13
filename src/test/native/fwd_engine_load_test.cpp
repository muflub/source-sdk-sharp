//========= SourceSharp — libss_fwd_engine, loaded as the game loads it =========//
//
// plan_engine_forwarders.md. The forwarder library links no runtime
// (NO_RUNTIME in src/fwd/native/CMakeLists.txt): libc alone on Linux, nothing
// at all on Windows, where it has no CRT and no entry point. So nothing runs
// when it loads but the loader's relocations, and a table the compiler chose
// to fill in a dynamic initialiser would load as nulls. fwd_engine_test cannot
// see that: it compiles fwd_engine.cpp into an executable that has a CRT.
// This loads the built file itself and reads its tables, on both platforms,
// and on Windows reads the file's import directory: the DLL imports nothing.
//=============================================================================//

#include "gtest/gtest.h"

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

#include "ss_fwd.h"

#if defined( _WIN32 )
	#include <windows.h>
#else
	#include <dlfcn.h>
#endif

namespace
{
using TableFn = const SSForwarderTable *( * )( const char * );

// The library's one export, from the built file; null with a message if the
// file does not load or does not export it.
TableFn LoadTableFunction()
{
#if defined( _WIN32 )
	HMODULE hLib = LoadLibraryA( SS_FWD_LIB );
	EXPECT_NE( nullptr, hLib ) << SS_FWD_LIB << " did not load (error " << GetLastError() << ")";
	return hLib ? reinterpret_cast< TableFn >( GetProcAddress( hLib, "SSFwdEngine_Table" ) ) : nullptr;
#else
	void *pLib = dlopen( SS_FWD_LIB, RTLD_NOW | RTLD_LOCAL );
	EXPECT_NE( nullptr, pLib ) << SS_FWD_LIB << " did not load: " << dlerror();
	return pLib ? reinterpret_cast< TableFn >( dlsym( pLib, "SSFwdEngine_Table" ) ) : nullptr;
#endif
}

#if defined( _WIN32 )
std::vector< unsigned char > ReadAll( const char *pszPath )
{
	std::ifstream in( pszPath, std::ios::binary );
	return std::vector< unsigned char >( std::istreambuf_iterator< char >( in ), std::istreambuf_iterator< char >() );
}

template < typename T >
bool At( const std::vector< unsigned char > &b, uint64_t off, T &out )
{
	if ( off + sizeof( T ) > b.size() )
		return false;
	memcpy( &out, b.data() + off, sizeof( T ) );
	return true;
}

// Data directory N of a PE32+ image (0 exports, 1 imports, 13 delay
// imports): its size, or -1 when the file is not a PE32+ image.
int64_t DataDirectorySize( const std::vector< unsigned char > &b, int index )
{
	uint32_t peOffset = 0, signature = 0;
	uint16_t magic = 0;
	if ( !At( b, 0x3c, peOffset ) || !At( b, peOffset, signature ) || signature != 0x4550 )
		return -1;
	const uint64_t optional = peOffset + 4 + 20;   // after "PE\0\0" and the COFF header
	if ( !At( b, optional, magic ) || magic != 0x20b )
		return -1;
	uint32_t size = 0;
	if ( !At( b, optional + 112 + index * 8 + 4, size ) )
		return -1;
	return size;
}
#endif
} // namespace

TEST( FwdEngineLoad, TheBuiltLibraryServesTheModelInfoTableWithEverySlotSet )
{
	TableFn table = LoadTableFunction();
	ASSERT_NE( nullptr, table ) << SS_FWD_LIB << " does not export SSFwdEngine_Table";

	const SSForwarderTable *pTable = table( "SSFwd_ModelInfo" );
	ASSERT_NE( nullptr, pTable );
	ASSERT_NE( nullptr, pTable->ppSlots );
	ASSERT_EQ( 12, pTable->nSlots );

	for ( int i = 0; i < pTable->nSlots; i++ )
	{
		EXPECT_NE( nullptr, pTable->ppSlots[ i ] ) << "slot " << i << " was not set at load";
		for ( int j = 0; j < i; j++ )
			EXPECT_NE( pTable->ppSlots[ j ], pTable->ppSlots[ i ] ) << "slots " << j << " and " << i;
	}
}

TEST( FwdEngineLoad, TheBuiltLibraryMatchesNamesExactly )
{
	TableFn table = LoadTableFunction();
	ASSERT_NE( nullptr, table );

	EXPECT_NE( nullptr, table( "SSFwd_ModelInfo" ) );
	EXPECT_EQ( nullptr, table( "SSFwd_ModelInf" ) );      // a prefix of a served name
	EXPECT_EQ( nullptr, table( "SSFwd_ModelInfoX" ) );    // a served name and more
	EXPECT_EQ( nullptr, table( "ssfwd_modelinfo" ) );
	EXPECT_EQ( nullptr, table( "" ) );
	EXPECT_EQ( nullptr, table( nullptr ) );
}

#if defined( _WIN32 )
TEST( FwdEngineLoad, TheDllImportsNothing )
{
	const std::vector< unsigned char > file = ReadAll( SS_FWD_LIB );
	// Proof of read: the export directory holds SSFwdEngine_Table.
	ASSERT_GT( DataDirectorySize( file, 0 ), 0 ) << SS_FWD_LIB << " read as no PE32+ image with exports";

	EXPECT_EQ( 0, DataDirectorySize( file, 1 ) ) << SS_FWD_LIB << " imports something: a CRT or system library came back";
	EXPECT_EQ( 0, DataDirectorySize( file, 13 ) ) << SS_FWD_LIB << " delay-imports something";
}
#endif
