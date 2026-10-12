//====== SourceSharp — the two strings that must land at index 0 ==============//
//
// plan_gamedll.md Part 8, Phase 6.
//
// THIS IS THE INVARIANT THAT IS SILENT WHEN IT IS WRONG, and that is the whole
// reason for this file. GetMaterialIndex and GetParticleSystemIndex both answer
// **0** for a name they cannot find (gameinterface.cpp:2477-2493, :2517-2530):
//
//     int nIndex = g_pStringTableMaterials->FindStringIndex( pMaterialName );
//     if ( nIndex != INVALID_STRING_INDEX ) return nIndex;
//     DevMsg( "Warning! GetMaterialIndex: couldn't find material %s\n" );
//     return 0;
//
// So index 0 of each table is the answer every FAILED lookup gets. Put anything
// else there first and a failed lookup silently resolves to a real material or
// a real particle system instead of to the error one -- everywhere, forever,
// with a DevMsg nobody reads and no other symptom at all.
//
// HOW THIS IS TESTED, because "a substring is present" would not be enough.
// The shipped body of SourceSharp_CreateNetworkStringTables is parsed into the
// SEQUENCE of operations it performs -- create this table, add this string to
// that one -- and that sequence is then REPLAYED against a model of the
// engine's own numbering (a new string in a table takes the next index, first
// one is 0). The assertion is on the replayed index, not on the text.
//
// That is stronger than a needle in two ways a transposition test is not:
//
//   * ANY earlier add to Materials or ParticleEffectNames fails it, including
//     one nobody thought to write a needle for -- a helpful `PrecacheMaterial(
//     "sprites/glow01" )` added to the top of the function is the exact edit
//     this catches and a needle would not.
//   * A table created AFTER its first add fails it too, which is the other way
//     to get index 0 wrong and does not look like a reordering at all.
//
// WHAT PHASE A8 CHANGED, because it changed the SUBJECT and not the method.
// plan_gamedll_after.md Phase A8 gives a MOD a CreateStringTable / AddString /
// GetString / FindStringIndex ABI, and the managed side of it runs from
// IManagedRuntimeServer::CreateNetworkStringTables -- a call inside the same
// engine entry point the two precaches are in. So "a managed AddString before
// the two precaches" became a reachable edit, and it is exactly the edit this
// file exists to refuse.
//
// The answer is NOT a second test. A parallel check would leave this replay
// covering half the sequence and nobody would notice which half. The REPLAY's
// subject is widened instead: it is now CShellGameDll::CreateNetworkStringTables
// -- the whole shipped sequence -- with SourceSharp_CreateNetworkStringTables
// INLINED where it is called, and the managed call-out modelled as an add of an
// unknown string to every one of the seven shared tables.
//
// That model is deliberately the WIDEST thing the ABI permits rather than what
// today's managed code happens to do. A mod holding an AddString and a table id
// can add to any table it can name, so the sequence has to be correct against
// that and not against the current caller's good manners. It gives three
// answers where an ordering needle gives one:
//
//   * the call-out AFTER the seven creates and both precaches -- the shipped
//     order -- puts the mod's string at index 1 and the invariant holds;
//   * the call-out BETWEEN the creates and the precaches -- which is what
//     "let the mod add its strings while the tables are fresh" looks like as a
//     diff -- puts it at index 0 and TheErrorMaterialLandsAtIndexZero fails on
//     the replayed INDEX;
//   * the call-out BEFORE the creates puts it in a table that does not exist
//     and NothingIsAddedToATableThatDoesNotExistYet fails.
//
// Source-read rather than linked, and that is forced rather than chosen:
// shell_stringtables.cpp includes cbase.h's whole world -- and since Phase A4
// PrecacheMaterial itself lives in that same file -- so no part of this can be
// called from a gtest binary. The live half of the gate is a running server -- see
// docs/gamedll-ledger.md, Phase 6 and Phase A8. The BEHAVIOUR of the six new
// ABI slots is a different question with a different instrument:
// sourcesharp/test/stringtable_abi_test.cpp runs the shipped bodies.
//
//=============================================================================//

#include <string>
#include <vector>
#include <map>
#include <cctype>
#include <cstring>
#include <fstream>
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

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// Comments are stripped FIRST and always. Every needle below would otherwise
// match the paragraph above it explaining what it is for, which is this tree's
// running list of checks that cannot fail.
std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::string Tight( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

const char *kTables     = "sourcesharp/game/server/shell_stringtables.cpp";
const char *kShellGame  = "sourcesharp/game/server/shell_gamedll.cpp";
const char *kGameIface  = "game/server/gameinterface.cpp";

//-----------------------------------------------------------------------------
// The body of a function, brace-matched, from a tightened signature.
//
// BOUNDED ON PURPOSE. The first version of the two "still answers zero" facts
// below searched for "return0;" from the START of GetMaterialIndex to the end
// of gameinterface.cpp -- a file with hundreds of them. That check could not
// fail: mutate the fallback and the search simply finds the next function's
// return instead. Number six in this tree's running list.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &tightCode, const char *pszTightSignature )
{
	const size_t at = tightCode.find( pszTightSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = tightCode.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < tightCode.size(); i++ )
	{
		if ( tightCode[ i ] == '{' ) depth++;
		else if ( tightCode[ i ] == '}' && --depth == 0 )
			return tightCode.substr( open + 1, i - open - 1 );
	}

	return std::string();
}

//-----------------------------------------------------------------------------
// The body of SourceSharp_CreateNetworkStringTables, comments stripped and
// whitespace removed, so that positions in it are positions in CODE.
//-----------------------------------------------------------------------------
std::string TableFunctionBody()
{
	// Brace-matched, so an operation written AFTER the function -- in some
	// other function in the same file -- is not read as part of it.
	const std::string body = BodyOf( Tight( StripComments( Read( kTables ) ) ),
	                                 "voidSourceSharp_CreateNetworkStringTables(void)" );

	EXPECT_FALSE( body.empty() )
		<< "SourceSharp_CreateNetworkStringTables is not in " << kTables
		<< " under that name -- every invariant below would pass vacuously";

	return body;
}

//-----------------------------------------------------------------------------
// Every `return <expr>;` in a tightened function body, in order.
//
// EVERY one, and this is the second correction this file needed. The first
// version of the two facts below asked only whether "return0;" appeared
// somewhere in GetMaterialIndex -- and that function has TWO fallbacks, one
// after the DevMsg and one for a null name. Mutating either alone left the
// other, so the check stayed green while the thing it was pinning had changed.
// Number seven in this tree's running list of checks that cannot fail.
//-----------------------------------------------------------------------------
std::vector<std::string> ReturnsIn( const std::string &body )
{
	std::vector<std::string> out;
	size_t at = 0;
	while ( ( at = body.find( "return", at ) ) != std::string::npos )
	{
		// not the tail of an identifier
		if ( at > 0 && ( isalnum( (unsigned char)body[ at - 1 ] ) || body[ at - 1 ] == '_' ) )
		{
			at += 6;
			continue;
		}
		const size_t semi = body.find( ';', at );
		if ( semi == std::string::npos ) break;
		out.push_back( body.substr( at + 6, semi - at - 6 ) );
		at = semi;
	}
	return out;
}

// A literal at a position. compare( pos, n, s ) needs n == strlen( s ) or it
// can never match -- which is a silent always-false, i.e. exactly the failure
// mode this file exists to catch one level down. The length comes from the
// literal itself so it cannot disagree with it.
bool Starts( const std::string &s, size_t at, const char *pszLit )
{
	return s.compare( at, strlen( pszLit ), pszLit ) == 0;
}

// One thing the sequence does, in the order it does it.
//
// FOUR KINDS AND NOT TWO. Phase A8 put two CALLS into the sequence that are
// neither a create nor an add on their own -- the native table function, which
// is spliced in where it is called, and the managed slot, which stands for
// whatever a mod does. Modelling them as ops rather than as special cases in
// the parser is what lets the replay stay one linear walk.
enum OpKind
{
	kCreate,        // a table was created
	kAdd,           // a string was added to one
	kNativeTables,  // SourceSharp_CreateNetworkStringTables(), spliced in
	kManaged,       // the managed CreateNetworkStringTables slot
};

struct Op
{
	OpKind kind;
	std::string table;     // the g_pStringTable... variable, or the logical table
	std::string arg;       // the table's NAME on a create, the string on an add
};

// The seven the shell creates natively, by the GLOBAL each is assigned to --
// which is how the replay keys a table, because that is what an add names.
const char *kSharedTableGlobals[] = {
	"g_pStringTableParticleEffectNames",
	"g_pStringTableEffectDispatch",
	"g_pStringTableVguiScreen",
	"g_pStringTableMaterials",
	"g_pStringTableInfoPanel",
	"g_pStringTableClientSideChoreoScenes",
	"g_pStringTableServerMapCycle",
};

const size_t kSharedTableCount = sizeof( kSharedTableGlobals ) / sizeof( kSharedTableGlobals[ 0 ] );

// What the managed slot is modelled as adding. Not a real material and not a
// real particle system, so it cannot be confused with one in a failure message.
const char *kModString = "<a string some mod added>";

// The two call forms Phase A8 put into the sequence, tightened as the parser
// sees them. Named once so the parser and the fact that refuses their absence
// cannot disagree about the spelling.
const char *kNativeCall  = "SourceSharp_CreateNetworkStringTables()";
const char *kManagedCall = "ManagedRuntimeServer()->CreateNetworkStringTables()";

std::string QuotedAt( const std::string &s, size_t from )
{
	const size_t a = s.find( '"', from );
	if ( a == std::string::npos ) return std::string();
	const size_t b = s.find( '"', a + 1 );
	if ( b == std::string::npos ) return std::string();
	return s.substr( a + 1, b - a - 1 );
}

//-----------------------------------------------------------------------------
// The function, reduced to what it DOES: creates and adds, in order.
//
// Four forms are recognised, which is every form this file uses:
//
//   g_pX=networkstringtable->CreateStringTable("Name",N)   -> create X as Name
//   PrecacheMaterial("s")                                  -> add s to Materials
//   PrecacheParticleSystem("s")                            -> add s to Particles
//   g_pX->AddString(...,"s"...)                            -> add s to X
//   SourceSharp_CreateNetworkStringTables()                -> the native seven
//   ManagedRuntimeServer()->CreateNetworkStringTables()         -> a mod's turn
//
// A form NOT recognised is a hole in this test, so the total operation count is
// asserted separately against the number of statements that can touch a table,
// and TheReplayedSequenceIsTheWholeShippedOne refuses a sequence that has lost
// either of the last two -- a needle that stops matching is this tree's most
// repeated defect and it would make every fact below pass vacuously.
//-----------------------------------------------------------------------------
std::vector<Op> Operations( const std::string &body )
{
	std::vector<Op> ops;

	for ( size_t i = 0; i < body.size(); i++ )
	{
		// THE LENGTH COMES FROM THE LITERAL, never from a number written
		// beside it. A hand-counted skip that is one too long steps over the
		// first character of whatever follows, and this file already records
		// what a hand-counted length costs: compare( i, 24, "a 23-character
		// literal" ) is silently always false, which made the particle arm of
		// this very parser match nothing while the fact passed.
		if ( Starts( body, i, kNativeCall ) )
		{
			Op op;
			op.kind = kNativeTables;
			ops.push_back( op );
			i += strlen( kNativeCall ) - 1;
			continue;
		}

		if ( Starts( body, i, kManagedCall ) )
		{
			Op op;
			op.kind = kManaged;
			ops.push_back( op );
			i += strlen( kManagedCall ) - 1;
			continue;
		}

		if ( Starts( body, i, "networkstringtable->CreateStringTable(" ) )
		{
			// walk back over "g_pStringTableFoo=" to the assignment target
			size_t eq = body.rfind( '=', i );
			size_t start = ( eq == std::string::npos ) ? 0 : eq;
			while ( start > 0 && body[ start - 1 ] != ';' && body[ start - 1 ] != '{' )
				start--;

			Op op;
			op.kind = kCreate;
			op.table = ( eq == std::string::npos ) ? std::string( "?" )
			                                       : body.substr( start, eq - start );
			op.arg = QuotedAt( body, i );
			ops.push_back( op );
			continue;
		}

		if ( Starts( body, i, "PrecacheMaterial(" ) )
		{
			Op op;
			op.kind = kAdd;
			op.table = "g_pStringTableMaterials";
			op.arg = QuotedAt( body, i );
			ops.push_back( op );
			continue;
		}

		if ( Starts( body, i, "PrecacheParticleSystem(" ) )
		{
			Op op;
			op.kind = kAdd;
			op.table = "g_pStringTableParticleEffectNames";
			op.arg = QuotedAt( body, i );
			ops.push_back( op );
			continue;
		}

		if ( Starts( body, i, "->AddString(" ) )
		{
			size_t start = i;
			while ( start > 0 && ( isalnum( (unsigned char)body[ start - 1 ] ) || body[ start - 1 ] == '_' ) )
				start--;

			Op op;
			op.kind = kAdd;
			op.table = body.substr( start, i - start );
			op.arg = QuotedAt( body, i );
			ops.push_back( op );
			continue;
		}
	}

	return ops;
}

//-----------------------------------------------------------------------------
// The engine's numbering, modelled: a table is a list, AddString appends a name
// it does not already hold, and the index of a name is its position. -1 for a
// name that never went in; -2 for a name added to a table that does not exist,
// which is the OTHER way to get index 0 wrong.
//-----------------------------------------------------------------------------
struct Replay
{
	std::map< std::string, std::vector<std::string> > tables;
	bool bAddedToAMissingTable;
	bool bUnsplicedNativeCall;

	Replay() : bAddedToAMissingTable( false ), bUnsplicedNativeCall( false ) {}

	void Run( const std::vector<Op> &ops )
	{
		for ( size_t i = 0; i < ops.size(); i++ )
		{
			if ( ops[ i ].kind == kCreate )
			{
				tables[ ops[ i ].table ] = std::vector<std::string>();
				continue;
			}

			// A MOD'S TURN, modelled as the widest thing the Phase A8 ABI
			// permits: one string into every one of the seven. Not what
			// today's managed code does -- it adds to a table of its own --
			// but what a mod HOLDING AddString and a table id is able to do,
			// which is the thing the sequence has to be correct against.
			if ( ops[ i ].kind == kManaged )
			{
				for ( size_t n = 0; n < kSharedTableCount; n++ )
					Add( kSharedTableGlobals[ n ], kModString );

				continue;
			}

			// kNativeTables is spliced out before the replay runs -- see
			// ShippedSequence. Reaching one here means the splice failed, and a
			// silently unspliced marker would drop the seven creates and both
			// precaches from the model while every fact still ran.
			if ( ops[ i ].kind == kNativeTables )
			{
				bUnsplicedNativeCall = true;
				continue;
			}

			Add( ops[ i ].table, ops[ i ].arg );
		}
	}

	void Add( const std::string &table, const std::string &value )
	{
		if ( tables.find( table ) == tables.end() )
		{
			bAddedToAMissingTable = true;
			return;
		}

		std::vector<std::string> &t = tables[ table ];

		for ( size_t j = 0; j < t.size(); j++ )
			if ( t[ j ] == value ) return;

		t.push_back( value );
	}

	int IndexOf( const char *pszTable, const char *pszString ) const
	{
		std::map< std::string, std::vector<std::string> >::const_iterator it = tables.find( pszTable );
		if ( it == tables.end() )
			return -2;

		for ( size_t j = 0; j < it->second.size(); j++ )
			if ( it->second[ j ] == pszString )
				return (int)j;

		return -1;
	}
};

//-----------------------------------------------------------------------------
// THE WHOLE SHIPPED SEQUENCE: CShellGameDll::CreateNetworkStringTables, with
// SourceSharp_CreateNetworkStringTables spliced in where it is called and the
// managed slot left as its own op.
//
// The SHELL's method is the subject and not the native function, because after
// Phase A8 the native function is only PART of the sequence -- the managed slot
// beside it can add strings too, and which of the two runs first is the whole
// invariant. Reading only the native half would leave the interesting edit
// invisible.
//-----------------------------------------------------------------------------
std::vector<Op> ShippedSequence()
{
	const std::string shell = BodyOf( Tight( StripComments( Read( kShellGame ) ) ),
	                                  "voidCreateNetworkStringTables()override" );

	EXPECT_FALSE( shell.empty() )
		<< "CShellGameDll::CreateNetworkStringTables is not in " << kShellGame
		<< " under that signature -- every invariant below would pass vacuously";

	const std::vector<Op> outer  = Operations( shell );
	const std::vector<Op> native = Operations( TableFunctionBody() );

	std::vector<Op> flat;

	for ( size_t i = 0; i < outer.size(); i++ )
	{
		if ( outer[ i ].kind == kNativeTables )
		{
			for ( size_t j = 0; j < native.size(); j++ )
				flat.push_back( native[ j ] );

			continue;
		}

		flat.push_back( outer[ i ] );
	}

	return flat;
}

Replay ReplayTheShippedFunction()
{
	Replay r;
	r.Run( ShippedSequence() );
	return r;
}

} // namespace

//-----------------------------------------------------------------------------
// THE INVARIANT. Both halves of it, one fact each.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, TheErrorMaterialLandsAtIndexZero )
{
	const Replay r = ReplayTheShippedFunction();

	EXPECT_EQ( r.IndexOf( "g_pStringTableMaterials", "debug/debugempty" ), 0 )
		<< "\"debug/debugempty\" is not the FIRST string in the Materials table.\n"
		<< "GetMaterialIndex answers 0 for a material it cannot find "
		<< "(gameinterface.cpp:2489), so index 0 is what every failed lookup "
		<< "gets. Whatever is at 0 instead is now silently the answer to every "
		<< "one of them -- there is no warning, no assert in release, and no "
		<< "other symptom.\n"
		<< "-2 means the Materials table is created AFTER something is added to "
		<< "it; -1 means \"debug/debugempty\" is not precached here at all.\n"
		<< "If the string at 0 is the mod one, the managed "
		<< "CreateNetworkStringTables slot now runs BEFORE the two precaches "
		<< "(plan_gamedll_after.md Phase A8). A mod holding AddString can put a "
		<< "string in any table it can name, so the slot has to stay after "
		<< "them -- which is what the shell's own comment says it is for.";
}

TEST( StringTableIndexZero, TheErrorParticleSystemLandsAtIndexZero )
{
	const Replay r = ReplayTheShippedFunction();

	EXPECT_EQ( r.IndexOf( "g_pStringTableParticleEffectNames", "error" ), 0 )
		<< "\"error\" is not the FIRST string in the ParticleEffectNames table.\n"
		<< "GetParticleSystemIndex answers 0 for a system it cannot find "
		<< "(gameinterface.cpp:2529), so every missing particle system now "
		<< "resolves to whatever took index 0 instead of to the error one.\n"
		<< "If the string at 0 is the mod one, the managed slot runs before "
		<< "the precache -- see TheErrorMaterialLandsAtIndexZero.";
}

TEST( StringTableIndexZero, NothingIsAddedToATableThatDoesNotExistYet )
{
	const Replay r = ReplayTheShippedFunction();

	EXPECT_FALSE( r.bAddedToAMissingTable )
		<< "a string is added to a table before that table is created. In the "
		<< "shipped build that is a null dereference through the g_pStringTable "
		<< "global, not a reordering -- and the two failures look identical in "
		<< "the source.";
}

//-----------------------------------------------------------------------------
// THE REPLAY IS READING THE WHOLE SEQUENCE, and not a piece of it.
//
// Every fact above is an assertion about a REPLAYED index, so every one of them
// passes vacuously against an op list that is missing the operation somebody
// changed. Phase A8 added two call forms to the parser, and a call form is a
// needle: rename the native step or the managed slot and the parser stops
// seeing it, the sequence quietly loses the seven creates or the mod's turn,
// and the file goes green while covering less than it did. That is this tree's
// most repeated defect and this is the fact that refuses it.
//
// SHAPE AND NOT A COUNT. What must be true is that both call-outs are present
// exactly once and that the native half really did splice -- not that the
// sequence has some particular length, which would have to be edited every time
// a table is added.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, TheReplayedSequenceIsTheWholeShippedOne )
{
	const std::vector<Op> ops = ShippedSequence();

	int nCreates = 0, nAdds = 0, nManaged = 0, nUnspliced = 0;

	for ( size_t i = 0; i < ops.size(); i++ )
	{
		switch ( ops[ i ].kind )
		{
		case kCreate:       nCreates++;   break;
		case kAdd:          nAdds++;      break;
		case kManaged:      nManaged++;   break;
		case kNativeTables: nUnspliced++; break;
		}
	}

	EXPECT_EQ( nManaged, 1 )
		<< "the managed CreateNetworkStringTables call-out appears " << nManaged
		<< " time(s) in the shell's method and must appear exactly once.\n"
		<< "0 means either the slot is gone -- it is the seam MOD-DECLARED "
		<< "tables go through, and deleting it because its managed half looks "
		<< "small means re-deriving it later -- or that this test's needle for "
		<< "it has stopped matching, in which case every index-0 fact above is "
		<< "now passing without ever modelling a mod's add.\n"
		<< "More than 1 means a mod gets two turns in one window, and only the "
		<< "first of them is after the two precaches.";

	EXPECT_EQ( nUnspliced, 0 )
		<< "SourceSharp_CreateNetworkStringTables was seen in the shell's "
		<< "method but its body could not be spliced in. The replay is then "
		<< "running against a sequence with no creates and no precaches in it "
		<< "at all, which every fact above would pass.";

	EXPECT_GE( nCreates, 7 )
		<< "the replayed sequence holds " << nCreates << " table create(s). "
		<< "The seven shared ones are created in the native half; fewer than "
		<< "seven means the splice found the wrong function or the parser's "
		<< "create form has stopped matching.";

	EXPECT_GE( nAdds, 2 )
		<< "the replayed sequence holds " << nAdds << " add(s) and there are at "
		<< "least three -- the two index-0 precaches and the mod's turn. Fewer "
		<< "means the form that expresses one of them is no longer recognised.";
}

//-----------------------------------------------------------------------------
// WHY index 0 is load-bearing. Read from the shipped source, so that the day
// somebody makes a missing lookup return -1 instead, the fact above is known to
// have stopped mattering rather than being quietly preserved for no reason.
//
// Phase A4 moved the six accessors out of gameinterface.cpp and into
// shell_stringtables.cpp -- the file that creates the two tables they read --
// so these two facts read kTables now. TheSixAccessorsLiveWithTheTablesTheyRead
// below is what says the move happened and did not leave a second copy behind.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, AFailedMaterialLookupStillAnswersZero )
{
	const std::string code = Tight( StripComments( Read( kTables ) ) );
	ASSERT_FALSE( code.empty() );

	const std::string body = BodyOf( code, "intGetMaterialIndex(constchar*pMaterialName)" );
	ASSERT_FALSE( body.empty() ) << "GetMaterialIndex is not in shell_stringtables.cpp";

	const std::vector<std::string> rets = ReturnsIn( body );
	ASSERT_FALSE( rets.empty() ) << "GetMaterialIndex returns nothing -- this fact is reading the wrong function";

	// EVERY return that is not the index it found must be literally 0. Both
	// misses -- the name it could not find and the null name -- and any third
	// one somebody adds later.
	for ( size_t i = 0; i < rets.size(); i++ )
	{
		if ( rets[ i ] == "nIndex" )
			continue;

		EXPECT_EQ( rets[ i ], "0" )
			<< "GetMaterialIndex has a miss path that answers '" << rets[ i ]
			<< "' rather than 0. If a failed lookup no longer resolves to index "
			<< "0, the two index-0 facts above are pinning something that has "
			<< "stopped mattering -- decide which, do not delete one of them "
			<< "quietly.";
	}
}

TEST( StringTableIndexZero, AFailedParticleSystemLookupStillAnswersZero )
{
	const std::string code = Tight( StripComments( Read( kTables ) ) );
	ASSERT_FALSE( code.empty() );

	const std::string body = BodyOf( code, "intGetParticleSystemIndex(constchar*pParticleSystemName)" );
	ASSERT_FALSE( body.empty() ) << "GetParticleSystemIndex is not in shell_stringtables.cpp";

	const std::vector<std::string> rets = ReturnsIn( body );
	ASSERT_FALSE( rets.empty() ) << "GetParticleSystemIndex returns nothing -- wrong function";

	for ( size_t i = 0; i < rets.size(); i++ )
	{
		if ( rets[ i ] == "nIndex" )
			continue;

		EXPECT_EQ( rets[ i ], "0" )
			<< "GetParticleSystemIndex has a miss path that answers '"
			<< rets[ i ] << "' rather than 0.";
	}
}

//-----------------------------------------------------------------------------
// PHASE A4 -- the six accessors live in the file that creates the two tables
// they read, and in exactly one file.
//
// Two ways this goes wrong and neither shows up as a build failure:
//
//   * somebody moves one back to gameinterface.cpp, and the two facts above
//     start reading a function that is no longer the one that ships. They
//     would go GREEN on an empty read if they only checked "is it here" -- so
//     they ASSERT on a non-empty body, and this fact checks the other end;
//   * somebody COPIES rather than moves. Two definitions of the same free
//     function is a duplicate-symbol link error today, but gameinterface.cpp
//     leaves the build at A9 and after that a stale copy in it is invisible.
//
// The signature must be immediately followed by `{`: enginecallback.h's
// declarations are the same characters up to a `;` and are not definitions.
//-----------------------------------------------------------------------------
bool DefinedIn( const char *pszFile, const char *pszTightSignature )
{
	const std::string code = Tight( StripComments( Read( pszFile ) ) );
	if ( code.empty() )
		return false;

	std::string needle( pszTightSignature );
	needle += '{';
	return code.find( needle ) != std::string::npos;
}

const char *kSixAccessors[] =
{
	"voidPrecacheMaterial(constchar*pMaterialName)",
	"intGetMaterialIndex(constchar*pMaterialName)",
	"voidPrecacheParticleSystem(constchar*pParticleSystemName)",
	"intGetParticleSystemIndex(constchar*pParticleSystemName)",
};

// TWO OF THE SIX WENT IN THE SERVER GC CULL (2026-10-01): nothing on the server
// called the name-from-index lookups, and --print-gc-sections dropped both.
const char *kGoneInTheCull[] =
{
	"constchar*GetMaterialNameFromIndex(intnMaterialIndex)",
	"constchar*GetParticleSystemNameFromIndex(intnMaterialIndex)",
};

TEST( StringTableIndexZero, TheSixAccessorsLiveWithTheTablesTheyRead )
{
	for ( size_t i = 0; i < sizeof( kSixAccessors ) / sizeof( kSixAccessors[ 0 ] ); i++ )
	{
		EXPECT_TRUE( DefinedIn( kTables, kSixAccessors[ i ] ) )
			<< "'" << kSixAccessors[ i ] << "' is not defined in " << kTables
			<< ". Phase A4 moved the six string-table accessors next to the two "
			<< "tables they read; the two miss-path facts above read their "
			<< "bodies out of that file.";
	}
	for ( size_t i = 0; i < sizeof( kGoneInTheCull ) / sizeof( kGoneInTheCull[ 0 ] ); i++ )
	{
		EXPECT_FALSE( DefinedIn( kTables, kGoneInTheCull[ i ] ) )
			<< "'" << kGoneInTheCull[ i ] << "' is defined in " << kTables
			<< " again, and nothing called it when the cull deleted it.";
	}
}

TEST( StringTableIndexZero, NoSecondCopyOfTheSixIsLeftInGameinterface )
{
	// THE FILE IS GONE, and that settles the "second copy" question the
	// strongest way it can be settled. plan_gamedll_after.md Phase A9 step 4
	// deleted game/server/gameinterface.cpp. Asserted as an absent FILE rather
	// than deleted, because a resurrected gameinterface.cpp comes back with
	// stock's six accessors in it, which is precisely what this fact refuses.
	{
		const char *pszRoot = SrcRoot();
		ASSERT_TRUE( pszRoot != NULL );
		struct stat st;
		EXPECT_NE( 0, stat( ( std::string( pszRoot ) + "/" + kGameIface ).c_str(), &st ) )
			<< kGameIface << " is back, and stock's six string-table accessors "
			<< "come back with it -- a duplicate symbol for each.";
	}

	// AND THE POSITIVE HALF: all six are defined in the file they moved to, so
	// this cannot pass by asserting only an absence.
	for ( size_t i = 0; i < sizeof( kSixAccessors ) / sizeof( kSixAccessors[ 0 ] ); i++ )
	{
		EXPECT_TRUE( DefinedIn( kTables, kSixAccessors[ i ] ) )
			<< "'" << kSixAccessors[ i ] << "' is not defined in " << kTables;
	}
}

//-----------------------------------------------------------------------------
// The seven tables themselves. A missing one is not an index-0 problem; it is a
// null table that every AddString anywhere in the binary walks into.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, AllSevenSharedTablesAreCreatedExactlyOnce )
{
	const std::vector<Op> ops = ShippedSequence();

	static const char *kNames[] = {
		"ParticleEffectNames", "EffectDispatch", "VguiScreen", "Materials",
		"InfoPanel", "Scenes", "ServerMapCycle"
	};

	for ( size_t n = 0; n < sizeof( kNames ) / sizeof( kNames[ 0 ] ); n++ )
	{
		int nSeen = 0;
		for ( size_t i = 0; i < ops.size(); i++ )
			if ( ops[ i ].kind == kCreate && ops[ i ].arg == kNames[ n ] )
				nSeen++;

		EXPECT_EQ( nSeen, 1 )
			<< "the \"" << kNames[ n ] << "\" string table is created " << nSeen
			<< " time(s) and must be created exactly once. Zero means every "
			<< "reader of it dereferences NULL; two means the second one "
			<< "replaces the first and everything that indexed into the first "
			<< "is now pointing at an empty table.";
	}
}

//-----------------------------------------------------------------------------
// The eighth table, which belongs to the game rules rather than to the list.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, TheGameRulesTableIsCreatedToo )
{
	const std::string body = TableFunctionBody();

	EXPECT_NE( body.find( "CreateNetworkStringTables_GameRules();" ), std::string::npos )
		<< "the game rules' own string table is not created. On this build that "
		<< "table is how the SERVER tells the CLIENT to build a rules object at "
		<< "all (shell_gamerules.cpp) -- without it every g_pGameRules-> on the "
		<< "client is a null dereference.";
}

//-----------------------------------------------------------------------------
// NATIVE FIRST, THEN MANAGED. The order of the two halves of the shell's
// CreateNetworkStringTables, which is a contract rather than a preference: a
// mod's tables may assume the seven exist, and nothing native may assume a mod
// did anything.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, TheShellCreatesTheSevenBeforeCallingManagedCode )
{
	const std::string code = Tight( StripComments( Read( kShellGame ) ) );
	ASSERT_FALSE( code.empty() );

	const size_t native  = code.find( "SourceSharp_CreateNetworkStringTables();" );
	const size_t managed = code.find( "ManagedRuntimeServer()->CreateNetworkStringTables();" );

	ASSERT_NE( native, std::string::npos )
		<< "the shell does not create the seven shared tables at all. Part 8 is "
		<< "not optional: a server that creates none has no materials, no "
		<< "effect-dispatch names and no info panel, and nothing says so.";
	ASSERT_NE( managed, std::string::npos )
		<< "the managed CreateNetworkStringTables slot is no longer called. It "
		<< "is where MOD-DECLARED tables go; deleting it because its managed "
		<< "half is empty today means re-deriving the seam later.";

	EXPECT_LT( native, managed )
		<< "the managed slot runs BEFORE the seven shared tables are created. A "
		<< "mod's table code may reasonably assume they exist; nothing native "
		<< "may assume a mod did anything.";
}

//-----------------------------------------------------------------------------
// The shell's own rule: no state at file scope in SourceSharp's C++. Everything
// this file writes is a pre-existing stock global.
//-----------------------------------------------------------------------------
TEST( StringTableIndexZero, TheTableFileAddsNoStateOfItsOwn )
{
	const std::string code = StripComments( Read( kTables ) );
	ASSERT_FALSE( code.empty() );

	// Every table pointer this file writes is STOCK's, defined at
	// gameinterface.cpp:237-243 outside CServerGameDLL's class body. Naming one
	// here WITHOUT `extern` would define a second object of the same name in
	// this translation unit -- which does not fail to link (the definitions are
	// in different TUs and one wins), it just means half the binary writes one
	// pointer and half reads the other.
	size_t nDefinitions = 0, nExterns = 0, at = 0;

	while ( ( at = code.find( "INetworkStringTable *g_pStringTable", at ) ) != std::string::npos )
	{
		const size_t bol = code.rfind( '\n', at );
		const std::string lead = code.substr( bol + 1, at - bol - 1 );

		if ( lead.find( "extern" ) != std::string::npos ) nExterns++;
		else                                              nDefinitions++;

		at += 10;
	}

	EXPECT_EQ( nDefinitions, 0u )
		<< "shell_stringtables.cpp DEFINES a g_pStringTable pointer rather than "
		<< "declaring stock's. Two definitions of one name in two translation "
		<< "units link fine and then disagree at runtime: half the binary writes "
		<< "one and half reads the other, and index 0 is correct in exactly one "
		<< "of them.";

	EXPECT_GT( nExterns, 0u )
		<< "this fact found nothing to check. If the table pointers are no "
		<< "longer named in this file at all, it is passing vacuously and has "
		<< "to be repointed rather than left green.";

	// And the project rule the whole file is written under.
	size_t st = 0;
	while ( ( st = code.find( "\nstatic ", st ) ) != std::string::npos )
	{
		const size_t eol = code.find( '\n', st + 1 );
		const std::string line = code.substr( st + 1, eol - st - 1 );

		EXPECT_NE( line.find( '(' ), std::string::npos )
			<< "shell_stringtables.cpp declares file-scope STATE: " << line
			<< "\nA static FUNCTION is internal linkage and is fine; a static "
			<< "variable is state, and this project's rule is that state lives "
			<< "in a class as a field.";
		st = eol;
	}
}
