//========= SourceSharp — C_Func_Dust is out of the build, the puff is not ====//
//
// A DELETION AND A KEEP IN ONE FILE, which is exactly the shape that goes wrong
// quietly. game/client/c_func_dust.cpp holds two unrelated things:
//
//   THE VOLUME   C_Func_Dust and its CDustEffect — func_dustmotes /
//                func_dustcloud, a brush full of drifting motes. GONE from this
//                build, behind `#ifndef SOURCESHARP`.
//   THE PUFF     FX_Dust — a burst of simple particles at a point, fired by the
//                temp entity and by EffectsClient.o. MOVED, by step 7a, to
//                sourcesharp/game/client/shell_dust_render.cpp, which already
//                owned the managed volume's motes. It is a leaf renderer in
//                plan_entity.md section 6's sense and stays native; what it
//                stopped being is the one body keeping a stock TU on the link
//                line. c_func_dust.cpp is now `$File`-less in
//                client_csharp.vpc and compiles nothing at all.
//   THE TEMP     C_TEDust and TE_Dust. GONE TOO, since C-Phase 3, and behind
//   ENTITY       the same guard: CTEDust is a managed ClientClass now and the
//                client cannot hold two RecvTables of one name (2E-bis). This
//                is a THIRD tenant of the file rather than part of the puff,
//                and separating the two is why this test still says something.
//
// c_func_dust.cpp AND c_func_dust.h HAVE SINCE LEFT THE SOURCE TREE: client.so
// compiles neither, so the assertions that placed the volume and the temp
// entity INSIDE their guards there were culled with them. What remains is the
// puff placed OUTSIDE (compiled) in the shim, the scanner's own fixtures, and
// the shell's replacement for the entity.
//
// WHY THE VOLUME IS DEAD, measured rather than assumed (lane U's ledger, §4
// cluster 6):
//
//   (N) NETWORK   C_Func_Dust pairs on a ServerClass named CFunc_Dust. This
//                 server compiles 52 network class names and that is not one of
//                 them: func_dustmotes and func_dustcloud are FuncDust.cs, and
//                 every managed entity arrives on one of generic_nettable.h's
//                 fourteen size tiers, never under an SDK class name. So the
//                 engine can never build one.
//   (K) STRING    neither side registers a dust classname, so CreateEntityByName
//                 cannot reach it either.
//   (S) SYMBOL    three referrers, and the split was decisive: EffectsClient.o
//                 wanted FX_Dust and c_te.o wanted TE_Dust — the two free
//                 functions — while the ONLY thing that wanted the entity was
//                 our own sourcesharp/game/client/shell_dust_render.cpp, which
//                 did `new C_Func_Dust` to draw a MANAGED dust brush. The
//                 inverted shape: the stock class was alive because our shim
//                 asked for it and for no other reason.
//
// THE THREE THINGS THE SHELL NOW HAS TO DO FOR ITSELF are asserted below, and
// each is a way the motes stop appearing with nothing to say so:
//
//   * THE POINT TEST. A dust brush is a shape, not a box. Stock picked a random
//     point in the bounds and kept it only if the BRUSH was solid there;
//     degrading that to "anywhere in the AABB" would fill the room around an
//     L-shaped volume and look like a lighting bug. Asserted by name.
//   * THE COLLIDEABLE IT ASKS. The shell wraps the managed entity's collideable
//     in one that answers SOLID_BSP with the brush's real bounds, because the
//     entity's own answers CONTENTS_EMPTY for every point inside its brush --
//     measured live, 0 of 400, against 174 of 400 through the wrapper. And the
//     wrapper only has a brush to describe because FuncDust.cs leaves the SERVER
//     brush SOLID_BSP underneath: `SetSolidity(BrushSolidity.None)` adds
//     FSOLID_NOT_SOLID and no more, and its own comment says the client asks the
//     brush this question for every mote. A well-meaning change to a solidity
//     with no shape would answer "no" ten times per spawn attempt and emit
//     nothing, silently. That half is a FIXTURE-AGAINST-NATIVE check: C# cannot
//     see the client, so the agreement is asserted here.
//   * THE PER-FRAME SWEEP. Stock got ClientThink from the engine. Nothing calls
//     a think on a thing that is not an entity, so the shell runs a
//     CClientSystemPerFrame PreRender; without it the spawner never fires. Since
//     plan_cworld_trace.md T4 that base does NOT register itself, so the sweep
//     also needs a kClientSystems[] row -- both halves are asserted, because
//     either one missing empties the volume in exactly the same silent way.
//
// ANCHORS, NOT LINE COUNTS. Every file is asserted to still contain a string
// that identifies it, because a source-scanning test whose file has been renamed
// reads an empty string and reports every absence invariant as satisfied — the
// check that cannot fail.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// `C_Func_Dust` and `FX_Dust` in the paragraphs explaining where each may be, and
// the scanned files' comments do the same. Same rule as
// effects_evicted_test.cpp.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as effects_evicted_test.cpp.
//-----------------------------------------------------------------------------
struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

//-----------------------------------------------------------------------------
// THE REGION SCANNER. Which byte ranges of a file the preprocessor throws away
// when SOURCESHARP is defined.
//
// Line-based and nesting-aware: an `#ifndef SOURCESHARP` opens a region and the
// region ends at ITS `#endif`, not at the first one — the guarded blocks in this
// tree routinely contain `#if defined( CLIENT_DLL )` inside them. `#else` inside
// the SOURCESHARP region ends the excluded half, because the other arm is what
// this build compiles.
//
// Not a preprocessor. It does not evaluate anything, it does not follow
// includes, and it takes no view on any macro but the one it is named after.
//-----------------------------------------------------------------------------
struct Range_t
{
	size_t begin;
	size_t end;
};

// Is this line a preprocessor directive of the given kind? Leading whitespace
// and whitespace after the '#' are both allowed, which is how this tree spells
// nested directives.
bool IsDirective( const std::string &strLine, const char *pszKeyword )
{
	size_t i = 0;
	while ( i < strLine.size() && ( strLine[i] == ' ' || strLine[i] == '\t' ) )
		i++;
	if ( i >= strLine.size() || strLine[i] != '#' )
		return false;
	i++;
	while ( i < strLine.size() && ( strLine[i] == ' ' || strLine[i] == '\t' ) )
		i++;

	const std::string kw( pszKeyword );
	if ( strLine.compare( i, kw.size(), kw ) != 0 )
		return false;

	// Whole word: `#if` must not match `#ifndef`.
	const size_t after = i + kw.size();
	if ( after < strLine.size() )
	{
		const char c = strLine[after];
		if ( ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) || ( c >= '0' && c <= '9' ) || c == '_' )
			return false;
	}
	return true;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) || ( c >= '0' && c <= '9' ) || c == '_';
}

//-----------------------------------------------------------------------------
// The macro named, as a WHOLE IDENTIFIER.
//
// THIS WAS WRONG ON ITS FIRST WRITING and it was wrong in the direction that
// does not show up: `strLine.find( "SOURCESHARP" )`. Every guard in this tree
// spells the macro exactly, so the substring answered correctly on every real
// file — and the mutation that was supposed to prove this test could fail
// renamed the guard to SOURCESHARP_DECOY, which STILL CONTAINS the substring.
// The scanner went on calling the region excluded, the whole suite stayed green
// over an unguarded header, and the proof read as "the test cannot fail".
//
// A needle located by substring where the mutation extends the name is the exact
// shape this port has recorded 44 times. Whole-identifier, therefore, with the
// decoy pinned as a fixture below.
//-----------------------------------------------------------------------------
bool NamesMacro( const std::string &strLine, const char *pszMacro )
{
	const std::string macro( pszMacro );
	size_t at = 0;

	while ( ( at = strLine.find( macro, at ) ) != std::string::npos )
	{
		const bool bLeftOk = ( at == 0 ) || !IsIdentChar( strLine[at - 1] );
		const size_t after = at + macro.size();
		const bool bRightOk = ( after >= strLine.size() ) || !IsIdentChar( strLine[after] );

		if ( bLeftOk && bRightOk )
			return true;

		at += 1;
	}
	return false;
}

bool OpensSourceSharpExclusion( const std::string &strLine )
{
	if ( IsDirective( strLine, "ifndef" ) && NamesMacro( strLine, "SOURCESHARP" ) )
		return true;

	if ( IsDirective( strLine, "if" ) && strLine.find( "!defined" ) != std::string::npos &&
		 NamesMacro( strLine, "SOURCESHARP" ) )
	{
		return true;
	}

	return false;
}

bool OpensAnyConditional( const std::string &strLine )
{
	return IsDirective( strLine, "if" ) || IsDirective( strLine, "ifdef" ) || IsDirective( strLine, "ifndef" );
}

std::vector<Range_t> ExcludedUnderSourceSharp( const std::string &src )
{
	std::vector<Range_t> out;

	size_t pos = 0;
	int nDepthInside = 0; // nesting depth within an open exclusion
	bool bInside = false;
	size_t nBegin = 0;

	while ( pos <= src.size() )
	{
		size_t eol = src.find( '\n', pos );
		if ( eol == std::string::npos )
			eol = src.size();

		const std::string line = src.substr( pos, eol - pos );

		if ( bInside )
		{
			if ( OpensAnyConditional( line ) )
			{
				nDepthInside++;
			}
			else if ( IsDirective( line, "endif" ) )
			{
				if ( nDepthInside == 0 )
				{
					Range_t r;
					r.begin = nBegin;
					r.end = pos;
					out.push_back( r );
					bInside = false;
				}
				else
				{
					nDepthInside--;
				}
			}
			else if ( ( IsDirective( line, "else" ) || IsDirective( line, "elif" ) ) && nDepthInside == 0 )
			{
				// The other arm is the one this build compiles.
				Range_t r;
				r.begin = nBegin;
				r.end = pos;
				out.push_back( r );
				bInside = false;
			}
		}
		else if ( OpensSourceSharpExclusion( line ) )
		{
			bInside = true;
			nDepthInside = 0;
			nBegin = pos;
		}

		if ( eol == src.size() )
			break;
		pos = eol + 1;
	}

	// An unterminated region runs to the end of the file, which is a malformed
	// source rather than something to be lenient about — but reporting it as
	// "excluded to EOF" is what makes the malformation show up as a failed
	// OUTSIDE assertion rather than as silence.
	if ( bInside )
	{
		Range_t r;
		r.begin = nBegin;
		r.end = src.size();
		out.push_back( r );
	}

	return out;
}

bool IsExcluded( const std::vector<Range_t> &ranges, size_t pos )
{
	for ( size_t i = 0; i < ranges.size(); i++ )
	{
		if ( pos >= ranges[i].begin && pos < ranges[i].end )
			return true;
	}
	return false;
}

// Where the needle is, and whether the preprocessor keeps it. Returns npos in
// pOut when the needle is absent, which every caller distinguishes.
enum Placement_t
{
	kAbsent,
	kCompiled,
	kGuardedOut
};

Placement_t Place( const std::string &src, const std::vector<Range_t> &ranges, const char *pszNeedle )
{
	const size_t at = src.find( pszNeedle );
	if ( at == std::string::npos )
		return kAbsent;
	return IsExcluded( ranges, at ) ? kGuardedOut : kCompiled;
}

//=============================================================================//
// THE SCANNER ITSELF, checked before anything is concluded from it.
//
// Not on the real files: on a fixture whose answer is known, so that a scanner
// which had quietly started answering the same thing everywhere is caught by
// this test rather than by the two it is used in.
//=============================================================================//
TEST( DustVerdictScanner, TellsTheTwoSidesOfAGuardApart )
{
	const std::string kFixture = "alpha\n"
								 "#ifndef SOURCESHARP\n"
								 "beta\n"
								 "#if defined( CLIENT_DLL )\n"
								 "gamma\n"
								 "#endif\n"
								 "delta\n"
								 "#endif\n"
								 "epsilon\n";

	const std::vector<Range_t> ranges = ExcludedUnderSourceSharp( kFixture );

	ASSERT_EQ( 1u, ranges.size() ) << "one guarded region, and the nested #if must not close it";

	EXPECT_EQ( kCompiled, Place( kFixture, ranges, "alpha" ) );
	EXPECT_EQ( kGuardedOut, Place( kFixture, ranges, "beta" ) );
	EXPECT_EQ( kGuardedOut, Place( kFixture, ranges, "gamma" ) )
		<< "the nested #endif closed the outer region — everything after a nested "
		   "conditional would read as compiled";
	EXPECT_EQ( kGuardedOut, Place( kFixture, ranges, "delta" ) );
	EXPECT_EQ( kCompiled, Place( kFixture, ranges, "epsilon" ) );
	EXPECT_EQ( kAbsent, Place( kFixture, ranges, "zeta" ) );
}

//-----------------------------------------------------------------------------
// THE DECOY, and it is here because it caught this test out once.
//
// A guard renamed to something that CONTAINS the macro name is the mutation this
// scanner's first writing survived: it matched by substring, went on calling the
// region excluded, and the whole file passed over a header that was no longer
// guarded at all. See NamesMacro.
//-----------------------------------------------------------------------------
TEST( DustVerdictScanner, AMacroThatMerelyCONTAINSTheNameOpensNothing )
{
	const std::string kFixture = "#ifndef SOURCESHARP_DECOY\n"
								 "nothing_is_guarded_here\n"
								 "#endif\n";

	const std::vector<Range_t> ranges = ExcludedUnderSourceSharp( kFixture );

	EXPECT_TRUE( ranges.empty() ) << "SOURCESHARP_DECOY is not SOURCESHARP. Matching the macro by substring "
									 "makes every rename of the guard invisible to this test.";

	EXPECT_EQ( kCompiled, Place( kFixture, ranges, "nothing_is_guarded_here" ) );

	// And the prefix direction, which is the other half of a whole-identifier
	// match: a macro the name is a suffix OF.
	const std::string kPrefixed = "#ifndef SS_SOURCESHARP\n"
								  "also_not_guarded\n"
								  "#endif\n";

	EXPECT_TRUE( ExcludedUnderSourceSharp( kPrefixed ).empty() );
}

TEST( DustVerdictScanner, AnElseEndsTheExcludedHalf )
{
	const std::string kFixture = "#ifndef SOURCESHARP\n"
								 "stock\n"
								 "#else\n"
								 "ours\n"
								 "#endif\n";

	const std::vector<Range_t> ranges = ExcludedUnderSourceSharp( kFixture );

	EXPECT_EQ( kGuardedOut, Place( kFixture, ranges, "stock" ) );
	EXPECT_EQ( kCompiled, Place( kFixture, ranges, "ours" ) ) << "the #else arm is what this build compiles";
}

//=============================================================================//
// THE VERDICT.
//=============================================================================//
//-----------------------------------------------------------------------------
// FIXTURE AGAINST NATIVE. The managed half decides the brush's solidity and
// cannot see the client that depends on it.
//-----------------------------------------------------------------------------
} // namespace
