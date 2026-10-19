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
// readers.
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

//-----------------------------------------------------------------------------
// LINE-INITIAL CONDITIONAL DIRECTIVES, counted on the RAW text.
//
// Needed because "this file has no alias guard left" cannot be asked with a
// substring: shell_renderable_facts.cpp still SPELLS
// `#ifndef SS_CBASEENTITY_ALIAS` in a comment (:189, describing the block the
// close deleted), and the close deliberately did not rewrite prose. A raw
// find() would read that sentence as a guard -- the "prose counts as code to a
// grep" hazard, which has fired on this lane before.
//
// `#include`, `#define` and `#pragma` select nothing and are not counted.
//-----------------------------------------------------------------------------
int CountConditionalDirectives( const std::string &strRaw )
{
	static const char *kWords[] = { "#if", "#ifdef", "#ifndef", "#else", "#elif" };

	int n = 0;
	size_t i = 0;
	while ( i < strRaw.size() )
	{
		size_t e = strRaw.find( '\n', i );
		if ( e == std::string::npos )
			e = strRaw.size();

		const std::string line = strRaw.substr( i, e - i );
		i = e + 1;

		const size_t w = line.find_first_not_of( " \t" );
		if ( w == std::string::npos || line[ w ] != '#' )
			continue;

		const std::string rest = line.substr( w );
		const size_t sp = rest.find_first_of( " \t" );
		const std::string word = rest.substr( 0, sp );

		for ( size_t k = 0; k < sizeof( kWords ) / sizeof( kWords[ 0 ] ); k++ )
		{
			if ( word == kWords[ k ] )
			{
				n++;
				break;
			}
		}
	}

	return n;
}

int CountOccurrences( const std::string &strHaystack, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t pos = strHaystack.find( strNeedle );
	      pos != std::string::npos;
	      pos = strHaystack.find( strNeedle, pos + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

// Step 7d's translation units, by the paths the vpc names them by. There were
// five; glow_outline_effect.cpp left client.so in plan_client_novalve C1 (its
// body sat under GLOWS_ENABLE, defined nowhere), detailobjectsystem.cpp in
// C7 cluster 10 (its decisions are SourceSharp.Client.Detail's, its executor
// shell_detail_props.cpp) and clientleafsystem.cpp in C7 cluster 12 L2 (its
// registry and decisions are SourceSharp.Client's LeafRegistry, its native
// residue libss_fwd_client's forwarder class), so "the five" below are two.
const char *kStep7dTus[] =
{
	"game/client/clientshadowmgr.cpp",
	"game/client/c_pixel_visibility.cpp",
};

const size_t kStep7dTuCount = sizeof( kStep7dTus ) / sizeof( kStep7dTus[ 0 ] );

} // namespace

TEST( Step7dSourceScan, AllFiveTranslationUnitsAreReadable )
{
	// The anchor. Every fact below reads these five, and ReadFile answers an
	// EMPTY STRING for a path it cannot open — which contains no needle and
	// would make every scan below pass for a tree with no source in it at all.
	for ( size_t i = 0; i < kStep7dTuCount; i++ )
	{
		EXPECT_FALSE( ReadFile( kStep7dTus[ i ] ).empty() ) << kStep7dTus[ i ];
	}
}

TEST( Step7dSourceScan, NoneOfTheFiveNamesTheStockClientEntityList )
{
	// THE STEP'S OWN GATE CLAUSE. Both spellings the census counts:
	// `ClientEntityList()` is the accessor and `cl_entitylist` is the pointer
	// behind it, and a file that switched from one to the other would satisfy a
	// check written for only one of them.
	for ( size_t i = 0; i < kStep7dTuCount; i++ )
	{
		const std::string strFile = StripComments( ReadFile( kStep7dTus[ i ] ) );
		ASSERT_FALSE( strFile.empty() ) << kStep7dTus[ i ];

		EXPECT_EQ( std::string::npos, strFile.find( "ClientEntityList()" ) )
			<< kStep7dTus[ i ] << " still calls ClientEntityList(), the STOCK "
			   "CClientEntityList. The list the engine holds is "
			   "CSourceSharpClientEntityList over the slot table; ask "
			   "SourceSharp_RenderableFromHandle (renderable_facts.h) instead.";

		EXPECT_EQ( std::string::npos, strFile.find( "cl_entitylist" ) )
			<< kStep7dTus[ i ] << " names cl_entitylist directly";
	}
}

TEST( Step7dSourceScan, NoneOfTheFiveAsksForTheLocalPlayerEntity )
{
	// The other entity reach these files had. `C_BasePlayer::GetLocalPlayer()`
	// has ONE definition on this link line and it is `return NULL`
	// (shell_player_orphans.cpp), so every one of these was a dead branch
	// standing on its initialiser. detailobjectsystem.cpp kept one, inside
	// `#ifdef USE_DETAIL_SHAPES`, which this build never defined; the file has
	// left client.so (C7 cluster 10).
	const char *kMustBeClean[] =
	{
		"game/client/clientshadowmgr.cpp",
		"game/client/c_pixel_visibility.cpp",
	};

	for ( size_t i = 0; i < sizeof( kMustBeClean ) / sizeof( kMustBeClean[ 0 ] ); i++ )
	{
		const std::string strFile = StripComments( ReadFile( kMustBeClean[ i ] ) );
		ASSERT_FALSE( strFile.empty() ) << kMustBeClean[ i ];

		EXPECT_EQ( std::string::npos, strFile.find( "GetLocalPlayer" ) )
			<< kMustBeClean[ i ] << " still asks C_BasePlayer::GetLocalPlayer(), "
			   "which answers NULL on this link line";
	}
}

TEST( Step7dSourceScan, PixelVisibilityNoLongerIncludesThePlayerHeader )
{
	// The include is the coupling, not the call. A file that stopped calling
	// GetLocalPlayer but kept c_baseplayer.h still drags the player entity's
	// whole surface into a render TU, and 7h's gate is about exactly that.
	const std::string strFile = StripComments( ReadFile( "game/client/c_pixel_visibility.cpp" ) );
	ASSERT_FALSE( strFile.empty() );

	EXPECT_EQ( std::string::npos, strFile.find( "c_baseplayer.h" ) );
}

TEST( Step7dSourceScan, PixelVisibilityIsEntityFreeByTheCensusOwnRegex )
{
	// THE CENSUS READS RAW TEXT, so a file whose only remaining mention of an
	// entity type is a COMMENT is still counted as a TU that uses one. This
	// file has none at all now, which moves it out of "stock TUs that use an
	// entity type but define none" and into "stock TUs with entity-free text" —
	// a real census movement of one TU rather than a grep tidied around.
	//
	// The needles are tools/entity-census's USES_RE spelled out, and they are
	// checked against the tool below so this cannot drift into checking for
	// something the census does not look for.
	const std::string strFile = ReadFile( "game/client/c_pixel_visibility.cpp" );
	ASSERT_FALSE( strFile.empty() );

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
			<< "c_pixel_visibility.cpp names " << kEntityTypes[ i ]
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

// THE COUNT IS TWELVE AND IT WAS THIRTEEN. Lane 4c-render deleted the
// thirteenth: BuildFlashlight's tail resolved a renderable only to ask it for
// its own rendering clip plane, which is two FIELDS on C_BaseEntity and
// therefore answered `false` on every path in every build that ran. The resolve
// went with the question. The other twelve are unchanged.
TEST( Step7dNotDeleted, TheShadowManagerStillResolvesTwelveHandles )
{
	// THE EXACT COUNT, not a floor. A floor here would pass just as happily for
	// a file with one resolve left and eleven `return`s in place of the rest,
	// and that is precisely the shape a compile gate rewards. Every one of the
	// twelve either tests the answer or Asserts it.
	const std::string strFile = StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	ASSERT_FALSE( strFile.empty() );

	// COUNTED UP TO PrintShadowReport AND NOT PAST IT. The report resolves once
	// per shadow too, so a whole-file count would be twelve ported sites plus
	// however many the INSTRUMENT makes — and an instrument that can hold the
	// count up on its own is the same defect as an instrument that reports its
	// own traffic. The subject is the render path's resolves; the report is not
	// one of them.
	const size_t posReport = strFile.find( "void CClientShadowMgr::PrintShadowReport" );
	ASSERT_NE( std::string::npos, posReport );

	EXPECT_EQ( 12, CountOccurrences( strFile.substr( 0, posReport ),
	                                 "SourceSharp_RenderableFromHandle(" ) )
		<< "clientshadowmgr.cpp no longer makes twelve handle->renderable "
		   "resolves. If a resolve was legitimately removed, move this number "
		   "and say which one in the commit — do not raise it to a floor.";

	EXPECT_NE( std::string::npos, strFile.find( "#include \"renderable_facts.h\"" ) );
}

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

// The body of FUNCTION in STRIPPED text, from its definition to the next
// line-initial '}' -- the shape every function in these Valve files closes
// with. Empty when the definition is not there.
std::string FunctionBody( const std::string &strCode, const std::string &strSignature )
{
	const size_t pos = strCode.find( strSignature );
	if ( pos == std::string::npos )
		return std::string();
	const size_t end = strCode.find( "\n}", pos );
	return strCode.substr( pos, end == std::string::npos ? std::string::npos : end - pos );
}

TEST( Step7dNotDeleted, TheThreeUnscaledSitesAreTheBareOnesTheLedgerNames )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	// The ledger row (the fact above) names three sites. A reader at the line
	// sees a bare 1 where stock multiplied by the FOV factor; the row is where
	// the why lives. These facts pin the CODE of the native site, read with
	// comments stripped, so they hold whether or not the SDK tree carries the
	// fork's comments: the glow site multiplies by nothing, and nothing calls
	// the deleted function. The two detail sites left with
	// detailobjectsystem.cpp (C7 cluster 10); DetailPropSystem.cs holds each
	// factor at 1 and DetailPropsOracleTests holds both fades to stock's,
	// recorded with the local player NULL.
	const std::string strPixel = StripComments( ReadFile( "game/client/c_pixel_visibility.cpp" ) );
	ASSERT_FALSE( strPixel.empty() );

	const std::string strGlow = FunctionBody( strPixel, "float GlowSightDistance(" );
	ASSERT_FALSE( strGlow.empty() ) << "GlowSightDistance is gone or renamed";

	EXPECT_EQ( std::string::npos, strGlow.find( "actor" ) )
		<< "GlowSightDistance scales by a factor again; row 6 says it does not";

	EXPECT_EQ( std::string::npos, strPixel.find( "FovDistanceAdjustFactor" ) );
}

TEST( Step7dNotDeleted, TheLostRenderableCounterIsActuallyRead )
{
	// The counter that makes the seam falsifiable. A struct field nothing
	// prints is a number that cannot fail — the recorded shape of a floor or a
	// sum that passes because nothing read it. Two independent readers are
	// required: the seam's own report and the shadow manager's.
	const std::string strFacts =
		StripComments( ReadFile( "sourcesharp/game/client/shell_renderable_facts.cpp" ) );
	ASSERT_FALSE( strFacts.empty() );

	EXPECT_NE( std::string::npos, strFacts.find( "s.nUnresolved" ) )
		<< "cs_cl_renderable_report no longer prints the lost-renderable count";
	EXPECT_NE( std::string::npos, strFacts.find( "++s_ResolveStats.nUnresolved" ) )
		<< "nothing increments the lost-renderable count, so the two readers "
		   "below are reading a permanent zero";

	// AND IT IS GATED ON THE HANDLE BEING VALID, which is the whole distinction
	// the counter exists to make: a NULL for an INVALID handle is a caller
	// asking about nothing, a NULL for a VALID one is a renderable the render
	// path has lost. This used to be RenderableSubject::IsLostRenderable, a
	// rule with its own unit test; lane 4c-render inlined it, so the shape is
	// what is checked now.
	EXPECT_NE( std::string::npos, strFacts.find( "else if ( hEnt.IsValid() )" ) )
		<< "the lost-renderable count is no longer gated on the handle being "
		   "valid, so every resolve of a null handle reads as a loss and the "
		   "number the shadow gate asserts is zero can never be zero";

	const std::string strShadow = StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	ASSERT_FALSE( strShadow.empty() );

	EXPECT_NE( std::string::npos, strShadow.find( "stats.nUnresolved" ) );
}

TEST( Step7dNotDeleted, TheShadowReportSnapshotsTheStatsBeforeItsOwnAsks )
{
	// THE INSTRUMENT MUST NOT ANSWER ABOUT ITSELF. PrintShadowReport calls
	// SourceSharp_RenderableFromHandle once per shadow, so reading the stats
	// after that loop would report the report's own traffic — and a gate
	// asserting `asks > 0` would then pass on a client whose render path had
	// asked nothing at all. The snapshot must be taken before the first
	// resolve, so it must appear before the loop that does them.
	const std::string strShadow = StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	ASSERT_FALSE( strShadow.empty() );

	const size_t posFn = strShadow.find( "void CClientShadowMgr::PrintShadowReport" );
	ASSERT_NE( std::string::npos, posFn );

	const size_t posSnapshot =
		strShadow.find( "SourceSharpRenderableResolveStats_t stats = SourceSharp_RenderableResolveStats()",
		                posFn );
	ASSERT_NE( std::string::npos, posSnapshot );

	const size_t posFirstResolve =
		strShadow.find( "SourceSharp_RenderableFromHandle( shadow.m_Entity )", posFn );
	ASSERT_NE( std::string::npos, posFirstResolve );

	EXPECT_LT( posSnapshot, posFirstResolve )
		<< "PrintShadowReport reads the resolve stats after its own resolves, "
		   "so the numbers it prints include its own traffic.";
}

TEST( Step7dNotDeleted, TheShadowReportSaysSoWhenItIsAboutNothing )
{
	// An empty shadow list prints "held 0" and every other number reads as a
	// clean bill of health. The report has to say that out loud, because "no
	// failures" over an empty list is the exact reading this port keeps being
	// fooled by.
	const std::string strShadow = ReadFile( "game/client/clientshadowmgr.cpp" );
	ASSERT_FALSE( strShadow.empty() );

	EXPECT_NE( std::string::npos, strShadow.find( "THE MANAGER HOLDS NO SHADOWS" ) );
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

	const std::string strShadow = StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	const std::string strFacts =
		StripComments( ReadFile( "sourcesharp/game/client/shell_renderable_facts.cpp" ) );
	const std::string strFactsHeader =
		StripComments( ReadFile( "sourcesharp/game/client/renderable_facts.h" ) );
	ASSERT_FALSE( strLeaf.empty() );
	ASSERT_FALSE( strShadow.empty() );
	ASSERT_FALSE( strFacts.empty() );
	ASSERT_FALSE( strFactsHeader.empty() );

	// 1. THE SEAM NAMES, IN ALL FOUR FILES. A name in any of them is either a
	//    restored body or a call with nothing to answer it.
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
		EXPECT_EQ( std::string::npos, strFacts.find( kSeams[ i ] ) )
			<< "the shim defines " << kSeams[ i ] << " again";
		EXPECT_EQ( std::string::npos, strFactsHeader.find( kSeams[ i ] ) )
			<< "the shim declares " << kSeams[ i ] << " again";
		EXPECT_EQ( std::string::npos, strLeaf.find( kSeams[ i ] ) )
			<< "the leaf system's native end calls " << kSeams[ i ] << " again";
		EXPECT_EQ( std::string::npos, strShadow.find( kSeams[ i ] ) )
			<< "clientshadowmgr.cpp calls " << kSeams[ i ] << " again";
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
		EXPECT_EQ( std::string::npos, strFacts.find( kReaches[ i ] ) )
			<< "the shim reaches an SDK entity again (" << kReaches[ i ] << "). "
			<< "That resolve walks the partition before it asks "
			<< "GetBaseEntity(), and there is no SDK class left for it to "
			<< "answer with -- so it is a partition walk in front of a NULL.";
		EXPECT_EQ( std::string::npos, strShadow.find( kReaches[ i ] ) )
			<< "clientshadowmgr.cpp reaches an SDK entity again ("
			<< kReaches[ i ] << ")";
		EXPECT_EQ( std::string::npos, strLeaf.find( kReaches[ i ] ) )
			<< "the leaf system's native end reaches an SDK entity again ("
			<< kReaches[ i ] << ")";
	}

	// 3. AND THE WHOLE-FILE FORM, which is what makes every absence above a
	//    statement about the build rather than about the arm I happen to be in.
	//    Counted line-initially: prose that spells a guard is not a guard.
	EXPECT_EQ( 0, CountConditionalDirectives( strFacts ) )
		<< "shell_renderable_facts.cpp has conditional compilation in it again. "
		   "Every absence above becomes a claim about one arm -- re-check each "
		   "against the guard before believing it.";

	// 4. THE POSITIVE ANCHOR FOR THE WHOLE FACT. Everything above is satisfied
	//    by an empty shim. The resolve is what the file is still for, and the
	//    two stock files still ask it.
	EXPECT_NE( std::string::npos,
	           strFacts.find( "IClientRenderable *SourceSharp_RenderableFromHandle( const CBaseHandle &hEnt )" ) )
		<< "shell_renderable_facts.cpp no longer defines the resolve, so every "
		   "absence above is a statement about an empty file";
	EXPECT_NE( std::string::npos, strShadow.find( "SourceSharp_RenderableFromHandle(" ) )
		<< "clientshadowmgr.cpp no longer resolves handles through the shim";
}

// READ THE ASSERTIONS, NOT THE NAME. The flashlight-target walk is the one of
// the five guarded regions that could NOT relocate -- its body called
// AddShadowToReceiver and IsFlashlightTarget, both CClientShadowMgr members --
// so it was deleted where it stood by the `residue` wave, on the strength of an
// argument that it was unreachable. This fact is that argument, kept standing.
//
// AN ABSENCE WRITTEN ALONE HERE WOULD PASS OVER AN EMPTY FILE and over one
// where the flashlight machinery had been deleted outright, so it is written
// WITH the vacuity wall that licensed the deletion:
//
//   presence -- m_hTargetEntity is still read into bLightSpecificEntity, still
//               written in exactly one place, and all three IsFlashlightTarget
//               call sites still short-circuit on `(!shadow.m_hTargetEntity) ||`.
//               FlashlightTargetVacuity in cpp_alias_t5_test.cpp owns the far
//               end.
//   absence  -- nothing dereferences the handle any more, in any arm.
TEST( Step7dNotDeleted, TheFlashlightTargetWalkIsGoneAndItsVacuityWallStands )
{
	// Comment-stripped, because clientshadowmgr.cpp's surviving prose is a long
	// explanation of the walk that went and it spells the names -- the same
	// trap this file's own StripComments header records for ClientEntityList().
	const std::string strShadowCode =
		StripComments( ReadFile( "game/client/clientshadowmgr.cpp" ) );
	ASSERT_FALSE( strShadowCode.empty() );

	EXPECT_NE( std::string::npos,
	           strShadowCode.find( "bool bLightSpecificEntity = shadow.m_hTargetEntity.Get() != NULL;" ) )
		<< "clientshadowmgr.cpp no longer reads m_hTargetEntity into "
		   "bLightSpecificEntity, so the early return that makes the deleted "
		   "walk unreachable is not there and the absence below proves nothing";
	EXPECT_NE( std::string::npos,
	           strShadowCode.find( "shadow.m_hTargetEntity = targetEntity;" ) )
		<< "SetFlashlightTarget no longer writes m_hTargetEntity; the \"written "
		   "in exactly one place\" half of the vacuity argument is gone";
	EXPECT_EQ( 3, CountOccurrences( strShadowCode,
	           "(!shadow.m_hTargetEntity) || IsFlashlightTarget( handle, pRenderable )" ) )
		<< "clientshadowmgr.cpp no longer has exactly three short-circuiting "
		   "IsFlashlightTarget call sites; the `||` is what keeps that function "
		   "from ever running, and BuildFlashlight's tail was deleted on the "
		   "strength of it";

	EXPECT_EQ( std::string::npos, strShadowCode.find( "m_hTargetEntity->" ) )
		<< "clientshadowmgr.cpp dereferences m_hTargetEntity again. That handle "
		   "answers a C_BaseEntity *, which is the entity reach shard T5 guarded "
		   "and the residue wave removed -- FirstMoveChild, GetModel and "
		   "GetClientRenderable were all reached through it";
	EXPECT_EQ( std::string::npos, strShadowCode.find( "FirstMoveChild()" ) )
		<< "clientshadowmgr.cpp walks the SDK move hierarchy again; "
		   "igameentity.h refuses GetMoveParent by name, so there is no managed "
		   "route for it";
	EXPECT_EQ( std::string::npos,
	           strShadowCode.find( "AddShadowToReceiver( handle, shadow.m_hTargetEntity" ) )
		<< "clientshadowmgr.cpp passes m_hTargetEntity to AddShadowToReceiver "
		   "again; IGameEntity * does not convert to IClientRenderable *";
}
