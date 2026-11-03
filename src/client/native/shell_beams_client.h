//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The beams' native half as one object, a member of the client DLL object: the
// IViewRenderBeams engine-facing code names (forwarding to managed, which decides everything
// about a beam), the pool of leaf-system renderables managed code moves by index, and the
// sprite bind with its wireframe material. Its IShimEffectsRenderClient slots are the pool's
// and the bind's; the area's other beam slots are free answers.

#pragma once

class CClientEngineInterfaces;
class CViewCameras;
class CClientEntityList;

#include <stdint.h>

// The header names CBaseEntity, the client's alias for C_BaseEntity.
#include "shared_classnames.h"
#include "igameentity_fwd.h"
class C_BaseEntity;
class ITraceFilter;
#include "iviewrender_beams.h"
#include "utlvector.h"

#include "shell_managed_areas_client.h"

class CBaseHandle;
class CBeamRenderable;
class IMaterial;

class CBeamsClient final : public IViewRenderBeams
{
public:
	CBeamsClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines, const CViewCameras& cameras,
				 CClientEntityList& entityList)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
		, m_EntityList(entityList)
	{
	}

	CBeamsClient(const CBeamsClient&) = delete;
	CBeamsClient& operator=(const CBeamsClient&) = delete;

	// Shim slots: the models, the box and the strips a managed beam draws with.
	int32_t BeamModelInfo(int32_t nModelIndex, int32_t* pOutFrameCount);
	int32_t BeamModelIndex(const char* pszName);
	int32_t BeamBoxVisibility(const float* pMinsXYZ, const float* pMaxsXYZ);
	int32_t BeamScreenPosition(const float* pPointXYZ, float* pOutScreenXYZ);
	int32_t BeamGlow(int32_t nModelIndex, int32_t bBind);
	void BeamDrawStrip(const float* pVerts, int32_t nVerts);
	void BeamDrawMesh(int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind, const float* pVerts, int32_t nVerts);

	// NULL outside a managed load.
	IManagedBeamsClient* Managed() const { return m_Areas.m_pBeamsClient; }

	// Shim slots: the view a managed rope or beam reads.
	void ClientViewState(int32_t bCurrentView, float* pOutForwardXYZ, float* pOutOriginXYZ, float* pOutMainOriginXYZ,
						 float* pOutCameraXYZ, int32_t* pOutScreenWidth, int32_t* pOutDxLevel, int32_t* pOutAAEnabled);
	void BeamView(int32_t bCurrentView, float* pOut42);

	// IViewRenderBeams. Nothing creates a beam through it, so the creators answer no Beam_t.
	void InitBeams() override;
	void ShutdownBeams() override;
	void ClearBeams() override;
	void UpdateTempEntBeams() override;

	void DrawBeam(Beam_t*) override {}

	void DrawBeam(C_Beam*, ITraceFilter*) override {}

	void KillDeadBeams(IGameEntity* pDead) override {}

	Beam_t* CreateBeamEnts(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamEntPoint(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamPoints(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamRing(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamRingPoint(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamCirclePoints(BeamInfo_t&) override { return NULL; }

	Beam_t* CreateBeamFollow(BeamInfo_t&) override { return NULL; }

	void FreeBeam(Beam_t*) override {}

	void UpdateBeamInfo(Beam_t*, BeamInfo_t&) override {}

	void CreateBeamEnts(int, int, int, int, float, float, float, float, float, float, float, float, int, float, float,
						float, float, int) override
	{
	}

	void CreateBeamEntPoint(int, const Vector*, int, const Vector*, int, int, float, float, float, float, float, float,
							float, float, int, float, float, float, float) override
	{
	}

	void CreateBeamPoints(Vector&, Vector&, int, int, float, float, float, float, float, float, float, float, int,
						  float, float, float, float) override
	{
	}

	void CreateBeamRing(int, int, int, int, float, float, float, float, float, float, float, float, int, float, float,
						float, float, int) override
	{
	}

	void CreateBeamRingPoint(const Vector&, float, float, int, int, float, float, float, float, float, float, float,
							 float, int, float, float, float, float, int) override
	{
	}

	void CreateBeamCirclePoints(int, Vector&, Vector&, int, int, float, float, float, float, float, float, float, float,
								int, float, float, float, float) override
	{
	}

	void CreateBeamFollow(int, int, int, float, float, float, float, float, float, float, float, float) override {}

	// Not IViewRenderBeams': stock's takes the entity, and a shell is not one.
	void KillDeadBeams(const CBaseHandle& hDeadEntity);

	// IShimEffectsRenderClient.
	uint32_t BeamEntityAt(int32_t nEntry);
	int32_t BeamEntityHasModel(uint32_t hEntity);
	int32_t BeamEntityFacts(uint32_t hEntity, int32_t nAttachment, int32_t bTryAttachment, float* pOut9);
	int32_t BeamRenderableAdd(const float* pOriginMinsMaxs9);
	void BeamRenderableChanged(int32_t nRenderable, const float* pOriginMinsMaxs9);
	void BeamRenderableRemove(int32_t nRenderable);
	int32_t BeamBindSprite(int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe);
	void BeamSetHdrColorScale(int32_t nModelIndex, int32_t nRenderMode, float flScale);

private:
	CBeamRenderable* RenderableAt(int nIndex);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CClientEntityList& m_EntityList;

	// One leaf-system renderable per drawn managed beam, pooled: a removed one is reused.
	CUtlVector<CBeamRenderable*> m_Renderables;
	CUtlVector<int> m_FreeRenderables;

	IMaterial* m_pWireframeMaterial = nullptr;
	unsigned int m_nHdrColorScaleCache = 0;
};
