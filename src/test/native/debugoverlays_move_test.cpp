//========= SourceSharp — the frame's debug overlays, after Phase A4 ==========//
//
// plan_gamedll_after.md Phase A4 subject 4 moved DrawAllDebugOverlays out of
// gameinterface.cpp and into sourcesharp/game/server/shell_debugoverlays.cpp.
//
// WHY IT NEEDS FACTS AT ALL, when a move that loses the function does not link.
// Three things about this one are not enforced by the linker:
//
//   * the frame still calls it. shell_frame.cpp reaches it through its own
//     forward declaration rather than a header, so deleting step 12's call is a
//     clean compile and a server that silently stops drawing every overlay,
//     every AI-network edit box and every point_message;
//   * it is NOT behind _DEBUG. Stock's own comment beside the call reads "allow
//     this in release for now", and wrapping it while moving it would be a
//     release-only behaviour change that no debug build could ever show;
//   * the two SS_CBASEENTITY_ALIAS guards inside it came across. Each stands
//     for a real hazard -- FindPickerEntity and CAI_Hint's owner are both
//     C-cast to CBaseEntity, and armed this build puts entities in those slots
//     that are not one. A guard dropped during a move is a wrong-vtable call
//     that only fires with the picker in use.
//
// Source-read, and forced: shell_debugoverlays.cpp includes cbase.h, player.h
// and the whole AI-network stack, and cannot be linked into a gtest binary.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

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

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::string Tight( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

std::string Code( const char *pszRel ) { return Tight( StripComments( Read( pszRel ) ) ); }

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

const char *kHome      = "sourcesharp/game/server/shell_debugoverlays.cpp";
const char *kGameIface = "game/server/gameinterface.cpp";
const char *kSteps     = "sourcesharp/game/server/shell_frame.cpp";

} // namespace

//-----------------------------------------------------------------------------
// One definition, in the new home.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, TheFunctionIsDefinedInItsNewHome )
{
	EXPECT_NE( Code( kHome ).find( "voidDrawAllDebugOverlays(void){" ), std::string::npos )
		<< "DrawAllDebugOverlays is not defined in " << kHome;
}

TEST( DebugOverlaysMove, NoSecondCopyIsLeftInGameinterface )
{
	// THE DECLARATION HALF OF THIS FACT IS SPENT, DELIBERATELY. A4 pinned the
	// declaration PRESENT in gameinterface.cpp, because stock's
	// CServerGameDLL::GameFrame still named the function and that body was
	// compiled and unreachable. plan_gamedll_after.md Phase A9 step 3 deleted
	// the class, so the last namer is gone and the declaration went with it;
	// step 4 deletes the file. What survives is the half that can still go
	// wrong: exactly one DEFINITION, in the new home, and none anywhere else.
	if ( !Exists( kGameIface ) )
		return;

	const std::string old = Code( kGameIface );
	ASSERT_FALSE( old.empty() );

	EXPECT_EQ( old.find( "voidDrawAllDebugOverlays(void){" ), std::string::npos )
		<< "DrawAllDebugOverlays is defined in " << kGameIface << " again -- a "
		<< "copy rather than a move. It is a duplicate symbol today and an "
		<< "invisible stale body once A9 deletes that file.";

	EXPECT_EQ( old.find( "voidDrawAllDebugOverlays(void);" ), std::string::npos )
		<< "gameinterface.cpp declares DrawAllDebugOverlays again. Nothing in "
		<< "that file calls it any more -- stock's GameFrame was its one namer "
		<< "and A9 step 3 deleted it -- so a declaration there is a promise "
		<< "about somebody else's symbol that nothing checks.";
}

//-----------------------------------------------------------------------------
// THE FRAME STILL CALLS IT. This is the fact the linker cannot make: step 12
// reaches the function through a forward declaration in its own file, so
// deleting the call compiles clean and draws nothing, forever, with no message.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, FrameStepTwelveStillCallsIt )
{
	const std::string steps = Code( kSteps );
	ASSERT_FALSE( steps.empty() );

	// THE STEP IS A VTABLE METHOD NOW, not a free function. plan_shim.md §5 3a
	// (lane 3b-enginebind) folded shell_framesteps.cpp's nineteen free
	// functions into the nineteen CShimFrame methods that used to forward to
	// them, so the needle is the method rather than SourceSharp_Frame...().
	//
	// AND THE CALL IS SCOPE-QUALIFIED, which is the merge's one real hazard
	// rather than a style change: inside a class whose own method is named
	// DrawAllDebugOverlays, an unqualified DrawAllDebugOverlays() resolves to
	// THAT METHOD and recurses until the stack is gone. `::` is what makes it
	// stock's global, and this needle is what keeps it there.
	const size_t at = steps.find( "virtualvoidDrawAllDebugOverlays()OVERRIDE{" );
	ASSERT_NE( at, std::string::npos )
		<< "the frame step that draws the overlays is gone from " << kSteps
		<< ". Nothing else calls DrawAllDebugOverlays on a live server.";

	const size_t close = steps.find( '}', at );
	ASSERT_NE( close, std::string::npos );

	EXPECT_NE( steps.substr( at, close - at ).find( "::DrawAllDebugOverlays();" ),
	           std::string::npos )
		<< "frame step 12 no longer calls the global DrawAllDebugOverlays. Its "
		<< "body is: " << steps.substr( at, close - at );
}

//-----------------------------------------------------------------------------
// NOT BEHIND _DEBUG, which is stock's decision and not an oversight.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, TheOverlaysAreNotWrappedInADebugOnlyGuard )
{
	const std::string raw = Read( kHome );
	ASSERT_FALSE( raw.empty() );

	EXPECT_EQ( raw.find( "#ifdef _DEBUG" ), std::string::npos )
		<< "the move put DrawAllDebugOverlays behind _DEBUG. Stock's own "
		<< "comment beside the call reads \"allow this in release for now\", "
		<< "so this would be a release-only behaviour change that no debug "
		<< "build can show.";
}

//-----------------------------------------------------------------------------
// BOTH ALIAS GUARDS ARE GONE, AND THE POINT IS THAT THEY WENT WITH THEIR
// SUBJECTS -- plan_entity.md Phase 4, step 4c.
//
// This fact used to read "both alias guards survived the move" and count two.
// It counts ZERO now, and that is not the guard budget being spent: the two
// regions each wrapped a C-cast to an entity of something this build may put a
// non-entity in, and both casts were DELETED.
//
//   * FindPickerEntity's answer. shell_entity_orphans.cpp has answered NULL
//     there since 13c and says why; the arm could never draw anything.
//   * CAI_Hint's owner -- plan_entity.md §3.16 FAMILY 6, the cast through a
//     void pointer, and the only site of that family on this side outside
//     baseentity_statics.cpp.
//
// Both sat inside `if (engine->IsInEditMode())`, and that block opened with
// `g_pAINetworkManager->GetEditOps()->...` with NO NULL TEST while
// shell_ai_absent.cpp defines g_pAINetworkManager as NULL -- so it did not draw
// the wrong thing, it dereferenced NULL. Deleted with the editor it drew for,
// and recorded in the managed ContractBaseline with a reason.
//
// A ZERO IS NOT ENOUGH ON ITS OWN, which is why there are three facts here and
// not one: a file emptied by accident would also carry no guards. So the two
// SUBJECTS are asserted absent by name, and the replacement asserted present.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, NeitherAliasGuardRemains )
{
	// COMMENTS STRIPPED, NEWLINES KEPT. This file's own header block explains
	// at length what it no longer contains, and 67 of this tree's 75
	// source-reading tests match text without stripping -- step 3g's own
	// replacement comment put two deleted cast sites straight back into the
	// census. StripComments and not Code(), because the needle below is
	// deliberately newline-terminated and Tight() would eat it.
	const std::string raw = StripComments( Read( kHome ) );
	ASSERT_FALSE( raw.empty() );

	// THE NEEDLE CARRIES ITS NEWLINE. Without it this is a PREFIX match and
	// mutation P5 proved it: renaming the macro to SS_CBASEENTITY_ALIAS_DROPPED
	// -- which disarms a guard -- left the old count green, because the longer
	// name still contains the shorter one. Kept for the same reason now that the
	// expected count is zero: a guard renamed rather than deleted has to fail.
	static const char *kGuard = "#ifndef SS_CBASEENTITY_ALIAS\n";

	size_t nOpen = 0;
	for ( size_t at = raw.find( kGuard );
	      at != std::string::npos;
	      at = raw.find( kGuard, at + 1 ) )
	{
		nOpen++;
	}

	EXPECT_EQ( nOpen, 0u )
		<< "shell_debugoverlays.cpp carries " << nOpen << " SS_CBASEENTITY_ALIAS "
		<< "guard(s) and step 4c left it none: both regions were deleted with "
		<< "their subjects. A guard here means one of them came back, or a new "
		<< "cast was added -- either way it wants an argument in the "
		<< "ContractBaseline rather than a #ifndef.";
}

TEST( DebugOverlaysMove, TheTwoDeletedCastSubjectsAreGone )
{
	// CODE, NOT TEXT. The header block above names both subjects in prose --
	// deliberately, because a tombstone that does not say what it buried is
	// worthless -- and an unstripped scan would fail on this file's own
	// explanation of why it passes.
	const std::string raw = Code( kHome );
	ASSERT_FALSE( raw.empty() );

	// FindPickerEntity is not merely unguarded here, it is not named at all --
	// the file no longer declares it, calls it, or has an arm for its answer.
	EXPECT_EQ( std::string::npos, raw.find( "FindPickerEntity" ) )
		<< "shell_debugoverlays.cpp names FindPickerEntity again. It answers "
		<< "NULL for every entity this build can make (shell_entity_orphans.cpp) "
		<< "and the picker overlay is SourceSharp_DrawEntityOverlay now.";

	// And §3.16 family 6's subject: the AI hint node's owner.
	EXPECT_EQ( std::string::npos, raw.find( "GetHint()" ) )
		<< "shell_debugoverlays.cpp reads an AI hint node again. That cast is "
		<< "plan_entity.md §3.16 family 6, and the block it lived in "
		<< "dereferenced a NULL g_pAINetworkManager before it got there.";

	// The whole editor block, by the line that opened it.
	EXPECT_EQ( std::string::npos, raw.find( "IsInEditMode()" ) )
		<< "the AI node editor block is back in shell_debugoverlays.cpp. It "
		<< "opens with g_pAINetworkManager->GetEditOps() and shell_ai_absent.cpp "
		<< "defines that pointer NULL.";
}

//-----------------------------------------------------------------------------
// WHAT REPLACED THE PICKER ARM, AND THAT IT TAKES A HANDLE.
//
// Step 4c's gate is "overlays draw for a handle". The deleted arm asked
// FindPickerEntity for an entity POINTER; the replacement resolves a
// CBaseHandle through the slot table, which is the one identity both
// hierarchies share. Asserted in text here because the behaviour itself is
// pinned on the other side of the boundary -- overlay_library_test for the
// drawing, OverlayContractTests for the handle contract -- and what THIS file
// owns is that the frame step still reaches it.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, ThePickerOverlayIsAnsweredByHandle )
{
	// Code and not text, for the reason above and sharper here: the header
	// block NAMES SourceSharp_DrawEntityOverlay, so an unstripped scan would
	// pass over a file that had deleted the function and kept the comment.
	const std::string raw = Code( kHome );
	ASSERT_FALSE( raw.empty() );

	EXPECT_NE( std::string::npos, raw.find( "SourceSharp_DrawEntityOverlay" ) )
		<< "the handle-addressed overlay entry is gone from "
		<< "shell_debugoverlays.cpp. Deleting the picker arm without it leaves "
		<< "the parallel hierarchy with no entity overlay at all, which is the "
		<< "silent no-op plan_entity.md §7 forbids.";

	EXPECT_NE( std::string::npos, raw.find( "SourceSharp_ShellFromHandle" ) )
		<< "SourceSharp_DrawEntityOverlay no longer resolves through the slot "
		<< "table. A resolver that is not the slot table is a second answer to "
		<< "\"which entity is this\" and does not check the serial.";
}

//-----------------------------------------------------------------------------
// AND THE VIEWER, which is what the six `return;` bodies in
// game/shared/debugoverlay_shared.cpp were missing rather than what they were
// refusing. It is established from a SLOT and an edict, never from an entity
// pointer -- see the header block of shell_debugoverlays.cpp.
//-----------------------------------------------------------------------------
TEST( DebugOverlaysMove, TheFrameStepEstablishesTheOverlayViewer )
{
	const std::string raw = Code( kHome );
	ASSERT_FALSE( raw.empty() );

	EXPECT_NE( std::string::npos, raw.find( "SourceSharp_EstablishOverlayViewer();" ) )
		<< "the frame step no longer establishes a viewer. Without one every "
		<< "clipped overlay entry counts nNoViewer and draws nothing, which is "
		<< "exactly the state 4c removed.";

	// From the player's own eye answer and eye angles, not from a pointer to a
	// class that no longer exists.
	EXPECT_NE( std::string::npos, raw.find( "SourceSharp_ClientViewOrigin" ) )
		<< "the viewer's origin no longer comes from the managed eye answer.";

	EXPECT_EQ( std::string::npos, raw.find( "UTIL_PlayerByIndex" ) )
		<< "shell_debugoverlays.cpp asks UTIL_PlayerByIndex again. It answers "
		<< "NULL for every player this build has (util_shared.cpp says so at "
		<< "length); the slot is the identity both hierarchies carry.";
}
