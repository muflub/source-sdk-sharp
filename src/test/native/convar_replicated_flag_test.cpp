//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// plan_shim.md Phase 2b, lane 2-physics. DOES MANAGED `RegisterConVar` ACCEPT
// `FCVAR_REPLICATED`?
// WHY THIS QUESTION IS WORTH A TEST FILE. Three of ragdoll_shared.cpp's ConVars
// carry FCVAR_REPLICATED (g_ragdoll_maxcount, g_debug_ragdoll_removal,
// g_ragdoll_important_maxcount), and the plan moves them to managed
// declarations. A replicated ConVar that silently registers UNREPLICATED is the
// worst shape of bug this project has a name for: everything works, the console
// lists the convar, the server's value is right, and the two clients disagree
// about the number -- a prediction bug nobody sees until two machines are in
// the same game.
// THE ROUND TRIP, READ END TO END, and every hop is text this file can check:
//   ConVar.cs:54        ConVarFlags.Replicated = 1u << 13
//   ConVar.cs:511-512   Engine.RegisterConVar( ..., unchecked((int)attr.Flags), ... )
//   EngineSide.cs       Api.RegisterConVar( pName, pDef, pHelp, flags, ... )
//   sourcesharp.h       int32_t (*RegisterConVar)( ..., int32_t nFlags, ... )
//   sourcesharp_gamesystem.cpp  CShimConsole::RegisterConVar( ..., nFlags, ... )
//   sourcesharp_gamesystem.cpp  m_ConVar( m_Name.c_str(), m_Default.c_str(), nFlags, ... )
// Nothing on that path masks, filters or re-maps the word. The ONE link that
// can go wrong without anyone noticing is the FIRST: the managed enum is a
// hand-written copy of the engine's bit layout, and a stand-in for native
// drifts silently. That is this tree's own rule -- "a stand-in for native
// drifts silently; assert the agreement" -- and it is what the first two facts
// below are.
// READ AS TEXT AND NOT THROUGH AN INCLUDE, deliberately. Including
// tier1/iconvar.h here would make this file's answer depend on the test
// binary's include path, and the C# half cannot be included at all -- so one
// side would be compiled and the other parsed, which is exactly the asymmetry
// that lets a drift hide. Both sides are parsed the same way.

#include "gtest/gtest.h"

#include <fstream>
#include <sstream>
#include <string>

namespace
{

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
		n++;
	return n;
}

// The bit position in `#define FCVAR_<NAME>\t\t(1<<N)`, or -1.
//
// PARSED RATHER THAN MATCHED WHOLE, because the spacing in iconvar.h is tabs of
// whatever width the original author used and a literal needle would be a test
// about whitespace.
//
// TWO THINGS THIS GOT WRONG ON THE FIRST RUN, both of which made it report a
// drift that was not there, and both of which are the reason the vacuity guards
// above every comparison exist:
//
//   * `#define` is followed by a TAB for some of these and a space for others
//     (`#define\tFCVAR_ARCHIVE` vs `#define FCVAR_CHEAT`), so a `"#define "`
//     needle silently skipped half the file;
//   * FCVAR_ARCHIVE is a PREFIX of FCVAR_ARCHIVE_XBOX, so the first match for
//     the short name was the long one's line and the parser read (1<<24) for a
//     flag that is (1<<7).
//
// So the name must be followed by whitespace, and the `(1<<` must be found
// before the end of that same line -- a define with no bit expression answers
// -1 rather than borrowing the next define's.
int EngineBit( const std::string &strHeader, const char *pszName )
{
	const std::string name( pszName );
	size_t at = 0;
	for ( ;; )
	{
		at = strHeader.find( "#define", at );
		if ( at == std::string::npos )
			return -1;

		size_t n = at + 7;
		while ( n < strHeader.size() && ( strHeader[ n ] == ' ' || strHeader[ n ] == '\t' ) )
			n++;

		if ( strHeader.compare( n, name.size(), name ) == 0 )
		{
			const size_t after = n + name.size();
			const char c = ( after < strHeader.size() ) ? strHeader[ after ] : '\0';
			if ( c == ' ' || c == '\t' )
				break;
		}
		at += 7;
	}

	const size_t nEol = strHeader.find( '\n', at );
	at = strHeader.find( "(1<<", at );
	if ( at == std::string::npos || ( nEol != std::string::npos && at > nEol ) )
		return -1;

	at += 4;
	int n = 0;
	bool bAny = false;
	while ( at < strHeader.size() && strHeader[ at ] >= '0' && strHeader[ at ] <= '9' )
	{
		n = n * 10 + ( strHeader[ at ] - '0' );
		bAny = true;
		at++;
	}
	return bAny ? n : -1;
}

// The bit position in `<Name>            = 1u << N,`, or -1.
int ManagedBit( const std::string &strCs, const char *pszName )
{
	size_t at = strCs.find( std::string( "\n    " ) + pszName );
	if ( at == std::string::npos )
		return -1;

	at = strCs.find( "1u <<", at );
	if ( at == std::string::npos )
		return -1;

	at += 5;
	while ( at < strCs.size() && strCs[ at ] == ' ' )
		at++;

	int n = 0;
	bool bAny = false;
	while ( at < strCs.size() && strCs[ at ] >= '0' && strCs[ at ] <= '9' )
	{
		n = n * 10 + ( strCs[ at ] - '0' );
		bAny = true;
		at++;
	}
	return bAny ? n : -1;
}

const char *const kIConVarH  = "public/tier1/iconvar.h";
const char *const kConVarCs  = "sourcesharp/managed/SourceSharp.Shared.Interop/ConVar.cs";
// ConVarFlags, split out of ConVar.cs (plan_abi_hotreload D3), and since
// plan_shim_areas_split phase 4 in the shared seams: a seam's signature uses it.
const char *const kConVarFlagsCs = "sourcesharp/managed/SourceSharp.Shared.Interface/ConVarFlags.cs";
const char *const kGameSys   = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
// EngineSide.cs since plan_engineapi_vtables B1: the per-side instance holds the bodies.
// The console binding, which holds the registration since
// plan_shim_areas_split phase 11 (EngineSide did before it).
const char *const kEngineCs  = "sourcesharp/managed/SourceSharp.Shared.Interop/EngineBinding.cs";
const char *const kGameDll   = "sourcesharp/game/server/shell_gamedll.cpp";

} // namespace


//=============================================================================
// 1. THE TWO SIDES AGREE ON THE BIT
//=============================================================================

TEST( ConVarReplicatedFlag, BothFilesWereReallyRead )
{
	// NOT A SKIP ARM. Every fact below is an equality between two parsed
	// numbers, and -1 == -1 is the shape that passes while measuring nothing.
	ASSERT_FALSE( ReadFile( kIConVarH ).empty() ) << kIConVarH << " did not open";
	ASSERT_FALSE( ReadFile( kConVarCs ).empty() ) << kConVarCs << " did not open";
	ASSERT_FALSE( ReadFile( kConVarFlagsCs ).empty() ) << kConVarFlagsCs << " did not open";
}

TEST( ConVarReplicatedFlag, TheReplicatedBitIsTheEnginesBit )
{
	const int nEngine  = EngineBit( ReadFile( kIConVarH ), "FCVAR_REPLICATED" );
	const int nManaged = ManagedBit( ReadFile( kConVarFlagsCs ), "Replicated" );

	ASSERT_NE( -1, nEngine )  << "FCVAR_REPLICATED's definition was not found in "
	                          << kIConVarH << ", so the equality below proves nothing";
	ASSERT_NE( -1, nManaged ) << "ConVarFlags.Replicated's value was not found in "
	                          << kConVarFlagsCs << ", so the equality below proves nothing";

	EXPECT_EQ( nEngine, nManaged )
		<< "ConVarFlags.Replicated is 1u << " << nManaged << " and FCVAR_REPLICATED "
		   "is (1<<" << nEngine << "). A managed declaration carrying the replicated "
		   "flag would register with the WRONG bit set -- the convar would appear, "
		   "take its default, and never be transmitted, and two clients would "
		   "disagree about its value with nothing in any log to say why.";
}

TEST( ConVarReplicatedFlag, ThreeMoreFlagsAgreeSoTheOneAboveIsNotACoincidence )
{
	// A single bit that happens to line up is a coincidence; four that do are
	// a layout. CHEAT is here because the ragdoll ConVars carry
	// FCVAR_REPLICATED|FCVAR_CHEAT together, so a mis-mapped CHEAT would make a
	// replicated convar settable on a live server.
	const std::string h  = ReadFile( kIConVarH );
	const std::string cs = ReadFile( kConVarFlagsCs );

	struct Pair_t { const char *pszEngine; const char *pszManaged; };
	static const Pair_t kPairs[] =
	{
		{ "FCVAR_CHEAT",           "Cheat"         },
		{ "FCVAR_NEVER_AS_STRING", "NeverAsString" },
		{ "FCVAR_ARCHIVE",         "Archive"       },
	};

	for ( size_t i = 0; i < sizeof( kPairs ) / sizeof( kPairs[ 0 ] ); i++ )
	{
		const int nEngine  = EngineBit( h,  kPairs[ i ].pszEngine );
		const int nManaged = ManagedBit( cs, kPairs[ i ].pszManaged );

		EXPECT_NE( -1, nEngine )  << kPairs[ i ].pszEngine  << " not found in " << kIConVarH;
		EXPECT_NE( -1, nManaged ) << kPairs[ i ].pszManaged << " not found in " << kConVarFlagsCs;
		EXPECT_EQ( nEngine, nManaged ) << kPairs[ i ].pszEngine << " vs ConVarFlags."
		                               << kPairs[ i ].pszManaged;
	}
}


//=============================================================================
// 2. NOTHING ON THE PATH MASKS THE WORD
//=============================================================================

TEST( ConVarReplicatedFlag, TheNativeRegistrarHandsTheFlagsStraightToTier1 )
{
	// The last hop, and the one with the most room to be clever in. A mask, a
	// filter or an "allowed flags" set here would be invisible from C#: the
	// call returns an id, the convar exists, and only the bit is gone.
	//
	// ONE BIT IS ADDED HERE NOW, AND ADDING IS NOT MASKING. The registrar ORs
	// the side's DLL identifier (FCVAR_GAMEDLL on the server, FCVAR_CLIENTDLL on
	// the client) into nFlags before constructing, because ConVar_Register gives
	// that flag only to convars that already exist and a managed one is built
	// later -- see convar_dllflag_test.cpp for the A/B that measured it. The
	// assertions below are unaffected: they pin that nFlags reaches tier1 as
	// nFlags, and nothing is removed from it, which is what protects the
	// replicated bit this file is about.
	const std::string code = ReadFile( kGameSys );
	ASSERT_FALSE( code.empty() ) << kGameSys << " did not open";

	// The ctor's own parameter list -- the ConVar is constructed with nFlags
	// verbatim, not with anything derived from it.
	EXPECT_EQ( 1u, CountOf( code, "m_ConVar( m_Name.c_str(), m_Default.c_str(), nFlags, m_Help.c_str()," ) )
		<< "CSourceSharpConVar no longer hands nFlags to the tier1 ConVar unchanged";

	// And the registrar passes the caller's word into it, likewise verbatim.
	EXPECT_EQ( 1u, CountOf( code, "pHelp ? pHelp : \"\", nFlags," ) )
		<< "CShimConsole::RegisterConVar no longer passes nFlags through";
}

TEST( ConVarReplicatedFlag, TheManagedSideSendsTheDeclaredFlagsUnmasked )
{
	const std::string cs = ReadFile( kConVarCs );
	ASSERT_FALSE( cs.empty() ) << kConVarCs << " did not open";

	// `unchecked((int)attr.Flags)` is the whole conversion: a widening of the
	// declared enum, with no & and no allowlist.
	EXPECT_EQ( 1u, CountOf( cs, "unchecked((int)attr.Flags)" ) )
		<< "the declaration's flags no longer reach Engine.RegisterConVar as "
		   "themselves -- something is now deriving the argument";
}

TEST( ConVarReplicatedFlag, TheAbiForwarderPassesFlagsThrough )
{
	const std::string cs = ReadFile( kEngineCs );
	ASSERT_FALSE( cs.empty() ) << kEngineCs << " did not open";

	// The router's retirement (docs/todo.md) put the typed area call inline in
	// the wrapper, and plan_shim_areas_split phase 7 made its two dispatch arms
	// one call on the side's own container (on its console area since phase
	// 9; on the console area the binding is handed since phase 11): `flags`
	// must reach that call as itself. The count moved with the shape, the
	// contract did not.
	EXPECT_EQ( 1u, CountOf( cs, "_consoleArea.RegisterConVar(pName, pDef, pHelp, flags," ) )
		<< "EngineConsole.RegisterConVar no longer forwards `flags` to its console "
		   "area -- something is now deriving the argument";
	EXPECT_EQ( 1u, CountOf( cs, "RegisterConVar(pName, pDef, pHelp, " ) )
		<< "Engine.RegisterConVar calls the shim area more than once again: "
		   "each call must forward `flags` as itself";
}


//=============================================================================
// 3. THE FLAG REACHES A PATH ONLY A REPLICATED CONVAR TAKES
//
// The two facts above prove the bit survives. This one proves it is READ: the
// shell's IConCommandBaseAccessor is stock's CGameDLL_ConVarAccessor
// transliterated, and its replicated-default capture is the behaviour that
// makes a replicated ConVar take the SERVER's default rather than the client's.
// Without it, a debug game DLL takes a shared ConVar's value from the client --
// which is the failure that arm was written for.
//=============================================================================

TEST( ConVarReplicatedFlag, TheShellsAccessorStillDoesStocksReplicatedDefaultCapture )
{
	const std::string code = ReadFile( kGameDll );
	ASSERT_FALSE( code.empty() ) << kGameDll << " did not open";

	EXPECT_EQ( 1u, CountOf( code, "pCommand->IsFlagSet(FCVAR_REPLICATED)" ) )
		<< "the shell's RegisterConCommandBase no longer asks whether the "
		   "ConVar is replicated, so a managed replicated declaration registers "
		   "and is then treated as an ordinary one";

	EXPECT_EQ( 1u, CountOf( code, "((ConVar *)pCommand)->SetValue(pDefault);" ) )
		<< "the replicated-default capture is gone: a replicated ConVar not "
		   "overridden at the command line no longer takes the server's value";
}


//=============================================================================
// 4. THE VERDICT, WRITTEN DOWN
//=============================================================================

TEST( ConVarReplicatedFlag, TheDoubleRegistrationNoteIsNarrowerThanItReads )
{
	// SourceSharp.Server/ConVars.cs opens with "a ConVar in Shared would
	// register from both DLLs and Source rejects the same name from client and
	// server". That is right about an ORDINARY convar and wrong as a general
	// rule, and the counter-example is in this tree: ragdoll_shared.cpp is
	// compiled into BOTH vpcs and declares three FCVAR_REPLICATED ConVars, so
	// both DLLs register those three names today and the game runs. Replication
	// REQUIRES both sides to declare the name -- that is what the flag means.
	//
	// Pinned here rather than edited there, because ConVars.cs is not this
	// lane's file and the sentence is true of what it is actually about. What
	// this fact stops is the note being read as "a replicated convar cannot be
	// declared from managed code", which is the conclusion that would leave
	// three replicated ConVars native forever.
	const std::string cs = ReadFile( "sourcesharp/managed/SourceSharp.Server/ConVars.cs" );
	ASSERT_FALSE( cs.empty() ) << "SourceSharp.Server/ConVars.cs did not open";

	// The needle is one LINE of the note, not the sentence: the XML doc
	// comment wraps it across two `/// ` lines, and a sentence-shaped needle
	// would answer zero for a note that is right there.
	EXPECT_LE( 1u, CountOf( cs, "Source rejects the same name from" ) )
		<< "the note this fact is about is gone from ConVars.cs -- if it was "
		   "corrected, delete this fact with it; if it moved, re-point it";
}
