//========= SourceSharp — lane shards-b's client arms, read as source =========//
//
// Phase 13 makes `CBaseEntity` a typedef for `IGameEntity`. This lane closed
// twenty client translation units from the step-6 dispatch manifest -- EIGHTEEN
// of which are still in the build, two having been evicted by plan_entity.md
// step 7g -- and its edits take two shapes only:
//
//   * a PLAYER ASK REROUTED to IMovementPlayer. `player->index`,
//     `entindex()`, `IsAlive()`, `GetMoveType()` become `MvEntIndex()`,
//     `MvIsAlive()`, `MvGetMoveType()`. No guard — IMovementPlayer is a base
//     CBasePlayer declares itself and it survives the alias — so an unarmed
//     build would not notice one of these being reverted;
//   * a GUARD round something that is CBaseEntity's own.
//
// A source-reading test because the armed survey is run by hand and these
// files are not. Between surveys an `#endif` can go to a merge splice (this
// tree has been bitten by exactly that), a guard's `#else` can vanish and
// still compile unarmed while deleting the SDK text from the shipping build.
//
// Same scanner shape as cpp_alias_t6_test.cpp, which does this for shard T6.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cstddef>
#include <fstream>
#include <string>
#include <vector>

namespace
{

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

//-----------------------------------------------------------------------------
// Comments and string literals both go before any match. Every guard in this
// lane explains itself, and the explanations NAME the member that went — the
// comment above the MapOverview team guard said "GetTeamNumber", which is
// exactly the kind of string a "the member is gone" check looks for.
//-----------------------------------------------------------------------------
std::string StripCommentsAndStrings( const std::string &strIn )
{
	std::string out;
	const size_t n = strIn.size();
	size_t i = 0;
	while ( i < n )
	{
		if ( i + 1 < n && strIn[i] == '/' && strIn[i + 1] == '*' )
		{
			const size_t j = strIn.find( "*/", i + 2 );
			i = ( j == std::string::npos ) ? n : j + 2;
			continue;
		}
		if ( i + 1 < n && strIn[i] == '/' && strIn[i + 1] == '/' )
		{
			while ( i < n && strIn[i] != '\n' )
				i++;
			continue;
		}
		if ( strIn[i] == '"' )
		{
			size_t j = i + 1;
			while ( j < n && strIn[j] != '"' )
			{
				if ( strIn[j] == '\\' )
					j++;
				j++;
			}
			i = ( j < n ) ? j + 1 : n;
			continue;
		}
		out += strIn[i];
		i++;
	}
	return out;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// One alias-conditional region, split into arms. This lane wrote `#ifndef`
// only, so the SDK arm is always first; the shape is still read off the line
// rather than assumed, so an `#ifdef` appearing here later is not silently
// read backwards.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;
	bool bArmedFirst;
	bool bHasElse;
	bool bClosed;
	std::vector<std::string> unarmed;
	std::vector<std::string> armed;

	Guard_t()
		: nLine( 0 )
		, bArmedFirst( false )
		, bHasElse( false )
		, bClosed( false )
	{
	}

	bool ArmedHasCode() const { return HasCode( armed ); }

	bool UnarmedHasCode() const { return HasCode( unarmed ); }

	std::string ArmedText() const { return Joined( armed ); }

private:
	static bool IsBlankOrComment( const std::string &L )
	{
		const std::string t = Trimmed( L );
		return t.empty() || ( t.size() >= 2 && t.compare( 0, 2, "//" ) == 0 );
	}

	static bool HasCode( const std::vector<std::string> &v )
	{
		for ( size_t i = 0; i < v.size(); i++ )
			if ( !IsBlankOrComment( v[i] ) )
				return true;
		return false;
	}

	static std::string Joined( const std::vector<std::string> &v )
	{
		std::string s;
		for ( size_t i = 0; i < v.size(); i++ )
			s += v[i] + "\n";
		return StripCommentsAndStrings( s );
	}
};

bool IsGuardOpen( const std::string &strLine, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t.empty() || t[0] != '#' )
		return false;
	if ( !Contains( t, "SS_CBASEENTITY_ALIAS" ) )
		return false;
	if ( t.compare( 0, 3, "#if" ) != 0 )
		return false; // an #endif comment naming the macro is not an opening

	*pbArmedFirst = !Contains( t, "ifndef" ) && !Contains( t, "!defined( SS_CBASEENTITY_ALIAS )" ) &&
		!Contains( t, "!defined(SS_CBASEENTITY_ALIAS)" );
	return true;
}

bool IsAnyConditionalOpen( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t.size() >= 3 && t.compare( 0, 3, "#if" ) == 0;
}

bool IsEndif( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t.size() >= 6 && t.compare( 0, 6, "#endif" ) == 0;
}

bool IsElse( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t == "#else" || ( t.size() >= 5 && t.compare( 0, 5, "#elif" ) == 0 );
}

std::vector<Guard_t> GuardsInLines( const std::string &strFile, const std::vector<std::string> &lines )
{
	std::vector<Guard_t> out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[i], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;
		g.bArmedFirst = bArmedFirst;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[j];
			if ( IsAnyConditionalOpen( L ) )
			{
				nDepth++;
			}
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 )
				{
					g.bClosed = true;
					break;
				}
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true;
				bInElse = true;
				continue;
			}
			if ( bInElse != bArmedFirst )
				g.armed.push_back( L );
			else
				g.unarmed.push_back( L );
		}
		out.push_back( g );
		i = j;
	}
	return out;
}

} // namespace

//-----------------------------------------------------------------------------
// THE RESIDUE WAVE'S TWO ZEROED ROWS LEFT WITH THEIR FILES. The successor
// property (the dead arms' bodies gone whole, the code around them untouched)
// was held here over vgui_fpspanel.cpp's cl_showpos arms until that file left
// (plan_client_novalve C1, 2026-10-05) and over movehelper_client.cpp's
// ProcessImpacts touch pass until it left in C6 (lane c6-input, 2026-10-11:
// stock's client move helper is ClientPredictionMoveWorld). With no file left
// to read, the fact went with its subject.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE SAME CLAIM FOR THE TWO SHIM FILES `the-close` 1/N (f288a5e11) CLEARED.
//
// A separate fact from the `residue` one above rather than two more rows in its
// table, because the two waves are two events -- `residue` owned src/game and
// src/public, this one owned src/sourcesharp/** -- and one fact covering both
// could not say which wave a regression came from.
//
// AND THE ROWS HERE CARRY A THIRD COLUMN THE RESIDUE TABLE DOES NOT: the LIVE
// SUCCESSOR, the thing that answers the question the deleted arm was asking.
// Four of these five arms sat behind `TraceEntity( *trace )` or
// `SDKEntityFromUnknown`, both of which resolve through
// IClientUnknown::GetBaseEntity() -- `mov $0x0,%eax; ret` in client.so, all
// four implementations -- so they were not merely uncompiled, they were
// unreachable, and `the-close` 4/N has since deleted TraceEntity itself. For
// three of the five the successor is a DECLARED ANSWER rather than a re-point,
// and saying which is which is the whole value of the column: a fact that only
// said "it is gone" would read the same for a question re-routed and a question
// abandoned.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The scanner's own check that it can fail: a guard built in memory, so that
// "no armed arm names a deleted member" is not passing because the arms are
// always empty.
//-----------------------------------------------------------------------------
TEST( CppAliasShardsBScanner, TheArmsAreAssignedRoundTheRightWay )
{
	std::vector<std::string> lines;
	lines.push_back( "#ifndef SS_CBASEENTITY_ALIAS" );
	lines.push_back( "\tint n = pEnt->entindex();" );
	lines.push_back( "#else" );
	lines.push_back( "\tint n = pEnt->MvEntIndex();" );
	lines.push_back( "#endif" );

	const std::vector<Guard_t> guards = GuardsInLines( "<memory>", lines );
	ASSERT_EQ( (size_t)1, guards.size() );
	EXPECT_TRUE( guards[0].bClosed );
	EXPECT_TRUE( guards[0].bHasElse );
	EXPECT_FALSE( guards[0].bArmedFirst );
	EXPECT_TRUE( guards[0].UnarmedHasCode() );
	EXPECT_TRUE( guards[0].ArmedHasCode() );
	EXPECT_TRUE( Contains( guards[0].ArmedText(), "MvEntIndex()" ) );
	EXPECT_FALSE( Contains( guards[0].ArmedText(), "pEnt->entindex()" ) );

	// A comments-only armed arm must read as carrying no code, or the
	// "an armed arm must carry code" check above cannot fail.
	std::vector<std::string> empty;
	empty.push_back( "#ifndef SS_CBASEENTITY_ALIAS" );
	empty.push_back( "\tDoTheThing();" );
	empty.push_back( "#else" );
	empty.push_back( "\t// nothing to do armed" );
	empty.push_back( "#endif" );
	const std::vector<Guard_t> g2 = GuardsInLines( "<memory>", empty );
	ASSERT_EQ( (size_t)1, g2.size() );
	EXPECT_TRUE( g2[0].bHasElse );
	EXPECT_FALSE( g2[0].ArmedHasCode() );
}
