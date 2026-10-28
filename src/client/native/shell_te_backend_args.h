//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Engine-free decoding of the temp entity backends' arguments into named fields, so the
// decode is testable without a running engine. Only the ABI header is included.
#pragma once

#include "sourcesharp.h"

namespace TeBackendArgs
{

// Three floats: this header has no mathlib.
struct Vec3
{
	float x;
	float y;
	float z;
};

inline Vec3 VecZero( void )
{
	Vec3 v;
	v.x = 0.0f;
	v.y = 0.0f;
	v.z = 0.0f;
	return v;
}

// A vector from the wire, or the origin when the pointer is absent, as stock reads a NULL
// endpoint. Callers that must not accept NULL check the pointer themselves.
inline Vec3 VecFrom( const float *p )
{
	if ( !p )
		return VecZero();

	Vec3 v;
	v.x = p[0];
	v.y = p[1];
	v.z = p[2];
	return v;
}

// The client's IEffects family. No PVS filter here: CEffectsClient applies its own inside
// each call.
struct EffectRequest
{
	bool  bDraw;

	Vec3  vecPosition;

	// A direction the caller may omit; only Sparks accepts none.
	bool  bHasDirection;
	Vec3  vecDirection;

	// Pitch-yaw-roll, for the muzzle flash.
	Vec3  vecAngles;

	int   nMagnitude;
	int   nTrailLength;
	int   nModelIndex;

	// Already tenfold for smoke, as both stock callers pass m_fScale * 10.0f.
	float flScale;
	float flFrameRate;

	// MUZZLEFLASH_TYPE_*.
	int   nType;
};

inline EffectRequest EffectRequestZero( void )
{
	EffectRequest req;

	req.bDraw         = false;
	req.vecPosition   = VecZero();
	req.bHasDirection = false;
	req.vecDirection  = VecZero();
	req.vecAngles     = VecZero();
	req.nMagnitude    = 0;
	req.nTrailLength  = 0;
	req.nModelIndex   = 0;
	req.flScale       = 0.0f;
	req.flFrameRate   = 0.0f;
	req.nType         = 0;

	return req;
}

// Electric sparks. The direction stays optional: a NULL is a spark with no preferred
// direction, not one aimed at a zero vector.
inline EffectRequest Sparks( const float *pPositionXYZ,
                             int nMagnitude, int nTrailLength,
                             const float *pDirectionXYZ )
{
	EffectRequest req = EffectRequestZero();

	if ( !pPositionXYZ )
		return req;

	req.bDraw         = true;
	req.vecPosition   = VecFrom( pPositionXYZ );
	req.nMagnitude    = nMagnitude;
	req.nTrailLength  = nTrailLength;
	req.bHasDirection = ( pDirectionXYZ != 0 );
	req.vecDirection  = VecFrom( pDirectionXYZ );

	return req;
}

// Ricochet and MetalSparks: a position and a direction.
inline EffectRequest PositionAndDirection( const float *pPositionXYZ,
                                           const float *pDirectionXYZ )
{
	EffectRequest req = EffectRequestZero();

	if ( !pPositionXYZ || !pDirectionXYZ )
		return req;

	req.bDraw         = true;
	req.vecPosition   = VecFrom( pPositionXYZ );
	req.bHasDirection = true;
	req.vecDirection  = VecFrom( pDirectionXYZ );

	return req;
}

inline EffectRequest Smoke( const float *pOriginXYZ, int nModelIndex,
                            float flScale, float flFrameRate )
{
	EffectRequest req = EffectRequestZero();

	if ( !pOriginXYZ )
		return req;

	req.bDraw       = true;
	req.vecPosition = VecFrom( pOriginXYZ );
	req.nModelIndex = nModelIndex;
	req.flScale     = flScale;
	req.flFrameRate = flFrameRate;

	return req;
}

// A muzzle flash. The angles are required.
inline EffectRequest MuzzleFlash( const float *pOriginXYZ, const float *pAnglesPYR,
                                  float flScale, int nType )
{
	EffectRequest req = EffectRequestZero();

	if ( !pOriginXYZ || !pAnglesPYR )
		return req;

	req.bDraw       = true;
	req.vecPosition = VecFrom( pOriginXYZ );
	req.vecAngles   = VecFrom( pAnglesPYR );
	req.flScale     = flScale;
	req.nType       = nType;

	return req;
}

struct DecalRequest
{
	bool  bDraw;

	int   nDecalIndex;

	// An entity index, resolved by the .cpp. Zero is the world and valid; negative is not.
	int   nEntity;

	Vec3  vecPosition;

	// The decal's S axis, optional. NULL and a zero vector are different arguments to
	// DecalShoot, so the flag is carried.
	bool  bHasSAxis;
	Vec3  vecSAxis;

	int   nFlags;
};

inline DecalRequest DecalRequestZero( void )
{
	DecalRequest req;

	req.bDraw       = false;
	req.nDecalIndex = -1;
	req.nEntity     = 0;
	req.vecPosition = VecZero();
	req.bHasSAxis   = false;
	req.vecSAxis    = VecZero();
	req.nFlags      = 0;

	return req;
}

// A decal on the world, a brush model or a static prop. A negative decal index (an unknown
// name's -1) or entity index is refused: DecalShoot would index below zero.
inline DecalRequest DecalShoot( int nDecalIndex, int nEntity,
                                const float *pPositionXYZ, const float *pSAxisXYZ,
                                int nFlags )
{
	DecalRequest req = DecalRequestZero();

	if ( !pPositionXYZ || nDecalIndex < 0 || nEntity < 0 )
		return req;

	req.bDraw       = true;
	req.nDecalIndex = nDecalIndex;
	req.nEntity     = nEntity;
	req.vecPosition = VecFrom( pPositionXYZ );
	req.bHasSAxis   = ( pSAxisXYZ != 0 );
	req.vecSAxis    = VecFrom( pSAxisXYZ );
	req.nFlags      = nFlags;

	return req;
}

// A decal on a static prop. nStaticPropIndex arrives already hitbox - 1, so a caller that
// skipped the hitbox != 0 test arrives with -1; refused.
struct StaticPropDecalRequest
{
	bool bDraw;

	Vec3 vecStart;
	Vec3 vecEnd;

	int  nStaticPropIndex;

	int  nDecalIndex;
};

inline StaticPropDecalRequest StaticPropDecalRequestZero( void )
{
	StaticPropDecalRequest req;

	req.bDraw            = false;
	req.vecStart         = VecZero();
	req.vecEnd           = VecZero();
	req.nStaticPropIndex = -1;
	req.nDecalIndex      = -1;

	return req;
}

inline StaticPropDecalRequest DecalStaticProp( const float *pStartXYZ, const float *pEndXYZ,
                                               int nStaticPropIndex, int nDecalIndex )
{
	StaticPropDecalRequest req = StaticPropDecalRequestZero();

	// Both points are dereferenced unchecked, and a negative index would underflow the array.
	if ( !pStartXYZ || !pEndXYZ || nStaticPropIndex < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw            = true;
	req.vecStart         = VecFrom( pStartXYZ );
	req.vecEnd           = VecFrom( pEndXYZ );
	req.nStaticPropIndex = nStaticPropIndex;
	req.nDecalIndex      = nDecalIndex;

	return req;
}

struct TraceLineRequest
{
	bool bTrace;

	Vec3 vecStart;
	Vec3 vecEnd;

	// One of SourceSharpTraceMask; this header cannot see the engine's MASK_* constants.
	int  nMask;
};

inline TraceLineRequest TraceLineRequestZero( void )
{
	TraceLineRequest req;

	req.bTrace   = false;
	req.vecStart = VecZero();
	req.vecEnd   = VecZero();
	req.nMask    = SOURCESHARP_TRACE_BRUSHES;

	return req;
}

inline TraceLineRequest TraceLineArgs( const float *pStartXYZ, const float *pEndXYZ, int nMask )
{
	TraceLineRequest req = TraceLineRequestZero();

	if ( !pStartXYZ || !pEndXYZ )
		return req;

	req.bTrace   = true;
	req.vecStart = VecFrom( pStartXYZ );
	req.vecEnd   = VecFrom( pEndXYZ );
	req.nMask    = nMask;

	return req;
}

// A trace's answer, unpacked so adjacent out values cannot trade places. Starting inside
// something counts as a hit, as stock's rule.
struct TraceAnswer
{
	bool  bHit;

	float flFraction;
	Vec3  vecEnd;
	Vec3  vecNormal;

	// Non-zero is a static prop, numbered from one; zero is world, brush or a hitbox-less entity.
	int   nHitbox;

	// Answers for both hierarchies. -1 for nothing.
	int   nEntityIndex;

	// csurface_t's shorts, widened to int for the boundary.
	int   nSurfaceProps;
	int   nSurfaceFlags;
};

inline TraceAnswer TraceAnswerMiss( const Vec3 &vecEnd )
{
	TraceAnswer ans;

	// A miss ends where it was aimed with fraction one.
	ans.bHit          = false;
	ans.flFraction    = 1.0f;
	ans.vecEnd        = vecEnd;
	ans.vecNormal     = VecZero();
	ans.nHitbox       = 0;
	ans.nEntityIndex  = -1;
	ans.nSurfaceProps = -1;
	ans.nSurfaceFlags = 0;

	return ans;
}

// The answer into whichever out pointers the caller supplied; every one may be NULL.
inline int WriteTraceOuts( const TraceAnswer &ans,
                           float *pOutFraction, float *pOutEndXYZ, float *pOutNormalXYZ,
                           int *pOutHitbox, int *pOutEntityIndex,
                           int *pOutSurfaceProps, int *pOutSurfaceFlags )
{
	if ( pOutFraction )
		*pOutFraction = ans.flFraction;

	if ( pOutEndXYZ )
	{
		pOutEndXYZ[0] = ans.vecEnd.x;
		pOutEndXYZ[1] = ans.vecEnd.y;
		pOutEndXYZ[2] = ans.vecEnd.z;
	}

	if ( pOutNormalXYZ )
	{
		pOutNormalXYZ[0] = ans.vecNormal.x;
		pOutNormalXYZ[1] = ans.vecNormal.y;
		pOutNormalXYZ[2] = ans.vecNormal.z;
	}

	if ( pOutHitbox )
		*pOutHitbox = ans.nHitbox;

	if ( pOutEntityIndex )
		*pOutEntityIndex = ans.nEntityIndex;

	if ( pOutSurfaceProps )
		*pOutSurfaceProps = ans.nSurfaceProps;

	if ( pOutSurfaceFlags )
		*pOutSurfaceFlags = ans.nSurfaceFlags;

	return ans.bHit ? 1 : 0;
}

// A name into a caller's buffer. Returns the length written without the terminator, 0
// for nothing; truncation shows in the length. An empty name is 0, not an empty string.
inline int CopyName( const char *pName, char *pOut, int nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return 0;

	pOut[0] = '\0';

	if ( !pName || !pName[0] )
		return 0;

	int n = 0;

	while ( pName[n] && n < nOutSize - 1 )
	{
		pOut[n] = pName[n];
		n++;
	}

	pOut[n] = '\0';

	return n;
}

// The studio and brush arms of C_BaseEntity::AddDecal. Entity 0 is the world and must be
// accepted. The studio arm takes the ray's start and delta; the brush arm takes a centre,
// which is not always the segment's end.

struct StudioDecalRequest
{
	bool bDraw;

	int  nEntity;

	Vec3 vecStart;
	Vec3 vecDelta;

	int  nDecalIndex;
};

inline StudioDecalRequest StudioDecalRequestZero( void )
{
	StudioDecalRequest req;

	req.bDraw       = false;
	req.nEntity     = -1;
	req.vecStart    = VecZero();
	req.vecDelta    = VecZero();
	req.nDecalIndex = -1;

	return req;
}

inline StudioDecalRequest DecalStudioModel( int nEntity,
                                            const float *pRayStartXYZ, const float *pRayDeltaXYZ,
                                            int nDecalIndex )
{
	StudioDecalRequest req = StudioDecalRequestZero();

	// Both points are dereferenced unchecked and a negative decal index would underflow.
	// Entity 0 is allowed; which arm the world reaches is decided elsewhere.
	if ( !pRayStartXYZ || !pRayDeltaXYZ || nEntity < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw       = true;
	req.nEntity     = nEntity;
	req.vecStart    = VecFrom( pRayStartXYZ );
	req.vecDelta    = VecFrom( pRayDeltaXYZ );
	req.nDecalIndex = nDecalIndex;

	return req;
}

struct BrushDecalRequest
{
	bool bDraw;

	int  nEntity;

	// AddDecal's third argument, not always its second.
	Vec3 vecCenter;

	int  nDecalIndex;
};

inline BrushDecalRequest BrushDecalRequestZero( void )
{
	BrushDecalRequest req;

	req.bDraw       = false;
	req.nEntity     = -1;
	req.vecCenter   = VecZero();
	req.nDecalIndex = -1;

	return req;
}

inline BrushDecalRequest DecalBrushModel( int nEntity, const float *pDecalCenterXYZ,
                                          int nDecalIndex )
{
	BrushDecalRequest req = BrushDecalRequestZero();

	// Entity 0 is the world and is drawn; do not tighten this to > 0.
	if ( !pDecalCenterXYZ || nEntity < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw       = true;
	req.nEntity     = nEntity;
	req.vecCenter   = VecFrom( pDecalCenterXYZ );
	req.nDecalIndex = nDecalIndex;

	return req;
}

// Which entities may stop a predicted shot: any that is solid. ClipRayToEntity does not
// skip non-solid ones itself. The values are const.h's; shell_worldquery_client.cpp
// asserts them.
const int kClipSolidNone = 0;        // SOLID_NONE
const int kClipNotSolid  = 0x0004;   // FSOLID_NOT_SOLID

inline bool ClipCandidateIsSolid( int nSolidType, int nSolidFlags )
{
	return nSolidType != kClipSolidNone && ( nSolidFlags & kClipNotSolid ) == 0;
}

// A clip that stopped, or began inside the entity, and is strictly nearer than the best.
inline bool ClipIsNearer( float flFraction, bool bStartSolid, float flBestFraction )
{
	return ( flFraction < 1.0f || bStartSolid ) && flFraction < flBestFraction;
}

} // namespace TeBackendArgs
