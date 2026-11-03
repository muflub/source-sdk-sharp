//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The ropes: the materials managed asks for, kept referenced and answered as small integers;
// the strips it queues for the view being drawn; and the world reads a rope makes. A member of
// the client DLL object.

#pragma once

#include <stdint.h>

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "c_rope.h"
#include "materialsystem/imaterial.h"
#include "shell_clientsystem.h"
#include "tier1/utlvector.h"

class CClientEngineInterfaces;

// The materials, by handle.
struct RopeMaterial_t
{
	char szName[128];
	IMaterial* pSolid;
	IMaterial* pBack;
	int nTextureHeight;
};

struct RopeStrip_t
{
	int nMaterial;
	int iFirst; // vertices, not floats
	int nVerts;
	int iFirstSolid;
	int nSolid;
	int nFlags;
};

struct RopeCache_t
{
	CUtlVector<RopeStrip_t> strips;
	CUtlVector<float> verts; // BEAM_STRIP_VERTEX_FLOATS a vertex

	void Clear()
	{
		strips.RemoveAll();
		verts.RemoveAll();
	}
};

class CRopeClient : public IRopeManager
{
public:
	// IShimEffectsRenderClient: the reads and draws of a managed rope.
	int32_t RopeMaterial(const char* pszName, int32_t* pOutTextureHeight, int32_t* pOutHasBack);
	void RopeDraw(int32_t nMaterial, const float* pVerts, int32_t nVerts, const float* pSolid, int32_t nSolid,
				  int32_t nFlags);
	void ClientComputeLighting(const float* pPosXYZ, float* pOutColorRGB, float* pOutBoxRGB18);
	float ClientTraceHullWorld(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
							   const float* pMaxsXYZ, int32_t nMask, int32_t* pOutSolidFlags, float* pOutPlaneXYZD);

	CClientSystem& SystemRow() { return m_SystemRow; }

	explicit CRopeClient(const CClientEngineInterfaces& engines);

	void Add(int nMaterial, const float* pVerts, int nVerts, const float* pSolid, int nSolid, int nFlags);

	// IRopeManager.
	virtual void ResetRenderCache() OVERRIDE;

	// A rope reaches the cache through RopeDraw instead.
	virtual void AddToRenderCache(C_RopeKeyframe*) OVERRIDE {}

	virtual void DrawRenderCache(bool bShadowDepth) OVERRIDE;

	virtual void OnRenderStart() OVERRIDE;

	// The holiday lights are TF's (USES_ECON_ITEMS), compiled out of stock here too.
	virtual void SetHolidayLightMode(bool) OVERRIDE {}

	virtual bool IsHolidayLightMode() OVERRIDE;

	virtual int GetHolidayLightStyle() OVERRIDE;

	void LevelShutdown();

private:
	void ReleaseMaterials();

	class CSystemRow final : public CClientSystem
	{
	public:
		explicit CSystemRow(CRopeClient& rope)
			: CClientSystem("CSourceSharpRopeSystem")
			, m_Rope(rope)
		{
		}

		void LevelShutdownPostEntity() override;

	private:
		CRopeClient& m_Rope;
	};

	const CClientEngineInterfaces& m_Engines;
	CUtlVector<RopeMaterial_t> m_Materials;
	CSystemRow m_SystemRow{*this};
	void Draw(bool bShadowDepth, RopeCache_t* pCache);

	// One mesh of every strip that uses the material: the built strip, or (on
	// the solid pass after a back pass) the solid strip managed code remapped.
	void DrawPass(IMatRenderContext* pRenderContext, RopeCache_t* pCache, int iMat, IMaterial* pMaterial, int nVerts,
				  int nIndices, bool bSolidStrips, bool bCount);

	RopeCache_t m_Cache;
	CUtlVector<RopeCache_t*> m_Queued[2];
	int m_iQueued;
	IMaterial* m_pDepthWrite;
};
