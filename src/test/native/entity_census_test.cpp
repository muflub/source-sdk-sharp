//========= SourceSharp — the entity census, and the ways it could lie ========//
//
// plan_entity.md Phase 0a builds `tools/entity-census`: the §1 table, the
// shim->stock cross-edge list and the §3.16 cast census, taken from the ninja
// object tables and from `nm`, with the result checked in as
// docs/entity-census.txt.
//
// THE CENSUS IS THE PORT'S ONLY ODOMETER. Every later phase reports progress as
// a number in that table going down, so a census that quietly measures the
// wrong thing does not merely mislead -- it lets a phase be declared finished
// on a number nobody moved. §1 records 1,207 server entity members where this
// census reads 1,185, on the same binaries: §1 took the count without a
// symbol-type filter. Two readings, identical in prose, 22 symbols apart. That
// is not a hypothetical failure mode, it is the one already in the plan.
//
// This file therefore holds two kinds of fact:
//
//   (a) THE TREE STILL READS WHAT THE BASELINE SAYS. One fact, and it is the
//       gate: `tools/entity-census` exits 0. It fails the moment a number moves
//       and docs/entity-census.txt does not move with it.
//
//   (b) THAT FIRST FACT CAN FAIL. A checker that always exits 0 passes (a)
//       forever. So the tool is also driven against a DELIBERATELY WRONG
//       baseline, a missing object table and a missing binary, and it must
//       refuse each one -- and the pattern loader is fed a backslash and must
//       hand it back, with a negative control proving the `read -r` is what
//       does the handing back and not an accident of the fixture.
//
// Everything here shells out to the real tool. A C++ reimplementation of the
// census would be a second thing to keep in step, and the bug it could not
// catch is precisely the one where the two drift.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "ss_ci_skip.h"

#include <dirent.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <unistd.h>

#include <ctype.h>

#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the worktree root. buildcpptests runs each binary with src/ as the
// working directory, and that src/ is the compat farm's (farm/<config>/src,
// mounted at its host path in the sniper container), so ".." is the farm --
// the old single-repo root, tools/ link included. The list also lets the binary run by hand from
// the root itself, and keeps "no tree found" distinguishable from "tree found,
// tool missing".
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string szProbe = std::string( kCandidates[ i ] ) + "/tools/entity-census";
		struct stat st;
		if ( stat( szProbe.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string RootPath( const char *pszRelative )
{
	const char *pszRoot = WorktreeRoot();
	if ( !pszRoot )
		return std::string();
	return std::string( pszRoot ) + "/" + pszRelative;
}

std::string ReadFile( const std::string &szPath )
{
	FILE *pFile = fopen( szPath.c_str(), "rb" );
	if ( !pFile )
		return std::string();

	std::string szOut;
	char rgchBuf[ 8192 ];
	size_t cRead;
	while ( ( cRead = fread( rgchBuf, 1, sizeof( rgchBuf ), pFile ) ) > 0 )
		szOut.append( rgchBuf, cRead );
	fclose( pFile );
	return szOut;
}

bool WriteFile( const std::string &szPath, const std::string &szBody )
{
	FILE *pFile = fopen( szPath.c_str(), "wb" );
	if ( !pFile )
		return false;
	const size_t cWrote = fwrite( szBody.data(), 1, szBody.size(), pFile );
	fclose( pFile );
	return cWrote == szBody.size();
}

//-----------------------------------------------------------------------------
// Running a command from the worktree root, capturing stdout+stderr and the
// exit status. popen reports the wait(2) status, so the exit code has to be
// pulled back out of it; a test that compared the raw status against 1 would
// be comparing against 256 and never fail.
//-----------------------------------------------------------------------------
struct Run_t
{
	int m_nExit;
	std::string m_szOutput;
};

Run_t RunAtRoot( const std::string &szCommand )
{
	Run_t run;
	run.m_nExit = -1;

	const char *pszRoot = WorktreeRoot();
	if ( !pszRoot )
		return run;

	const std::string szFull = std::string( "cd '" ) + pszRoot + "' && " + szCommand + " 2>&1";
	FILE *pPipe = popen( szFull.c_str(), "r" );
	if ( !pPipe )
		return run;

	char rgchBuf[ 4096 ];
	while ( fgets( rgchBuf, sizeof( rgchBuf ), pPipe ) )
		run.m_szOutput += rgchBuf;

	const int nStatus = pclose( pPipe );
	run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
	return run;
}

// A scratch path inside the worktree's own tmp, so a parallel lane's run of the
// same test cannot collide on it, and so the file is on the same filesystem the
// tool reads from.
std::string ScratchPath( const char *pszSuffix )
{
	char rgchBuf[ 256 ];
	snprintf( rgchBuf, sizeof( rgchBuf ), "/tmp/entity_census_test_%d_%s", (int)getpid(), pszSuffix );
	return std::string( rgchBuf );
}

const char *const kToolRelative     = "tools/entity-census";
const char *const kPatternsRelative = "tools/entity-census-patterns";
const char *const kBaselineRelative = "docs/entity-census.txt";

// The exit codes the tool promises. 1 is "the tree and the baseline disagree",
// 2 is "I could not take the measurement". A test that accepted "non-zero" for
// both would pass when a drift check turned into a crash.
const int kExitAgrees   = 0;
const int kExitDisagrees = 1;
const int kExitRefused  = 2;

//=============================================================================
// (0) The scanner is itself checked: the subjects exist and are not stubs.
//=============================================================================

TEST( EntityCensus, TheWorktreeRootIsFound )
{
	ASSERT_TRUE( WorktreeRoot() != NULL )
		<< "No directory among '.', '..', '../..' holds tools/entity-census. "
		   "Every other fact in this file would then be vacuously true.";
}

TEST( EntityCensus, TheToolIsExecutable )
{
	const std::string szPath = RootPath( kToolRelative );
	ASSERT_FALSE( szPath.empty() );

	struct stat st;
	ASSERT_EQ( 0, stat( szPath.c_str(), &st ) ) << szPath;
	EXPECT_TRUE( ( st.st_mode & S_IXUSR ) != 0 )
		<< kToolRelative << " is not executable, so every caller of it silently "
		   "gets a shell error instead of a census.";
	EXPECT_GT( ReadFile( szPath ).size(), 4000u )
		<< kToolRelative << " came back too small to be the census tool.";
}

TEST( EntityCensus, ThePatternFileCarriesTheCastFamilies )
{
	const std::string szBody = ReadFile( RootPath( kPatternsRelative ) );
	ASSERT_FALSE( szBody.empty() ) << kPatternsRelative << " read back empty.";

	// §3.16 names eleven rows; the tool splits reinterpret_cast and assert_cast
	// apart (twelve) and adds ToEngineIface for the direction §3.16 does not
	// cover (thirteen). Fewer means a family was dropped and its casts stopped
	// being counted -- silently, because a family nobody counts reads as zero.
	int cFamilies = 0;
	size_t iPos = 0;
	while ( iPos < szBody.size() )
	{
		const size_t iEnd = szBody.find( '\n', iPos );
		const std::string szLine = szBody.substr( iPos, iEnd == std::string::npos ? std::string::npos : iEnd - iPos );
		if ( !szLine.empty() && szLine[ 0 ] != '#' && szLine.find( '\t' ) != std::string::npos )
			cFamilies++;
		if ( iEnd == std::string::npos )
			break;
		iPos = iEnd + 1;
	}
	EXPECT_GE( cFamilies, 13 ) << "only " << cFamilies << " cast families in " << kPatternsRelative;
}

TEST( EntityCensus, TheEntityToInterfaceCastFamilyIsStillCounted )
{
	// tools/cast-audit owned this bug class and was deleted at b6c429e1, so the
	// census is the only thing left that counts these. An explicit cast from an
	// entity to IHandleEntity / IServerEntity / IClientRenderable is a plain
	// derived->base conversion WHILE the base relationship holds, and becomes a
	// silent reinterpret_cast the moment this plan removes it -- no diagnostic,
	// at any -W level, on either side of the change. Dropping the family would
	// take the whole class back to invisible, which is why it has its own fact
	// rather than only the family-count floor above.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --dump-patterns" );
	ASSERT_EQ( kExitAgrees, run.m_nExit ) << run.m_szOutput;
	ASSERT_NE( std::string::npos, run.m_szOutput.find( "ToEngineIface\t" ) )
		<< "the ToEngineIface family is gone from the loaded pattern list:\n"
		<< run.m_szOutput;

	// And the family must still MATCH something. A regex that survives in the
	// file but matches nothing is the same hole with a row printed over it.
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );
	const size_t iRow = szBody.find( "\nToEngineIface" );
	ASSERT_NE( std::string::npos, iRow ) << "no ToEngineIface row in the census.";
	const size_t iEol = szBody.find( '\n', iRow + 1 );
	const std::string szLine = szBody.substr( iRow + 1, iEol - iRow - 1 );
	EXPECT_EQ( std::string::npos, szLine.find( "0 in 0 files        0 in 0 files" ) )
		<< "ToEngineIface matched nothing on a whole side: " << szLine;
}

TEST( EntityCensus, TheCommittedBaselineIsSubstantial )
{
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() ) << kBaselineRelative << " read back empty.";
	EXPECT_GT( szBody.size(), 4000u );
	EXPECT_NE( std::string::npos, szBody.find( "WHERE THE TREE STANDS" ) );
	EXPECT_NE( std::string::npos, szBody.find( "CAST CENSUS" ) );
	EXPECT_NE( std::string::npos, szBody.find( "CROSS-EDGES" ) );
}

TEST( EntityCensus, TheBaselineRecordsWhenAndWhereItWasMeasured )
{
	// A census with no provenance is a number with no date on it, which is how
	// §1 came to carry counts from a tree nobody could name any more.
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );
	EXPECT_NE( std::string::npos, szBody.find( "# commit " ) );
	EXPECT_NE( std::string::npos, szBody.find( "# measured " ) );
}

TEST( EntityCensus, TheBaselineSaysWhichNumbersAreFloors )
{
	// A count grepped from the linker is a floor, not a proof, and a reader who
	// does not know which is which will read "0 cross-edges" as "dead code".
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );
	EXPECT_NE( std::string::npos, szBody.find( "FLOOR" ) );
	EXPECT_NE( std::string::npos, szBody.find( "SUFFICIENT" ) );
}

//-----------------------------------------------------------------------------
// The §2 CAST CENSUS section of the committed baseline, and the rows in it.
// Split out so the two facts below read the same text: one asserts what the
// rows say, the other asserts that the rows are still being MEASURED at all.
//-----------------------------------------------------------------------------
std::string CastCensusSection()
{
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	if ( szBody.empty() )
		return std::string();
	const size_t iCast = szBody.find( "CAST CENSUS" );
	const size_t iEnd  = szBody.find( "CROSS-EDGES", ( iCast == std::string::npos ) ? 0 : iCast );
	if ( iCast == std::string::npos || iEnd == std::string::npos )
		return std::string();
	return szBody.substr( iCast, iEnd - iCast );
}

// Every data row of that section -- the ones with counts on them, not the
// header and not the '#' preamble.
std::vector< std::string > CastCensusRows()
{
	std::vector< std::string > vecRows;
	const std::string szSection = CastCensusSection();
	size_t iPos = 0;
	while ( iPos < szSection.size() )
	{
		const size_t iEol = szSection.find( '\n', iPos );
		const std::string szLine =
			szSection.substr( iPos, iEol == std::string::npos ? std::string::npos : iEol - iPos );
		if ( !szLine.empty() && szLine[ 0 ] != '#' && szLine.find( " in " ) != std::string::npos )
			vecRows.push_back( szLine );
		if ( iEol == std::string::npos )
			break;
		iPos = iEol + 1;
	}
	return vecRows;
}

// How many of a row's four columns read "0 in 0 files".
int ZeroColumns( const std::string &szRow )
{
	int cZero = 0;
	size_t iFind = szRow.find( "0 in 0 files" );
	while ( iFind != std::string::npos )
	{
		cZero++;
		iFind = szRow.find( "0 in 0 files", iFind + 1 );
	}
	return cZero;
}

//-----------------------------------------------------------------------------
// THIS FACT WAS `TheCastCensusIsNotAllZero` AND IT HAS BEEN TURNED ROUND.
//
// It read: "no family may read zero in all four columns -- that is the exact
// shape a backslash-eating pattern loader leaves behind, the mangled regex
// still a valid regex that matches nothing". For the whole port that was a
// sound proxy, because the tree had thousands of cast sites and a family at
// zero could only be the loader.
//
// It is not sound any more, and the census is the document that says why: the
// port drove the gated families to zero, so ELEVEN of the thirteen rows read
// "0 in 0 files" four times over, and the fact went red for the success it was
// written to be a proxy for. The proxy and the property have come apart.
//
// SO THE TWO ARE SPLIT. This half asserts the PROPERTY -- the gated families
// read zero, with the one exception the allowlist names and no other. The half
// below asserts the LOADER IS STILL MATCHING, which is what the old fact was
// really for, against the two rows that are still non-zero by design.
// Together they are strictly sharper than the row they replace: the old one
// would have passed on a census where every gated family read one site.
//-----------------------------------------------------------------------------
TEST( EntityCensus, EveryGatedCastFamilyReadsZeroExceptTheAllowlistedResolvers )
{
	const std::vector< std::string > vecRows = CastCensusRows();
	ASSERT_GE( vecRows.size(), (size_t)13 )
		<< "the cast census has fewer rows than the pattern file has families; "
		   "a section that stopped printing rows would satisfy the loop below by "
		   "never entering it.";

	for ( size_t i = 0; i < vecRows.size(); i++ )
	{
		const std::string &szRow = vecRows[ i ];

		// ToEngineIface is the OTHER direction (entity -> engine interface) and
		// §3.16 does not gate it. It is reported, never driven to zero.
		if ( szRow.compare( 0, strlen( "ToEngineIface" ), "ToEngineIface" ) == 0 )
			continue;

		// static_cast carries the two allowlisted slot-table resolvers, one per
		// side, and both are SHIM. The columns are
		// "server stock  server shim  client stock  client shim", so the two
		// stock columns must still read zero: an allowlisted line is one exact
		// line inside the shim, and the same spelling appearing in a stock TU is
		// not covered by anything.
		if ( szRow.compare( 0, strlen( "static_cast" ), "static_cast" ) == 0 )
		{
			EXPECT_EQ( 2, ZeroColumns( szRow ) )
				<< "the static_cast row should read zero in the two STOCK columns "
				   "and one site in each SHIM column -- the two resolvers on "
				   "tools/entity-gates-allowlist: " << szRow;
			EXPECT_NE( std::string::npos, szRow.find( "1 in 1 files" ) )
				<< "neither resolver is counted; the allowlist excuses a site the "
				   "census cannot see: " << szRow;
			continue;
		}

		EXPECT_EQ( 4, ZeroColumns( szRow ) )
			<< "a gated cast family is still counted on this tree. "
			   "plan_entity.md's clause 3 says there are no entity casts left "
			   "except the resolvers: " << szRow;
	}
}

TEST( EntityCensus, TheCastCensusIsStillMatchingRatherThanQuietlyMatchingNothing )
{
	// THE HAZARD THE FACT ABOVE USED TO CARRY, kept and re-pointed. A pattern
	// loader that eats backslashes leaves every regex valid and matching
	// nothing, and on THIS tree the result -- an all-zero cast census -- is
	// exactly what a finished port looks like. So "all zero" can no longer be
	// the alarm; two rows that are non-zero BY DESIGN have to be, and if either
	// falls to zero the census has stopped measuring rather than the port having
	// moved.
	//
	// The direct structural checks on the same hazard are
	// TheLoaderHandsBackTheBackslashesItWasGiven / ABareReadWouldHaveEatenThem /
	// TheRealPatternFileStillContainsBackslashes, further down. This is the
	// end-to-end one: those three read the loader, this reads its output.
	const std::string szSection = CastCensusSection();
	ASSERT_FALSE( szSection.empty() ) << "no CAST CENSUS section in the baseline.";
	ASSERT_NE( std::string::npos, szSection.find( "dynamic_cast" ) );

	const std::vector< std::string > vecRows = CastCensusRows();
	int cLiveRows = 0;
	for ( size_t i = 0; i < vecRows.size(); i++ )
	{
		if ( ZeroColumns( vecRows[ i ] ) < 4 )
			cLiveRows++;
	}

	EXPECT_GE( cLiveRows, 2 )
		<< "every row in the cast census reads zero in all four columns. On a "
		   "finished tree that is indistinguishable from the pattern loader "
		   "having eaten its own regexes -- which is the bug this fact was "
		   "written for. Two rows are non-zero by design: static_cast (the two "
		   "allowlisted resolvers) and ToEngineIface (the ungated other "
		   "direction).\n" << szSection;
}

//-----------------------------------------------------------------------------
// Pulls the two integers off a census row: "<label>  <server>  <client>".
// Returns false when the row is absent or unparseable, so a fact built on it
// fails loudly instead of comparing two zeroes.
//-----------------------------------------------------------------------------
bool ReadRow( const std::string &szBody, const char *pszNeedle, long *pnServer, long *pnClient )
{
	const size_t iFound = szBody.find( pszNeedle );
	if ( iFound == std::string::npos )
		return false;

	// A needle may open with '\n' to anchor itself to the start of a line --
	// several rows' labels also occur inside the file's '#' preamble. Step over
	// it before looking for the row's END, or the row is the empty string
	// between one newline and itself and every number parse comes back short.
	const size_t iRow = ( szBody[ iFound ] == '\n' ) ? iFound + 1 : iFound;
	const size_t iEol = szBody.find( '\n', iRow );
	const std::string szLine = szBody.substr( iRow, iEol == std::string::npos ? std::string::npos : iEol - iRow );

	std::vector< long > vecNumbers;
	for ( size_t i = 0; i < szLine.size(); )
	{
		if ( isdigit( (unsigned char)szLine[ i ] ) )
		{
			char *pszEnd = NULL;
			vecNumbers.push_back( strtol( szLine.c_str() + i, &pszEnd, 10 ) );
			i = (size_t)( pszEnd - szLine.c_str() );
		}
		else
		{
			i++;
		}
	}
	if ( vecNumbers.size() < 2 )
		return false;

	*pnServer = vecNumbers[ vecNumbers.size() - 2 ];
	*pnClient = vecNumbers[ vecNumbers.size() - 1 ];
	return true;
}

TEST( EntityCensus, TheBaselineCarriesBothReadingsOfTheEntityMemberCount )
{
	// plan_entity.md §1 says 1,207 / 602; this census says 1,185 / 584. Neither
	// is stale -- §1 took the count with no symbol-type filter. Carrying both
	// readings is what stops a later lane reporting a 22-symbol "regression"
	// that is really the filter doing its job.
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );

	long nFilteredServer = -1, nFilteredClient = -1;
	long nLooseServer = -1, nLooseClient = -1;
	ASSERT_TRUE( ReadRow( szBody, "\nCBaseEntity:: / C_BaseEntity:: members", &nFilteredServer, &nFilteredClient ) )
		<< "the census has no type-filtered entity-member row.";
	ASSERT_TRUE( ReadRow( szBody, "with NO symbol-type filter", &nLooseServer, &nLooseClient ) )
		<< "the census stopped carrying §1's own reading, so the two can no "
		   "longer be reconciled.";

	// The filter can only ever REMOVE rows. If the unfiltered count came back
	// smaller, the two rows are not measuring the same thing.
	EXPECT_LE( nFilteredServer, nLooseServer );
	EXPECT_LE( nFilteredClient, nLooseClient );
}

TEST( EntityCensus, TheBaselineCarriesBothReadingsOfTheEntityListCount )
{
	// The same story on the entity list: §1's row leaves CBaseEntityList:: out
	// of the pattern; Appendix A's command puts it in. 33/91 and 62/120 are the
	// same binaries read two ways.
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );

	long nWideServer = -1, nWideClient = -1;
	long nNarrowServer = -1, nNarrowClient = -1;
	ASSERT_TRUE( ReadRow( szBody, "\nentity-list members", &nWideServer, &nWideClient ) );
	ASSERT_TRUE( ReadRow( szBody, "without CBaseEntityList::", &nNarrowServer, &nNarrowClient ) );

	// Dropping a term from the pattern can only ever match fewer symbols.
	EXPECT_LE( nNarrowServer, nWideServer );
	EXPECT_LE( nNarrowClient, nWideClient );
}

//=============================================================================
// (1) THE GATE. The tree still reads what the baseline says.
//=============================================================================

TEST( EntityCensus, TheTreeAgreesWithTheCommittedBaseline )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	// The baseline is the DEBUG build's (-O2 inlines cross-edges away), so a
	// release run measures farm/debug too -- the VPC-era tool read
	// sdk_csharp_debug.ninja whatever the farm. RunAtRoot runs at
	// farm/<config>, so ../debug is farm/debug.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) +
		" --build-dir ../debug/build --bindir ../debug/game/mod_sharp/bin/linux64" );
	EXPECT_EQ( kExitAgrees, run.m_nExit )
		<< "\n" << run.m_szOutput
		<< "\nThe census moved and " << kBaselineRelative << " did not. If the "
		   "move is the intended one, re-take the baseline:\n"
		   "    tools/entity-census --write && git add " << kBaselineRelative << "\n"
		   "If it is not, something changed that nobody meant to change.\n"
		   "(Exit 2 means the tree is not built: src/buildsourcesharp debug.)";
}

//=============================================================================
// (2) THAT GATE CAN FAIL. Every arm below drives the tool into a state where
//     it MUST refuse, because a checker that cannot refuse is not a checker.
//=============================================================================

TEST( EntityCensus, ADisagreeingBaselineIsRejected )
{
	// The mutation is on the BASELINE rather than on the tree, so the fact is
	// self-contained: it needs no source file edited and no rebuild, and it
	// exercises exactly the comparison the gate above relies on.
	const std::string szBody = ReadFile( RootPath( kBaselineRelative ) );
	ASSERT_FALSE( szBody.empty() );

	// "\nobjects linked" and not "objects linked": the phrase also occurs inside
	// the file's own '#' preamble, and '#' lines are NOT compared -- perturbing
	// one of those produced a baseline the tool cheerfully accepted, which is
	// how this fact came to be written the way it is.
	const size_t iRow = szBody.find( "\nobjects linked" ) + 1;
	ASSERT_NE( std::string::npos, szBody.find( "\nobjects linked" ) )
		<< "the baseline has no 'objects linked' data row to perturb.";
	const size_t iEol = szBody.find( '\n', iRow );
	ASSERT_NE( std::string::npos, iEol );

	std::string szMutated = szBody;
	szMutated.replace( iRow, iEol - iRow, "objects linked                    999999    999999" );
	ASSERT_NE( szBody, szMutated ) << "the mutation changed nothing, so it proves nothing.";

	const std::string szTmp = ScratchPath( "baseline.txt" );
	ASSERT_TRUE( WriteFile( szTmp, szMutated ) );

	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --baseline '" + szTmp + "'" );
	unlink( szTmp.c_str() );

	EXPECT_EQ( kExitDisagrees, run.m_nExit )
		<< "the tool accepted a baseline claiming 999999 objects:\n" << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "999999" ) )
		<< "the tool refused, but did not print WHICH line disagreed:\n" << run.m_szOutput;
}

TEST( EntityCensus, AMissingObjectTableIsRefusedRatherThanCountedAsZero )
{
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --print --build-dir /nonexistent/build" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "no object table" ) ) << run.m_szOutput;
}

TEST( EntityCensus, AMissingBinaryIsRefusedRatherThanPrintedAsADash )
{
	// The failure this forbids: an unbuilt tree produces a census of dashes,
	// somebody runs --write, and the dashes become the baseline.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --print --bindir /nonexistent/bin" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "build first" ) ) << run.m_szOutput;
}

TEST( EntityCensus, AnEmptyPatternFileIsRefused )
{
	const std::string szTmp = ScratchPath( "empty-patterns" );
	ASSERT_TRUE( WriteFile( szTmp, "# nothing but a comment\n\n" ) );

	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --dump-patterns --patterns '" + szTmp + "'" );
	unlink( szTmp.c_str() );

	EXPECT_EQ( kExitRefused, run.m_nExit )
		<< "an empty pattern list would report zero casts everywhere:\n" << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "produced no patterns" ) ) << run.m_szOutput;
}

TEST( EntityCensus, APatternLineWithNoTabIsRefused )
{
	// Spaces instead of a TAB is the natural way to mistype this file, and the
	// silent reading of it is "a family whose regex is the empty string", which
	// matches every line of every file.
	const std::string szTmp = ScratchPath( "spaced-patterns" );
	ASSERT_TRUE( WriteFile( szTmp, "cstyle dynamic_cast\n" ) );

	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --dump-patterns --patterns '" + szTmp + "'" );
	unlink( szTmp.c_str() );

	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "TAB separated" ) ) << run.m_szOutput;
}

//=============================================================================
// (3) THE BACKSLASH. plan_entity.md Appendix A records that the cast census was
//     once taken with a loader that used bare `read`, which ate every backslash
//     in the pattern file and made every count wrong -- silently, because the
//     mangled regexes are still valid regexes.
//=============================================================================

// The probe carries every backslash form the real pattern file uses: \( \s \*
// \b \) — and a `\<` for good measure, because that is the character bare
// `read` treats as a line continuation when it lands at end of line.
const char *const kProbeRegex = "\\bTo(BasePlayer|BaseFlex)\\s*\\(\\s*C_?Base[A-Za-z]*\\s*\\*\\s*\\)";

TEST( EntityCensus, TheLoaderHandsBackTheBackslashesItWasGiven )
{
	const std::string szTmp = ScratchPath( "probe-patterns" );
	const std::string szLine = std::string( "probe\t" ) + kProbeRegex + "\n";
	ASSERT_TRUE( WriteFile( szTmp, szLine ) );

	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --dump-patterns --patterns '" + szTmp + "'" );
	unlink( szTmp.c_str() );

	ASSERT_EQ( kExitAgrees, run.m_nExit ) << run.m_szOutput;
	EXPECT_EQ( szLine, run.m_szOutput )
		<< "the pattern went in as\n  " << szLine
		<< "and came back as\n  " << run.m_szOutput
		<< "\nA `read` without -r eats the backslashes and every cast count is "
		   "then wrong. See tools/entity-census-patterns.";
}

TEST( EntityCensus, ABareReadWouldHaveEatenThem )
{
	// THE NEGATIVE CONTROL for the fact above. Without it, that fact passes
	// just as happily on a fixture that never had a backslash in it, and on a
	// shell where `read` happened not to strip them -- neither of which is
	// evidence that the tool's -r is doing anything.
	const std::string szTmp = ScratchPath( "control-patterns" );
	const std::string szLine = std::string( "probe\t" ) + kProbeRegex + "\n";
	ASSERT_TRUE( WriteFile( szTmp, szLine ) );

	// Deliberately WITHOUT -r: the bug, reproduced.
	// The IFS below is a LITERAL TAB, not the two characters backslash-t: the
	// point of the control is that it differs from the tool in exactly one way,
	// the missing -r.
	const std::string szCmd =
		"bash -c 'while IFS=\"\t\" read n r; do printf \"%s\\n\" \"$r\"; done < " + szTmp + "'";
	const Run_t run = RunAtRoot( szCmd );
	unlink( szTmp.c_str() );

	ASSERT_EQ( kExitAgrees, run.m_nExit ) << run.m_szOutput;
	EXPECT_EQ( std::string::npos, run.m_szOutput.find( '\\' ) )
		<< "a bare `read` returned the backslashes intact, so the -r in the "
		   "tool is not what is keeping them and the fact above proves nothing. "
		   "Got: " << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "bTo(BasePlayer|BaseFlex)" ) )
		<< "the control did not read the fixture at all. Got: " << run.m_szOutput;
}

TEST( EntityCensus, TheRealPatternFileStillContainsBackslashes )
{
	// The fixture facts above stay green if somebody rewrites the real pattern
	// file into backslash-free regexes -- which would change what the census
	// counts without failing anything. This is the fact that notices.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --dump-patterns" );
	ASSERT_EQ( kExitAgrees, run.m_nExit ) << run.m_szOutput;

	int cBackslashes = 0;
	for ( size_t i = 0; i < run.m_szOutput.size(); i++ )
		if ( run.m_szOutput[ i ] == '\\' )
			cBackslashes++;

	EXPECT_GT( cBackslashes, 20 )
		<< "only " << cBackslashes << " backslashes survived the loader over the "
		   "real pattern file:\n" << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "\\s*" ) ) << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "\\b" ) ) << run.m_szOutput;
}

} // namespace
