//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The shared app systems the engine asks the client DLL for before loading it:
// soundemittersystem and scenefilecache.
#include "appframework/IAppSystemGroup.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "scenefilecache/ISceneFileCache.h"

#include "cdll_int.h"
#include "shell_gameui.h"
#include "shell_clientdll_exports.h"

CClientDLLSharedAppSystems::CClientDLLSharedAppSystems(CGameUiClient& gameUi)
	: m_GameUi(gameUi)
{
	AddAppSystem("soundemittersystem" DLL_EXT_STRING, SOUNDEMITTERSYSTEM_INTERFACE_VERSION);
	AddAppSystem("scenefilecache" DLL_EXT_STRING, SCENE_FILE_CACHE_INTERFACE_VERSION);
}

int CClientDLLSharedAppSystems::Count()
{
	m_GameUi.InstallFactorySwap();
	return m_Systems.Count();
}

char const* CClientDLLSharedAppSystems::GetDllName(int idx)
{
	return m_Systems[idx].m_pModuleName;
}

char const* CClientDLLSharedAppSystems::GetInterfaceName(int idx)
{
	return m_Systems[idx].m_pInterfaceName;
}

void CClientDLLSharedAppSystems::AddAppSystem(char const* moduleName, char const* interfaceName)
{
	AppSystemInfo_t sys;
	sys.m_pModuleName = moduleName;
	sys.m_pInterfaceName = interfaceName;
	m_Systems.AddToTail(sys);
}
