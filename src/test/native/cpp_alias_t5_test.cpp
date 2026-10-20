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
// no longer compiles and were culled with them. The flashlight-target vacuity
// that clientshadowmgr.cpp's arms rested on (section 5) went when that file
// left client.so (C7 cluster 12 S2, 2026-10-19). Only the scanner's
// self-tests remain.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The self-tests
// assert that comment stripping really strips (on synthetic input, because
// every arm here explains itself in prose that NAMES the member it stopped
// calling).
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string>

namespace
{

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
// 5. FLASHLIGHT TARGET VACUITY, RETIRED (2026-10-19, C7 cluster 12 S2:
// clientshadowmgr.cpp, the client's last fork TU, left client.so).
//
// The two facts here pinned what clientshadowmgr.cpp's two whole-body arms
// rested on: SetFlashlightTarget was m_hTargetEntity's only writer, and its only
// caller, c_env_projectedtexture.cpp, was out of the client build, so the arms
// guarding IsFlashlightTarget were vacuous. The file left with the managed
// shadow manager (src/client/managed/Render/Shadows/), which has no arm to be
// vacuous: SetFlashlightTarget and IsFlashlightTarget are ported whole
// (ShadowManager.Projection.cs, ShadowManager.Receivers.cs) and checked against
// stock by the shadow manager oracle (ShadowManagerOracleTests.cs). There is no
// subject left for either fact to read.
//-----------------------------------------------------------------------------

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
