//========= SourceSharp — shard 2's .cpp edits, read as source ===============//
//
// A SOURCE SCANNER, like soundemitter_lift_test, precache_model_move_test and
// cpp_alias_lane1_test, and for the same reason: the claim is about the TEXT of
// files that need a whole engine to run, so the text is what is read.
//
// WHAT SHARD 2 DID, and it is deliberately NOT a set of #ifdef arms.
// docs/typedef-dryrun/shard2_classification.md classifies all 1,018 armed sites
// in the ten server .cpp files this shard took; 846 of them are EVICTION -- the
// class must stop deriving, and an armed arm would be a pure stub. Twenty-eight
// were not, and those are what this file speaks for. Each is an UNCONDITIONAL
// edit rather than a guard, because in each case the answer that survives the
// flip is also the right answer today:
//
//   * TWENTY-TWO calls to CBaseEntity::PrecacheScriptSound in client.cpp and
//     effects.cpp. That member is `static` -- it takes no `this` and reads no
//     entity -- and its body is GameSound_PrecacheScript. The class scope was
//     the entire dependency, so the call sites ask the free function. The
//     one-line forward that used to stand between them is gone: its quarantine,
//     game/shared/baseentity_sound_forwarders.cpp, was retired in route step 1
//     and soundemitter_lift_test now guards the retirement instead.
//
//   * THREE calls to CBaseEntity::IsPrecacheAllowed /
//     ::IsSimulatingOnAlternateTicks in util.cpp and physics.cpp. Same shape,
//     same lift already landed: entity_shared_statics.h.
//
//   * THREE sites where EntityFromEntityHandle's NULL is a LIVE bug rather than
//     a Phase 13 one. See the three sections below.
//
// WHY THIS NEEDS A GUARD, and none of these produces a diagnostic:
//
//   1. Somebody writes `CBaseEntity::PrecacheScriptSound` back into one of
//      these files -- it is what every other SDK file still says, so it is the
//      natural thing to copy. It compiles today and is a Phase 13 site again.
//   2. Somebody "simplifies" one of the three filter fixes back to
//      EntityFromEntityHandle. It compiles, the suite stays green, and the
//      NULL dereference comes back for exactly the entities this build is made
//      of.
//   3. A merge splice eats one of the null checks. Nothing says so.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The fixtures at
// the bottom assert that every file was really opened and is not trivial,
// and that comment stripping really strips (on synthetic input, because every
// edit here explains itself in prose that NAMES the member it stopped calling --
// matching raw text would read the name straight out of the sentence saying it
// is gone).
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
const char *SourceRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		const std::string probe = std::string( kCandidates[ i ] ) + "/sourcesharp/game/server";
		DIR *pDir = opendir( probe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and here that is load-bearing rather
// than tidiness. Every edit this file speaks for carries a comment naming the
// member it stopped calling -- "this was `TraceEntity( *pTrace )->entindex()`"
// is in util.cpp today. A scanner matching raw text would find
// `TraceEntity( *pTrace )` in the sentence saying it is gone and report a
// correct fix as broken. header_alias_test.cpp and cpp_alias_lane1_test.cpp
// were both caught by exactly this.
//-----------------------------------------------------------------------------
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
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string CodeOf( const std::string &strRelative )
{
	return StripComments( ReadFile( strRelative ) );
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	if ( strNeedle.empty() )
		return 0;
	int n = 0;
	for ( size_t p = strHay.find( strNeedle ); p != std::string::npos;
	      p = strHay.find( strNeedle, p + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

bool Has( const std::string &strHay, const std::string &strNeedle )
{
	return strHay.find( strNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// THE FILES, and the anchor each one is identified by. An anchor rather than a
// line count, because a count goes stale on every unrelated edit and then gets
// "fixed" by copying whatever the file says now -- which is the check that
// cannot fail, arriving by the back door.
//-----------------------------------------------------------------------------
// EffectsPrecache's home moved. game/server/effects.cpp is `-$File` out of
// server_csharp.vpc (see effects_evicted_test.cpp), and the function went with
// UTIL_BloodSpray to the orphans shell -- so the lift this file guards is
// guarded THERE now. The lift itself is unchanged: the class scope is gone and
// the free function is what is called.
const char *kEffects = "sourcesharp/game/server/shell_absent.cpp";

} // namespace


//=============================================================================//
// 1. The sound statics left the class at these call sites.
//=============================================================================//

TEST( Shard2Lifts, EffectsPrecacheAsksTheFreeSoundFunction )
{
	// EffectsPrecache left this file at plan_server_native_v2 T4e: its six
	// script sounds are managed's world precache (ServerGameDll.WorldPrecache,
	// through the SoundEmitterSystem's own precache). The lift still holds where
	// it was guarded: nothing here asks the class scope, the free function or
	// the managed area for a script precache.
	const std::string code = CodeOf( kEffects );

	ASSERT_TRUE( Has( code, "void UTIL_BloodSpray" ) )
		<< "the file's anchor moved; this test is reading the wrong file";

	EXPECT_EQ( 0, CountOf( code, "CBaseEntity::PrecacheScriptSound" ) )
		<< "the class scope is asked again -- that scope is what Phase 13 deletes";
	EXPECT_EQ( 0, CountOf( code, "GameSound_PrecacheScript(" ) )
		<< "server.so defines no GameSound_PrecacheScript any more";
	EXPECT_EQ( 0, CountOf( code, "ServerPrecacheScriptSound(" ) )
		<< "a native script precache is back beside the world's managed one";
	EXPECT_EQ( 0, CountOf( code, "void EffectsPrecache" ) );
}

//=============================================================================//
// 3. The scanner checking itself. Without these every assertion above passes on
//    an empty string, which is precisely how a check that cannot fail is born.
//=============================================================================//

TEST( Shard2ScannerSelfCheck, EveryFileWasReallyOpenedAndIsNotTrivial )
{
	ASSERT_TRUE( SourceRoot() != NULL ) << "no src tree found from the cwd";

	const char *kAll[] = { kEffects };
	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		const std::string raw = ReadFile( kAll[ i ] );
		EXPECT_LT( 10000u, raw.size() )
			<< kAll[ i ] << " read as " << raw.size()
			<< " bytes -- every claim about it would pass vacuously";
	}
}

TEST( Shard2ScannerSelfCheck, StrippingIsWhatStopsTheProseFalsePositive )
{
	// The exact shape this file's subjects are written in: a comment naming the
	// member the fix stopped calling, directly above the fix.
	const std::string strFixture =
		"// SOURCESHARP: this was TraceEntity( *pTrace )->entindex(), with no\n"
		"// null check.\n"
		"int i = pTrace->m_pEnt->GetRefEHandle().GetEntryIndex();\n";

	EXPECT_TRUE( Has( strFixture, "TraceEntity(" ) )
		<< "the raw text does contain the token -- that is the trap";
	EXPECT_FALSE( Has( StripComments( strFixture ), "TraceEntity(" ) )
		<< "stripping is what makes 'the fix must not name the old call' honest";
	EXPECT_TRUE( Has( StripComments( strFixture ), "GetEntryIndex()" ) )
		<< "...and it must not eat the code";

	// Block comments too: the file headers here are all `/* ... */`-free today,
	// but nothing stops the next one.
	EXPECT_FALSE( Has( StripComments( "/* EntityFromEntityHandle */ int x;" ),
	                   "EntityFromEntityHandle" ) );
	EXPECT_TRUE( Has( StripComments( "/* c */ int x;" ), "int x;" ) );
}

TEST( Shard2ScannerSelfCheck, CountOfCountsRatherThanFindingOnce )
{
	EXPECT_EQ( 0, CountOf( "abc", "z" ) );
	EXPECT_EQ( 1, CountOf( "abc", "b" ) );
	EXPECT_EQ( 3, CountOf( "b b b", "b" ) );
	EXPECT_EQ( 2, CountOf( "aaaa", "aa" ) ) << "non-overlapping, deliberately";
}
