//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

//========= SourceSharp — what is left of c_effects.cpp: the wind's answer ==//
//
// This file was stock's game/client/c_effects.cpp, moved whole. All of it is
// C# now except the wind's native answer below:
//   * CClient_Precipitation's rain, snow and ash, and CSnowFallManager's
//     snowfall, went to SourceSharp.Client.Effects.PrecipitationSystem
//     (plan_client_novalve C2, lane c2-sims). Stock run offline is the oracle
//     (src/test/oracle/native/precipitation_oracle_test.cpp,
//     PrecipitationOracleTests). The tracer and splash executor, and the
//     engine reads, are in shell_precipitation_client.cpp.
//   * C_EnvWind's simulation went to WindSim (ClientWinds).
//   * C_Embers went to EmbersSim.
//   * C_QuadraticBeam went to SourceSharp_DrawBeamQuadratic.
//=============================================================================//

#include "cbase.h"
#include "shell_standalone_client.h"   // SourceSharp_LocalPlayerShell, for cs_cl_wind_report
#include "materialsystem/imaterialsystem.h"
#include "renderparm.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//=============================================================================//
// THE WIND, PUBLISHED (plan_client_novalve C2, lane c2-sims).
//
// CEnvWindShared's simulation -- the two seeded streams, the gust state
// machine, the ramp, the variation and the tree sway -- is C#'s
// (SourceSharp.Client.Effects.WindSim, replayed bit for bit against the stock
// simulation in WindSimOracleTests). The client env_wind entities own their
// sims and the client module thinks them once a frame (ClientWinds); what
// they publish lands here, through IShimEffectsRenderClient::WindPublish.
//
// WHAT STAYS NATIVE, AND WHY. c_rope.cpp, particles_simple.cpp,
// c_func_dust.cpp and c_te_legacytempents.cpp all stay and all read the wind
// through GetWindspeedAtTime (plan_entity.md §6), so the value stays readable
// natively: a plain intra-DLL call that answers the published vector, which is
// stock's head controller's m_currentWindVector. The tree sway's shader
// parameter is set here too, because the render context is native.
//
// GetWindspeedAtLocation answers zero, as it did before the move: it reads
// m_location and m_windRadius, which nothing in a SourceSharp build ever
// assigned, so no controller ever claimed a point and none was global.
//=============================================================================//

static Vector s_vecPublishedWind( 0, 0, 0 );
static int s_nPublishedWinds = 0;

//-----------------------------------------------------------------------------
// IShimEffectsRenderClient::WindPublish: the head wind's vector (what
// GetWindspeedAtTime answers), the controller count, and, when a wind thought
// this frame, the tree-sway vector stock's UpdateTreeSway handed the shaders.
//-----------------------------------------------------------------------------
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
		pRenderContext->SetVectorRenderingParameter( VECTOR_RENDERPARM_WIND_DIRECTION, Vector( pSway[0], pSway[1], pSway[2] ) );
	}
}

// Stock's level-init reset: every controller's vector to zero until it next
// thinks (the client module republishes on its next frame).
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

//-----------------------------------------------------------------------------
// cs_cl_wind_report: the vector GetWindspeedAtTime hands the four §6 readers
// this frame, and how many winds published it. Instrumentation for
// tools/f-wind-gate; prints and changes nothing.
//-----------------------------------------------------------------------------
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
		s_nPublishedWinds,
		vecAtTime.x, vecAtTime.y, vecAtTime.z, vecAtTime.Length(),
		vecAtLocal.x, vecAtLocal.y, vecAtLocal.z );
}
