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

const char *const kShimCvars = "sourcesharp/game/server/shell_gamedll_convars.cpp";
const char *const kShimGameDll = "sourcesharp/game/server/shell_gamedll.cpp";
const char *const kInterfaces = "sourcesharp/game/server/shell_engineboundary.cpp";
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
// nDefs is how many times the SYMBOL is defined in the file, and it is 1 for
// every one of the seventeen -- including `flashlight`, which stock declared
// TWICE, `#ifdef CSTRIKE` at "1" and `#else` at "0".
//
// The pair was carried across verbatim at first, and tools/dead-conditionals
// reported it: CSTRIKE is on neither command line of this build, so the
// CSTRIKE arm is dead code now that these declarations are shim code rather
// than stock. That tool scans sourcesharp/** and not game/**, which is why
// game.cpp held the pair for as long as it did with nothing saying so -- the
// move is what made it visible. The arm the build takes was kept; see
// TheFlashlightDeclarationIsNotConditional.
struct Cvar { const char *pSymbol; const char *pName; size_t nDefs; };

const Cvar kSeventeen[] = {
	{ "displaysoundlist", "\"displaysoundlist\"", 1 },
	{ "mapcyclefile",     "\"mapcyclefile\"",     1 },
	{ "servercfgfile",    "\"servercfgfile\"",    1 },
	{ "lservercfgfile",   "\"lservercfgfile\"",   1 },
	{ "teamplay",         "\"mp_teamplay\"",      1 },
	{ "falldamage",       "\"mp_falldamage\"",    1 },
	{ "weaponstay",       "\"mp_weaponstay\"",    1 },
	{ "forcerespawn",     "\"mp_forcerespawn\"",  1 },
	{ "footsteps",        "\"mp_footsteps\"",     1 },
	{ "flashlight",       "\"mp_flashlight\"",    1 },
	{ "aimcrosshair",     "\"mp_autocrosshair\"", 1 },
	{ "decalfrequency",   "\"decalfrequency\"",   1 },
	{ "teamlist",         "\"mp_teamlist\"",      1 },
	{ "teamoverride",     "\"mp_teamoverride\"",  1 },
	{ "defaultteam",      "\"mp_defaultteam\"",   1 },
	{ "allowNPCs",        "\"mp_allowNPCs\"",     1 },
	{ "suitvolume",       "\"suitvolume\"",       1 },
};
const size_t kNumSeventeen = sizeof( kSeventeen ) / sizeof( kSeventeen[ 0 ] );

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
		   "seventeen ConVars as " << kShimCvars << ", so both rows together "
		   "are seventeen duplicate registrations -- which the ConVar system "
		   "reports at runtime and no compile or link step sees.";
}

TEST( GameDllConVars, TheShimFileIsStillCompiledForTheDeveloperHandle )
{
	// The row is still there and the reason changed. Phase 1d kept this file
	// for the seventeen declarations; Phase 2c took those to
	// SourceSharp.Server/GameDllConVars.cs and left one pointer behind.
	// physics.o and shell_soundscape.o import g_pDeveloper and
	// shell_engineboundary.o imports InitializeCvars, so the row is still
	// load-bearing -- for three symbols instead of twenty.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShimCvars ) )
		<< kShimCvars << " is not compiled into server.so (server_sourcesharp, "
		   "src/server/native/CMakeLists.txt), so "
		   "g_pDeveloper is undefined and InitializeCvars has no body.";
}

//=============================================================================//
// 2. ALL SEVENTEEN SURVIVED, BY SYMBOL AND BY NAME
//=============================================================================//

TEST( GameDllConVars, NoCompiledNativeFileDeclaresAnyOfTheSeventeen )
{
	// THE FLIP. This fact used to require each symbol to be defined EXACTLY
	// ONCE in the shim file, because game.h declared all seventeen extern and
	// too few was an undefined reference at link time. The declarations are
	// managed now and game.h's externs went with them, so the requirement
	// inverts: a native definition that came back would be a SECOND ConVar
	// registered under a name managed code already registers, which tier1
	// resolves by making the later one a CHILD of the first -- neither a
	// compile error nor a link error, and, as util_shared_library.cpp's own
	// note on `developer` records, "neither the console nor the game says which
	// one it read."
	const std::string code = Code( ReadFile( kShimCvars ) );
	ASSERT_FALSE( code.empty() ) << kShimCvars << " did not load";

	for ( size_t i = 0; i < kNumSeventeen; i++ )
	{
		const std::string def = std::string( "ConVar" ) + kSeventeen[ i ].pSymbol + "(";
		EXPECT_EQ( 0u, CountOf( code, def ) )
			<< kSeventeen[ i ].pSymbol << " is declared natively again in "
			<< kShimCvars << ". SourceSharp.Server/GameDllConVars.cs declares "
			   "the same NAME, so the engine would hold two ConVars for it and "
			   "say nothing about which one a reader got.";
	}
}

TEST( GameDllConVars, TheManagedFileDeclaresAllSeventeenNames )
{
	// BY NAME, which is the half that is reached from outside this DLL and the
	// half a rename would break silently. The managed file is read raw: C#
	// attribute arguments are code, and the file's own prose does not contain a
	// quoted convar name outside one -- NoManagedNeedleIsSatisfiedByProseAlone
	// checks that rather than assuming it.
	const std::string cs = ReadFile( kManaged );
	ASSERT_FALSE( cs.empty() ) << kManaged << " did not load";

	for ( size_t i = 0; i < kNumSeventeen; i++ )
	{
		EXPECT_LE( 1u, CountOf( cs, std::string( "[ConVar(" ) + kSeventeen[ i ].pName ) )
			<< "the ConVar NAME " << kSeventeen[ i ].pName << " is not declared "
			   "by " << kManaged << ". The name is what callers outside this "
			   "DLL use -- SoundEnt.cs, the engine's ConVarRef, and "
			   "cfg/settings.scr -- and dropping it is silent at every build "
			   "step.";
	}
}

TEST( GameDllConVars, TheManagedFlashlightDeclarationIsNotTheCstrikeArm )
{
	// stock's game.cpp declares mp_flashlight TWICE, `#ifdef CSTRIKE` at "1"
	// and `#else` at "0". CSTRIKE is on neither command line of this build, so
	// the CSTRIKE arm has always been dead. Picking it up on the way into C#
	// would be a behaviour change smuggled in as a move, and nothing about the
	// build would say so.
	const std::string cs = ReadFile( kManaged );
	ASSERT_FALSE( cs.empty() ) << kManaged << " did not load";

	EXPECT_LE( 1u, CountOf( cs, "[ConVar(\"mp_flashlight\", \"0\"" ) )
		<< "mp_flashlight no longer defaults to \"0\" in " << kManaged
		<< ". \"1\" is stock's CSTRIKE value; this mod is not Counter-Strike.";

	// The half that pinned game.cpp still carrying both arms was culled when
	// server.so stopped compiling game.cpp and it left the source tree.
}

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

TEST( GameDllConVars, InitializeCvarsAssignsTheDeveloperHandleAndIsStillCalled )
{
	const std::string cvars = Code( ReadFile( kShimCvars ) );
	ASSERT_FALSE( cvars.empty() ) << kShimCvars << " did not load";

	EXPECT_EQ( 1u, CountOf( cvars, "voidInitializeCvars(void){" ) )
		<< kShimCvars << " does not define InitializeCvars; "
		<< kInterfaces << " declares it extern and calls it.";
	EXPECT_EQ( 1u, CountOf( cvars, "g_pDeveloper=cvar->FindVar(\"developer\")" ) )
		<< "g_pDeveloper is not assigned. physics.cpp and shell_soundscape.cpp "
		   "dereference it, so a NULL here is a crash on the first cheat "
		   "check rather than \"cheats are off\".";

	const std::string iface = Code( ReadFile( kInterfaces ) );
	ASSERT_FALSE( iface.empty() ) << kInterfaces << " did not load";
	EXPECT_EQ( 1u, CountOf( iface, "InitializeCvars();" ) )
		<< kInterfaces << " no longer calls InitializeCvars, so g_pDeveloper "
		   "stays NULL.";
}

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
	const std::string sibling = ReadFile( "game/server/entity_funcs.h" );
	ASSERT_LT( 0u, CountOf( sibling, "#ifndef ENTITY_FUNCS_H" ) )
		<< "game/server/entity_funcs.h did not read, so the absence below is "
		   "not evidence";

	EXPECT_FALSE( std::ifstream( kStockHdr ).good() )
		<< kStockHdr << " is readable from the farm again. Nothing has "
		   "compiled it since plan_server_novalve.md P5; if a target needs it "
		   "back, check it declares none of the seventeen extern.";
}

//=============================================================================//
// 6. THE CHECK ON THE CHECKS
//=============================================================================//

TEST( GameDllConVars, NoNeedleIsSatisfiedByProseAlone )
{
	// Every assertion above reads Code(), which removes comments. This proves
	// that is not a formality: the file's banner NAMES game.cpp, ConVar_Register
	// and six of the ConVars, so a test reading the raw text would pass on the
	// commentary alone.
	const std::string raw  = ReadFile( kShimCvars );
	const std::string code = Code( raw );
	ASSERT_FALSE( raw.empty() );

	EXPECT_LT( 0u, CountOf( raw, "ConVar_Register" ) )
		<< "the banner no longer explains which ConVar_Register call is the "
		   "live one -- rewrite this fact rather than deleting it.";
	EXPECT_EQ( 0u, CountOf( code, "ConVar_Register" ) )
		<< kShimCvars << " CALLS ConVar_Register. It must not: that would be "
		   "the second call, and ExactlyOneCompiledServerFileCallsConVarRegister "
		   "is the fact about it.";
}

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
	EXPECT_EQ( kNumSeventeen, CountOf( cs, "[ConVar(" ) )
		<< kManaged << " declares " << CountOf( cs, "[ConVar(" )
		<< " ConVars rather than " << kNumSeventeen << ".";
}
