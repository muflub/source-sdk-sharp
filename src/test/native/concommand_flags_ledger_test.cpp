//===== SourceSharp — what the deferred-abi rows are ACTUALLY blocked on =====//
//
// WHY THIS EXISTS. Thirteen rows of docs/convar-ledger.tsv carried the verdict
// `deferred-abi` and, all thirteen of them, the SAME sentence for a reason:
//
//     blocked because Engine.RegisterConCommand takes (name, help) and no
//     FCVAR word, so a managed declaration cannot carry this command's flags
//
// The slot carries an FCVAR word as of ABI 250. So that sentence is now false
// of every one of the thirteen -- and every one of the thirteen is STILL
// blocked, each on a different missing crossing that the row never named:
// the client soundscape system, the temp-entity pool, a standalone's payload
// words, a server-side DispatchEffect, the game string pool, three unread
// player-movement fields.
//
// A LEDGER THAT KEEPS A REASON THAT HAS BEEN LIFTED IS WORSE THAN ONE WITH NO
// REASON, because the next lane reads it, lands the lifting change, and finds
// thirteen rows that still will not move. That happened here in miniature: the
// brief for this lane said "append what is needed and move those 13", on the
// ledger's word that the flag word was what was needed. It was not, for any of
// them.
//
// So this pins the rewrite: no deferred-abi row may name the FCVAR word as its
// blocker again, and every one must say what it IS blocked on.
//=============================================================================//
#include "gtest/gtest.h"
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

std::string Read( const char *pszRel )
{
	const char *kRoots[] = { "", "../", "../../" };
	for ( size_t i = 0; i < sizeof( kRoots ) / sizeof( kRoots[0] ); i++ )
	{
		std::ifstream f( ( std::string( kRoots[i] ) + pszRel ).c_str() );
		if ( !f )
			continue;
		std::ostringstream ss;
		ss << f.rdbuf();
		return ss.str();
	}
	return std::string();
}

const char *kLedger = "../docs/convar-ledger.tsv";

struct Row
{
	std::string name;
	std::string verdict;
	std::string evidence;
};

std::vector<std::string> Split( const std::string &line, char sep )
{
	std::vector<std::string> out;
	std::string cur;

	for ( size_t i = 0; i < line.size(); i++ )
	{
		if ( line[i] == sep )
		{
			out.push_back( cur );
			cur.clear();
		}
		else
		{
			cur += line[i];
		}
	}

	out.push_back( cur );
	return out;
}

// Only the deferred-abi rows. A comment line, the header and every other
// verdict are skipped -- this file has one subject.
std::vector<Row> DeferredRows()
{
	std::vector<Row> rows;
	std::istringstream in( Read( kLedger ) );
	std::string line;

	while ( std::getline( in, line ) )
	{
		if ( line.empty() || line[0] == '#' )
			continue;

		const std::vector<std::string> c = Split( line, '\t' );

		if ( c.size() < 12 || c[6] != "deferred-abi" )
			continue;

		Row r;
		r.name = c[0];
		r.verdict = c[6];
		r.evidence = c[11];
		rows.push_back( r );
	}

	return rows;
}

} // namespace

TEST( ConCommandFlagsLedger, TheLedgerIsReallyReadAndHasDeferredRows )
{
	ASSERT_FALSE( Read( kLedger ).empty() ) << kLedger
											<< " did not open; every assertion below would pass "
											   "vacuously over an empty row list";

	// Not a fixed 13. Rows move as lanes land, and a test that pinned the count
	// would fail on somebody else's unrelated progress -- which is the one
	// direction this must not fire in. What it must refuse is a scan that found
	// NOTHING, which is what a moved column or a changed separator looks like
	// and which would make the two checks below pass over an empty vector.
	ASSERT_FALSE( DeferredRows().empty() ) << "no row carries the deferred-abi verdict, so the checks below have "
											  "no subject -- either every one has moved (say so and delete this "
											  "file) or the column layout changed under the scan";
}

TEST( ConCommandFlagsLedger, NoRowStillBlamesTheMissingFcvarWord )
{
	const std::vector<Row> rows = DeferredRows();
	ASSERT_FALSE( rows.empty() );

	std::string stale;

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[i].evidence.find( "no FCVAR word" ) != std::string::npos ||
			 rows[i].evidence.find( "takes (name, help)" ) != std::string::npos )
			stale += "\n  " + rows[i].name;
	}

	EXPECT_TRUE( stale.empty() ) << "these rows still name the missing FCVAR word as what blocks them, "
									"and it was lifted at ABI 250 -- RegisterConCommand takes an FCVAR "
									"word and [ConCommand] has a Flags property:"
								 << stale
								 << "\n  Each of them is blocked on something else. Read the body and "
									"say what.";
}

TEST( ConCommandFlagsLedger, EveryDeferredRowSaysWhatItIsWaitingFor )
{
	const std::vector<Row> rows = DeferredRows();
	ASSERT_FALSE( rows.empty() );

	std::string bare;

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		// The phrase the rewrite uses. A row whose evidence does not contain it
		// is either an older deferral nobody re-measured or a new one written
		// without saying what is missing; both are the same defect from a
		// reader's side.
		if ( rows[i].evidence.find( "what is still missing is" ) == std::string::npos )
			bare += "\n  " + rows[i].name + ": " + rows[i].evidence.substr( 0, 80 );
	}

	EXPECT_TRUE( bare.empty() ) << "these deferred-abi rows do not say what crossing they are waiting "
								   "for:"
								<< bare
								<< "\n  `deferred-abi` means 'ruled managed, blocked on an ABI "
								   "addition'. Which addition is the whole content of the row.";
}
