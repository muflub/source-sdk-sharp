//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The rope executor. Every rope decision is managed's; this holds what managed
// cannot:
//   * the materials, kept referenced and answered as small integers;
//   * the per-view render cache viewrender resets before a view's opaque
//     entities and draws after them, filled by RopeDraw in between;
//   * the mesh: each strip arrives as camera-facing vertices
//     (beam_strip_vertex.h's layout), written into one triangle list per
//     material.
// When the material system renders on another thread, the cache is copied
// into one of two frame buffers (OnRenderStart switches them) and the draw is
// queued.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "c_rope.h"

#include "materialsystem/imesh.h"
#include "tier1/callqueue.h"

#include "cdll_util.h"

#include "util_shared.h"

#include "beam_strip_vertex.h"
#include "shell_effect_ledger.h"
#include "shell_clientsystem.h"
#include "shell_rope_client.h"
#include "shell_client_engine_interfaces.h"

// RopeDraw's flags. Kinds, not decisions: managed code chose them.
enum
{
	ROPE_DRAW_FAKEAA = 0x1, // the strip was built for the back pass; draw it with the back material
	ROPE_DRAW_SOLID = 0x2, // draw the solid pass (rope_rendersolid)
};

int32_t CRopeClient::RopeMaterial(const char* pszName, int32_t* pOutTextureHeight, int32_t* pOutHasBack)
{
	if (!pszName || !pszName[0] || !m_Engines.m_pMaterials)
		return -1;

	int iFound = -1;
	for (int i = 0; i < m_Materials.Count(); i++)
	{
		if (!V_stricmp(m_Materials[i].szName, pszName))
		{
			iFound = i;
			break;
		}
	}

	if (iFound < 0)
	{
		RopeMaterial_t entry;
		V_strncpy(entry.szName, pszName, sizeof(entry.szName));

		entry.pSolid = m_Engines.m_pMaterials->FindMaterial(pszName, TEXTURE_GROUP_OTHER);
		entry.nTextureHeight = entry.pSolid ? entry.pSolid->GetMappingHeight() : 1;
		if (entry.pSolid)
			entry.pSolid->IncrementReferenceCount();

		char szBack[512];
		V_snprintf(szBack, sizeof(szBack), "%s_back", pszName);
		entry.pBack = m_Engines.m_pMaterials->FindMaterial(szBack, TEXTURE_GROUP_OTHER, false);
		if (IsErrorMaterial(entry.pBack))
			entry.pBack = NULL;
		if (entry.pBack)
			entry.pBack->IncrementReferenceCount();

		iFound = m_Materials.AddToTail(entry);
	}

	if (pOutTextureHeight)
		*pOutTextureHeight = m_Materials[iFound].nTextureHeight;
	if (pOutHasBack)
		*pOutHasBack = m_Materials[iFound].pBack ? 1 : 0;

	return iFound;
}

void CRopeClient::ReleaseMaterials()
{
	for (int i = 0; i < m_Materials.Count(); i++)
	{
		if (m_Materials[i].pSolid)
			m_Materials[i].pSolid->DecrementReferenceCount();
		if (m_Materials[i].pBack)
			m_Materials[i].pBack->DecrementReferenceCount();
	}

	m_Materials.RemoveAll();
}

// One view's strips: where each sits in the segment pool, and how to draw it.

CRopeClient::CRopeClient(const CClientEngineInterfaces& engines)
	: m_Engines(engines)
	, m_iQueued(0)
	, m_pDepthWrite(NULL)
{
}

void CRopeClient::Add(int nMaterial, const float* pVerts, int nVerts, const float* pSolid, int nSolid, int nFlags)
{
	RopeStrip_t strip;
	strip.nMaterial = nMaterial;
	strip.iFirst = m_Cache.verts.Count() / BEAM_STRIP_VERTEX_FLOATS;
	strip.nVerts = nVerts;
	m_Cache.verts.AddMultipleToTail(nVerts * BEAM_STRIP_VERTEX_FLOATS, pVerts);
	strip.iFirstSolid = m_Cache.verts.Count() / BEAM_STRIP_VERTEX_FLOATS;
	strip.nSolid = pSolid ? nSolid : 0;
	if (strip.nSolid > 0)
		m_Cache.verts.AddMultipleToTail(strip.nSolid * BEAM_STRIP_VERTEX_FLOATS, pSolid);
	strip.nFlags = nFlags;
	m_Cache.strips.AddToTail(strip);
}

void CRopeClient::ResetRenderCache()
{
	m_Cache.Clear();
}

void CRopeClient::DrawRenderCache(bool bShadowDepth)
{
	if (m_Cache.strips.Count() == 0)
		return;

	ICallQueue* pCallQueue = m_Engines.m_pMaterials->GetRenderContext()->GetCallQueue();

	if (!pCallQueue)
	{
		Draw(bShadowDepth, &m_Cache);
		return;
	}

	// Copied into this frame's buffer, which outlives the queued call: the
	// other buffer is the one OnRenderStart clears.
	CUtlVector<RopeCache_t*>& frame = m_Queued[m_iQueued];
	RopeCache_t* pCopy = new RopeCache_t;
	pCopy->strips.CopyArray(m_Cache.strips.Base(), m_Cache.strips.Count());
	pCopy->verts.CopyArray(m_Cache.verts.Base(), m_Cache.verts.Count());
	frame.AddToTail(pCopy);

	pCallQueue->QueueCall(this, &CRopeClient::Draw, bShadowDepth, pCopy);
}

void CRopeClient::OnRenderStart()
{
	m_iQueued = 1 - m_iQueued;
	m_Queued[m_iQueued].PurgeAndDeleteElements();
}

bool CRopeClient::IsHolidayLightMode()
{
	return false;
}

int CRopeClient::GetHolidayLightStyle()
{
	return 0;
}

void CRopeClient::LevelShutdown()
{
	m_Cache.Clear();
	m_Queued[0].PurgeAndDeleteElements();
	m_Queued[1].PurgeAndDeleteElements();
}

void CRopeClient::Draw(bool bShadowDepth, RopeCache_t* pCache)
{
	if (bShadowDepth && !m_pDepthWrite && m_Engines.m_pMaterials)
	{
		KeyValues* pVMT = new KeyValues("DepthWrite");
		pVMT->SetInt("$no_fullbright", 1);
		pVMT->SetInt("$alphatest", 0);
		pVMT->SetInt("$nocull", 1);
		m_pDepthWrite = m_Engines.m_pMaterials->FindProceduralMaterial("__DepthWrite01", TEXTURE_GROUP_OTHER, pVMT);
		m_pDepthWrite->IncrementReferenceCount();
	}

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);

	// One batch per material, in handle order.
	for (int iMat = 0; iMat < m_Materials.Count(); iMat++)
	{
		int nVerts = 0, nIndices = 0, nStrips = 0;
		int nFlags = 0;

		for (int i = 0; i < pCache->strips.Count(); i++)
		{
			const RopeStrip_t& s = pCache->strips[i];
			if (s.nMaterial != iMat)
				continue;
			nVerts += s.nVerts;
			nIndices += (s.nVerts / 2 - 1) * 6;
			nFlags |= s.nFlags;
			nStrips++;
		}

		if (!nStrips)
			continue;

		const RopeMaterial_t& mat = m_Materials[iMat];
		const bool bBack = !bShadowDepth && (nFlags & ROPE_DRAW_FAKEAA) && mat.pBack;

		if (bBack)
			DrawPass(pRenderContext, pCache, iMat, mat.pBack, nVerts, nIndices, false, true);

		if (nFlags & ROPE_DRAW_SOLID)
		{
			IMaterial* pMaterial = bShadowDepth ? m_pDepthWrite : mat.pSolid;
			if (pMaterial)
				DrawPass(pRenderContext, pCache, iMat, pMaterial, nVerts, nIndices, bBack, false);
		}
	}
}

void CRopeClient::DrawPass(IMatRenderContext* pRenderContext, RopeCache_t* pCache, int iMat, IMaterial* pMaterial,
						   int nVerts, int nIndices, bool bSolidStrips, bool bCount)
{
	CMeshBuilder meshBuilder;
	IMesh* pMesh = pRenderContext->GetDynamicMesh(true, NULL, NULL, pMaterial);
	meshBuilder.Begin(pMesh, MATERIAL_TRIANGLES, nVerts, nIndices);

	int nVertsSoFar = 0;

	for (int i = 0; i < pCache->strips.Count(); i++)
	{
		const RopeStrip_t& s = pCache->strips[i];
		if (s.nMaterial != iMat)
			continue;

		const int iFirst = bSolidStrips ? s.iFirstSolid : s.iFirst;
		const int nStripVerts = bSolidStrips ? s.nSolid : s.nVerts;

		if (nStripVerts >= 4)
		{
			ss::beamstrip::WriteIndexed(meshBuilder, &pCache->verts[iFirst * BEAM_STRIP_VERTEX_FLOATS], nStripVerts,
										nVertsSoFar);

			if (bCount)
				m_Engines.m_pEffectLedger->nRopeDraws++;

			nVertsSoFar += nStripVerts;
		}
	}

	meshBuilder.End();
	pMesh->Draw();
}

// One rope's strip for the view being drawn, and the solid pass's (the same
// count or none). Called from the managed owner's DrawModel, between
// viewrender's ResetRenderCache and DrawRenderCache.
void CRopeClient::RopeDraw(int32_t nMaterial, const float* pVerts, int32_t nVerts, const float* pSolid, int32_t nSolid,
						   int32_t nFlags)
{
	if (!pVerts || nVerts < 4 || (nVerts & 1) || nMaterial < 0 || nMaterial >= m_Materials.Count())
		return;
	if (nSolid != 0 && (nSolid != nVerts || !pSolid))
		nSolid = 0;

	Add(nMaterial, pVerts, nVerts, pSolid, nSolid, nFlags);
}

// The level's materials and caches go with it.
void CRopeClient::CSystemRow::LevelShutdownPostEntity()
{
	m_Rope.LevelShutdown();
	m_Rope.ReleaseMaterials();
}

// engine->ComputeLighting at a point: the averaged colour and the six box colours.
void CRopeClient::ClientComputeLighting(const float* pPosXYZ, float* pOutColorRGB, float* pOutBoxRGB18)
{
	if (!pPosXYZ)
		return;

	Vector vColor;
	Vector boxColors[6];

	m_Engines.m_pEngine->ComputeLighting(Vector(pPosXYZ[0], pPosXYZ[1], pPosXYZ[2]), NULL, true, vColor, boxColors);

	if (pOutColorRGB)
		VectorCopy(vColor, *reinterpret_cast<Vector*>(pOutColorRGB));
	if (pOutBoxRGB18)
	{
		for (int i = 0; i < 6; i++)
			VectorCopy(boxColors[i], *reinterpret_cast<Vector*>(pOutBoxRGB18 + i * 3));
	}
}

// A hull trace against the world only (CTraceFilterWorldOnly). Returns the
// fraction; out: allsolid (bit 0) and startsolid (bit 1), the plane normal and
// distance.
float CRopeClient::ClientTraceHullWorld(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
										const float* pMaxsXYZ, int32_t nMask, int32_t* pOutSolidFlags,
										float* pOutPlaneXYZD)
{
	if (!pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ)
		return 1.0f;

	CTraceFilterWorldOnly filter;
	trace_t tr;

	UTIL_TraceHull(m_Engines, Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]),
				   Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]), Vector(pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2]),
				   Vector(pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2]), (unsigned int)nMask, &filter, &tr);

	if (pOutSolidFlags)
		*pOutSolidFlags = (tr.allsolid ? 1 : 0) | (tr.startsolid ? 2 : 0);
	if (pOutPlaneXYZD)
	{
		pOutPlaneXYZD[0] = tr.plane.normal.x;
		pOutPlaneXYZD[1] = tr.plane.normal.y;
		pOutPlaneXYZD[2] = tr.plane.normal.z;
		pOutPlaneXYZD[3] = tr.plane.dist;
	}

	return tr.fraction;
}
