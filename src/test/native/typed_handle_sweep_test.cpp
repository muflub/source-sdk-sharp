//========= SourceSharp — typed handles out of the server shim (1e) ==========//
//
// plan_entity.md Phase 1 step 1e: "Every typed handle becomes CBaseHandle plus
// a slot-table resolve at the use site."
//
// WHAT A TYPED HANDLE IS AND WHY IT LEAVES. `EHANDLE` is
// `CHandle<CBaseEntity>` and `CHandle<T>::Get()` is
//
//     return (T*)CBaseHandle::Get();                     (game/shared/ehandle.h)
//
// — a C-style cast off whatever the game's ENTITY LIST hands back. Two things
// are wrong with that in the shim, and neither is style:
//
//   THE TABLE IS THE WRONG TABLE. §3.3 puts the shim's identity in the SLOT
//   TABLE, which checks the serial and returns the pointer that was bound.
//   CBaseHandle::Get() reads g_pEntityList instead, which is the table this
//   plan deletes.
//
//   THE CAST IS UNCHECKED. An entry can hold an entity of the other hierarchy.
//   handle_cross.h has the argument in full: the address even LOOKS right,
//   because both hierarchies put IHandleEntity at the front, and what is wrong
//   is every vtable slot past the shared interface.
//
// SO THIS FILE IS THE SWEEP'S LEDGER, and it is source-read because it has to
// be: every subject includes cbase.h's whole world and none of them can be
// linked into a gtest. It is the same instrument entity_list_sweep_test.cpp
// uses for 1d, for the same reason.
//
// THREE KINDS OF FACT HERE, and the first is the only one that cannot be
// evaded:
//
//   1. THE DIRECTORY WALK. Every .cpp under sourcesharp/game/server and
//      sourcesharp/game/shared is read off the FILESYSTEM and required to
//      spell no typed handle. A ledger of file names could be shortened; a
//      directory cannot.
//   2. THE NAMED SITES. What each retyped site says now, one fact each, so a
//      revert is a red test that names the file rather than a number that moved.
//   3. THE COUNT THIS STEP STARTED FROM, recorded so the sweep's size is a fact
//      and not a memory.
//
// tools/entity-census measures the same property over the COMPILED TU list and
// diffs it against docs/entity-census.txt. This is stricter on purpose: it
// covers shim TUs the build does not compile today, which is where a typed
// handle would sit unnoticed until the day a vpc line brought the file in.
//
//=============================================================================

#include <dirent.h>

#include <algorithm>
#include <limits.h>
#include <stdlib.h>
#include <unistd.h>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// The tree, from wherever the leg was started. Same shape as
// entity_gates_test's SrcRoot: "." first, because src/buildcpptests runs its
// legs with src/ as the working directory.
//-----------------------------------------------------------------------------
std::string SrcRoot()
{
	static const char *const kCandidates[] = { ".", "src", "../src", "../../src" };

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		std::string probe = std::string( kCandidates[i] ) + "/sourcesharp/game/server/shell_standalone_base.cpp";
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
// THE CENSUS PATTERN, IMPLEMENTED RATHER THAN APPROXIMATED.
//
// tools/entity-census-patterns spells this family
//
//     EHANDLE   \b(EHANDLE|CHandle\s*<)
//
// and the two halves are not interchangeable. `EHANDLE` needs a word boundary
// BEFORE it and none after, which is why INVALID_EHANDLE_INDEX does not count
// (the underscore is a word character, so there is no boundary) while
// EHANDLE_H does (there is one, and the trailing text is not looked at).
// `CHandle` needs a following `<`, optionally spaced, because `CHandle` on its
// own is the class NAME and naming it in prose is not holding one.
//
// Written out here rather than "just grep for EHANDLE", because the two tools
// disagreeing about what they count is exactly how a gate stops meaning what
// its row label says.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE PATH A READ ACTUALLY RESOLVED TO, for a failure message.
//
// Added by lane 2-physics from lane 2-utilshared's finding: a test helper that
// WALKS UP to find the repo root escapes its worktree into the main checkout,
// and then answers confidently about a different tree. It does not error. A
// worktree's `.git` is a FILE and not a directory, and every tracked file
// exists in both trees, so none of the usual markers tells them apart -- the
// only thing that makes the difference visible is printing the path that was
// actually opened.
//
// This file's own root search does not walk up past one level, so it is not
// exposed today. The point of printing the path is that "not exposed today" is
// a property of the search, and the search is three lines somebody will edit.
//-----------------------------------------------------------------------------
std::string Resolved( const std::string &strRel )
{
	char szBuf[ PATH_MAX ];
	if ( realpath( strRel.c_str(), szBuf ) != NULL )
		return std::string( szBuf );

	// A missing file is often the POINT of the check, so a failed realpath is
	// not an error here: resolve the directory instead and say it is absent.
	char szCwd[ PATH_MAX ];
	if ( getcwd( szCwd, sizeof( szCwd ) ) != NULL )
		return std::string( szCwd ) + "/" + strRel + "   (not present)";

	return strRel + "   (unresolved)";
}

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
// Every .cpp in a shim directory, off the filesystem.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SAME TEXT WITH THE COMMENTS TAKEN OUT.
//
// ADDED AT plan_shim.md PHASE 2a (lane 2-physics), and the reason is the reason
// entity_list_sweep_test already gives for the same split: "the shim stopped
// holding a typed handle" and "somebody wrote a paragraph about one" produce
// the SAME raw number, so the raw number cannot carry the rule on its own.
//
// That was a distinction without a difference while every file under
// sourcesharp/ had been written for this port. It stopped being one when Phase
// 2 began RELOCATING stock files into this tree -- physics.cpp,
// physics_shared.cpp and ragdoll_shared.cpp arrived carrying six comments about
// what the SDK's entity list, CWatcherList and CRagdollLRURetirement hold. Not
// one is a held handle, and each records why something is NOT done, which is
// the history entity_list_sweep_test's own message calls "the only thing
// standing between this file and a re-litigation".
//
// So the ZERO moves to code, where the rule always lived, and the raw count
// gets a fact of its own below. Both directions are still pinned.
//
// AND STRING LITERALS GO WITH THE COMMENTS, which the first run of this split
// found rather than my anticipating it. physics.cpp:525 spells EHANDLE inside a
// CPhysicsBridgeRefusal's REASON TEXT -- the sentence the build prints to say
// why the SDK shadow-entity list is not re-homed ("Re-homing it would carry
// entitem_t's EHANDLE and g_EntListMemPool into a file §6 keeps"). That is a
// diagnostic message, not a held handle, and it is the same kind of recorded
// history as the comments: §7 requires a refusal to SAY what it refuses, so
// naming the type is the point of the sentence.
//
// A matcher that stripped comments and not strings would read the refusal's own
// explanation as a violation of the rule the refusal exists to obey. The
// self-tests below drive both halves.
//-----------------------------------------------------------------------------
std::string StripCommentsAndStrings( const std::string &strIn )
{
	std::string out;
	bool bInBlock  = false;
	bool bInLine   = false;
	bool bInString = false;
	bool bInChar   = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInString || bInChar )
		{
			// A backslash escapes the next character, the closing quote
			// included -- without this an escaped quote ends the literal one
			// character early and the rest of the file reads as code.
			if ( strIn[ i ] == '\\' )
			{
				i++;
				continue;
			}
			if ( bInString && strIn[ i ] == '"' )
				bInString = false;
			else if ( bInChar && strIn[ i ] == '\'' )
				bInChar = false;
			continue;
		}
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( bInLine )
		{
			if ( strIn[ i ] == '\n' )
			{
				bInLine = false;
				out += '\n';
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			bInLine = true;
			i++;
			continue;
		}
		if ( strIn[ i ] == '"' )
		{
			bInString = true;
			continue;
		}
		if ( strIn[ i ] == '\'' )
		{
			bInChar = true;
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

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

const char *const kServerDir   = "sourcesharp/game/server";
const char *const kSharedDir   = "sourcesharp/game/shared";

// cs_sv_handle_cross_check, a debug-only diagnostic since 2026-10-01: only a
// debug build compiles this file (server_sourcesharp_debug_sources).
const char *const kShellBaseDebug = "sourcesharp/game/server/shell_standalone_base_debug.cpp";
const char *const kShellAbsent = "sourcesharp/game/server/shell_absent.cpp";
const char *const kGlobals     = "sourcesharp/game/server/shell_globals.cpp";

} // namespace

//=============================================================================
// 1. THE SWEEP, OVER THE DIRECTORY RATHER THAN OVER A LIST.
//=============================================================================

TEST( TypedHandleSweep, NoServerShimSourceSpellsATypedEntityHandle )
{
	const std::vector< std::string > files = SourcesIn( kServerDir );

	ASSERT_FALSE( files.empty() ) << "found no sources under " << kServerDir
		<< " -- the walk is looking in the wrong place and would pass on an "
		   "empty directory";

	for ( size_t i = 0; i < files.size(); i++ )
	{
		EXPECT_EQ( 0, TypedHandleSpellings( StripCommentsAndStrings( Read( files[i] ) ) ) )
			<< files[i] << " spells a typed entity handle IN CODE. plan_entity.md 1e: "
			   "the shim holds CBaseHandle and resolves through the slot table "
			   "at the use site (SourceSharp_ShellFromHandle / "
			   "SourceSharp_ShellFromIndex / SourceSharp_ShellFromHandleEntity).";
	}
}

TEST( TypedHandleSweep, NoSharedShimSourceSpellsATypedEntityHandle )
{
	const std::vector< std::string > files = SourcesIn( kSharedDir );

	ASSERT_FALSE( files.empty() ) << "found no sources under " << kSharedDir;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		EXPECT_EQ( 0, TypedHandleSpellings( StripCommentsAndStrings( Read( files[i] ) ) ) )
			<< files[i] << " spells a typed entity handle IN CODE.";
	}
}


//-----------------------------------------------------------------------------
// AND THE RAW-TEXT SURFACE, which the two facts above used to carry on their
// own (plan_shim.md Phase 2a, lane 2-physics).
//
// The unit tools/entity-census counts in, so the two instruments can still be
// compared -- which is the whole reason TypedHandleSpellings implements the
// census's pattern rather than approximating it.
//
// FIVE, NONE OF THEM A HELD HANDLE, ALL IN THE THREE FILES PHASE 2
// RELOCATED -- four in comments and one in a refusal's printed reason text. The sweep's own
// rule is "the shim holds CBaseHandle and resolves through the slot table at
// the use site"; a comment describing what the SDK's entity list holds is not a
// violation of it, and the zero above -- now over comment-stripped code -- is
// what says so.
//
// SIX UNTIL plan_shim.md 4a item 3 (lane 3a), and it went DOWN for the reason
// the paragraph below names: "a comment recording why something is NOT done was
// deleted". The sixth was ragdoll_shared.cpp's note that CRagdollLRURetirement's
// m_LRU holds CHandle<CBaseAnimating>, and the class it described is gone --
// its only writer, MoveToTopOfLRU, had zero importers on either link line, so
// the list was permanently empty. A note about a member of a deleted class is
// not a note anybody needs. ragdoll_shared.cpp kept its ROW at zero until the
// file itself was deleted (plan_physics_bones_native_zero Phase 3); a carrier
// that is not on disk cannot be opened, so its row went with it.
//
// PINNED IN BOTH DIRECTIONS, like entity_list_sweep_test's twin of this fact.
// If it goes UP: either a handle came back (which the zeroes above catch), or a
// paragraph was written, or another stock file was relocated into this tree
// carrying comments it already had. If it goes DOWN: a comment recording why
// something is NOT done was deleted.
//-----------------------------------------------------------------------------
TEST( TypedHandleSweep, TheOnlyRawSpellingsLeftAreProseInTheRelocatedFiles )
{
	struct Carrier_t { const char *pszFile; int nRaw; };
	static const Carrier_t kCarriers[] =
	{
		// physics.cpp:467  the SDK list's member is an EHANDLE
		// physics.cpp:497  the refusal message naming what it would drag in
		// physics_debug.cpp:749 the list this file exists to stop being an EHANDLE
		//   -- MOVED, not lost: CConstraintFloodList went with physics_budget into
		//   the debug-only file (2026-10-01), its comment with it. Total unchanged.
		//
		// 4 -> 3, AND THE SPELLING THAT WENT IS NAMED, because a bare lowered
		// number is how a lost one hides. It was PhysicsRefuseEntitySolver's
		// banner -- "CLogicalEntity holds two EHANDLEs", describing the
		// CPhysicsEntitySolver that refusal declined to re-home. plan_shim.md
		// §4a item 15 deleted the function: its banner claimed "ONE refusal
		// object for TWO call sites", but ShouldFreezeObject had already been
		// reduced to a plain `return true` by an earlier lane, so its only real
		// caller was UpdatePenetrateEvents -- which went with the penetration
		// rule set, measured unreachable (19,302 pairs into
		// ShouldSolvePenetration, 0 past its owner guard).
		//
		// So the prose went with the code it was about, which is the ONE way
		// this count is allowed to fall. The other three are unmoved, and the
		// line numbers above are re-measured rather than carried.
		{ "sourcesharp/game/server/physics.cpp",        2 },
		// physics_debug.cpp's row went 1 -> 0 at plan_physics_bones_native_zero
		// 2.3 (physics_budget's note left with the command), and the file
		// itself on 2026-10-18 (its commands are managed's
		// PhysicsDebugCommands.cs). The directory walk below covers it if it
		// comes back.
		// physics_shared.cpp's row (1: CWatcherList holds EHANDLEs) went with
		// the file (plan_physics_bones_native_zero 2.6: its decisions are
		// managed's; the client remnant carries no such prose).
		// ragdoll_shared.cpp's zero row went with the file
		// (plan_physics_bones_native_zero Phase 3: its primitives are
		// SourceSharp.Client's RagdollSolver). The directory walk below still
		// covers every shared source that remains.
	};
	const size_t nCarriers = sizeof( kCarriers ) / sizeof( kCarriers[ 0 ] );

	int nTotal = 0;
	for ( size_t i = 0; i < nCarriers; i++ )
	{
		const std::string raw = Read( kCarriers[ i ].pszFile );
		ASSERT_FALSE( raw.empty() )
			<< kCarriers[ i ].pszFile << " did not open, so its count below is "
			   "the reader's and not the tree's. Resolved to: "
			<< Resolved( SrcRoot() + "/" + kCarriers[ i ].pszFile );

		const int nRaw = TypedHandleSpellings( raw );
		EXPECT_EQ( kCarriers[ i ].nRaw, nRaw ) << kCarriers[ i ].pszFile;

		// THE HALF THAT MAKES THE LINE ABOVE MEAN SOMETHING: every one of them
		// is in a comment. Raw minus stripped is the prose count, and it has to
		// be all of it.
		EXPECT_EQ( 0, TypedHandleSpellings( StripCommentsAndStrings( raw ) ) )
			<< kCarriers[ i ].pszFile << " holds a typed handle in CODE; the raw "
			   "count above is no longer all comment and refusal text";

		nTotal += nRaw;
	}

	// 5 -> 4 for the single reason named on the physics.cpp row above, and
	// 4 -> 3 for the one named on the physics_debug.cpp row, and 3 -> 2 for
	// physics_shared.cpp's row leaving with the file.
	EXPECT_EQ( 2, nTotal );

	// AND NO OTHER SHIM SOURCE CARRIES ONE, so the three rows above are the
	// whole population rather than three files somebody happened to list.
	const char *const kDirs[] = { kServerDir, kSharedDir };
	for ( size_t d = 0; d < 2; d++ )
	{
		const std::vector< std::string > files = SourcesIn( kDirs[ d ] );
		ASSERT_FALSE( files.empty() ) << "found no sources under " << kDirs[ d ];

		for ( size_t i = 0; i < files.size(); i++ )
		{
			bool bListed = false;
			for ( size_t c = 0; c < nCarriers; c++ )
				bListed = bListed || ( files[ i ] == kCarriers[ c ].pszFile );
			if ( bListed )
				continue;

			EXPECT_EQ( 0, TypedHandleSpellings( Read( files[ i ] ) ) )
				<< files[ i ] << " has grown a raw EHANDLE/CHandle<> spelling and "
				   "is not one of the three relocated files above. If it is prose, "
				   "add it here with its line; if it is code, the zero sweep has "
				   "already told you.";
		}
	}
}

//-----------------------------------------------------------------------------
// THE MATCHER ITSELF CAN FAIL, and this is what says so.
//
// Every fact above is an EXPECT_EQ against zero, and a matcher that answered
// zero for everything would make all of them pass on a tree that had not been
// swept at all -- the "check that cannot fail" shape. So the matcher is given
// text it must count and text it must not.
//-----------------------------------------------------------------------------
TEST( TypedHandleSweep, TheMatcherCountsWhatTheCensusPatternCounts )
{
	EXPECT_EQ( 1, TypedHandleSpellings( "EHANDLE m_hEnt;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CUtlVector< EHANDLE > m_List;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CHandle<CBaseEntity> h;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "CHandle < IGameEntity > h;" ) );
	EXPECT_EQ( 1, TypedHandleSpellings( "EHANDLE_H" ) );
}

//-----------------------------------------------------------------------------
// AND SO CAN THE STRIPPER, which is now doing half the work.
//
// StripCommentsAndStrings decides what "in code" means for both zero sweeps
// above. A stripper that returned the empty string would make every one of them
// pass over a tree full of held handles; one that stripped nothing would fail
// on the six lines of recorded history Phase 2 relocated in. So it is driven
// both ways, on the shapes that actually occur in the three relocated files.
//-----------------------------------------------------------------------------
TEST( TypedHandleSweep, TheStripperRemovesCommentsAndStringsAndNothingElse )
{
	// GONE: a line comment, a block comment, and a string literal -- the three
	// places the six surviving spellings live.
	EXPECT_EQ( 0, TypedHandleSpellings(
		StripCommentsAndStrings( "// the member is an EHANDLE\n" ) ) );
	EXPECT_EQ( 0, TypedHandleSpellings(
		StripCommentsAndStrings( "/* m_LRU holds CHandle<CBaseAnimating> */" ) ) );
	EXPECT_EQ( 0, TypedHandleSpellings(
		StripCommentsAndStrings( "Refuse( \"would carry entitem_t's EHANDLE\" );" ) ) );

	// KEPT: a real declaration, which is the whole subject of the sweep.
	EXPECT_EQ( 1, TypedHandleSpellings(
		StripCommentsAndStrings( "EHANDLE m_hEnt;" ) ) );
	EXPECT_EQ( 1, TypedHandleSpellings(
		StripCommentsAndStrings( "CHandle<CBaseEntity> m_h;" ) ) );

	// KEPT ACROSS A STRING, which is the failure an unterminated-literal bug
	// produces: one bad quote and the rest of the file reads as string.
	EXPECT_EQ( 1, TypedHandleSpellings(
		StripCommentsAndStrings( "Msg( \"EHANDLE\" ); EHANDLE m_hEnt;" ) ) );

	// AN ESCAPED QUOTE DOES NOT END THE LITERAL. Without the escape arm this
	// reads as code from the backslash onward and the trailing EHANDLE inside
	// the string is counted.
	EXPECT_EQ( 0, TypedHandleSpellings(
		StripCommentsAndStrings( "Msg( \"a \\\" quote then EHANDLE\" );" ) ) );

	// AND THE STRIPPER DOES NOT SIMPLY EMPTY THE TEXT, which is what would make
	// every zero above vacuous.
	EXPECT_FALSE( StripCommentsAndStrings( "int x = 1; // note\n" ).empty() );
}

TEST( TypedHandleSweep, TheMatcherDoesNotCountTheSpellingsTheCensusLeavesAlone )
{
	// No word boundary before EHANDLE: the underscore is a word character.
	EXPECT_EQ( 0, TypedHandleSpellings( "INVALID_EHANDLE_INDEX" ) );
	EXPECT_EQ( 0, TypedHandleSpellings( "NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS" ) );

	// CHandle without a following '<' is the class name, not a handle.
	EXPECT_EQ( 0, TypedHandleSpellings( "the typed CHandle route" ) );

	// CBaseHandle is what the sweep puts IN. Counting it would make the sweep
	// unfinishable.
	EXPECT_EQ( 0, TypedHandleSpellings( "CBaseHandle m_hEnt;" ) );
}

//=============================================================================
// 2. THE NAMED SITES.
//
// One fact per site. A count going back up says the sweep regressed; these say
// WHERE, and what the site is supposed to read.
//=============================================================================

// TheSoundPatchHoldsAnUntypedHandle and TheSoundPatchResolvesItsEntityThroughTheSlotTable
// stood here. Their subject, the native CSoundPatch in shell_soundenvelope.cpp,
// is ServerSoundPatches (C#) since plan_server_native_v2 T4a: it holds the
// entity's EntityHandle and asks the managed entity area whether it is valid,
// so there is no C++ handle spelling left to pin (ServerSoundsTests).

// TheUserMessageEHandleAsksTheEntityForItsOwnHandle stood here. Its subject,
// MessageWriteEHandle, was deleted with the other sixteen MessageWrite*
// writers in plan_server_novalve.md P2.2; nothing sends an ehandle in a user
// message natively any more.

TEST( TypedHandleSweep, TheVariantEHandleFieldIsReadAsAnUntypedHandle )
{
	const std::string src = Read( kGlobals );
	ASSERT_FALSE( src.empty() );

	//
	// A SITE THE FLIP ADDED, AND THE DIRECTORY WALK IS WHY IT WAS FOUND.
	//
	// `variant_t::Set` moved into this shim during the armed-link work
	// (fb324d0ef), carrying stock's line verbatim:
	//
	//     case FIELD_EHANDLE:  eVal = *((EHANDLE *)data);
	//
	// `EHANDLE` is `CHandle<CBaseEntity>`, so that is a TYPED handle in a shim
	// .cpp -- the one thing NoServerShimSourceSpellsATypedEntityHandle forbids
	// outright. The lane that landed it quoted entity_list_sweep_test and
	// variant_convert_test green in its own handover and did not run this one.
	// This is the file-naming half the header asks for: "a count going back up
	// says the sweep regressed; these say WHERE".
	//
	// The value in a datamap FIELD_EHANDLE slot is an index and a serial and
	// nothing else, so it is read as the untyped handle it is and assigned to
	// eVal's own CBaseHandle subobject. Same bytes, no typed spelling -- and
	// above all no CBaseHandle::Get() at this site, which is the half of §1e
	// that is about the TABLE rather than about the cast.
	EXPECT_TRUE( Has( src, "static_cast< CBaseHandle & >( eVal ) = *((CBaseHandle *)data);" ) )
		<< "variant_t::Set no longer copies the FIELD_EHANDLE slot through the "
		   "untyped base. If it has gone back to `eVal = *((EHANDLE *)data)` "
		   "the directory walk above will say so too; this says which line.";

	// And the OTHER arm of the same switch, untouched, so this fact cannot be
	// satisfied by the whole conversion having been deleted.
	EXPECT_TRUE( Has( src, "case FIELD_CLASSPTR:" ) )
		<< "variant_t::Set has lost its FIELD_CLASSPTR arm, so the EHANDLE arm "
		   "above is being read out of a function that no longer converts "
		   "anything.";
}

// TheRestoreListHoldsUntypedHandles and TheRestoreWalkResolvesNothingAnd...
// went with shell_restoreentities.cpp itself (plan_server_native_min P1):
// the whole restore path -- the list, the walk and the loud tail -- is
// deleted, inert since BeginRestoreEntities lost its definition. The facts
// the tests kept alive come back with the managed save/restore codec
// (plan_server_native_min P3c), which writes its own.

// IT IS ONE STUB NOW, NOT TWO, and the second is asserted ABSENT rather than
// dropped from the fact.
//
// plan_entity.md step 4f re-measured shell_ai_absent.cpp against
// `tools/4a-owes` and deleted the 140 definitions nothing on the server link
// line named. InstancedAutoGeneratedSoundScene was one of them: its callers are
// game/server/sceneentity.cpp and game/server/baseflex.cpp, neither of which
// server_csharp.vpc compiles.
//
// Keeping the row at "absent" rather than deleting it is the same disposition
// cpp_alias_shard_t4_test.cpp's in_main.cpp floor takes at zero: silently
// dropping a row is how a fact stops checking the thing it was written for. If
// the stub comes back, this goes red and whoever brings it back has to
// re-derive its out-parameter spelling. (TheSceneEntityHeaderAgreesWithThe-
// StubItDeclares, which pinned sceneentity.h's declaration as untyped, retired
// when that header left the farm in plan_server_novalve.md P6.)
TEST( TypedHandleSweep, BothAbsentSceneStubsAreGoneFromTheShim )
{
	// IT IS NO STUBS NOW, AND THE PRESENCE HALF RETIRED WITH ITS SUBJECT.
	//
	// This fact used to require InstancedScriptedScene to be DEFINED in
	// shell_ai_absent.cpp, spelling its out parameter as an untyped
	// CBaseHandle*. Phase 1b deleted that definition: no object on the server
	// link line imported it (`./1b-census.sh sym server InstancedScriptedScene`
	// -> 0, on a cold build over the 138 objects the ninja graph's link line
	// names), which is the same measurement that took
	// InstancedAutoGeneratedSoundScene at step 4f.
	//
	// So the row stays at "absent" for BOTH, which is the disposition the
	// paragraph above already argued for the second one and which is now the
	// only one available for the first. The claim this file exists to make is
	// unchanged and STRONGER: no compiled server shim translation unit spells a
	// typed entity handle in a scene stub, because there is no scene stub.
	//
	// TheSceneEntityHeaderAgreesWithTheStubItDeclares used to pin
	// sceneentity.h's declaration as untyped; it retired when that header left
	// the farm (plan_server_novalve.md P6), and the absences below stand alone.
	const std::string src = Read( kShellAbsent );
	ASSERT_FALSE( src.empty() );

	EXPECT_FALSE( Has( src, "float InstancedScriptedScene(" ) )
		<< "the absent-AI scene stub is back in shell_absent.cpp. Nothing on "
		   "the server link line named it when Phase 1b deleted it; if a caller "
		   "has returned, re-derive the out-parameter spelling from "
		   "sceneentity.h rather than restoring the old body.";

	EXPECT_FALSE( Has( src, "float InstancedAutoGeneratedSoundScene(" ) )
		<< "the absent-AI sound-scene stub is back in shell_absent.cpp. "
		   "Nothing on the server link line named it when step 4f deleted it "
		   "(sceneentity.cpp and baseflex.cpp are not in server_csharp.vpc); if "
		   "a caller has returned, re-derive the out-parameter spelling from "
		   "sceneentity.h rather than restoring the old body.";
}

//-----------------------------------------------------------------------------
// TheSceneEntityHeaderAgreesWithTheStubItDeclares IS RETIRED. It pinned
// sceneentity.h's two declarations as spelling an untyped CBaseHandle *
// out-parameter, so a returning stub would have to match. game/server/
// sceneentity.h is compiled by nothing since plan_server_novalve.md P5 and left
// the farm in P6: no stub that comes back can be declared by it, so there is no
// spelling left for it to govern.
//-----------------------------------------------------------------------------

//=============================================================================
// 3. THE LIVE CHECK THE SWEEP REPLACED ITS LAST TYPED HANDLE WITH.
//
// cs_sv_handle_cross_check used to build a CHandle over IGameEntity and compare
// its Get() with the compiler's own crossing. That was the last typed handle in
// the server shim, and deleting it would have deleted the only thing asking the
// REAL class -- on real entities, in a running game -- whether the resolver
// agrees with the compiler. So it was repointed rather than removed, and it now
// exercises BOTH arms of the slot table.
//=============================================================================

TEST( TypedHandleSweep, TheLiveCrossCheckAsksTheSlotTableAndNotATypedHandle )
{
	const std::string src = Read( kShellBaseDebug );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SS_NATIVE_COMMAND( cs_sv_handle_cross_check )" );
	ASSERT_NE( std::string::npos, at ) << "cs_sv_handle_cross_check is gone. It "
		"is the only live check that the slot table hands back the pointer that "
		"was bound, on the real class rather than on a mock.";

	const std::string cmd = src.substr( at );

	EXPECT_TRUE( Has( cmd, "SourceSharp_ShellFromHandle( handle )" ) )
		<< "the by-handle arm is no longer exercised.";

	EXPECT_TRUE( Has( cmd, "SourceSharp_ShellFromIndex( handle.GetEntryIndex() )" ) )
		<< "the BY-INDEX arm is no longer exercised. Nothing else in a live "
		   "run reaches it: CSoundPatch::Build is its only caller and needs a "
		   "looping sound started by entity index.";
}

TEST( TypedHandleSweep, TheLiveCrossCheckSaysWhichCallersGoQuietWhenNothingIsBound )
{
	const std::string src = Read( kShellBaseDebug );
	ASSERT_FALSE( src.empty() );

	const size_t at = src.find( "SS_NATIVE_COMMAND( cs_sv_handle_cross_check )" );
	ASSERT_NE( std::string::npos, at );

	const std::string cmd = src.substr( at );

	EXPECT_TRUE( Has( cmd, "NOTHING is bound in the slot table" ) )
		<< "the unbound-table warning is gone. `bound 0` is the state 1c and "
		   "1d both predicted and it reads as silence everywhere else: no "
		   "touch is delivered, no sound is attached, no prop is simulated. "
		   "The one place it should be a sentence is here.";

	EXPECT_TRUE( Has( cmd, "Toucher_t::pGame" ) )
		<< "the warning no longer names the callers that go quiet, which is "
		   "the whole of what makes it actionable.";
}

//=============================================================================
// 4. WHAT THIS STEP STARTED FROM.
//
// Not a gate -- a record, so the size of the sweep is a measurement somebody
// can re-take rather than a sentence in a handover. The number came from the
// census on the commit this lane branched from.
//=============================================================================

TEST( TypedHandleSweep, TheStepStartedFromThirtyFourSpellingsInSeventeenFiles )
{
	// The seventeen files the census named, and what each one held. Re-read
	// here from the tree at zero: the pairing is what makes the "34 in 17"
	// claim checkable rather than recited.
	static const char *const kWas[] = {
		"sourcesharp/game/server/shell_absent.cpp",
		"sourcesharp/game/server/shell_entityapi.cpp",
		"sourcesharp/game/server/shell_gameents.cpp",
		// shell_restoreentities.cpp is DELETED, not swept (plan_server_native_min
		// P1): the whole restore path went, so there is no handle spelling left
		// to count.
		"sourcesharp/game/server/shell_soundent.cpp",
		// shell_soundenvelope.cpp is DELETED, not swept (plan_server_native_v2
		// T4a): the server's sound patch is managed.
		"sourcesharp/game/server/shell_standalone_base.cpp",
		"sourcesharp/game/server/shell_standalone_hierarchy.cpp",
		"sourcesharp/game/server/shell_te_forwards.cpp",
		"sourcesharp/game/server/shell_usermessage_send.cpp",
		"sourcesharp/game/server/shell_worldquery.cpp",
		"sourcesharp/game/shared/shell_gamerules.cpp",
		// shell_savegame.cpp is DELETED, not swept (the client save codec):
		// the client's save cluster forwards to the managed SaveBlockSession.
		"sourcesharp/game/shared/sourcesharp_gamesystem.cpp",
	};

	const size_t nFiles = sizeof( kWas ) / sizeof( kWas[0] );
	// 17 -> 15: gameentity_handle.cpp is DELETED, not swept; so is
	// shell_restoreentities.cpp (plan_server_native_min P1). Its one function is
	// inline in public/networkvar.h now (plan_entity.md's Phase 1 exit names
	// `gameentity_handle.*` among the things that are gone), and networkvar.h is
	// an engine prelude full of CHandle<> declarations -- it cannot stand in for
	// the file in a list of shim TUs that must hold no typed handle.
	// 16 -> 15: shell_constraints.cpp is DELETED, not swept: the constraint
	// table is managed's (plan_physics_bones_native_zero 2.4).
	// 15 -> 14: shell_motioncontrollers.cpp likewise (managed's
	// PhysicsMotionControllers, 2.4).
	// 14 -> 13: shell_restoreentities.cpp likewise (plan_server_native_min P1).
	// 13 -> 12: shell_soundenvelope.cpp likewise (plan_server_native_v2 T4a).
	// 12 -> 11: shell_savegame.cpp likewise (the client save codec).
	EXPECT_EQ( (size_t)11, nFiles );

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
