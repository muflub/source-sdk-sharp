//========= SourceSharp — what the NATIVE map parse still makes =============//
//
// THE EXIT CONDITION FOR mapentities.cpp, MADE PRECISE.
//
// SourceSharp.Server/MapSpawner.cs:212-248 splits the map's entity lump in two:
// every block whose classname the managed registry owns is spawned in managed
// code, and the REST is rebuilt into a lump of its own and handed back down to
// the native `MapEntity_ParseAllEntities` through
// `sourcesharp/game/server/shell_level.cpp`. ServerGameDll.cs records the
// exit condition against that residue:
//
//     "On dm_lockdown that is 19 of 605 entities and four classnames --
//      worldspawn, plus info_node, func_useableladder and
//      info_ladder_dismount. When that list is empty, mapentities.cpp can
//      leave the vpc."
//
// THE FINDING THIS FILE PINS, AND IT MAKES THAT CONDITION SMALLER THAN IT
// LOOKS: three of those four classnames HAVE NO FACTORY IN THIS BUILD. Their
// registrations live in `game/server/ai_initutils.cpp` and
// `game/shared/func_ladder.cpp`, and neither file is compiled into server.so
// (server_sourcesharp, src/server/native/CMakeLists.txt). So the native parse reaches them, fails to
// construct anything, and prints stock's `Warning("Can't init %s\n", ...)`
// (`game/server/mapentities.cpp:1376`) — once per block, every map load.
//
// Measured on the live gate's own console output, dm_lockdown, one gate run:
//
//     70  Can't init info_ladder_dismount
//     15  Can't init func_useableladder
//      5  Can't init info_node
//
// So the residue is FOUR classnames but only ONE entity: `worldspawn`, whose
// factory is `sourcesharp/game/server/world_adapter.cpp` — a SHIM file, already
// on this side of the port, registering itself into `EntityFactoryDictionary()`
// rather than through `LINK_ENTITY_TO_CLASS` (world_adapter.cpp:152 records
// why). Nothing stock constructs a residue entity at all.
//
// WHY THAT IS WORTH A GATE RATHER THAN A NOTE. Two different things are being
// separated, and only one of them is a hole:
//
//   * `worldspawn` is a REAL dependency. The native parse is what calls
//     `pParsedWorld->Spawn()` (mapentities.cpp:663-666), which is
//     `CWorld::Spawn` in world_adapter.cpp — the body that runs
//     `g_EventQueue.Init()` and `GlobalEntity_Add`. Emptying the residue means
//     giving the managed spawner its own way to make the world; it does NOT
//     mean deleting the call.
//
//   * the other three are a SILENT ABSENCE. `func_useableladder` and
//     `info_ladder_dismount` are the map's ladders and
//     `info_node` its AI node graph: live map content that this build does not
//     construct, reported only as stock's bare `Can't init` in a log nobody
//     gates on. plan_entity.md §7 asks for a declared, printed refusal and
//     never a silent no-op. This file is the declaration, so the absence is
//     recorded rather than discovered again.
//
// A SOURCE SCAN, FOR THE REASON stock_serverclass_census_test.cpp GIVES. A
// factory is a file-static that installs itself from its own constructor; there
// is no symbol to ask about and no function to call. The fact is about which
// .cpp the server compiles and what is written in it, so the server's source
// list is read (cmake_graph.h) and the files it names are read — derived, never
// listed.
//
// COMMENTS ARE STRIPPED BEFORE EVERY SEARCH. This tree's prose names these
// classes constantly — `game/server/world.h:54` writes
// `LINK_ENTITY_TO_CLASS( worldspawn, CWorld )` inside a comment explaining why
// it is NOT used — and a raw grep reads prose as code. That hole has been found
// in three of this project's vpc parsers already.
//
//=============================================================================//

#include <gtest/gtest.h>

#include "cmake_graph.h"

#include <fstream>
#include <set>
#include <string>
#include <vector>

#include <sys/stat.h>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as
// stock_serverclass_census_test.cpp, deliberately.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

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

// Raw, comments kept. Empty means "could not open", which every caller here
// tells apart from "opened and found nothing" — a scanner that silently read
// nothing would call every absence below green.
std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

std::string ReadStripped( const std::string &strRelative )
{
	const std::string raw = ReadRaw( strRelative );
	if ( raw.empty() )
		return raw;
	return StripComments( raw );
}

//-----------------------------------------------------------------------------
// THE COMPILED SET, DERIVED FROM THE BUILD.
//
// server_sourcesharp's sources, read through cmake_graph.h: compile_commands.json
// cross-checked against src/server/native/CMakeLists.txt, farm-relative paths.
// (This was a comment-stripping `$File` parser over server_csharp.vpc; the
// cross-check replaces the hazards that parser had to guard by hand.)
//-----------------------------------------------------------------------------

// A row every reader agrees is present, so "the list read to nothing" cannot
// pass as "the file is not a row".
//
// IT WAS game/shared/baseentity_shared.cpp AND THE FLIP TOOK THAT ROW OUT, so
// this premise was RED on the armed branch -- an inherited flip red rather than
// a broken parser, and it was failing the whole binary and with it every fact
// below. The replacement is the row this file's subject IS: mapentities.cpp is
// the native map parse, it is what the exit condition at the top of this file
// is about, and a build that has dropped it has dropped the thing being
// measured -- which the very next premise asserts in its own right. A file that
// can only leave the build when this test retires is the strongest anchor
// available here.
const char *kServerListAnchor = "sourcesharp/game/server/shell_mapparse.cpp";

std::vector< std::string > CompiledServerSources()
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	return std::vector< std::string >( compiled.begin(), compiled.end() );
}

bool IsCompiled( const std::string &strPath )
{
	const std::vector< std::string > files = CompiledServerSources();
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[ i ] == strPath )
			return true;
	}
	return false;
}

// Every compiled .cpp, comments gone, concatenated. One read of the tree for
// the several questions below to be asked of.
const std::string &CompiledServerCode()
{
	static std::string s_all;
	static bool s_bDone = false;
	if ( !s_bDone )
	{
		s_bDone = true;
		const std::vector< std::string > files = CompiledServerSources();
		for ( size_t i = 0; i < files.size(); i++ )
			s_all += ReadStripped( files[ i ] );
	}
	return s_all;
}

//-----------------------------------------------------------------------------
// Does the compiled server register a factory for this classname, by ANY of the
// three spellings this tree uses?
//
//   LINK_ENTITY_TO_CLASS( name, Class )      the SDK macro
//   InstallFactory( this, "name" )           what world_adapter.cpp does
//   InstallFactory( &x, "name" )             the same, from a named instance
//
// Whitespace inside the macro call varies across the tree (`subs.cpp` writes
// `LINK_ENTITY_TO_CLASS(info_null,CNullEntity)` with none at all), so the
// classname is looked for after the opening paren with spaces skipped rather
// than by matching a fixed string.
//-----------------------------------------------------------------------------
bool RegistersFactoryFor( const std::string &all, const std::string &strClassName )
{
	static const char *kMacro = "LINK_ENTITY_TO_CLASS";
	for ( size_t at = all.find( kMacro ); at != std::string::npos;
		at = all.find( kMacro, at + 1 ) )
	{
		size_t p = all.find( '(', at );
		if ( p == std::string::npos )
			continue;
		p++;
		while ( p < all.size() && ( all[ p ] == ' ' || all[ p ] == '\t' || all[ p ] == '\n' ) )
			p++;
		if ( all.compare( p, strClassName.size(), strClassName ) == 0 )
		{
			const char c = ( p + strClassName.size() < all.size() )
				? all[ p + strClassName.size() ] : '\0';
			// A whole token, so `info_node` does not match `info_node_climb`.
			if ( c == ',' || c == ' ' || c == '\t' || c == '\n' )
				return true;
		}
	}

	// The factory-dictionary spelling, which takes the name as a string
	// literal. Searched for as `"name"` inside an InstallFactory call.
	const std::string quoted = "\"" + strClassName + "\"";
	static const char *kInstall = "InstallFactory";
	for ( size_t at = all.find( kInstall ); at != std::string::npos;
		at = all.find( kInstall, at + 1 ) )
	{
		const size_t close = all.find( ')', at );
		if ( close == std::string::npos )
			continue;
		if ( all.find( quoted, at ) != std::string::npos
			&& all.find( quoted, at ) < close )
		{
			return true;
		}
	}
	return false;
}

// The three residue classnames this build does not construct.
const char *kAbsentResidueClasses[] =
{
	"info_node",
	"func_useableladder",
	"info_ladder_dismount",
};
const size_t kAbsentResidueCount =
	sizeof( kAbsentResidueClasses ) / sizeof( kAbsentResidueClasses[ 0 ] );

} // namespace

//=============================================================================
// THE PREMISES. Each of these is a property of a file this test does not own,
// so each is asserted rather than assumed — a scanner that quietly read nothing
// would otherwise report every absence below as a pass.
//=============================================================================

TEST( MapResidueFactories, TheServerSourceListIsARealFileList )
{
	// The old "parsed to more than 100 sources" floor is retired:
	// CompiledSources throws unless compile_commands.json and the CMakeLists
	// list agree exactly, so an unread or truncated list is a failure, never a
	// short answer. The anchor remains the proof-of-read.
	EXPECT_TRUE( IsCompiled( kServerListAnchor ) )
		<< "the anchor row " << kServerListAnchor << " is not compiled into "
		   "server_sourcesharp (src/server/native/CMakeLists.txt).";
}

TEST( MapResidueFactories, TheNativeMapParseIsStillInTheBuild )
{
	// The whole subject: if mapentities.cpp has left the build then the residue
	// has nowhere to go and this file is measuring a build that no longer
	// exists. It is the row the exit condition is about.
	EXPECT_TRUE( IsCompiled( "sourcesharp/game/server/shell_mapparse.cpp" ) )
		<< "mapentities.cpp is no longer compiled -- if that is deliberate, "
		   "this test's subject is gone and it should be retired with the row.";
}

//=============================================================================
// THE FINDING.
//=============================================================================

TEST( MapResidueFactories, WorldspawnIsTheOneResidueClassThisBuildConstructs )
{
	const std::string &all = CompiledServerCode();
	ASSERT_FALSE( all.empty() ) << "read no compiled source at all";

	EXPECT_TRUE( RegistersFactoryFor( all, "worldspawn" ) )
		<< "nothing in the compiled server registers a worldspawn factory. "
		   "The native map parse would construct no world at all.";
}

TEST( MapResidueFactories, TheOtherThreeResidueClassesHaveNoFactoryInThisBuild )
{
	const std::string &all = CompiledServerCode();
	ASSERT_FALSE( all.empty() ) << "read no compiled source at all";

	for ( size_t i = 0; i < kAbsentResidueCount; i++ )
	{
		EXPECT_FALSE( RegistersFactoryFor( all, kAbsentResidueClasses[ i ] ) )
			<< kAbsentResidueClasses[ i ] << " now HAS a factory in the "
			   "compiled server. That is progress rather than a fault -- but "
			   "this file records it as absent, and the record has to move "
			   "with the tree.";
	}
}

//=============================================================================
// THE ARMED PARSE, AND THE HOLE IT HAD IN IT. Lane `worldspawn`.
//
// Everything above this line is true of the UNARMED build and stayed true
// while the armed one could not load a map at all. `f-srvshim`'s a06ef86c7 put
// the whole call to MapEntity_ParseAllEntities behind `#ifndef
// SS_CBASEENTITY_ALIAS` in shell_level.cpp, on the measured ground that
// "armed the dictionary it asks holds one name, which no map carries". The two
// instruments behind that sentence were a source scan for LINK_ENTITY_TO_CLASS
// and an objdump scan for CEntityFactory<T>.
//
// worldspawn is registered by NEITHER -- which is the finding this very file
// already records, four screens up: "its factory is world_adapter.cpp ...
// registering itself into EntityFactoryDictionary() rather than through
// LINK_ENTITY_TO_CLASS". RegistersFactoryFor above exists precisely because one
// spelling was not enough, and both of the guard's instruments used only that
// one. So the armed build parsed no world, ran no CWorld::Spawn, ran no
// CWorld::Precache, left EntityPrecache_IsAllowed() false for the whole level
// init, and died in the engine's ray cast on the first decal that traced.
//
// THE TESTS BELOW ARE NOT "THE GUARD IS GONE". The guard is still there and
// still correct for everything except one classname; what is asserted is that
// the ARMED SIDE OF IT REACHES THE WORLD, which is a positive claim about a
// named function rather than the absence of a `#ifndef`.
//=============================================================================

