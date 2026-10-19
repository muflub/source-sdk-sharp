//========= SourceSharp — baseentity.cpp's nine static data members ==========//
//
// A SOURCE SCANNER, like baseentity_shared_lift_test, physicslink_lift_test and
// soundemitter_lift_test, and for the same reason: the claim is about the TEXT
// of files that need a whole engine to run, so the text is what is read.
// buildcpptests links each test binary against gtest and nothing else.
//
// WHAT WAS DONE. plan_baseentity.md Phase 13 note C counted SIXTEEN static DATA
// members of CBaseEntity / C_BaseEntity across the tree and found that FOURTEEN
// of them lived in the two files this lane owns:
//
//   baseentity.o    6   m_pClassSendTable, s_bAbsQueriesValid,
//                       m_pPredictionPlayer, m_nPredictionRandomSeed,
//                       m_nPredictionRandomSeedServer, m_DataMap
//   c_baseentity.o  8   those, minus m_nPredictionRandomSeedServer, plus
//                       m_pClassRecvTable, s_bAbsRecomputationEnabled,
//                       s_bInterpolate, m_PredMap
//
// NINE of the fourteen were written by hand in the two .cpp files and this lane
// removed all nine. The other five are emitted by the network-table and datamap
// MACROS -- IMPLEMENT_SERVERCLASS_ST_NOBASE, IMPLEMENT_CLIENTCLASS,
// BEGIN_DATADESC_NO_BASE, BEGIN_PREDICTION_DATA_NO_BASE -- so no line of either
// file spells them and no source scanner can see them. They are a separate
// problem with a separate shape and they are named in the plan.
//
//   THREE WERE DELETED, not moved. CBaseEntity::m_nPredictionRandomSeed,
//   CBaseEntity::m_nPredictionRandomSeedServer and
//   C_BaseEntity::m_nPredictionRandomSeed had no odr-user anywhere: the lane
//   that landed entity_prediction_seed.h rerouted every reader and writer, but
//   the definitions were in THESE files, which were not that lane's, so they
//   stayed behind as dead objects. Proven with nm --undefined-only over all 199
//   server and 328 client objects on the debug link lines, naming no referrer,
//   plus a tree-wide grep finding only the definitions and the declarations.
//
//   SIX WERE MOVED, because they are live.
//     m_pPredictionPlayer          -> EntityPrediction_PlayerStorage()
//                                     in shared/entity_prediction_seed.h
//     s_bAbsQueriesValid           -> EntityAbs_QueriesValidRef()
//     s_bAbsRecomputationEnabled   -> EntityAbs_RecomputationEnabledRef()
//     s_bInterpolate               -> EntityInterp_EnabledRef()
//                                     in shared/entity_abs_query_state.h (new)
//   -- two of those names are one per side, which is how six members come from
//   four storages.
//
// WHY A DATA MEMBER IS THE WORST OF THE THREE FAILURES -- CORRECTED, and the
// correction matters because work was planned on the old claim.
//
//   THIS FILE USED TO SAY that a static DATA member of an alias "stops the
//   translation unit PARSING, so every other diagnostic in that file
//   disappears behind it", and that arming step 4 with these in place "would
//   have produced two files that stop parsing". THAT IS FALSE. The diagnostic
//   is fully recoverable and gcc keeps going.
//
//   MEASURED, three times independently rather than argued. Armed, at the tip
//   that carried these, game/server/baseentity.cpp:296 is
//     error: 'SendTable* IGameEntity::m_pClassSendTable' is not a static data
//            member of 'class IGameEntity'
//   and 283 FURTHER DIAGNOSTICS FOLLOW IT IN THE SAME TRANSLATION UNIT -- 573
//   in that file altogether. On the client, 915 followed the first. Reduced to
//   twelve lines and re-run standalone, the same construct still reports every
//   later error in the file:
//
//     struct SendTable {};
//     struct IGameEntity { SendTable *m_pClassSendTable; };
//     using CBaseEntity = IGameEntity;
//     SendTable *CBaseEntity::m_pClassSendTable = 0;   // the error
//     int later = undeclared;                          // still diagnosed
//
//     $ g++ -fsyntax-only -fmax-errors=0 probe.cpp
//     ... 'SendTable* IGameEntity::m_pClassSendTable' is not a static data member
//     ... 'undeclared' was not declared in this scope
//
//   WHAT IS ACTUALLY TRUE, and it is still the worst of the three. An
//   out-of-line FUNCTION definition written `CBaseEntity::Foo()` under
//   `using CBaseEntity = IGameEntity;` is one error on one line. A static DATA
//   member is not one error: the definition is rejected, the member stays
//   undeclared at that scope, and every later mention of it is rejected in
//   turn. The cost is VOLUME, not a lost TU -- hundreds of diagnostics whose
//   single cause is one line, with the real errors of the file scattered among
//   them. Step 4 arming with these in place would still have given a list of
//   broken TUs to work through; it would have given one buried in an order of
//   magnitude more output than it needed to be.
//
//   So the lift these nine justified was worth doing, for error volume. Nobody
//   should plan around the TU being lost, because it is not.
//
// THE DECLARATIONS DELIBERATELY STAY in baseentity.h and c_baseentity.h. A
// static data member that is declared and never defined links clean as long as
// nobody odr-uses it, and the declarations go away with the class body at step
// 4. That is entity_prediction_seed.h's landed shape, followed here.
//
// WHY THIS NEEDS A GUARD, and none of these produces a diagnostic:
//
//   1. Somebody puts logic in a forwarder. Then there are two implementations
//      of a gate: the SDK's call sites take the class-scope one, the port's own
//      files take the free one, and they drift silently in both directions.
//   2. Somebody moves the @MULTICORE worker-thread rule out of the client's
//      class-scope functions and into the shared storage. That would hand the
//      SERVER a behaviour it never had -- IsAbsQueriesValid answering true off
//      the main thread -- and no test in the tree would notice.
//   3. Somebody drops one of the includes, and the accessor resolves through
//      some other transitive path today and stops resolving tomorrow.
//
// game/server/baseentity.cpp, game/client/c_baseentity.cpp and
// c_baseentity_statics.cpp are no longer compiled by client.so or server.so and
// have left the source tree. The checks that read them -- the nine static data
// members gone from both .cpp files, the class-scope definitions gone from
// them, the blocked-static inventory, the client's own two of the deleted
// names -- and the static-data-member and member-function detectors that
// existed only for them were culled with them. What is left pins the headers
// and the shim/statics files the build does compile.
//
// Each file is pinned by an ANCHOR string rather than a line count: a lane
// elsewhere had `lines.size() > 100` sitting four lines from a false alarm.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
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

// game/server/baseentity.h is compiled by nothing since plan_server_novalve.md
// P5 and left the farm in P6. The ServerHeader() accessor that read it went with
// it; what is left is whether it can be read at all, which
// TheServerEntityHeaderIsOutOfTheFarm asks with a sibling as its anchor.
bool GameFileReadable( const char *pszRelative )
{
	return !ReadGameFile( pszRelative ).empty();
}

const std::string &ClientHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity.h" );
	return s;
}

// THE CLIENT FLIP's HOME for the ones that outlive c_baseentity_statics.cpp
// too -- CLIENT_FLIP lane 2 (`c8-entitystatics`). That file is itself in the
// flip's removal set, so the free functions the port still needs moved once
// more, into the shim; the ones it does not need RETIRED and are defined
// nowhere. Which is which is ruled in shell_entitystatics.h and printed by
// `cs_cl_entitystatics`.
const std::string &ClientEntityStaticsShim()
{
	static const std::string s =
		ReadGameFile( "../sourcesharp/game/client/shell_entitystatics.cpp" );
	return s;
}

const std::string &AbsStateHeader()
{
	static const std::string s = ReadGameFile( "shared/entity_abs_query_state.h" );
	return s;
}

const std::string &PredictionSeedHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/shared/entity_prediction_seed.h" );
	return s;
}

const std::string &ClientStaticsHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity_statics.h" );
	return s;
}

const std::string &SharedBaseEntityHeader()
{
	static const std::string s = ReadGameFile( "shared/baseentity_shared.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a name written in PROSE -- and every
// file under test is full of exactly that, being about exactly this -- is not
// read as code. This file's own header block would fail nearly every assertion
// below without it.
//
// Not a C++ lexer: it does not track string or character literals, and does not
// need to, because what is built on it is a DEFINITION detector and a
// definition cannot occur inside a string literal.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	for ( size_t i = 0; i < s.size(); )
	{
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '/' )
		{
			while ( i < s.size() && s[ i ] != '\n' )
				i++;
			continue;
		}
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[ i ] == '*' && s[ i + 1 ] == '/' ) )
				i++;
			i = ( i + 1 < s.size() ) ? i + 2 : s.size();
			continue;
		}
		out += s[ i ];
		i++;
	}
	return out;
}

std::string Squeezed( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\r' && c != '\n' )
			out += c;
	}
	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) ||
	       ( c >= '0' && c <= '9' ) || c == '_';
}

//-----------------------------------------------------------------------------
// The body of one function DEFINED as `<Scope>::<Name>(...)`, squeezed of
// whitespace. Empty if it is not defined in this text. Used to prove a
// forwarder still forwards and has not grown a second implementation.
//-----------------------------------------------------------------------------
std::string FunctionBody( const std::string &strSource,
                          const std::string &strQualifiedName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strQualifiedName ); pos != std::string::npos;
	      pos = strCode.find( strQualifiedName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + strQualifiedName.size();
		if ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			continue;   // a longer name that starts with this one

		const size_t open = strCode.find( '{', i );
		const size_t stop = strCode.find( ';', i );
		if ( open == std::string::npos )
			continue;
		if ( stop != std::string::npos && stop < open )
			continue;   // a declaration, not a definition

		int depth = 0;
		for ( size_t k = open; k < strCode.size(); k++ )
		{
			if ( strCode[ k ] == '{' )
				depth++;
			else if ( strCode[ k ] == '}' )
			{
				depth--;
				if ( depth == 0 )
					return Squeezed( strCode.substr( open + 1, k - open - 1 ) );
			}
		}
	}
	return std::string();
}

//-----------------------------------------------------------------------------
// IS THERE A DEFINITION OF THIS FREE FUNCTION AT ALL -- as opposed to
// FunctionBody, which answers WHAT IS IN IT.
//
// WHY BOTH EXIST, AND IT IS A HOLE THIS LANE FELL INTO. FunctionBody returns
// the body's CONTENTS, so `FunctionBody( src, f ).empty()` is true for "f is
// not defined here" AND for "f is defined here with an empty body". Every
// "must not be defined" assertion written on top of it therefore passes on an
// EMPTY STUB -- which is exactly the silent no-op plan_entity.md §7 forbids,
// and exactly what such an assertion is supposed to catch. Measured, not
// feared: a retired function given a `{ // comment }` body was invisible to
// FunctionBody and a `{ int x = 1; }` body was not.
//
// So a presence/absence question asks THIS, and a "what does it do" question
// asks FunctionBody.
//
// A DEFINITION IS `name(...)` FOLLOWED BY `{`. A declaration and a call are
// both followed by `;`, which is what separates the three after Squeezed has
// removed the whitespace that would otherwise have to be pattern-matched.
bool DefinesFreeFunction( const std::string &strSource, const std::string &strName )
{
	// NOT Squeezed, and that is the whole of getting this right. Squeezing glues
	// the return type to the name -- `void EntityAbs_SetQueriesValid(` becomes
	// `voidEntityAbs_SetQueriesValid(` -- so the "is the previous character part
	// of a longer identifier" guard, which is what stops this matching a name
	// that merely ENDS with the one asked about, rejects every definition there
	// is. Written that way first; every EXPECT went red at once, which is the
	// only reason it is not still written that way.
	const std::string strCode = StripComments( strSource );
	const std::string strNeedle = strName + "(";

	for ( size_t pos = strCode.find( strNeedle ); pos != std::string::npos;
	      pos = strCode.find( strNeedle, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;   // a longer identifier that ends with this name

		// To the matching close paren, so a parenthesised type in the argument
		// list does not end the search early.
		size_t i = pos + strName.size();
		int depth = 0;
		for ( ; i < strCode.size(); i++ )
		{
			if ( strCode[ i ] == '(' )
			{
				depth++;
			}
			else if ( strCode[ i ] == ')' )
			{
				depth--;
				if ( depth == 0 )
					break;
			}
		}

		// Then the first thing that is not whitespace. `{` is a definition;
		// `;` is a declaration or a call, and both are somebody ELSE's question.
		for ( i++; i < strCode.size(); i++ )
		{
			const char c = strCode[ i ];
			if ( c == ' ' || c == '\t' || c == '\r' || c == '\n' )
				continue;
			if ( c == '{' )
				return true;
			break;
		}
	}

	return false;
}

bool Contains( const std::string &strHaystack, const char *pszNeedle )
{
	return strHaystack.find( pszNeedle ) != std::string::npos;
}

} // namespace

//=============================================================================
// THE FILES ARE THERE AT ALL. Every assertion below is over text read off
// disk, and a missing file reads as an empty string -- which passes every
// empty-list assertion in this file. This is the check that stops the rest of
// them being checks that cannot fail.
//=============================================================================

TEST( BaseEntityStatics, TheFilesUnderTestWereActuallyRead )
{
	struct { const char *pszWhat; const std::string *pStr; const char *pszAnchor; } kFiles[] =
	{
		// Anchors, not line counts. One lane's `lines.size() > 100` sat four
		// lines from a false alarm against a 102-line file.
		// server/baseentity.cpp and client/c_baseentity.cpp were here. Neither
		// client.so nor server.so compiles them any more, so they left the
		// source tree, and the checks that read them were culled.
		// THE TWO ENTITY HEADERS' ANCHORS WERE ACCESSORS OF THE CLASS, and the
		// class body is gone: `class CBaseEntity` and `class C_BaseEntity` were
		// the `#ifndef SS_CBASEENTITY_ALIAS` arm of their own headers, the alias
		// retired, and the arm went with the other 452. An anchor has to be a
		// line the file is expected to KEEP, so both are now the entity-handle
		// typedef and the network table -- the tail that SURVIVES the flip, which
		// is the part of each header this build actually compiles.
		// server/baseentity.h's row is gone: it is compiled by nothing since
		// plan_server_novalve.md P5 and left the farm in P6. Its absence is
		// asserted, anchored, in TheServerEntityHeaderIsOutOfTheFarm below.
		{ "client/c_baseentity.h",            &ClientHeader(),
		  "EXTERN_RECV_TABLE(DT_BaseEntity);" },
		{ "shared/entity_abs_query_state.h",  &AbsStateHeader(),
		  "ENTITY_ABS_QUERY_STATE_H" },
		{ "shared/entity_prediction_seed.h",  &PredictionSeedHeader(),
		  "ENTITY_PREDICTION_SEED_H" },
		// RENAMED, NOT REMOVED: this pair of accessors is defined out of line in
		// baseentity_shared.h and survived, spelled IGameEntity.
		{ "shared/baseentity_shared.h",       &SharedBaseEntityHeader(),
		  "inline void IGameEntity::SetPredictionPlayer( CBasePlayer *player )" },
	};

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		ASSERT_FALSE( kFiles[ i ].pStr->empty() )
			<< kFiles[ i ].pszWhat << " could not be read. Every assertion in "
			<< "this file is over text, and an unreadable file passes all of "
			<< "them silently.";

		EXPECT_TRUE( Contains( *kFiles[ i ].pStr, kFiles[ i ].pszAnchor ) )
			<< kFiles[ i ].pszWhat << " no longer contains its anchor \""
			<< kFiles[ i ].pszAnchor << "\". Either the wrong file was read, or "
			<< "the thing this test is about was renamed or gutted -- in which "
			<< "case the rest of this file is asserting about something else.";
	}
}

//=============================================================================
// THE SERVER ENTITY HEADER IS OUT OF THE FARM.
//
// Several facts below asserted ABSENCES in game/server/baseentity.h: no
// class-scope accessor bodies, no s_bAbsQueriesValid member, no call into the
// client's lifted EntityAbs_* pair, no gEntList. The header is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6, which is the
// stronger form of every one of them -- nothing can come back into a header no
// file can include. So those absences are retired at their sites and asserted
// here once, as the file's absence, anchored on a sibling the farm still links
// so a reader that sees nothing cannot satisfy it.
//=============================================================================

TEST( BaseEntityStatics, TheServerEntityHeaderIsOutOfTheFarm )
{
	ASSERT_TRUE( GameFileReadable( "shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h could not be read, so the absence below "
		<< "would be answered by a reader that sees nothing.";

	EXPECT_FALSE( GameFileReadable( "server/baseentity.h" ) )
		<< "game/server/baseentity.h is readable from the farm again. Every "
		<< "absence this file used to assert over it -- the class-scope "
		<< "accessors, s_bAbsQueriesValid, the client's EntityAbs_* pair, "
		<< "gEntList -- is unguarded until those facts are put back.";
}

//=============================================================================
// THE NINE ARE GONE FROM THE TWO .cpp FILES.
//=============================================================================

//=============================================================================
// THE STORAGE EXISTS, AND IS WHERE THE COMMITS SAY IT IS.
//=============================================================================

TEST( BaseEntityStatics, TheAbsStateHeaderCarriesTheThreeStorages )
{
	const std::string strCode = StripComments( AbsStateHeader() );

	EXPECT_TRUE( Contains( strCode, "inline bool s_bAbsQueriesValid = true;" ) )
		<< "entity_abs_query_state.h no longer holds the abs-queries flag. It "
		<< "is what 32 server and 14 client objects reach through the class-"
		<< "scope accessors.";
	EXPECT_TRUE( Contains( strCode, "inline bool s_bAbsRecomputationEnabled = true;" ) )
		<< "entity_abs_query_state.h no longer holds the abs-recomputation flag.";
	EXPECT_TRUE( Contains( strCode, "inline bool s_bInterpolate = true;" ) )
		<< "entity_abs_query_state.h no longer holds the interpolation flag.";

	// All three were `true` in the class. A storage that starts false would
	// silently disable interpolation on the first frame and fire the abs-query
	// asserts everywhere, and nothing else in the tree pins the initial value.
	EXPECT_FALSE( Contains( strCode, "= false;" ) )
		<< "a storage in entity_abs_query_state.h is initialised false. All "
		<< "three of these were `true` as static data members "
		<< "(baseentity.cpp:100, c_baseentity.cpp:92-94).";
}

TEST( BaseEntityStatics, ThePredictionPlayerStorageIsAVoidPointer )
{
	const std::string strCode = StripComments( PredictionSeedHeader() );

	EXPECT_TRUE( Contains( strCode, "inline void *&EntityPrediction_PlayerStorage()" ) )
		<< "EntityPrediction_PlayerStorage() is gone or changed type. It is "
		<< "deliberately void*: on the client `CBasePlayer` is a MACRO for "
		<< "`C_BasePlayer` (shared_classnames.h:20), so a forward declaration "
		<< "of it in this header would name one type in a TU that reached "
		<< "shared_classnames.h first and a different one in a TU that did "
		<< "not -- giving the accessor two mangled names, and therefore two "
		<< "prediction players. That is the exact "
		<< "bug the member exists to prevent, introduced by include order.";

	EXPECT_TRUE( Contains( strCode, "inline void *s_pPredictionPlayer = 0;" ) )
		<< "the prediction player storage is gone or no longer starts null.";
}

//=============================================================================
// THE FORWARDERS STILL FORWARD. A forwarder that grows a body is two
// implementations of one gate, and the build is green in both directions.
//=============================================================================

TEST( BaseEntityStatics, TheServerAbsQueryAccessorsWentWithTheClassAndTheStorageIsWhatIsLeft )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was
	// TheServerAbsQueryAccessorsAreForwarders, and it pinned
	// CBaseEntity::SetAbsQueriesValid / ::IsAbsQueriesValid in baseentity.h as
	// one-line forwarders onto EntityAbs_QueriesValidRef() -- because a
	// forwarder that grows a body is TWO implementations of one gate, and the
	// build is green in both directions while they drift.
	//
	// THE CLASS BODY IS GONE. `class CBaseEntity` was the `#ifndef
	// SS_CBASEENTITY_ALIAS` arm of baseentity.h; the alias retired and the arm
	// went with the other 452 the compiler never saw. Both accessors went with
	// it, exactly as the header said they would ("they go away with the class
	// body at step 4"), and nothing on the server calls either name any more.
	//
	// THE DRIFT HAZARD DID NOT GO WITH THEM -- it inverted. There is now ONE
	// implementation, and the way to get two back is to write a second one:
	// either a class-scope accessor returning, or a server-side call into the
	// CLIENT's lifted pair, which carries a worker-thread arm the server never
	// had. So absence is asserted WITH the storage that survived it.
	//
	// GONE: the accessors, and the static data member they were written for.
	// Those three absences read game/server/baseentity.h, which is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6; the
	// header's own absence is asserted in TheServerEntityHeaderIsOutOfTheFarm.

	// LIVE: the single storage, still an inline function-local static, still
	// true at rest.
	const std::string strAbs = StripComments( AbsStateHeader() );
	ASSERT_FALSE( strAbs.empty() );
	EXPECT_NE( std::string::npos, strAbs.find( "inline bool &EntityAbs_QueriesValidRef()" ) )
		<< "EntityAbs_QueriesValidRef() is gone. It is what the accessors "
		<< "forwarded to, and after the class body went it is the flag itself.";
	EXPECT_NE( std::string::npos, strAbs.find( "inline bool s_bAbsQueriesValid = true;" ) )
		<< "the abs-query flag no longer starts true, or no longer has storage.";

	// "baseentity.h still includes entity_abs_query_state.h" is retired:
	// game/server/baseentity.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6.
}

TEST( BaseEntityStatics, ThePredictionPlayerAccessorsAreForwarders )
{
	// RENAMED, NOT MOVED, and this pair is the reason the rename is a rename:
	// these two out-of-line definitions are in baseentity_shared.h, NOT in the
	// class body, so they survived the arm that took the class -- spelled
	// IGameEntity, which is the name `CBaseEntity` was an alias FOR.
	EXPECT_EQ( "returnstatic_cast<CBasePlayer*>(EntityPrediction_PlayerStorage());",
	           FunctionBody( SharedBaseEntityHeader(), "IGameEntity::GetPredictionPlayer" ) )
		<< "IGameEntity::GetPredictionPlayer in baseentity_shared.h is no "
		<< "longer the single typed read of EntityPrediction_PlayerStorage().";

	EXPECT_EQ( "EntityPrediction_PlayerStorage()=player;",
	           FunctionBody( SharedBaseEntityHeader(), "IGameEntity::SetPredictionPlayer" ) )
		<< "CBaseEntity::SetPredictionPlayer in baseentity_shared.h is no "
		<< "longer the single typed write of EntityPrediction_PlayerStorage(). "
		<< "These two are the ONLY place the void* gets its type back; both "
		<< "casts must stay in this one pair against one static type, or a "
		<< "base-subobject adjustment can creep in between them.";
}

TEST( BaseEntityStatics, TheClientInterpolationAccessorWentWithTheClassAndTheStorageIsWhatIsLeft )
{
	// READ THE ASSERTIONS, NOT THE NAME -- the same move as the server's abs
	// pair above. C_BaseEntity::IsInterpolationEnabled was a one-line forwarder
	// onto EntityInterp_EnabledRef() in c_baseentity.h; `class C_BaseEntity` was
	// that header's `#ifndef SS_CBASEENTITY_ALIAS` arm, the alias retired, and
	// the forwarder went with the class body.
	//
	// The flag did not go anywhere, and it is still written once a frame -- by
	// the shim, not by the class -- so the absence is asserted with the writer.
	EXPECT_EQ( "", FunctionBody( ClientHeader(), "C_BaseEntity::IsInterpolationEnabled" ) )
		<< "a class-scope IsInterpolationEnabled body is back in c_baseentity.h. "
		<< "EntityInterp_EnabledRef() is the flag; a second reader of it that "
		<< "can grow a body is two implementations of one gate.";

	const std::string strAbs = StripComments( AbsStateHeader() );
	ASSERT_FALSE( strAbs.empty() );
	EXPECT_NE( std::string::npos, strAbs.find( "inline bool &EntityInterp_EnabledRef()" ) )
		<< "EntityInterp_EnabledRef() is gone -- it is the storage the accessor "
		<< "read, and after the class body went it is the whole of this gate.";
	EXPECT_NE( std::string::npos, strAbs.find( "inline bool s_bInterpolate = true;" ) )
		<< "the interpolation flag no longer starts true, so the very first "
		<< "frame -- before anything has recomputed it -- behaves differently.";

	// LIVE: the one writer. The flag is recomputed from cl_interpolate at the
	// top of the client's frame, and that write is in the shim now.
	EXPECT_NE( std::string::npos,
	           StripComments( ClientEntityStaticsShim() ).find( "EntityInterp_EnabledRef() = cl_interpolate.GetBool();" ) )
		<< "nothing recomputes the interpolation flag from cl_interpolate any "
		<< "more, so it would sit at its rest value for the whole session.";
}

//=============================================================================
// THE WORKER-THREAD RULE STAYED WHERE IT WAS. This is the one behaviour
// difference between the two sides, and moving it into the shared storage
// would hand the server a rule it never had.
//=============================================================================

TEST( BaseEntityStatics, TheMulticoreRuleIsInTheClientFunctionsNotTheStorage )
{
	// The six accessors moved ONCE MORE, to
	// sourcesharp/game/client/shell_entitystatics.cpp, because the client flip
	// deletes c_baseentity_statics.cpp as well (CLIENT_FLIP lane 2). The rule is
	// looked for THERE now. What is being asserted has not changed: the guard
	// lives in the client's function bodies and not in the storage the two DLLs
	// share.
	const std::string strClient = StripComments( ClientEntityStaticsShim() );

	EXPECT_TRUE( Contains( strClient, "if ( !ThreadInMainThread() )" ) )
		<< "shell_entitystatics.cpp no longer guards the abs-state accessors "
		<< "with ThreadInMainThread(). That rule -- \"@MULTICORE: always allow "
		<< "in worker threads, assume higher level code is handling "
		<< "correctly\" -- is client-only and predates this lane.";

	const std::string strAbs = StripComments( AbsStateHeader() );
	EXPECT_FALSE( Contains( strAbs, "ThreadInMainThread" ) )
		<< "entity_abs_query_state.h has taken on the worker-thread rule. That "
		<< "header is compiled by BOTH server.so and client.so, so putting the "
		<< "rule there gives CBaseEntity::IsAbsQueriesValid() a worker-thread "
		<< "answer on the server, which it never had.";
}

//=============================================================================
// THE HEADERS ARE REACHED DIRECTLY, not by luck through some other include.
//=============================================================================

TEST( BaseEntityStatics, BothEntityHeadersIncludeTheAbsStateHeader )
{
	// READ THE ASSERTIONS, NOT THE NAME: only the client header is left. The
	// server half is retired -- game/server/baseentity.h is compiled by nothing
	// since plan_server_novalve.md P5 and left the farm in P6.
	EXPECT_TRUE( Contains( ClientHeader(), "#include \"entity_abs_query_state.h\"" ) )
		<< "game/client/c_baseentity.h does not include "
		<< "entity_abs_query_state.h, yet its inline EntityToWorldTransform, "
		<< "GetAbsVelocity and IsInterpolationEnabled call into it.";
}

TEST( BaseEntityStatics, TheNewHeaderForwardDeclaresNoEntityClass )
{
	// alias_mechanics_test's NoForwardDeclarationSurvivesTheAlias sweeps every
	// header in the tree for this, so the guard is enforced generally. Stated
	// here too because it is a property of THIS file that a reader of THIS
	// lane needs: entity_abs_query_state.h holds three bools and needs no
	// entity type at all, which is why it carries no #ifndef SS_CBASEENTITY_ALIAS
	// block -- there is nothing to guard, rather than a guard that was
	// forgotten.
	const std::string strCode = StripComments( AbsStateHeader() );

	EXPECT_FALSE( Contains( strCode, "class CBaseEntity;" ) )
		<< "entity_abs_query_state.h forward-declares CBaseEntity. If it now "
		<< "genuinely needs the type, the declaration must sit behind "
		<< "#ifndef SS_CBASEENTITY_ALIAS -- `using CBaseEntity = IGameEntity;` "
		<< "and `class CBaseEntity;` cannot coexist.";
	EXPECT_FALSE( Contains( strCode, "class C_BaseEntity;" ) )
		<< "entity_abs_query_state.h forward-declares C_BaseEntity; same rule.";
}

//=============================================================================
// THE DETECTOR ANSWERS TRUE WHEN THERE IS SOMETHING TO FIND.
//
// Without these, every empty-list assertion above passes for free. Twenty-seven
// checks-that-cannot-fail have been found in this tree, several inside
// instruments built to catch them.
//=============================================================================

//-----------------------------------------------------------------------------
// THE DEFINITION DETECTOR, ON FIXTURES -- and the FIRST fact is the one that
// made it necessary.
//
// FunctionBody answers with a body's CONTENTS, so an EMPTY body and no
// definition at all are the same answer: "". Every "must not be defined here"
// assertion built on it therefore passes on an empty stub, which is exactly the
// silent no-op plan_entity.md §7 forbids and exactly what those assertions
// exist to catch. Measured on this tree, not feared: a retired function given a
// `{ // comment }` body was invisible and a `{ int x = 1; }` body was not.
//-----------------------------------------------------------------------------
TEST( BaseEntityStaticsFixture, DefinitionDetectorSeesAnEmptyBodiedStub )
{
	const std::string strEmpty = "void EntityInterp_MarkAimEntsDirty()\n{\n}\n";

	EXPECT_TRUE( DefinesFreeFunction( strEmpty, "EntityInterp_MarkAimEntsDirty" ) )
		<< "an empty-bodied stub is a DEFINITION. If this cannot see one, every "
		<< "'this function is retired and defined nowhere' assertion in this "
		<< "file passes on a stub that reintroduces it as a silent no-op.";

	EXPECT_TRUE( FunctionBody( strEmpty, "EntityInterp_MarkAimEntsDirty" ).empty() )
		<< "and this is WHY the detector exists: FunctionBody cannot tell that "
		<< "stub from an absent function. If this ever stops being true the "
		<< "detector is redundant -- which would be good news, and should be "
		<< "acted on rather than left as two overlapping helpers.";

	const std::string strComment =
		"void EntityInterp_MarkAimEntsDirty()\n{\n\t// nothing\n}\n";
	EXPECT_TRUE( DefinesFreeFunction( strComment, "EntityInterp_MarkAimEntsDirty" ) );
}

TEST( BaseEntityStaticsFixture, DefinitionDetectorIgnoresDeclarationsAndCalls )
{
	EXPECT_FALSE( DefinesFreeFunction( "void EntityTool_RecordEntities();\n",
	                                   "EntityTool_RecordEntities" ) )
		<< "a declaration is not a definition; c_baseentity_statics.h declares "
		<< "several of these and outlives none of them.";

	EXPECT_FALSE( DefinesFreeFunction( "\tEntityTool_RecordEntities();\n",
	                                   "EntityTool_RecordEntities" ) )
		<< "a CALL is not a definition. shell_clientdll.cpp is full of these and "
		<< "the absence assertions read that file's callees, not its callers.";

	EXPECT_FALSE( DefinesFreeFunction( "// void EntityTool_RecordEntities()\n{\n}\n",
	                                   "EntityTool_RecordEntities" ) )
		<< "prose counts as code to a raw grep, which is a standing hazard of "
		<< "this port; the retirement notes name the very functions they retired.";
}

TEST( BaseEntityStaticsFixture, DefinitionDetectorDoesNotMatchALongerName )
{
	EXPECT_FALSE( DefinesFreeFunction( "void MyEntityTool_RecordEntities()\n{\n}\n",
	                                   "EntityTool_RecordEntities" ) )
		<< "a name that merely ENDS with the one asked about is a different "
		<< "function.";

	EXPECT_FALSE( DefinesFreeFunction( "void EntityTool_RecordEntitiesTwice()\n{\n}\n",
	                                   "EntityTool_RecordEntities" ) )
		<< "and so is one that merely BEGINS with it -- the needle carries the "
		<< "open paren, which is what rules this out.";

	EXPECT_TRUE( DefinesFreeFunction(
			"void EntityAbs_SetQueriesValid( bool bValid )\n{\n\tx();\n}\n",
			"EntityAbs_SetQueriesValid" ) )
		<< "and a real definition with arguments still reads as one.";
}

TEST( BaseEntityStaticsFixture, FunctionBodyExtractorWorksOnFixtures )
{
	const std::string strForwarder =
		"inline bool CBaseEntity::IsAbsQueriesValid()\n"
		"{\n"
		"    return EntityAbs_QueriesValidRef();\n"
		"}\n";
	EXPECT_EQ( "returnEntityAbs_QueriesValidRef();",
	           FunctionBody( strForwarder, "IsAbsQueriesValid" ) );

	const std::string strGrown =
		"inline bool CBaseEntity::IsAbsQueriesValid()\n"
		"{\n"
		"    if ( g_bSomething ) return true;\n"
		"    return EntityAbs_QueriesValidRef();\n"
		"}\n";
	EXPECT_NE( "returnEntityAbs_QueriesValidRef();",
	           FunctionBody( strGrown, "IsAbsQueriesValid" ) )
		<< "a forwarder that grew a second implementation compared EQUAL to a "
		<< "clean one; the four forwarder assertions above then cannot fail";

	const std::string strDeclOnly =
		"static inline bool IsAbsQueriesValid();\n";
	EXPECT_EQ( "", FunctionBody( strDeclOnly, "IsAbsQueriesValid" ) )
		<< "a declaration was read as a definition";
}

//=============================================================================
// THE SIX CLIENT ACCESSORS THAT WERE LIFTED WITH THE STORAGE.
//
// c_baseentity.cpp defined all six as `static` C_BaseEntity members. They take
// no `this` and read none, so the class was a namespace; under the alias a
// definition written `C_BaseEntity::Foo()` is a compile error. The bodies did
// NOT move -- four reach that file's own eight-deep save stack and all six
// reach ThreadInMainThread() -- only the scope did, which is
// EntitySim_IsSimulatingOnAlternateTicks's landed shape.
//=============================================================================

namespace
{

struct Lifted
{
	const char *pszMember;   // the old C_BaseEntity:: name, now an inline forwarder
	const char *pszFree;     // the free function it forwards to
};

const Lifted kLifted[] =
{
	{ "SetAbsQueriesValid",           "EntityAbs_SetQueriesValid" },
	{ "IsAbsQueriesValid",            "EntityAbs_AreQueriesValid" },
	{ "PushEnableAbsRecomputations",  "EntityAbs_PushEnableRecomputations" },
	{ "PopEnableAbsRecomputations",   "EntityAbs_PopEnableRecomputations" },
	{ "EnableAbsRecomputations",      "EntityAbs_SetRecomputationsEnabled" },
	{ "IsAbsRecomputationsEnabled",   "EntityAbs_AreRecomputationsEnabled" },
};

const size_t kNumLifted = sizeof( kLifted ) / sizeof( kLifted[ 0 ] );

} // namespace

// THE SIX MOVED OUT OF c_baseentity.cpp ENTIRELY, and then out of
// c_baseentity_statics.cpp too (CLIENT_FLIP lane 2): both files are gone from
// the build and the tree, and cdll_client_int.cpp / shell_clientdll.cpp still
// call these by their free names, so a missing body is an undefined symbol at
// link time with no compile symptom anywhere. The absence halves that read the
// two dead files were culled with them.
TEST( BaseEntityStatics, TheSixFreeFunctionsAreDefinedInTheShim )
{
	const std::string strNew     = StripComments( ClientEntityStaticsShim() );

	ASSERT_FALSE( strNew.empty() )
		<< "sourcesharp/game/client/shell_entitystatics.cpp did not read. Every EXPECT below "
		<< "would pass on an empty string, which is the shape of a check that "
		<< "cannot fail.";

	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		EXPECT_TRUE( DefinesFreeFunction( strNew, kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not DEFINED in "
			<< "shell_entitystatics.cpp. The client flip deleted BOTH "
			<< "c_baseentity.cpp and c_baseentity_statics.cpp while "
			<< "shell_clientdll.cpp -- which calls it -- compiles perfectly "
			<< "clean armed. Without the definition the client fails to LINK, "
			<< "and no compile survey in this port can see that.";
	}
}

TEST( BaseEntityStatics, TheSixClassScopeNamesWentWithTheClassBodyAndTheFreeSixRemain )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was
	// TheSixClassScopeNamesAreOneLineForwarders: each of the six lifted statics
	// kept its C_BaseEntity:: name as an INLINE forwarder in c_baseentity.h,
	// because 21 SDK call sites across nine files name it qualified -- "so it
	// has to keep existing until the class body goes at step 4".
	//
	// THE CLASS BODY HAS GONE. It was c_baseentity.h's `#ifndef
	// SS_CBASEENTITY_ALIAS` arm, the alias retired, and the arm went with the
	// other 452. The forwarders went with it, which is the outcome the fact
	// predicted rather than a regression of it -- and the 21 call sites went
	// with the files that held them.
	//
	// ABSENCE ALONE WOULD PASS OVER A DELETED HEADER, and worse, it would pass
	// if the six free functions had been deleted too -- which is the one thing
	// that must not happen, because shell_clientdll.cpp calls them and a missing
	// definition is a LINK failure no compile survey in this port can see. So
	// each row is asserted three ways: no class-scope body, a declaration in
	// entity_abs_query_state.h, and a definition in the shim.
	const std::string strAbs = StripComments( AbsStateHeader() );
	const std::string strShim = StripComments( ClientEntityStaticsShim() );

	ASSERT_FALSE( strAbs.empty() );
	ASSERT_FALSE( strShim.empty() )
		<< "shell_entitystatics.cpp did not read, so every presence assertion "
		<< "below would pass on an empty string.";

	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		EXPECT_EQ( "", FunctionBody( ClientHeader(), kLifted[ i ].pszMember ) )
			<< "C_BaseEntity::" << kLifted[ i ].pszMember << " has a body in "
			<< "c_baseentity.h again. The class it would be a member of does "
			<< "not exist, and a second implementation of this gate drifts from "
			<< kLifted[ i ].pszFree << " silently.";

		EXPECT_NE( std::string::npos, strAbs.find( kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not declared in "
			<< "entity_abs_query_state.h at all -- the class-scope name is gone, "
			<< "so this declaration is the only way to reach this gate.";

		EXPECT_TRUE( DefinesFreeFunction( ClientEntityStaticsShim(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not DEFINED in "
			<< "shell_entitystatics.cpp. With the class-scope forwarder gone "
			<< "this is the only definition, and shell_clientdll.cpp calls it -- "
			<< "so its absence is a link failure, not a compile error.";
	}
}

TEST( BaseEntityStatics, TheSixAreDeclaredBehindClientDll )
{
	const std::string strCode = StripComments( AbsStateHeader() );

	const size_t guard = strCode.find( "#ifdef CLIENT_DLL" );

	ASSERT_NE( std::string::npos, guard )
		<< "entity_abs_query_state.h no longer guards the six client "
		<< "declarations with CLIENT_DLL.";

	// The closing `#endif` is matched by POSITION, not by the trailing
	// `// CLIENT_DLL` label -- StripComments has already removed that, which is
	// what this assertion originally got wrong. The block contains no nested
	// preprocessor conditional, asserted next, so the first #endif after the
	// #ifdef is its partner.
	const size_t endGuard = strCode.find( "#endif", guard );
	ASSERT_NE( std::string::npos, endGuard )
		<< "the CLIENT_DLL block in entity_abs_query_state.h is never closed.";
	ASSERT_LT( guard, endGuard );

	const std::string strInside = strCode.substr( guard + 1, endGuard - guard - 1 );
	ASSERT_EQ( std::string::npos, strInside.find( "#if" ) )
		<< "a nested preprocessor conditional appeared inside the CLIENT_DLL "
		<< "block, so matching the first #endif no longer finds its partner "
		<< "and the range check below means nothing.";

	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		const size_t at = strCode.find( kLifted[ i ].pszFree );
		ASSERT_NE( std::string::npos, at )
			<< kLifted[ i ].pszFree << " is not declared in "
			<< "entity_abs_query_state.h at all.";
		EXPECT_TRUE( at > guard && at < endGuard )
			<< kLifted[ i ].pszFree << " is declared OUTSIDE the CLIENT_DLL "
			<< "guard. The server has its own SetAbsQueriesValid and "
			<< "IsAbsQueriesValid (baseentity.h) and they are NOT these -- they "
			<< "have no ThreadInMainThread() arm, because the server never had "
			<< "one. Visible to the server, a server file can call this and "
			<< "silently acquire a worker-thread behaviour it never had.";
	}
}

TEST( BaseEntityStatics, TheServerStillCannotReachTheClientsWorkerThreadArm )
{
	// READ THE ASSERTIONS, NOT THE NAME, AND NOTE WHY THIS ONE HAD TO CHANGE
	// SHAPE RATHER THAN BEING LEFT ALONE: it was still GREEN and it had stopped
	// measuring anything. It read the BODY of CBaseEntity::SetAbsQueriesValid
	// out of baseentity.h and asserted the body did not name the client's
	// lifted function -- and the class body is gone, so FunctionBody answers
	// the empty string, and `"".find( anything ) == npos` passes for free. That
	// is the exact shape of a check that cannot fail.
	//
	// The property it was for is unchanged and is still worth a fact: the two
	// names exist on both sides with DIFFERENT bodies -- the client's six
	// no-op or answer `true` off the main thread, the server never had that
	// arm -- so a SERVER file that reached the client's pair would silently
	// acquire a worker-thread behaviour. The CLIENT_DLL guard is what stops it.
	// It was asserted over the WHOLE server header -- game/server/baseentity.h
	// -- which is compiled by nothing since plan_server_novalve.md P5 and left
	// the farm in P6. That header-text half is retired (its absence is
	// TheServerEntityHeaderIsOutOfTheFarm); the guard is what is left.
	//
	// THE GUARD THAT MAKES A SERVER CALL A COMPILE ERROR RATHER THAN A
	// CONVENTION.
	const std::string strAbs = StripComments( AbsStateHeader() );
	const size_t guard = strAbs.find( "#ifdef CLIENT_DLL" );
	ASSERT_NE( std::string::npos, guard )
		<< "entity_abs_query_state.h no longer guards the client's six with "
		<< "CLIENT_DLL, so a server file naming one is no longer a compile "
		<< "error -- it is a silent behaviour change.";
	const size_t at = strAbs.find( "EntityAbs_AreQueriesValid" );
	ASSERT_NE( std::string::npos, at );
	EXPECT_GT( at, guard );
}

//=============================================================================
// THE FRAME-LOOP STATICS: NINE LIFTED, ONE BLOCKED.
//
// c_baseentity.cpp declares EIGHTEEN `static` members. Six of them are the abs
// cluster above. Of the rest, three lifted cleanly, SIX were blocked because a
// free function is not a member and cannot touch a private or protected one,
// and those six are lifted now.
//
// THE BLOCKED LIST WORKED. It said in so many words that it was an inventory
// and not a ban -- "when one is unblocked the test goes red for the right
// reason and says so" -- and that is exactly what happened: the lane that added
// the friend block ran this file and got six failures naming six members.
//
// FOUR OF THE SIX WERE UNBLOCKED BY A FRIEND DECLARATION at the bottom of
// C_BaseEntity, which is the decision this list declined to make. TWO WERE
// NEVER BLOCKED AT ALL, and the reasons are worth keeping:
//
//   InterpolateServerEntities  was blocked on the two PROTECTED statics it
//                              calls. Lifting those dissolved it rather than
//                              working around it.
//   AddVisibleEntities         was listed as blocked on "reads
//                              ent->m_PredictableID". That field is PUBLIC
//                              (c_baseentity.h:1407, under the `public:` at
//                              1369) and always was. The entry was written
//                              from a read of the body rather than from a
//                              probe -- in a list whose own comment says the
//                              compiler is the only reliable oracle.
//
// ONE REMAINED BLOCKED (CreatePredictedEntityByName, on CPredictableList's
// protected members). Its inventory read c_baseentity.cpp, which is no longer
// compiled and has left the tree, so it was culled with that file.
//
// The per-lift detail -- the forwarder bodies, the four friend declarations and
// the allocation operators that went the same way -- is in
// c_baseentity_lift_test.cpp. What stays here is the inventory.
//=============================================================================

namespace
{

// WHERE EACH OF THE NINE ENDED UP -- CLIENT_FLIP lane 2 (`c8-entitystatics`).
//
// Step 6 evacuated all nine to c_baseentity_statics.cpp so the link would hold
// when c_baseentity.cpp went. The client flip deletes THAT file too, and at
// that point the nine stop being one group: one of them is the port's own
// mechanism and eight walked SDK registries that only C_BaseEntity ever added
// to -- g_AimEntsList, g_TeleportList, g_InterpolationList, the predictables
// list -- so after the flip every one of those loops runs over nothing.
//
// bShim distinguishes the two, and BOTH are asserted, in opposite directions.
// A row that is RETIRED must be defined in NO file: a retired function left
// defined somewhere is a capability everybody believes is gone. A row that is
// SHIM must be defined in the shim and in neither dying file.
struct FrameLoopLifted
{
	const char *pszMember;
	const char *pszFree;
	bool        bShim;      // true: moved to the shim. false: RETIRED.
};

const FrameLoopLifted kFrameLoopLifted[] =
{
	{ "MarkAimEntsDirty",     "EntityInterp_MarkAimEntsDirty",     false },

	// THE ONE SURVIVOR UNTIL 2026-10-19, kept so the SFM record path refused
	// by name. RETIRED now with the tool framework: SFM is dead (user ruling
	// 2026-10-19), so it is defined in no file.
	{ "ToolRecordEntities",   "EntityTool_RecordEntities",         false },

	{ "CheckCLInterpChanged", "EntityInterp_CheckCLInterpChanged", false },

	// the six that were blocked
	{ "CalcAimEntPositions",       "EntityInterp_CalcAimEntPositions",       false },
	{ "InterpolateServerEntities", "EntityInterp_InterpolateServerEntities", false },
	{ "ProcessTeleportList",       "EntityInterp_ProcessTeleportList",       false },
	{ "ProcessInterpolatedList",   "EntityInterp_ProcessInterpolatedList",   false },
	{ "AddVisibleEntities",        "EntityRender_AddVisibleEntities",        false },
	{ "CheckInterpolatedVarParanoidMeasurement",
	                               "EntityInterp_CheckParanoidMeasurement",  false },
};

const size_t kNumFrameLoopLifted =
	sizeof( kFrameLoopLifted ) / sizeof( kFrameLoopLifted[ 0 ] );

} // namespace

TEST( BaseEntityStatics, TheThreeFrameLoopStaticsLeftTheClassScope )
{
	// c_baseentity.cpp and c_baseentity_statics.cpp, the two files this used
	// to read for "no definition left behind", are no longer compiled and have
	// left the tree; those halves were culled with them.
	for ( size_t i = 0; i < kNumFrameLoopLifted; i++ )
	{
		// ALL NINE MOVED OUT of c_baseentity.cpp with the step-6 link-only
		// evacuation, and this assertion moved with them. It used to say the
		// body "deliberately stays in that translation unit -- it reaches
		// g_AimEntsList, the tool framework recorder or the cl_interp ConVar
		// objects". Step 6 DELETES that translation unit, and
		// cdll_client_int.cpp calls six of these once per frame and survives,
		// so the state they reach was exported and the bodies went with it.
		if ( kFrameLoopLifted[ i ].bShim )
		{
			EXPECT_TRUE( DefinesFreeFunction( ClientEntityStaticsShim(),
			                                  kFrameLoopLifted[ i ].pszFree ) )
				<< kFrameLoopLifted[ i ].pszFree << " is not defined in "
				<< "shell_entitystatics.cpp. It is kept so that the capability "
				<< "it served refuses BY NAME instead of going silent, and "
				<< "shell_clientdll.cpp still calls it once a frame -- so "
				<< "without the definition the client fails to LINK.";
		}
		else
		{
			EXPECT_FALSE( DefinesFreeFunction( ClientEntityStaticsShim(),
			                                   kFrameLoopLifted[ i ].pszFree ) )
				<< kFrameLoopLifted[ i ].pszFree << " is RETIRED and must be "
				<< "defined nowhere. A retired function that still has a body "
				<< "somewhere is a capability everybody believes is gone, which "
				<< "is worse than either keeping it or removing it outright.";
		}

		// THE INLINE FORWARDER IS GONE WITH THE CLASS BODY, and this half used
		// to assert it was there: "cdll_client_int.cpp calls it qualified once
		// per frame". `class C_BaseEntity` was c_baseentity.h's
		// `#ifndef SS_CBASEENTITY_ALIAS` arm; the alias retired and the arm went
		// with the other 452, taking every forwarder in the class body with it.
		// The qualified call sites went with the files that held them.
		//
		// Absence alone would pass over a deleted header AND over a deleted free
		// function, so it is asserted against the row's own destination: SHIM
		// rows keep their definition (shell_clientdll.cpp still calls them once a
		// frame), RETIRED rows have none anywhere -- both already asserted above.
		EXPECT_EQ( "", FunctionBody( ClientHeader(), kFrameLoopLifted[ i ].pszMember ) )
			<< "C_BaseEntity::" << kFrameLoopLifted[ i ].pszMember << " has a "
			<< "body in c_baseentity.h again. There is no class for it to be a "
			<< "member of, and for a RETIRED row it would be the capability "
			<< "coming back under the name everybody believes is gone.";
	}
}

TEST( BaseEntityStatics, TheNewClientHeaderIsReachedAndDeclaresTheThree )
{
	ASSERT_FALSE( ClientStaticsHeader().empty() )
		<< "game/client/c_baseentity_statics.h could not be read.";

	EXPECT_TRUE( Contains( ClientHeader(), "#include \"c_baseentity_statics.h\"" ) )
		<< "c_baseentity.h does not include c_baseentity_statics.h, yet its "
		<< "inline forwarders call into it.";

	const std::string strCode = StripComments( ClientStaticsHeader() );
	for ( size_t i = 0; i < kNumFrameLoopLifted; i++ )
	{
		EXPECT_TRUE( Contains( strCode, kFrameLoopLifted[ i ].pszFree ) )
			<< kFrameLoopLifted[ i ].pszFree << " is not declared in "
			<< "c_baseentity_statics.h.";
	}

	// No entity type appears in any signature there, which is why the header
	// carries no SS_CBASEENTITY_ALIAS guard. If one arrives, the guard has to
	// arrive with it: `using CBaseEntity = IGameEntity;` and
	// `class CBaseEntity;` cannot coexist.
	EXPECT_FALSE( Contains( strCode, "class C_BaseEntity;" ) )
		<< "c_baseentity_statics.h forward-declares C_BaseEntity without a "
		<< "#ifndef SS_CBASEENTITY_ALIAS guard.";
	EXPECT_FALSE( Contains( strCode, "class CBaseEntity;" ) )
		<< "c_baseentity_statics.h forward-declares CBaseEntity without a "
		<< "#ifndef SS_CBASEENTITY_ALIAS guard.";
}

//=============================================================================
// THE SERVER SLICE: THREE LIFTED, THREE MOVED INTO THE CLASS BODY, ONE BLOCKED.
//
// baseentity.cpp declares SEVEN `static` CBaseEntity members. Three were lifted
// to free functions in the new sourcesharp/game/server/baseentity_statics.h with their
// bodies unmoved. Three needed no lifting at all -- they were already one-line
// forwarders onto free functions that exist -- so only their DEFINITIONS moved,
// into the class body, which removes the symbol without inventing anything.
// One is blocked.
//=============================================================================

namespace
{

// The kServerLifted table went with baseentity_statics.{h,cpp}
// (plan_server_native_min P1): the files and the absence pins over them are
// gone together.

// THE STEP-6 EVACUATION, of the four above. A body stays in baseentity.cpp
// unless something OUTSIDE the removal set names it, in which case it has to
// leave -- baseentity.cpp is deleted at step 6 and the caller is not.
//
// Measured, not guessed: src/evict_consumers relinks server.so with
// baseentity.o removed and src/s6r_linkonly.py reads the undefined symbols that
// are not CBaseEntity:: members. EntityCreate_Spawned came back demanded from
// gameinterface.cpp:1108. EntityCreate_NoSpawn did NOT -- its only demand was
// baseentity.h's inline forwarder, which dies with the class -- and it is
// evacuated anyway, because EntityCreate_Spawned calls it.
//
// GameSound_EmitSentenceByIndex IS HERE, AND IT APPEARS IN NO PROBE ON A NORMAL
// BUILD. game/server/sound.cpp names it at four sites and survives step 6, so
// leaving the definition in the removal set is a link failure -- but with
// --gc-sections on, the linker drops sound.cpp's four SENTENCEG_* emitters
// before it ever needs the callee, so the reference never becomes undefined. It
// is absent from a release evict_consumers, absent from a debug one, and
// present under `make debug GC_SECTIONS=0`. Makefile:30-52 documents that
// failure mode for an evicted FILE; this is the same thing through a lifted
// free function.
//
// EntityPredict_CreateByName was in no probe in any of the three
// configurations, so it stays, and this table says so rather than leaving it to
// be inferred from an absence.
// (The evacuation table that stood here held GameSound_EmitSentenceByIndex
// alone; it went with the function in the server gc cull, 2026-10-01.)

// (kInBody stood here: PrecacheModel, PrecacheModelComponents and Instance,
// the three in-body one-liners whose absence was read out of
// game/server/baseentity.h. That header is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, and the table went with
// the facts that read it.)

} // namespace

// TheServerForwardersWentWithTheClassAndTheFreeFunctionsAreWhatIsLeft went
// with the files it read (plan_server_native_min P1): baseentity_statics.{h,cpp}
// are deleted -- the last lifted body left in the 2026-10-01 gc cull and the
// two tenants that remained (one unreferenced send proxy, one write-only
// global) had no reader between them. The client half of the fact is pinned
// by the client tests that follow.

// TheDefaultArgumentsAreNotRepeatedOnTheFreeFunctions read
// baseentity_statics.h and went with it (plan_server_native_min P1).

// TheServerHeaderNamesNoEntityTypeAndForwardDeclaresNone read
// baseentity_statics.h and went with it (plan_server_native_min P1).

// TheDeadFactoriesStayDeleted read baseentity_statics.{h,cpp} and went with
// them (plan_server_native_min P1).

//=============================================================================
// THE FIVE DELETIONS, PINNED AS ABSENCES.
//
// A deletion that gets typed back is exactly as invisible as one that never
// happened, so each is pinned here -- absent from baseentity.cpp AND from
// baseentity.h, because a declaration without a definition is the shape that
// invites somebody to supply one.
//
// Found by a four-stage funnel over baseentity.o, and the two middle stages are
// the ones that matter, because without them the answer is 120 and every one of
// those 120 is alive:
//
//   261  defined out of line
//   124  no object on the link line names it undefined
//   120  ... and not declared `virtual` in EITHER header
//         (a virtual call emits no symbol, so the linker never saw its callers)
//    16  ... and the DEFINING FILE names it exactly once -- the definition
//         (this is the stage that matters most: CBaseEntity's own datadesc,
//          send table and VScript registrations live in baseentity.cpp and take
//          member-function POINTERS -- DEFINE_INPUTFUNC( ..., InputAlpha ),
//          DEFINE_SCRIPTFUNC( GetScriptScope ). A pointer taken in the defining
//          TU produces no undefined symbol anywhere, so every input function
//          and every script function reads as dead without this filter)
//     6  ... and no other source file in the tree names it
//     5  ... minus GetBaseMap, which is MACRO-generated: BEGIN_DATADESC_NO_BASE
//         defines it and BEGIN_DATADESC_GUTS calls it, both by token pasting, so
//         the text of baseentity.cpp never spells it and no source scan can see
//         the call. Alive.
//
// The same funnel over c_baseentity.o answers ZERO: 266 defined, 62
// unreferenced, 13 surviving the own-file filter, and the single survivor is
// GetBaseMap again. The client has no dead member by this standard.
//=============================================================================

// TheFiveDeletedMembersAreStillGoneFromTheServer is RETIRED. It asserted
// IsInTeam, IsInAnyTeam, GetTransmitState, ComputeAbsDirection and
// SetLocalTransform stayed undeclared in game/server/baseentity.h, which is
// compiled by nothing since plan_server_novalve.md P5 and left the farm in P6.
// Nothing can be redeclared in a header no file includes; the header's own
// absence is TheServerEntityHeaderIsOutOfTheFarm.

//=============================================================================
// THE void* ROUND TRIP. The one piece of this lane that is LOGIC rather than
// text: storing a typed pointer through void* and reading it back must give
// the same pointer. Run rather than read, because a source scanner cannot say
// anything about it.
//=============================================================================

namespace
{

// Stand-ins for the real shapes. A player is not a standard-layout single-base
// type in the SDK, and the risk the void* raises is precisely that a cast on
// one side and a cast on the other pick up different base-subobject offsets.
struct FakeUnknownBase { virtual ~FakeUnknownBase() {} int a; };
struct FakeNetworkableBase { virtual ~FakeNetworkableBase() {} int b; };
struct FakePlayer : public FakeUnknownBase, public FakeNetworkableBase { int c; };

// The header's shape, reproduced exactly: a function-local static behind an
// inline accessor. Reproduced rather than included because buildcpptests links
// gtest and nothing else, and entity_prediction_seed.h is reachable only with
// the SDK's include tree.
inline void *&FakePlayerStorage()
{
	static void *s_p = 0;
	return s_p;
}

// The two forwarders from baseentity_shared.h, same static type at both casts.
inline FakePlayer *GetFakePlayer() { return static_cast< FakePlayer * >( FakePlayerStorage() ); }
inline void SetFakePlayer( FakePlayer *p ) { FakePlayerStorage() = p; }

} // namespace

TEST( BaseEntityStatics, VoidStorageRoundTripsThePointerExactly )
{
	FakePlayer player;
	player.c = 0x5A5A;

	SetFakePlayer( &player );
	ASSERT_EQ( &player, GetFakePlayer() )
		<< "the void* storage did not return the pointer that was put in it. "
		<< "The two casts in baseentity_shared.h are the only place the "
		<< "prediction player's type exists; if they stop agreeing, "
		<< "GetPredictionPlayer() hands out a pointer into the middle of an "
		<< "object and every caller reads garbage.";
	EXPECT_EQ( 0x5A5A, GetFakePlayer()->c );

	SetFakePlayer( 0 );
	EXPECT_EQ( ( FakePlayer * )0, GetFakePlayer() )
		<< "storing NULL did not read back as NULL. player_command.cpp and "
		<< "prediction.cpp both clear it that way at the end of a usercmd, and "
		<< "IsInPrediction() is `GetPredictionPlayer() != NULL`.";
}

TEST( BaseEntityStatics, VoidStorageStartsNull )
{
	// Not a tautology with the round trip: this is about the INITIAL value, and
	// it is what makes IsInPrediction() false before any usercmd has run. The
	// member it replaced was `= NULL`.
	//
	// A second storage, untouched by the test above, because that one has
	// already written to FakePlayerStorage().
	struct Fresh { static void *&Ref() { static void *s_p = 0; return s_p; } };
	EXPECT_EQ( ( void * )0, Fresh::Ref() )
		<< "a function-local static void* did not start null";
}
