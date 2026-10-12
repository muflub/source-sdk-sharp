//====== SourceSharp — SCC 11 cluster A: the entity base, and what it did FOR
//====== every entity that was not it =========================================//
//
// plan_entity.md, the Phase 4 exit, lane p4-base.
//
// WHY THIS FILE EXISTS.
//
// Eight silent losses have been found in this port and EVERY ONE of them was a
// thing the base class did FOR an entity: CWorld::Spawn's shared activity and
// event lists and its eight g_sModelIndex* sprites; ent_create's drop-to-floor;
// shadow re-projection through CBaseEntity::InvalidatePhysicsRecursive; every
// managed sound emitting GENDER_NONE because C_BaseEntity::Instance answers
// NULL for a managed entity; SetVisible(false) writing ModelIndex = 0; the
// physics event ring draining into nothing; UpdateAllClientData walking a
// UTIL_PlayerByIndex that cannot answer; and C_BaseEntity::PostDataUpdate never
// running at all.
//
// None of those was visible to the compiler, to `make check`, or to any live
// gate. They are all the same shape, and this cluster -- baseentity.cpp,
// baseentity_shared.cpp, entitylist.cpp, cbase.cpp, client.cpp, subs.cpp -- is
// the class they all came from. So every removal this lane makes carries a row
// here naming what the removed code was DOING and where that now lives, and
// every mechanism the base still runs on behalf of a hierarchy that is no
// longer under it carries a row saying it is STILL WIRED.
//
// THE STRIPPER IS LOAD-BEARING, for the reason recorded three times in this
// project: a needle that lives only inside a comment is a check that cannot
// fail. These files carry a great deal of prose ABOUT the names they no longer
// call, so every scan here is over comment-stripped text.
//
// baseentity.cpp, client.cpp and subs.cpp ARE NO LONGER COMPILED by
// server.so and have left the source tree. The rows that read them -- the
// ConsoleFireTargets / DumpScriptScope guards and GetNextCommandEntity count,
// the retired native ClientPrecache list, the stock half of the teleport
// discontinuity, subs.cpp's four retired registrations -- and the
// nesting-aware guard scanner that existed only for them were culled with
// them. What stays is the managed and shim half of each row.
//
//=============================================================================//

#include <string>
#include <vector>
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
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
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

std::string ReadRaw( const std::string &strRelative )
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

std::string ReadStripped( const std::string &strRelative )
{
	return StripComments( ReadRaw( strRelative ) );
}

bool Has( const std::string &strHaystack, const char *pszNeedle )
{
	return strHaystack.find( pszNeedle ) != std::string::npos;
}

int CountOf( const std::string &strHaystack, const char *pszNeedle )
{
	int n = 0;
	const size_t len = strlen( pszNeedle );
	for ( size_t at = strHaystack.find( pszNeedle ); at != std::string::npos;
	      at = strHaystack.find( pszNeedle, at + len ) )
	{
		n++;
	}
	return n;
}

// Anchors are deliberately NOT the thing each test asserts about: an anchor
// equal to the assertion cannot catch a rename of the subject.
const char *kWorldAdapter = "sourcesharp/game/server/world_adapter.cpp";
const char *kRetired      = "sourcesharp/managed/SourceSharp.Server/DevCommandsRetired.cs";
const char *kLive         = "sourcesharp/managed/SourceSharp.Server/DevCommandsLive.cs";

void RequireAnchor( const std::string &strSrc, const char *pszWhat, const char *pszAnchor )
{
	ASSERT_FALSE( strSrc.empty() ) << pszWhat << " did not open at all";
	ASSERT_TRUE( Has( strSrc, pszAnchor ) )
		<< pszWhat << " no longer contains its anchor '" << pszAnchor << "'; every "
		<< "absence assertion in this file would be vacuously satisfied. Re-point "
		<< "the anchor before trusting anything else here.";
}

//=============================================================================//
// 2. AND THE BEHAVIOUR IS NOT LOST -- it was already retired, out loud.
//
// This is the ent_create drop-to-floor shape: ConsoleFireTargets and
// DumpScriptScope in baseentity.cpp were guarded away because their only
// callers were, and "its only caller is guarded away" is a statement about the
// CALL GRAPH -- the behaviour can walk out with it. (Section 1, which pinned
// the guards themselves, read baseentity.cpp and was culled with it.)
//=============================================================================//

TEST( EntityBaseCluster, FiretargetIsALiveManagedCommandAndNotMerelyRetired )
{
	// firetarget is the stronger of the two: it is not retired at all, it is
	// REIMPLEMENTED. DevCommandsLive.cs does stock's Use-by-name on BOTH
	// hierarchies, so guarding ConsoleFireTargets away loses nothing a player
	// can type.
	const std::string src = ReadStripped( kLive );
	RequireAnchor( src, "DevCommandsLive.cs", "class " );

	EXPECT_TRUE( Has( src, "[ConCommand(\"firetarget\"" ) )
		<< "the `firetarget` spelling is no longer registered from managed code. "
		<< "Guarding ConsoleFireTargets away is only safe while something still "
		<< "ANSWERS the word a player types.";
}

TEST( EntityBaseCluster, EntScriptDumpIsADeclaredAndPrintedRetirementInManagedCode )
{
	const std::string src = ReadStripped( kRetired );
	RequireAnchor( src, "DevCommandsRetired.cs", "class " );

	EXPECT_TRUE( Has( src, "[ConCommand(\"ent_script_dump\"" ) )
		<< "the `ent_script_dump` spelling is no longer registered from managed "
		<< "code. A retirement deleted along with the code it retires looks "
		<< "exactly like a port.";
}

//=============================================================================//
// 3. WHAT client.cpp STILL OWES THE BUILD, so the exit lane knows what has to
//    move with it rather than discovering it at link time.
//
// Measured on this lane's own build with two independent instruments that agree:
//
//   $ tools/4a-owes client                    (and .p4base/owes.py client)
//     shell_engineboundary (1)   sv_cheats
//     shell_standalone_player (1)  sv_cheats
//     world_adapter (1)            ClientPrecache()
//
// ClientPrecache is the g_sModelIndex* shape exactly: a precache list whose only
// caller is a level-load path, whose failure mode is an index left at 0 and
// nothing logged.
//=============================================================================//

//=============================================================================//
// 3b. THE TWENTY-THREE CLIENT ASSETS ARE MANAGED, AND BOTH SIDES ARE COUNTED.
//
// This is the g_sModelIndex* shape and the reason it is checked from both ends
// rather than "moved and trusted": the failure mode of a dropped entry is a
// missing material or a silent sound at RUNTIME. Nothing in a build, in
// `make check` or in a live gate says a precache did not happen.
//
// The MANAGED half -- every name, both totals, the slash rule -- is
// ClientAssetsTests.cs. This half holds that the native list really is retired
// and that nothing calls the retired function, so the two lists cannot both
// exist and quietly disagree.
//=============================================================================//

TEST( EntityBaseCluster, WorldAdapterCallsTheShimsGamePrecacheAndNotTheStockOne )
{
	const std::string src = ReadStripped( kWorldAdapter );
	RequireAnchor( src, "world_adapter.cpp", "void CPrecacheRegister::Precache()" );

	EXPECT_TRUE( Has( src, "ClientGamePrecache();" ) )
		<< "world_adapter.cpp no longer calls ClientGamePrecache(), which is what "
		<< "precaches the player model the engine requires before anyone spawns.";
	EXPECT_EQ( 0, CountOf( src, "ClientPrecache();" ) )
		<< "world_adapter.cpp calls the retired ClientPrecache() again. That symbol "
		<< "is behind #ifndef SOURCESHARP, so this is a link error today -- but if "
		<< "someone un-guards the C++ list to fix the link, the mod gets two lists.";
}

TEST( EntityBaseCluster, TheSvCheatsStorageIsTheShimsAndTheDeclarationDidNotMove )
{
	// The IMPLEMENT_MOVEHELPER() shape: storage belongs with its installer.
	const std::string shim = ReadStripped( "sourcesharp/game/server/shell_engineboundary.cpp" );
	RequireAnchor( shim, "shell_engineboundary.cpp", "g_pCVar->FindVar( \"sv_cheats\" )" );

	EXPECT_TRUE( Has( shim, "ConVar *sv_cheats = NULL;" ) )
		<< "the sv_cheats storage is not in the file that fills it.";

	// The other half -- game/server/client.cpp defining no copy -- read a file
	// server.so no longer compiles and which has left the source tree.

	// The declaration half -- player.h's `extern ConVar *sv_cheats;` -- is
	// retired: game/server/player.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6.
}

//=============================================================================//
// 4. DEFECT 8 -- C_BaseEntity::PostDataUpdate NEVER RUNS IN THIS BUILD, ARM BY
//    ARM.
//
// Reported by 8e-classes as an observation and left unowned: "the first team
// probe sat there and printed nothing across a full two-pass gate while 8,480
// sibling lines reached the same log. C_StandaloneEntity overrides it and does
// not chain."
//
// THE FIRST HALF OF THE SENTENCE IS TRUE AND THE SECOND IS NOT, and the
// difference matters. C_StandaloneEntity does not OVERRIDE C_BaseEntity's
// PostDataUpdate and cannot: it is not derived from C_BaseEntity at all
// (`class C_StandaloneEntity : public IClientEntity, public ICollideable,
// public IGameEntity`), so there is nothing to chain TO. C_BaseEntity's version
// does not run because NO C_BaseEntity IS EVER CONSTRUCTED: the client factory
// runs when the server sends an entity of that class, and this build's server
// registers four stock ServerClasses --
//
//     $ <every compiled server stock TU> | grep IMPLEMENT_SERVERCLASS
//       baseanimating.cpp        CBaseAnimating   / DT_BaseAnimating
//       baseentity.cpp           CBaseEntity      / DT_BaseEntity
//       EntityDissolve.cpp       CEntityDissolve  / DT_EntityDissolve
//       physics_prop_ragdoll.cpp CRagdollProp     / DT_Ragdoll
//
// -- and none of the four is spawned by either shipped map (p4-lifecycle
// measured that against both entity lumps).
//
// SO IT IS NOT A BUG, IT IS AN ACCURATE STATEMENT ABOUT THE PORT'S STATE. What
// IS the question worth asking is the one this project has paid for eight
// times: WHAT DID THAT FUNCTION DO FOR AN ENTITY, and does anything do it now?
// Sixteen arms, enumerated below. Fourteen are answered; ONE is a recorded
// deliberate divergence; ONE is an open defect nobody had written down.
//
//   #   arm of C_BaseEntity::PostDataUpdate      where it is on this hierarchy
//   --  --------------------------------------  ----------------------------
//   1   MDLCACHE_CRITICAL_SECTION               mechanism, not behaviour
//   2   MoveToLastReceivedPosition              NoteNetworkedPosition +
//                                               OnPositionChanged, every update
//   3   `if ( index == 0 )` force the world's   C_World's OWN answers -- step 5e,
//       model index 1 / SOLID_BSP / origin /    written out in
//       angles                                  shell_world_nettable_client.cpp
//   4   SetRenderMode on a change               ApplyRenderPolicy, serial-gated
//                                               off Block().Client.PolicyOutputs
//   5   OnLatchInterpolatedVariables            the managed interpolation pass
//                                               (Interpolation.SamplePass)
//   6   OnStoreLastNetworkedValue               prediction is ClientPlayerPrediction
//   7   HierarchySetParent(m_hNetworkMoveParent) parenting is managed on BOTH
//                                               sides; the client reads the
//                                               networked Head.WorldTransform
//   8   MarkMessageReceived                     NoteNetworkedPosition writes
//                                               Client.SimulationTime from the
//                                               same engine->GetLastTimeStamp(),
//                                               and SamplePass uses it for the
//                                               same question
//   9   ValidateModelIndex                      by construction: GetModel()
//                                               resolves Head.ModelIndex live,
//                                               there is no cached pointer
//  10   ResetLatched on DATA_UPDATE_CREATED     Interpolation's `fresh` arm
//  11   CheckInitPredictable                    ClientPlayerPrediction
//  12   the player-simulation list              no player-simulated entity exists
//  13   UpdatePartitionListEntry                ** DELIBERATE DIVERGENCE **, and
//                                               it is recorded: shell_standalone_client.h's
//                                               opening paragraph, "no edict to
//                                               own, no spatial partition to
//                                               join". ClientTeExplosion.cs works
//                                               around it by name.
//  14   AddToTeleportList on Teleported()       ** OPEN DEFECT **, recorded
//                                               nowhere until this lane. See below.
//  15   UpdateVisibility on a parent change     ApplyRenderPolicy -> UpdateVisibility
//  16   UpdateVisibility on a ShouldDraw change ApplyRenderPolicy -> UpdateVisibility
//
// Arms 13 and 14 are the whole finding, and they are DIFFERENT KINDS of thing:
// one is a decision somebody made and wrote down, the other is a gap nobody
// chose. Filing them together would turn the second into the first, which is
// exactly what ContractBaseline keeps Refusals and OpenDefects apart for.
//=============================================================================//

TEST( EntityBaseCluster, TheClientPartitionAbsenceIsARECORDEDDivergenceAndNotADiscovery )
{
	// Arm 13. Asserted so that a later lane cannot "discover" it again and file
	// it as a bug -- and so that the record cannot quietly disappear while the
	// behaviour stays.
	const std::string hdr = ReadRaw( "sourcesharp/game/client/shell_standalone_client.h" );
	ASSERT_FALSE( hdr.empty() );

	EXPECT_TRUE( Has( hdr, "no spatial partition" ) )
		<< "shell_standalone_client.h no longer records that this hierarchy joins "
		<< "no client spatial partition. The behaviour is unchanged either way; "
		<< "what would be lost is the only statement that it is deliberate.";
}

TEST( EntityBaseCluster, TheStandaloneTeleportStillDoesNotSayItWasADiscontinuity )
{
	// Arm 14, half two, and it is asserted in the DIRECTION OF THE BUG on
	// purpose: this fact goes RED when somebody fixes it, at which point the
	// OpenDefects entry has to go too. A fact that asserted the FIX would be
	// red today and would be deleted by the next person who ran the suite.
	const std::string src = ReadStripped( "sourcesharp/game/server/shell_entityapi.cpp" );
	RequireAnchor( src, "shell_entityapi.cpp", "void SourceSharp_EntityTeleport(" );

	const size_t at = src.find( "void SourceSharp_EntityTeleport(" );
	ASSERT_NE( std::string::npos, at );
	const size_t end = src.find( "\n}", at );
	ASSERT_NE( std::string::npos, end );
	const std::string body = src.substr( at, end - at );

	EXPECT_TRUE( Has( body, "pStandalone->SetAbsOrigin( vecOrigin );" ) )
		<< "the standalone teleport arm changed shape; re-read it against "
		<< "CBaseEntity::Teleport before trusting the OpenDefects entry.";
	EXPECT_EQ( 0, CountOf( body, "InterpolationFrame" ) )
		<< "the standalone teleport arm now moves an interpolation-frame flag. "
		<< "If that is the fix, DELETE the OpenDefects entry 'An entity that "
		<< "teleports is drawn sliding to where it went' -- this fact exists to "
		<< "make the list expire with the bug.";
}

//=============================================================================//
// 5. subs.cpp's FOUR MAP CLASSNAMES -- the 5e shape, checked rather than assumed.
//
// game/server/subs.cpp is 399 lines and every one of them is CBaseEntity doing
// something for an entity that is not it: SUB_Remove and SUB_DoNothing (think
// functions other classes install), FireTargets, CBaseToggle, and FOUR
// LINK_ENTITY_TO_CLASS registrations. The registrations are the dangerous half,
// because a map classname that nothing answers produces NO entity and no error.
//
//   info_player_deathmatch   CBaseDMStart    -> PlayerSpawnPoint.cs
//   info_player_start        CPointEntity    -> PlayerSpawnPoint.cs
//   info_landmark            CPointEntity    -> nothing; appears in no shipped map
//   info_null                CNullEntity     -> nothing; appears in no shipped map,
//                                              AND its stock body is
//                                              UTIL_Remove(this), so "no factory
//                                              answers" reaches the same end state
//
//     $ for c in ...; do strings -a game/mod_sharp/maps/dm_lockdown.bsp | grep -c "\"$c\""; done
//       info_player_deathmatch 9   info_player_start 1
//       info_landmark 0            info_null 0
//
// All four are ALREADY inside #ifndef SOURCESHARP, so this is a statement about
// what has to keep answering, not a change.
//=============================================================================//

TEST( EntityBaseCluster, TheTwoSpawnPointClassnamesSubsCppRetiredAreAnsweredInManagedCode )
{
	const std::string cs = ReadStripped(
		"sourcesharp/managed/SourceSharp.Server/Entities/Markers/PlayerSpawnPoint.cs" );
	RequireAnchor( cs, "PlayerSpawnPoint.cs", "class PlayerSpawnPoint" );

	EXPECT_TRUE( Has( cs, "EntityNames.InfoPlayerDeathmatch" ) )
		<< "nothing answers info_player_deathmatch. dm_lockdown has NINE of them "
		<< "and a classname no factory answers produces no entity and no error.";
	EXPECT_TRUE( Has( cs, "EntityNames.InfoPlayerStart" ) )
		<< "nothing answers info_player_start.";
}

}  // namespace
