//========= SourceSharp — the player's hulls, declared once in C# ============//
//
// plan_shim.md §5 3e. The nine vectors behind shareddefs.h's VEC_HULL_MIN,
// VEC_DUCK_VIEW and their seven neighbours are declared in
// SourceSharp.Shared's Movement.Current and pushed across in
// SourceSharpMovementParams; shell_gamerules.cpp's
// SourceSharp_PublishViewVectors writes them into the CViewVectors table that
// g_pGameRules->GetViewVectors() answers with.
//
// WHAT THIS FILE PINS, AND WHY EACH FACT CANNOT BE A BEHAVIOUR TEST.
//
// 1. THE TWO TABLES AGREE. The native table is still nine literals, because a
//    read BEFORE the managed side has registered has to answer something and
//    the honest something is stock's hull rather than a player with no
//    collision box. That makes it a second copy of numbers that must match, and
//    a second copy nobody compares is how this tree has twice found a default
//    quietly disagreeing with the thing it is a default for. Nothing observable
//    goes wrong when they disagree: the game plays with one hull until managed
//    starts and a different one afterwards, and the change is a few units.
//
// 2. THE PUBLISH REFUSES A DEGENERATE HULL AND SAYS SO. An older managed build,
//    or a mod that fills MovementParams field by field and misses these,
//    registers zeroes. Publishing zeroes gives every player a box of no size --
//    they fall through the world and nothing reports an error. This is the
//    silent-loss shape, so the refusal is a fact rather than a comment.
//
// 3. THE WRITE REACHES ALL NINE. A publish that set eight of the nine would
//    leave one field at stock's value for ever, and no test that plays the game
//    could see which. So every field name is counted on both sides of the
//    assignment.
//
// SOURCE SCANS because there is no CViewVectors, no g_pGameRules and no managed
// runtime in a gtest binary: the table is a file-scope static in a translation
// unit that needs the whole engine to link. What can go wrong here is an EDIT
// -- somebody changing one table and not the other -- and an edit is exactly
// what a source scan sees.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cctype>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

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

std::string Read( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( !pRoot )
		return std::string();

	std::ifstream in( ( std::string( pRoot ) + "/" + pRelative ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// Whitespace squashed so a reformat cannot move a needle. Comments are NOT
// stripped here, because two of the three needles below are numbers that also
// appear in the prose beside them -- so each scan narrows to a REGION first
// (the table's own parentheses, the initialiser's own braces) and only then
// looks inside it.
std::string Squash( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		if ( !isspace( (unsigned char)s[ i ] ) )
			out.push_back( s[ i ] );
	}
	return out;
}

// The text between the first `pOpen` at or after `pAfter` and the first
// `pClose` after that. Empty when any of the three is missing, which every
// caller asserts on rather than scanning an empty string and passing.
std::string Region( const std::string &text, const char *pAfter,
                    const char *pOpen, const char *pClose )
{
	const size_t a = text.find( pAfter );
	if ( a == std::string::npos )
		return std::string();

	const size_t o = text.find( pOpen, a );
	if ( o == std::string::npos )
		return std::string();

	const size_t c = text.find( pClose, o );
	if ( c == std::string::npos )
		return std::string();

	return text.substr( o, c - o );
}

// Every float literal in a region, in order, as text -- "-16", "0", "64".
// Trailing 'f' dropped so C# and C++ spellings of one number compare equal.
std::vector< std::string > Numbers( const std::string &region )
{
	std::vector< std::string > out;
	size_t i = 0;
	while ( i < region.size() )
	{
		const bool bSign = ( region[ i ] == '-' ) && i + 1 < region.size()
		                   && isdigit( (unsigned char)region[ i + 1 ] );
		if ( !bSign && !isdigit( (unsigned char)region[ i ] ) )
		{
			i++;
			continue;
		}

		size_t j = bSign ? i + 1 : i;
		while ( j < region.size()
		        && ( isdigit( (unsigned char)region[ j ] ) || region[ j ] == '.' ) )
		{
			j++;
		}

		std::string n = region.substr( i, j - i );
		// "16.0" and "16" are the same number; normalise a trailing ".0" away
		// so the two languages' spellings compare.
		while ( n.find( '.' ) != std::string::npos
		        && ( n[ n.size() - 1 ] == '0' || n[ n.size() - 1 ] == '.' ) )
		{
			const bool bDot = ( n[ n.size() - 1 ] == '.' );
			n.erase( n.size() - 1 );
			if ( bDot )
				break;
		}
		out.push_back( n );

		if ( j < region.size() && ( region[ j ] == 'f' || region[ j ] == 'F' ) )
			j++;
		i = j;
	}
	return out;
}

const std::string &Rules()
{
	static const std::string s = Read( "sourcesharp/game/shared/shell_gamerules.cpp" );
	return s;
}

const std::string &Managed()
{
	static const std::string s = Read( "sourcesharp/managed/SourceSharp.Shared/Movement.cs" );
	return s;
}

const std::string &Header()
{
	static const std::string s = Read( "sourcesharp/host/sourcesharp.h" );
	return s;
}

const std::string &AbiParams()
{
	static const std::string s = Read( "sourcesharp/managed/SourceSharp.Abi/MovementBlobs.cs" );
	return s;
}

bool Has( const std::string &haystack, const char *needle )
{
	return Squash( haystack ).find( Squash( needle ) ) != std::string::npos;
}

} // namespace

//=============================================================================
// FACT 1 — the two tables hold the same twenty-seven numbers, in order.
//=============================================================================

TEST( GameRulesViewVectors, BothFilesReallyOpened )
{
	ASSERT_FALSE( Rules().empty() ) << "shell_gamerules.cpp did not open";
	ASSERT_FALSE( Managed().empty() ) << "SourceSharp.Shared/Movement.cs did not open";
	ASSERT_FALSE( Header().empty() ) << "sourcesharp.h did not open";
	ASSERT_FALSE( AbiParams().empty() ) << "SourceSharp.Abi/MovementBlobs.cs did not open";
}

TEST( GameRulesViewVectors, TheNativeDefaultIsStockAndIsNineVectors )
{
	const std::string table = Region( Rules(), "g_DefaultViewVectors(", "(", ");" );
	ASSERT_FALSE( table.empty() )
		<< "the g_DefaultViewVectors initialiser could not be found. It is the "
		   "value a native VEC_HULL_MIN read answers with before the managed "
		   "side has registered.";

	const std::vector< std::string > n = Numbers( table );
	ASSERT_EQ( 27u, n.size() )
		<< "the native table is no longer nine three-component vectors.";

	static const char *const kStock[ 27 ] =
	{
		"0", "0", "64",          // VEC_VIEW
		"-16", "-16", "0",       // VEC_HULL_MIN
		"16", "16", "72",        // VEC_HULL_MAX
		"-16", "-16", "0",       // VEC_DUCK_HULL_MIN
		"16", "16", "36",        // VEC_DUCK_HULL_MAX
		"0", "0", "28",          // VEC_DUCK_VIEW
		"-10", "-10", "-10",     // VEC_OBS_HULL_MIN
		"10", "10", "10",        // VEC_OBS_HULL_MAX
		"0", "0", "14",          // VEC_DEAD_VIEWHEIGHT
	};

	for ( int i = 0; i < 27; i++ )
	{
		EXPECT_EQ( std::string( kStock[ i ] ), n[ i ] )
			<< "native default component " << i << " is no longer stock's. This "
			   "is what a read before managed registration gets, so a change "
			   "here is a change to how the game plays for part of every level "
			   "load.";
	}
}

TEST( GameRulesViewVectors, TheManagedDeclarationMatchesTheNativeDefaultExactly )
{
	// The managed initialiser's hull half, from the first hull field to the end
	// of the object initialiser. Narrowed to the region so the prose above it --
	// which names 32, 72, 64 and 28 while explaining what they are -- cannot
	// satisfy the scan.
	const std::string managed = Region( Managed(), "View        = new Vector(",
	                                    "new Vector(", "};" );
	ASSERT_FALSE( managed.empty() )
		<< "Movement.Current's hull block could not be found in Movement.cs.";

	const std::vector< std::string > m = Numbers( managed );
	ASSERT_EQ( 27u, m.size() )
		<< "Movement.Current no longer declares exactly nine hull vectors. Found "
		<< m.size() << " numbers in the block.";

	const std::string table = Region( Rules(), "g_DefaultViewVectors(", "(", ");" );
	ASSERT_FALSE( table.empty() );
	const std::vector< std::string > n = Numbers( table );
	ASSERT_EQ( 27u, n.size() );

	for ( int i = 0; i < 27; i++ )
	{
		EXPECT_EQ( n[ i ], m[ i ] )
			<< "component " << i << ": shell_gamerules.cpp's pre-registration "
			   "default and SourceSharp.Shared's declaration disagree. One of "
			   "them was edited and the other was not. The game then plays with "
			   "one hull until the managed side starts and a different one "
			   "afterwards, and nothing anywhere says so.";
	}
}

//=============================================================================
// FACT 2 — the publish refuses a hull with no volume, loudly.
//=============================================================================

TEST( GameRulesViewVectors, ADegenerateStandingHullIsRefusedRatherThanPublished )
{
	ASSERT_FALSE( Rules().empty() );

	EXPECT_TRUE( Has( Rules(), "SourceSharpHullIsDegenerate(pParams)" ) )
		<< "SourceSharp_PublishViewVectors no longer tests the hull it is about "
		   "to publish. A managed build that registers zeroes then gives every "
		   "player a collision box of no size: they fall through the world, "
		   "every trace answers nothing, and no error is printed anywhere.";

	// The test itself, so a guard that is present but can never fire is caught.
	EXPECT_TRUE( Has( Rules(), "p->vecHullMax[2]<=p->vecHullMin[2]" ) );
	EXPECT_TRUE( Has( Rules(), "p->vecHullMax[0]<=p->vecHullMin[0]" ) );
	EXPECT_TRUE( Has( Rules(), "p->vecHullMax[1]<=p->vecHullMin[1]" ) );

	// And that the refusal is a refusal: it warns and RETURNS, leaving the
	// stock table in place, rather than warning and publishing anyway.
	const std::string body = Region( Rules(), "SourceSharpHullIsDegenerate( pParams )",
	                                 "{", "g_DefaultViewVectors.m_vView" );
	ASSERT_FALSE( body.empty() );
	EXPECT_NE( Squash( body ).find( "return;" ), std::string::npos )
		<< "the degenerate-hull arm warns and then publishes the degenerate hull "
		   "anyway, which is a warning nobody can act on attached to the failure "
		   "it was supposed to prevent.";
	EXPECT_NE( Squash( body ).find( "Warning(" ), std::string::npos )
		<< "the degenerate-hull arm is silent. A refusal nobody can see is the "
		   "same as no refusal.";
}

//=============================================================================
// FACT 3 — all nine fields are written, and all nine exist on both sides.
//=============================================================================

TEST( GameRulesViewVectors, EveryOneOfTheNineIsPublished )
{
	ASSERT_FALSE( Rules().empty() );

	static const char *const kPairs[ 9 ][ 2 ] =
	{
		{ "m_vView",           "vecView" },
		{ "m_vHullMin",        "vecHullMin" },
		{ "m_vHullMax",        "vecHullMax" },
		{ "m_vDuckHullMin",    "vecDuckHullMin" },
		{ "m_vDuckHullMax",    "vecDuckHullMax" },
		{ "m_vDuckView",       "vecDuckView" },
		{ "m_vObsHullMin",     "vecObsHullMin" },
		{ "m_vObsHullMax",     "vecObsHullMax" },
		{ "m_vDeadViewHeight", "vecDeadViewHeight" },
	};

	for ( int i = 0; i < 9; i++ )
	{
		const std::string assign = std::string( "g_DefaultViewVectors." ) + kPairs[ i ][ 0 ]
		                         + "=SourceSharpVec3(pParams->" + kPairs[ i ][ 1 ] + ")";
		EXPECT_TRUE( Has( Rules(), assign.c_str() ) )
			<< kPairs[ i ][ 0 ] << " is not published from " << kPairs[ i ][ 1 ]
			<< ". A publish that writes eight of the nine leaves one field at "
			   "stock's value for ever, and nothing that plays the game can tell "
			   "which one.";
	}
}

TEST( GameRulesViewVectors, TheNineFieldsExistOnBothSidesOfTheAbi )
{
	ASSERT_FALSE( Header().empty() );
	ASSERT_FALSE( AbiParams().empty() );

	static const char *const kC[ 9 ] =
	{
		"float vecView[3];", "float vecHullMin[3];", "float vecHullMax[3];",
		"float vecDuckHullMin[3];", "float vecDuckHullMax[3];",
		"float vecDuckView[3];", "float vecObsHullMin[3];",
		"float vecObsHullMax[3];", "float vecDeadViewHeight[3];",
	};
	static const char *const kCs[ 9 ] =
	{
		"public float ViewX, ViewY, ViewZ;",
		"public float HullMinX, HullMinY, HullMinZ;",
		"public float HullMaxX, HullMaxY, HullMaxZ;",
		"public float DuckHullMinX, DuckHullMinY, DuckHullMinZ;",
		"public float DuckHullMaxX, DuckHullMaxY, DuckHullMaxZ;",
		"public float DuckViewX, DuckViewY, DuckViewZ;",
		"public float ObsHullMinX, ObsHullMinY, ObsHullMinZ;",
		"public float ObsHullMaxX, ObsHullMaxY, ObsHullMaxZ;",
		"public float DeadViewHeightX, DeadViewHeightY, DeadViewHeightZ;",
	};

	for ( int i = 0; i < 9; i++ )
	{
		EXPECT_TRUE( Has( Header(), kC[ i ] ) )
			<< kC[ i ] << " is not in SourceSharpMovementParams.";
		EXPECT_TRUE( Has( AbiParams(), kCs[ i ] ) )
			<< kCs[ i ] << " is not in MovementParams. The two structs are "
			   "blittable copies of one another and a field on one side only is "
			   "a struct that reads garbage from the field after it.";
	}
}
