//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The beam executor; managed code decides everything about a beam. This holds
// CBeamsClient's bodies (shell_beams_client.h), the material system draw calls, and
// the reads managed code needs: the view, model frames, endpoint facts, box
// tests, the halo's sight test and the overlay's screen position.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"
#include "iviewrender_beams.h"
#include "enginesprite.h"

#include "materialsystem/imaterialvar.h"

#include "particles/particles.h"

#include "client_entity.h"

#include "beam_strip_vertex.h"
#include "shell_cliententitylist.h"
#include "slot_table.h"
// c_env_fog_controller.h before the player shell: C_StandalonePlayer's
// CNetworkHandle< C_FogController > needs the class complete.
#include "shell_standalone_client.h"
#include "collideable_center.h"
#include "imanaged_areas_client.h"
#include "shell_beams_client.h"
#include "ss_command_line.h"
#include "client_entity_list.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

namespace
{

// The raw handle managed code holds, back into a CBaseHandle (0xFFFFFFFF is none).
CBaseHandle HandleFromRaw(uint32_t hRaw)
{
	if (hRaw == 0xFFFFFFFFu)
		return CBaseHandle();
	return CBaseHandle((int)(hRaw & ENT_ENTRY_MASK), (int)(hRaw >> NUM_SERIAL_NUM_SHIFT_BITS));
}

uint32_t RawFromEntity(IClientEntity* pEnt)
{
	return pEnt ? (uint32_t)pEnt->GetRefEHandle().ToInt() : 0xFFFFFFFFu;
}

} // namespace

// One leaf-system renderable per drawn managed beam. Bounds and origin are
// managed code's; the rest is stock Beam_t's: translucent, always drawn, full blend.
class CBeamRenderable : public CDefaultClientRenderable
{
public:
	explicit CBeamRenderable(const CManagedAreasClient& areas)
		: m_Areas(areas)
		, m_nIndex(-1)
		, m_bLive(false)
	{
		m_vecOrigin.Init();
		m_vecMins.Init();
		m_vecMaxs.Init();
	}

	void Set(const float* p9)
	{
		m_vecOrigin.Init(p9[0], p9[1], p9[2]);
		m_vecMins.Init(p9[3], p9[4], p9[5]);
		m_vecMaxs.Init(p9[6], p9[7], p9[8]);
	}

	const Vector& GetRenderOrigin() override { return m_vecOrigin; }

	const QAngle& GetRenderAngles() override { return vec3_angle; }

	const matrix3x4_t& RenderableToWorldTransform() override
	{
		SetIdentityMatrix(m_Transform);
		PositionMatrix(m_vecOrigin, m_Transform);
		return m_Transform;
	}

	void GetRenderBounds(Vector& mins, Vector& maxs) override
	{
		mins = m_vecMins;
		maxs = m_vecMaxs;
	}

	bool ShouldDraw() override { return true; }

	bool IsTransparent() override { return true; }

	void ComputeFxBlend() override {}

	int GetFxBlend() override { return 255; }

	int DrawModel(int) override
	{
		if (IManagedBeamsClient* pBeams = m_Areas.m_pBeamsClient)
			pBeams->BeamsDraw(m_nIndex);
		return 0;
	}

	const CManagedAreasClient& m_Areas;
	int m_nIndex;
	bool m_bLive;

private:
	Vector m_vecOrigin;
	Vector m_vecMins;
	Vector m_vecMaxs;
	matrix3x4_t m_Transform;
};

enum BeamLifecycle_t
{
	BEAM_LIFECYCLE_INIT = 0,
	BEAM_LIFECYCLE_SHUTDOWN,
	BEAM_LIFECYCLE_CLEAR,
};

// CBeamsClient (shell_beams_client.h).

void CBeamsClient::InitBeams()
{
	if (IManagedBeamsClient* pBeams = Managed())
	{
		char szParticles[32];
		pBeams->BeamsLifecycle(
			BEAM_LIFECYCLE_INIT,
			ss::CommandLineValue("-particles", "", szParticles, sizeof(szParticles)) ? atoi(szParticles) : -1);
	}
}

void CBeamsClient::ShutdownBeams()
{
	if (IManagedBeamsClient* pBeams = Managed())
		pBeams->BeamsLifecycle(BEAM_LIFECYCLE_SHUTDOWN, 0);
}

void CBeamsClient::ClearBeams()
{
	if (IManagedBeamsClient* pBeams = Managed())
		pBeams->BeamsLifecycle(BEAM_LIFECYCLE_CLEAR, 0);
}

void CBeamsClient::UpdateTempEntBeams()
{
	if (IManagedBeamsClient* pBeams = Managed())
		pBeams->BeamsUpdate(m_Engines.m_pGlobals->curtime, m_Engines.m_pGlobals->frametime);
}

void CBeamsClient::KillDeadBeams(const CBaseHandle& hDeadEntity)
{
	if (IManagedBeamsClient* pBeams = Managed())
		pBeams->BeamsKillDead((uint32_t)hDeadEntity.ToInt(), m_Engines.m_pGlobals->curtime);
}

CBeamRenderable* CBeamsClient::RenderableAt(int nIndex)
{
	if (nIndex < 0 || nIndex >= m_Renderables.Count())
		return NULL;
	CBeamRenderable* p = m_Renderables[nIndex];
	return p->m_bLive ? p : NULL;
}

// The reads a managed rope or beam makes, each a pass-through onto the engine.
//
// The view being drawn (CurrentViewForward/Origin, and the render context's
// world-space camera the strip faces: only inside a view, which is what
// bCurrentView says the caller is), the main view's origin, the screen width,
// and the two hardware facts FakeAA rope smoothing reads.
void CBeamsClient::ClientViewState(int32_t bCurrentView, float* pOutForwardXYZ, float* pOutOriginXYZ,
								   float* pOutMainOriginXYZ, float* pOutCameraXYZ, int32_t* pOutScreenWidth,
								   int32_t* pOutDxLevel, int32_t* pOutAAEnabled)
{
	const Vector& vMain = m_Cameras.MainOrigin();

	if (bCurrentView)
	{
		if (pOutForwardXYZ)
			VectorCopy(m_Cameras.CurrentForward(), *reinterpret_cast<Vector*>(pOutForwardXYZ));
		if (pOutOriginXYZ)
			VectorCopy(m_Cameras.CurrentOrigin(), *reinterpret_cast<Vector*>(pOutOriginXYZ));
		if (pOutCameraXYZ)
		{
			CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
			pRenderContext->GetWorldSpaceCameraPosition(reinterpret_cast<Vector*>(pOutCameraXYZ));
		}
	}
	if (pOutMainOriginXYZ)
		VectorCopy(vMain, *reinterpret_cast<Vector*>(pOutMainOriginXYZ));
	if (pOutScreenWidth)
		*pOutScreenWidth = ScreenWidth(m_Engines);
	if (pOutDxLevel)
		*pOutDxLevel = m_Engines.m_pEngine->GetDXSupportLevel();
	if (pOutAAEnabled)
		*pOutAAEnabled = m_Engines.m_pHardwareConfig && m_Engines.m_pHardwareConfig->IsAAEnabled() ? 1 : 0;
}

void CBeamsClient::BeamView(int32_t bCurrentView, float* pOut42)
{
	if (!pOut42)
		return;

	Vector* v = reinterpret_cast<Vector*>(pOut42);
	if (bCurrentView)
	{
		v[0] = m_Cameras.CurrentOrigin();
		v[1] = m_Cameras.CurrentForward();
		v[2] = m_Cameras.CurrentRight();
		v[3] = m_Cameras.CurrentUp();
		const QAngle& angles = m_Cameras.CurrentAngles();
		v[4].Init(angles.x, angles.y, angles.z);

		// The render context's world-space camera: what a strip faces.
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
		pRenderContext->GetWorldSpaceCameraPosition(reinterpret_cast<Vector*>(pOut42 + 39));
	}
	v[5] = m_Cameras.MainUp();
	v[6] = m_Cameras.MainRight();

	const VMatrix& worldToScreen = m_Engines.m_pEngine->WorldToScreenMatrix();
	for (int i = 0; i < 16; i++)
		pOut42[21 + i] = worldToScreen.m[i / 4][i % 4];

	pOut42[37] = m_Engines.m_pGlobals->curtime;
	pOut42[38] = m_Engines.m_pGlobals->frametime;
}

int32_t CBeamsClient::BeamModelInfo(int32_t nModelIndex, int32_t* pOutFrameCount)
{
	const model_t* pModel = m_Engines.m_pModelInfo->GetModel(nModelIndex);
	if (pOutFrameCount)
		*pOutFrameCount = pModel ? m_Engines.m_pModelInfo->GetModelFrameCount(pModel) : 0;
	return pModel ? 1 : 0;
}

int32_t CBeamsClient::BeamModelIndex(const char* pszName)
{
	return pszName ? m_Engines.m_pModelInfo->GetModelIndex(pszName) : -1;
}

uint32_t CBeamsClient::BeamEntityAt(int32_t nEntry)
{
	return RawFromEntity(m_EntityList.EntityFromIndex(nEntry));
}

int32_t CBeamsClient::BeamEntityHasModel(uint32_t hEntity)
{
	IClientEntity* pEnt = m_EntityList.EntityFromHandle(HandleFromRaw(hEntity));
	return (pEnt && pEnt->GetModel()) ? 1 : 0;
}

// The facts a beam endpoint's position is decided from: which hierarchy holds
// the handle, whether it renders and is a player, and the candidate points (the
// attachment, asked only when managed code says to, the collision centre and the
// render origin).
enum BeamEntityFact_t
{
	BEAM_FACT_RESOLVED = 0x01,
	BEAM_FACT_RENDERABLE = 0x04,
	BEAM_FACT_PLAYER = 0x08,
	BEAM_FACT_ATTACHMENT = 0x10,
	BEAM_FACT_COLLIDEABLE = 0x20,
};

int32_t CBeamsClient::BeamEntityFacts(uint32_t hEntity, int32_t nAttachment, int32_t bTryAttachment, float* pOut9)
{
	if (!pOut9)
		return 0;
	Vector* pOut = reinterpret_cast<Vector*>(pOut9);

	IClientUnknown* pUnknown = m_EntityList.UnknownFromHandle(HandleFromRaw(hEntity));
	if (!pUnknown)
		return 0;

	int32_t nFacts = BEAM_FACT_RESOLVED;

	IClientRenderable* pRenderable = pUnknown->GetClientRenderable();
	if (!pRenderable)
		return nFacts;
	nFacts |= BEAM_FACT_RENDERABLE;

	C_StandaloneEntity* pShell = m_EntityList.ShellFromHandleEntity(pUnknown);
	if (pShell && pShell->IsPlayerShell())
		nFacts |= BEAM_FACT_PLAYER;

	if (bTryAttachment)
	{
		QAngle angles;
		if (pRenderable->GetAttachment(nAttachment, pOut[0], angles))
			nFacts |= BEAM_FACT_ATTACHMENT;
	}

	if (ICollideable* pCollideable = pUnknown->GetCollideable())
	{
		pOut[1] = UTIL_CollideableWorldSpaceCenter(pCollideable);
		nFacts |= BEAM_FACT_COLLIDEABLE;
	}

	pOut[2] = pRenderable->GetRenderOrigin();
	return nFacts;
}

int32_t CBeamsClient::BeamBoxVisibility(const float* pMinsXYZ, const float* pMaxsXYZ)
{
	const Vector& mins = *reinterpret_cast<const Vector*>(pMinsXYZ);
	const Vector& maxs = *reinterpret_cast<const Vector*>(pMaxsXYZ);
	return (m_Engines.m_pEngine->IsBoxVisible(mins, maxs) ? 1 : 0) | (m_Engines.m_pEngine->CullBox(mins, maxs) ? 2 : 0);
}

int32_t CBeamsClient::BeamScreenPosition(const float* pPointXYZ, float* pOutScreenXYZ)
{
	Vector screen;
	const int behind = m_Engines.m_pDebugOverlay->ScreenPosition(*reinterpret_cast<const Vector*>(pPointXYZ), screen);
	VectorCopy(screen, *reinterpret_cast<Vector*>(pOutScreenXYZ));
	return behind;
}

int32_t CBeamsClient::BeamRenderableAdd(const float* pOriginMinsMaxs9)
{
	int nIndex;
	if (m_FreeRenderables.Count())
	{
		nIndex = m_FreeRenderables.Tail();
		m_FreeRenderables.RemoveMultipleFromTail(1);
	}
	else
	{
		nIndex = m_Renderables.AddToTail(new CBeamRenderable(m_Areas));
		m_Renderables[nIndex]->m_nIndex = nIndex;
	}

	CBeamRenderable* p = m_Renderables[nIndex];
	p->m_bLive = true;
	p->Set(pOriginMinsMaxs9);
	m_Engines.m_pLeafSystem->AddRenderable(p, RENDER_GROUP_TRANSLUCENT_ENTITY);
	return nIndex;
}

void CBeamsClient::BeamRenderableChanged(int32_t nRenderable, const float* pOriginMinsMaxs9)
{
	CBeamRenderable* p = RenderableAt(nRenderable);
	if (!p)
		return;
	p->Set(pOriginMinsMaxs9);
	m_Engines.m_pLeafSystem->RenderableChanged(p->RenderHandle());
}

void CBeamsClient::BeamRenderableRemove(int32_t nRenderable)
{
	CBeamRenderable* p = RenderableAt(nRenderable);
	if (!p)
		return;
	m_Engines.m_pLeafSystem->RemoveRenderable(p->RenderHandle());
	// RemoveRenderable does not reset it when the index is already gone (level
	// shutdown), and a pooled beam must not keep it.
	p->RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;
	p->m_bLive = false;
	m_FreeRenderables.AddToTail(nRenderable);
}

static CEngineSprite* SpriteFor(IVModelInfoClient* pModelInfo, int32_t nModelIndex)
{
	const model_t* pModel = pModelInfo->GetModel(nModelIndex);
	return pModel ? (CEngineSprite*)pModelInfo->GetModelExtraData(pModel) : NULL;
}

// Binds a sprite model's material for a frame and render mode
// (Draw_SetSpriteTexture, with the wireframe choice made by the caller).
int32_t CBeamsClient::BeamBindSprite(int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe)
{
	CEngineSprite* pSprite = SpriteFor(m_Engines.m_pModelInfo, nModelIndex);
	if (!pSprite)
		return 0;

	IMaterial* pMaterial = pSprite->GetMaterial((RenderMode_t)nRenderMode, nFrame);
	if (!pMaterial)
		return 0;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	if (bWireframe)
	{
		if (!m_pWireframeMaterial)
			m_pWireframeMaterial =
				m_Engines.m_pMaterials->FindMaterial("shadertest/wireframevertexcolor", TEXTURE_GROUP_OTHER);
		pRenderContext->Bind(m_pWireframeMaterial, NULL);
		return 1;
	}

	pRenderContext->Bind(pMaterial);
	return 1;
}

void CBeamsClient::BeamSetHdrColorScale(int32_t nModelIndex, int32_t nRenderMode, float flScale)
{
	CEngineSprite* pSprite = SpriteFor(m_Engines.m_pModelInfo, nModelIndex);
	IMaterial* pMaterial = pSprite ? pSprite->GetMaterial((RenderMode_t)nRenderMode) : NULL;
	if (!pMaterial)
		return;

	if (IMaterialVar* pVar = pMaterial->FindVarFast("$hdrcolorscale", &m_nHdrColorScaleCache))
		pVar->SetFloatValue(flScale);
}

// A halo sprite's glow material: whether it has one, or bind it as it is.
int32_t CBeamsClient::BeamGlow(int32_t nModelIndex, int32_t bBind)
{
	CEngineSprite* pSprite = SpriteFor(m_Engines.m_pModelInfo, nModelIndex);
	IMaterial* pMaterial = pSprite ? pSprite->GetMaterial(kRenderGlow) : NULL;
	if (!pMaterial)
		return 0;

	if (bBind)
	{
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
		pRenderContext->Bind(pMaterial);
	}
	return 1;
}

// One strip over the bound material, two vertices a segment (beam_strip_vertex.h).
void CBeamsClient::BeamDrawStrip(const float* pVerts, int32_t nVerts)
{
	if (!pVerts || nVerts < 4 || (nVerts & 1))
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	ss::beamstrip::DrawStrip(pRenderContext->GetDynamicMesh(), pVerts, nVerts);
}

// One vertex as BeamVertex lays it out: position, r g b a, u v.
enum
{
	BEAM_VERTEX_FLOATS = 9
};

enum BeamColorKind_t
{
	BEAM_COLOR_FLOAT = 0, // Color3fv
	BEAM_COLOR_RGB, // Color3ubv
	BEAM_COLOR_RGBA, // Color4ubv
};

void CBeamsClient::BeamDrawMesh(int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind, const float* pVerts,
								int32_t nVerts)
{
	if (!pVerts || nVerts <= 0 || nPrimitives <= 0)
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	IMesh* pMesh = pRenderContext->GetDynamicMesh();

	CMeshBuilder meshBuilder;
	meshBuilder.Begin(pMesh, (MaterialPrimitiveType_t)nPrimitive, nPrimitives);

	for (int i = 0; i < nVerts; i++)
	{
		const float* v = pVerts + i * BEAM_VERTEX_FLOATS;

		switch (nColorKind)
		{
			case BEAM_COLOR_RGB:
			{
				const unsigned char c[3] = {(unsigned char)v[3], (unsigned char)v[4], (unsigned char)v[5]};
				meshBuilder.Color3ubv(c);
				break;
			}
			case BEAM_COLOR_RGBA:
			{
				const unsigned char c[4] = {(unsigned char)v[3], (unsigned char)v[4], (unsigned char)v[5],
											(unsigned char)v[6]};
				meshBuilder.Color4ubv(c);
				break;
			}
			default:
				meshBuilder.Color3fv(v + 3);
				break;
		}

		meshBuilder.TexCoord2f(0, v[7], v[8]);
		meshBuilder.Position3fv(v);
		meshBuilder.AdvanceVertex();
	}

	meshBuilder.End();
	pMesh->Draw();
}
