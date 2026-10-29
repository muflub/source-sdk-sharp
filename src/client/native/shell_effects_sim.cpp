//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

// The wind's native answer; the rest of stock's c_effects.cpp is managed.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "renderparm.h"

#include "shell_standalone_client.h"

// The wind, as published by managed's WindSim. Native readers call
// GetWindspeedAtTime, which answers the published head wind vector; the tree
// sway shader parameter is set here because the render context is native.
// GetWindspeedAtLocation answers zero: no controller ever claims a point.

static Vector s_vecPublishedWind( 0, 0, 0 );
static int s_nPublishedWinds = 0;

// IShimEffectsRenderClient::WindPublish: the head wind's vector, the controller
// count, and, when a wind thought this frame, the tree-sway vector for the shaders.
void SourceSharp_WindPublish( const float *pWind, const float *pSway, int32_t nWinds )
{
	s_nPublishedWinds = nWinds > 0 ? nWinds : 0;
	if ( pWind && s_nPublishedWinds > 0 )
		s_vecPublishedWind.Init( pWind[0], pWind[1], pWind[2] );
	else
		s_vecPublishedWind.Init( 0, 0, 0 );

	if ( pSway )
	{
		CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
		pRenderContext->SetVectorRenderingParameter( VECTOR_RENDERPARM_WIND_DIRECTION,
													 Vector( pSway[0], pSway[1], pSway[2] ) );
	}
}

// Stock's level-init reset: every controller's vector to zero until it next thinks.
void ResetWindspeed()
{
	s_vecPublishedWind.Init( 0, 0, 0 );
}

Vector GetWindspeedAtLocation( const Vector &location )
{
	return Vector( 0, 0, 0 );
}

void GetWindspeedAtTime( float flTime, Vector &vecVelocity )
{
	VectorCopy( s_vecPublishedWind, vecVelocity );
}

// cs_cl_wind_report: the vector GetWindspeedAtTime answers this frame, and how
// many winds published it.
CON_COMMAND( cs_cl_wind_report,
			 "the wind vector GetWindspeedAtTime hands the four section-6 readers, and the controllers behind it" )
{
	Vector vecAtTime;
	GetWindspeedAtTime( gpGlobals->curtime, vecAtTime );

	Vector vecAtLocal( 0, 0, 0 );
	if ( C_StandaloneEntity *pShell = SourceSharp_LocalPlayerShell() )
		vecAtLocal = GetWindspeedAtLocation( pShell->GetAbsOrigin() );

	Msg( "[SourceSharp/Client] cs_cl_wind_report: %d controller(s)  "
		 "at_time (%.4f %.4f %.4f) len %.4f  at_local (%.4f %.4f %.4f)\n",
		 s_nPublishedWinds, vecAtTime.x, vecAtTime.y, vecAtTime.z, vecAtTime.Length(), vecAtLocal.x, vecAtLocal.y,
		 vecAtLocal.z );
}
