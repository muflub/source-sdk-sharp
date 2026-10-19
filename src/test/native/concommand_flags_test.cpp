//========= SourceSharp — the FCVAR word on a managed console command =========//
//
// WHY THIS EXISTS. `Engine.RegisterConCommand` took a name and a help string
// and nothing else, so a C#-declared console command was always an UNFLAGGED
// one. docs/convar-ledger.tsv carried thirteen rows at `deferred-abi` for
// exactly that: every one of them is a probe or a fixture whose declaration
// says FCVAR_CHEAT, and a cheat command that runs with sv_cheats 0 is not the
// command that was being ported. Two of the thirteen carry a second flag the
// slot could not express either -- FCVAR_SERVER_CAN_EXECUTE on
// `cl_soundscape_flush` and FCVAR_DEVELOPMENTONLY on
// `sv_soundscape_printdebuginfo`.
//
// THE FLAG WORD IS NOT ENOUGH ON ITS OWN, which is the half a reviewer would
// not think to ask for. ConVar_Register ORs its nCVarFlag into every
// ConCommandBase on the static construction list and then sets that list to
// NULL (tier1/convar.cpp:70-80); a managed ConCommand is built long after that
// has run, is on no list, and so is given neither FCVAR_GAMEDLL nor
// FCVAR_CLIENTDLL. For a CONVAR the consequence is AreConVarsLinkable refusing
// the replicated link -- that is convar_dllflag_test's subject, next door. For
// a COMMAND the consequence is different and this file is about that one:
// the engine reads FCVAR_GAMEDLL off the ConCommandBase it found to decide
// whether a line typed at a listen server's console is the SERVER's command
// and the command client must be set before dispatch
// (tier1/commandbuffer.cpp:559-585). A managed server command without the flag
// is still found and still run, with nobody recorded as having typed it.
//
// So the assertions below are three, and each is a separate way to get this
// wrong: the flag word reaches the ConCommand constructor at all, the side
// flag is ORed in, and it is ORed in BEFORE the object is constructed rather
// than after -- tier1 reads the flags in the constructor and hands the object
// straight to the engine.
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

const char *kRegistrar = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
// The slot's declaration: IShimConsole's method since the engine API struct
// was deleted (plan_client_vtables CC-last), and what managed calls it
// through, the generated forwarder struct ShimConsoleFwd
// (plan_forwarders_everywhere.md).
const char *kHeader     = "sourcesharp/game/shared/ishim_console.h";
const char *kForwarders = "sourcesharp/managed/SourceSharp.Abi/SharedForwarders.g.cs";

size_t IndexOf( const std::string &hay, const std::string &needle )
{
	return hay.find( needle );
}

// One generated forwarder struct, from its declaration to its closing brace,
// or empty: a field found anywhere else in the file is another struct's.
std::string LayoutWindow( const std::string &s, const char *pszStruct )
{
	const size_t open = s.find( std::string( "internal unsafe struct " ) + pszStruct + " : IForwarderTable\n" );
	if ( open == std::string::npos )
		return std::string();

	const size_t close = s.find( "\n}", open );
	return ( close == std::string::npos ) ? std::string() : s.substr( open, close - open );
}

// The registrar, from its opening line to the closing brace of the function.
// Every assertion about ORDER below is taken inside this window: the convar
// registrar directly above it ORs the same two flags, so a search over the
// whole file would find ITS lines and pass while this function had none.
std::string RegistrarBody( const std::string &s )
{
	const size_t open = IndexOf( s, "int32_t CShimConsole::RegisterConCommand(" );

	if ( open == std::string::npos )
		return std::string();

	const size_t close = s.find( "\n}\n", open );

	return ( close == std::string::npos ) ? std::string() : s.substr( open, close - open );
}

} // namespace

TEST( ConCommandFlags, TheFilesAreReallyReadAndAreTheirSubjects )
{
	const std::string reg = Read( kRegistrar );
	const std::string hdr = Read( kHeader );

	ASSERT_FALSE( reg.empty() ) << kRegistrar << " did not open; every assertion "
	                               "below would pass vacuously on an empty string";
	ASSERT_FALSE( hdr.empty() ) << kHeader << " did not open";

	EXPECT_NE( IndexOf( reg, "CShimConsole::RegisterConCommand" ), std::string::npos )
		<< "this file no longer defines the managed console-command registrar";
	EXPECT_NE( IndexOf( hdr, "RegisterConCommand" ), std::string::npos )
		<< "the console's shim header no longer declares a RegisterConCommand slot";
}

TEST( ConCommandFlags, TheAbiSlotCarriesAFlagWord )
{
	const std::string hdr = Read( kHeader );
	ASSERT_FALSE( hdr.empty() );

	EXPECT_NE( IndexOf( hdr,
	                    "virtual int32_t RegisterConCommand( const char *pName, const char *pHelp, "
	                    "int32_t nFlags ) = 0;" ),
	           std::string::npos )
		<< "the RegisterConCommand slot no longer takes an FCVAR word, so a "
		   "managed [ConCommand] can only ever be an unflagged command and the "
		   "thirteen deferred-abi ledger rows are blocked again";

	// And what managed calls carries the word too: the object, the name, the
	// help, the flags, and the int32 answer. A forwarder struct that dropped it
	// would call the method with a flag word read out of whatever the register
	// held.
	const std::string layout = LayoutWindow( Read( kForwarders ), "ShimConsoleFwd" );
	ASSERT_FALSE( layout.empty() ) << "ShimConsoleFwd was not found in " << kForwarders;
	EXPECT_NE( IndexOf( layout, "delegate* unmanaged<void*, byte*, byte*, int, int> RegisterConCommand;" ),
	           std::string::npos )
		<< "ShimConsoleFwd's RegisterConCommand is not the four-argument, flag-carrying shape";
}

TEST( ConCommandFlags, TheRegistrarIsFoundAsAWindow )
{
	const std::string body = RegistrarBody( Read( kRegistrar ) );

	ASSERT_FALSE( body.empty() )
		<< "the registrar's opening line or its closing brace moved, so the "
		   "three ordering assertions below would be taken over an empty string";
	EXPECT_NE( IndexOf( body, "g_SourceSharpCommands.Add(" ), std::string::npos )
		<< "the window found is not the function that appends a command";
}

TEST( ConCommandFlags, TheFlagWordReachesTheConCommandConstructor )
{
	const std::string reg = Read( kRegistrar );
	ASSERT_FALSE( reg.empty() );

	EXPECT_NE( IndexOf( reg, "m_Command( m_Name.c_str(), this, m_Help.c_str(), nFlags )" ),
	           std::string::npos )
		<< "CSourceSharpCommand builds its ConCommand with something other than "
		   "the flag word it was handed; it was a literal 0 before this slot "
		   "carried flags, and a literal here means every managed command is "
		   "unflagged however it was declared";
}

TEST( ConCommandFlags, BothSideFlagsAreAppliedAndGuardedApart )
{
	const std::string body = RegistrarBody( Read( kRegistrar ) );
	ASSERT_FALSE( body.empty() );

	const size_t client = IndexOf( body, "nFlags |= FCVAR_CLIENTDLL;" );
	const size_t server = IndexOf( body, "nFlags |= FCVAR_GAMEDLL;" );
	const size_t guard  = IndexOf( body, "#ifdef CLIENT_DLL" );

	EXPECT_NE( client, std::string::npos )
		<< "the client arm no longer ORs FCVAR_CLIENTDLL into a managed command's "
		   "flags";
	EXPECT_NE( server, std::string::npos )
		<< "the server arm no longer ORs FCVAR_GAMEDLL into a managed command's "
		   "flags, so the engine cannot tell a managed server command from a client "
		   "one and dispatches it with no command client set";

	ASSERT_NE( client, std::string::npos );
	ASSERT_NE( server, std::string::npos );
	ASSERT_NE( guard,  std::string::npos );

	// Guarded apart, not both. One value for both sides is the shape that took
	// the convar warning count from 5 to 29 rather than to 0.
	EXPECT_LT( guard, client );
	EXPECT_LT( client, server );
}

TEST( ConCommandFlags, TheSideFlagIsAppliedBeforeTheCommandIsConstructed )
{
	const std::string body = RegistrarBody( Read( kRegistrar ) );
	ASSERT_FALSE( body.empty() );

	const size_t server = IndexOf( body, "nFlags |= FCVAR_GAMEDLL;" );
	const size_t build  = IndexOf( body, "new CSourceSharpCommand(" );

	ASSERT_NE( server, std::string::npos );
	ASSERT_NE( build,  std::string::npos );
	EXPECT_LT( server, build )
		<< "the side flag is ORed in AFTER the command is constructed, which is "
		   "too late: tier1 reads the flags in the ConCommand constructor and "
		   "the engine has already been handed the object";
}
