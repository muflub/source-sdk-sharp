//========= SourceSharp — the temp entity backends' ARGUMENTS ================//
//
// plan_tmp.md Part 4, C-Phase 2, gate 2.
//
// WHAT THIS EXISTS TO CATCH. Fifteen new client backends landed in one pass and
// every one of them is the same shape: unpack a flat argument list, call one
// drawing primitive. The call cannot be checked here — beams, g_pEffects and
// effects are the client DLL's globals — but the unpacking can, and the
// unpacking is where the mistake lives:
//
//   * (The six beam backends that made this file necessary left with the
//     managed beam system; TempEntBeamClientTests pins their argument order
//     against IClientBeams now.)
//   * A pointer that MAY be null (a spark's direction, a decal's S axis) and
//     one that MAY NOT (an effect's position) look identical at the call site,
//     and stock dereferences the second kind without a check.
//
// So the tests below use DISTINCT VALUES for every argument. A test that passed
// 1.0f for five floats in a row would agree with every permutation of them,
// which is this file's version of "do not compare a computation with itself".
//
// Engine-free: the only include is the header under test, which includes the
// ABI header and nothing else.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <string.h>

#include "../game/client/shell_te_backend_args.h"

using namespace TeBackendArgs;

namespace
{
	// Three coordinates nobody could confuse for another three.
	const float g_Start[3]  = { 11.0f, 12.0f, 13.0f };
	const float g_End[3]    = { 21.0f, 22.0f, 23.0f };
	const float g_Centre[3] = { 31.0f, 32.0f, 33.0f };

	void ExpectVec( const Vec3 &v, float x, float y, float z )
	{
		EXPECT_FLOAT_EQ( x, v.x );
		EXPECT_FLOAT_EQ( y, v.y );
		EXPECT_FLOAT_EQ( z, v.z );
	}
}

//=============================================================================//
// THE VECTOR HELPERS, which everything below leans on.
//=============================================================================//

TEST( TeBackendVec, ReadsThreeFloatsInOrder )
{
	ExpectVec( VecFrom( g_Start ), 11.0f, 12.0f, 13.0f );
}

TEST( TeBackendVec, ANullPointerIsTheOrigin )
{
	// vec3_origin is what stock reads a missing endpoint as -- see
	// c_te_beamentpoint.cpp's `pStart ? *pStart : vec3_origin`. Callers that
	// must NOT accept a null check the pointer themselves and refuse; this
	// fallback exists for the ones stock lets through.
	ExpectVec( VecFrom( NULL ), 0.0f, 0.0f, 0.0f );
}

//=============================================================================//
// THE IEffects FAMILY.
//=============================================================================//

TEST( TeBackendSparks, CarriesMagnitudeAndTrailLengthSeparately )
{
	const EffectRequest req = Sparks( g_Start, /*magnitude*/ 3, /*trail*/ 9, g_End );

	EXPECT_TRUE( req.bDraw );
	ExpectVec( req.vecPosition, 11.0f, 12.0f, 13.0f );
	EXPECT_EQ( 3, req.nMagnitude );
	EXPECT_EQ( 9, req.nTrailLength );
	EXPECT_TRUE( req.bHasDirection );
	ExpectVec( req.vecDirection, 21.0f, 22.0f, 23.0f );
}

//-----------------------------------------------------------------------------
// NO DIRECTION IS NOT A ZERO DIRECTION. IEffects::Sparks declares the parameter
// `const Vector *pvecDir = NULL` and FX_ElectricSpark reads the POINTER, so a
// null is "no preferred direction" and a zero vector is a different effect.
// Collapsing the two here would be a decode that quietly changed the drawing.
//-----------------------------------------------------------------------------
TEST( TeBackendSparks, TellsAbsentDirectionFromAZeroOne )
{
	const float zero[3] = { 0.0f, 0.0f, 0.0f };

	const EffectRequest absent = Sparks( g_Start, 1, 1, NULL );
	const EffectRequest given  = Sparks( g_Start, 1, 1, zero );

	EXPECT_TRUE( absent.bDraw );
	EXPECT_FALSE( absent.bHasDirection );

	EXPECT_TRUE( given.bDraw );
	EXPECT_TRUE( given.bHasDirection );

	// Same VALUE either way; only the flag tells them apart, which is why the
	// flag is carried rather than inferred.
	ExpectVec( absent.vecDirection, 0.0f, 0.0f, 0.0f );
	ExpectVec( given.vecDirection,  0.0f, 0.0f, 0.0f );
}

TEST( TeBackendSparks, RefusesAMissingPosition )
{
	EXPECT_FALSE( Sparks( NULL, 1, 1, g_End ).bDraw );
}

//-----------------------------------------------------------------------------
// Ricochet and MetalSparks share this decoder because they share their
// arguments. The difference between them -- one makes a sound -- is which
// primitive the .cpp calls, and that is the whole reason there are two slots
// rather than one with a flag.
//-----------------------------------------------------------------------------
TEST( TeBackendPositionAndDirection, KeepsThemTheRightWayRound )
{
	const EffectRequest req = PositionAndDirection( g_Start, g_End );

	EXPECT_TRUE( req.bDraw );
	EXPECT_TRUE( req.bHasDirection );
	ExpectVec( req.vecPosition,  11.0f, 12.0f, 13.0f );
	ExpectVec( req.vecDirection, 21.0f, 22.0f, 23.0f );
}

TEST( TeBackendPositionAndDirection, RefusesEitherMissing )
{
	EXPECT_FALSE( PositionAndDirection( NULL,    g_End ).bDraw );
	EXPECT_FALSE( PositionAndDirection( g_Start, NULL  ).bDraw );
}

TEST( TeBackendSmoke, CarriesModelScaleAndFrameRate )
{
	const EffectRequest req = Smoke( g_Start, /*model*/ 77, /*scale*/ 12.5f, /*rate*/ 4.0f );

	EXPECT_TRUE( req.bDraw );
	ExpectVec( req.vecPosition, 11.0f, 12.0f, 13.0f );
	EXPECT_EQ( 77, req.nModelIndex );
	EXPECT_FLOAT_EQ( 12.5f, req.flScale );
	EXPECT_FLOAT_EQ( 4.0f, req.flFrameRate );

	// Smoke is not aimed and has no type.
	EXPECT_FALSE( req.bHasDirection );
	EXPECT_EQ( 0, req.nType );
}

//-----------------------------------------------------------------------------
// The muzzle flash's ANGLES are angles, not a direction. Both are three floats
// and a decoder that put them in vecDirection would compile, so the two fields
// are separate and this says which is which.
//-----------------------------------------------------------------------------
TEST( TeBackendMuzzleFlash, PutsAnglesInTheAngleFieldAndNotTheDirection )
{
	const EffectRequest req = MuzzleFlash( g_Start, g_End, /*scale*/ 2.0f, /*type*/ 2 );

	EXPECT_TRUE( req.bDraw );
	ExpectVec( req.vecPosition, 11.0f, 12.0f, 13.0f );
	ExpectVec( req.vecAngles,   21.0f, 22.0f, 23.0f );
	ExpectVec( req.vecDirection, 0.0f, 0.0f, 0.0f );
	EXPECT_FALSE( req.bHasDirection );

	EXPECT_FLOAT_EQ( 2.0f, req.flScale );
	EXPECT_EQ( 2, req.nType );
}

TEST( TeBackendMuzzleFlash, RefusesMissingAngles )
{
	// g_pEffects->MuzzleFlash takes a `const QAngle &`; an unoriented flash is
	// not something the primitive can draw.
	EXPECT_FALSE( MuzzleFlash( g_Start, NULL, 1.0f, 0 ).bDraw );
	EXPECT_FALSE( MuzzleFlash( NULL, g_End, 1.0f, 0 ).bDraw );
}

//-----------------------------------------------------------------------------
// An unknown muzzle flash TYPE is passed through, not defaulted.
// g_pEffects->MuzzleFlash prints "No case for Muzzleflash type: %d" for
// anything it has no case for, and that line is the diagnostic. Silently
// substituting zero here would turn a caller's mistake into a flash that looks
// right, which is worse than no flash at all.
//-----------------------------------------------------------------------------
TEST( TeBackendMuzzleFlash, PassesAnUnknownTypeThroughRatherThanDefaultingIt )
{
	EXPECT_EQ( 99, MuzzleFlash( g_Start, g_End, 1.0f, 99 ).nType );
}

//=============================================================================//
// THE DECALS.
//=============================================================================//

TEST( TeBackendDecalShoot, CarriesIndexEntityPositionAndFlags )
{
	const DecalRequest req = DecalShoot( /*decal*/ 12, /*entity*/ 34, g_Start,
	                                     /*sAxis*/ g_End, /*flags*/ 8 );

	EXPECT_TRUE( req.bDraw );
	EXPECT_EQ( 12, req.nDecalIndex );
	EXPECT_EQ( 34, req.nEntity );
	ExpectVec( req.vecPosition, 11.0f, 12.0f, 13.0f );
	EXPECT_TRUE( req.bHasSAxis );
	ExpectVec( req.vecSAxis, 21.0f, 22.0f, 23.0f );
	EXPECT_EQ( 8, req.nFlags );
}

//-----------------------------------------------------------------------------
// ENTITY ZERO IS THE WORLD, and it has to stay drawable: c_te_worlddecal.cpp
// and c_te_bspdecal.cpp both ask for entity 0 by name. A guard written as
// "entity <= 0" would drop every world decal in the game and leave the
// footprints working, which is the kind of half-failure nobody reports.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalShoot, EntityZeroIsTheWorldAndIsDrawn )
{
	const DecalRequest req = DecalShoot( 5, 0, g_Start, NULL, 0 );

	EXPECT_TRUE( req.bDraw );
	EXPECT_EQ( 0, req.nEntity );
}

//-----------------------------------------------------------------------------
// A NULL S axis is not a zero one, for Sparks' reason: DecalShoot reads the
// pointer and picks its own axis when there is none.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalShoot, TellsAnAbsentSAxisFromAZeroOne )
{
	const float zero[3] = { 0.0f, 0.0f, 0.0f };

	EXPECT_FALSE( DecalShoot( 5, 0, g_Start, NULL, 0 ).bHasSAxis );
	EXPECT_TRUE ( DecalShoot( 5, 0, g_Start, zero, 0 ).bHasSAxis );
}

//-----------------------------------------------------------------------------
// -1 is what Draw_DecalIndexFromName answers for a name the engine does not
// know, and it flows straight into m_nIndex for anything that built its index
// from a name. Handing it on would index the engine's decal array from below
// zero.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalShoot, RefusesAnUnknownDecalIndex )
{
	EXPECT_FALSE( DecalShoot( -1, 0, g_Start, NULL, 0 ).bDraw );
	EXPECT_FALSE( DecalShoot(  5, -1, g_Start, NULL, 0 ).bDraw );
	EXPECT_FALSE( DecalShoot(  5, 0, NULL, NULL, 0 ).bDraw );
}

// (The spray's arguments are the managed PlayerSprays' since lane M1-C;
// PlayerSpraysTests holds player 0 and a negative entity refused.)


//=============================================================================//
// THE DISPATCH.
//=============================================================================//

namespace
{
	SourceSharpEffectData_t ZeroEffectData( void )
	{
		SourceSharpEffectData_t data;
		memset( &data, 0, sizeof( data ) );
		return data;
	}
}

//-----------------------------------------------------------------------------
// The struct's OWN SHAPE, checked from the native side.
//
// AbiParityTests::TheEffectDataStructMatchesTheHeader holds the managed mirror
// against the header, which is the half that can drift. This is the half that
// cannot be checked from C#: that the header's own struct is all four-byte
// fields with no padding anywhere, so the two really do lay out the same. A
// field of another width inserted here would satisfy the name-and-order check
// on the managed side and shift every byte after it.
//-----------------------------------------------------------------------------
TEST( TeBackendDispatch, TheEffectDataStructIsFlatFourByteFields )
{
	// 7 vectors and 14 scalars, all 4 bytes.
	EXPECT_EQ( (size_t)( ( 7 * 3 + 14 ) * 4 ), sizeof( SourceSharpEffectData_t ) );

	SourceSharpEffectData_t data = ZeroEffectData();

	// And the last field really is the last: writing it must not reach past the
	// struct, which a wrongly sized member above it would make possible.
	data.ControlPointOffsetXYZ[2] = 1.0f;
	EXPECT_FLOAT_EQ( 1.0f, data.ControlPointOffsetXYZ[2] );
}

//=============================================================================//
// THE SHATTERED PANE (C-Phase 3, lane CE) left native with lane c5-particles:
// the decode's refusals (no shard size, less than one shard across, more than
// 4,096 shards) and the grid are ShardEffects.ShatterSurface's, held by
// ShardsOracleTests against the recorded stock panes; the colours and vectors
// are decoded by ClientTeShatterSurface and held by TempEntClientCeTests.
//=============================================================================//

//=============================================================================//
// C-PHASE 3 LANE CX. The four capabilities the last three client decal halves
// were blocked on.
//
// ONE OF THE FOUR HAS NO ARGUMENTS AT ALL and that is recorded here rather than
// left as an absence: ClientIsLowViolence takes void and returns an int, so
// there is no order to transpose and nothing for this file to hold. The other
// three each have a decode, and two of them have one that is easy to get wrong:
//
//   * ClientTraceLine has TEN parameters, SEVEN of them out pointers, and any
//     of the seven may be NULL. `fraction` and `hitbox` are both "the answer"
//     to a caller that has one, and hitbox / entity index / surface props /
//     surface flags are FOUR ADJACENT INTS -- a decal or an impact written from
//     the wrong one lands somewhere plausible. So WriteTraceOuts is exercised
//     with a value in every field that could only have come from that field,
//     and then again with every pointer NULL. Two of the seven have no caller
//     yet (the surface pair, carried for ledger rows C4 and C5), which makes
//     this file the only thing standing between them and a silent transposition
//     on the day one arrives.
//
//   * ClientDecalNameFromIndex copies a name into a caller's buffer, which is
//     the one place on this ABI where a length is computed rather than passed
//     through. An empty name and a name that does not fit both have to come
//     back as something a caller can tell apart from a name.
//=============================================================================//

TEST( TeBackendStaticPropDecal, CarriesBothPointsAndBothIndices )
{
	const StaticPropDecalRequest req = DecalStaticProp( g_Start, g_End,
	                                                    /*prop*/ 41, /*decal*/ 7 );

	EXPECT_TRUE( req.bDraw );
	ExpectVec( req.vecStart, 11.0f, 12.0f, 13.0f );
	ExpectVec( req.vecEnd,   21.0f, 22.0f, 23.0f );
	EXPECT_EQ( 41, req.nStaticPropIndex );
	EXPECT_EQ(  7, req.nDecalIndex );
}

//-----------------------------------------------------------------------------
// START AND END ARE NOT INTERCHANGEABLE. AddDecalToStaticProp takes a ray and
// projects the decal along it, so a request with them swapped draws on the far
// face. Distinct coordinates in both, asserted in both places.
//-----------------------------------------------------------------------------
TEST( TeBackendStaticPropDecal, DoesNotSwapTheRayEnds )
{
	const StaticPropDecalRequest forwards = DecalStaticProp( g_Start, g_End, 0, 0 );
	const StaticPropDecalRequest backwards = DecalStaticProp( g_End, g_Start, 0, 0 );

	EXPECT_FLOAT_EQ( 11.0f, forwards.vecStart.x );
	EXPECT_FLOAT_EQ( 21.0f, forwards.vecEnd.x );
	EXPECT_FLOAT_EQ( 21.0f, backwards.vecStart.x );
	EXPECT_FLOAT_EQ( 11.0f, backwards.vecEnd.x );
}

//-----------------------------------------------------------------------------
// A NEGATIVE PROP INDEX IS THE FAILURE THIS REFUSAL EXISTS FOR, and it is not
// hypothetical. The caller passes stock's `hitbox - 1`, so a caller that
// reached this without testing `hitbox != 0` first arrives with -1 -- which
// indexes the engine's static prop array from below zero. A prop index of ZERO
// is perfectly ordinary and must still draw.
//-----------------------------------------------------------------------------
TEST( TeBackendStaticPropDecal, RefusesANegativeIndexAndDrawsPropZero )
{
	EXPECT_TRUE ( DecalStaticProp( g_Start, g_End,  0, 0 ).bDraw );
	EXPECT_FALSE( DecalStaticProp( g_Start, g_End, -1, 0 ).bDraw );
	EXPECT_FALSE( DecalStaticProp( g_Start, g_End,  0, -1 ).bDraw );
	EXPECT_FALSE( DecalStaticProp( NULL,    g_End,  0, 0 ).bDraw );
	EXPECT_FALSE( DecalStaticProp( g_Start, NULL,   0, 0 ).bDraw );
}

TEST( TeBackendClientTrace, CarriesBothPointsAndTheMask )
{
	const TraceLineRequest req = TraceLineArgs( g_Start, g_End, SOURCESHARP_TRACE_ALL );

	EXPECT_TRUE( req.bTrace );
	ExpectVec( req.vecStart, 11.0f, 12.0f, 13.0f );
	ExpectVec( req.vecEnd,   21.0f, 22.0f, 23.0f );
	EXPECT_EQ( (int)SOURCESHARP_TRACE_ALL, req.nMask );
}

//-----------------------------------------------------------------------------
// THE MASK IS CARRIED, NOT TRANSLATED, and each of the four is distinct. The
// projected decal needs SOURCESHARP_TRACE_ALL specifically: stock traces
// MASK_ALL, and a decal projected through MASK_SHOT lands on a different
// surface. A request that quietly answered BRUSHES for a mask it did not know
// is exactly what the server arm would have done before this value was mapped
// there too.
//-----------------------------------------------------------------------------
TEST( TeBackendClientTrace, TellsTheFourMasksApart )
{
	EXPECT_EQ( 0, TraceLineArgs( g_Start, g_End, SOURCESHARP_TRACE_BRUSHES ).nMask );
	EXPECT_EQ( 1, TraceLineArgs( g_Start, g_End, SOURCESHARP_TRACE_SHOT ).nMask );
	EXPECT_EQ( 2, TraceLineArgs( g_Start, g_End, SOURCESHARP_TRACE_BRUSHES_AND_WATER ).nMask );
	EXPECT_EQ( 3, TraceLineArgs( g_Start, g_End, SOURCESHARP_TRACE_ALL ).nMask );
}

TEST( TeBackendClientTrace, RefusesEitherPointMissing )
{
	EXPECT_FALSE( TraceLineArgs( NULL,    g_End, SOURCESHARP_TRACE_ALL ).bTrace );
	EXPECT_FALSE( TraceLineArgs( g_Start, NULL,  SOURCESHARP_TRACE_ALL ).bTrace );
	EXPECT_FALSE( TraceLineArgs( NULL,    NULL,  SOURCESHARP_TRACE_ALL ).bTrace );
}

//-----------------------------------------------------------------------------
// FIVE OUT POINTERS, FIVE VALUES THAT COULD ONLY HAVE COME FROM ONE FIELD.
//
// This is the assertion the whole slot's shape rests on. Every number below is
// distinct from every other, so the test agrees with exactly one arrangement --
// the "do not compare a computation with itself" rule this file opens with,
// applied to outputs instead of inputs.
//-----------------------------------------------------------------------------
TEST( TeBackendTraceOuts, EachFieldLandsInItsOwnPointer )
{
	TraceAnswer ans;
	ans.bHit          = true;
	ans.flFraction    = 0.25f;
	ans.vecEnd.x      = 41.0f; ans.vecEnd.y    = 42.0f; ans.vecEnd.z    = 43.0f;
	ans.vecNormal.x   = 51.0f; ans.vecNormal.y = 52.0f; ans.vecNormal.z = 53.0f;
	ans.nHitbox       = 61;
	ans.nEntityIndex  = 71;
	ans.nSurfaceProps = 81;
	ans.nSurfaceFlags = 91;

	float flFraction = -1.0f;
	float end[3]     = { -1.0f, -1.0f, -1.0f };
	float normal[3]  = { -1.0f, -1.0f, -1.0f };
	int   nHitbox    = -1;
	int   nEntity    = -1;
	int   nProps     = -1;
	int   nFlags     = -1;

	const int nHit = WriteTraceOuts( ans, &flFraction, end, normal, &nHitbox, &nEntity,
	                                 &nProps, &nFlags );

	EXPECT_EQ( 1, nHit );
	EXPECT_FLOAT_EQ( 0.25f, flFraction );
	EXPECT_FLOAT_EQ( 41.0f, end[0] );
	EXPECT_FLOAT_EQ( 42.0f, end[1] );
	EXPECT_FLOAT_EQ( 43.0f, end[2] );
	EXPECT_FLOAT_EQ( 51.0f, normal[0] );
	EXPECT_FLOAT_EQ( 52.0f, normal[1] );
	EXPECT_FLOAT_EQ( 53.0f, normal[2] );

	// The four adjacent ints, each a number only its own field could have put
	// there. This is the assertion the widened slot rests on.
	EXPECT_EQ( 61, nHitbox );
	EXPECT_EQ( 71, nEntity );
	EXPECT_EQ( 81, nProps );
	EXPECT_EQ( 91, nFlags );
}

//-----------------------------------------------------------------------------
// EVERY OUT MAY BE NULL, which is the server arm's rule and this one's. A
// caller asking only "did it arrive" passes none of them, and must still get
// the return value rather than a fault.
//-----------------------------------------------------------------------------
TEST( TeBackendTraceOuts, EveryPointerMayBeNull )
{
	TraceAnswer ans = TraceAnswerMiss( VecZero() );
	ans.bHit = true;

	EXPECT_EQ( 1, WriteTraceOuts( ans, NULL, NULL, NULL, NULL, NULL, NULL, NULL ) );

	ans.bHit = false;
	EXPECT_EQ( 0, WriteTraceOuts( ans, NULL, NULL, NULL, NULL, NULL, NULL, NULL ) );
}

//-----------------------------------------------------------------------------
// A MISS ENDS WHERE IT WAS AIMED, with fraction 1 and no hitbox. The projected
// decal's first test is `fraction == 1.0f` and it returns on it, so an answer
// that reported 0 for a trace that hit nothing would draw a decal at the
// muzzle. -1 for the entity, because nothing is not entity zero -- zero is the
// world.
//-----------------------------------------------------------------------------
TEST( TeBackendTraceOuts, AMissEndsWhereItWasAimed )
{
	Vec3 aim;
	aim.x = 81.0f; aim.y = 82.0f; aim.z = 83.0f;

	const TraceAnswer ans = TraceAnswerMiss( aim );

	EXPECT_FALSE( ans.bHit );
	EXPECT_FLOAT_EQ( 1.0f, ans.flFraction );
	ExpectVec( ans.vecEnd, 81.0f, 82.0f, 83.0f );
	EXPECT_EQ(  0, ans.nHitbox );
	EXPECT_EQ( -1, ans.nEntityIndex );

	// -1 rather than 0 for the surface, because 0 is a REAL surfaceprops index
	// (the default material) and a caller feeding it to
	// SurfacePropGameMaterial would be told what the default is made of rather
	// than that nothing was hit. The flags are 0, which is honest: a miss has
	// no SURF_ bits.
	EXPECT_EQ( -1, ans.nSurfaceProps );
	EXPECT_EQ(  0, ans.nSurfaceFlags );
}

//-----------------------------------------------------------------------------
// The decal name, into a caller's buffer.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalName, CopiesTheNameAndReportsItsLength )
{
	char buf[ 32 ];
	memset( buf, 'Z', sizeof( buf ) );

	EXPECT_EQ( 9, CopyName( "Blood_Big", buf, (int)sizeof( buf ) ) );
	EXPECT_STREQ( "Blood_Big", buf );
}

//-----------------------------------------------------------------------------
// AN EMPTY NAME IS ZERO, NOT AN EMPTY NAME. CDecalEmitterSystem answers "" for
// an index it does not hold (decals.cpp:161), and the caller's next step is a
// substring test for "blood" -- which "" would pass for "does this name contain
// nothing". The length is what says the lookup failed.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalName, AnEmptyNameIsALengthOfZero )
{
	char buf[ 32 ];
	memset( buf, 'Z', sizeof( buf ) );

	EXPECT_EQ( 0, CopyName( "", buf, (int)sizeof( buf ) ) );
	EXPECT_STREQ( "", buf );

	memset( buf, 'Z', sizeof( buf ) );
	EXPECT_EQ( 0, CopyName( NULL, buf, (int)sizeof( buf ) ) );
	EXPECT_STREQ( "", buf );
}

//-----------------------------------------------------------------------------
// TRUNCATION REPORTS WHAT FITTED, and the buffer is always terminated. A name
// longer than the buffer must not run off the end and must not come back
// claiming its full length -- a caller comparing the answer with strlen can
// tell it was cut.
//-----------------------------------------------------------------------------
TEST( TeBackendDecalName, TruncatesAndStillTerminates )
{
	char buf[ 5 ];
	memset( buf, 'Z', sizeof( buf ) );

	EXPECT_EQ( 4, CopyName( "Blood_Big", buf, (int)sizeof( buf ) ) );
	EXPECT_STREQ( "Bloo", buf );
}

TEST( TeBackendDecalName, RefusesNoBufferAtAll )
{
	char buf[ 4 ];
	memset( buf, 'Z', sizeof( buf ) );

	EXPECT_EQ( 0, CopyName( "Blood_Big", NULL, 32 ) );
	EXPECT_EQ( 0, CopyName( "Blood_Big", buf, 0 ) );
	EXPECT_EQ( 0, CopyName( "Blood_Big", buf, -1 ) );

	// Untouched: a zero size means the buffer may not be written at all, not
	// that it may be terminated.
	EXPECT_EQ( 'Z', buf[0] );
}

//=============================================================================//
// C-PHASE 3 LANE DECALS. Behaviour ledger rows C2 and C3 -- the two arms of
// C_BaseEntity::AddDecal, and the bloat that sits above both of them.
//
// THREE THINGS HERE COULD BE WRONG AND STILL DRAW SOMETHING:
//
//   * ENTITY 0 REFUSED. It is the world, it is a brush model, and it is row
//     C3's ORDINARY case -- every unnamed info_projecteddecal on map geometry.
//     A "the index must be positive" guard written out of habit deletes that
//     case entirely and leaves the static prop arm working, which looks like a
//     port that landed.
//   * THE BLOAT APPLIED TO THE ENDPOINT rather than to the delta, or applied
//     to both ends, or not applied at all (managed's since lane M1-C, which
//     hands the delta over). All three still produce a segment through roughly
//     the right place.
//   * THE CENTRE AND THE SEGMENT'S END CROSSED. They are the same vector at
//     c_te_decal.cpp's call site and different vectors at
//     c_te_projecteddecal.cpp's, so a test written against the first alone
//     cannot see it. g_Centre is distinct from both here.
//=============================================================================//

// Since lane M1-C the third argument is the ray's DELTA, already lengthened by
// managed (ClientEntityDecal.StudioRayDelta); g_End stands in for one here.
TEST( TeBackendStudioDecal, CarriesTheEntityTheStartTheDeltaAndTheDecal )
{
	const StudioDecalRequest req = DecalStudioModel( /*entity*/ 17, g_Start, g_End,
	                                                 /*decal*/ 7 );

	EXPECT_TRUE( req.bDraw );
	EXPECT_EQ( 17, req.nEntity );
	ExpectVec( req.vecStart, 11.0f, 12.0f, 13.0f );
	ExpectVec( req.vecDelta, 21.0f, 22.0f, 23.0f );
	EXPECT_EQ( 7, req.nDecalIndex );
}

//-----------------------------------------------------------------------------
// THE START AND THE DELTA ARE NOT INTERCHANGEABLE: the decal is projected from
// the start along the delta, so a swapped request is a different ray.
//-----------------------------------------------------------------------------
TEST( TeBackendStudioDecal, DoesNotSwapTheStartAndTheDelta )
{
	const StudioDecalRequest forwards  = DecalStudioModel( 1, g_Start, g_End, 0 );
	const StudioDecalRequest backwards = DecalStudioModel( 1, g_End, g_Start, 0 );

	EXPECT_FLOAT_EQ( 11.0f, forwards.vecStart.x );
	EXPECT_FLOAT_EQ( 21.0f, forwards.vecDelta.x );
	EXPECT_FLOAT_EQ( 21.0f, backwards.vecStart.x );
	EXPECT_FLOAT_EQ( 11.0f, backwards.vecDelta.x );
}

//-----------------------------------------------------------------------------
// ENTITY ZERO IS THE WORLD AND IS DRAWN. It reaches the BRUSH arm in practice,
// but which arm it reaches is ClientEntityModelType's answer -- a refusal here
// would be a second copy of that rule, disagreeing with it.
//-----------------------------------------------------------------------------
TEST( TeBackendStudioDecal, RefusesNegativesAndDrawsEntityZero )
{
	EXPECT_TRUE ( DecalStudioModel(  0, g_Start, g_End,  0 ).bDraw );
	EXPECT_FALSE( DecalStudioModel( -1, g_Start, g_End,  0 ).bDraw );
	EXPECT_FALSE( DecalStudioModel(  0, g_Start, g_End, -1 ).bDraw );
	EXPECT_FALSE( DecalStudioModel(  0, NULL,    g_End,  0 ).bDraw );
	EXPECT_FALSE( DecalStudioModel(  0, g_Start, NULL,   0 ).bDraw );
}

TEST( TeBackendBrushDecal, CarriesTheEntityTheCentreAndTheDecal )
{
	const BrushDecalRequest req = DecalBrushModel( /*entity*/ 0, g_Centre, /*decal*/ 31 );

	EXPECT_TRUE( req.bDraw );
	EXPECT_EQ( 0, req.nEntity );
	ExpectVec( req.vecCenter, 31.0f, 32.0f, 33.0f );
	EXPECT_EQ( 31, req.nDecalIndex );
}

//-----------------------------------------------------------------------------
// THE WORLD IS ENTITY ZERO AND MUST DRAW. This is the assertion that stands
// between row C3 and a port that closed it for static props only.
//-----------------------------------------------------------------------------
TEST( TeBackendBrushDecal, RefusesNegativesAndDrawsTheWorld )
{
	EXPECT_TRUE ( DecalBrushModel(  0, g_Centre,  0 ).bDraw );
	EXPECT_TRUE ( DecalBrushModel( 42, g_Centre,  0 ).bDraw );
	EXPECT_FALSE( DecalBrushModel( -1, g_Centre,  0 ).bDraw );
	EXPECT_FALSE( DecalBrushModel(  0, g_Centre, -1 ).bDraw );
	EXPECT_FALSE( DecalBrushModel(  0, NULL,      0 ).bDraw );
}

//-----------------------------------------------------------------------------
// THE BLOAT ON THE DELTA is managed's since lane M1-C: ClientEntityDecalTests'
// AddStudioDecal facts hold the (10,10,10) -> (11,11,11) arithmetic, the
// zero-length segment and the sign.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE PREDICTED SHOT'S CLIP (ClientClipRayToEntities): which named entity may
// stop it, and which answer wins.
//-----------------------------------------------------------------------------
TEST( ClientClipRayToEntities, OnlyASolidEntityMayStopTheShot )
{
	const int kSolidBsp = 1;          // SOLID_BSP
	const int kSolidVPhysics = 6;     // SOLID_VPHYSICS
	const int kTrigger = 0x0008;      // FSOLID_TRIGGER, which a trigger carries with NOT_SOLID

	EXPECT_TRUE( ClipCandidateIsSolid( kSolidBsp, 0 ) );
	EXPECT_TRUE( ClipCandidateIsSolid( kSolidVPhysics, kTrigger ) );
	EXPECT_FALSE( ClipCandidateIsSolid( kClipSolidNone, 0 ) );
	EXPECT_FALSE( ClipCandidateIsSolid( kSolidBsp, kClipNotSolid ) );
	EXPECT_FALSE( ClipCandidateIsSolid( kSolidBsp, kClipNotSolid | kTrigger ) );
}

TEST( ClientClipRayToEntities, TheNearestStopWinsAndAMissNever )
{
	EXPECT_TRUE( ClipIsNearer( 0.25f, false, 1.0f ) );
	EXPECT_TRUE( ClipIsNearer( 0.25f, false, 0.5f ) );
	EXPECT_FALSE( ClipIsNearer( 0.5f, false, 0.25f ) );
	EXPECT_FALSE( ClipIsNearer( 0.25f, false, 0.25f ) );   // a tie keeps the first
	EXPECT_FALSE( ClipIsNearer( 1.0f, false, 1.0f ) );     // a clip that did not stop
}

TEST( ClientClipRayToEntities, AShotThatBeginsInsideAnEntityStopsThere )
{
	EXPECT_TRUE( ClipIsNearer( 0.0f, true, 1.0f ) );
	EXPECT_FALSE( ClipIsNearer( 0.0f, true, 0.0f ) );
}
