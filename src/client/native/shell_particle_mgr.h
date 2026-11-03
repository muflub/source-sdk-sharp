//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// CParticleMgr's remaining mechanics as free functions: the PMaterial table, the
// old-style draw state, and the particle library's setup.

#pragma once

#include "particlemgr.h"
#include "materialsystem/imaterialvar.h"
#include "cdll_int.h"
#include "particledraw.h"
#include "view.h"
#include "voice_status.h"

class CClientEngineInterfaces;
class IMaterialSystem;
class IParticleSystemQuery;

class CParticleMgrClient final
{
public:
	explicit CParticleMgrClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CParticleMgrClient(const CParticleMgrClient&) = delete;
	CParticleMgrClient& operator=(const CParticleMgrClient&) = delete;

	// The library's manager with the client's particle query and built-in operators,
	// and the particle manifests read.
	bool Init(IMaterialSystem* pMaterials, IParticleSystemQuery* pQuery);

	// Releases the table's materials, uncaches the library's systems and the
	// material system's unused materials.
	void Term();

	void LevelInit();

	// Releases every held material; handles stay valid and re-acquire on next use.
	void LevelShutdown();

	// The handle for a material by name, or NULL when the material system has none.
	PMaterialHandle FindMaterial(const char* pszName);

	// The handle's material, re-acquired if a level shutdown released it.
	IMaterial* Material(PMaterialHandle hMaterial);

	// The draw state: the model-view matrix of the draw in progress, the light the
	// particle light proxy reads, and the argument particle binds carry.
	VMatrix& ModelView() { return m_ModelView; }

	void GetLight(CParticleLightInfo& info) const { info = m_Light; }

	void SetLight(const CParticleLightInfo& info) { m_Light = info; }

	void* BindArg() { return this; }

private:
	CUtlVector<CParticleSubTextureGroup*>& Groups();
	CUtlDict<CParticleSubTexture*, unsigned short>& SubTextures();
	CParticleSubTextureGroup* FindOrAddSubTextureGroup(IMaterial* pPageMaterial);
	void RepairPMaterial(PMaterialHandle hMaterial);
	void ReleaseMaterials(bool bDelete);

	const CClientEngineInterfaces& m_Engines;

	// CParticleMgr's members that outlived it: the material system Init hands over, the table,
	// and the draw state.
	IMaterialSystem* m_pMaterialSystem = nullptr;
	CUtlVector<CParticleSubTextureGroup*>* m_pGroups = nullptr; // lookup by page material
	CUtlDict<CParticleSubTexture*, unsigned short>* m_pSubTextures = nullptr; // lookup by material name
	CParticleLightInfo m_Light = {};
	VMatrix m_ModelView;
};
