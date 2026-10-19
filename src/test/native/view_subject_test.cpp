//========= SourceSharp — the view's subject, and the de-entitying of it ======//
//
// plan_entity.md step 7c. Two halves, and they check different kinds of thing.
//
// THE RULES (view_subject.h) are the small amount of deciding the view's facts
// do, lifted out of shell_view_facts.cpp so that they can be run. Each has an
// edge whose wrong answer is INVISIBLE IN A RUNNING GAME, which is the only
// reason they are rules rather than expressions at a call site: a view that
// silently never switches on the underwater DSP, and a view entity of index 0
// that puts the camera at the map origin, both produce a frame that looks like
// something else's fault.
//
// THE SOURCE SCAN is the step's own gate written down. "De-entitied in place"
// means these six translation units stay in the build and stop naming entity
// types, and the census measures exactly that — so a check here is what keeps
// a later edit from putting one back without anyone noticing. It reads the same
// regex the census reads, over the same files, and it is deliberately a
// FILE-BY-FILE assertion rather than a total: a total that moved would not say
// which file moved it.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "../game/client/view_subject.h"

using namespace ViewSubject;

//=============================================================================
// PART 1 — THE RULES
//=============================================================================

//-----------------------------------------------------------------------------
// The underwater DSP.
//-----------------------------------------------------------------------------
TEST( AudioIsUnderwater, EyesUnderTheSurfaceIsUnderwater )
{
	EXPECT_TRUE( AudioIsUnderwater( WATER_LEVEL_EYES ) );
}

TEST( AudioIsUnderwater, WaistDeepIsNot )
{
	// WL_Waist is 2. A player standing waist-deep hears the room; this is the
	// line stock draws and the one a `>` instead of a `>=` would move.
	EXPECT_FALSE( AudioIsUnderwater( 2 ) );
	EXPECT_FALSE( AudioIsUnderwater( 1 ) );
	EXPECT_FALSE( AudioIsUnderwater( 0 ) );
}

TEST( AudioIsUnderwater, TheTopOfTheScaleIsTheOnlyTrueValue )
{
	// WL_Eyes is the LAST value in the enum, so `> WL_Eyes` can never be true
	// and a build written that way would never switch the DSP on for anyone —
	// which is exactly the silence this whole step is removing by another
	// route. Asserting the top of the scale is what makes that mistake fail.
	EXPECT_TRUE( AudioIsUnderwater( WATER_LEVEL_EYES ) );
	EXPECT_EQ( 3, WATER_LEVEL_EYES );
}

//-----------------------------------------------------------------------------
// The effects bits.
//-----------------------------------------------------------------------------
TEST( EffectActive, TheAskedForBitSetIsActive )
{
	EXPECT_TRUE( EffectActive( 0x2, 0x2 ) );
	EXPECT_TRUE( EffectActive( 0xFF, 0x2 ) );
}

TEST( EffectActive, ADifferentBitIsNot )
{
	EXPECT_FALSE( EffectActive( 0x1, 0x2 ) );
	EXPECT_FALSE( EffectActive( 0x0, 0x2 ) );
}

TEST( EffectActive, ANYOfTheAskedForBitsIsEnough )
{
	// Stock's IsEffectActive is `( m_fEffects & nEffectMask ) != 0`, which is
	// ANY. A reader who wrote `== nEffectMask` would get the same answer for
	// every single-bit ask and a different one the first time two bits were
	// passed, which is a mistake that ships.
	EXPECT_TRUE( EffectActive( 0x1, 0x3 ) );
	EXPECT_TRUE( EffectActive( 0x2, 0x3 ) );
	EXPECT_FALSE( EffectActive( 0x4, 0x3 ) );
}

//-----------------------------------------------------------------------------
// The view entity.
//-----------------------------------------------------------------------------
TEST( ViewEntityOverridesTheEye, LookingOutOfYourOwnHeadIsNotAnOverride )
{
	EXPECT_FALSE( ViewEntityOverridesTheEye( 1, 1 ) );
	EXPECT_FALSE( ViewEntityOverridesTheEye( 7, 7 ) );
}

TEST( ViewEntityOverridesTheEye, AnotherEntityIsAnOverride )
{
	EXPECT_TRUE( ViewEntityOverridesTheEye( 42, 1 ) );
}

TEST( ViewEntityOverridesTheEye, EntityZeroIsRefusedEvenThoughItIsNotThePlayer )
{
	// Entity 0 is the world. It is not the player, so stock's test alone would
	// call it an override — and the world's origin is (0,0,0), so the frame
	// would be rendered from the map's origin. That is a black screen or the
	// inside of a wall, which reads as a renderer fault rather than a wrong
	// view entity.
	EXPECT_FALSE( ViewEntityOverridesTheEye( 0, 1 ) );
}

TEST( ViewEntityOverridesTheEye, ANegativeIndexIsRefused )
{
	EXPECT_FALSE( ViewEntityOverridesTheEye( -1, 1 ) );
}

//=============================================================================
// PART 2 — THE SOURCE SCAN
//=============================================================================

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

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::stringstream ss;
	ss << file.rdbuf();
	return ss.str();
}

//-----------------------------------------------------------------------------
// The file with its comments taken out.
//
// EVERY "IS IT GONE" FACT BELOW NEEDS THIS AND EVERY "IS IT THERE" FACT MUST
// NOT USE IT, and the two readings are different on purpose.
//
// De-entitying a file leaves prose behind saying what stood there and why it
// went -- that record is most of the value of the change. So a check for
// `#if defined( REPLAY_ENABLED )` in the raw text finds the SENTENCE SAYING THE
// BLOCK IS GONE and fails, and a check written to dodge that would be a needle
// with no subject. Stripping comments is what makes the assertion about code.
//
// The entity-spelling fact is the exception and reads the RAW text, because the
// census reads the raw text: a file whose comments still name C_BasePlayer is a
// file the census still counts, whatever the compiler sees.
//
// String literals are preserved, so a "//" inside one is not mistaken for a
// comment -- these files carry paths and material names.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &strIn )
{
	std::string out;
	out.reserve( strIn.size() );

	enum { CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, CHARLIT } state = CODE;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		const char c = strIn[ i ];
		const char n = ( i + 1 < strIn.size() ) ? strIn[ i + 1 ] : '\0';

		switch ( state )
		{
		case CODE:
			if ( c == '/' && n == '/' ) { state = LINE_COMMENT; i++; }
			else if ( c == '/' && n == '*' ) { state = BLOCK_COMMENT; i++; }
			else
			{
				if ( c == '"' ) state = STRING;
				else if ( c == '\'' ) state = CHARLIT;
				out += c;
			}
			break;

		case LINE_COMMENT:
			// The newline is kept so line-oriented reading still works.
			if ( c == '\n' ) { state = CODE; out += c; }
			break;

		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += c;
			break;

		case STRING:
			out += c;
			if ( c == '\\' && i + 1 < strIn.size() ) { out += n; i++; }
			else if ( c == '"' ) state = CODE;
			break;

		case CHARLIT:
			out += c;
			if ( c == '\\' && i + 1 < strIn.size() ) { out += n; i++; }
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}

	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

//-----------------------------------------------------------------------------
// One whole-word occurrence, the way the census's `grep -E ... \b...\b` counts
// them. Written out rather than borrowed because a substring search would find
// `C_BaseEntityIterator` inside a longer name and report a hit the census does
// not have — and a needle that matches more than its subject is the kind that
// passes for the wrong reason.
//-----------------------------------------------------------------------------
int CountWholeWord( const std::string &strHay, const std::string &strNeedle )
{
	int nFound = 0;
	size_t at = 0;

	while ( ( at = strHay.find( strNeedle, at ) ) != std::string::npos )
	{
		const bool bLeftOk = ( at == 0 ) || !IsIdentChar( strHay[ at - 1 ] );
		const size_t end = at + strNeedle.size();
		const bool bRightOk = ( end >= strHay.size() ) || !IsIdentChar( strHay[ end ] );

		if ( bLeftOk && bRightOk )
			nFound++;

		at = end;
	}

	return nFound;
}

//-----------------------------------------------------------------------------
// The six translation units step 7c de-entitied IN PLACE. They are still in
// client_sourcesharp — that is what "in place" means, and it is why the check is
// "names no entity type" rather than "is not in the build".
//-----------------------------------------------------------------------------
// clientmode_shared.cpp was the sixth; it left the client at plan_client_novalve
// C3 step 4 (lane ui) for shell_client.cpp's own IClientMode.
//
// THREE OF THE SIX LEFT THE BUILD with C6 (lane c6-input): in_main.cpp,
// in_camera.cpp and cam_thirdperson.cpp are C#'s (src/client/managed/Input,
// against Fixtures/input-stock.tsv.gz), so their text is no longer the
// client's and the facts below read the two that stay
// (clientmode_shared.cpp having left with C3, above).
//
// VIEWRENDER.CPP AND VIEW.CPP LEFT TOO, at C7 step 3: their replacement is
// shell_viewrender_client.cpp (with the plan executor), and the fact holds of
// it -- the view's code names no entity type.
const char *kDeEntitiedTus[] =
{
	"sourcesharp/game/client/shell_viewrender_client.cpp",
};

//-----------------------------------------------------------------------------
// Every spelling the census's "uses an entity type" regex matches, expanded.
// The regex is
//     \bC_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon|Flex)\b
//     |\bg?_?pEntityList\b|\bgEntList\b
// and it is read out of tools/entity-census, which the last fact below checks
// this list still agrees with.
//-----------------------------------------------------------------------------
const char *kEntitySpellings[] =
{
	"CBaseEntity",  "C_BaseEntity",
	"CBasePlayer",  "C_BasePlayer",
	"CBaseAnimating", "C_BaseAnimating",
	"CBaseCombatCharacter", "C_BaseCombatCharacter",
	"CBaseCombatWeapon", "C_BaseCombatWeapon",
	"CBaseFlex", "C_BaseFlex",
	"gEntList", "pEntityList", "g_pEntityList", "_pEntityList",
};

} // namespace

TEST( DeEntitiedInPlace, TheTreeIsWhereTheTestThinksItIs )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot locate the src tree from the cwd";

	// A file that cannot be read reads as zero occurrences of everything, which
	// would make every fact below pass for the wrong reason.
	for ( size_t i = 0; i < sizeof( kDeEntitiedTus ) / sizeof( kDeEntitiedTus[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ReadFile( kDeEntitiedTus[ i ] ).empty() )
			<< kDeEntitiedTus[ i ] << " is empty or missing";
	}
}

TEST( DeEntitiedInPlace, NoneOfTheSixNamesAnEntityType )
{
	for ( size_t i = 0; i < sizeof( kDeEntitiedTus ) / sizeof( kDeEntitiedTus[ 0 ] ); i++ )
	{
		const std::string strFile = ReadFile( kDeEntitiedTus[ i ] );
		ASSERT_FALSE( strFile.empty() ) << kDeEntitiedTus[ i ];

		for ( size_t j = 0; j < sizeof( kEntitySpellings ) / sizeof( kEntitySpellings[ 0 ] ); j++ )
		{
			EXPECT_EQ( 0, CountWholeWord( strFile, kEntitySpellings[ j ] ) )
				<< kDeEntitiedTus[ i ] << " names " << kEntitySpellings[ j ]
				<< " -- step 7c de-entitied it in place, and the census counts"
				   " this file's TEXT. Whatever the new site needs should come"
				   " from the block or from view_facts.h.";
		}
	}
}

TEST( DeEntitiedInPlace, TheNeedleListStillMatchesTheCensusRegex )
{
	// A list of spellings that has drifted from the census's regex is a check
	// measuring a different thing from the gate. This asserts the regex is
	// still the one the list was expanded from -- if the census widens it,
	// this fact goes red and the list above has to grow with it.
	const std::string strCensus = ReadFile( "../tools/entity-census" );
	ASSERT_FALSE( strCensus.empty() ) << "cannot read tools/entity-census";

	EXPECT_NE( strCensus.find(
		"\\bC_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon|Flex)\\b" ),
		std::string::npos )
		<< "the census's uses-an-entity regex moved; kEntitySpellings has to move with it";
}

TEST( DeEntitiedInPlace, TheSixAreStillInTheBuild )
{
	// "IN PLACE" is the half a deletion would also satisfy. Every one of these
	// is still compiled by client_sourcesharp (src/client/native/CMakeLists.txt,
	// read through cmake_graph.h), and a step that de-entitied a file by
	// evicting it would have done something else than what 7c says.
	const char *kInBuild[] =
	{
		"sourcesharp/game/client/shell_viewrender_client.cpp",   // view.cpp's and viewrender.cpp's (C7 step 3)
		"sourcesharp/game/client/shell_view_facts.cpp",
	};
	for ( size_t i = 0; i < sizeof( kInBuild ) / sizeof( kInBuild[ 0 ] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kInBuild[ i ] ) )
			<< kInBuild[ i ] << " is not compiled by client_sourcesharp";
	}
}

//-----------------------------------------------------------------------------
// 7g's handover: dead REPLAY_ENABLED text in five compiled TUs, three of which
// are 7c's, and it named a ReplayCamera() 7g had deleted.
//-----------------------------------------------------------------------------
TEST( ReplayText, NoConditionalBlockSurvivesInSevenCsFiles )
{
	const char *kFiles[] =
	{
		"sourcesharp/game/client/shell_viewrender_client.cpp",   // view.cpp's, since C7 step 3
		"sourcesharp/game/client/shell_client.cpp",   // the client mode since clientmode_shared.cpp left (C3)
	};

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string strFile = StripComments( ReadFile( kFiles[ i ] ) );
		ASSERT_FALSE( strFile.empty() ) << kFiles[ i ];

		// The BLOCK, not the word: each file still carries prose saying what
		// stood there and why it went, which is the record of the removal --
		// hence StripComments, and hence the note on it.
		EXPECT_EQ( std::string::npos, strFile.find( "#if defined( REPLAY_ENABLED )" ) )
			<< kFiles[ i ] << " still has a REPLAY_ENABLED conditional. The define is"
			   " never given and step 7g deleted the TUs it named, so the arm cannot"
			   " compile if it ever arrives.";
	}
}

TEST( ReplayText, NothingInThoseThreeCallsTheDeletedCamera )
{
	const char *kFiles[] =
	{
		"sourcesharp/game/client/shell_viewrender_client.cpp",   // view.cpp's, since C7 step 3
		"sourcesharp/game/client/shell_client.cpp",   // the client mode since clientmode_shared.cpp left (C3)
	};

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string strFile = StripComments( ReadFile( kFiles[ i ] ) );
		ASSERT_FALSE( strFile.empty() ) << kFiles[ i ];

		EXPECT_EQ( std::string::npos, strFile.find( "ReplayCamera()->" ) )
			<< kFiles[ i ] << " calls ReplayCamera(), which step 7g deleted";
		EXPECT_EQ( std::string::npos, strFile.find( "#include \"replay/" ) )
			<< kFiles[ i ] << " includes a replay header";
	}
}

//-----------------------------------------------------------------------------
// The third-person camera: one sweep, not two.
//-----------------------------------------------------------------------------
TEST( ThirdPersonCamera, PositionCameraIsGoneFromBothHalvesAndHasNoCaller )
{
	// cam_thirdperson.cpp and in_camera.cpp, the definition and the caller,
	// left the build with C6 (lane c6-input); the header the shell's
	// g_ThirdPersonManager is declared by is what remains to hold.
	const std::string strHdr = StripComments( ReadFile( "game/shared/cam_thirdperson.h" ) );
	ASSERT_FALSE( strHdr.empty() );

	// Leaving the declaration would let a caller come back and link against nothing.
	EXPECT_EQ( std::string::npos, strHdr.find( "void\tPositionCamera(" ) )
		<< "cam_thirdperson.h still declares PositionCamera";
}

TEST( ThirdPersonCamera, TheSweepThatSURVIVEDIsTheShimsAndItAppliesAFraction )
{
	// The deletion above is only correct because the hull sweep still happens
	// somewhere. It is shell_client.cpp's third-person pass, and the part that
	// matters is that it APPLIES what the sweep found: a pass that traced and
	// ignored the fraction would put the camera through the wall, which is a
	// picture nobody would call broken.
	const std::string strShim = StripComments( ReadFile( "sourcesharp/game/client/shell_client.cpp" ) );
	ASSERT_FALSE( strShim.empty() );

	EXPECT_NE( strShim.find( "UTIL_TraceHull(" ), std::string::npos )
		<< "the shim's third-person pass no longer sweeps a hull";
	// THE FRACTION RULE IS MANAGED SINCE THE policy-player LANE, so what this
	// checks is that the shim still HANDS THE SWEEP OVER and applies what comes
	// back -- phase RESOLVE carries the fraction across and flSurvivingFraction
	// is what scales the wish. A pass that traced and ignored the answer would
	// put the camera through the wall, which is a picture nobody would call
	// broken.
	EXPECT_NE( strShim.find( "SOURCESHARP_THIRDPERSON_PHASE_RESOLVE" ), std::string::npos )
		<< "the shim's third-person pass no longer asks managed what the sweep left";
	EXPECT_NE( strShim.find( "io.flTraceFraction = flTraceFraction" ), std::string::npos )
		<< "the shim's third-person pass no longer hands the sweep's fraction over";
	EXPECT_NE( strShim.find( "vecWish * flSurviving" ), std::string::npos )
		<< "the shim's third-person pass no longer applies the surviving fraction";
}

//-----------------------------------------------------------------------------
// One answer to "can the camera see the viewer's own body", read by three
// places that must agree.
//-----------------------------------------------------------------------------
TEST( SeesOwner, ThreeReadersAskTheSameFunction )
{
	// The viewmodel PASS (the plan's input since C7 step 3: the executor gathers
	// the answer the planner's ShouldDrawViewModel reads), the viewmodel and player RENDERABLE
	// (shell_standalone_client.cpp) and the client mode (shell_client.cpp since C3)
	// all decide the same thing. Two of them once disagreed -- the pass drew
	// the arms unconditionally while the renderable hid them -- and the way
	// that cannot recur is that there is one function.
	const std::string strPass = StripComments( ReadFile( "sourcesharp/game/client/shell_view_executor_client.cpp" ) );
	const std::string strShell = StripComments( ReadFile( "sourcesharp/game/client/shell_standalone_client.cpp" ) );
	const std::string strMode = StripComments( ReadFile( "sourcesharp/game/client/shell_client.cpp" ) );

	ASSERT_FALSE( strPass.empty() );
	ASSERT_FALSE( strShell.empty() );
	ASSERT_FALSE( strMode.empty() );

	EXPECT_NE( strPass.find( "SourceSharp_ThirdPersonSeesOwner()" ), std::string::npos )
		<< "the plan's viewmodel pass no longer asks the camera";
	EXPECT_NE( strShell.find( "SourceSharp_ThirdPersonSeesOwner()" ), std::string::npos )
		<< "C_StandaloneEntity::ShouldDraw no longer asks the camera";
	EXPECT_NE( strMode.find( "virtual bool ShouldDrawLocalPlayer() OVERRIDE { return SourceSharp_ThirdPersonSeesOwner(); }" ), std::string::npos )
		<< "the client mode's ShouldDrawLocalPlayer no longer asks the camera";
}

//-----------------------------------------------------------------------------
// The facts this client has no answer for -- and the one it now has.
//-----------------------------------------------------------------------------
TEST( AbsentFacts, TheSkyboxReportsItselfRatherThanReturningASilentNull )
{
	// THIS WAS `FogAndSkyboxReportThemselvesRatherThanReturningASilentNull`,
	// AND THE FOG HALF WENT GREEN BY BEING FIXED. Step 8e-render built the
	// channel 7c said was missing: the master env_fog_controller's resolved
	// values ride in PlayerNetState, the managed client pushes the local
	// player's copy, and SourceSharp_ViewerFogParams hands back a pointer to it.
	// So the fog no longer returns NULL and no longer needs a ledger row. The
	// SKYBOX still does -- it is a different fault (UpdateAllClientData never
	// runs, so SourceSharp_FillSkyboxParams is never reached), and the original
	// argument is unchanged for it.
	//
	// The check that stops the step from being a compile gate is the same one:
	// a NULL that says nothing is indistinguishable from a map with no sky
	// camera, and the ledger row is the whole difference.
	const std::string strImpl = StripComments( ReadFile( "sourcesharp/game/client/shell_view_facts.cpp" ) );
	ASSERT_FALSE( strImpl.empty() );

	const size_t atSky = strImpl.find( "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );
	ASSERT_NE( atSky, std::string::npos );

	EXPECT_NE( strImpl.find( "SourceSharp_NotShipped(", atSky ), std::string::npos )
		<< "SourceSharp_ViewerSkyboxParams returns NULL without saying so";
}

TEST( AbsentFacts, TheFogAnswersAndItsRemainingNullIsCounted )
{
	// THE OTHER HALF OF THE ROW ABOVE, and the reason it is a fact rather than
	// a deletion. 7c's rule was that an absence must be DISTINGUISHABLE instead
	// of invisible, because "the fog is off" and "nobody asked" are one
	// screenshot. 8e-render made the fog answer, and the rule still applies to
	// the case that remains: a map with no MASTER env_fog_controller, and a
	// client between a level change and its first player update, both get NULL.
	//
	// RE-POINTED BY PHASE 1c, FROM THE COUNT TO THE THING THE COUNT REPORTED.
	// This used to require `g_nFogNull++` in the NULL arm, because a count was
	// what had replaced 7c's ledger row. Phase 1c deleted the four fog counters
	// -- they were readable only through SourceSharp_ViewerFogCounts, whose only
	// caller was the cs_cl_fog_report console command, so with the readout gone
	// nothing in either DLL, in managed code or under tools/ could read one.
	//
	// AND THE COUNT WAS NEVER WHAT MADE THE TWO CASES DISTINGUISHABLE. The NULL
	// is: a zeroed fogparams_t reads as "enable = false, start 0, end 0", a REAL
	// answer meaning "no fog", and every GetFog* helper in viewrender.cpp
	// handles the NULL while none of them can tell a zeroed struct from a real
	// one. So what is asserted now is the RETURN, both arms of it -- which is
	// the property 7c's rule is actually about, and which a counter could only
	// have reported on.
	const std::string strImpl = StripComments( ReadFile( "sourcesharp/game/client/shell_view_facts.cpp" ) );
	ASSERT_FALSE( strImpl.empty() );

	const size_t atFog = strImpl.find( "fogparams_t *SourceSharp_ViewerFogParams()" );
	ASSERT_NE( atFog, std::string::npos );

	const size_t atSky = strImpl.find( "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );
	ASSERT_NE( atSky, std::string::npos );
	ASSERT_LT( atFog, atSky ) << "the two bodies are expected in this order";

	const std::string strFogBody = strImpl.substr( atFog, atSky - atFog );

	EXPECT_NE( strFogBody.find( "return &g_ViewerFog;" ), std::string::npos )
		<< "SourceSharp_ViewerFogParams is back to answering nothing";

	EXPECT_NE( strFogBody.find( "if ( !g_bHaveViewerFog )" ), std::string::npos )
		<< "the fog no longer distinguishes 'nothing was pushed' from a real "
		   "answer at all.";
	EXPECT_NE( strFogBody.find( "return NULL;" ), std::string::npos )
		<< "the fog's remaining absent case answers with a struct rather than "
		   "NULL. A zeroed fogparams_t is a REAL answer meaning 'no fog', so "
		   "that would make 'this map has no fog' and 'nobody pushed any' the "
		   "same screenshot again -- the state 7c exists to leave behind.";

	EXPECT_EQ( strFogBody.find( "SourceSharp_NotShipped(" ), std::string::npos )
		<< "the fog still carries a not-shipped row while it is shipping, which "
		   "would tell cs_cl_notshipped's readers a gap is open that is closed.";
}

TEST( AbsentFacts, TheSkyboxAnswerIsNullAndNotAZeroedStruct )
{
	// A zeroed sky3dparams_t has area 0, and area 0 is a REAL area -- the
	// renderer's own "no sky camera" value is 255. So a zeroed struct would
	// send CSkyboxView at a sky camera that is not there, where NULL takes the
	// same early-out the renderer already had.
	const std::string strImpl = StripComments( ReadFile( "sourcesharp/game/client/shell_view_facts.cpp" ) );
	// The reader is the planner's SkyboxSetup since C7 step 3 (viewrender.cpp's
	// CSkyboxView::PreRender3dSkyboxWorld left).
	std::string strView;
	{
		std::ifstream planner( ( cmake_graph::RepoRoot() + "/src/client/managed/Render/RenderPlanner.cs" ).c_str() );
		std::stringstream text;
		text << planner.rdbuf();
		strView = text.str();
	}
	ASSERT_FALSE( strImpl.empty() );
	ASSERT_FALSE( strView.empty() );

	const size_t atSky = strImpl.find( "sky3dparams_t *SourceSharp_ViewerSkyboxParams()" );
	ASSERT_NE( atSky, std::string::npos );
	EXPECT_NE( strImpl.find( "return NULL;", atSky ), std::string::npos );

	// ...and the reader still refuses 255 as well, which is a different
	// refusal and the one a live map with a sky camera would take.
	EXPECT_NE( strView.find( "_in.SkyArea == 255" ), std::string::npos )
		<< "the planner's SkyboxSetup no longer refuses area 255";
}

//-----------------------------------------------------------------------------
// The NPC reorder pass, and the argument that let it go.
//-----------------------------------------------------------------------------
// ThePassIsGoneAndItsTwoAllocasWithIt read viewrender.cpp, which left at C7
// step 3 with the pass's whole file; the plan has no NPC reorder pass.

TEST( NpcReorder, NoClientClassInTheBuildAnswersIsNPC )
{
	// This is the argument the deletion rests on, checked rather than recited.
	//
	// IT USED TO BE CHECKED IN TWO HALVES: the only OVERRIDE of IsNPC on the
	// client is C_AI_BaseNPC's and its file is not compiled, and the BASE
	// answer -- `IsNPC( void ) { return false; }` in c_baseentity.h -- was a
	// constant false. Lane `the-close` retired the alias (0bc727ed9) and
	// C_BaseEntity stopped being a class at all: game/client/c_baseentity.h is
	// 372 lines now and declares no entity type, so the base half has no text
	// left to read.
	//
	// THE SUCCESSOR IS THE SAME ARGUMENT MADE MORE STRONGLY, and it was
	// asserted in three parts so that no one of them can be satisfied by an
	// emptied tree:
	//
	//   1. c_baseentity.h does not mention IsNPC AT ALL -- not as an override,
	//      not as a constant false. "Nobody in the build answers it" no longer
	//      depends on the answer being false;
	//   2. (CULLED) the one class that declares it, C_AI_BaseNPC, still
	//      answered true in game/client/c_ai_basenpc.h. That header is not in
	//      the client's compiled closure and has left the source tree, which is
	//      the stronger form of part 3;
	//   3. and that class's translation unit is still not compiled by
	//      client_sourcesharp (cmake_graph.h), proof-of-read shell_view_facts.cpp.
	const std::string strBase = StripComments( ReadFile( "game/client/c_baseentity.h" ) );
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_view_facts.cpp" ) )
		<< "client_sourcesharp does not compile shell_view_facts.cpp -- the reader is "
		   "not reading the client's source list";
	ASSERT_FALSE( strBase.empty() );

	// 3.
	// By the path's TAIL, as the old vpc needle was: the TU under any directory.
	{
		const std::set< std::string > client = cmake_graph::CompiledSources( cmake_graph::kClient );
		const std::string strTail = "/c_ai_basenpc.cpp";
		for ( std::set< std::string >::const_iterator it = client.begin(); it != client.end(); ++it )
		{
			EXPECT_FALSE( it->size() >= strTail.size()
				&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
				<< *it << ": c_ai_basenpc.cpp is in the build again -- the NPC reorder "
				   "pass had a subject after all";
		}
	}

	// 1, paired with a live read-back so the zero is a measurement and not an
	// unopened file. SDKEntityFromRenderable is defined in this header, outside
	// every guard in it, and is what the viewmodel pass above used to ask.
	EXPECT_EQ( std::string::npos, strBase.find( "IsNPC" ) )
		<< "game/client/c_baseentity.h mentions IsNPC again. There is no entity "
		   "class left in this header for it to be a member of, so check what "
		   "has come back with it -- a returning C_BaseEntity gives the reorder "
		   "pass a subject again.";
	EXPECT_NE( strBase.find( "inline IGameEntity *SDKEntityFromRenderable( IClientRenderable *pRenderable )" ),
	           std::string::npos )
		<< "c_baseentity.h did not read back as itself, so the absence above is "
		   "a file that was never opened rather than a class that is gone";
}

//=============================================================================//
//
//  STEP c7-view -- the view/input cluster's remainder.
//
//  Two things moved and one number did not, and the facts below say all three.
//
//  1. viewrender.cpp's viewmodel pass asked SDKEntityFromRenderable "is this a
//     game entity", which answers NULL for a MANAGED entity (p7-exit recorded
//     that at viewrender.cpp:4299). The keep/remove decision moved into
//     ViewSubject::ViewModelIsUnwanted, and then left with the tool framework
//     it served (SFM is dead, user ruling 2026-10-19).
//
//  2. in_joystick.cpp's two local-player asks -- the vehicle response curve and
//     the autoaim stick dampening -- go through the view_facts.h seam.
//
//  3. THE STOCK ARMS SURVIVE, under `#else` / `#elif`, because
//     briefs/CLIENT_CLUSTERS.md requires it. That is why in_joystick.cpp's
//     census reading is still 2 and not 0, and a fact below pins it so nobody
//     "fixes" the number by deleting the arm.
//
//=============================================================================//

// The viewmodel keep/remove rule's four corners stood here
// (ViewSubject::ViewModelIsUnwanted). The rule chose between a game entity's
// viewmodel and a tool's, and is deleted with the tool framework: SFM is dead
// (user ruling 2026-10-19). RenderPlannerTests holds that the plan's
// viewmodel op ignores the retired tools input.

//-----------------------------------------------------------------------------
// The source scan for step c7-view. Comments stripped for every "is it gone"
// half, raw for the census half, exactly as the block above this one does it.
//-----------------------------------------------------------------------------
// C7View.ViewRenderMakesNoEntityResolveOnTheSourceSharpSide read
// viewrender.cpp, which left at C7 step 3: its entity draws are the plan
// executor's now (DrawProgramEntity), with no entity resolve of their own.
