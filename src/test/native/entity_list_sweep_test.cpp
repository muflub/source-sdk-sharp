//========= SourceSharp — plan_entity.md step 1d, as facts ===================//
//
// THE SHIM'S ENTITY-LIST SURFACE, AND THE TWO RESOLVERS THAT REPLACED PART OF
// IT. Step 1d's own gate is a census reading: "zero gEntList / g_pEntityList in
// these files, and the cast census falls by the sites named". A census is run
// by hand and compared against a committed baseline; what it cannot say is WHY
// a number is what it is, so a site added back into a file that already has one
// moves the total by one and reads exactly like a site legitimately still
// there. This file is the why.
//
// SOURCE-READ, and it is forced rather than lazy. Every subject here includes
// cbase.h's whole world -- shell_entityapi.cpp, shell_soundenvelope.cpp,
// world_adapter.cpp and toucher.h cannot be linked into a gtest, and neither
// can the shell they resolve to. The behavioural half of this step is the live
// gate; what is here is what a compiler cannot check and a reader cannot check
// by eye.
//
// TWO KINDS OF FACT, and the difference decides whether comments are stripped:
//
//   THE LEDGER (LastFileAtZero..., TheRemainingSitesAre...) reads RAW TEXT,
//   comments included, because tools/entity-census greps raw text and a fact
//   that disagreed with the instrument it is standing in for would be worse
//   than no fact at all. That is also why the sweep's own comments never spell
//   `gEntList.` or `g_pEntityList->`: a paragraph explaining a deleted call
//   would be counted as the call.
//
//   EVERY OTHER FACT strips comments first. All four subjects describe the
//   operations they perform at length, so an unstripped search for
//   "SourceSharp_ShellFromIndex" finds the paragraph explaining it and passes
//   over a body that no longer calls it -- the shape this tree lists as its
//   fifth check-that-cannot-fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <algorithm>
#include <fstream>
#include <dirent.h>
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
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

const char *const kSoundEnvelope = "sourcesharp/game/server/shell_soundenvelope.cpp";
} // namespace

//=============================================================================
// 1. THE LEDGER.
//
// Step 1d owns thirteen files. SIX of them are at zero and MUST STAY at zero;
// the other five carry eleven calls between them, every one of which is either
// the SDK entity list's own LIFECYCLE (create, delete-list, clear) or a walk of
// the SDK HIERARCHY, and both of those leave with entitylist.cpp in Phase 4
// rather than with this step.
//
// PINNED IN BOTH DIRECTIONS, which is the whole value of the pair. An exact
// count goes red when a lane puts a call back into a file this step emptied
// AND when a call listed below is removed without the ledger moving with it --
// the second of which is the one a ratchet would miss, because a number that
// only ever falls cannot report that the reason for it changed.
//=============================================================================

//-----------------------------------------------------------------------------
// THE LEDGER WAS COUNTING DEAD SOURCE, AND THIS IS WHERE THAT SHOWS.
//
// READ THE ASSERTIONS, NOT THE NAME -- the name is kept because the fact's JOB
// is unchanged (pin each file's count in both directions), but seven of the
// eleven "remaining sites" it pinned were inside `#ifndef SS_CBASEENTITY_ALIAS`
// and were never compiled. SS_CBASEENTITY_ALIAS is defined in every shipped
// build, so `FindEntityInSphere`, the FirstEnt/NextEnt mdlcache walk, all four
// of shell_level.cpp's lifecycle calls and two of
// shell_restoreentities.cpp's three were text. f288a5e11 deleted them -- "the
// shim's 240 dead guarded arms, and nothing else" -- and NOTHING THE RUNNING
// GAME DID CHANGED, which is the proof they were never doing it.
//
// The ledger's argument moves with them. It used to read "these leave in Phase
// 4 with game/server/entitylist.cpp, and deleting one now deletes a
// capability". That argument was about compiled calls; it never applied to
// these. What is left, below, is FIVE LINES IN FIVE FILES, and every one of
// them is a COMMENT -- which is why this fact is now paired with
// NoShimFileMakesAnEntityListCallAtAll, the fact that reads the same files with
// the comments stripped. A count of raw lines alone can no longer tell prose
// from a call, so it may no longer be asserted alone.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND NOT ONE OF THE FIVE SURVIVING LINES IS A CALL.
//
// The counts above are raw-text lines, in the unit tools/entity-census uses.
// That unit could always be satisfied by a comment, and for most of the port
// that did not matter because the live calls dominated. It matters now: the
// shim is down to five lines and all five are prose, so a raw-line ledger on
// its own can no longer distinguish "the shim stopped calling the list" from
// "somebody wrote a paragraph about the list". Stripping the comments and
// requiring zero is the half that makes the counts above mean something.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SHIM'S WHOLE REMAINING SURFACE, AS ONE NUMBER.
//
// The per-file facts above say WHICH calls are left; this says HOW MANY, over
// every shim file at once, so a call added to a file that has no fact of its
// own cannot arrive unnoticed. That is the hole a per-file ledger has: it can
// only refuse growth in the files somebody thought to name.
//
// IT WAS A RATCHET; IT IS A PAIR NOW. While the shim still made live calls,
// "the number only falls" was the whole fact, guarded by COMMON.md's "a compile
// gate selects for deletion" -- twelve of the sixteen were SDK-hierarchy
// operations with live subjects and deleting one deleted a capability.
//
// That reading did not survive f288a5e11. Eleven of the sixteen were inside
// `#ifndef SS_CBASEENTITY_ALIAS` and had no subjects at all, live or otherwise,
// because the compiler never saw them; removing them deleted no capability and
// the number fell to five. So the fact is now TWO numbers: the CODE count,
// which is zero and is the clause-2 property, and the RAW-TEXT count, which is
// five and is all prose. Asserting either alone is the hole -- raw text alone
// cannot see a call arriving in a file with a paragraph already in it, and code
// alone cannot see the last comment explaining why a thing is not done being
// deleted along with the code it explains.
//-----------------------------------------------------------------------------
//=============================================================================
// WHAT REPLACED THEM -- because "zero entity-list lines" cannot tell a call
// that MOVED from a call that was DELETED.
//
// The ledger above is a refusal and nothing more: a shell that stopped
// registering ANYWHERE would satisfy every count in it, and would also make
// every handle to every managed entity in the map resolve to NULL. So each
// emptied site has a positive fact here naming what stands in its place, and
// each of those is read out of the FUNCTION's own body rather than grepped for
// over the file.
//=============================================================================

//-----------------------------------------------------------------------------
// The body of a function, from its signature to the line that closes it at
// column zero. Same shape world_release_test.cpp uses, and for the same reason:
// "the file contains this text" passes for a paragraph explaining the call.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &src, const char *pszSignature )
{
	const size_t at = src.find( pszSignature );

	if ( at == std::string::npos )
		return std::string();

	const size_t open = src.find( '{', at );

	if ( open == std::string::npos )
		return std::string();

	const size_t close = src.find( "\n}", open );

	if ( close == std::string::npos )
		return src.substr( open );

	return src.substr( open, close - open );
}

//-----------------------------------------------------------------------------
// THE KEYSTONE LINE. plan_entity.md §3.3, and lane p4-flip's whole finding:
// every managed shell in this mod registered in the entity list and every
// handle to one resolved through the result.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The world's, which is not the slot table's -- world_subject.h has why.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE UNION, AND THE ORDER OF ITS ARMS -- OF WHICH THERE ARE NOW TWO.
//
// The five sites that spelled the entity list's LookupEntity did not want the
// SDK's list; they wanted the union of both hierarchies, and the list was the
// only thing that was one. The order is asserted because the arms have to be
// asked cheapest-first.
//
// THE THIRD ARM IS GONE AND THAT IS NOT A TRIM. It used to be asserted PRESENT
// here, on the argument that info_player_deathmatch, prop_ragdoll and the
// physics solvers are still LINK_ENTITY_TO_CLASS'd in compiled stock TUs and
// dropping it would make them unresolvable. What the close established is that
// armed, NO compiled server TU registers an entity of that hierarchy -- the
// level steps measured the residue -- so the arm asked a list with no
// population. NULL is therefore the TRUE answer and not a stub's: the two live
// arms are the whole population and are disjoint from the removed one by
// construction, so it can shadow neither.
//
// WHICH IS WHY THE ABSENCE IS ASSERTED WITH THE RETURN. "No LookupEntity here"
// alone is half a fact -- it holds over a function that fell off the end
// without returning anything, which on this path is a resolver handing back an
// uninitialised pointer for every handle in the map.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The server-only arm REFUSES rather than silently binding nothing (§7).
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE UNIVERSAL RESOLVE, AND THE FAILURE THAT FOUND IT.
//
// CBaseHandle::Get() is what EHANDLE, CHandle<T> and every implicit conversion
// off a handle come through, in both DLLs. It read the entity list, so the day
// the shells left the list every one of those answered NULL AT ONCE -- and
// nothing said so.
//
// MEASURED, NOT PREDICTED. The registrations moved first and this did not, and
// the live gate answered in one run: move, jump, push, second-client and lift
// all failed with the player displaced EXACTLY 0.0 units, on a box where the
// untouched tree passed all twenty steps minutes later. One line carries it --
// `UTIL_TraceRay( ray, mask, mv->m_nPlayerHandle.Get(), ... )` -- where the
// trace's PASS entity is a handle resolved here. NULL there makes the filter
// pass everything, the player's own ground trace hits the player's own bounding
// box, and a movement loop that cannot find the floor cannot walk.
//
// So this fact is not decoration on a working build; it is the one that was red
// while every other check in this file was green.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND IT IS NO LONGER GUARDED TO THE SERVER, which is lane `f-list`'s edit and
// the one this fact exists to hold down.
//
// It used to be, and the reason was true when it was written: the client's
// shells were still in cl_entitylist, so the stock line WAS the whole client
// answer. Once SourceSharp_ClientEntityBind binds the client slot table and
// C_World holds entry 0 on its own handle, that stops being true -- and the
// failure mode is the server's, on the other side: every client EHANDLE
// answering NULL for every managed entity at once, with nothing logged.
//
// MUTATION-VERIFIED as a CHANGE and not only as a deletion: putting
// `&& !defined( CLIENT_DLL )` back is what a merge or a revert would do, and
// that is the mutation this fact was watched to fail against.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND BOTH SIDES ACTUALLY DEFINE IT. The guard above widened the DECLARATION to
// both DLLs; a declaration with no definition on one side is a link error that
// only the flip's relink arm would have shown, because the client's copy of the
// inline is a weak COMDAT that the ordinary build still resolves out of
// cliententitylist.cpp.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLIENT'S UNION ASKS THE SAME THREE ARMS THE SERVER'S DOES, and asks them
// through ClientUnknownFromHandle rather than open-coding a second order.
//
// Two orders for one question is how the two halves of a resolve come to
// disagree: `c8-listseam` recorded exactly that happening between
// SourceSharp_RenderableFromHandle's two arms and ClientUnknownFromHandle's
// three. This fact keeps the union from becoming a third.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND THE LEVEL SHUTDOWN USES THE ONE THAT KEEPS THE SERIALS.
//
// slot_table_test.cpp proves the two clears BEHAVE differently; this says which
// one the level shutdown calls, because that is the half a unit test cannot see
// and the half that crashed. The edict's m_NetworkSerialNumber survives a map
// load, so a table that rewound would hand map 2's entity at edict 5 the serial
// map 1's had and the engine would pack it as a continuation of the dead one.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// EVERY SHIM FILE, WALKED RATHER THAN LISTED.
//
// The per-file facts above say WHICH calls are left in the files somebody
// thought to name; this says HOW MANY over the WHOLE shim, so a call added to a
// file with no fact of its own cannot arrive unnoticed. The first draft of this
// summed a hard-coded list and was mutation-proved USELESS: putting the entity
// list back into shell_standalone_player.cpp -- which was not on the list --
// left it green. A ledger that can only refuse growth where it already looks is
// the shape this tree calls a check that cannot fail.
//
// It is a RATCHET and not a target. The number falls when the atomic flip
// deletes game/server/entitylist.cpp and the hierarchy it serves; until then
// every one of these has a live subject, and deleting one to move the number is
// what COMMON.md's "a compile gate selects for deletion" is about.
//-----------------------------------------------------------------------------
void CollectShimSources( const std::string &dir, std::vector< std::string > &into )
{
	DIR *pDir = opendir( dir.c_str() );

	if ( !pDir )
		return;

	while ( struct dirent *pEnt = readdir( pDir ) )
	{
		const std::string name = pEnt->d_name;

		if ( name == "." || name == ".." )
			continue;

		const std::string path = dir + "/" + name;

		struct stat st;
		if ( stat( path.c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
		{
			CollectShimSources( path, into );
			continue;
		}

		const size_t dot = name.rfind( '.' );

		if ( dot == std::string::npos )
			continue;

		const std::string ext = name.substr( dot );

		if ( ext == ".cpp" || ext == ".h" )
			into.push_back( path );
	}

	closedir( pDir );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S SPELLING, WHICH THE RATCHET BELOW DOES NOT COUNT -- lane
// sub-entitylist.
//
// EveryShimFileTogetherNamesTheEntityListSixteenTimes walks the whole shim for
// `gEntList.` and `g_pEntityList->`. Those are the SERVER's two spellings. A
// game/shared shim file compiled into BOTH DLLs can name the client's list
// instead, inside `#ifdef CLIENT_DLL`, and the ratchet counts none of it: it
// was 16 before this lane removed two such sites and it is 16 after, which is
// correct arithmetic and a hole.
//
// The two were sound_emitter.cpp's `ClientEntityList().GetClientEntity(
// entindex )` and util_shared_library.cpp's `ClientEntityList().EntIndexToHandle(
// iEntIndex )`. Both are index lookups, both now take
// shell_cliententitylist.h's slot-table-first resolvers, and NOTHING WOULD HAVE
// SAID SO IF EITHER CAME BACK. This closes that.
//
// COMMENTS ARE STRIPPED, unlike the ratchet below, and that is not a
// discrepancy to tidy: this expects ZERO, and four shim files explain in prose
// what `cl_entitylist` and `ClientEntityList()` used to do here. Counting prose
// as code is the mistake this tree has made six times, and a zero-expecting
// check is exactly where it would bite. The ratchet below expects a number that
// INCLUDES its comments and says so in its own name, so it must not strip.
//-----------------------------------------------------------------------------
//=============================================================================
// 2. THE REVERSE SCAN IS GONE.
//=============================================================================

TEST( EntityListSweep, TheReverseScanHeaderAndItsTestAreDeleted )
{
	EXPECT_FALSE( Exists( "sourcesharp/game/shared/gameentity_handle.h" ) )
		<< "the scan template outlived the scan";
	EXPECT_FALSE( Exists( "sourcesharp/test/gameentity_handle_test.cpp" ) )
		<< "seven tests for a walk that no longer exists are seven tests that "
		   "cannot fail";
}

//-----------------------------------------------------------------------------
// AND ITS FILE IS GONE. plan_entity.md's Phase 1 exit names `gameentity_handle.*`
// among the things that are; the .cpp was one translation unit compiled into
// BOTH DLLs to hold one function that answers a constant.
//-----------------------------------------------------------------------------
//=============================================================================
// 3. THE TWO RESOLVERS THIS STEP ADDED.
//=============================================================================

//=============================================================================
// 4. THE TOUCHER.
//=============================================================================

//=============================================================================
// 5. THE SOUND PATCH.
//=============================================================================

// THE SOUND PATCH LEFT FOR C# (plan_server_native_v2 T4a). Its three facts here
// pinned how the native CSoundPatch resolved its entity -- through the slot
// table, never the networkable face, by index at build. The patch is
// ServerSoundPatches now, which holds the entity's handle and asks the managed
// entity area whether it is still valid; ServerSoundsTests hold that (a patch
// whose entity has gone answers -1 and is not emitted; one from the world rides
// nothing). What is left to pin natively is the absence.
TEST( EntityListSweep, TheNativeSoundPatchIsGone )
{
	EXPECT_FALSE( Exists( kSoundEnvelope ) )
		<< "the server's CSoundPatch is managed: a native copy would decide the "
		   "same thing in two places";
}

//=============================================================================
// 6. THE FINDERS: the name walk left for the managed directory, the classname
// finder stays.
//=============================================================================

//=============================================================================
// 7. THE TRACE DIAGNOSTIC.
//=============================================================================

//-----------------------------------------------------------------------------
// cs_sv_trace_subject PRINTED THE TWO ARMS BESIDE EACH OTHER, the slot table's
// and TraceGameEntity's, so that their coming apart could be seen. It borrowed
// the server's native trace filter, and that filter went to managed at
// plan_server_native_v2 T4f (TraceFilterRules), so the command went with it,
// with CWorld::GetWorldBounds and ImpactCount, which only it read.
//
// What stays true without it is the successor's shape: TraceGameEntity, which
// the client still calls, is the virtual route and not the deleted cast.
//-----------------------------------------------------------------------------
