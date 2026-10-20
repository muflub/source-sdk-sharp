//========= SourceSharp — what a projectile's impact is allowed to NAME =======//
//
// THE BUG. A projectile on the parallel hierarchy flies by tracing a line each
// tick and telling managed code what it hit. What it hit is a pointer the
// ENGINE wrote, at the entity's IHandleEntity offset, and the engine has no
// idea whether the thing there is a CBaseEntity — gametrace.h says exactly that
// at the field. The shell narrowed it anyway:
//
//     OnManagedImpact( TraceEntity( tr ) );          // shell_standalone_base.cpp
//     ... SourceSharp_HandleFromEntity( pOther )     // shell_dispatch.cpp
//
// TraceEntity()'s job is to answer NULL for anything outside the SDK's tree,
// and util_shared.h calls that NULL "the SAFE answer for an entity outside the
// SDK — it reads as hit the world". It is safe for a caller about to
// dereference a CBaseEntity. This caller does not dereference: it hands the
// answer over AS A HANDLE, and a handle never cared which hierarchy its owner
// lived on. So a managed rocket striking a managed crate produced the same
// invalid handle as a rocket striking nothing at all, and the managed side
// could not tell the two apart. Silently — an impact nobody could name looks
// exactly like an impact against the world.
//
// TWO HALVES, because the fault has two halves.
//
//   THE DISAGREEMENT is real code and is exercised: handle_cross.h's
//   SourceSharp_SDKEntityFromUnknown — the function EntityFromEntityHandle's
//   server arm IS — driven by a mock standalone, showing that it
//   answers NULL for a standalone entity while the handle-table pointer names
//   it. Not a stand-in for the narrowing; the narrowing itself.
//
//   THE BAN is a source scan, in sdk_player_cast_test.cpp's idiom and for its
//   reason: the thing that must stay true is a SHAPE at three call sites, and
//   no value a gtest can compute would notice somebody spelling it back. Read
//   with whole-line comments stripped, because this file's own prose names both
//   functions repeatedly and a check a comment can satisfy is a check about
//   comments.
//
// WHERE THE CALL SITE WENT. plan_entity.md step 2f moved one frame of flight
// into SourceSharp.Server/EntityMotion.cs: the shell no longer traces the step,
// no longer decides what an impact means, and no longer calls OnManagedImpact
// at all. The narrowing this file bans has one place left where it could be
// spelled — the handle of the trace's entity (SourceSharp_TraceLine's *pOutHit
// write until plan_server_native_v2 T4f, libss_fwd_server's
// SSFwd_TraceFilter_HandleOf since), which is what every managed impact in the
// build now reads — so BAN A follows it there. The
// other two halves are untouched: OnManagedImpact still exists, is still
// declared IHandleEntity-wide, and still crosses by the wide spelling; nothing
// calls it, and the request in .claude/entity-port/abi-requests/2f.md asks for
// it and ENTITY_OP_IMPACT to be retired together, because the op enum is not a
// file one lane may edit.
//
// PROVEN RED. Narrow the trace's subject — write
// `*pOutHit = SourceSharp_HandleFromEntity( tr.m_pEnt );` in
// shell_worldquery.cpp — and TheTraceNamesWhatItHitWidely fails twice, once
// on the banned spelling and once on the wide one being absent. Put
// `void CStandaloneEntity::UpdateFlight()` back in shell_standalone_base.cpp
// and TheShellNoLongerFliesAProjectileItself fails. Spell
// SourceSharp_HandleFromEntity back into the scripted handler and
// TheScriptedImpactCrossesWidely fails the same way. Retype the declaration
// back to CBaseEntity * and TheImpactBoundaryTakesAHandleEntity fails. Each
// half of the change has its own failure, so a partial revert cannot pass.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <iterator>
#include <sstream>
#include <string>
#include <vector>

#include "../game/shared/handle_cross.h"

namespace
{

//=============================================================================//
// HALF ONE — the disagreement, on mocks of the two hierarchies.
//=============================================================================//
//
// The server's chain, not the client's: IHandleEntity -> IServerUnknown ->
// IServerEntity is single inheritance, so the partition is reached by a cast
// that adjusts nothing, and the game-entity boundary is a SEPARATE base. That
// separateness is what makes the comparison mean anything — on a CBaseEntity
// every view of the object is the same address and both answers agree.

class FakeGameEntity;

class FakeHandleEntity                                  // ~ IHandleEntity
{
public:
	virtual ~FakeHandleEntity() {}
	virtual int RefEHandle() const = 0;
};

class FakeGameEntity                                    // ~ IGameEntity, CBaseEntity on the server
{
public:
	virtual ~FakeGameEntity() {}
	virtual int WhoAreYou() const = 0;
};

// ONE ACCESSOR SINCE plan_server_native_v2 T1.3: stock IServerUnknown's
// GetBaseEntity(), typed by the entity face. The SDK's tree this half used to
// model beside the standalone (FakeBase, whose GetBaseEntity() answered
// itself) is gone from the server, so the narrowing below answers NULL for
// every object there is -- which is the bug's premise in its strongest form.
class FakeUnknown : public FakeHandleEntity             // ~ IServerUnknown
{
public:
	virtual FakeGameEntity *GetBaseEntity() = 0;
};

class FakeServerEntity : public FakeUnknown             // ~ IServerEntity
{
};

// A padded base BEFORE the boundary, so the IGameEntity subobject is not at
// offset zero — exactly as CStandaloneEntity's is its fourth base. Nothing here
// casts to it; the layout is what makes "the two views are different addresses"
// a fact this file can show rather than repeat.
class FakePadding
{
public:
	virtual ~FakePadding() {}
	int m_nPadding[ 4 ];
};

class FakeStandalone : public FakeServerEntity, public FakePadding, public FakeGameEntity
{
public:
	virtual FakeGameEntity *GetBaseEntity() { return this; }

	virtual int RefEHandle() const { return 0x57A10; }
	virtual int WhoAreYou() const { return 0x57A10; }
};

// What the shell used to do to the trace's pointer, spelled with the REAL
// function: EntityFromEntityHandle's server arm is this call and nothing else.
FakeGameEntity *NarrowToTheSdksTree( FakeHandleEntity *pHit )
{
	if ( !pHit )
		return 0;

	FakeUnknown *pUnknown = static_cast< FakeUnknown * >( pHit );

	return SourceSharp_SDKEntityFromUnknown< FakeServerEntity >( pUnknown );
}

TEST( ImpactSubject, NothingHitNamesNothingEitherWay )
{
	EXPECT_EQ( ( FakeGameEntity * )0, NarrowToTheSdksTree( 0 ) );
}

TEST( ImpactSubject, StandaloneIsNamedWhereNarrowingDropsIt )
{
	FakeStandalone standalone;
	FakeHandleEntity *pHit = &standalone;

	// The premise: the standalone HAS a face, so the NULL below is the
	// narrowing's verdict and not an empty object.
	ASSERT_NE( ( FakeGameEntity * )0, standalone.GetBaseEntity() );

	// The narrowing answers NULL, exactly as designed — and the shell then
	// turned that NULL into the invalid handle and told C# "you hit nothing".
	EXPECT_EQ( ( FakeGameEntity * )0, NarrowToTheSdksTree( pHit ) );

	// The handle-table pointer names it, and under its own handle. This is the
	// number the managed side gets now and did not get before.
	EXPECT_EQ( 0x57A10, pHit->RefEHandle() );
}

TEST( ImpactSubject, TheStandalonesTwoViewsAreDifferentAddresses )
{
	FakeStandalone standalone;

	// The premise the parallel hierarchy rests on, made visible: the
	// handle-table view and the game-entity view of ONE object are different
	// addresses, so naming by handle and naming by interface are genuinely
	// different acts rather than two spellings of a cast.
	FakeHandleEntity *pTableView = &standalone;
	FakeGameEntity *pBoundaryView = standalone.GetBaseEntity();

	EXPECT_NE( ( void * )pTableView, ( void * )pBoundaryView );
	EXPECT_EQ( 0x57A10, pBoundaryView->WhoAreYou() );
}

//=============================================================================//
// HALF TWO — the ban, read out of the source.
//=============================================================================//

// buildcpptests runs each binary with src/ as the working directory. A list
// rather than one path so that running the binary by hand from the repo root
// works too, and so "no directory" is distinguishable from "a directory with
// nothing in it".
const char *SourceRoot()
{
	static const char *kCandidates[] = { "sourcesharp", "src/sourcesharp", "../src/sourcesharp" };
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

// The file's lines with whole-line comments dropped.
//
// THE REASON IS sdk_player_cast_test.cpp's, learned there the hard way: a
// fixture went green because the file's own PROSE contained the string it was
// looking for. Every ban below names a function this file's subject discusses
// at length in comments, so a scan over raw text would be a scan over
// commentary.
std::vector< std::string > CodeLines( const char *pszRelative )
{
	std::vector< std::string > out;

	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return out;

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	if ( !file.is_open() )
		return out;

	std::string line;
	while ( std::getline( file, line ) )
	{
		const size_t nFirst = line.find_first_not_of( " \t" );
		if ( nFirst != std::string::npos && line.compare( nFirst, 2, "//" ) == 0 )
			continue;

		out.push_back( line );
	}

	return out;
}

// Every code line naming something, with its line number. Returning the hits
// rather than a count is what lets each assertion print the offending line.
struct Hit_t
{
	int nLine;
	std::string strText;
};

std::vector< Hit_t > LinesNaming( const std::vector< std::string > &lines, const char *pszNeedle )
{
	std::vector< Hit_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszNeedle ) == std::string::npos )
			continue;

		Hit_t hit;
		hit.nLine = (int)i + 1;
		hit.strText = lines[ i ];
		out.push_back( hit );
	}

	return out;
}

std::string Joined( const std::vector< Hit_t > &hits )
{
	std::ostringstream out;
	for ( size_t i = 0; i < hits.size(); i++ )
		out << "\n    line " << hits[ i ].nLine << ": " << hits[ i ].strText;
	return out.str();
}

// THE PROJECTILE'S OWN TRACE MOVED, and this constant moved with it.
//
// plan_entity.md step 2f took one frame of flight out of
// shell_standalone_base.cpp: the step trace, the impact decision and the report
// to managed code are SourceSharp.Server/EntityMotion.cs now, and the trace
// itself is the ABI's TraceLine slot. So the call site this file used to ban a
// narrowing at does not exist any more -- and the narrowing it banned would now
// be spelled where the trace's subject is turned into a handle for C# to read
// (SourceSharp_TraceLine until plan_server_native_v2 T4f; see BAN A).
//
// The ban followed the subject rather than being deleted with the caller. What
// it protects is unchanged: what a projectile hit must be nameable whichever
// hierarchy it lives on.
const char *kShellBase      = "game/server/shell_standalone_base.cpp";
// plan_server_native_v2 T4f: the ONE place a trace's subject becomes a handle
// is libss_fwd_server's SSFwd_TraceFilter_HandleOf now (managed traces through
// IEngineTrace::TraceRay and asks it of trace_t::m_pEnt), so the ban followed it.
const char *kTrace          = "fwd/fwd_trace_filter.cpp";
const char *kShellScripted  = "game/server/shell_dispatch.cpp";
const char *kShellHeader    = "game/server/shell_standalone.h";

// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL. Every ban below is an
// assertion that something is ABSENT, and a file that could not be read is
// absent in the same way — so a rename, a move, or a working directory nobody
// expected would turn all three green and mean nothing. Each ban's anchor is
// asserted present first.
TEST( ImpactSubjectBan, TheFilesTheBanIsAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so the source bans below are vacuous";

	ASSERT_FALSE( CodeLines( kShellBase ).empty() ) << kShellBase;
	ASSERT_FALSE( CodeLines( kTrace ).empty() ) << kTrace;
	ASSERT_FALSE( CodeLines( kShellScripted ).empty() ) << kShellScripted;
	ASSERT_FALSE( CodeLines( kShellHeader ).empty() ) << kShellHeader;

	// And the anchors exist, so "no hits" below means "no narrowing" rather
	// than "the call got renamed out from under this file".
	EXPECT_FALSE( LinesNaming( CodeLines( kTrace ), "SSFwd_TraceFilter_HandleOf(" ).empty() )
		<< "nothing in " << kTrace << " defines the handle of a trace's entity "
		   "any more, which is where the subject is named -- see the note above "
		   "TheTraceNamesWhatItHitWidely";
	EXPECT_FALSE( LinesNaming( CodeLines( kShellScripted ), "OnManagedImpact" ).empty() )
		<< "nothing in " << kShellScripted << " implements OnManagedImpact any more";
	EXPECT_FALSE( LinesNaming( CodeLines( kShellHeader ), "OnManagedImpact(" ).empty() )
		<< "nothing in " << kShellHeader << " declares OnManagedImpact any more";
}

// AND THE FLIGHT REALLY DID LEAVE, which is what makes moving the ban above an
// accurate account rather than a convenient one. If UpdateFlight came back, the
// call site the ban used to cover would be live again and uncovered.
TEST( ImpactSubjectBan, TheShellNoLongerFliesAProjectileItself )
{
	ASSERT_TRUE( SourceRoot() != NULL );

	const std::vector< std::string > lines = CodeLines( kShellBase );

	ASSERT_FALSE( lines.empty() ) << kShellBase;

	EXPECT_TRUE( LinesNaming( lines, "void CStandaloneEntity::UpdateFlight()" ).empty() )
		<< "UpdateFlight is back in " << kShellBase << ", so a projectile is "
		   "being flown natively AND by SourceSharp.Server/EntityMotion.cs -- "
		   "and the impact ban above no longer covers the native one";

	EXPECT_TRUE( LinesNaming( lines, "OnManagedImpact(" ).empty() )
		<< "something in " << kShellBase << " reports an impact again; the ban "
		   "on narrowing it moved to " << kTrace << " when the flight did";
}

// One function's body, from its definition line to the closing brace in column
// one. Crude, and deliberately so: the file is formatted that way, and the
// anchor assertion above means a formatting change that broke this shows up as
// an empty body rather than as a ban that quietly stopped looking.
std::vector< Hit_t > BodyOf( const std::vector< std::string > &lines, const char *pszSignature )
{
	std::vector< Hit_t > out;
	bool bInside = false;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( !bInside )
		{
			if ( lines[ i ].find( pszSignature ) == std::string::npos )
				continue;
			bInside = true;
		}

		Hit_t hit;
		hit.nLine = (int)i + 1;
		hit.strText = lines[ i ];
		out.push_back( hit );

		if ( bInside && lines[ i ] == "}" )
			break;
	}

	return out;
}

std::vector< Hit_t > Naming( const std::vector< Hit_t > &body, const char *pszNeedle )
{
	std::vector< Hit_t > out;
	for ( size_t i = 0; i < body.size(); i++ )
		if ( body[ i ].strText.find( pszNeedle ) != std::string::npos )
			out.push_back( body[ i ] );
	return out;
}

// BAN A — the TRACE must hand over what it actually holds.
//
// This used to read shell_standalone_base.cpp, where UpdateFlight called
// OnManagedImpact( tr.m_pEnt ) directly. Step 2f made the projectile ask the
// ABI for the trace instead, and the one place a trace's subject became a
// handle for C# was SourceSharp_TraceLine's pOutHit write (then the body it
// shared with SourceSharp_TraceLineSurface, SourceSharp_TraceLineInto).
//
// THE BAN FOLLOWED ITS SUBJECT A THIRD TIME (plan_server_native_v2 T4f). The
// server's traces are managed's now: ServerTracer calls IEngineTrace::TraceRay
// through its forwarder and hands the trace's m_pEnt to libss_fwd_server's
// SSFwd_TraceFilter_HandleOf, which asks the engine's entity for its own
// handle. That is still exactly ONE place in the build where a trace's subject
// becomes a handle for C# to read, for every trace at once, and it is still
// forbidden to narrow it: every managed impact reads that word, and narrowing
// it would do to every projectile what the original bug did to one.
TEST( ImpactSubjectBan, TheTraceNamesWhatItHitWidely )
{
	const std::vector< Hit_t > body =
		BodyOf( CodeLines( kTrace ), "uint32_t SSFwd_TraceFilter_HandleOf(" );

	ASSERT_FALSE( body.empty() )
		<< "no SSFwd_TraceFilter_HandleOf body found in " << kTrace
		<< ", so this ban is looking at nothing. If the handle of a trace's "
		   "entity is named somewhere else now, this ban has to follow it again";

	// The ANSWER: the line that asks the engine's entity for its own handle,
	// which every hierarchy (and the world) answers.
	const std::vector< Hit_t > answer = Naming( body, "GetRefEHandle()" );

	ASSERT_FALSE( answer.empty() )
		<< "the trace no longer tells C# what it hit at all -- nothing in "
		   "SSFwd_TraceFilter_HandleOf asks the entity for its handle";

	for ( size_t i = 0; i < body.size(); i++ )
	{
		EXPECT_TRUE( body[ i ].strText.find( "TraceEntity(" ) == std::string::npos )
			<< "the trace's subject is narrowed through TraceEntity(), which "
			   "answers NULL for the parallel hierarchy:\n    line "
			<< body[ i ].nLine << ": " << body[ i ].strText;

		EXPECT_TRUE( body[ i ].strText.find( "GameEntityFromEntityHandle(" ) == std::string::npos )
			<< "the trace's subject is narrowed to a game entity, which answers "
			   "NULL for the world and a static prop:\n    line "
			<< body[ i ].nLine << ": " << body[ i ].strText;
	}
}

// BAN B — the crossing to managed must be the wide handle spelling.
//
// SCOPED TO THE ONE FUNCTION, and the first draft of this test was not, which
// is how it learned the difference. A whole-file ban on
// SourceSharp_HandleFromEntity fires on four sites in this file that are
// RIGHT: the activator crossing already asks both arms and picks the spelling
// per arm, and the damage ones read CTakeDamageInfo, which is CBaseEntity-only
// all the way down and has no other arm to ask. A ban that has to be argued
// with is a ban somebody deletes.
TEST( ImpactSubjectBan, TheScriptedImpactCrossesWidely )
{
	const std::vector< Hit_t > body =
		BodyOf( CodeLines( kShellScripted ), "::OnManagedImpact(" );

	ASSERT_FALSE( body.empty() )
		<< "no OnManagedImpact body found in " << kShellScripted
		<< ", so this ban is looking at nothing";

	const std::vector< Hit_t > narrow = Naming( body, "SourceSharp_HandleFromEntity(" );

	EXPECT_TRUE( narrow.empty() )
		<< "the impact names its subject with the CBaseEntity-only spelling, "
		   "which is the invalid handle for everything on the parallel "
		   "hierarchy:" << Joined( narrow );

	EXPECT_FALSE( Naming( body, "SourceSharp_HandleFromAnyEntity(" ).empty() )
		<< "the impact does not cross by the wide handle spelling at all";
}

// BAN C — and the boundary itself must be typed so the narrow answer cannot be
// passed at all. A ban on two call sites that leaves the signature narrow is a
// ban somebody re-breaks by writing the obvious thing.
TEST( ImpactSubjectBan, TheImpactBoundaryTakesAHandleEntity )
{
	const std::vector< Hit_t > decls =
		LinesNaming( CodeLines( kShellHeader ), "OnManagedImpact(" );

	for ( size_t i = 0; i < decls.size(); i++ )
	{
		EXPECT_TRUE( decls[ i ].strText.find( "IHandleEntity" ) != std::string::npos )
			<< "the impact boundary does not take an IHandleEntity, so an entity "
			   "outside the SDK cannot be named through it:\n    line "
			<< decls[ i ].nLine << ": " << decls[ i ].strText;
	}
}

} // namespace
