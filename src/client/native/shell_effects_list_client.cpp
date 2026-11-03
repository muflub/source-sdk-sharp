//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The client-side effect list's native half; the list and its effect kinds are
// managed.
//   CEffectsListClient       the IEffectsList the view draws and the client DLL
//                            flushes, the material table, the smoke fog overlay and
//                            two client systems rows (shell_effects_list_client.h).
//   FX_Add*, FX_TracerSound  entries the probe build's effect probes call, carried
//                            to managed with their arguments; an IMaterial* crosses
//                            as an id.
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
#include "shell_effects_list_client.h"

#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "client_entity_list.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

int32_t CEffectsListClient::MaterialId(IMaterial* pMaterial)
{
	if (!pMaterial)
		return 0;
	for (int i = 0; i < m_nMaterials; ++i)
	{
		if (m_pMaterials[i] == pMaterial)
			return i + 1;
	}
	if (m_nMaterials >= kMaxMaterials)
		return 0;
	m_pMaterials[m_nMaterials++] = pMaterial;
	return m_nMaterials;
}

IMaterial* CEffectsListClient::MaterialOf(int32_t nMaterial) const
{
	return (nMaterial >= 1 && nMaterial <= m_nMaterials) ? m_pMaterials[nMaterial - 1] : NULL;
}

// A material id as the material, for the pixel-visibility command stream.
// A material as its id, no reference taken, for the glow overlays.
void CEffectsListClient::DrawEffects(double frametime)
{
	// The view passes gpGlobals->frametime, a float: crossing as one is exact.
	if (IManagedFxClient* pFx = Managed())
		pFx->DrawEffects((float)frametime);
}

void CEffectsListClient::Flush()
{
	if (IManagedFxClient* pFx = Managed())
		pFx->FlushEffects();
}

void CEffectsListClient::CBulletWhizRow::LevelInitPreEntity()
{
	if (IManagedFxClient* pFx = m_Effects.Managed())
		pFx->EffectsLevelInit();
}

// IShimFxClient: the effect list's carry-out.
int32_t CEffectsListClient::FxMaterialFind(const char* pName)
{
	if (!pName || !m_Engines.m_pMaterials)
		return 0;
	IMaterial* pMaterial = m_Engines.m_pMaterials->FindMaterial(pName, TEXTURE_GROUP_CLIENT_EFFECTS);
	if (!pMaterial)
		return 0;
	pMaterial->IncrementReferenceCount();
	return MaterialId(pMaterial);
}

void CEffectsListClient::FxMaterialAddRef(int32_t nMaterial)
{
	if (IMaterial* pMaterial = MaterialOf(nMaterial))
		pMaterial->IncrementReferenceCount();
}

void CEffectsListClient::FxMaterialRelease(int32_t nMaterial)
{
	if (IMaterial* pMaterial = MaterialOf(nMaterial))
		pMaterial->DecrementReferenceCount();
}

int32_t CEffectsListClient::FxMaterialName(int32_t nMaterial, char* pOut, int32_t nOutSize)
{
	IMaterial* pMaterial = MaterialOf(nMaterial);
	const char* pName = pMaterial ? pMaterial->GetName() : NULL;
	if (!pName)
		return -1;
	if (pOut && nOutSize > 0)
		Q_strncpy(pOut, pName, nOutSize);
	return Q_strlen(pName);
}

void CEffectsListClient::FxDrawQuads(int32_t nMaterial, int32_t nVertices, const float* pVertices, int32_t bByteColor)
{
	// Twelve floats a vertex (FxVertex): position, s t, r g b a, normal.
	const int kStride = 12;
	if (!pVertices || nVertices < 4 || (nVertices % 4) != 0)
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
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

void CEffectsListClient::FxView(float* pOut)
{
	const Vector& origin = m_Cameras.CurrentOrigin();
	const Vector& forward = m_Cameras.CurrentForward();
	pOut[0] = origin.x;
	pOut[1] = origin.y;
	pOut[2] = origin.z;
	pOut[3] = forward.x;
	pOut[4] = forward.y;
	pOut[5] = forward.z;
	pOut[6] = (float)ScreenWidth();
}

void CEffectsListClient::FxMainViewOrigin(float* pOutXYZ)
{
	const Vector& origin = m_Cameras.MainOrigin();
	pOutXYZ[0] = origin.x;
	pOutXYZ[1] = origin.y;
	pOutXYZ[2] = origin.z;
}

int32_t CEffectsListClient::FxLocalPlayer()
{
	return engine ? m_Engines.m_pEngine->GetLocalPlayer() : 0;
}

void CEffectsListClient::FxViewAngles(float* pOutPYR)
{
	QAngle angles(0, 0, 0);
	if (m_Engines.m_pEngine)
		m_Engines.m_pEngine->GetViewAngles(angles);
	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
}

int32_t CEffectsListClient::FxEntityAttachment(int32_t nEntIndex, int32_t nAttachment, float* pOutXYZ, char* pModelOut,
											   int32_t nModelSize)
{
	if (pModelOut && nModelSize > 0)
		pModelOut[0] = '\0';
	IClientRenderable* pRenderable =
		(nEntIndex >= 0) ? m_EntityList.RenderableFromHandle(m_EntityList.HandleFromIndex(nEntIndex)) : NULL;
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
			Q_strncpy(pModelOut, m_Engines.m_pModelInfo->GetModelName(pRenderable->GetModel()), nModelSize);
		return 0;
	}
	return 1;
}

// The shell-eject effects' owner render angles, and the brass.
int32_t CEffectsListClient::FxEntityRenderAngles(int32_t nEntIndex, float* pOutPYR)
{
	IClientRenderable* pRenderable =
		(nEntIndex >= 0) ? m_EntityList.RenderableFromHandle(m_EntityList.HandleFromIndex(nEntIndex)) : NULL;
	if (!pRenderable)
		return 0;
	const QAngle& angles = pRenderable->GetRenderAngles();
	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
	return 1;
}

void CEffectsListClient::FxEjectBrass(const float* pOriginXYZ, const float* pAnglesPYR, const float* pGunAnglesPYR,
									  int32_t nType)
{
	tempents->EjectBrass(Vector(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]),
						 QAngle(pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2]),
						 QAngle(pGunAnglesPYR[0], pGunAnglesPYR[1], pGunAnglesPYR[2]), nType);
}

// The materials the managed and native effects draw with, precached as stock's
// CLIENTEFFECT_REGISTER blocks did: referenced at a level's start and released at its end,
// by managed (IManagedFxClient::EffectsPrecache).
static const char* const kEffectPrecacheMaterials[] = {
	// PrecacheEffectsTest (shell_effects_list_client.cpp).
	"effects/spark",
	"effects/gunshiptracer",
	"effects/bluespark",
	// PrecacheSmokeFogOverlay (shell_effects_list_client.cpp).
	"particle/screenspace_fog",
	// PrecacheEffectGlow (shell_glow_client.cpp).
	"sun/overlay",
	"sprites/light_glow02_add_noz",
	// PrecacheEffectWarp (shell_glow_client.cpp).
	"sun/overlay",
	// PrecacheEffectImpacts (shell_impact_effects.cpp).
	"effects/fleck_cement1",
	"effects/fleck_cement2",
	"effects/fleck_antlion1",
	"effects/fleck_antlion2",
	"effects/fleck_wood1",
	"effects/fleck_wood2",
	"effects/blood",
	"effects/blood2",
	"sprites/bloodspray",
	"particle/particle_noisesphere",
	// PrecacheEffectMuzzleFlash (shell_legacy_tempents_client.cpp).
	"effects/muzzleflash1",
	"effects/muzzleflash2",
	"effects/muzzleflash3",
	"effects/muzzleflash4",
	"effects/muzzleflash1_noz",
	"effects/muzzleflash2_noz",
	"effects/muzzleflash3_noz",
	"effects/muzzleflash4_noz",
	"effects/combinemuzzle1",
	"effects/combinemuzzle2",
	"effects/combinemuzzle1_noz",
	"effects/combinemuzzle2_noz",
	"effects/strider_muzzle",
	// PrecacheEffectBloodSpray (shell_managed_particles.cpp).
	"effects/blood_core",
	"effects/blood_gore",
	"effects/blood_drop",
	"effects/blood_puff",
	// PrecacheEffectExplosion (shell_managed_particles.cpp).
	"effects/fire_cloud1",
	"effects/fire_cloud2",
	"effects/fire_embers1",
	"effects/fire_embers2",
	"effects/fire_embers3",
	"particle/particle_smokegrenade",
	"particle/particle_smokegrenade1",
	"effects/splash3",
	"effects/splashwake1",
	// PrecacheMuzzleFlash (shell_managed_particles.cpp).
	"effects/muzzleflash1",
	"effects/muzzleflash2",
	"effects/muzzleflash3",
	"effects/muzzleflash4",
	"effects/bluemuzzle",
	"effects/gunshipmuzzle",
	"effects/gunshiptracer",
	"effects/huntertracer",
	"sprites/physcannon_bluelight2",
	"effects/combinemuzzle1",
	"effects/combinemuzzle2",
	"effects/combinemuzzle2_nocull",
	// PrecacheEffectSplash (shell_managed_particles.cpp).
	"effects/splash1",
	"effects/splash2",
	"effects/splash4",
	"effects/slime1",
	// PrecacheEffectSparks (shell_managed_particles.cpp).
	"effects/combinemuzzle1_nocull",
	"effects/combinemuzzle2_nocull",
	"effects/energyball",
	"effects/energysplash",
	"effects/spark",
	"effects/yellowflare",
	"effects/yellowflare_noz",
	"sprites/blueflare1",
	"sprites/rico1",
	"sprites/rico1_noz",
	// PrecacheOcclusionProxy (shell_pixelvis_client.cpp).
	"engine/occlusionproxy",
	"engine/occlusionproxy_countdraw",
	// PrecachePrecipitationTracers (shell_precipitation_client.cpp).
	"particle/rain",
	"particle/snow",
	// PrecachePlayerDecal (shell_te_backends_client.cpp): without these the spray still draws,
	// with a hitch on the first spray of each level. Stock's list repeats playerlogo40..49; each
	// name appears once here.
	"decals/playerlogo01",
	"decals/playerlogo02",
	"decals/playerlogo03",
	"decals/playerlogo04",
	"decals/playerlogo05",
	"decals/playerlogo06",
	"decals/playerlogo07",
	"decals/playerlogo08",
	"decals/playerlogo09",
	"decals/playerlogo10",
	"decals/playerlogo11",
	"decals/playerlogo12",
	"decals/playerlogo13",
	"decals/playerlogo14",
	"decals/playerlogo15",
	"decals/playerlogo16",
	"decals/playerlogo17",
	"decals/playerlogo18",
	"decals/playerlogo19",
	"decals/playerlogo20",
	"decals/playerlogo21",
	"decals/playerlogo22",
	"decals/playerlogo23",
	"decals/playerlogo24",
	"decals/playerlogo25",
	"decals/playerlogo26",
	"decals/playerlogo27",
	"decals/playerlogo28",
	"decals/playerlogo29",
	"decals/playerlogo30",
	"decals/playerlogo31",
	"decals/playerlogo32",
	"decals/playerlogo33",
	"decals/playerlogo34",
	"decals/playerlogo35",
	"decals/playerlogo36",
	"decals/playerlogo37",
	"decals/playerlogo38",
	"decals/playerlogo39",
	"decals/playerlogo40",
	"decals/playerlogo41",
	"decals/playerlogo42",
	"decals/playerlogo43",
	"decals/playerlogo44",
	"decals/playerlogo45",
	"decals/playerlogo46",
	"decals/playerlogo47",
	"decals/playerlogo48",
	"decals/playerlogo49",
	"decals/playerlogo50",
	"decals/playerlogo51",
	"decals/playerlogo52",
	"decals/playerlogo53",
	"decals/playerlogo54",
	"decals/playerlogo55",
	"decals/playerlogo56",
	"decals/playerlogo57",
	"decals/playerlogo58",
	"decals/playerlogo59",
	"decals/playerlogo60",
	"decals/playerlogo61",
	"decals/playerlogo62",
	"decals/playerlogo63",
	"decals/playerlogo64",
	// PrecacheEffectGlassShatter (shell_te_backends_client.cpp): the impact code caches handles
	// for them but does not precache them.
	"effects/fleck_glass1",
	"effects/fleck_glass2",
	"effects/fleck_tile1",
	"effects/fleck_tile2",
};

// The post-processing chain's: stock precached them only at DX level 90 and up.
static const char* const kPostProcessingMaterials[] = {
	"dev/blurfiltery_and_add_nohdr",
	"dev/blurfilterx",
	"dev/blurfilterx_nohdr",
	"dev/blurfiltery",
	"dev/blurfiltery_nohdr",
	"dev/bloomadd",
	"dev/downsample",
	"dev/downsample_non_hdr",
	"dev/no_pixel_write",
	"dev/lumcompare",
	"dev/floattoscreen_combine",
	"dev/copyfullframefb_vanilla",
	"dev/copyfullframefb",
	"dev/engine_post",
	"dev/motion_blur",
	"dev/upscale",
};

void CEffectsListClient::CPrecacheRow::LevelInitPreEntity()
{
	if (IManagedFxClient* pFx = m_Effects.Managed())
		pFx->EffectsPrecache(0);
}

void CEffectsListClient::CPrecacheRow::LevelShutdownPostEntity()
{
	if (IManagedFxClient* pFx = m_Effects.Managed())
		pFx->EffectsPrecache(1);
}

int32_t CEffectsListClient::FxPrecacheCount()
{
	return (int32_t)(ARRAYSIZE(kEffectPrecacheMaterials) + ARRAYSIZE(kPostProcessingMaterials));
}

// Precaches a material by artificially raising its reference count, or lets it go.
void CEffectsListClient::FxPrecacheCache(int32_t nIndex, int32_t bPrecache)
{
	const int32_t nEffects = (int32_t)ARRAYSIZE(kEffectPrecacheMaterials);
	if (nIndex < 0 || nIndex >= FxPrecacheCount())
		return;
	if (nIndex >= nEffects && m_Engines.m_pEngine->GetDXSupportLevel() < 90)
		return;

	const char* pszName =
		nIndex < nEffects ? kEffectPrecacheMaterials[nIndex] : kPostProcessingMaterials[nIndex - nEffects];
	IMaterial* pMaterial = m_Engines.m_pMaterials->FindMaterial(pszName, TEXTURE_GROUP_CLIENT_EFFECTS);
	if (IsErrorMaterial(pMaterial))
		return;

	if (bPrecache)
		pMaterial->IncrementReferenceCount();
	else
		pMaterial->DecrementReferenceCount();
}

void CEffectsListClient::FxCacheUsedMaterials()
{
	m_Engines.m_pMaterials->CacheUsedMaterials();
}

void CEffectsListClient::FxCacheMaterialHandles()
{
	FX_CacheMaterialHandles();
}

void CEffectsListClient::InitSmokeFog()
{
	TermSmokeFog();

	m_flSmokeFogAlpha = 0;

	if (m_Engines.m_pMaterials)
	{
		m_pSmokeFogMaterial =
			m_Engines.m_pMaterials->FindMaterial("particle/screenspace_fog", TEXTURE_GROUP_CLIENT_EFFECTS);
		if (m_pSmokeFogMaterial)
			m_pSmokeFogMaterial->IncrementReferenceCount();
	}
}

void CEffectsListClient::TermSmokeFog()
{
	if (m_pSmokeFogMaterial)
	{
		m_pSmokeFogMaterial->DecrementReferenceCount();
		m_pSmokeFogMaterial = NULL;
	}
}

void CEffectsListClient::DrawSmokeFog()
{
	IManagedFxClient* pFx = Managed();
	if (!pFx)
		return;

	float rgba[4];
	if (!pFx->SmokeFogPlan(m_flSmokeFogAlpha, (m_pSmokeFogMaterial && m_Engines.m_pMaterials) ? 1 : 0, m_SmokeFogColor,
						   rgba))
		return;

	// The plan overwrites the overlay colour, as stock's draw did.
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->MatrixMode(MATERIAL_PROJECTION);
	pRenderContext->LoadIdentity();
	pRenderContext->Ortho(0, 0, 1, 1, -99999, 99999);

	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->LoadIdentity();

	pRenderContext->MatrixMode(MATERIAL_MODEL);
	pRenderContext->LoadIdentity();

	IMesh* pMesh = pRenderContext->GetDynamicMesh(false, NULL, NULL, m_pSmokeFogMaterial);
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
