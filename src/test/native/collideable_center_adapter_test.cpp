//========= SourceSharp — the ICollideable adapter, and the point it is not ===//
//
// collideable_center_test pins the ARITHMETIC (collideable_center_math.h), with
// no engine and no mathlib in it. This binary pins the ADAPTER one level up —
// UTIL_CollideableWorldSpaceCenter in game/shared/collideable_center.h — which
// is the part that decides WHICH of an ICollideable's answers the arithmetic
// gets, and it needs real Vectors, a real QAngle and a real matrix3x4_t to do
// that. Hence a second binary, linking mathlib, rather than more cases in the
// first.
//
// WHY IT EXISTS NOW. AnyEntity_t::BodyTarget (game/server/anyentity.h) used to
// aim a blast's line-of-sight trace at the midpoint of an entity's
// WorldSpaceSurroundingBounds, and now asks this adapter instead. Both are "a
// centre", which is exactly why the swap needs a test that can fail: if the two
// quantities were the same number, the change would be a no-op and every
// assertion about it would pass whether or not the code was there.
//
// So the load-bearing case here is BoundsMidpointIsNotTheObbCentreWhenRotated,
// which asserts they DIFFER on the shape CStandaloneEntity actually has:
//
//   WorldSpaceSurroundingBounds() = m_vecOrigin + m_vecMins ..
//                                   m_vecOrigin + m_vecMaxs   (never rotated)
//   CollisionToWorldTransform()   = AngleMatrix( m_angRotation, m_vecOrigin )
//
// — shell_standalone.h:270/287-289 and shell_standalone_base.cpp's
// SetAbsTransform and WorldSpaceSurroundingBounds.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"

#include "mathlib_test_env.h"

// ICollideable.h forward-declares SolidType_t as a bare `enum SolidType_t;`,
// which only compiles where const.h has already defined it — cbase.h reaches
// const.h first in every real translation unit. This binary has no cbase.h, so
// it says so itself.
#include "const.h"

#include "../game/shared/collideable_center.h"

namespace
{

//-----------------------------------------------------------------------------
// An ICollideable that answers only the five methods the adapter reads, and
// traps the rest.
//
// The traps are the point of writing it out rather than reaching for a mock:
// the adapter's contract is that it asks OBBMins/OBBMaxs (NOT the PreScaled
// pair), the COLLISION origin, the COLLISION angles, and the transform only
// when the angles are non-zero. Every method outside that set aborts the test
// if it is called, so a "simplification" that starts reading the model, the
// solid type or the pre-scaled bounds goes red instead of silently answering a
// different number.
//-----------------------------------------------------------------------------
class CStubCollideable : public ICollideable
{
public:
	CStubCollideable( const Vector &vecMins, const Vector &vecMaxs, const Vector &vecOrigin, const QAngle &angles )
		: m_vecMins( vecMins )
		, m_vecMaxs( vecMaxs )
		, m_vecOrigin( vecOrigin )
		, m_angRotation( angles )
		, m_nTransformReads( 0 )
	{
		AngleMatrix( m_angRotation, m_vecOrigin, m_Transform );

		// The pre-scaled pair is deliberately POISONED rather than mirrored:
		// mirroring it would make "the adapter reads the wrong one" invisible.
		m_vecPoison.Init( -8192.5f, -8192.5f, -8192.5f );
	}

	// --- what the adapter is allowed to ask -------------------------------
	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }

	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }

	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return m_vecOrigin; }

	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return m_angRotation; }

	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE
	{
		m_nTransformReads++;
		return m_Transform;
	}

	int TransformReads() const { return m_nTransformReads; }

	// The surrounding bounds are NOT part of the adapter's contract — they are
	// here so a test can compute the quantity BodyTarget used to use, on the
	// same stub, and show the two are different points.
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		if ( pMins )
			*pMins = m_vecOrigin + m_vecMins;
		if ( pMaxs )
			*pMaxs = m_vecOrigin + m_vecMaxs;
	}

	// --- the traps ---------------------------------------------------------
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE
	{
		ADD_FAILURE() << "OBBMinsPreScaled";
		return m_vecPoison;
	}

	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE
	{
		ADD_FAILURE() << "OBBMaxsPreScaled";
		return m_vecPoison;
	}

	virtual IHandleEntity *GetEntityHandle() OVERRIDE
	{
		ADD_FAILURE() << "GetEntityHandle";
		return NULL;
	}

	virtual void WorldSpaceTriggerBounds( Vector *, Vector * ) const OVERRIDE
	{
		ADD_FAILURE() << "WorldSpaceTriggerBounds";
	}

	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE
	{
		ADD_FAILURE() << "TestCollision";
		return false;
	}

	virtual bool TestHitboxes( const Ray_t &, unsigned int, trace_t & ) OVERRIDE
	{
		ADD_FAILURE() << "TestHitboxes";
		return false;
	}

	virtual int GetCollisionModelIndex() OVERRIDE
	{
		ADD_FAILURE() << "GetCollisionModelIndex";
		return -1;
	}

	virtual const model_t *GetCollisionModel() OVERRIDE
	{
		ADD_FAILURE() << "GetCollisionModel";
		return NULL;
	}

	virtual SolidType_t GetSolid() const OVERRIDE
	{
		ADD_FAILURE() << "GetSolid";
		return (SolidType_t)0;
	}

	virtual int GetSolidFlags() const OVERRIDE
	{
		ADD_FAILURE() << "GetSolidFlags";
		return 0;
	}

	virtual IClientUnknown *GetIClientUnknown() OVERRIDE
	{
		ADD_FAILURE() << "GetIClientUnknown";
		return NULL;
	}

	virtual int GetCollisionGroup() const OVERRIDE
	{
		ADD_FAILURE() << "GetCollisionGroup";
		return 0;
	}

	virtual bool ShouldTouchTrigger( int ) const OVERRIDE
	{
		ADD_FAILURE() << "ShouldTouchTrigger";
		return false;
	}

	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE
	{
		ADD_FAILURE() << "GetRootParentToWorldTransform";
		return NULL;
	}

private:
	Vector m_vecMins;
	Vector m_vecMaxs;
	Vector m_vecOrigin;
	QAngle m_angRotation;
	matrix3x4_t m_Transform;
	Vector m_vecPoison;
	mutable int m_nTransformReads;
};

// An OBB that is NOT centred on the entity's origin. Every claim below needs
// that: a symmetric box has its centre at the origin, so rotating it moves
// nothing and the two quantities agree by accident.
const Vector kMins( -8.0f, -4.0f, 0.0f );
const Vector kMaxs( 40.0f, 4.0f, 72.0f );
const Vector kOrigin( 100.0f, -250.0f, 32.0f );

//-----------------------------------------------------------------------------
// Unrotated: the adapter takes the add arm, and must not read the transform at
// all. (CCollisionProperty's own CollisionToWorldTransform hands back one of
// four rotating static buffers, so "reads it anyway and ignores it" is not
// harmless in the general case; the add arm exists precisely so it is not read.)
//-----------------------------------------------------------------------------
TEST( CollideableCenterAdapter, UnrotatedIsMidpointPlusOriginAndReadsNoTransform )
{
	CStubCollideable stub( kMins, kMaxs, kOrigin, vec3_angle );

	const Vector vecCenter = UTIL_CollideableWorldSpaceCenter( &stub );

	const Vector vecExpected = kOrigin + ( kMins + kMaxs ) * 0.5f;

	EXPECT_FLOAT_EQ( vecExpected.x, vecCenter.x );
	EXPECT_FLOAT_EQ( vecExpected.y, vecCenter.y );
	EXPECT_FLOAT_EQ( vecExpected.z, vecCenter.z );

	EXPECT_EQ( 0, stub.TransformReads() );
}

//-----------------------------------------------------------------------------
// Rotated: the transform arm, read once, and the answer is the OBB centre
// carried through it — computed here by hand rather than by the header.
//-----------------------------------------------------------------------------
TEST( CollideableCenterAdapter, RotatedCarriesTheObbCentreThroughTheTransform )
{
	const QAngle angles( 0.0f, 90.0f, 0.0f );
	CStubCollideable stub( kMins, kMaxs, kOrigin, angles );

	const Vector vecCenter = UTIL_CollideableWorldSpaceCenter( &stub );

	matrix3x4_t matExpected;
	AngleMatrix( angles, kOrigin, matExpected );

	Vector vecExpected;
	VectorTransform( ( kMins + kMaxs ) * 0.5f, matExpected, vecExpected );

	EXPECT_NEAR( vecExpected.x, vecCenter.x, 0.001f );
	EXPECT_NEAR( vecExpected.y, vecCenter.y, 0.001f );
	EXPECT_NEAR( vecExpected.z, vecCenter.z, 0.001f );

	EXPECT_EQ( 1, stub.TransformReads() );
}

//-----------------------------------------------------------------------------
// THE CHECK THE SWAP RESTS ON.
//
// AnyEntity_t::BodyTarget used the midpoint of WorldSpaceSurroundingBounds and
// now uses this adapter. If those were the same point the change would be a
// no-op and nothing above could tell. On CStandaloneEntity's shape — surrounding
// bounds that are never rotated, a transform that is — they are not, and this
// says by how much.
//-----------------------------------------------------------------------------
TEST( CollideableCenterAdapter, BoundsMidpointIsNotTheObbCentreWhenRotated )
{
	const QAngle angles( 0.0f, 90.0f, 0.0f );
	CStubCollideable stub( kMins, kMaxs, kOrigin, angles );

	Vector vecMins, vecMaxs;
	stub.WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );
	const Vector vecOldAnswer = ( vecMins + vecMaxs ) * 0.5f;

	const Vector vecNewAnswer = UTIL_CollideableWorldSpaceCenter( &stub );

	EXPECT_GT( ( vecNewAnswer - vecOldAnswer ).Length(), 1.0f )
		<< "old " << vecOldAnswer.x << "," << vecOldAnswer.y << "," << vecOldAnswer.z << "  new " << vecNewAnswer.x
		<< "," << vecNewAnswer.y << "," << vecNewAnswer.z;
}

//-----------------------------------------------------------------------------
// And the other half of that: when the entity is NOT rotated, the two agree
// exactly — so the divergence above is about the rotation and not about the
// adapter computing some third thing.
//-----------------------------------------------------------------------------
TEST( CollideableCenterAdapter, BoundsMidpointIsTheObbCentreWhenUnrotated )
{
	CStubCollideable stub( kMins, kMaxs, kOrigin, vec3_angle );

	Vector vecMins, vecMaxs;
	stub.WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );
	const Vector vecBoundsMid = ( vecMins + vecMaxs ) * 0.5f;

	const Vector vecCenter = UTIL_CollideableWorldSpaceCenter( &stub );

	EXPECT_FLOAT_EQ( vecBoundsMid.x, vecCenter.x );
	EXPECT_FLOAT_EQ( vecBoundsMid.y, vecCenter.y );
	EXPECT_FLOAT_EQ( vecBoundsMid.z, vecCenter.z );
}

} // namespace
