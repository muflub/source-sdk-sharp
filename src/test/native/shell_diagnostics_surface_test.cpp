//========= SourceSharp — what shell_diagnostics.h promises ==================//
//
// plan_shim.md §5 3e. This header is included by thirty-four translation units,
// and every name it declares is a PROMISE made to all of them: that a function
// exists, and that somebody may call it. Four of those promises had no taker.
//
// TWO WERE DEAD ON BOTH SIDES:
//
//   SourceSharp_DumpDataMap        walked a prediction/save datamap, for a
//                                  structure this port's entities do not use --
//                                  they go through SourceSharp.Abi's
//                                  SavedEntityCodec, and the engine's own
//                                  datamaps are read by shell_savegame.h's
//                                  codec. A walker for a thing the build does
//                                  not produce.
//   SourceSharp_GetAssertsLogged   the count of assertions -assertlog demoted.
//                                  The counter stays and the summary line
//                                  prints it; the accessor had no caller.
//
// TWO HAD EXACTLY ONE CALLER, IN THE SAME .cpp, and are file-local now:
//
//   SourceSharp_NotShippedLedger   read by the cs_{sv,cl}_notshipped body. Its
//                                  declaration exported CShellNotShipped's
//                                  whole interface to every includer.
//   SourceSharp_InstallAssertStacks  called by SourceSharp_InstallAssertLogging
//                                  thirty lines below it. The header said
//                                  "Installed from SourceSharp_Install-
//                                  AssertLogging" and declared it publicly
//                                  anyway.
//
// AND WHAT MUST NOT GO WITH THEM, which is the more important half of this
// file. Three things in this cluster are reached BY NAME, and no census, link
// line or compiler sees any of them:
//
//   -assertlog             31 tools pass it on every launch. The decision
//                          behind it is assert_stack.h's
//                          SourceSharp_AssertShouldBeFatal.
//   cs_{sv,cl}_crossings   8 tools, tools/live-gate and the DebugApi drive it.
//   cs_cl_notshipped       tools/7a-effect-gate and tools/7c-view-gate read
//                          its rows.
//
// A ConCommand that stops being registered is not a link error and not a test
// failure. It is a tool that sends a command into a console which answers
// "Unknown command", which reads as a typo in the tool.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cctype>
#include <fstream>
#include <sstream>
#include <string>

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

std::string Read( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( !pRoot )
		return std::string();

	std::ifstream in( ( std::string( pRoot ) + "/" + pRelative ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// Comments stripped, whitespace squashed: every rule below is argued at length
// in a comment beside it, and a scan over the raw text would be satisfied by
// the argument rather than by the code.
std::string CodeOf( const char *pRelative )
{
	const std::string raw = Read( pRelative );

	std::string out;
	out.reserve( raw.size() );

	bool bLine = false, bBlock = false;
	for ( size_t i = 0; i < raw.size(); i++ )
	{
		if ( bLine )
		{
			if ( raw[ i ] == '\n' ) { bLine = false; out.push_back( '\n' ); }
			continue;
		}
		if ( bBlock )
		{
			if ( raw[ i ] == '*' && i + 1 < raw.size() && raw[ i + 1 ] == '/' ) { bBlock = false; i++; }
			continue;
		}
		if ( raw[ i ] == '/' && i + 1 < raw.size() && raw[ i + 1 ] == '/' ) { bLine = true; continue; }
		if ( raw[ i ] == '/' && i + 1 < raw.size() && raw[ i + 1 ] == '*' ) { bBlock = true; i++; continue; }
		out.push_back( raw[ i ] );
	}

	std::string squashed;
	squashed.reserve( out.size() );
	for ( size_t i = 0; i < out.size(); i++ )
	{
		if ( !isspace( (unsigned char)out[ i ] ) )
			squashed.push_back( out[ i ] );
	}
	return squashed;
}

bool Has( const std::string &haystack, const char *needle )
{
	std::string n;
	for ( const char *p = needle; *p; ++p )
	{
		if ( !isspace( (unsigned char)*p ) )
			n.push_back( *p );
	}
	return haystack.find( n ) != std::string::npos;
}

const std::string &Header()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_diagnostics.h" );
	return s;
}

const std::string &Source()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_diagnostics.cpp" );
	return s;
}

// The server's two command bodies, cs_sv_crossings and cs_sv_notshipped. They
// are diagnostics, so they live in a file only a debug build of server.so
// compiles, and reach the shared printers through shell_diagnostics_commands.h.
const std::string &DebugSource()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_diagnostics_debug.cpp" );
	return s;
}

const std::string &CommandsHeader()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_diagnostics_commands.h" );
	return s;
}

} // namespace

TEST( ShellDiagnosticsSurface, TheFilesOpened )
{
	ASSERT_FALSE( Header().empty() ) << "shell_diagnostics.h did not open";
	ASSERT_FALSE( Source().empty() ) << "shell_diagnostics.cpp did not open";
}

TEST( ShellDiagnosticsSurface, TheTwoDeadEntryPointsAreGoneFromBothFiles )
{
	ASSERT_FALSE( Header().empty() );
	ASSERT_FALSE( Source().empty() );

	EXPECT_FALSE( Has( Header(), "SourceSharp_DumpDataMap" ) );
	EXPECT_FALSE( Has( Source(), "SourceSharp_DumpDataMap" ) )
		<< "the datamap walker is back. This port's entities do not go through "
		   "a datamap -- SavedEntityCodec is what saves them -- so it prints "
		   "nothing about the thing its comment describes.";

	EXPECT_FALSE( Has( Header(), "SourceSharp_GetAssertsLogged" ) );
	EXPECT_FALSE( Has( Source(), "SourceSharp_GetAssertsLogged" ) );

	// And the counter it read is STILL THERE, which is the half the deletion
	// could have taken with it: a reading nobody can get is worse than an
	// accessor nobody calls.
	EXPECT_TRUE( Has( Source(), "g_nAssertsLogged" ) )
		<< "the assertion counter went with its accessor. -assertlog's summary "
		   "line is the reading anybody wanted, and 31 tools pass that flag.";
}

TEST( ShellDiagnosticsSurface, TheTwoSingleCallerEntryPointsAreFileLocal )
{
	ASSERT_FALSE( Header().empty() );
	ASSERT_FALSE( Source().empty() );

	for ( const char *pszFn : { "SourceSharp_NotShippedLedger",
	                            "SourceSharp_InstallAssertStacks" } )
	{
		EXPECT_FALSE( Has( Header(), pszFn ) )
			<< pszFn << " is declared in shell_diagnostics.h again. The header "
			   "is included by thirty-four translation units and its one caller "
			   "is in the .cpp beside it.";
	}

	EXPECT_TRUE( Has( Source(), "staticconstCShellNotShipped&SourceSharp_NotShippedLedger()" ) )
		<< "SourceSharp_NotShippedLedger lost its `static`, which makes it an "
		   "exported symbol again with no declaration to match -- the worst of "
		   "both, because nothing then says what its linkage is meant to be.";
	EXPECT_TRUE( Has( Source(), "staticvoidSourceSharp_InstallAssertStacks()" ) );
}

//=============================================================================
// WHAT MUST STAY, and each of the three is reached by a NAME that no
// instrument in this tree can follow.
//=============================================================================

TEST( ShellDiagnosticsSurface, TheCrossingsCommandIsStillRegisteredOnBothSides )
{
	ASSERT_FALSE( Source().empty() );

	// tools/3d-cmdgate, tools/policy-player-crossings,
	// tools/6a-crossings-by-entity-count, tools/6b-policy-by-entity-count,
	// tools/sub-variant-inputs, tools/sub-entitylist-crossings,
	// tools/savesmash-repro and tools/live-gate all send one of these two
	// names into a running console.
	ASSERT_FALSE( DebugSource().empty() ) << "shell_diagnostics_debug.cpp did not open";
	EXPECT_TRUE( Has( Source(), "CON_COMMAND(cs_cl_crossings," ) );
	EXPECT_TRUE( Has( DebugSource(), "SS_NATIVE_COMMAND(cs_sv_crossings)" ) );
	EXPECT_TRUE( Has( DebugSource(), "SourceSharp_PrintCrossings(args.ArgC()>1&&ss::StrICmp(args.Arg(1),\"reset\")==0);" ) )
		<< "cs_sv_crossings no longer prints through the shared body, or lost its reset verb.";
	EXPECT_TRUE( Has( CommandsHeader(), "voidSourceSharp_PrintCrossings(boolbReset);" ) );
	EXPECT_FALSE( Has( Header(), "SourceSharp_PrintCrossings" ) )
		<< "the printer is declared in shell_diagnostics.h, which dozens of TUs include; "
		   "shell_diagnostics_commands.h is its declaration.";

	// AND IT MUST ANSWER WHEN MANAGED IS NOT THERE, which is the reason this
	// command is native and stays native. A managed ConCommand does not exist
	// until the managed side has started, so the moment the report is most
	// worth asking for -- "the managed side did not come up, what does the
	// counter say" -- is the moment a managed registration would leave the
	// console answering "Unknown command".
	EXPECT_TRUE( Has( Source(), "SourceSharp_FetchCrossingsReport" ) )
		<< "the report is no longer fetched through crossings_fetch.h, which is "
		   "what turns a null slot into a printed reason rather than silence.";
}

TEST( ShellDiagnosticsSurface, TheNotShippedCommandIsStillRegisteredOnBothSides )
{
	ASSERT_FALSE( Source().empty() );

	// tools/7a-effect-gate and tools/7c-view-gate read cs_cl_notshipped's rows
	// and assert on them.
	ASSERT_FALSE( DebugSource().empty() ) << "shell_diagnostics_debug.cpp did not open";
	EXPECT_TRUE( Has( Source(), "CON_COMMAND(cs_cl_notshipped," ) );
	EXPECT_TRUE( Has( DebugSource(), "SS_NATIVE_COMMAND(cs_sv_notshipped){SourceSharp_PrintNotShipped();}" ) );
	EXPECT_TRUE( Has( CommandsHeader(), "voidSourceSharp_PrintNotShipped();" ) );
	EXPECT_FALSE( Has( Header(), "SourceSharp_PrintNotShipped" ) );

	// The DECLARED list is printed beside the reached ledger, which is what
	// lets a reader tell an empty ledger from an empty declaration -- opposite
	// findings that print the same zero.
	EXPECT_TRUE( Has( Source(), "ShellNotShipped_Interfaces(&nRows)" ) );
}

TEST( ShellDiagnosticsSurface, TheAssertLogInstallerIsStillAnEntryPoint )
{
	ASSERT_FALSE( Header().empty() );
	ASSERT_FALSE( Source().empty() );

	// 31 tools pass -assertlog on every launch; sourcesharp_gamesystem.cpp
	// calls this before anything that can assert.
	EXPECT_TRUE( Has( Header(), "voidSourceSharp_InstallAssertLogging();" ) )
		<< "the -assertlog installer is no longer declared. It is called from "
		   "sourcesharp_gamesystem.cpp, so this would be a link error -- but the "
		   "fact is here because the flag itself is reached by name and 31 tools "
		   "depend on it doing something.";
	EXPECT_TRUE( Has( Source(), "voidSourceSharp_InstallAssertLogging()" ) );

	// And -assertstack is still installed FROM it, which is the arrangement
	// that let its installer stop being an entry point.
	EXPECT_TRUE( Has( Source(), "SourceSharp_InstallAssertStacks();" ) );
}

//=============================================================================//
// THE SERVER'S REPORT COMES FROM ITS MANAGED-BUILT AREA (plan_engineapi_vtables
// E14), and the client's from its own (plan_client_vtables CE9a). Read as
// PrintCrossings' two arms.
//=============================================================================//

namespace
{

// The #ifndef CLIENT_DLL arm and the #else arm inside PrintCrossings, or empty.
void CrossingsArms( std::string &strServer, std::string &strClient )
{
	const std::string &src = Source();
	const size_t body  = src.find( "voidSourceSharp_PrintCrossings(boolbReset){" );
	const size_t open  = body == std::string::npos ? body : src.find( "#ifndefCLIENT_DLL", body );
	const size_t other = open == std::string::npos ? open : src.find( "#else", open );
	const size_t close = other == std::string::npos ? other : src.find( "#endif", other );
	if ( close == std::string::npos )
		return;
	strServer = src.substr( open, other - open );
	strClient = src.substr( other, close - other );
}

} // namespace

TEST( ShellDiagnosticsSurface, TheServerArmAsksTheDiagnosticsArea )
{
	std::string server, client;
	CrossingsArms( server, client );
	ASSERT_FALSE( server.empty() ) << "PrintCrossings has no #ifndef CLIENT_DLL / #else arms";

	EXPECT_TRUE( Has( server, "SourceSharpCrossingsReportFn pFn = ManagedDiagnostics() ? &SourceSharp_ServerCrossingsReport : NULL;" ) )
		<< "the server arm does not hand the fetch loop the area's adapter";
	EXPECT_FALSE( Has( server, "SourceSharp_GetManagedApi" ) )
		<< "the server arm still reads the ManagedApi struct, whose CrossingsReport it no longer fills";
}

TEST( ShellDiagnosticsSurface, TheServerAdapterAsksTheAreaOnEveryCall )
{
	ASSERT_FALSE( Source().empty() );

	EXPECT_TRUE( Has( Source(),
		"static int SourceSharp_ServerCrossingsReport( char *pBuffer, int nBytes, int bReset ) {"
		" IManagedDiagnostics *pDiagnostics = ManagedDiagnostics();"
		" return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1; }" ) )
		<< "the adapter does not read the area at the point of use, or does not refuse without one";
}

// plan_client_vtables CE9a: the client's report moved to its managed-built
// IManagedDiagnosticsClient; the Bootstrap no longer fills its slot.
TEST( ShellDiagnosticsSurface, TheClientArmAsksTheDiagnosticsArea )
{
	std::string server, client;
	CrossingsArms( server, client );
	ASSERT_FALSE( client.empty() ) << "PrintCrossings has no #else arm";

	EXPECT_TRUE( Has( client, "SourceSharpCrossingsReportFn pFn = ManagedDiagnosticsClient() ? &SourceSharp_ClientCrossingsReport : NULL;" ) )
		<< "the client arm does not hand the fetch loop the area's adapter";
	EXPECT_FALSE( Has( client, "SourceSharp_GetManagedApi" ) )
		<< "the client arm still reads the ManagedApi struct, whose CrossingsReport it no longer fills";
}

TEST( ShellDiagnosticsSurface, TheClientAdapterAsksTheAreaOnEveryCall )
{
	ASSERT_FALSE( Source().empty() );

	EXPECT_TRUE( Has( Source(),
		"static int SourceSharp_ClientCrossingsReport( char *pBuffer, int nBytes, int bReset ) {"
		" IManagedDiagnosticsClient *pDiagnostics = ManagedDiagnosticsClient();"
		" return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1; }" ) )
		<< "the adapter does not read the area at the point of use, or does not refuse without one";
}
