//========= SourceSharp — a guard that swallowed its own live arm ===========//
//
// lane `worldspawn`. ONE INVARIANT OVER THE WHOLE SHIM, and it is the one that
// four functions broke at once without anything noticing.
//
//     No `#ifndef SS_CBASEENTITY_ALIAS` region may contain the WHOLE of a
//     `pStandalone` arm and offer no `#else`.
//
// WHY THAT IS A RULE AND NOT A STYLE. The flip's whole meaning is that armed
// there is no SDK hierarchy: `SourceSharp_ResolveEntity` can set `pBase` only
// when the guard is OFF, and `pStandalone` is the only arm any of these
// functions can ever take in an armed build. So a region shaped
//
//     #ifndef SS_CBASEENTITY_ALIAS
//     if ( ent.pBase )        ...the SDK answer...
//     else if ( ent.pStandalone )  ...THE ONLY ANSWER THAT ARMED EXISTS...
//     #endif
//
// compiles clean, links clean, and is a SILENT NO-OP on every entity in the
// game. This file's own subject is that these were not caught by anything: the
// guard ledgers (cpp_alias_t*) count regions and this shape does not change the
// count; the link cannot see it because nothing goes undefined; and unarmed --
// which is every build anyone had run until this branch -- the code is correct.
//
// FOUR OF THEM WERE LIVE IN shell_entityapi.cpp:
//
//     SourceSharp_EntitySetHealth     wrote nothing
//     SourceSharp_EntitySetOrigin     wrote nothing
//     SourceSharp_EntityGetAngles     answered (0,0,0) for every entity
//     SourceSharp_EntitySetVelocity   the `case` LABEL was inside the guard, so
//                                     armed the switch had no such arm at all
//
// SetHealth is the one worth remembering. SsPlayer.OnSpawn's
// `Health = SpawnHealth` crosses to it, so armed it wrote nothing,
// CStandalonePlayer::GetHealth() stayed 0, and MvIsAlive() -- which is
// `GetHealth() > 0 && !deadflag` -- was false. THE PLAYER SPAWNED DEAD on every
// armed map load, and a dead player does not walk, take a weapon, push a crate,
// ride a lift or open a door: 45 live-gate checks, one line.
//
// THE CORRECT SHAPE IS THIS FILE'S OWN RULE EVERYWHERE ELSE -- guard the SDK arm
// ONLY, and leave the standalone arm outside it. SourceSharp_EntityIsPlayer and
// SourceSharp_EntityGetIndex are the shape; shell_scripted.cpp's velocity read
// is the shape with an `#else`, which is why it is not in the finding.
//
// SCANNED, NOT EXECUTED, and the limitation is stated rather than hidden: this
// is a text rule over source, so it can be fooled by a spelling nobody uses
// today. It is asserted anyway because the alternative -- an engine-free binary
// that resolves an entity -- does not exist, and because the ONE thing a scan is
// reliably good at is finding a shape that is supposed to appear zero times.
//
// ANCHORED: the scanner asserts it read a real tree and that the known-correct
// shapes are present, so a scanner that quietly read nothing is a RED test and
// never a vacuous pass.
//
//-----------------------------------------------------------------------------
// WHAT THE CLOSE DID TO THIS FILE'S SUBJECT, AND WHY THE RULE STAYS
//-----------------------------------------------------------------------------
//
// The alias retired (the-close 3/N, 0bc727ed9): public/ss_cbaseentity_alias.h is
// deleted, `CBaseEntity` is not a name in the compiled tree, and the-close 1/N
// deleted the shim's 240 dead guarded arms. There is now NOT ONE
// `#ifndef SS_CBASEENTITY_ALIAS` DIRECTIVE ANYWHERE UNDER sourcesharp/game --
// every remaining mention of the macro under that tree is prose in a comment,
// which was left alone deliberately (a blind rename destroys facts; see
// 0bc727ed9). So the construct this file convicts cannot occur today.
//
// THAT MAKES THE ORIGINAL ANCHOR A CHECK THAT CANNOT FAIL, and it was one:
// TheScannerCanSeeAGuardedRegionAtAll searched shell_entityapi.cpp for the
// SUBSTRING "#ifndef SS_CBASEENTITY_ALIAS" and went green off two COMMENTS at
// lines 2358 and 2457, in a file that carries zero preprocessor conditionals of
// any kind. It is replaced below by a DIRECTIVE-aware scan that asserts the
// absence, anchored on the 237 conditional directives the shim's .cpp files
// really do carry so a scanner that stopped reading is red.
//
// AND THE FOUR FUNCTIONS' ARMS ARE NOW THE LIVE SUCCESSOR PROPERTY. The bug was
// "the only arm an armed build can take is inside a guard". Its successor, now
// that there is no guard, is "the only arm is UNCONDITIONAL" -- no preprocessor
// conditional of ANY kind stands between `SourceSharp_EntitySetHealth`'s opening
// brace and its `ent.pStandalone->SetHealth`, and the same for the other three.
// That is asserted below and it is what actually keeps the player alive. The
// rule itself stays because it is a shape that must read zero, and a shape that
// must read zero is exactly what a scan is good for -- but it is stated as an
// absence WITH its successor rather than left to pass over an empty set.
//
//=============================================================================//

#include <cctype>
#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <dirent.h>
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
		const std::string probe = std::string( kCandidates[ i ] ) + "/sourcesharp/game";
		if ( stat( probe.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return ".";
}

std::string ReadFile( const std::string &strRelative )
{
	std::ifstream in( ( std::string( SrcRoot() ) + "/" + strRelative ).c_str() );
	if ( !in )
		return std::string();
	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// Every .cpp under sourcesharp/game, recursively, EXCEPT the test tree itself --
// a test may legitimately write the broken shape as a fixture.
void CollectShimSources( const std::string &strRelDir, std::vector< std::string > &out )
{
	DIR *pDir = opendir( ( std::string( SrcRoot() ) + "/" + strRelDir ).c_str() );
	if ( !pDir )
		return;

	while ( struct dirent *pEntry = readdir( pDir ) )
	{
		const std::string name = pEntry->d_name;
		if ( name == "." || name == ".." )
			continue;

		const std::string rel = strRelDir + "/" + name;
		struct stat st;
		if ( stat( ( std::string( SrcRoot() ) + "/" + rel ).c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
			CollectShimSources( rel, out );
		else if ( name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0 )
			out.push_back( rel );
	}
	closedir( pDir );
}

const std::vector< std::string > &ShimSources()
{
	static std::vector< std::string > s_files;
	static bool s_bDone = false;
	if ( !s_bDone )
	{
		s_bDone = true;
		CollectShimSources( "sourcesharp/game", s_files );
	}
	return s_files;
}

std::string TrimLeft( const std::string &s )
{
	size_t i = 0;
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' ) )
		i++;
	return s.substr( i );
}

bool StartsWith( const std::string &s, const char *pszPrefix )
{
	const std::string prefix = pszPrefix;
	return s.size() >= prefix.size() && s.compare( 0, prefix.size(), prefix ) == 0;
}

struct Swallowed_t
{
	std::string strFile;
	int         nOpenLine;
	std::string strArm;
};

// A region is SWALLOWING when it opens `#ifndef SS_CBASEENTITY_ALIAS`, names
// pStandalone somewhere inside, and closes on `#endif` rather than `#else`.
//
// An `#else` means the armed build was given its own answer and the region is
// deliberate, whatever it contains. That is exactly shell_scripted.cpp's
// velocity read, and it is why this rule does not convict it.
std::vector< Swallowed_t > FindSwallowedArms()
{
	std::vector< Swallowed_t > out;

	const std::vector< std::string > &files = ShimSources();
	for ( size_t f = 0; f < files.size(); f++ )
	{
		const std::string body = ReadFile( files[ f ] );
		std::istringstream in( body );
		std::string line;

		bool bInside = false;
		int  nLine = 0, nOpenLine = 0;
		std::string strArm;

		while ( std::getline( in, line ) )
		{
			nLine++;
			const std::string t = TrimLeft( line );

			if ( !bInside )
			{
				if ( StartsWith( t, "#ifndef SS_CBASEENTITY_ALIAS" ) )
				{
					bInside = true;
					nOpenLine = nLine;
					strArm.clear();
				}
				continue;
			}

			if ( StartsWith( t, "#else" ) )
			{
				bInside = false;                       // answered armed; fine
				continue;
			}

			if ( StartsWith( t, "#endif" ) )
			{
				if ( !strArm.empty() )
				{
					Swallowed_t hit;
					hit.strFile   = files[ f ];
					hit.nOpenLine = nOpenLine;
					hit.strArm    = strArm;
					out.push_back( hit );
				}
				bInside = false;
				continue;
			}

			// A nested conditional inside the region would make the `#endif`
			// bookkeeping wrong, and there are none today. Counted rather than
			// assumed: the balance fact below fails if one appears.
			if ( strArm.empty() && t.find( "pStandalone" ) != std::string::npos )
				strArm = TrimLeft( line );
		}
	}
	return out;
}

//-----------------------------------------------------------------------------
// DIRECTIVE-AWARE, which the substring search this replaced was not. A line is a
// conditional directive when its first non-blank character is '#' and the token
// after it is one of if/ifdef/ifndef/elif. A comment mentioning one is not.
//-----------------------------------------------------------------------------
bool IsConditionalDirective( const std::string &strLine, std::string *pstrRest )
{
	const std::string t = TrimLeft( strLine );
	if ( t.empty() || t[ 0 ] != '#' )
		return false;

	const std::string after = TrimLeft( t.substr( 1 ) );
	static const char *kWords[] = { "ifndef", "ifdef", "elif", "if" };
	for ( size_t i = 0; i < sizeof( kWords ) / sizeof( kWords[ 0 ] ); i++ )
	{
		const std::string w = kWords[ i ];
		if ( after.size() < w.size() || after.compare( 0, w.size(), w ) != 0 )
			continue;
		// `#ifdefined` is not `#if defined`; require a break after the word.
		const char c = after.size() > w.size() ? after[ w.size() ] : ' ';
		if ( isalnum( (unsigned char)c ) || c == '_' )
			continue;
		if ( pstrRest )
			*pstrRest = after;
		return true;
	}
	return false;
}

// Every conditional directive in the shim, with the ones naming the retired
// alias macro separated out.
void ScanShimDirectives( std::vector< std::string > *pAll, std::vector< std::string > *pAlias )
{
	const std::vector< std::string > &files = ShimSources();
	for ( size_t f = 0; f < files.size(); f++ )
	{
		std::istringstream in( ReadFile( files[ f ] ) );
		std::string line, rest;
		int nLine = 0;
		while ( std::getline( in, line ) )
		{
			nLine++;
			if ( !IsConditionalDirective( line, &rest ) )
				continue;

			const std::string strWhere =
				files[ f ] + ":" + std::to_string( nLine ) + "  " + TrimLeft( line );
			if ( pAll )
				pAll->push_back( strWhere );
			if ( pAlias && rest.find( "SS_CBASEENTITY_ALIAS" ) != std::string::npos )
				pAlias->push_back( strWhere );
		}
	}
}

// The body of a free function that starts at a line beginning with its return
// type and ends at a `}` in column zero -- which is how every function in
// shell_entityapi.cpp and shell_scripted.cpp is written.
std::vector< std::string > FunctionBody( const std::string &strFile, const char *pszName )
{
	std::vector< std::string > body;
	std::istringstream in( ReadFile( strFile ) );
	std::string line;
	bool bInside = false;
	const std::string strOpen = std::string( pszName ) + "(";

	while ( std::getline( in, line ) )
	{
		if ( !bInside )
		{
			// The DEFINITION, not a call and not a comment: column zero, and the
			// name immediately followed by its parameter list.
			if ( !line.empty() && line[ 0 ] != ' ' && line[ 0 ] != '\t' && line[ 0 ] != '/'
			     && line.find( strOpen ) != std::string::npos )
				bInside = true;
			continue;
		}

		body.push_back( line );
		if ( line == "}" )
			break;
	}
	return body;
}

} // namespace

//=============================================================================//
// THE PREMISES. A scanner that read nothing must be RED, not green.
//=============================================================================//

TEST( ArmedArmSwallowed, TheShimTreeWasReallyWalked )
{
	EXPECT_GT( ShimSources().size(), 100u )
		<< "the shim walk found almost no .cpp under sourcesharp/game; every "
		   "absence this file reports would be vacuous.";
}

TEST( ArmedArmSwallowed, TheFileTheFourLivedInIsStillThere )
{
	const std::string api = ReadFile( "sourcesharp/game/server/shell_entityapi.cpp" );
	ASSERT_FALSE( api.empty() ) << "could not read shell_entityapi.cpp";

	// The four subjects, by name. A rename would otherwise retire the fact
	// silently.
	EXPECT_NE( api.find( "SourceSharp_EntitySetHealth" ), std::string::npos );
	EXPECT_NE( api.find( "SourceSharp_EntitySetOrigin" ), std::string::npos );
	EXPECT_NE( api.find( "SourceSharp_EntityGetAngles" ), std::string::npos );
	EXPECT_NE( api.find( "SourceSharp_EntitySetVelocity" ), std::string::npos );
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This fact used to be called
// TheScannerCanSeeAGuardedRegionAtAll and asserted the OPPOSITE of what it
// asserts now, because the alias retired (0bc727ed9) and the shim's 240 dead
// guarded arms went with it (the-close 1/N).
//
// It went green on the wrong thing first, which is why it is written this way:
// it searched shell_entityapi.cpp for the SUBSTRING "#ifndef SS_CBASEENTITY_
// ALIAS" and found two COMMENTS (lines 2358 and 2457) in a file that carries
// zero preprocessor conditionals of any kind. A comment is not a guard.
//
// So: the absence is asserted DIRECTIVE-AWARE, and it is anchored on the
// conditional directives the shim really does carry -- #ifdef CLIENT_DLL,
// #ifdef GAME_DLL, #if defined( SS_COVERAGE ) -- so a scanner that has stopped
// recognising a directive at all cannot read as "none forbidden".
//-----------------------------------------------------------------------------
TEST( ArmedArmSwallowed, NoAliasGuardedDirectiveRemainsAnywhereInTheShim )
{
	std::vector< std::string > all, alias;
	ScanShimDirectives( &all, &alias );

	ASSERT_GT( all.size(), 100u )
		<< "the directive scan found almost no #if/#ifdef/#ifndef under "
		   "sourcesharp/game. Either the walk is broken or IsConditionalDirective "
		   "has stopped recognising a directive -- either way the absence below "
		   "would be vacuous.";

	std::string strReport;
	for ( size_t i = 0; i < alias.size(); i++ )
		strReport += "\n    " + alias[ i ];

	EXPECT_EQ( 0u, alias.size() )
		<< "SS_CBASEENTITY_ALIAS is DEFINED by both vpcs and selects nothing in "
		   "any compiled file (0bc727ed9). A live conditional on it in the shim "
		   "is therefore a dead arm: unarmed-only code the compiler never sees, "
		   "which is exactly the shape this whole file exists to find."
		<< strReport;
}

//=============================================================================//
// THE RULE.
//=============================================================================//

TEST( ArmedArmSwallowed, NoGuardedRegionSwallowsTheOnlyArmArmedBuildsCanTake )
{
	const std::vector< Swallowed_t > hits = FindSwallowedArms();

	std::string strReport;
	for ( size_t i = 0; i < hits.size(); i++ )
	{
		strReport += "\n    " + hits[ i ].strFile + ":"
		          + std::to_string( hits[ i ].nOpenLine ) + "  ->  "
		          + hits[ i ].strArm;
	}

	EXPECT_EQ( 0u, hits.size() )
		<< "a `#ifndef SS_CBASEENTITY_ALIAS` region contains the whole of a "
		   "pStandalone arm and has no `#else`. Armed, pStandalone is the ONLY "
		   "arm that can ever be taken, so that code is a silent no-op or a "
		   "plausible zero on every entity in the game -- it compiles, it links, "
		   "and unarmed it is correct. Guard the SDK arm only "
		   "(SourceSharp_EntityIsPlayer is the shape), or give the region an "
		   "`#else` that answers armed (shell_scripted.cpp's velocity read is "
		   "that shape)." << strReport;
}

//-----------------------------------------------------------------------------
// THE LIVE SUCCESSOR OF THE BUG, which is what actually keeps the player alive.
//
// The rule above now reads zero over an EMPTY POPULATION -- there is no
// `#ifndef SS_CBASEENTITY_ALIAS` region left in the shim to convict (the fact
// above asserts exactly that). Zero over nothing is half a fact. The other half
// is the property the four functions are supposed to have ended up with: the
// arm that armed builds take is UNCONDITIONAL. Not "guarded correctly" any
// more -- not guarded at all, by SS_CBASEENTITY_ALIAS or by anything else.
//
// So this reads each of the four bodies and requires (a) the pStandalone arm is
// in there, and (b) no preprocessor conditional of ANY kind stands anywhere
// inside the function. A guard reintroduced around one of these -- for CLIENT_DLL,
// for _DEBUG, for a macro invented next year -- fails here whatever it is named,
// which the alias-specific rule above would miss.
//-----------------------------------------------------------------------------
// The velocity and angular-velocity needles drop their `ent.` since the
// velocity home left native (2026-10-12): the bodies bind the shell as
// `pStandalone` and keep only its member; the property is unchanged.
TEST( ArmedArmSwallowed, TheFourArmsAreUnconditionalNow )
{
	struct Case_t { const char *pszFn; const char *pszArm; };
	static const Case_t kCases[] = {
		{ "SourceSharp_EntitySetHealth",   "ent.pStandalone->SetHealth" },
		{ "SourceSharp_EntitySetOrigin",   "ent.pStandalone->SetAbsOrigin" },
		{ "SourceSharp_EntityGetAngles",   "ent.pStandalone->GetAbsAngles" },
		{ "SourceSharp_EntitySetVelocity", "pStandalone->SetVelocity" },
	};

	for ( size_t i = 0; i < sizeof( kCases ) / sizeof( kCases[ 0 ] ); i++ )
	{
		const std::vector< std::string > body =
			FunctionBody( "sourcesharp/game/server/shell_entityapi.cpp", kCases[ i ].pszFn );

		ASSERT_FALSE( body.empty() )
			<< kCases[ i ].pszFn << " has no body this scan can find in "
			   "shell_entityapi.cpp -- renamed, moved, or the `}` in column zero "
			   "the extractor keys on is gone. Every assertion about it below "
			   "would otherwise be vacuous.";

		bool bHasArm = false;
		std::string strDirectives;
		for ( size_t j = 0; j < body.size(); j++ )
		{
			if ( body[ j ].find( kCases[ i ].pszArm ) != std::string::npos )
				bHasArm = true;
			if ( IsConditionalDirective( body[ j ], NULL ) )
				strDirectives += "\n    " + TrimLeft( body[ j ] );
		}

		EXPECT_TRUE( bHasArm )
			<< kCases[ i ].pszFn << " no longer writes through "
			<< kCases[ i ].pszArm
			<< ". This is the arm that was swallowed; SetHealth's absence is the "
			   "player spawning dead.";

		EXPECT_EQ( "", strDirectives )
			<< kCases[ i ].pszFn << " has a preprocessor conditional back inside "
			   "it. The standalone arm is the only arm this function can ever "
			   "take, so a conditional around it is a silent no-op again -- which "
			   "is the whole finding this file records." << strDirectives;
	}
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was
// AnElseIsWhatExemptsARegionAndOneStillExists, and it required the velocity read
// to appear TWICE -- once in the guarded SDK arm's ternary and once in the
// `#else` -- because the `#else` was what exempted the region from the rule
// above.
//
// The guard and its `#else` are gone with the alias (0bc727ed9). What SURVIVED
// is the arm that was in the `#else`: the standalone read, which now answers on
// its own, unconditionally, for every entity. So the exemption has no witness
// because it has no subject, and the successor property is the stronger one --
// ONE occurrence, not two, and nothing conditional around it.
//
// Asserting only "the #else is gone" would pass over a file that had lost the
// velocity read entirely, so the count is pinned at exactly one and the read is
// required to still be there.
//-----------------------------------------------------------------------------
TEST( ArmedArmSwallowed, TheFormerlyExemptReadNowAnswersUnconditionally )
{
	const std::string scripted = ReadFile( "sourcesharp/game/server/shell_scripted.cpp" );
	ASSERT_FALSE( scripted.empty() ) << "could not read shell_scripted.cpp";

	size_t n = 0;
	for ( size_t p = scripted.find( "pStandalone->GetAngularVelocity()" );
	      p != std::string::npos;
	      p = scripted.find( "pStandalone->GetAngularVelocity()", p + 1 ) )
		n++;

	EXPECT_EQ( 1u, n )
		<< "shell_scripted.cpp's angular velocity read is not the single "
		   "unconditional answer it became when the alias retired. Zero means "
		   "SourceSharp_EntityGetAngularVelocity has stopped answering for an "
		   "entity with no physics object; two means a guard and an `#else` are "
		   "back, and the arm inside the guard is one the build can never take.";

	const std::vector< std::string > body = FunctionBody(
		"sourcesharp/game/server/shell_scripted.cpp", "SourceSharp_EntityGetAngularVelocity" );
	ASSERT_FALSE( body.empty() )
		<< "SourceSharp_EntityGetAngularVelocity has no body this scan can find.";

	std::string strDirectives;
	bool bHasArm = false;
	for ( size_t j = 0; j < body.size(); j++ )
	{
		if ( body[ j ].find( "pStandalone->GetAngularVelocity()" ) != std::string::npos )
			bHasArm = true;
		if ( IsConditionalDirective( body[ j ], NULL ) )
			strDirectives += "\n    " + TrimLeft( body[ j ] );
	}

	EXPECT_TRUE( bHasArm )
		<< "the read moved out of SourceSharp_EntityGetAngularVelocity; the count "
		   "above is then measuring some other site.";
	EXPECT_EQ( "", strDirectives )
		<< "a preprocessor conditional is back inside "
		   "SourceSharp_EntityGetAngularVelocity." << strDirectives;
}
