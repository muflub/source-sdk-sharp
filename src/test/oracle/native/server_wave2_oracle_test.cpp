//====== SourceSharp — ORACLE: the client event ids are upstream's ==========//
//
// Moved out of server_wave2_lifts_test.cpp. precache_model.cpp carries five
// animation-event ids (CL_EVENT_SOUND, the four footsteps) that were folded in
// from game/shared/ModelSoundsCache.cpp; they are baked into the MODELS, so
// each must still be upstream's number. Upstream's file is read from the
// pristine Valve SDK clone (valve_sdk.h): neither DLL compiles it any more.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "valve_sdk.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[i] ) + "/game";
		const std::string pub = std::string( kCandidates[i] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) && stat( pub.c_str(), &st ) == 0 &&
			 S_ISDIR( st.st_mode ) )
		{
			return kCandidates[i];
		}
	}
	return NULL;
}

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ), std::istreambuf_iterator<char>() );
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[i] == '*' && strIn[i + 1] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '/' )
		{
			while ( i < strIn.size() && strIn[i] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[i];
	}
	return out;
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[i];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

// One of our files (the farm, cwd) when bStock is false; upstream's (the
// pristine Valve SDK, valve_sdk.h) when it is true.
std::string Tight( const char *pszRel, const char *pszAnchor, bool bStock = false )
{
	std::string raw;
	if ( bStock )
	{
		raw = ReadValve( pszRel );
	}
	else
	{
		const char *pszRoot = SrcRoot();
		EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
		if ( !pszRoot )
			return std::string();
		raw = ReadRaw( std::string( pszRoot ) + "/" + pszRel );
	}
	EXPECT_FALSE( raw.empty() ) << "could not open " << pszRel << " -- every invariant about it would pass vacuously";

	const std::string tight = NoWhitespace( StripComments( raw ) );
	EXPECT_NE( tight.find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " no longer contains its anchor \"" << pszAnchor
		<< "\" outside of comments; this test is reasoning about a file that "
		<< "has been rewritten.";
	return tight;
}

} // namespace

TEST( ServerWave2, TheClientEventIdsCameAcrossUnchanged )
{
	// plan_server_novalve.md P3: the model sound walk is C#'s now
	// (SourceSharp.Server.ModelComponents), so the ids are held THERE against
	// stock's ModelSoundsCache.cpp. Animation-event ids are baked into the
	// MODELS, with a stock comment saying they must match cl_animevent.h in the
	// client dll; nothing checks that at build time, so a transcription slip
	// is a footstep that stops making a sound.
	const std::string cs = Tight( "sourcesharp/managed/SourceSharp.Server/ModelComponents.cs", "ClEventSound" );
	const std::string stock = Tight( "src/game/shared/ModelSoundsCache.cpp", "CL_EVENT_SOUND", true );
	ASSERT_FALSE( cs.empty() );
	ASSERT_FALSE( stock.empty() );

	static const struct
	{
		const char *pszCs;
		const char *pszStock;
	} kIds[] = {
		{ "publicconstintClEventSound=5004;", "#defineCL_EVENT_SOUND5004" },
		{ "publicconstintClEventFootstepLeft=6004;", "#defineCL_EVENT_FOOTSTEP_LEFT6004" },
		{ "publicconstintClEventFootstepRight=6005;", "#defineCL_EVENT_FOOTSTEP_RIGHT6005" },
	};

	for ( size_t i = 0; i < sizeof( kIds ) / sizeof( kIds[0] ); i++ )
	{
		EXPECT_NE( cs.find( kIds[i].pszCs ), std::string::npos )
			<< "ModelComponents.cs does not carry " << kIds[i].pszCs;
		EXPECT_NE( stock.find( kIds[i].pszStock ), std::string::npos )
			<< "game/shared/ModelSoundsCache.cpp no longer carries " << kIds[i].pszStock
			<< " -- the C# copy is now out of step with the file it came from.";
	}
}
