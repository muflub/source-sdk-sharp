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

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
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

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

std::string Tight( const char *pszRel, const char *pszAnchor )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	const std::string raw = ReadRaw( std::string( pszRoot ) + "/" + pszRel );
	EXPECT_FALSE( raw.empty() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";

	const std::string tight = NoWhitespace( StripComments( raw ) );
	EXPECT_NE( tight.find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " no longer contains its anchor \"" << pszAnchor
		<< "\" outside of comments; this test is reasoning about a file that "
		<< "has been rewritten.";
	return tight;
}

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

// Every usermessages->Register( "name", size ) in a comment-stripped file, in
// source order. Order is the entire point: the index IS the wire identity.
std::vector< std::string > RegistrationsIn( const std::string &strTight )
{
	std::vector< std::string > out;
	const std::string needle = "usermessages->Register(";
	for ( size_t at = strTight.find( needle ); at != std::string::npos;
	      at = strTight.find( needle, at + 1 ) )
	{
		const size_t close = strTight.find( ')', at );
		if ( close == std::string::npos )
			break;
		out.push_back( strTight.substr( at + needle.size(),
		                                close - at - needle.size() ) );
	}
	return out;
}

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
TEST( ServerWave2, TheLiftedHapticRegistrationIsGoneFromTheShell )
{
	// Anchored on a symbol that is still there. shell_shared.cpp no longer even
	// defines RegisterUserMessages -- step 3 removed its only caller along with
	// usermessages.cpp -- so anchoring on either name would make this test
	// reason about a file it could not find, which Tight() reports as a
	// separate failure rather than as this one.
	// (Anchored on SourceSharp_RegisterAmmoType until plan_server_native_v2 T2
	// made that slot the client's alone.)
	const std::string shell = Tight( "sourcesharp/game/shared/shell_shared.cpp",
	                                 "default_fov" );
	ASSERT_FALSE( shell.empty() );

	const std::vector< std::string > mine = RegistrationsIn( shell );

	EXPECT_EQ( mine.size(), 0u )
		<< "shell_shared.cpp registers " << mine.size() << " user message(s) "
		<< "natively. C# is the only registrar now, and anything registered "
		<< "from here lands at an index C# does not know about -- which shifts "
		<< "every managed message relative to the other side's.";

	EXPECT_EQ( shell.find( "voidRegisterHapticMessages(void){" ), std::string::npos )
		<< "shell_shared.cpp defines RegisterHapticMessages again. The client "
		<< "compiles haptic_msgs.cpp, which defines it too.";

	EXPECT_EQ( shell.find( "voidRegisterUserMessages(void){" ), std::string::npos )
		<< "shell_shared.cpp defines RegisterUserMessages again. Nothing calls "
		<< "it: usermessages.cpp held the only caller and is out of both "
		<< "builds. A definition here is either dead or a second registrar.";
}

TEST( ServerWave2, NothingInTheBuildStillCallsRegisterHapticMessages )
{
	// haptic_msgs.cpp still DEFINES it -- the file is on the client's link line
	// for its hooks and its senders. What must not come back is a caller: the
	// six would then be registered twice on the client and once on the server,
	// which is a fatal duplicate Register() at best and a six-index desync at
	// worst.
	//
	// Every SourceSharp file that could reach it, not just one: the shell where
	// the lift lived, and the two native user-message files left after the
	// list moved to C# (plan_side_isolation P2).
	const char *pszFiles[] = {
		"sourcesharp/game/shared/shell_shared.cpp",
		"sourcesharp/game/server/shell_usermessage_send.cpp",
		"sourcesharp/game/client/shell_usermessages_client.cpp",
	};
	const char *pszAnchors[] = {
		"default_fov",
		"SourceSharp_UserMessageSend",
		"SourceSharp_ClientViewportQuery",
	};

	for ( size_t i = 0; i < sizeof( pszFiles ) / sizeof( pszFiles[ 0 ] ); i++ )
	{
		const std::string code = Tight( pszFiles[ i ], pszAnchors[ i ] );
		ASSERT_FALSE( code.empty() ) << pszFiles[ i ];

		EXPECT_EQ( code.find( "RegisterHapticMessages();" ), std::string::npos )
			<< pszFiles[ i ] << " calls RegisterHapticMessages again. C# "
			<< "declares the same six at the head of its list, so this "
			<< "registers each of them a second time.";
	}
}

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
// plan_server_novalve.md P3: THE CACHE IS GONE, and the list it held is built
// in C# on every precache (SourceSharp.Server.ModelComponents), held live to
// the cache's own output first.
TEST( ServerWave2, TheModelSoundsCacheLeftForCSharp )
{
	const std::string pm = Tight( "sourcesharp/game/server/precache_model.cpp",
	                              "Precache_ModelComponents" );
	ASSERT_FALSE( pm.empty() );

	EXPECT_EQ( pm.find( "CModelSoundsCache" ), std::string::npos )
		<< "the model sounds cache is back in precache_model.cpp";
	EXPECT_EQ( pm.find( "modelsounds.cache" ), std::string::npos );
	EXPECT_NE( pm.find( "pArea->ModelPrecacheComponents(" ), std::string::npos )
		<< "Precache_ModelComponents no longer hands the model to C#";
}

//-----------------------------------------------------------------------------
// The precache_register fold: ONE list head, not two.
//-----------------------------------------------------------------------------
TEST( ServerWave2, ThereIsExactlyOnePrecacheListHead )
{
	const std::string wa = Tight( "sourcesharp/game/server/world_adapter.cpp",
	                              "CPrecacheRegister::Precache();" );
	ASSERT_FALSE( wa.empty() );

	EXPECT_NE( wa.find( "CPrecacheRegister*g_pPrecacheRegisters=0;" ), std::string::npos )
		<< "world_adapter.cpp does not define the list head, and "
		<< "precache_register.cpp is out of the build -- so nothing does.";

	EXPECT_NE( wa.find( "CPrecacheRegister::CPrecacheRegister(" ), std::string::npos )
		<< "the constructor that PUSHES onto the list is missing.";
	EXPECT_NE( wa.find( "voidCPrecacheRegister::Precache()" ), std::string::npos )
		<< "the walk is missing.";

	// The registrant lives in a different file and is the only thing on the
	// list; if it goes, the mechanism is still correct and precaches nothing.
	const std::string orphans = Tight( "sourcesharp/game/server/shell_absent.cpp",
	                                   "EffectsPrecache" );
	EXPECT_NE( orphans.find( "PRECACHE_REGISTER_FN(EffectsPrecache)" ), std::string::npos )
		<< "nothing registers with the precache list any more, so the walk in "
		<< "world_adapter.cpp iterates an empty list and six game sounds -- the "
		<< "splash, the underwater impact, the ricochet, the explosion -- go "
		<< "unprecached. The first play of each is silent.";
}

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
TEST( ServerWave2, IntervalLeftTheBuildAndNoNativeTranslationUnitCallsIt )
{
	ASSERT_TRUE( Compiles( cmake_graph::kServer, kProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	EXPECT_FALSE( Compiles( cmake_graph::kServer, "interval.cpp" ) )
		<< "interval.cpp is back in the server build. If that is deliberate, "
		<< "the inline definitions in game/shared/interval.h have to go with "
		<< "it -- two definitions of ReadInterval is a link error, and the one "
		<< "that wins is whichever the linker saw first.";

	// The premise that made Wave 2's eviction fail, still true -- but ASKED OF
	// A DIFFERENT FILE since plan_shim.md Phase 1d.
	//
	// It used to be asked of game/server/AI_Criteria.cpp, whose row Phase 1d
	// deleted: tools/shim-census reports 0 objects importing it on either
	// side, and its two BEGIN_SIMPLE_DATADESCs are imported by nothing
	// either. Those two assertions are retired with the row; "AI_Criteria.cpp
	// has left the build" is now simply true, and a test that says so is a
	// test about a file that is not there.
	//
	// THE CLAIM, as it stands since sound cluster 17 (lane c2-sims): interval.cpp
	// is out of the build and NO NATIVE TRANSLATION UNIT CALLS EITHER FUNCTION.
	// The callers went one by one: AI_Criteria.cpp (plan_shim.md Phase 1d),
	// shell_soundscape_client.cpp (the client soundscape system is C#, its
	// intervals SourceSharp.Shared.Interval, held to stock by IntervalTests and
	// SoundscapeOracleTests), and public/SoundParametersInternal.cpp, which
	// left client.so when its one importer (TextToSoundLevel) went with the
	// soundscape. So the inlines below are definitions with no reader: a
	// caller coming back still links (that is what they are kept for), and
	// this scan is what makes its return visible rather than silent.
	//
	// The scan reads every source both targets compile, comment-stripped; its
	// control is a call the client is known to make, so an empty read cannot
	// pass.
	size_t nRead = 0;
	bool bControl = false;
	std::vector< std::string > callers;
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t t = 0; t < 2; t++ )
	{
		const std::set< std::string > src = cmake_graph::CompiledSources( kTargets[ t ] );
		for ( std::set< std::string >::const_iterator it = src.begin(); it != src.end(); ++it )
		{
			const std::string raw = ReadRaw( std::string( SrcRoot() ) + "/" + *it );
			if ( raw.empty() )
				continue;
			nRead++;
			const std::string tight = NoWhitespace( StripComments( raw ) );
			if ( tight.find( "GameSound_Emit(filter,SOUND_FROM_WORLD,ep)" ) != std::string::npos )
				bControl = true;
			if ( tight.find( "ReadInterval(" ) != std::string::npos
				|| tight.find( "RandomInterval(" ) != std::string::npos )
				callers.push_back( *it );
		}
	}
	EXPECT_GT( nRead, 200u ) << "the scan read " << nRead << " compiled sources; it is not reading the source lists";
	EXPECT_TRUE( bControl )
		<< "the scan did not see shell_soundscape_audio_client.cpp's GameSound_Emit, so it is not reading text";
	for ( size_t i = 0; i < callers.size(); i++ )
		ADD_FAILURE() << callers[ i ] << " calls ReadInterval or RandomInterval. The rules are managed "
		              << "(SourceSharp.Shared.Interval); a native caller would be a second copy of "
		              << "them, held to stock by nothing.";

	// AND THE HALF WITHOUT WHICH THE ABOVE IS THE WAVE 2 BUG. The definitions
	// have to be somewhere, and with the .cpp gone the header is the only
	// somewhere there is.
	const std::string hdr = Tight( "game/shared/interval.h", "ReadInterval" );
	ASSERT_FALSE( hdr.empty() );
	EXPECT_NE( hdr.find( "inlineinterval_tReadInterval(" ), std::string::npos )
		<< "interval.h does not define ReadInterval inline, and interval.cpp "
		<< "is out of the build -- so nothing defines it. This is exactly the "
		<< "Wave 2 failure, and --gc-sections will hide it until somebody "
		<< "builds with GC_SECTIONS=0.";
	EXPECT_NE( hdr.find( "inlinefloatRandomInterval(" ), std::string::npos )
		<< "interval.h does not define RandomInterval inline; see above.";
}
