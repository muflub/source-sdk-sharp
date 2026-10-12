//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the temp entity backends' ARGUMENTS ================//
//
// plan_tmp.md Part 4, C-Phase 2. The half of shell_te_backends_client.cpp that
// can be checked without a running Source.
//
// WHAT THIS EXISTS TO CATCH, and it is one thing rather than a family. A
// backend slot is a decode of arguments followed by one call into drawing code
// that already exists. The call cannot be tested here — beams, g_pEffects and
// effects are the client DLL's globals and pulling any of them in would drag
// the whole engine into a gtest. The DECODE can, and the decode is where the
// mistake lives:
//
//   * (The six beam decoders left with the managed beam system; their
//     argument order is pinned in TempEntBeamClientTests.)
//   * A pointer argument that may be NULL (a spark's direction, a decal's S
//     axis, an ent-point beam's endpoint) and one that may not (a beam's two
//     points) look identical at the call site.
//
// None of those is visible in a build, in a dump, or in a live join short of
// looking at the beam. So the arguments are unpacked into NAMED FIELDS here,
// the unpacking is what the tests read, and the .cpp's job shrinks to one
// assignment per line into the engine's own call.
//
// NO ENGINE HEADERS, on purpose, and it is the same argument tempent_schema.h
// and generic_payload.h make. The only include is the ABI header, which is pure
// C and knows nothing about Source. See sourcesharp/test/te_backend_args_test.cpp.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_TE_BACKEND_ARGS_H
#define SOURCESHARP_SHELL_TE_BACKEND_ARGS_H
#ifdef _WIN32
#pragma once
#endif

#include "sourcesharp.h"

namespace TeBackendArgs
{

//-----------------------------------------------------------------------------
// Three floats, which is what every vector on this boundary is. Deliberately
// NOT Vector: this header has no mathlib, and the .cpp converts once at the
// point where the engine wants one.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A vector from the wire, or the ORIGIN when the pointer is absent.
//
// vec3_origin is what stock reads a NULL endpoint as -- c_te_beamentpoint.cpp's
// `pStart ? *pStart : vec3_origin`, by way of view_beams.cpp -- so the fallback
// is stock's, not an invention. Callers that must NOT accept a NULL check the
// pointer themselves and refuse; see BeamPoints below.
//-----------------------------------------------------------------------------
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

//=============================================================================//
// THE CLIENT'S IEffects FAMILY.
//
// Five effects that differ mostly in which two or three of the same fields they
// use, which is exactly the shape a shared struct is for: a decoder that filled
// nType for a spark, or nMagnitude for a muzzle flash, would be reading the
// wrong wire field and nothing else would say so.
//
// THE PVS FILTER IS NOT HERE and must not be added. CEffectsClient applies a
// CPVSFilter inside each of these calls and drops the effect if the local
// player cannot see it; a filter here would be a second copy of that decision,
// and lane C1 has already lost a reading to a PVS filter with no recipients
// sending nothing and printing nothing.
//=============================================================================//
struct EffectRequest
{
	bool  bDraw;

	Vec3  vecPosition;

	// A direction the caller MAY omit. Sparks takes a `const Vector *` and
	// passes it straight through; the rest require one.
	bool  bHasDirection;
	Vec3  vecDirection;

	// Pitch-yaw-roll, for the muzzle flash. Never a direction: the flash is
	// oriented, not aimed.
	Vec3  vecAngles;

	int   nMagnitude;
	int   nTrailLength;
	int   nModelIndex;

	// Already tenfold for smoke: both of stock's callers pass m_fScale * 10.0f
	// and say why in a comment.
	float flScale;
	float flFrameRate;

	// The muzzle flash kind (MUZZLEFLASH_TYPE_*). g_pEffects->MuzzleFlash
	// prints a line for anything it has no case for, which is stock's own
	// behaviour and is left alone.
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

//-----------------------------------------------------------------------------
// Electric sparks. c_te_sparks.cpp.
//
// THE DIRECTION IS OPTIONAL and stays optional. IEffects::Sparks declares it
// `const Vector *pvecDir = NULL` and FX_ElectricSpark reads the pointer, so a
// NULL is a spark with no preferred direction rather than a spark at the
// origin. Defaulting it to a zero vector HERE would be a different effect.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A position and a direction, which is the whole of both halves of
// c_te_armorricochet.cpp -- Ricochet (sparks AND a sound) and MetalSparks (the
// same sparks, no sound). One decoder because the arguments are one shape; the
// difference is which primitive the .cpp calls, and that difference is the
// whole reason there are two slots rather than a flag.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Smoke sprites. c_te_smoke.cpp.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A muzzle flash. c_te_muzzleflash.cpp.
//
// THE ANGLES ARE REQUIRED. g_pEffects->MuzzleFlash takes a `const QAngle &`,
// and an unoriented flash is not a thing the primitive can draw.
//-----------------------------------------------------------------------------
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

//=============================================================================//
// THE DECALS.
//=============================================================================//
struct DecalRequest
{
	bool  bDraw;

	int   nDecalIndex;

	// An edict index, resolved through cl_entitylist by the .cpp because
	// DecalShoot wants the entity's model_t*, which is not a thing that crosses
	// this boundary. Zero is the WORLD, which is what the world and BSP decals
	// ask for -- so zero is valid and negative is not.
	int   nEntity;

	Vec3  vecPosition;

	// The decal's S axis. Optional: a footprint has one, the world and BSP
	// decals pass NULL and let the engine pick. NULL and a zero vector are
	// DIFFERENT arguments to DecalShoot, so the flag is carried rather than
	// inferred from the value.
	bool  bHasSAxis;
	Vec3  vecSAxis;

	// FDECAL_PERMANENT for a BSP decal, zero for the others.
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

//-----------------------------------------------------------------------------
// A decal on the world, a brush model or a static prop. c_te_worlddecal.cpp,
// c_te_bspdecal.cpp and c_te_footprint.cpp.
//
// A NEGATIVE DECAL INDEX IS REFUSED and a negative entity index with it.
// Draw_DecalIndexFromName answers -1 for a name the engine does not know, and
// that -1 flows straight into m_nIndex on the wire for anything that built its
// index from a name; handing it to DecalShoot indexes the engine's decal array
// from below zero.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A player's spray logo. c_te_playerdecal.cpp.
//
// The decal index field is unused -- a spray's material is built from the
// player's own custom file, not looked up in the decal table -- and nPlayer
// rides in its place would be a lie. It is a separate argument.
//-----------------------------------------------------------------------------
struct PlayerDecalRequest
{
	bool bDraw;
	Vec3 vecPosition;
	int  nPlayer;
	int  nEntity;
};

inline PlayerDecalRequest PlayerDecalShoot( const float *pPositionXYZ,
                                            int nPlayer, int nEntity )
{
	PlayerDecalRequest req;

	req.bDraw       = false;
	req.vecPosition = VecZero();
	req.nPlayer     = 0;
	req.nEntity     = 0;

	// A spray is ALWAYS someone's: player 0 is not a player, and
	// engine->GetPlayerInfo on it answers a blank record whose customFiles[0]
	// is zero, which CreateTempMaterialForPlayerLogo then refuses one call
	// deeper. Refused here instead so the refusal has a name.
	if ( !pPositionXYZ || nPlayer <= 0 || nEntity < 0 )
		return req;

	req.bDraw       = true;
	req.vecPosition = VecFrom( pPositionXYZ );
	req.nPlayer     = nPlayer;
	req.nEntity     = nEntity;

	return req;
}

//=============================================================================//
// THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP).
//
// The seven TEs on DT_TEParticleSystem share a base TABLE and nothing else, so
// they get eight named primitives rather than one kind+params slot -- the whole
// argument is over the declarations in sourcesharp.h. Sparks needs none of
// them: ClientSparks above already is its primitive, which is why there are
// eight backends for seven effects rather than seven.
//
// WHAT IS DECODED HERE is what is decodable without an engine: a null pointer,
// a refusal, and a value stock derives rather than sends. Everything else is
// one assignment per line in the .cpp.
//=============================================================================//

//-----------------------------------------------------------------------------
// A stream of blood. c_te_bloodstream.cpp.
//
// THE AMOUNT IS ALSO THE SPEED. Stock's `float speedCopy = amount;` seeds the
// particle velocity from the same number that decides how many rogue voxels
// there are (`amount/5`), and both readings are kept -- separating them here
// would be inventing a second parameter the wire does not carry.
//
// A DIRECTION IS REQUIRED, unlike a spark's. It is normalised before use and
// VectorNormalize of a zero vector answers zero with a divide guard, so a
// missing direction would be a stream that went nowhere rather than nothing at
// all; refused here so it has a name.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The debug line. c_te_showline.cpp.
//
// BOTH ENDS ARE REQUIRED. Stock walks from start to end in 3-unit steps and the
// loop is bounded by the DISTANCE between them, so a missing end is not a short
// line -- it is a line to the world origin, which from anywhere in a map is
// thousands of particles at 30 seconds each.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The dust puff and the gauss discharge, which are one shape: a point, a
// direction, and one scalar pair or one kind.
//
// TWO SLOTS RATHER THAN ONE, again for the Ricochet/MetalSparks reason: FX_Dust
// and FX_GaussExplosion are unrelated bodies in unrelated files and only their
// argument lists rhyme. (The gauss slot left with lane c5-particles: its body is
// C#, SparkEffects.GaussExplosion.)
//-----------------------------------------------------------------------------
struct PuffRequest
{
	bool  bDraw;
	Vec3  vecOrigin;
	Vec3  vecDirection;

	// Dust: the sprite size and the speed it is thrown at.
	float flSize;
	float flSpeed;

};

inline PuffRequest PuffRequestZero( void )
{
	PuffRequest req;

	req.bDraw        = false;
	req.vecOrigin    = VecZero();
	req.vecDirection = VecZero();
	req.flSize       = 0.0f;
	req.flSpeed      = 0.0f;

	return req;
}



//=============================================================================//
// C-PHASE 3 LANE CX. The three decals lane CD could not port, and the four
// capabilities between them.
//
// TWO OF THE FOUR HAVE A DECODE WORTH READING and both are here. The other two
// do not, and saying which is which is part of the gate rather than an excuse:
//
//   ClientDecalStaticProp   four arguments, two of them indices that must not
//                           go negative. DecalStaticProp below.
//   ClientTraceLine         eight parameters, FIVE of them out pointers, every
//                           one of which may be NULL. That is the transposition
//                           risk in this lane -- fraction and hitbox are both
//                           "the answer" and land in different pointers -- so
//                           the OUT WRITING is the thing unpacked here, not
//                           just the inputs. TraceLineArgs and WriteTraceOuts.
//   ClientIsLowViolence     takes nothing and returns an int. There is no
//                           argument order to get wrong and nothing here.
//   ClientDecalNameFromIndex  the index goes straight to the engine; what can
//                           be got wrong is the COPY OUT, so CopyName is here
//                           and the lookup is not.
//=============================================================================//

//-----------------------------------------------------------------------------
// A decal on a static prop. c_te_decal.cpp and c_te_projecteddecal.cpp.
//
// nStaticPropIndex ARRIVES ALREADY DECREMENTED. Stock passes `hitbox - 1` at
// both call sites, and the subtraction stays in C# beside the `hitbox != 0`
// test that reads the same convention -- the engine numbers a prop's hitbox
// from one so that zero can mean "not a static prop". What that means HERE is
// that a caller who forgot the test arrives with -1, and -1 indexes the static
// prop array from below zero. Refused.
//-----------------------------------------------------------------------------
struct StaticPropDecalRequest
{
	bool bDraw;

	Vec3 vecStart;
	Vec3 vecEnd;

	// The prop's index, already hitbox - 1. Never negative in a drawn request.
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

	// Both points are dereferenced by the engine without a check, and a
	// negative decal index is Draw_DecalIndexFromName's own "no such name"
	// answer flowing through -- DecalShoot above refuses it for the same
	// reason, and AddDecalToStaticProp indexes the same array.
	if ( !pStartXYZ || !pEndXYZ || nStaticPropIndex < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw            = true;
	req.vecStart         = VecFrom( pStartXYZ );
	req.vecEnd           = VecFrom( pEndXYZ );
	req.nStaticPropIndex = nStaticPropIndex;
	req.nDecalIndex      = nDecalIndex;

	return req;
}

//-----------------------------------------------------------------------------
// A line through the client's world: what goes IN.
//-----------------------------------------------------------------------------
struct TraceLineRequest
{
	bool bTrace;

	Vec3 vecStart;
	Vec3 vecEnd;

	// One of SourceSharpTraceMask. Carried rather than translated: the engine's
	// MASK_* constants are engine surface and this header has no engine in it.
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

//-----------------------------------------------------------------------------
// A line through the client's world: what comes OUT.
//
// SEVEN OUT POINTERS, and this struct exists because seven is more than enough
// for two of them to trade places unnoticed. `fraction` and `hitbox` are both
// "the answer" to the caller that has one, and `hitbox`, `entity index`,
// `surface props` and `surface flags` are FOUR ADJACENT INTS -- a decal or an
// impact written from the wrong one lands somewhere plausible, and nothing
// anywhere says so.
//
// bHit is stock's own rule and the server arm's: starting inside something
// counts as a hit, because an emitter buried in a wall has not got a clear line.
//-----------------------------------------------------------------------------
struct TraceAnswer
{
	bool  bHit;

	float flFraction;
	Vec3  vecEnd;
	Vec3  vecNormal;

	// tr.hitbox. Non-zero means a static prop, numbered from one; zero means
	// world, brush or a hitbox-less entity. This is the field the client had no
	// way to ask for before this lane.
	int   nHitbox;

	// tr.GetEntityIndex(), which on this side answers for BOTH hierarchies --
	// IClientUnknown -> IClientNetworkable::entindex(). -1 for nothing.
	int   nEntityIndex;

	// tr.surface.surfaceProps and tr.surface.flags. NO CALLER IN THIS LANE and
	// carried anyway -- see the slot's comment in sourcesharp.h. Measured off
	// c_te_legacytempents.cpp's collision block, which is ledger rows C4 and
	// C5: it fills a CEffectData's m_nSurfaceProp from the first and tests the
	// second for SURF_SKY. Both are shorts in csurface_t and int here, because
	// a short has no fixed width on that boundary.
	int   nSurfaceProps;
	int   nSurfaceFlags;
};

inline TraceAnswer TraceAnswerMiss( const Vec3 &vecEnd )
{
	TraceAnswer ans;

	// A miss ends where it was aimed and its fraction is one, which is what a
	// caller checking `fraction == 1.0f` is testing for.
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

//-----------------------------------------------------------------------------
// The answer, into whichever of the five out pointers the caller supplied.
//
// EVERY ONE MAY BE NULL, and a caller that supplied none still gets the return
// value -- a blast asking only "did it arrive" wants exactly that.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// A name into a caller's buffer. Returns the length written, not counting the
// terminator; 0 for nothing to write.
//
// TRUNCATION IS SILENT AND THE LENGTH SAYS SO -- the answer is what FITTED, not
// what the name was, so a caller comparing lengths can tell. A decal name is a
// material path and 260 is what every other string slot on this ABI gives
// itself, so this is a rule that will not fire rather than one that will.
//
// CDecalEmitterSystem answers "" for an index it does not hold (decals.cpp:161)
// and that must come back as ZERO rather than as a name that happens to be
// empty: the caller's next step is a substring test for "blood", and "" would
// pass a test for "does this name contain nothing".
//-----------------------------------------------------------------------------
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


//=============================================================================//
// C-PHASE 3 LANE DECALS. Behaviour ledger rows C2 and C3 -- the two arms of
// C_BaseEntity::AddDecal this hierarchy never had.
//
// WHAT CAN BE GOT WRONG HERE, and it is not the same thing as the static prop
// slot two sections up:
//
//   * ENTITY 0 IS A LEGAL SUBJECT and refusing it would silently delete row
//     C3's ordinary case. cl_entitylist->GetClientEntity( 0 ) is the world, and
//     the world is a brush model -- an "index must be positive" check written
//     out of habit turns every projected decal on map geometry into nothing,
//     which is exactly the state these rows were opened for. Negative is
//     refused; zero is not.
//   * THE BLOAT IS A MULTIPLY ON A DELTA, not on an endpoint, and doing it the
//     other way moves the decal. BloatedRayDelta below is that one line, out
//     where a test can read it.
//   * The studio arm takes the SEGMENT and the brush arm takes the CENTER, and
//     at c_te_decal.cpp's call site the center is the same vector as the
//     segment's END while at c_te_projecteddecal.cpp's it is not. A helper that
//     took one vector for both would agree with the first file and be wrong in
//     the second.
//=============================================================================//

//-----------------------------------------------------------------------------
// A decal on a studio model: what goes in.
//-----------------------------------------------------------------------------
struct StudioDecalRequest
{
	bool bDraw;

	int  nEntity;

	Vec3 vecStart;
	Vec3 vecEnd;

	int  nDecalIndex;
};

inline StudioDecalRequest StudioDecalRequestZero( void )
{
	StudioDecalRequest req;

	req.bDraw       = false;
	req.nEntity     = -1;
	req.vecStart    = VecZero();
	req.vecEnd      = VecZero();
	req.nDecalIndex = -1;

	return req;
}

inline StudioDecalRequest DecalStudioModel( int nEntity,
                                            const float *pRayStartXYZ, const float *pRayEndXYZ,
                                            int nDecalIndex )
{
	StudioDecalRequest req = StudioDecalRequestZero();

	// Both points are dereferenced without a check on the way to Ray_t::Init,
	// and a negative decal index is Draw_DecalIndexFromName's "no such name"
	// flowing through -- modelrender->AddDecal indexes the same array that
	// DecalShoot and AddDecalToStaticProp do.
	//
	// nEntity == 0 IS ALLOWED. It is the world, and the world reaches the
	// BRUSH arm rather than this one -- but which arm it reaches is
	// ClientEntityModelType's answer and not this function's guess, and a
	// refusal written here would be a second, disagreeing copy of that rule.
	if ( !pRayStartXYZ || !pRayEndXYZ || nEntity < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw       = true;
	req.nEntity     = nEntity;
	req.vecStart    = VecFrom( pRayStartXYZ );
	req.vecEnd      = VecFrom( pRayEndXYZ );
	req.nDecalIndex = nDecalIndex;

	return req;
}

//-----------------------------------------------------------------------------
// Stock's bloat, on the RAY'S DELTA. c_baseentity.cpp:3450 --
//
//     ray.Init( rayStart, rayEnd );
//     // FIXME: Better bloat?
//     // Bloat a little bit so we get the intersection
//     ray.m_Delta *= 1.1f;
//
// Ray_t::Init sets m_Delta to end - start, so computing (end - start) * 1.1f
// here is that pair of lines exactly, and not merely closely: the alternative
// -- moving the ENDPOINT out by a tenth and letting Ray_t::Init subtract --
// hands the engine ( start + d * 1.1f ) - start, which in floating point is not
// d * 1.1f. The difference is a rounding, and a rounding on a decal is a decal
// on the other side of a corner.
//
// The start is untouched: stock bloats one end of the segment, not both.
//-----------------------------------------------------------------------------
inline Vec3 BloatedRayDelta( const Vec3 &vecStart, const Vec3 &vecEnd )
{
	Vec3 d;

	d.x = ( vecEnd.x - vecStart.x ) * 1.1f;
	d.y = ( vecEnd.y - vecStart.y ) * 1.1f;
	d.z = ( vecEnd.z - vecStart.z ) * 1.1f;

	return d;
}

//-----------------------------------------------------------------------------
// A decal on a brush model, the world included: what goes in.
//-----------------------------------------------------------------------------
struct BrushDecalRequest
{
	bool bDraw;

	int  nEntity;

	// Stock's decalCenter -- AddDecal's THIRD argument, which is not always its
	// second. c_te_decal.cpp passes *pos for both; c_te_projecteddecal.cpp
	// passes *pos and endpos.
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

	// ENTITY 0 IS THE WORLD AND IS DRAWN, which is the one thing in this file
	// that must not be tidied into "> 0". Row C3's ordinary case -- every
	// unnamed info_projecteddecal on map geometry -- arrives here with entity
	// 0, and DecalShoot above already takes it for the same reason.
	if ( !pDecalCenterXYZ || nEntity < 0 || nDecalIndex < 0 )
		return req;

	req.bDraw       = true;
	req.nEntity     = nEntity;
	req.vecCenter   = VecFrom( pDecalCenterXYZ );
	req.nDecalIndex = nDecalIndex;

	return req;
}

} // namespace TeBackendArgs

#endif // SOURCESHARP_SHELL_TE_BACKEND_ARGS_H
