//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The engine's text overlays, for the UI's draw. A member of the client DLL object.

#pragma once

#include <stdint.h>

class CClientEngineInterfaces;

class CDebugOverlayClient final
{
public:
	explicit CDebugOverlayClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CDebugOverlayClient(const CDebugOverlayClient&) = delete;
	CDebugOverlayClient& operator=(const CDebugOverlayClient&) = delete;

	int32_t UiDebugOverlayText(int32_t nIndex, float* pOutX, float* pOutY, int32_t* pOutLine, uint32_t* pOutRgba,
							   uint8_t* pText, int32_t nTextBytes);
	void UiDebugOverlayClearDead();

private:
	const CClientEngineInterfaces& m_Engines;
};
