//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// Where the world render state managed map entities decide goes: the water LOD
// distances and the tonemap controls. No state of its own; it lives in the
// entities' replicated fields.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"
#include "iviewrender.h"

#include "shell_standalone_client.h"
#include "sourcesharp.h"
#include "shell_standalone_clients.h"
#include "shell_worldrender_object.h"
#include "iviewrender.h"
#include "shell_client_engine_interfaces.h"

// The tonemap controls viewpostprocess.cpp held as globals; written here, read
// by the render executor's trace note.

// A short block is dropped with a warning rather than read past the caller's buffer.
static bool WorldRenderCountOk(const char* pWhat, int32_t nCount, int32_t nWanted)
{
	if (nCount >= nWanted)
		return true;

	Warning("[SourceSharp] ClientSetWorldRenderState(%s): %d values, wanted %d — ignored\n", pWhat, nCount, nWanted);
	return false;
}

void CWorldRenderClient::ClientSetWorldRenderState(int32_t nWhich, const float* pValues, int32_t nCount)
{
	if (!pValues || nCount <= 0)
		return;

	// At developer 2: nothing else shows that this ran.
	DevMsg(2, "[SourceSharp] world render state %d, %d value(s)\n", nWhich, nCount);

	switch (nWhich)
	{
		case SOURCESHARP_WORLDRENDER_WATERLOD:
		{
			if (!WorldRenderCountOk("waterlod", nCount, 2))
				return;

			m_View.SetCheapWaterStartDistance(pValues[0]);
			m_View.SetCheapWaterEndDistance(pValues[1]);
			return;
		}

		case SOURCESHARP_WORLDRENDER_TONEMAP:
		{
			if (!WorldRenderCountOk("tonemap", nCount, 7))
				return;

			m_bUseCustomAutoExposureMin = (pValues[0] != 0.0f);
			m_bUseCustomAutoExposureMax = (pValues[1] != 0.0f);
			m_bUseCustomBloomScale = (pValues[2] != 0.0f);
			m_flCustomAutoExposureMin = pValues[3];
			m_flCustomAutoExposureMax = pValues[4];
			m_flCustomBloomScale = pValues[5];
			m_flCustomBloomScaleMinimum = pValues[6];
			return;
		}

		default:
			Warning("[SourceSharp] ClientSetWorldRenderState: unknown selector %d\n", nWhich);
			return;
	}
}

// The colour-correction lookups on the material system, through the opaque
// handles managed code holds. As stock's FIXME notes, the active count is of
// SetWeight calls with a non-zero weight, not of distinct lookups.
void* CWorldRenderClient::ColorLookupAdd(const char* pLookupFile)
{
	if (!pLookupFile || !pLookupFile[0])
		return NULL;

	// A copy: the argument is managed memory this side must not write through.
	char szFile[MAX_PATH];
	Q_strncpy(szFile, pLookupFile, sizeof(szFile));

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	ColorCorrectionHandle_t hLookup = pRenderContext->AddLookup(szFile);

	if (!hLookup)
	{
		Warning("Cannot find color correction lookup file: '%s'\n", szFile);
		return NULL;
	}

	pRenderContext->LockLookup(hLookup);
	pRenderContext->LoadLookup(hLookup, szFile);
	pRenderContext->UnlockLookup(hLookup);

	// At developer 2: nothing else shows that this ran.
	DevMsg(2, "[SourceSharp] colour lookup loaded from '%s'\n", szFile);

	return (void*)hLookup;
}

void CWorldRenderClient::ColorLookupSetWeight(void* hLookup, float flWeight)
{
	if (!hLookup)
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->SetLookupWeight((ColorCorrectionHandle_t)hLookup, flWeight);

	if (flWeight != 0.0f)
		++m_nActiveColorWeights;
}

void CWorldRenderClient::ColorLookupRemove(void* hLookup)
{
	if (!hLookup)
		return;

	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->RemoveLookup((ColorCorrectionHandle_t)hLookup);
}

void CWorldRenderClient::ColorLookupResetWeights()
{
	CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
	pRenderContext->ResetLookupWeights();
	m_nActiveColorWeights = 0;
}

// Whether any lookup was given a non-zero weight since the reset.
bool CWorldRenderClient::ColorCorrectionActive() const
{
	return m_nActiveColorWeights != 0;
}

// The local player's origin (stock's choice, not the view origin). Answers 0
// with no local player rather than a zero vector, which would read as the map
// origin.
int32_t CStandaloneClients::GetLocalPlayerOrigin(float* pOutXYZ)
{
	if (!pOutXYZ)
		return 0;

	Vector vecOrigin;

	if (C_StandaloneEntity* pStandalone = LocalPlayer())
	{
		vecOrigin = pStandalone->GetAbsOrigin();
	}
	else
	{
		return 0;
	}

	pOutXYZ[0] = vecOrigin.x;
	pOutXYZ[1] = vecOrigin.y;
	pOutXYZ[2] = vecOrigin.z;
	return 1;
}
