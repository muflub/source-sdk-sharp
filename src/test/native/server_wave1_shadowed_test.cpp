//====== SourceSharp — plan_server_all.md Wave 1: the shadowed five ==========//
//
// Four of the five left the server. Each was a file whose DECISIONS already
// lived in C# and which survived only as a native service; each went by a
// different exit, and the exits are what this file holds, because a later lane
// reading only the meter cannot tell them apart.
//
//   weapon_parse.cpp        DELETED FROM THE LIST, nothing replaced it.
//                           ./evict_consumers already answered
//                           "0 undefined refs ; 0 consumers" before the wave
//                           touched it. Its one hook into the shim went with
//                           it: CreateWeaponInfo in shell_shared.cpp existed
//                           for weapon_parse.cpp:103 and no other caller.
//
//   env_wind_shared.cpp     DELETED FROM THE LIST after ONE dead call went.
//                           gameinterface.cpp called ResetWindspeed() from
//                           LevelInit; that walks s_windControllers, and the
//                           only thing that ever put a CEnvWindShared in it on
//                           the SERVER was CEnvWind in effects.cpp, which is
//                           not in this build. A loop over an empty list.
//
//   soundscape_system.cpp   RELOCATED AND SHRUNK, 466 lines to ~200, as
//                           sourcesharp/game/server/shell_soundscape.cpp. What
//                           stayed is the name->index registry, and it stayed
//                           native on purpose: the index is SHARED WITH THE
//                           CLIENT, which parses the same files in the same
//                           order into its own copy. What went was dead: an
//                           entity list nothing fills, a cluster index over it,
//                           the per-frame "which soundscape do you hear"
//                           (Soundscapes.cs decides that now), and an X360
//                           sound cache.
//
//   func_areaportalbase.cpp FOLDED, all 71 lines, into shell_areaportal.cpp --
//                           which was already the only thing deriving from
//                           CFuncAreaPortalBase.
//
//                           AND THEN UNFOLDED AGAIN, at plan_entity.md step 4g:
//                           the list is retyped to a record that is not an
//                           entity, in the shim's own areaportal_list.h, and
//                           the class the bodies belonged to has no instances
//                           left. So this file's areaportal fact stopped being
//                           "the bodies are here" and became "the bodies are
//                           gone and nothing brought the .cpp back", which is
//                           the same eviction stated against the shape that
//                           replaced it. func_areaportalbase.h is still on disk
//                           and is now read by NOTHING the build compiles.
//
// TemplateEntities.cpp, the fifth, did NOT leave and this file says so rather
// than being quiet about it -- see the last test.
//
// STRIPPER RULE: the rationale comments (in server_csharp.vpc while it lived,
// and in the shell sources still) name every one of these files in the paragraphs explaining why it went, so a scan that does not
// remove comments reads the explanation as the violation.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>

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

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
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

std::string NoWhitespace( const std::string &s )
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

bool Exists( const std::string &strPath )
{
	struct stat st;
	return stat( strPath.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

// Comment-stripped, whitespace-squashed contents of a file under src/, with
// an anchor that must survive -- so a rewritten file fails loudly instead of
// making every absence assertion true.
std::string Tight( const char *pszRel, const char *pszAnchor )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	const std::string raw = ReadRaw( std::string( pszRoot ) + "/" + pszRel );
	EXPECT_FALSE( raw.empty() )
		<< "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";

	const std::string tight = NoWhitespace( StripComments( raw ) );

	EXPECT_NE( tight.find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " no longer contains its anchor \"" << pszAnchor
		<< "\" outside of comments. The file this test reasons about has been "
		<< "rewritten and its conclusions are stale.";

	return tight;
}

// WHAT EACH SIDE COMPILES is the target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt): server_sourcesharp and
// client_sourcesharp. Until Phase 6b this read server_csharp.vpc and
// client_csharp.vpc. The readers throw on a missing, empty or stale input, so
// an empty list can no longer answer "not named" for free; each absence below
// is still paired with a proof-of-read, the side's own shell entry point.
typedef std::set< std::string > SourceList_t;

const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";
const char *const kClientProofOfRead = "sourcesharp/game/client/shell_clientdll.cpp";

SourceList_t ServerList()
{
	return cmake_graph::CompiledSources( cmake_graph::kServer );
}

SourceList_t ClientList()
{
	return cmake_graph::CompiledSources( cmake_graph::kClient );
}

// BY BASENAME, IN ANY DIRECTORY, as the vpc check was: the question is "is a
// file of this name compiled at all", so a copy of weapon_parse.cpp moved to
// another directory still answers. The basename must be the whole last path
// component -- "c_baseentity.cpp" must not answer for "baseentity.cpp".
bool Names( const SourceList_t &list, const char *pszBasename )
{
	const std::string base = pszBasename;
	for ( SourceList_t::const_iterator it = list.begin(); it != list.end(); ++it )
	{
		const size_t slash = it->rfind( '/' );
		const std::string last = slash == std::string::npos ? *it : it->substr( slash + 1 );
		if ( last == base )
			return true;
	}
	return false;
}

} // namespace

//-----------------------------------------------------------------------------
// The four are out of the server's file list.
//-----------------------------------------------------------------------------
TEST( ServerWave1, TheFourShadowedFilesAreOutOfTheServerList )
{
	const SourceList_t list = ServerList();
	ASSERT_TRUE( list.count( kServerProofOfRead ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not compile "
		<< kServerProofOfRead << " -- the reader is not reading the server's list";

	EXPECT_FALSE( Names( list, "weapon_parse.cpp" ) )
		<< "weapon_parse.cpp is back in the server list. It had ZERO consumers "
		<< "and ZERO undefined refs before it went -- its weapon-script "
		<< "database is a file-local CUtlDict and nothing this build compiles "
		<< "names LookupWeaponInfoSlot, ReadWeaponDataFromFileForSlot or any "
		<< "of the rest.";

	EXPECT_FALSE( Names( list, "env_wind_shared.cpp" ) )
		<< "env_wind_shared.cpp is back. Its one server consumer was "
		<< "gameinterface.cpp's ResetWindspeed() call, and that walked a list "
		<< "only CEnvWind in effects.cpp ever added to.";

	EXPECT_FALSE( Names( list, "soundscape_system.cpp" ) )
		<< "soundscape_system.cpp is back. The soundscape index is C#'s "
		<< "(SoundscapeLibrary, plan_server_novalve.md P2.4); a native registry "
		<< "beside it is a second answer to one question.";

	EXPECT_FALSE( Names( list, "func_areaportalbase.cpp" ) )
		<< "func_areaportalbase.cpp is back. Its three bodies and g_AreaPortals "
		<< "are inside shell_areaportal.cpp now; compiling both defines "
		<< "g_AreaPortals twice.";
}

//-----------------------------------------------------------------------------
// weapon_parse: the shim hook that existed only for it went with it.
//-----------------------------------------------------------------------------
TEST( ServerWave1, CreateWeaponInfoLeftTheShimWithItsOnlyCaller )
{
	const std::string shell = Tight( "sourcesharp/game/shared/shell_shared.cpp",
	                                 "default_fov" );
	ASSERT_FALSE( shell.empty() );

	EXPECT_EQ( shell.find( "FileWeaponInfo_t*CreateWeaponInfo()" ), std::string::npos )
		<< "shell_shared.cpp defines CreateWeaponInfo again. It is the factory "
		<< "weapon_parse.cpp:103 calls to allocate a mod's weapon struct, and "
		<< "with weapon_parse.cpp out of the server build it has no caller on "
		<< "this side. The CLIENT gets its own from weapon_parse_default.cpp, "
		<< "which is why this was inside `#ifndef CLIENT_DLL`.";
	// (The premise -- game/shared/weapon_parse.cpp is CreateWeaponInfo's only
	// caller -- was read out of that file, which neither DLL compiles; the pin
	// left with it, culled 2026-09-27. The weapon script oracle compiles
	// upstream's copy and calls CreateWeaponInfo through it.)
}

//-----------------------------------------------------------------------------
// env_wind: the dead call went, and the fact that made it dead is asserted
// where it lives -- in somebody else's file.
//-----------------------------------------------------------------------------
TEST( ServerWave1, GameInterfaceNoLongerResetsAWindNothingCreates )
{
	// THE FILE THIS ASKED ABOUT IS GONE. The deleted call sat in stock
	// CServerGameDLL::LevelInit, which plan_gamedll_after.md Phase A9 step 3
	// deleted whole; its successor is CShellGameDll's level-start step list in
	// sourcesharp/game/server/shell_level.cpp, which is where a level
	// start would put the call back. The fact is unchanged and reads the file
	// that can now break it. Anchored on a step so that a file rewritten around
	// this test fails to anchor rather than reading as clean.
	//
	// THE ANCHOR EARNED ITS KEEP AND THEN MOVED. plan_shim.md \u00a75 3a (lane
	// 3b-enginebind) folded shell_levelshim.cpp into shell_levelsteps.cpp as
	// shell_level.cpp, turning every step from a free function into a vtable
	// method -- and this test went red on the anchor rather than passing over a
	// file whose steps it could no longer find, which is what the anchor is
	// for. It is the method now.
	const std::string gi = Tight( "sourcesharp/game/server/shell_level.cpp",
	                              "virtual void ResetServerTime() OVERRIDE" );
	ASSERT_FALSE( gi.empty() );

	EXPECT_EQ( gi.find( "ResetWindspeed()" ), std::string::npos )
		<< "the level-start step list calls ResetWindspeed() again. Nothing "
		<< "defines it in this build, so this is a link error waiting on "
		<< "--gc-sections to stop hiding it.";

	EXPECT_EQ( gi.find( "#include\"env_wind_shared.h\"" ), std::string::npos )
		<< "shell_level.cpp includes env_wind_shared.h again.";
}

TEST( ServerWave1, NothingLeftOnTheServerConstructsACEnvWindShared )
{
	const SourceList_t list = ServerList();
	ASSERT_TRUE( list.count( kServerProofOfRead ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not compile "
		<< kServerProofOfRead << " -- the reader is not reading the server's list";

	// s_windControllers is fed from CEnvWindShared's constructor. The only
	// server-side construction was CEnvWind, in effects.cpp. That file being
	// out of the build is the whole safety argument for deleting the reset,
	// and it is a fact about a file this lane does not own.
	EXPECT_FALSE( Names( list, "effects.cpp" ) )
		<< "effects.cpp is back in the server build. It holds CEnvWind, which "
		<< "embeds a CEnvWindShared -- so s_windControllers is no longer "
		<< "guaranteed empty and removing the level start's ResetWindspeed() "
		<< "call has become a behaviour change. Re-decide rather than leaving "
		<< "this test green by editing it.";
}

//-----------------------------------------------------------------------------
// soundscape: the registry moved to shell_soundscape.cpp in this wave, and
// then, in plan_server_novalve.md P2.4, out of C++ altogether. The index is the
// shared C# SoundscapeLibrary (stock's traversal; SoundscapeLibraryTests pin
// it clause by clause), held to the native registry's table on dm_lockdown,
// index for index, before the native one was deleted.
//-----------------------------------------------------------------------------
TEST( ServerWave1, TheSoundscapeRegistryLeftForCSharp )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	EXPECT_FALSE( Exists( std::string( pszRoot ) + "/sourcesharp/game/server/shell_soundscape.cpp" ) )
		<< "shell_soundscape.cpp is back: a second soundscape index beside C#'s, and "
		<< "any disagreement between them plays the wrong ambience silently.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer,
		"sourcesharp/game/server/shell_soundscape.cpp" ) );

	// The retention half: the index still exists, in C#, and the entity asks it.
	const std::string service = ReadRaw( std::string( pszRoot )
		+ "/sourcesharp/managed/SourceSharp.Server/Entities/Soundscape/Soundscapes.cs" );
	ASSERT_FALSE( service.empty() );
	EXPECT_NE( service.find( "_library = SoundscapeLibrary.Load(_files.ReadGameFile, mapName," ),
	           std::string::npos )
		<< "Soundscapes no longer builds the level's library at level init";

	const std::string entity = ReadRaw( std::string( pszRoot )
		+ "/sourcesharp/managed/SourceSharp.Server/Entities/Soundscape/EnvSoundscape.cs" );
	ASSERT_FALSE( entity.empty() );
	EXPECT_NE( entity.find( "SoundscapeIndex = Soundscapes.IndexOf(Vars.SoundscapeName);" ),
	           std::string::npos )
		<< "env_soundscape no longer resolves its name through the C# library";
}

//-----------------------------------------------------------------------------
// areaportal: the bodies moved, the declaration did not, and there is exactly
// one definition of the list.
//-----------------------------------------------------------------------------
TEST( ServerWave1, TheAreaPortalListIsDefinedExactlyOnceAndInTheShell )
{
	const std::string ap = Tight( "sourcesharp/game/server/shell_areaportal.cpp",
	                              "SourceSharp_AreaPortalCreate" );
	ASSERT_FALSE( ap.empty() );

	EXPECT_NE( ap.find( "std::list<CAreaPortal*>g_AreaPortals;" ),
	           std::string::npos )
		<< "g_AreaPortals is not defined in shell_areaportal.cpp. "
		<< "ClientSetupVisibility walks it every frame for every client; with "
		<< "no definition the link fails, and with two it fails differently.";

	// The join and the leave are the record's constructor and destructor, and
	// they are in the header with the rest of the engine-free half --
	// areaportal_list_test.cpp drives them for real. Named here because "the
	// list is defined once" and "something is in it" are two facts and this
	// test is about the pair.
	const std::string list = Tight( "sourcesharp/game/server/areaportal_list.h",
	                                "class CAreaPortal" );
	ASSERT_FALSE( list.empty() );

	EXPECT_NE( list.find( "m_itList=g_AreaPortals.insert(g_AreaPortals.end(),this);" ), std::string::npos )
		<< "nothing puts a portal IN g_AreaPortals. Without that a portal is "
		<< "never walked, never gets its bit set, and the room beyond it stops "
		<< "drawing.";

	EXPECT_NE( list.find( "g_AreaPortals.erase(m_itList);" ), std::string::npos )
		<< "nothing takes a portal OUT of g_AreaPortals. A freed record left in "
		<< "the list is walked every frame for every client.";

	EXPECT_NE( ap.find( "AREAPORTAL_VIEWER_PADDING" ), std::string::npos )
		<< "the viewer padding is gone from the backface test. Without it a "
		<< "portal snaps shut the instant the viewer crosses its plane.";
}

//-----------------------------------------------------------------------------
// ...and the SDK class the bodies used to belong to has no instances left.
//
// A separate fact from the one above on purpose: "the list is ours" and "the
// old class is unused" are two things, and a single test asserting both would
// go green on either being true if the other were quietly restored.
//-----------------------------------------------------------------------------
TEST( ServerWave1, NothingDerivesFromTheSdkAreaPortalClassAnyMore )
{
	const std::string ap = Tight( "sourcesharp/game/server/shell_areaportal.cpp",
	                              "SourceSharp_AreaPortalCreate" );
	ASSERT_FALSE( ap.empty() );

	EXPECT_EQ( ap.find( "CFuncAreaPortalBase" ), std::string::npos )
		<< "shell_areaportal.cpp names CFuncAreaPortalBase again. The record is "
		<< "an SDK entity once more, with the network property, collision "
		<< "property, datadesc, think list, touch links and save/restore a "
		<< "portal does not use.";

	const std::string walk = Tight( "sourcesharp/game/server/shell_gameclients.cpp",
	                                "for(CAreaPortal*pCur:g_AreaPortals)" );
	ASSERT_FALSE( walk.empty() );

	EXPECT_EQ( walk.find( "func_areaportalbase.h" ), std::string::npos );
	EXPECT_EQ( walk.find( "func_areaportalwindow.h" ), std::string::npos )
		<< "the visibility walk includes an SDK areaportal header again. It "
		<< "reaches the list through areaportal_list.h now.";

	// The stock files are still on disk and still out of the build. Asserted
	// because "nothing includes them" is only half of "they are not compiled",
	// and the window one was never on the eviction list above at all.
	const SourceList_t list = ServerList();
	ASSERT_TRUE( list.count( kServerProofOfRead ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not compile "
		<< kServerProofOfRead << " -- the reader is not reading the server's list";
	EXPECT_FALSE( Names( list, "func_areaportal.cpp" ) );
	EXPECT_FALSE( Names( list, "func_areaportalwindow.cpp" ) );
}

//-----------------------------------------------------------------------------
// The fifth LEFT, and this fact is the replacement the old one asked for.
//
// It used to read TemplateEntitiesIsStillHereAndStillDoingWork, and its message
// said: "If that was done on purpose, delete this test and say in its place
// which of the three exits it took." It was done on purpose -- plan-audit D11 --
// and the exit is the SECOND one, the same one soundscape_system.cpp took: the
// file is replaced by a shell holding the part of it this build can still reach,
// and everything else moved to C#.
//
// The plan's premise for the file WAS wrong when it was written, and the old
// fact was right to say so: PointTemplate.cs owned the spawning and the store
// behind it was 200-odd lines of live C++. What changed is not that the store
// stopped mattering, it is that the store stopped needing to be native -- a
// list, a case-insensitive name match, a counter and a string substitution over
// a byte buffer, with the tokeniser under all four now held to the real
// mapentities_shared.cpp by sourcesharp/test/map_entity_block_cases.txt.
//
// WHAT IS ASSERTED IS THE RESIDUE, not the absence. A fact that only said "the
// $File is gone" would pass equally on an eviction that dropped the two
// forwards, and dropping them is a link error at best and a template store that
// silently holds nothing at worst.
//-----------------------------------------------------------------------------
TEST( ServerWave1, TemplateEntitiesLeftAndItsTwoForwardsDidNot )
{
	const SourceList_t list = ServerList();
	ASSERT_TRUE( list.count( kServerProofOfRead ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not compile "
		<< kServerProofOfRead << " -- the reader is not reading the server's list";

	EXPECT_FALSE( Names( list, "TemplateEntities.cpp" ) )
		<< "TemplateEntities.cpp is back in the server build. Wave 1 evicted it "
		<< "(plan-audit D11) and the store is SourceSharp.Server's TemplateStore; "
		<< "compiling both would give the tree two template stores, and the one "
		<< "the map parse fills would not be the one point_template reads.";

	// THE ROW STAYS AND ITS REASON HAS EXPIRED, which is recorded here rather
	// than left to be re-derived. Until plan_shim.md Phase 2c the map parse
	// called Templates_AddNamed from inside MapEntity_TemplateCapture's sweep;
	// that sweep only ever ran inside a window MapEntity_ParseAllEntities
	// opened, that function had no caller, and the whole band went. Measured
	// after: BOTH exports of shell_engineservices.cpp now have 0 importers on the
	// server link line.
	//
	// So this file is a candidate for eviction on the same argument Wave 1 used
	// for TemplateEntities.cpp -- and that is Phase 1b's ledger and another
	// lane's file, so what is asserted is the state today.
	EXPECT_TRUE( Names( list, "shell_engineservices.cpp" ) )
		<< "shell_engineservices.cpp is not in the server build. Since Phase 2c "
		<< "nothing imports Templates_Add or Templates_AddNamed, so removing the "
		<< "row may well be right -- but it belongs in a ledger with a "
		<< "0-importer proof beside it rather than as a silent deletion.";

	// THE TWO FORWARDS ARE GONE NOW (server gc cull, 2026-10-01). That is the
	// ruling this fact waited for: the user's cull removes what the linker
	// drops, and server.so's --print-gc-sections link dropped both
	// (0 importers since Phase 2c). The file stays for its other entry points.
	// (Anchored on SourceSharp_FilesReadGameFile until the game-file reads
	// folded onto IBaseFileSystem's forwarders, 2026-10-19.)
	const std::string shell = Tight( "sourcesharp/game/server/shell_engineservices.cpp",
	                                 "SourceSharp_EntitiesInvalidateMdlCacheAll" );
	ASSERT_FALSE( shell.empty() );

	EXPECT_EQ( shell.find( "Templates_AddNamed(" ), std::string::npos )
		<< "Templates_AddNamed is back in shell_engineservices.cpp; it had no "
		<< "caller when the cull deleted it.";
	EXPECT_EQ( shell.find( "Templates_Add(" ), std::string::npos )
		<< "Templates_Add is back in shell_engineservices.cpp; it had no caller "
		<< "when the cull deleted it.";

	// And the two calls that DID go, which is the measurable half of the port.
	const std::string api = Tight( "sourcesharp/game/server/shell_entityapi.cpp",
	                               "SourceSharp_" );
	EXPECT_EQ( api.find( "Templates_ReconnectIOForGroup" ), std::string::npos )
		<< "shell_entityapi.cpp still calls Templates_ReconnectIOForGroup, which "
		<< "no longer has a definition. The reconnect is TemplateStore's and is "
		<< "one managed object calling another.";

	EXPECT_EQ( api.find( "Templates_SchedulePrecache" ), std::string::npos )
		<< "shell_entityapi.cpp still calls Templates_SchedulePrecache, which no "
		<< "longer has a definition. The pending list is TemplateStore's.";
}

//-----------------------------------------------------------------------------
// AND THE CLIENT CAN BE ASKED THE SAME QUESTION NOW.
//
// Three of the five files above are SHARED -- game/shared/weapon_parse.cpp and
// env_wind_shared.cpp compile into the client too -- so "it left the server"
// was only ever half an answer, and until plan_vpc_client.md landed the other
// half could not be asked at all: the client's list was subtractive, and a
// file it had never been told about read as present whether it was compiled or
// not.
//
// It is additive now -- 288 $File entries, all of them .cpp. This fact records
// which side each of them is on, so a later wave that evicts one from the
// client has to come here and say so rather than discovering it from a link
// error.
//-----------------------------------------------------------------------------
TEST( ServerWave1, TheClientListAnswersForTheSharedFilesToo )
{
	const SourceList_t list = ClientList();

	// The list is not empty and is the file list -- asserted before anything
	// reads a FALSE out of it.
	// REPOINTED BY plan_entity.md 5b, not weakened. The client's entry point is
	// the file carrying the class exposed as IBaseClientDLL, and that is
	// sourcesharp/game/client/shell_clientdll.cpp now: 5b moved CHLClient out of
	// game/client/cdll_client_int.cpp and took the stock translation unit off
	// this list. The anchor's JOB is unchanged -- prove the string being read is
	// the file list before any EXPECT_FALSE below is believed.
	ASSERT_TRUE( list.count( kClientProofOfRead ) )
		<< "the client list does not name its own entry point, so this test is "
		<< "reading something that is not the file list.";

	// weapon_parse.cpp left the SERVER in Wave 1 and HAS NOW LEFT THE CLIENT
	// TOO -- plan_entity.md §6's "17 that LEAVE", lane c8-weapontrio. It went
	// as one atomic commit with weapons_resource.cpp and
	// weapon_parse_default.cpp -- batch 45 of the client-side removal order, 3
	// TUs and 779 lines. The instrument that computed those batches is deleted
	// now, with the other six removal-set instruments; the number is kept
	// because it says how big the move was, not because anything can re-run it.
	//
	// SAYING WHICH CONSUMER WENT WITH IT, as this assertion's old message
	// asked. The comment above used to say "C_BaseCombatWeapon reads weapon
	// scripts through it", and by the time the file left, that was no longer
	// true of anything COMPILED: C_BaseCombatWeapon is not in this project, and
	// neither are the HUD readers stock has (weapon_selection.cpp,
	// history_resource.cpp, hud_ammo.cpp, c_baseplayer.cpp).
	//
	// Measured rather than asserted: over all 287 client objects, every symbol
	// the three files DEFINE against the undefined sets of the other 284, there
	// was exactly ONE consumer edge --
	// PrecacheFileWeaponInfoDatabase, wanted by world_adapter_client.o for
	// W_Precache(). §6's own text claims that symbol has no consumer either,
	// and that part of §6 is wrong. It is answered now by
	// sourcesharp/game/client/shell_weapon_scripts.cpp, and the database it
	// used to fill is SourceSharp.Shared/WeaponScriptDatabase.cs, pinned to the
	// C++ parse's own answers by sourcesharp/test/weapon_script_oracle_test.cpp.
	EXPECT_FALSE( Names( list, "weapon_parse.cpp" ) )
		<< "weapon_parse.cpp is back in the client list. The weapon script "
		<< "database is managed now; if this file is wanted again, say which "
		<< "consumer needs the C++ parse rather than WeaponScriptDatabase.cs.";

	EXPECT_FALSE( Names( list, "weapon_parse_default.cpp" ) )
		<< "weapon_parse_default.cpp is back in the client list. It is the other "
		<< "half of the same atomic commit -- it exists only to supply "
		<< "CreateWeaponInfo() to weapon_parse.cpp, which has gone.";

	EXPECT_FALSE( Names( list, "weapons_resource.cpp" ) )
		<< "weapons_resource.cpp is back in the client list. gWR had zero "
		<< "consumers outside the trio and its two surviving methods were "
		<< "already returning NULL under SOURCESHARP.";

	// AND THE SYMBOL THE TRIO LEFT BEHIND IS NOT ASKED FOR ANY MORE.
	//
	// REWRITTEN, not re-pointed. This assertion used to require the client
	// list to name shell_weapon_scripts.cpp, whose only reason to exist was to
	// define PrecacheFileWeaponInfoDatabase for the one caller -- W_Precache()
	// in world_adapter_client.cpp. The BEHAVIOUR it was protecting is "the
	// client links": the three EXPECT_FALSEs above are otherwise satisfied by
	// a build with an undefined symbol in it.
	//
	// Phase 1c deleted the caller as well as the definition, so the way to ask
	// the same question is now the OTHER side of the edge: nothing in the
	// client's own sources calls it. Asked of the source text rather than of
	// the source list, because that is where a reintroduced call would appear,
	// and a list row cannot say whether anyone calls anything.
	EXPECT_FALSE( Names( list, "shell_weapon_scripts.cpp" ) )
		<< "shell_weapon_scripts.cpp is back in the client list. It existed "
		<< "only to define PrecacheFileWeaponInfoDatabase for W_Precache(); "
		<< "that call is gone and the parse is "
		<< "SourceSharp.Shared/WeaponScriptDatabase.cs.";

	const std::string worldAdapter =
		Tight( "sourcesharp/game/client/world_adapter_client.cpp", "void W_Precache(void)" );

	EXPECT_EQ( worldAdapter.find( "PrecacheFileWeaponInfoDatabase" ), std::string::npos )
		<< "world_adapter_client.cpp calls PrecacheFileWeaponInfoDatabase "
		<< "again. Nothing on the client link line defines it -- the client "
		<< "will not link. The weapon script database is managed "
		<< "(SourceSharp.Shared/WeaponScriptDatabase.cs).";

	// soundscape_system.cpp is the SERVER's soundscape system and left the
	// server in Wave 1. The client's half is c_soundscape.cpp, and this is the
	// assertion that would have been unaskable before the flattening.
	EXPECT_FALSE( Names( list, "soundscape_system.cpp" ) )
		<< "soundscape_system.cpp is in the client list. It is the SERVER's "
		<< "soundscape system; the client's half is c_soundscape.cpp.";
}
