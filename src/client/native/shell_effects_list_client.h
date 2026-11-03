//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client-side effect list's native half as one object, a member of the client DLL
// object: the IEffectsList the view draws and the DLL flushes (the list and its effect
// kinds are managed), the material table effects cross by id, the smoke fog overlay, and
// the two client systems rows that start and end a level's effects. Its IShimFxClient
// slots are the material ones; the area's other slots are free answers.

#pragma once

class CClientEngineInterfaces;
class CViewCameras;
class CClientEntityList;

#include <stdint.h>

#include "clientsideeffects.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class IMaterial;

class CEffectsListClient final : public IEffectsList
{
public:
	CEffectsListClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					   const CViewCameras& cameras, CClientEntityList& entityList)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
		, m_EntityList(entityList)
	{
	}

	CEffectsListClient(const CEffectsListClient&) = delete;
	CEffectsListClient& operator=(const CEffectsListClient&) = delete;

	// Shim slots: what managed effects ask of the engine.
	int32_t FxLocalPlayer();
	void FxViewAngles(float* pOutPYR);
	void FxEjectBrass(const float* pOriginXYZ, const float* pAnglesPYR, const float* pGunAnglesPYR, int32_t nType);
	int32_t FxPrecacheCount();
	void FxPrecacheCache(int32_t nIndex, int32_t bPrecache);
	void FxCacheUsedMaterials();
	void FxCacheMaterialHandles();

	// Shim slots: the view the effects read.
	void FxView(float* pOut);
	void FxMainViewOrigin(float* pOutXYZ);

	// NULL outside a managed load.
	IManagedFxClient* Managed() const { return m_Areas.m_pFxClient; }

	// IEffectsList: managed holds the effects; the add and remove of a native effect
	// object have nothing to add it to.
	void AddEffect(CClientSideEffect* effect) override {}

	void RemoveEffect(CClientSideEffect* effect) override {}

	void DrawEffects(double frametime) override;
	void Flush() override;

	// The material table: an id per IMaterial* handed out, stable for the process so a
	// managed reload keeps its ids. Never shrinks; materials are reference-counted by the
	// IShimFxClient calls. 0 is no material.
	int32_t MaterialId(IMaterial* pMaterial);
	IMaterial* MaterialOf(int32_t nMaterial) const;

	// Stock's FXCreationAllowed: set while the view's effects may be created.
	void SetCreationAllowed(bool bAllowed) { m_bCreationAllowed = bAllowed; }

	bool CreationAllowed() const { return m_bCreationAllowed; }

	// The smoke fog overlay: its alpha (the smoke volume's write, through the render plan)
	// and colour, the material, and the quad. Whether and how it draws is managed's
	// (IManagedFxClient::SmokeFogPlan).
	void InitSmokeFog();
	void TermSmokeFog();

	void SetSmokeFogAlpha(float flAlpha) { m_flSmokeFogAlpha = flAlpha; }

	void DrawSmokeFog();

	// The client systems rows: the near-miss clock reset at level start, and the effect
	// precache's level start and end.
	CClientSystem& BulletWhizRow() { return m_BulletWhizRow; }

	CClientSystem& PrecacheRow() { return m_PrecacheRow; }

	// IShimFxClient.
	int32_t FxEntityAttachment(int32_t nEntIndex, int32_t nAttachment, float* pOutXYZ, char* pModelOut,
							   int32_t nModelSize);
	int32_t FxEntityRenderAngles(int32_t nEntIndex, float* pOutPYR);
	int32_t FxMaterialFind(const char* pName);
	void FxMaterialAddRef(int32_t nMaterial);
	void FxMaterialRelease(int32_t nMaterial);
	int32_t FxMaterialName(int32_t nMaterial, char* pOut, int32_t nOutSize);
	void FxDrawQuads(int32_t nMaterial, int32_t nVertices, const float* pVertices, int32_t bByteColor);

private:
	class CBulletWhizRow final : public CClientSystem
	{
	public:
		explicit CBulletWhizRow(CEffectsListClient& effects)
			: CClientSystem("CBulletWhizTimer")
			, m_Effects(effects)
		{
		}

		void LevelInitPreEntity() override;

	private:
		CEffectsListClient& m_Effects;
	};

	class CPrecacheRow final : public CClientSystem
	{
	public:
		// Stock's name, its spelling included.
		explicit CPrecacheRow(CEffectsListClient& effects)
			: CClientSystem("CCLientEffectPrecacheSystem")
			, m_Effects(effects)
		{
		}

		void LevelInitPreEntity() override;
		void LevelShutdownPostEntity() override;

	private:
		CEffectsListClient& m_Effects;
	};

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CClientEntityList& m_EntityList;

	static const int kMaxMaterials = 1024;
	IMaterial* m_pMaterials[kMaxMaterials] = {};
	int m_nMaterials = 0;

	bool m_bCreationAllowed = false;

	IMaterial* m_pSmokeFogMaterial = nullptr;
	float m_flSmokeFogAlpha = 0.0f;
	float m_SmokeFogColor[3] = {};

	CBulletWhizRow m_BulletWhizRow{*this};
	CPrecacheRow m_PrecacheRow{*this};
};
