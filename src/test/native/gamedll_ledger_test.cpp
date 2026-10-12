//====== SourceSharp — the sixteen TODOs, and that none lost its verdict ======//
//
// plan_gamedll.md Phase 9. The phase's whole deliverable is one table:
//
//     "A TODO deleted without a verdict is the one failure mode this plan is
//      arranged to prevent -- the comment was the only record that the question
//      existed."
//
// Fourteen of the sixteen were comments in shell_gamedll.cpp and two were
// comments in sourcesharp_gamesystem.h. Every one of them is gone from the tree
// as a bare `// TODO:` -- which is the RIGHT outcome and also, letter for
// letter, what the failure mode looks like in a diff. The only thing that tells
// the two apart is that a verdict was written down in its place, and nothing in
// the build has ever checked that it was.
//
// So this file is the check. Three subjects, and each catches a different way
// the record rots:
//
//   THE TABLE      -- sixteen rows, each with a verdict and a commit. Nine of
//                     these cells were blank for most of this plan's life, and
//                     a blank cell is indistinguishable from a question nobody
//                     answered. An empty verdict fails here.
//
//   THE SITES      -- every surviving `TODO` in the two files sits beside a
//                     verdict word. A bare `// TODO:` reappearing is a question
//                     with no answer; a verdict deleted from beside one is an
//                     answer with no question.
//
//   THE METERS     -- Phase 9's two numbers, in the section that reports them.
//                     The refusal counter reading ZERO after a played map is
//                     the evidence for Part 7's argument about all sixteen
//                     refusals, and "50 from managed, 7 from g_pServerClassHead"
//                     is the endgame meter the plan predicted backwards. Both
//                     are readings, and a reading deleted is a claim.
//
// Source-read, and it has to be: the subject is a markdown table and two
// comment blocks, neither of which any binary contains. shell_gamedll.cpp
// includes cbase.h's whole world and can never be linked into a gtest.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstdio>
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
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadRelative( const char *pszPrefix, const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + pszPrefix + pszRel ).c_str(),
	                    std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}
std::string ReadFromSrc( const char *pszRel )      { return ReadRelative( "/",    pszRel ); }

const char *kShell   = "sourcesharp/game/server/shell_gamedll.cpp";
const char *kEnvHdr  = "sourcesharp/game/shared/sourcesharp_gamesystem.h";

std::string Lower( const std::string &s )
{
	std::string out( s );
	for ( size_t i = 0; i < out.size(); i++ )
		if ( out[ i ] >= 'A' && out[ i ] <= 'Z' ) out[ i ] = (char)( out[ i ] - 'A' + 'a' );
	return out;
}

// A verdict, not a placeholder. "TBD", "see below" and a bare restatement of the
// question are all things that fit in the cell and answer nothing.
bool ReadsAsAVerdict( const std::string &cell )
{
	const std::string low = Lower( cell );
	return low.find( "closed" )   != std::string::npos
	    || low.find( "rejected" ) != std::string::npos
	    || low.find( "refused" )  != std::string::npos;
}

// A TODO SITE is a `//` comment line whose first word is TODO -- which is the
// shape all sixteen of them had, and the shape a seventeenth would arrive in.
// Deliberately NOT every occurrence of the four letters: the verdicts that
// replaced them talk ABOUT the TODOs they answer ("that is not one of Part 9's
// eight TODOs", "the TODO this replaces asked the right question"), and a rule
// that counted those would be a rule against explaining yourself.
bool IsTodoLineStart( const std::string &src, size_t at )
{
	size_t line = src.rfind( '\n', at );
	line = ( line == std::string::npos ) ? 0 : line + 1;

	while ( line < at && ( src[ line ] == ' ' || src[ line ] == '\t' ) ) line++;
	if ( line + 1 >= src.size() || src[ line ] != '/' || src[ line + 1 ] != '/' ) return false;

	line += 2;
	while ( line < at && ( src[ line ] == ' ' || src[ line ] == '\t' ) ) line++;

	return line == at;
}

// The run of `//` comment lines immediately above `at`, and nothing above the
// first blank or code line. Bounded on purpose: a fixed-size window back from a
// method reaches into the PREVIOUS method's comment, and then every assertion
// about "the block above this one" is really about its neighbour.
std::string CommentRunAbove( const std::string &src, size_t at )
{
	size_t begin = src.rfind( '\n', at );
	begin = ( begin == std::string::npos ) ? 0 : begin + 1;

	while ( begin > 0 )
	{
		size_t prev = src.rfind( '\n', begin - 2 );
		prev = ( prev == std::string::npos ) ? 0 : prev + 1;

		size_t scan = prev;
		while ( scan < src.size() && ( src[ scan ] == ' ' || src[ scan ] == '\t' ) ) scan++;
		if ( scan + 1 >= src.size() || src[ scan ] != '/' || src[ scan + 1 ] != '/' )
			break;

		begin = prev;
		if ( prev == 0 ) break;
	}

	return src.substr( begin, at - begin );
}

// The site, with the comment block it sits in -- from the first line above it
// that is not a `//` comment, to the last line below it that is. The whole
// block, because a verdict this long does not fit on the TODO's own line and
// never has.
struct Site
{
	size_t      at;
	std::string block;
};

std::vector<Site> TodoSites( const std::string &src )
{
	std::vector<Site> sites;

	// Case-insensitively, because two of the original sixteen were spelled
	// `// todo:` and a seventeenth is as likely to be.
	const std::string hay = Lower( src );

	for ( size_t at = hay.find( "todo" ); at != std::string::npos; at = hay.find( "todo", at + 4 ) )
	{
		if ( !IsTodoLineStart( src, at ) )
			continue;

		// Walk back over whole comment lines.
		size_t begin = src.rfind( '\n', at );
		begin = ( begin == std::string::npos ) ? 0 : begin + 1;
		while ( begin > 0 )
		{
			size_t prev = src.rfind( '\n', begin - 2 );
			prev = ( prev == std::string::npos ) ? 0 : prev + 1;

			size_t scan = prev;
			while ( scan < src.size() && ( src[ scan ] == ' ' || src[ scan ] == '\t' ) ) scan++;
			if ( scan + 1 >= src.size() || src[ scan ] != '/' || src[ scan + 1 ] != '/' )
				break;

			begin = prev;
			if ( prev == 0 ) break;
		}

		// And forward over them.
		size_t end = src.find( '\n', at );
		end = ( end == std::string::npos ) ? src.size() : end + 1;
		while ( end < src.size() )
		{
			size_t scan = end;
			while ( scan < src.size() && ( src[ scan ] == ' ' || src[ scan ] == '\t' ) ) scan++;
			if ( scan + 1 >= src.size() || src[ scan ] != '/' || src[ scan + 1 ] != '/' )
				break;

			const size_t nl = src.find( '\n', end );
			end = ( nl == std::string::npos ) ? src.size() : nl + 1;
		}

		Site site;
		site.at    = at;
		site.block = src.substr( begin, end - begin );
		sites.push_back( site );
	}

	return sites;
}

std::string LineNumberOf( const std::string &src, size_t at )
{
	int line = 1;
	for ( size_t i = 0; i < at && i < src.size(); i++ )
		if ( src[ i ] == '\n' ) line++;

	char buf[ 32 ];
	snprintf( buf, sizeof( buf ), "%d", line );
	return buf;
}

} // namespace

//-----------------------------------------------------------------------------
// THE SITES. Every surviving TODO carries its answer.
//
// This is the direction the table cannot cover: the table says sixteen
// questions were answered, and this says nothing in the two files is asking a
// seventeenth with nobody watching.
//-----------------------------------------------------------------------------
TEST( GameDllLedger, NoTodoInTheShellStandsWithoutAVerdictBesideIt )
{
	const std::string src = ReadFromSrc( kShell );
	ASSERT_FALSE( src.empty() );

	const std::vector<Site> sites = TodoSites( src );
	ASSERT_FALSE( sites.empty() )
		<< "shell_gamedll.cpp mentions TODO nowhere at all. Fourteen of the "
		<< "sixteen were here and each was replaced by a comment that names the "
		<< "TODO it answers -- so zero mentions means the verdicts went too.";

	for ( size_t i = 0; i < sites.size(); i++ )
	{
		EXPECT_TRUE( ReadsAsAVerdict( sites[ i ].block ) )
			<< "the TODO at shell_gamedll.cpp:" << LineNumberOf( src, sites[ i ].at )
			<< " has no verdict in its comment block. A bare TODO here is a "
			<< "question with no answer, and this plan's one stated failure mode "
			<< "is losing the question -- which is the same record either way.";
	}
}

TEST( GameDllLedger, TheEnvironmentsTwoTodosCarryTheRefusalAndItsReason )
{
	const std::string src = ReadFromSrc( kEnvHdr );
	ASSERT_FALSE( src.empty() );

	const std::vector<Site> sites = TodoSites( src );
	ASSERT_FALSE( sites.empty() )
		<< "sourcesharp_gamesystem.h mentions TODO nowhere. #15 and #16 lived "
		<< "here, and a file with neither the questions nor their answers is "
		<< "exactly the diff this plan exists to refuse.";

	std::string all;
	for ( size_t i = 0; i < sites.size(); i++ )
	{
		all += sites[ i ].block;

		EXPECT_TRUE( ReadsAsAVerdict( sites[ i ].block ) )
			<< "the TODO at sourcesharp_gamesystem.h:" << LineNumberOf( src, sites[ i ].at )
			<< " has no verdict in its comment block.";
	}

	// The reason, not just the word. Both refusals rest on one measured fact
	// each, and a refusal whose argument is gone reads as an arbitrary rule.
	EXPECT_NE( all.find( "g_pServerClassHead" ), std::string::npos )
		<< "#15's refusal does not name g_pServerClassHead. The whole argument "
		<< "is that a ServerClass links ITSELF into that chain, alphabetically, "
		<< "and that both sides' class ids depend on the order -- so a second "
		<< "way to add one is a way to renumber every class on one side.";

	EXPECT_NE( all.find( "CStandardSendProxies" ), std::string::npos )
		<< "#16's refusal does not name CStandardSendProxies. It is a table the "
		<< "engine matches by pointer identity, not a list to append to.";
}

//-----------------------------------------------------------------------------
// THE THREE THAT WERE NEVER TODOS, and are the same failure one level up.
//
// InvalidateMdlCache, GameServerSteamAPIActivated and GameServerSteamAPIShutdown
// are Part 11a2's three gaps -- "two are real losses of behaviour, and were
// being dropped silently". They carried no TODO comment, so the sixteen-row
// table does not cover them and neither does the site rule above: an EMPTY BODY
// with no comment is indistinguishable from a body somebody decided should be
// empty, which is the same record as a deleted question.
//
// So the verdict is written at each site and this is the check that it stays.
//
// AT PHASE A2 THE VERDICT CHANGED AND THIS FACT FOLLOWED IT RATHER THAN BEING
// WEAKENED. All three now have bodies: each forwards to managed, and managed
// calls back one native step. The fact asserts three things where it asserted
// two, and the added one is the one that matters most -- THE BODY IS NOT EMPTY.
// A verdict block that says "implemented" above a `{}` is exactly the record
// this fact exists to refuse, one level further on.
//-----------------------------------------------------------------------------
TEST( GameDllLedger, ThePartElevenA2GapsCarryAVerdictEvenThoughTheyNeverHadATodo )
{
	const std::string src = ReadFromSrc( kShell );
	ASSERT_FALSE( src.empty() );

	const char *kGaps[] = { "void InvalidateMdlCache() override",
	                        "void GameServerSteamAPIActivated() override",
	                        "void GameServerSteamAPIShutdown() override" };

	for ( size_t i = 0; i < sizeof( kGaps ) / sizeof( kGaps[ 0 ] ); i++ )
	{
		const size_t at = src.find( kGaps[ i ] );
		ASSERT_NE( at, std::string::npos )
			<< "shell_gamedll.cpp no longer declares " << kGaps[ i ]
			<< " in a shape this test can find, so it is reasoning about a file "
			<< "that has changed.";

		// THE CONTIGUOUS COMMENT RUN IMMEDIATELY ABOVE, and nothing else.
		//
		// The first version of this took a fixed 2400 characters back, and that
		// is a check that cannot fail: these three sit one after another, so
		// GameServerSteamAPIActivated's window reached into
		// InvalidateMdlCache's block and found ITS verdict. Mutation G2 --
		// "the Steam pair stops naming the part that found them" -- was GREEN
		// against it. Number NINE in this tree's running list.
		const std::string before = CommentRunAbove( src, at );
		ASSERT_FALSE( before.empty() )
			<< kGaps[ i ] << " has no comment block above it at all. Its body is "
			<< "empty, so there is nothing anywhere saying whether that is a "
			<< "decision or unfinished work.";

		// Case-insensitively: this file's house style SHOUTS the heading of a
		// block and writes the same name in prose below it, and a check that
		// only saw one of the two spellings would be a check about typography.
		const std::string lowered = Lower( before );

		EXPECT_NE( lowered.find( "part 11a2" ), std::string::npos )
			<< kGaps[ i ] << " has no verdict block naming Part 11a2 above it. "
			<< "Its body is empty and an empty body with no comment is "
			<< "indistinguishable from a decision -- which is precisely the "
			<< "record this ledger exists to refuse.";

		// THE VERDICT PHRASE, NOT THE WORD. This file's house style is
		// "THE VERDICT IS: <verdict>." and the word on its own is not enough:
		// InvalidateMdlCache's block says the CLIENT half "has always been
		// implemented", which is a sentence about cdll_client_int.cpp, and
		// mutation G1 was GREEN against a check that accepted it. That is the
		// same defect as H1 -- a verdict word surviving elsewhere in its own
		// block -- and it is why the needle is the phrase.
		EXPECT_NE( lowered.find( "the verdict is: implemented" ), std::string::npos )
			<< kGaps[ i ] << "'s block does not say THE VERDICT IS: IMPLEMENTED. Phase A2's "
			<< "whole subject is that an empty body with no verdict reads as a "
			<< "decision; a verdict that has stopped tracking the code reads as "
			<< "one too, and is worse, because it is asserted rather than absent.";

		// AND THE BODY IS NOT EMPTY. Everything above is about the COMMENT, and
		// a comment saying "implemented" over a `{}` is the same failure this
		// fact exists to refuse -- with the added feature of being a claim.
		//
		// The body is what sits between the signature's `{` and its matching
		// `}`, and "not empty" is spelled as "reaches the managed runtime",
		// because these three are pure notifications: the only correct body is
		// one that hands the call across, and a body with a comment in it and
		// nothing else would pass a whitespace test.
		const size_t open = src.find( '{', at );
		ASSERT_NE( open, std::string::npos ) << kGaps[ i ] << " has no body at all";
		const size_t close = src.find( '}', open );
		ASSERT_NE( close, std::string::npos ) << kGaps[ i ] << " has no closing brace";

		const std::string body = src.substr( open + 1, close - open - 1 );
		EXPECT_NE( body.find( "ManagedRuntimeServer()->" ), std::string::npos )
			<< kGaps[ i ] << " does not reach the managed runtime. Phase A2 "
			<< "implemented all three by forwarding; a body that answers here "
			<< "in C++ would be the shell deciding, which is the one thing the "
			<< "whole port removes -- and an EMPTY body is where this started.";
	}
}
