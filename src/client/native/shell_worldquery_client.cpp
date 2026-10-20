//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's half of the world query area ==========//
// SS_REASON e  a synchronous mid-step answer
//
// shell_worldquery.cpp does this for the server. This is the same area's CLIENT
// arm, and it holds exactly what the client can answer and the server cannot
// answer for it.
//
// WHY IT EXISTS AT ALL, since the server has answered PointContents since long
// before the temp entity port. Because it was answering only for the SERVER.
// EngineServices.AddTo registers every engine area on both sides, so managed
// client code has always been able to hold an IEngineWorldQuery and ask it what
// is at a point — and until this file the slot behind that question was NULL on
// this side, so Engine.cs's null guard answered CONTENTS_EMPTY and said nothing.
// That is the "(no model)" shape SideAuditTests records three previous instances
// of (EntityGetModelName, EntityGetAngles, EntitySetSkin), each found the same
// way: a managed caller on the wrong side of a one-sided slot, getting a
// plausible zero.
//
// C_TEExplosion is what found this one. Stock's PostDataUpdate decides between a
// fireball and a water splash with `UTIL_PointContents( m_vecOrigin ) &
// CONTENTS_WATER` — on the CLIENT, in the client's own copy of the BSP, rather
// than trusting a flag from the server (c_te_explosion.cpp:290). Porting that
// decision to C# needs the question askable here. Answering it from the server's
// arm instead is not available: the two DLLs are two processes' worth of state
// in the listen-server case and a network away in every other.
//
// ONE SLOT, TWO IMPLEMENTATIONS, which is what SideAuditTests calls DualWired
// and is the same shape TempEntRegister itself has.
//
// C-PHASE 3 LANE CX ADDED THE SECOND TENANT: a client TraceLine. That one is
// NOT dual -- it is client-only, and deliberately a separate slot from the
// server's TraceLine rather than a second arm of it. Three reasons, in the
// order they matter:
//
//   1. The server's slot has no hitbox in its outs, and tr.hitbox is the ONLY
//      thing the first caller reads. Widening that slot changes a signature
//      the server side already ships.
//   2. This side answers from the client's own copy of the BSP and the client's
//      own entity list, which is the copy that decides what the client draws --
//      the argument PointContents above already makes.
//   3. It reports what it hit as an entity INDEX rather than a handle. See
//      the slot's comment in sourcesharp.h: a handle read back through
//      CBaseHandle::Get() is ledger row C8's segfault, and entindex() is the
//      one accessor that answers for both hierarchies on this side.
//
//=============================================================================//

#include "cbase.h"

// plan_entity.md Phase 5a: an entity index or handle resolves through §3.3's
// slot table, not through cl_entitylist. shell_cliententitylist.h says which of
// the five resolvers answers which question.
#include "shell_cliententitylist.h"
#include "standalone_renderable_handle.h"
#include "shell_standalone_client.h"
#include "util_shared.h"
#include "engine/IEngineTrace.h"

// TraceLineArgs, TraceAnswer and WriteTraceOuts. The header has no engine in it
// and sourcesharp/test/te_backend_args_test.cpp is what reads it -- which is
// the only way an eight-parameter call with five out pointers gets its
// out-writing checked without a running Source. It is named for the temp entity
// backends because that is what filled it; this slot is the first tenant that
// is not one, and a second engine-free header holding three functions would be
// a file whose only purpose is its name.
#include "shell_te_backend_args.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

using namespace TeBackendArgs;

//-----------------------------------------------------------------------------
// What is at a point, on the client.
//
// The whole CONTENTS_ mask comes back rather than a predicate, and the server
// arm's comment on that applies verbatim: a predicate per caller is how an ABI
// grows without getting more useful. The explosion asks only about water.
//
// UTIL_PointContents is SHARED code (util_shared.h:442) — `enginetrace->
// GetPointContents` on both sides — so this arm is the same call reaching this
// side's engine, not a second implementation of the question.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PointContents( const float *pOrigin )
{
	if ( !pOrigin )
		return 0;

	return (int32_t)UTIL_PointContents( Vector( pOrigin[0], pOrigin[1], pOrigin[2] ) );
}


//-----------------------------------------------------------------------------
// The index of what a trace hit, read off the hit's own handle through this
// side's table (SourceSharp_ClientIndexOfHit says which entries answer). Was
// CGameTrace::GetEntityIndex, which left client.so with gametrace_client.cpp.
//-----------------------------------------------------------------------------
static int TraceHitIndex( IHandleEntity *pHit )
{
	if ( !pHit )
		return -1;

	const CBaseHandle hHit = pHit->GetRefEHandle();
	return SourceSharp_ClientIndexOfHit( hHit, SourceSharp_ClientNetworkableFromHandle( hHit ) != NULL );
}

//-----------------------------------------------------------------------------
// A line through the client's world.
//
// STOCK'S FILTER, WHICH IS NO FILTER. CTraceFilterHitAll returns true for
// everything and asks for TRACE_EVERYTHING, and that is what
// c_te_projecteddecal.cpp traces with -- the only place on this side that
// traces at all today. There is no ignore-entity parameter, and the slot's
// comment in sourcesharp.h says why at length: switching to CTraceFilterSimple
// to gain one would also bring collision groups and gamerules along, so a
// caller passing "nothing to ignore" would get a different trace than a caller
// passing nothing.
//
// The mask vocabulary is SourceSharpTraceMask, shared with the server's arm so
// that a value means one thing on both sides. SOURCESHARP_TRACE_ALL exists for
// this caller: a decal projected with MASK_SHOT instead of MASK_ALL lands on a
// different surface, and stock projects with MASK_ALL.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask,
                                     int32_t nFilter, int32_t nPassEntityIndex,
                                     float *pOutFraction, float *pOutEndXYZ, float *pOutNormalXYZ,
                                     int32_t *pOutHitbox, int32_t *pOutEntityIndex,
                                     int32_t *pOutSurfaceProps, int32_t *pOutSurfaceFlags )
{
	const TraceLineRequest req = TraceLineArgs( pStartXYZ, pEndXYZ, nMask );

	if ( !req.bTrace )
		return WriteTraceOuts( TraceAnswerMiss( VecZero() ),
		                       pOutFraction, pOutEndXYZ, pOutNormalXYZ,
		                       pOutHitbox, pOutEntityIndex,
		                       pOutSurfaceProps, pOutSurfaceFlags );

	const Vector vecStart( req.vecStart.x, req.vecStart.y, req.vecStart.z );
	const Vector vecEnd  ( req.vecEnd.x,   req.vecEnd.y,   req.vecEnd.z   );

	// The server arm's default, and for its reason: brushes only is the mask
	// that means "does a wall shield you from this".
	unsigned int nTraceMask = MASK_SOLID_BRUSHONLY;
	if ( req.nMask == SOURCESHARP_TRACE_SHOT )
		nTraceMask = MASK_SHOT;
	else if ( req.nMask == SOURCESHARP_TRACE_BRUSHES_AND_WATER )
		nTraceMask = MASK_SOLID_BRUSHONLY | MASK_WATER;
	else if ( req.nMask == SOURCESHARP_TRACE_ALL )
		nTraceMask = MASK_ALL;
	// MASK_SHOT_HULL, which is what CRagdollExplosionEnumerator traces the
	// blast line with (c_te_explosion.cpp:86) and what the managed impulse path
	// that replaced it asks for. Not MASK_SHOT: the hitbox bit is out and the
	// grate bit is in, so a blast line stops on a floor vent and does not stop
	// on a body it is about to throw.
	else if ( req.nMask == SOURCESHARP_TRACE_SHOT_HULL )
		nTraceMask = MASK_SHOT_HULL;
	// What a THING moving through the world runs into, which is what the legacy
	// tempent pool's collision arm traces with. Not MASK_SHOT: that adds debris
	// and hitboxes and drops grates, so a gib would stop on a scrap of debris
	// and fall through a grating.
	else if ( req.nMask == SOURCESHARP_TRACE_SOLID )
		nTraceMask = MASK_SOLID;
	// MASK_BLOCKLOS -- "is the line of sight between these two points broken",
	// which is not the brushes-only default: that stops on glass and does not
	// stop on a CONTENTS_BLOCKLOS volume. Wired here as well as on the server
	// arm, which is the arm that needed it, for SOURCESHARP_TRACE_ALL's reason.
	else if ( req.nMask == SOURCESHARP_TRACE_BLOCKLOS )
		nTraceMask = MASK_BLOCKLOS;

	trace_t tr;

	// THREE FILTERS, EACH A STOCK CLASS BY NAME. See SourceSharpTraceFilter for
	// why this is not an ignore-entity parameter: CTraceFilterSimple is not
	// CTraceFilterHitAll with one entity removed -- it also applies collision
	// groups and g_pGameRules->ShouldCollide -- so "ignore nothing" would not
	// answer the hit-all trace a caller asking for one expects.
	if ( nFilter == SOURCESHARP_TRACE_FILTER_WORLD_ONLY )
	{
		CTraceFilterWorldOnly traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else if ( nFilter == SOURCESHARP_TRACE_FILTER_SIMPLE )
	{
		// The pass entity, resolved from an INDEX. A handle read back through
		// CBaseHandle::Get() is row C8's unfiltered reinterpret, and this
		// side's handle table holds managed entities; the slot table's index
		// lookup (§3.3) is the one that does not do that.
		//
		// A NULL pass entity is not an error and not a fallback to hit-all:
		// CTraceFilterSimple with no pass entity is still a simple filter, and
		// still applies the collision group and the game rules that make it
		// different from hit-all. Answering hit-all here would be the exact
		// substitution this enumeration exists to prevent.
		IClientEntity *pEnt = ( nPassEntityIndex >= 0 )
		                    ? SourceSharp_ClientEntityFromIndex( nPassEntityIndex )
		                    : NULL;

		// Through IClientUnknown explicitly: IClientEntity inherits
		// IHandleEntity down more than one path, so the implicit conversion is
		// ambiguous and the compiler says so.
		IHandleEntity *pPass = pEnt ? static_cast< IClientUnknown * >( pEnt ) : NULL;

		// COLLISION_GROUP_NONE always; the group is not a parameter and
		// SourceSharpTraceFilter says why.
		CTraceFilterSimple traceFilter( pPass, COLLISION_GROUP_NONE );
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}
	else
	{
		CTraceFilterHitAll traceFilter;
		UTIL_TraceLine( vecStart, vecEnd, nTraceMask, &traceFilter, &tr );
	}

	TraceAnswer ans;

	// Starting inside something counts as a hit, which is the server arm's rule
	// and stock's: a ray that began in a wall has not got a clear line.
	ans.bHit         = ( tr.fraction < 1.0f || tr.startsolid );
	ans.flFraction   = tr.fraction;
	ans.vecEnd.x     = tr.endpos.x;
	ans.vecEnd.y     = tr.endpos.y;
	ans.vecEnd.z     = tr.endpos.z;
	ans.vecNormal.x  = tr.plane.normal.x;
	ans.vecNormal.y  = tr.plane.normal.y;
	ans.vecNormal.z  = tr.plane.normal.z;

	// The two this slot exists for. The index is read off the hit's own
	// handle, through the table that resolves both hierarchies
	// (SourceSharp_ClientIndexOfHit; gametrace_client.cpp's GetEntityIndex
	// left client.so in C6).
	ans.nHitbox      = tr.hitbox;
	ans.nEntityIndex = TraceHitIndex( tr.m_pEnt );

	// NO CALLER YET, and the slot's comment in sourcesharp.h says why they are
	// here anyway: rows C4 and C5 sit in a collision block that reads exactly
	// these two off its own trace_t, and the alternative was that lane widening
	// a slot other lanes already call. Both are shorts in csurface_t; the cast
	// is the widening, not a reinterpretation.
	ans.nSurfaceProps = (int)tr.surface.surfaceProps;
	ans.nSurfaceFlags = (int)tr.surface.flags;

	return WriteTraceOuts( ans, pOutFraction, pOutEndXYZ, pOutNormalXYZ,
	                       pOutHitbox, pOutEntityIndex,
	                       pOutSurfaceProps, pOutSurfaceFlags );
}

//=============================================================================//
// THE OBSERVER'S CAMERA (plan_client_novalve C7 item 5).
//
// ClientTraceHull: UTIL_TraceHull with the stock filter managed names -- the
// chase camera's CTraceFilterNoNPCsOrPlayer passing its target, the death
// camera's CTraceFilterSimple passing the player. The mask is one of the
// SOURCESHARP_TRACE_ values; the camera's is SOURCESHARP_TRACE_SOLID.
// Answers whether it stopped short, with the fraction and the end.
//
// ClientEntityRenderPose: an entity's drawn origin and angles by index
// (GetRenderOrigin / GetRenderAngles), and whether it has a model -- what the
// chase camera reads off its target (GetObserverCamOrigin is the render origin).
//=============================================================================//
namespace
{
// util_shared.cpp's CTraceFilterNoNPCsOrPlayer, whose body left client.so with
// that TU: what the simple filter hits, minus NPCs and players. Every client
// entity here is a standalone shell (no C_BaseEntity is linked), so the shell's
// own player flag answers; any other shell is hit, as a stock prop would be,
// and a renderable that is no entity is not (stock's NULL arm).
class CObserverNoNPCsOrPlayerFilter : public CTraceFilterSimple
{
public:
	CObserverNoNPCsOrPlayerFilter( const IHandleEntity *pPass ) : CTraceFilterSimple( pPass, COLLISION_GROUP_NONE ) {}

	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask ) OVERRIDE
	{
		if ( !CTraceFilterSimple::ShouldHitEntity( pHandleEntity, contentsMask ) )
			return false;
		IClientUnknown *pUnknown = static_cast< IClientUnknown * >( pHandleEntity );
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

int32_t SourceSharp_ClientTraceHull( const float *pStartXYZ, const float *pEndXYZ,
                                     const float *pMinsXYZ, const float *pMaxsXYZ,
                                     int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex,
                                     float *pOutFraction, float *pOutEndXYZ )
{
	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ )
		return 0;
	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );
	const Vector vecMins( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] );
	const Vector vecMaxs( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] );

	unsigned int nTraceMask = MASK_SOLID_BRUSHONLY;
	if ( nMask == SOURCESHARP_TRACE_SOLID )
		nTraceMask = MASK_SOLID;
	else if ( nMask == SOURCESHARP_TRACE_SHOT )
		nTraceMask = MASK_SHOT;

	IClientEntity *pEnt = ( nPassEntityIndex >= 0 ) ? SourceSharp_ClientEntityFromIndex( nPassEntityIndex ) : NULL;
	IHandleEntity *pPass = pEnt ? static_cast< IClientUnknown * >( pEnt ) : NULL;

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

//=============================================================================//
// C-PHASE 5: THE TWO MODEL QUESTIONS A MANAGED SPAWN HELPER OPENS WITH.
//
// c_te_legacytempents.cpp's seventeen spawn helpers are moving to C#, and ten
// of them start by asking how many frames the model has -- it sets m_flFrameMax,
// it is what a random starting frame is drawn from, and it is what TempSprite's
// default lifetime is computed from ( frameCount * 0.1 + 1 ).
//
// ModelFrameCount IS NOT A NEW SLOT, and that is the interesting half. It has
// existed since a managed env_sprite needed m_flMaxFrame -- but only with a
// SERVER arm, so a managed client asking it got -1 and no error. That is the
// same one-sided shape PointContents above had, and EntityGetModelName,
// EntityGetAngles and EntitySetSkin before it; SideAuditTests keeps the list.
// This is its client arm, and the two answer from two precache tables about one
// file, which for a frame count is the same number.
//
// ModelType is new, and is wired on BOTH sides for the reason sourcesharp.h
// gives beside it: mod_bad is 0, so an unwired arm answers a plausible wrong
// value rather than nothing.
//=============================================================================//

// SourceSharp_ModelFrameCount AND SourceSharp_ModelType ARE GONE --
// plan_native_api.md Phase 4.
//
// Both were GetModel followed by one question about the model, and both are
// now IFwdModelInfo.FrameCountAt / TypeAt, which do the same two calls
// through the same interface with no shim in between. The DECISIONS moved
// with them and are tested there: -1 for an index that names no model and
// never 0, because 0 is a real frame count; and ModelType.Bad where -1 is
// not available at the type, because mod_bad IS zero.


// physprops, which is game/shared/physics_shared.h's and is the SAME
// IPhysicsSurfaceProps both sides use. Included here rather than at the top
// because it is this one function's only need -- see it for why the two arms
// agree by construction.
#include "physics_shared.h"

//-----------------------------------------------------------------------------
// WHAT A SURFACE IS MADE OF, on this side.
//
// The sixth instance of the shape SideAuditTests keeps a list of, and it was
// found the way the fifth was -- by a client caller appearing and the audit
// refusing it. SurfacePropGameMaterial had a SERVER arm only, so managed client
// code holding an IEngineWorldQuery could ask what a surface was made of and
// get 0 back with no error. Zero is not "unknown": it is a character code, and
// the caller would have read it as a real material.
//
// C-PHASE 5 IS WHAT NEEDED IT, and for a smaller reason than the four before
// it. The pool's collision block hands surfaceProps straight to
// CEffectData::m_nSurfaceProp without interpreting it, so rows C4 and C5 do not
// need this. What needs it is cs_cl_te_trace, the probe that CONFIRMS the
// surfaceProps out lane CX added and could not verify: an index printed as a
// number cannot be told apart from a wrong one, and an index turned into the
// material the surface visibly is can.
//
// physprops is the shared IPhysicsSurfaceProps and answers from the same
// surfaceprops script on both sides, so the two arms agree by construction.
//-----------------------------------------------------------------------------
int32_t SourceSharp_SurfacePropGameMaterial( int32_t nSurfaceProp )
{
	if ( !physprops || nSurfaceProp < 0 )
		return 0;

	surfacedata_t *pSurface = physprops->GetSurfaceData( nSurfaceProp );

	return pSurface ? pSurface->game.material : 0;
}
