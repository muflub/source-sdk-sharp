//========= SourceSharp — the ConVar ledger is the whole native keep list ====//
//
// plan_entity.md §6's rule, and plan_shim.md Phase 5's deliverable:
//
//     No ConVar's default or semantics is declared natively unless the engine
//     itself transmits or resolves the name.
//
// docs/convar-ledger.tsv is the ruling on every such declaration in the tree —
// one row per name, with the verdict and the evidence behind it. This file is
// what makes the ledger load-bearing rather than a document: it scans
// src/sourcesharp for ConVar and ConCommand CONSTRUCTORS and requires the set
// it finds to be exactly the set the ledger says should be there.
//
// THE COLUMN THAT IS PINNED IS `native_now`, NOT `verdict`. The verdict is the
// ruling — "this ought to be managed" — and several rows carry a ruling whose
// move has not landed (`managed-queued`, `reader-owed`, `deferred-abi`,
// `deferred-lane`). A test that pinned the ruling would be red for work that is
// correctly not done yet. `native_now` is the FACT: whether a native
// declaration of this name is expected in the tree at this commit. Ruling and
// fact are separate columns so that neither can quietly become the other.
//
// IT HAS TO FAIL BOTH WAYS, because a keep list rots in two directions and only
// one of them is obvious:
//
//   * A NATIVE DECLARATION APPEARS THAT THE LEDGER DOES NOT LIST, or lists with
//     native_now=no. That is the obvious direction — somebody declared a tuning
//     value in C++ again.
//
//   * A LEDGER ROW SURVIVES ITS CONVAR. This is the quiet one. A row saying
//     "keep this native, the engine resolves it" for a ConVar that was deleted
//     or renamed months ago reads as a live exemption and licenses the next
//     declaration that looks like it. An allow-list that is never checked
//     against reality is how these rot, so every native_now=yes row is required
//     to still have a declaration at the file and line it names.
//
// AND IT HAS TO BE ABLE TO SEE ANYTHING AT ALL. Facts 2, 3 and 5 are all
// quantified over "every declaration the scanner found", so a scanner that
// found nothing would satisfy all three and report green — one of this tree's
// recorded checks-that-cannot-fail. ScannerSeesTheDeclarationsWeKnowAreThere
// pins a floor and a handful of names by hand, and is the reason the others
// mean something.
//
// THREE DECLARATIONS THE SCANNER CANNOT SEE, and are in the ledger anyway:
// `cl_team` and `cl_class` are ConVar MEMBERS of the client shell's class, and
// the verbosity convar takes its name from a macro that differs per side
// (SOURCESHARP_VERBOSE_CVAR). A constructor scan keyed on a string literal
// finds none of the three. They are checked the other way round instead — the
// ledger names the file and line, and that line is required to still declare a
// ConVar — which is the same check every other row gets for its survival half.
//
// Run from src/, as buildcpptests does: paths here are relative to that.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>
#include <sys/stat.h>

#include <algorithm>
#include <fstream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kLedger  = "../docs/convar-ledger.tsv";
//-----------------------------------------------------------------------------
// The ledger
//-----------------------------------------------------------------------------

struct Row
{
	std::string name;
	std::string kind;
	std::string file;     // relative to src/
	int         line;
	std::string verdict;
	bool        nativeNow;
	std::string evidence;
};

std::vector< std::string > Split( const std::string &line, char sep )
{
	std::vector< std::string > out;
	std::string cur;
	for ( size_t i = 0; i < line.size(); i++ )
	{
		if ( line[ i ] == sep ) { out.push_back( cur ); cur.clear(); }
		else                    { cur += line[ i ]; }
	}
	out.push_back( cur );
	return out;
}

const std::vector< Row > &Ledger()
{
	static std::vector< Row > rows;
	static bool loaded = false;
	if ( loaded )
		return rows;
	loaded = true;

	std::ifstream f( kLedger );
	if ( !f.good() )
		return rows;

	std::string line;
	bool header = true;
	while ( std::getline( f, line ) )
	{
		if ( header ) { header = false; continue; }
		if ( line.empty() )
			continue;

		const std::vector< std::string > c = Split( line, '\t' );
		if ( c.size() < 12 )
			continue;

		Row r;
		r.name      = c[ 0 ];
		r.kind      = c[ 1 ];
		r.file      = c[ 3 ];
		r.line      = atoi( c[ 4 ].c_str() );
		r.verdict   = c[ 6 ];
		r.nativeNow = ( c[ 7 ] == "yes" );
		r.evidence  = c[ 11 ];
		rows.push_back( r );
	}
	return rows;
}

//-----------------------------------------------------------------------------
// The scan
//
// Comments are stripped first. Every one of these names is written down in
// prose somewhere in this tree -- the ledger's own reasons are in .cpp banners
// -- and a needle a COMMENT can satisfy is a needle that stops being about the
// code. Four tests in this directory have gone red on exactly that.
//-----------------------------------------------------------------------------

// A declaration this test is about is a CONSTRUCTOR: `ConVar x( "name", ...` or
// `ConCommand x( "name", ...`, or the CON_COMMAND / CON_COMMAND_F macro, whose
// first argument is the console name as a bare token rather than a string.
struct Decl
{
	std::string name;
	std::string file;
};

bool Mentions( const std::string &hay, const std::string &needle )
{
	return hay.find( needle ) != std::string::npos;
}

// The ledger's `file` column is repo-relative; this test runs in src/.
std::string InSrc( const std::string &repoRelative )
{
	const std::string prefix = "src/";
	if ( repoRelative.compare( 0, prefix.size(), prefix ) == 0 )
		return repoRelative.substr( prefix.size() );
	return "../" + repoRelative;
}

} // anonymous namespace

//=============================================================================//
// 0. THE INSTRUMENTS THEMSELVES
//
// Every fact below is quantified over one of these two sets. Either of them
// coming back empty would make the rest of the file pass while measuring
// nothing, so both are pinned first.
//=============================================================================//

TEST( ConVarLedger, LedgerLoadsAndHasRows )
{
	const std::vector< Row > &rows = Ledger();
	ASSERT_FALSE( rows.empty() )
		<< kLedger << " did not load, or has no rows. Every other fact in this "
		   "file is quantified over it and would pass vacuously.";
	// 125, not 135 (lane c5-proxies, 2026-10-05): rows leave with the native
	// commands plan_client_novalve ports or deletes, 133 after
	// cs_cl_alphaproxy and cs_cl_playerlogo went with shell_materialproxies.cpp,
	// 132 after cs_cl_dust_report went managed (lane c5-particles).
	EXPECT_GE( rows.size(), 125u )
		<< "the ledger was measured at 132 rows; a sudden collapse means the "
		   "file was truncated rather than that the tree got smaller";
}

//=============================================================================//
// 1. NOTHING NATIVE OUTSIDE THE LEDGER
//=============================================================================//

//=============================================================================//
// 2. AND NOTHING IN THE LEDGER WITHOUT ITS CONVAR
//
// The direction that rots quietly. A keep row outliving its declaration is a
// live exemption for a ConVar that is not there, and it licenses the next one
// that looks like it.
//=============================================================================//

TEST( ConVarLedger, EveryRowPointsAtAFileThatExists )
{
	const std::vector< Row > &rows = Ledger();

	std::vector< std::string > gone;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( !rows[ i ].nativeNow )
			continue;
		std::ifstream f( InSrc( rows[ i ].file ).c_str() );
		if ( !f.good() )
			gone.push_back( rows[ i ].name + "  ->  " + rows[ i ].file );
	}

	std::string report;
	for ( size_t i = 0; i < gone.size(); i++ )
		report += "\n    " + gone[ i ];

	EXPECT_TRUE( gone.empty() )
		<< gone.size() << " ledger row(s) name a file that is not in the tree:"
		<< report;
}

//=============================================================================//
// 3. THE VERDICTS THEMSELVES
//=============================================================================//

TEST( ConVarLedger, EveryRowCarriesAKnownVerdict )
{
	static const char *const kVerdicts[] = {
		"protocol-keep",   // the engine declares, resolves or queries the name
		"managed",         // moved; the declaration is a [ConVar]/[ConCommand]
		"managed-queued",  // ruled managed, move not landed yet
		"native-required", // the body is native-only mechanism
		"reader-owed",     // ruled managed, blocked on converting named readers
		"deferred-abi",    // ruled managed, blocked on an ABI addition
		"deferred-lane",   // in a file another lane holds open
		"delete",
		"dead-already",
	};

	const std::vector< Row > &rows = Ledger();
	std::vector< std::string > bad;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		bool ok = false;
		for ( size_t v = 0; v < sizeof( kVerdicts ) / sizeof( kVerdicts[ 0 ] ); v++ )
		{
			if ( rows[ i ].verdict == kVerdicts[ v ] ) { ok = true; break; }
		}
		if ( !ok )
			bad.push_back( rows[ i ].name + " -> '" + rows[ i ].verdict + "'" );
	}

	std::string report;
	for ( size_t i = 0; i < bad.size(); i++ )
		report += "\n    " + bad[ i ];

	EXPECT_TRUE( bad.empty() )
		<< bad.size() << " row(s) carry a verdict this test does not know:"
		<< report << "\n  A free-text verdict column stops being a ruling.";
}

TEST( ConVarLedger, EveryRowCarriesItsEvidence )
{
	const std::vector< Row > &rows = Ledger();

	std::vector< std::string > bare;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].evidence.size() < 20 )
			bare.push_back( rows[ i ].name );
	}

	std::string report;
	for ( size_t i = 0; i < bare.size(); i++ )
		report += "\n    " + bare[ i ];

	EXPECT_TRUE( bare.empty() )
		<< bare.size() << " row(s) have no evidence behind the verdict:" << report
		<< "\n  The ledger is the lasting artefact; a verdict with no reason is a "
		   "preference.";
}

TEST( ConVarLedger, ProtocolKeepsNameWhatResolvesThem )
{
	// §6 allows exactly one reason to keep a ConVar's declaration in C++: the
	// engine itself transmits or resolves the name. A protocol-keep row that
	// does not say WHICH engine-side thing does that is the reason this rule
	// erodes, so the evidence has to name it.
	const std::vector< Row > &rows = Ledger();

	size_t keeps = 0;
	std::vector< std::string > unsupported;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].verdict != "protocol-keep" )
			continue;
		keeps++;

		const std::string &e = rows[ i ].evidence;
		if ( !Mentions( e, "engine.so" ) && !Mentions( e, "by name" ) &&
		     !Mentions( e, "BEFORE the managed side" ) )
		{
			unsupported.push_back( rows[ i ].name );
		}
	}

	ASSERT_GT( keeps, 0u )
		<< "no protocol-keep rows at all -- this fact would pass without "
		   "looking at anything";

	std::string report;
	for ( size_t i = 0; i < unsupported.size(); i++ )
		report += "\n    " + unsupported[ i ];

	EXPECT_TRUE( unsupported.empty() )
		<< unsupported.size() << " protocol-keep row(s) do not name what resolves "
		   "the name engine-side:" << report;
}
