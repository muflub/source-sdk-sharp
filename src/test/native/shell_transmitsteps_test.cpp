//=============================================================================//
// CHECKTRANSMIT'S CROSSING, SOURCE-READ -- plan_server.md Phase 8b,
// docs/plan-audit.md item C4.
//
// WHY THIS FILE IS ALL SOURCE READS AND NOT ONE RUNNING BODY.
//
// The two steps in shell_transmitsteps.cpp need `engine`, `edict_t` and
// `CStandalonePlayer` -- the entire server. (They needed `GetContainingEntity`
// and `CBasePlayer::m_Local` too, until the close proved that reach was a
// constant NULL and deleted it; see
// TheSdkStepsUnguardedCastIsGoneWithTheNullItCastAndTheStandaloneStepAnswersInstead.) Nothing in that file can ever be linked into a gtest, exactly
// as shell_gamedll.cpp cannot, and shell_gamedll_registration_test.cpp reads
// that file for the same reason.
//
// The BEHAVIOUR of the crossing is checked in three other places, and this file
// is the fourth kind of check rather than a substitute for any of them:
//
//   ServerEntitiesTests (xUnit, 8 facts) -- the ORDER the two hierarchies are
//   asked in, over a strict engine double. Mutation-verified by
//   sourcesharp/test/server_entities_mutate.sh, 5 arms, 5 red.
//
//   tools/gate5 -- the recorded transmit set for one client on one frame,
//   against a baseline taken BEFORE this crossing landed, plus the skybox area
//   the walk was given. Byte-identical across the change.
//
//   ManagedRuntimeServerSources.TheNewSlotsAreTheTailOfTheManagedTable -- that
//   the managed slot was APPENDED and not inserted.
//
// What is left, and what is here, is the ARRANGEMENT: that the disjunction
// actually left C++, that the loop actually stayed, that each step reads ONE
// hierarchy, and that the out parameter is written only on a hit. Every one of
// those is a property of text that compiles and runs identically whether or not
// it holds -- which is the definition of the property a source read is for.
//=============================================================================//

#include <fstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

namespace
{

std::string Read( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	EXPECT_TRUE( in.good() ) << "cannot read " << strPath;

	std::string strOut, strLine;
	while ( std::getline( in, strLine ) )
	{
		strOut += strLine;
		strOut += "\n";
	}
	return strOut;
}

//-----------------------------------------------------------------------------
// Comment-stripped, and it is load-bearing here rather than tidy: every one of
// these files EXPLAINS the crossing in prose that names the same symbols the
// assertions look for. shell_gameents.cpp's own header now contains the words
// "CStandalonePlayer::FromEdict" inside a paragraph saying that reach LEFT --
// so a raw search for it would find the sentence and report the code.
//-----------------------------------------------------------------------------
std::string Stripped( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	EXPECT_TRUE( in.good() ) << "cannot read " << strPath;

	std::string strOut, strLine;
	while ( std::getline( in, strLine ) )
	{
		const size_t at = strLine.find( "//" );
		if ( at != std::string::npos )
			strLine = strLine.substr( 0, at );
		strOut += strLine;
		strOut += "\n";
	}
	return strOut;
}

int CountOccurrences( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	size_t at = 0;
	while ( ( at = strHay.find( strNeedle, at ) ) != std::string::npos )
	{
		n++;
		at += strNeedle.size();
	}
	return n;
}

const char *kSteps  = "sourcesharp/game/server/shell_transmitsteps.cpp";
const char *kEnts   = "sourcesharp/game/server/shell_gameents.cpp";
const char *kAbi    = "sourcesharp/host/sourcesharp.h";
const char *kSystem = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
// The server's slots since plan_engineapi_vtables C6c: shim-area methods.
const char *kAreas     = "sourcesharp/game/shared/ishim_areas.h";
const char *kAreasImpl = "sourcesharp/game/shared/shell_shim_areas.inc";

} // namespace

//=============================================================================
// THE DISJUNCTION LEFT.
//=============================================================================

TEST( TransmitCrossing, TheInterfaceFileNoLongerKnowsTheSecondHierarchyExists )
{
	// THE WHOLE SHAPE OF C4, IN ONE ASSERTION. Phase 9a fixed "a standalone
	// recipient sees nothing" with an `#ifdef SOURCESHARP` block inside
	// CheckTransmit that reached for CStandalonePlayer::FromEdict. That block
	// WAS the decision -- which hierarchy answers, and in which order -- and
	// plan_gamedll.md Part 4's rule is that a step is never one.
	//
	// If it comes back, it comes back as a plausible local fix to a bug in this
	// file, and the C# body would go on being called and go on being right and
	// go on being ignored.
	const std::string ents = Stripped( kEnts );

	EXPECT_EQ( 0, CountOccurrences( ents, "CStandalonePlayer" ) )
		<< "shell_gameents.cpp names CStandalonePlayer again -- the disjunction "
		   "C4 moved into C# has grown a second copy in C++";
	EXPECT_EQ( 0, CountOccurrences( ents, "m_skybox3d" ) )
		<< "shell_gameents.cpp reads a skybox area directly again";
	EXPECT_EQ( 0, CountOccurrences( ents, "shell_standalone_player.h" ) );
}

TEST( TransmitCrossing, CheckTransmitAsksCSharpForItsRecipient )
{
	const std::string ents = Stripped( kEnts );

	// EXACTLY ONE ASK. Two would be two crossings a frame per client for one
	// answer, and worse, two chances for the second to disagree with the first
	// inside one walk.
	EXPECT_EQ( 1, CountOccurrences( ents, "SourceSharp_SkyboxAreaForClient(" ) );
}

TEST( TransmitCrossing, TheEarlyReturnIsTheManagedAnswerAndNotAPointerTest )
{
	// Stock's guard was `if ( !pRecipientEntity && !bStandaloneRecipient )
	// return;`. Both halves are now one bool from C#. A guard left testing the
	// SDK pointer would decline every standalone recipient -- which is the bug
	// Phase 9a fixed, reintroduced by a tidy-up.
	const std::string ents = Stripped( kEnts );

	EXPECT_EQ( 1, CountOccurrences( ents, "if ( !bHaveRecipient )" ) );
	EXPECT_EQ( 0, CountOccurrences( ents, "if ( !pRecipientEntity && !bStandaloneRecipient )" ) );
	EXPECT_EQ( 0, CountOccurrences( ents, "bStandaloneRecipient" ) );
	EXPECT_EQ( 0, CountOccurrences( ents, "nStandaloneSkyBoxArea" ) );
}

//=============================================================================
// THE LOOP STAYED.
//=============================================================================

TEST( TransmitCrossing, TheWalkIsStillNative )
{
	// plan_server.md Phase 8b, and the audit's C4 row repeating it: "keep
	// CheckTransmit's loop native per the plan's own rule -- the work is one
	// step plus ServerSkyboxAreaForClient, NOT A TRANSLITERATION".
	//
	// The walk runs once per edict per client per frame -- 473 iterations a
	// frame per client on dm_lockdown -- and decides nothing that is not already
	// an FL_EDICT_* flag. Crossing it would be thousands of crossings a frame to
	// move a loop with no decision in it.
	const std::string ents = Stripped( kEnts );

	EXPECT_EQ( 1, CountOccurrences( ents, "for ( int i=0; i < nEdicts; i++ )" ) )
		<< "CheckTransmit's edict walk is not in shell_gameents.cpp any more";
	EXPECT_LT( 0, CountOccurrences( ents, "FL_EDICT_PVSCHECK" ) );
	EXPECT_LT( 0, CountOccurrences( ents, "m_pTransmitEdict" ) );
}

//=============================================================================
// EACH STEP READS ONE HIERARCHY, AND DECIDES NOTHING.
//=============================================================================

TEST( TransmitCrossing, TheStandaloneStepIsTheOnlyStepAndReadsOneHierarchy )
{
	const std::string steps = Stripped( kSteps );

	// ONE STEP since plan_server_native_v2 T2: the SDK step answered 0 for every
	// edict (server.so builds no CBasePlayer), so it was deleted with its slot
	// and EngineBinding answers "no SDK area" itself.
	EXPECT_EQ( std::string::npos, steps.find( "SourceSharp_EntitiesSdkPlayerSkyboxArea(" ) )
		<< "the SDK skybox step is back; there is no SDK player on the server";
	const size_t other = steps.find( "int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea(" );
	ASSERT_NE( std::string::npos, other );

	const std::string strOtherBody = steps.substr( other );
	EXPECT_EQ( 0, CountOccurrences( strOtherBody, "GetContainingEntity" ) );
	EXPECT_EQ( 0, CountOccurrences( strOtherBody, "CBasePlayer" ) );
}

TEST( TransmitCrossing, TheStepDoesNotWriteItsOutParameterOnAMiss )
{
	// AREA 0 IS A REAL VISLEAF AREA. A step that zeroed *pOutArea on the way out
	// would make "the world's first area" and "no such recipient" the same two
	// bytes -- and CheckTransmit returns early for one of them and walks every
	// edict in the snapshot for the other.
	//
	// Checked as "every `return 0` is reached without having written", which is
	// what the shape of these bodies makes readable: every assignment to
	// *pOutArea in the file is immediately before a `return 1`.
	//
	// READ THE ASSERTIONS, NOT THE COUNT. There were TWO assignments when both
	// steps could answer yes. The SDK step's was behind GetContainingEntity,
	// which answered a constant NULL in this build and has been deleted with its
	// ten call sites; that step now returns 0 for every edict and writes
	// nothing. So the live count is ONE, and the SDK step's zero writes are
	// asserted below rather than dropped -- a step that started writing again
	// would have to have grown a recipient to write for.
	const std::string steps = Stripped( kSteps );

	EXPECT_EQ( 1, CountOccurrences( steps, "*pOutArea = " ) )
		<< "one assignment, in the one step that can answer yes, and no others";

	const size_t other = steps.find( "int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea(" );
	ASSERT_NE( std::string::npos, other );

	// The one assignment is the standalone step's (the SDK step that wrote
	// nothing was deleted, plan_server_native_v2 T2).
	EXPECT_EQ( 1, CountOccurrences( steps.substr( other ), "*pOutArea = " ) );

	// And each is immediately followed by the success return.
	size_t at = 0;
	int nChecked = 0;
	while ( ( at = steps.find( "*pOutArea = ", at ) ) != std::string::npos )
	{
		const size_t line_end = steps.find( '\n', at );
		ASSERT_NE( std::string::npos, line_end );
		const size_t next_end = steps.find( '\n', line_end + 1 );
		ASSERT_NE( std::string::npos, next_end );

		const std::string strNext = steps.substr( line_end + 1, next_end - line_end - 1 );
		EXPECT_NE( std::string::npos, strNext.find( "return 1;" ) )
			<< "an assignment to *pOutArea is not immediately followed by `return 1;`"
			   " -- the write and the yes-answer must not be separable";
		nChecked++;
		at = next_end;
	}
	EXPECT_EQ( 1, nChecked );
}

TEST( TransmitCrossing, TheSdkStepsUnguardedCastIsGoneWithTheStepAndTheStandaloneStepAnswersInstead )
{
	// READ THE ASSERTIONS, NOT THE NAME -- and read them as a pair.
	//
	// THIS FACT USED TO SAY THE OPPOSITE. Stock reads m_Local.m_skybox3d.area
	// off a raw `static_cast<CBasePlayer *>( pEntity )` of whatever
	// GetContainingEntity returned, and this step reproduced that cast
	// deliberately, unguarded, because adding an IsPlayer() guard would be a
	// behaviour change on a path nobody had measured.
	//
	// It was reproducing a cast that could never run. GetContainingEntity is
	// SourceSharp_SDKEntityFromUnknown< IServerEntity >, which bottoms out in
	// IServerUnknown::GetBaseEntity(), and all three implementations of that in
	// server.so are `mov $0x0,%eax; ret` -- game/server/util.h:724 had already
	// written that down in prose. So `pEntity` was a constant NULL, the cast was
	// unreachable, and the whole function is deleted with its ten call sites.
	//
	// ABSENCE ALONE WOULD ALSO PASS OVER AN EMPTY FILE, so the live successor is
	// asserted beside it: the SDK step is now a constant 0 for every edict, and
	// the step that actually answers this question is the standalone one, which
	// does NOT cast -- it asks CStandalonePlayer::FromEdict and null-checks the
	// answer before reading the same field.
	const std::string steps = Stripped( kSteps );

	// GONE: the cast, the NULL it cast, and every spelling of the reach.
	EXPECT_EQ( 0, CountOccurrences( steps, "static_cast<CBasePlayer *>( pEntity )" ) );
	EXPECT_EQ( 0, CountOccurrences( steps, "GetContainingEntity" ) );
	EXPECT_EQ( 0, CountOccurrences( steps, "m_Local.m_skybox3d.area" ) );

	// GONE TOO: the SDK step that answered 0 for every edict
	// (plan_server_native_v2 T2).
	EXPECT_EQ( 0, CountOccurrences( steps, "SourceSharp_EntitiesSdkPlayerSkyboxArea(" ) );
	const size_t other = steps.find( "int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea(" );
	ASSERT_NE( std::string::npos, other );

	// LIVE: the standalone step is the one that can say yes, and it reads the
	// skybox area through the standalone hierarchy's own accessor.
	const std::string strOtherBody = steps.substr( other );
	EXPECT_EQ( 1, CountOccurrences( strOtherBody, "CStandalonePlayer::FromEdict( pEdict )" ) );
	EXPECT_EQ( 1, CountOccurrences( strOtherBody, "pStandalone->MvLocal().m_skybox3d.area" ) );
	EXPECT_EQ( 1, CountOccurrences( strOtherBody, "if ( pStandalone == NULL )" ) )
		<< "the one step that can answer lost its null check";

	// AND THE ORIGINAL FACT STILL STANDS where it can: no IsPlayer() guard was
	// smuggled in anywhere in this file.
	EXPECT_EQ( 0, CountOccurrences( steps, "IsPlayer()" ) )
		<< "an IsPlayer() guard was added to a cast stock leaves unguarded -- "
		   "read the file header before deciding that is an improvement";
}

//=============================================================================
// THE WIRING.
//=============================================================================

TEST( TransmitCrossing, TheEngineStepIsWiredExactlyOnce )
{
	const std::string sys = Stripped( kSystem );

	// The server's area methods forward to the two steps, once each (C6c).
	const std::string impl = Stripped( kAreasImpl );
	EXPECT_EQ( 0, CountOccurrences( impl, "SourceSharp_EntitiesSdkPlayerSkyboxArea" ) )
		<< "the SDK skybox step is wired again; it was deleted with its slot";
	EXPECT_EQ( 1, CountOccurrences( impl,
		"{ return ::SourceSharp_EntitiesStandalonePlayerSkyboxArea( iEdict, pOutArea ); }" ) );

	// And the managed ask has exactly one body. Two would be two places to keep
	// the null guard in, and the null guard is what stands between a missing
	// runtime and a walk over an area of 0.
	EXPECT_EQ( 1, CountOccurrences( sys, "int SourceSharp_SkyboxAreaForClient( int iClientEdict, int *pOutArea )" ) );
}

TEST( TransmitCrossing, TheTwoEngineSlotsLeftTheEngineTableForAServerArea )
{
	// They were APPENDED to SourceSharpEngineApi, and this fact held them at its
	// tail. plan_engineapi_vtables C6c moved every server-only slot out of the
	// struct: these are IShimEntitiesServer methods now, whose order
	// tools/vtable-layout checks against the managed mirror. What is left to
	// say here is that they are there, in order, and that no struct holds them:
	// plan_client_vtables CC-last deleted the engine struct itself.
	const std::string abi = Read( kAbi );
	const std::string areas = Stripped( kAreas );

	ASSERT_NE( std::string::npos, abi.find( "(*SourceSharpLogFn)" ) )
		<< "the control: the header read declares the ABI's one surviving function "
		   "pointer, found by the same \"(*Name)\" needle as the absences below";
	EXPECT_EQ( std::string::npos, abi.find( "} SourceSharpManagedApi;" ) );
	EXPECT_EQ( std::string::npos, abi.find( "} SourceSharpEngineApi;" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*EntitiesSdkPlayerSkyboxArea)" ) );
	EXPECT_EQ( std::string::npos, abi.find( "(*EntitiesStandalonePlayerSkyboxArea)" ) );

	// The SDK ask left the area too (plan_server_native_v2 T2).
	EXPECT_EQ( std::string::npos, areas.find( " EntitiesSdkPlayerSkyboxArea(" ) );
	EXPECT_NE( std::string::npos, areas.find( " EntitiesStandalonePlayerSkyboxArea(" ) );
}
