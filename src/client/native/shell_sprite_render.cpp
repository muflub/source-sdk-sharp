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
#include "shell_beam_render.h"
#include "shell_particle_systems.h"

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
class CSourceSharpSpriteRenderer : public C_SpriteRenderer
{
public:
	int Draw(IClientEntity* pEntity, const model_t* pModel, const Vector& vecOrigin, const QAngle& angAngles,
			 float flFrame, int nRenderMode, int nRenderFx, int a, int r, int g, int b, float flScale,
			 bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize)
	{
		return SourceSharp_PlanSprite(&m_queryHandle, pEntity, pModel, vecOrigin, angAngles, flFrame,
									  /*attachedto=*/NULL, /*attachmentindex=*/0, nRenderMode, nRenderFx, a, r, g, b,
									  flScale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize);
	}

	// The direct path's first half: the request for a plan made in managed.
	int Begin(IClientEntity* pEntity, const model_t* pModel, const Vector& vecOrigin, const QAngle& angAngles,
			  float flFrame, int nRenderMode, int nRenderFx, int a, int r, int g, int b, float flScale,
			  bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float* pIn)
	{
		return SourceSharp_SpriteDrawBegin(&m_queryHandle, pEntity, pModel, vecOrigin, angAngles, flFrame, nRenderMode,
										   nRenderFx, a, r, g, b, flScale, bWorldSpaceScale, flHDRColorScale,
										   flGlowProxySize, pIn);
	}
};

// Which renderer belongs to which managed entity, keyed by handle because the shell
// entity is shared by every managed class.
struct SourceSharpSprite_t
{
	uint32_t hOwner;
	CSourceSharpSpriteRenderer renderer;
};

static CUtlVector<SourceSharpSprite_t*> g_ManagedSprites;

static CSourceSharpSpriteRenderer* RendererFor(uint32_t hOwner)
{
	for (int i = 0; i < g_ManagedSprites.Count(); i++)
	{
		if (g_ManagedSprites[i]->hOwner == hOwner)
			return &g_ManagedSprites[i]->renderer;
	}

	SourceSharpSprite_t* pEntry = new SourceSharpSprite_t;
	pEntry->hOwner = hOwner;

	g_ManagedSprites.AddToTail(pEntry);

	return &pEntry->renderer;
}

// One sprite this frame: stock CSprite::DrawModel with its members as arguments. A model
// that is not a sprite is declined quietly. The attachment is always NULL and 0: only C++
// callers outside this build set one.
static int32_t SourceSharpDrawSpriteCore(uint32_t hOwner, const Vector& vecOffset, int32_t nModelIndex, float flFrame,
										 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
										 int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
										 float flGlowProxySize, float* pBeginIn = NULL)
{
	if (nModelIndex <= 0)
		return 0;

	C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(hOwner);

	if (!pOwner)
		return 0;

	const model_t* pModel = modelinfo->GetModel(nModelIndex);

	if (!pModel || modelinfo->GetModelType(pModel) != mod_sprite)
		return 0;

	static bool s_bWarnedDrawSprite = false;
	if (SpriteRenderModeRefused(nRenderMode, "DrawSprite", s_bWarnedDrawSprite))
		return 0;

	// The entity's origin is the interpolated one for this frame. The offset is added in world
	// axes, as stock's C_Plasma does; it is zero for every caller but the plasma flame.
	const Vector vecOrigin = pOwner->GetAbsOrigin() + vecOffset;
	const QAngle angAngles = pOwner->GetAbsAngles();

	// The world-space scale division is the planner's.
	CSourceSharpSpriteRenderer* pRenderer = RendererFor(hOwner);

	if (pBeginIn)
		return pRenderer->Begin(pOwner, pModel, vecOrigin, angAngles, flFrame, nRenderMode, nRenderFx, a, r, g, b,
								flScale, bWorldSpaceScale != 0, flHDRColorScale, flGlowProxySize, pBeginIn);

	return pRenderer->Draw(pOwner, pModel, vecOrigin, angAngles, flFrame, nRenderMode, nRenderFx, a, r, g, b, flScale,
						   bWorldSpaceScale != 0, flHDRColorScale, flGlowProxySize);
}

// One sprite at its owner's origin.
int32_t SourceSharp_DrawSprite(uint32_t hOwner, int32_t nModelIndex, float flFrame, int32_t nRenderMode,
							   int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale,
							   int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize)
{
	return SourceSharpDrawSpriteCore(hOwner, vec3_origin, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a,
									 flScale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize);
}

// One sprite offset from its owner: how one plasma entity draws its seven flame sprites.
int32_t SourceSharp_DrawSpriteOffset(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
									 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a,
									 float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
									 float flGlowProxySize)
{
	const Vector vecOffset = pOffsetXYZ ? Vector(pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2]) : vec3_origin;

	return SourceSharpDrawSpriteCore(hOwner, vecOffset, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a,
									 flScale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize);
}

// Fills the request for the sprite at its owner's place plus the offset; FxSpriteDrawEnd
// (shell_sprite_leaf.cpp) draws the plan. Called from inside the owner's draw.
int32_t SourceSharp_FxSpriteDrawBegin(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
									  int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
									  int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
									  float flGlowProxySize, float* pIn)
{
	if (!pIn)
		return 0;

	const Vector vecOffset = pOffsetXYZ ? Vector(pOffsetXYZ[0], pOffsetXYZ[1], pOffsetXYZ[2]) : vec3_origin;

	return SourceSharpDrawSpriteCore(hOwner, vecOffset, nModelIndex, flFrame, nRenderMode, nRenderFx, r, g, b, a,
									 flScale, bWorldSpaceScale, flHDRColorScale, flGlowProxySize, pIn);
}

// A managed env_spritetrail's ribbon as a renderable. The ring, ribbon and bounds are
// managed's; the PreRender sweep ticks it and the leaf system draws it.
class CSourceSharpSpriteTrail : public CShellRenderable
{
public:
	DECLARE_CLASS_GAMEROOT(CSourceSharpSpriteTrail, CShellRenderable);

	// The block arrives zeroed, so only what a zero is wrong for is set.
	CSourceSharpSpriteTrail() { m_bVisible = true; }

	// CDefaultClientRenderable leaves ShouldDraw and IsTransparent pure.
	virtual bool ShouldDraw() OVERRIDE { return m_bVisible; }

	virtual bool IsTransparent() OVERRIDE { return true; }

	virtual int DrawModel(int flags) OVERRIDE
	{
		IManagedFxClient* pFx = ManagedFxClient();
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
		IManagedFxClient* pFx = ManagedFxClient();
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

	uint32_t m_hOwner;
	Vector m_vecRenderMins;
	Vector m_vecRenderMaxs;
	bool m_bVisible;
};

struct SourceSharpSpriteTrail_t
{
	uint32_t hOwner;
	CSourceSharpSpriteTrail* pTrail;
};

// Not ShellEffects_Find's rows: RendererFor's list holds pointers because renderers are
// handed out by address. This list is rows.
static CUtlVector<SourceSharpSpriteTrail_t> g_ManagedTrails;

// Called from the renderable's destructor: a raw pointer cannot tell it is stale.
static void ManagedSpriteTrail_Forget(CShellRenderable* pGone)
{
	for (int i = g_ManagedTrails.Count() - 1; i >= 0; i--)
	{
		if (g_ManagedTrails[i].pTrail == pGone)
		{
			g_ManagedTrails.Remove(i);
			return;
		}
	}
}

void SourceSharp_SpriteTrailUpdate(uint32_t hOwner, int32_t nRenderMode, int32_t bVisible)
{
	C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(hOwner);

	if (!pOwner)
		return;

	// Its own warn-once flag: the mode comes from a different managed enum than DrawSprite's.
	static bool s_bWarnedTrail = false;
	if (SpriteRenderModeRefused(nRenderMode, "SpriteTrailUpdate", s_bWarnedTrail))
		return;

	int iEntry = ShellEffects_Find(g_ManagedTrails, hOwner);

	if (iEntry < 0)
	{
		CSourceSharpSpriteTrail* pNew = new CSourceSharpSpriteTrail;

		if (!pNew)
			return;

		pNew->SetAbsOrigin(pOwner->GetAbsOrigin());
		pNew->SetAbsAngles(pOwner->GetAbsAngles());

		// Into the leaf system, so DrawModel gets called.
		pNew->AddToLeafSystem(RENDER_GROUP_TRANSLUCENT_ENTITY);
		pNew->SetGoneCallback(ManagedSpriteTrail_Forget);

		SourceSharpSpriteTrail_t entry;
		entry.hOwner = hOwner;
		entry.pTrail = pNew;

		iEntry = g_ManagedTrails.AddToTail(entry);
	}

	g_ManagedTrails[iEntry].pTrail->SourceSharpUpdate(hOwner, bVisible != 0);
}

void SourceSharp_SpriteTrailDestroy(uint32_t hOwner)
{
	const int iEntry = ShellEffects_Find(g_ManagedTrails, hOwner);

	if (iEntry < 0)
		return;

	g_ManagedTrails[iEntry].pTrail->Release();
}

// Every trail ticked and moved to its entity's place. A trail is a separate renderable,
// and a move alone raises no data change, so its place is read every frame. Backwards,
// since Release() shortens the list.
static void ManagedSpriteTrails_Follow()
{
	for (int i = g_ManagedTrails.Count() - 1; i >= 0; i--)
	{
		CSourceSharpSpriteTrail* pTrail = g_ManagedTrails[i].pTrail;

		C_StandaloneEntity* pOwner = SourceSharp_StandaloneFromHandle(g_ManagedTrails[i].hOwner);

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

// PreRender, so a trail lays its point where its entity is interpolated this frame. The
// renderer table is dropped on a level change; a renderer otherwise lives on.
#include "shell_clientsystem.h"

class CSourceSharpSpriteSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpSpriteSystem()
		: CClientSystemPerFrame("CSourceSharpSpriteSystem")
	{
	}

	virtual void PreRender() OVERRIDE
	{
		ManagedSpriteTrails_Follow();
		ShellParticles_PreRender();
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		g_ManagedSprites.PurgeAndDeleteElements();

		// Trails are ours to delete; Release() removes the row through ManagedSpriteTrail_Forget.
		while (g_ManagedTrails.Count() > 0)
		{
			const int nBefore = g_ManagedTrails.Count();

			g_ManagedTrails[0].pTrail->Release();

			// Guards against an infinite loop if the gone callback is ever dropped.
			if (g_ManagedTrails.Count() >= nBefore)
			{
				AssertMsg(false, "CShellRenderable::Release did not remove its row");
				g_ManagedTrails.RemoveAll();
				break;
			}
		}

		ShellParticles_LevelShutdown();
	}
};

static CSourceSharpSpriteSystem g_SpriteSystem;

CClientSystem* ClientSystem_Sprite()
{
	return &g_SpriteSystem;
}
