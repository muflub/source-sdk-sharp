//====== SourceSharp — every shim translation unit says why it is native =====//
//
// plan_shim.md §8, ratchets 2 and 3. `plan.md` §1a allows native code only
// where the engine leaves no choice, and §1 of plan_shim.md gives the eight
// reasons that count. Until this commit that ruling lived in
// `plan_shim_rulings.tsv` -- a 439-row table measured at 85a7449d1, describing
// paths that have since moved, split or gone, and which nothing re-derived.
// A reader opening a .cpp could not see why it was allowed to exist.
//
// So the letter is now in the file it is about, as line 2:
//
//     // SS_REASON a  a vtable the engine calls through
//
// and this test is what keeps that true.
//
//=============================================================================
// THE MARKER IS A FORM, NOT A WORD, AND THAT IS THE WHOLE POINT
//=============================================================================
//
// Four separate times on this project a grep for a NAME hit a comment, a
// failure-message string, a test needle quoting a declaration, or a paragraph
// of documentation, and the hit was read as code. A reason-letter check is
// unusually exposed to that: the letters are discussed in prose all over this
// tree, and a check that accepted "the file's reason is (a)" in a paragraph
// would be satisfied by every file that merely TALKS about the rule.
//
// The marker is therefore anchored hard: column zero, the exact token
// `// SS_REASON `, one letter, TWO spaces, and then the canonical text for
// that letter and no other text. `MarkerLetterOf` below is driven over eight
// near-misses -- indented, prose, wrong letter, one space, a mention inside a
// sentence -- and has to reject every one of them.
//
// And EXACTLY ONE marker per file, which is the direction that catches the
// other failure: a file that quotes the marker form in its own header block
// while discussing the rule would otherwise be both marked and talking about
// being marked, and the two could disagree.
//
//=============================================================================
// THE NINTH ANSWER, WHICH IS THE ONE THAT MATTERS
//=============================================================================
//
// Eight of the 168 files fit NO letter. They are not native by right: each is
// native only because some OTHER native caller has not been ported yet, and
// each one's exit condition is a named file. Forcing a letter onto them would
// have hidden the single most useful number this close can produce, so they
// carry `SS_REASON x` and are listed with their exit conditions in
// docs/shim-reasons.md.
//
// `shim_reason_none` in docs/shim-ratchet.tsv is the ceiling on that count.
// It is the number the port exists to drive to zero.
//
//=============================================================================
// BOTH DIRECTIONS, FOR EVERY ROW
//=============================================================================
//
//   up     a count above its ceiling reddens.
//   stale  a ceiling whose subject is GONE reddens: the walk must find at
//          least a hundred translation units, and every x-file must have a
//          docs/shim-reasons.md entry AND every entry in that document must
//          name a file that still carries an x. A keep-list nobody checks
//          against the tree rots silently -- that exact failure was caught at
//          a merge on this project.
//
//=============================================================================//

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include <dirent.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <sys/stat.h>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// The src/ root, from the test's own working directory. Deliberately NOT an
// upward walk to a repo root: a walk escapes a git worktree into the main
// checkout and then answers about the wrong tree.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string shim = std::string( kCandidates[ i ] ) + "/sourcesharp";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
		     && stat( shim.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string Read( const std::string &strAbs )
{
	std::ifstream f( strAbs.c_str(), std::ios::binary );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string ReadFromSrc( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
		return std::string();
	return Read( std::string( pszRoot ) + "/" + strRel );
}

std::vector< std::string > Lines( const std::string &strText )
{
	std::vector< std::string > v;
	std::string cur;
	for ( size_t i = 0; i < strText.size(); i++ )
	{
		if ( strText[ i ] == '\n' )
		{
			v.push_back( cur );
			cur.clear();
		}
		else if ( strText[ i ] != '\r' )
		{
			cur += strText[ i ];
		}
	}
	if ( !cur.empty() )
		v.push_back( cur );
	return v;
}

//-----------------------------------------------------------------------------
// THE SHIPPED SHIM. Everything under src/sourcesharp/ except:
//
//   test/      the native gtests. They ship nothing and bind no engine.
//   managed/   C#, which is the destination and not the subject.
//
// Both exclusions are by DIRECTORY NAME at any depth, so a file cannot escape
// the walk by moving; a new subdirectory is INCLUDED by default, which is the
// safe direction -- a new home for native code has to be marked, not excused.
//-----------------------------------------------------------------------------
void Walk( const std::string &strDir, const char *pszExt, std::vector< std::string > &out )
{
	DIR *d = opendir( strDir.c_str() );
	if ( d == NULL )
		return;
	struct dirent *e;
	while ( ( e = readdir( d ) ) != NULL )
	{
		const std::string name = e->d_name;
		if ( name == "." || name == ".." )
			continue;
		const std::string path = strDir + "/" + name;
		struct stat st;
		if ( stat( path.c_str(), &st ) != 0 )
			continue;
		if ( S_ISDIR( st.st_mode ) )
		{
			if ( name == "test" || name == "managed" )
				continue;
			Walk( path, pszExt, out );
		}
		else if ( name.size() > strlen( pszExt )
		          && name.compare( name.size() - strlen( pszExt ), strlen( pszExt ), pszExt ) == 0 )
		{
			out.push_back( path );
		}
	}
	closedir( d );
}

std::vector< std::string > ShimSources( const char *pszExt )
{
	std::vector< std::string > v;
	const char *pszRoot = SrcRoot();
	if ( pszRoot != NULL )
		Walk( std::string( pszRoot ) + "/sourcesharp", pszExt, v );
	std::sort( v.begin(), v.end() );
	return v;
}

//-----------------------------------------------------------------------------
// The marker. Column zero, exact token, one letter, exactly two spaces, and
// then text that must be the canonical text for that letter.
//-----------------------------------------------------------------------------
const char *kMarkerPrefix = "// SS_REASON ";

struct Letter
{
	char c;
	const char *pszText;
};

const Letter kLetters[] = {
	{ 'a', "a vtable the engine calls through" },
	{ 'b', "an interface resolved by name at load" },
	{ 'c', "static-init registration" },
	{ 'd', "a layout the engine reads at fixed offsets" },
	{ 'e', "a synchronous mid-step answer" },
	{ 'f', "a bit-level wire codec" },
	{ 'g', "leaf math over mdl/bsp data" },
	{ 'h', "the CoreCLR host and bootstrap dispatch" },
	{ 'x', "NO LETTER FITS -- see docs/shim-reasons.md" },
};
const size_t kNumLetters = sizeof( kLetters ) / sizeof( kLetters[ 0 ] );

const char *TextForLetter( char c )
{
	for ( size_t i = 0; i < kNumLetters; i++ )
		if ( kLetters[ i ].c == c )
			return kLetters[ i ].pszText;
	return NULL;
}

// '\0' when the line is not a marker; otherwise the letter.
char MarkerLetterOf( const std::string &strLine )
{
	const size_t nPrefix = strlen( kMarkerPrefix );
	if ( strLine.compare( 0, nPrefix, kMarkerPrefix ) != 0 )
		return '\0';                         // must start at column zero
	if ( strLine.size() < nPrefix + 3 )
		return '\0';
	const char c = strLine[ nPrefix ];
	if ( TextForLetter( c ) == NULL )
		return '\0';                         // not one of the nine
	if ( strLine[ nPrefix + 1 ] != ' ' || strLine[ nPrefix + 2 ] != ' ' )
		return '\0';                         // exactly two spaces
	return c;
}

std::string MarkerTextOf( const std::string &strLine )
{
	return strLine.substr( strlen( kMarkerPrefix ) + 3 );
}

struct Marked
{
	std::string strPath;
	char c;
	std::string strText;
	int nMarkers;
};

std::vector< Marked > Markers()
{
	std::vector< Marked > out;
	const std::vector< std::string > files = ShimSources( ".cpp" );
	for ( size_t i = 0; i < files.size(); i++ )
	{
		Marked m;
		m.strPath = files[ i ];
		m.c = '\0';
		m.nMarkers = 0;
		const std::vector< std::string > lines = Lines( Read( files[ i ] ) );
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			const char c = MarkerLetterOf( lines[ j ] );
			if ( c == '\0' )
				continue;
			m.nMarkers++;
			if ( m.nMarkers == 1 )
			{
				m.c = c;
				m.strText = MarkerTextOf( lines[ j ] );
			}
		}
		out.push_back( m );
	}
	return out;
}

long RatchetCeiling( const char *pszMetric )
{
	const std::vector< std::string > lines = Lines( ReadFromSrc( "../docs/shim-ratchet.tsv" ) );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].empty() || lines[ i ][ 0 ] == '#' )
			continue;
		const size_t t1 = lines[ i ].find( '\t' );
		if ( t1 == std::string::npos || lines[ i ].substr( 0, t1 ) != pszMetric )
			continue;
		const size_t t2 = lines[ i ].find( '\t', t1 + 1 );
		if ( t2 == std::string::npos )
			continue;
		return strtol( lines[ i ].substr( t2 + 1 ).c_str(), NULL, 10 );
	}
	return -1;
}

// The offset of the "EASON (" in a `REASON (g)` / `reason (g)` token, or npos.
// Returns the offset such that offset+7 is the letter.
size_t FindReasonParen( const std::string &strLine )
{
	for ( size_t at = 1; at + 8 < strLine.size(); at++ )
	{
		const char cBefore = strLine[ at - 1 ];
		if ( cBefore != 'R' && cBefore != 'r' )
			continue;
		bool bWord = true;
		static const char *pszWord = "EASON (";
		for ( size_t k = 0; k < 7 && bWord; k++ )
		{
			const char c = strLine[ at + k ];
			bWord = ( c == pszWord[ k ] || c == (char)tolower( pszWord[ k ] ) );
		}
		if ( bWord )
			return at;
	}
	return std::string::npos;
}

// The path as docs/shim-reasons.md spells it: from src/ down.
std::string Relative( const std::string &strPath )
{
	const size_t at = strPath.find( "sourcesharp/" );
	return ( at == std::string::npos ) ? strPath : strPath.substr( at );
}

} // namespace

//-----------------------------------------------------------------------------
// Anti-vacuity. Without this every "every file" assertion below is an
// assertion about the empty set.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, TheWalkFindsTheShimsTranslationUnits )
{
	ASSERT_NE( SrcRoot(), (const char *)NULL )
		<< "neither '.', 'src' nor '../src' held both game/ and sourcesharp/";

	const std::vector< std::string > cpp = ShimSources( ".cpp" );
	ASSERT_GE( cpp.size(), 100u )
		<< "the walk found " << cpp.size() << " shim .cpp files. The shim has "
		   "not been anywhere near that small at any point in this project, so "
		   "the walk is broken and every 'every file carries a letter' fact "
		   "below would pass over almost nothing.";

	const long lCeiling = RatchetCeiling( "shim_cpp_files" );
	ASSERT_GE( lCeiling, 0 )
		<< "docs/shim-ratchet.tsv has no shim_cpp_files row";
	EXPECT_LE( (long)cpp.size(), lCeiling )
		<< "the shim has " << cpp.size() << " translation units against a "
		   "ceiling of " << lCeiling << ". A shim that grows is a port running "
		   "backwards. If a file genuinely had to be added, raise the row "
		   "deliberately and say what it binds.";

	for ( size_t i = 0; i < cpp.size(); i++ )
	{
		EXPECT_EQ( cpp[ i ].find( "/test/" ), std::string::npos )
			<< "the walk descended into sourcesharp/test/ and picked up "
			<< cpp[ i ] << ". The gtests bind no engine and ship nothing; "
			   "marking them would make every count here mean something else.";
		EXPECT_EQ( cpp[ i ].find( "/managed/" ), std::string::npos )
			<< "the walk descended into sourcesharp/managed/, which is C# and "
			   "is the destination rather than the subject";
	}
}

//-----------------------------------------------------------------------------
// Ratchet 2.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, EveryShimTranslationUnitCarriesExactlyOneReasonLetter )
{
	const std::vector< Marked > m = Markers();
	std::vector< std::string > unmarked, doubled;
	for ( size_t i = 0; i < m.size(); i++ )
	{
		if ( m[ i ].nMarkers == 0 )
			unmarked.push_back( Relative( m[ i ].strPath ) );
		else if ( m[ i ].nMarkers > 1 )
			doubled.push_back( Relative( m[ i ].strPath ) );
	}

	std::string strUnmarked;
	for ( size_t i = 0; i < unmarked.size(); i++ )
		strUnmarked += "\n  " + unmarked[ i ];
	EXPECT_TRUE( unmarked.empty() )
		<< unmarked.size() << " shim translation unit(s) carry no SS_REASON "
		   "marker. Native code exists only where the engine leaves no choice "
		   "(plan.md §1a); a file that cannot say which of plan_shim.md §1's "
		   "letters applies to it is a file that has not been ruled on. Put "
		   "the marker on line 2, or use `x` and give it an exit condition in "
		   "docs/shim-reasons.md." << strUnmarked;

	std::string strDoubled;
	for ( size_t i = 0; i < doubled.size(); i++ )
		strDoubled += "\n  " + doubled[ i ];
	EXPECT_TRUE( doubled.empty() )
		<< doubled.size() << " file(s) carry more than one marker, so the file "
		   "both states a reason and quotes the form while discussing it, and "
		   "the two can disagree. One marker per file." << strDoubled;
}

TEST( ShimReasonLetters, EveryMarkerCarriesItsLettersOwnText )
{
	const std::vector< Marked > m = Markers();
	for ( size_t i = 0; i < m.size(); i++ )
	{
		if ( m[ i ].nMarkers != 1 )
			continue;   // reported by the fact above
		const char *pszWant = TextForLetter( m[ i ].c );
		ASSERT_NE( pszWant, (const char *)NULL );
		EXPECT_EQ( m[ i ].strText, std::string( pszWant ) )
			<< Relative( m[ i ].strPath ) << " is marked (" << m[ i ].c
			<< ") but its text is not that letter's. The letter and the "
			   "sentence have to move together, or a retype of the letter "
			   "alone leaves a file claiming one reason and reading as "
			   "another.";
	}
}

//-----------------------------------------------------------------------------
// The matcher itself, over the near-misses it exists to reject. Without this
// a MarkerLetterOf that matched everything would make the fact above pass and
// mean nothing.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, ProseAboutTheMarkerIsNotAMarker )
{
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON a  a vtable the engine calls through" ), 'a' )
		<< "the positive control: the real form is not matched, so every "
		   "rejection below is a rejection of everything";

	EXPECT_EQ( MarkerLetterOf( "\t// SS_REASON a  a vtable the engine calls through" ), '\0' )
		<< "indented -- inside a function body or a quoted block, not a file "
		   "header";
	EXPECT_EQ( MarkerLetterOf( " // SS_REASON a  a vtable the engine calls through" ), '\0' );
	EXPECT_EQ( MarkerLetterOf( "// the SS_REASON a marker goes on line 2" ), '\0' )
		<< "a sentence ABOUT the marker. This is the exact shape that has "
		   "been read as code four times on this project.";
	EXPECT_EQ( MarkerLetterOf( "//  SS_REASON a  a vtable the engine calls through" ), '\0' );
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON a a vtable the engine calls through" ), '\0' )
		<< "one space, not two";
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON i  some ninth reason nobody agreed" ), '\0' )
		<< "there are eight letters and one x. A ninth would be a new licence "
		   "to write C++ and cannot be created by typing one.";
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON A  a vtable the engine calls through" ), '\0' )
		<< "lower case only, so there is one spelling to grep for";
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON" ), '\0' );
	EXPECT_EQ( MarkerLetterOf( "" ), '\0' );
}

//-----------------------------------------------------------------------------
// Ratchet: the files for which no letter fits. BOTH directions against the
// document, so neither the marker set nor the document can rot alone.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, TheFilesWithNoLetterAreCappedAndEachHasAnExitCondition )
{
	const long lCeiling = RatchetCeiling( "shim_reason_none" );
	ASSERT_GE( lCeiling, 0 )
		<< "docs/shim-ratchet.tsv has no shim_reason_none row, so the one "
		   "number this close exists to drive to zero is unwatched";

	const std::vector< Marked > m = Markers();
	std::vector< std::string > none;
	for ( size_t i = 0; i < m.size(); i++ )
		if ( m[ i ].nMarkers == 1 && m[ i ].c == 'x' )
			none.push_back( Relative( m[ i ].strPath ) );

	EXPECT_LE( (long)none.size(), lCeiling )
		<< none.size() << " files claim no reason letter, against a ceiling of "
		   << lCeiling << ". Every one of these is native only because some "
		   "other native caller has not been ported; adding one moves the port "
		   "backwards.";

	const std::string strDoc = ReadFromSrc( "../docs/shim-reasons.md" );
	ASSERT_FALSE( strDoc.empty() )
		<< "docs/shim-reasons.md is missing or empty, so the exit conditions "
		   "the x marker points at do not exist";

	// Every x file is named in the document...
	for ( size_t i = 0; i < none.size(); i++ )
		EXPECT_NE( strDoc.find( none[ i ] ), std::string::npos )
			<< none[ i ] << " is marked (x) and docs/shim-reasons.md does not "
			   "name it. `x` means 'native only until a named caller goes'; "
			   "without the name it means 'native because nobody asked'.";

	// ...and every file the document names as an x still carries one. This is
	// the half that stops the list rotting: a row for a file that was deleted,
	// or that earned a real letter, has to be removed with it.
	const std::vector< std::string > lines = Lines( strDoc );
	int nRows = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].compare( 0, 8, "| `sourc" ) != 0 )
			continue;
		const size_t open = lines[ i ].find( '`' );
		const size_t close = lines[ i ].find( '`', open + 1 );
		if ( close == std::string::npos )
			continue;
		const std::string strNamed = lines[ i ].substr( open + 1, close - open - 1 );
		nRows++;
		EXPECT_TRUE( std::find( none.begin(), none.end(), strNamed ) != none.end() )
			<< "docs/shim-reasons.md has a row for " << strNamed << ", which "
			   "carries no (x) marker in the tree -- it was deleted, renamed, "
			   "or it earned a real letter. Remove the row in the same commit.";
	}
	EXPECT_EQ( nRows, (int)none.size() )
		<< "docs/shim-reasons.md lists " << nRows << " no-letter files and the "
		   "tree has " << none.size();
}

//-----------------------------------------------------------------------------
// The re-derived "what remains C++" table, checked against the walk that
// derived it. plan_shim.md §8 asked for that table re-derived rather than
// transcribed, and a derived table nobody re-derives is the shape this project
// keeps finding: plan_entity.md §6's prose version went eight files and two
// corrections out of date without a single test noticing.
//
// The row format is the one docs/shim-reasons.md is generated in:
//
//     | a | a vtable ... | 23 | 5 | 26 |  |  |  | 54 | 48,970 |
//       ^letter                                    ^files ^lines
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, TheDerivedTableAgreesWithTheWalkThatDerivedIt )
{
	const std::vector< std::string > doc = Lines( ReadFromSrc( "../docs/shim-reasons.md" ) );
	ASSERT_FALSE( doc.empty() ) << "docs/shim-reasons.md is missing or empty";

	// What the tree says.
	const std::vector< Marked > m = Markers();
	long nFiles[ 128 ] = { 0 };
	long nLines[ 128 ] = { 0 };
	for ( size_t i = 0; i < m.size(); i++ )
	{
		if ( m[ i ].nMarkers != 1 )
			continue;
		const unsigned char c = (unsigned char)m[ i ].c;
		nFiles[ c ]++;
		const std::string s = Read( m[ i ].strPath );
		nLines[ c ] += (long)std::count( s.begin(), s.end(), '\n' );
	}

	// A cell's number with the thousands separators and bold marks taken out;
	// an empty cell is 0 (the per-directory columns leave zeros blank).
	auto Num = []( const std::string &s ) -> long {
		std::string digits;
		for ( size_t j = 0; j < s.size(); j++ )
			if ( s[ j ] >= '0' && s[ j ] <= '9' )
				digits += s[ j ];
		return digits.empty() ? 0 : strtol( digits.c_str(), NULL, 10 );
	};

	int nRowsRead = 0;
	long lColumnSum[ 6 ] = { 0 };
	bool bTotalRead = false;
	long lTotalColumns[ 6 ] = { 0 };
	long lTotalFiles = -1, lTotalLines = -1;
	for ( size_t i = 0; i < doc.size(); i++ )
	{
		// Split the markdown row into cells.
		if ( doc[ i ].size() < 4 || doc[ i ][ 0 ] != '|' )
			continue;
		std::vector< std::string > cells;
		std::string cur;
		for ( size_t j = 1; j < doc[ i ].size(); j++ )
		{
			if ( doc[ i ][ j ] == '|' )
			{
				cells.push_back( cur );
				cur.clear();
			}
			else
			{
				cur += doc[ i ][ j ];
			}
		}
		// The generated grid has ten cells: letter, reason, six per-directory
		// counts, files, lines. The legend table earlier in the document has
		// three, and would otherwise be parsed as data.
		if ( cells.size() < 8 )
			continue;

		// The total row: no letter, "**total**" in the reason column.
		if ( cells.size() == 10 && cells[ 1 ].find( "total" ) != std::string::npos )
		{
			bTotalRead = true;
			for ( int k = 0; k < 6; k++ )
				lTotalColumns[ k ] = Num( cells[ 2 + k ] );
			lTotalFiles = Num( cells[ 8 ] );
			lTotalLines = Num( cells[ 9 ] );
			continue;
		}

		std::string strLetter = cells[ 0 ];
		while ( !strLetter.empty() && ( strLetter[ 0 ] == ' ' || strLetter[ 0 ] == '*' ) )
			strLetter.erase( 0, 1 );
		while ( !strLetter.empty()
		        && ( *strLetter.rbegin() == ' ' || *strLetter.rbegin() == '*' ) )
			strLetter.erase( strLetter.size() - 1 );
		if ( strLetter.size() != 1 || TextForLetter( strLetter[ 0 ] ) == NULL )
			continue;

		// The last two cells, with the thousands separators taken out.
		long lCell[ 2 ] = { 0, 0 };
		for ( int k = 0; k < 2; k++ )
		{
			std::string s = cells[ cells.size() - 2 + k ];
			std::string digits;
			for ( size_t j = 0; j < s.size(); j++ )
				if ( s[ j ] >= '0' && s[ j ] <= '9' )
					digits += s[ j ];
			lCell[ k ] = digits.empty() ? -1 : strtol( digits.c_str(), NULL, 10 );
		}

		const unsigned char c = (unsigned char)strLetter[ 0 ];
		nRowsRead++;
		if ( cells.size() == 10 )
			for ( int k = 0; k < 6; k++ )
				lColumnSum[ k ] += Num( cells[ 2 + k ] );
		EXPECT_EQ( lCell[ 0 ], nFiles[ c ] )
			<< "docs/shim-reasons.md says (" << strLetter << ") covers "
			<< lCell[ 0 ] << " files and the walk finds " << nFiles[ c ]
			<< ". The table is generated FROM the markers; regenerate it in "
			   "the commit that moved one.";
		EXPECT_EQ( lCell[ 1 ], nLines[ c ] )
			<< "docs/shim-reasons.md says (" << strLetter << ") is "
			<< lCell[ 1 ] << " lines and the walk finds " << nLines[ c ];
	}

	EXPECT_EQ( nRowsRead, 9 )
		<< "read " << nRowsRead << " letter rows out of docs/shim-reasons.md "
		   "and there are nine letters. Either the table was reformatted out "
		   "from under this parser -- in which case it is no longer checked "
		   "against anything -- or a letter lost its row.";

	// The total row is derived too: it once read 125,788 while the rows summed
	// to 125,398, and nothing noticed (2026-09-30, a merge of two re-derived
	// tables). Files and lines against the walk; each directory column
	// against the rows above it.
	ASSERT_TRUE( bTotalRead ) << "docs/shim-reasons.md's table has no **total** row";
	long lWalkFiles = 0, lWalkLines = 0;
	for ( int c = 0; c < 128; c++ )
	{
		if ( TextForLetter( (char)c ) == NULL )
			continue;
		lWalkFiles += nFiles[ c ];
		lWalkLines += nLines[ c ];
	}
	EXPECT_EQ( lWalkFiles, lTotalFiles )
		<< "the **total** row says " << lTotalFiles << " files and the walk finds " << lWalkFiles;
	EXPECT_EQ( lWalkLines, lTotalLines )
		<< "the **total** row says " << lTotalLines << " lines and the walk finds " << lWalkLines;
	for ( int k = 0; k < 6; k++ )
		EXPECT_EQ( lColumnSum[ k ], lTotalColumns[ k ] )
			<< "the **total** row's directory column " << k << " says " << lTotalColumns[ k ]
			<< " and the letter rows above it sum to " << lColumnSum[ k ];
}

//-----------------------------------------------------------------------------
// Ratchet 3: the shim's size.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, TheShimHasNotGrown )
{
	const long lCeiling = RatchetCeiling( "shim_lines" );
	ASSERT_GE( lCeiling, 0 ) << "docs/shim-ratchet.tsv has no shim_lines row";

	std::vector< std::string > all = ShimSources( ".cpp" );
	const std::vector< std::string > hdr = ShimSources( ".h" );
	all.insert( all.end(), hdr.begin(), hdr.end() );

	ASSERT_GE( all.size(), 150u )
		<< "the walk found " << all.size() << " shim sources and headers "
		   "together; the line count below would be a count of nothing";

	long lLines = 0;
	for ( size_t i = 0; i < all.size(); i++ )
	{
		const std::string s = Read( all[ i ] );
		lLines += (long)std::count( s.begin(), s.end(), '\n' );
	}

	EXPECT_LE( lLines, lCeiling )
		<< "the shim is " << lLines << " lines against a ceiling of "
		   << lCeiling << " recorded at the close of plan_shim.md. Every phase "
		   "of this port takes it down. If something genuinely had to grow, "
		   "raise the row deliberately and say in the commit what it binds -- "
		   "do not raise it to make a run green.";

	// The other direction: a ceiling far above the tree is a ceiling that has
	// stopped measuring. It is not an error to be well under it, but it is an
	// error to LEAVE it there, because the next 20,000 lines would land free.
	EXPECT_GE( lLines * 100 / ( lCeiling > 0 ? lCeiling : 1 ), 80 )
		<< "the shim is " << lLines << " lines and the ceiling is "
		   << lCeiling << " -- more than a fifth of slack. Work landed and the "
		   "row was not lowered with it. Re-record it now, in the commit that "
		   "did the work.";
}

//-----------------------------------------------------------------------------
// The agreement check. Eleven files already stated their letter in prose,
// written by the lanes that relocated them, and the markers were derived from
// that prose. A fixture that stands in for the tree drifts from it silently,
// so the two are required to agree rather than assumed to.
//-----------------------------------------------------------------------------
TEST( ShimReasonLetters, AMarkerAgreesWithTheProseItWasDerivedFrom )
{
	const std::vector< Marked > m = Markers();
	int nChecked = 0;
	for ( size_t i = 0; i < m.size(); i++ )
	{
		if ( m[ i ].nMarkers != 1 )
			continue;
		const std::vector< std::string > lines = Lines( Read( m[ i ].strPath ) );
		for ( size_t j = 0; j < lines.size() && j < 60; j++ )
		{
			// `REASON (g).` / `reason (f) ===` / `REASON (e),` -- the three
			// spellings the relocating lanes used, in either case, matched as
			// a whole token so that "reason (see below)" is not one. Note
			// that `REASONS (a), (b-adjacent)...` -- physics_shared.cpp, which
			// lists four -- deliberately does NOT match: a file with several
			// reasons has no single prose letter for the marker to agree with.
			const size_t at = FindReasonParen( lines[ j ] );
			if ( at == std::string::npos )
				continue;
			const size_t l = at + 7;
			if ( l + 1 >= lines[ j ].size() || lines[ j ][ l + 1 ] != ')' )
				continue;
			const char cProse = lines[ j ][ l ];
			if ( TextForLetter( cProse ) == NULL || cProse == 'x' )
				continue;
			nChecked++;
			EXPECT_EQ( m[ i ].c, cProse )
				<< Relative( m[ i ].strPath ) << " is marked (" << m[ i ].c
				<< ") and its own header says REASON (" << cProse << "): "
				<< lines[ j ];
			break;
		}
	}
	// TEN after physics_fx.cpp (REASON (g)) was deleted 2026-10-11, ELEVEN again
	// when studio_sequence_timing.cpp, split out of bone_setup.cpp, arrived
	// stating reason (g) the same day.
	// TEN when ragdoll_shared.cpp (REASON (g)) was deleted 2026-10-12 (plan_physics_bones_native_zero Phase 3).
	// NINE when bone_setup.cpp (REASON (g)) was deleted 2026-10-12 (plan_physics_bones_native_zero Phase 3).
	// EIGHT when studio_sequence_timing.cpp (REASON (g)) was deleted 2026-10-12: the descriptor's durations are managed.
	// SEVEN when usercmd.cpp (REASON (f)) was deleted 2026-10-12: the client's codec moved to C#.
	// SIX when studio.cpp (reason (d)) was deleted 2026-10-18: its last consumers read the managed StudioHdr.
	// FIVE when collisionutils.cpp (REASON (g)) was deleted 2026-10-20 (C7 cluster 12 S2): clientshadowmgr.cpp,
	// whose ComputeSeparatingPlane it served, left client.so, and nothing else imported it.
	EXPECT_EQ( nChecked, 5 )
		<< "found " << nChecked << " files stating a letter in prose, and there "
		   "were FIVE once collisionutils.cpp was deleted -- all agreeing with their marker. Fewer means the prose was "
		   "reformatted away, a file was deleted, or the matcher broke -- and "
		   "this agreement check is then checking less than it says. More "
		   "means a file gained a prose letter and should be looked at, not "
		   "absorbed silently.";
}
