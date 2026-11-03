//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The particle manifests' reads as one object, a member of the client DLL object. The
// manifests are read in C#; the client still loads every definition to simulate and draw it.
// Its IShimParticlesClient slot is the count of full reads, for the dev API.

#pragma once

#include <stdint.h>

#include <string>
#include <vector>

#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;

class CParticleManifestClient
{
public:
	CParticleManifestClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines)
		: m_Areas(areas)
		, m_Engines(engines)
	{
	}

	CParticleManifestClient(const CParticleManifestClient&) = delete;
	CParticleManifestClient& operator=(const CParticleManifestClient&) = delete;

	// The first read is asked for before the managed runtime is up; it is held and run from
	// RuntimeUp, still in Init.
	void Parse(bool bLoadSheets, bool bPrecache);
	void RuntimeUp();

	void Reload();

	// The per-map manifest; how many files it named.
	int ParseMapFiles(const char* pszMapName, bool bLoadSheets);

	// Re-precaches the always-precache definitions that LevelShutdown uncached, without
	// re-reading the DMX files. Returns how many it precached.
	int PrecacheDefined();

	// IShimParticlesClient.
	int32_t ParticleDefinitionReads() const { return m_nDefinitionReads; }

private:
	int ManifestFiles(bool bMap, const char* pszMapName, std::vector<std::string>& list);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;

	bool m_bReadPending = false;
	bool m_bPendingLoadSheets = false;
	bool m_bPendingPrecache = false;

	int m_nDefinitionReads = 0;
};
