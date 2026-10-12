//========= SourceSharp — the player's state is in the block (step 3c) =======//
//
// WHAT THIS FILE IS FOR.
//
// plan_entity.md step 3c moves CPlayerState out of a C++ member and into the
// engine block's server tail, and makes IServerGameClients::GetPlayerState
// answer out of it. The two claims that matter are both invisible to any
// behavioural test this project can run:
//
//   1. THE METHOD MAY NOT ANSWER NULL. The two engine sites that make a NULL
//      fatal are in the SourceTV and Replay servers, and step 0d states
//      that the live gate covers NEITHER HLTV NOR REPLAY. A green gate is
//      therefore no evidence at all about this, and never will be until the gate
//      grows a SourceTV leg. What CAN be checked is that the body has no NULL
//      return in it, which is a property of the source.
//
//   2. THE STORAGE IS THE BLOCK'S AND THERE IS NO SECOND COPY. A member beside
//      the block would hold a well-formed CPlayerState and so would the block;
//      the engine would read one and the shell would write the other, and the
//      symptom would be view angles the server never accepted. Nothing reports
//      that. standalone_layout_test.cpp makes exactly this argument for the
//      thirteen values step 1b moved, and this is the fourteenth.
//
// THE REFUSAL COUNTER IS RUN, NOT READ. CShellPlayerInfoRefusals is engine-free
// for that reason (shell_playerinfo.h says so): the properties worth checking
// are properties of the body -- that a call counts, that the second call to one
// method does not open a second row, that the warning is said once per method
// rather than per call -- and only running it can check those.
//
// TEXTUAL CHECKS STRIP COMMENTS FIRST. Every file this reads explains at length
// what it no longer has: "m_PlayerState IS GONE" and "the `if (
// !player->GetUnknown() )` that used to open it" are sentences whose whole job
// is to say the thing is absent, and a raw substring search reads them as the
// thing being present. A check that failed on its own explanation gets deleted,
// and the defect comes back with it.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <stdio.h>
#include <string.h>

#include <fstream>
#include <string>
#include <vector>

#include "shell_playerinfo.h"

//-----------------------------------------------------------------------------
// The recorder half of the refusal seam. shell_playerinfo.h declares this and
// shell_playerinfo.cpp defines it with tier0's Warning(); here it remembers,
// which is what lets the "said once per method" property be a fact rather than
// something a reader has to take on trust from the console.
//-----------------------------------------------------------------------------
namespace
{

struct RefusalLine_t
{
	std::string strMethod;
	int         nTotal;
};

std::vector< RefusalLine_t > g_Warnings;

}  // namespace

void ShellPlayerInfo_RefusalWarning( const char *pMethod, int nTotal )
{
	RefusalLine_t line;
	line.strMethod = ( pMethod != NULL ) ? pMethod : "(unnamed)";
	line.nTotal    = nTotal;
	g_Warnings.push_back( line );
}

namespace
{

//=============================================================================
// PART 1 -- the refusal counter, run.
//=============================================================================

class Refusals : public ::testing::Test
{
protected:
	void SetUp() override { g_Warnings.clear(); }

	CShellPlayerInfoRefusals m_Counter;
};

TEST_F( Refusals, AFreshCounterHasRefusedNothing )
{
	EXPECT_EQ( 0, m_Counter.Total() );
	EXPECT_EQ( 0, m_Counter.MethodCount() );
}

TEST_F( Refusals, OneRefusalCountsOneAndOpensOneRow )
{
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );

	EXPECT_EQ( 1, m_Counter.Total() );
	EXPECT_EQ( 1, m_Counter.MethodCount() );
	EXPECT_EQ( 1, m_Counter.CountOf( "IPlayerInfo::GetWeaponName" ) );
}

TEST_F( Refusals, TheSameMethodTwiceDoesNotOpenASecondRow )
{
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );

	EXPECT_EQ( 2, m_Counter.Total() );
	EXPECT_EQ( 1, m_Counter.MethodCount() );
	EXPECT_EQ( 2, m_Counter.CountOf( "IPlayerInfo::GetWeaponName" ) );
}

TEST_F( Refusals, TwoMethodsAreTwoRows )
{
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );
	m_Counter.Refused( "IPlayerInfo::ChangeTeam" );

	EXPECT_EQ( 2, m_Counter.Total() );
	EXPECT_EQ( 2, m_Counter.MethodCount() );
	EXPECT_EQ( 1, m_Counter.CountOf( "IPlayerInfo::GetWeaponName" ) );
	EXPECT_EQ( 1, m_Counter.CountOf( "IPlayerInfo::ChangeTeam" ) );
}

TEST_F( Refusals, AMethodThatNeverRefusedCountsZero )
{
	m_Counter.Refused( "IPlayerInfo::ChangeTeam" );

	EXPECT_EQ( 0, m_Counter.CountOf( "IPlayerInfo::GetMaxHealth" ) );
}

TEST_F( Refusals, ARowIsMatchedByTextAndNotByPointer )
{
	// The names are string literals in one translation unit today, so pointer
	// identity would pass today and stop passing silently the moment one moved
	// -- and the failure would be a duplicate row rather than an error. Two
	// distinct buffers holding the same characters is that situation, provoked.
	char first[ 32 ];
	char second[ 32 ];
	strcpy( first,  "IPlayerInfo::GetMaxHealth" );
	strcpy( second, "IPlayerInfo::GetMaxHealth" );

	ASSERT_NE( first, second );

	m_Counter.Refused( first );
	m_Counter.Refused( second );

	EXPECT_EQ( 1, m_Counter.MethodCount() );
	EXPECT_EQ( 2, m_Counter.CountOf( "IPlayerInfo::GetMaxHealth" ) );
}

TEST_F( Refusals, TheWarningIsSaidOncePerMethodAndNotOncePerCall )
{
	// A plugin walking the player list every frame would call GetWeaponName
	// every frame. The COUNT has to keep climbing -- that is the finding -- but
	// the console line must not, or it buries everything else in the log.
	for ( int i = 0; i < 50; i++ )
		m_Counter.Refused( "IPlayerInfo::GetWeaponName" );

	EXPECT_EQ( 50, m_Counter.CountOf( "IPlayerInfo::GetWeaponName" ) );
	EXPECT_EQ( size_t( 1 ), g_Warnings.size() );
	EXPECT_EQ( std::string( "IPlayerInfo::GetWeaponName" ), g_Warnings[ 0 ].strMethod );
}

TEST_F( Refusals, EachMethodGetsItsOwnFirstWarning )
{
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );
	m_Counter.Refused( "IPlayerInfo::ChangeTeam" );

	ASSERT_EQ( size_t( 2 ), g_Warnings.size() );
	EXPECT_EQ( std::string( "IPlayerInfo::GetWeaponName" ), g_Warnings[ 0 ].strMethod );
	EXPECT_EQ( std::string( "IPlayerInfo::ChangeTeam" ),    g_Warnings[ 1 ].strMethod );
}

TEST_F( Refusals, TheWarningCarriesTheRunningTotalTheReadoutWillShow )
{
	// The line a person sees first and the tally cs_sv_playerinfo prints have to
	// be the same number, or the two readings of one console disagree.
	m_Counter.Refused( "IPlayerInfo::GetWeaponName" );
	m_Counter.Refused( "IPlayerInfo::ChangeTeam" );

	ASSERT_EQ( size_t( 2 ), g_Warnings.size() );
	EXPECT_EQ( 1, g_Warnings[ 0 ].nTotal );
	EXPECT_EQ( 2, g_Warnings[ 1 ].nTotal );
}

TEST_F( Refusals, ANullMethodStillCountsAndStillWarns )
{
	// Losing the row is better than losing the number: the total is the gate.
	m_Counter.Refused( NULL );

	EXPECT_EQ( 1, m_Counter.Total() );
	EXPECT_EQ( 0, m_Counter.MethodCount() );
	EXPECT_EQ( size_t( 1 ), g_Warnings.size() );
}

TEST_F( Refusals, AnOverflowingTableKeepsCountingTheTotal )
{
	// Distinct storage per row, because the table keeps the POINTER it was
	// given -- a loop that reused one buffer would be testing something else
	// entirely, and a std::vector<std::string> would move its contents out from
	// under the pointers as it grew.
	static const int kRows = CShellPlayerInfoRefusals::kMaxMethods + 5;
	char szNames[ kRows ][ 32 ];

	for ( int i = 0; i < kRows; i++ )
	{
		snprintf( szNames[ i ], sizeof( szNames[ i ] ), "IPlayerInfo::M%d", i );
		m_Counter.Refused( szNames[ i ] );
	}

	EXPECT_EQ( kRows, m_Counter.Total() );
	EXPECT_EQ( (int)CShellPlayerInfoRefusals::kMaxMethods, m_Counter.MethodCount() );
}

TEST_F( Refusals, AnIndexOutsideTheTableAnswersNothingRatherThanReadingPastIt )
{
	m_Counter.Refused( "IPlayerInfo::ChangeTeam" );

	EXPECT_EQ( (const char *)NULL, m_Counter.MethodAt( -1 ) );
	EXPECT_EQ( (const char *)NULL, m_Counter.MethodAt( 1 ) );
	EXPECT_EQ( 0, m_Counter.CountAt( -1 ) );
	EXPECT_EQ( 0, m_Counter.CountAt( 1 ) );
}

//=============================================================================
// PART 2 -- the source facts.
//=============================================================================

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

std::string Read( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

// COMMENTS REMOVED, STRING LITERALS KEPT. See the file header for why every
// check below needs the first half: the subjects explain their own absences in
// prose, and a needle that matched a comment would stay green across the
// deletion it exists to notice. The second half is deliberate the other way --
// the interface name a plugin asks for IS a string literal
// ("PlayerInfoManager002"), and a pass that dropped it would leave that check
// unable to fail.
std::string CodeOnly( const std::string &strText )
{
	std::string out;
	out.reserve( strText.size() );

	bool bBlock = false, bLine = false, bStr = false, bChr = false;
	for ( size_t i = 0; i < strText.size(); i++ )
	{
		const char c = strText[ i ];
		const char n = ( i + 1 < strText.size() ) ? strText[ i + 1 ] : '\0';

		if ( bBlock ) { if ( c == '*' && n == '/' ) { bBlock = false; i++; } continue; }
		if ( bLine )  { if ( c == '\n' ) { bLine = false; out += c; } continue; }

		if ( !bStr && !bChr )
		{
			if ( c == '/' && n == '*' ) { bBlock = true; i++; continue; }
			if ( c == '/' && n == '/' ) { bLine = true; i++; continue; }
		}

		if ( bStr )      { if ( c == '\\' ) { out += c; i++; if ( i < strText.size() ) out += strText[i]; continue; } if ( c == '"' ) bStr = false; }
		else if ( bChr ) { if ( c == '\\' ) { out += c; i++; if ( i < strText.size() ) out += strText[i]; continue; } if ( c == '\'' ) bChr = false; }
		else if ( c == '"' )  bStr = true;
		else if ( c == '\'' ) bChr = true;

		out += c;
	}

	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

// A WHOLE-WORD find. m_PlayerState is a substring of nothing here today and
// would be a substring of m_PlayerStateOld the day somebody writes one.
bool ContainsWord( const std::string &strHay, const std::string &strWord )
{
	size_t at = strHay.find( strWord );
	while ( at != std::string::npos )
	{
		const bool bLeft  = ( at == 0 ) || !IsIdentChar( strHay[ at - 1 ] );
		const size_t end  = at + strWord.size();
		const bool bRight = ( end >= strHay.size() ) || !IsIdentChar( strHay[ end ] );

		if ( bLeft && bRight )
			return true;

		at = strHay.find( strWord, at + 1 );
	}

	return false;
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	int nFound = 0;
	size_t at = strHay.find( strNeedle );
	while ( at != std::string::npos )
	{
		nFound++;
		at = strHay.find( strNeedle, at + strNeedle.size() );
	}
	return nFound;
}

// The function body between `strSignature` and the brace that closes it.
std::string FunctionBody( const std::string &strFile, const std::string &strSignature )
{
	const size_t at = strFile.find( strSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = strFile.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strFile.size(); i++ )
	{
		if ( strFile[ i ] == '{' )
			nDepth++;
		else if ( strFile[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strFile.substr( open, i - open + 1 );
		}
	}

	return std::string();
}

const char *const kPlayerHeader   = "sourcesharp/game/server/shell_standalone_player.h";
const char *const kGameClients    = "sourcesharp/game/server/shell_gameclients.cpp";
const char *const kPlayerInfoCpp  = "sourcesharp/game/server/shell_playerinfo.cpp";
// cs_sv_playerinfo is a diagnostic and is compiled only into a debug build.
const char *const kPlayerInfoDebugCpp = "sourcesharp/game/server/shell_playerinfo_debug.cpp";
const char *const kBlockGenerated = "sourcesharp/game/shared/engine_block_generated.h";

//-----------------------------------------------------------------------------
// The player state has ONE home, and it is the block.
//-----------------------------------------------------------------------------
TEST( PlayerStateInTheBlock, TheBlockDeclaresTheRoomForIt )
{
	const std::string block = CodeOnly( Read( kBlockGenerated ) );
	ASSERT_FALSE( block.empty() ) << kBlockGenerated << " could not be read.";

	// NON-VACUITY FIRST. If this row named a field the block does not have, the
	// absence checked below would be "the player header does not have a member
	// nobody declares anywhere", which is true of any header at all.
	EXPECT_TRUE( ContainsWord( block, "PlayerState" ) )
		<< "EngineBlockServerTail_t no longer declares PlayerState, so the "
		   "check below is finding nothing rather than finding an absence.";
}

TEST( PlayerStateInTheBlock, TheShellDeclaresNoSecondCopyOfIt )
{
	const std::string header = CodeOnly( Read( kPlayerHeader ) );
	ASSERT_FALSE( header.empty() ) << kPlayerHeader << " could not be read.";

	// The anchor: a member that is still supposed to be there. Without it a
	// broken read of the file would pass this whole test by finding nothing.
	ASSERT_TRUE( ContainsWord( header, "m_Local" ) )
		<< "class CStandalonePlayer no longer declares m_Local, so this search "
		   "is finding nothing rather than finding an absence.";

	EXPECT_FALSE( ContainsWord( header, "m_PlayerState" ) )
		<< "CStandalonePlayer declares m_PlayerState again, which is a second "
		   "copy of EngineBlockServerTail_t::PlayerState.\n\n"
		   "plan_entity.md 3c: the engine reads fixangle, v_angle and the "
		   "network name through GetPlayerState. A member beside the block is a "
		   "value with two writers and no rule about which one wins, and both "
		   "hold a well-formed CPlayerState -- nothing anywhere would report "
		   "the disagreement.";
}

TEST( PlayerStateInTheBlock, TheAccessorReadsTheBlockAndNotAMember )
{
	const std::string header = CodeOnly( Read( kPlayerHeader ) );
	ASSERT_FALSE( header.empty() ) << kPlayerHeader << " could not be read.";

	EXPECT_NE( header.find( "Block().Server.PlayerState" ), std::string::npos )
		<< "PlayerStatePtr() no longer reads the block's server tail. Whatever "
		   "it reads instead is storage the engine block does not own.";
}

TEST( PlayerStateInTheBlock, ThePlayerConstructsItInPlace )
{
	// CPlayerState declares a virtual destructor, so it carries a vtable
	// pointer. PvAllocEntPrivateData hands back ZEROED memory and a zeroed vptr
	// is a null dereference through the first virtual call -- so the object has
	// to be constructed, not merely occupied.
	const std::string cpp = CodeOnly( Read( "sourcesharp/game/server/shell_standalone_player.cpp" ) );
	ASSERT_FALSE( cpp.empty() ) << "shell_standalone_player.cpp could not be read.";

	EXPECT_NE( cpp.find( "Construct( PlayerStatePtr() )" ), std::string::npos )
		<< "the player no longer constructs its CPlayerState in the block.";
	EXPECT_NE( cpp.find( "Destruct( PlayerStatePtr() )" ), std::string::npos )
		<< "the player no longer destroys the CPlayerState it constructed.";
}

//-----------------------------------------------------------------------------
// GetPlayerState may not answer NULL.
//-----------------------------------------------------------------------------
TEST( GetPlayerStateIsNeverNull, TheBodyHasNoNullReturnLeftInIt )
{
	const std::string file = CodeOnly( Read( kGameClients ) );
	ASSERT_FALSE( file.empty() ) << kGameClients << " could not be read.";

	const std::string body =
		FunctionBody( file, "CPlayerState *CServerGameClients::GetPlayerState(" );

	ASSERT_FALSE( body.empty() )
		<< "GetPlayerState's body could not be extracted from " << kGameClients
		<< ", so every check below would be about an empty string.";

	// NON-VACUITY: the body really is the body.
	ASSERT_NE( body.find( "SourceSharp_PlayerStateForEdict" ), std::string::npos )
		<< "the extracted body does not call the function that owns the "
		   "storage; the extraction found the wrong function.";

	// AND IT RESOLVES THE EDICT ONCE. The engine calls this every tick per
	// client; a second CStandalonePlayer::FromEdict here -- which is what
	// counting the arms at this end costs -- is a lookup spent on the counting.
	EXPECT_EQ( 0, CountOf( body, "CStandalonePlayer::FromEdict" ) )
		<< "GetPlayerState resolves the edict itself as well as through the "
		   "storage function.";

	EXPECT_EQ( 0, CountOf( body, "return NULL" ) )
		<< "GetPlayerState returns NULL again.\n\n"
		   "Two engine sites write through the result with NO null test -- "
		   "the SourceTV and Replay servers set the hltv and replay flags -- "
		   "and both are reached on an edict whose GetUnknown() is NULL, "
		   "because the engine makes their master client as a fake client and "
		   "ClientPutInServer has not run. "
		   "The live gate covers neither HLTV nor Replay (step 0d), so nothing "
		   "you can run will catch this.";

	EXPECT_EQ( 0, CountOf( body, "return 0" ) )
		<< "GetPlayerState returns a null pointer spelled as 0.";
}

TEST( GetPlayerStateIsNeverNull, TheStorageFunctionAlwaysHasSomethingToAnswerWith )
{
	const std::string file = CodeOnly( Read( kPlayerInfoCpp ) );
	ASSERT_FALSE( file.empty() ) << kPlayerInfoCpp << " could not be read.";

	const std::string body =
		FunctionBody( file, "CPlayerState *SourceSharp_PlayerStateForEdict(" );

	ASSERT_FALSE( body.empty() )
		<< "SourceSharp_PlayerStateForEdict's body could not be extracted.";

	EXPECT_NE( body.find( "PlayerStatePtr()" ), std::string::npos )
		<< "a player's answer no longer comes out of the player's own block.";
	EXPECT_NE( body.find( "Orphan(" ), std::string::npos )
		<< "there is no answer left for an edict with no player, which is "
		   "exactly the case the HLTV and Replay masters are in.";
	EXPECT_EQ( 0, CountOf( body, "return NULL" ) )
		<< "the storage function answers NULL, so GetPlayerState can too.";

	// 0e'S PROBE IS THE STEP'S GATE AND IT IS ARMED HERE. The arms live in this
	// function rather than at the call site because the edict is resolved once;
	// what that must not become is one arm, or none, which would leave the live
	// gate reading a counter nothing increments and calling it evidence.
	EXPECT_EQ( 1, CountOf( body, "SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Standalone )" ) )
		<< "the block answer is not counted, so the live gate's "
		   "\"GetPlayerState answered out of the block N times\" reads zero on a "
		   "build where it answered every call.";
	EXPECT_EQ( 1, CountOf( body, "SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Null )" ) )
		<< "the no-player answer is not counted, so nothing says how often the "
		   "orphan storage is what kept this from being a NULL.";
}

//-----------------------------------------------------------------------------
// The view origin: one question, one crossing, two readers.
//-----------------------------------------------------------------------------
TEST( ViewOriginIsAskedOnce, TheCacheIsStampedWithTheFrame )
{
	const std::string file = CodeOnly( Read( kPlayerInfoCpp ) );
	ASSERT_FALSE( file.empty() ) << kPlayerInfoCpp << " could not be read.";

	const std::string body =
		FunctionBody( file, "bool SourceSharp_ClientViewOrigin(" );

	ASSERT_FALSE( body.empty() )
		<< "SourceSharp_ClientViewOrigin's body could not be extracted.";

	EXPECT_NE( body.find( "ViewOriginFrame != gpGlobals->framecount" ), std::string::npos )
		<< "the frame stamp is gone, so the managed side is asked again for "
		   "every PVS-filtered message rather than once a frame.";
	EXPECT_EQ( 1, CountOf( body, "SourceSharp_EarPositionForClient(" ) )
		<< "the crossing is not made exactly once in this body.";
	EXPECT_NE( body.find( "tail.ViewOriginFrame = gpGlobals->framecount" ), std::string::npos )
		<< "the answer is never stamped, so the cache can never be a hit.";
}

TEST( ViewOriginIsAskedOnce, BothEngineReadersGoThroughIt )
{
	const std::string file = CodeOnly( Read( kGameClients ) );
	ASSERT_FALSE( file.empty() ) << kGameClients << " could not be read.";

	const std::string vis =
		FunctionBody( file, "void CServerGameClients::ClientSetupVisibility(" );
	const std::string ears =
		FunctionBody( file, "void CServerGameClients::ClientEarPosition(" );

	ASSERT_FALSE( vis.empty() )  << "ClientSetupVisibility's body could not be extracted.";
	ASSERT_FALSE( ears.empty() ) << "ClientEarPosition's body could not be extracted.";

	EXPECT_NE( vis.find( "SourceSharp_ClientViewOrigin(" ), std::string::npos )
		<< "the PVS no longer asks managed code where the eyes are; whatever it "
		   "reads instead is native state, which is what plan_entity.md 3.10 "
		   "moves.";
	EXPECT_NE( ears.find( "SourceSharp_ClientViewOrigin(" ), std::string::npos )
		<< "the sound listener no longer shares the cached answer, so it is "
		   "back to a crossing per multicast message.";

	// AND THE NATIVE EYE POSITION IS GONE FROM BOTH. This is the half a
	// search for the new call would miss: adding the new reader while leaving
	// the old one is two answers to one question, and both are well formed.
	//
	// THE COUNT WAS TWO AND IS NOW ZERO, and the fall is the whole of the
	// change. Both of the two were named here, and neither was ever compiled:
	//
	//   * `pVE->EyePosition()`, the VIEW ENTITY arm, sat inside
	//     `#ifndef SS_CBASEENTITY_ALIAS`, and pVE came from
	//     GetContainingEntity, which answered NULL for every edict in this
	//     build -- all three implementations of IServerUnknown::GetBaseEntity()
	//     in server.so are `mov $0x0,%eax; ret`.
	//   * `pPlayer->EyePosition()`, STOCK's arm, sat inside the `#else` of
	//     `#ifdef SOURCESHARP`, and SOURCESHARP is defined for both DLLs this
	//     mod builds.
	//
	// The close deleted both arms with the other 453 the compiler never saw, and
	// GetContainingEntity with them. ZERO IS THE STRONGER PIN, not a weaker one:
	// while the count was two, a third had to be told apart from the two that
	// belonged; now ANY native eye-position computation in this file is a second
	// answer to a question managed code has already answered, whatever it is
	// spelled on. Searching for "pStandalone->" instead would still have let the
	// next one in under a different name.
	EXPECT_EQ( 0, CountOf( file, "EyePosition()" ) )
		<< "a native eye-position computation is back in " << kGameClients
		<< ". The two that used to be here were both in arms this build never "
		   "compiled and both are deleted; a player's eyes come out of the "
		   "block cache, through SourceSharp_ClientViewOrigin.";

	// AND THE ANSWER THEY WERE REPLACED BY IS ASKED EXACTLY THREE TIMES -- the
	// two readers above and the block's own refresh. Without this the zero above
	// would pass over a file that had lost the managed reader too, which is the
	// same bug with nothing left in its place.
	EXPECT_EQ( 3, CountOf( file, "SourceSharp_ClientViewOrigin(" ) )
		<< "the number of asks for the cached view origin in " << kGameClients
		<< " moved. Three belong here: ClientSetupVisibility's, the sound "
		   "listener's in ClientEarPosition, and the one inside the block "
		   "refresh they share.";

	EXPECT_EQ( 0, CountOf( ears, "EyePosition()" ) )
		<< "ClientEarPosition computes an eye position natively beside the "
		   "managed answer.";
}

TEST( ViewOriginIsAskedOnce, ThePvsOriginIsNoLongerUninitialisedStack )
{
	// A REAL DEFECT THIS STEP FIXED, pinned so it cannot come back. `Vector
	// org;` is stack garbage until an arm writes it, and NO arm fires for a
	// client with no player entity -- which is what the HLTV and Replay master
	// clients are, and SetupPackInfo calls ClientSetupVisibility for them like
	// any other client. The garbage went to CFuncAreaPortalBase::UpdateVisibility
	// for every areaportal on the map.
	const std::string file = CodeOnly( Read( kGameClients ) );
	const std::string vis =
		FunctionBody( file, "void CServerGameClients::ClientSetupVisibility(" );

	ASSERT_FALSE( vis.empty() ) << "ClientSetupVisibility's body could not be extracted.";

	EXPECT_EQ( 0, CountOf( vis, "Vector org;" ) )
		<< "the PVS origin is an uninitialised local again.";
	EXPECT_NE( vis.find( "Vector org = vec3_origin;" ), std::string::npos )
		<< "the PVS origin is no longer initialised where it is declared.";
}

//-----------------------------------------------------------------------------
// IPlayerInfo: what it refuses is declared, and it is reached the way a plugin
// reaches it.
//-----------------------------------------------------------------------------
TEST( PluginFacingPlayerInfo, EveryRefusalIsDeclaredAndPrinted )
{
	const std::string file = CodeOnly( Read( kPlayerInfoCpp ) );
	ASSERT_FALSE( file.empty() ) << kPlayerInfoCpp << " could not be read.";

	// Four methods, four reasons, each named at its site. The count is the
	// check: a fifth refusal added without a line here is a method quietly
	// answering nothing.
	EXPECT_EQ( 4, CountOf( file, "Refuse( SS_PLAYERINFO_REFUSAL_" ) )
		<< "the number of refusing IPlayerInfo methods moved. Add the reason "
		   "here as well as at the site, or a method has started refusing with "
		   "nobody having argued that it should.";

	EXPECT_NE( file.find( "SS_PLAYERINFO_REFUSAL_CHANGE_TEAM" ),  std::string::npos );
	EXPECT_NE( file.find( "SS_PLAYERINFO_REFUSAL_WEAPON_NAME" ),  std::string::npos );
	EXPECT_NE( file.find( "SS_PLAYERINFO_REFUSAL_MAX_HEALTH" ),   std::string::npos );
	EXPECT_NE( file.find( "SS_PLAYERINFO_REFUSAL_LAST_COMMAND" ), std::string::npos );
}

TEST( PluginFacingPlayerInfo, TheManagerIsExposedUnderTheInterfaceNameAPluginAsksFor )
{
	const std::string file = CodeOnly( Read( kPlayerInfoCpp ) );
	ASSERT_FALSE( file.empty() ) << kPlayerInfoCpp << " could not be read.";

	// Exposed = its row in server.so's factory table (shell_factory.h), and
	// the row answers this object as IPlayerInfoManager.
	EXPECT_NE( file.find( "return (IPlayerInfoManager *)&s_ShellPlayerInfoManager;" ),
	           std::string::npos );
	EXPECT_NE( file.find( "const SourceSharpExposed kExposePlayerInfoManager = { INTERFACEVERSION_PLAYERINFOMANAGER," ),
	           std::string::npos )
		<< "the manager is not exposed through the DLL's factory, so a server "
		   "plugin asking gameServerFactory for PlayerInfoManager002 gets "
		   "nothing -- which is what this build did before step 3c.";
	EXPECT_NE( file.find( "INTERFACEVERSION_PLAYERINFOMANAGER" ), std::string::npos )
		<< "the interface name is spelled by hand rather than taken from "
		   "iplayerinfo.h, so the two can drift.";
}

TEST( PluginFacingPlayerInfo, TheConsoleQueryGoesThroughTheFactoryAndNotTheObject )
{
	// THE POINT OF THE COMMAND. Calling s_ShellPlayerInfoManager directly would
	// prove the object works and say nothing about whether the factory can find
	// it -- and the factory is the ONLY route anything outside this DLL has.
	const std::string file = CodeOnly( Read( kPlayerInfoDebugCpp ) );
	ASSERT_FALSE( file.empty() ) << kPlayerInfoDebugCpp << " could not be read.";
	const std::string body = FunctionBody( file, "SS_NATIVE_COMMAND( cs_sv_playerinfo )" );

	ASSERT_FALSE( body.empty() ) << "cs_sv_playerinfo's body could not be extracted.";

	EXPECT_NE( body.find( "Sys_GetFactoryThis()( INTERFACEVERSION_PLAYERINFOMANAGER" ),
	           std::string::npos )
		<< "the query no longer goes through CreateInterface, so it is not a "
		   "plugin-style query at all.";
	EXPECT_EQ( 0, CountOf( body, "s_ShellPlayerInfoManager.GetPlayerInfo" ) )
		<< "the query reaches around the factory to the object.";
}

TEST( PluginFacingPlayerInfo, TheViewsReadTheBlockRatherThanAskingManagedCode )
{
	// plan_entity.md 3.10: "IPlayerInfo is a shim reading the block." A getter
	// that crossed would put a managed call on a path a plugin can walk once
	// per player per frame.
	const std::string file = CodeOnly( Read( kPlayerInfoCpp ) );
	const std::string body = FunctionBody( file, "class CShellPlayerInfo : public IPlayerInfo" );

	ASSERT_FALSE( body.empty() ) << "class CShellPlayerInfo's body could not be extracted.";

	EXPECT_LT( 4, CountOf( body, "Block().Head" ) )
		<< "the placement getters no longer read the block.";
	EXPECT_EQ( 0, CountOf( body, "SourceSharp_GetManagedApi" ) )
		<< "an IPlayerInfo getter crosses into managed code.";
}

}  // namespace
