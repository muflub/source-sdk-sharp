//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client weather's native half ===================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C2 (lane c2-sims). Stock's client weather --
// CClient_Precipitation's rain, snow and ash and CSnowFallManager's snowfall,
// all of c_effects.cpp's that is not the wind -- is C#'s:
// SourceSharp.Client.Effects.PrecipitationSystem, held bit for bit to stock
// run offline (PrecipitationOracleTests, Fixtures/precipitation-stock.tsv; the
// oracle is src/test/oracle/native/precipitation_oracle_test.cpp).
//
// What is here:
//
//   DrawPrecipitation, which the fork's viewrender.cpp calls in its two
//     render passes. It hands managed the view (IManagedPrecipitationClient::
//     PrecipitationRender) and carries out the plan that comes back: each
//     water ripple, then each tracer, in view space, with stock's render
//     context dance around them. The ash and the snowfall are the particle
//     executor's (shell_managed_particles.cpp).
//
//   IShimPrecipitationClient's bodies: the world the weather asks -- two
//     traces, the point contents, the PVS, the local player, the world's
//     bounds and the screen width. Each is one engine read.
//=============================================================================//

#include "cbase.h"
#include "view.h"
#include "iviewrender.h"
#include "viewrender.h"
#include "fx_water.h"
#include "c_world.h"
#include "engine/IEngineTrace.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystem.h"
#include "clienteffectprecachesystem.h"
#include "shell_standalone_client.h"
#include "imanaged_areas_client.h"
#include "sourcesharp_entrypoints.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// The two tracer materials, precached as stock's CLIENTEFFECT list did.
CLIENTEFFECT_REGISTER_BEGIN( PrecachePrecipitationTracers )
CLIENTEFFECT_MATERIAL( "particle/rain" )
CLIENTEFFECT_MATERIAL( "particle/snow" )
CLIENTEFFECT_REGISTER_END()

namespace
{

// The plan's layouts (PrecipitationRender's floats).
const int kViewFloats = 24;     // world-to-view 16, view forward 3, view origin 3, view id, draw flags
const int kTracerFloats = 10;   // start 3, delta 3, width, alpha, material (0 rain, 1 snow), volume ordinal
const int kSplashFloats = 6;    // origin 3, scale, lifetime, alpha
const int kMaxTracers = 16384;
const int kMaxSplashes = 64;

float s_Tracers[ kMaxTracers * kTracerFloats ];
float s_Splashes[ kMaxSplashes * kSplashFloats ];

// The render's tracers in IManagedParticlesClient::TracerQuads' layout (start
// 3, delta 3, width), and the corners and verdicts it answers: c_tracer.cpp's
// Tracer_ComputeVerts is TracerGeometry's (C#, lane c5-particles), asked once a
// render for the whole plan, not once a streak.
float s_TracerIn[ kMaxTracers * 7 ];
Vector s_TracerVerts[ kMaxTracers * 4 ];
uint8_t s_TracerValid[ kMaxTracers ];

IMaterial *TracerMaterial( int nMaterial )
{
	return materials->FindMaterial( nMaterial == 1 ? "particle/snow" : "particle/rain", TEXTURE_GROUP_CLIENT_EFFECTS );
}

} // namespace

//-----------------------------------------------------------------------------
// The fork's viewrender.cpp calls this in each pass that draws the weather.
//-----------------------------------------------------------------------------
void DrawPrecipitation()
{
	IManagedPrecipitationClient *pApi = ManagedPrecipitationClient();
	if ( !pApi )
		return;

	float flView[ kViewFloats ];
	const VMatrix &m = CurrentWorldToViewMatrix();
	for ( int r = 0; r < 4; ++r )
		for ( int c = 0; c < 4; ++c )
			flView[ r * 4 + c ] = m[ r ][ c ];
	const Vector &fwd = CurrentViewForward();
	const Vector &org = CurrentViewOrigin();
	flView[ 16 ] = fwd.x; flView[ 17 ] = fwd.y; flView[ 18 ] = fwd.z;
	flView[ 19 ] = org.x; flView[ 20 ] = org.y; flView[ 21 ] = org.z;
	flView[ 22 ] = (float)CurrentViewID();
	flView[ 23 ] = (float)view->GetDrawFlags();

	int32_t nSplashes = 0;
	const int32_t nTracers = pApi->PrecipitationRender( flView, s_Tracers, kMaxTracers, s_Splashes, kMaxSplashes, &nSplashes );

	for ( int i = 0; i < nSplashes && i < kMaxSplashes; ++i )
	{
		const float *s = &s_Splashes[ i * kSplashFloats ];
		Vector vColor( 0.5f, 0.5f, 0.5f );
		FX_WaterRipple( Vector( s[ 0 ], s[ 1 ], s[ 2 ] ), s[ 3 ], &vColor, s[ 4 ], s[ 5 ] );
	}

	if ( nTracers <= 0 )
		return;

	// Every tracer's corners in one crossing (TracerGeometry, C#), before the
	// render context is touched.
	const int nCount = MIN( nTracers, kMaxTracers );
	for ( int i = 0; i < nCount; ++i )
	{
		for ( int k = 0; k < 7; ++k )
			s_TracerIn[ i * 7 + k ] = s_Tracers[ i * kTracerFloats + k ];
	}
	IManagedParticlesClient *pParticles = ManagedParticlesClient();
	if ( !pParticles || pParticles->TracerQuads( nCount, s_TracerIn, s_TracerVerts[ 0 ].Base(), s_TracerValid ) != nCount )
		return;

	CMatRenderContextPtr pRenderContext( materials );

	// In view space, as stock's Render: the identity view, and the user clip
	// planes kept on the real one.
	VMatrix tempView;
	pRenderContext->GetMatrix( MATERIAL_VIEW, &tempView );
	pRenderContext->MatrixMode( MATERIAL_VIEW );
	pRenderContext->LoadIdentity();
	pRenderContext->EnableUserClipTransformOverride( true );
	pRenderContext->UserClipTransform( tempView );

	// One mesh per volume, as stock drew each volume's tracers in one batch.
	int iStart = 0;
	while ( iStart < nCount )
	{
		const float flVolume = s_Tracers[ iStart * kTracerFloats + 9 ];
		int iEnd = iStart;
		while ( iEnd < nCount && s_Tracers[ iEnd * kTracerFloats + 9 ] == flVolume )
			++iEnd;

		pRenderContext->Bind( TracerMaterial( (int)s_Tracers[ iStart * kTracerFloats + 8 ] ) );
		IMesh *pMesh = pRenderContext->GetDynamicMesh();
		if ( pMesh )
		{
			CMeshBuilder mb;
			mb.Begin( pMesh, MATERIAL_QUADS, iEnd - iStart );
			// Tracer_Draw( &mb, start, delta, width, { 1, 1, 1, alpha }, 1 ) as it
			// wound each quad: corners 0, 1, 3, 2; u 0, 1, 1, 0; v 1 (its startV,
			// and endV defaulted to 1); a tracer behind the eye writes nothing.
			static const int s_nCorner[ 4 ] = { 0, 1, 3, 2 };
			static const float s_flU[ 4 ] = { 0.0f, 1.0f, 1.0f, 0.0f };
			for ( int i = iStart; i < iEnd; ++i )
			{
				if ( !s_TracerValid[ i ] )
					continue;
				const float flColor[ 4 ] = { 1, 1, 1, s_Tracers[ i * kTracerFloats + 7 ] };
				for ( int k = 0; k < 4; ++k )
				{
					const Vector &v = s_TracerVerts[ i * 4 + s_nCorner[ k ] ];
					mb.Position3f( v.x, v.y, v.z );
					mb.TexCoord2f( 0, s_flU[ k ], 1.0f );
					mb.Color4fv( flColor );
					mb.AdvanceVertex();
				}
			}
			mb.End( false, true );
		}

		iStart = iEnd;
	}

	pRenderContext->EnableUserClipTransformOverride( false );
	pRenderContext->MatrixMode( MATERIAL_VIEW );
	pRenderContext->LoadMatrix( tempView );
}

//-----------------------------------------------------------------------------
// IShimPrecipitationClient: the world the weather asks.
//-----------------------------------------------------------------------------

// UTIL_TraceLine with stock's mask, through CTraceFilterSimple, ignoring the
// local player when asked (the ash's forward trace). Out: fraction, end xyz,
// surface flags.
int32_t SourceSharp_PrecipitationTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask,
                                            int32_t bIgnorePlayer, float *pOut )
{
	if ( !pStartXYZ || !pEndXYZ || !pOut )
		return 0;

	IHandleEntity *pIgnore = NULL;
	if ( bIgnorePlayer )
	{
		if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
			pIgnore = static_cast< IClientUnknown * >( pPlayer );
	}

	trace_t tr;
	CTraceFilterSimple filter( pIgnore, COLLISION_GROUP_NONE );
	UTIL_TraceLine( Vector( pStartXYZ[ 0 ], pStartXYZ[ 1 ], pStartXYZ[ 2 ] ),
	                Vector( pEndXYZ[ 0 ], pEndXYZ[ 1 ], pEndXYZ[ 2 ] ), (unsigned int)nMask, &filter, &tr );
	pOut[ 0 ] = tr.fraction;
	pOut[ 1 ] = tr.endpos.x;
	pOut[ 2 ] = tr.endpos.y;
	pOut[ 3 ] = tr.endpos.z;
	pOut[ 4 ] = (float)tr.surface.flags;
	return 5;
}

// UTIL_TraceModel against the volume's own brush: ClipRayToEntity, MASK_ALL.
// Out: hit (the volume), fraction, start solid, end xyz.
int32_t SourceSharp_PrecipitationTraceVolume( uint32_t hOwner, const float *pStartXYZ, const float *pEndXYZ,
                                              const float *pHullMinXYZ, const float *pHullMaxXYZ, float *pOut )
{
	if ( !pStartXYZ || !pEndXYZ || !pHullMinXYZ || !pHullMaxXYZ || !pOut )
		return 0;

	const Vector vecStart( pStartXYZ[ 0 ], pStartXYZ[ 1 ], pStartXYZ[ 2 ] );
	const Vector vecEnd( pEndXYZ[ 0 ], pEndXYZ[ 1 ], pEndXYZ[ 2 ] );
	trace_t tr;
	memset( (void *)&tr, 0, sizeof( tr ) );
	tr.fraction = 1.0f;
	tr.endpos = vecEnd;

	// Stock's ShouldCollide( group, MASK_ALL ) cull is true for every entity
	// (only debris refuses, and MASK_ALL carries CONTENTS_DEBRIS), so it is not asked.
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	if ( pOwner )
	{
		Ray_t ray;
		ray.Init( vecStart, vecEnd, Vector( pHullMinXYZ[ 0 ], pHullMinXYZ[ 1 ], pHullMinXYZ[ 2 ] ),
		          Vector( pHullMaxXYZ[ 0 ], pHullMaxXYZ[ 1 ], pHullMaxXYZ[ 2 ] ) );
		enginetrace->ClipRayToEntity( ray, MASK_ALL, static_cast< IClientUnknown * >( pOwner ), &tr );
	}

	const bool bHit = ( tr.fraction < 1.0f || tr.startsolid ) && tr.m_pEnt != NULL;
	pOut[ 0 ] = bHit ? 1.0f : 0.0f;
	pOut[ 1 ] = tr.fraction;
	pOut[ 2 ] = tr.startsolid ? 1.0f : 0.0f;
	pOut[ 3 ] = tr.endpos.x;
	pOut[ 4 ] = tr.endpos.y;
	pOut[ 5 ] = tr.endpos.z;
	return 6;
}

int32_t SourceSharp_PrecipitationPointContents( const float *pXYZ )
{
	return pXYZ ? enginetrace->GetPointContents( Vector( pXYZ[ 0 ], pXYZ[ 1 ], pXYZ[ 2 ] ) ) : 0;
}

int32_t SourceSharp_PrecipitationInPvs( uint32_t hOwner )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	return pOwner && g_pClientLeafSystem->IsRenderableInPVS( pOwner->GetClientRenderable() ) ? 1 : 0;
}

// The local player as the weather reads it: origin xyz, eye xyz, view yaw,
// world-space center xyz, FOV, default FOV. 0 with no local player.
int32_t SourceSharp_PrecipitationPlayer( float *pOut )
{
	C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell();
	if ( !pOut || !pPlayer )
		return 0;

	const Vector origin = pPlayer->GetAbsOrigin();
	const Vector eye = pPlayer->EyePosition();
	QAngle angles;
	engine->GetViewAngles( angles );
	const Vector center = origin + ( pPlayer->OBBMins() + pPlayer->OBBMaxs() ) * 0.5f;
	static ConVarRef default_fov( "default_fov" );

	pOut[ 0 ] = origin.x; pOut[ 1 ] = origin.y; pOut[ 2 ] = origin.z;
	pOut[ 3 ] = eye.x;    pOut[ 4 ] = eye.y;    pOut[ 5 ] = eye.z;
	pOut[ 6 ] = angles.y;
	pOut[ 7 ] = center.x; pOut[ 8 ] = center.y; pOut[ 9 ] = center.z;
	pOut[ 10 ] = view->GetPlayerViewSetup()->fov;
	pOut[ 11 ] = default_fov.IsValid() ? (float)default_fov.GetInt() : 90.0f;
	return 1;
}

void SourceSharp_PrecipitationWorldBounds( float *pOut6 )
{
	if ( !pOut6 )
		return;
	C_World *pWorld = GetClientWorldEntity();
	const Vector mins = pWorld ? pWorld->GetWorldMins() : vec3_origin;
	const Vector maxs = pWorld ? pWorld->GetWorldMaxs() : vec3_origin;
	pOut6[ 0 ] = mins.x; pOut6[ 1 ] = mins.y; pOut6[ 2 ] = mins.z;
	pOut6[ 3 ] = maxs.x; pOut6[ 4 ] = maxs.y; pOut6[ 5 ] = maxs.z;
}

int32_t SourceSharp_PrecipitationScreenWidth()
{
	return ScreenWidth();
}
