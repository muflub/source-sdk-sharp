//====== SourceSharp — plan_server_all.md Wave 2: two lifts and a fold =======//
//
// Wave 2's tractable set turned out to be small (docs/server_eviction_graph.md
// says why: 83 of 97 stock objects are blocked by a stock consumer). Three
// files left by three different routes and each has a way of going wrong that
// no link error would report.
//
//   haptic_msgs.cpp       LIFTED. Six usermessages->Register calls moved into
//                         shell_shared.cpp under `#ifdef GAME_DLL`. A user
//                         message travels on the wire as an INDEX INTO THE
//                         REGISTERED LIST, and the CLIENT still registers the
//                         same six from the original file. So the names, the
//                         sizes AND THE ORDER have to match, and a mismatch is
//                         silent: the client misreads every message after the
//                         divergence. That is what most of this file checks.
//
//   ModelSoundsCache.cpp  FOLDED into precache_model.cpp, which was already its
//                         only consumer and already owns the
//                         CUtlCachedFileData< CModelSoundsCache > it is an entry
//                         of. Not portable: the class IS a binary file format
//                         (modelsounds.cache), section 3's second case.
//
//   precache_register.cpp FOLDED into world_adapter.cpp, mechanism intact.
//
// ...and one that was taken out and PUT BACK, which is the entry that earns its
// keep: interval.cpp. See the last two tests.
//
// STRIPPER RULE, as everywhere in this series: rationale comments (in
// server_csharp.vpc then, in the shim files now) name each of these files while
// explaining why it went.
//
//=============================================================================//

#include <string>
#include <set>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

// WHAT EACH DLL COMPILES is its CMake target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt). Paths are farm-relative.
//
// MATCHED BY BASENAME, under any directory. The VPC reader this replaced had
// to accept two spellings (a bare basename for the project's own directory, a
// full $SRCDIR path for everything else) after its first version caught only
// one and let evicted files back in by full path. A farm-relative path has one
// spelling, but a relocated file (game/ -> sourcesharp/game/) must still answer,
// so the question stays "is a file OF THIS NAME compiled", and it still refuses
// a suffix match -- "c_baseentity.cpp" must not answer for "baseentity.cpp".
bool Compiles( const char *pszTarget, const char *pszBasename )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( pszTarget );
	const std::string base( pszBasename );
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		const size_t slash = it->find_last_of( '/' );
		if ( ( slash == std::string::npos ? *it : it->substr( slash + 1 ) ) == base )
			return true;
	}
	return false;
}

// The proof-of-read, for both targets: the port's own shim, which no eviction
// can take. An absence answered by a reader that saw nothing is free.
const char *const kProofOfRead = "shell_shared.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The three are out of the server's file list.
//-----------------------------------------------------------------------------
TEST( ServerWave2, TheThreeFoldedFilesAreOutOfTheServerList )
{
	ASSERT_TRUE( Compiles( cmake_graph::kServer, kProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	EXPECT_FALSE( Compiles( cmake_graph::kServer, "haptic_msgs.cpp" ) )
		<< "haptic_msgs.cpp is back in the server list. shell_shared.cpp now "
		<< "defines RegisterHapticMessages under `#ifdef GAME_DLL`, so both "
		<< "would define it and the link fails -- or worse, does not.";

	EXPECT_FALSE( Compiles( cmake_graph::kServer, "ModelSoundsCache.cpp" ) )
		<< "ModelSoundsCache.cpp is back. Its bodies are inside "
		<< "precache_model.cpp; two definitions of CModelSoundsCache::Save is a "
		<< "duplicate-symbol link.";

	EXPECT_FALSE( Compiles( cmake_graph::kServer, "precache_register.cpp" ) )
		<< "precache_register.cpp is back. g_pPrecacheRegisters is defined in "
		<< "world_adapter.cpp now, and TWO list heads means the registrant "
		<< "pushes onto one and the walk reads the other -- a silent no-op, not "
		<< "a link error, and six game sounds go unprecached.";
}

//-----------------------------------------------------------------------------
// The haptics lift is OVER, and this is what replaced it.
//
// plan_gamedll.md Phase 5a step 2 deleted the lifted RegisterHapticMessages
// from shell_shared.cpp and, with it, the only call to haptic_msgs.cpp's copy
// in this build. The six are now the first six [UserMessage] declarations in
// C#, so there is exactly one registrar and the engine index equals the C#
// index.
//
// The invariant this file used to hold -- the same six names, sizes and order
// on both sides -- went with the native list (plan_side_isolation P2): an id is
// carried by the SsUserMessages table, never by a position. What is left here
// is the part specific to Wave 2: that the lift's residue is gone, and gone
// from both sides rather than only from the server.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// And usermessages.cpp itself, which held the static-init registration, is out
// of BOTH builds. plan_gamedll.md Phase 5a step 3.
//
// Its replacement went too (plan_side_isolation P2): the list is C#'s, and what
// each side compiles is a file of forwarders -- shell_usermessage_send.cpp on
// the server, shell_usermessages_client.cpp on the client. Those are the
// positive controls; stock's file and the evicted native registry are the
// absences.
//-----------------------------------------------------------------------------
TEST( ServerWave2, StockUserMessagesCppIsOutOfBothBuilds )
{
	// Positive control first, per target, or an absence assertion over a list
	// that could not be read passes for the wrong reason.
	ASSERT_TRUE( Compiles( cmake_graph::kServer, "shell_usermessage_send.cpp" ) )
		<< "server_sourcesharp does not compile shell_usermessage_send.cpp, "
		<< "so it is not the file list this test thinks it is.";

	ASSERT_TRUE( Compiles( cmake_graph::kClient, "shell_usermessages_client.cpp" ) )
		<< "client_sourcesharp does not compile shell_usermessages_client.cpp, "
		<< "so it is not the file list this test thinks it is.";

	const char *pszGone[] = {
		"usermessages.cpp",
		"shell_usermessage_registry.cpp",
		"shell_usermessages.cpp",
	};

	for ( size_t i = 0; i < sizeof( pszGone ) / sizeof( pszGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Compiles( cmake_graph::kServer, pszGone[ i ] ) )
			<< pszGone[ i ] << " is back in the server list: a second user-message "
			<< "registry beside C#'s.";
		EXPECT_FALSE( Compiles( cmake_graph::kClient, pszGone[ i ] ) )
			<< pszGone[ i ] << " is back in the client list: a second user-message "
			<< "registry beside C#'s.";
	}
}

//-----------------------------------------------------------------------------
// The ModelSoundsCache fold.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The precache_register fold: ONE list head, not two -- and since
// plan_server_native_v2 T4e, none. The list's one registrant (EffectsPrecache,
// six game sounds) is managed's world precache, so the list head, its push,
// its walk and the registration all went; a head back here would be a second
// precache path beside the managed one.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// interval.cpp: taken out, put back, and taken out again -- by a DIFFERENT
// route, which is the whole content of this test now.
//
// THE HISTORY, because the next lane needs it and the assertion alone does not
// carry it. Wave 2 evicted interval.cpp on a probe that said FREE: 0 undefined
// refs, 0 consumers, in both configurations. It was not free. `make debug
// GC_SECTIONS=0` named four undefined references to RandomInterval from
// AI_Criteria.cpp -- the collector had been dropping the sections that
// referenced it, so the probe was measuring a link that had already thrown the
// evidence away. The file went back in and this test was written to stop the
// experiment being repeated.
//
// plan_shared.md Phase 3 removed it again and the premise did not evaporate:
// AI_CRITERIA.CPP STILL CALLS RandomInterval AND IS STILL IN THE BUILD. What
// changed is that the two functions are `inline` in game/shared/interval.h, so
// every caller compiles its own copy and there is nothing left to link
// against. That is the ONE route out of this trap that does not require the
// caller to leave first, and this test now pins it: the eviction and the
// inline definitions are one fact, and either without the other is the Wave 2
// failure again.
//
// VERIFIED THE WAY WAVE 2 SHOULD HAVE BEEN: `make debug GC_SECTIONS=0` links
// with zero undefined references. A probe under --gc-sections would still say
// FREE and still be measuring nothing.
//-----------------------------------------------------------------------------
