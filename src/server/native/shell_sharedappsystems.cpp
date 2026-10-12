//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON b  an interface resolved by name at load
//
// IServerDLLSharedAppSystems -- THE TWO DLLs THE ENGINE LOADS ON OUR BEHALF.
//
// plan_gamedll_after.md Phase A9 step 1. Carried verbatim out of
// game/server/gameinterface.cpp:3070-3103, which this phase deletes.
//
// THESE THREE METHODS CAN NEVER FORWARD TO MANAGED, AND THAT IS NOT A "NOT
// PORTED YET". The engine asks a server DLL which shared libraries to bring up
// *as app systems* -- soundemittersystem and scenefilecache -- during
// AppSystemGroup startup, which is BEFORE it calls DLLInit. DLLInit is where
// SourceSharp acquires the runtime: there is no CLR, no host, and no managed
// assembly loaded at the moment Count()/GetDllName()/GetInterfaceName() are
// called. A managed implementation is not a later refinement of this file, it
// is impossible, and anyone reading the port's "everything goes to C#" rule
// against this file should stop here rather than re-deriving it.
//
// So the answer stays two native constants, built in the constructor of a
// singleton whose only job is to hold them.
//
// NO FILE-SCOPE STATE: m_Systems is a member, as it always was, and the
// singleton is the anonymous-namespace object shell_gamedll.cpp already uses
// for the same purpose.
//
//===========================================================================//

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

	virtual int	Count()
	{
		return (int)m_Systems.size();
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
		m_Systems.push_back( sys );
	}

	std::vector< AppSystemInfo_t >	m_Systems;
};

namespace
{
	CServerDLLSharedAppSystems s_ServerDLLSharedAppSystems;
}
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerDllSharedAppSystems() { return (IServerDLLSharedAppSystems *)&s_ServerDLLSharedAppSystems; }
const SourceSharpExposed kExposeServerDllSharedAppSystems = { SERVER_DLL_SHARED_APPSYSTEMS, SourceSharp_GetServerDllSharedAppSystems };
