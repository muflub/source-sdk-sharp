// The game DLLs export CreateInterface and nothing else, on every platform:
// the engine asks each for its interfaces through that one function, and
// anything more is a root the linker must keep and a name another module could
// bind to.
//
// Read from the built files themselves, so one test holds both platforms:
// ELF's dynamic symbol table on Linux (what `nm -D --defined-only` lists), the
// PE export directory on Windows. The Windows case is the one that drifted:
// MSVC exports a definition of a function a header declares dllimport, and
// server.dll exported 21 tier0 ports beside CreateInterface, client.dll those
// and vstdlib's random stream; both exported Valve's DLL_EXPORT `cvar` (as
// client.so did on Linux).

#include "gtest/gtest.h"

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <set>
#include <string>
#include <vector>

namespace
{

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

std::string CString( const std::vector< unsigned char > &b, uint64_t off )
{
	std::string s;
	while ( off < b.size() && b[ off ] )
		s += (char)b[ off++ ];
	return s;
}

// The PE export directory's names (data directory 0 of a PE32+ image).
std::set< std::string > PeExports( const std::vector< unsigned char > &b )
{
	std::set< std::string > names;
	uint32_t pe = 0;
	uint16_t nSections = 0, nOptional = 0, magic = 0;
	if ( !At( b, 0x3c, pe ) || !At( b, pe + 6, nSections ) || !At( b, pe + 20, nOptional ) || !At( b, pe + 24, magic ) || magic != 0x20b )
		return names;
	const uint64_t opt = pe + 24, sections = opt + nOptional;
	uint32_t exportRva = 0;
	At( b, opt + 112, exportRva );
	auto toOffset = [ & ]( uint32_t rva ) -> uint64_t
	{
		for ( uint16_t i = 0; i < nSections; i++ )
		{
			uint32_t vsize = 0, va = 0, rawSize = 0, rawPtr = 0;
			const uint64_t s = sections + 40ull * i;
			At( b, s + 8, vsize ); At( b, s + 12, va ); At( b, s + 16, rawSize ); At( b, s + 20, rawPtr );
			if ( rva >= va && rva < va + ( vsize > rawSize ? vsize : rawSize ) )
				return rva - va + rawPtr;
		}
		return UINT64_MAX;
	};
	if ( !exportRva )
		return names;
	const uint64_t dir = toOffset( exportRva );
	uint32_t nNames = 0, namesRva = 0;
	if ( !At( b, dir + 24, nNames ) || !At( b, dir + 32, namesRva ) )
		return names;
	const uint64_t table = toOffset( namesRva );
	for ( uint32_t i = 0; i < nNames; i++ )
	{
		uint32_t nameRva = 0;
		if ( At( b, table + 4ull * i, nameRva ) )
			names.insert( CString( b, toOffset( nameRva ) ) );
	}
	return names;
}

// ELF64's dynamic symbols that are defined and global or weak: what
// `nm -D --defined-only` lists, and what another module can bind to.
std::set< std::string > ElfExports( const std::vector< unsigned char > &b )
{
	std::set< std::string > names;
	uint64_t shoff = 0;
	uint16_t shentsize = 0, shnum = 0;
	if ( !At( b, 0x28, shoff ) || !At( b, 0x3a, shentsize ) || !At( b, 0x3c, shnum ) )
		return names;
	for ( uint16_t i = 0; i < shnum; i++ )
	{
		const uint64_t sh = shoff + (uint64_t)shentsize * i;
		uint32_t type = 0, link = 0;
		uint64_t off = 0, size = 0, entsize = 0;
		At( b, sh + 4, type ); At( b, sh + 24, off ); At( b, sh + 32, size ); At( b, sh + 40, link ); At( b, sh + 56, entsize );
		if ( type != 11 /* SHT_DYNSYM */ || entsize != 24 )
			continue;
		uint64_t strOff = 0;
		At( b, shoff + (uint64_t)shentsize * link + 24, strOff );
		for ( uint64_t s = off + entsize; s + entsize <= off + size; s += entsize )
		{
			uint32_t name = 0;
			uint8_t info = 0;
			uint16_t shndx = 0;
			At( b, s, name ); At( b, s + 4, info ); At( b, s + 6, shndx );
			const int bind = info >> 4;
			if ( shndx != 0 /* SHN_UNDEF */ && ( bind == 1 /* GLOBAL */ || bind == 2 /* WEAK */ ) )
				names.insert( CString( b, strOff + name ) );
		}
	}
	return names;
}

std::set< std::string > Exports( const char *pszPath )
{
	const std::vector< unsigned char > b = ReadAll( pszPath );
	if ( b.size() > 4 && b[ 0 ] == 0x7f && b[ 1 ] == 'E' && b[ 2 ] == 'L' && b[ 3 ] == 'F' )
		return ElfExports( b );
	if ( b.size() > 2 && b[ 0 ] == 'M' && b[ 1 ] == 'Z' )
		return PeExports( b );
	return {};
}

} // namespace

TEST( GameDllExports, TheReaderSeesTheHostsExportTable )
{
	// The control: the managed host exports its whole C API, so a reader that
	// returned nothing, or one name, could not pass the two facts below.
	const std::set< std::string > host = Exports( SS_HOST_LIB );
	EXPECT_TRUE( host.count( "SourceSharp_Acquire" ) ) << SS_HOST_LIB << " read as having no SourceSharp_Acquire";
	EXPECT_TRUE( host.count( "SourceSharp_LoadServer" ) ) << SS_HOST_LIB;
	EXPECT_GT( host.size(), 5u ) << SS_HOST_LIB;
}

TEST( GameDllExports, TheServerExportsCreateInterfaceAlone )
{
	EXPECT_EQ( std::set< std::string >{ "CreateInterface" }, Exports( SS_SERVER_LIB ) ) << SS_SERVER_LIB;
}

TEST( GameDllExports, TheClientExportsCreateInterfaceAlone )
{
	EXPECT_EQ( std::set< std::string >{ "CreateInterface" }, Exports( SS_CLIENT_LIB ) ) << SS_CLIENT_LIB;
}
