//========= SourceSharp — the managed host is ONE object per process =========//
//
// THE BUG THIS STANDS ON.
//
// sourcesharp/host/sourcesharp.cpp owns the .NET runtime: the hostfxr context,
// the resolved bootstrap delegates, and the refcount that decides which side
// closes the runtime. Its own comment said
//
//     State. Single runtime per process, refcounted across client.so + server.so.
//
// and sourcesharp.vpc built it as a STATIC LIB linked into both game DLLs,
// under a comment saying two copies of that state were safe. They were not.
// `g_State` is file-static, so each module got its own; on a live listen server
// BOTH ran the cold path in SourceSharp_Acquire and BOTH logged "refcount 1",
// while hostfxr told the second one Success_HostAlreadyInitialized. The
// "runtime already running" branch never executed once, in any build, ever.
//
// It survived thirteen green phases. Nothing could see it:
//
//   * the fake-engine harness (test/fake_engine_main.cpp) calls Acquire twice
//     and asserts the refcount reaches 2 -- but both calls are in ONE binary,
//     so it passes on the broken build and always would have;
//   * the log lines carried no module name, so two cold acquires read exactly
//     like one cold acquire and one warm one;
//   * it does not crash on this engine, because the load order happens to be
//     benign: server.so acquires first and gets hostfxr's PRIMARY context,
//     client.so second and gets a SECONDARY, and the engine tears the client
//     down first -- so the primary always outlives every secondary. Nothing
//     enforced that. Reverse it and the primary is closed under a live
//     secondary.
//
// THE FIX is libmanaged_host.so, its own shared object, mapped once per process
// and linked by both game DLLs. That makes "one instance" a property of the
// LOADER rather than of a build flag: dropping `static` would only have worked
// if the engine loaded both modules RTLD_GLOBAL, and would have broken again in
// silence under -fvisibility=hidden or -Bsymbolic.
//
// WHAT THIS FILE ASSERTS, AND WHY IN TWO LAYERS.
//
// Layer 1 is the BINARIES, because that is where the bug actually lived -- a
// source scanner over the vpc files is a stand-in for the link, and a stand-in
// drifts. It parses ELF directly rather than shelling out to readelf, so the
// test has no dependency it can silently lose:
//
//   * libmanaged_host.so DEFINES the host: g_State in its symbol table, and
//     SourceSharp_Acquire and friends exported from .dynsym;
//   * server.so and client.so DEFINE NEITHER -- their SourceSharp_* entries are
//     UNDEFINED imports, and g_State is not in their symbol tables at all;
//   * both game DLLs name libmanaged_host.so in DT_NEEDED.
//
// The positive assertions on libmanaged_host.so are the vacuity guard for the
// negative ones: if the ELF reader stopped working, "server.so does not define
// g_State" would pass for the wrong reason, and "libmanaged_host.so DOES define
// g_State" would fail and say so.
//
// Layer 2 is the SOURCE, so the intent is pinned even before a link happens --
// `$Lib sourcesharp` must not come back in either game project, and the rpath
// that lets the loader find the object next to the module must stay.
//
// THE BINARIES MUST EXIST. A test that skipped when they were missing would be
// a check that cannot fail, which is the thing this tree keeps finding. `make
// check` builds before it tests; if you reached this message another way, run
// `make debug` first.
//
//=============================================================================//

#include <gtest/gtest.h>

#include <elf.h>
#include <dirent.h>
#include <stdint.h>
#include <string.h>

#include <fstream>
#include <string>
#include <vector>
#include <map>
#include <algorithm>

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Where the tree is, from wherever the harness happened to start us. Same shape
// as gc_sections_link_test.cpp's, for the same reason.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string strProbe( kCandidates[ i ] );
		strProbe += "/sourcesharp/game";

		DIR *pDir = opendir( strProbe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadSrcFile( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

std::string BinaryPath( const char *pszName )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/../game/mod_sharp/bin/linux64/";
	strPath += pszName;
	return strPath;
}

//-----------------------------------------------------------------------------
// The smallest ELF64 reader that answers the three questions above: does this
// object DEFINE a symbol, does it IMPORT one, and what does it name in
// DT_NEEDED. Both symbol tables are read -- .dynsym for the exported/imported
// interface, .symtab for g_State, which is file-static and so never dynamic.
//-----------------------------------------------------------------------------
class ElfFile
{
public:
	explicit ElfFile( const std::string &strPath )
		: m_bLoaded( false )
	{
		std::ifstream file( strPath.c_str(), std::ios::binary );
		if ( !file.is_open() )
			return;

		m_Bytes.assign( ( std::istreambuf_iterator< char >( file ) ),
		                std::istreambuf_iterator< char >() );

		if ( m_Bytes.size() < sizeof( Elf64_Ehdr ) )
			return;
		if ( memcmp( &m_Bytes[ 0 ], ELFMAG, SELFMAG ) != 0 )
			return;
		if ( (unsigned char)m_Bytes[ EI_CLASS ] != ELFCLASS64 )
			return;

		m_bLoaded = Parse();
	}

	bool Loaded() const { return m_bLoaded; }

	// A symbol this object supplies: present in a symbol table with a real
	// section index rather than SHN_UNDEF.
	bool Defines( const char *pszName ) const { return Has( pszName, true ); }

	// A symbol this object asks somebody else for: present, SHN_UNDEF.
	bool Imports( const char *pszName ) const { return Has( pszName, false ); }

	bool Needs( const char *pszName ) const
	{
		for ( size_t i = 0; i < m_Needed.size(); i++ )
		{
			if ( m_Needed[ i ] == pszName )
				return true;
		}
		return false;
	}

	const std::vector< std::string > &Needed() const { return m_Needed; }

private:
	struct Sym
	{
		std::string strName;
		bool		bDefined;
	};

	const void *At( uint64_t nOffset, uint64_t nSize ) const
	{
		if ( nOffset + nSize > m_Bytes.size() )
			return NULL;
		return &m_Bytes[ nOffset ];
	}

	bool Has( const char *pszName, bool bDefined ) const
	{
		for ( size_t i = 0; i < m_Syms.size(); i++ )
		{
			if ( m_Syms[ i ].strName == pszName && m_Syms[ i ].bDefined == bDefined )
				return true;
		}
		return false;
	}

	bool ReadSymbols( const Elf64_Shdr *pSections, uint16_t nSections,
	                  const Elf64_Shdr &section )
	{
		if ( section.sh_link >= nSections || section.sh_entsize == 0 )
			return false;

		const Elf64_Shdr &strtab = pSections[ section.sh_link ];
		const char *pStrings = (const char *)At( strtab.sh_offset, strtab.sh_size );
		if ( !pStrings )
			return false;

		const uint64_t nCount = section.sh_size / section.sh_entsize;
		const Elf64_Sym *pSyms =
			(const Elf64_Sym *)At( section.sh_offset, section.sh_size );
		if ( !pSyms )
			return false;

		for ( uint64_t i = 0; i < nCount; i++ )
		{
			if ( pSyms[ i ].st_name == 0 || pSyms[ i ].st_name >= strtab.sh_size )
				continue;

			Sym sym;
			sym.strName = pStrings + pSyms[ i ].st_name;
			sym.bDefined = ( pSyms[ i ].st_shndx != SHN_UNDEF );
			m_Syms.push_back( sym );
		}
		return true;
	}

	void ReadNeeded( const Elf64_Shdr *pSections, uint16_t nSections,
	                 const Elf64_Shdr &section )
	{
		if ( section.sh_link >= nSections || section.sh_entsize == 0 )
			return;

		const Elf64_Shdr &strtab = pSections[ section.sh_link ];
		const char *pStrings = (const char *)At( strtab.sh_offset, strtab.sh_size );
		if ( !pStrings )
			return;

		const uint64_t nCount = section.sh_size / section.sh_entsize;
		const Elf64_Dyn *pDyn = (const Elf64_Dyn *)At( section.sh_offset, section.sh_size );
		if ( !pDyn )
			return;

		for ( uint64_t i = 0; i < nCount; i++ )
		{
			if ( pDyn[ i ].d_tag == DT_NULL )
				break;
			if ( pDyn[ i ].d_tag != DT_NEEDED )
				continue;
			if ( pDyn[ i ].d_un.d_val >= strtab.sh_size )
				continue;

			m_Needed.push_back( pStrings + pDyn[ i ].d_un.d_val );
		}
	}

	bool Parse()
	{
		const Elf64_Ehdr *pHeader = (const Elf64_Ehdr *)&m_Bytes[ 0 ];
		if ( pHeader->e_shentsize != sizeof( Elf64_Shdr ) || pHeader->e_shnum == 0 )
			return false;

		const Elf64_Shdr *pSections = (const Elf64_Shdr *)At(
			pHeader->e_shoff, (uint64_t)pHeader->e_shnum * sizeof( Elf64_Shdr ) );
		if ( !pSections )
			return false;

		bool bAnySymbols = false;
		for ( uint16_t i = 0; i < pHeader->e_shnum; i++ )
		{
			if ( pSections[ i ].sh_type == SHT_SYMTAB ||
			     pSections[ i ].sh_type == SHT_DYNSYM )
			{
				bAnySymbols |= ReadSymbols( pSections, pHeader->e_shnum, pSections[ i ] );
			}
			else if ( pSections[ i ].sh_type == SHT_DYNAMIC )
			{
				ReadNeeded( pSections, pHeader->e_shnum, pSections[ i ] );
			}
		}
		return bAnySymbols;
	}

	std::string					m_Bytes;
	std::vector< Sym >			m_Syms;
	std::vector< std::string >	m_Needed;
	bool						m_bLoaded;
};

const ElfFile &Host()
{
	static const ElfFile f( BinaryPath( "libmanaged_host.so" ) );
	return f;
}

const ElfFile &Server()
{
	static const ElfFile f( BinaryPath( "server.so" ) );
	return f;
}

const ElfFile &Client()
{
	static const ElfFile f( BinaryPath( "client.so" ) );
	return f;
}

// The link line a target's link edge in build.ninja gives the linker: its
// LINK_FLAGS and LINK_LIBRARIES (cmake_graph.h; throws when the build is not
// there, so nothing below can pass on an empty read).
std::string LinkText( const char *pszTarget )
{
	const cmake_graph::LinkEdge edge = cmake_graph::Link( pszTarget );
	std::string s;
	std::map< std::string, std::string >::const_iterator it = edge.vars.find( "LINK_FLAGS" );
	if ( it != edge.vars.end() )
		s += it->second + " ";
	it = edge.vars.find( "LINK_LIBRARIES" );
	if ( it != edge.vars.end() )
		s += it->second;
	return s;
}

bool LinksToken( const char *pszTarget, const std::string &strTok )
{
	const std::vector< std::string > libs = cmake_graph::LinkLibraries( pszTarget );
	return std::find( libs.begin(), libs.end(), strTok ) != libs.end();
}

// g_State is file-static, so this is the name the compiler actually emits:
// `_ZL` is Itanium-ABI for internal linkage, 7 is the length of "g_State".
const char *const kStateSymbol = "_ZL7g_State";

} // namespace

//-----------------------------------------------------------------------------
// Layer 0: the three objects exist and parse. Everything below is read as
// evidence only because these passed -- an unreadable file would make every
// "does not define" assertion below pass for the wrong reason.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, TheHostSharedObjectExists )
{
	EXPECT_TRUE( Host().Loaded() )
		<< "game/mod_sharp/bin/linux64/libmanaged_host.so is missing or unreadable.\n"
		<< "The host must be its own shared object -- one mapping per process is\n"
		<< "what makes the refcount single. Run `make debug` if you have not built.";
}

TEST( ManagedHostSingleInstance, BothGameModulesExist )
{
	EXPECT_TRUE( Server().Loaded() )
		<< "game/mod_sharp/bin/linux64/server.so is missing. Run `make debug`.";
	EXPECT_TRUE( Client().Loaded() )
		<< "game/mod_sharp/bin/linux64/client.so is missing. Run `make debug`.";
}

//-----------------------------------------------------------------------------
// Layer 1a: the host defines the state. This is the vacuity guard -- it is the
// assertion that goes red if the ELF reader stops finding anything.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, HostDefinesTheRuntimeState )
{
	ASSERT_TRUE( Host().Loaded() );
	EXPECT_TRUE( Host().Defines( kStateSymbol ) )
		<< "libmanaged_host.so does not define " << kStateSymbol << ".\n"
		<< "Either the host is no longer compiled into it, or this test's ELF\n"
		<< "reader has stopped working -- and in the second case every 'does not\n"
		<< "define' assertion below is passing for the wrong reason.";
}

TEST( ManagedHostSingleInstance, HostExportsTheAcquireApi )
{
	ASSERT_TRUE( Host().Loaded() );
	EXPECT_TRUE( Host().Defines( "SourceSharp_Acquire" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_Release" ) );
	// plan_side_entries SE1: one export per side, none taking the side.
	EXPECT_TRUE( Host().Defines( "SourceSharp_LoadServer" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_LoadClient" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_UnloadServer" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_UnloadClient" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_ReloadCheckServer" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_ReloadCheckClient" ) );
	EXPECT_FALSE( Host().Defines( "SourceSharp_LoadSideV2" ) );
	EXPECT_FALSE( Host().Defines( "SourceSharp_UnloadSide" ) );
	EXPECT_FALSE( Host().Defines( "SourceSharp_ReloadCheck" ) );
	EXPECT_TRUE( Host().Defines( "SourceSharp_RefCount" ) );
}

//-----------------------------------------------------------------------------
// Layer 1b: THE REGRESSION ITSELF. A game module that defines the host's state
// is a second copy of a thing that must be single -- exactly the shape the bug
// had. This is what goes red if anyone links the host statically again.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, ServerDoesNotDefineTheRuntimeState )
{
	ASSERT_TRUE( Server().Loaded() );
	EXPECT_FALSE( Server().Defines( kStateSymbol ) )
		<< "server.so has its OWN copy of the host's g_State. That is the bug:\n"
		<< "the refcount is per-module again, Acquire's 'runtime already running'\n"
		<< "branch will never fire, and both game DLLs will cold-start the CLR.";
}

TEST( ManagedHostSingleInstance, ClientDoesNotDefineTheRuntimeState )
{
	ASSERT_TRUE( Client().Loaded() );
	EXPECT_FALSE( Client().Defines( kStateSymbol ) )
		<< "client.so has its OWN copy of the host's g_State. That is the bug:\n"
		<< "the refcount is per-module again, Acquire's 'runtime already running'\n"
		<< "branch will never fire, and both game DLLs will cold-start the CLR.";
}

TEST( ManagedHostSingleInstance, ServerImportsTheAcquireApi )
{
	ASSERT_TRUE( Server().Loaded() );
	EXPECT_TRUE( Server().Imports( "SourceSharp_Acquire" ) )
		<< "server.so does not IMPORT SourceSharp_Acquire.";
	EXPECT_FALSE( Server().Defines( "SourceSharp_Acquire" ) )
		<< "server.so DEFINES SourceSharp_Acquire, so it carries its own host.";
	EXPECT_FALSE( Server().Defines( "SourceSharp_Release" ) );
}

TEST( ManagedHostSingleInstance, ClientImportsTheAcquireApi )
{
	ASSERT_TRUE( Client().Loaded() );
	EXPECT_TRUE( Client().Imports( "SourceSharp_Acquire" ) )
		<< "client.so does not IMPORT SourceSharp_Acquire.";
	EXPECT_FALSE( Client().Defines( "SourceSharp_Acquire" ) )
		<< "client.so DEFINES SourceSharp_Acquire, so it carries its own host.";
	EXPECT_FALSE( Client().Defines( "SourceSharp_Release" ) );
}

//-----------------------------------------------------------------------------
// Layer 1c: and they name the object they import it from, so the single mapping
// is the loader's job and not a coincidence of who happened to be loaded first.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, BothGameModulesNeedTheHostObject )
{
	ASSERT_TRUE( Server().Loaded() );
	ASSERT_TRUE( Client().Loaded() );

	EXPECT_TRUE( Server().Needs( "libmanaged_host.so" ) )
		<< "server.so has no DT_NEEDED on libmanaged_host.so.";
	EXPECT_TRUE( Client().Needs( "libmanaged_host.so" ) )
		<< "client.so has no DT_NEEDED on libmanaged_host.so.";

	// Not vacuous: the reader really did find this object's other dependencies.
	// (libtier0.so was the server's control until P4.8 took it off the link,
	// and the client's until plan_client_novalve C9 did the same.)
	EXPECT_TRUE( Server().Needs( "libstdc++.so.6" ) );
	EXPECT_TRUE( Client().Needs( "libstdc++.so.6" ) );
}

//-----------------------------------------------------------------------------
// plan_server_novalve.md P4: server.so needs no Valve library. The reader's
// positive arm is the fact above (libstdc++, libmanaged_host found).
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, TheServerNeedsNoValveLibrary )
{
	ASSERT_TRUE( Server().Loaded() );
	EXPECT_FALSE( Server().Needs( "libtier0.so" ) );
	EXPECT_FALSE( Server().Needs( "libvstdlib.so" ) );
	EXPECT_FALSE( Server().Needs( "libsteam_api.so" ) );
}

//-----------------------------------------------------------------------------
// plan_client_novalve C9: client.so needs neither tier0 nor vstdlib; its names
// from them are its own or bound to the engine's copies (tier_bindings_client).
// The reader's positive arm is BothGameModulesNeedTheHostObject.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, TheClientNeedsNeitherTier0NorVstdlib )
{
	ASSERT_TRUE( Client().Loaded() );
	EXPECT_FALSE( Client().Needs( "libtier0.so" ) );
	EXPECT_FALSE( Client().Needs( "libvstdlib.so" ) );
}

//-----------------------------------------------------------------------------
// Layer 2: the source says so too, so a tree that has not been linked yet still
// fails the moment the intent is reverted.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, HostProjectIsADynamicLibrary )
{
	// src/host/native/CMakeLists.txt (Phase 6b; was sourcesharp.vpc).
	const std::string list = cmake_graph::ListText( cmake_graph::kHost );
	EXPECT_NE( list.find( "add_library(sourcesharp SHARED)" ), std::string::npos )
		<< "the host target no longer builds a shared object.";
	EXPECT_EQ( list.find( "add_library(sourcesharp STATIC)" ), std::string::npos )
		<< "the host is a STATIC LIB again -- which is the bug, because a\n"
		<< "static lib linked into two modules is two copies of the state.";
	EXPECT_EQ( "out/libmanaged_host.so", cmake_graph::Link( cmake_graph::kHost ).strOutput )
		<< "the host no longer links libmanaged_host.so.";
}

TEST( ManagedHostSingleInstance, NeitherGameProjectLinksTheHostStatically )
{
	const char *const kGame[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t i = 0; i < 2; i++ )
	{
		const std::vector< std::string > libs = cmake_graph::LinkLibraries( kGame[ i ] );
		for ( size_t k = 0; k < libs.size(); k++ )
			EXPECT_EQ( libs[ k ].find( "libsourcesharp.a" ), std::string::npos )
				<< kGame[ i ] << " links the host as a static lib again: " << libs[ k ];
		// By target (ss_link_host): the shared object itself, whose SONAME is
		// what DT_NEEDED records (it was -L/-l while the host had no SONAME).
		EXPECT_TRUE( LinksToken( kGame[ i ], "out/libmanaged_host.so" ) )
			<< kGame[ i ] << " no longer links libmanaged_host.so.";
	}
}

TEST( ManagedHostSingleInstance, BothGameProjectsKeepTheOriginRunpath )
{
	// The mod's bin directory is not on the engine's LD_LIBRARY_PATH, so
	// without this the loader cannot find the object sitting beside the module
	// and the game DLL fails to load for a reason that does not name this.
	const char *const kGame[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string text = LinkText( kGame[ i ] );
		ASSERT_NE( text.find( "out/libmanaged_host.so" ), std::string::npos )
			<< kGame[ i ] << "'s link line read as something else";
		EXPECT_NE( text.find( "-Wl,-rpath,\"\\$$ORIGIN\"" ), std::string::npos )
			<< kGame[ i ] << " has lost its $ORIGIN runpath:\n" << text;
	}
}

//-----------------------------------------------------------------------------
// The log lines that would have caught this in 2026 and did not, because they
// did not say which module they came from. Two cold acquires are
// indistinguishable from one unless the line names its module.
//-----------------------------------------------------------------------------
TEST( ManagedHostSingleInstance, EveryRefcountLineNamesItsModule )
{
	const std::string strHost = ReadSrcFile( "sourcesharp/host/sourcesharp.cpp" );
	ASSERT_FALSE( strHost.empty() );

	// Each of the four lines that reports a refcount carries the caller tag.
	EXPECT_NE( strHost.find( "\"Acquire[%s]: runtime already running, refcount now %d" ),
	           std::string::npos );
	EXPECT_NE( strHost.find( "\"Acquire[%s]: runtime ready, refcount 1\"" ), std::string::npos );
	EXPECT_NE( strHost.find( "\"Release[%s]: refcount now %d\"" ), std::string::npos );
	EXPECT_NE( strHost.find( "\"Release[%s]: last reference dropped" ), std::string::npos );

	// And the tag is the CALLER's module, not this one -- dladdr on a function
	// in the host would answer "libmanaged_host.so" every time, which is true
	// and tells you nothing about who is acquiring.
	EXPECT_NE( strHost.find( "SS_DescribeCaller" ), std::string::npos );
	EXPECT_NE( strHost.find( "dladdr" ), std::string::npos );
}
