//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The particle manifest half of particle_parse.cpp, plus the two world-point
// DispatchParticleEffect overloads; nothing here names an entity type.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "ss_string.h"
#ifdef CLIENT_DLL
#include "particles/particles.h"
#include "imanaged_areas_client.h"
#include "ss_bitblock.h"

#include <string>
#include <vector>
#endif

#ifdef GAME_DLL
#include "networkstringtable_gamedll.h"
#include "te_effect_dispatch.h"
#else

#include "c_te_effect_dispatch.h"
#endif

// Client only: the server links no particle system. The manifests are read in C#
// on both sides; the client still loads every definition to simulate and draw it.
#ifdef CLIENT_DLL

// A manifest's file entries come back from C# NUL-separated in one block.
// ManifestFiles answers the count, or -1 for a map with no manifest.
enum
{
	kManifestBlockBytes = 16 * 1024
};

// CParticleMgr::Init asks for the first read before the managed runtime is up;
// it is held and run from SourceSharp_ParticleManifestRuntimeUp, still in Init.
static bool s_bReadPending;
static bool s_bPendingLoadSheets;
static bool s_bPendingPrecache;

void ParseParticleEffects(bool bLoadSheets, bool bPrecache);

static int ManifestFiles(bool bMap, const char* pszMapName, std::vector<std::string>& list)
{
	IManagedParticlesClient* p = ManagedParticlesClient();
	if (!p)
		return 0;

	static uint8_t s_Block[ss::kBitBlockHeaderBytes + kManifestBlockBytes];
	const int nFiles = p->ManifestFiles(bMap ? 1 : 0, pszMapName ? pszMapName : "", s_Block, sizeof(s_Block));
	if (nFiles == -2)
	{
		Warning("[SourceSharp] a particle manifest's file list is over %d bytes; not loaded\n",
				(int)kManifestBlockBytes);
		return 0;
	}
	if (nFiles <= 0)
		return nFiles;

	const char* pszAt = (const char*)s_Block + ss::kBitBlockHeaderBytes;
	const char* pszEnd = pszAt + ss::BitBlockBits(s_Block) / 8;
	for (int i = 0; i < nFiles && pszAt < pszEnd; i++)
	{
		list.emplace_back(pszAt);
		pszAt += strlen(pszAt) + 1;
	}
	return nFiles;
}

// The game manifest's files.
static void GameManifestFiles(std::vector<std::string>& list)
{
	ManifestFiles(false, NULL, list);
}

void SourceSharp_ParticleManifestRuntimeUp()
{
	if (!s_bReadPending || !ManagedParticlesClient())
		return;
	s_bReadPending = false;
	ParseParticleEffects(s_bPendingLoadSheets, s_bPendingPrecache);
}

// Full reads of the manifest's definitions, for the dev API.
static int s_nParticleDefinitionReads;

int32_t SourceSharp_ParticleDefinitionReads()
{
	return s_nParticleDefinitionReads;
}

void ParseParticleEffects(bool bLoadSheets, bool bPrecache)
{
	MEM_ALLOC_CREDIT();

	if (!ManagedParticlesClient())
	{
		s_bReadPending = true;
		s_bPendingLoadSheets = bLoadSheets;
		s_bPendingPrecache = bPrecache;
		return;
	}

	s_nParticleDefinitionReads++;

	g_pParticleSystemMgr->ShouldLoadSheets(bLoadSheets);

	std::vector<std::string> files;
	GameManifestFiles(files);

	int nCount = (int)files.size();
	for (int i = 0; i < nCount; ++i)
	{
		g_pParticleSystemMgr->ReadParticleConfigFile(files[i].c_str(), bPrecache, false);
	}

	g_pParticleSystemMgr->DecommitTempMemory();
}

void ReloadParticleEffects()
{
	MEM_ALLOC_CREDIT();

	std::vector<std::string> files;
	GameManifestFiles(files);

	// CAB 2/17/11 Reload all the particles regardless (Fixes filename change exploits).
	bool bReloadAll = true;

	if (bReloadAll)
	{
		ParseParticleEffects(true, true);
	}

	g_pParticleSystemMgr->DecommitTempMemory();
}

// Loads the per-map manifest.
int ParseParticleEffectsMapFiles(const char* pMapName, bool bLoadSheets)
{
	MEM_ALLOC_CREDIT();

	std::vector<std::string> files;
	if (ManifestFiles(true, pMapName, files) < 0)
		return 0; // no manifest, and no warning: most maps have none

	int nCount = (int)files.size();
	if (!nCount)
	{
		return 0;
	}

	g_pParticleSystemMgr->ShouldLoadSheets(bLoadSheets);

	for (int i = 0; i < nCount; ++i)
	{
		g_pParticleSystemMgr->ReadParticleConfigFile(files[i].c_str(), true, true);
	}

	g_pParticleSystemMgr->DecommitTempMemory();
	return nCount;
}

// The fork's particle_parse.h declares it void.
void ParseParticleEffectsMap(const char* pMapName, bool bLoadSheets)
{
	ParseParticleEffectsMapFiles(pMapName, bLoadSheets);
}

// Re-precaches the always-precache definitions that LevelShutdown uncached,
// without re-reading the DMX files. Returns how many it precached.
int PrecacheDefinedParticleSystems()
{
	int nPrecached = 0;
	const int nCount = g_pParticleSystemMgr->GetParticleSystemCount();
	for (int i = 0; i < nCount; ++i)
	{
		const char* pszName = g_pParticleSystemMgr->GetParticleSystemNameFromIndex(i);
		CParticleSystemDefinition* pDef = pszName ? g_pParticleSystemMgr->FindParticleSystem(pszName) : NULL;
		if (pDef && pDef->ShouldAlwaysPrecache())
		{
			g_pParticleSystemMgr->PrecacheParticleSystem(pszName);
			++nPrecached;
		}
	}
	return nPrecached;
}

#endif // CLIENT_DLL

void DispatchParticleEffect(int iEffectIndex, Vector vecOrigin, Vector vecStart, QAngle vecAngles, IGameEntity* pEntity)
{
	CEffectData data;

	data.m_nHitBox = iEffectIndex;
	data.m_vOrigin = vecOrigin;
	data.m_vStart = vecStart;
	data.m_vAngles = vecAngles;

	// pEntity is ignored: the effect has no owner.
	{
#ifdef CLIENT_DLL
		data.m_hEntity = NULL;
#else
		data.m_nEntIndex = 0;
#endif
	}

	DispatchEffect("ParticleEffect", data);
}

void DispatchParticleEffect(const char* pszParticleName, Vector vecOrigin, QAngle vecAngles, IGameEntity* pEntity)
{
	int iIndex = GetParticleSystemIndex(pszParticleName);
	DispatchParticleEffect(iIndex, vecOrigin, vecOrigin, vecAngles, pEntity);
}
