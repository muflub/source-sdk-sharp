//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The client-side effect list's native half; the list and its effect kinds are
// managed.
//   clienteffects            the IEffectsList the view draws and the client DLL
//                            flushes, both carried to managed.
//   FX_Add*, FX_TracerSound  entries the fork's effect TUs call, carried to
//                            managed with their arguments; an IMaterial* crosses
//                            as an id.
//   CBulletWhizTimer         resets the near-miss clock at level start.
//   IShimFxClient::Fx*       materials by id, one quad-mesh draw, the view reads,
//                            the near-miss sound, and the tracer effects' asks.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "clientsideeffects.h"
#include "fx_line.h"
#include "fx_quad.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imesh.h"
#include "view.h"
#include "clienteffectprecachesystem.h"
#include "fx.h"
#include "smoke_fog_overlay.h"
#include "particle_parse.h"
#include "c_te_legacytempents.h"
#include "engine/ivmodelinfo.h"
#include "mathlib/vector.h"
#include "particles/particles.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "cdll_util.h"
#include "fx_staticline.h"
#include "iclientrenderable.h"
#include "texture_group_names.h"
#include "shell_clientsystem.h"

#include "shell_cliententitylist.h"

#include "sourcesharp.h"
#include "imanaged_areas_client.h"

CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectsTest)
CLIENTEFFECT_MATERIAL("effects/spark")
CLIENTEFFECT_MATERIAL("effects/gunshiptracer")
CLIENTEFFECT_MATERIAL("effects/bluespark")
CLIENTEFFECT_REGISTER_END()

// The material table: an id per IMaterial* handed out, stable for the process
// so a managed reload keeps its ids. Never shrinks; materials are
// reference-counted by the calls below.
static const int kMaxMaterials = 1024;
static IMaterial* s_pMaterials[kMaxMaterials];
static int s_nMaterials = 0;

static int32_t MaterialId(IMaterial* pMaterial)
{
	if (!pMaterial)
		return 0;
	for (int i = 0; i < s_nMaterials; ++i)
	{
		if (s_pMaterials[i] == pMaterial)
			return i + 1;
	}
	if (s_nMaterials >= kMaxMaterials)
		return 0;
	s_pMaterials[s_nMaterials++] = pMaterial;
	return s_nMaterials;
}

static IMaterial* MaterialOf(int32_t nMaterial)
{
	return (nMaterial >= 1 && nMaterial <= s_nMaterials) ? s_pMaterials[nMaterial - 1] : NULL;
}

// A material id as the material, for the render plan's executor.
IMaterial* SourceSharp_FxMaterialOf(int32_t nMaterial)
{
	return MaterialOf(nMaterial);
}

// A material as its id, no reference taken, for the glow overlays.
int32_t SourceSharp_FxMaterialIdOf(IMaterial* pMaterial)
{
	return MaterialId(pMaterial);
}

class CEffectsListThunk final : public IEffectsList
{
public:
	void AddEffect(CClientSideEffect* effect) override {}

	void RemoveEffect(CClientSideEffect* effect) override {}

	void DrawEffects(double frametime) override
	{
		// The view passes gpGlobals->frametime, a float: crossing as one is exact.
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->DrawEffects((float)frametime);
	}

	void Flush() override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->FlushEffects();
	}
};

static CEffectsListThunk s_EffectsListThunk;

class CBulletWhizTimer : public CClientSystem
{
public:
	CBulletWhizTimer(char const* name)
		: CClientSystem(name)
	{
	}

	void LevelInitPreEntity() override
	{
		if (IManagedFxClient* pFx = ManagedFxClient())
			pFx->EffectsLevelInit();
	}
};

static CBulletWhizTimer g_BulletWhiz("CBulletWhizTimer");

static bool g_FXCreationAllowed = false;

IEffectsList* clienteffects = &s_EffectsListThunk;

CClientSystem* ClientSystem_BulletWhizTimer()
{
	return &g_BulletWhiz;
}

void SetFXCreationAllowed(bool state)
{
	g_FXCreationAllowed = state;
}

bool FXCreationAllowed()
{
	return g_FXCreationAllowed;
}

// The FX_* entries the fork's effect TUs call, carried to managed as they came.
void FX_AddLine(const FXLineData_t& data)
{
	IManagedFxClient* pFx = ManagedFxClient();
	if (!pFx)
		return;
	const float f[] = {
		data.m_vecStart.x,         data.m_vecStart.y,       data.m_vecStart.z,         data.m_vecEnd.x,
		data.m_vecEnd.y,           data.m_vecEnd.z,         data.m_vecStartVelocity.x, data.m_vecStartVelocity.y,
		data.m_vecStartVelocity.z, data.m_vecEndVelocity.x, data.m_vecEndVelocity.y,   data.m_vecEndVelocity.z,
		data.m_flStartAlpha,       data.m_flEndAlpha,       data.m_flStartScale,       data.m_flEndScale,
		data.m_flDieTime,
	};
	pFx->AddLine(f, MaterialId(data.m_pMaterial));
}

void FX_AddStaticLine(const Vector& start, const Vector& end, float scale, float life, const char* materialName,
					  unsigned char flags)
{
	if (IManagedFxClient* pFx = ManagedFxClient())
		pFx->AddStaticLine(start.Base(), end.Base(), scale, life, materialName, flags);
}

void FX_AddDiscreetLine(const Vector& start, const Vector& direction, float velocity, float length, float clipLength,
						float scale, float life, const char* shader)
{
	if (IManagedFxClient* pFx = ManagedFxClient())
		pFx->AddDiscreetLine(start.Base(), direction.Base(), velocity, length, clipLength, scale, life, shader);
}

void FX_AddQuad(const FXQuadData_t& data)
{
	IManagedFxClient* pFx = ManagedFxClient();
	if (!pFx)
		return;
	const float f[] = {
		data.m_vecOrigin.x,  data.m_vecOrigin.y,  data.m_vecOrigin.z, data.m_vecNormal.x, data.m_vecNormal.y,
		data.m_vecNormal.z,  data.m_flStartScale, data.m_flEndScale,  data.m_flDieTime,   data.m_flLifeTime,
		data.m_flStartAlpha, data.m_flEndAlpha,   data.m_Color.x,     data.m_Color.y,     data.m_Color.z,
		data.m_flYaw,        data.m_flDeltaYaw,   data.m_flScaleBias, data.m_flAlphaBias,
	};
	// The caller's SetMaterial took the reference the quad gives back.
	pFx->AddQuad(f, data.m_uiFlags, MaterialId(data.m_pMaterial));
}

void FX_AddQuad(const Vector& origin, const Vector& normal, float startSize, float endSize, float sizeBias,
				float startAlpha, float endAlpha, float alphaBias, float yaw, float deltaYaw, const Vector& color,
				float lifeTime, const char* shader, unsigned int flags)
{
	IManagedFxClient* pFx = ManagedFxClient();
	if (!pFx)
		return;
	const float f[] = {
		origin.x,   origin.y, origin.z,  normal.x, normal.y, normal.z, startSize, endSize, sizeBias,
		startAlpha, endAlpha, alphaBias, yaw,      deltaYaw, color.x,  color.y,   color.z, lifeTime,
	};
	pFx->AddQuadNamed(f, flags, shader);
}

void FX_TracerSound(const Vector& start, const Vector& end, int iTracerType)
{
	if (IManagedFxClient* pFx = ManagedFxClient())
		pFx->TracerSound(start.Base(), end.Base(), iTracerType);
}

// IShimFxClient: the effect list's carry-out.
int32_t SourceSharp_FxMaterialFind(const char* pName)
{
	if (!pName || !materials)
		return 0;
	IMaterial* pMaterial = materials->FindMaterial(pName, TEXTURE_GROUP_CLIENT_EFFECTS);
	if (!pMaterial)
		return 0;
	pMaterial->IncrementReferenceCount();
	return MaterialId(pMaterial);
}

void SourceSharp_FxMaterialAddRef(int32_t nMaterial)
{
	if (IMaterial* pMaterial = MaterialOf(nMaterial))
		pMaterial->IncrementReferenceCount();
}

void SourceSharp_FxMaterialRelease(int32_t nMaterial)
{
	if (IMaterial* pMaterial = MaterialOf(nMaterial))
		pMaterial->DecrementReferenceCount();
}

int32_t SourceSharp_FxMaterialName(int32_t nMaterial, char* pOut, int32_t nOutSize)
{
	IMaterial* pMaterial = MaterialOf(nMaterial);
	const char* pName = pMaterial ? pMaterial->GetName() : NULL;
	if (!pName)
		return -1;
	if (pOut && nOutSize > 0)
		Q_strncpy(pOut, pName, nOutSize);
	return Q_strlen(pName);
}

void SourceSharp_FxDrawQuads(int32_t nMaterial, int32_t nVertices, const float* pVertices, int32_t bByteColor)
{
	// Twelve floats a vertex (FxVertex): position, s t, r g b a, normal.
	const int kStride = 12;
	if (!pVertices || nVertices < 4 || (nVertices % 4) != 0)
		return;

	CMatRenderContextPtr pRenderContext(materials);
	IMesh* pMesh = pRenderContext->GetDynamicMesh(true, NULL, NULL, MaterialOf(nMaterial));
	CMeshBuilder meshBuilder;
	meshBuilder.Begin(pMesh, MATERIAL_QUADS, nVertices / 4);
	for (int i = 0; i < nVertices; ++i)
	{
		const float* v = pVertices + i * kStride;
		meshBuilder.Position3fv(v);
		meshBuilder.TexCoord2f(0, v[3], v[4]);
		if (bByteColor)
			meshBuilder.Color4ub((unsigned char)v[5], (unsigned char)v[6], (unsigned char)v[7], (unsigned char)v[8]);
		else
			meshBuilder.Color4fv(v + 5);
		meshBuilder.Normal3fv(v + 9);
		meshBuilder.AdvanceVertex();
	}
	meshBuilder.End();
	pMesh->Draw();
}

void SourceSharp_FxView(float* pOut)
{
	const Vector& origin = CurrentViewOrigin();
	const Vector& forward = CurrentViewForward();
	pOut[0] = origin.x;
	pOut[1] = origin.y;
	pOut[2] = origin.z;
	pOut[3] = forward.x;
	pOut[4] = forward.y;
	pOut[5] = forward.z;
	pOut[6] = (float)ScreenWidth();
}

void SourceSharp_FxMainViewOrigin(float* pOutXYZ)
{
	const Vector& origin = MainViewOrigin();
	pOutXYZ[0] = origin.x;
	pOutXYZ[1] = origin.y;
	pOutXYZ[2] = origin.z;
}

int32_t SourceSharp_FxLocalPlayer()
{
	return engine ? engine->GetLocalPlayer() : 0;
}

void SourceSharp_FxViewAngles(float* pOutPYR)
{
	QAngle angles(0, 0, 0);
	if (engine)
		engine->GetViewAngles(angles);
	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
}

int32_t SourceSharp_FxEntityAttachment(int32_t nEntIndex, int32_t nAttachment, float* pOutXYZ, char* pModelOut,
									   int32_t nModelSize)
{
	if (pModelOut && nModelSize > 0)
		pModelOut[0] = '\0';
	IClientRenderable* pRenderable =
		(nEntIndex >= 0) ? SourceSharp_ClientRenderableFromHandle(SourceSharp_ClientHandleFromIndex(nEntIndex)) : NULL;
	if (!pRenderable)
		return -1;

	// On a miss GetAttachment writes the entity's own origin, which stock used
	// without checking the answer.
	Vector origin(0, 0, 0);
	QAngle angles;
	const bool bFound = pRenderable->GetAttachment(nAttachment, origin, angles);
	pOutXYZ[0] = origin.x;
	pOutXYZ[1] = origin.y;
	pOutXYZ[2] = origin.z;
	if (!bFound)
	{
		if (pModelOut && nModelSize > 0)
			Q_strncpy(pModelOut, modelinfo->GetModelName(pRenderable->GetModel()), nModelSize);
		return 0;
	}
	return 1;
}

void SourceSharp_FxDispatchParticleEffect(int32_t nSystem, const float* pStartXYZ, const float* pEndXYZ,
										  const float* pAnglesPYR, int32_t nEntIndex)
{
	// No entity: DispatchParticleEffect clears the effect's entity on this side
	// whatever it is handed.
	(void)nEntIndex;
	DispatchParticleEffect(nSystem, Vector(pStartXYZ[0], pStartXYZ[1], pStartXYZ[2]),
						   Vector(pEndXYZ[0], pEndXYZ[1], pEndXYZ[2]),
						   QAngle(pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2]), NULL);
}

// The shell-eject effects' owner render angles, and the brass.
int32_t SourceSharp_FxEntityRenderAngles(int32_t nEntIndex, float* pOutPYR)
{
	IClientRenderable* pRenderable =
		(nEntIndex >= 0) ? SourceSharp_ClientRenderableFromHandle(SourceSharp_ClientHandleFromIndex(nEntIndex)) : NULL;
	if (!pRenderable)
		return 0;
	const QAngle& angles = pRenderable->GetRenderAngles();
	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
	return 1;
}

void SourceSharp_FxEjectBrass(const float* pOriginXYZ, const float* pAnglesPYR, const float* pGunAnglesPYR,
							  int32_t nType)
{
	tempents->EjectBrass(Vector(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]),
						 QAngle(pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2]),
						 QAngle(pGunAnglesPYR[0], pGunAnglesPYR[1], pGunAnglesPYR[2]), nType);
}

// The client effect precache registry. CLIENTEFFECT_REGISTER objects register
// here at static init, so the registry stays native; what is done with it at a
// level's start and end is managed's (IManagedFxClient::EffectsPrecache). The
// subclass exists to reach the protected m_Effects.
class CSourceSharpEffectPrecacheSystem : public CClientEffectPrecacheSystem
{
public:
	int Count() const { return m_Effects.Count(); }

	void CacheAt(int i, bool bPrecache)
	{
		if (m_Effects.IsValidIndex(i))
			m_Effects[i]->Cache(bPrecache);
	}
};

static CSourceSharpEffectPrecacheSystem* EffectPrecache()
{
	static CSourceSharpEffectPrecacheSystem s_ClientEffectPrecacheSystem;
	return &s_ClientEffectPrecacheSystem;
}

CClientEffectPrecacheSystem* ClientEffectPrecacheSystem()
{
	return EffectPrecache();
}

void CClientEffectPrecacheSystem::LevelInitPreEntity()
{
	if (IManagedFxClient* pFx = ManagedFxClient())
		pFx->EffectsPrecache(0);
}

void CClientEffectPrecacheSystem::LevelShutdownPreEntity() {}

void CClientEffectPrecacheSystem::LevelShutdownPostEntity()
{
	if (IManagedFxClient* pFx = ManagedFxClient())
		pFx->EffectsPrecache(1);
}

// The registry's own memory, at the DLL's shutdown.
void CClientEffectPrecacheSystem::Shutdown()
{
	m_Effects.Purge();
}

void CClientEffectPrecacheSystem::Register(IClientEffect* effect)
{
	m_Effects.AddToTail(effect);
}

int32_t SourceSharp_FxPrecacheCount()
{
	return EffectPrecache()->Count();
}

void SourceSharp_FxPrecacheCache(int32_t nIndex, int32_t bPrecache)
{
	EffectPrecache()->CacheAt(nIndex, bPrecache != 0);
}

void SourceSharp_FxCacheUsedMaterials()
{
	materials->CacheUsedMaterials();
}

void SourceSharp_FxCacheMaterialHandles()
{
	FX_CacheMaterialHandles();
}

// The smoke fog overlay: the two globals the smoke volume and debug view write,
// the material, and the quad. Whether and how it draws is managed's
// (IManagedFxClient::SmokeFogPlan).
static IMaterial* g_pSmokeFogMaterial = NULL;

float g_SmokeFogOverlayAlpha;
Vector g_SmokeFogOverlayColor;

CLIENTEFFECT_REGISTER_BEGIN(PrecacheSmokeFogOverlay)
CLIENTEFFECT_MATERIAL("particle/screenspace_fog")
CLIENTEFFECT_REGISTER_END()

void InitSmokeFogOverlay()
{
	TermSmokeFogOverlay();

	g_SmokeFogOverlayAlpha = 0;

	if (materials)
	{
		g_pSmokeFogMaterial = materials->FindMaterial("particle/screenspace_fog", TEXTURE_GROUP_CLIENT_EFFECTS);
		if (g_pSmokeFogMaterial)
			g_pSmokeFogMaterial->IncrementReferenceCount();
	}
}

void TermSmokeFogOverlay()
{
	if (g_pSmokeFogMaterial)
	{
		g_pSmokeFogMaterial->DecrementReferenceCount();
		g_pSmokeFogMaterial = NULL;
	}
}

void DrawSmokeFogOverlay()
{
	IManagedFxClient* pFx = ManagedFxClient();
	if (!pFx)
		return;

	float color[3] = {g_SmokeFogOverlayColor.x, g_SmokeFogOverlayColor.y, g_SmokeFogOverlayColor.z};
	float rgba[4];
	if (!pFx->SmokeFogPlan(g_SmokeFogOverlayAlpha, (g_pSmokeFogMaterial && materials) ? 1 : 0, color, rgba))
		return;

	// The plan overwrites the overlay colour, as stock's draw did.
	g_SmokeFogOverlayColor.Init(color[0], color[1], color[2]);

	CMatRenderContextPtr pRenderContext(materials);

	pRenderContext->MatrixMode(MATERIAL_PROJECTION);
	pRenderContext->LoadIdentity();
	pRenderContext->Ortho(0, 0, 1, 1, -99999, 99999);

	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->LoadIdentity();

	pRenderContext->MatrixMode(MATERIAL_MODEL);
	pRenderContext->LoadIdentity();

	IMesh* pMesh = pRenderContext->GetDynamicMesh(false, NULL, NULL, g_pSmokeFogMaterial);
	CMeshBuilder meshBuilder;

	// A full-screen quad in the unit ortho view, at stock's depth.
	const float dist = 10;
	const float corners[4][2] = {{0, 0}, {0, 1}, {1, 1}, {1, 0}};

	meshBuilder.Begin(pMesh, MATERIAL_QUADS, 1);
	for (int i = 0; i < 4; ++i)
	{
		meshBuilder.Position3f(corners[i][0], corners[i][1], dist);
		meshBuilder.Color4f(rgba[0], rgba[1], rgba[2], rgba[3]);
		meshBuilder.TexCoord2f(0, 0.0f, 0.0f);
		meshBuilder.AdvanceVertex();
	}
	meshBuilder.End();
	pMesh->Draw();
}
