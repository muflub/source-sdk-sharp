//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Pixel visibility's native half: the occlusion query objects, named to managed
// code by id (0 for none), sphere diameters, the current view, and the commands
// managed code answers at a view's end, carried out.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "view.h"
#include "viewrender.h"
#include "materialsystem/imesh.h"
#include "clienteffectprecachesystem.h"

#include "imanaged_areas_client.h"

#include "shell_pixelvis_client.h"
#include "shell_effects_list_client.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

namespace
{

float PixelVisFloat(int32_t nBits)
{
	float f;
	memcpy(&f, &nBits, sizeof(f));
	return f;
}

} // namespace

OcclusionQueryObjectHandle_t CPixelVisClient::QueryOf(int32_t nQuery) const
{
	if (nQuery < 1 || nQuery > m_Queries.Count())
		return INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
	return m_Queries[nQuery - 1];
}

// One view's commands, in order.
void CPixelVisClient::Execute(const int32_t* pCmds, int nWords)
{
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	int i = 0;
	while (i < nWords)
	{
		const int32_t nKind = pCmds[i];
		if (nKind == SS_PIXELVIS_CMD_BIND && i + 2 <= nWords)
		{
			pRenderContext->Bind(m_Effects.MaterialOf(pCmds[i + 1]));
			i += 2;
		}
		else if (nKind == SS_PIXELVIS_CMD_DEPTHRANGE && i + 3 <= nWords)
		{
			pRenderContext->DepthRange(PixelVisFloat(pCmds[i + 1]), PixelVisFloat(pCmds[i + 2]));
			i += 3;
		}
		else if (nKind == SS_PIXELVIS_CMD_PROXY && i + SS_PIXELVIS_PROXY_WORDS <= nWords)
		{
			const OcclusionQueryObjectHandle_t hQuery = QueryOf(pCmds[i + 1]);
			IMaterial* pMaterial = m_Effects.MaterialOf(pCmds[i + 2]);
			const int32_t* pVerts = pCmds + i + 3;
			pRenderContext->BeginOcclusionQueryDrawing(hQuery);
			CMeshBuilder meshBuilder;
			IMesh* pMesh = pRenderContext->GetDynamicMesh(false, NULL, NULL, pMaterial);
			meshBuilder.Begin(pMesh, MATERIAL_TRIANGLES, 4);
			for (int v = 0; v < 12; v++)
			{
				const float pos[3] = {PixelVisFloat(pVerts[v * 3]), PixelVisFloat(pVerts[v * 3 + 1]),
									  PixelVisFloat(pVerts[v * 3 + 2])};
				meshBuilder.Position3fv(pos);
				meshBuilder.AdvanceVertex();
			}
			meshBuilder.End();
			pMesh->Draw();
			pRenderContext->EndOcclusionQueryDrawing(hQuery);
			i += SS_PIXELVIS_PROXY_WORDS;
		}
		else
		{
			Warning("[SourceSharp] pixel visibility: command %d at word %d of %d is not one; the rest is dropped\n",
					nKind, i, nWords);
			return;
		}
	}
}

int32_t CPixelVisClient::PixelVisQueryCreate()
{
	if (!m_Engines.m_pMaterials)
		return 0;
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	const OcclusionQueryObjectHandle_t h = pRenderContext->CreateOcclusionQueryObject();
	if (h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
		return 0;
	for (int i = 0; i < m_Queries.Count(); i++)
	{
		if (m_Queries[i] == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
		{
			m_Queries[i] = h;
			return i + 1;
		}
	}
	m_Queries.AddToTail(h);
	return m_Queries.Count();
}

void CPixelVisClient::PixelVisQueryDestroy(int32_t nQuery)
{
	const OcclusionQueryObjectHandle_t h = QueryOf(nQuery);
	if (h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE || !m_Engines.m_pMaterials)
		return;
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->DestroyOcclusionQueryObject(h);
	m_Queries[nQuery - 1] = INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
}

void CPixelVisClient::PixelVisQueryReset(int32_t nQuery)
{
	const OcclusionQueryObjectHandle_t h = QueryOf(nQuery);
	if (h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE || !m_Engines.m_pMaterials)
		return;
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->ResetOcclusionQueryObject(h);
}

void CPixelVisClient::PixelVisQueryPixels(int32_t nVisible, int32_t nCount, int32_t* pOut)
{
	if (!pOut)
		return;
	pOut[0] = -1;
	pOut[1] = -1;
	if (!m_Engines.m_pMaterials)
		return;
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	// The counting query first, as stock's GetFractionVisible read them.
	const OcclusionQueryObjectHandle_t hCount = QueryOf(nCount);
	if (hCount != INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
		pOut[1] = pRenderContext->OcclusionQuery_GetNumPixelsRendered(hCount);
	const OcclusionQueryObjectHandle_t hVisible = QueryOf(nVisible);
	if (hVisible != INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
		pOut[0] = pRenderContext->OcclusionQuery_GetNumPixelsRendered(hVisible);
}

void CPixelVisClient::PixelVisDiameters(const float* pSpheres, int32_t nSpheres, float* pOut)
{
	if (!pSpheres || !pOut || nSpheres <= 0 || !m_Engines.m_pMaterials)
		return;
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	for (int i = 0; i < nSpheres; i++)
	{
		const float* s = pSpheres + i * 4;
		pOut[i] = pRenderContext->ComputePixelDiameterOfSphere(Vector(s[0], s[1], s[2]), s[3]);
	}
}

int32_t CPixelVisClient::PixelVisView(float* pOut, int32_t nFloats)
{
	if (pOut && nFloats >= 12)
	{
		const Vector* axes[] = {&m_Cameras.CurrentOrigin(), &m_Cameras.CurrentForward(), &m_Cameras.CurrentRight(),
								&m_Cameras.CurrentUp()};
		for (int a = 0; a < 4; a++)
		{
			pOut[a * 3] = axes[a]->x;
			pOut[a * 3 + 1] = axes[a]->y;
			pOut[a * 3 + 2] = axes[a]->z;
		}
	}
	return m_Cameras.CurrentID();
}

void CPixelVisClient::EndView()
{
	IManagedPixelVisClient* pManaged = m_Areas.m_pPixelVisClient;
	if (!pManaged)
		return;

	float view[SS_PIXELVIS_VIEW_FLOATS];
	view[0] = (float)m_Cameras.CurrentID();
	PixelVisView(view + 1, 12);
	const VMatrix& m = m_Engines.m_pEngine->WorldToScreenMatrix();
	for (int r = 0; r < 4; r++)
		for (int c = 0; c < 4; c++)
			view[13 + r * 4 + c] = m[r][c];

	if (m_Commands.Count() < 4096)
		m_Commands.SetCount(4096);
	int32_t nWords = pManaged->EndView(view, SS_PIXELVIS_VIEW_FLOATS, m_Commands.Base(), m_Commands.Count());
	if (nWords > m_Commands.Count())
	{
		m_Commands.SetCount(nWords);
		nWords = pManaged->Commands(m_Commands.Base(), m_Commands.Count());
	}
	if (nWords > 0 && nWords <= m_Commands.Count())
		Execute(m_Commands.Base(), nWords);
}

void CPixelVisClient::EndScene()
{
	if (IManagedPixelVisClient* pManaged = m_Areas.m_pPixelVisClient)
		pManaged->EndScene();
}
