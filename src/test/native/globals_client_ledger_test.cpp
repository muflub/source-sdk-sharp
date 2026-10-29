//====== SourceSharp — the client DLL's static state, and it is measured =====//
//
// The client's half of plan_globals.md step G0. globals_ledger_test.cpp does
// this for the server against docs/globals-ledger.tsv; this file does it for
// the client against docs/globals-client-ledger.tsv and
// docs/globals-client-ratchet.tsv, with the SAME tool (`tools/globals-census
// --side client`) and not a second one. The server's files belong to the lanes
// ruling server rows, so nothing here reads or writes them.
//
// STATE IS A SECTION, NOT A KEYWORD: readelf over the objects on the client
// link edge. A function-local static is state; so is a static inside an inline
// accessor; so is every DECLARE_HUDELEMENT / DECLARE_CLIENT_EFFECT node, which
// is reached BY NAME and links nothing.
//
//=============================================================================
// BOTH DIRECTIONS, AND A POSITIVE CONTROL FOR EACH
//=============================================================================
//
//   up     a symbol with no ledger row reddens.
//   stale  a ledger row whose symbol is gone reddens -- against the symbol
//          table (the tool) AND against the tree (every row names a TU and
//          this test opens it).
//   rot    a ratchet row whose SUBJECT is gone reddens. A keep-list nobody
//          re-derives rots silently; that exact failure was caught at a merge
//          on this project on 2026-09-20.
//
// A gate that shells out and asserts exit 0 is a check that cannot fail if
// the tool stops reading. So each direction has a control run on a COPY with
// one deliberate defect, and each control requires EXACTLY ONE failure of the
// kind it created -- not merely a non-zero exit, which any breakage produces.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *kLedger = "docs/globals-client-ledger.tsv";
const char *kRatchet = "docs/globals-client-ratchet.tsv";

//-----------------------------------------------------------------------------
// The worktree root. buildcpptests runs each binary from src/; in the sniper
// container the tree is at /my_mod. A short candidate list, NOT an upward walk:
// a walk escapes a git worktree into the main checkout and then answers about
// the wrong tree.
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string tool = std::string( kCandidates[i] ) + "/tools/globals-census";
		const std::string src = std::string( kCandidates[i] ) + "/src";
		if ( stat( tool.c_str(), &st ) == 0 && stat( src.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[i];
	}
	return NULL;
}

std::string ReadFile( const std::string &szPath )
{
	std::ifstream f( szPath.c_str(), std::ios::binary );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string ReadFromRoot( const std::string &szRel )
{
	const char *pszRoot = WorktreeRoot();
	if ( pszRoot == NULL )
		return std::string();
	return ReadFile( std::string( pszRoot ) + "/" + szRel );
}

bool ExistsUnderRoot( const std::string &szRel )
{
	const char *pszRoot = WorktreeRoot();
	if ( pszRoot == NULL || szRel.empty() )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + szRel ).c_str(), &st ) == 0;
}

std::vector<std::string> Lines( const std::string &szText )
{
	std::vector<std::string> out;
	std::string cur;
	for ( size_t i = 0; i < szText.size(); i++ )
	{
		if ( szText[i] == '\n' )
		{
			out.push_back( cur );
			cur.clear();
		}
		else if ( szText[i] != '\r' )
		{
			cur += szText[i];
		}
	}
	if ( !cur.empty() )
		out.push_back( cur );
	return out;
}

std::vector<std::string> Split( const std::string &szLine, char c )
{
	std::vector<std::string> out;
	std::string cur;
	for ( size_t i = 0; i < szLine.size(); i++ )
	{
		if ( szLine[i] == c )
		{
			out.push_back( cur );
			cur.clear();
		}
		else
		{
			cur += szLine[i];
		}
	}
	out.push_back( cur );
	return out;
}

bool IsLedgerDataLine( const std::string &szLine )
{
	if ( szLine.empty() || szLine[0] == '#' )
		return false;
	const std::vector<std::string> f = Split( szLine, '\t' );
	return f.size() >= 11 && f[0] != "home";
}

//-----------------------------------------------------------------------------
// popen reports the wait(2) status; the exit code has to be pulled back out,
// or a comparison against 1 is a comparison against 256 and never fails.
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
	if ( pszRoot == NULL )
		return run;

	const std::string szFull = std::string( "cd '" ) + pszRoot + "' && " + szCommand + " 2>&1";
	FILE *pPipe = popen( szFull.c_str(), "r" );
	if ( pPipe == NULL )
		return run;

	char rgchBuf[4096];
	while ( fgets( rgchBuf, sizeof( rgchBuf ), pPipe ) )
		run.m_szOutput += rgchBuf;

	const int nStatus = pclose( pPipe );
	run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
	return run;
}

//-----------------------------------------------------------------------------
// The ratchet: `metric \t subject \t ceiling`. -1 when absent, which every
// caller ASSERTs on; a ceiling that quietly read as zero would pass for the
// wrong reason.
//-----------------------------------------------------------------------------
struct Ratchet_t
{
	std::string m_szMetric;
	std::string m_szSubject;
	long m_lCeiling;
};

std::vector<Ratchet_t> RatchetRows( const std::string &szText )
{
	std::vector<Ratchet_t> out;
	const std::vector<std::string> lines = Lines( szText );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[i].empty() || lines[i][0] == '#' )
			continue;
		const std::vector<std::string> f = Split( lines[i], '\t' );
		if ( f.size() < 3 )
			continue;
		Ratchet_t row;
		row.m_szMetric = f[0];
		row.m_szSubject = f[1];
		row.m_lCeiling = strtol( f[2].c_str(), NULL, 10 );
		out.push_back( row );
	}
	return out;
}

long RatchetCeiling( const char *pszMetric )
{
	const std::vector<Ratchet_t> rows = RatchetRows( ReadFromRoot( kRatchet ) );
	for ( size_t i = 0; i < rows.size(); i++ )
		if ( rows[i].m_szMetric == pszMetric )
			return rows[i].m_lCeiling;
	return -1;
}

// The metrics this file reads. A ratchet row NOT in this list is a ceiling
// nothing enforces, which is its own kind of rot.
const char *kMetrics[] = {
	"client_globals_symbols_with_no_row",
	"client_globals_rows_with_no_symbol",
	"client_globals_link_level",
	"client_globals_unruled",
	"client_globals_floor",
	"client_globals_objects_with_static_init",
	"client_globals_reconcile_unattributed",
};
const size_t kNumMetrics = sizeof( kMetrics ) / sizeof( kMetrics[0] );

bool IsKnownMetric( const std::string &sz )
{
	for ( size_t i = 0; i < kNumMetrics; i++ )
		if ( sz == kMetrics[i] )
			return true;
	return false;
}

//-----------------------------------------------------------------------------
// The ratchet's rot check as a function of the ratchet TEXT, so the control
// below can drive it over a copy. Returns the number of problems: a required
// metric with no row, a row whose subject is not in the tree, a row whose
// metric nothing reads.
//-----------------------------------------------------------------------------
size_t RatchetProblems( const std::string &szText, std::string *pszFirst )
{
	const std::vector<Ratchet_t> rows = RatchetRows( szText );
	size_t cBad = 0;
	for ( size_t m = 0; m < kNumMetrics; m++ )
	{
		bool bFound = false;
		for ( size_t i = 0; i < rows.size(); i++ )
			bFound = bFound || rows[i].m_szMetric == kMetrics[m];
		if ( !bFound )
		{
			cBad++;
			if ( pszFirst && pszFirst->empty() )
				*pszFirst = std::string( "no row for " ) + kMetrics[m];
		}
	}
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( !ExistsUnderRoot( rows[i].m_szSubject ) )
		{
			cBad++;
			if ( pszFirst && pszFirst->empty() )
				*pszFirst = rows[i].m_szMetric + " caps '" + rows[i].m_szSubject + "', which is not in the tree";
		}
		if ( !IsKnownMetric( rows[i].m_szMetric ) )
		{
			cBad++;
			if ( pszFirst && pszFirst->empty() )
				*pszFirst = rows[i].m_szMetric + " is a ceiling nothing reads";
		}
	}
	return cBad;
}

//-----------------------------------------------------------------------------
// A `key \t value` line of a census report; -1 when absent, so a report whose
// shape changed reddens instead of comparing against nothing.
//-----------------------------------------------------------------------------
long ReportedNumber( const std::string &szOutput, const char *pszKey )
{
	const std::vector<std::string> lines = Lines( szOutput );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[i].empty() || lines[i][0] == '#' )
			continue;
		const std::vector<std::string> f = Split( lines[i], '\t' );
		if ( f.size() < 2 )
			continue;
		std::string key = f[0];
		while ( !key.empty() && key[0] == ' ' )
			key.erase( 0, 1 );
		if ( key == pszKey )
			return strtol( f[1].c_str(), NULL, 10 );
	}
	return -1;
}

struct LedgerRow_t
{
	std::string m_szHome;
	std::string m_szMangled;
	std::string m_szName;
	std::string m_szTu;
	std::string m_szDisposition;
	std::string m_szReason;
};

std::vector<LedgerRow_t> LedgerRows()
{
	std::vector<LedgerRow_t> out;
	const std::vector<std::string> lines = Lines( ReadFromRoot( kLedger ) );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( !IsLedgerDataLine( lines[i] ) )
			continue;
		const std::vector<std::string> f = Split( lines[i], '\t' );
		LedgerRow_t row;
		row.m_szHome = f[0];
		row.m_szMangled = f[1];
		row.m_szName = f[2];
		row.m_szTu = f[3];
		row.m_szDisposition = f[8];
		row.m_szReason = f[10];
		out.push_back( row );
	}
	return out;
}

const char *kDispositions[] = { "GONE", "DEAD", "CONST", "MANAGED", "MEMBER", "FLOOR", "UNRULED" };

bool IsADisposition( const std::string &sz )
{
	for ( size_t i = 0; i < sizeof( kDispositions ) / sizeof( kDispositions[0] ); i++ )
		if ( sz == kDispositions[i] )
			return true;
	return false;
}

//-----------------------------------------------------------------------------
// The runs, taken once each: 253 objects is the expensive part of this binary.
//-----------------------------------------------------------------------------
const Run_t &Gate()
{
	static Run_t run = RunAtRoot( "python3 tools/globals-census gate --side client" );
	return run;
}

const Run_t &Summary()
{
	static Run_t run = RunAtRoot( "python3 tools/globals-census summary --side client" );
	return run;
}

// `--config auto`: lib/public/linux64/*.a is not config-qualified, so only the
// config built LAST is reconcilable, and the tool picks it rather than
// reddening because somebody ran `make release`.
const Run_t &Reconcile()
{
	static Run_t run = RunAtRoot( "python3 tools/globals-census reconcile --side client --config auto" );
	return run;
}

//-----------------------------------------------------------------------------
// A copy of the real ledger with one edit, gated. The copy lives in a private
// temp directory, never in the tree: tools/valve-text-audit reads every
// untracked file in the tree, and a copy written under tools/ beside it was
// listed, deleted, then read (a FileNotFoundError in make test-full,
// 2026-10-04). The gate takes --ledger as a path, so an absolute one works.
//-----------------------------------------------------------------------------
Run_t GateOverCopy( const std::string &szBody, const char *pszName )
{
	Run_t run;
	run.m_nExit = -1;
	const char *pszTmp = getenv( "TMPDIR" );
	std::string szDir = std::string( pszTmp && *pszTmp ? pszTmp : "/tmp" ) + "/globals-client-ledger.XXXXXX";
	std::vector<char> dir( szDir.begin(), szDir.end() );
	dir.push_back( '\0' );
	if ( mkdtemp( dir.data() ) == NULL )
		return run;
	std::string szPath = std::string( dir.data() ) + "/" + pszName;
	{
		std::ofstream f( szPath.c_str(), std::ios::binary );
		f << szBody;
		if ( !f.good() )
		{
			unlink( szPath.c_str() );
			rmdir( dir.data() );
			return run;
		}
	}
	run = RunAtRoot( std::string( "python3 tools/globals-census gate "
								  "--side client --ledger " ) +
					 szPath );
	unlink( szPath.c_str() );
	rmdir( dir.data() );
	return run;
}

//=============================================================================
// Which tree the gate answered about.
//=============================================================================

TEST( GlobalsClientLedger, TheGateReadsTheClientLinkEdgeAndNotTheServers )
{
	const Run_t &run = Gate();
	ASSERT_NE( run.m_nExit, -1 ) << "the gate could not be run at all";
	EXPECT_NE( run.m_szOutput.find( "# side\tclient" ), std::string::npos )
		<< "the gate's provenance does not say client:\n"
		<< run.m_szOutput;
	// The server's link level is in the hundreds and the client's in the
	// thousands; an equal ledger-row count would mean both runs read one file.
	EXPECT_EQ( ReportedNumber( run.m_szOutput, "ledger_rows" ), (long)LedgerRows().size() )
		<< "the gate did not read " << kLedger
		<< " -- `--side client` "
		   "fell back to another side's ledger:\n"
		<< run.m_szOutput;
}

//=============================================================================
// Membership, both ways. Both ceilings are zero.
//=============================================================================

TEST( GlobalsClientLedger, EveryPieceOfClientStateHasALedgerRow )
{
	const long lCeiling = RatchetCeiling( "client_globals_symbols_with_no_row" );
	ASSERT_GE( lCeiling, 0 );
	const long lMissing = ReportedNumber( Gate().m_szOutput, "symbols_with_no_ledger_row" );
	ASSERT_GE( lMissing, 0 ) << "no symbols_with_no_ledger_row line:\n" << Gate().m_szOutput;
	EXPECT_LE( lMissing, lCeiling ) << lMissing
									<< " pieces of state on the client link line have no row "
									   "in "
									<< kLedger
									<< ". Run `tools/globals-census reseed --side "
									   "client --write` and rule them.\n"
									<< Gate().m_szOutput;
}

TEST( GlobalsClientLedger, EveryClientLedgerRowStillNamesASymbol )
{
	const long lCeiling = RatchetCeiling( "client_globals_rows_with_no_symbol" );
	ASSERT_GE( lCeiling, 0 );
	const long lStale = ReportedNumber( Gate().m_szOutput, "ledger_rows_with_no_symbol" );
	ASSERT_GE( lStale, 0 ) << "no ledger_rows_with_no_symbol line:\n" << Gate().m_szOutput;
	EXPECT_LE( lStale, lCeiling ) << lStale
								  << " client ledger rows name a symbol no longer on the link "
									 "line.\n"
								  << Gate().m_szOutput;
}

TEST( GlobalsClientLedger, TheClientGateItselfPasses )
{
	EXPECT_EQ( Gate().m_nExit, 0 ) << "tools/globals-census gate --side client is red:\n" << Gate().m_szOutput;
}

//=============================================================================
// THE POSITIVE CONTROLS, one per direction.
//=============================================================================

TEST( GlobalsClientLedger, TheGateFailsWhenTheLedgerIsMissingARow )
{
	const std::vector<std::string> lines = Lines( ReadFromRoot( kLedger ) );
	size_t iLastData = 0, cData = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
		if ( IsLedgerDataLine( lines[i] ) )
		{
			iLastData = i;
			cData++;
		}
	// Anti-vacuity floors: 1000 until C8 (lane c6-input) took the ledger to
	// exactly 1000 rows; 800 until C7 step 3's eviction took it to 796; 600 is
	// still far above any stale or empty read.
	ASSERT_GT( cData, 500u ) << kLedger << " has almost no rows, so removing one proves nothing";

	std::string szBody;
	for ( size_t i = 0; i < lines.size(); i++ )
		if ( i != iLastData )
			szBody += lines[i] + "\n";

	const Run_t run = GateOverCopy( szBody, "control-up.tsv" );
	EXPECT_NE( run.m_nExit, 0 ) << "the gate PASSED against a ledger with a row removed; it is not "
								   "measuring this tree.\n"
								<< run.m_szOutput;
	EXPECT_EQ( ReportedNumber( run.m_szOutput, "symbols_with_no_ledger_row" ), 1 )
		<< "the gate should report exactly the one removed row:\n"
		<< run.m_szOutput;
	EXPECT_EQ( ReportedNumber( run.m_szOutput, "ledger_rows_with_no_symbol" ), 0 )
		<< "and nothing in the other direction:\n"
		<< run.m_szOutput;
}

//=============================================================================
// The counts, against their ceilings.
//=============================================================================

TEST( GlobalsClientLedger, TheClientLinkLevelIsAtOrBelowItsCeiling )
{
	const long lCeiling = RatchetCeiling( "client_globals_link_level" );
	ASSERT_GE( lCeiling, 0 );
	const long lNow = ReportedNumber( Gate().m_szOutput, "link_level" );
	ASSERT_GE( lNow, 0 ) << Gate().m_szOutput;
	EXPECT_LE( lNow, lCeiling ) << lNow
								<< " pieces of static state on the client link line against a "
								   "ceiling of "
								<< lCeiling
								<< ". If this rose for a reason, raise the "
								   "row deliberately and say what came back.";
	EXPECT_GT( lNow, 0 ) << "zero would far more likely mean the census stopped reading the "
							"link edge than that the client has no state";
}

TEST( GlobalsClientLedger, TheClientUnruledCountIsAtOrBelowItsCeiling )
{
	const long lCeiling = RatchetCeiling( "client_globals_unruled" );
	ASSERT_GE( lCeiling, 0 );
	const long lNow = ReportedNumber( Gate().m_szOutput, "unruled" );
	ASSERT_GE( lNow, 0 ) << Gate().m_szOutput;
	EXPECT_LE( lNow, lCeiling ) << lNow << " client rows are UNRULED against " << lCeiling;
}

TEST( GlobalsClientLedger, TheClientFloorIsAtOrBelowItsCeiling )
{
	const long lCeiling = RatchetCeiling( "client_globals_floor" );
	ASSERT_GE( lCeiling, 0 );
	const long lNow = ReportedNumber( Gate().m_szOutput, "disposition_FLOOR" );
	ASSERT_GE( lNow, 0 ) << Gate().m_szOutput;
	EXPECT_LE( lNow, lCeiling ) << lNow << " client rows are FLOOR against a ceiling of " << lCeiling
								<< ". FLOOR hides a row from the UNRULED count, so it is the one "
								   "ruling that can make this ledger look finished without anything "
								   "moving. Raise the ceiling only with a named reason in "
								   "tools/globals-census.";
}

TEST( GlobalsClientLedger, EveryClientFloorRowSaysWhy )
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	size_t cFloor = 0, cBare = 0;
	std::string szFirst;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[i].m_szDisposition != "FLOOR" )
			continue;
		cFloor++;
		if ( rows[i].m_szReason.empty() )
		{
			cBare++;
			if ( szFirst.empty() )
				szFirst = rows[i].m_szName;
		}
	}
	ASSERT_GT( cFloor, 0u ) << "no FLOOR rows at all, so this checks nothing";
	EXPECT_EQ( cBare, 0u ) << cBare << " FLOOR rows carry no reason, first: " << szFirst
						   << ". A disposition with no evidence is worse than UNRULED.";
}

TEST( GlobalsClientLedger, TheClientStaticInitialiserCountIsAtOrBelowItsCeiling )
{
	const long lCeiling = RatchetCeiling( "client_globals_objects_with_static_init" );
	ASSERT_GE( lCeiling, 0 );
	const long lNow = ReportedNumber( Summary().m_szOutput, "objects_with_a_static_initialiser" );
	ASSERT_GE( lNow, 0 ) << Summary().m_szOutput;
	EXPECT_LE( lNow, lCeiling ) << lNow << " client objects carry a static initialiser against " << lCeiling
								<< "; each runs before Init, in link order, with no engine.";
}

//=============================================================================
// The second instrument: the linked client.so, partitioned, residue empty.
//=============================================================================

TEST( GlobalsClientLedger, TheTwoInstrumentsReconcileWithNothingLeftOver )
{
	const long lCeiling = RatchetCeiling( "client_globals_reconcile_unattributed" );
	ASSERT_GE( lCeiling, 0 );
	const Run_t &run = Reconcile();
	ASSERT_NE( run.m_nExit, -1 ) << "the reconciliation could not be run";
	const long lLeftOver = ReportedNumber( run.m_szOutput, "UNATTRIBUTED" );
	ASSERT_GE( lLeftOver, 0 ) << "no UNATTRIBUTED line, so this compared against nothing:\n" << run.m_szOutput;
	EXPECT_LE( lLeftOver, lCeiling ) << lLeftOver
									 << " mutable symbols in client.so belong to no link-line "
										"object, no archive and no named crt symbol.\n"
									 << run.m_szOutput;
	// THE ARCHIVE TERM READ SOMETHING. Since C7 particles phase 4 took
	// particlemgr.cpp (and tier1's utlsymbol.o with it) off the link line,
	// client.so takes no mutable state from an archive: from_an_archive is 0
	// and correct. The control is therefore on the term's INPUT, the mutable
	// symbols the link line's archives define, which an archive term that
	// read nothing would also report as 0.
	//
	// Since C9 tier1 took libtier1.a off the client's line too, there is NO
	// archive on it (`# archives 0`, as server.so's). The term then has
	// nothing to attribute against, and both its numbers must be 0; should an
	// archive come back, its mutable symbols must be read again.
	long lArchives = -1;
	for ( const std::string &line : Lines( run.m_szOutput ) )
	{
		const std::vector<std::string> f = Split( line, '\t' );
		if ( f.size() >= 2 && f[0] == "# archives" )
			lArchives = strtol( f[1].c_str(), NULL, 10 );
	}
	ASSERT_GE( lArchives, 0 ) << "no '# archives' provenance line\n" << run.m_szOutput;
	ASSERT_GE( ReportedNumber( run.m_szOutput, "from_an_archive" ), 0 ) << "no from_an_archive line\n"
																		<< run.m_szOutput;
	if ( lArchives > 0 )
	{
		EXPECT_GT( ReportedNumber( run.m_szOutput, "archive_mutable_defined" ), 0 )
			<< "the link line's archives define no mutable symbol: the partition's "
			   "archive term read nothing, and an empty residue is then not evidence\n"
			<< run.m_szOutput;
	}
	else
	{
		EXPECT_EQ( ReportedNumber( run.m_szOutput, "from_an_archive" ), 0 ) << run.m_szOutput;
		EXPECT_EQ( ReportedNumber( run.m_szOutput, "archive_mutable_defined" ), 0 ) << run.m_szOutput;
	}
	EXPECT_EQ( run.m_nExit, 0 ) << run.m_szOutput;
}

//=============================================================================
// The ledger and the ratchet read as TEXT, independently of the tool.
//=============================================================================

TEST( GlobalsClientLedger, TheClientLedgerVocabularyCannotGrowAWord )
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	ASSERT_GT( rows.size(), 500u );
	for ( size_t i = 0; i < rows.size(); i++ )
		EXPECT_TRUE( IsADisposition( rows[i].m_szDisposition ) )
			<< rows[i].m_szName << " has disposition '" << rows[i].m_szDisposition << "'";
}

TEST( GlobalsClientLedger, EveryClientLedgerRowNamesATranslationUnitThatStillExists )
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	ASSERT_GT( rows.size(), 500u );
	size_t cMissing = 0;
	std::string szFirst;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[i].m_szTu.empty() || rows[i].m_szTu == "?" )
			continue;
		if ( ExistsUnderRoot( "src/" + rows[i].m_szTu ) )
			continue;
		cMissing++;
		if ( szFirst.empty() )
			szFirst = rows[i].m_szTu + " (" + rows[i].m_szName + ")";
	}
	EXPECT_EQ( cMissing, 0u ) << cMissing << " rows name a missing TU, first: " << szFirst;
}

TEST( GlobalsClientLedger, TheLedgerAndTheRatchetAgreeOnHowManyRowsThereAre )
{
	const long lCeiling = RatchetCeiling( "client_globals_link_level" );
	ASSERT_GE( lCeiling, 0 );
	EXPECT_EQ( (long)LedgerRows().size(), lCeiling )
		<< "a row per link-level symbol: when the count moves, both move in "
		   "the same commit";
}

TEST( GlobalsClientLedger, EveryRatchetRowNamesASubjectThatStillExists )
{
	std::string szFirst;
	const size_t cBad = RatchetProblems( ReadFromRoot( kRatchet ), &szFirst );
	EXPECT_EQ( cBad, 0u ) << szFirst;
	EXPECT_EQ( RatchetRows( ReadFromRoot( kRatchet ) ).size(), kNumMetrics )
		<< kRatchet << " should hold exactly the rows this file reads";
}

TEST( GlobalsClientLedger, TheRatchetCheckFailsWhenASubjectIsGone )
{
	// The control for the check above: the real ratchet with ONE subject
	// pointed at a path that is not in the tree must be exactly one problem.
	std::string szBody = ReadFromRoot( kRatchet );
	const size_t at = szBody.find( "client_globals_link_level" );
	ASSERT_NE( at, std::string::npos );
	const size_t tab = szBody.find( '\t', at );
	const size_t tab2 = szBody.find( '\t', tab + 1 );
	ASSERT_NE( tab2, std::string::npos );
	szBody.replace( tab + 1, tab2 - tab - 1, "src/game/there_is_no_such_dir" );

	std::string szFirst;
	EXPECT_EQ( RatchetProblems( szBody, &szFirst ), 1u )
		<< "a ratchet row whose subject is gone was not reported: " << szFirst;
}

} // namespace
