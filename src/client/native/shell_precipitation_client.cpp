//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The client weather's native half; the weather itself is managed's
// PrecipitationSystem.
//   CPrecipitationClient::Draw  viewrender.cpp calls it in its two render passes. It
//                      hands managed the view and carries out the plan that
//                      comes back: each water ripple, then each tracer, in view
//                      space.
//   IShimPrecipitationClient's bodies: two traces, the point contents, the PVS,
//                      the local player, the world's bounds and the screen width.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "viewrender.h"
#include "fx_water.h"

#include "clienteffectprecachesystem.h"

#include "particles/particles.h"

#include "cdll_util.h"

#include "util_shared.h"

#include "shell_standalone_client.h"
#include "imanaged_areas_client.h"
#include "shell_precipitation_draw_client.h"
#include "shell_standalone_clients.h"
#include "shell_precipitation_queries_client.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

namespace
{

// The plan's layouts (PrecipitationRender's floats).
const int kViewFloats = 24; // world-to-view 16, view forward 3, view origin 3, view id, draw flags
const int kTracerFloats = 10; // start 3, delta 3, width, alpha, material (0 rain, 1 snow), volume ordinal
const int kSplashFloats = 6; // origin 3, scale, lifetime, alpha
const int kMaxTracers = 16384;
const int kMaxSplashes = 64;

IMaterial* TracerMaterial(IMaterialSystem* pMaterials, int nMaterial)
{
	return pMaterials->FindMaterial(nMaterial == 1 ? "particle/snow" : "particle/rain", TEXTURE_GROUP_CLIENT_EFFECTS);
}

} // namespace

struct CPrecipitationClient::Plan_t
{
	float Tracers[kMaxTracers * kTracerFloats];
	float Splashes[kMaxSplashes * kSplashFloats];

	// The render's tracers in IManagedParticlesClient::TracerQuads' layout (start 3, delta 3,
	// width), and the corners and verdicts it answers, asked once a render for the whole plan.
	float TracerIn[kMaxTracers * 7];
	Vector TracerVerts[kMaxTracers * 4];
	uint8_t TracerValid[kMaxTracers];
};

CPrecipitationClient::~CPrecipitationClient()
{
	delete m_pPlan;
}

// viewrender.cpp calls this in each pass that draws the weather.
void CPrecipitationClient::Draw()
{
	if (!m_pPlan)
		m_pPlan = new Plan_t;
	Plan_t* const pPlan = m_pPlan;
	IManagedParticlesClient* const pParticles = m_Areas.m_pParticlesClient;

	IManagedPrecipitationClient* pApi = m_Areas.m_pPrecipitationClient;
	if (!pApi)
		return;

	float flView[kViewFloats];
	const VMatrix& m = m_Cameras.CurrentWorldToView();
	for (int r = 0; r < 4; ++r)
		for (int c = 0; c < 4; ++c)
			flView[r * 4 + c] = m[r][c];
	const Vector& fwd = m_Cameras.CurrentForward();
	const Vector& org = m_Cameras.CurrentOrigin();
	flView[16] = fwd.x;
	flView[17] = fwd.y;
	flView[18] = fwd.z;
	flView[19] = org.x;
	flView[20] = org.y;
	flView[21] = org.z;
	flView[22] = (float)m_Cameras.CurrentID();
	flView[23] = (float)view->GetDrawFlags();

	int32_t nSplashes = 0;
	const int32_t nTracers =
		pApi->PrecipitationRender(flView, pPlan->Tracers, kMaxTracers, pPlan->Splashes, kMaxSplashes, &nSplashes);

	for (int i = 0; i < nSplashes && i < kMaxSplashes; ++i)
	{
		const float* s = &pPlan->Splashes[i * kSplashFloats];
		Vector vColor(0.5f, 0.5f, 0.5f);
		if (pParticles)
			pParticles->WaterSplash(2, s, NULL, s[3], vColor.Base(), s[4], s[5]);
	}

	if (nTracers <= 0)
		return;

	// Every tracer's corners in one crossing, before the render context is touched.
	const int nCount = MIN(nTracers, kMaxTracers);
	for (int i = 0; i < nCount; ++i)
	{
		for (int k = 0; k < 7; ++k)
			pPlan->TracerIn[i * 7 + k] = pPlan->Tracers[i * kTracerFloats + k];
	}
	if (!pParticles ||
		pParticles->TracerQuads(nCount, pPlan->TracerIn, pPlan->TracerVerts[0].Base(), pPlan->TracerValid) != nCount)
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);

	// In view space, as stock's Render: the identity view, and the user clip
	// planes kept on the real one.
	VMatrix tempView;
	pRenderContext->GetMatrix(MATERIAL_VIEW, &tempView);
	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->LoadIdentity();
	pRenderContext->EnableUserClipTransformOverride(true);
	pRenderContext->UserClipTransform(tempView);

	// One mesh per volume, as stock drew each volume's tracers in one batch.
	int iStart = 0;
	while (iStart < nCount)
	{
		const float flVolume = pPlan->Tracers[iStart * kTracerFloats + 9];
		int iEnd = iStart;
		while (iEnd < nCount && pPlan->Tracers[iEnd * kTracerFloats + 9] == flVolume)
			++iEnd;

		pRenderContext->Bind(TracerMaterial(m_Engines.m_pMaterials, (int)pPlan->Tracers[iStart * kTracerFloats + 8]));
		IMesh* pMesh = pRenderContext->GetDynamicMesh();
		if (pMesh)
		{
			CMeshBuilder mb;
			mb.Begin(pMesh, MATERIAL_QUADS, iEnd - iStart);
			// Wound as Tracer_Draw did: corners 0, 1, 3, 2; u 0, 1, 1, 0; v 1; a
			// tracer behind the eye writes nothing.
			static const int s_nCorner[4] = {0, 1, 3, 2};
			static const float s_flU[4] = {0.0f, 1.0f, 1.0f, 0.0f};
			for (int i = iStart; i < iEnd; ++i)
			{
				if (!pPlan->TracerValid[i])
					continue;
				const float flColor[4] = {1, 1, 1, pPlan->Tracers[i * kTracerFloats + 7]};
				for (int k = 0; k < 4; ++k)
				{
					const Vector& v = pPlan->TracerVerts[i * 4 + s_nCorner[k]];
					mb.Position3f(v.x, v.y, v.z);
					mb.TexCoord2f(0, s_flU[k], 1.0f);
					mb.Color4fv(flColor);
					mb.AdvanceVertex();
				}
			}
			mb.End(false, true);
		}

		iStart = iEnd;
	}

	pRenderContext->EnableUserClipTransformOverride(false);
	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->LoadMatrix(tempView);
}

// IShimPrecipitationClient: the world the weather asks.

// UTIL_TraceLine with stock's mask, through CTraceFilterSimple, ignoring the
// local player when asked (the ash's forward trace). Out: fraction, end xyz,
// surface flags.
int32_t CPrecipitationQueriesClient::PrecipitationTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask,
															int32_t bIgnorePlayer, float* pOut)
{
	if (!pStartXYZ || !pEndXYZ || !pOut)
		return 0;

	IHandleEntity* pIgnore = NULL;
	if (bIgnorePlayer)
	{
		if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
			pIgnore = static_cast<IClientUnknown*>(pPlayer);
	}

	trace_t tr;
	CTraceFilterSimple filter(pIgnore, COLLISION_GROUP_NONE);
	UTIL_TraceLine(Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]), Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]),
				   (unsigned int)nMask, &filter, &tr);
	pOut[0] = tr.fraction;
	pOut[1] = tr.endpos.x;
	pOut[2] = tr.endpos.y;
	pOut[3] = tr.endpos.z;
	pOut[4] = (float)tr.surface.flags;
	return 5;
}

// UTIL_TraceModel against the volume's own brush: ClipRayToEntity, MASK_ALL.
// Out: hit (the volume), fraction, start solid, end xyz.
int32_t CPrecipitationQueriesClient::PrecipitationTraceVolume(uint32_t hOwner, const float* pStartXYZ,
															  const float* pEndXYZ, const float* pHullMinXYZ,
															  const float* pHullMaxXYZ, float* pOut)
{
	if (!pStartXYZ || !pEndXYZ || !pHullMinXYZ || !pHullMaxXYZ || !pOut)
		return 0;

	const Vector vecStart(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]);
	const Vector vecEnd(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]);
	trace_t tr;
	memset((void*)&tr, 0, sizeof(tr));
	tr.fraction = 1.0f;
	tr.endpos = vecEnd;

	// Stock's ShouldCollide( group, MASK_ALL ) cull is true for every entity
	// (only debris refuses, and MASK_ALL carries CONTENTS_DEBRIS), so it is not asked.
	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);
	if (pOwner)
	{
		Ray_t ray;
		ray.Init(vecStart, vecEnd, Vector(pHullMinXYZ[0], pHullMinXYZ[1], pHullMinXYZ[2]),
				 Vector(pHullMaxXYZ[0], pHullMaxXYZ[1], pHullMaxXYZ[2]));
		m_Engines.m_pTrace->ClipRayToEntity(ray, MASK_ALL, static_cast<IClientUnknown*>(pOwner), &tr);
	}

	const bool bHit = (tr.fraction < 1.0f || tr.startsolid) && tr.m_pEnt != NULL;
	pOut[0] = bHit ? 1.0f : 0.0f;
	pOut[1] = tr.fraction;
	pOut[2] = tr.startsolid ? 1.0f : 0.0f;
	pOut[3] = tr.endpos.x;
	pOut[4] = tr.endpos.y;
	pOut[5] = tr.endpos.z;
	return 6;
}

int32_t CPrecipitationQueriesClient::PrecipitationPointContents(const float* pXYZ)
{
	return pXYZ ? m_Engines.m_pTrace->GetPointContents(Vector(pXYZ[0], pXYZ[1], pXYZ[2])) : 0;
}

int32_t CPrecipitationQueriesClient::PrecipitationInPvs(uint32_t hOwner)
{
	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);
	return pOwner && g_pClientLeafSystem->IsRenderableInPVS(pOwner->GetClientRenderable()) ? 1 : 0;
}

// The local player as the weather reads it: origin xyz, eye xyz, view yaw,
// world-space center xyz, FOV, default FOV. 0 with no local player.
int32_t CPrecipitationQueriesClient::PrecipitationPlayer(float* pOut)
{
	C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer();
	if (!pOut || !pPlayer)
		return 0;

	const Vector origin = pPlayer->GetAbsOrigin();
	const Vector eye = pPlayer->EyePosition();
	QAngle angles;
	m_Engines.m_pEngine->GetViewAngles(angles);
	const Vector center = origin + (pPlayer->OBBMins() + pPlayer->OBBMaxs()) * 0.5f;

	pOut[0] = origin.x;
	pOut[1] = origin.y;
	pOut[2] = origin.z;
	pOut[3] = eye.x;
	pOut[4] = eye.y;
	pOut[5] = eye.z;
	pOut[6] = angles.y;
	pOut[7] = center.x;
	pOut[8] = center.y;
	pOut[9] = center.z;
	pOut[10] = view->GetPlayerViewSetup()->fov;
	pOut[11] = m_DefaultFov.IsValid() ? (float)m_DefaultFov.GetInt() : 90.0f;
	return 1;
}

void CPrecipitationQueriesClient::PrecipitationWorldBounds(float* pOut6)
{
	if (!pOut6)
		return;
	C_World* pWorld = GetClientWorldEntity();
	const Vector mins = pWorld ? pWorld->GetWorldMins() : vec3_origin;
	const Vector maxs = pWorld ? pWorld->GetWorldMaxs() : vec3_origin;
	pOut6[0] = mins.x;
	pOut6[1] = mins.y;
	pOut6[2] = mins.z;
	pOut6[3] = maxs.x;
	pOut6[4] = maxs.y;
	pOut6[5] = maxs.z;
}

int32_t CPrecipitationQueriesClient::PrecipitationScreenWidth()
{
	return ScreenWidth();
}
