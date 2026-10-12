//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- IClientDLLSharedAppSystems ========================//
// SS_REASON b  an interface resolved by name at load
//
// plan_entity.md Phase 5, the 5b split. ONE CLASS, ONE FILE.
//
// The engine asks the client DLL, before it loads it properly, which shared
// app systems it needs the launcher to bring up: soundemittersystem and
// scenefilecache. That is the whole of this class, and it is answered from a
// CUtlVector the class already owns -- so like CGameClientExports it carries
// no file-scope state and needs no proxy.
//
// The server's counterpart is sourcesharp/game/server/shell_sharedappsystems.cpp,
// which is already one class in one file; this is that shape on the client.
//
//===========================================================================//
#include "cbase.h"
#include "appframework/IAppSystemGroup.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "scenefilecache/ISceneFileCache.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Purpose: the shared app systems the launcher must bring up for this client
//-----------------------------------------------------------------------------
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
