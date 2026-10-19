//========= SourceSharp — what a permanent decal is allowed to be STORED ON ===//
//
// THE BUG. SourceSharp_PaintDecal traces a short line through the position it
// was given and stores the mark against whatever it found. What it found is a
// pointer the ENGINE wrote, at the entity's IHandleEntity offset, and the shell
// narrowed it:
//
//     CBaseEntity *pHit = TraceEntity( trace );                // shell_worldquery.cpp
//     const int nEntityIndex = pHit ? pHit->entindex() : 0;
//
// TraceEntity()'s job is to answer NULL for anything outside the SDK's tree,
// and util_shared.h calls that NULL "the SAFE answer for an entity outside the
// SDK — it reads as hit the world". It is safe for a caller about to
// dereference a CBaseEntity. This caller does not dereference: it turns the
// answer into an ENTITY INDEX, and the index it turns NULL into is 0 — which is
// the world, a real entity with a real (identity) transform.
//
// So a permanent decal aimed at a managed brush was stored against the world at
// world coordinates. Two things then went wrong at once and neither said so:
//
//   * the wrong SUBJECT. "Painted on the managed crate" and "painted on
//     nothing" produced the identical engine call, so the mark did not travel
//     with the crate and did not go when the crate went.
//   * the wrong FRAME. Stock expresses the position in the hit entity's own
//     space precisely so a decal on a door does not stay behind when the door
//     opens. Against index 0 that frame is the identity, so the local position
//     was silently the world position.
//
// THE FIX crosses by the handle — `SourceSharp_ResolveEntity(
// SourceSharp_HandleFromAnyEntity( trace.m_pEnt ) )` — and asks the result for
// the index, the model and the frame. Not a hand-rolled cast: a handle names an
// entity rather than a C++ class, and a static prop, which is in the partition
// and is not an entity at all, has no valid handle and still comes out as 0.
//
// TWO HALVES, because the fault has two halves.
//
//   THE FRAME is real arithmetic and is exercised with real mathlib. The
//   projection's two arms ask two DIFFERENT functions, and the reason has to be
//   demonstrated rather than asserted: ICollideable::CollisionToWorldTransform
//   is NOT CBaseEntity::EntityToWorldTransform. CCollisionProperty's version
//   drops the rotation on the branch it takes for a SOLID_NONE, a SOLID_BBOX or
//   an FSOLID_FORCE_WORLD_ALIGNED entity, and keeps only the translation.
//   CStandaloneEntity's version has no such branch — SetAbsTransform builds
//   m_Transform with AngleMatrix from the origin AND the angles, every time —
//   so on THAT arm the two really are one matrix. The cases below measure the
//   gap, and measure that it closes at zero rotation, which is why an
//   axis-aligned test map could never have found this.
//
//   THE SUBJECT is a source scan, in impact_subject_test.cpp's idiom and for
//   its reason: what must stay true is a SHAPE at one call site, and no value a
//   gtest can compute would notice somebody spelling the narrowing back. Read
//   with whole-line comments stripped, because this file's own prose names
//   TraceEntity repeatedly and a check a comment can satisfy is a check about
//   comments.
//
// PROVEN RED. Documented per test at each TEST body.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"

#include "mathlib_test_env.h"

namespace
{

const float kEps = 0.001f;

//=============================================================================//
// HALF ONE — the frame, on real mathlib.
//=============================================================================//

// What CBaseEntity::EntityToWorldTransform hands back, and what
// CStandaloneEntity::CollisionToWorldTransform hands back: the same
// construction, m_rgflCoordinateFrame on one side and m_Transform on the other,
// both built from the origin and the angles together.
matrix3x4_t EntityFrame( const Vector &vecOrigin, const QAngle &angles )
{
	matrix3x4_t mat;
	AngleMatrix( angles, vecOrigin, mat );
	return mat;
}

// What CCollisionProperty::CollisionToWorldTransform hands back on the OTHER
// branch — !IsBoundsDefinedInEntitySpace(), which is a SOLID_NONE, a SOLID_BBOX
// or an FSOLID_FORCE_WORLD_ALIGNED entity. Identity, with the collision origin
// dropped into column three, and the rotation gone.
matrix3x4_t WorldAlignedFrame( const Vector &vecOrigin )
{
	matrix3x4_t mat;
	SetIdentityMatrix( mat );
	MatrixSetTranslation( vecOrigin, mat );
	return mat;
}

// The one line of SourceSharp_PaintDecal this half is about: a world point
// expressed in the frame the decal will be stored against.
Vector LocalPositionThrough( const matrix3x4_t &frame, const Vector &vecWorld )
{
	Vector vecLocal;
	VectorITransform( vecWorld, frame, vecLocal );
	return vecLocal;
}

// PROVEN RED: make WorldAlignedFrame call AngleMatrix instead of
// SetIdentityMatrix and this fails — which is the mutation that says the two
// really are one matrix, i.e. the mistake the projection exists to prevent.
TEST( PermanentDecalFrame, TheTwoTransformsDisagreeOnARotatedEntity )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 90.0f, 0.0f );          // a yawed brush
	const Vector vecHit( 160.0f, -64.0f, 40.0f );      // a point on its face

	const Vector vecEntitySpace =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );
	const Vector vecWorldAligned =
		LocalPositionThrough( WorldAlignedFrame( vecOrigin ), vecHit );

	// 32 units along world +X from the origin. In the entity's own frame that
	// is 32 units along its LOCAL -Y, because the entity is yawed 90 degrees.
	EXPECT_NEAR( 0.0f,  vecEntitySpace.x, kEps );
	EXPECT_NEAR( -32.0f, vecEntitySpace.y, kEps );
	EXPECT_NEAR( 8.0f,  vecEntitySpace.z, kEps );

	// The world-aligned frame keeps the translation and nothing else, so the
	// same point comes out on +X.
	EXPECT_NEAR( 32.0f, vecWorldAligned.x, kEps );
	EXPECT_NEAR( 0.0f,  vecWorldAligned.y, kEps );
	EXPECT_NEAR( 8.0f,  vecWorldAligned.z, kEps );

	// And therefore: substituting one for the other moves the mark. 45.25
	// units, on a brush 32 units wide — a decal off the surface entirely.
	EXPECT_GT( ( vecEntitySpace - vecWorldAligned ).Length(), 1.0f );
}

// THE REASON THIS NEEDED A TEST RATHER THAN A READING. At zero rotation the two
// frames are the same matrix, so every axis-aligned map, and every hand check
// on one, agrees. The bug is invisible until something is turned.
//
// PROVEN RED: give `angles` a non-zero yaw here and the EXPECT_NEAR on y fails.
TEST( PermanentDecalFrame, TheyAgreeExactlyWhenNothingIsRotated )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 0.0f, 0.0f );
	const Vector vecHit( 160.0f, -64.0f, 40.0f );

	const Vector vecEntitySpace =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );
	const Vector vecWorldAligned =
		LocalPositionThrough( WorldAlignedFrame( vecOrigin ), vecHit );

	EXPECT_NEAR( vecEntitySpace.x, vecWorldAligned.x, kEps );
	EXPECT_NEAR( vecEntitySpace.y, vecWorldAligned.y, kEps );
	EXPECT_NEAR( vecEntitySpace.z, vecWorldAligned.z, kEps );
}

// The OLD behaviour, priced. Narrowing answered NULL for a managed brush, the
// index came out 0, and index 0's frame is the identity — no translation
// either. So the "local" position handed to the engine was the raw world
// position, which for a brush anywhere but the map origin is not close.
//
// PROVEN RED: change the identity here to WorldAlignedFrame( vecOrigin ) — i.e.
// pretend the narrowed path at least kept the translation — and the
// EXPECT_NEARs on the world position fail.
TEST( PermanentDecalFrame, TheNarrowedAnswerStoredTheWorldPositionUntouched )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 90.0f, 0.0f );
	const Vector vecHit( 160.0f, -64.0f, 40.0f );

	matrix3x4_t matWorld;
	SetIdentityMatrix( matWorld );

	const Vector vecAgainstTheWorld = LocalPositionThrough( matWorld, vecHit );

	EXPECT_NEAR( vecHit.x, vecAgainstTheWorld.x, kEps );
	EXPECT_NEAR( vecHit.y, vecAgainstTheWorld.y, kEps );
	EXPECT_NEAR( vecHit.z, vecAgainstTheWorld.z, kEps );

	// And how far that is from where the mark belonged.
	const Vector vecCorrect =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );

	EXPECT_GT( ( vecAgainstTheWorld - vecCorrect ).Length(), 100.0f );
}

//=============================================================================//
// HALF TWO — the subject, read out of the source.
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

// The file's lines with whole-line comments dropped. See the header comment:
// this file's own prose names every function it bans.
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

struct Hit_t
{
	int nLine;
	std::string strText;
};

// One function's body, from its definition line to the closing brace in column
// one. Crude, and deliberately so: the file is formatted that way, and the
// anchor assertion below means a formatting change that broke this shows up as
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

		if ( lines[ i ] == "}" )
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

std::string Joined( const std::vector< Hit_t > &hits )
{
	std::ostringstream out;
	for ( size_t i = 0; i < hits.size(); i++ )
		out << "\n    line " << hits[ i ].nLine << ": " << hits[ i ].strText;
	return out.str();
}

const char *kWorldQuery   = "game/server/shell_worldquery.cpp";
const char *kResolveHdr   = "game/server/shell_resolve.h";
const char *kStandaloneCpp = "game/server/shell_standalone_base.cpp";

const char *kDecalFn      = "SourceSharp_PaintDecal(";
const char *kFrameFn      = "SourceSharpEntity_t::EntityToWorld()";

// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL. Every ban below asserts
// that something is ABSENT, and a file that could not be read is absent in the
// same way — so a rename, a move, or a working directory nobody expected would
// turn all of them green and mean nothing. Each anchor is asserted present
// first.
TEST( PermanentDecalBan, TheFilesTheBanIsAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so the source bans below are vacuous";

	ASSERT_FALSE( CodeLines( kWorldQuery ).empty() ) << kWorldQuery;
	ASSERT_FALSE( CodeLines( kResolveHdr ).empty() ) << kResolveHdr;
	ASSERT_FALSE( CodeLines( kStandaloneCpp ).empty() ) << kStandaloneCpp;

	// And the two bodies the bans read are findable, so "no hits" below means
	// "no narrowing" rather than "the function got renamed out from under this
	// file".
	ASSERT_FALSE( BodyOf( CodeLines( kWorldQuery ), kDecalFn ).empty() )
		<< "no " << kDecalFn << " body in " << kWorldQuery;
	ASSERT_FALSE( BodyOf( CodeLines( kStandaloneCpp ), kFrameFn ).empty() )
		<< "no " << kFrameFn << " body in " << kStandaloneCpp;
}

// BAN A — the decal must not narrow its subject, and must cross by the handle.
//
// PROVEN RED: put `CBaseEntity *pHit = TraceEntity( trace );` back into
// SourceSharp_PaintDecal and the first EXPECT fails, naming the line.
TEST( PermanentDecalBan, TheDecalNamesWhatItHitWidely )
{
	const std::vector< Hit_t > body = BodyOf( CodeLines( kWorldQuery ), kDecalFn );

	const std::vector< Hit_t > narrow = Naming( body, "TraceEntity(" );

	EXPECT_TRUE( narrow.empty() )
		<< "the decal's subject is narrowed through TraceEntity(), which answers "
		   "NULL for the parallel hierarchy — and this site turns that NULL into "
		   "entity index 0, the world:" << Joined( narrow );

	EXPECT_FALSE( Naming( body, "SourceSharp_HandleFromAnyEntity(" ).empty() )
		<< "the decal does not name its subject by the wide handle spelling at "
		   "all, so an entity outside the SDK cannot be its subject";

	EXPECT_FALSE( Naming( body, "ss::TraceHit( trace )" ).empty() )
		<< "the decal no longer reads the trace's own m_pEnt, so what it does "
		   "read needs re-reading against this ban";
}

// BAN B — and it must take the FRAME from the same projection, not from
// whichever arm happens to be non-NULL. A subject fixed with a frame still read
// off pBase is a decal on the right entity in the wrong place, and pBase is
// NULL for every entity on the parallel hierarchy — so it would be a null
// dereference on exactly the case this change is for.
//
// PROVEN RED: spell `pHit->EntityToWorldTransform()` back into the body and the
// first EXPECT fails; delete the EntityToWorld() call and the second does.
TEST( PermanentDecalBan, TheDecalTakesItsFrameFromTheProjection )
{
	const std::vector< Hit_t > body = BodyOf( CodeLines( kWorldQuery ), kDecalFn );

	const std::vector< Hit_t > direct = Naming( body, "EntityToWorldTransform(" );

	EXPECT_TRUE( direct.empty() )
		<< "the decal reads the SDK arm's transform directly, which is NULL for "
		   "every entity on the parallel hierarchy:" << Joined( direct );

	EXPECT_FALSE( Naming( body, "EntityToWorld()" ).empty() )
		<< "the decal does not ask the projection for its frame at all";
}

// BAN C — the projection's two arms must stay two arms.
//
// This is the ban that carries the fact. ICollideable::CollisionToWorldTransform
// and CBaseEntity::EntityToWorldTransform are not the same matrix, as HALF ONE
// measures — but on the PARALLEL hierarchy they are, because CStandaloneEntity's
// version is an unbranched AngleMatrix. Collapsing the projection to one call,
// in either direction, is therefore a change that compiles, runs, and is wrong
// on half its inputs and only when something is rotated.
//
// PROVEN RED: replace the pBase arm's EntityToWorldTransform() with
// GetCollideable()->CollisionToWorldTransform() and the first EXPECT fails;
// replace the pStandalone arm's CollisionToWorldTransform() with anything else
// and the second does.
TEST( PermanentDecalBan, TheFrameProjectionAsksEachHierarchyItsOwnQuestion )
{
	// READ THE ASSERTIONS, NOT THE NAME. There is ONE hierarchy left that can
	// answer this question. The name is kept because the lane ledger keys on
	// it, and because the ban it names is still in force -- in a stronger form.
	//
	// WHY THE SDK HALF WENT. The arm was
	//
	//     #ifndef SS_CBASEENTITY_ALIAS
	//     if ( pBase )
	//         return pBase->EntityToWorldTransform();
	//     #endif
	//
	// and game/server/server_csharp.vpc:69 defines SS_CBASEENTITY_ALIAS, so it
	// was NEVER COMPILED. Nor could it have run un-guarded: pBase is resolved
	// through GetBaseEntity(), and all eight implementations of that in the two
	// DLLs return a constant NULL -- which is why lane `the-close` deleted the
	// resolvers as well (d3a0bfa3a). So this assertion was green while
	// describing a branch that had never once been taken. Deleted at f288a5e11.
	//
	// THE BAN SURVIVES AND SHARPENS. What it forbade was COLLAPSING the two
	// arms into one call, in either direction, because CCollisionProperty's
	// CollisionToWorldTransform drops the rotation for a SOLID_BBOX, a
	// SOLID_NONE or a world-aligned entity while CStandaloneEntity's is an
	// unbranched AngleMatrix -- a change that compiles, runs, and is wrong on
	// half its inputs and only when something is rotated. With one arm there is
	// nothing left to collapse. What must be defended instead is that the arm
	// that remains asks the question that is RIGHT FOR ITS HIERARCHY, and that
	// the NO-ARM case answers an identity rather than something plausible.
	const std::vector< Hit_t > body = BodyOf( CodeLines( kStandaloneCpp ), kFrameFn );

	ASSERT_FALSE( body.empty() )
		<< kFrameFn << " has no body in " << kStandaloneCpp << "; every "
		   "assertion below would pass vacuously for a deleted projection";

	// THE SURVIVING ARM, and it is the one this hierarchy's frame really lives
	// in: SetAbsTransform builds the block's WorldTransform with AngleMatrix
	// from the origin and the angles together, unconditionally.
	EXPECT_FALSE( Naming( body, "pStandalone->CollisionToWorldTransform()" ).empty() )
		<< "the parallel arm no longer asks CollisionToWorldTransform, which is "
		   "the only thing on that hierarchy holding the origin and the angles "
		   "together";

	// THE ARM THAT WENT, asserted absent so that a restore is a red rather than
	// a quiet return of a branch nothing ever took.
	EXPECT_TRUE( Naming( body, "pBase" ).empty() )
		<< "the projection reaches pBase again. It is a constant NULL in this "
		   "build -- every GetBaseEntity() in server.so is `mov $0x0,%eax; ret` "
		   "-- so an un-guarded arm here is a branch that is never taken with a "
		   "dereference at the end of it, and a guarded one is a restored dead "
		   "arm.";

	EXPECT_TRUE( Naming( body, "EntityToWorldTransform()" ).empty() )
		<< "the projection asks EntityToWorldTransform again. That is "
		   "CBaseEntity's spelling of the frame, and CBaseEntity is not a name "
		   "in this build.";

	// AND NO GUARD CAME BACK WITH IT. Checked because the arms above are
	// assertions about the FILE, and a guard would make them assertions about
	// whichever arm I happened to read -- the failure mode finding 3 of
	// .claude/entity-port/status/residue-tests.md names.
	EXPECT_TRUE( Naming( body, "#ifndef SS_CBASEENTITY_ALIAS" ).empty() )
		<< "the projection is guarded again, so the arm counts above describe "
		   "one configuration rather than the build";
	EXPECT_TRUE( Naming( body, "#ifdef" ).empty() )
		<< "the projection has conditional compilation in it again";

	// THE THIRD ANSWER, which is what stops "one arm" from meaning "one arm and
	// a fall-through nobody looked at". A caller that transforms a point
	// through this without checking must get the point back unchanged -- world
	// space, which is what "against no entity" means to every engine call that
	// takes one of these -- rather than a garbage frame.
	EXPECT_FALSE( Naming( body, "s_matIdentity" ).empty() )
		<< "the no-arm case no longer returns a named identity";
	EXPECT_FALSE( Naming( body, "1.0f, 0.0f, 0.0f, 0.0f" ).empty() )
		<< "the no-arm frame is not the identity matrix any more. A decal "
		   "projected through a non-identity fallback lands somewhere nobody "
		   "chose, and nothing says so.";
}

// BAN D — the three engine-facing projections must be declared where they can
// be found, because the whole point of putting them on SourceSharpEntity_t is
// that the NEXT site needing an index or a frame has somewhere to ask.
//
// PROVEN RED: delete any one declaration from shell_resolve.h and its EXPECT
// fails. (It would also stop compiling, which is the point — this catches the
// case where somebody keeps the declaration but moves it somewhere private.)
TEST( PermanentDecalBan, TheProjectionsAreOnTheOneResolver )
{
	const std::vector< std::string > lines = CodeLines( kResolveHdr );

	std::vector< Hit_t > all;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		Hit_t hit;
		hit.nLine = (int)i + 1;
		hit.strText = lines[ i ];
		all.push_back( hit );
	}

	EXPECT_FALSE( Naming( all, "int EntityIndex() const" ).empty() );
	EXPECT_FALSE( Naming( all, "int ModelIndex() const" ).empty() );
	EXPECT_FALSE( Naming( all, "const matrix3x4_t &EntityToWorld() const" ).empty() );
}

} // namespace
