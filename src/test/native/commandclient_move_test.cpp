//========= SourceSharp — who typed the command, after Phase A4 ===============//
//
// plan_gamedll_after.md Phase A4 subject 3 split ONE fact across two files.
// Until this phase, `g_nCommandClientIndex` was a file-scope int in
// gameinterface.cpp: CServerGameClients::SetCommandClient assigned it at :3537
// and UTIL_GetCommandClientIndex read it at :312, 3,200 lines apart but in the
// same translation unit, so the compiler guaranteed they were the same object.
//
// They are now in different files and the guarantee is a LINK, not a scope. The
// way this breaks is silent and this project has already paid for it once: if
// the write stops reaching the slot, UTIL_GetCommandClientIndex answers 1
// forever, UTIL_GetCommandClientActivator resolves whoever is in slot 1, and
// every ent_fire from a console appears to be ignored by entities that were
// never sent anything. docs/gamedll-ledger.md and util.h:641-653 record the
// hour that cost.
//
// Source-read, and that is forced rather than chosen: shell_commandclient.cpp
// includes cbase.h's whole world -- CBasePlayer, CStandalonePlayer,
// Activator_t -- and can never be linked into a gtest binary. The behavioural
// half is a live gate: a console ent_fire on a running server.
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

const char *kHome        = "sourcesharp/game/server/shell_commandclient.cpp";
// The slot's one WRITER, CServerGameClients::SetCommandClient. It was in
// game/server/gameinterface.cpp when A4 split the read from the write, and it
// moved with its class to shell_gameclients.cpp at plan_gamedll_after.md Phase
// A9 step 2. The two are still in different translation units, which is the
// whole hazard this file exists for -- what used to be a scope guarantee is a
// link -- so the fact is unchanged and only the file naming the writer moved.
const char *kGameIface   = "sourcesharp/game/server/shell_gameclients.cpp";

} // namespace

//-----------------------------------------------------------------------------
// THE WRITE. SetCommandClient is the ONLY writer of the slot, and after A4 it
// writes through a call rather than an assignment.
//-----------------------------------------------------------------------------
TEST( CommandClientMove, SetCommandClientWritesThroughTheSetter )
{
	const std::string code = Code( kGameIface );
	ASSERT_FALSE( code.empty() );

	const size_t at = code.find( "voidCServerGameClients::SetCommandClient(intindex){" );
	ASSERT_NE( at, std::string::npos )
		<< "CServerGameClients::SetCommandClient is not in " << kGameIface
		<< " any more. It is the engine's only way of naming the console "
		<< "client; if it moved, this whole fact is reading the wrong file.";

	const size_t close = code.find( '}', at );
	ASSERT_NE( close, std::string::npos );

	const std::string body = code.substr( at, close - at );
	EXPECT_NE( body.find( "UTIL_SetCommandClientIndex(index);" ), std::string::npos )
		<< "SetCommandClient no longer reaches the slot through the setter. "
		<< "Its body is now: " << body;
}

TEST( CommandClientMove, NothingAssignsAFileScopeSlotAnyMore )
{
	const std::string code = Code( kGameIface );
	ASSERT_FALSE( code.empty() );

	EXPECT_EQ( code.find( "g_nCommandClientIndex" ), std::string::npos )
		<< "the file-scope slot is back in gameinterface.cpp. There is one "
		<< "slot and it is a field of CSourceSharpCommandClient; a second "
		<< "record of it is a value that can be written and never read.";
}

//-----------------------------------------------------------------------------
// THE READ, and the off-by-one that came with it. The engine's slot is 0-based
// and -1 for the dedicated console; every caller of UTIL_GetCommandClientIndex
// expects a 1-based index. Stock did the +1 here and so does this.
//-----------------------------------------------------------------------------
TEST( CommandClientMove, TheGetterStillAddsOneToTheEnginesSlot )
{
	const std::string code = Code( kHome );
	ASSERT_FALSE( code.empty() );

	const size_t at = code.find( "intUTIL_GetCommandClientIndex(void){" );
	ASSERT_NE( at, std::string::npos )
		<< "UTIL_GetCommandClientIndex is not defined in " << kHome;

	const size_t close = code.find( '}', at );
	ASSERT_NE( close, std::string::npos );

	const std::string body = code.substr( at, close - at );
	EXPECT_NE( body.find( "return(CommandClient().Slot()+1);" ), std::string::npos )
		<< "UTIL_GetCommandClientIndex no longer answers the engine's slot "
		<< "plus one. Its body is now: " << body << " -- dropping the +1 "
		<< "makes every console command resolve to the player one slot below "
		<< "the one that typed it, and answers 0 (nobody) for player 1.";
}

//-----------------------------------------------------------------------------
// THE SLOT IS A FIELD. tools/statics-audit gates a file-scope static out of
// src/sourcesharp, and this is the shape it was replaced with.
//-----------------------------------------------------------------------------
TEST( CommandClientMove, TheSlotIsAPrivateFieldOfAClass )
{
	const std::string code = Code( kHome );
	ASSERT_FALSE( code.empty() );

	const size_t cls = code.find( "classCSourceSharpCommandClient" );
	ASSERT_NE( cls, std::string::npos ) << "the slot is no longer held by a class.";

	const size_t priv = code.find( "private:", cls );
	ASSERT_NE( priv, std::string::npos ) << "the class has no private section.";

	EXPECT_NE( code.find( "intm_nSlot;", priv ), std::string::npos )
		<< "the slot is not a private field of the class.";

	EXPECT_EQ( code.find( "staticintg_nCommandClientIndex" ), std::string::npos )
		<< "the file-scope static came with the move. statics-audit gates "
		<< "that shape out of src/sourcesharp.";
}

//-----------------------------------------------------------------------------
// ONE DEFINITION EACH, and the setter has a declaration a caller can see.
//
// gameinterface.cpp leaves the build at A9. A copy left behind in it is a
// duplicate symbol today and an invisible stale body afterwards.
//-----------------------------------------------------------------------------
TEST( CommandClientMove, TheThreeReadersAreDefinedOnlyInTheirNewHome )
{
	// There were four. UTIL_GetCommandClient returned a CBasePlayer *, and
	// server.so builds none (UTIL_PlayerByIndex is NULL on every path); it had
	// no caller and left in plan_server_novalve.md P5 wave 8b -- see the next
	// fact.
	//
	// AND TWO MORE LEFT IN THE SERVER GC CULL (2026-10-01):
	// UTIL_PlayerActivatorBySlot and UTIL_GetCommandClientActivator had no
	// caller, and server.so's --print-gc-sections link dropped both; asserted
	// absent below.
	static const char *kFour[] =
	{
		"intUTIL_GetCommandClientIndex(void)",
	};
	static const char *kGoneInTheCull[] =
	{
		"UTIL_PlayerActivatorBySlot(",
		"UTIL_GetCommandClientActivator(",
	};

	const std::string home = Code( kHome );
	const std::string old  = Code( kGameIface );
	ASSERT_FALSE( home.empty() );
	ASSERT_FALSE( old.empty() );

	for ( size_t i = 0; i < sizeof( kFour ) / sizeof( kFour[ 0 ] ); i++ )
	{
		const std::string def = std::string( kFour[ i ] ) + "{";

		EXPECT_NE( home.find( def ), std::string::npos )
			<< "'" << kFour[ i ] << "' is not defined in " << kHome;

		EXPECT_EQ( old.find( def ), std::string::npos )
			<< "'" << kFour[ i ] << "' is STILL defined in " << kGameIface
			<< " -- a copy rather than a move.";
	}
	for ( size_t i = 0; i < sizeof( kGoneInTheCull ) / sizeof( kGoneInTheCull[ 0 ] ); i++ )
	{
		EXPECT_EQ( home.find( kGoneInTheCull[ i ] ), std::string::npos )
			<< "'" << kGoneInTheCull[ i ] << "' is defined in " << kHome
			<< " again, and nothing called it when the cull deleted it.";
	}
}

TEST( CommandClientMove, TheCBasePlayerReaderIsDefinedNowhere )
{
	const std::string def = "CBasePlayer*UTIL_GetCommandClient(void){";
	const std::string home = Code( kHome );
	const std::string old  = Code( kGameIface );
	ASSERT_FALSE( home.empty() );
	ASSERT_FALSE( old.empty() );

	EXPECT_EQ( home.find( def ), std::string::npos )
		<< "UTIL_GetCommandClient is defined in " << kHome << " again. It "
		   "answered a CBasePlayer *, which server.so never builds, and had no caller";
	EXPECT_EQ( old.find( def ), std::string::npos )
		<< "UTIL_GetCommandClient is defined in " << kGameIface;
}

// TheSetterIsDeclaredWhereItsCallerCanSeeIt WAS HERE and read util.h for the
// setter's declaration. game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, so the fact has lost its
// subject.
