//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The client's temp entity drawing primitives. Each temp entity's decode and
// decision are managed; these slots carry out the draw, ending in g_pEffects,
// the engine's IVEfx decal system, the static prop manager, or the effect
// registry. Arguments are unpacked in shell_te_backend_args.h, which is
// engine-free so its argument order can be tested.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "IEffects.h"

#include "iefx.h"
#include "fx.h"
#include "decals.h"

#include "util_shared.h"
#include "c_te_effect_dispatch.h"

#include "materialsystem/itexture.h"
#include "materialsystem/imaterialvar.h"
#include "clienteffectprecachesystem.h"
#include "view.h"
#include "networkstringtable_clientdll.h"

#include "vphysics_interface.h"

#include "shell_cliententitylist.h"

#include "shell_te_backend_args.h"

using namespace TeBackendArgs;

// A round off an energy shield. bExplosive crosses as an int32 because a bool
// has no fixed width in C.
void SourceSharp_ClientEnergySplash(const float* pPosXYZ, const float* pDirXYZ, int32_t bExplosive)
{
	if (!pPosXYZ || !pDirXYZ || !g_pEffects)
		return;

	const Vector vecPos(pPosXYZ[0], pPosXYZ[1], pPosXYZ[2]);
	const Vector vecDir(pDirXYZ[0], pDirXYZ[1], pDirXYZ[2]);

	g_pEffects->EnergySplash(vecPos, vecDir, bExplosive != 0);
}

// The client's IEffects family. g_pEffects applies its own PVS filter inside
// each call, so callers must not add one.

// Electric sparks (FX_ElectricSpark). A NULL direction is a spark with no
// preferred direction, which differs from one aimed at the origin.
void SourceSharp_ClientSparks(const float* pPositionXYZ, int32_t nMagnitude, int32_t nTrailLength,
							  const float* pDirectionXYZ)
{
	const EffectRequest req = Sparks(pPositionXYZ, nMagnitude, nTrailLength, pDirectionXYZ);

	if (!req.bDraw || !g_pEffects)
		return;

	const Vector vecPosition(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z);
	const Vector vecDirection(req.vecDirection.x, req.vecDirection.y, req.vecDirection.z);

	g_pEffects->Sparks(vecPosition, req.nMagnitude, req.nTrailLength, req.bHasDirection ? &vecDirection : NULL);
}

// A ricochet: metal sparks and the sound.
void SourceSharp_ClientRicochet(const float* pPositionXYZ, const float* pDirectionXYZ)
{
	const EffectRequest req = PositionAndDirection(pPositionXYZ, pDirectionXYZ);

	if (!req.bDraw || !g_pEffects)
		return;

	const Vector vecPosition(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z);
	const Vector vecDirection(req.vecDirection.x, req.vecDirection.y, req.vecDirection.z);

	g_pEffects->Ricochet(vecPosition, vecDirection);
}

// The same sparks with no sound (FX_MetalSpark).
void SourceSharp_ClientMetalSparks(const float* pPositionXYZ, const float* pDirectionXYZ)
{
	const EffectRequest req = PositionAndDirection(pPositionXYZ, pDirectionXYZ);

	if (!req.bDraw || !g_pEffects)
		return;

	const Vector vecPosition(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z);
	const Vector vecDirection(req.vecDirection.x, req.vecDirection.y, req.vecDirection.z);

	g_pEffects->MetalSparks(vecPosition, vecDirection);
}

// Smoke sprites (FX_Smoke). flScale arrives already multiplied by ten, as stock
// does it in the decode.
void SourceSharp_ClientSmoke(const float* pOriginXYZ, int32_t nModelIndex, float flScale, float flFrameRate)
{
	const EffectRequest req = Smoke(pOriginXYZ, nModelIndex, flScale, flFrameRate);

	if (!req.bDraw || !g_pEffects)
		return;

	g_pEffects->Smoke(Vector(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z), req.nModelIndex, req.flScale,
					  req.flFrameRate);
}

// A muzzle flash. MuzzleFlash prints a line for an unknown type, as stock does.
void SourceSharp_ClientMuzzleFlash(const float* pOriginXYZ, const float* pAnglesPYR, float flScale, int32_t nType)
{
	const EffectRequest req = MuzzleFlash(pOriginXYZ, pAnglesPYR, flScale, nType);

	if (!req.bDraw || !g_pEffects)
		return;

	g_pEffects->MuzzleFlash(Vector(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z),
							QAngle(req.vecAngles.x, req.vecAngles.y, req.vecAngles.z), req.flScale, req.nType);
}

// A decal on the world, a brush model or a static prop. The entity is resolved
// here because DecalShoot wants a model_t*, which does not cross; it answers for
// either hierarchy, and entity 0 is the world. Callers test r_decals first.
void SourceSharp_ClientDecalShoot(int32_t nDecalIndex, int32_t nEntity, const float* pPositionXYZ,
								  const float* pSAxisXYZ, int32_t nFlags)
{
	const DecalRequest req = DecalShoot(nDecalIndex, nEntity, pPositionXYZ, pSAxisXYZ, nFlags);

	if (!req.bDraw || !effects)
		return;

	IClientEntity* pEntity = SourceSharp_ClientEntityFromIndex(req.nEntity);

	if (!pEntity)
	{
		// Stock's own diagnostic: an entity that left the PVS is ordinary.
		DevMsg(1, "SourceSharp decal: no entity %i\n", req.nEntity);
		return;
	}

	const Vector vecPosition(req.vecPosition.x, req.vecPosition.y, req.vecPosition.z);
	const Vector vecSAxis(req.vecSAxis.x, req.vecSAxis.y, req.vecSAxis.z);

	effects->DecalShoot(req.nDecalIndex, req.nEntity, pEntity->GetModel(), pEntity->GetAbsOrigin(),
						pEntity->GetAbsAngles(), vecPosition, req.bHasSAxis ? &vecSAxis : NULL, req.nFlags);
}

// The sixty-four spray logo materials, precached. Without this the spray still
// draws, with a hitch on the first spray of each level. Stock's list repeats
// playerlogo40..49; each name appears once here.
CLIENTEFFECT_REGISTER_BEGIN(PrecachePlayerDecal)
CLIENTEFFECT_MATERIAL("decals/playerlogo01")
CLIENTEFFECT_MATERIAL("decals/playerlogo02")
CLIENTEFFECT_MATERIAL("decals/playerlogo03")
CLIENTEFFECT_MATERIAL("decals/playerlogo04")
CLIENTEFFECT_MATERIAL("decals/playerlogo05")
CLIENTEFFECT_MATERIAL("decals/playerlogo06")
CLIENTEFFECT_MATERIAL("decals/playerlogo07")
CLIENTEFFECT_MATERIAL("decals/playerlogo08")
CLIENTEFFECT_MATERIAL("decals/playerlogo09")
CLIENTEFFECT_MATERIAL("decals/playerlogo10")
CLIENTEFFECT_MATERIAL("decals/playerlogo11")
CLIENTEFFECT_MATERIAL("decals/playerlogo12")
CLIENTEFFECT_MATERIAL("decals/playerlogo13")
CLIENTEFFECT_MATERIAL("decals/playerlogo14")
CLIENTEFFECT_MATERIAL("decals/playerlogo15")
CLIENTEFFECT_MATERIAL("decals/playerlogo16")
CLIENTEFFECT_MATERIAL("decals/playerlogo17")
CLIENTEFFECT_MATERIAL("decals/playerlogo18")
CLIENTEFFECT_MATERIAL("decals/playerlogo19")
CLIENTEFFECT_MATERIAL("decals/playerlogo20")
CLIENTEFFECT_MATERIAL("decals/playerlogo21")
CLIENTEFFECT_MATERIAL("decals/playerlogo22")
CLIENTEFFECT_MATERIAL("decals/playerlogo23")
CLIENTEFFECT_MATERIAL("decals/playerlogo24")
CLIENTEFFECT_MATERIAL("decals/playerlogo25")
CLIENTEFFECT_MATERIAL("decals/playerlogo26")
CLIENTEFFECT_MATERIAL("decals/playerlogo27")
CLIENTEFFECT_MATERIAL("decals/playerlogo28")
CLIENTEFFECT_MATERIAL("decals/playerlogo29")
CLIENTEFFECT_MATERIAL("decals/playerlogo30")
CLIENTEFFECT_MATERIAL("decals/playerlogo31")
CLIENTEFFECT_MATERIAL("decals/playerlogo32")
CLIENTEFFECT_MATERIAL("decals/playerlogo33")
CLIENTEFFECT_MATERIAL("decals/playerlogo34")
CLIENTEFFECT_MATERIAL("decals/playerlogo35")
CLIENTEFFECT_MATERIAL("decals/playerlogo36")
CLIENTEFFECT_MATERIAL("decals/playerlogo37")
CLIENTEFFECT_MATERIAL("decals/playerlogo38")
CLIENTEFFECT_MATERIAL("decals/playerlogo39")
CLIENTEFFECT_MATERIAL("decals/playerlogo40")
CLIENTEFFECT_MATERIAL("decals/playerlogo41")
CLIENTEFFECT_MATERIAL("decals/playerlogo42")
CLIENTEFFECT_MATERIAL("decals/playerlogo43")
CLIENTEFFECT_MATERIAL("decals/playerlogo44")
CLIENTEFFECT_MATERIAL("decals/playerlogo45")
CLIENTEFFECT_MATERIAL("decals/playerlogo46")
CLIENTEFFECT_MATERIAL("decals/playerlogo47")
CLIENTEFFECT_MATERIAL("decals/playerlogo48")
CLIENTEFFECT_MATERIAL("decals/playerlogo49")
CLIENTEFFECT_MATERIAL("decals/playerlogo50")
CLIENTEFFECT_MATERIAL("decals/playerlogo51")
CLIENTEFFECT_MATERIAL("decals/playerlogo52")
CLIENTEFFECT_MATERIAL("decals/playerlogo53")
CLIENTEFFECT_MATERIAL("decals/playerlogo54")
CLIENTEFFECT_MATERIAL("decals/playerlogo55")
CLIENTEFFECT_MATERIAL("decals/playerlogo56")
CLIENTEFFECT_MATERIAL("decals/playerlogo57")
CLIENTEFFECT_MATERIAL("decals/playerlogo58")
CLIENTEFFECT_MATERIAL("decals/playerlogo59")
CLIENTEFFECT_MATERIAL("decals/playerlogo60")
CLIENTEFFECT_MATERIAL("decals/playerlogo61")
CLIENTEFFECT_MATERIAL("decals/playerlogo62")
CLIENTEFFECT_MATERIAL("decals/playerlogo63")
CLIENTEFFECT_MATERIAL("decals/playerlogo64")
CLIENTEFFECT_REGISTER_END()

// Managed reads the logo CRC off player_info_t at this offset.
COMPILE_TIME_ASSERT(offsetof(player_info_t, customFiles) == 112);

// A player's spray logo. Which material, which texture and every gate are
// managed's; this finds both by name (a name that does not resolve draws
// nothing), points the logo material at the player's texture, and shoots the
// decal on the entity.
void SourceSharp_ClientPlayerLogoDecal(const float* pPositionXYZ, int32_t nPlayer, int32_t nEntity,
									   const char* pszMaterial, const char* pszTexture)
{
	if (!pPositionXYZ || !pszMaterial || !pszTexture || !effects || !materials)
		return;

	IClientEntity* pEntity = SourceSharp_ClientEntityFromIndex(nEntity);

	if (!pEntity)
		return;

	IMaterial* pLogo = materials->FindMaterial(pszMaterial, TEXTURE_GROUP_DECAL);

	if (IsErrorMaterial(pLogo))
		return;

	ITexture* pTexture = materials->FindTexture(pszTexture, TEXTURE_GROUP_DECAL);

	if (IsErrorTexture(pTexture))
		return;

	// Update the texture used by the material if need be.
	bool bFound = false;
	IMaterialVar* pMatVar = pLogo->FindVar("$basetexture", &bFound);

	if (bFound && pMatVar && pMatVar->GetTextureValue() != pTexture)
	{
		pMatVar->SetTextureValue(pTexture);
		pLogo->RefreshPreservingMaterialVars();
	}

	const color32 rgbaColour = {255, 255, 255, 255};

	effects->PlayerDecalShoot(pLogo, (void*)(intp)nPlayer, nEntity, pEntity->GetModel(), pEntity->GetAbsOrigin(),
							  pEntity->GetAbsAngles(), Vector(pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2]), 0, 0,
							  rgbaColour);
}

// Unused: sprays end in SourceSharp_ClientPlayerLogoDecal. Kept because the
// shim table only appends.
void SourceSharp_ClientPlayerDecalShoot(const float*, int32_t, int32_t) {}

// A decal's index from its name in the engine's table, or -1 for a name the
// engine does not know.
int32_t SourceSharp_ClientDecalIndexFromName(const char* pName)
{
	if (!pName || !pName[0] || !effects)
		return -1;

	return effects->Draw_DecalIndexFromName((char*)pName);
}

// A decal on a static prop. The trace is out-only, and bDoTrace is false as at
// both of stock's call sites.
void SourceSharp_ClientDecalStaticProp(const float* pStartXYZ, const float* pEndXYZ, int32_t nStaticPropIndex,
									   int32_t nDecalIndex)
{
	const StaticPropDecalRequest req = DecalStaticProp(pStartXYZ, pEndXYZ, nStaticPropIndex, nDecalIndex);

	if (!req.bDraw || !staticpropmgr)
		return;

	// r_decals is not checked: neither of stock's callers tests it here.
	const Vector vecStart(req.vecStart.x, req.vecStart.y, req.vecStart.z);
	const Vector vecEnd(req.vecEnd.x, req.vecEnd.y, req.vecEnd.z);

	trace_t tr;
	staticpropmgr->AddDecalToStaticProp(vecStart, vecEnd, req.nStaticPropIndex, req.nDecalIndex, false, tr);
}

// A decal's name from its index, from the game's decal table (decalsystem), not
// the engine's. An index the table does not hold answers a length of zero.
int32_t SourceSharp_ClientDecalNameFromIndex(int32_t nDecalIndex, char* pOut, int32_t nOutSize)
{
	if (!decalsystem)
		return CopyName(NULL, pOut, nOutSize);

	return CopyName(decalsystem->GetDecalNameForIndex(nDecalIndex), pOut, nOutSize);
}

// Decals on entities: C_BaseEntity::AddDecal's two arms. The switch over the
// model type is managed, over ClientEntityModelType. doTrace is false at both
// of stock's call sites, so no trace_t crosses.

// A client entity's model type (modtype_t numbering), or -1 for no entity, no
// model or no modelinfo; -1 is distinct from mod_bad.
int32_t SourceSharp_ClientEntityModelType(int32_t nEntity)
{
	if (nEntity < 0 || !modelinfo)
		return -1;

	IClientEntity* pEntity = SourceSharp_ClientEntityFromIndex(nEntity);

	if (!pEntity)
		return -1;

	const model_t* pModel = pEntity->GetModel();

	if (!pModel)
		return -1;

	// Passed through: SourceSharpModelType uses modtype_t's numbering.
	return (int32_t)modelinfo->GetModelType(pModel);
}

// A decal on a studio model (AddStudioDecal, doTrace false). Managed has
// already tested r_drawmodeldecals and lengthened the ray. Decals are kept per
// model instance, hence CreateModelInstance(); GetBody() is on the interface
// either hierarchy is reached through.
void SourceSharp_ClientDecalStudioModel(int32_t nEntity, const float* pRayStartXYZ, const float* pRayDeltaXYZ,
										int32_t nDecalIndex)
{
	const StudioDecalRequest req = DecalStudioModel(nEntity, pRayStartXYZ, pRayDeltaXYZ, nDecalIndex);

	if (!req.bDraw || !modelrender)
		return;

	IClientEntity* pEntity = SourceSharp_ClientEntityFromIndex(req.nEntity);

	if (!pEntity)
		return;

	pEntity->CreateModelInstance();

	const Vector vecStart(req.vecStart.x, req.vecStart.y, req.vecStart.z);
	const Vector vecDelta(req.vecDelta.x, req.vecDelta.y, req.vecDelta.z);

	Ray_t ray;
	ray.Init(vecStart, vecStart + vecDelta);

	// The delta as managed aimed it: ( start + d ) - start is not d in floating point.
	ray.m_Delta = vecDelta;

	// FIXME: Pass in decal up? (stock)
	const Vector up(0.0f, 0.0f, 1.0f);

	modelrender->AddDecal(pEntity->GetModelInstance(), ray, up, req.nDecalIndex, pEntity->GetBody(), false,
						  ADDDECAL_TO_ALL_LODS);
}

// A decal on a brush model, the world included (AddBrushModelDecal, doTrace
// false). r_decals is not checked, as stock does not. No S axis and no flags,
// as stock passes.
void SourceSharp_ClientDecalBrushModel(int32_t nEntity, const float* pDecalCenterXYZ, int32_t nDecalIndex)
{
	const BrushDecalRequest req = DecalBrushModel(nEntity, pDecalCenterXYZ, nDecalIndex);

	if (!req.bDraw || !effects)
		return;

	IClientEntity* pEntity = SourceSharp_ClientEntityFromIndex(req.nEntity);

	if (!pEntity)
		return;

	const Vector vecCenter(req.vecCenter.x, req.vecCenter.y, req.vecCenter.z);

	effects->DecalShoot(req.nDecalIndex, req.nEntity, pEntity->GetModel(), pEntity->GetAbsOrigin(),
						pEntity->GetAbsAngles(), vecCenter, 0, 0);
}

// The materials the shattered pane draws with, precached; the impact code
// caches handles for them but does not precache them.
CLIENTEFFECT_REGISTER_BEGIN(PrecacheEffectGlassShatter)
CLIENTEFFECT_MATERIAL("effects/fleck_glass1")
CLIENTEFFECT_MATERIAL("effects/fleck_glass2")
CLIENTEFFECT_MATERIAL("effects/fleck_tile1")
CLIENTEFFECT_MATERIAL("effects/fleck_tile2")
CLIENTEFFECT_REGISTER_END()

// The EffectDispatch string table, read backwards: the wire carries an index
// and the registry is keyed by name. An empty string means the table does not
// hold it; the table is NULL until the client has it from the server.
void SourceSharp_EffectDispatchName(int32_t nIndex, char* pOut, int32_t nOutBytes)
{
	if (!pOut || nOutBytes <= 0)
		return;

	pOut[0] = '\0';

	if (nIndex < 0 || !g_StringTableEffectDispatch)
		return;

	const char* pName = g_StringTableEffectDispatch->GetString(nIndex);

	if (!pName)
		return;

	Q_strncpy(pOut, pName, nOutBytes);
}

CON_COMMAND_F(cs_cl_te_backend, "TEMPORARY (C-Phase 2): fire one temp entity backend in front of the camera.",
			  FCVAR_CLIENTDLL)
{
	if (args.ArgC() < 2)
	{
		Msg("cs_cl_te_backend <sparks|metalsparks|ricochet|muzzleflash|smoke|splash|"
			"decal|dispatch|"
			"bloodstream|showline|dust>\n");
		return;
	}

	const char* pWhich = args.Arg(1);

	const Vector vecForward = MainViewForward();
	const Vector vecAt = MainViewOrigin() + vecForward * 96.0f;

	const float pAt[3] = {vecAt.x, vecAt.y, vecAt.z};
	const float pDir[3] = {-vecForward.x, -vecForward.y, -vecForward.z};

	// A second point off to one side, so a beam has two ends rather than none.
	const Vector vecSide = vecAt + Vector(0.0f, 0.0f, 64.0f);
	const float pSide[3] = {vecSide.x, vecSide.y, vecSide.z};

	const float pAngles[3] = {0.0f, 0.0f, 0.0f};

	Msg("[SourceSharp] cs_cl_te_backend %s at (%.0f %.0f %.0f)\n", pWhich, pAt[0], pAt[1], pAt[2]);

	if (!Q_stricmp(pWhich, "sparks"))
	{
		SourceSharp_ClientSparks(pAt, 4, 4, pDir);
	}
	else if (!Q_stricmp(pWhich, "metalsparks"))
	{
		SourceSharp_ClientMetalSparks(pAt, pDir);
	}
	else if (!Q_stricmp(pWhich, "ricochet"))
	{
		SourceSharp_ClientRicochet(pAt, pDir);
	}
	else if (!Q_stricmp(pWhich, "muzzleflash"))
	{
		SourceSharp_ClientMuzzleFlash(pAt, pAngles, 1.0f, 0);
	}
	else if (!Q_stricmp(pWhich, "smoke"))
	{
		SourceSharp_ClientSmoke(pAt, 0, 10.0f, 4.0f);
	}
	else if (!Q_stricmp(pWhich, "splash"))
	{
		SourceSharp_ClientEnergySplash(pAt, pDir, 1);
	}
	else if (!Q_stricmp(pWhich, "decal"))
	{
		// The name lookup answers an index; -1 means the engine has no decal by
		// that name.
		const char* pName = (args.ArgC() > 2) ? args.Arg(2) : "Impact.Concrete";
		const int32_t nIndex = SourceSharp_ClientDecalIndexFromName(pName);

		Msg("[SourceSharp]   decal '%s' -> index %d\n", pName, nIndex);

		// Entity 0 is the world, which is what a world decal asks for.
		SourceSharp_ClientDecalShoot(nIndex, 0, pAt, NULL, 0);
	}
	else if (!Q_stricmp(pWhich, "dispatch"))
	{
		const char* pName = (args.ArgC() > 2) ? args.Arg(2) : "Impact";

		// Through the dispatch every native caller takes. The default
		// CEffectData is stock's: scale 1, no entity.
		CEffectData effect;
		effect.m_vOrigin.Init(pAt[0], pAt[1], pAt[2]);
		effect.m_vNormal.Init(pDir[0], pDir[1], pDir[2]);
		DispatchEffectToCallback(pName, effect);
	}
	else
	{
		Msg("[SourceSharp] cs_cl_te_backend: no backend called '%s'\n", pWhich);
	}
}
