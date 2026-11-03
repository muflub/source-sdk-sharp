//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The sound envelope controller's patches as one object, a member of the client DLL object.
// Managed decides each patch's envelope; the controller here holds the patches and carries
// the decisions out. Its IShimSoundscapeClient slots are the patch's.

#pragma once

#include <stdint.h>

#include "shell_managed_areas_client.h"

class CShellClientSoundController;
class CSoundEnvelopeController;
class CGameRulesClient;
class CStandaloneClients;

class CClientEngineInterfaces;

class CSoundPatchesClient
{
public:
	CSoundPatchesClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
						CStandaloneClients& standalone, const CGameRulesClient& gameRules);
	~CSoundPatchesClient();

	CSoundPatchesClient(const CSoundPatchesClient&) = delete;
	CSoundPatchesClient& operator=(const CSoundPatchesClient&) = delete;

	// Stock's GetController, for the probe build's sound sessions.
	CSoundEnvelopeController& Controller();

	// IShimSoundscapeClient.
	void SoundPatchEmit(int32_t hPatch, float flVolume01, float flPitch, int32_t nFlags, float flStartTime,
						int32_t bEmitCaption);
	void SoundPatchStop(int32_t hPatch);
	void SoundPatchState(int32_t hPatch, int32_t* pnEntIndex, int32_t* pbFilterActive);
	void SoundPatchFree(int32_t hPatch);
	int32_t SoundPatchCreate(uint32_t hEntity, int32_t nChannel, const char* pSoundName, int32_t nSoundLevel);

private:
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
	CShellClientSoundController* m_pController;
};
