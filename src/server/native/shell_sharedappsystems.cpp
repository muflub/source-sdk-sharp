//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

#include "shell_sharedappsystems.h"

#include "appframework/IAppSystemGroup.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "scenefilecache/ISceneFileCache.h"

namespace
{
const AppSystemInfo_t kSharedAppSystems[] = {
	{ "soundemittersystem" DLL_EXT_STRING, SOUNDEMITTERSYSTEM_INTERFACE_VERSION },
	{ "scenefilecache" DLL_EXT_STRING, SCENE_FILE_CACHE_INTERFACE_VERSION },
};
} // namespace

int CServerDLLSharedAppSystems::Count()
{
	return (int)( sizeof( kSharedAppSystems ) / sizeof( kSharedAppSystems[0] ) );
}

char const *CServerDLLSharedAppSystems::GetDllName( int idx )
{
	return kSharedAppSystems[idx].m_pModuleName;
}

char const *CServerDLLSharedAppSystems::GetInterfaceName( int idx )
{
	return kSharedAppSystems[idx].m_pInterfaceName;
}
