//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// C_SoundscapeSystem holds the stock system's slot in the client-systems walk so its
// stages run when stock's did; the system itself is managed. Scripts must load at
// LevelInitPreEntity: the server's first push arrives inside ClientActive, before managed
// level init. A member of the client systems table.

#pragma once

#include <stdint.h>

#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CViewCameras;

class CClientEngineInterfaces;

class C_SoundscapeSystem final : public CClientSystemPerFrame
{
public:
	C_SoundscapeSystem(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines)
		: CClientSystemPerFrame("C_SoundScapeSystem")
		, m_Areas(areas)
		, m_Engines(engines)
	{
	}

	void LevelInitPreEntity() override;
	void LevelShutdownPostEntity() override;
	void Update(float frametime) override;

private:
	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
};

// The soundscape shim slots: what the managed system asks of the engine's sound and the view.
class CClientEngineInterfaces;

class CSoundscapeAudioClient final
{
public:
	CSoundscapeAudioClient(const CClientEngineInterfaces& engines, const CViewCameras& cameras)
		: m_Engines(engines)
		, m_Cameras(cameras)
	{
	}

	void SoundscapeSetRoomType(int32_t nRoomType);
	void SoundscapeSetPlayerDsp(int32_t nDspType);
	void SoundscapeMainView(float* pOut9);
	int32_t SoundscapeMakingResLists();
	void SoundscapeTouchSoundFile(const char* pszWave);

private:
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
};
