//========= SourceSharp — where the four table chains end ====================//
//
// Phase 13 step 4 makes `CBaseEntity` an alias for `IGameEntity`. Five static
// DATA members of CBaseEntity stand in the way, all macro-generated:
//
//     m_DataMap           both sides   DECLARE_SIMPLE_DATADESC
//     m_PredMap           client       DECLARE_PREDICTABLE_IMPL
//     m_pClassSendTable   server       DECLARE_SERVERCLASS_IMPL
//     m_pClassRecvTable   client       DECLARE_CLIENTCLASS_IMPL
//
// A static data member of an ALIAS is not a link error, it is a compile error,
// and it is NOT confined to the three files that define them. Each of the four
// table macros names the member of the BASE class, so every derived class
// writes it in its OWN translation unit:
//
//     BEGIN_DATADESC        -> DataMapAccess( (BaseClass *)NULL, &p )  -> &T::m_DataMap
//     BEGIN_PREDICTION_DATA -> &BaseClass::m_PredMap
//     BEGIN_SEND_TABLE      -> className::BaseClass::m_pClassSendTable
//     BEGIN_RECV_TABLE      -> className::BaseClass::m_pClassRecvTable
//
// Measured at 674f3e0e over the release object tree, by undefined-symbol
// reference (`nm -u OBJ | c++filt`):
//
//     m_DataMap           19 server objects,  2 client
//     m_pClassSendTable    4 server
//     m_pClassRecvTable                       6 client
//     m_PredMap                               6 client
//     ------------------------------------------------------
//     37 references across 29 DISTINCT objects (several name two or three)
//
// THE FIX, and the one design decision in it: each of the four is reached
// through a call, and each gets a NON-TEMPLATE overload taking IGameEntity *.
// For IGameEntity * that overload is an exact match and beats the template, so
// the chain terminates in NULL -- which is what BEGIN_DATADESC_NO_BASE has
// always meant and is the truth here, because the base entity's tables live on
// the managed side. For a class DERIVED from IGameEntity the template is the
// exact match and the overload would need a derived-to-base conversion, so the
// template still wins and a real base class still contributes its real table.
//
// WHAT THIS FILE IS FOR. The overload-resolution claim in the paragraph above
// is the whole fix, and it is invisible: it is inert in an unarmed tree (no
// class has IGameEntity for a BaseClass today), so nothing in a normal build
// exercises it, and an armed survey only shows the errors GONE -- never that
// the derived case still resolves the other way. Both halves are asserted here.
//
// NO STAND-IN. This includes the REAL public/datamap.h, public/dt_send.h,
// public/dt_recv.h and public/igameentity.h. A replica of four overload sets
// would drift from them silently, and drift is exactly what would hide the
// failure: a replica in which the template ALWAYS won would pass every
// assertion about NULL that a correct one passes.
//
//=============================================================================//
#include "gtest/gtest.h"

// This binary links mathlib -- not because it does any geometry, but because
// igameentity.h includes mathlib/vector.h. mathlib_init_guard_test enforces
// the rule for EVERY build_and_run_mathlib binary regardless, and it is right
// to: an uninitialised mathlib prints its assertion and returns, so a binary
// that skips this is green for a reason that is not evidence. This file was
// written without it and that guard is what caught it.
#include "mathlib_test_env.h"

#include <dirent.h>

#include <fstream>
#include <string>

// The real thing. datamap.h opens tier0/memdbgon.h and closes it again with
// memdbgoff.h before it ends, so including it before gtest's headers would be
// the only ordering that mattered -- and gtest is above, so it does not.
#include "datamap.h"
#include "dt_send.h"
#include "dt_recv.h"
#include "igameentity.h"

namespace
{

//-----------------------------------------------------------------------------
// A class that OWNS all four tables, spelled exactly as the four DECLARE_
// macros spell them. Not derived from anything -- this is the ordinary SDK
// base class whose behaviour must not change.
//-----------------------------------------------------------------------------
struct OwnsTables
{
	static datamap_t  m_DataMap;
	static datamap_t  m_PredMap;
	static SendTable *m_pClassSendTable;
	static RecvTable *m_pClassRecvTable;
};

datamap_t  OwnsTables::m_DataMap = { 0, 0, "OwnsTables", NULL };
datamap_t  OwnsTables::m_PredMap = { 0, 0, "OwnsTables", NULL };
SendTable *OwnsTables::m_pClassSendTable = NULL;
RecvTable *OwnsTables::m_pClassRecvTable = NULL;

//-----------------------------------------------------------------------------
// THE CASE THE OVERLOAD MUST NOT CAPTURE: derived from the boundary, and
// carrying its own four tables. Under the flip this is every SDK class that
// keeps a datadesc of its own while chaining to a managed base. If the
// IGameEntity overload won here, that class's table would silently vanish from
// the chain -- a save/restore and networking bug with no diagnostic at all.
//
// Never instantiated, so IGameEntity's six pure virtuals stay unimplemented;
// only pointers to it are formed, which needs no complete-object semantics.
//-----------------------------------------------------------------------------
struct DerivedOwnsTables : public IGameEntity
{
	static datamap_t  m_DataMap;
	static datamap_t  m_PredMap;
	static SendTable *m_pClassSendTable;
	static RecvTable *m_pClassRecvTable;
};

datamap_t  DerivedOwnsTables::m_DataMap = { 0, 0, "DerivedOwnsTables", NULL };
datamap_t  DerivedOwnsTables::m_PredMap = { 0, 0, "DerivedOwnsTables", NULL };
SendTable *DerivedOwnsTables::m_pClassSendTable = NULL;
RecvTable *DerivedOwnsTables::m_pClassRecvTable = NULL;

// Distinct non-null values, so "the right table came back" is a real
// comparison rather than NULL == NULL. Set once, in SetUp of each test that
// reads them, to keep the static initialisers above trivial.
SendTable *const kOwnSend      = reinterpret_cast<SendTable *>( 0x1001 );
SendTable *const kDerivedSend  = reinterpret_cast<SendTable *>( 0x1002 );
RecvTable *const kOwnRecv      = reinterpret_cast<RecvTable *>( 0x2001 );
RecvTable *const kDerivedRecv  = reinterpret_cast<RecvTable *>( 0x2002 );

class BaseTableChainEnd : public ::testing::Test
{
protected:
	void SetUp() override
	{
		OwnsTables::m_pClassSendTable        = kOwnSend;
		DerivedOwnsTables::m_pClassSendTable = kDerivedSend;
		OwnsTables::m_pClassRecvTable        = kOwnRecv;
		DerivedOwnsTables::m_pClassRecvTable = kDerivedRecv;
	}
};

//-----------------------------------------------------------------------------
// m_DataMap  --  BEGIN_DATADESC's GetBaseMap()
//-----------------------------------------------------------------------------

TEST_F( BaseTableChainEnd, DataMapChainEndsAtTheManagedBoundary )
{
	// The reference every one of the 19 server objects writes, once the alias
	// is armed. Before the overload this line did not COMPILE.
	datamap_t *p = reinterpret_cast<datamap_t *>( 0xDEAD );
	DataMapAccess( static_cast<IGameEntity *>( NULL ), &p );
	EXPECT_EQ( NULL, p ) << "the datadesc chain must terminate at IGameEntity";
}

TEST_F( BaseTableChainEnd, DataMapStillChainsForAnOrdinaryBase )
{
	datamap_t *p = NULL;
	DataMapAccess( static_cast<OwnsTables *>( NULL ), &p );
	EXPECT_EQ( &OwnsTables::m_DataMap, p );
}

TEST_F( BaseTableChainEnd, DataMapStillChainsForABaseDERIVEDFromTheBoundary )
{
	// THE HALF AN ARMED SURVEY CANNOT SHOW. If the non-template overload were
	// greedy -- or if this were a SFINAE "has m_DataMap" probe that guessed
	// wrong -- this would answer NULL and the class's own save fields would
	// leave the chain with no diagnostic anywhere.
	datamap_t *p = NULL;
	DataMapAccess( static_cast<DerivedOwnsTables *>( NULL ), &p );
	EXPECT_EQ( &DerivedOwnsTables::m_DataMap, p );
	EXPECT_NE( static_cast<datamap_t *>( NULL ), p );
}

//-----------------------------------------------------------------------------
// m_PredMap  --  BEGIN_PREDICTION_DATA's aggregate initialiser
//-----------------------------------------------------------------------------

TEST_F( BaseTableChainEnd, PredMapChainEndsAtTheManagedBoundary )
{
	EXPECT_EQ( NULL, PredMapBaseAccess( static_cast<IGameEntity *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, PredMapStillChainsForAnOrdinaryBase )
{
	// The half m_DataMap has had all along and m_PredMap did not, which is a
	// gap GCC found before anybody did: OwnsTables::m_PredMap was DEFINED and
	// never named, so -Wunused-variable reported it on every native test
	// build. The definition was not the mistake -- the missing assertion was.
	//
	// An ordinary SDK base, not derived from the boundary, must chain to its
	// OWN prediction table. If the IGameEntity overload were greedy this would
	// answer NULL and every predicted class in the tree would silently lose
	// its own fields, which is the same failure the DERIVED case above guards
	// from the other side.
	EXPECT_EQ( &OwnsTables::m_PredMap,
	           PredMapBaseAccess( static_cast<OwnsTables *>( NULL ) ) );
	EXPECT_NE( static_cast<datamap_t *>( NULL ),
	           PredMapBaseAccess( static_cast<OwnsTables *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, PredMapStillChainsForABaseDERIVEDFromTheBoundary )
{
	EXPECT_EQ( &DerivedOwnsTables::m_PredMap,
	           PredMapBaseAccess( static_cast<DerivedOwnsTables *>( NULL ) ) );

	// ...and the two are different objects, so the line above is a comparison
	// rather than an accident of both being the same table.
	EXPECT_NE( PredMapBaseAccess( static_cast<OwnsTables *>( NULL ) ),
	           PredMapBaseAccess( static_cast<DerivedOwnsTables *>( NULL ) ) );
}

// THE INITIALISATION CATEGORY, asserted rather than described. BEGIN_PREDICTION_DATA
// puts this call inside the aggregate initialiser of a static datamap_t, so it
// has to stay a constant expression; a plain inline function would compile and
// silently demote that object to dynamic initialisation. static_assert is the
// only thing that can tell the difference, and it fails at BUILD time, so this
// pair guards the property even though neither line runs.
constexpr datamap_t *kPredEndsAtBoundary =
	PredMapBaseAccess( static_cast<IGameEntity *>( NULL ) );
static_assert( kPredEndsAtBoundary == NULL,
               "PredMapBaseAccess(IGameEntity*) must be usable in a constant "
               "expression: BEGIN_PREDICTION_DATA calls it from the aggregate "
               "initialiser of a static datamap_t." );

constexpr datamap_t *kPredDerived =
	PredMapBaseAccess( static_cast<DerivedOwnsTables *>( NULL ) );
static_assert( kPredDerived == &DerivedOwnsTables::m_PredMap,
               "the template arm must ALSO be a constant expression, or every "
               "predicted class in the SDK loses its constant initialisation." );

//-----------------------------------------------------------------------------
// m_pClassSendTable  --  BEGIN_SEND_TABLE's "baseclass" prop
//-----------------------------------------------------------------------------

TEST_F( BaseTableChainEnd, SendTableChainEndsAtTheManagedBoundary )
{
	EXPECT_EQ( NULL, SendTableBaseAccess( static_cast<IGameEntity *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, SendTableStillChainsForAnOrdinaryBase )
{
	EXPECT_EQ( kOwnSend, SendTableBaseAccess( static_cast<OwnsTables *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, SendTableStillChainsForABaseDERIVEDFromTheBoundary )
{
	EXPECT_EQ( kDerivedSend,
	           SendTableBaseAccess( static_cast<DerivedOwnsTables *>( NULL ) ) );
}

//-----------------------------------------------------------------------------
// m_pClassRecvTable  --  BEGIN_RECV_TABLE's "baseclass" prop
//-----------------------------------------------------------------------------

TEST_F( BaseTableChainEnd, RecvTableChainEndsAtTheManagedBoundary )
{
	EXPECT_EQ( NULL, RecvTableBaseAccess( static_cast<IGameEntity *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, RecvTableStillChainsForAnOrdinaryBase )
{
	EXPECT_EQ( kOwnRecv, RecvTableBaseAccess( static_cast<OwnsTables *>( NULL ) ) );
}

TEST_F( BaseTableChainEnd, RecvTableStillChainsForABaseDERIVEDFromTheBoundary )
{
	EXPECT_EQ( kDerivedRecv,
	           RecvTableBaseAccess( static_cast<DerivedOwnsTables *>( NULL ) ) );
}

//-----------------------------------------------------------------------------
// THE MACROS ACTUALLY ROUTE THROUGH THE ACCESSORS.
//
// Every assertion above would still pass if BEGIN_SEND_TABLE and
// BEGIN_RECV_TABLE had been left naming `className::BaseClass::m_pClassSendTable`
// directly -- the accessors would simply be unused, and the flip would still
// not parse. The macros cannot be EXPANDED here (they define namespaces,
// tables and an init function, and need a real ServerClass/ClientClass), so
// what is checked is that the macro body names the accessor. Stringised from
// the real macro, so it tracks the real definition rather than a copy of it.
//-----------------------------------------------------------------------------

// VARIADIC on purpose: the macro under test expands to a namespace, a table
// and an init function, so the text handed to the stringiser is full of
// top-level commas. A one-parameter stringiser is a compile error here, not a
// wrong answer -- which is how this was found.
#define SS_STRINGISE_( ... ) #__VA_ARGS__
#define SS_STRINGISE( ... )  SS_STRINGISE_( __VA_ARGS__ )

TEST_F( BaseTableChainEnd, SendTableMacroRoutesThroughTheAccessor )
{
	const std::string body = SS_STRINGISE( BEGIN_SEND_TABLE( CFoo, DT_Foo ) );
	EXPECT_NE( std::string::npos, body.find( "SendTableBaseAccess" ) )
		<< "BEGIN_SEND_TABLE stopped routing through the accessor; the flip "
		<< "will not parse again. Body was: " << body;
	EXPECT_EQ( std::string::npos, body.find( "BaseClass::m_pClassSendTable" ) )
		<< "BEGIN_SEND_TABLE still names the member directly: " << body;
}

TEST_F( BaseTableChainEnd, RecvTableMacroRoutesThroughTheAccessor )
{
	const std::string body = SS_STRINGISE( BEGIN_RECV_TABLE( CFoo, DT_Foo ) );
	EXPECT_NE( std::string::npos, body.find( "RecvTableBaseAccess" ) )
		<< "BEGIN_RECV_TABLE stopped routing through the accessor; the flip "
		<< "will not parse again. Body was: " << body;
	EXPECT_EQ( std::string::npos, body.find( "BaseClass::m_pClassRecvTable" ) )
		<< "BEGIN_RECV_TABLE still names the member directly: " << body;
}

//-----------------------------------------------------------------------------
// THE FOURTH MACRO, which cannot be stringised here.
//
// BEGIN_PREDICTION_DATA lives in game/shared/predictable_entity.h behind
// `#if defined( CLIENT_DLL )` and drags in predictioncopy.h, recvproxy.h and
// the rest of the client prelude -- so this TU cannot include it to stringise
// it the way the send and recv macros are stringised above. Its ROUTING is
// therefore read out of the source instead.
//
// Comments are stripped first, and that is load-bearing rather than tidy: the
// text this looks for is spelled out in the prose above the macro, in both
// predictable_entity.h and datamap.h, so an unstripped scan passes on the
// comment alone and keeps passing after somebody reverts the code.
//-----------------------------------------------------------------------------

const char *SourceRoot()
{
	static const char *kCandidates[] = { "game", "src/game", "../src/game" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadGameFile( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

// Blanks // and /* */ comments, keeping newlines so nothing else shifts.
std::string StripComments( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );

	enum { kCode, kLine, kBlock } state = kCode;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( state == kCode )
		{
			if ( in[ i ] == '/' && i + 1 < in.size() && in[ i + 1 ] == '/' )
			{
				state = kLine; i++; continue;
			}
			if ( in[ i ] == '/' && i + 1 < in.size() && in[ i + 1 ] == '*' )
			{
				state = kBlock; i++; continue;
			}
			out += in[ i ];
		}
		else if ( state == kLine )
		{
			if ( in[ i ] == '\n' ) { state = kCode; out += '\n'; }
		}
		else
		{
			if ( in[ i ] == '*' && i + 1 < in.size() && in[ i + 1 ] == '/' )
			{
				state = kCode; i++;
			}
		}
	}
	return out;
}

const std::string &PredictableEntityCode()
{
	static const std::string s = StripComments( ReadGameFile( "shared/predictable_entity.h" ) );
	return s;
}

TEST_F( BaseTableChainEnd, FoundPredictableEntityHeader )
{
	// THE ANCHOR. Without it every assertion below passes on an empty string
	// when the file moves or the working directory changes -- a check that
	// cannot fail. Deliberately NOT a line count: the file is edited often and
	// a count would be a false alarm rather than a real one.
	ASSERT_NE( std::string::npos,
	           PredictableEntityCode().find( "#define BEGIN_PREDICTION_DATA_GUTS" ) )
		<< "game/shared/predictable_entity.h not found or not the expected file; "
		<< "every other assertion in this group would be vacuous.";
}

TEST_F( BaseTableChainEnd, PredictionMacroRoutesThroughTheAccessor )
{
	const std::string &code = PredictableEntityCode();
	ASSERT_NE( std::string::npos, code.find( "#define BEGIN_PREDICTION_DATA_GUTS" ) );

	EXPECT_NE( std::string::npos, code.find( "PredMapBaseAccess( (BaseClass *)NULL )" ) )
		<< "BEGIN_PREDICTION_DATA stopped routing through PredMapBaseAccess; "
		<< "the 6 client objects that name C_BaseEntity::m_PredMap will stop "
		<< "parsing again the moment the alias is armed.";

	EXPECT_EQ( std::string::npos, code.find( "&BaseClass::m_PredMap" ) )
		<< "BEGIN_PREDICTION_DATA still names the member directly.";
}

//=============================================================================//
// THE FREE-RIDERS -- the case the IGameEntity * overload CANNOT reach.
//
// Everything above ends a chain whose BaseClass IS the boundary. An armed
// survey at 19451ca6, with all four of those overloads in place, still reported
// this shape in eight translation units:
//
//   'm_DataMap' is not a member of 'CPointEntity'     GameStats.o client.o
//                                                     sound.o soundent.o subs.o
//   'm_DataMap' is not a member of 'CLogicalEntity'   physics_npc_solver.o
//                                                     shell_entity_orphans.o
//   'm_DataMap' is not a member of 'C_BaseAnimating'  c_baseanimating.o
//   'm_DataMap' is not a member of 'C_Sprite'         SpriteTrail.o
//
// A FREE-RIDER is a class that derives from the boundary and declares NO
// datadesc of its own. It therefore has no m_DataMap; the name resolves
// through the base. A class deriving from a free-rider emits
// DataMapAccess( (FreeRider *)NULL, &p ), for which the TEMPLATE is the exact
// match -- so the template wins, and its body names the inherited member,
// which armed does not exist. The IGameEntity * overload is not even a
// contender: it would need a derived-to-base conversion.
//
// The fix is one more non-template overload per free-rider, beside its class,
// under #ifdef SS_CBASEENTITY_ALIAS -- sourcesharp/game/server/entity_bases.h,
// game/client/c_baseanimating.h, game/shared/SpriteTrail.h. Two claims are
// asserted below and neither is visible in a survey: the overload-resolution
// behaviour (here, on stand-ins), and that the three real headers actually
// carry it inside the right guard (further down, by reading them).
//=============================================================================//

//-----------------------------------------------------------------------------
// Derives from the boundary, declares no datadesc. Naming T::m_DataMap for
// T = this type is precisely the eight diagnostics above.
//-----------------------------------------------------------------------------
struct FreeRider : public IGameEntity
{
};

inline void DataMapAccess( FreeRider *, datamap_t **p ) { *p = NULL; }

//-----------------------------------------------------------------------------
// A free-rider UNDER a free-rider. Not hypothetical: CServerOnlyPointEntity
// and CLogicalEntity both sit under CServerOnlyEntity, so two of these
// overloads are visible at once and one argument type converts to the other.
// If that were ambiguous this would not compile.
//-----------------------------------------------------------------------------
struct NestedFreeRider : public FreeRider
{
};

inline void DataMapAccess( NestedFreeRider *, datamap_t **p ) { *p = NULL; }

//-----------------------------------------------------------------------------
// The class that chains THROUGH a free-rider and has a real datadesc of its
// own -- C_ClientRagdoll, CSpriteTrail, and the twenty-odd point entities.
//-----------------------------------------------------------------------------
struct RidesOnFreeRider : public NestedFreeRider
{
	static datamap_t m_DataMap;
};

datamap_t RidesOnFreeRider::m_DataMap = { 0, 0, "RidesOnFreeRider", NULL };

TEST_F( BaseTableChainEnd, DataMapChainEndsAtAFreeRider )
{
	datamap_t *p = reinterpret_cast<datamap_t *>( 0xDEAD );
	DataMapAccess( static_cast<FreeRider *>( NULL ), &p );
	EXPECT_EQ( NULL, p )
		<< "a class deriving from a free-rider must terminate the chain; "
		<< "this is the CPointEntity/CLogicalEntity/C_BaseAnimating/CSprite case";
}

TEST_F( BaseTableChainEnd, DataMapChainEndsAtANestedFreeRider )
{
	// The template is the EXACT match for NestedFreeRider * and its body would
	// be a hard error -- there is no NestedFreeRider::m_DataMap. That this
	// compiles at all is the assertion; the value is the confirmation.
	datamap_t *p = reinterpret_cast<datamap_t *>( 0xDEAD );
	DataMapAccess( static_cast<NestedFreeRider *>( NULL ), &p );
	EXPECT_EQ( NULL, p )
		<< "two free-rider overloads in one hierarchy must resolve to the "
		<< "exact match, not ambiguously and not to the base one";
}

TEST_F( BaseTableChainEnd, FreeRiderOverloadDoesNotSwallowARealDatadescBelowIt )
{
	// THE HALF AN ARMED SURVEY CANNOT SHOW, one level lower than
	// DataMapStillChainsForABaseDERIVEDFromTheBoundary. If a free-rider
	// overload out-resolved the template for a class that DOES own a datadesc,
	// that class's save fields would leave the chain with no diagnostic
	// anywhere -- the same silent failure the unguarded version of these
	// overloads would cause in the shipping build.
	datamap_t *p = NULL;
	DataMapAccess( static_cast<RidesOnFreeRider *>( NULL ), &p );
	EXPECT_EQ( &RidesOnFreeRider::m_DataMap, p );
	EXPECT_NE( static_cast<datamap_t *>( NULL ), p );
}

//-----------------------------------------------------------------------------
// ...AND THE THREE REAL HEADERS CARRY IT, INSIDE THE RIGHT GUARD.
//
// The three assertions above are about overload resolution and would pass
// unchanged if no game header had ever been edited. These read the real files.
// They cannot be behavioural: entity_bases.h, c_baseanimating.h and
// SpriteTrail.h need the whole server or client prelude, which this
// mathlib-only binary does not have.
//
// THE GUARD IS THE POINT, not the presence of the overload. datamap.h's
// IGameEntity * overload is safe unguarded because nothing has IGameEntity for
// a BaseClass in an unarmed tree. These are NOT unreachable: disarmed, an
// unguarded free-rider overload beats the template for every class deriving
// from a point entity and answers NULL where the answer must be
// &CBaseEntity::m_DataMap -- no compile error, just a silently truncated
// save/restore chain in the shipping build. So containment is checked by
// POSITION -- the overload must lie between its guard and the next #endif --
// rather than by the guard merely existing somewhere in the file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE GUARD WAS THE POINT UNTIL THE ALIAS WENT, AND NOW ITS ABSENCE IS.
//
// `#ifdef SS_CBASEENTITY_ALIAS` protected a configuration that no longer
// exists. Its whole argument was about the DISARMED tree -- "disarmed they
// would out-resolve the template for every class deriving from a free-rider and
// answer NULL where the correct answer is &CBaseEntity::m_DataMap" -- and
// `public/ss_cbaseentity_alias.h` is deleted, so there is no disarmed tree to
// protect. The macro is never defined anywhere now, which turns the guard from
// load-bearing into LETHAL: an overload left inside it is compiled out, the
// template becomes the exact match again, and its body names an m_DataMap the
// free-rider does not have -- the eight hard diagnostics this whole block
// exists to prevent, back.
//
// So the assertion is TURNED ROUND rather than deleted, and it is still an
// assertion about POSITION rather than about presence: present, and NOT inside
// any SS_CBASEENTITY_ALIAS block. Both halves are needed. Presence alone was
// never enough (the guarded form was present too) and absence-of-guard alone
// would pass over a file with no overload in it at all.
//-----------------------------------------------------------------------------
void ExpectUnconditionalOverload( const char *pszFile, const char *pszAnchor,
                                  const char *pszNeedle )
{
	const std::string code = StripComments( ReadGameFile( pszFile ) );

	// THE ANCHOR, unchanged: the derived class that chains a datadesc through
	// the free-rider, which is the construct that MAKES the overload necessary.
	// Without it every assertion below passes on the empty string when the file
	// moves.
	ASSERT_NE( std::string::npos, code.find( pszAnchor ) )
		<< "game/" << pszFile << " not found, or no longer contains `"
		<< pszAnchor << "`; the assertion below would be vacuous.";

	EXPECT_NE( std::string::npos, code.find( pszNeedle ) )
		<< "game/" << pszFile << " no longer declares `" << pszNeedle
		<< "`. Without it the template is the exact match for the free-rider and "
		   "its body names an m_DataMap the class does not have -- a hard compile "
		   "error, not a silent one.";

	// AND IT IS COMPILED. Any SS_CBASEENTITY_ALIAS directive in this file's CODE
	// is a block that can never be entered, because nothing defines the macro
	// since public/ss_cbaseentity_alias.h was deleted. Checked over the whole
	// file rather than around the needle: a block opened anywhere above it and
	// closed below would contain it just as effectively.
	static const char *const kDeadGuards[] = {
		"#ifdef SS_CBASEENTITY_ALIAS",
		"#ifndef SS_CBASEENTITY_ALIAS",
		"defined( SS_CBASEENTITY_ALIAS )",
		"defined(SS_CBASEENTITY_ALIAS)",
	};
	for ( size_t i = 0; i < sizeof( kDeadGuards ) / sizeof( kDeadGuards[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, code.find( kDeadGuards[ i ] ) )
			<< "game/" << pszFile << " carries `" << kDeadGuards[ i ] << "` in "
			<< "code. Nothing defines that macro any more, so whatever it guards "
			<< "is never compiled -- and if that is the overload above, the "
			<< "diagnostics it prevents come straight back.";
	}
}

TEST_F( BaseTableChainEnd, ServerFreeRiderOverloadsAreUnconditional )
{
	// All four, including the two nothing chains to at this tip: they are the
	// same free-riders and the next class to derive from one must not be a
	// fresh compile error for a solved reason.
	const char *kFile   = "../sourcesharp/game/server/entity_bases.h";
	const char *kAnchor = "DECLARE_CLASS( CLogicalEntity, CServerOnlyEntity )";

	ExpectUnconditionalOverload( kFile, kAnchor, "DataMapAccess( CPointEntity *" );
	ExpectUnconditionalOverload( kFile, kAnchor, "DataMapAccess( CServerOnlyEntity *" );
	ExpectUnconditionalOverload( kFile, kAnchor, "DataMapAccess( CServerOnlyPointEntity *" );
	ExpectUnconditionalOverload( kFile, kAnchor, "DataMapAccess( CLogicalEntity *" );
}

TEST_F( BaseTableChainEnd, BaseAnimatingFreeRiderOverloadIsUnconditional )
{
	ExpectUnconditionalOverload( "client/c_baseanimating.h",
	                             "DECLARE_CLASS( C_ClientRagdoll, C_BaseAnimating )",
	                             "DataMapAccess( C_BaseAnimating *" );
}

} // namespace
