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
#include "tier0/basetypes.h"
#include "tier1/interface.h"
#include "tier1/utlvector.h"
#include "cdll_int.h"

class CClientDLLSharedAppSystems : public IClientDLLSharedAppSystems
{
public:
	CClientDLLSharedAppSystems()
	{
		AddAppSystem( "soundemittersystem" DLL_EXT_STRING, SOUNDEMITTERSYSTEM_INTERFACE_VERSION );
		AddAppSystem( "scenefilecache" DLL_EXT_STRING, SCENE_FILE_CACHE_INTERFACE_VERSION );
	}

	virtual int	Count()
	{
		return m_Systems.Count();
	}
	virtual char const *GetDllName( int idx )
	{
		return m_Systems[ idx ].m_pModuleName;
	}
	virtual char const *GetInterfaceName( int idx )
	{
		return m_Systems[ idx ].m_pInterfaceName;
	}
private:
	void AddAppSystem( char const *moduleName, char const *interfaceName )
	{
		AppSystemInfo_t sys;
		sys.m_pModuleName = moduleName;
		sys.m_pInterfaceName = interfaceName;
		m_Systems.AddToTail( sys );
	}

	CUtlVector< AppSystemInfo_t >	m_Systems;
};

EXPOSE_SINGLE_INTERFACE( CClientDLLSharedAppSystems, IClientDLLSharedAppSystems, CLIENT_DLL_SHARED_APPSYSTEMS );
