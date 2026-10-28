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

// WHAT EACH SIDE COMPILES is the target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt): server_sourcesharp and
// client_sourcesharp. Until Phase 6b this read server_csharp.vpc and
// client_csharp.vpc. The readers throw on a missing, empty or stale input, so
// an empty list can no longer answer "not named" for free; each absence below
// is still paired with a proof-of-read, the side's own shell entry point.
typedef std::set< std::string > SourceList_t;

const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";
SourceList_t ServerList()
{
	return cmake_graph::CompiledSources( cmake_graph::kServer );
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
//-----------------------------------------------------------------------------
// env_wind: the dead call went, and the fact that made it dead is asserted
// where it lives -- in somebody else's file.
//-----------------------------------------------------------------------------
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
//-----------------------------------------------------------------------------
// areaportal: the records are managed (plan_server_native_v2 T4d), and the SDK
// class the bodies used to belong to has no instances left.
//
// Until T4d this was two facts over shell_areaportal.cpp and areaportal_list.h
// (the list defined once, joined and left by the record, the viewer padding in
// the backface test). Those files are gone; AreaPortalsTests holds the joins,
// the padding and the walk. What stays here is the server's side of it: the
// shim TU is not compiled, the visibility pass reaches the records through the
// managed walk and names no SDK areaportal header, and the stock files are out.
//-----------------------------------------------------------------------------
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
