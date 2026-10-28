//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The impact effects' native half: the precache list, the PMaterial handles the
// native muzzle flash emitters draw with, and one engine call each for the
// managed bullet impact (a MASK_SHOT trace, the surface-lighting reads, the
// effect ledger's rows and the new-style impact particle).

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "trace_hit.h"
#include "fx.h"
#include "clienteffectprecachesystem.h"
#include "engine/IStaticPropMgr.h"
#include "engine/IEngineTrace.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier1/smartptr.h"
#include "basehandle.h"
#include "bspflags.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "cmodel.h"
#include "const.h"
#include "gametrace.h"
#include "icliententitylist.h"
#include "ihandleentity.h"
#include "particlemgr.h"
#include "trace.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "shell_new_particles.h"
#include "shell_particle_mgr.h"
#include "shell_effect_ledger.h"
#include "shell_cliententitylist.h"
#include "world_subject.h"
#include "slot_table.h"
#include "sourcesharp.h"
#include "shell_particle_systems.h"

CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectImpacts )
CLIENTEFFECT_MATERIAL( "effects/fleck_cement1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_cement2" )
CLIENTEFFECT_MATERIAL( "effects/fleck_antlion1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_antlion2" )
CLIENTEFFECT_MATERIAL( "effects/fleck_wood1" )
CLIENTEFFECT_MATERIAL( "effects/fleck_wood2" )
CLIENTEFFECT_MATERIAL( "effects/blood" )
CLIENTEFFECT_MATERIAL( "effects/blood2" )
CLIENTEFFECT_MATERIAL( "sprites/bloodspray" )
CLIENTEFFECT_MATERIAL( "particle/particle_noisesphere" )
CLIENTEFFECT_REGISTER_END()

// Cached handles to commonly used materials.
PMaterialHandle g_Mat_Fleck_Wood[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Cement[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Antlion[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Glass[2] = { NULL, NULL };
PMaterialHandle g_Mat_Fleck_Tile[2] = { NULL, NULL };
PMaterialHandle g_Mat_DustPuff[2] = { NULL, NULL };
PMaterialHandle g_Mat_BloodPuff[2] = { NULL, NULL };
PMaterialHandle g_Mat_SMG_Muzzleflash[4] = { NULL, NULL, NULL, NULL };
PMaterialHandle g_Mat_Combine_Muzzleflash[3] = { NULL, NULL, NULL };

void FX_CacheMaterialHandles( void )
{
	g_Mat_Fleck_Wood[0] = ShellParticleMaterial_Find( "effects/fleck_wood1" );
	g_Mat_Fleck_Wood[1] = ShellParticleMaterial_Find( "effects/fleck_wood2" );

	g_Mat_Fleck_Cement[0] = ShellParticleMaterial_Find( "effects/fleck_cement1");
	g_Mat_Fleck_Cement[1] = ShellParticleMaterial_Find( "effects/fleck_cement2" );

	g_Mat_Fleck_Antlion[0] = ShellParticleMaterial_Find( "effects/fleck_antlion1" );
	g_Mat_Fleck_Antlion[1] = ShellParticleMaterial_Find( "effects/fleck_antlion2" );

	g_Mat_Fleck_Glass[0] = ShellParticleMaterial_Find( "effects/fleck_glass1" );
	g_Mat_Fleck_Glass[1] = ShellParticleMaterial_Find( "effects/fleck_glass2" );

	g_Mat_Fleck_Tile[0] = ShellParticleMaterial_Find( "effects/fleck_tile1" );
	g_Mat_Fleck_Tile[1] = ShellParticleMaterial_Find( "effects/fleck_tile2" );

	g_Mat_DustPuff[0] = ShellParticleMaterial_Find( "particle/particle_smokegrenade" );
	g_Mat_DustPuff[1] = ShellParticleMaterial_Find( "particle/particle_noisesphere" );

	g_Mat_BloodPuff[0] = ShellParticleMaterial_Find( "effects/blood" );
	g_Mat_BloodPuff[1] = ShellParticleMaterial_Find( "effects/blood2" );
	
	g_Mat_SMG_Muzzleflash[0] = ShellParticleMaterial_Find( "effects/muzzleflash1" );
	g_Mat_SMG_Muzzleflash[1] = ShellParticleMaterial_Find( "effects/muzzleflash2" );
	g_Mat_SMG_Muzzleflash[2] = ShellParticleMaterial_Find( "effects/muzzleflash3" );
	g_Mat_SMG_Muzzleflash[3] = ShellParticleMaterial_Find( "effects/muzzleflash4" );
	g_Mat_Combine_Muzzleflash[0] = ShellParticleMaterial_Find( "effects/combinemuzzle1" );
	g_Mat_Combine_Muzzleflash[1] = ShellParticleMaterial_Find( "effects/combinemuzzle2" );
	g_Mat_Combine_Muzzleflash[2] = ShellParticleMaterial_Find( "effects/strider_muzzle" );
}

// A MASK_SHOT trace. nEntity >= 0 clips the ray to that entity and answers -1
// when the index names no client entity; a negative nEntity is a world trace.
//
// Out: floats [fraction, endpos xyz, plane normal xyz]; ints [DidHit, hitbox,
// the hit's entity index (-1 for none or a client-only entity), surfaceProps,
// surface flags, whether the hit is the world]. Answers DidHit (1 or 0).
int32_t SourceSharp_ClientShotTrace( const float *pStartXYZ, const float *pEndXYZ, int32_t nEntity,
                                     float *pOutFloats, int32_t nOutFloats, int32_t *pOutInts,
                                     int32_t nOutInts )
{
	if ( !pStartXYZ || !pEndXYZ || !pOutFloats || nOutFloats < 7 || !pOutInts || nOutInts < 6 )
		return -1;

	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );

	trace_t tr;
	memset( &tr, 0, sizeof( trace_t ) );
	tr.fraction = 1.0f;

	if ( nEntity >= 0 )
	{
		IClientEntity *pEntity = SourceSharp_ClientEntityFromIndex( nEntity );
		if ( !pEntity )
			return -1;

		Ray_t ray;
		ray.Init( vecStart, vecEnd );
		enginetrace->ClipRayToEntity( ray, MASK_SHOT, pEntity, &tr );
	}
	else
	{
		UTIL_TraceLine( vecStart, vecEnd, MASK_SHOT, NULL, COLLISION_GROUP_NONE, &tr );
	}

	pOutFloats[0] = tr.fraction;
	pOutFloats[1] = tr.endpos.x;
	pOutFloats[2] = tr.endpos.y;
	pOutFloats[3] = tr.endpos.z;
	pOutFloats[4] = tr.plane.normal.x;
	pOutFloats[5] = tr.plane.normal.y;
	pOutFloats[6] = tr.plane.normal.z;

	// The hit's index off its own handle, through the table that resolves both
	// hierarchies.
	int32_t nHit = -1;
	IHandleEntity *pHit = ss::TraceHit( tr );
	if ( pHit )
	{
		const CBaseHandle hHit = pHit->GetRefEHandle();
		nHit = SourceSharp_ClientIndexOfHit( hHit, SourceSharp_ClientNetworkableFromHandle( hHit ) != NULL );
	}

	const bool bHit = tr.DidHit();
	pOutInts[0] = bHit ? 1 : 0;
	pOutInts[1] = tr.hitbox;
	pOutInts[2] = nHit;
	pOutInts[3] = (int32_t)tr.surface.surfaceProps;
	pOutInts[4] = (int32_t)tr.surface.flags;
	pOutInts[5] = SourceSharp_IsTheWorld( ss::TraceHit( tr ) ) ? 1 : 0;

	return bHit ? 1 : 0;
}

// GetColorForSurface's world read; managed has already pushed the end 1.1
// along the ray, as stock does. Out: diffuse xyz, base xyz.
void SourceSharp_ClientWorldLighting( const float *pStartXYZ, const float *pEndXYZ, float *pOut6 )
{
	if ( !pStartXYZ || !pEndXYZ || !pOut6 )
		return;

	Vector vecDiffuse, vecBase;
	engine->TraceLineMaterialAndLighting( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ),
	                                      Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                                      vecDiffuse, vecBase );

	pOut6[0] = vecDiffuse.x;
	pOut6[1] = vecDiffuse.y;
	pOut6[2] = vecDiffuse.z;
	pOut6[3] = vecBase.x;
	pOut6[4] = vecBase.y;
	pOut6[5] = vecBase.z;
}

// GetColorForSurface's static-prop read, over a trace holding the start, end
// and plane normal the engine reads.
void SourceSharp_ClientStaticPropLighting( const float *pStartXYZ, const float *pEndXYZ,
                                           const float *pNormalXYZ, int32_t nStaticProp, float *pOut6 )
{
	if ( !pStartXYZ || !pEndXYZ || !pNormalXYZ || !pOut6 )
		return;

	trace_t tr;
	memset( &tr, 0, sizeof( trace_t ) );
	tr.startpos.Init( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	tr.endpos.Init( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );
	tr.plane.normal.Init( pNormalXYZ[0], pNormalXYZ[1], pNormalXYZ[2] );

	Vector vecDiffuse, vecBase;
	staticpropmgr->GetStaticPropMaterialColorAndLighting( &tr, nStaticProp, vecDiffuse, vecBase );

	pOut6[0] = vecDiffuse.x;
	pOut6[1] = vecDiffuse.y;
	pOut6[2] = vecDiffuse.z;
	pOut6[3] = vecBase.x;
	pOut6[4] = vecBase.y;
	pOut6[5] = vecBase.z;
}

// The effect ledger's two impact rows: the flecks and dust particles a managed
// impact started.
void SourceSharp_ClientImpactLedger( int32_t nFlecks, int32_t nDust )
{
	if ( nFlecks > 0 )
		g_SourceSharpEffects.nImpactFlecks += nFlecks;
	if ( nDust > 0 )
		g_SourceSharpEffects.nImpactDust += nDust;
}

// The new-style impact particle: control points 0..2 at the points and
// orientations managed computed (pPoints: three of position, forward, right,
// up), each bound to the entity hit, control point 3 the scale, and control
// point 4 the surface colour when the definition reads it. Answers 1 when a
// definition answered the name.
int32_t SourceSharp_ParticleSystemImpact( const char *pszName, const float *pPoints, int32_t nEntity,
                                          float flScale, const float *pColorXYZ )
{
	if ( !pszName || !pPoints )
		return 0;

	CSmartPtr<CShellNewParticleEffect> pEffect = CShellNewParticleEffect::Create( pszName );
	if ( !pEffect->IsValid() )
		return 0;

	// Watched as a one-shot; the manager still owns its life.
	ShellParticles_WatchOneShot( pEffect.GetObject() );

	// The shell the trace hit: GetGameEntity() answers nothing on the client.
	IClientUnknown *pHit = ( nEntity >= 0 ) ? SourceSharp_ClientUnknownFromIndex( nEntity ) : NULL;
	C_StandaloneEntity *pShell = pHit ? SourceSharp_ClientShellFromTraceHit( pHit ) : NULL;

	for ( int nPoint = 0; nPoint < 3; nPoint++ )
	{
		const float *p = pPoints + nPoint * 12;
		pEffect->SetControlPoint( nPoint, Vector( p[0], p[1], p[2] ) );
		pEffect->SetControlPointOrientation( nPoint, Vector( p[3], p[4], p[5] ),
		                                     Vector( p[6], p[7], p[8] ), Vector( p[9], p[10], p[11] ) );
		pEffect->SetControlPointEntity( nPoint, pShell );
	}

	pEffect->SetControlPoint( 3, Vector( flScale, flScale, flScale ) );

	if ( pColorXYZ && pEffect->DefinitionReadsControlPoint( 4 ) )
		pEffect->SetControlPoint( 4, Vector( pColorXYZ[0], pColorXYZ[1], pColorXYZ[2] ) );

	return 1;
}
