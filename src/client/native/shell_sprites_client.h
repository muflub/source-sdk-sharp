//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The managed sprites and sprite trails as one object, a member of the client DLL object:
// the pixel-visibility query each sprite owner keeps, the trails' renderables, the plan a
// draw managed starts holds between its two calls, and the plans stored for a batch.
// shell_sprite_render.cpp holds the owners' half and shell_sprite_leaf.cpp the plan's. Its
// IShimEffectsRenderClient and IShimFxClient slots are the sprites'.

#pragma once

#include <stdint.h>

#include "tier1/utlmap.h"
#include "utlvector.h"

#include "bound_callback.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class CViewCameras;
class CEngineSprite;
class CShellRenderable;
class CSourceSharpSpriteTrail;
class CStandaloneClients;
class IClientEntity;
class QAngle;
class Vector;
struct model_t;
typedef int pixelvis_handle_t;

class CSpritesClient
{
public:
	CSpritesClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
				   const CViewCameras& cameras, CStandaloneClients& standalone);

	// Any trail still live forgets its gone callback first: the callback is this object's
	// thunk, whose page goes with it.
	~CSpritesClient();

	CSpritesClient(const CSpritesClient&) = delete;
	CSpritesClient& operator=(const CSpritesClient&) = delete;

	CClientSystem& SystemRow() { return m_SystemRow; }

	// Plans and draws one sprite as C_SpriteRenderer::DrawSprite did.
	int PlanSprite(pixelvis_handle_t* pQuery, IClientEntity* entity, const model_t* model, const Vector& origin,
				   const QAngle& angles, float frame, IClientEntity* attachedto, int attachmentindex, int rendermode,
				   int renderfx, int alpha, int r, int g, int b, float scale, bool bWorldSpaceScale,
				   float flHDRColorScale, float flGlowProxySize);

	// Carries out the plan stored for pEntity by FxSpriteDrawStore and answers what DrawModel
	// returns; -1 when the plan was made for another blend (it is dropped), -2 when nothing is
	// stored. Clear drops every stored plan.
	int ExecuteStored(IClientEntity* pEntity, float flBlendNow);
	void StoredClear();

	// IShimEffectsRenderClient.
	int32_t DrawSprite(uint32_t hOwner, int32_t nModelIndex, float flFrame, int32_t nRenderMode, int32_t nRenderFx,
					   int32_t r, int32_t g, int32_t b, int32_t a, float flScale, int32_t bWorldSpaceScale,
					   float flHDRColorScale, float flGlowProxySize);
	int32_t DrawSpriteOffset(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
							 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a,
							 float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize);
	void SpriteTrailUpdate(uint32_t hOwner, int32_t nRenderMode, int32_t bVisible);
	void SpriteTrailDestroy(uint32_t hOwner);

	// IShimFxClient.
	int32_t FxSpriteDrawBegin(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
							  int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a,
							  float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize,
							  float* pIn);
	int32_t FxSpriteDrawEnd(const float* pOut);
	int32_t FxSpriteDrawStore(const float* pOut);

private:
	// Keeps each trail at its owner and ticks it every frame, so a trail lays its point where
	// its entity is interpolated. The query handles go on a level change.
	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CSpritesClient& sprites)
			: CClientSystemPerFrame("CSourceSharpSpriteSystem")
			, m_Sprites(sprites)
		{
		}

		void PreRender() override;
		void LevelShutdownPostEntity() override;

	private:
		CSpritesClient& m_Sprites;
	};

	// The query handle one owner keeps: it must be the same handle every frame for the
	// occlusion test to settle. Heap rows, as a plan holds a row's handle by address.
	struct Sprite_t
	{
		uint32_t hOwner;
		pixelvis_handle_t hQuery;
	};

	// hOwner and pEffect are the members shell_effect_registry.h requires of a row.
	struct Trail_t
	{
		uint32_t hOwner;
		CSourceSharpSpriteTrail* pTrail;
	};

	// What the draw needs that the plan does not carry.
	struct Pending_t
	{
		CEngineSprite* pSprite;
		const model_t* pModel;
		IClientEntity* pEntity;
		float flFrame;
		int nRenderMode;
	};

	// A plan kept against its entity with its blend, carried out when the entity's DrawModel
	// comes round. kPlanFloats is the plan's size, asserted beside its enum.
	static const int kPlanFloats = 23;

	struct Stored_t
	{
		Pending_t pending;
		float out[kPlanFloats];
		float flBlend;
	};

	pixelvis_handle_t* QueryFor(uint32_t hOwner);
	int32_t DrawCore(uint32_t hOwner, const Vector& vecOffset, int32_t nModelIndex, float flFrame, int32_t nRenderMode,
					 int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale,
					 int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float* pBeginIn);
	int DrawBegin(pixelvis_handle_t* pQuery, IClientEntity* entity, const model_t* model, const Vector& origin,
				  const QAngle& angles, float frame, int rendermode, int renderfx, int alpha, int r, int g, int b,
				  float scale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float* pIn);
	bool Gather(IClientEntity* entity, const model_t* model, const Vector& origin, const QAngle& angles, float frame,
				IClientEntity* attachedto, int attachmentindex, int rendermode, int renderfx, int alpha, int r, int g,
				int b, float scale, bool bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float* in,
				Pending_t& pending);
	int Execute(const Pending_t& pending, const float* out);
	void StorePlanQuery(const float* pOut);
	void ForgetTrail(CShellRenderable* pGone);
	void FollowTrails();
	void LevelShutdown();

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CStandaloneClients& m_Standalone;

	CUtlVector<Sprite_t*> m_Sprites;
	CUtlVector<Trail_t> m_Trails;

	// Each its own warn-once: the two modes come from different managed enums.
	bool m_bWarnedDrawSprite = false;
	bool m_bWarnedTrail = false;

	// Where the direct path's plan stores the query handle back. Sprites draw one at a time on
	// the render thread, so one slot is enough; NULL outside a plan.
	pixelvis_handle_t* m_pPlanQuery = nullptr;

	// The blend the last request was gathered under.
	float m_flDirectBlend = 0.0f;

	unsigned int m_nHDRColorScaleCache = 0;

	// A draw managed starts plans between FxSpriteDrawBegin and FxSpriteDrawEnd.
	Pending_t m_DirectPending = {};
	bool m_bDirectPending = false;

	CUtlMap<IClientEntity*, Stored_t> m_Stored;

	// A trail released by any route is forgotten through its gone callback, which takes no
	// context: a thunk bound to this object (bound_callback.h).
	typedef CBoundCallback<void(CShellRenderable*)> ForgetHook_t;
	ForgetHook_t m_ForgetHook{this, &ForgetHook_t::Trampoline<CSpritesClient, &CSpritesClient::ForgetTrail>};

	CSystemRow m_SystemRow{*this};
};
