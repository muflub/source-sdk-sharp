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

class CPrecipitationClient
{
public:
	explicit CPrecipitationClient(const CManagedAreasClient& areas)
		: m_Areas(areas)
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

	// Made at the first draw: the plan's buffers are megabytes.
	Plan_t* m_pPlan = nullptr;
};
