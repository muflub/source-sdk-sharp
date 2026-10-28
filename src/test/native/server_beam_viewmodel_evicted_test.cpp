//====== SourceSharp — CBeam and CBaseViewModel leave the SERVER build ========//
//
// Three `-$File` lines in server_csharp.vpc (then; now three files absent from
// server_sourcesharp's list in src/server/native/CMakeLists.txt):
//
//   game/shared/beam_shared.cpp          CBeam            DT_Beam
//   game/shared/baseviewmodel_shared.cpp CBaseViewModel   DT_BaseViewModel
//   game/server/baseviewmodel.cpp        (the server half of the same class)
//
// Both classes are still CONSTRUCTIBLE in principle -- each has a live
// LINK_ENTITY_TO_CLASS -- and neither is constructed. That is the whole claim,
// and it is not one a linker can make: a factory registered under a classname is
// reached by a STRING, from a .bsp or from managed code, and no symbol appears
// anywhere in the link when it is.
//
// WHAT THE LINK ALREADY AGREES ABOUT, and therefore what this does not re-check:
// `./evict_probe {release,debug} server beam_shared.o baseviewmodel_shared.o
// baseviewmodel.o` answers exit 0, 0 undefined references, in BOTH
// configurations. Nothing compiled into the server derives from CBeam or
// CBaseViewModel and nothing calls a member of either; a compiler would say so.
//
// WHAT THE LINK CANNOT SEE, which is everything below:
//
//   * THE CLASSNAMES. `beam` and `viewmodel` are registered by these files and
//     leave with them. A map that declares either gets "can't init <classname>"
//     and no entity, at load time, with nothing failing at build time. So every
//     map in the tree is scanned -- the tracked .bsp and the .vmf the sandbox is
//     built from.
//
//   * THE JOIN, and BOTH PAIRS HAVE NOW LEFT BOTH ENDS. When this file was
//     written the client still compiled beam_shared.cpp and
//     baseviewmodel_shared.cpp, so it kept receive tables whose send tables were
//     gone -- the SAFE direction, since a receive table nothing sends is never
//     decoded, whereas a class the server sends and the client cannot decode
//     breaks the connection. The client cliff lane then took beam_shared.cpp,
//     and lane U3 (ledger.md cluster 8) has taken baseviewmodel_shared.cpp and
//     c_baseviewmodel.cpp with it. So the one-ended shape has expired for both,
//     and what is guarded now is that neither comes BACK on the client alone.
//
//   * WHY ONLY THE .cpp FILES GO. sourcesharp/game/server/shell_entityapi.cpp
//     and shell_worldquery.cpp still `#include "beam_shared.h"` and name nothing
//     in it -- which is why the probe was clean -- so the header stays on disk
//     exactly as props.h does. Both halves are asserted: lose the include and the
//     header becomes deletable; gain a use and the link is about to disagree.
//
//   * THE REPLACEMENTS. ss_beam and ss_viewmodel are managed ScriptedEntities on
//     the parallel hierarchy. SsBeam.cs says "It replaces a native CBeam" in
//     prose; this asserts the registration rather than the prose.
//
// ANCHORS, NOT LINE COUNTS. Every named file is asserted to still contain a
// string that identifies it, and the map walk is asserted to have visited
// something. A source-scanning test whose
// subject has been renamed reads an empty string and reports every absence
// invariant as satisfied: the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sstream>
#include <dirent.h>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as client_orphan_evictions_test.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[i] ) + "/game";
		const std::string pub = std::string( kCandidates[i] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) && stat( pub.c_str(), &st ) == 0 &&
			 S_ISDIR( st.st_mode ) )
		{
			return kCandidates[i];
		}
	}
	return NULL;
}

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// THE ANCHOR MOVED; NOTHING THIS FILE ASSERTS DID.
//
// It used to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP
// REMOVED THAT ROW from both vpcs along with the rest of the entity hierarchy.
// A stock TU is not an anchor for this port -- removing stock TUs is precisely
// what the port does, so an anchor on one is a proof-of-read the flip can evict.
// Stale here is the worst case: this anchor is the only thing stopping every
// absence invariant it guards from passing vacuously against a file it is not
// really reading.
//
// So the proof-of-read is now the PORT'S OWN shim, which no flip can take, and
// it is the anchor the client-side entries in this family already use.
//
// WHAT EACH SIDE COMPILES is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). Until Phase 6b it was server_csharp.vpc
// and client_csharp.vpc. The proof-of-read is the same shim on both sides.
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// the trace slots left at plan_server_native_v2 T4f

// Does this target leave the file out of the build?
//
// A file is out when NO compiled source of the target has its BASENAME -- the
// same strength the flattened-vpc check had (it refused the basename in any
// directory as well as the full path), so a copy moved to another directory
// still counts as back. cmake_graph throws on a missing, empty or stale list;
// the proof-of-read below is what stops a reader that saw the wrong list from
// answering "excluded" for everything.
bool Excludes( const char *pszTarget, const std::string &strPath )
{
	const std::set<std::string> compiled = cmake_graph::CompiledSources( pszTarget );

	EXPECT_TRUE( compiled.count( kProofOfRead ) ) << pszTarget << " does not compile " << kProofOfRead
												  << " -- this is not the target's source list, and every exclusion "
													 "below would pass vacuously";

	const std::string strBase = strPath.substr( strPath.rfind( '/' ) + 1 );
	for ( std::set<std::string>::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == strBase )
			return false;
	}
	return true;
}

bool EndsWith( const std::string &s, const char *pszSuffix )
{
	const std::string suf( pszSuffix );
	return s.size() >= suf.size() && s.compare( s.size() - suf.size(), suf.size(), suf ) == 0;
}

std::string Join( const std::set<std::string> &s )
{
	std::string out;
	for ( std::set<std::string>::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( !out.empty() )
			out += ", ";
		out += *it;
	}
	return out.empty() ? std::string( "<nothing>" ) : out;
}

//-----------------------------------------------------------------------------
// THE MAP WALK.
//
// A classname reaches a factory as a STRING out of a map, so the map set is the
// population that decides whether a LINK_ENTITY_TO_CLASS is live. Both the
// compiled .bsp files under game/mod_sharp/maps and the .vmf sources under
// maps/ are read whole, as bytes: a .bsp's entity lump is plain text in the same
// `"classname" "<name>"` form the .vmf uses, which is what makes one scanner
// enough for both.
//
// ss_sandbox.bsp is GENERATED and .gitignore'd, so it is present in a built tree
// and absent in a fresh clone. The walk therefore requires the two TRACKED maps
// by name and takes whatever else it finds -- an absence invariant over an empty
// map set is satisfied by every classname there has ever been.
//-----------------------------------------------------------------------------
std::string ReadWholeBinary( const std::string &strPath )
{
	std::ifstream file( strPath.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();
	std::ostringstream ss;
	ss << file.rdbuf();
	return ss.str();
}

// Every map file, as <relative-to-repo-root, whole contents>.
const std::vector<std::pair<std::string, std::string>> &MapFiles()
{
	static std::vector<std::pair<std::string, std::string>> s_maps;
	static bool s_bDone = false;
	if ( !s_bDone )
	{
		s_bDone = true;
		const char *pszSrc = SrcRoot();
		if ( pszSrc )
		{
			// SrcRoot()/.. is the repo root whichever of the three candidates
			// matched, because all three name the src directory itself.
			const std::string repo = std::string( pszSrc ) + "/..";
			static const char *kDirs[] = { "game/mod_sharp/maps", "maps" };
			for ( size_t d = 0; d < sizeof( kDirs ) / sizeof( kDirs[0] ); d++ )
			{
				std::vector<std::string> names;
				DIR *pDir = opendir( ( repo + "/" + kDirs[d] ).c_str() );
				if ( !pDir )
					continue;
				struct dirent *pEnt;
				while ( ( pEnt = readdir( pDir ) ) != NULL )
				{
					const std::string name( pEnt->d_name );
					if ( EndsWith( name, ".bsp" ) || EndsWith( name, ".vmf" ) )
						names.push_back( name );
				}
				closedir( pDir );
				for ( size_t i = 0; i < names.size(); i++ )
				{
					const std::string rel = std::string( kDirs[d] ) + "/" + names[i];
					const std::string body = ReadWholeBinary( repo + "/" + rel );
					if ( !body.empty() )
						s_maps.push_back( std::make_pair( rel, body ) );
				}
			}
		}
	}
	return s_maps;
}

// Which maps declare `"classname" "<name>"`.
std::set<std::string> MapsDeclaring( const char *pszClassName )
{
	std::set<std::string> out;
	const std::string needle = std::string( "\"classname\" \"" ) + pszClassName + "\"";
	const std::vector<std::pair<std::string, std::string>> &maps = MapFiles();
	for ( size_t i = 0; i < maps.size(); i++ )
	{
		if ( maps[i].second.find( needle ) != std::string::npos )
			out.insert( maps[i].first );
	}
	return out;
}

// The classnames the evicted files register, and the managed name that has taken
// each one's job. Neither classname may appear in a map or in managed code.
struct ClassName_t
{
	const char *pszStock;
	const char *pszManaged;
};

const ClassName_t kClassNames[] = {
	{ "beam", "ss_beam" },
	{ "viewmodel", "ss_viewmodel" },
};
const size_t kClassNameCount = sizeof( kClassNames ) / sizeof( kClassNames[0] );

} // namespace

TEST( ServerBeamViewModelEvicted, TheMapWalkReadsTheMaps )
{
	const std::vector<std::pair<std::string, std::string>> &maps = MapFiles();

	std::set<std::string> names;
	for ( size_t i = 0; i < maps.size(); i++ )
		names.insert( maps[i].first );

	// The two TRACKED maps. ss_sandbox.bsp is generated and .gitignore'd, so it
	// is deliberately not required -- but dm_lockdown.bsp and the sandbox's .vmf
	// source are in the repository and their absence means the walk is looking
	// somewhere else.
	EXPECT_TRUE( names.count( "game/mod_sharp/maps/dm_lockdown.bsp" ) )
		<< "the map walk found { " << Join( names )
		<< " }. Without a map to scan, "
		   "'no map declares this classname' is satisfied by every classname there "
		   "has ever been";
	EXPECT_TRUE( names.count( "maps/ss_sandbox.vmf" ) ) << "the map walk found { " << Join( names ) << " }";

	// ...and the scanner finds what is really there. worldspawn is in every map
	// ever compiled; func_wall is the classname bmodels.cpp was REFUSED over, and
	// it is in the sandbox. A scanner that answers "no" to everything passes
	// every absence test in this file.
	EXPECT_FALSE( MapsDeclaring( "worldspawn" ).empty() )
		<< "the map scanner cannot find worldspawn, which every map has. It is "
		   "not reading the entity lump, and every absence answer it gives below "
		   "is worthless";
	EXPECT_FALSE( MapsDeclaring( "info_target" ).empty() )
		<< "the map scanner cannot find info_target, which both maps declare";
}

//-----------------------------------------------------------------------------
// The exclusion itself. Three `-$File` lines and nothing else -- the .cpp files
// stay on disk and the client goes on compiling two of them, so NO compiler
// anywhere sees this decision.
//-----------------------------------------------------------------------------
TEST( ServerBeamViewModelEvicted, TheServerBuildExcludesAllThreeFiles )
{
	static const char *kPaths[] = {
		"game/shared/beam_shared.cpp",
		"game/shared/baseviewmodel_shared.cpp",
		"game/server/baseviewmodel.cpp",
	};

	for ( size_t i = 0; i < sizeof( kPaths ) / sizeof( kPaths[0] ); i++ )
	{
		EXPECT_TRUE( Excludes( cmake_graph::kServer, kPaths[i] ) )
			<< kPaths[i]
			<< " is back in the server build. It brings back a "
			   "class nothing constructs, a consumer of baseentity.o, and -- for "
			   "the two shared files -- a send table the client already decodes "
			   "from a receive table nothing was sending to";
	}
}

//-----------------------------------------------------------------------------
// THE CLASSNAMES.
//
// This is the eviction's real risk. `beam` and `viewmodel` are live
// LINK_ENTITY_TO_CLASS registrations -- not guarded, not dead -- so the files
// are safe to lose only while nothing ASKS for either name. A map that does gets
// "can't init beam" in the console and no entity; managed code that does gets a
// NULL back from Create. Neither is a build failure.
//-----------------------------------------------------------------------------
TEST( ServerBeamViewModelEvicted, NoMapDeclaresTheClassnamesThatLeaveWithTheseFiles )
{
	for ( size_t i = 0; i < kClassNameCount; i++ )
	{
		const std::set<std::string> maps = MapsDeclaring( kClassNames[i].pszStock );
		EXPECT_TRUE( maps.empty() ) << "\"" << kClassNames[i].pszStock << "\" is declared by { " << Join( maps )
									<< " }. That classname's factory leaves the server "
									   "with its file: the entity would not spawn, the map would load "
									   "anyway, and nothing would fail at build time";
	}
}

//-----------------------------------------------------------------------------
// THE JOIN, from both ends -- and both pairs are now gone from both ends.
//
// The one-ended shape (server drops the send table, client keeps the receive
// table) was the safe direction and the same one 13e batches 4 and 5 left. It
// has now expired for BOTH classes, so what this guards is the reverse edit:
// putting either .cpp back on the client alone re-creates a receive table with
// no send table. It stays safe only while no file that IS still compiled
// re-declares one of the three names.
//-----------------------------------------------------------------------------
TEST( ServerBeamViewModelEvicted, TheClientBuildHasDroppedBothSharedTranslationUnits )
{
	// beam_shared.cpp: THE ONE-ENDED SHAPE HAS EXPIRED, and this assertion is
	// inverted from what it was.
	//
	// It used to read EXPECT_FALSE with the message "the client has ALSO dropped
	// beam_shared.cpp. That is not unsafe by itself -- nothing sends DT_Beam any
	// more -- but it means the pair is gone from both ends and this file's
	// reasoning about a one-ended table no longer describes the tree." That is
	// exactly what happened: the client cliff lane took beam_shared.cpp out of
	// client_csharp.vpc too, along with the 233-line
	// CViewRenderBeams::DrawBeam( C_Beam * ) that was its only referrer. The
	// prediction was right and it was written down, so the test moves rather than
	// being deleted -- what it now guards is that the pair stays gone from BOTH
	// ends, which is the state client_beam_evicted_test.cpp reasons about.
	EXPECT_TRUE( Excludes( cmake_graph::kClient, "game/shared/beam_shared.cpp" ) )
		<< "beam_shared.cpp is back in the CLIENT build. It left both sides -- "
		   "the server first, then the client -- and client_beam_evicted_test.cpp "
		   "is written against it being gone from both. Putting it back on the "
		   "client alone re-creates a receive table with no send table, which is "
		   "the safe direction but is no longer what either file describes.";

	// baseviewmodel_shared.cpp: THE SAME INVERSION, one lane later, and for the
	// reason the refusal itself predicted. The client cliff lane refused this one
	// because c_baseviewmodel.cpp's free function FormatViewModelAttachment is
	// live on the particle path and lifting it "buys nothing the eviction was
	// for". Under `using CBaseEntity = IGameEntity` it buys 37 member-access
	// sites that would otherwise need a hand port, so lane U3 lifted it into
	// shell_client_orphans.cpp and took both TUs. Assertion inverted, and this
	// test's name with it.
	EXPECT_TRUE( Excludes( cmake_graph::kClient, "game/shared/baseviewmodel_shared.cpp" ) )
		<< "baseviewmodel_shared.cpp is back in the CLIENT build. It left both "
		   "sides -- the server first, then the client -- and putting it back on "
		   "the client alone re-creates a receive table with no send table, which "
		   "is the safe direction but is no longer what this file describes.";

	// THE CLIENT'S PAIR, and it is a pair for the same reason the server's is.
	// c_baseviewmodel.cpp holds twenty-odd members of the class
	// baseviewmodel_shared.cpp emits the vtable for -- measured: 25 of
	// c_baseviewmodel.o's symbols are wanted by baseviewmodel_shared.o and by
	// nothing else in the link. Excluding one alone is an undefined vtable slot,
	// which a linker WOULD catch; stated here rather than relied on, in the same
	// shape as BothHalvesOfTheViewModelLeaveTogether does for the server.
	EXPECT_EQ( Excludes( cmake_graph::kClient, "game/shared/baseviewmodel_shared.cpp" ),
			   Excludes( cmake_graph::kClient, "game/client/c_baseviewmodel.cpp" ) )
		<< "one half of C_BaseViewModel is excluded from the CLIENT build and the "
		   "other is not. They leave together or not at all.";
}

//-----------------------------------------------------------------------------
// WHY ONLY THE .cpp FILES GO.
//
// No server translation unit includes beam_shared.h any more. Two did, naming
// nothing in it -- which is exactly why evicting the .cpp linked clean. Both
// dropped it in plan_server_novalve.md P5 (shell_entityapi.cpp in wave 8a,
// shell_worldquery.cpp in 8c, which now declares the RadiusDamage it was getting
// through it), because the header reaches player.h through baseplayer_shared.h.
// The header stays on disk for the CLIENT, which still compiles it
// (shell_te_backends_client.cpp, view_beams.cpp). Pinned both ways: the
// include does not come back, and nothing here starts naming CBeam.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE RETIRED API.
//
// SourceSharp_PlayerGetViewModelEntity was retired with CBasePlayer in 13c and
// its slot deleted in plan_server_native_v2 T2 (EngineBinding answers
// EntityHandle.None). If a view model entry point comes back and builds a
// CBaseViewModel, this eviction is the thing that makes it a NULL.
//-----------------------------------------------------------------------------
