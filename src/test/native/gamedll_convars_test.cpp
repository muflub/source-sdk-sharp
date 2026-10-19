//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// plan_shim.md Phase 1d, lane 1d. game/server/game.cpp left server_csharp.vpc (the
// server's source list, now server_sourcesharp's in src/server/native/CMakeLists.txt);
// its seventeen ConVar declarations did not.
// THREE FAILURES THIS PINS, all of them silent:
//   1. A DECLARATION DROPPED WITH THE TRANSLATION UNIT. Seven of the
//      seventeen have a consumer that reaches them by NAME -- managed code
//      through FindConVarValue, the engine through ConVarRef, and the
//      dedicated server's settings dialog through cfg/settings.scr. None of
//      those is a symbol import, so the link-line census that authorised this
//      deletion reads 0 importers for all seventeen and is right about the
//      symbols and wrong about the ConVars. A name that is not registered is
//      not a link error and not a warning: FindConVarValue answers nothing and
//      the caller reads its own default, which for displaysoundlist is
//      indistinguishable from "the display is switched off".
//   2. TWO REGISTRATIONS, OR NONE. Registration is ConVar_Register, tier1
//      guards it with s_bRegistered, and whichever call runs first supplies
//      the accessor that applies command-line values. Before this lane there
//      were two -- stock's in game.cpp and the shell's transliteration of it --
//      and stock's ran first, which made the shell's override dead code. With
//      game.cpp gone the shell's is the only one left. A second one added back
//      anywhere in the compiled server set would make the shell's dead again,
//      and nothing about that is visible at compile or link time.
//   3. THE HANDLE LEFT UNASSIGNED. g_pDeveloper is dereferenced by
//      physics.cpp and shell_soundscape.cpp. It is assigned in exactly one
//      place, and that place is called from one call site.

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

// Comment-stripped, whitespace-free. Every needle below is matched against
// this form, so no assertion here can be satisfied by prose -- which
// NoNeedleIsSatisfiedByProseAlone checks rather than assumes.
std::string Code( const std::string &raw )
{
	std::string out;
	out.reserve( raw.size() );
	for ( size_t i = 0; i < raw.size(); )
	{
		if ( raw.compare( i, 2, "//" ) == 0 )
		{
			while ( i < raw.size() && raw[ i ] != '\n' )
				i++;
			continue;
		}
		if ( raw.compare( i, 2, "/*" ) == 0 )
		{
			size_t e = raw.find( "*/", i + 2 );
			i = ( e == std::string::npos ) ? raw.size() : e + 2;
			continue;
		}
		if ( !isspace( (unsigned char)raw[ i ] ) )
			out += raw[ i ];
		i++;
	}
	return out;
}

const char *const kShimGameDll = "sourcesharp/game/server/shell_gamedll.cpp";
const char *const kStockHdr  = "game/server/game.h";
const char *const kSoundEnt  = "sourcesharp/managed/SourceSharp.Server/SoundEnt.cs";
const char *const kManaged   = "sourcesharp/managed/SourceSharp.Server/GameDllConVars.cs";
const char *const kStockCpp  = "game/server/game.cpp";
const char *const kSettings  = "../game/mod_sharp/cfg/settings.scr";

// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h: compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt, farm-relative paths. (This used to be a
// hand-written $File walker over server_csharp.vpc; the cross-check is what
// now proves the whole list was read.)

// The seventeen, as game.h declares them, paired with the ConVar NAME each
// one registers -- the two differ for eleven of them, and the name is the half
// that is reached from outside this DLL.
//
// The kSeventeen symbol table went with the native file it counted
// (plan_server_native_min P1); the managed count below pins the number.

} // namespace

//=============================================================================//
// 1. THE STOCK ROW IS GONE AND THE SHIM ROW IS THERE
//=============================================================================//

TEST( GameDllConVars, TheStockTranslationUnitHasLeftTheServer )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShimGameDll ) )
		<< "server_sourcesharp does not compile " << kShimGameDll
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "game.cpp is back in server_sourcesharp (src/server/native/CMakeLists.txt). It defines the same "
		   "seventeen ConVars GameDllConVars.cs declares, so both rows together "
		   "are seventeen duplicate registrations -- which the ConVar system "
		   "reports at runtime and no compile or link step sees.";
}

// TheShimFileIsStillCompiledForTheDeveloperHandle went with the file it
// required present (plan_server_native_min P1): g_pDeveloper's two native
// readers left with physics's and the soundscape's ports, so the pointer and
// the one call that assigned it are deleted, not re-pinned.

//=============================================================================//
// 2. ALL SEVENTEEN SURVIVED, BY SYMBOL AND BY NAME
//=============================================================================//

// NoCompiledNativeFileDeclaresAnyOfTheSeventeen went with the file it read
// (plan_server_native_min P1). What it guarded against -- a second native
// ConVar under a managed name -- is the tier1 child-registration hazard, and
// the whole-tree form of the guard is ExactlyOneCompiledServerFileCalls-
// ConVarRegister above, which walks every compiled server source.

//=============================================================================//
// 3. THE BY-NAME CONSUMERS THE CENSUS CANNOT SEE, each still asking
//=============================================================================//

TEST( GameDllConVars, ManagedSoundEntStillReadsDisplaySoundListByName )
{
	const std::string cs = ReadFile( kSoundEnt );
	ASSERT_FALSE( cs.empty() ) << kSoundEnt << " did not load";

	EXPECT_LE( 1u, CountOf( cs, "IntConVar(\"displaysoundlist\")" ) )
		<< kSoundEnt << " no longer reads displaysoundlist by name. If that "
		   "is deliberate the declaration in " << kManaged << " has lost "
		   "its last reader and this fact should retire with it; while the "
		   "read is there, the declaration is load-bearing.";
}

TEST( GameDllConVars, TheDedicatedServerSettingsDialogStillNamesFive )
{
	const std::string scr = ReadFile( kSettings );
	ASSERT_FALSE( scr.empty() ) << kSettings << " did not load";

	static const char *const kNamed[] = {
		"\"mp_teamplay\"", "\"mp_weaponstay\"", "\"mp_forcerespawn\"",
		"\"mp_footsteps\"", "\"mp_flashlight\"",
	};
	for ( size_t i = 0; i < 5; i++ )
	{
		EXPECT_LE( 1u, CountOf( scr, kNamed[ i ] ) )
			<< kSettings << " no longer offers " << kNamed[ i ] << "; the "
			<< "declaration it sets is in " << kManaged << ".";
	}
}

//=============================================================================//
// 4. ONE REGISTRATION IN THE WHOLE COMPILED SERVER SET
//=============================================================================//

TEST( GameDllConVars, ExactlyOneCompiledServerFileCallsConVarRegister )
{
	// The whole compiled server set. The walker's old ">= 100 paths" guard is
	// retired: CompiledSources throws unless compile_commands.json and the
	// CMakeLists list agree exactly, which is a stronger "the whole list was
	// read" than a population floor. The proof-of-read is the file the one
	// registration must be in.
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	const std::vector< std::string > sources( compiled.begin(), compiled.end() );
	ASSERT_TRUE( compiled.count( kShimGameDll ) )
		<< "server_sourcesharp does not compile " << kShimGameDll
		<< " -- the reader is not reading the server's source list";

	std::vector< std::string > callers;
	for ( size_t i = 0; i < sources.size(); i++ )
	{
		// A compiled source that cannot be opened is a failure, not a skip:
		// skipping it would let a caller hide behind an unreadable path.
		std::ifstream probe( sources[ i ].c_str() );
		if ( !probe.good() )
		{
			ADD_FAILURE() << "cannot open compiled server source " << sources[ i ];
			continue;
		}
		std::stringstream ss;
		ss << probe.rdbuf();
		// A CALL, not the definition: since tier1's convar.cpp was ported
		// (convar_port.cpp) the function is defined in the compiled set too.
		const std::string code = Code( ss.str() );
		const size_t nMentions = CountOf( code, "ConVar_Register(" );
		const size_t nDefinitions = CountOf( code, "void ConVar_Register(" ) + CountOf( code, "voidConVar_Register(" );
		if ( nMentions > nDefinitions )
			callers.push_back( sources[ i ] );
	}

	std::string where;
	for ( size_t i = 0; i < callers.size(); i++ )
		where += ( i ? ", " : "" ) + callers[ i ];

	ASSERT_EQ( 1u, callers.size() )
		<< "the compiled server set calls ConVar_Register in "
		<< callers.size() << " files (" << where << "). tier1/convar.cpp:63 "
		   "returns immediately once s_bRegistered is set, so a second call "
		   "never runs and the accessor passed to it -- the code that applies "
		   "command-line values and replicated defaults -- becomes dead "
		   "without one compile or link diagnostic. That is exactly what "
		   "game.cpp did to shell_gamedll.cpp's override before Phase 1d.";
	EXPECT_EQ( kShimGameDll, callers[ 0 ] )
		<< "the one registration is in " << callers[ 0 ] << " rather than in "
		<< kShimGameDll << ", whose accessor is the one with the comments "
		   "explaining why all three of its steps are load-bearing.";
}

//=============================================================================//
// 5. THE ENGINE HANDLE, assigned once and called once
//=============================================================================//

// InitializeCvarsAssignsTheDeveloperHandleAndIsStillCalled went with its
// subject (plan_server_native_min P1): InitializeCvars is deleted from
// shell_engineboundary.cpp with the pointer it assigned, whose last two
// dereferences left with their files' ports.

TEST( GameDllConVars, TheStockHeaderHasLeftTheFarm )
{
	// WAS TheStockHeaderNoLongerDeclaresWhatNothingDefines, which read
	// game/server/game.h and asserted none of the seventeen was declared
	// `extern` there any more: the definitions are managed, and an
	// `extern ConVar teamplay;` left behind would tell every reader the object
	// exists. game.h is compiled by nothing since plan_server_novalve.md P5 and
	// left the farm in P6, which is the stronger form of that absence: no
	// compiled file can see a declaration in a header it cannot reach. Its
	// other half -- that game.h still declared g_pDeveloper -- lost its subject
	// with the header and is retired.
	//
	// ReadFile EXPECTs the file to open, so the absence is probed directly. The
	// sibling read is what keeps it from being true of a cwd that holds no
	// game/server at all.
	const std::string sibling = ReadFile( "game/shared/shareddefs.h" );
	ASSERT_LT( 0u, CountOf( sibling, "#ifndef SHAREDDEFS_H" ) )
		<< "game/shared/shareddefs.h did not read, so the absence below is "
		   "not evidence";

	EXPECT_FALSE( std::ifstream( kStockHdr ).good() )
		<< kStockHdr << " is readable from the farm again. Nothing has "
		   "compiled it since plan_server_novalve.md P5; if a target needs it "
		   "back, check it declares none of the seventeen extern.";
}

//=============================================================================//
// 6. THE CHECK ON THE CHECKS
//=============================================================================//

// NoNeedleIsSatisfiedByProseAlone read kShimCvars's banner; the file is
// deleted (plan_server_native_min P1) and with it the banner it proved
// non-empty. NoManagedNeedleIsSatisfiedByProseAlone keeps the form over the
// managed file.

TEST( GameDllConVars, NoManagedNeedleIsSatisfiedByProseAlone )
{
	// TheManagedFileDeclaresAllSeventeenNames reads the managed file RAW,
	// because a C# attribute argument is code and stripping C++ comments out of
	// C# is the wrong tool. The needle it uses carries `[ConVar(`, so prose
	// cannot satisfy it -- and this is the fact that says so rather than the
	// comment above it.
	//
	// The file's own documentation names five of the convars in <c> tags, which
	// is exactly the shape that WOULD have satisfied a bare-name needle.
	const std::string cs = ReadFile( kManaged );
	ASSERT_FALSE( cs.empty() ) << kManaged << " did not load";

	EXPECT_LT( 0u, CountOf( cs, "<c>mapcyclefile</c>" ) )
		<< "the managed file no longer explains which readers reach these by "
		   "name -- rewrite this fact rather than deleting it.";
	EXPECT_EQ( 0u, CountOf( cs, "[ConVar(<c>" ) );

	// Seventeen [ConVar] attributes and not one more: a name this port invented
	// and parked in this file would be a convar stock never had, sitting in the
	// file whose whole claim is that it is stock's seventeen.
	EXPECT_EQ( (size_t)17, CountOf( cs, "[ConVar(" ) )
		<< kManaged << " declares " << CountOf( cs, "[ConVar(" )
		<< " ConVars rather than 17.";
}
