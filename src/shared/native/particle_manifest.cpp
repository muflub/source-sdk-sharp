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
#include "shell_particle_manifest_client.h"

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

int CParticleManifestClient::ManifestFiles(bool bMap, const char* pszMapName, std::vector<std::string>& list)
{
	IManagedParticlesClient* p = m_Areas.m_pParticlesClient;
	if (!p)
		return 0;

	std::vector<uint8_t> block(ss::kBitBlockHeaderBytes + kManifestBlockBytes);
	uint8_t* pBlock = block.data();
	const int nFiles = p->ManifestFiles(bMap ? 1 : 0, pszMapName ? pszMapName : "", pBlock, (int32_t)block.size());
	if (nFiles == -2)
	{
		Warning("[SourceSharp] a particle manifest's file list is over %d bytes; not loaded\n",
				(int)kManifestBlockBytes);
		return 0;
	}
	if (nFiles <= 0)
		return nFiles;

	const char* pszAt = (const char*)pBlock + ss::kBitBlockHeaderBytes;
	const char* pszEnd = pszAt + ss::BitBlockBits(pBlock) / 8;
	for (int i = 0; i < nFiles && pszAt < pszEnd; i++)
	{
		list.emplace_back(pszAt);
		pszAt += strlen(pszAt) + 1;
	}
	return nFiles;
}

void CParticleManifestClient::RuntimeUp()
{
	if (!m_bReadPending || !m_Areas.m_pParticlesClient)
		return;
	m_bReadPending = false;
	Parse(m_bPendingLoadSheets, m_bPendingPrecache);
}

void CParticleManifestClient::Parse(bool bLoadSheets, bool bPrecache)
{
	MEM_ALLOC_CREDIT();

	if (!m_Areas.m_pParticlesClient)
	{
		m_bReadPending = true;
		m_bPendingLoadSheets = bLoadSheets;
		m_bPendingPrecache = bPrecache;
		return;
	}

	m_nDefinitionReads++;

	g_pParticleSystemMgr->ShouldLoadSheets(bLoadSheets);

	std::vector<std::string> files;
	ManifestFiles(false, NULL, files);

	int nCount = (int)files.size();
	for (int i = 0; i < nCount; ++i)
	{
		g_pParticleSystemMgr->ReadParticleConfigFile(files[i].c_str(), bPrecache, false);
	}

	g_pParticleSystemMgr->DecommitTempMemory();
}

void CParticleManifestClient::Reload()
{
	MEM_ALLOC_CREDIT();

	std::vector<std::string> files;
	ManifestFiles(false, NULL, files);

	// CAB 2/17/11 Reload all the particles regardless (Fixes filename change exploits).
	bool bReloadAll = true;

	if (bReloadAll)
	{
		Parse(true, true);
	}

	g_pParticleSystemMgr->DecommitTempMemory();
}

// Loads the per-map manifest.
int CParticleManifestClient::ParseMapFiles(const char* pMapName, bool bLoadSheets)
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

// Re-precaches the always-precache definitions that LevelShutdown uncached,
// without re-reading the DMX files. Returns how many it precached.
int CParticleManifestClient::PrecacheDefined()
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
