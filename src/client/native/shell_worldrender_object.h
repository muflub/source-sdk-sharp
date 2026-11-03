//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The world render state managed sets and the colour correction lookups it holds. A member of
// the client DLL object.

#pragma once

#include <stdint.h>

class CClientEngineInterfaces;
class IViewRender;

class CWorldRenderClient final
{
public:
	CWorldRenderClient(const CClientEngineInterfaces& engines, IViewRender& view)
		: m_Engines(engines)
		, m_View(view)
	{
	}

	CWorldRenderClient(const CWorldRenderClient&) = delete;
	CWorldRenderClient& operator=(const CWorldRenderClient&) = delete;

	void ClientSetWorldRenderState(int32_t nWhich, const float* pValues, int32_t nCount);
	void* ColorLookupAdd(const char* pLookupFile);
	void ColorLookupSetWeight(void* hLookup, float flWeight);
	void ColorLookupRemove(void* hLookup);
	void ColorLookupResetWeights();

	bool ColorCorrectionActive() const;

	// The tonemap overrides managed sets, which the executor's tonemap ops read.
	bool m_bUseCustomAutoExposureMin = false;
	bool m_bUseCustomAutoExposureMax = false;
	bool m_bUseCustomBloomScale = false;
	float m_flCustomAutoExposureMin = 0.0f;
	float m_flCustomAutoExposureMax = 0.0f;
	float m_flCustomBloomScale = 0.0f;
	float m_flCustomBloomScaleMinimum = 0.0f;

private:
	const CClientEngineInterfaces& m_Engines;
	IViewRender& m_View;
	int m_nActiveColorWeights = 0;
};
