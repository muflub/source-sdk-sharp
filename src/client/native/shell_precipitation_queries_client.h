//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The weather's world questions, a member of the client DLL object: traces, contents, the
// local player and the world's bounds, each a slot of IShimPrecipitationClient.

#pragma once

#include <stdint.h>

#include "tier1/convar.h"

#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CStandaloneClients;

class CClientEngineInterfaces;

class CPrecipitationQueriesClient final : public IShimPrecipitationClient
{
public:
	CPrecipitationQueriesClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone)
		: m_Engines(engines)
		, m_Standalone(standalone)
	{
	}

	CPrecipitationQueriesClient(const CPrecipitationQueriesClient&) = delete;
	CPrecipitationQueriesClient& operator=(const CPrecipitationQueriesClient&) = delete;

	int32_t PrecipitationTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, int32_t bIgnorePlayer,
								   float* pOut) override;
	int32_t PrecipitationTraceVolume(uint32_t hOwner, const float* pStartXYZ, const float* pEndXYZ,
									 const float* pHullMinXYZ, const float* pHullMaxXYZ, float* pOut) override;
	int32_t PrecipitationPointContents(const float* pXYZ) override;
	int32_t PrecipitationInPvs(uint32_t hOwner) override;
	int32_t PrecipitationPlayer(float* pOut) override;
	void PrecipitationWorldBounds(float* pOut6) override;
	int32_t PrecipitationScreenWidth() override;

private:
	// default_fov, for the player's read.
	ConVarRef m_DefaultFov{"default_fov"};

	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
};
