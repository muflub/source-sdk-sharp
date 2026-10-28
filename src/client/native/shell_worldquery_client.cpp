//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer
// The client's arm of the world query area: answers from the client's own BSP
// and entity table, which decide what the client draws. The client traces are
// separate slots from the server's: they report a hitbox, and the hit as an
// entity index rather than a handle (entindex() answers for both hierarchies).

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"
#include "trace_hit.h"
#include "util_shared.h"

#include "shell_cliententitylist.h"
#include "standalone_renderable_handle.h"
#include "shell_standalone_client.h"

// TraceLineArgs, TraceAnswer and WriteTraceOuts, in an engine-free header so
// the out-writing can be tested.
#include "shell_te_backend_args.h"
#include "flashlight_trace_filter.h"
#include "glow_trace_filter.h"

using namespace TeBackendArgs;

namespace
{
// Stock's CTraceFilterSkipPlayerAndViewModel: skips players, view models and
// the two debris groups, and hits a thing that is no entity. The rule is in
// flashlight_trace_filter.h. A shell's collision group is not networked, so the
// debris arm cannot fire yet.
class CSkipPlayerAndViewModelFilter : public CTraceFilter
{
public:
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask ) OVERRIDE
	{
		// A handle this side's table does not answer (a static prop's) is no
		// entity, and stock hits it.
		if ( !pHandleEntity )
			return true;
		const CBaseHandle hHit = pHandleEntity->GetRefEHandle();
		if ( !SourceSharp_ClientNetworkableFromHandle( hHit ) )
			return true;
		C_StandaloneEntity *pShell = SourceSharp_StandaloneFromHandle( (uint32_t)hHit.ToInt() );
		if ( !pShell )
			return true;
		return SkipPlayerAndViewModelHits( pShell->IsPlayerShell(), pShell->Block().Client.ViewModel != 0,
										   pShell->GetCollisionGroup() );
	}
};

// Stock's CTraceFilterGlow: a CTraceFilterSimple with no pass entity that only
// a VPHYSICS or BSP collideable may stop. The rule is in glow_trace_filter.h.
class CGlowSightFilter : public CTraceFilterSimple
{
public:
	CGlowSightFilter()
		: CTraceFilterSimple( NULL, COLLISION_GROUP_NONE )
	{
	}

	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask ) OVERRIDE
	{
		IClientUnknown *pUnk = (IClientUnknown *)pHandleEntity;
		ICollideable *pCollide = pUnk->GetCollideable();
		if ( !GlowSightAdmits( pCollide->GetSolid() ) )
			return false;
		return CTraceFilterSimple::ShouldHitEntity( pHandleEntity, contentsMask );
	}
};
} // namespace

// The index of what a trace hit, read off the hit's own handle through this
// side's table.
static int TraceHitIndex( IHandleEntity *pHit )
{
	if ( !pHit )
		return -1;

	const CBaseHandle hHit = pHit->GetRefEHandle();
	return SourceSharp_ClientIndexOfHit( hHit, SourceSharp_ClientNetworkableFromHandle( hHit ) != NULL );
}

// The engine mask a SOURCESHARP_TRACE_ value names, for every trace slot on
// this side.
static unsigned int ClientTraceMaskOf( int32_t nMask )
{
	// Brushes only: "does a wall shield you from this".
	unsigned int nTraceMask = MASK_SOLID_BRUSHONLY;
	if ( nMask == SOURCESHARP_TRACE_SHOT )
		nTraceMask = MASK_SHOT;
	else if ( nMask == SOURCESHARP_TRACE_BRUSHES_AND_WATER )
		nTraceMask = MASK_SOLID_BRUSHONLY | MASK_WATER;
	else if ( nMask == SOURCESHARP_TRACE_ALL )
		nTraceMask = MASK_ALL;
	// The blast line: the hitbox bit out and the grate bit in, so it stops on a
	// floor vent and not on a body it is about to throw.
	else if ( nMask == SOURCESHARP_TRACE_SHOT_HULL )
		nTraceMask = MASK_SHOT_HULL;
	// What a moving thing runs into (the tempent pool's collision): not
	// MASK_SHOT, which adds debris and hitboxes and drops grates.
	else if ( nMask == SOURCESHARP_TRACE_SOLID )
		nTraceMask = MASK_SOLID;
	// Line of sight: unlike the default it passes glass and stops on a
	// CONTENTS_BLOCKLOS volume.
	else if ( nMask == SOURCESHARP_TRACE_BLOCKLOS )
		nTraceMask = MASK_BLOCKLOS;
	// The flashlight's direction mask.
	else if ( nMask == SOURCESHARP_TRACE_OPAQUE_AND_NPCS_WINDOW )
		nTraceMask = MASK_OPAQUE_AND_NPCS | CONTENTS_WINDOW;
	// A glow's sight line: no glass.
	else if ( nMask == SOURCESHARP_TRACE_GLOW_SIGHT )
		nTraceMask = MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS;
	// A glow overlay's line at the sky.
	else if ( nMask == SOURCESHARP_TRACE_CONTENTS_SOLID )
		nTraceMask = CONTENTS_SOLID;
	// A shot through glass (the RPG's laser).
	else if ( nMask == SOURCESHARP_TRACE_SHOT_NO_WINDOWS )
		nTraceMask = MASK_SHOT & ~CONTENTS_WINDOW;
	// What a crossbow bolt's line behind its victim stops on.
	else if ( nMask == SOURCESHARP_TRACE_OPAQUE )
		nTraceMask = MASK_OPAQUE;

	return nTraceMask;
}

// A line through the client's world. SOURCESHARP_TRACE_ALL exists because stock
// projects decals with MASK_ALL.
int32_t SourceSharp_ClientTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask, int32_t nFilter,
									 int32_t nPassEntityIndex, float *pOutFraction, float *pOutEndXYZ,
									 float *pOutNormalXYZ, int32_t *pOutHitbox, int32_t *pOutEntityIndex,
									 int32_t *pOutSurfaceProps, int32_t *pOutSurfaceFlags )
{
	const TraceLineRequest req = TraceLineArgs( pStartXYZ, pEndXYZ, nMask );

	if ( !req.bTrace )
		return WriteTraceOuts( TraceAnswerMiss( VecZero() ), pOutFraction, pOutEndXYZ, pOutNormalXYZ, pOutHitbox,
							   pOutEntityIndex, pOutSurfaceProps, pOutSurfaceFlags );

	const Vector vecStart( req.vecStart.x, req.vecStart.y, req.vecStart.z );
	const Vector vecEnd( req.vecEnd.x, req.vecEnd.y, req.vecEnd.z );

	const unsigned int nTraceMask = ClientTraceMaskOf( req.nMask );

	trace_t tr;

	// Three filters, each a stock class by name. CTraceFilterSimple also applies
	// collision groups and the game rules, so it is not hit-all minus one entity.
	if ( nFilter == SOURCESHARP_TRACE_FILTER_WORLD_ONLY )
	{
		CTraceFilterWorldOnly traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_SIMPLE )
	{
		// The pass entity, resolved from an index (a handle read back through
		// CBaseHandle::Get() is unsafe on this side). A NULL pass entity still
		// gives a simple filter, not hit-all.
		IClientEntity *pEnt = ( nPassEntityIndex >= 0 ) ? SourceSharp_ClientEntityFromIndex( nPassEntityIndex ) : NULL;

		// Through IClientUnknown explicitly: IClientEntity inherits
		// IHandleEntity down more than one path, so the conversion is ambiguous.
		IHandleEntity *pPass = pEnt ? static_cast<IClientUnknown *>( pEnt ) : NULL;

		// COLLISION_GROUP_NONE always; the group is not a parameter.
		CTraceFilterSimple traceFilter( pPass, COLLISION_GROUP_NONE );
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_SKIP_PLAYER_AND_VIEW_MODEL )
	{
		CSkipPlayerAndViewModelFilter traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_GLOW )
	{
		CGlowSightFilter traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else
	{
		CTraceFilterHitAll traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}

	TraceAnswer ans;

	// Starting inside something counts as a hit, as on the server and in stock.
	ans.bHit = ( tr.fraction < 1.0f || tr.startsolid );
	ans.flFraction = tr.fraction;
	ans.vecEnd.x = tr.endpos.x;
	ans.vecEnd.y = tr.endpos.y;
	ans.vecEnd.z = tr.endpos.z;
	ans.vecNormal.x = tr.plane.normal.x;
	ans.vecNormal.y = tr.plane.normal.y;
	ans.vecNormal.z = tr.plane.normal.z;

	// The index is read off the hit's own handle, through the table that
	// resolves both hierarchies.
	ans.nHitbox = tr.hitbox;
	ans.nEntityIndex = TraceHitIndex( ss::TraceHit( tr ) );

	// No caller yet. Both are shorts in csurface_t; the cast widens.
	ans.nSurfaceProps = (int)tr.surface.surfaceProps;
	ans.nSurfaceFlags = (int)tr.surface.flags;

	return WriteTraceOuts( ans, pOutFraction, pOutEndXYZ, pOutNormalXYZ, pOutHitbox, pOutEntityIndex, pOutSurfaceProps,
						   pOutSurfaceFlags );
}

// A line against the entities the caller names. The shells are in no spatial
// partition, so no engine trace on this side can stop on one; this clips the
// ray against each as the engine's enumerator would, passing over the
// non-solid, and answers the nearest hit in ClientTraceLine's outs.
COMPILE_TIME_ASSERT( (int)SOLID_NONE == kClipSolidNone );
COMPILE_TIME_ASSERT( (int)FSOLID_NOT_SOLID == kClipNotSolid );

int32_t SourceSharp_ClientClipRayToEntities( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask,
											 const int32_t *pEntityIndices, int32_t nEntities, float *pOutFraction,
											 float *pOutEndXYZ, float *pOutNormalXYZ, int32_t *pOutHitbox,
											 int32_t *pOutEntityIndex, int32_t *pOutSurfaceProps,
											 int32_t *pOutSurfaceFlags )
{
	const TraceLineRequest req = TraceLineArgs( pStartXYZ, pEndXYZ, nMask );

	if ( !req.bTrace || !pEntityIndices || nEntities <= 0 )
		return WriteTraceOuts( TraceAnswerMiss( req.bTrace ? req.vecEnd : VecZero() ), pOutFraction, pOutEndXYZ,
							   pOutNormalXYZ, pOutHitbox, pOutEntityIndex, pOutSurfaceProps, pOutSurfaceFlags );

	const unsigned int nTraceMask = ClientTraceMaskOf( req.nMask );

	Ray_t ray;
	ray.Init( Vector( req.vecStart.x, req.vecStart.y, req.vecStart.z ),
			  Vector( req.vecEnd.x, req.vecEnd.y, req.vecEnd.z ) );

	trace_t best;
	memset( &best, 0, sizeof( best ) );
	best.fraction = 1.0f;
	bool bHit = false;

	for ( int32_t i = 0; i < nEntities; ++i )
	{
		IClientEntity *pEnt = SourceSharp_ClientEntityFromIndex( pEntityIndices[i] );
		ICollideable *pCollide = pEnt ? pEnt->GetCollideable() : NULL;

		if ( !pCollide || !ClipCandidateIsSolid( (int)pCollide->GetSolid(), pCollide->GetSolidFlags() ) )
			continue;

		trace_t tr;
		enginetrace->ClipRayToCollideable( ray, nTraceMask, pCollide, &tr );

		if ( ClipIsNearer( tr.fraction, tr.startsolid, best.fraction ) )
		{
			best = tr;
			bHit = true;
		}
	}

	if ( !bHit )
		return WriteTraceOuts( TraceAnswerMiss( req.vecEnd ), pOutFraction, pOutEndXYZ, pOutNormalXYZ, pOutHitbox,
							   pOutEntityIndex, pOutSurfaceProps, pOutSurfaceFlags );

	TraceAnswer ans;
	ans.bHit = true;
	ans.flFraction = best.fraction;
	ans.vecEnd.x = best.endpos.x;
	ans.vecEnd.y = best.endpos.y;
	ans.vecEnd.z = best.endpos.z;
	ans.vecNormal.x = best.plane.normal.x;
	ans.vecNormal.y = best.plane.normal.y;
	ans.vecNormal.z = best.plane.normal.z;
	ans.nHitbox = best.hitbox;
	ans.nEntityIndex = TraceHitIndex( best.m_pEnt );
	ans.nSurfaceProps = (int)best.surface.surfaceProps;
	ans.nSurfaceFlags = (int)best.surface.flags;

	return WriteTraceOuts( ans, pOutFraction, pOutEndXYZ, pOutNormalXYZ, pOutHitbox, pOutEntityIndex, pOutSurfaceProps,
						   pOutSurfaceFlags );
}

// The observer's camera.
//
// ClientTraceHull: UTIL_TraceHull with the stock filter managed names (the
// chase camera's CTraceFilterNoNPCsOrPlayer, the death camera's
// CTraceFilterSimple). Answers whether it stopped short, the fraction and the end.
//
// ClientEntityRenderPose: an entity's drawn origin and angles by index, and
// whether it has a model.
namespace
{
// Stock's CTraceFilterNoNPCsOrPlayer: the simple filter minus NPCs and players.
// Every client entity is a standalone shell, so the shell's player flag
// answers; a renderable that is no entity is not hit (stock's NULL arm).
class CObserverNoNPCsOrPlayerFilter : public CTraceFilterSimple
{
public:
	CObserverNoNPCsOrPlayerFilter( const IHandleEntity *pPass )
		: CTraceFilterSimple( pPass, COLLISION_GROUP_NONE )
	{
	}

	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask ) OVERRIDE
	{
		if ( !CTraceFilterSimple::ShouldHitEntity( pHandleEntity, contentsMask ) )
			return false;
		IClientUnknown *pUnknown = static_cast<IClientUnknown *>( pHandleEntity );
		uint32_t nWord = 0;
		if ( RenderableEntityHandleWord( pUnknown, nWord ) )
		{
			if ( C_StandaloneEntity *pShell = SourceSharp_StandaloneFromHandle( nWord ) )
				return !pShell->IsPlayerShell();
		}
		return false;
	}
};
} // namespace

int32_t SourceSharp_ClientTraceHull( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ,
									 const float *pMaxsXYZ, int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex,
									 float *pOutFraction, float *pOutEndXYZ )
{
	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ )
		return 0;
	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );
	const Vector vecMins( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] );
	const Vector vecMaxs( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] );

	const unsigned int nTraceMask = ClientTraceMaskOf( nMask );

	IClientEntity *pEnt = ( nPassEntityIndex >= 0 ) ? SourceSharp_ClientEntityFromIndex( nPassEntityIndex ) : NULL;
	IHandleEntity *pPass = pEnt ? static_cast<IClientUnknown *>( pEnt ) : NULL;

	trace_t tr;
	if ( nFilter == SOURCESHARP_TRACE_FILTER_NO_NPCS_OR_PLAYER )
	{
		CObserverNoNPCsOrPlayerFilter traceFilter( pPass );
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_SIMPLE )
	{
		CTraceFilterSimple traceFilter( pPass, COLLISION_GROUP_NONE );
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_WORLD_ONLY )
	{
		CTraceFilterWorldOnly traceFilter;
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_SKIP_PLAYER_AND_VIEW_MODEL )
	{
		CSkipPlayerAndViewModelFilter traceFilter;
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_GLOW )
	{
		CGlowSightFilter traceFilter;
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}
	else
	{
		CTraceFilterHitAll traceFilter;
		UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &tr );
	}

	if ( pOutFraction )
		*pOutFraction = tr.fraction;
	if ( pOutEndXYZ )
	{
		pOutEndXYZ[0] = tr.endpos.x;
		pOutEndXYZ[1] = tr.endpos.y;
		pOutEndXYZ[2] = tr.endpos.z;
	}
	return ( tr.fraction < 1.0f || tr.startsolid ) ? 1 : 0;
}

// ClientTraceHullEntities: a box swept through the world and clipped against
// each entity managed names (the shells are in no spatial partition). The
// nearest stop is the answer: whether it stopped, the fraction and end, the
// index of what it stopped on (the world's is 0) and that entity's GetAbsOrigin.
int32_t SourceSharp_ClientTraceHullEntities( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ,
											 const float *pMaxsXYZ, int32_t nMask, int32_t nPassEntityIndex,
											 const int32_t *pEntityIndices, int32_t nEntities, float *pOutFraction,
											 float *pOutEndXYZ, int32_t *pOutEntityIndex, float *pOutEntityOriginXYZ )
{
	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ )
		return 0;
	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );
	const Vector vecMins( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] );
	const Vector vecMaxs( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] );

	const unsigned int nTraceMask = ClientTraceMaskOf( nMask );

	IClientEntity *pPassEnt = ( nPassEntityIndex >= 0 ) ? SourceSharp_ClientEntityFromIndex( nPassEntityIndex ) : NULL;
	IHandleEntity *pPass = pPassEnt ? static_cast<IClientUnknown *>( pPassEnt ) : NULL;

	trace_t best;
	CTraceFilterSimple traceFilter( pPass, COLLISION_GROUP_NONE );
	UTIL_TraceHull( vecStart, vecEnd, vecMins, vecMaxs, nTraceMask, &traceFilter, &best );
	bool bHit = ( best.fraction < 1.0f || best.startsolid );
	int32_t nHitIndex = bHit ? TraceHitIndex( ss::TraceHit( best ) ) : -1;

	Ray_t ray;
	ray.Init( vecStart, vecEnd, vecMins, vecMaxs );
	for ( int32_t i = 0; pEntityIndices && i < nEntities; ++i )
	{
		IClientEntity *pEnt = SourceSharp_ClientEntityFromIndex( pEntityIndices[i] );
		ICollideable *pCollide = pEnt ? pEnt->GetCollideable() : NULL;

		if ( !pCollide || !ClipCandidateIsSolid( (int)pCollide->GetSolid(), pCollide->GetSolidFlags() ) )
			continue;

		trace_t tr;
		enginetrace->ClipRayToCollideable( ray, nTraceMask, pCollide, &tr );

		if ( ClipIsNearer( tr.fraction, tr.startsolid, bHit ? best.fraction : 1.0f ) )
		{
			best = tr;
			bHit = true;
			nHitIndex = pEntityIndices[i];
		}
	}

	IClientEntity *pHitEnt = ( nHitIndex > 0 ) ? SourceSharp_ClientEntityFromIndex( nHitIndex ) : NULL;
	const Vector vecOrigin = pHitEnt ? pHitEnt->GetAbsOrigin() : vec3_origin;
	if ( pOutFraction )
		*pOutFraction = best.fraction;
	if ( pOutEndXYZ )
	{
		pOutEndXYZ[0] = best.endpos.x;
		pOutEndXYZ[1] = best.endpos.y;
		pOutEndXYZ[2] = best.endpos.z;
	}
	if ( pOutEntityIndex )
		*pOutEntityIndex = nHitIndex;
	if ( pOutEntityOriginXYZ )
	{
		pOutEntityOriginXYZ[0] = vecOrigin.x;
		pOutEntityOriginXYZ[1] = vecOrigin.y;
		pOutEntityOriginXYZ[2] = vecOrigin.z;
	}
	return bHit ? 1 : 0;
}

int32_t SourceSharp_ClientEntityRenderPose( int32_t nEntityIndex, float *pOutOriginXYZ, float *pOutAngles )
{
	IClientEntity *pEnt = ( nEntityIndex >= 0 ) ? SourceSharp_ClientEntityFromIndex( nEntityIndex ) : NULL;
	IClientRenderable *pRenderable = pEnt ? pEnt->GetClientRenderable() : NULL;
	if ( !pRenderable )
		return -1;
	const Vector &origin = pRenderable->GetRenderOrigin();
	const QAngle &angles = pRenderable->GetRenderAngles();
	if ( pOutOriginXYZ )
	{
		pOutOriginXYZ[0] = origin.x;
		pOutOriginXYZ[1] = origin.y;
		pOutOriginXYZ[2] = origin.z;
	}
	if ( pOutAngles )
	{
		pOutAngles[0] = angles.x;
		pOutAngles[1] = angles.y;
		pOutAngles[2] = angles.z;
	}
	return pRenderable->GetModel() ? 1 : 0;
}
