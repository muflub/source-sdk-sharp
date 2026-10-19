//========= SourceSharp — the leaf cluster's de-entitying, and why the 85-TU
//          cycle does not move for it =======================================//
//
// plan_entity.md wave 2b, lane `c7-leaf`: clientleafsystem, clientshadowmgr,
// detailobjectsystem, c_pixel_visibility, client_thinklist, studio_stats and
// toolframework_client. Two halves, and the second is the one nobody had
// written down.
//
// HALF 1 — THE RELOCATION IS A MOVE AND NOT A DELETION.
//
// plan_entity.md 3.9: the leaf system and the shadow manager "stay,
// de-entitied: they address renderables through IClientRenderable and handles
// resolved by the slot table, never through C_BaseEntity or
// ClientEntityList()." clientleafsystem.cpp's last three C_BaseEntity reaches
// were the bone-merge render-bounds special case -- Tracker 37433, a stunstick
// posed onto a combine soldier's hand -- and those moved WHOLE into
// shell_renderable_facts.cpp's two seam functions.
//
// A grep for `C_BaseEntity` in clientleafsystem.cpp goes to zero just as
// happily when the special case is DELETED as when it is ported, and deleting
// it is a real regression that renders normally: a bone-merged prop keeps its
// stale own-origin AABB, so it is frustum-culled at the edge of the screen and
// vanishes for a frame or two at a time. Nothing asserts, nothing crashes.
// So every scan below is paired with a fact that the behaviour still EXISTS at
// the far end and that the caller still consults it.
//
// HALF 2 — WHY THE SCC DOES NOT MOVE, MEASURED.
//
// CLIENT_CLUSTERS.md's one rule is p7-exit's: "de-entitying a TU off the entity
// list does NOT take it out of the cycle while it still references
// c_baseentity.o", 85 before and 85 after. This lane found a THIRD answer that
// is neither of the two the rule offers:
//
//   `client_base.vpc` compiled this project with `-include cbase.h`, and
//   cbase.h -> baseentity_shared.h -> c_baseentity.h -> particle_property.h
//   -> particles_new.h. CNewParticleEffect declares ~20 virtuals inline there,
//   so at -O0 EVERY TU that gets the PCH emits its own weak copy of the vtable
//   and leaves 21 out-of-line virtuals undefined -- symbols whose only definer
//   is particles_new.o, which is inside the cycle.
//
// `tools/c7-leaf-cycle` TOOK the numeric reading (70 of 70 PCH'd stock client
// TUs carry that edge; 0 do not). It is DELETED -- one of seven removal-set
// instruments retired in `the-close 11/N` -- and it needed a built tree in any
// case, so the facts here pin the CAUSE and are now the whole of the guard:
// the four include links, and the build setting (a PCH) that would turn them
// into an edge.
// Break any one of them and this file goes red, which is what stops the
// finding rotting into a stale paragraph in a handover. The numbers quoted
// throughout this file are that tool's last readings, and they are HISTORY --
// they are what the facts were calibrated against, not something re-measured
// on every run.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "ss_ci_skip.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

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
// The file with its comments taken out. 7b's recorded shape: a raw needle reads
// non-zero for a file whose CODE reads zero, because the prose explaining what
// went is allowed to spell the name. Every count below is of code.
//
// String literals are preserved so a "//" inside one is not read as a comment.
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

bool Contains( const std::string &strHaystack, const std::string &strNeedle )
{
	return strHaystack.find( strNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// The body of a function, by brace balance from its signature line.
//
// Needed rather than a whole-file scan because the two facts that matter about
// the leaf system's seam are about WHERE the call is: a call to
// SourceSharp_BoneMergeBoundsFromFollowedEntity that is not inside
// DefaultRenderBoundsWorldspace, or one whose result nothing tests, is a file
// that greps clean and has lost the behaviour.
//-----------------------------------------------------------------------------
std::string FunctionBody( const std::string &strFile, const std::string &strSignature )
{
	const std::string all = ReadFile( strFile );

	// SKIP FORWARD DECLARATIONS, and this is not a nicety: clientleafsystem.cpp
	// forward-declares CalcRenderableWorldSpaceAABB_Fast five lines above
	// DefaultRenderBoundsWorldspace, so "the first match, then the next {"
	// hands back the WRONG FUNCTION'S BODY -- and every fact in this file would
	// then have been asserted about DefaultRenderBoundsWorldspace twice while
	// reading as though it covered both. Caught by the two seam facts going red
	// on a tree that had both calls in it.
	size_t open = std::string::npos;
	for ( size_t start = all.find( strSignature );
	      start != std::string::npos;
	      start = all.find( strSignature, start + 1 ) )
	{
		const size_t brace = all.find( '{', start );
		const size_t semi = all.find( ';', start );
		if ( brace == std::string::npos )
			break;
		if ( semi != std::string::npos && semi < brace )
			continue;   // a declaration, not this definition
		open = brace;
		break;
	}
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < all.size(); i++ )
	{
		if ( all[ i ] == '{' ) depth++;
		else if ( all[ i ] == '}' )
		{
			depth--;
			if ( depth == 0 )
				return all.substr( open, i - open + 1 );
		}
	}
	return std::string();
}

// C7 cluster 12 L2: clientleafsystem.cpp left client.so. The leaf system's
// registry and decisions are SourceSharp.Client's; its native end is the
// forwarder class (libss_fwd_client) and its client.so home, which also holds
// the one bounds function other renderables still call.
const char *kStockLeafSystem = "game/client/clientleafsystem.cpp";
const char *kLeafSystem = "sourcesharp/game/client/shell_leaf_system_client.cpp";
const char *kLeafForwarder = "sourcesharp/fwd/fwd_leaf_system.cpp";
const char *kFacts = "sourcesharp/game/client/shell_renderable_facts.cpp";
const char *kFactsHeader = "sourcesharp/game/client/renderable_facts.h";

} // namespace

//=============================================================================
// PART 1 — THE LEAF SYSTEM IS DE-ENTITIED, AND THE BEHAVIOUR IS AT THE FAR END
//=============================================================================

//-----------------------------------------------------------------------------
// plan_entity.md 3.9's sentence about the leaf system, as a check, now about
// its native end: neither file names C_BaseEntity. The stock file's one
// C_BaseEntity sat inside `#ifdef DUMP_RENDERABLE_LEAFS`, defined nowhere; it
// left with the file.
//-----------------------------------------------------------------------------
TEST( LeafSystemDeEntitied, TheLeafSystemsNativeEndNamesNoEntity )
{
	const char *kFiles[] = { kLeafSystem, kLeafForwarder };
	for ( const char *pszFile : kFiles )
	{
		const std::string code = StripComments( ReadFile( pszFile ) );
		ASSERT_FALSE( code.empty() ) << pszFile << " read as nothing";
		EXPECT_EQ( 0, CountOccurrences( code, "C_BaseEntity" ) )
			<< pszFile << " names C_BaseEntity; the leaf system addresses "
			<< "renderables through IClientRenderable";
	}
}

TEST( LeafSystemDeEntitied, TheStockLeafSystemIsNotCompiled )
{
	// The eviction itself: the farm has no stock file, and the client's own
	// list compiles none -- proof-of-read first, so the absence is about the
	// client's list.
	EXPECT_TRUE( ReadFile( kStockLeafSystem ).empty() ) << kStockLeafSystem << " is back in the farm";
	const std::string list = cmake_graph::ListText( cmake_graph::kClient );
	ASSERT_TRUE( Contains( list, "shell_leaf_system_client.cpp" ) )
		<< cmake_graph::ListFile( cmake_graph::kClient ) << " was not read";
	EXPECT_FALSE( Contains( list, "clientleafsystem.cpp" ) )
		<< cmake_graph::ListFile( cmake_graph::kClient ) << " compiles clientleafsystem.cpp again";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kStockLeafSystem ) )
		<< "client_sourcesharp has a compile command for " << kStockLeafSystem;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLeafSystem ) )
		<< "the graph does not see the leaf system's client.so home; the absence above proves nothing";
}

//-----------------------------------------------------------------------------
// THE ANTI-DELETION HALF, REWRITTEN BY LANE 4c-render.
//
// READ THE ASSERTIONS, NOT THE HISTORY. There is no bone-merge seam any more.
//
// What this half used to assert was that Tracker 37433's special case had been
// MOVED to the shim rather than deleted -- a grep for C_BaseEntity in
// clientleafsystem.cpp goes to zero just as happily either way, and a deletion
// renders: a bone-merged prop keeps its stale own-origin AABB and is culled at
// the screen edge for a frame at a time, with no assert and no crash.
//
// THE MOVE HAPPENED AND THEN THE MOVED BODY WAS MEASURED. Both arms were
// `return false;` and nothing else, because the resolve they depended on
// (SDKEntityFromRenderable) walks the partition before it asks GetBaseEntity()
// and this link line has no SDK C_BaseEntity for it to answer with. So the
// special case had not applied to anything in any build that ever ran, and the
// two calls were two branches never taken. Lane 4c-render deleted the seam and
// both call sites.
//
// THE CLAIM THAT SURVIVES IS THE ONE THAT CAN STILL FAIL: each caller must
// still produce a box for every renderable, by its own general path, and must
// reach no entity to do it. A deletion that took the general path with the
// seam would leave an uninitialised AABB, which is the SAME rendering failure
// the old facts were about -- so the successor is not a weaker check, it is the
// same failure asserted at the only place that still has a subject.
//-----------------------------------------------------------------------------
TEST( LeafSystemDeEntitied, BothBoundsFunctionsStillProduceABoxByTheirOwnPath )
{
	const std::string dflt = StripComments(
		FunctionBody( kLeafSystem,
		              "void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable" ) );
	ASSERT_FALSE( dflt.empty() )
		<< "DefaultRenderBoundsWorldspace has no balanced body — the extractor "
		<< "is reading nothing, so this rule cannot fail";

	EXPECT_TRUE( Contains( dflt, "pRenderable->GetRenderBounds( mins, maxs )" ) )
		<< "DefaultRenderBoundsWorldspace no longer measures the renderable's "
		   "own bounds, so it fills nothing in and every renderable is placed "
		   "in the leaves with an uninitialised box.";
	EXPECT_TRUE( Contains( dflt, "TransformAABB( toWorld, mins, maxs, absMins, absMaxs )" ) )
		<< "the rotated case no longer builds a world-aligned box, so an "
		   "angled renderable gets its unrotated local bounds.";

	// CalcRenderableWorldSpaceAABB_Fast's successor: the managed registry
	// places a renderable by the box it answers GetRenderBoundsWorldspace with,
	// which the forwarder library's facts batch asks it for.
	const std::string facts = StripComments(
		FunctionBody( kLeafForwarder,
		              "void SSFwd_LeafSystem_Facts( void **ppRenderables" ) );
	ASSERT_FALSE( facts.empty() ) << "SSFwd_LeafSystem_Facts has no balanced body";
	EXPECT_TRUE( Contains( facts, "p->GetRenderBoundsWorldspace( mins, maxs )" ) )
		<< "the facts batch no longer asks a renderable for its own world-space "
		   "box, so the registry places it in the leaves with none.";
}

TEST( LeafSystemDeEntitied, NeitherBoundsFunctionReachesAnEntityOrADeletedSeam )
{
	// The absence half, asserted inside the two function bodies rather than
	// over the whole file, so that the record of WHY the special case went --
	// which names GetFollowedEntity and GetMoveParent in prose two lines above
	// each -- cannot satisfy it.
	const char *kFns[] = {
		"void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable",
	};
	const char *kGone[] = {
		"SourceSharp_BoneMergeBoundsFromFollowedEntity",
		"SourceSharp_BoneMergeBoundsFromMoveParent",
		"GetFollowedEntity()",
		"GetMoveParent()",
		"C_BaseEntity",
		"flBloatSize",
	};

	for ( size_t i = 0; i < sizeof( kFns ) / sizeof( kFns[ 0 ] ); i++ )
	{
		const std::string body = StripComments( FunctionBody( kLeafSystem, kFns[ i ] ) );
		ASSERT_FALSE( body.empty() ) << kFns[ i ] << ": no body";

		for ( size_t j = 0; j < sizeof( kGone ) / sizeof( kGone[ 0 ] ); j++ )
		{
			EXPECT_FALSE( Contains( body, kGone[ j ] ) )
				<< kFns[ i ] << " names " << kGone[ j ] << " again. The "
				<< "bone-merge parent lookup is an SDK C_BaseEntity ask and "
				<< "there is no such class on this link line, so this is "
				<< "either dead text or a read of something that is not there.";
		}
	}
}

TEST( LeafSystemDeEntitied, TheDeletedSeamIsGoneFromBOTHSidesAndTheResolveIsNot )
{
	// The pair that makes the deletion a deletion rather than a rename: the six
	// bodies are gone from the shim AND from its header, and the ONE thing that
	// file is still for is still there. Without the positive half every name
	// below is satisfied by an empty or unopenable file.
	const std::string factsCode = StripComments( ReadFile( kFacts ) );
	const std::string header = StripComments( ReadFile( kFactsHeader ) );
	ASSERT_FALSE( factsCode.empty() ) << kFacts << " read as nothing";
	ASSERT_FALSE( header.empty() ) << kFactsHeader << " read as nothing";

	EXPECT_TRUE( Contains( factsCode, "IClientRenderable *SourceSharp_RenderableFromHandle( const CBaseHandle &hEnt )" ) )
		<< kFacts << " no longer defines the handle->renderable resolve, which "
		   "is the one thing the file is for";
	EXPECT_TRUE( Contains( header, "IClientRenderable *SourceSharp_RenderableFromHandle" ) )
		<< kFactsHeader << " no longer declares the resolve";

	const char *kDeleted[] = {
		"SourceSharp_BoneMergeBoundsFromFollowedEntity",
		"SourceSharp_BoneMergeBoundsFromMoveParent",
		"SourceSharp_ShadowRenderingClipPlane",
		"SourceSharp_ShadowSourceIsNpcOrPlayer",
		"SourceSharp_ShadowBoneSetupQueueAdd",
		"SourceSharp_ShadowBoneSetupQueueRun",
		"SourceSharp_ShadowBoneSetupQueueReset",
		"SourceSharp_ViewerFovDistanceAdjustFactor",
	};
	for ( size_t i = 0; i < sizeof( kDeleted ) / sizeof( kDeleted[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Contains( factsCode, kDeleted[ i ] ) )
			<< kFacts << " defines " << kDeleted[ i ] << " again. Every one of "
			<< "these answered a constant on this link line; a body here is a "
			<< "body with no caller unless a call site came back too.";
		EXPECT_FALSE( Contains( header, kDeleted[ i ] ) )
			<< kFactsHeader << " declares " << kDeleted[ i ] << " again";
	}
}

TEST( LeafSystemDeEntitied, TheLeafSystemNoLongerNeedsTheFactsHeaderAtAll )
{
	// The include went with the last call. It is asserted because an include
	// left behind is how a file keeps a dependency edge it no longer uses --
	// which is the exact thing the leaf-system cycle work below is about.
	const char *kFiles[] = { kLeafSystem, kLeafForwarder };
	for ( const char *pszFile : kFiles )
	{
		const std::string code = StripComments( ReadFile( pszFile ) );
		ASSERT_FALSE( code.empty() ) << pszFile << " read as nothing";

		EXPECT_FALSE( Contains( code, "#include \"renderable_facts.h\"" ) )
			<< pszFile << " includes renderable_facts.h with nothing to ask it";
		EXPECT_EQ( 0, CountOccurrences( code, "SourceSharp_" ) )
			<< pszFile << " calls into the shim again; the stock leaf system asked "
			<< "the shim for exactly two things and both were deleted";
	}
}


//=============================================================================
// PART 1b — THE SHADOW MANAGER, SAME MOVE, SAME ANTI-DELETION ARGUMENT
//=============================================================================

namespace
{
const char *kShadowMgr = "game/client/clientshadowmgr.cpp";
}

TEST( ShadowMgrDeEntitied, ItNamesCBaseEntityOnlyInTheFlashlightTargetGuards )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	// plan_entity.md 3.9 gives this file the leaf system's sentence. Three of
	// its five guarded regions moved to the shim; the two that stayed were the
	// flashlight-target walk and IsFlashlightTarget, whose bodies call
	// CClientShadowMgr's own members and which cpp_alias_t5_test's
	// FlashlightTargetVacuity already measures as unreachable in this project.
	//
	// REWRITTEN BY THE `residue` WAVE (74b071771..a46e8d645), AND THE NUMBER
	// IS NOW ZERO RATHER THAN TWO.
	//
	// The two walks this fact counted were at game/client/clientshadowmgr.cpp
	// :2738-2767 and :4252-4266 at 74b071771, and BOTH sat inside
	// `#ifndef SS_CBASEENTITY_ALIAS`. The one configuration this project builds
	// has SS_CBASEENTITY_ALIAS and SOURCESHARP both DEFINED, so neither arm was
	// ever compiled: the residue wave deleted 5,282 lines of exactly that kind
	// of dead text out of 44 stock TUs and the compiler's input did not move
	// (588 objects, identical DEFINED and UNDEFINED symbol sets). Counting them
	// was counting text the flip had already removed.
	//
	// So the successor property is the STRONGER one -- this file's CODE now
	// names no SDK entity type at all -- and it is written as an absence, which
	// is exactly the shape that passes over an empty or unopenable file. Every
	// zero below is therefore paired with a positive anchor:
	//
	//   * the file still IS the shadow manager (the four renderable-typed
	//     FirstShadowChild() walks and the three NextShadowPeer() steps that
	//     replaced the move hierarchy, and AddShadowToReceiver's definition);
	//   * docs/absence-client.md row 15 still carries the paragraph that says
	//     WHY the walks were never retyped, so a later deletion of the record
	//     is caught too. It was a comment in the fork's copy of the file; it
	//     moved to the ledger so the comment-free patched upstream (make
	//     sdk-upstream) keeps it.
	const std::string code = StripComments( ReadFile( kShadowMgr ) );
	const std::string doc = ReadFile( "../docs/absence-client.md" );
	ASSERT_FALSE( doc.empty() ) << "docs/absence-client.md read as nothing";
	const size_t posRow = doc.find( "## 15. The flashlight-target tail" );
	ASSERT_NE( std::string::npos, posRow ) << "the absence ledger lost row 15";
	const std::string raw = doc.substr( posRow, doc.find( "\n## ", posRow + 1 ) - posRow );
	ASSERT_FALSE( code.empty() ) << kShadowMgr << " read as nothing";

	// ---- the positive half: this is still the shadow manager -----------------
	EXPECT_EQ( 4, CountOccurrences( code, "FirstShadowChild()" ) )
		<< kShadowMgr << " no longer walks the renderable child hierarchy; the "
		<< "zeroes below would then be satisfied by deleting the file's work "
		<< "rather than by the flip having already removed dead text";
	EXPECT_EQ( 3, CountOccurrences( code, "NextShadowPeer()" ) )
		<< kShadowMgr << "'s renderable-typed peer steps are not all there";
	EXPECT_TRUE( Contains( code, "void CClientShadowMgr::AddShadowToReceiver(" ) )
		<< kShadowMgr << " no longer defines AddShadowToReceiver";

	// ---- the absence half: no entity reach survives in code ------------------
	EXPECT_EQ( 0, CountOccurrences( code, "C_BaseEntity" ) )
		<< kShadowMgr << " names C_BaseEntity in CODE again. The two "
		<< "flashlight-target walks that used to account for this were dead "
		<< "`#ifndef SS_CBASEENTITY_ALIAS` text and the `residue` wave deleted "
		<< "them; a new one is a new entity reach, not the old one coming back";
	EXPECT_EQ( 0, CountOccurrences( code, "C_BaseAnimating" ) )
		<< kShadowMgr << " still names C_BaseAnimating; the bone-setup queue "
		<< "and its two CUtlVector< C_BaseAnimating * > statics moved";
	EXPECT_EQ( 0, CountOccurrences( code, "FirstMoveChild()" ) )
		<< kShadowMgr << " walks the MOVE hierarchy again. That is the SDK "
		<< "entity hierarchy read off C_BaseEntity, and igameentity.h refuses "
		<< "GetMoveParent by name -- there is no managed route for it";

	// ---- and the record of why the walks were never retyped ------------------
	EXPECT_TRUE( Contains( raw, "igameentity.h says so for GetMoveParent by name" ) )
		<< "docs/absence-client.md row 15 has lost the paragraph that says why the flashlight "
		<< "walks were deleted rather than ported. Without it the zeroes above "
		<< "read as a file that was simply tidied";
}

//-----------------------------------------------------------------------------
// THE SHADOW MANAGER'S THREE RELOCATED REGIONS, REWRITTEN BY LANE 4c-render.
//
// READ THE ASSERTIONS, NOT THE HISTORY. All three are gone from both sides.
//
// The bone-setup queue, the rendering clip plane and the NPC/player static-prop
// gate were relocated out of this file by wave 2b because each is something the
// file DID with an entity rather than a place it looked one up -- and the
// queue's ELEMENT TYPE was an SDK entity class, which no respelling can fix.
// Measured at the far end, all three were constants: `false`, `false`, and an
// Add() with an empty body whose Run() therefore handed ParallelProcess two
// zero-length arrays twice a frame. Each depended on a resolve that walks the
// partition before it asks GetBaseEntity(), and this link line has no SDK
// C_BaseEntity. Lane 4c-render deleted all three and their five call sites.
//
// EACH SUCCESSOR IS AN ABSENCE PAIRED WITH THE POSITIVE ANCHOR IT WOULD
// OTHERWISE BE SATISFIED BY DELETING. A file that no longer poses shadows, no
// longer clips flashlights and no longer adds anything to a static prop passes
// every "does not name X" on its own; so what is asserted is that the
// SURROUNDING WORK is still there and that only the entity reach went.
//-----------------------------------------------------------------------------
TEST( ShadowMgrDeEntitied, ThePosePassIsGoneAndTheThreadedBranchItBracketedIsNot )
{
	const std::string code = StripComments( ReadFile( kShadowMgr ) );
	ASSERT_FALSE( code.empty() ) << kShadowMgr << " read as nothing";

	// ---- the positive half: the threaded setup pass still exists ------------
	EXPECT_TRUE( Contains( code, "m_bThreaded" ) )
		<< kShadowMgr << " no longer has the threaded setup branch at all, so "
		<< "the absences below would be satisfied by deleting the work rather "
		<< "than by deleting an empty queue";
	EXPECT_TRUE( Contains( code, "if ( BuildSetupListForRenderToTextureShadow( info.m_hShadow, info.m_flArea ) )" ) )
		<< kShadowMgr << " no longer builds the setup list inside that branch";
	EXPECT_TRUE( Contains( code, "bDrewTexture = true;" ) )
		<< "BuildSetupShadowHierarchy no longer records that it had a model "
		<< "shadow to draw; that flag is the branch's real output and it never "
		<< "depended on the queue";

	// ---- the absence half: nothing queues, drains or poses an entity --------
	const char *kGone[] = {
		"SourceSharp_ShadowBoneSetupQueueReset",
		"SourceSharp_ShadowBoneSetupQueueAdd",
		"SourceSharp_ShadowBoneSetupQueueRun",
		"C_BaseAnimating",
		"SetupBones(",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0, CountOccurrences( code, kGone[ i ] ) )
			<< kShadowMgr << " names " << kGone[ i ] << " in CODE again. The "
			<< "queue's element type was an SDK entity class and there is no "
			<< "such class here; a queue that fills again poses nothing and "
			<< "costs a traversal.";
	}
}

TEST( ShadowMgrDeEntitied, TheShadowIsClippedByTheThreePlanesComputeExtraClipPlanesMakes )
{
	// The fourth plane came off the renderable's own m_fRenderingClipPlane, two
	// FIELDS on C_BaseEntity. It is gone; the three the function computes are
	// the whole answer and must still be added, or the shadow stops being
	// bounded at all -- which renders, and looks like a lighting bug.
	const std::string body = StripComments(
		FunctionBody( kShadowMgr, "void CClientShadowMgr::ComputeExtraClipPlanes( IClientRenderable* pRenderable, " ) );
	ASSERT_FALSE( body.empty() ) << kShadowMgr << ": no ComputeExtraClipPlanes body";

	EXPECT_TRUE( Contains( body, "AddExtraClipPlane( handle, normal, dist )" ) )
		<< "ComputeExtraClipPlanes no longer adds the planes it computes, so the "
		<< "shadow is not clipped by its own bounding box";
	EXPECT_FALSE( Contains( body, "SourceSharp_ShadowRenderingClipPlane" ) )
		<< "the deleted clip-plane seam is back; it answered false on every "
		<< "path because C_StandaloneEntity declares no clip plane at all";
	EXPECT_FALSE( Contains( body, "flClipDist" ) )
		<< "ComputeExtraClipPlanes reads a clip distance from somewhere again";
}

TEST( ShadowMgrDeEntitied, NothingIsAddedToAStaticPropExceptByTheFlashlightPath )
{
	// The render-to-texture branch's add was gated on "an NPC or player casts
	// this", read off the caster's FL_* flags -- SDK movement state that never
	// crosses (igameentity.h states the verdict by name). The gate answered
	// false always, so the add never happened, so both went. The FLASHLIGHT
	// branch's add is a different path with a different condition and is the
	// positive anchor: if it went too, this file has stopped lighting props.
	const std::string body = StripComments(
		FunctionBody( kShadowMgr, "void CClientShadowMgr::AddShadowToReceiver( ClientShadowHandle_t handle," ) );
	ASSERT_FALSE( body.empty() ) << kShadowMgr << ": no AddShadowToReceiver body";

	EXPECT_EQ( 1, CountOccurrences( body, "AddShadowToStaticProp(" ) )
		<< "AddShadowToReceiver adds to a static prop on a number of paths "
		<< "other than one. The flashlight path is the only one left: the "
		<< "render-to-texture path's add was gated on an entity's FL_NPC / "
		<< "FL_CLIENT, which never answered true here.";
	EXPECT_TRUE( Contains( body, "IsFlashlightTarget( handle, pRenderable )" ) )
		<< "the flashlight path's own condition is gone, so the single add "
		<< "counted above may be the wrong one";
	EXPECT_FALSE( Contains( body, "SourceSharp_ShadowSourceIsNpcOrPlayer" ) )
		<< "the deleted NPC/player gate is back";
}


//=============================================================================
// PART 2 — WHY THE CYCLE DOES NOT MOVE
//=============================================================================

//-----------------------------------------------------------------------------
// THE PCH CHAIN. Four links, each asserted separately so a red says which one
// went. The numeric reading used to come off a built tree; the tool that took
// it is deleted, so this IS the guard rather than a companion to one.
//-----------------------------------------------------------------------------
TEST( LeafCyclePch, CbaseReachesParticlesNewInFourIncludes )
{
	struct Link_t { const char *pszFile; const char *pszInclude; };
	const Link_t kChain[] = {
		{ "game/client/cbase.h", "baseentity_shared.h" },
		{ "game/shared/baseentity_shared.h", "c_baseentity.h" },
		{ "game/client/c_baseentity.h", "particle_property.h" },
		{ "game/shared/particle_property.h", "particles_new.h" },
	};
	for ( size_t i = 0; i < sizeof( kChain ) / sizeof( kChain[ 0 ] ); i++ )
	{
		const std::string text = ReadFile( kChain[ i ].pszFile );
		ASSERT_FALSE( text.empty() ) << kChain[ i ].pszFile << " read as nothing";
		EXPECT_TRUE( Contains( text, kChain[ i ].pszInclude ) )
			<< kChain[ i ].pszFile << " no longer includes "
			<< kChain[ i ].pszInclude
			<< " — the chain this lane's finding rests on is broken, so "
			<< "re-take a reading on a built tree before quoting it";
	}
}

TEST( LeafCyclePch, NoPrecompiledHeaderDirectiveExistsInThisProjectAnyMore )
{
	// This case used to assert that every TU in THIS project was compiled with
	// `-include cbase.h`, and that a per-file "Not Using Precompiled Headers"
	// block was what took one off it -- c_pixel_visibility.cpp being the worked
	// example, the one file of the seven with no edge to particles_new.o.
	//
	// plan_shim.md 0b deleted the mechanism from client_csharp.vpc, so the
	// reading this lane took is HISTORY rather than a live property, and what
	// is pinned now is that it cannot come back without saying so.
	//
	// RETIRED (Phase 6b): three assertions that Valve's stock client_base.vpc
	// still carried `$Create/UsePrecompiledHeader`, `$Create/UsePCHThroughFile`
	// and "cbase.h" -- kept as the record of the shape the reading was taken
	// against. The stock vpcs describe Valve's stock build, which this repo
	// never ran and which has left the tree; the shape is described above.
	//
	// The build this repo DOES run is client_sourcesharp
	// (src/client/native/CMakeLists.txt), read through cmake_graph.h.
	const char *const kPixelVis = "game/client/c_pixel_visibility.cpp";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kPixelVis ) )
		<< "c_pixel_visibility.cpp has left the build, so the cluster this "
		<< "lane measured is not the one on disk";

	EXPECT_FALSE( Contains( cmake_graph::ListText( cmake_graph::kClient ), "target_precompile_headers" ) )
		<< cmake_graph::ListFile( cmake_graph::kClient ) << " declares a "
		<< "precompiled header again. Every TU in the project is then compiled "
		<< "with cbase.h whatever its own include list says, and the chain above "
		<< "becomes a link EDGE again for all of them at once.";

	// What the compiler is actually handed: CMake's PCH arrives as a forced
	// `-include .../cmake_pch.hxx`; a forced include of cbase.h is the stock
	// shape under another name.
	const std::string cmd = cmake_graph::CompileCommand( cmake_graph::kClient, kPixelVis );
	EXPECT_FALSE( Contains( cmd, "cmake_pch" ) )
		<< kPixelVis << " is compiled with a PCH:\n" << cmd;
	EXPECT_FALSE( Contains( cmd, " -include" ) )
		<< kPixelVis << " is compiled with a forced include, and there is "
		<< "nothing left for a file to opt out of:\n" << cmd;
}

TEST( LeafCyclePch, CNewParticleEffectIsTheHeaderShapeTheReadingWasTakenAgainst )
{
	// The third ingredient. The 21 undefined symbols are CNewParticleEffect's
	// virtuals, left over from a vtable the compiler emits weakly in each TU
	// that gets the header -- observed directly:
	//
	//   nm    .../client_thinklist.o | c++filt | grep CNewParticleEffect
	//     -> `W CNewParticleEffect::IsTransparent()` and friends, DEFINED weak
	//   nm -u .../client_thinklist.o | c++filt | grep -c CNewParticleEffect
	//     -> 21, whose only definer is particles_new.o
	//
	// c7-leaf wrote, correctly, that it had NOT established the ABI rule: "the
	// class's first virtual is declared and not defined, which under the plain
	// key-function rule would put the vtable in particles_new.o alone."
	//
	// Lane c7-pch established it, and c7-leaf was right about the rule and
	// wrong about the header. `bool ShouldDraw( void );` IS the first virtual
	// this class declares without a body -- but particles_new.h then DEFINED it
	// `inline` further down the same file, and under GCC's key-function rule
	// that is exactly the case where the class ends up with no key function and
	// the vtable goes weak in every TU that sees the declaration. Moving that
	// one body into particles_new.cpp took the client's largest SCC from 85 TUs
	// to 78 and `nm -u recvproxy.o | grep -c CNewParticleEffect` from 21 to 0.
	// The next fact pins that; this one still pins the header SHAPE the
	// 21-symbol reading was taken against.
	//
	// Counted on the CODE, not on the file: this header's explanatory comments
	// now spell "virtual" themselves, and prose counting as code is this port's
	// standing defect.
	const std::string header = StripComments( ReadFile( "game/client/particles_new.h" ) );
	ASSERT_FALSE( header.empty() ) << "particles_new.h read as nothing";
	EXPECT_TRUE( Contains( header, "class CNewParticleEffect" ) )
		<< "CNewParticleEffect is no longer declared here";
	// 14, not c7-leaf's 15. Its reading was taken on the raw file, and the
	// fifteenth `virtual` is at particles_new.h:117, inside a commented-out
	// declaration of NotifyDestroyParticle. Prose counting as code, in the very
	// file whose header says every count below is of code.
	EXPECT_EQ( 14, CountOccurrences( header, "virtual" ) )
		<< "particles_new.h's virtual count moved; the 21-symbol edge this "
		<< "lane reports was measured against 14 in code (the retired "
		<< "c7-leaf-cycle read 15 on the raw file, counting a commented-out "
		<< "declaration), so re-take that reading before quoting it";
	EXPECT_TRUE( Contains( header, "virtual bool				ShouldSimulate() const { return m_bSimulate; }" ) )
		<< "the inline virtual bodies are gone from the header; that is the "
		<< "single cheapest change to the client's cycle anyone has found, so "
		<< "re-measure before assuming it changed nothing";
}

//-----------------------------------------------------------------------------
// THE KEY FUNCTION. This is the change the fact above calls "the single
// cheapest change to the client's cycle anyone has found", and it is one
// function body moved between two files.
//
// GCC emits a class's vtable, typeinfo and thunks in the single object that
// defines the class's KEY FUNCTION -- the first non-pure virtual the class
// declares without a body. Give that function an inline body and the class has
// no key function: every TU that sees the declaration emits its own weak
// vtable, which references the class's out-of-line members and leaves them
// undefined. That is where the 21 CNewParticleEffect symbols, and
// clientleafsystem.o's DefaultRenderBoundsWorldspace behind
// CDefaultClientRenderable::GetRenderBoundsWorldspace, came from in 73 of the
// client's 190 stock objects -- none of which calls a particle function.
//
// Measured on the built tree by the now-retired c7-leaf-cycle, one change
// apart -- these two rows are the recorded reading, not a live one:
//     inline in the header      largest SCC 85 TUs, 104319 lines, 881 edges
//     out of line in the .cpp   largest SCC 78 TUs, 100702 lines, 746 edges
//
// A fact that only looked in the .cpp would pass with the body in BOTH places,
// which still leaves the class without a key function, so both halves are
// asserted -- on comment-stripped code, because the prose in both files now
// names the function repeatedly.
//-----------------------------------------------------------------------------
TEST( LeafCyclePch, ShouldDrawIsTheKeyFunctionAndStaysOutOfLine )
{
	// C7 PARTICLES PHASE 4 TOOK THE DEFINING OBJECT. particles_new.cpp left
	// client.so: nothing constructs a CNewParticleEffect any more (the
	// new-style effects are CShellNewParticleEffect, shell_new_particles.h), so
	// no object defines its key function or its vtable, and none may reference
	// them. The header is still read (c_baseentity.h -> particle_property.h ->
	// particles_new.h), so its half of the fact stands as it was: ShouldDraw
	// stays a bodyless declaration, and an inline body there would put a weak
	// vtable -- and the class's undefined members, defined nowhere now -- into
	// every TU that sees it.
	const std::string header = StripComments( ReadFile( "game/client/particles_new.h" ) );
	ASSERT_FALSE( header.empty() ) << "particles_new.h read as nothing";

	// Still DECLARED in the class body, and still without a body there.
	EXPECT_TRUE( Contains( header, "bool ShouldDraw( void );" ) )
		<< "CNewParticleEffect no longer declares ShouldDraw as a bodyless "
		<< "virtual, so it is no longer the key function and the vtable is "
		<< "free to go weak again -- re-measure on a built tree before "
		<< "assuming the cycle is where this file says it is";

	// And NOT defined in the header, inline or otherwise.
	EXPECT_FALSE( Contains( header, "CNewParticleEffect::ShouldDraw" ) )
		<< "particles_new.h defines the key function again; that is the change "
		<< "that put 22 undefined symbols into 73 client objects and takes the "
		<< "client's largest SCC from 78 TUs back to 85";

	// And the object that defined it is gone from the client's sources, with
	// its successor in its place.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/particles_new.cpp" ) )
		<< "particles_new.cpp is compiled into client.so again";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_new_particles_client.cpp" ) )
		<< "the new-style effects' shell is not compiled into client.so";
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. THE INSTRUMENT IS GONE, AND THIS FACT IS
// WHAT STANDS IN ITS PLACE.
//
// tools/c7-leaf-cycle was deleted in `the-close 11/N` with six other
// removal-set instruments. It needed a BUILT TREE, so it was never run by any
// gate; what it produced was the two recorded readings quoted above, and the
// argument it carried is written out in this file's own banner.
//
// So the claim is inverted and kept: the tool is absent, and the property its
// correlation was about -- CNewParticleEffect having a key function, in exactly
// one object -- is pinned HERE, as text, on every run. That is strictly more
// often than a tool nobody could run without a build.
//-----------------------------------------------------------------------------
TEST( LeafCyclePch, TheInstrumentIsPresentAndNamesTheThingItMeasures )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	std::ifstream tool( ( std::string( pszRoot ) + "/../tools/c7-leaf-cycle" ).c_str() );
	EXPECT_FALSE( tool.is_open() )
		<< "tools/c7-leaf-cycle is back. It was retired because it needs a "
		   "built tree and no gate could run it; if it is wanted again, the "
		   "facts in this file are the thing to keep it honest against, not "
		   "the other way round.";

	// THE LIVE SUCCESSOR, and it is the reading that mattered: the key function
	// is declared bodyless in the header and defined in at most ONE object.
	// Since C7 particles phase 4 that is none: particles_new.cpp left client.so
	// and nothing constructs the class (ShouldDrawIsTheKeyFunctionAndStays-
	// OutOfLine above). The header half stands.
	const std::string header = StripComments( ReadFile( "game/client/particles_new.h" ) );
	ASSERT_FALSE( header.empty() ) << "particles_new.h read as nothing";

	EXPECT_EQ( 0, CountOccurrences( header, "CNewParticleEffect::ShouldDraw" ) )
		<< "the key function has a body in the header again, so every TU that "
		   "sees the declaration emits its own weak vtable and the client's "
		   "largest SCC goes from 78 TUs back to 85.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/particles_new.cpp" ) )
		<< "particles_new.cpp is compiled into client.so again; it defined the "
		   "key function, so with it back there is again one object to check.";
}
