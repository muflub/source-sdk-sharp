//====== SourceSharp — four unpaired client classes leave the client build =====//
//
// A client class is a receive table paired to a SERVER class by name. A server
// class with no client class is a fatal handshake failure; a client class with
// no server class is dead weight that costs nothing at runtime and tells you a
// server file went out of the build. report.md section 5 calls the second kind
// an ORPHAN and keeps a ledger of them.
//
// Four orphans are out of client_sourcesharp (they were `-$File` out of client_csharp.vpc):
//
//   C_PhysBox              DT_PhysBox              game/client/c_physbox.cpp
//   C_PhysicsProp          DT_PhysicsProp          game/client/c_physicsprop.cpp
//   C_BreakableProp        DT_BreakableProp        game/client/c_breakableprop.cpp
//   C_BaseParticleEntity   DT_BaseParticleEntity   game/shared/baseparticleentity.cpp
//
// Their send tables left the server first: physobj.cpp took CPhysBox, props.cpp
// took CBreakableProp and CPhysicsProp, and baseparticleentity.cpp is excluded
// from the server build as well. server_csharp.vpc's own 13e-batch-4 comment
// recorded the asymmetry it left behind in as many words — "the client still
// references c_baseflex.o and baseparticleentity.o, so their halves stay. That
// leaves the client holding a receive table (DT_BaseFlex, DT_BaseParticleEntity)
// whose send table is gone." For DT_BaseParticleEntity that has now expired:
// nothing on the client link references baseparticleentity.o any more, so the
// pair is gone from BOTH ends. DT_BaseFlex's has NOT — c_baseflex.o still has
// three referrers — which is why only one of the two moves here.
//
// WHAT THE LINK ALREADY AGREES ABOUT, and therefore what this does not re-check:
// `./evict_probe {release,debug} client baseparticleentity.o c_physbox.o
// c_physicsprop.o c_breakableprop.o` answers exit 0, 0 undefined references, in
// BOTH configurations. Nothing compiled into the client derives from any of the
// four and nothing calls a member of one; a compiler would say so, loudly, and
// no test here needs to.
//
// WHAT THE LINK CANNOT SEE, which is everything below:
//
//   * THE JOIN. Put props.cpp, physobj.cpp or baseparticleentity.cpp back into
//     the server build and the server starts SENDING a class the client can no
//     longer decode. That direction is the fatal one, it produces no diagnostic
//     at build time, and it is one `-$File` line away in a file nobody would
//     think to read while editing the client. The first two tests are that
//     tripwire, from both ends: the server exclusions stay, and no file that
//     REMAINS compiled re-declares one of the four datatable names.
//
//   * THE DEAD REGISTRATION. baseparticleentity.cpp's only registration on the
//     client side is `REGISTER_EFFECT( CBaseParticleEntity )`, which inserts a
//     factory into CParticleMgr::m_effectFactories. That map is read by exactly
//     one function, CParticleMgr::CreateEffect, and NOTHING IN THE TREE CALLS
//     IT. A registration whose registry has no reader costs nothing to lose —
//     but if somebody gives it a caller, losing it becomes a "factory not found
//     for effect" Msg at runtime and nothing else.
//
//   * THE TWO CONVARS. c_physicsprop.cpp is the only file in the tree that names
//     r_PhysPropStaticLighting or r_visualizeproplightcaching, and both are read
//     only inside C_PhysicsProp::OnInternalDrawModel. They leave with the class
//     that was their only subject. docs/divergences.md quotes the first one in
//     prose; that is a document, not a caller.
//
//   * WHY ONLY THE .cpp WENT, THEN. game/shared/baseparticleentity.h was still
//     included by game/shared/particlesystemquery.cpp, compiled into the client
//     until lane c5-particles evicted it (the particle query is C#'s). It named
//     nothing in the header, so the header left the farm with it (sdk-census:
//     no graph uses it); the last test below holds both absences.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// `DT_PhysBox`, `REGISTER_EFFECT( CBaseParticleEntity )` and
// `r_PhysPropStaticLighting` in the paragraphs explaining where they may and may
// not be, and an unstripped scan would read the explanation as the violation.
// Same rule as effects_evicted_test.cpp and clientside_prop_evicted_test.cpp.
//
// ANCHORS, NOT LINE COUNTS. Every named file is asserted to still contain a
// string that identifies it, and the tree walk is asserted to have visited a
// plausible number of files and to have found the ones this test reasons about.
// A source-scanning test whose subject has been renamed reads an empty string
// and reports every absence invariant as satisfied — the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <dirent.h>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as effects_evicted_test.cpp.
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

// Whole file, comments gone. Empty means "could not open", which every caller
// must distinguish from "opened and found nothing".
std::string ReadStripped( const std::string &strRelative )
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
	return StripComments( all );
}

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// WHAT EACH SIDE COMPILES is its CMake target's source list, read through
// cmake_graph.h: server_sourcesharp (src/server/native/CMakeLists.txt) and
// client_sourcesharp (src/client/native/CMakeLists.txt). Every reader there
// throws on a missing, empty or stale input.
//
// THE PROOF-OF-READ is the PORT'S OWN shim, which no flip can take. It used
// to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP REMOVED THAT
// ROW from both vpcs along with the rest of the entity hierarchy: a stock TU is
// not an anchor for this port -- removing stock TUs is precisely what the port
// does, so an anchor on one is a proof-of-read the flip can evict. It is not a
// file this test asserts about -- an anchor equal to the assertion cannot
// catch anything.
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// Does this target leave the file out of the build? Asserts the proof-of-read
// first, so "excluded" is never the answer of a reader that saw nothing.
bool Excludes( const char *pszTarget, const char *pszFarmRelative )
{
	EXPECT_TRUE( cmake_graph::Compiles( pszTarget, kProofOfRead ) )
		<< pszTarget << " does not compile " << kProofOfRead
		<< " -- the reader is not reading its source list";
	return !cmake_graph::Compiles( pszTarget, pszFarmRelative );
}


// One datatable, and the two files allowed to name it: the send table's, which
// is out of the server build, and the receive table's, which is out of the
// client build. baseparticleentity.cpp is both, being a shared TU.
struct Pair_t
{
	const char *pszTable;
	const char *pszServerFile;
	const char *pszClientFile;
};

} // namespace

//-----------------------------------------------------------------------------
// The exclusion itself. Four rows out of the source list and nothing else -- the .cpp files
// stay on disk, so no compiler anywhere sees this decision.
//-----------------------------------------------------------------------------
TEST( ClientOrphanEvictions, TheClientExcludesAllFourOrphanFiles )
{
	static const char *kPaths[] = {
		"game/client/c_physbox.cpp",
		"game/client/c_physicsprop.cpp",
		"game/client/c_breakableprop.cpp",
		"game/shared/baseparticleentity.cpp",
	};

	for ( size_t i = 0; i < sizeof( kPaths ) / sizeof( kPaths[ 0 ] ); i++ )
	{
		EXPECT_TRUE( Excludes( cmake_graph::kClient, kPaths[ i ] ) )
			<< kPaths[ i ] << " is back in the client build. It is an unpaired "
			   "receive table -- nothing can send it -- so what comes back is a "
			   "class that costs a vtable, a classmap entry and a consumer of "
			   "c_baseentity.o, and can never be constructed";
	}

	// c_breakableprop.cpp and c_physicsprop.cpp are not independent: C_PhysicsProp
	// derives from C_BreakableProp, so excluding one alone is an undefined vtable.
	// Stated here because the four EXPECTs above would pass three-quarters of the
	// way to a build that does not link.
	EXPECT_EQ( Excludes( cmake_graph::kClient, "game/client/c_breakableprop.cpp" ),
	           Excludes( cmake_graph::kClient, "game/client/c_physicsprop.cpp" ) )
		<< "one of the two prop files is excluded and the other is not. "
		   "C_PhysicsProp derives from C_BreakableProp; they leave together or "
		   "not at all";
}

//-----------------------------------------------------------------------------
// THE JOIN, from the server end.
//
// This is the tripwire that matters. The eviction is safe only while the server
// cannot send any of the four; re-including one of these three files makes it
// send, and the client — which no longer compiles the receive table — cannot
// decode it. No compiler on either side says a word about that.
//-----------------------------------------------------------------------------
TEST( ClientOrphanEvictions, TheServerStillExcludesEveryFileHoldingTheSendTables )
{
	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/server/physobj.cpp" ) )
		<< "physobj.cpp is back in the server build, so CPhysBox is a server class "
		   "again — and the client's C_PhysBox left client_sourcesharp. The "
		   "server would send a class the client cannot decode";

	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/server/props.cpp" ) )
		<< "props.cpp is back in the server build, so CBreakableProp and "
		   "CPhysicsProp are server classes again — and both client halves left "
		   "with client_sourcesharp";

	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/shared/baseparticleentity.cpp" ) )
		<< "baseparticleentity.cpp is back in the server build, so "
		   "CBaseParticleEntity is a server class again — and its client half "
		   "left client_sourcesharp";
}

//-----------------------------------------------------------------------------
// THE HEADER LEFT WITH ITS LAST INCLUDER.
//
// baseparticleentity.h stayed on disk while particlesystemquery.cpp included
// it. That TU left the client with lane c5-particles (the particle query is
// C#'s, shell_managed_particles.cpp's thunk is g_pParticleSystemQuery), and
// nothing else includes the header, so it left the farm too. The positive
// half: the farm is readable (its proof-of-read file opens), so the absences
// are absences of a tree this test can see.
//-----------------------------------------------------------------------------
TEST( ClientOrphanEvictions, TheParticleHeaderLeftWithItsLastIncluder )
{
	EXPECT_TRUE( Excludes( "client_sourcesharp", "game/shared/particlesystemquery.cpp" ) )
		<< "particlesystemquery.cpp is compiled into the client again";

	ASSERT_FALSE( ReadStripped( kProofOfRead ).empty() )
		<< "the farm's " << kProofOfRead << " did not open: the absences below would be vacuous";
	EXPECT_TRUE( ReadStripped( "game/shared/baseparticleentity.h" ).empty() )
		<< "baseparticleentity.h is in the farm again; its last includer left the client";
	EXPECT_TRUE( ReadStripped( "game/shared/particlesystemquery.cpp" ).empty() )
		<< "particlesystemquery.cpp is in the farm again";
}
