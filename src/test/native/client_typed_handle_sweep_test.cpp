//========= SourceSharp — typed handles out of the CLIENT shim (5d) ==========//
//
// plan_entity.md Phase 5 step 5d: 1e's gate, client side. 1e's ledger is
// sourcesharp/test/typed_handle_sweep_test.cpp and this is its twin; the
// argument for the instrument is made there and is not repeated. What IS
// repeated is the matcher, and deliberately: the two files must count the same
// thing as tools/entity-census-patterns or the two gates stop meaning the same
// sentence, and a shared helper header for eleven lines of character loop would
// hide that they agree rather than show it. The matcher's own facts below are
// what keep the copy honest.
//
// WHAT IS DIFFERENT ON THIS SIDE, and it is the reason 5d is a step and not a
// footnote to 1e:
//
//   ON THE SERVER a typed handle resolves through g_pEntityList -- the table
//   this plan DELETES. A typed handle there is a read of something that is
//   going away, and the sweep is largely bookkeeping ahead of that.
//
//   ON THE CLIENT, since step 5a, CHandle<T>::Get() resolves through
//   ClientEntityList(), and ClientEntityList() IS the slot table
//   (CSourceSharpClientEntityList in shell_cliententitylist.cpp). Its two
//   primitives answer for BOTH kinds of occupant: a C_StandaloneEntity out of
//   §3.3's table, and a stock client entity out of the port seam that is left
//   of CClientEntityList -- 5a measured that seam answering 372,026 handle and
//   111,011 index lookups inside ONE map load, so it is not a theoretical arm.
//
//   So a typed handle on this side does not fail to resolve. It SUCCEEDS, to
//   whichever of the two kinds the entry holds, and then applies
//   `return (T*)CBaseHandle::Get();` to the answer. A live pointer of the wrong
//   class comes back at an address that even looks right, and what is wrong is
//   every vtable slot past IHandleEntity. beam_endpoint.h (gone with the C2
//   beam port) recorded what that looked like in a running client: behaviour ledger row C8, a client killed
//   outright at the first virtual call in ComputeBeamEntPosition.
//
// THREE KINDS OF FACT, and the first is the only one that cannot be evaded:
//
//   1. THE DIRECTORY WALK. Every .cpp under sourcesharp/game/client is read off
//      the FILESYSTEM and required to spell no typed handle. A ledger of file
//      names could be shortened; a directory cannot. sourcesharp/game/shared is
//      1e's walk and is not re-walked here -- those TUs compile into BOTH DLLs
//      and are counted in both census columns, so a second walk would be a
//      second voice saying one thing.
//   2. THE NAMED SITES. What each retyped site says now, one fact each, so a
//      revert is a red test naming the file rather than a number that moved.
//   3. THE COUNT THIS STEP STARTED FROM, recorded so the sweep's size is a fact
//      and not a memory.
//
// tools/entity-census measures the same property over the COMPILED client TU
// list and diffs it against docs/entity-census.txt. This is stricter on
// purpose: it covers client shim TUs the build does not compile today, which is
// where a typed handle would sit unnoticed until a vpc line brought it in.
//
//=============================================================================

#include <dirent.h>

#include <algorithm>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// The tree, from wherever the leg was started. "." first, because
// src/buildcpptests runs its legs with src/ as the working directory.
//-----------------------------------------------------------------------------
std::string SrcRoot()
{
	static const char *const kCandidates[] = { ".", "src", "../src", "../../src" };

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		std::string probe = std::string( kCandidates[i] )
		                  + "/sourcesharp/game/client/shell_standalone_client.cpp";
		std::ifstream in( probe.c_str() );
		if ( in.good() )
			return std::string( kCandidates[i] );
	}

	return ".";
}

std::string Read( const std::string &strRel )
{
	std::ifstream in( ( SrcRoot() + "/" + strRel ).c_str() );
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

bool Has( const std::string &strHay, const char *pszNeedle )
{
	return strHay.find( pszNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// PROSE IS NOT CODE, and this file learned that the hard way: the ABSENCE
// assertions below are about what a file HOLDS, and shell_rope_render.cpp's own
// comments explain, at length, what it used to hold and why it no longer does.
// A raw scan reads the explanation as the violation -- the same hazard
// tools/entity-gates carries a comment-skip for and tools/entity-census had to
// grow one for, and the same reason rope_verdict_test.cpp strips before it
// scans.
//
// A PRESENCE assertion does NOT need this and does not get it: a needle found
// only in a comment is a different failure (the code moved, the comment stayed)
// and stripping would hide it. So this is used exactly where an absence is
// being claimed.
//
// FLOOR: a /* */ block spanning lines is tracked; a // inside a string literal
// is not. Neither shape occurs in the files scanned here, and the count of
// stripped bytes is asserted below so a stripper that silently ate everything
// -- which would make every absence pass -- is a red test.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[i] == '*' && strIn[i + 1] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}

		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}

		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '/' )
		{
			while ( i < strIn.size() && strIn[i] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}

		out += strIn[i];
	}

	return out;
}

// How MANY times. An absence check cannot tell "one site is there" from "two
// are", and the rope executor's one strip entry is a count.
int CountOf( const std::string &strHay, const char *pszNeedle )
{
	const size_t nLen = strlen( pszNeedle );
	if ( nLen == 0 )
		return 0;

	int n = 0;
	for ( size_t at = strHay.find( pszNeedle ); at != std::string::npos;
	      at = strHay.find( pszNeedle, at + nLen ) )
	{
		n++;
	}
	return n;
}

//-----------------------------------------------------------------------------
// THE CENSUS PATTERN, IMPLEMENTED RATHER THAN APPROXIMATED.
//
// tools/entity-census-patterns spells this family
//
//     EHANDLE   \b(EHANDLE|CHandle\s*<)
//
// and the two halves are not interchangeable. `EHANDLE` needs a word boundary
// BEFORE it and none after, which is why INVALID_EHANDLE_INDEX does not count
// (the underscore is a word character, so there is no boundary) while EHANDLE_H
// does. `CHandle` needs a following `<`, optionally spaced, because `CHandle`
// on its own is the class NAME and naming it in prose is not holding one.
//-----------------------------------------------------------------------------
bool IsWordChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
	    || ( c >= '0' && c <= '9' ) || c == '_';
}

int TypedHandleSpellings( const std::string &src )
{
	int nHits = 0;

	for ( size_t at = 0; at < src.size(); at++ )
	{
		if ( at > 0 && IsWordChar( src[at - 1] ) )
			continue;

		if ( src.compare( at, 7, "EHANDLE" ) == 0 )
		{
			nHits++;
			continue;
		}

		if ( src.compare( at, 7, "CHandle" ) == 0 )
		{
			size_t after = at + 7;
			while ( after < src.size() && ( src[after] == ' ' || src[after] == '\t' ) )
				after++;

			if ( after < src.size() && src[after] == '<' )
				nHits++;
		}
	}

	return nHits;
}

//-----------------------------------------------------------------------------
// Every .cpp in the client shim directory, off the filesystem.
//-----------------------------------------------------------------------------
std::vector< std::string > SourcesIn( const char *pszDirRel )
{
	std::vector< std::string > out;

	const std::string strDir = SrcRoot() + "/" + pszDirRel;
	DIR *pDir = opendir( strDir.c_str() );
	if ( !pDir )
		return out;

	while ( struct dirent *pEntry = readdir( pDir ) )
	{
		const std::string name = pEntry->d_name;
		if ( name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0 )
			out.push_back( std::string( pszDirRel ) + "/" + name );
	}

	closedir( pDir );
	std::sort( out.begin(), out.end() );
	return out;
}

const char *const kClientDir  = "sourcesharp/game/client";

const char *const kRope       = "sourcesharp/game/client/shell_rope_render.cpp";
const char *const kClientDll  = "sourcesharp/game/client/shell_clientdll.cpp";
const char *const kShell      = "sourcesharp/game/client/shell_standalone_client.cpp";
const char *const kShellH     = "sourcesharp/game/client/shell_standalone_client.h";

} // namespace

//=============================================================================
// 1. THE SWEEP, OVER THE DIRECTORY RATHER THAN OVER A LIST.
//=============================================================================

TEST( ClientTypedHandleSweep, NoClientShimSourceSpellsATypedEntityHandle )
{
	const std::vector< std::string > files = SourcesIn( kClientDir );

	ASSERT_FALSE( files.empty() ) << "found no sources under " << kClientDir
		<< " -- the walk is looking in the wrong place and would pass on an "
		   "empty directory";

	for ( size_t i = 0; i < files.size(); i++ )
	{
		// STRIPPED, WHICH IS THIS FILE'S OWN RULE APPLIED TO THE ONE PLACE IT
		// WAS NOT. The paragraph above StripComments says it in terms: "PROSE
		// IS NOT CODE ... A PRESENCE assertion does NOT need this and does not
		// get it ... So this is used exactly where an ABSENCE is being
		// claimed." This is an absence assertion and it was reading raw.
		//
		// IT BIT: lane `f-list` added a paragraph to shell_cliententitylist.cpp
		// explaining that CBaseHandle::Get() is what every entity handle on the
		// client resolves through -- a sentence that cannot be written without
		// naming the typed spellings -- and the scan read the explanation as
		// the violation. Exactly the hazard the paragraph above describes,
		// arriving in the assertion it had not been applied to.
		//
		// STRIPPING CANNOT MASK A REAL ONE: a typed handle in CODE survives the
		// stripper, and the mutation that puts one back is red. The stripper
		// has its own guard below -- the count of stripped bytes is asserted,
		// so a stripper that silently ate everything is a red test rather than
		// a green sweep.
		EXPECT_EQ( 0, TypedHandleSpellings( StripComments( Read( files[i] ) ) ) )
			<< files[i] << " spells a typed entity handle. plan_entity.md 5d: "
			   "the client shim holds CBaseHandle and resolves at the use site "
			   "(SourceSharp_ClientShellFromHandle / SourceSharp_ClientShellFromIndex "
			   "for a shell, SourceSharp_ClientUnknownFromHandle + "
			   "SDKEntityFromUnknown for a stock client entity).";
	}
}

//-----------------------------------------------------------------------------
// THE WALK IS ACTUALLY WALKING THE CLIENT.
//
// SourcesIn() returns an empty vector for a directory it cannot open, and the
// ASSERT above catches exactly that -- but only if the directory it names is
// the one the step is about. This pins the two files the walk MUST be seeing,
// so a walk repointed at some other directory (or at the server's, where 1e
// already swept) is a red test rather than a green one over the wrong tree.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheWalkSeesTheFilesTheStepRetyped )
{
	const std::vector< std::string > files = SourcesIn( kClientDir );

	EXPECT_NE( files.end(), std::find( files.begin(), files.end(), std::string( kRope ) ) )
		<< "the walk does not include shell_rope_render.cpp, which held three "
		   "of this step's four typed handles.";

	EXPECT_NE( files.end(), std::find( files.begin(), files.end(), std::string( kClientDll ) ) )
		<< "the walk does not include shell_clientdll.cpp.";
}

//-----------------------------------------------------------------------------
// THE MATCHER ITSELF CAN FAIL, and this is what says so.
//
// Every fact above is an EXPECT_EQ against zero, and a matcher that answered
// zero for everything would make all of them pass on a tree that had not been
// swept at all -- the "check that cannot fail" shape. So the matcher is given
// text it must count and text it must not.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheMatcherCountsWhatTheCensusPatternCounts )
{
	EXPECT_EQ( 1, TypedHandleSpellings( "EHANDLE m_hEnt;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CUtlVector< EHANDLE > m_List;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CHandle<C_BaseEntity> h;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CHandle < C_StandaloneEntity > h;" ) );
	EXPECT_EQ( 3, TypedHandleSpellings( "CHandle< C_RopeKeyframe > a; CHandle<C_BaseEntity> b, EHANDLE c" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "EHANDLE_H" ) );
}

TEST( ClientTypedHandleSweep, TheMatcherDoesNotCountTheSpellingsTheCensusLeavesAlone )
{
	// No word boundary before EHANDLE: the underscore is a word character.
	EXPECT_EQ( 0, TypedHandleSpellings( "INVALID_EHANDLE_INDEX" ) );
	EXPECT_EQ( 0, TypedHandleSpellings( "NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS" ) );

	// CHandle without a following '<' is the class name, not a handle.
	EXPECT_EQ( 0, TypedHandleSpellings( "the typed CHandle route" ) );

	// CBaseHandle is what the sweep puts IN. Counting it would make the sweep
	// unfinishable.
	EXPECT_EQ( 0, TypedHandleSpellings( "CBaseHandle m_hEnt;" ) );
	EXPECT_EQ( 0, TypedHandleSpellings( "CUtlVector< CBaseHandle > m_RestoredEntities;" ) );
}

//=============================================================================
// 2. THE NAMED SITES.
//=============================================================================

//-----------------------------------------------------------------------------
// THE ROPE. It held three typed handles, and it now holds no handle at all.
//
// 5d retyped this file's three CHandle< T >s to CBaseHandle and resolved each
// at the use site, because a typed handle's Get() is an unchecked (T*) over
// whatever the client entity list hands back for that index and serial -- and
// since 5a that list holds C_StandaloneEntity shells beside the stock client
// entities, so a reused entry could come back wearing the wrong class's type.
//
// STEP c8-ropeanchor REMOVED THE POPULATION RATHER THAN THE CAST. The two
// anchors were SDK entities this file constructed and are gone entirely -- the
// endpoints are vectors on the rope now -- and C_RopeKeyframe is no longer an
// entity: no index, no row in the client entity list, and
// CDefaultClientRenderable::GetRefEHandle is an Assert(false). There is no
// handle left to type, no list read to check, and no entry for anything else to
// be reused into.
//
// SO THE ASSERTIONS INVERT, AND THE INVERSION IS THE POINT: what is asserted
// now is that the file holds NO handle of either kind for its ropes, and that
// what replaced the check is ownership plus a destroy callback -- not a raw
// pointer followed on the strength of a memory, which is what the typed handle
// was and what this test exists to keep out.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheRopeSpanHoldsNoEntityHandleAtAll )
{
	const std::string src = Read( kRope );
	ASSERT_FALSE( src.empty() );

	// Test 1's sweep already covers CHandle< T >. This covers the UNTYPED one
	// too: the executor holds strips and materials, and a handle here would be
	// a claim about a list that holds neither.
	const std::string code = StripComments( src );

	EXPECT_FALSE( Has( code, "CBaseHandle" ) )
		<< "shell_rope_render.cpp holds an entity handle again. The rope spans "
		   "are C#'s (ClientRopeSystem, keyed by the owner's managed handle); the "
		   "executor holds a frame's strips and nothing that outlives it.";

	EXPECT_FALSE( Has( code, "GetRefEHandle()" ) )
		<< "shell_rope_render.cpp asks a renderable for its handle.";

	// Positively, so that "holds no handle" cannot be satisfied by a file that
	// holds no strips either.
	EXPECT_TRUE( Has( code, "CUtlVector< RopeStrip_t > strips;" ) )
		<< "the executor no longer holds a view's strips at all.";
}

//-----------------------------------------------------------------------------
// THE STOCK ROPE OBJECT IS GONE, and with it the pointer that needed a destroy
// callback: there is no rope object on this side to outlive its owner.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheExecutorHoldsNoRopeObject )
{
	const std::string code = StripComments( Read( kRope ) );
	ASSERT_FALSE( code.empty() );

	EXPECT_EQ( 0, CountOf( code, "C_RopeKeyframe::" ) )
		<< "shell_rope_render.cpp reaches into the stock rope again.";
	EXPECT_EQ( 1, CountOf( code, "void SourceSharp_RopeDraw(" ) )
		<< "shell_rope_render.cpp no longer has exactly one strip entry; the "
		   "absences here would then be about a file that takes no rope.";
	EXPECT_EQ( 0, CountOf( code, "*pRope" ) + CountOf( code, " pRope" ) )
		<< "shell_rope_render.cpp holds a rope pointer again; a pointer nothing "
		   "clears is the unchecked typed handle in another shape.";
}

//-----------------------------------------------------------------------------
// THE STRIPPER ITSELF CAN FAIL, and this is what says so.
//
// The absence assertions above are read from stripped source. A stripper that
// returned the empty string would make every one of them pass, on any tree --
// the exact "check that cannot fail" shape this port has found 43 times. So it
// is given text it must cut and text it must keep, and the real file is
// asserted to survive it.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheCommentStripperCutsCommentsAndKeepsCode )
{
	EXPECT_EQ( "int x;\n", StripComments( "int x;// CBaseHandle h;\n" ) );
	EXPECT_EQ( "int x;", StripComments( "int /* CBaseHandle */x;" ) );

	// A needle that is ONLY in a comment goes; one that is in code stays.
	EXPECT_FALSE( Has( StripComments( "// CBaseHandle h;" ), "CBaseHandle" ) );
	EXPECT_TRUE( Has( StripComments( "CBaseHandle h; // a handle" ), "CBaseHandle" ) );

	// And the real file is not eaten.
	const std::string code = StripComments( Read( kRope ) );

	EXPECT_TRUE( Has( code, "SourceSharp_RopeDraw" ) )
		<< "the stripper ate shell_rope_render.cpp's code as well as its "
		   "comments; every absence asserted from it is vacuous.";
	EXPECT_GT( code.size(), (size_t)1000 )
		<< "stripped shell_rope_render.cpp is " << code.size() << " bytes, which "
		   "is not a source file";
}

//-----------------------------------------------------------------------------
// AND THE ROPE ROUTE IS READABLE FROM A RUNNING CLIENT.
//
// Every way a span can fail is SILENT: a cable that is not there, on a map
// whose ropes are static decor nobody is looking at. cs_cl_rope_spans is what
// makes that a number; it moved to C# with the spans (RopeSpansCommand.cs).
//-----------------------------------------------------------------------------
const char *const kRopeCommand = "sourcesharp/managed/SourceSharp.Client/Ropes/RopeSpansCommand.cs";

TEST( ClientTypedHandleSweep, TheRopeRouteHasALiveReadout )
{
	const std::string src = Read( kRopeCommand );
	ASSERT_FALSE( src.empty() ) << "could not read " << kRopeCommand;

	ASSERT_TRUE( Has( src, "[ConCommand(\"cs_cl_rope_spans\"," ) )
		<< "cs_cl_rope_spans is gone. It is the only thing that says whether "
		   "the spans are simulating and drawing on a running client.";

	EXPECT_TRUE( Has( src, "{ropes.Strips} strip(s) drawn" ) )
		<< "the readout no longer reports how many strips reached the executor.";

	EXPECT_TRUE( Has( src, "not one span has drawn a strip" ) )
		<< "the readout no longer warns when spans exist and nothing drew -- the "
		   "shape a reader would otherwise mistake for a map with no ropes.";
}

//-----------------------------------------------------------------------------
// AND THE READOUT REPORTS THE SHAPE, NOT ONLY THE TALLY: a rope strung between
// two zeroes, or one that never left its straight initial line, increments
// every count and still draws. These are the numbers those two cannot fake.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheRopeReadoutReportsTheGeometryAndNotJustACount )
{
	const std::string src = Read( kRopeCommand );
	ASSERT_FALSE( src.empty() );

	EXPECT_TRUE( Has( src, "e0 {r.StartError:F2} e1 {r.EndError:F2}" ) )
		<< "the readout no longer reports how far the end NODES are from the "
		   "two places the span was told to hang between.";

	EXPECT_TRUE( Has( src, "sag {r.Sag:F2}" ) )
		<< "the readout no longer reports the sag. Zero sag is a TAUT rope.";

	EXPECT_TRUE( Has( src, "owner {r.OwnerError:F2}" ) )
		<< "the readout no longer reports node 0's distance from the owner's "
		   "origin, which is the per-frame sweep measured.";

	// The sag is measured against the chord, not against a constant.
	const std::string sys = Read( "sourcesharp/managed/SourceSharp.Client/Ropes/ClientRopeSystem.cs" );
	EXPECT_TRUE( Has( sys, "float drop = RopeMath.Lerp(start, end, t).Z - sim.Physics.Node(i).Pos.Z;" ) )
		<< "the sag is no longer measured against the straight line between the "
		   "two ends.";
}

//-----------------------------------------------------------------------------
// THE CLIENT RESTORE LIST. The direct twin of 1e's server restore list.
//-----------------------------------------------------------------------------
// TheClientRestoreListHoldsUntypedHandles stood here. The list left in C8
// (lane c6-input): its one writer, the free AddRestoredEntity, had no caller on
// the link line, so there is no list to type (ClientC8Evicted holds that).

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE OLD NAME -- and this is the exact twin of the
// server's in typed_handle_sweep_test.cpp, changing for the same reason.
//
// This was `TheClientRestoreWalkResolvesThroughTheListsUnknown` and it required
// `C_BaseEntity *pEntity = SDKEntityFromUnknown(` in DispatchOnRestore's loop.
// That resolve and the OnRestore() it guarded were inside
// `#ifndef SS_CBASEENTITY_ALIAS` and were never compiled; armed,
// SDKEntityFromUnknown answers NULL for EVERY occupant, because there is no SDK
// client hierarchy left for it to answer with. f288a5e11 deleted the arm.
//
// The remaining clause -- NOT the handle's own operator-> -- is unchanged and
// is still the point: `m_RestoredEntities[i]->OnRestore()` is CBaseHandle::Get()
// with an unchecked (T*) on the end, and it would be a wrong answer rather than
// a missing one. The successor clauses are what keep "resolves nothing" from
// being satisfied by deleting the method.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheClientRestoreWalkResolvesNothingAndNeverThroughTheHandle )
{
	const std::string src = Read( kClientDll );
	ASSERT_FALSE( src.empty() );

	EXPECT_FALSE( Has( src, "m_RestoredEntities[i]->OnRestore();" ) )
		<< "DispatchOnRestore is back on the handle's own operator->, which is "
		   "CBaseHandle::Get() with an unchecked (T*) on the end.";

	EXPECT_FALSE( Has( src, "SDKEntityFromUnknown(" ) )
		<< "DispatchOnRestore resolves an SDK entity again. It answers NULL for "
		   "every occupant of this list armed, so the only thing a pointer from "
		   "it can be used for is calling C_BaseEntity members on entities that "
		   "are not C_BaseEntitys.";

	// THE LIVE SUCCESSOR: the method is still there to be the shape a managed
	// restore comes back in. (The list it drained left in C8: nothing filled it.)
	EXPECT_TRUE( Has( src, "void CShellClientDLL::DispatchOnRestore()" ) )
		<< "DispatchOnRestore is gone. The engine calls it; deleting it is not "
		   "the same as it having nothing to do.";
}

//=============================================================================
// 3. THE LIVE CHECK THE SWEEP REPLACED ITS LAST TYPED HANDLE WITH -- RETIRED
//    IN PHASE 1c, WITH THE CAPABILITY IT PROVIDED NAMED RATHER THAN GLOSSED.
//
// WHAT WAS HERE. Three facts over `CON_COMMAND( cs_cl_handle_cross_check, )` in
// shell_standalone_client.cpp:
//
//   TheLiveCrossCheckAsksTheSlotTableAndNotATypedHandle
//   TheLiveCrossCheckStillComparesAgainstTheCompilersOwnCrossing
//   TheLiveCrossCheckSaysWhatAnUnboundClientTableLooksLike
//
// The command was the sweep's own replacement for the last typed handle in the
// client shim: rather than deleting `CHandle< IGameEntity >` and losing what it
// demonstrated, the lane repointed it at the slot table's two resolvers and
// compared their answers with the compiler's own crossing, on every live client
// standalone entity.
//
// WHY IT IS RETIRED AND NOT REWRITTEN. Phase 1c deletes the client bring-up
// instruments, and this is one: a ConCommand nothing drives. Nothing under
// tools/ runs it (tools/7e-particle-sound-gate quotes a line of its output in
// a docstring and never sends the command), no managed code names it, and it
// changes nothing, so no gate depends on it.
//
// AND HERE IS WHAT ACTUALLY GOES WITH IT, said plainly because it is a real
// loss and not a tidy-up. The comment these facts sat under claimed the command
// was "the only thing asking the REAL class -- on real entities, in a running
// client -- whether the resolver agrees with the compiler", and that claim was
// true. handle_cross_test.cpp asks the same question of MOCKS, which cannot
// reproduce the hazard the crossing exists for: an object whose game-entity
// view sits at a different address from its handle view. The command counted
// exactly those ("had the two views at different addresses"), and a ZERO there
// was the standing evidence that the hazard is not reproducible on this build.
//
// Nobody has taken that reading since the instrument stopped being driven, so
// its absence changes no verification that was actually running. If it is
// wanted back it belongs in Debug.Api over the same two resolvers --
// SourceSharp_ClientShellFromHandle and SourceSharp_ClientShellFromIndex --
// which is where this phase sends every other client instrument's finding.
//
// WHAT IS KEPT HERE IS THE SWEEP'S OWN INVARIANT, asked of the file the three
// retired facts read. It is the thing this whole test file exists for, and the
// command was only ever the place the last violation of it had lived.
//=============================================================================

TEST( ClientTypedHandleSweep, TheShellSpellsNoTypedEntityHandleWhereTheLastOneLived )
{
	// shell_standalone_client.cpp held the sweep's LAST typed handle -- a
	// CHandle< IGameEntity > built inside the live cross-check so that its
	// Get() could be compared with the compiler's own crossing. The command is
	// gone; this is the assertion that its removal did not quietly become the
	// reason nobody notices a new one.
	//
	// NoClientShimSourceSpellsATypedEntityHandle above walks the whole shim and
	// would also catch it. This is asked of this ONE file anyway, by name,
	// because the walk's file list is data and a file dropping off it is
	// invisible -- and this is the file with the history.
	const std::string src = StripComments( Read( kShell ) );
	ASSERT_FALSE( src.empty() ) << "could not read " << kShell;

	EXPECT_FALSE( Has( src, "CHandle< IGameEntity >" ) )
		<< kShell << " spells a typed handle over IGameEntity again. This file "
		   "is where the sweep's last one lived.";
	EXPECT_FALSE( Has( src, "CHandle<IGameEntity>" ) )
		<< kShell << " spells a typed handle over IGameEntity again (no-space "
		   "spelling).";
}

TEST( ClientTypedHandleSweep, BothSlotTableResolversAreStillReachedFromTheShippingPath )
{
	// THE PROPERTY THE RETIRED CROSS-CHECK EXERCISED, asked where it is true
	// in the SHIPPING build rather than inside a console command.
	//
	// The old fact required the command to call both
	// SourceSharp_ClientShellFromHandle and SourceSharp_ClientShellFromIndex,
	// its stated reason being that "nothing else in a passive run reaches"
	// the by-index arm. That was a claim about a console command's coverage.
	// The claim worth holding is the stronger one underneath it: BOTH arms are
	// on the engine's own path, so a build that lost one is broken for every
	// client whether or not anyone types a command.
	const std::string list = StripComments(
		Read( "sourcesharp/game/client/shell_cliententitylist.cpp" ) );
	ASSERT_FALSE( list.empty() ) << "could not read shell_cliententitylist.cpp";

	EXPECT_TRUE( Has( list, "SourceSharp_ClientShellFromHandle( hEnt )" ) )
		<< "IClientEntityList's by-handle resolver no longer asks the slot "
		   "table, so every engine lookup by handle falls through to the port "
		   "seam -- which answers NULL.";
	EXPECT_TRUE( Has( list, "SourceSharp_ClientShellFromIndex( nEntry )" ) )
		<< "IClientEntityList's by-index resolver no longer asks the slot "
		   "table. This is the arm the engine uses ~818k times in forty "
		   "seconds; a NULL from it is every entity vanishing.";
}

//=============================================================================
// 4. THE BAN IS WIRED IN, from the direction the ban itself cannot check.
//
// standalone_handle_ban_client_test.cpp asserts that shell_standalone_client.h
// includes the ban. This asserts it is LAST in that header, which is the
// property the include alone does not have: an include above a declaration of
// the shell would leave a window in which the class is nameable and the
// specialization has not been seen, and the compiler would take an implicit
// instantiation in that window rather than the ban.
//=============================================================================

TEST( ClientTypedHandleSweep, TheBanIsTheLastIncludeInTheShellHeader )
{
	const std::string src = Read( kShellH );
	ASSERT_FALSE( src.empty() );

	const size_t ban = src.find( "#include \"standalone_handle_ban_client.h\"" );
	ASSERT_NE( std::string::npos, ban ) << "shell_standalone_client.h no longer "
		"includes the ban.";

	EXPECT_EQ( std::string::npos, src.find( "#include", ban + 1 ) )
		<< "another #include follows the ban in shell_standalone_client.h. The "
		   "ban has to be the last one: a header included after it can spell "
		   "C_StandaloneEntity in a context where the specialization has not "
		   "been declared yet.";
}

//=============================================================================
// 5. WHAT THIS STEP STARTED FROM.
//
// Not a gate -- a record, so the size of the sweep is a measurement somebody
// can re-take rather than a sentence in a handover. The numbers came from the
// census's own TU list and pattern on the commit this lane branched from
// (1a14675f): 15 spellings in 6 files, of which four were code and
// eleven were prose naming the spelling the files no longer use.
//=============================================================================

TEST( ClientTypedHandleSweep, TheStepStartedFromFifteenSpellingsInSixFiles )
{
	// The six files the census named. Re-read here at zero: the pairing is what
	// makes the "15 in 6" claim checkable rather than recited.
	static const char *const kWas[] = {
		"sourcesharp/game/client/shell_clientdll.cpp",
		"sourcesharp/game/client/shell_rope_render.cpp",
		"sourcesharp/game/client/shell_standalone_client.cpp",
		"sourcesharp/game/client/shell_te_forwards_client.cpp",
		"sourcesharp/game/client/shell_te_pool_client.cpp",
		"sourcesharp/game/client/shell_worldquery_client.cpp",
	};

	const size_t nFiles = sizeof( kWas ) / sizeof( kWas[0] );
	EXPECT_EQ( (size_t)6, nFiles );

	for ( size_t i = 0; i < nFiles; i++ )
	{
		const std::string src = Read( kWas[i] );

		ASSERT_FALSE( src.empty() )
			<< kWas[i] << " is gone. If it was deleted rather than swept, this "
			   "list is stale and the count above describes a tree that no "
			   "longer exists.";

		EXPECT_EQ( 0, TypedHandleSpellings( src ) ) << kWas[i];
	}
}

//-----------------------------------------------------------------------------
// AND THE TWO HEADERS THE STEP DELIBERATELY LEFT ALONE, said out loud.
//
// beam_endpoint.h and shell_te_backend_args.h spelled EHANDLE once each, in
// prose describing the STOCK bug (view_beams.cpp storing a beam endpoint as a
// typed handle and reading it back through CBaseHandle::Get()) that behaviour
// ledger row C8 is about. The C2 beam port took both paragraphs: beam_endpoint.h
// left (its route is BeamEndpoint.cs's) and the beam decoders left
// shell_te_backend_args.h. The header that stays is still read, so a typed
// handle arriving in it is still seen.
//
// Neither is in the census's scope: its TU list is the .cpp files the ninja
// object table names, so a header is never counted on either side, and 1e's
// server walk is .cpp-only for the same reason. Recorded as a FACT rather than
// as a comment so that a later widening of either walk to headers meets a
// deliberate decision rather than a surprise.
//-----------------------------------------------------------------------------
TEST( ClientTypedHandleSweep, TheTwoProseHeadersAreTheKnownAndDeliberateExceptions )
{
	const std::string args = Read( "sourcesharp/game/client/shell_te_backend_args.h" );

	ASSERT_FALSE( args.empty() );

	EXPECT_EQ( 0, TypedHandleSpellings( args ) )
		<< "shell_te_backend_args.h spells a typed entity handle again, and the "
		   "census does not read headers.";
}
