//======= SourceSharp — CSoundEnt is a GAME SYSTEM, and ai_sound is C#'s ======//
//
// game/server/soundent.cpp used to hold two entities. Neither is one now, and
// the two went for different reasons:
//
//   CSoundEnt   DE-ENTIFIED. It is the world's record of recent noise — a
//               singleton list — and being a CPointEntity bought it a Think
//               slot and a row in the entity list and nothing else. Every one
//               of its callers in this build asks a STATIC of it. It is a
//               CAutoGameSystemPerFrame now, which is where the Think slot
//               comes from instead.
//
//   CAISound    DELETED. The map-facing half, `ai_sound`, moved to C#
//               (SourceSharp.Server/Entities/Env/AiSound.cs) and the C++
//               registration had been inside `#ifndef SOURCESHARP` for
//               batches. The class had no caller and no registration left.
//
// WHY THIS IS A TEST AND NOT A COMMENT. The de-entification rests on facts
// about files other people change: that no map spawns a `soundent`, that
// nothing outside soundent.cpp constructs one, and that the six statics its
// callers name are still spelled the way those callers spell them. A rename or
// a re-registration would put a second factory back in the tree with nothing
// anywhere to say so. Red here is not automatically a regression — read the
// message and re-take the verdict.
//
// SAME STRIPPER RULE as bmodels_verdict_test.cpp and gib_verdict_test.cpp:
// this file's own prose names LINK_ENTITY_TO_CLASS and CPointEntity in the
// paragraphs explaining them, and so does soundent.cpp's, so every scan reads
// the file with comments REMOVED or it reads an explanation as a violation.
// (Moot since P6: game/server/soundent.h left the farm, the text scans retired
// with it, and the stripper went with its last caller.)
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, the same three-candidate way the other verdict tests do:
// the tests run with src/ as the working directory, but nothing asserts that.
//-----------------------------------------------------------------------------
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

const char *MapDir()
{
	static const char *kCandidates[] = { "../game/mod_sharp/maps",
	                                     "game/mod_sharp/maps",
	                                     "../../game/mod_sharp/maps" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		if ( stat( kCandidates[ i ], &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
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

//-----------------------------------------------------------------------------
// The entity lump of a compiled .bsp, which is plain text. Same reader as
// bmodels_verdict_test.cpp: header 'VBSP', int version, then 64 lump_t of
// { fileofs, filelen, version, fourCC }; lump 0 is the entities.
//-----------------------------------------------------------------------------
int ReadInt( const std::string &s, size_t at )
{
	if ( at + 4 > s.size() )
		return -1;
	int v = 0;
	memcpy( &v, s.data() + at, 4 );
	return v;
}

std::string EntityLump( const std::string &strMapPath )
{
	const std::string data = ReadRaw( strMapPath );
	if ( data.size() < 8 + 64 * 16 || data.compare( 0, 4, "VBSP" ) != 0 )
		return std::string();

	const int ofs = ReadInt( data, 8 );
	const int len = ReadInt( data, 12 );
	if ( ofs < 0 || len < 0 || (size_t)( ofs + len ) > data.size() )
		return std::string();

	return data.substr( ofs, len );
}

int CountClassname( const std::string &strLump, const char *pszClassName )
{
	const std::string needle = std::string( "\"classname\" \"" ) + pszClassName + "\"";
	int n = 0;
	for ( size_t at = strLump.find( needle ); at != std::string::npos;
	      at = strLump.find( needle, at + 1 ) )
	{
		n++;
	}
	return n;
}

// The stock header that declared CSoundEnt. It is compiled by nothing since
// plan_server_novalve.md P5 (the sound list is ss_soundent.h's managed pool,
// soundent_managed_test.cpp) and left the farm in P6.
const char *kSoundEntH   = "game/server/soundent.h";

// A file beside it that the farm still links, so "soundent.h cannot be read"
// is never answered by a reader looking in the wrong place.
const char *kSiblingH    = "game/server/activator.h";

} // namespace

//-----------------------------------------------------------------------------
// The disposition itself: a list, not an entity -- NOW AN ANCHORED ABSENCE.
//
// This read game/server/soundent.h and asserted CSoundEnt derived from
// CServerSystemPerFrame, had a FrameUpdatePreEntityThink sweep, and was on
// neither CAutoGameSystemPerFrame nor CPointEntity. The header is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6, so the
// positive halves retired with it; the absences have a stronger form, which
// is that the stock class's header cannot be reached at all.
//-----------------------------------------------------------------------------
TEST( SoundEntGameSystem, TheStockSoundEntHeaderIsOutOfTheFarm )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL ) << "no source root found from the working directory";

	ASSERT_FALSE( ReadRaw( std::string( pszRoot ) + "/" + kSiblingH ).empty() )
		<< kSiblingH << " could not be read, so the absence below would be "
		<< "answered by a reader that sees nothing";

	EXPECT_TRUE( ReadRaw( std::string( pszRoot ) + "/" + kSoundEntH ).empty() )
		<< kSoundEntH << " is readable from the farm again. It declares stock's "
		<< "entity-shaped CSoundEnt; the sound list is ss_soundent.h's managed "
		<< "pool now, and a second declaration is a second subsystem.";
}

//-----------------------------------------------------------------------------
// The map evidence the disposition rests on, read from the .bsp rather than
// from a list of names — because a list is what went wrong for func_clip_vphysics
// one file over (bmodels_verdict_test.cpp).
//-----------------------------------------------------------------------------
TEST( SoundEntGameSystem, NoTrackedMapSpawnsASoundent )
{
	const char *pszDir = MapDir();
	ASSERT_TRUE( pszDir != NULL )
		<< "no game/mod_sharp/maps directory found from the working directory; "
		<< "this test would otherwise pass by reading nothing";

	const std::string strTracked = std::string( pszDir ) + "/dm_lockdown.bsp";
	const std::string lump = EntityLump( strTracked );
	ASSERT_FALSE( lump.empty() )
		<< strTracked << " has no readable entity lump. It is a TRACKED file, "
		<< "so this is not a missing build artefact — either the map has been "
		<< "replaced or the reader below is broken, and in both cases every "
		<< "count it produces is meaningless."
		<< " (read " << ReadRaw( strTracked ).size() << " bytes, starting \""
		<< ReadRaw( strTracked ).substr( 0, 4 ) << "\")";

	// The reader, checked before it is believed. A garbage lump reports zero
	// for everything, and a zero `soundent` count would then read as good news.
	ASSERT_GE( CountClassname( lump, "worldspawn" ), 1 )
		<< "no worldspawn in " << strTracked << "'s entity lump; what this "
		<< "test read is not an entity lump";

	EXPECT_EQ( CountClassname( lump, "soundent" ), 0 )
		<< strTracked << " now places a `soundent` entity. Nothing registers "
		<< "that classname any more, so the engine will discard it with a "
		<< "warning — and if the map genuinely needs one, the list it wants is "
		<< "already up as a game system and needs no entity.";

	EXPECT_EQ( CountClassname( lump, "ai_sound" ), 0 )
		<< strTracked << " now places an `ai_sound`. That is not an error — C# "
		<< "serves the classname (SourceSharp.Server/Entities/Env/AiSound.cs) — "
		<< "but this test asserted it was absent, so the assertion is what is "
		<< "stale. Check `debug-api classes` and update the count.";
}

//-----------------------------------------------------------------------------
// TheStaticsItsCallersNameAreStillOnTheClass is RETIRED: it pinned six statics'
// spelling in game/server/soundent.h, which is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6.
//-----------------------------------------------------------------------------
