//========= SourceSharp — UpdateAllClientData walked a list with nobody on it ==//
//
// THE BUG, found by the Phase 4 exit lane while moving
// game/server/playerlocaldata.cpp into the shim.
//
// UpdateAllClientData's loop was:
//
//     for ( i = 1; i <= gpGlobals->maxClients; i++ )
//     {
//         pl = ( CBasePlayer * )UTIL_PlayerByIndex( i );
//         if ( !pl ) continue;
//         ClientData_Update( pl );
//     }
//
// and in this build UTIL_PlayerByIndex CANNOT ANSWER. Its helper
// UTIL_SDKPlayerFromEdict returns NULL unconditionally under SOURCESHARP and
// says so in its own comment: "Output: NULL, always, in this build -- nothing
// here constructs a CBasePlayer". So the loop ran maxClients times and its BODY
// NEVER RAN ONCE. Two things went with it, neither visible to a compiler, to
// `make check`, or to any live gate this project has:
//
//   1. THE 3D SKYBOX NEVER REACHED A CLIENT. sky_camera is a C# class; the
//      managed side pushes its parameters through SourceSharp_SetSkyboxParams,
//      and ClientData_Update is the ONLY caller of SourceSharp_FillSkyboxParams
//      in the whole tree. The arm had no reader.
//
//   2. m_skybox3d.area STAYED AT ITS CONSTRUCTED 0 INSTEAD OF STOCK'S 255.
//      shell_gameents.cpp's CheckTransmit reads it every frame per client, and
//      shell_transmitsteps.cpp's own comment says why the difference matters:
//      "area 0 is a real visleaf area ... CheckTransmit RETURNS EARLY for one
//      of them and walks 473 edicts for the other."
//
// It is the recorded shape a sixth time -- something a base class did FOR the
// entity, lost because the managed entity is not on that hierarchy. Spawn()
// (5e), ent_create's drop-to-floor (gateseq), shadow re-projection (7d),
// GENDER_NONE (7e) and SetVisible(false) (7a) are the others. Here what the
// base did was BE FOUND BY THE WALK.
//
// PROVED LIVE, A/B, with an instrument that already existed --
// `cs_sv_transmitset` prints skyarea=%d from the value CheckTransmit actually
// used, so tools/p4-exit-probe-skybox adds no convar, counter or debt:
//
//     fix reverted, rebuilt   recipient=1 count=124 skyarea=0
//     with the fix            recipient=1 count=157 skyarea=255
//
// WHAT THIS FILE PINS, AND WHAT IT CANNOT. It cannot construct a
// CStandalonePlayer, so it does not test the walk by running it -- the probe
// does that. What it holds is the four things a later edit could quietly undo
// while the probe is not being run, plus the PREMISE the whole fix rests on:
// that UTIL_SDKPlayerFromEdict still cannot answer. If that ever changes, this
// fix stops being the only live arm and its ordering starts to matter for real,
// and a fact that went on passing would be decoration.
//
//=============================================================================//

#include <fstream>
#include <sstream>
#include <string>

#include "gtest/gtest.h"

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

std::string Code( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); )
	{
		if ( in.compare( i, 2, "//" ) == 0 )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
		}
		else if ( in.compare( i, 2, "/*" ) == 0 )
		{
			i += 2;
			while ( i + 1 < in.size() && in.compare( i, 2, "*/" ) != 0 ) i++;
			i = ( i + 1 < in.size() ) ? i + 2 : in.size();
		}
		else
		{
			out += in[ i++ ];
		}
	}
	return out;
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

const char *const kLocalData = "sourcesharp/game/server/shell_standalone_player.cpp";
// The armed link moved UTIL_SDKPlayerFromEdict and UTIL_PlayerByIndex out of
// game/server/util.cpp, which the armed server does not compile at all, and
// into the shim beside the slot-walk family. The body this file reads is
// unchanged; only its address is.
// cs_sv_transmitset is a diagnostic: debug builds only, in shell_gamedll_debug.cpp.
const char *const kGameDllDebug = "sourcesharp/game/server/shell_gamedll_debug.cpp";
const char *const kProbe     = "../tools/p4-exit-probe-skybox";

} // anonymous namespace

//=============================================================================//
// 1. THE WALK ASKS THE SLOT TABLE
//=============================================================================//

TEST( ClientDataWalk, TheWalkAsksTheSlotTableForAStandalonePlayer )
{
	const std::string cpp = Code( ReadFile( kLocalData ) );
	ASSERT_FALSE( cpp.empty() ) << kLocalData << " did not load";
	// With the ARGUMENT: a bare `FromIndex` would also match a declaration.
	EXPECT_EQ( 1u, CountOf( cpp, "CStandalonePlayer::FromIndex( i )" ) )
		<< "UpdateAllClientData no longer asks the slot table, so its body runs "
		   "for nobody again -- which is the bug this file exists for";
	EXPECT_EQ( 1u, CountOf( cpp, "StandaloneClientData_Update( pStandalone )" ) )
		<< "the standalone arm is not called";
}

//-----------------------------------------------------------------------------
// TheStockArmIsStillTriedFirst IS RETIRED, AND ITS OWN NOTE ASKED FOR THIS
// READING BEFORE THE DELETION RATHER THAN INSTEAD OF IT.
//
// The fact required four things of UpdateAllClientData: a `pl = NULL;`
// assignment, a `ClientData_Update( pl );` call after it, that call before the
// standalone one, and no `UTIL_PlayerByIndex( i )` anywhere in the loop. Its
// note read "If UTIL_PlayerByIndex can answer again, the two arms stop being
// mutually exclusive and the ordering above starts to matter for real -- read
// it again rather than deleting this."
//
// READ AGAIN, and the answer is that it still cannot answer, which is why the
// arm is gone rather than why it stays. UTIL_PlayerByIndex resolves through
// UTIL_SDKPlayerFromEdict, which has exactly ONE definition on the server link
// line (shell_playerslots.o, confirmed with nm --defined-only and a symbol-type
// filter) and whose every one of three return statements is `return NULL`.
// That is an exhaustive proof over all paths, not a sample of the paths some
// run took. plan_shim.md 5 3a, lane 3b-enginebind, deleted the arm and with it
// ClientData_Update, whose only caller it was.
//
// WHAT THE ORDERING FACT WAS PROTECTING is not lost, it has moved into the
// thing that can still go wrong. The ordering mattered only while there were
// two arms; with one, the question is whether the walk reaches ANY player at
// all -- and that is TheWalkAsksTheSlotTableForAStandalonePlayer above, which
// is this file's original bug and is untouched.
//
// THE GUARD AGAINST THE ARM COMING BACK is kept, because a resurrected
// UTIL_PlayerByIndex call is exactly the mistake the retired fact's note was
// written about. It is one assertion now instead of five, and it is about the
// whole file rather than about one loop.
//-----------------------------------------------------------------------------
TEST( ClientDataWalk, TheSdkArmDoesNotComeBack )
{
	const std::string cpp = Code( ReadFile( kLocalData ) );
	ASSERT_FALSE( cpp.empty() );

	const size_t loop = cpp.find( "void UpdateAllClientData( void )" );
	ASSERT_NE( loop, std::string::npos ) << "UpdateAllClientData is gone";

	EXPECT_EQ( cpp.find( "UTIL_PlayerByIndex", loop ), std::string::npos )
		<< "UpdateAllClientData asks UTIL_PlayerByIndex again. It answers NULL "
		   "on every path -- server_csharp.vpc evicts player.cpp and its whole "
		   "hierarchy, so server.so links no CBasePlayer vtable -- so the arm "
		   "under it would be dead on arrival a second time.";

	EXPECT_EQ( cpp.find( "ClientData_Update( pl )", loop ), std::string::npos )
		<< "the SDK arm is back. If a native CBasePlayer is ever linked again "
		   "the two arms stop being mutually exclusive and their ORDER starts "
		   "to matter -- stock tried the SDK one first, and that is the thing "
		   "to restore deliberately rather than to let reappear.";

	// VACUITY GUARD. Three absences over a loop that had been renamed or
	// emptied would all pass, and this file exists because the walk once ran
	// for nobody.
	EXPECT_NE( cpp.find( "CStandalonePlayer::FromIndex( i )", loop ), std::string::npos )
		<< "the walk no longer reaches the standalone slot table either, so the "
		   "absences above are about an empty function";
}

//=============================================================================//
// 2. WHAT THE STANDALONE ARM DOES
//=============================================================================//

TEST( ClientDataWalk, TheStandaloneArmReadsTheManagedSkybox )
{
	const std::string cpp = Code( ReadFile( kLocalData ) );
	ASSERT_FALSE( cpp.empty() );
	// ONE ASKER, AND IT IS THE REACHABLE ONE. This read TWO -- stock's
	// ClientData_Update carried a guarded arm nothing could reach, because
	// nothing could hand it a CBasePlayer, and StandaloneClientData_Update
	// carries the one that runs. The count was two rather than a total
	// precisely because a total of 1 would pass with EITHER deleted, and
	// deleting the reachable one is this file's whole bug coming back.
	//
	// plan_shim.md 5 3a (lane 3b-enginebind) deleted the unreachable one, so
	// the ambiguity the pair guarded against no longer exists: there is one
	// asker and the assertion below names the arm it must be in. Deleting the
	// reachable one now fails on the very next line, which is what the count of
	// two was buying.
	EXPECT_EQ( 1u, CountOf( cpp, "SourceSharp_FillSkyboxParams( managed )" ) )
		<< "there must be exactly one asker -- the standalone arm. Stock's "
		   "unreachable twin was deleted, so a second one is a new caller "
		   "rather than the old record.";
	EXPECT_EQ( 0u, CountOf( cpp, "pl->m_Local.m_skybox3d.CopyFrom( managed )" ) )
		<< "stock's arm is back. It takes a CBasePlayer *, which nothing in "
		   "this build can produce.";
	EXPECT_EQ( 1u, CountOf( cpp, "local.m_skybox3d.CopyFrom( managed )" ) )
		<< "the standalone arm -- the one that actually runs -- no longer "
		   "copies it, so SourceSharp_SetSkyboxParams has no reachable reader "
		   "in the tree again";
}

TEST( ClientDataWalk, TheNoSkyboxAnswerIsStocksTwoFiftyFiveAndNotZero )
{
	const std::string cpp = Code( ReadFile( kLocalData ) );
	ASSERT_FALSE( cpp.empty() );
	EXPECT_EQ( 1u, CountOf( cpp, "local.m_skybox3d.area = 255;" ) )
		<< "0 is a REAL visleaf area and it is what the field holds when "
		   "nothing writes it; 255 is stock's 'there is no 3d skybox' and it is "
		   "what CheckTransmit needs to see";

	// THE CROSS-CHECK AGAINST STOCK'S OWN ARM IS SPENT. It read
	//
	//     EXPECT_EQ( 1u, CountOf( cpp, "pl->m_Local.m_skybox3d.area = 255;" ) )
	//
	// and its reason was that the standalone arm COPIES stock's constant, so a
	// change to stock's line would leave this file's 255 stale. That check had
	// a live subject only while stock's arm was in this file; plan_shim.md
	// 5 3a (lane 3b-enginebind) deleted ClientData_Update, whose only caller
	// was an arm guarded by a hardcoded NULL.
	//
	// SO THE CONSTANT NOW NEEDS ITS OWN AUTHORITY, and it has one that does not
	// depend on a copy of stock sitting in the shim: 255 is what
	// sky3dparams_t::area means by "there is no 3d skybox", which is read on
	// the wire side by CheckTransmit. The assertion above is the whole claim,
	// and 0 -- a real visleaf area, and what the field holds when nothing
	// writes it -- is the wrong answer it exists to exclude.
	EXPECT_EQ( 0u, CountOf( cpp, "pl->m_Local.m_skybox3d.area = 255;" ) )
		<< "stock's unreachable arm is back in this file. Its 255 is not a "
		   "second source of truth -- it is a second thing to keep in step.";
}

//=============================================================================//
// 3. THE PREMISE — if this stops being true the fix's shape is wrong
//=============================================================================//

// TheSdkPlayerLookupIsGoneBecauseItCouldNeverAnswer went with
// shell_playerslots.cpp itself (plan_server_native_min P1): the whole
// slot-walk family is deleted, so there is no file left to prove the absence
// in. The live walk above (TheWalkAsksTheSlotTableForAStandalonePlayer) is
// what keeps the standalone arm honest now.

//=============================================================================//
// 4. THE LIVE PROBE AND THE READOUT IT READS
//=============================================================================//

TEST( ClientDataWalk, TheReadoutTheProbeParsesStillHasTheSkyboxAreaInIt )
{
	// tools/p4-exit-probe-skybox greps cs_sv_transmitset's header for
	// `skyarea=`. If the format line is reworded the probe stops measuring and
	// reports "answered nothing usable" -- which is a refusal rather than a
	// false pass, but it is still a probe that has quietly stopped working.
	//
	// cs_sv_transmitset is a diagnostic, so its body is in shell_gamedll_debug.cpp
	// and only a debug server.so has it (2026-10-01): the probe needs a debug build.
	const std::string cpp = ReadFile( kGameDllDebug );
	ASSERT_FALSE( cpp.empty() ) << kGameDllDebug << " did not load";
	EXPECT_EQ( 1u, CountOf( cpp, "TRANSMITSET: recipient=%d count=%d skyarea=%d" ) )
		<< "the line tools/p4-exit-probe-skybox parses has been reworded";
}

TEST( ClientDataWalk, TheProbeIsInTheTreeAndCanBeAskedToExpectTheBug )
{
	const std::string probe = ReadFile( kProbe );
	ASSERT_FALSE( probe.empty() ) << kProbe << " did not load";
	// --expect is what makes the probe falsifiable: the BEFORE half of the A/B
	// runs it with `--expect 0` against a build with the fix reverted, and it
	// passed. A probe with only one possible answer is not a probe.
	EXPECT_NE( probe.find( "\"--expect\"" ), std::string::npos )
		<< "the probe can no longer be asked to expect the buggy value, so the "
		   "A/B that proved it can fail cannot be re-run";
}
