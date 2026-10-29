//====== SourceSharp — ORACLE: the native damage masks are upstream's =======//
//
// Moved out of gamerules_damage_test.cpp (the other two checks stay there):
// the NATIVE DEFAULTS in shell_gamerules.cpp -- the five DMG_* masks that
// answer before the managed runtime is up -- must equal CMultiplayRules's,
// mask by mask. The authority is upstream's game/shared/multiplay_gamerules.cpp,
// read from the pristine Valve SDK clone (valve_sdk.h): the farm no longer
// carries it, because neither DLL compiles it.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
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
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[i];
	}
	return NULL;
}

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream f( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( f.is_open() ) << "could not open " << pszRel;
	if ( !f.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( f ) ), std::istreambuf_iterator<char>() );
}

// Every DMG_* name in a region of text, ignoring COMMENT lines.
//
// Comments are skipped rather than read, and that is not tidiness. The first
// version of this read them, so a field whose comment listed the right flags
// would have passed while its code listed the wrong ones -- and it also read to
// the next `;`, which inside a struct initialiser is the end of the WHOLE
// struct, so every field reported every flag in the table and three of the four
// comparisons were quietly against the same over-wide set.
std::set<std::string> FlagsIn( const std::string &body )
{
	std::string code;
	size_t line = 0;

	while ( line < body.size() )
	{
		const size_t eol = body.find( '\n', line );
		const size_t stop = ( eol == std::string::npos ? body.size() : eol );
		const std::string one = body.substr( line, stop - line );

		const size_t first = one.find_first_not_of( " \t" );
		if ( first == std::string::npos || one.compare( first, 2, "//" ) != 0 )
			code += one + "\n";

		if ( eol == std::string::npos )
			break;
		line = eol + 1;
	}

	std::set<std::string> out;
	for ( size_t i = code.find( "DMG_" ); i != std::string::npos; i = code.find( "DMG_", i + 1 ) )
	{
		size_t j = i;
		while ( j < code.size() && ( isalnum( (unsigned char)code[j] ) || code[j] == '_' ) )
			j++;
		out.insert( code.substr( i, j - i ) );
	}
	return out;
}

// One FIELD of the native table: from its `// nName` comment to the blank line
// that ends it.
std::set<std::string> FieldFlags( const std::string &hay, const std::string &anchor )
{
	const size_t at = hay.find( anchor );
	if ( at == std::string::npos )
		return std::set<std::string>();

	size_t end = hay.find( "\n\n", at );
	if ( end == std::string::npos )
		end = hay.size();

	return FlagsIn( hay.substr( at, end - at ) );
}

// One METHOD of stock: from its name to the `;` ending its single statement.
std::set<std::string> FlagsAfter( const std::string &hay, const std::string &from )
{
	const size_t at = hay.find( from );
	if ( at == std::string::npos )
		return std::set<std::string>();

	const size_t end = hay.find( '\n', hay.find( ';', at ) );
	return FlagsIn( hay.substr( at, ( end == std::string::npos ? hay.size() : end ) - at ) );
}

const char *kShell = "sourcesharp/game/shared/shell_gamerules.cpp";
const char *kStock = "src/game/shared/multiplay_gamerules.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The native defaults are stock's, mask by mask.
//-----------------------------------------------------------------------------
TEST( GameRulesDamage, TheNativeDefaultsAreStocksValues )
{
	const std::string shell = Read( kShell );
	const std::string stock = ReadValve( kStock );
	ASSERT_FALSE( shell.empty() );
	ASSERT_FALSE( stock.empty() );

	struct Case_t
	{
		const char *pszShellAnchor;
		const char *pszStockAnchor;
		const char *pszWhat;
	};

	static const Case_t kCases[] = {
		{ "// nTimeBased", "CMultiplayRules::Damage_GetTimeBased", "which damage is applied a tick at a time" },
		{ "// nShouldGibCorpse", "CMultiplayRules::Damage_GetShouldGibCorpse", "which damage can gib a corpse" },
		{ "// nShouldNotBleed", "CMultiplayRules::Damage_GetShouldNotBleed", "which damage draws no blood" },
		{ "// nShowOnHud", "CMultiplayRules::Damage_GetShowOnHud", "which damage the HUD has art for" },
	};

	for ( size_t i = 0; i < sizeof( kCases ) / sizeof( kCases[0] ); i++ )
	{
		const std::set<std::string> mine = FieldFlags( shell, kCases[i].pszShellAnchor );
		const std::set<std::string> theirs = FlagsAfter( stock, kCases[i].pszStockAnchor );

		// Two empty sets compare equal, so refuse them.
		ASSERT_FALSE( theirs.empty() ) << "found no DMG_* flags after " << kCases[i].pszStockAnchor << " in " << kStock
									   << " (the pristine Valve SDK). The oracle's authority "
									   << "is upstream's text: its parse, not this port, is what changed.";
		ASSERT_FALSE( mine.empty() ) << "found no DMG_* flags after " << kCases[i].pszShellAnchor << " in " << kShell;

		EXPECT_EQ( mine, theirs ) << kCases[i].pszShellAnchor << " disagrees with stock about " << kCases[i].pszWhat
								  << ". The native table is what answers before "
								  << "the managed runtime is up.";
	}
}
