//========= SourceSharp — tests for the entity op table ======================//
//
// plan_entity.md §3.4, step 1f. The op number is the whole mechanism: it is
// what one Dispatch slot carries in place of thirteen, and it is a BIT POSITION
// in the per-class override mask, which is what lets a class that does not
// override an op cost zero crossings for it.
//
// So three things are held here.
//
// 1. THE NUMBERS, pinned as literals. They cross the ABI as a bare int32_t.
//    Renumbering one side turns every touch into a think and nothing would say
//    so — the C# class would simply start receiving the wrong callback, which
//    reads as a bug in the C# and is not one. `EntityOpTests.OpNumbersAreFixed`
//    pins the same numbers on the far side, and the cross-read below is what
//    stops one pin going stale while the other moves. A pin on one side alone
//    cannot see a renumbering on the other; that is the whole failure this pair
//    exists to catch.
//
// 2. THE ARGUMENT LAYOUT, by offsetof against the C# declaration's own pins.
//    SourceSharpOpArgs crosses BY POINTER and is read field for field, so a
//    disagreement is a wild read and not a compile error.
//
// 3. THE SHELL ACTUALLY TESTS THE BIT. A relay that forgot ClassOverridesOp
//    would be correct, would pass every functional test there is, and would
//    silently put the crossing back — which is exactly the regression the mask
//    exists to prevent and exactly the kind nothing else would catch.
//
// Engine-free: entity_op.h takes nothing from Valve and sourcesharp.h
// deliberately knows nothing about Source, so both include cleanly here and the
// mechanism can be checked without a running game.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/entity_op.h"
#include "../host/sourcesharp.h"

#include <sys/stat.h>

#include <cstddef>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The tree, found from wherever the binary was run.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

const char *const kManagedEnum =
	"sourcesharp/managed/SourceSharp.Abi/EntityOp.cs";
const char *const kRelayFile =
	"sourcesharp/game/server/shell_dispatch.cpp";
const char *const kShellHeader =
	"sourcesharp/game/server/shell_standalone.h";

//-----------------------------------------------------------------------------
// The value C# gives one op, read out of EntityOp.cs. -1 for a name it does not
// declare; -2 when the file could not be read at all, so "no such op" and
// "no such file" are two different failures rather than one.
//
// A hand parse rather than a regex, because this test links gtest and nothing
// else. The declaration it reads is `    Name = 7,` — one per line, which the
// C# side's own OpValuesAreContiguousFromZero keeps true.
//-----------------------------------------------------------------------------
int ManagedOpValue( const std::string &strName )
{
	const std::string cs = Read( kManagedEnum );
	if ( cs.empty() )
		return -2;

	const std::string needle = "\n    " + strName + " = ";
	const size_t at = cs.find( needle );
	if ( at == std::string::npos )
		return -1;

	return atoi( cs.c_str() + at + needle.size() );
}

//-----------------------------------------------------------------------------
// ENTITY_OP_START_TOUCH is StartTouch. One convention, written once.
//-----------------------------------------------------------------------------
std::string ManagedNameOf( const std::string &strNative )
{
	// Drop the ENTITY_OP_ prefix and camel-case the rest.
	std::string tail = strNative.substr( strlen( "ENTITY_OP_" ) );
	std::string out;
	bool bUpper = true;

	for ( size_t i = 0; i < tail.size(); i++ )
	{
		if ( tail[ i ] == '_' )
		{
			bUpper = true;
			continue;
		}

		out += bUpper ? tail[ i ] : (char)tolower( tail[ i ] );
		bUpper = false;
	}

	return out;
}

struct Op_t
{
	const char *pszNative;
	int nValue;
};

// EVERY op, with its number written out. The list is walked by the cross-read
// below, so an op added to the enum and left off this list is caught by
// EveryOpIsPinnedHere rather than being silently unchecked.
const Op_t kOps[] = {
	{ "ENTITY_OP_SPAWN",            0 },
	{ "ENTITY_OP_KEY_VALUE",        1 },
	{ "ENTITY_OP_INPUT",            2 },
	{ "ENTITY_OP_THINK",            3 },
	{ "ENTITY_OP_REMOVED",          4 },
	{ "ENTITY_OP_START_TOUCH",      5 },
	{ "ENTITY_OP_TOUCH",            6 },
	{ "ENTITY_OP_END_TOUCH",        7 },
	{ "ENTITY_OP_IMPACT",           8 },
	{ "ENTITY_OP_MOVE_DONE",        9 },
	{ "ENTITY_OP_BLOCKED",         10 },
	{ "ENTITY_OP_DAMAGE",          11 },
	{ "ENTITY_OP_KILLED",          12 },
	{ "ENTITY_OP_FILTER_PASSES",   13 },
	{ "ENTITY_OP_POSITION_CHANGED", 14 },
	{ "ENTITY_OP_PHYSICS_CREATED",  15 },
};

const size_t kOpCount = sizeof( kOps ) / sizeof( kOps[ 0 ] );

} // namespace

//=============================================================================//
// 1. The numbers.
//=============================================================================//

//-----------------------------------------------------------------------------
// The pins themselves. Written out as literals rather than looped over the
// enum: a loop would re-derive the numbers from the declaration under test and
// pass whatever they became.
//-----------------------------------------------------------------------------
TEST( EntityOpNumbers, EveryOpHasItsFixedValue )
{
	EXPECT_EQ( 0,  (int)ENTITY_OP_SPAWN );
	EXPECT_EQ( 1,  (int)ENTITY_OP_KEY_VALUE );
	EXPECT_EQ( 2,  (int)ENTITY_OP_INPUT );
	EXPECT_EQ( 3,  (int)ENTITY_OP_THINK );
	EXPECT_EQ( 4,  (int)ENTITY_OP_REMOVED );
	EXPECT_EQ( 5,  (int)ENTITY_OP_START_TOUCH );
	EXPECT_EQ( 6,  (int)ENTITY_OP_TOUCH );
	EXPECT_EQ( 7,  (int)ENTITY_OP_END_TOUCH );
	EXPECT_EQ( 8,  (int)ENTITY_OP_IMPACT );
	EXPECT_EQ( 9,  (int)ENTITY_OP_MOVE_DONE );
	EXPECT_EQ( 10, (int)ENTITY_OP_BLOCKED );
	EXPECT_EQ( 11, (int)ENTITY_OP_DAMAGE );
	EXPECT_EQ( 12, (int)ENTITY_OP_KILLED );
	EXPECT_EQ( 13, (int)ENTITY_OP_FILTER_PASSES );
	EXPECT_EQ( 14, (int)ENTITY_OP_POSITION_CHANGED );
	EXPECT_EQ( 15, (int)ENTITY_OP_PHYSICS_CREATED );
}

//-----------------------------------------------------------------------------
// ENTITY_OP_COUNT is one past the last, which is what the shell range-checks
// against and what sizes the crossing counter array on the far side.
//-----------------------------------------------------------------------------
TEST( EntityOpNumbers, CountIsOnePastTheLastOp )
{
	EXPECT_EQ( (int)kOpCount, (int)ENTITY_OP_COUNT );
	EXPECT_EQ( kOps[ kOpCount - 1 ].nValue + 1, (int)ENTITY_OP_COUNT );
}

//-----------------------------------------------------------------------------
// The table above names every op the header declares. Without this an op could
// be added to entity_op.h, dispatched by the shell, and never appear in any
// pin -- free to be renumbered afterwards by either side.
//-----------------------------------------------------------------------------
TEST( EntityOpNumbers, EveryOpIsPinnedHere )
{
	const std::string header = Read( "sourcesharp/game/shared/entity_op.h" );
	ASSERT_FALSE( header.empty() ) << "entity_op.h could not be read; this test "
	                                  "would then be checking nothing.";

	size_t nDeclared = 0;
	size_t at = header.find( "\tENTITY_OP_" );

	while ( at != std::string::npos )
	{
		const size_t nameStart = at + 1;
		const size_t nameEnd = header.find_first_of( " =", nameStart );
		const std::string strName = header.substr( nameStart, nameEnd - nameStart );

		if ( strName != "ENTITY_OP_COUNT" )
		{
			nDeclared++;

			bool bFound = false;
			for ( size_t i = 0; i < kOpCount; i++ )
			{
				if ( strName == kOps[ i ].pszNative )
					bFound = true;
			}

			EXPECT_TRUE( bFound ) << strName << " is declared in entity_op.h and "
			                                    "has no pin in this file.";
		}

		at = header.find( "\tENTITY_OP_", at + 1 );
	}

	EXPECT_EQ( kOpCount, nDeclared )
		<< "the header declares " << nDeclared << " ops and this file pins "
		<< kOpCount;
}

//=============================================================================//
// The cross-read: the far side's numbers, out of its own declaration.
//=============================================================================//

//-----------------------------------------------------------------------------
// The file is there and parses. A cross-check that silently reads nothing
// passes for free, and this one's whole job is the two sides agreeing.
//-----------------------------------------------------------------------------
TEST( EntityOpParity, TheManagedEnumCanBeRead )
{
	EXPECT_EQ( 0, ManagedOpValue( "Spawn" ) )
		<< "EntityOp.cs could not be read or no longer declares Spawn = 0 one "
		   "per line; every parity check below is measuring nothing.";
}

//-----------------------------------------------------------------------------
// Op for op. This is the check the two independent pin lists cannot make for
// themselves.
//-----------------------------------------------------------------------------
TEST( EntityOpParity, EveryOpHasTheSameNumberInCsharp )
{
	for ( size_t i = 0; i < kOpCount; i++ )
	{
		const std::string strManaged = ManagedNameOf( kOps[ i ].pszNative );
		const int nManaged = ManagedOpValue( strManaged );

		EXPECT_EQ( kOps[ i ].nValue, nManaged )
			<< kOps[ i ].pszNative << " is " << kOps[ i ].nValue
			<< " here and EntityOp." << strManaged << " is " << nManaged;
	}
}

//=============================================================================//
// The mask.
//=============================================================================//

TEST( EntityOpMask, TheBitIsTheShift )
{
	for ( size_t i = 0; i < kOpCount; i++ )
	{
		EXPECT_EQ( (uint32_t)( 1u << kOps[ i ].nValue ),
		           ENTITY_OP_BIT( kOps[ i ].nValue ) );
	}
}

//-----------------------------------------------------------------------------
// THE FLOOR IS FOUR OPS, and exactly four.
//
// They are the ones where the managed side does BOOKKEEPING rather than merely
// calling a virtual a class may not have overridden: spawn and keyvalue create
// the C# instance, removed drops the registry row, and the filter answer has no
// virtual to be computed from. An op added to this set is a crossing nobody can
// ever switch off again; one removed from it is an entity with no managed half.
//-----------------------------------------------------------------------------
TEST( EntityOpMask, TheAlwaysMaskIsTheFourBookkeepingOps )
{
	const uint32_t expected = ENTITY_OP_BIT( ENTITY_OP_SPAWN )
		| ENTITY_OP_BIT( ENTITY_OP_KEY_VALUE )
		| ENTITY_OP_BIT( ENTITY_OP_REMOVED )
		| ENTITY_OP_BIT( ENTITY_OP_FILTER_PASSES );

	EXPECT_EQ( expected, (uint32_t)ENTITY_OP_ALWAYS_MASK );

	// And nothing else. Counted rather than compared alone, so an op OR'd in
	// with the same value by accident would still be caught.
	int nBits = 0;
	for ( int i = 0; i < (int)ENTITY_OP_COUNT; i++ )
	{
		if ( ENTITY_OP_ALWAYS_MASK & ENTITY_OP_BIT( i ) )
			nBits++;
	}

	EXPECT_EQ( 4, nBits );
}

//-----------------------------------------------------------------------------
// The default is ALL ONES and not zero, and the direction is the point: a class
// whose RegisterClassOps never arrived keeps working and merely pays the
// crossings it used to pay. Zero would silently stop delivering touches and
// thinks to entities that DO override them, which is a game broken in a way no
// build error names.
//-----------------------------------------------------------------------------
TEST( EntityOpMask, TheUnregisteredDefaultArmsEverything )
{
	for ( int i = 0; i < (int)ENTITY_OP_COUNT; i++ )
		EXPECT_NE( 0u, ENTITY_OP_MASK_ALL & ENTITY_OP_BIT( i ) ) << "op " << i;
}

//=============================================================================//
// 2. The argument layout.
//=============================================================================//

//-----------------------------------------------------------------------------
// 64 bytes, and no interior padding anywhere.
//
// The struct crosses BY POINTER and is read field for field by a marshaller
// that has its own idea of the layout. Padding is where two declarations of one
// layout disagree without either being wrong, so the order is chosen to have
// none -- and the size is asserted rather than trusted, on both sides
// (EntityOpTests.OpArgsIsSixtyFourBytes).
//-----------------------------------------------------------------------------
TEST( EntityOpArgs, IsSixtyFourBytes )
{
	EXPECT_EQ( (size_t)64, sizeof( SourceSharpOpArgs ) );
}

TEST( EntityOpArgs, EveryFieldIsWhereCsharpPinsIt )
{
	EXPECT_EQ( (size_t)0,  offsetof( SourceSharpOpArgs, pBlob ) );
	EXPECT_EQ( (size_t)8,  offsetof( SourceSharpOpArgs, nBlobBytes ) );
	EXPECT_EQ( (size_t)12, offsetof( SourceSharpOpArgs, hOther ) );
	EXPECT_EQ( (size_t)16, offsetof( SourceSharpOpArgs, pText0 ) );
	EXPECT_EQ( (size_t)24, offsetof( SourceSharpOpArgs, pText1 ) );
	EXPECT_EQ( (size_t)32, offsetof( SourceSharpOpArgs, pText2 ) );
	EXPECT_EQ( (size_t)40, offsetof( SourceSharpOpArgs, pVec0 ) );
	EXPECT_EQ( (size_t)48, offsetof( SourceSharpOpArgs, pVec1 ) );
	EXPECT_EQ( (size_t)56, offsetof( SourceSharpOpArgs, flFloat0 ) );
	EXPECT_EQ( (size_t)60, offsetof( SourceSharpOpArgs, nInt0 ) );
}

//-----------------------------------------------------------------------------
// The two fixed-width words really are four bytes each. The rule the port runs
// on is that anything mirrored byte-for-byte uses int32_t and never int, and
// the fingerprint is computed from the C# side -- so a C++ width change is
// precisely the drift it cannot see.
//-----------------------------------------------------------------------------
TEST( EntityOpArgs, TheScalarsAreFixedWidth )
{
	SourceSharpOpArgs args;

	EXPECT_EQ( (size_t)4, sizeof( args.nBlobBytes ) );
	EXPECT_EQ( (size_t)4, sizeof( args.hOther ) );
	EXPECT_EQ( (size_t)4, sizeof( args.flFloat0 ) );
	EXPECT_EQ( (size_t)4, sizeof( args.nInt0 ) );
}

//=============================================================================//
// 3. The shell tests the bit.
//=============================================================================//

namespace
{

// One relay, and the op it must guard on before it crosses.
struct Relay_t
{
	const char *pszSignature;
	const char *pszOp;
};

const Relay_t kRelays[] = {
	{ "void CStandaloneEntity::OnManagedSpawn()",     "ENTITY_OP_SPAWN" },
	{ "bool CStandaloneEntity::OnManagedKeyValue(",   "ENTITY_OP_KEY_VALUE" },
	{ "bool CStandaloneEntity::OnManagedInput(",      "ENTITY_OP_INPUT" },
	// NO THINK RELAY. plan_entity.md step 1g moved the think schedule into
	// managed code (SourceSharp.Abi/ThinkScheduler.cs) and deleted
	// CStandaloneEntity::OnManagedThink with it, because the native frame
	// dispatching a think was one crossing per thinking entity per frame --
	// exactly what §3.4 forbids. ENTITY_OP_THINK keeps its pinned number 3 (the
	// vocabulary is numeric and shared; a hole is cheaper than a renumber) and
	// the SERVER no longer sends it.
	// NO REMOVAL RELAY EITHER. plan_entity.md step 2c moved the removal
	// announcement into managed code -- EntityDirectory's free pass tells the
	// entity and writes the liveness record before it asks for the shell to be
	// freed -- and deleted the base's relay with it, because ~CStandaloneEntity
	// calling it was one crossing per entity removed and a level teardown was
	// six hundred of them. ENTITY_OP_REMOVED keeps its pinned number 4 for the
	// reason ENTITY_OP_THINK keeps 3, and the SERVER no longer sends it.
	{ "void CStandaloneEntity::OnManagedImpact(",     "ENTITY_OP_IMPACT" },
	{ "void CStandaloneEntity::OnManagedMoveDone()",  "ENTITY_OP_MOVE_DONE" },
	{ "void CStandaloneEntity::OnManagedBlocked(",    "ENTITY_OP_BLOCKED" },
	{ "int CStandaloneEntity::OnManagedDamage(",      "ENTITY_OP_DAMAGE" },
	{ "void CStandaloneEntity::OnManagedKilled(",     "ENTITY_OP_KILLED" },
	// NO PHYSICS-CREATED RELAY (2026-10-12): managed's EntityPhysicsCreation
	// makes the object and tells the class itself. ENTITY_OP_PHYSICS_CREATED
	// keeps its number and its override bit; the SERVER no longer sends it.

	// NO TOUCH RELAYS. plan_entity.md step 2b moved the touch link table into
	// managed code (EntityDirectory's touch half) and deleted ReportTouch,
	// OnManagedStartTouch, OnManagedTouch and OnManagedEndTouch with it,
	// because a shell dispatching a touch was one crossing per touching pair
	// per frame -- the same thing §3.4 forbids and 1g removed for think.
	// Native hands managed the overlapping pairs once a frame
	// (SourceSharpEngineApi::EntityOverlapReport) and the diff raises the three
	// edges on the instance directly.
	//
	// ENTITY_OP_START_TOUCH, ENTITY_OP_TOUCH and ENTITY_OP_END_TOUCH keep their
	// pinned numbers 5, 6 and 7 and their override bits -- they are still what
	// a class OVERRIDES, and managed code reads the bits. The SERVER no longer
	// sends them.
};

const size_t kRelayCount = sizeof( kRelays ) / sizeof( kRelays[ 0 ] );

// The braced body of a function, from its signature.
std::string BodyOf( const std::string &file, const std::string &strSignature )
{
	const size_t at = file.find( strSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = file.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < file.size(); i++ )
	{
		if ( file[ i ] == '{' )
			nDepth++;
		else if ( file[ i ] == '}' && --nDepth == 0 )
			return file.substr( open, i - open + 1 );
	}

	return std::string();
}

} // namespace

//-----------------------------------------------------------------------------
// EVERY RELAY GUARDS ON ITS OWN OP BEFORE IT CROSSES.
//
// This is the fact the crossing budget rests on and it has no runtime symptom.
// A relay that dropped its ClassOverridesOp would be CORRECT -- the managed side
// would receive an op it does nothing with and return -- so every functional
// test in the tree would pass, the live gate would pass, and the only trace
// would be a crossing count that quietly went back up. The live gate's budget
// is what catches it after the fact; this catches it in the diff.
//
// The op is checked as well as the call, because guarding on the WRONG op is
// the same bug with an extra step: a think that tested the touch bit would
// cross for classes that touch and stay silent for classes that think.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, EveryRelayTestsItsOverrideBit )
{
	const std::string file = Read( kRelayFile );
	ASSERT_FALSE( file.empty() ) << kRelayFile << " could not be read.";

	for ( size_t i = 0; i < kRelayCount; i++ )
	{
		const std::string body = BodyOf( file, kRelays[ i ].pszSignature );

		ASSERT_FALSE( body.empty() )
			<< kRelays[ i ].pszSignature << " is not in the file; this check "
			   "measures nothing until that is fixed -- do not delete it.";

		const std::string guard =
			std::string( "ClassOverridesOp( " ) + kRelays[ i ].pszOp + " )";

		EXPECT_NE( std::string::npos, body.find( guard ) )
			<< kRelays[ i ].pszSignature << " does not test "
			<< kRelays[ i ].pszOp << " before crossing. A relay without its "
			   "guard is CORRECT and silently costs a managed transition per "
			   "call for every class that does not override it.";
	}
}

//-----------------------------------------------------------------------------
// AND THE LIST ABOVE IS EVERY RELAY. Counted rather than listed, because the
// failure this guards against is a FOURTEENTH relay added later without a
// guard -- which the loop above cannot see, since it only walks what it was
// given.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, TheRelayListIsEveryRelay )
{
	const std::string file = Read( kRelayFile );
	ASSERT_FALSE( file.empty() ) << kRelayFile << " could not be read.";

	size_t nDefined = 0;
	size_t at = file.find( "CStandaloneEntity::OnManaged" );

	while ( at != std::string::npos )
	{
		// Definitions only: a definition is `Type CStandaloneEntity::OnX(`,
		// and this file has no declarations of them at all.
		nDefined++;
		at = file.find( "CStandaloneEntity::OnManaged", at + 1 );
	}

	// EIGHT OnManaged* definitions, and the guard list above names all eight.
	//
	// NINE     until the entity physics init went managed and took
	//          OnManagedPhysicsCreated with it (2026-10-12).
	// EIGHT    until physics stock parity B4 added OnManagedPhysicsCreated.
	// THIRTEEN until step 1g deleted OnManagedThink.
	// TWELVE   until step 2c deleted OnManagedRemoved.
	// ELEVEN   until step 2b deleted the three touch forwarders and their shared
	//          body ReportTouch -- the touch link table is managed now.
	//
	// The count moves WITH each deletion rather than the deletion being hidden
	// by a >=. It is one assertion and not two: 2b and 2c each edited THIS
	// NUMBER on their own branch, and a keep-both merge left 11 and 9 sitting
	// side by side, contradicting each other so that one had to fail. Counted
	// against the file rather than reasoned from the two branches.
	EXPECT_EQ( (size_t)8, nDefined )
		<< "shell_dispatch.cpp defines " << nDefined
		<< " OnManaged* relays; the guard list in this file names 8. A relay "
		   "that is not in the list is a relay nothing checks the guard of.";
}

//-----------------------------------------------------------------------------
// THE THREE TOUCH EDGES DO NOT CROSS AT ALL ANY MORE -- plan_entity.md step 2b.
//
// This test used to assert that OnManagedStartTouch, OnManagedTouch and
// OnManagedEndTouch each forwarded to ReportTouch, so that the count above
// could not balance while one of them grew an unguarded body of its own. All
// four are deleted, and the same hazard has the same shape: the count balances
// again the day somebody puts a touch relay back, and a relay that dispatches a
// touch from C++ is one inbound crossing per touching pair per frame.
//
// So the check is inverted rather than dropped. NONE of the four names may
// appear as a definition in the relay file.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, TheTouchEdgesAreNotRelayedFromCpp )
{
	const std::string file = Read( kRelayFile );
	ASSERT_FALSE( file.empty() ) << kRelayFile << " could not be read.";

	static const char *const kGone[] = {
		"void CStandaloneEntity::OnManagedStartTouch(",
		"void CStandaloneEntity::OnManagedTouch(",
		"void CStandaloneEntity::OnManagedEndTouch(",
		"void CStandaloneEntity::ReportTouch(",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, file.find( kGone[ i ] ) )
			<< kGone[ i ] << " is back in " << kRelayFile
			<< ". Step 2b made the touch diff managed: a native relay for an "
			   "edge is one inbound crossing per touching pair per frame, "
			   "which is what the step removed.";
	}
}

//-----------------------------------------------------------------------------
// AND THE NATIVE SIDE ANSWERS THE ONE QUESTION IT KEPT.
//
// The report is what replaced them. Without this, deleting the relays above
// would pass every check in this file with nothing reporting an overlap at all
// -- which is a game where no trigger fires and no pickup can be collected, and
// which looks from outside exactly like a map whose outputs are not wired.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, TheOverlapReportIsWhatReplacedThem )
{
	const std::string shell =
		Read( "sourcesharp/game/server/shell_standalone_base.cpp" );

	ASSERT_FALSE( shell.empty() ) << "shell_standalone_base.cpp could not be read.";

	EXPECT_NE( std::string::npos,
	           shell.find( "int32_t SourceSharp_EntityOverlapReport( uint32_t *pOutPairs, "
	                       "int32_t nMaxPairs )" ) )
		<< "SourceSharp_EntityOverlapReport is not defined where step 2b put it.";

	// A server shim-area method since plan_engineapi_vtables C6c.
	const std::string header = Read( "sourcesharp/game/shared/ishim_areas.h" );
	ASSERT_FALSE( header.empty() ) << "ishim_areas.h could not be read.";

	EXPECT_NE( std::string::npos,
	           header.find( "virtual int32_t EntityOverlapReport( uint32_t * pOutPairs, "
	                        "int32_t nMaxPairs ) = 0;" ) )
		<< "the EntityOverlapReport slot is not on a server shim area.";
}

//-----------------------------------------------------------------------------
// THE GUARD ITSELF ANSWERS YES FOR A SHELL WITH NO CLASS.
//
// m_pClass is NULL between the allocation and PostConstruct. An entity that
// crossed when it did not need to is a wasted call; an entity that did NOT
// cross when it should have is a spawn that never reached C#. The two are not
// symmetrical, so the unknown case has to take the safe side -- and this is a
// source check because ClassOverridesOp is an inline member of a class no
// engine-free test can construct.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, TheGuardAnswersYesWithNoClass )
{
	const std::string header = Read( kShellHeader );
	ASSERT_FALSE( header.empty() ) << kShellHeader << " could not be read.";

	const std::string body = BodyOf( header, "bool ClassOverridesOp( int nOp ) const" );
	ASSERT_FALSE( body.empty() ) << "ClassOverridesOp is not declared where this "
	                                "test reads it.";

	EXPECT_NE( std::string::npos, body.find( "if ( !m_pClass )\n\t\t\treturn true;" ) )
		<< "the guard no longer answers TRUE for a shell with no class "
		   "descriptor. Answering false there is an entity whose spawn never "
		   "reaches managed code.";
}

//-----------------------------------------------------------------------------
// THE CLASS THE STEP DELETED STAYS DELETED.
//
// CStandaloneScriptedEntity existed to hold thirteen relays into thirteen ABI
// slots. There is one slot now, so a subclass whose entire content is a switch
// on an integer is not a class -- and §3.1 says there is exactly ONE shell per
// side. The gate for the step is `nm -C server.so | grep -c` reading 0; this is
// the source half of it, which fails in the diff rather than after a link.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, TheScriptedSubclassDoesNotComeBack )
{
	static const char *const kFiles[] = {
		"sourcesharp/game/server/shell_standalone.h",
		"sourcesharp/game/server/shell_standalone_class.h",
		"sourcesharp/game/server/shell_standalone_class.cpp",
		"sourcesharp/game/server/shell_dispatch.cpp",
		"sourcesharp/game/server/shell_standalone_player.h",
	};

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string body = Read( kFiles[ i ] );
		ASSERT_FALSE( body.empty() ) << kFiles[ i ] << " could not be read.";

		EXPECT_EQ( std::string::npos, body.find( "class CStandaloneScriptedEntity" ) )
			<< kFiles[ i ] << " declares CStandaloneScriptedEntity again. "
			   "plan_entity.md §3.1: one shell per side, final, with no "
			   "subclasses but the player's.";
	}
}

//-----------------------------------------------------------------------------
// THE OUTBOUND OP'S HANDLER IS THE ONE THE NATIVE PATH USES.
//
// ENTITY_OP_POSITION_CHANGED runs CStandaloneEntity::OnPositionChanged, which is
// also the tail of SetAbsTransform -- so the op path is not a copy of the
// native path and the two cannot drift. Were it a copy, the op's handler would
// be exercised only by its own test while every move in the game ran the other
// one.
//-----------------------------------------------------------------------------
TEST( EntityOpDispatch, ThePositionOpSharesTheNativePath )
{
	const std::string file = Read( kRelayFile );
	ASSERT_FALSE( file.empty() ) << kRelayFile << " could not be read.";

	const std::string body =
		BodyOf( file, "int32_t SourceSharp_EntityDispatch(" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_EntityDispatch is not in the file.";

	EXPECT_NE( std::string::npos, body.find( "ENTITY_OP_POSITION_CHANGED" ) );
	EXPECT_NE( std::string::npos, body.find( "OnPositionChanged( vecPrevOrigin )" ) )
		<< "the op no longer calls the same function SetAbsTransform's tail "
		   "became; a second copy of that work is a second thing to keep right.";

	const std::string setter = Read( "sourcesharp/game/server/shell_standalone_base.cpp" );
	ASSERT_FALSE( setter.empty() );

	const std::string transform =
		BodyOf( setter, "void CStandaloneEntity::SetAbsTransform(" );

	ASSERT_FALSE( transform.empty() );

	EXPECT_NE( std::string::npos, transform.find( "OnPositionChanged( vecPrevOrigin )" ) )
		<< "SetAbsTransform no longer delegates to the op's handler, so the "
		   "handler is exercised only by its own test.";
}
