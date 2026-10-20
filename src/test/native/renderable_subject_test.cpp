//========= SourceSharp — the render path's de-entitying, checked ============//
//
// plan_entity.md step 7d, "Leaf system and shadows". Two halves.
//
// THE SOURCE SCAN is the step's gate clause "the census shows zero
// ClientEntityList() in these files", written down so a later edit cannot put
// one back quietly. It reads file by file rather than as a total, because a
// total that moved would not say which file moved it.
//
// THE ANTI-COMPILE-GATE HALF is the part that stops the scan being satisfied by
// deletion. A grep goes to zero just as happily when the behaviour is gone as
// when it is ported, so these facts assert that the twelve resolves still EXIST
// and go somewhere, and that the loss counter is still read by two independent
// readers. (The twelve resolves were the fork's clientshadowmgr.cpp's, and
// left with it in C7 cluster 12 S2, 2026-10-19; what stays reads the shim,
// the shadow manager's native residue and its managed report.)
//
// A THIRD HALF USED TO BE HERE AND IS NOT: the RULES, run out of
// renderable_subject.h without an engine. There were two of them. The resolve
// classification is now three counter increments at the resolve itself -- the
// header existed to make those three lines testable and nothing else -- and the
// FOV distance adjust factor is deleted outright, with its one caller, which
// passed it (1.0f, 1.0f). Lane 4c-render retired nine facts with them:
//
//   ClassifyResolve x4, IsLostRenderable x1   the classification, now inline
//   FovDistanceAdjustFactor x4                the formula, now uncalled
//
// The four FOV facts were about a real edge -- an FOV ratio the wrong way up
// fades props OUT as a scope zooms IN, which reads as an LOD fault in the
// models -- and that edge now belongs to whoever restores the factor.
// docs/absence-client.md row 6 carries the warning, including that stock
// inverts the factor at one of the three sites and not the other two.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "ss_ci_skip.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>

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
// The file with its comments taken out — 7c's stripper, and this step needs it
// for the same reason 7b did.
//
// clientshadowmgr.cpp's new include block EXPLAINS what `ClientEntityList()`
// was and why it went, and it spells the name to do it. A raw grep therefore
// reads ONE for a file whose code reads zero, exactly as 7b's
// `C_BaseTempEntity::` needle read FIVE for four comments. Stripping is what
// makes the assertion about code; the raw reading is reported beside it in the
// handover so nobody is surprised by their own grep.
//
// String literals are preserved so a "//" in a path is not read as a comment.
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
			if ( c == '\n' ) { state = CODE; out += c; }
			break;

		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += c;
			break;

		case STRING:
			out += c;
			if ( c == '\\' && n != '\0' ) { out += n; i++; }
			else if ( c == '"' ) state = CODE;
			break;

		case CHARLIT:
			out += c;
			if ( c == '\\' && n != '\0' ) { out += n; i++; }
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}

	return out;
}

// Step 7d's translation units, by the paths the vpc names them by. There were
// five; glow_outline_effect.cpp left client.so in plan_client_novalve C1 (its
// body sat under GLOWS_ENABLE, defined nowhere), detailobjectsystem.cpp in
// C7 cluster 10 (its decisions are SourceSharp.Client.Detail's, its executor
// shell_detail_props.cpp) and clientleafsystem.cpp in C7 cluster 12 L2 (its
// registry and decisions are SourceSharp.Client's LeafRegistry, its native
// residue libss_fwd_client's forwarder class) and c_pixel_visibility.cpp in C7
// (its decisions are SourceSharp.Client's PixelVisibilitySystem, its executor
// shell_pixelvis_client.cpp). The last, clientshadowmgr.cpp, left with C7
// cluster 12 S2 (2026-10-19: the client's last fork TU left client.so; the
// shadow manager is SourceSharp.Client's, src/client/managed/Render/Shadows/,
// its native residue shell_shadows_client.cpp and fwd_shadows.cpp). So the
// three facts that read "the five" -- AllFiveTranslationUnitsAreReadable,
// NoneOfTheFiveNamesTheStockClientEntityList and
// NoneOfTheFiveAsksForTheLocalPlayerEntity -- had no subject left and are
// retired with it; leaf_cycle_test.cpp's TheStockShadowManagerIsNotCompiled
// holds the eviction.

// The shadow manager's native residue, read where the fork file was.
const char kShadowsShell[] = "sourcesharp/game/client/shell_shadows_client.cpp";
const char kShadowsForwarder[] = "sourcesharp/fwd/fwd_shadows.cpp";
// And its managed report, the successor of the fork's PrintShadowReport.
const char kShadowReport[] = "sourcesharp/managed/SourceSharp.Client/Render/Shadows/ShadowManager.Debug.cs";

} // namespace

// The two facts that stood here held c_pixel_visibility.cpp free of the player
// header and of every entity type the census looks for. The file left client.so
// in C7; its port is C#, which the census does not read, so the fact below
// holds the same needles to the port.
const char kPixelVisPort[] = "sourcesharp/managed/SourceSharp.Client/Render/PixelVisibility/PixelVisibilitySystem.cs";

TEST( Step7dSourceScan, PixelVisibilityIsEntityFreeByTheCensusOwnRegex )
{
	// THE CENSUS READS RAW TEXT, so a file whose only remaining mention of an
	// entity type is a COMMENT is still counted as a TU that uses one. The
	// native file had none when it left, and its port has none either: the
	// pixel visibility asks the view and the material system, never an entity.
	//
	// The needles are tools/entity-census's USES_RE spelled out, and they are
	// checked against the tool below so this cannot drift into checking for
	// something the census does not look for.
	const std::string strFile = ReadFile( kPixelVisPort );
	ASSERT_FALSE( strFile.empty() ) << kPixelVisPort;
	ASSERT_NE( std::string::npos, strFile.find( "public float GlowSightDistance(" ) )
		<< kPixelVisPort << " no longer defines GlowSightDistance, so this reads something else";

	const char *kEntityTypes[] =
	{
		"C_BaseEntity", "CBaseEntity", "C_BasePlayer", "CBasePlayer",
		"C_BaseAnimating", "CBaseAnimating", "C_BaseCombatCharacter",
		"CBaseCombatCharacter", "C_BaseCombatWeapon", "CBaseCombatWeapon",
		"C_BaseFlex", "CBaseFlex", "gEntList",
	};

	for ( size_t i = 0; i < sizeof( kEntityTypes ) / sizeof( kEntityTypes[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, strFile.find( kEntityTypes[ i ] ) )
			<< kPixelVisPort << " names " << kEntityTypes[ i ]
			<< " -- in code or in a comment, the census counts it either way";
	}
}

TEST( Step7dSourceScan, TheCensusStillLooksForTheTypesTheFactAboveChecks )
{
	// THE ANCHOR FOR THE FACT ABOVE. A list of needles copied out of another
	// tool is a list that goes stale silently: if the census renamed its regex
	// or dropped a family, the loop above would keep passing while measuring
	// nothing the census measures. Reading the tool is what stops that.
	const std::string strCensus = ReadFile( "../tools/entity-census" );
	ASSERT_FALSE( strCensus.empty() );

	EXPECT_NE( std::string::npos, strCensus.find( "USES_RE=" ) );
	EXPECT_NE( std::string::npos,
	           strCensus.find( "C_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon|Flex)" ) );
}

//=============================================================================
// PART 3 — THE PART THAT STOPS THIS BEING A COMPILE GATE
//
// "A compile gate selects for deletion." Every fact in part 2 is satisfied by
// deleting the code, so each one needs a partner saying the behaviour is still
// there and goes somewhere real.
//=============================================================================

// TheShadowManagerStillResolvesTwelveHandles STOOD HERE and is retired with
// its subject (2026-10-19, C7 cluster 12 S2: clientshadowmgr.cpp, the client's
// last fork TU, left client.so). It held the fork file to exactly twelve
// SourceSharp_RenderableFromHandle resolves before PrintShadowReport (it was
// thirteen until lane 4c-render deleted BuildFlashlight's clip-plane ask). The
// managed shadow manager resolves through its native residue's RenderableOf
// (shell_shadows_client.cpp), one op per ask, and its oracle
// (ShadowManagerOracleTests.cs) holds what each path does with the answer.

// READ THE ASSERTIONS, NOT THE NAME OF THE FACT THIS REPLACES. Both of the
// facts that stood here -- "the two FOV sites still scale something" and "the
// FOV gap is on the not-shipped ledger" -- were about
// SourceSharp_ViewerFovDistanceAdjustFactor, which lane 4c-render deleted along
// with all three of its call sites.
//
// WHY THEY COULD NOT BE KEPT. The function returned exactly 1.0f on every path:
// PlayerNetState carries no fov, so stock's localFOV/defaultFOV has nothing to
// divide and is 1 BY CONSTRUCTION. Its three call sites multiplied or divided
// by 1 every frame, and its ledger row fired the first time a viewer existed
// and said the same sentence for the rest of the session. Deleting it changed
// no pixel.
//
// THE SUCCESSOR IS THE RECORD, WHICH IS THE HALF THAT WAS ALWAYS THE POINT. The
// original fact existed because a bare `1.0f` is indistinguishable from a
// working factor, so a reader who finds that a scope does not hold detail props
// in view has nothing to find. That is now docs/absence-client.md row 6, and
// this fact holds it there -- INCLUDING the asymmetry, which is the part a
// restoration gets wrong: stock inverts the factor at SortSpritesBackToFront
// and not at the other two sites, so a restoration that spells all three the
// same way doubles the error instead of cancelling it.
TEST( Step7dNotDeleted, TheFovGapIsRecordedWhereARestorerWillFindIt )
{
	const std::string strDoc = ReadFile( "../docs/absence-client.md" );
	ASSERT_FALSE( strDoc.empty() )
		<< "docs/absence-client.md did not open, so every check below passes "
		   "over nothing";

	EXPECT_NE( std::string::npos, strDoc.find( "FOV-scaled render fade distance" ) )
		<< "the absence ledger no longer carries the FOV gap by name, so the "
		   "three unscaled distances are a silent 1.0f again";

	const size_t posRow = strDoc.find( "FOV-scaled render fade distance" );
	const std::string strRow = strDoc.substr( posRow );

	EXPECT_NE( std::string::npos, strRow.find( "SortSpritesBackToFront" ) )
		<< "the row no longer names the site stock INVERTS the factor at";
	EXPECT_NE( std::string::npos, strRow.find( "BuildDetailObjectRenderLists" ) )
		<< "the row no longer names the second detail-prop site";
	EXPECT_NE( std::string::npos, strRow.find( "GlowSightDistance" ) )
		<< "the row no longer names the pixel-visibility site";
	EXPECT_NE( std::string::npos, strRow.find( "doubles the error" ) )
		<< "the row no longer warns that a restoration spelling all three "
		   "sites the same way doubles the error instead of cancelling it";
}

TEST( Step7dNotDeleted, TheThreeUnscaledSitesAreTheBareOnesTheLedgerNames )
{
	// The ledger row (the fact above) names three sites. A reader at the line
	// sees a bare 1 where stock multiplied by the FOV factor; the row is where
	// the why lives. These facts pin the CODE of the glow site, read with
	// comments stripped, so they hold whether or not the SDK tree carries the
	// fork's comments: the glow site multiplies by nothing, and nothing calls
	// the deleted function. The two detail sites left with
	// detailobjectsystem.cpp (C7 cluster 10); DetailPropSystem.cs holds each
	// factor at 1 and DetailPropsOracleTests holds both fades to stock's,
	// recorded with the local player NULL. The glow site left with
	// c_pixel_visibility.cpp (C7): PixelVisibilitySystem.cs's GlowSightDistance,
	// whose body is read here (it closes at its own four-space '}'), and
	// PixelVisibilityOracleTests holds its distances to stock's.
	const std::string strPixel = StripComments( ReadFile( kPixelVisPort ) );
	ASSERT_FALSE( strPixel.empty() ) << kPixelVisPort;

	const size_t posGlow = strPixel.find( "public float GlowSightDistance(" );
	ASSERT_NE( std::string::npos, posGlow ) << "GlowSightDistance is gone or renamed";
	const size_t endGlow = strPixel.find( "\n    }", posGlow );
	ASSERT_NE( std::string::npos, endGlow );
	const std::string strGlow = strPixel.substr( posGlow, endGlow - posGlow );
	ASSERT_NE( std::string::npos, strGlow.find( "return dist;" ) )
		<< "the body read is not GlowSightDistance's";

	EXPECT_EQ( std::string::npos, strGlow.find( "actor" ) )
		<< "GlowSightDistance scales by a factor again; row 6 says it does not";
	EXPECT_EQ( std::string::npos, strGlow.find( "Fov" ) )
		<< "GlowSightDistance scales by a factor again; row 6 says it does not";

	EXPECT_EQ( std::string::npos, strPixel.find( "FovDistanceAdjustFactor" ) );
}

TEST( Step7dNotDeleted, TheLostRenderableCounterIsActuallyRead )
{
	// The counter that makes the seam falsifiable. A struct field nothing
	// prints is a number that cannot fail -- the recorded shape of a floor or a
	// sum that passes because nothing read it.
	//
	// THE SEAM'S OWN COUNTER LEFT WITH IT: shell_renderable_facts.cpp's
	// s_ResolveStats.nUnresolved (counted only for a VALID handle that resolved
	// to NULL, and printed by cs_cl_renderable_report) went when
	// SourceSharp_RenderableFromHandle lost its last importer,
	// clientshadowmgr.cpp (C7 cluster 12 S2). The reader left is the managed
	// shadow report's (ShadowManager.Debug.cs), which counts the shadows whose
	// renderable did not resolve itself, prints the count and names each.
	EXPECT_TRUE( ReadFile( "sourcesharp/game/client/shell_renderable_facts.cpp" ).empty() )
		<< "shell_renderable_facts.cpp is back in the farm";
	const std::string strReport = StripComments( ReadFile( kShadowReport ) );
	ASSERT_FALSE( strReport.empty() ) << kShadowReport;
	ASSERT_NE( std::string::npos, strReport.find( "public void PrintShadowReport(bool resetCounters)" ) )
		<< kShadowReport << " no longer defines PrintShadowReport, so this reads something else";

	EXPECT_NE( std::string::npos, strReport.find( "if (renderable == 0)\n                ++lost;" ) )
		<< "the managed shadow report no longer counts a shadow whose renderable "
		   "did not resolve";
	EXPECT_NE( std::string::npos, strReport.find( "lostRenderable {lost}" ) )
		<< "the managed shadow report no longer prints its lost-renderable count";
}

// TheShadowReportSnapshotsTheStatsBeforeItsOwnAsks STOOD HERE and is retired
// with clientshadowmgr.cpp (C7 cluster 12 S2, 2026-10-19). It held the fork's
// PrintShadowReport to snapshot SourceSharp_RenderableResolveStats() before
// its own per-shadow resolves, so the report could not count its own traffic.
// The managed report prints no resolve stats at all -- only the counts it
// takes over the list it walks -- so there is no snapshot to order.
//
// The report has to say so out loud when it is about nothing: an empty shadow
// list prints "held 0" and every other number reads as a clean bill of
// health, and "no failures" over an empty list is the exact reading this port
// keeps being fooled by. RE-POINTED with the same date and reason from the
// fork's PrintShadowReport to its managed successor, which prints the same
// sentence; the guard is read too, so a line printed always (or never) fails.
TEST( Step7dNotDeleted, TheShadowReportSaysSoWhenItIsAboutNothing )
{
	const std::string strReport = StripComments( ReadFile( kShadowReport ) );
	ASSERT_FALSE( strReport.empty() ) << kShadowReport;

	const size_t posFn = strReport.find( "public void PrintShadowReport(bool resetCounters)" );
	ASSERT_NE( std::string::npos, posFn ) << kShadowReport << " no longer defines PrintShadowReport";

	const size_t posGuard = strReport.find( "if (held == 0)", posFn );
	ASSERT_NE( std::string::npos, posGuard )
		<< "PrintShadowReport no longer tests for an empty shadow list";
	const size_t posSay = strReport.find( "THE MANAGER HOLDS NO SHADOWS", posFn );
	EXPECT_NE( std::string::npos, posSay )
		<< "PrintShadowReport no longer says that the list it reported on is empty";
	EXPECT_LT( posGuard, posSay ) << "the empty-list sentence is not under its guard";
	// The sentence is the statement right after the guard, not one somewhere later.
	EXPECT_EQ( std::string::npos, strReport.substr( posGuard, posSay - posGuard ).find( ';' ) )
		<< "something stands between the empty-list guard and its sentence";
}

// READ THE ASSERTIONS, NOT THE NAME. THE FOUR BLOCKS ARE GONE FROM BOTH ENDS,
// AND THAT IS THE THIRD TIME THIS FACT HAS BEEN REWRITTEN FOR THE SAME REASON.
//
// The history in one paragraph. Shard T5 ruled that clientleafsystem.cpp's
// bone-merge bounds and clientshadowmgr.cpp's clip plane, bone-setup queue and
// NPC/player gate should be GUARDED rather than deleted, and this fact forbade
// deleting them. Wave 2b's lane c7-leaf RELOCATED all four into the shim, and
// the fact moved with them. Lane `the-close` deleted the `#ifndef
// SS_CBASEENTITY_ALIAS` arms inside them -- source no compiler ever saw, since
// both vpcs define the macro -- and the fact was rewritten to assert the
// remaining `return false;`. Lane 4c-render measured what was left: each seam
// was a constant, and the stock callers were four branches never taken. Seams
// and calls are both deleted.
//
// WHAT WAS ALWAYS THE POINT, AND IS STILL CHECKABLE. T5's verdict protected one
// thing: that the entity BEHAVIOUR had not been quietly dropped while the grep
// went to zero. It has been dropped, on purpose, and the honest successor is
// therefore a pair --
//
//   the absence:  no entity reach survives at any of the four places, in any
//                 arm, in either stock file or in the shim;
//   the presence: each of the four places still does the work that does not
//                 need an entity, so "no entity reach" cannot be satisfied by
//                 deleting the function around it.
//
// The presence half of each is asserted in detail by leaf_cycle_test.cpp --
// both bounds functions still build a box, the threaded setup pass still runs,
// BuildFlashlight still adds the three planes it computes, the flashlight path
// still adds to static props. What is asserted HERE is the absence, in both
// files at once, which is the half that catches a block moving back home.
// (The shadow manager's presence facts left with clientshadowmgr.cpp in C7
// cluster 12 S2, 2026-10-19; its managed port's oracle holds those paths now,
// and its native residue is the shadow end read below.)
TEST( Step7dNotDeleted, TheFourRelocatedBlocksAreGoneFromBothEnds )
{
	// THE LEAF SYSTEM'S END IS NOW OURS. clientleafsystem.cpp left client.so
	// (C7 cluster 12 L2) and its dead dump with it; what client.so and
	// libss_fwd_client keep of it is the forwarder class and its home, and
	// they are read here as the leaf end. The stock file is gone from the farm.
	EXPECT_TRUE( ReadFile( "game/client/clientleafsystem.cpp" ).empty() )
		<< "clientleafsystem.cpp is back in the farm";
	const std::string strLeaf =
		StripComments( ReadFile( "sourcesharp/game/client/shell_leaf_system_client.cpp" ) ) +
		StripComments( ReadFile( "sourcesharp/fwd/fwd_leaf_system.cpp" ) );
	ASSERT_NE( std::string::npos, strLeaf.find( "IClientLeafSystem *g_pClientLeafSystem" ) )
		<< "the leaf end was not read";
	ASSERT_NE( std::string::npos, strLeaf.find( "class SSClientLeafSystem final : public IClientLeafSystem" ) )
		<< "the forwarder class was not read";

	// THE SHADOW MANAGER'S END IS NOW OURS TOO. clientshadowmgr.cpp, the
	// client's last fork TU, left client.so with C7 cluster 12 S2 (2026-10-19);
	// its decisions are SourceSharp.Client's and what client.so and
	// libss_fwd_client keep of it is the executor and its forwarder table,
	// read here as the shadow end on the leaf end's model. The stock file is
	// gone from the farm, and the two residue files are proved read by an
	// anchor each, so the absences below are about files that were opened.
	EXPECT_TRUE( ReadFile( "game/client/clientshadowmgr.cpp" ).empty() )
		<< "clientshadowmgr.cpp is back in the farm";
	const std::string strShadowShell = StripComments( ReadFile( kShadowsShell ) );
	const std::string strShadowFwd = StripComments( ReadFile( kShadowsForwarder ) );
	ASSERT_NE( std::string::npos, strShadowShell.find( "IClientRenderable *RenderableOf( uint32_t hEntity )" ) )
		<< kShadowsShell << " was not read, or no longer resolves a shadow's renderable";
	ASSERT_NE( std::string::npos, strShadowFwd.find( "int32_t SSFwd_Shadows_Run( const SSShadowEnv *pEnv" ) )
		<< kShadowsForwarder << " was not read";
	const std::string strShadow = strShadowShell + strShadowFwd;
	ASSERT_FALSE( strLeaf.empty() );
	ASSERT_FALSE( strShadow.empty() );
	// The shim the seams were relocated OUT of went too, with its last resolve
	// (C7 cluster 12 S2): its absence is the strongest form of the absences
	// below.
	EXPECT_TRUE( ReadFile( "sourcesharp/game/client/shell_renderable_facts.cpp" ).empty() )
		<< "shell_renderable_facts.cpp is back in the farm";
	EXPECT_TRUE( ReadFile( "sourcesharp/game/client/renderable_facts.h" ).empty() )
		<< "renderable_facts.h is back in the farm";

	// 1. THE SEAM NAMES, IN BOTH ENDS. A name in either is either a restored
	//    body or a call with nothing to answer it.
	const char *kSeams[] = {
		"SourceSharp_BoneMergeBoundsFromFollowedEntity",
		"SourceSharp_BoneMergeBoundsFromMoveParent",
		"SourceSharp_ShadowRenderingClipPlane",
		"SourceSharp_ShadowSourceIsNpcOrPlayer",
		"SourceSharp_ShadowBoneSetupQueueReset",
		"SourceSharp_ShadowBoneSetupQueueAdd",
		"SourceSharp_ShadowBoneSetupQueueRun",
	};
	for ( size_t i = 0; i < sizeof( kSeams ) / sizeof( kSeams[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, strLeaf.find( kSeams[ i ] ) )
			<< "the leaf system's native end calls " << kSeams[ i ] << " again";
		EXPECT_EQ( std::string::npos, strShadow.find( kSeams[ i ] ) )
			<< "the shadow manager's native end calls " << kSeams[ i ] << " again";
	}

	// 2. AND THE ENTITY READS THEMSELVES, which is what a restoration would
	//    actually bring back -- under any name, including no name at all if
	//    someone puts the block back inline where it started.
	const char *kReaches[] = {
		"GetFollowedEntity()",
		"IsFollowingEntity()",
		"m_fRenderingClipPlane",
		"SDKEntityFromRenderable(",
		"SDKEntityFromUnknown(",
		"C_BaseAnimating",
	};
	for ( size_t i = 0; i < sizeof( kReaches ) / sizeof( kReaches[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, strShadow.find( kReaches[ i ] ) )
			<< "the shadow manager's native end reaches an SDK entity again ("
			<< kReaches[ i ] << ")";
		EXPECT_EQ( std::string::npos, strLeaf.find( kReaches[ i ] ) )
			<< "the leaf system's native end reaches an SDK entity again ("
			<< kReaches[ i ] << ")";
	}
}

// TheFlashlightTargetWalkIsGoneAndItsVacuityWallStands STOOD HERE and is
// retired with its subject (2026-10-19, C7 cluster 12 S2: clientshadowmgr.cpp,
// the client's last fork TU, left client.so). It kept standing the argument
// that licensed the `residue` wave's deletion of the fork file's
// flashlight-target walk: m_hTargetEntity read into bLightSpecificEntity,
// written in one place, and three IsFlashlightTarget call sites that
// short-circuited on it, with nothing dereferencing the handle. Its partner,
// cpp_alias_t5_test.cpp's FlashlightTargetVacuity, is retired the same day.
// The managed shadow manager ports the target path whole (SetFlashlightTarget
// and IsFlashlightTarget, ShadowManager.Projection.cs, resolving the target
// by handle through RenderableOf), so there is no vacuous arm left to wall.
