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

// The tonemap controls viewpostprocess.cpp held as globals; written here, read
// by the render executor's trace note.
bool g_bUseCustomAutoExposureMin = false;
bool g_bUseCustomAutoExposureMax = false;
bool g_bUseCustomBloomScale = false;
float g_flCustomAutoExposureMin = 0.0f;
float g_flCustomAutoExposureMax = 0.0f;
float g_flCustomBloomScale = 0.0f;
float g_flCustomBloomScaleMinimum = 0.0f;

// A short block is dropped with a warning rather than read past the caller's buffer.
static bool WorldRenderCountOk(const char* pWhat, int32_t nCount, int32_t nWanted)
{
	if (nCount >= nWanted)
		return true;

	Warning("[SourceSharp] ClientSetWorldRenderState(%s): %d values, wanted %d — ignored\n", pWhat, nCount, nWanted);
	return false;
}

void SourceSharp_ClientSetWorldRenderState(int32_t nWhich, const float* pValues, int32_t nCount)
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

			view->SetCheapWaterStartDistance(pValues[0]);
			view->SetCheapWaterEndDistance(pValues[1]);
			return;
		}

		case SOURCESHARP_WORLDRENDER_TONEMAP:
		{
			if (!WorldRenderCountOk("tonemap", nCount, 7))
				return;

			g_bUseCustomAutoExposureMin = (pValues[0] != 0.0f);
			g_bUseCustomAutoExposureMax = (pValues[1] != 0.0f);
			g_bUseCustomBloomScale = (pValues[2] != 0.0f);
			g_flCustomAutoExposureMin = pValues[3];
			g_flCustomAutoExposureMax = pValues[4];
			g_flCustomBloomScale = pValues[5];
			g_flCustomBloomScaleMinimum = pValues[6];
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
static int g_nActiveColorWeights;

void* SourceSharp_ColorLookupAdd(const char* pLookupFile)
{
	if (!pLookupFile || !pLookupFile[0])
		return NULL;

	// A copy: the argument is managed memory this side must not write through.
	char szFile[MAX_PATH];
	Q_strncpy(szFile, pLookupFile, sizeof(szFile));

	CMatRenderContextPtr pRenderContext(g_pMaterialSystem);
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

void SourceSharp_ColorLookupSetWeight(void* hLookup, float flWeight)
{
	if (!hLookup)
		return;

	CMatRenderContextPtr pRenderContext(g_pMaterialSystem);
	pRenderContext->SetLookupWeight((ColorCorrectionHandle_t)hLookup, flWeight);

	if (flWeight != 0.0f)
		++g_nActiveColorWeights;
}

void SourceSharp_ColorLookupRemove(void* hLookup)
{
	if (!hLookup)
		return;

	CMatRenderContextPtr pRenderContext(g_pMaterialSystem);
	pRenderContext->RemoveLookup((ColorCorrectionHandle_t)hLookup);
}

void SourceSharp_ColorLookupResetWeights()
{
	CMatRenderContextPtr pRenderContext(g_pMaterialSystem);
	pRenderContext->ResetLookupWeights();
	g_nActiveColorWeights = 0;
}

// Whether any lookup was given a non-zero weight since the reset.
bool SourceSharp_ColorCorrectionActive()
{
	return g_nActiveColorWeights != 0;
}

// The local player's origin (stock's choice, not the view origin). Answers 0
// with no local player rather than a zero vector, which would read as the map
// origin.
int32_t SourceSharp_GetLocalPlayerOrigin(float* pOutXYZ)
{
	if (!pOutXYZ)
		return 0;

	Vector vecOrigin;

	if (C_StandaloneEntity* pStandalone = SourceSharp_LocalPlayerShell())
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
