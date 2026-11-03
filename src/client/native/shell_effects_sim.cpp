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
#include "shell_standalone_clients.h"
#include "shell_wind_client.h"

// The wind, as published by managed's WindSim. Native readers call
// GetWindspeedAtTime, which answers the published head wind vector; the tree
// sway shader parameter is set here because the render context is native.
// GetWindspeedAtLocation answers zero: no controller ever claims a point.

// IShimEffectsRenderClient::WindPublish: the head wind's vector, the controller
// count, and, when a wind thought this frame, the tree-sway vector for the shaders.
void CWindClient::WindPublish(const float* pWind, const float* pSway, int32_t nWinds)
{
	m_nPublishedWinds = nWinds > 0 ? nWinds : 0;
	if (pWind && m_nPublishedWinds > 0)
		m_vecPublishedWind.Init(pWind[0], pWind[1], pWind[2]);
	else
		m_vecPublishedWind.Init(0, 0, 0);

	if (pSway)
	{
		CMatRenderContextPtr pRenderContext(g_pMaterialSystem);
		pRenderContext->SetVectorRenderingParameter(VECTOR_RENDERPARM_WIND_DIRECTION,
													Vector(pSway[0], pSway[1], pSway[2]));
	}
}

// cs_cl_wind_report: the vector GetWindspeedAtTime answers this frame, and how
// many winds published it.
void CWindClient::ReportCommand(const CCommand& args)
{
	Vector vecAtTime;
	vecAtTime = m_vecPublishedWind;

	// No controller ever claims a point.
	const Vector vecAtLocal(0, 0, 0);
	Msg("[SourceSharp/Client] cs_cl_wind_report: %d controller(s)  "
		"at_time (%.4f %.4f %.4f) len %.4f  at_local (%.4f %.4f %.4f)\n",
		m_nPublishedWinds, vecAtTime.x, vecAtTime.y, vecAtTime.z, vecAtTime.Length(), vecAtLocal.x, vecAtLocal.y,
		vecAtLocal.z);
}

// What FTENT_WINDBLOWN accelerates towards; zero with no wind.
void CWindClient::ClientWindSpeed(float flTime, float* pOutXYZ)
{
	if (!pOutXYZ)
		return;

	pOutXYZ[0] = m_vecPublishedWind.x;
	pOutXYZ[1] = m_vecPublishedWind.y;
	pOutXYZ[2] = m_vecPublishedWind.z;
}
