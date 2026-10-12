//========= SourceSharp — baseentity.cpp's eight departed definitions ========//
//
// A SOURCE SCANNER, like c_baseentity_lift_test, baseentity_shared_lift_test,
// physicslink_lift_test, soundemitter_lift_test and baseentity_statics_test,
// and for the same reason: the claim is about the TEXT of two files that need
// a whole engine to run, so the text is what is read. buildcpptests links each
// test binary against gtest and nothing else.
//
// WHAT WAS DONE. plan_baseentity.md Phase 13 step 3 is the set of files that
// DEFINE CBaseEntity members out of line; under `using CBaseEntity =
// IGameEntity;` an alias is not a class scope, so each such definition is a
// compile error and can only be lifted, ported or deleted.
// game/server/baseentity.cpp is the largest of them: 257 such symbols in the
// release object at 0e8979b8 and 259 in debug, measured with
//
//     nm --defined-only <obj> | awk '$2=="T"{print $3}'
//         | c++filt | grep -cE '^C_?BaseEntity::'
//
// EIGHT definitions left, taking release to 250 and debug to 251. They are NOT
// eight of a kind, and this file guards two different shapes:
//
//   TWO GENUINE LIFTS, declared in baseentity_statics.h with their bodies
//   unmoved in baseentity.cpp, reached through inline forwarders:
//
//       ShouldToggle                -> EntityUse_ShouldToggle
//       CreatePredictedEntityByName -> EntityPredict_CreateByName
//
//   SIX WITH NOTHING TO LIFT -- empty bodies, or bodies that are empty in
//   every configuration this tree builds -- moved into the class body instead,
//   which takes the symbol off just the same and adds no free function:
//
//       DrawRBoxOverlay          SDK ships it empty
//       SetPredictionEligible    its one statement is commented out in the SDK
//       ClearModelIndexOverrides body is entirely #ifdef TF_DLL
//       FunctionCheck            body is entirely #ifdef USES_SAVERESTORE, and
//                                the whole thing #ifdef _DEBUG
//       MyNPCPointer             #ifdef SOURCESHARP arm inline in the class
//       ScriptGetModelKeyValues  body; real branch still out of line under
//                                #ifndef SOURCESHARP
//
// WHAT MOVED UNDER THIS FILE, AND WHY FIVE ASSERTIONS BELOW CHANGED SHAPE.
// game/server/baseentity.h NO LONGER DECLARES AN ENTITY CLASS. The whole
// `class CBaseEntity : public IServerEntity { ... }` span, and the member
// definitions that followed it, sat inside `#ifndef SS_CBASEENTITY_ALIAS` --
// an arm the compiler never saw, because the alias header armed it -- and the
// close deleted all 2,693 lines of it along with the rest of the tree's dead
// arms. `CBaseEntity` is not a name at all any more either: the alias header
// is retired and the CODE tokens that spelled it now read `IGameEntity`. What
// SURVIVES in baseentity.h is its live tail -- the CREATE_PREDICTED_ENTITY
// macro (which now names `IGameEntity::CreatePredictedEntityByName`), the free
// funnels, the extern send tables and the CHandle<IGameEntity> specialisation.
//
// So the four facts that read the CLASS BODY -- "inline in the class body",
// "the two forwarders are one call", "the default argument lives on the class
// declaration", "the class still declares operator new" -- had their subject
// inside an arm the compiler never saw. That is the check-that-cannot-fail
// shape. NONE OF THEM IS DELETED. Each is re-stated as the ABSENCE it now is,
// PAIRED with the live successor that carries the same guarantee: the free
// functions declared in sourcesharp/game/server/baseentity_statics.h, whose
// baseentity_statics.cpp IS in the build, and the header's own surviving tail,
// which TheHeaderNoLongerDeclaresAnEntityClassAtAll pins in both directions.
//
// game/server/baseentity.cpp was NOT renamed with the rest of the tree, and
// is not a compiled TU: neither client.so nor server.so builds it, so it is not
// in the source farm. The checks that read it -- the out-of-line definitions
// gone from the class scope, the two free functions' bodies, the two
// `#ifndef SOURCESHARP` arms, the ScriptGetModelKeyValues pointer-to-member
// site, the class-scope allocation operators -- and the out-of-line-definition
// and guard-range detectors that existed only for them were culled with it.
// What is left pins the two files the build does compile: baseentity.h's live
// tail, and baseentity_statics.h's declarations of the two lifted functions.
//
// AND THEN THE HEADER WENT TOO. game/server/baseentity.h is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6, so its live
// tail lost its subject and every absence read from it became the single
// anchored absence TheServerHeaderHasLeftTheFarm. What still reads text here
// is baseentity_statics.h.
//
// WHY THOSE STILL NEED A GUARD:
//
//   1. Somebody puts logic back in a header-level ShouldToggle or
//      CreatePredictedEntityByName. Then there are two implementations, and
//      they drift silently in both directions.
//
//   2. Somebody "finishes the job" on the allocation operators. A free
//      `operator new( size_t )` declared in baseentity.h is not a lifted
//      CBaseEntity::operator new -- it REPLACES THE GLOBAL ONE, and nothing
//      about that is a compile error.
//
//   3. THE DEFAULT ARGUMENT. `persist = false` lived on the class declaration
//      and must NOT be repeated on the free function -- repeating a default in
//      two places is how the two drift, and C++ will not complain because the
//      free declaration is in a different scope.
//
// Each file is pinned by an ANCHOR string rather than a line count: a file
// that fails to open reads as empty, which makes every absence assertion true
// at once.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

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

const std::string &ServerHeader()
{
	static const std::string s = ReadGameFile( "server/baseentity.h" );
	return s;
}

const std::string &StaticsHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/server/baseentity_statics.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a name written in PROSE -- and all three
// files under test are full of exactly that, being about exactly this -- is not
// read as code. This file's own header block would fail nearly every assertion
// below without it.
//
// Not a C++ lexer: it does not track string or character literals, and does not
// need to: the only needles read through it are names and #include lines.
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
			const size_t end = s.find( "*/", i + 2 );
			if ( end == std::string::npos )
				break;
			i = end + 2;
			continue;
		}
		out += s[ i++ ];
	}

	return out;
}

bool Contains( const std::string &strHaystack, const char *pszNeedle )
{
	return strHaystack.find( pszNeedle ) != std::string::npos;
}

}  // namespace

//=============================================================================
// The files were actually read.
//=============================================================================

TEST( BaseEntityLift, TheFilesUnderTestWereActuallyRead )
{
	// Anchors, not line counts. A file that fails to open reads as empty, and
	// every "no longer declares" assertion below is then true at once. A line
	// count is no defence either -- one lane's `lines.size() > 100` sat four
	// lines from a false alarm.
	//
	// game/server/baseentity.h used to be the first row here. It is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6; its
	// absence is pinned in TheServerHeaderHasLeftTheFarm below instead.
	ASSERT_FALSE( StaticsHeader().empty() )
		<< "sourcesharp/game/server/baseentity_statics.h read as empty. Every "
		<< "absence assertion in this file would pass for free.";
	EXPECT_TRUE( Contains( StaticsHeader(), "BASEENTITY_STATICS_H" ) )
		<< "sourcesharp/game/server/baseentity_statics.h no longer contains its "
		<< "anchor \"BASEENTITY_STATICS_H\". Either the wrong file was read, or "
		<< "the anchor needs updating -- do not simply delete it.";
}

//=============================================================================
// THE HEADER ITSELF IS GONE, which is the stronger form of every fact this
// file used to make about it.
//
// Seven facts read game/server/baseentity.h: that it declared no entity class
// (CBaseEntity in code, an IGameEntity body), that none of the four
// empty-bodied members and neither lifted name was defined in it, that it
// carried no `persist = false` default, no MyNPCPointer or
// ScriptGetModelKeyValues arm, and no free `operator new`/`operator delete`.
// Each guarded a stale SDK thing staying gone from a header every server TU
// included. Since plan_server_novalve.md P5 no binary compiles the header, and
// in P6 it left the farm: nothing can grow back in a file no TU can reach. The
// facts that pinned its LIVE tail -- the IGameEntity forward declaration, the
// CREATE_PREDICTED_ENTITY macro, EXTERN_SEND_TABLE(DT_BaseEntity) and the
// CHandle<IGameEntity> specialisation -- lost their subject with it and are
// retired, as are the definition detectors and their fixtures, which only
// those facts used.
//
// THE POSITIVE ANCHOR is what keeps this from being vacuous: ReadGameFile
// returns empty both for a missing file and for "no tree found", so a sibling
// in the same directory must read, with its own guard, from the same root.
//=============================================================================

TEST( BaseEntityLift, TheServerHeaderHasLeftTheFarm )
{
	const std::string strSibling = ReadGameFile( "server/entity_funcs.h" );
	ASSERT_TRUE( Contains( strSibling, "#ifndef ENTITY_FUNCS_H" ) )
		<< "game/server/entity_funcs.h did not read from the farm, so the "
		<< "absence below would be true of a tree that was never found.";

	EXPECT_TRUE( ServerHeader().empty() )
		<< "game/server/baseentity.h is readable from the farm again. Nothing "
		<< "has compiled it since plan_server_novalve.md P5; if a target needs "
		<< "it again, its stale class arms and free operators come back with it.";
}

//=============================================================================
// The two genuine lifts.
//=============================================================================

TEST( BaseEntityLift, TheTwoLiftedDeclarationsAreGoneBecauseNothingDefinedThem )
{
	// WAS TheTwoFreeFunctionsAreDeclaredInTheStaticsHeader. Both lifts were
	// DECLARED in baseentity_statics.h and DEFINED nowhere: EntityUse_ShouldToggle
	// and EntityPredict_CreateByName had no body in any compiled TU and no
	// caller. A declaration with no definition is the shape that invites
	// somebody to supply one, so they went (P5 lane E), with the shareddefs.h
	// include that existed only for ShouldToggle's USE_TYPE.
	//
	// THE POSITIVE HALF proves the header was read, by its guard: its last
	// declaration, GameSound_EmitSentenceByIndex, went in the server gc cull.
	const std::string strStatics = StripComments( StaticsHeader() );
	ASSERT_TRUE( Contains( strStatics, "#ifndef BASEENTITY_STATICS_H" ) )
		<< "baseentity_statics.h did not read; the absences below would mean nothing";

	EXPECT_FALSE( Contains( strStatics, "EntityUse_ShouldToggle" ) )
		<< "baseentity_statics.h declares EntityUse_ShouldToggle again. Nothing "
		<< "defines it.";
	EXPECT_FALSE( Contains( strStatics, "EntityPredict_CreateByName" ) )
		<< "baseentity_statics.h declares EntityPredict_CreateByName again. "
		<< "Nothing defines it.";
	EXPECT_FALSE( Contains( strStatics, "#include \"shareddefs.h\"" ) )
		<< "baseentity_statics.h includes shareddefs.h again; nothing in it "
		<< "names a shareddefs.h type now, and the include keeps the header off "
		<< "the R5 set.";
}

TEST( BaseEntityLift, NoPersistDefaultCameBackOnEitherHeader )
{
	// WAS TheDefaultArgumentWentWithTheClassAndDidNotMoveToTheFreeOne. Its
	// presence half -- the `bool persist )` parameter surviving on the free
	// declaration -- had its subject deleted with EntityPredict_CreateByName.
	// The absence half stands: no `persist = false` default on either header.
	// Its baseentity.h half is now TheServerHeaderHasLeftTheFarm: that header is
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in P6.
	const std::string strStatics = StripComments( StaticsHeader() );
	ASSERT_FALSE( strStatics.empty() ) << "baseentity_statics.h did not read";
	EXPECT_FALSE( Contains( strStatics, "persist" ) )
		<< "baseentity_statics.h names `persist` again; the declaration that "
		<< "took it is gone.";
}
