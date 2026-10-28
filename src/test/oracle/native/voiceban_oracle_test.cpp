//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE VOICE BAN ORACLE: stock's CVoiceBanMgr (game/shared/voice_banmgr.cpp,
// PRISTINE) run over an in-memory voice_ban.dt (voiceban_stub/filesystem.h),
// through a scripted sequence of bans, unbans, loads and saves, and written
// down as the golden src/test/client/Fixtures/voiceban-stock.tsv: the bytes
// stock writes after each step and its answer for each id. The managed ban
// list (SourceSharp.Client's VoiceBanList) replays the same script against
// the golden (VoiceBanOracleTests), so a ban list written by either reads in
// the other.
//
// THE SUBJECT IS INCLUDED, so `filesystem` is the stand-in.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. Regenerating it to make this test
// green deletes the check: touch voiceban_golden.regen beside this file, run,
// delete the marker, and say why in the commit.
//
//=============================================================================

#include <fstream>
#include <sstream>
#include <string>
#include <cstdlib>
#include <cstring>

#include "gtest/gtest.h"

#include <const.h>
#include "voice_banmgr.cpp"

COracleFileSystem g_OracleFileSystem;
COracleFileSystem *filesystem = &g_OracleFileSystem;

namespace
{

// A 32-byte id from text, zero padded, as player_info_t's guid holds it.
struct Id_t
{
	char bytes[SIGNED_GUID_LEN];

	explicit Id_t( const char *psz )
	{
		memset( bytes, 0, sizeof( bytes ) );
		strncpy( bytes, psz, sizeof( bytes ) );
	}
};

// Same byte sum as kA ("...1001" against "...1010"): the same hash bucket.
const char *kIds[] = { "STEAM_1:0:1001", "STEAM_1:0:1002", "STEAM_1:0:1010", "STEAM_1:1:77", "BOT" };

std::string Hex( const std::string &s )
{
	static const char kDigits[] = "0123456789abcdef";
	std::string out;
	for ( unsigned char c : s )
	{
		out += kDigits[c >> 4];
		out += kDigits[c & 15];
	}
	return out;
}

void Ban( CVoiceBanMgr &m, int i, bool b )
{
	Id_t id( kIds[i] );
	m.SetPlayerBan( id.bytes, b );
}

void Query( std::string &log, const char *pszStep, CVoiceBanMgr &m )
{
	for ( size_t i = 0; i < sizeof( kIds ) / sizeof( kIds[0] ); i++ )
	{
		Id_t id( kIds[i] );
		log += std::string( "ban\t" ) + pszStep + "\t" + kIds[i] + "\t" + ( m.GetPlayerBan( id.bytes ) ? "1" : "0" ) +
			"\n";
	}
}

void Save( std::string &log, const char *pszStep, CVoiceBanMgr &m )
{
	m.SaveState( "unused" );
	log +=
		std::string( "save\t" ) + pszStep + "\t" + filesystem->m_strLastName + "\t" + Hex( filesystem->m_Data ) + "\n";
}

void Load( CVoiceBanMgr &m, const std::string &data, bool bExists = true )
{
	filesystem->m_Data = data;
	filesystem->m_bExists = bExists;
	m.Init( "unused" );
}

std::string Version( int n )
{
	return std::string( (const char *)&n, sizeof( n ) );
}

std::string RunScenario()
{
	std::string log;

	// 1. From no file: bans, an unban, a repeat, a re-ban; two ids share a bucket.
	CVoiceBanMgr m1;
	Load( m1, "", false );
	Ban( m1, 0, true );
	Ban( m1, 1, true );
	Ban( m1, 2, true );
	Ban( m1, 1, false );
	Ban( m1, 3, true );
	Ban( m1, 1, true );
	Ban( m1, 0, true );
	Query( log, "fresh", m1 );
	Save( log, "fresh", m1 );
	const std::string saved = filesystem->m_Data;

	// 2. Loaded back: one more ban, one unban.
	CVoiceBanMgr m2;
	Load( m2, saved );
	Query( log, "loaded", m2 );
	Ban( m2, 4, true );
	Ban( m2, 0, false );
	Query( log, "edited", m2 );
	Save( log, "edited", m2 );

	// 3. Another version: nothing is read.
	CVoiceBanMgr m3;
	Load( m3, Version( 2 ) + saved.substr( 4 ) );
	Query( log, "version2", m3 );
	Save( log, "version2", m3 );

	// 4. A partial id at the end: the whole ids are read, the rest is not.
	CVoiceBanMgr m4;
	Load( m4, saved + std::string( 10, 'x' ) );
	Query( log, "partial", m4 );
	Save( log, "partial", m4 );

	// 5. The version alone.
	CVoiceBanMgr m5;
	Load( m5, Version( 1 ) );
	Query( log, "versiononly", m5 );
	Save( log, "versiononly", m5 );

	return log;
}

std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + pszRel;
}

} // namespace

TEST( VoiceBanOracle, StockProducesTheGolden )
{
	const std::string run = RunScenario();
	const std::string golden = RepoPath( "/src/test/client/Fixtures/voiceban-stock.tsv" );

	if ( std::ifstream( RepoPath( "/src/test/oracle/native/voiceban_golden.regen" ) ).good() )
	{
		std::ofstream out( golden );
		out << run;
	}

	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	EXPECT_EQ( ss.str(), run )
		<< "stock's ban list no longer matches the golden. Do NOT regenerate it to make this pass; see the file header.";
}

TEST( VoiceBanOracle, TheScenarioReachesEveryArm )
{
	const std::string run = RunScenario();
	EXPECT_NE( run.find( "ban\tfresh\tSTEAM_1:0:1010\t1" ), std::string::npos ) << "the colliding id was not banned";
	EXPECT_NE( run.find( "ban\tedited\tSTEAM_1:0:1001\t0" ), std::string::npos ) << "the unban did not take";
	EXPECT_NE( run.find( "ban\tversion2\tSTEAM_1:1:77\t0" ), std::string::npos ) << "another version was read";
	EXPECT_NE( run.find( "save\tversiononly\tvoice_ban.dt\t01000000\n" ), std::string::npos )
		<< "an empty list did not save as its version alone";
}
