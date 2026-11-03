//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The managed dust motes' spawn test, a member of the client DLL object.

#pragma once

#include <stdint.h>

#include "shell_managed_areas_client.h"

class CStandaloneClients;

class CClientEngineInterfaces;

class CDustClient
{
public:
	CDustClient(const CClientEngineInterfaces& engines, CStandaloneClients& standalone)
		: m_Engines(engines)
		, m_Standalone(standalone)
	{
	}

	CDustClient(const CDustClient&) = delete;
	CDustClient& operator=(const CDustClient&) = delete;

	int32_t ParticlesBrushPointContents(uint32_t hOwner, const float* pPointXYZ);

private:
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
};
