//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The weather's draw, a member of the client DLL object, handed to the view executor:
// managed plans the tracers and splashes, this builds their meshes.

#pragma once

#include "shell_managed_areas_client.h"

class CClientEngineInterfaces;
class CViewCameras;

class CPrecipitationClient
{
public:
	CPrecipitationClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
						 const CViewCameras& cameras)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
	{
	}

	~CPrecipitationClient();

	CPrecipitationClient(const CPrecipitationClient&) = delete;
	CPrecipitationClient& operator=(const CPrecipitationClient&) = delete;

	// viewrender.cpp's DrawPrecipitation: each pass that draws the weather.
	void Draw();

private:
	struct Plan_t;

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;

	// Made at the first draw: the plan's buffers are megabytes.
	Plan_t* m_pPlan = nullptr;
};
