//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The client soundscape system's native half; the system itself is managed.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "filesystem.h"
#include "soundchars.h"
#include "view.h"

#include "cdll_util.h"
#include "imanaged_areas_client.h"
#include "shell_soundscape_audio_client.h"

#include "ss_command_line.h"
#include "shell_view_cameras.h"
#include "shell_client_engine_interfaces.h"

void C_SoundscapeSystem::LevelInitPreEntity()
{
	if (IManagedSoundscapesClient* pApi = m_Areas.m_pSoundscapesClient)
	{
		char szMapName[MAX_PATH];
		pApi->ClientSoundscapesLevelInit(SourceSharp_ClientMapName(szMapName, sizeof(szMapName)));
	}
}

void C_SoundscapeSystem::LevelShutdownPostEntity()
{
	if (IManagedSoundscapesClient* pApi = m_Areas.m_pSoundscapesClient)
		pApi->ClientSoundscapesLevelShutdown();
}

void C_SoundscapeSystem::Update(float frametime)
{
	if (IManagedSoundscapesClient* pApi = m_Areas.m_pSoundscapesClient)
		pApi->ClientSoundscapesUpdate(frametime);
}

void CSoundscapeAudioClient::SoundscapeSetRoomType(int32_t nRoomType)
{
	CLocalPlayerFilter filter;
	m_Engines.m_pSound->SetRoomType(filter, nRoomType);
}

void CSoundscapeAudioClient::SoundscapeSetPlayerDsp(int32_t nDspType)
{
	CLocalPlayerFilter filter;
	m_Engines.m_pSound->SetPlayerDSP(filter, nDspType, false);
}

void CSoundscapeAudioClient::SoundscapeMainView(float* pOut9)
{
	if (!pOut9)
		return;

	const Vector& o = m_Cameras.MainOrigin();
	const Vector& f = m_Cameras.MainForward();
	const Vector& r = m_Cameras.MainRight();
	const float v[9] = {o.x, o.y, o.z, f.x, f.y, f.z, r.x, r.y, r.z};
	for (int i = 0; i < 9; ++i)
		pOut9[i] = v[i];
}

int32_t CSoundscapeAudioClient::SoundscapeMakingResLists()
{
	return ss::CommandLineHas("-makereslists") ? 1 : 0;
}

void CSoundscapeAudioClient::SoundscapeTouchSoundFile(const char* pszWave)
{
	if (pszWave)
		m_Engines.m_pFileSystem->GetFileTime(VarArgs("sound/%s", PSkipSoundChars(pszWave)), "GAME");
}
