//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The native side of the managed client shadow manager: what the engine holds pointers to
// and the queue the entity shells write.
#pragma once

#include <stdint.h>

#include "ivrenderview.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "utlvector.h"

#include "bound_callback.h"
#include "fwd_shadows.h"
#include "shadow_pending_queue.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CViewSetup;
class CBaseHandle;

// Shell updates are queued, three int32 an entry, and handed over with the manager's next
// call, so an entity that moves every frame costs no crossing of its own.
enum ShellShadowPending_t
{
	SHELL_SHADOW_PENDING_DIRTY = 1, // entity, 1 when its render-to-texture texture is dirty too
	SHELL_SHADOW_PENDING_REFRESH = 2, // entity, shadow type | shadow flags << 8 (the block's, as managed wrote them)
	SHELL_SHADOW_PENDING_DESTROY = 3, // entity
	SHELL_SHADOW_PENDING_WORDS = 3,
	SHELL_SHADOW_PENDING_MAX = 256,
};

// The shadow manager's client half as one object, a member of the client DLL object: the
// references the managed manager holds by id, the flashlight textures, the shell updates'
// queue, the forwarder library's environment and the client systems row.
class CShadowsClient
{
public:
	explicit CShadowsClient(const CManagedAreasClient& areas)
		: m_Areas(areas)
	{
	}

	CShadowsClient(const CShadowsClient&) = delete;
	CShadowsClient& operator=(const CShadowsClient&) = delete;

	// NULL outside a managed load.
	IManagedShadowsClient* Managed() const { return m_Areas.m_pShadowsClient; }

	CClientSystem& SystemRow() { return m_SystemRow; }

	// The environment the shadow forwarder table calls out over.
	void* AcquireEnv();

	// C_BaseEntity::InvalidatePhysicsRecursive's AddToDirtyShadowList( this ) and, when the
	// pose moved, MarkRenderToTextureShadowDirty( GetShadowHandle() ). Collapsed per entity:
	// a second mark ORs into the first.
	void MarkDirty(const CBaseHandle& hEntity, bool bTextureDirty);

	// The entity's shadow as its block now says (C_BaseEntity::CreateShadow).
	void Refresh(const CBaseHandle& hEntity, int nShadowType, int nShadowFlags);

	// The entity's shadow gone (C_BaseEntity::DestroyShadow). Drops the entity's queued
	// entries before it: the renderable may be freed next.
	void Destroy(const CBaseHandle& hEntity);

	// The render plan's shadow ops, each one call of the managed manager.
	void AdvanceFrame();
	void PreRender();
	void ComputeShadowTextures(int nLeafCount, const LeafIndex_t* pLeafList);
	void ComputeShadowDepthTextures(const CViewSetup& view);
	void UnlockAllShadowDepthTextures();
	void RenderShadowTexture(int w, int h);

	// IShimFlashlightClient: the texture table is the environment's, so a FlashlightState_t
	// the manager hands the engine names the same reference.
	int32_t FlashlightTexture(const char* pszName);

private:
	// The references, by id (0 is none). A freed slot is reused; a live slot's id is never
	// handed out.
	template <typename T>
	class CReferenceTable
	{
	public:
		int32_t Add(T* p)
		{
			for (int i = 0; i < m_Slots.Count(); ++i)
			{
				if (!m_Slots[i])
				{
					m_Slots[i] = p;
					return i + 1;
				}
			}
			m_Slots.AddToTail(p);
			return m_Slots.Count();
		}

		T* Find(int32_t id) const { return (id >= 1 && id <= m_Slots.Count()) ? m_Slots[id - 1] : NULL; }

		T* Remove(int32_t id)
		{
			T* p = Find(id);
			if (p)
				m_Slots[id - 1] = NULL;
			return p;
		}

	private:
		CUtlVector<T*> m_Slots;
	};

	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CShadowsClient& shadows)
			: CClientSystemPerFrame("CClientShadowMgr")
			, m_Shadows(shadows)
		{
		}

		char const* Name() override;
		bool Init() override;
		void Shutdown() override;
		void LevelInitPreEntity() override;
		void LevelShutdownPostEntity() override;
		void PreRender() override;

	private:
		CShadowsClient& m_Shadows;
	};

	static void HandOverToManaged(const int32_t* pEntries, int32_t nCount, void* pContext);

	void BuildEnv();
	IManagedShadowsClient* Target();
	IManagedShadowsClient* Carrier();

	// The environment's callbacks, and the material system's restore function, take no
	// context: thunks bound to this object (bound_callback.h).
	int32_t InitMaterial(const char* pszName, const char* pszGroup);
	void ShutdownMaterial(int32_t id);
	IMaterial* Material(int32_t id);
	int32_t InitRenderTarget(const SSShadowRenderTarget* pTarget);
	void ShutdownTexture(int32_t id);
	ITexture* Texture(int32_t id);
	ITexture* FlashlightTextureAt(int32_t nIndex);
	void RestoreRenderState(int);
	void AddRestoreFunc();
	void RemoveRestoreFunc();

#define SS_SHADOW_THUNK(name, sig, method) \
	typedef CBoundCallback<sig> name##_t;  \
	name##_t name { this, &name##_t::Trampoline<CShadowsClient, &CShadowsClient::method> }

	SS_SHADOW_THUNK(m_InitMaterial, int32_t(const char*, const char*), InitMaterial);
	SS_SHADOW_THUNK(m_ShutdownMaterial, void(int32_t), ShutdownMaterial);
	SS_SHADOW_THUNK(m_Material, IMaterial*(int32_t), Material);
	SS_SHADOW_THUNK(m_InitRenderTarget, int32_t(const SSShadowRenderTarget*), InitRenderTarget);
	SS_SHADOW_THUNK(m_ShutdownTexture, void(int32_t), ShutdownTexture);
	SS_SHADOW_THUNK(m_Texture, ITexture*(int32_t), Texture);
	SS_SHADOW_THUNK(m_FlashlightTexture, ITexture*(int32_t), FlashlightTextureAt);
	SS_SHADOW_THUNK(m_RestoreFunc, void(int), RestoreRenderState);
	SS_SHADOW_THUNK(m_AddRestoreFunc, void(), AddRestoreFunc);
	SS_SHADOW_THUNK(m_RemoveRestoreFunc, void(), RemoveRestoreFunc);
#undef SS_SHADOW_THUNK

	const CManagedAreasClient& m_Areas;

	CReferenceTable<CMaterialReference> m_Materials;
	CReferenceTable<CTextureReference> m_Textures;

	// Held for the DLL's life so the pointer the engine keeps in a FlashlightState_t stays
	// referenced.
	static const int kMaxFlashlightTextures = 4;
	CTextureReference* m_FlashlightTextures[kMaxFlashlightTextures] = {};
	char m_szFlashlightTextureNames[kMaxFlashlightTextures][MAX_PATH] = {};
	int m_nFlashlightTextures = 0;

	// The queue rides in PreRender's arguments; any other call hands it over first, so its
	// entries reach the manager in order before the call's own work.
	CShadowPendingQueue m_Queue;

	SSShadowEnv m_Env = {};

	CSystemRow m_SystemRow{*this};
};
