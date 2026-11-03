//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The materials the string tables and the detail props precache, held for the level. A member
// of the client DLL object.

#pragma once

#include "tier1/utlvector.h"

class CClientEngineInterfaces;
class IMaterial;

class CMaterialCacheClient final
{
public:
	explicit CMaterialCacheClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CMaterialCacheClient(const CMaterialCacheClient&) = delete;
	CMaterialCacheClient& operator=(const CMaterialCacheClient&) = delete;

	void Precache(const char* pMaterialName);
	void UncacheAll();

	// The string-table callbacks request the sweep and FRAME_RENDER_START runs it.
	void RequestCache() { m_bRequestCache = true; }

	void ProcessCache();

private:
	const CClientEngineInterfaces& m_Engines;
	CUtlVector<IMaterial*> m_Cached;
	bool m_bRequestCache = false;
};
