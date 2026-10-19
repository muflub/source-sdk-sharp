//=============================================================================//
// THE REFUSAL SITES -- plan_gamedll.md Part 7 and Phase 8.
//
// THE COUNTER IS GONE AND THIS FILE IS THE ARRANGEMENT HALF ONLY. There used
// to be a second half over CShellGameDllRefusals' bodies -- a per-method table
// with a total, read by `cs_sv_refusals` and by a second readout in
// DLLShutdown. It had no reader: nothing in tools/, docs/, the managed tree or
// game/mod_sharp/cfg/ ever ran that command, and the table's own two readouts
// were the only things that read the table. shell_gamedll.cpp's own DLLShutdown
// comment already said which channel the live gate was actually answered with:
// the WARNING, counted with `grep -c REFUSED console.log`, which needs no
// frame, no level and no clean exit -- and which the DLLShutdown readout did.
//
// SO WHAT IS LEFT IS THE HALF THAT DECAYS. The ARRANGEMENT in
// shell_gamedll.cpp -- which methods refuse, and above all that IsRestoring
// does NOT. That file includes gameinterface.h and cbase.h's world and can
// never be linked here, so it is source-read, the way
// shell_gamedll_registration_test.cpp reads the same file for the same reason.
// Comment-stripped, because every comment in that file NAMES the symbols it
// explains and a raw search would pass on prose.
//
// IT HAS TWO LISTS RATHER THAN ONE, and the reason is
// that eleven of Part 7's sixteen stopped refusing. Stage 1 of the save shim
// (docs/managed-saverestore.md) implements the plain save/load round trip, so
// SaveInit, the two field codecs, PreSave, Save, the two header methods,
// Restore, PreSaveGameLoaded and the global-state pair ANSWER -- and each of
// them is checked for the opposite property to the four that remain: no
// refusal, and a trace line, because a method that stopped refusing and also
// stopped doing anything is precisely the invisible stub the counter exists
// for. What still refuses is the level-TRANSITION half, which needs the entity
// table stage 1 leaves empty on purpose.
//
// IsRestoring IS THE ONE THAT MATTERS. It returns false, which is correct, but
// GameFrame reads it as its first gate (gameinterface.cpp:1300) so it is called
// EVERY FRAME. A warning there is not a wrong answer, it is a console nobody
// can read and a game nobody can play -- a failure mode that no assertion about
// return values would ever catch. So it is pinned from three directions: it is
// not in the file's refusal list, its body holds no ShellGameDll_Unreachable,
// and the count of call sites equals the count of methods that are supposed to
// have one.
//=============================================================================//

#include <fstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"


namespace
{

//-----------------------------------------------------------------------------
// THE ARRANGEMENT, source-read.
//-----------------------------------------------------------------------------

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

std::string ReadSrc( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( pRoot == NULL )
		return std::string();

	std::ifstream f( ( std::string( pRoot ) + "/" + pRelative ).c_str(), std::ios::binary );
	if ( !f.is_open() )
		return std::string();

	return StripComments( std::string( ( std::istreambuf_iterator<char>( f ) ),
	                                   std::istreambuf_iterator<char>() ) );
}

std::string Shell()
{
	return ReadSrc( "sourcesharp/game/server/shell_gamedll.cpp" );
}

// Where a method is DEFINED, as opposed to called.
//
// Two things separate the two, and both are needed. `Save(` matches inside
// `PreSave(` and inside `m_pDelegate->Save(`, so a plain find lands on the
// wrong one of three: the character before the name must not be part of an
// identifier, and the text between the name and its body's `{` must contain
// `override`, which every definition on this class carries and no call does.
size_t FindDefinition( const std::string &strFile, const std::string &strName )
{
	const std::string strNeedle = strName + "(";

	for ( size_t at = strFile.find( strNeedle ); at != std::string::npos;
	      at = strFile.find( strNeedle, at + 1 ) )
	{
		if ( at > 0 )
		{
			const char c = strFile[ at - 1 ];
			if ( ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) ||
			     ( c >= '0' && c <= '9' ) || c == '_' )
				continue;
		}

		const size_t open = strFile.find( '{', at );
		if ( open == std::string::npos )
			continue;

		if ( strFile.substr( at, open - at ).find( "override" ) != std::string::npos )
			return at;
	}

	return std::string::npos;
}

// The text of one method's body, from its signature to the matching close
// brace. Brace-counted rather than line-counted, because a body that grows or
// gains a nested block would silently move out of a fixed window and the check
// would pass on a method it was no longer reading.
std::string BodyOf( const std::string &strFile, const std::string &strName )
{
	const size_t at = FindDefinition( strFile, strName );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = strFile.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strFile.size(); i++ )
	{
		if ( strFile[ i ] == '{' ) nDepth++;
		else if ( strFile[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strFile.substr( open, i - open + 1 );
		}
	}

	return std::string();
}

int CountOccurrences( const std::string &strIn, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strIn.find( strNeedle ); at != std::string::npos;
	      at = strIn.find( strNeedle, at + strNeedle.size() ) )
		n++;
	return n;
}

// WHAT STILL REFUSES, after stage 1 of the save shim
// (docs/managed-saverestore.md) made eleven of the cluster answer.
//
// THE LIST SHRANK AND THAT IS THE POINT OF KEEPING IT. Part 7 refused all
// fifteen on the argument that no command in this build could reach a save;
// D16 measured that false at `-maxplayers 1`, and stage 1 implements the plain
// save/load round trip. What is left here is not what nobody got round to: it
// is the LEVEL-TRANSITION half, which needs the entity table stage 1
// deliberately leaves empty (docs/managed-saverestore.md 2d), plus
// GetSaveComment, which refuses the WORK and fills its out-parameter anyway.
//
// An answer from one of these would be a WRONG answer rather than a partial
// one -- a transition that half-loads a world -- which is why they keep the
// instrument and the eleven that answer gave theirs up.
const char *kSaveClusterRefusals[] = {
	"GetSaveComment",
	"CreateEntityTransitionList",
	"BuildAdjacentMapList",
};

// The eleven that now ANSWER. Listed by name rather than counted, because the
// failure this catches is the reverse of the one above: a method that stopped
// refusing and also stopped doing anything, which from outside is exactly the
// stub Part 7 built the counter to expose.
const char *kSaveClusterAnswers[] = {
	"SaveInit",
	"SaveWriteFields",
	"SaveReadFields",
	"SaveGlobalState",
	"RestoreGlobalState",
	"PreSave",
	"Save",
	"WriteSaveHeaders",
	"ReadRestoreHeaders",
	"Restore",
	"PreSaveGameLoaded",
};

TEST( RefusalSites, EveryStillRefusedSaveClusterMethodSaysWhichItIs )
{
	const std::string strShell = Shell();
	ASSERT_FALSE( strShell.empty() ) << "shell_gamedll.cpp not found";

	for ( size_t i = 0; i < sizeof( kSaveClusterRefusals ) / sizeof( kSaveClusterRefusals[ 0 ] ); i++ )
	{
		const std::string strName = kSaveClusterRefusals[ i ];
		const std::string strBody = BodyOf( strShell, strName );

		ASSERT_FALSE( strBody.empty() ) << strName << " has no body in shell_gamedll.cpp";
		EXPECT_NE( std::string::npos,
		           strBody.find( "ShellGameDll_Unreachable( \"IServerGameDLL::" + strName + "\" )" ) )
			<< strName << " is one of Part 7's refusals and must warn and count "
			<< "under its own name -- an empty body and a body that is called "
			<< "every frame look identical from outside";
	}
}

TEST( RefusalSites, EverySaveClusterMethodThatAnswersStoppedRefusingAndSaysWhatItDid )
{
	const std::string strShell = Shell();
	ASSERT_FALSE( strShell.empty() ) << "shell_gamedll.cpp not found";

	for ( size_t i = 0; i < sizeof( kSaveClusterAnswers ) / sizeof( kSaveClusterAnswers[ 0 ] ); i++ )
	{
		const std::string strName = kSaveClusterAnswers[ i ];
		const std::string strBody = BodyOf( strShell, strName );

		ASSERT_FALSE( strBody.empty() ) << strName << " has no body in shell_gamedll.cpp";

		EXPECT_EQ( std::string::npos, strBody.find( "ShellGameDll_Unreachable" ) )
			<< strName << " both answers and refuses, which cannot both be true";

		// AND IT NARRATES. Six of the eleven answer by moving no bytes -- an
		// argued decision, written at each method -- and a body that does
		// nothing and says nothing is the exact thing Part 7's counter exists
		// to make impossible. The trace is what replaced the counter here, so
		// it is what gets asserted: a "[SourceSharp][save]" line of its own,
		// or the forward into the managed SaveGameSession, which narrates
		// each of its five (plan_server_native_v2 T4c).
		const bool bNarrates = strBody.find( "Msg( \"[SourceSharp][save] " + strName ) != std::string::npos;
		const bool bForwards = strBody.find( "ManagedSaveRestore()" ) != std::string::npos
		                    && strBody.find( "pArea->Save" ) != std::string::npos;
		EXPECT_TRUE( bNarrates || bForwards )
			<< strName << " answers silently: a save that quietly did nothing "
			<< "and a save that worked look identical from outside, which is "
			<< "why the engine's two silent SaveInit aborts cost a whole "
			<< "measurement run to find";
	}
}

TEST( RefusalSites, IsRestoringDoesNotWarnAndDoesNotCount )
{
	const std::string strBody = BodyOf( Shell(), "IsRestoring" );

	ASSERT_FALSE( strBody.empty() ) << "IsRestoring has no body in shell_gamedll.cpp";

	EXPECT_EQ( std::string::npos, strBody.find( "ShellGameDll_Unreachable" ) )
		<< "IsRestoring returns false, which is CORRECT -- but GameFrame reads "
		   "it as its first gate (gameinterface.cpp:1300), so it is called every "
		   "frame. A warning there floods the console and makes the game "
		   "unusable, which is worse than a wrong answer and is not something "
		   "any assertion about return values would catch";
	EXPECT_NE( std::string::npos, strBody.find( "return false" ) )
		<< "and it still has to answer false";
}

TEST( RefusalSites, TheTransitionMapLoadArmStillRefusesAndTheSavegameArmDoesNot )
{
	const std::string strShell = Shell();

	// OnMapLoad_Transition is still the save cluster wearing a different name.
	// A transition needs the entity table docs/managed-saverestore.md section
	// 2d leaves empty, and an answer there would be a wrong answer rather than
	// a partial one.
	EXPECT_NE( std::string::npos,
	           strShell.find( "ShellGameDll_Unreachable( \"IServerGameDLL::LevelInit (transition)\" )" ) )
		<< "LevelInit's transition arm is Part 7's OnMapLoad_Transition";

	// THE SAVEGAME ARM IS IMPLEMENTED, and its refusal is gone with it. This
	// is asserted rather than merely deleted because a refusal is the cheapest
	// thing in this file to put back by reflex, and putting it back would
	// count a method that answers.
	EXPECT_EQ( std::string::npos,
	           strShell.find( "ShellGameDll_Unreachable( \"IServerGameDLL::LevelInit (savegame)\" )" ) )
		<< "LevelInit's loadGame arm respawns the map and asks the engine for "
		   "the game state; a method cannot both answer and refuse";
}

// THE FILE SAID SIXTEEN AND THE BUILD SAYS SIX. shell_gamedll_refusals.h's
// prose opened "Sixteen methods on CShellGameDll do not get a managed side" and
// repeated the number twice more. Measured at 216533fa8, shell_gamedll.cpp held
// SIX ShellGameDll_Unreachable call sites, which is what this fact has always
// counted and what the header's own arithmetic below adds up to. Eleven of the
// sixteen stopped refusing when the save shim's stage 1 landed and the prose was
// never updated; the header is gone now, so the number goes with it.
TEST( RefusalSites, TheCountOfCallSitesIsTheCountOfRefusalsAndNoMore )
{
	const std::string strShell = Shell();

	// A COUNT rather than a per-method sweep, because the failure this catches
	// is an EXTRA site: a refusal added to a method Part 7 never argued about,
	// or -- the one that matters -- to IsRestoring by someone tidying up the
	// odd one out. The per-method checks above cannot see a site they do not
	// name.
	//
	// STATUS IS THE ONE PART 10 FORWARD THAT DOES NOT COUNT, and that is the
	// same judgement IsRestoring gets rather than a different one: `status` is
	// a console command an operator types on a live server, so it is genuinely
	// reachable and a warning would be noise on a working feature. The other
	// two ARE claimed unreachable in this build -- nothing issues a
	// StartQueryCvarValue, and a listen server does not hibernate -- so they
	// carry Part 7's instrument for Part 7's reason.
	const int nSites = CountOccurrences( strShell, "ShellGameDll_Unreachable( \"" );
	const int nExpected = 3  /* what is left of the save cluster: the two
	                            transition methods and GetSaveComment */
	                    + 1  /* LevelInit's TRANSITION arm. Stage 2 implemented
	                            the savegame arm and took its refusal with it,
	                            which is why this is 1 and not 2 */
	                    + 2; /* Part 10's two unreachable forwards; not Status */

	EXPECT_EQ( nExpected, nSites )
		<< "every ShellGameDll_Unreachable site is a method Part 7 or Part 10 "
		   "argued about by name; an unaccounted one is either a refusal nobody "
		   "wrote down or IsRestoring having been made to match its neighbours";
}

TEST( RefusalSites, TheTwoUnreachablePartTenForwardsCountAndStatusDoesNot )
{
	const std::string strShell = Shell();

	// Part 10's argument for these two is Part 7's argument: they are
	// unreachable in THIS build -- nothing calls StartQueryCvarValue, and a
	// listen server does not hibernate -- so the claim gets an instrument
	// rather than a comment.
	EXPECT_NE( std::string::npos,
	           BodyOf( strShell, "OnQueryCvarValueFinished" )
	               .find( "ShellGameDll_Unreachable" ) )
		<< "a forward with a counter, not a feature";
	EXPECT_NE( std::string::npos,
	           BodyOf( strShell, "SetServerHibernation" )
	               .find( "ShellGameDll_Unreachable" ) )
		<< "stock's flag is read by a DEDICATED server idling with nobody on "
		   "it; this build is a listen server, so a call here is a finding";

	// Status is not one of them, for IsRestoring's reason rather than a
	// different one: an operator types `status` on a live server.
	EXPECT_EQ( std::string::npos,
	           BodyOf( strShell, "Status" ).find( "ShellGameDll_Unreachable" ) )
		<< "`status` is reachable by design -- a warning there is noise on a "
		   "working feature";
}

TEST( RefusalSites, StatusReturnsLinesFromManagedAndThePrinterNeverCrosses )
{
	const std::string strBody = BodyOf( Shell(), "Status" );

	ASSERT_FALSE( strBody.empty() ) << "Status has no body in shell_gamedll.cpp";

	// Part 4's rule INVERTED, and forced rather than chosen: `print` is a C
	// variadic function pointer, which no managed delegate can be. So the shell
	// must never hand it across, and must do the printing itself.
	EXPECT_NE( std::string::npos, strBody.find( "OnStatusLines( m_szStatusLines" ) )
		<< "managed returns LINES into the shell's own buffer";
	EXPECT_NE( std::string::npos, strBody.find( "print(" ) )
		<< "and the SHELL prints them";
	EXPECT_EQ( std::string::npos, strBody.find( "OnStatusLines( print" ) )
		<< "the variadic printer must not cross the boundary in any form";
}



} // namespace
