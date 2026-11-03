//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Command words of IManagedPixelVisClient::EndView's reply; managed code mirrors these values.

#pragma once

#include <stdint.h>

#include "materialsystem/imaterialsystem.h"
#include "utlvector.h"

#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class CViewCameras;
class CEffectsListClient;

enum
{
	SS_PIXELVIS_CMD_BIND = 1, // material
	SS_PIXELVIS_CMD_DEPTHRANGE = 2, // near, far (float bits)
	SS_PIXELVIS_CMD_PROXY = 3, // query id, material, 12 vertices x 3 (float bits)
	SS_PIXELVIS_PROXY_WORDS = 3 + 36,
	SS_PIXELVIS_VIEW_FLOATS = 29, // id, origin, forward, right, up, world-to-screen (row-major)
};

// The pixel visibility system's native half, a member of the client DLL object: the occlusion
// query objects by id, and the view's end, whose commands it runs. Its IShimViewClient slots
// are the queries' and the view read.
class CPixelVisClient
{
public:
	CPixelVisClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
					const CViewCameras& cameras, CEffectsListClient& effects)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
		, m_Effects(effects)
	{
	}

	CPixelVisClient(const CPixelVisClient&) = delete;
	CPixelVisClient& operator=(const CPixelVisClient&) = delete;

	// The plan's ops.
	void EndView();
	void EndScene();

	// IShimViewClient.
	int32_t PixelVisQueryCreate();
	void PixelVisQueryDestroy(int32_t nQuery);
	void PixelVisQueryReset(int32_t nQuery);
	void PixelVisQueryPixels(int32_t nVisible, int32_t nCount, int32_t* pOut);
	void PixelVisDiameters(const float* pSpheres, int32_t nSpheres, float* pOut);
	int32_t PixelVisView(float* pOut, int32_t nFloats);

private:
	OcclusionQueryObjectHandle_t QueryOf(int32_t nQuery) const;
	void Execute(const int32_t* pCmds, int nWords);

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CEffectsListClient& m_Effects;

	// Query objects by id: slot id - 1, INVALID when free.
	CUtlVector<OcclusionQueryObjectHandle_t> m_Queries;

	// The commands a view's end answers with (PixelVisDraw's, in C#).
	CUtlVector<int32_t> m_Commands;
};
