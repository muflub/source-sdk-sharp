//========= SourceSharp — shard T5's .cpp edits, read as source ==============//
//
// A SOURCE SCANNER, like cpp_alias_lane1_test.cpp and cpp_alias_shard2_test.cpp,
// and for the same reason those two give: the claim is about the TEXT of files
// that need a whole engine to run, so the text is what is read. The compile-side
// evidence is `src/survey_run` with the alias armed, which is a measurement run
// by hand rather than a standing test — these are the half that rots between
// surveys.
//
// WHAT SHARD T5 DID. 194 armed sites across fourteen .cpp files
// (docs/typedef-dryrun/route/s5_shards.json, shard "T5"), in three shapes:
//
//   UNCONDITIONAL RETYPES, six of them, where the pointer was CARRIED rather
//   than questioned and the narrow type bought nothing. These are not Phase 13
//   accommodations — each is the right code today, and two of them fix a live
//   defect. They have no guard, so nothing in the build notices one being
//   written back.
//
//   SUBSTITUTING ARMS — #ifndef / #else / #endif, both arms carrying code. The
//   armed arm computes the same answer by a route that survives the flip.
//
//   WHOLE-BODY ARMS — #ifndef / #endif, armed side deliberately empty, used
//   where the guarded code is provably vacuous once the flip lands.
//
// THE HAZARD THIS SHARD ADDS, and it is lane 1's own rule turned round.
// cpp_alias_lane1_test.cpp requires every whole-body arm to sit in a VOID
// function, because a guard that swallows the only `return` leaves a non-void
// function falling off the end: well-formed enough to compile with a warning,
// undefined at run time, and INVISIBLE to the survey, which is the worst
// available outcome. Shard T5 could not take that rule — several of its sites
// are in functions that return a value — so it took the other side of the same
// bargain: where a guard swallows a `return`, the ARMED text of the function
// must still contain one.
//
// WHAT IS LEFT HERE. The guard, retype and return checks read files the build
// no longer compiles and were culled with them. What remains is the
// flashlight-target vacuity that clientshadowmgr.cpp's arms rested on
// (section 5), which reads the client's build list and the shadow manager.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The self-tests
// assert that comment stripping really strips (on synthetic input, because
// every arm here explains itself in prose that NAMES the member it stopped
// calling).
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <sys/stat.h>

#include <cstring>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as cpp_alias_lane1_test.cpp.
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

std::vector< std::string > ReadLines( const std::string &strRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

std::string ReadFile( const std::string &strRelative )
{
	const std::vector< std::string > lines = ReadLines( strRelative );
	std::string out;
	for ( size_t i = 0; i < lines.size(); i++ )
		out += lines[ i ] + "\n";
	return out;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and this is not tidiness — see
// cpp_alias_lane1_test.cpp's note. Every arm in this shard is preceded by prose
// naming the very member it stopped calling, so matching raw text would read the
// name straight out of the sentence saying it is gone.
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

} // namespace

//-----------------------------------------------------------------------------
// 5. FLASHLIGHT TARGET VACUITY, which is what clientshadowmgr.cpp's two armed
// arms rest on.
//
// CClientShadowMgr::m_hTargetEntity is written in exactly one place,
// SetFlashlightTarget, whose only caller in the tree is
// c_env_projectedtexture.cpp — and this project does not compile that file. So
// bLightSpecificEntity has always been false here and IsFlashlightTarget has
// never been called, all three of its call sites short-circuiting on
// `(!shadow.m_hTargetEntity) ||`.
//
// THE QUESTION IS ASKED THE OTHER WAY ROUND NOW. This used to look for the
// `-$File` that removed the file. client_csharp.vpc was flattened
// (plan_vpc_client.md, phase A10 of plan_gamedll_after.md): it has no -$File
// at all, it lists the 288 files it compiles, and "out of the project" is
// ABSENCE from that list. That is the stronger form -- a removal list can only
// name what somebody wrote down, and this one is exact by construction -- but
// an absence assertion is vacuous on a file that could not be read, so the
// positive control below runs first.
//
// SINCE PHASE 6b the list is client_sourcesharp's, read through cmake_graph.h
// (compile_commands.json cross-checked against src/client/native/CMakeLists.txt)
// -- VPC is gone. The positive control and the absence are unchanged in kind.
//
// Same shape of evidence, and deliberately the same shape of fixture, as
// PredictionVacuity in engine_core_retype_test.cpp: the arms are only exact
// while the exclusion holds, so the exclusion is what is pinned.
//-----------------------------------------------------------------------------
TEST( CppAliasT5, FlashlightTargetVacuity_ProjectedTextureIsNotInThisProject )
{
	// POSITIVE CONTROL. Without it, a reader that saw the wrong list -- or none
	// -- would make the assertion below pass for the wrong reason.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/clientshadowmgr.cpp" ) )
		<< "client_sourcesharp does not compile clientshadowmgr.cpp, the file this "
		   "whole test is about. Either it left the build or the reader is not "
		   "reading the client's list, and the absence assertion below would pass "
		   "vacuously.";

	// By BASENAME in any directory, the strength the old substring search had.
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kClient );
	std::string strFound;
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == "c_env_projectedtexture.cpp" )
			strFound = *it;
	}
	EXPECT_EQ( strFound, "" )
		<< strFound << " is back in the client build (client_sourcesharp), so "
		   "SetFlashlightTarget has a caller again and clientshadowmgr.cpp's two "
		   "armed arms are no longer vacuous";
}

TEST( CppAliasT5, FlashlightTargetVacuity_NothingElseCallsSetFlashlightTarget )
{
	// The other half, and without it the fixture above pins a fact about a file
	// that might not be the only caller any more. Read out of the shadow manager
	// itself: if a second caller appeared, it would be here or in a file this
	// scanner cannot see — so this is a floor, and the build-list entry is the wall.
	const std::string strCpp = StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	ASSERT_FALSE( strCpp.empty() );

	EXPECT_TRUE( Contains( strCpp, "void CClientShadowMgr::SetFlashlightTarget(" ) )
		<< "SetFlashlightTarget has moved; the vacuity argument names it by "
		   "being the only writer of m_hTargetEntity";

	// The one write, still the one write.
	size_t n = 0, at = 0;
	while ( ( at = strCpp.find( "m_hTargetEntity =", at ) ) != std::string::npos )
	{
		n++; at += 1;
	}
	EXPECT_EQ( n, (size_t)1 )
		<< "m_hTargetEntity is assigned in " << n << " places, not one — the "
		   "vacuity argument for BuildFlashlight and IsFlashlightTarget assumes "
		   "SetFlashlightTarget is the only writer";
}

//=============================================================================//
// SCANNER SELF-TESTS.
//
// header_alias_test.cpp's scanner has been wrong twice — once matching raw text
// through a comment, once blind to #ifdef polarity — and both times the rules
// above it stayed green. These exist so this scanner's own faults are caught by
// something other than luck.
//=============================================================================//

TEST( CppAliasT5Scanner, StripCommentsRemovesBothCommentKinds )
{
	EXPECT_EQ( StripComments( "a // GetMoveType\nb\n" ), "a \nb\n" );
	EXPECT_EQ( StripComments( "a /* GetMoveType */ b" ), "a  b" );
	EXPECT_FALSE( Contains( StripComments( "/*\n DecalTrace(\n*/\nx();\n" ), "DecalTrace(" ) );
	// ...and it must not eat code.
	EXPECT_TRUE( Contains( StripComments( "/* gone */ DecalTrace( a );\n" ), "DecalTrace(" ) );
}

TEST( CppAliasT5Scanner, StrippingIsWhatStopsTheProseFalsePositive )
{
	// The failure reproduced on SYNTHETIC input rather than on the tree: an
	// armed arm that is CORRECT — it does not call the member — but whose
	// comment explains which member it stopped calling. Judged on raw text, the
	// rule "the armed arm must not name the deleted member" fires and a correct
	// arm reads as broken. Every arm in this shard is written in exactly that
	// style, so this is load-bearing here rather than defensive.
	const std::string strArmedArm =
		"\t// m_debugOverlays never crosses, so this does nothing.\n"
		"\treturn;\n";

	EXPECT_TRUE( Contains( strArmedArm, "m_debugOverlays" ) )
		<< "the synthetic arm no longer reproduces the false positive";
	EXPECT_FALSE( Contains( StripComments( strArmedArm ), "m_debugOverlays" ) )
		<< "stripping failed to remove the prose mention — the rule above would "
		   "report this correct arm as broken";

	// The converse, which is what stops the stripping from being TOO greedy: a
	// genuinely broken armed arm must survive stripping and be caught. Without
	// this, StripComments could return the empty string and every rule above
	// would pass vacuously.
	const std::string strBrokenArm =
		"\t// m_debugOverlays never crosses.\n"
		"\tpEntity->m_debugOverlays |= bit;\n";

	EXPECT_TRUE( Contains( StripComments( strBrokenArm ), "m_debugOverlays" ) )
		<< "stripping ate the code as well as the comment";
}
