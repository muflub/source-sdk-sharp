//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's world traces and render pose reads, slots of IShim areas, as one object, a
// member of the client DLL object handed the standalone clients.

#pragma once

#include <stdint.h>

#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CStandaloneClients;

class CClientEngineInterfaces;

class CWorldQueryClient
{
public:
	CWorldQueryClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone)
		: m_Engines(engines)
		, m_Standalone(standalone)
	{
	}

	CWorldQueryClient(const CWorldQueryClient&) = delete;
	CWorldQueryClient& operator=(const CWorldQueryClient&) = delete;

	int32_t ClientTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, int32_t nFilter,
							int32_t nPassEntityIndex, float* pOutFraction, float* pOutEndXYZ, float* pOutNormalXYZ,
							int32_t* pOutHitbox, int32_t* pOutEntityIndex, int32_t* pOutSurfaceProps,
							int32_t* pOutSurfaceFlags);
	int32_t ClientTraceHull(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ, const float* pMaxsXYZ,
							int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex, float* pOutFraction,
							float* pOutEndXYZ);
	int32_t ClientEntityRenderPose(int32_t nEntityIndex, float* pOutOriginXYZ, float* pOutAngles);
	int32_t ClientClipRayToEntities(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask,
									const int32_t* pEntityIndices, int32_t nEntities, float* pOutFraction,
									float* pOutEndXYZ, float* pOutNormalXYZ, int32_t* pOutHitbox,
									int32_t* pOutEntityIndex, int32_t* pOutSurfaceProps, int32_t* pOutSurfaceFlags);
	int32_t ClientTraceHullEntities(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
									const float* pMaxsXYZ, int32_t nMask, int32_t nPassEntityIndex,
									const int32_t* pEntityIndices, int32_t nEntities, float* pOutFraction,
									float* pOutEndXYZ, int32_t* pOutEntityIndex, float* pOutEntityOriginXYZ);

private:
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
};
