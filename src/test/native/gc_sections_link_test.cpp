//========= SourceSharp — GC_SECTIONS=0 still links ==========================//
//
// A SOURCE SCANNER, for the usual reason -- a gtest linked against gtest and
// nothing else cannot run a link of server.so -- and for one more that matters
// here: the thing being defended is a BUILD CONFIGURATION, and the only real
// proof is `make debug GC_SECTIONS=0`. This file cannot be that proof. What it
// can do is pin the facts that, when they stopped being true, made that
// configuration fail; so it is a tripwire on a known failure MODE, and it says
// so rather than pretending to be the link.
//
// WHAT WENT WRONG, ON BOTH SIDES, AND IT IS WORTH KEEPING EVEN THOUGH THE
// SUBJECT HAS SINCE LEFT THE BUILD. Makefile:30 recommends `GC_SECTIONS=0` as
// "the first thing to try when a symbol goes missing without a link error". In
// this tree it once failed the SERVER link with eight undefined references to
// three symbols, and -- once the clientside-prop eviction landed -- the CLIENT
// link with four more to one of them:
//
//     BreakModelCreateSingle              4x   props_shared.cpp
//     PropBreakableCapEdictsOnCreateAll   2x   props_shared.cpp
//     breakable_multiplayer               2x   props_shared.cpp
//
// game/shared/props_shared.cpp was the whole prop-breaking system and BOTH DLLs
// compiled it. It went on declaring these `extern` and naming them from
// PropBreakableCreateAll and CreateGibsFromList, while the file that DEFINED
// them was evicted on each side -- game/server/props.cpp and
// game/client/physpropclientside.cpp. The repair was a stub,
// sourcesharp/game/shared/shell_props_absent.cpp, supplying all three.
//
// WHY THE NORMAL BUILD HID IT, WHICH IS THE PART WORTH REMEMBERING. Nothing
// reachable called either of those two functions any more, on either side. So
// --gc-sections collected the CALLERS before the linker ever needed the
// callees, and both evictions looked clean. Turn dead-code removal off and the
// callers are retained, and their references become undefined symbols.
//
// So the documented first-resort diagnostic was itself broken. That is a
// specially bad thing to break: GC_SECTIONS=0 is only ever reached for while
// something ELSE is already wrong, so the cost lands on someone who is already
// debugging and who has no reason to suspect the tool.
//
// THIS CLASS IS INVISIBLE TO EVERY OTHER CHECK IN THE TREE. `make` passes.
// `make test` passes. src/evict_probe -- the tool whose whole job is to say
// whether an eviction is safe -- relinks using the exact link command ninja
// uses, which carries `-Wl,--gc-sections` like every other build, so it is
// structurally blind to it.
//
//=============================================================================
// AND THE SUBJECT IS GONE NOW, WHICH IS WHY THIS FILE IS A THIRD ITS OLD SIZE.
//
// plan_shared.md Phase 5 took game/shared/props_shared.cpp out of BOTH
// projects. The three symbols were named only by PropBreakableCreateAll and
// CreateGibsFromList, and those two had no caller in either build -- their
// callers had all been evicted, which is the very fact that made the failure
// invisible in the first place. So they left with the file, the stub's remit
// emptied, and shell_props_absent.cpp is deleted.
//
// THE FAILURE MODE DID NOT GO WITH THEM, and that is what this file still
// exists for. It is: an evicted file leaves a surviving one naming a symbol
// nothing defines, and --gc-sections hides it. Any future eviction can
// re-create it. So what is pinned here is the CURRENT shape of the same
// question --
//
//   1. props_shared.cpp is in NEITHER project, so its externs are on no link
//      line and cannot come undone. This is the fact that replaced all three
//      of the old legs at once.
//   2. The file that replaced it, shell_propdata_system.cpp, is gone too (C9):
//      nothing on the client read what it built.
//   3. shell_props_absent.cpp is GONE rather than kept as an empty file: a stub
//      whose remit has emptied is dead code that looks like a defence.
//   4. The Makefile still documents the diagnostic and still carries the note
//      about how an eviction can break it invisibly. That note is the only
//      place the failure mode is written down where somebody about to evict a
//      file would see it.
//
// -- and the real proof stays what it always was: `make debug GC_SECTIONS=0`,
// run in this phase, zero undefined references.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <string>
#include <sys/stat.h>

namespace
{

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

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream f( ( std::string( pszRoot ) + "/" + pszRel ).c_str() );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

} // namespace

TEST( GcSectionsLinkFixture, EveryFileWasReallyOpened )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "no src/ tree found";

	// props_shared.cpp was read here too while it was evicted but still on
	// disk. Neither DLL compiles it, so it has left the source tree and that
	// half was culled; so has shell_propdata_system.cpp, the file that
	// replaced it (C9), and the tripwire that read it went with it.
	EXPECT_TRUE( Has( Read( "../Makefile" ), "GC_SECTIONS" ) )
		<< "the Makefile did not load";
}

TEST( GcSectionsLink, PropsSharedIsInNeitherProject )
{
	// THE FACT THAT REPLACED THE OLD THREE LEGS. While props_shared.cpp was
	// compiled, the three symbols had to be defined by somebody or
	// GC_SECTIONS=0 broke. It is compiled by nobody now, so they do not have to
	// be -- and the whole class of failure is closed rather than defended
	// against.
	// What the two DLLs compile (cmake_graph.h), each with a proof-of-read.
	// shell_propdata_system.cpp, the file that replaced props_shared.cpp, left
	// the server in P5 lane SP and the client in C9 (its CPropData parsed
	// propdata.txt for a chunk table nothing on the client read, and its
	// "precache" was modelinfo->GetModelIndex, a lookup), and is deleted. The
	// proofs-of-read are files each DLL does compile.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_serversystems.cpp" ) );
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_clientsystems.cpp" ) );
	for ( const char *pszTarget : { cmake_graph::kServer, cmake_graph::kClient } )
		EXPECT_FALSE( cmake_graph::Compiles( pszTarget, "sourcesharp/game/shared/shell_propdata_system.cpp" ) )
			<< "shell_propdata_system.cpp is back in " << pszTarget << "; it was deleted with nothing reading it.";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/shared/props_shared.cpp" ) )
		<< "props_shared.cpp is back in the server project. It names three "
		   "symbols nothing in this build defines, and --gc-sections will hide "
		   "that until somebody runs GC_SECTIONS=0.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/shared/props_shared.cpp" ) )
		<< "props_shared.cpp is back in the client project; see above.";
}

TEST( GcSectionsLink, TheStubIsGoneRatherThanEmptied )
{
	// A stub whose remit has emptied is dead code that LOOKS like a defence,
	// which is worse than no defence: the next reader takes it for one.
	EXPECT_FALSE( Exists( "sourcesharp/game/shared/shell_props_absent.cpp" ) )
		<< "shell_props_absent.cpp is back. If something needs those three "
		   "symbols again, say what in this test's header first -- the whole "
		   "argument above is about which file names them.";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/shared/shell_props_absent.cpp" ) );
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/shared/shell_props_absent.cpp" ) );
}

TEST( GcSectionsLink, TheMakefileStillRecommendsTheDiagnostic )
{
	const std::string mk = Read( "../Makefile" );

	EXPECT_TRUE( Has( mk, "GC_SECTIONS=0 turns it off" ) )
		<< "Makefile no longer documents GC_SECTIONS=0. It was broken once and "
		   "the fix was to make it work again, not to withdraw the advice -- if "
		   "it is genuinely unfixable now, say so there WITH the reason rather "
		   "than removing the paragraph.";

	EXPECT_TRUE( Has( mk, "KEEP THIS WORKING" ) )
		<< "the Makefile no longer carries the note explaining that an eviction "
		   "can break GC_SECTIONS=0 invisibly. That note is the only place the "
		   "failure MODE is written down where somebody about to evict a file "
		   "would see it -- and this phase is proof the mode outlives any "
		   "particular file.";
}
