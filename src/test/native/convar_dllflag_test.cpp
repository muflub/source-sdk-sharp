//========= SourceSharp — the DLL identifier flag on a managed convar =========//
//
// WHY THIS EXISTS. ConVar_Register ORs its nCVarFlag into every ConCommandBase
// on the static construction list, calls Init on each, and then sets that list
// to NULL (tier1/convar.cpp:70-80). A managed convar is built AFTER that has
// run, so it is on no list and is given no flag — it carries neither
// FCVAR_GAMEDLL nor FCVAR_CLIENTDLL, where every natively declared convar in
// the same DLL carries one.
//
// AreConVarsLinkable refuses the replicated link when the child is
// FCVAR_CLIENTDLL and the parent is not FCVAR_GAMEDLL. So before the line this
// file pins, a managed server-side replicated declaration became a parent the
// client's native twin was refused: m_pParent never assigned, the twin left at
// its own declared default forever. Measured live, A/B on one build with and
// without the line, same map, same 123 managed convars registered either way:
//
//     without   5  "For FCVAR_REPLICATED, ConVar must be defined in ..."
//     with      0
//
// The five were GameVars.cs's: mp_allowspectators, mp_fadetoblack,
// mp_forcecamera, mp_friendlyfire, sv_hudhint_sound.
//
// THE FLAG IS CHOSEN NATIVELY, PER SIDE, AND THAT IS THE POINT. This file is
// compiled into both DLLs; a flag chosen on the managed side would be one value
// for both, which is the shape of an earlier attempt that took the warnings
// from 5 to 29 rather than to 0.
//=============================================================================//
#include "gtest/gtest.h"
#include <fstream>
#include <sstream>
#include <string>

namespace {

std::string Read( const char *pszRel )
{
	const char *kRoots[] = { "", "../", "../../" };
	for ( size_t i = 0; i < sizeof( kRoots ) / sizeof( kRoots[ 0 ] ); i++ )
	{
		std::ifstream f( ( std::string( kRoots[ i ] ) + pszRel ).c_str() );
		if ( !f )
			continue;
		std::ostringstream ss;
		ss << f.rdbuf();
		return ss.str();
	}
	return std::string();
}

const char *kFile = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";

size_t IndexOf( const std::string &hay, const std::string &needle )
{
	return hay.find( needle );
}

} // namespace

TEST( ConVarDllFlag, TheFileIsReallyReadAndIsTheRegistrar )
{
	const std::string s = Read( kFile );
	ASSERT_FALSE( s.empty() ) << kFile << " did not open; every assertion below "
	                             "would pass vacuously on an empty string";
	EXPECT_NE( IndexOf( s, "CShimConsole::RegisterConVar" ), std::string::npos )
		<< "this file no longer defines the managed convar registrar, so it is "
		   "not the file this test reasons about";
}

TEST( ConVarDllFlag, BothSideFlagsAreAppliedAndGuardedApart )
{
	const std::string s = Read( kFile );
	ASSERT_FALSE( s.empty() );

	const size_t client = IndexOf( s, "nFlags |= FCVAR_CLIENTDLL;" );
	const size_t server = IndexOf( s, "nFlags |= FCVAR_GAMEDLL;" );

	EXPECT_NE( client, std::string::npos )
		<< "the client side no longer ORs FCVAR_CLIENTDLL into a managed convar's "
		   "flags; its replicated convars become unlinkable children";
	EXPECT_NE( server, std::string::npos )
		<< "the server side no longer ORs FCVAR_GAMEDLL into a managed convar's "
		   "flags; AreConVarsLinkable then refuses every client twin of a managed "
		   "replicated declaration";

	ASSERT_NE( client, std::string::npos );
	ASSERT_NE( server, std::string::npos );

	// Guarded apart, not both: one value for both sides is the failure mode that
	// took the warning count from 5 to 29 rather than to 0.
	const size_t guard = IndexOf( s, "#ifdef CLIENT_DLL" );
	EXPECT_NE( guard, std::string::npos );
	EXPECT_LT( guard, client )
		<< "FCVAR_CLIENTDLL is not inside a CLIENT_DLL guard, so both DLLs would "
		   "take the same arm";
	EXPECT_LT( client, server )
		<< "the two flags are not in the #ifdef/#else order this test assumes; "
		   "read the registrar before trusting either assertion";
}

TEST( ConVarDllFlag, TheFlagIsAppliedBeforeTheConVarIsConstructed )
{
	const std::string s = Read( kFile );
	ASSERT_FALSE( s.empty() );

	const size_t server = IndexOf( s, "nFlags |= FCVAR_GAMEDLL;" );
	const size_t build  = IndexOf( s, "new CSourceSharpConVar(" );

	ASSERT_NE( server, std::string::npos );
	ASSERT_NE( build,  std::string::npos );
	EXPECT_LT( server, build )
		<< "the flag is ORed in AFTER the ConVar is constructed, which is too "
		   "late: tier1 reads the flags in the constructor and the engine has "
		   "already been handed the object";
}
