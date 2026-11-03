//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// Free functions client.so still names whose stock files are not built.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "view.h"
#include "view_shared.h"
#include "engine/ivmodelinfo.h"

#include "mathlib_port.h"

#include "shell_standalone_clients.h"
#include "shell_standalone_client.h"
#include "client_entity_list.h"

class C_BasePlayer;

// c_func_reflective_glass.cpp's IsReflectiveGlassInView over the shells: the
// view's frustum against every awake func_reflective_glass's brush planes, the
// first plane whose front faces the eye carried out.
bool IsReflectiveGlassInView(CStandaloneClients& clients, const CViewSetup& view, cplane_t& plane)
{
	const CClientEngineInterfaces& engines = clients.Engines();
	if (!engines.m_pModelInfo)
		return false;

	Frustum_t frustum;
	const float fov = view.fov, aspect = view.m_flAspectRatio;
	ss_mathlib::GeneratePerspectiveFrustum(view.origin, view.angles, view.zNear, view.zFar, fov, aspect, frustum);

	cplane_t localPlane;
	Vector vecOrigin, vecWorld, vecDelta, vecForward;
	ss_mathlib::AngleVectors(view.angles, &vecForward, NULL, NULL);

	CClientEntityList& entities = clients.EntityList();
	const int nHighest = entities.GetHighestEntityIndex();
	for (int i = 0; i <= nHighest; ++i)
	{
		C_StandaloneEntity* pReflectiveGlass = entities.ShellFromIndex(i);
		if (!pReflectiveGlass || pReflectiveGlass->IsDormant())
			continue;

		if (Q_stricmp(pReflectiveGlass->m_szClassname, "func_reflective_glass"))
			continue;

		Vector vecMins, vecMaxs;
		pReflectiveGlass->GetRenderBoundsWorldspace(vecMins, vecMaxs);
		if (ss_mathlib::R_CullBox(vecMins, vecMaxs, frustum))
			continue;

		const model_t* pModel = pReflectiveGlass->GetModel();
		const matrix3x4_t& mat = pReflectiveGlass->CollisionToWorldTransform();

		int nCount = engines.m_pModelInfo->GetBrushModelPlaneCount(pModel);
		for (int j = 0; j < nCount; ++j)
		{
			engines.m_pModelInfo->GetBrushModelPlane(pModel, j, localPlane, &vecOrigin);

			ss_mathlib::MatrixTransformPlane(mat, localPlane, plane); // Transform to world space
			ss_mathlib::VectorTransform(vecOrigin.Base(), mat, vecWorld.Base());

			if (view.origin.Dot(plane.normal) <= plane.dist) // Check for view behind plane
				continue;

			VectorSubtract(vecWorld, view.origin, vecDelta); // Backface cull
			if (vecDelta.Dot(plane.normal) >= 0)
				continue;

			return true;
		}
	}

	return false;
}
