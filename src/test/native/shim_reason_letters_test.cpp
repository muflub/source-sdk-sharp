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
const size_t kNumLetters = sizeof( kLetters ) / sizeof( kLetters[0] );

const char *TextForLetter( char c )
{
	for ( size_t i = 0; i < kNumLetters; i++ )
		if ( kLetters[i].c == c )
			return kLetters[i].pszText;
	return NULL;
}

// '\0' when the line is not a marker; otherwise the letter.
char MarkerLetterOf( const std::string &strLine )
{
	const size_t nPrefix = strlen( kMarkerPrefix );
	if ( strLine.compare( 0, nPrefix, kMarkerPrefix ) != 0 )
		return '\0'; // must start at column zero
	if ( strLine.size() < nPrefix + 3 )
		return '\0';
	const char c = strLine[nPrefix];
	if ( TextForLetter( c ) == NULL )
		return '\0'; // not one of the nine
	if ( strLine[nPrefix + 1] != ' ' || strLine[nPrefix + 2] != ' ' )
		return '\0'; // exactly two spaces
	return c;
}

struct Marked
{
	std::string strPath;
	char c;
	std::string strText;
	int nMarkers;
};

} // namespace

//-----------------------------------------------------------------------------
// Anti-vacuity. Without this every "every file" assertion below is an
// assertion about the empty set.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Ratchet 2.
//-----------------------------------------------------------------------------
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
	EXPECT_EQ( MarkerLetterOf( "// SS_REASON a a vtable the engine calls through" ), '\0' ) << "one space, not two";
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
//-----------------------------------------------------------------------------
// Ratchet 3: the shim's size.
//-----------------------------------------------------------------------------
