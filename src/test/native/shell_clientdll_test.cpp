//========= SourceSharp ======================================================//
//
// THE IBaseClientDLL SHELL'S ARRANGEMENT, AND THE DOCUMENT IT IS READ OUT OF.
//
// THIS FILE USED TO BE clientdll_probe_test.cpp AND USED TO OWN A PROBE.
// shell_clientdll_probe.h armed all seventy-nine IBaseClientDLL methods and
// counted which ones the engine actually called, behind two console commands.
// That probe was a MEASURING INSTRUMENT and its measurement is finished: the
// per-slot table is docs/clientdll-boundary.md and the summary is
// docs/engine-boundary.md §6a. Lane 5b deleted the probe, its two commands and
// its seventy-nine arming lines; what is left here is everything in the old
// file that was never about the probe, plus the facts below that replace it.
//
// WHAT WENT, AND WHY IT IS NOT A LOST CHECK. The old file asserted that every
// probe id was armed exactly once -- a fact about the instrument's own wiring,
// which cannot outlive the instrument. Nothing it protected is protected by
// anything else, because there is nothing left to protect: no arming lines
// exist.
//
// WHAT REPLACED THE ENUM, AND WHY IT IS STRICTLY BETTER. The doc facts below
// used to enumerate the probe's hand-written enum -- a TRANSCRIPTION of
// IBaseClientDLL. They now read public/cdll_int.h, the interface itself. A
// transcription can agree with a document while both disagree with the engine;
// this cannot. It also re-derives the seventy-nine-not-eighty fact from the
// source rather than restating it in a comment.
//
// SOURCE-READ, and that is not a shortcut: shell_clientdll.cpp includes
// cbase.h's whole world and can never be linked into a gtest. What is asserted
// is the ARRANGEMENT, which is exactly what has gone wrong here before.
//
//===========================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <algorithm>
#include <cctype>
#include <cstring>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{
	// Every path here is relative to the tree's src/, which is where the
	// harness runs these binaries from.
	std::string FileText( const char *pRelative )
	{
		std::ifstream in( pRelative );
		EXPECT_TRUE( in.good() )
			<< "cannot read " << pRelative << " -- run this from the tree's src/";

		std::ostringstream ss;
		ss << in.rdbuf();
		return ss.str();
	}

	std::string ShellText() { return FileText( "sourcesharp/game/client/shell_clientdll.cpp" ); }
	std::string DocText()   { return FileText( "../docs/clientdll-boundary.md" ); }

	// How many times `needle` occurs in `haystack`.
	int CountOf( const std::string &haystack, const std::string &needle )
	{
		int n = 0;
		for ( size_t at = haystack.find( needle );
		      at != std::string::npos;
		      at = haystack.find( needle, at + needle.size() ) )
		{
			++n;
		}
		return n;
	}

	//-------------------------------------------------------------------------
	// THE INTERFACE'S OWN METHOD NAMES, off public/cdll_int.h.
	//
	// For each `virtual`, the text up to the next `;` or `{` is the
	// declaration; the identifier before its first `(` is the method name.
	// Declarations are returned IN ORDER and WITH the one duplicate, so a
	// caller can assert both the eighty and the seventy-nine.
	//-------------------------------------------------------------------------
	std::vector< std::string > DeclaredMethods()
	{
		const std::string hdr = FileText( "public/cdll_int.h" );

		std::vector< std::string > out;

		const size_t open = hdr.find( "abstract_class IBaseClientDLL" );
		if ( open == std::string::npos )
			return out;

		const size_t close = hdr.find( "\n};", open );
		const std::string blk = hdr.substr( open, close - open );

		size_t pos = 0;
		for ( size_t v = blk.find( "virtual" ); v != std::string::npos; v = blk.find( "virtual", pos ) )
		{
			pos = v + strlen( "virtual" );

			size_t stop = blk.size();
			const size_t semi  = blk.find( ';', pos );
			const size_t brace = blk.find( '{', pos );
			if ( semi  != std::string::npos ) stop = std::min( stop, semi );
			if ( brace != std::string::npos ) stop = std::min( stop, brace );

			const std::string seg = blk.substr( pos, stop - pos );

			const size_t paren = seg.find( '(' );
			if ( paren == std::string::npos )
				continue;

			size_t e = paren;
			while ( e > 0 && isspace( (unsigned char)seg[ e - 1 ] ) )
				--e;

			size_t s = e;
			while ( s > 0 && ( isalnum( (unsigned char)seg[ s - 1 ] ) || seg[ s - 1 ] == '_' ) )
				--s;

			if ( s < e )
				out.push_back( seg.substr( s, e - s ) );
		}

		return out;
	}
}

//=============================================================================
// 1. The interface, and the seventy-nine.
//=============================================================================

// SEVENTY-NINE, NOT EIGHTY, AND THE REASON IS READ OFF THE HEADER RATHER THAN
// RESTATED. public/cdll_int.h declares eighty virtuals but StartStatsReporting
// is declared TWICE -- once under #ifdef WIN32, once under #ifdef POSIX -- so a
// Linux vtable has seventy-nine entries. This used to be an EXPECT_EQ against a
// transcribed enum, which could only ever agree with the transcription.
TEST( ClientDllInterface, EightyDeclarationsBecomeSeventyNineLinuxSlots )
{
	const std::vector< std::string > decls = DeclaredMethods();
	ASSERT_FALSE( decls.empty() ) << "public/cdll_int.h did not parse";

	EXPECT_EQ( 80u, decls.size() );

	const std::set< std::string > distinct( decls.begin(), decls.end() );
	EXPECT_EQ( 79u, distinct.size() );
}

TEST( ClientDllInterface, StartStatsReportingIsTheOneDeclaredTwice )
{
	const std::vector< std::string > decls = DeclaredMethods();

	int n = 0;
	for ( size_t i = 0; i < decls.size(); ++i )
		if ( decls[ i ] == "StartStatsReporting" )
			++n;

	EXPECT_EQ( 2, n )
		<< "the duplicate declaration is what makes eighty declarations into "
		   "seventy-nine Linux vtable slots; if it is no longer "
		   "StartStatsReporting, every row in docs/clientdll-boundary.md "
		   "shifts by one.";
}

// A PARSE THAT QUIETLY RETURNED THE WRONG THING would satisfy the two counts
// above by accident -- eighty of something is not eighty METHODS. These four
// are spread across the interface and are named in the doc's own rows.
TEST( ClientDllInterface, TheParseYieldsActualMethodNames )
{
	const std::vector< std::string > decls = DeclaredMethods();
	const std::set< std::string > distinct( decls.begin(), decls.end() );

	EXPECT_EQ( 1u, distinct.count( "Init" ) );
	EXPECT_EQ( 1u, distinct.count( "FrameStageNotify" ) );
	EXPECT_EQ( 1u, distinct.count( "HudUpdate" ) );
	EXPECT_EQ( 1u, distinct.count( "LevelShutdown" ) );
}

//=============================================================================
// 2. The probe is gone.
//=============================================================================

TEST( ClientDllProbeRemoval, TheShellArmsNothingAndNamesNoProbeCommand )
{
	const std::string shell = ShellText();
	ASSERT_FALSE( shell.empty() );

	EXPECT_EQ( 0, CountOf( shell, "SS_CLIENTDLL_PROBE" ) );
	EXPECT_EQ( 0, CountOf( shell, "ClientDllProbes()" ) );
	EXPECT_EQ( 0, CountOf( shell, "shell_clientdll_probe.h" ) );
	EXPECT_EQ( 0, CountOf( shell, "ss_cl_clientdll_probe_report" ) );
	EXPECT_EQ( 0, CountOf( shell, "ss_cl_clientdll_probe_reset" ) );
}

// THE COUNT THE PROBE USED TO PROVIDE, KEPT. ss_cl_stage_crossings compares the
// engine's FrameStageNotify calls against the shell's crossings into managed
// code, and it used to read the first number off the probe's own row. A field
// on the object the engine calls replaces it -- deleting the probe must not
// quietly turn that command into a readout of one number.
TEST( ClientDllShell, TheStageCrossingsCommandStillComparesTwoNumbers )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 1, CountOf( shell, "CON_COMMAND( ss_cl_stage_crossings," ) );
	EXPECT_EQ( 1, CountOf( shell, "++m_nFrameStageNotifyCalls;" ) );
	EXPECT_EQ( 1, CountOf( shell, "(unsigned long long)SourceSharp_FrameStageNotifyCalls()," ) );
	EXPECT_EQ( 1, CountOf( shell, "(unsigned long long)SourceSharp_ClientStageCrossings() );" ) );
}

//=============================================================================
// 3. The refusals -- cs_cl_refusals' mechanism, which the probe did NOT take
//    with it. The macro used to write two records, the probe's row and the
//    tally; it now writes the tally alone and names the method directly.
//=============================================================================

// The two the engine has no known caller for. See the block comment above
// CShellClientDLL::BHaveChatSuspensionInCurrentMatch.
TEST( ClientDllShell, TheTwoMethodsWithNoEngineCallerAreRefusals )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::BHaveChatSuspensionInCurrentMatch\" )" ) );
	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::DisplayVoiceUnavailableMessage\" )" ) );
}

// The five Xbox-style matchmaking and rich presence methods, refused at
// plan_entity.md step 7g when game/client/basepresence.cpp left the build. They
// are a DIFFERENT kind of refusal from the two above: the engine's matchmaking
// DOES call these and IS built on PC. What makes them refusals is not the
// absence of a caller but the absence of an IMPLEMENTATION -- every body they
// forwarded to in basepresence.cpp was empty on PC, under Valve's own comment
// "TODO: Implement for PC".
TEST( ClientDllShell, TheFivePresenceMethodsAreRefusals )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::SetupGameProperties\" )" ) );
	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::GetPresenceID\" )" ) );
	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::GetPropertyIdString\" )" ) );
	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::GetPropertyDisplayString\" )" ) );
	EXPECT_EQ( 1, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::StartStatsReporting\" )" ) );
}

TEST( ClientDllShell, NothingElseIsARefusal )
{
	const std::string shell = ShellText();

	// SEVEN: 5b's two, plus the five step 7g added with basepresence.cpp's
	// deletion. Counted on the call spelling WITH its argument, so the
	// `#define` line -- which takes a parameter named pszMethod -- cannot make
	// this read eight.
	EXPECT_EQ( 7, CountOf( shell, "SS_CLIENTDLL_UNREACHABLE( \"IBaseClientDLL::" ) );
}

// And that no method still reaches the deleted singleton. A body that kept
// `presence->` would not link, which is the loud half; a body that dropped the
// refusal and returned 0 quietly is the half this catches.
TEST( ClientDllShell, NoMethodStillForwardsToTheDeletedPresenceSingleton )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 0, CountOf( shell, "presence->" ) )
		<< "the client shell reaches `presence` again; basepresence.cpp was "
		<< "deleted at step 7g and there is nothing behind that pointer.";
	EXPECT_EQ( 0, CountOf( shell, "#include \"ipresence.h\"" ) );
}

// THE SERVER'S SHAPE, ASSERTED. The user's instruction for this step is that
// IBaseClientDLL be implemented the way IServerGameDLL is, and the refusals
// mechanism is the half of that which is about behaviour. These are the four
// pieces CShellGameDll has, named here so a later edit that quietly replaces
// one of them with a silent no-op fails rather than drifting.
TEST( ClientDllShell, TheRefusalMechanismMirrorsTheServersFourPieces )
{
	const std::string shell = ShellText();

	// 1. the tally is a FIELD on the one object the engine holds, not a
	//    file-scope static.
	EXPECT_EQ( 1, CountOf( shell, "CShellClientDllRefusals m_Refusals;" ) );

	// 2. one private forwarding line, spelt as the server spells its own.
	EXPECT_EQ( 1, CountOf( shell, "void ShellClientDll_Unreachable( const char *pMethod )" ) );

	// 3. the readout: ONE body, and TWO callers -- Shutdown and the console
	//    command -- because a readout written twice is two readouts that can
	//    disagree about what zero looks like.
	EXPECT_EQ( 1, CountOf( shell, "static void ShellClientDll_PrintRefusals(" ) );
	EXPECT_EQ( 1, CountOf( shell, "ShellClientDll_PrintRefusals( m_Refusals );" ) );
	EXPECT_EQ( 1, CountOf( shell, "ShellClientDll_PrintRefusals( g_ShellClientDLL.Refusals() );" ) );

	// 4. the warning, defined here against the seam the header declares.
	EXPECT_EQ( 1, CountOf( shell, "void ShellClientDll_RefusalWarning( const char *pMethod, int nTotal )" ) );

	// And the console command the gate is read through.
	EXPECT_EQ( 1, CountOf( shell, "CON_COMMAND( cs_cl_refusals," ) );
}

// The whole reason the file exists: the class the engine resolves by name is a
// SourceSharp shell, and the interface is still exposed under the version
// string the engine asks for.
TEST( ClientDllShell, TheShellIsWhatIsExposedAsIBaseClientDLL )
{
	const std::string shell = ShellText();

	EXPECT_NE( std::string::npos, shell.find( "class CShellClientDLL : public IBaseClientDLL" ) );
	EXPECT_NE( std::string::npos,
	           shell.find( "EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CShellClientDLL, IBaseClientDLL, "
	                       "CLIENT_DLL_INTERFACE_VERSION, g_ShellClientDLL )" ) );
}

// plan_entity.md 5b: cdll_client_int.cpp LEAVES. Read off the build's source
// list rather than off the filesystem, because the file itself stays on disk
// and its absence from OUR build is the fact. Since Phase 6b the list is
// client_sourcesharp's (src/client/native/CMakeLists.txt, through
// cmake_graph.h); it was client_csharp.vpc. Both halves by BASENAME in any
// directory, the reach the old substring count had.
TEST( ClientDllShell, TheStockTranslationUnitIsOffTheClientBuild )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kClient );

	int nStock = 0, nShell = 0;
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		const std::string base = it->substr( it->rfind( '/' ) + 1 );
		if ( base == "cdll_client_int.cpp" ) nStock++;
		if ( base == "shell_clientdll.cpp" ) nShell++;
	}

	// The proof-of-read, and the replacement: exactly one client shell.
	ASSERT_EQ( 1, nShell ) << "client_sourcesharp does not compile shell_clientdll.cpp once";
	EXPECT_EQ( 0, nStock ) << "cdll_client_int.cpp is back in client_sourcesharp";
}

//=============================================================================
// 4. THE LEVEL-SHUTDOWN TEARDOWN.
//
// WHY THESE EXIST. Lane `shell-lifetime` ran the four cells of {seam on/off} x
// {teardown present/absent} on a live game, three map loads each, and the
// client segfaults in the shadow manager on the third load in exactly one of
// them: the one where NEITHER of the two destroyers of a C_StandaloneEntity is
// reachable. With the seam, `CClientEntityList::Release()` destroyed every
// shell (measured by backtrace: cliententitylist.cpp:95); the flip deletes that
// file, and these two calls are what replaces it. Delete them and 79 shells
// survive the level change holding 33 shadow handles into a manager the
// shutdown has just reset -- `Assert( m_Shadows.Count() == 0 )` fires, and the
// NEXT level double-frees one of them.
//
// ORDER IS ONE OF THE FACTS AND NOT A STYLE POINT. The shells must go before
// the stock walk, because unarmed the stock walk would otherwise destroy them
// first and the teardown would find an empty list.
//=============================================================================

namespace
{
	// THE ASSIGNMENTS AND NOT THE BARE NAMES. `shell_clientdll.cpp` DECLARES
	// `int SourceSharp_StandaloneClientsLevelShutdown();` locally -- this
	// file's own idiom for the hierarchy -- ten lines above it calls it, so a
	// search for the bare name finds the DECLARATION and every ordering fact
	// below would be about a line that is not the call. Measured: the
	// "teardown moved inside the arming guard" mutation stayed green twice
	// because of it.
	const char *const kShellTeardownCall =
		"const int nShells = SourceSharp_StandaloneClientsLevelShutdown();";
	const char *const kWorldTeardownCall =
		"const bool bWorld = SourceSharp_ClientWorldLevelShutdown();";
	// The reset the entity teardown has to run BEFORE. lane `f-list` measured
	// what violating it costs: a segfault in the client shadow manager on the
	// SECOND map load, 0 of 5 runs, with every shell surviving the level change
	// holding leaf-system and shadow handles into subsystems this very function
	// had already reset.
	const char *const kPostEntity = "SourceSharp_ClientSystemsLevelShutdownPostEntity();";
}

TEST( ClientDllShell, LevelShutdownDestroysTheStandaloneShells )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 1, CountOf( shell, kShellTeardownCall ) )
		<< "CShellClientDLL::LevelShutdown must call "
		   "SourceSharp_StandaloneClientsLevelShutdown(). Without it nothing "
		   "destroys a C_StandaloneEntity once the port seam goes, and the "
		   "client segfaults in clientshadowmgr on the next level.";
}

TEST( ClientDllShell, LevelShutdownReleasesTheClientWorld )
{
	const std::string shell = ShellText();

	EXPECT_EQ( 1, CountOf( shell, kWorldTeardownCall ) )
		<< "CShellClientDLL::LevelShutdown must call "
		   "SourceSharp_ClientWorldLevelShutdown(). C_World::Release() has no "
		   "other caller once cliententitylist.cpp is gone.";
}

TEST( ClientDllShell, TheShellTeardownRunsBeforeTheSubsystemsItHoldsHandlesInto )
{
	const std::string shell = ShellText();

	const size_t shells = shell.find( kShellTeardownCall );
	const size_t world  = shell.find( kWorldTeardownCall );
	const size_t post   = shell.find( kPostEntity );

	ASSERT_NE( std::string::npos, shells );
	ASSERT_NE( std::string::npos, world );
	ASSERT_NE( std::string::npos, post )
		<< "CShellClientDLL::LevelShutdown no longer runs the post-entity "
		   "system shutdown at all, so there is nothing for the two teardowns "
		   "to be ordered against -- and the subsystems the shells hold "
		   "handles into are never reset.";

	EXPECT_LT( shells, world ) << "shells, then the world: a shell's destructor "
	                              "can reach the world and nothing in the "
	                              "world's Release reaches a shell";
	EXPECT_LT( world, post )
		<< "the entity teardown runs AFTER the post-entity system shutdown. "
		   "That is the ordering lane `f-list` measured the cost of: a "
		   "segfault in the client shadow manager on the second map load, 0 of "
		   "5 runs.";
}

// READ THE ASSERTIONS, NOT THE NAME. There is no arm flag left in this file.
// The claim is unconditional: the teardown runs in whatever build this file is
// compiled into. Asserted as the absence of the flag PLUS the live successor --
// both teardowns present, ahead of the walk -- because "no guard" alone is also
// true of a file that lost the teardown.
TEST( ClientDllShell, TheShellTeardownIsNotGuardedOnTheArmFlag )
{
	const std::string shell = ShellText();

	const size_t shells = shell.find( kShellTeardownCall );
	ASSERT_NE( std::string::npos, shells );

	EXPECT_EQ( 0, CountOf( shell, "SS_CBASEENTITY_ALIAS" ) )
		<< "shell_clientdll.cpp names the arm flag again. Every project that "
		   "builds this file defines it, so an `#ifndef` on it is source no "
		   "compiler sees and an `#ifdef` on it is decoration.";

	const size_t world = shell.find( kWorldTeardownCall );
	const size_t post  = shell.find( kPostEntity );
	ASSERT_NE( std::string::npos, world );
	ASSERT_NE( std::string::npos, post );
	EXPECT_LT( shells, post )
		<< "the shell teardown is no longer ahead of the post-entity system "
		   "shutdown";
	EXPECT_LT( world, post )
		<< "the world teardown is no longer ahead of the post-entity system "
		   "shutdown";
}

//=============================================================================
// 5. The document the capture is recorded in.
//
// docs/clientdll-boundary.md carries one row per vtable slot and is now the
// ONLY record of which methods the engine calls -- the probe that produced it
// is gone, so a row lost from it cannot be re-derived by re-running anything
// short of restoring the probe from history. It is COUNTED against the
// interface, not spot checked.
//=============================================================================

TEST( ClientDllDoc, EveryDeclaredMethodHasARow )
{
	const std::string doc = DocText();
	ASSERT_FALSE( doc.empty() );

	const std::vector< std::string > decls = DeclaredMethods();
	const std::set< std::string > distinct( decls.begin(), decls.end() );
	ASSERT_EQ( 79u, distinct.size() );

	for ( std::set< std::string >::const_iterator it = distinct.begin();
	      it != distinct.end(); ++it )
	{
		// The row's own cell: "| `Method` |". Backticked so `Init` does not
		// match the word inside "ReplayInit" or inside a sentence.
		const std::string cell = "| `" + *it + "` |";
		EXPECT_EQ( 1, CountOf( doc, cell ) )
			<< "docs/clientdll-boundary.md has no row (or more than one) for "
			<< *it;
	}
}

TEST( ClientDllDoc, TheDocAgreesOnHowManySlotsThereAre )
{
	const std::string doc = DocText();

	EXPECT_NE( std::string::npos, doc.find( "seventy-nine" ) );
	EXPECT_NE( std::string::npos, doc.find( "27/79 methods called, 0 refused" ) );
}

TEST( ClientDllDoc, TheDocNamesTheSameTwoRefusalsTheShellDoes )
{
	const std::string doc = DocText();

	EXPECT_NE( std::string::npos, doc.find( "BHaveChatSuspensionInCurrentMatch" ) );
	EXPECT_NE( std::string::npos, doc.find( "DisplayVoiceUnavailableMessage" ) );

	// And claims no others. The needle ENDS THE LINE, so it matches the two
	// table ROWS and not the verdict-key entry that explains the phrase.
	EXPECT_EQ( 2, CountOf( doc, "| not called this run \xE2\x80\x94 **refusal armed** |\n" ) );
}

// THE COMMANDS THE DOC TELLS A READER TO RUN NO LONGER EXIST, so the doc must
// not still tell them to. This is the fact that makes the deletion honest: a
// document instructing a reader to run a removed command is worse than one that
// says nothing.
TEST( ClientDllDoc, TheDocDoesNotTellTheReaderToRunARemovedCommand )
{
	const std::string doc = DocText();

	EXPECT_EQ( 0, CountOf( doc, "./debug-api cmd ss_cl_clientdll_probe_report" ) );
	EXPECT_EQ( 0, CountOf( doc, "./debug-api cmd ss_cl_clientdll_probe_reset" ) );

	// And says instead where the capture came from and how to retake it.
	EXPECT_NE( std::string::npos, doc.find( "restoring the probe" ) );
}
