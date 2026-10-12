//=============================================================================//
// THE SWITCH, and the three things about it that are invisible to every other
// gate in this tree. plan_gamedll.md Phase 4, and docs/gamedll-ledger.md's
// corrections to it.
//
// The engine now finds CShellGameDll when it asks for "ServerGameDLL012".
// Three properties make that safe rather than catastrophic, and NONE of them
// is a link error, a compile error, or a count `strings` can produce:
//
//   1. EXACTLY ONE CLASS REGISTERS EACH GAME-DLL INTERFACE. Two InterfaceReg
//      entries for one version string both link. Which one CreateInterface
//      hands the engine is decided by static-initialisation order across
//      translation units -- a silent, nondeterministic wrong answer.
//
//   2. THE GUARD IS ON THE REGISTRATION AND NOT ON THE CLASS. Phase 4's text
//      asks for both `#ifndef SOURCESHARP_SHELL_GAMEDLL` around
//      CServerGameDLL's class body AND a `CServerGameDLL *` delegate on the
//      shell (T4). Those are mutually exclusive; a pointer needs a complete
//      type with a definition to link. Guarding only the EXPOSE_ satisfies the
//      gate the phase actually states and keeps the delegate, which is what
//      makes Phases 5-7 A/B-testable on one binary.
//
//   3. THE SHELL ASSIGNS g_pSharedChangeInfo. Stock assigns it inside
//      CServerGameDLL::DLLInit. Its reader is CBaseEdict::StateChanged
//      (public/edict.h:295), an INLINE on the entity-networking path that
//      dereferences it unconditionally. A shell that owns DLLInit and does not
//      assign it is a null dereference on the first networked state change --
//      which is not reachable offline at all, so this file pins the assignment
//      instead. That is a weaker proof than running it and it is stated as
//      what it is.
//
// SOURCE-READ RATHER THAN RUN, and that is forced rather than chosen:
// shell_gamedll.cpp includes gameinterface.h, eiface.h and cbase.h's world, so
// there is no way to link its bodies into a gtest binary. What can be checked
// without an engine is the ARRANGEMENT, and every check here is one a reader
// could get wrong by eye -- which of two adjacent lines comes first, whether a
// method that was added last week got its branch, whether an EXPOSE_ is inside
// a guard or two lines below its #endif.
//
// The binary half of property 1 is tools/gamedll-interface-census, which reads
// the symbol table of the built server.so and cannot be run from here.
//=============================================================================//

#include <algorithm>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>
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
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

// CODE ONLY. Every comment in this phase's files NAMES the symbols it is
// explaining -- "g_pSharedChangeInfo", "EXPOSE_SINGLE_INTERFACE_GLOBALVAR",
// "m_pSharedChangeInfo IS GONE" -- so a raw-text search would find the prose
// and pass on a file whose code says the opposite. Worse, it would pass on a
// commented-out EXPOSE_, which is precisely the state Phases 1-3 were in.
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

std::string ReadRaw( const std::string &strPath )
{
	std::ifstream f( strPath.c_str(), std::ios::binary );
	if ( !f.is_open() )
		return std::string();
	return std::string( ( std::istreambuf_iterator<char>( f ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string Read( const std::string &strPath )
{
	return StripComments( ReadRaw( strPath ) );
}

std::string ReadSrc( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( pRoot == NULL )
		return std::string();
	return Read( std::string( pRoot ) + "/" + pRelative );
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos;
	      at = strHay.find( strNeedle, at + 1 ) )
		n++;
	return n;
}

// The -D definitions of one compile command, as NAME or NAME=VALUE, matched
// whole: a set lookup cannot be satisfied by a prefix, which is the failure the
// old VPC directive matcher had to guard against by hand.
std::set< std::string > DefinesOf( const std::string &strCommand )
{
	std::set< std::string > out;
	std::istringstream toks( strCommand );
	std::string strTok;
	while ( toks >> strTok )
		if ( strTok.compare( 0, 2, "-D" ) == 0 && strTok.size() > 2 )
			out.insert( strTok.substr( 2 ) );
	return out;
}

// The half-open [ #ifndef SOURCESHARP_SHELL_GAMEDLL, matching #endif ) ranges of
// a comment-stripped file, nesting-aware. "Inside the guard" is the whole
// question this file asks about four different lines, and eyeballing which
// #endif closes which #if is exactly what a reader gets wrong.
std::vector< std::pair< size_t, size_t > > ShellGuardRanges( const std::string &s )
{
	std::vector< std::pair< size_t, size_t > > out;
	size_t at = 0;
	while ( ( at = s.find( "#ifndef SOURCESHARP_SHELL_GAMEDLL", at ) ) != std::string::npos )
	{
		int nDepth = 1;
		size_t scan = at + 1;
		size_t end = std::string::npos;
		while ( nDepth > 0 && scan < s.size() )
		{
			const size_t atIf   = s.find( "\n#if", scan );
			const size_t atEnd  = s.find( "\n#endif", scan );
			if ( atEnd == std::string::npos )
				break;
			if ( atIf != std::string::npos && atIf < atEnd )
			{
				nDepth++; scan = atIf + 1; continue;
			}
			nDepth--; scan = atEnd + 1;
			if ( nDepth == 0 )
				end = atEnd;
		}
		out.push_back( std::make_pair( at, end == std::string::npos ? s.size() : end ) );
		at = at + 1;
	}
	return out;
}

bool InsideShellGuard( const std::string &s, size_t at )
{
	const std::vector< std::pair< size_t, size_t > > r = ShellGuardRanges( s );
	for ( size_t i = 0; i < r.size(); i++ )
		if ( at > r[ i ].first && at < r[ i ].second )
			return true;
	return false;
}

bool SrcExists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

const char *kShell   = "sourcesharp/game/server/shell_gamedll.cpp";
const char *kStockCpp = "game/server/gameinterface.cpp";
const char *kStockH   = "game/server/gameinterface.h";

} // namespace


//=============================================================================//
// Property 1 -- one registering implementation per interface
//=============================================================================//

// FOLLOWED THE WORK, AND INVERTED. This used to assert that stock's own
// EXPOSE_ sat INSIDE #ifndef SOURCESHARP_SHELL_GAMEDLL, because the class was
// still compiled and a second live registration of "ServerGameDLL012" would be
// resolved by static-init order rather than by an error.
//
// plan_gamedll_after.md Phase A9 step 3 deleted stock's CServerGameDLL --
// class, instance and registration -- so there is no second registration to
// guard and no guard to check. What replaces the fact is the stronger one:
// NOBODY registers IServerGameDLL except CShellGameDll, whatever the state of
// that macro. That is checked against the object files as well, by
// tools/gamedll-interface-census, which reads the built .so.
TEST( ShellGameDllRegistration, NothingButTheShellRegistersServerGameDll )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 1, CountOf( s, "INTERFACEVERSION_SERVERGAMEDLL," ) )
		<< "shell_gamedll.cpp does not register INTERFACEVERSION_SERVERGAMEDLL "
		   "exactly once";

	// And in the file that used to hold the other one, if it is still there at
	// all. Step 4 of the same phase deletes it; until then a resurrected
	// registration is exactly the silent duplicate this fact has always been
	// about.
	if ( SrcExists( kStockCpp ) )
	{
		const std::string old = ReadSrc( kStockCpp );
		EXPECT_EQ( 0, CountOf( old, "INTERFACEVERSION_SERVERGAMEDLL" ) )
			<< "gameinterface.cpp registers ServerGameDLL012 again";
	}
}

TEST( ShellGameDllRegistration, TheShellRegistersItselfAndTheLineIsNotCommentedOut )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// The factory's row for it (shell_factory.cpp reads it), and the object
	// the row answers with, cast to the interface the engine asked for.
	EXPECT_EQ( 1, CountOf( s,
		"const SourceSharpExposed kExposeServerGameDll = { INTERFACEVERSION_SERVERGAMEDLL, "
		"SourceSharp_GetServerGameDll };" ) )
		<< "the shell's own registration must be live. This is read from the "
		   "COMMENT-STRIPPED file on purpose: from Phase 1 to Phase 3 the "
		   "registration was present and commented out, and a raw-text search "
		   "passes on both states";
	EXPECT_EQ( 1, CountOf( s, "return (IServerGameDLL *)&s_gameShellDll;" ) )
		<< "the row must answer the shell object as IServerGameDLL";
}

TEST( ShellGameDllRegistration, TheShellRegistersNothingElseYet )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// Part 1's end state is seven EXPOSE_ lines on this class. It is DEFERRED,
	// and this fact is what makes the deferral a decision rather than a state
	// nobody noticed. Taking ServerGameTags001 used to be the one that bit: its
	// EXPOSE_SINGLE_INTERFACE was in gameinterface.h, so SIX translation units
	// registered it -- harmless while all six were CServerGameTags, and a
	// silent static-init race the moment a seventh was a different class.
	// plan_gamedll_after.md Phase A9 step 2 moved that EXPOSE_ into
	// shell_gametags.cpp and the count is one, so the hazard is now the plain
	// one: a SECOND registration of a version string this class already answers.
	// The EXPOSE_ macros are gone from server.so: the factory is one table
	// (shell_factory.cpp), and a file answers a name by defining its row.
	EXPECT_EQ( 1, CountOf( s, "const SourceSharpExposed " ) )
		<< "the shell answers ServerGameDLL012 and nothing else this phase";
	EXPECT_EQ( 0, CountOf( s, "EXPOSE_SINGLE_INTERFACE" ) )
		<< "an EXPOSE_ macro is back in the shell; server.so has no registry for it";
}

TEST( ShellGameDllRegistration, TheOtherFourInterfacesAreStillStocksAndAreNotGuarded )
{
	// gameinterface.cpp is deleted by step 4 of the same phase. Until then it
	// is read and asserted empty of registrations; after, there is nothing to
	// read. The header that carried the "not from a header" half has since left
	// the farm too (TheStockHeaderHasLeftTheFarm).
	const std::string sCpp = SrcExists( kStockCpp ) ? ReadSrc( kStockCpp ) : std::string();

	// Guarding these would take four interfaces out of the build with nothing
	// answering them: no player lifecycle, no CheckTransmit, no server-browser
	// tags, and no soundemittersystem/scenefilecache app-system bootstrap.
	//
	// THEY ARE NO LONGER IN gameinterface.cpp OR gameinterface.h. Every one of
	// the four moved to a file of its own at plan_gamedll_after.md Phase A9,
	// which is why each row below carries the file it is now in. The PROPERTY
	// is unchanged and is the whole point of the row: the registration is live,
	// exactly once, and is NOT inside #ifndef SOURCESHARP_SHELL_GAMEDLL.
	//
	// gameinterface.cpp is asserted to hold NONE of them, below, so this cannot
	// pass by reading a stale second copy. gameinterface.h's half is
	// TheStockHeaderHasLeftTheFarm: the header is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6, so no TU can register
	// anything through it.
	struct Reg_t { const char *pszFile; const char *pszLine; };
	static const Reg_t kStillStock[] = {
		{ "sourcesharp/game/server/shell_gameclients.cpp",
		  "const SourceSharpExposed kExposeServerGameClients003 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3," },
		{ "sourcesharp/game/server/shell_gameclients.cpp",
		  "const SourceSharpExposed kExposeServerGameClients004 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_4," },
		{ "sourcesharp/game/server/shell_gameclients.cpp",
		  "const SourceSharpExposed kExposeServerGameClients = { INTERFACEVERSION_SERVERGAMECLIENTS," },
		{ "sourcesharp/game/server/shell_gameents.cpp",
		  "const SourceSharpExposed kExposeServerGameEnts = { INTERFACEVERSION_SERVERGAMEENTS," },
		{ "sourcesharp/game/server/shell_sharedappsystems.cpp",
		  "const SourceSharpExposed kExposeServerDllSharedAppSystems = { SERVER_DLL_SHARED_APPSYSTEMS," },
		{ "sourcesharp/game/server/shell_gametags.cpp",
		  "const SourceSharpExposed kExposeServerGameTags = { INTERFACEVERSION_SERVERGAMETAGS," },
	};

	for ( size_t i = 0; i < sizeof( kStillStock ) / sizeof( kStillStock[ 0 ] ); i++ )
	{
		const std::string s = ReadSrc( kStillStock[ i ].pszFile );
		ASSERT_FALSE( s.empty() ) << kStillStock[ i ].pszFile << " is unreadable";
		EXPECT_EQ( 1, CountOf( s, kStillStock[ i ].pszLine ) )
			<< kStillStock[ i ].pszFile << " does not register "
			<< kStillStock[ i ].pszLine << " exactly once";
		const size_t at = s.find( kStillStock[ i ].pszLine );
		ASSERT_NE( std::string::npos, at ) << kStillStock[ i ].pszLine;
		EXPECT_FALSE( InsideShellGuard( s, at ) ) << kStillStock[ i ].pszLine;
	}

	// AND NOT A SECOND COPY IN THE OLD HOMES. Two InterfaceReg entries for one
	// version string do not collide at link time; static-init order picks one.
	//
	// gameinterface.cpp is asserted to register exactly ONE thing and it is
	// stock's own IServerGameDLL, inside #ifndef SOURCESHARP_SHELL_GAMEDLL --
	// the line TheStockRegistrationIsGuarded above is about. Counting to zero
	// here would be counting that one too, and it is not this fact's subject.
	for ( size_t i = 0; i < sizeof( kStillStock ) / sizeof( kStillStock[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0, CountOf( sCpp, kStillStock[ i ].pszLine ) )
			<< "gameinterface.cpp registers " << kStillStock[ i ].pszLine
			<< " again -- a copy rather than a move";
	}
	EXPECT_EQ( 0, CountOf( sCpp, "EXPOSE_SINGLE_INTERFACE" ) )
		<< "gameinterface.cpp registers an interface again. After A9 step 3 it "
		   "registers NOTHING: the last one was stock's own IServerGameDLL, "
		   "which went with the class";
}

// WAS three assertions over game/server/gameinterface.h: that it registered
// none of the six moved interfaces, held no EXPOSE_SINGLE_INTERFACE (from a
// HEADER that is one registration per INCLUDING translation unit -- six of
// them for ServerGameTags001 before A9 step 2), and declared no
// CServerGameDLL. gameinterface.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, which is the stronger
// form of all three: nothing can register through a header no TU includes.
// The absence is ANCHORED -- SrcExists answers false for a tree it never found
// -- by a sibling in the same directory that must exist and read as itself.
TEST( ShellGameDllRegistration, TheStockHeaderHasLeftTheFarm )
{
	const char *const kSibling = "game/server/entity_funcs.h";
	ASSERT_TRUE( SrcExists( kSibling ) ) << kSibling << " is not in the farm, so "
		"the absence below is not evidence";
	ASSERT_EQ( 1, CountOf( ReadSrc( kSibling ), "#ifndef ENTITY_FUNCS_H" ) )
		<< kSibling << " did not read as itself";

	EXPECT_FALSE( SrcExists( kStockH ) )
		<< kStockH << " is in the farm again. Nothing has compiled it since "
		   "plan_server_novalve.md P5; if a target needs it back, check it "
		   "registers none of the moved interfaces and declares no CServerGameDLL.";
}

TEST( ShellGameDllRegistration, TheDefineIsInTheProjectBesideSOURCESHARP )
{
	// The guard only guards if the macro is defined for this project, and it is
	// defined in exactly one place. A #ifndef whose macro nothing defines is
	// inert and reads identically to one that works.
	//
	// RE-POINTED IN PHASE 6b. This read server_csharp.vpc's
	// $PreprocessorDefinitions directive, with a hand-built matcher (and two
	// self-checks of it) because a VPC define list is one quoted string: the
	// matcher had to see SOURCESHARP_SHELL_GAMEDLL beside SOURCESHARP, not pin
	// the tabs, not pin the rest of the list, and not accept a longer name that
	// begins with this one. The define now lives in server_sourcesharp's
	// target_compile_definitions block (src/server/native/CMakeLists.txt), one
	// name per token, so each of those hazards is a set lookup: exact names,
	// order- and whitespace-free. The "$Configuration block that follows the
	// copied one" half was VPC's own ordering hazard and has no CMake analogue.
	//
	// Which define is armed in which target IS asserted, elsewhere and
	// deliberately -- alias_mechanics_test's "armed in BOTH targets or
	// neither".
	const std::vector< std::string > listed = cmake_graph::ListedDefinitions( cmake_graph::kServer );
	ASSERT_EQ( 1, (int)std::count( listed.begin(), listed.end(), std::string( "SOURCESHARP" ) ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not define "
		   "SOURCESHARP exactly once -- the reader is not reading the server's "
		   "target_compile_definitions block, or the port's own define is gone";
	EXPECT_EQ( 1, (int)std::count( listed.begin(), listed.end(), std::string( "SOURCESHARP_SHELL_GAMEDLL" ) ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) must define "
		   "SOURCESHARP_SHELL_GAMEDLL, once, beside SOURCESHARP";

	// AND THE LIST IS WHAT THE COMPILER SAW. The listed block is the source of
	// truth; the compile command of the file the guard is IN is the proof that
	// it reached the TU (a define on another target, or a per-source override,
	// would read the same in the list).
	const std::set< std::string > seen = DefinesOf( cmake_graph::CompileCommand( cmake_graph::kServer, kShell ) );
	ASSERT_TRUE( seen.count( "SOURCESHARP" ) )
		<< kShell << "'s compile command carries no -DSOURCESHARP -- the reader "
		   "is not reading the right command";
	EXPECT_TRUE( seen.count( "SOURCESHARP_SHELL_GAMEDLL" ) )
		<< kShell << " is compiled without -DSOURCESHARP_SHELL_GAMEDLL, so every "
		   "#ifndef SOURCESHARP_SHELL_GAMEDLL guard in stock is inert";
}


//=============================================================================//
// Property 2 -- the guard is on the registration, not on the class
//=============================================================================//

// FOLLOWED THE WORK, AND THE PROPERTY IS NOW ITS OPPOSITE.
//
// Phase 7a left stock's CServerGameDLL class body compiled and unreachable, and
// said exactly why: guarding it out meant also moving four free functions and a
// ConVar that were interleaved with its ~50 methods, in the file that also held
// four of the census's seven interfaces. plan_gamedll_after.md Phase A4 moved
// the free functions and A9 steps 1-2 moved the interfaces, so step 3 deleted
// the class. This asserts the deletion in both directions, because a class body
// that comes back is not a compile error -- it is a second IServerGameDLL
// implementation sitting one EXPOSE_ away from a static-init race.
TEST( ShellGameDllRegistration, StocksClassIsDeletedAndTheShellStillDoesNotNameIt )
{
	const std::string s  = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// (The gameinterface.h half -- no `class CServerGameDLL` declared there --
	// is TheStockHeaderHasLeftTheFarm now: that header is compiled by nothing
	// since plan_server_novalve.md P5 and left the farm in P6.)

	if ( SrcExists( kStockCpp ) )
	{
		const std::string sCpp = ReadSrc( kStockCpp );
		EXPECT_EQ( 0, CountOf( sCpp, "CServerGameDLL" ) )
			<< "gameinterface.cpp names CServerGameDLL again -- the class body, "
			   "its instance, or a method definition is back";
	}

	// AND THE HALF THAT ALWAYS MATTERED. The shell must not name stock's class
	// or its instance at all. A `CServerGameDLL *` back on this class is the
	// delegate back, and a delegating DLLInit is stock's DLLInit -- which no
	// longer calls IGameSystem::InitAllSystems, so it would bring the server up
	// with not one game system initialised and die on the first physics prop.
	EXPECT_EQ( 0, CountOf( s, "g_ServerGameDLL" ) )
		<< "the shell reaches stock's instance again -- the delegate is back";
	EXPECT_EQ( 0, CountOf( s, "CServerGameDLL" ) )
		<< "the shell names stock's class again -- the delegate is back";
}
TEST( ShellGameDllRegistration, ThereIsNoSwitchLeftToChooseWith )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// FOLLOWED THE WORK, and inverted. This asserted that -shellgamedll is read
	// exactly once, into a member, and that no other method re-asks the command
	// line -- because a build where one method thinks it is in shell mode and
	// another does not was the hardest version of that bug.
	//
	// Phase 7a deletes the delegate, so there is no second answer to be
	// inconsistent about and the switch reads nothing. The same class of
	// mistake is still worth refusing: a method that goes back to asking the
	// command line is a method reintroducing a mode.
	EXPECT_EQ( 0, CountOf( s, "m_bShellMode" ) )
		<< "the shell-mode member is back. There is nothing for it to select: "
		   "the delegate was deleted because stock's DLLInit is what called "
		   "IGameSystem::InitAllSystems, so the absent-switch path would start "
		   "a server with no game system initialised";
	EXPECT_EQ( 0, CountOf( s, "m_pDelegate" ) );
	EXPECT_EQ( 0, CountOf( s, "-shellgamedll\"" ) )
		<< "the switch is being read again";
	EXPECT_EQ( 0, CountOf( s, "CommandLine()" ) )
		<< "nothing on this class reads the command line any more";
}

// DELETED AT PHASE 7a, and the reason is recorded rather than the fact simply
// vanishing: EveryEngineFacingMethodHandsTheCallBackWhenTheSwitchIsAbsent.
//
// It scanned every `override` on this class and required each one to contain a
// `m_pDelegate->` branch, because between Phases 5 and 7 a method that gained a
// managed body without a delegate branch silently changed what the game did with
// the switch OFF and broke the A/B. There is no switch and no delegate now, so
// the fact's subject does not exist -- and the only way to make it pass would
// have been to put the delegate back, which is the one edit this phase forbids.
//
// Its inverse is asserted instead, immediately below. That is a real check and
// not a placeholder: the failure it catches is one method quietly regaining a
// hand-back to stock, which would run stock's un-inited world for that call.
TEST( ShellGameDllRegistration, NoEngineFacingMethodHandsItsCallBackToStock )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 0, CountOf( s, "m_pDelegate->" ) );
	EXPECT_EQ( 0, CountOf( s, "g_ServerGameDLL." ) );

	// And the scan is over a class that still HAS engine-facing methods, so a
	// zero above is a pass rather than a file that failed to load or a class
	// that lost its overrides. Twelve is well under the real count and is there
	// to catch the parse breaking, not to pin the class's size.
	EXPECT_GT( CountOf( s, " override" ), 12 );
}

TEST( ShellGameDllRegistration, TheManagedFrameHasExactlyOneDriver )
{
	const std::string s  = ReadSrc( kShell );
	const std::string gs = ReadSrc( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( s.empty() );
	ASSERT_FALSE( gs.empty() );

	// FOLLOWED THE WORK. This was TheManagedFrameAndStocksCannotBothRun, and it
	// answered the double-drive question by ORDERING: the delegate branch had to
	// return before the managed call, because with the switch absent stock's
	// GameFrame drove CSourceSharpSystem, which drove the managed frame.
	//
	// Phase 7a answers it by SUBTRACTION instead, which is stronger: stock's
	// driver is not in the server binary. CSourceSharpSystem is behind
	// #ifdef CLIENT_DLL, so on the server the type is not instantiated and does
	// not exist, and the shell's kServerSystems[] row is the only thing that
	// reaches managed code per frame. An ordering can be got wrong by an edit;
	// a type that is not compiled cannot.
	// RETARGETED at plan_cworld_trace.md T4, AT THE SAME SUBJECT: the needle was
	// `public CAutoGameSystemPerFrame`, and T4 took stock's base off the client
	// too -- CSourceSharpSystem is a kClientSystems[] row deriving from
	// CClientSystemPerFrame now. The subject is unchanged: the class must stay
	// behind #ifdef CLIENT_DLL, or the server gets a second driver.
	//
	// AND THE SUBTRACTION GOT STRONGER RATHER THAN WEAKER, which is worth saying
	// because a retargeted needle usually does not. Its base is now a type
	// shell_clientsystem.h REFUSES to define outside CLIENT_DLL with an #error --
	// so a future edit that lifts this guard does not compile a second driver on
	// the server, it fails the build.
	const size_t atGuard = gs.find( "#ifdef CLIENT_DLL" );
	const size_t atClass = gs.find( "class CSourceSharpSystem : public CClientSystemPerFrame" );
	ASSERT_NE( std::string::npos, atClass );
	ASSERT_NE( std::string::npos, atGuard );
	EXPECT_LT( atGuard, atClass )
		<< "CSourceSharpSystem is not behind #ifdef CLIENT_DLL. On the server "
		   "that class registers itself and drives the managed frame, and the "
		   "kServerSystems[] row drives it too -- every managed participant "
		   "twice a frame";

	// And it is a ROW on the side that keeps it. Before T4 its base registered
	// it; now the only thing that runs it is being named in kClientSystems[], and
	// a managed frame that is not a row is a client with no managed frame at all.
	const std::string ct = ReadSrc( "sourcesharp/game/client/shell_clientsystems.cpp" );
	ASSERT_FALSE( ct.empty() );
	EXPECT_NE( std::string::npos, ct.find( "ClientSystem_SourceSharpManaged" ) )
		<< "the client's managed frame is not reached by kClientSystems[]";

	const size_t atFrame = s.find( "void GameFrame(bool simulating) override" );
	ASSERT_NE( std::string::npos, atFrame );
	const size_t atEnd = s.find( "\n    }", atFrame );
	ASSERT_NE( std::string::npos, atEnd );
	const std::string strBody = s.substr( atFrame, atEnd - atFrame );

	EXPECT_NE( std::string::npos, strBody.find( "ManagedRuntimeServer()->OnGameFrame" ) )
		<< "the shell's GameFrame must reach managed code -- it is the only "
		   "driver there is now";
	EXPECT_EQ( std::string::npos, strBody.find( "m_pDelegate" ) );
}

TEST( ShellGameDllRegistration, TheReloadIsServicedFromThinkAndFromNowhereElse )
{
	const std::string s  = ReadSrc( kShell );
	const std::string gs = ReadSrc( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( s.empty() );
	ASSERT_FALSE( gs.empty() );

	// FOLLOWED THE WORK. This was TheReloadServicePointIsTheOneThingNotBehindThe
	// Switch: the service call sat ABOVE the delegate's early return so that it
	// ran in both modes, because in delegate mode the gamesystem's own point
	// inside GameFrame was the only one and GameFrame is not called with no
	// level loaded (eiface.h:503 against :560).
	//
	// There is no switch now, and there is no second service point either --
	// Phase 7a removed CSourceSharpSystem's FrameUpdatePostEntityThink from the
	// server with the class. So the property to pin is the one the old fact was
	// working towards all along: EXACTLY ONE site, and it is Think.
	const size_t atThink = s.find( "void Think(bool finalTick) override" );
	ASSERT_NE( std::string::npos, atThink );
	const size_t atEnd = s.find( "\n    }", atThink );
	ASSERT_NE( std::string::npos, atEnd );
	const std::string strBody = s.substr( atThink, atEnd - atThink );

	EXPECT_NE( std::string::npos, strBody.find( "SourceSharp_ServiceReloadRequest()" ) )
		<< "Think() is the one service point, because it is the only per-frame "
		   "entry the engine calls with no level loaded";
	EXPECT_EQ( 1, CountOf( s, "SourceSharp_ServiceReloadRequest()" ) )
		<< "exactly one service call on this class -- two reintroduce the "
		   "interleaving Part 3b exists to prevent";

	// And the server's old second site is gone with its class. A call to the
	// service outside the client's guard in the shared file is that site back.
	const size_t atGuard   = gs.find( "#ifdef CLIENT_DLL" );
	const size_t atService = gs.find( "SourceSharp_ServiceReloadRequest();" );
	ASSERT_NE( std::string::npos, atGuard );
	ASSERT_NE( std::string::npos, atService );
	EXPECT_LT( atGuard, atService )
		<< "the shared file services the reload outside the client's guard -- "
		   "that is the server's second service point back";
}


//=============================================================================//
// Property 3 -- g_pSharedChangeInfo, the crash Phase 4 walks into
//=============================================================================//

TEST( ShellGameDllRegistration, TheShellAssignsTheGlobalSharedChangeInfo )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 1, CountOf( s, "g_pSharedChangeInfo = m_pEngine->GetSharedEdictChangeInfo();" ) )
		<< "stock assigns g_pSharedChangeInfo inside CServerGameDLL::DLLInit. "
		   "With the engine calling the SHELL's DLLInit instead, nothing else "
		   "does -- and CBaseEdict::StateChanged (public/edict.h:295) is an "
		   "INLINE that dereferences it unconditionally on every networked "
		   "variable write. The failure is a null dereference at the first "
		   "state change, which no offline gate can reach";
}

TEST( ShellGameDllRegistration, TheAssignmentIsOnTheOnlyPathThereIs )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// FOLLOWED THE WORK, and this one is the fact predicting its own phase.
	// It read: "a shell that only reached the assignment through the delegate
	// would look correct, run correctly, and crash the day Phase 7a deletes the
	// delegate", and required the assignment BELOW the delegate's early return.
	// This is that day. The early return is gone, so what is left to say is
	// that the assignment is on DLLInit's own path and reached unconditionally.
	const size_t atInit   = s.find( "bool DLLInit(CreateInterfaceFn engineFactory" );
	const size_t atAssign = s.find( "g_pSharedChangeInfo = m_pEngine->GetSharedEdictChangeInfo();" );
	const size_t atEnd    = s.find( "\n    }", atInit );
	ASSERT_NE( std::string::npos, atInit );
	ASSERT_NE( std::string::npos, atAssign );
	ASSERT_NE( std::string::npos, atEnd );
	EXPECT_LT( atInit, atAssign );
	EXPECT_LT( atAssign, atEnd )
		<< "the assignment must be inside DLLInit. CBaseEdict::StateChanged "
		   "(public/edict.h:295) is an inline that dereferences the global "
		   "unconditionally on every networked variable write, and nothing "
		   "else assigns it now that stock's DLLInit is unreachable";
}

TEST( ShellGameDllRegistration, TheInertMemberIsGoneRatherThanWired )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// Part 5 says of the shell's third member: "grep g_pSharedChangeInfo for a
	// real reader, then wire or delete." The member was never the subject --
	// it captured the same value into private storage nothing read, so wiring
	// it changes nothing and deleting it alone would have looked like the
	// question was answered while leaving the global NULL.
	EXPECT_EQ( 0, CountOf( s, "m_pSharedChangeInfo" ) )
		<< "the inert member must be gone, and the global assigned instead. "
		   "Both, or neither, is the failure";
}

TEST( ShellGameDllRegistration, TheGlobalIsNotClearedOnShutdownBecauseStockDoesNotEither )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	EXPECT_EQ( 0, CountOf( s, "g_pSharedChangeInfo = nullptr" ) )
		<< "it points at engine-owned storage that outlives this DLL, and a "
		   "NULL there is a crash in an inline on the networking path rather "
		   "than a caught error. Stock's DLLShutdown does not clear it";
}


//=============================================================================//
// PHASE 5 -- the two pointers and the constants (Part 5, Part 6)
//
// The same forced arrangement as above: shell_gamedll.cpp cannot be linked into
// a gtest binary, so what is checked is the ORDER and the SHAPE of things a
// reader gets wrong. The behavioural half of Part 6 lives in the managed suite
// (ListingBuffer, ServerGameDll, ShellSlotWiringTests) and in the adapter's own
// gtests, which compile the shipped header.
//=============================================================================//

TEST( ShellGameDllPointers, TheEngineIsHandedTheMembersAndNotTheGlobals )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// `return g_pServerClassHead;` and `return &g_StandardSendProxies;` are the
	// tidy one-line answer and the move that never reaches the end state:
	// g_pServerClassHead is a list assembled by static constructors at DLL
	// load, which is the thing this port is removing, and a shell that returns
	// it has adopted it. With members, the day the last SDK
	// IMPLEMENT_SERVERCLASS is evicted is the day the SEEDING changes and these
	// two getters do not.
	const size_t atClasses = s.find( "ServerClass *GetAllServerClasses() override" );
	ASSERT_NE( std::string::npos, atClasses );
	const std::string strClasses = s.substr( atClasses, s.find( "\n    }", atClasses ) - atClasses );
	EXPECT_NE( std::string::npos, strClasses.find( "return m_pServerClasses;" ) );
	EXPECT_EQ( std::string::npos, strClasses.find( "return g_pServerClassHead" ) );

	const size_t atProxies = s.find( "CStandardSendProxies *GetStandardSendProxies() override" );
	ASSERT_NE( std::string::npos, atProxies );
	const std::string strProxies = s.substr( atProxies, s.find( "\n    }", atProxies ) - atProxies );
	EXPECT_NE( std::string::npos, strProxies.find( "return m_pSendProxies;" ) );
	EXPECT_EQ( std::string::npos, strProxies.find( "&g_StandardSendProxies" ) );
}

TEST( ShellGameDllPointers, TheSendProxiesAreCopiedByValueIntoStorageTheShellOwns )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// The engine compares the function pointers INSIDE CStandardSendProxies to
	// recognise the standard prop encodings, so their identity is what matters
	// and the struct's address is not. A by-value copy is therefore transparent
	// -- and it is what lets g_StandardSendProxies be deleted the day
	// sourcesharp's own dt_send.cpp is the only thing that fills it.
	EXPECT_EQ( 1, CountOf( s, "CStandardSendProxies m_SendProxies = {};" ) )
		<< "the storage is a member BY VALUE, not a second pointer at the global";
	EXPECT_EQ( 1, CountOf( s, "m_SendProxies = g_StandardSendProxies;" ) );
	EXPECT_EQ( 1, CountOf( s, "m_pSendProxies = &m_SendProxies;" ) );
}

TEST( ShellGameDllPointers, TheSeedDoesNotRelinkTheChain )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// THE DEVIATION FROM PART 5, PINNED SO IT CANNOT BE "FIXED" BACK.
	//
	// Part 5 asks for the member to be seeded "managed registrations first,
	// then the surviving g_pServerClassHead chain appended". That order breaks
	// networking, and not as a matter of taste: a ServerClass links itself into
	// g_pServerClassHead ALPHABETICALLY (public/server_class.h:35-51) and
	// CClientClass does the same into g_pClientClassHead. The engine assigns
	// class IDs by walking the server's list in order, and in the ordinary case
	// where it does not send send-tables the client rebuilds those IDs by
	// walking ITS list in order. They agree only because both are sorted by the
	// same rule. Re-linking the server's list to put the managed classes first
	// gives the same class different IDs on the two sides, silently, and every
	// entity decodes as something else.
	//
	// So the seed writes no m_pNext anywhere. A future edit that "implements
	// Part 5 properly" fails here with the reason attached.
	EXPECT_EQ( 0, CountOf( s, "m_pNext =" ) )
		<< "the shell must not relink the ServerClass chain: the wire order is "
		   "the alphabetical order both sides sort to, and reordering the "
		   "server's list alone renumbers every class id";
	EXPECT_EQ( 1, CountOf( s, "m_pServerClasses = g_pServerClassHead;" ) )
		<< "seeded from the chain as it stands, once, at DLLInit";
}

TEST( ShellGameDllPointers, TheSeedIsAfterManagedStartupBecauseThatIsWhatFillsIt )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// The standalone tier ServerClasses come from C# type declarations and the
	// temp-entity ones from C# [TempEntity] declarations, and both are
	// constructed during managed startup. Seeded before OnSystemStart the
	// member would be the head of a list C# had not finished writing -- and the
	// engine walks GetAllServerClasses exactly once, so a class that is not in
	// it is not merely late, it is absent for the whole session.
	const size_t atStart = s.find( "ManagedRuntimeServer()->OnSystemStart()" );
	const size_t atSeed  = s.find( "SeedNetworkingPointers();" );
	ASSERT_NE( std::string::npos, atStart );
	ASSERT_NE( std::string::npos, atSeed );
	EXPECT_LT( atStart, atSeed );
}

TEST( ShellGameDllPointers, TheMeterPrintsBothPopulations )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// Part 5's meter for the endgame. This plan does not evict the surviving
	// SDK registrations, so the second number stays large; printing it is what
	// makes it a number going down instead of a global nobody measures, and
	// what makes a regression that re-adds twenty visible rather than inferred.
	EXPECT_NE( std::string::npos,
		s.find( "server classes: %d from managed, %d from g_pServerClassHead" ) )
		<< "both counts, in one line, at DLLInit";

	// TWO POPULATIONS COUNTED SEPARATELY, not one counted and the other
	// subtracted from a total. A subtraction cannot notice a class that is in
	// neither list, which is exactly what a third construction site would
	// produce.
	EXPECT_EQ( 1, CountOf( s, "nManaged++" ) );
	EXPECT_EQ( 1, CountOf( s, "nStock++" ) );
	EXPECT_EQ( 1, CountOf( s, "SourceSharp_IsManagedServerClass( pClass )" ) );
}

TEST( ShellGameDllConstants, TheTickIntervalIsTakenOnceAndReadFromAMember )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// GetTickInterval is `const`, and the whole reason Part 6 hesitates over it
	// is that a managed answer read per call needs a `mutable` member. It does
	// not: the engine asks exactly once, so the answer is taken at DLLInit and
	// this method reads a member.
	EXPECT_EQ( 1, CountOf( s, "m_flTickInterval = ManagedRuntimeServer()->OnTickInterval();" ) );
	EXPECT_EQ( 0, CountOf( s, "mutable " ) )
		<< "nothing here needs to be mutable, which is the point";

	const size_t at = s.find( "float GetTickInterval() const override" );
	ASSERT_NE( std::string::npos, at );
	const std::string strBody = s.substr( at, s.find( "\n    }", at ) - at );
	EXPECT_NE( std::string::npos, strBody.find( "return m_flTickInterval;" ) );
	EXPECT_EQ( std::string::npos, strBody.find( "OnTickInterval" ) )
		<< "asked once at DLLInit, not per call";
}

TEST( ShellGameDllConstants, TheGameDescriptionUsesTheRouteThatWasAlreadyThere )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// Part 6 names this trap itself: GetGameDescription ALREADY has a managed
	// answer, reached through SourceSharp_GetGameDescription() and the
	// GetGameDescription ABI slot, and that function already owns the buffer
	// the engine reads after the call. A second route is two things to keep in
	// agreement, and they would disagree the first time one of them was
	// changed.
	const size_t at = s.find( "const char *GetGameDescription() override" );
	ASSERT_NE( std::string::npos, at );
	const std::string strBody = s.substr( at, s.find( "\n    }", at ) - at );
	EXPECT_NE( std::string::npos, strBody.find( "return SourceSharp_GetGameDescription();" ) );
	EXPECT_EQ( std::string::npos, strBody.find( "ManagedRuntimeServer()->" ) )
		<< "no sixth slot: five were added this phase, and this is the sixth "
		   "answer that already had one";
}

TEST( ShellGameDllConstants, NoAnswerReachesTheEngineAsNullAndAnEmptyOneDoesNot )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// Null and empty are different answers to the engine and stay different: a
	// null browser-map override means "show the real map name" and an empty one
	// means "show nothing". Managed distinguishes them by returning -1 against
	// 0, and Answer() is the single place that turns -1 into a null pointer --
	// single, because a second copy of "-1 means no answer" is a second place
	// for it to become "-1 means empty".
	EXPECT_EQ( 1, CountOf( s, "static const char *Answer( const char *pBuffer, int nSize, int nWritten )" ) );
	EXPECT_NE( std::string::npos, s.find( "if ( nWritten < 0 || nWritten >= nSize )" ) );
	EXPECT_EQ( 2, CountOf( s, "return Answer( m_sz" ) )
		<< "both browser answers go through it";
}

TEST( ShellGameDllConstants, TheRefusalReasonBufferIsClearedBeforeTheAskAndNulledWhenAllowed )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	const size_t at = s.find( "bool IsManualMapChangeOkay(const char **pszReason) override" );
	ASSERT_NE( std::string::npos, at );
	const std::string strBody = s.substr( at, s.find( "\n    }", at ) - at );

	// CLEARED BEFORE THE CALL, and this is what makes ONE crossing enough.
	// Managed writes the reason only when it refuses; without the clear the
	// allow path leaves the PREVIOUS refusal's text in the buffer, and the
	// engine tests the pointer rather than the verdict. With it, "was a reason
	// written" is answerable from the buffer, so the verdict can be the return
	// value -- no second out-parameter, and no second ask of a property that
	// could answer differently the second time.
	const size_t atClear = strBody.find( "m_szMapChangeReason[ 0 ] = '\\0';" );
	const size_t atAsk   = strBody.find( "OnIsManualMapChangeOkay(" );
	ASSERT_NE( std::string::npos, atClear );
	ASSERT_NE( std::string::npos, atAsk );
	EXPECT_LT( atClear, atAsk );

	// And an allowed change hands back a NULL, not an empty string.
	EXPECT_NE( std::string::npos, strBody.find( "*pszReason = ( !bOkay && m_szMapChangeReason[ 0 ] != '\\0' )" ) );
	EXPECT_NE( std::string::npos, strBody.find( ": nullptr;" ) );
}

TEST( ShellGameDllConstants, EveryPartSixAnswerIsAskedOfManagedAndNotOfAConstant )
{
	const std::string s = ReadSrc( kShell );
	ASSERT_FALSE( s.empty() );

	// THE FACT THAT WOULD HAVE CAUGHT THE PHASE NOT HAPPENING. Each of these
	// returned a literal with a `// TODO: Have this be setable by managed
	// runtime` above it, and a body that still does is indistinguishable from
	// one that was ported -- it compiles, links, boots, and answers.
	EXPECT_EQ( 0, CountOf( s, "TODO: Have this be setable by managed runtime" ) )
		<< "Part 6's six TODOs are answered, so none of them is still in the file";

	static const char *kAsked[] = {
		"ManagedRuntimeServer()->OnTickInterval()",
		"ManagedRuntimeServer()->OnShouldHideServer()",
		"ManagedRuntimeServer()->OnBrowserMapOverride(",
		"ManagedRuntimeServer()->OnBrowserGameData(",
		"ManagedRuntimeServer()->OnIsManualMapChangeOkay(",
	};
	for ( size_t i = 0; i < sizeof( kAsked ) / sizeof( kAsked[ 0 ] ); i++ )
		EXPECT_EQ( 1, CountOf( s, kAsked[ i ] ) ) << kAsked[ i ];
}

TEST( ShellGameDllPointers, TheMeterIsAnsweredByIdentityInOnePlace )
{
	const std::string s = ReadSrc( "sourcesharp/game/server/shell_nettable.cpp" );
	ASSERT_FALSE( s.empty() );

	// EVERY ServerClass IS ON ONE LIST. SourceSharp's link themselves into
	// g_pServerClassHead from their own constructors exactly as the SDK's do,
	// so the two populations cannot be told apart by walking the chain. They
	// are told apart by identity against the two places SourceSharp constructs
	// one -- and a THIRD site that does not answer here is counted as stock,
	// which is the direction that fails safe: the meter then reads as more work
	// to do rather than as work already done.
	EXPECT_NE( std::string::npos, s.find( "bool SourceSharp_IsManagedServerClass( const ServerClass *pClass )" ) );
	EXPECT_NE( std::string::npos, s.find( "SourceSharp_IsStandaloneTierServerClass( pClass )" ) );
	EXPECT_NE( std::string::npos, s.find( "SourceSharp_IsTempEntServerClass( pClass )" ) );

	// The two answers are by pointer identity and not by class NAME. A name
	// test would have to agree with SourceSharp_TierClassName's format forever,
	// and a rename would move a class from one column of the meter to the other
	// with nothing said.
	const std::string strTiers = ReadSrc( "sourcesharp/game/server/shell_standalone.cpp" );
	const std::string strTe    = ReadSrc( "sourcesharp/game/server/shell_tempents.cpp" );
	ASSERT_FALSE( strTiers.empty() );
	ASSERT_FALSE( strTe.empty() );
	EXPECT_NE( std::string::npos, strTiers.find( "g_pTierClasses[ i ] == pClass" ) );
	EXPECT_NE( std::string::npos, strTe.find( "g_TeBuilt[ i ].pClass == pClass" ) );
}
