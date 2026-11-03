//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// Draws the managed sprites (env_sprite, env_sprite_oriented, env_glow) and sprite trails.
// A managed entity never arrives as DT_Sprite, so no C_Sprite exists; a sprite is drawn
// from inside its owner's DrawModel, which supplies the interpolated origin. The only
// per-sprite state kept is the pixel-visibility query handle, which must be the same
// handle every frame for the occlusion test to settle.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"
#include "Sprite.h"

#include "model_types.h"

#include "shell_standalone_client.h"
#include "shell_renderable.h"
#include "shell_sprites_client.h"
#include "shell_standalone_clients.h"

#include "shell_effect_registry.h"
#include "imanaged_areas_client.h"

// CEngineSprite::GetMaterial subscripts an array of kRenderModeCount with no check, so a
// managed render mode out of range is refused rather than clamped. Each caller has its
// own warn-once flag.
static bool SpriteRenderModeRefused(int32_t nRenderMode, const char* pszCaller, bool& bWarned)
{
	if (nRenderMode >= 0 && nRenderMode < kRenderModeCount)
		return false;

	if (!bWarned)
	{
		bWarned = true;
		Warning("[SourceSharp] %s: render mode %d is outside RenderMode_t "
				"(0..%d). Nothing drawn. Check the managed RenderMode enum against "
				"public/const.h.\n",
				pszCaller, nRenderMode, kRenderModeCount - 1);
	}

	return true;
}

// One managed sprite's renderer, kept for its pixel-visibility query handle.
CSpritesClient::CSpritesClient(const CManagedAreasClient& areas, CStandaloneClients& standalone)
	: m_Areas(areas)
	, m_Standalone(standalone)
	, m_Stored(DefLessFunc(IClientEntity*))
{
}

// The query handle an owner keeps, keyed by handle because the shell entity is shared by
// every managed class.
pixelvis_handle_t* CSpritesClient::QueryFor(uint32_t hOwner)
{
	for (int i = 0; i < m_Sprites.Count(); i++)
	{
		if (m_Sprites[i]->hOwner == hOwner)
			return &m_Sprites[i]->hQuery;
	}

	Sprite_t* pEntry = new Sprite_t;
	pEntry->hOwner = hOwner;
	pEntry->hQuery = 0;

	m_Sprites.AddToTail(pEntry);

	return &pEntry->hQuery;
}

// One sprite this frame: stock CSprite::DrawModel with its members as arguments. A model
// that is not a sprite is declined quietly. The attachment is always NULL and 0: only C++
// callers outside this build set one.
int32_t CSpritesClient::DrawCore(uint32_t hOwner, const Vector& vecOffset, int32_t nModelIndex, float flFrame,
								 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a,
								 float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize,
								 float* pBeginIn)
{
	if (nModelIndex <= 0)
		return 0;

	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);

	if (!pOwner)
		return 0;

	const model_t* pModel = modelinfo->GetModel(nModelIndex);

	if (!pModel || modelinfo->GetModelType(pModel) != mod_sprite)
		return 0;

	if (SpriteRenderModeRefused(nRenderMode, "DrawSprite", m_bWarnedDrawSprite))
		return 0;

	// The entity's origin is the interpolated one for this frame. The offset is added in world
	// axes, as stock's C_Plasma does; it is zero for every caller but the plasma flame.
	const Vector vecOrigin = pOwner->GetAbsOrigin() + vecOffset;
	const QAngle angAngles = pOwner->GetAbsAngles();

	// The world-space scale division is the planner's.
	pixelvis_handle_t* pQuery = QueryFor(hOwner);

	if (pBeginIn)
		return DrawBegin(pQuery, pOwner, pModel, vecOrigin, angAngles, flFrame, nRenderMode, nRenderFx, a, r, g, b,
						 flScale, bWorldSpaceScale != 0, flHDRColorScale, flGlowProxySize, pBeginIn);

	return PlanSprite(pQuery, pOwner, pModel, vecOrigin, angAngles, flFrame, /*attachedto=*/NULL,
					  /*attachmentindex=*/0, nRenderMode, nRenderFx, a, r, g, b, flScale, bWorldSpaceScale != 0,
					  flHDRColorScale, flGlowProxySize);
}

// One sprite at its owner's origin.
int32_t CSpritesClient::DrawSprite(uint32_t hOwner, int32_t nModelIndex, float flFrame, int32_t nRenderMode,
								   int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale,
								   int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize)
{
	return DrawCore(hOwner, vec3_origin, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a, flScale,
					bWorldSpaceScale, flHDRColorScale, flGlowProxySize, NULL);
}

// One sprite offset from its owner: how one plasma entity draws its seven flame sprites.
int32_t CSpritesClient::DrawSpriteOffset(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
										 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
										 int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
										 float flGlowProxySize)
{
	const Vector vecOffset = pOffsetXYZ ? Vector(pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2]) : vec3_origin;

	return DrawCore(hOwner, vecOffset, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a, flScale,
					bWorldSpaceScale, flHDRColorScale, flGlowProxySize, NULL);
}

// Fills the request for the sprite at its owner's place plus the offset; FxSpriteDrawEnd
// (shell_sprite_leaf.cpp) draws the plan. Called from inside the owner's draw.
int32_t CSpritesClient::FxSpriteDrawBegin(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
										  int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
										  int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
										  float flGlowProxySize, float* pIn)
{
	if (!pIn)
		return 0;

	const Vector vecOffset = pOffsetXYZ ? Vector(pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2]) : vec3_origin;

	return DrawCore(hOwner, vecOffset, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a, flScale,
					bWorldSpaceScale, flHDRColorScale, flGlowProxySize, pIn);
}

// A managed env_spritetrail's ribbon as a renderable. The ring, ribbon and bounds are
// managed's; the PreRender sweep ticks it and the leaf system draws it.
class CSourceSharpSpriteTrail : public CShellRenderable
{
public:
	DECLARE_CLASS_GAMEROOT(CSourceSharpSpriteTrail, CShellRenderable);

	// The block arrives zeroed, so only what a zero is wrong for is set.
	explicit CSourceSharpSpriteTrail(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
		m_bVisible = true;
	}

	// CDefaultClientRenderable leaves ShouldDraw and IsTransparent pure.
	virtual bool ShouldDraw() OVERRIDE { return m_bVisible; }

	virtual bool IsTransparent() OVERRIDE { return true; }

	virtual int DrawModel(int flags) OVERRIDE
	{
		IManagedFxClient* pFx = m_Areas.m_pFxClient;
		if (!pFx)
			return 0;

		float origin[3];
		PutOrigin(origin);
		return pFx->SpriteTrailDraw(m_hOwner, gpGlobals->curtime, origin) > 0 ? 1 : 0;
	}

	virtual void GetRenderBounds(Vector& mins, Vector& maxs) OVERRIDE
	{
		mins = m_vecRenderMins;
		maxs = m_vecRenderMaxs;
	}

	// The managed ring lays a point and re-measures.
	void Tick()
	{
		IManagedFxClient* pFx = m_Areas.m_pFxClient;
		if (!pFx)
			return;

		float origin[3], box[6];
		PutOrigin(origin);
		if (pFx->SpriteTrailTick(m_hOwner, gpGlobals->curtime, origin, box))
		{
			m_vecRenderMins.Init(box[0], box[1], box[2]);
			m_vecRenderMaxs.Init(box[3], box[4], box[5]);
		}
	}

	void SourceSharpUpdate(uint32_t hOwner, bool bVisible)
	{
		m_hOwner = hOwner;
		m_bVisible = bVisible;
	}

private:
	void PutOrigin(float* p)
	{
		const Vector& o = GetAbsOrigin();
		p[0] = o.x;
		p[1] = o.y;
		p[2] = o.z;
	}

	const CManagedAreasClient& m_Areas;
	uint32_t m_hOwner;
	Vector m_vecRenderMins;
	Vector m_vecRenderMaxs;
	bool m_bVisible;
};

// Called from the renderable's destructor: a raw pointer cannot tell it is stale.
void CSpritesClient::ForgetTrail(CShellRenderable* pGone)
{
	for (int i = m_Trails.Count() - 1; i >= 0; i--)
	{
		if (m_Trails[i].pTrail == pGone)
		{
			m_Trails.Remove(i);
			return;
		}
	}
}

CSpritesClient::~CSpritesClient()
{
	for (int i = 0; i < m_Trails.Count(); i++)
		m_Trails[i].pTrail->SetGoneCallback(NULL);
}

void CSpritesClient::SpriteTrailUpdate(uint32_t hOwner, int32_t nRenderMode, int32_t bVisible)
{
	C_StandaloneEntity* pOwner = m_Standalone.FromHandle(hOwner);

	if (!pOwner)
		return;

	if (SpriteRenderModeRefused(nRenderMode, "SpriteTrailUpdate", m_bWarnedTrail))
		return;

	int iEntry = ShellEffects_Find(m_Trails, hOwner);

	if (iEntry < 0)
	{
		CSourceSharpSpriteTrail* pNew = new CSourceSharpSpriteTrail(m_Areas);

		if (!pNew)
			return;

		pNew->SetAbsOrigin(pOwner->GetAbsOrigin());
		pNew->SetAbsAngles(pOwner->GetAbsAngles());

		// Into the leaf system, so DrawModel gets called.
		pNew->AddToLeafSystem(RENDER_GROUP_TRANSLUCENT_ENTITY);
		pNew->SetGoneCallback(m_ForgetHook.Get());

		Trail_t entry;
		entry.hOwner = hOwner;
		entry.pTrail = pNew;

		iEntry = m_Trails.AddToTail(entry);
	}

	m_Trails[iEntry].pTrail->SourceSharpUpdate(hOwner, bVisible != 0);
}

void CSpritesClient::SpriteTrailDestroy(uint32_t hOwner)
{
	const int iEntry = ShellEffects_Find(m_Trails, hOwner);

	if (iEntry < 0)
		return;

	m_Trails[iEntry].pTrail->Release();
}

// Every trail ticked and moved to its entity's place. A trail is a separate renderable,
// and a move alone raises no data change, so its place is read every frame. Backwards,
// since Release() shortens the list.
void CSpritesClient::FollowTrails()
{
	for (int i = m_Trails.Count() - 1; i >= 0; i--)
	{
		CSourceSharpSpriteTrail* pTrail = m_Trails[i].pTrail;

		C_StandaloneEntity* pOwner = m_Standalone.FromHandle(m_Trails[i].hOwner);

		if (!pOwner)
		{
			pTrail->Release();
			continue;
		}

		pTrail->SetAbsOrigin(pOwner->GetAbsOrigin());
		pTrail->SetAbsAngles(pOwner->GetAbsAngles());
		pTrail->Tick();
	}
}

// The query table is dropped on a level change; a query otherwise lives on.
void CSpritesClient::LevelShutdown()
{
	m_Sprites.PurgeAndDeleteElements();

	// Trails are ours to delete; Release() removes the row through ForgetTrail.
	while (m_Trails.Count() > 0)
	{
		const int nBefore = m_Trails.Count();

		m_Trails[0].pTrail->Release();

		// Guards against an infinite loop if the gone callback is ever dropped.
		if (m_Trails.Count() >= nBefore)
		{
			AssertMsg(false, "CShellRenderable::Release did not remove its row");
			m_Trails.RemoveAll();
			break;
		}
	}
}

void CSpritesClient::CSystemRow::PreRender()
{
	m_Sprites.FollowTrails();
}

void CSpritesClient::CSystemRow::LevelShutdownPostEntity()
{
	m_Sprites.LevelShutdown();
}
