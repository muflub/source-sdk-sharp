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
	explicit CBeamsClient(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
	}

	CBeamsClient(const CBeamsClient&) = delete;
	CBeamsClient& operator=(const CBeamsClient&) = delete;

	// NULL outside a managed load.
	IManagedBeamsClient* Managed() const { return m_Areas.m_pBeamsClient; }

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
	int32_t BeamRenderableAdd(const float* pOriginMinsMaxs9);
	void BeamRenderableChanged(int32_t nRenderable, const float* pOriginMinsMaxs9);
	void BeamRenderableRemove(int32_t nRenderable);
	int32_t BeamBindSprite(int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe);
	void BeamSetHdrColorScale(int32_t nModelIndex, int32_t nRenderMode, float flScale);

private:
	CBeamRenderable* RenderableAt(int nIndex);

	const CManagedAreasClient& m_Areas;

	// One leaf-system renderable per drawn managed beam, pooled: a removed one is reused.
	CUtlVector<CBeamRenderable*> m_Renderables;
	CUtlVector<int> m_FreeRenderables;

	IMaterial* m_pWireframeMaterial = nullptr;
	unsigned int m_nHdrColorScaleCache = 0;
};
