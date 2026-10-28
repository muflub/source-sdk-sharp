//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
//
// IServerDLLSharedAppSystems: the two DLLs the engine loads as app systems on the game's
// behalf (gameinterface.cpp). Necessarily native: the engine asks before DLLInit, when
// no managed runtime exists.

#include "appframework/IAppSystemGroup.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "scenefilecache/ISceneFileCache.h"
#include "eiface.h"
#include "shell_factory.h"

#include <vector>

class CServerDLLSharedAppSystems : public IServerDLLSharedAppSystems
{
public:
	CServerDLLSharedAppSystems()
	{
		AddAppSystem( "soundemittersystem" DLL_EXT_STRING, SOUNDEMITTERSYSTEM_INTERFACE_VERSION );
		AddAppSystem( "scenefilecache" DLL_EXT_STRING, SCENE_FILE_CACHE_INTERFACE_VERSION );
	}

	virtual int Count() { return (int)m_Systems.size(); }

	virtual char const *GetDllName( int idx ) { return m_Systems[idx].m_pModuleName; }

	virtual char const *GetInterfaceName( int idx ) { return m_Systems[idx].m_pInterfaceName; }

private:
	void AddAppSystem( char const *moduleName, char const *interfaceName )
	{
		AppSystemInfo_t sys;
		sys.m_pModuleName = moduleName;
		sys.m_pInterfaceName = interfaceName;
		m_Systems.push_back( sys );
	}

	std::vector<AppSystemInfo_t> m_Systems;
};

namespace
{
CServerDLLSharedAppSystems s_ServerDLLSharedAppSystems;
}

// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerDllSharedAppSystems()
{
	return (IServerDLLSharedAppSystems *)&s_ServerDLLSharedAppSystems;
}

const SourceSharpExposed kExposeServerDllSharedAppSystems = { SERVER_DLL_SHARED_APPSYSTEMS,
															  SourceSharp_GetServerDllSharedAppSystems };
