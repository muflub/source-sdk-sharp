//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the sound emitter system's row, forwarded ===========//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// stock's CSoundEmitterSystem (game/shared/SoundEmitterSystem.cpp) is a game
// system, and its stages are managed now: SourceSharp.Client.Sounds'
// ClientSoundEmitterSystem loads and unloads the script table (ModInit,
// ModShutdown) and adds and clears the level's own override script. Its row in
// kClientSystems[] stays where stock's CHLClient::Init added it -- second,
// before anything else asks the table for a script -- and this class is that
// row: each stage one crossing (IManagedSoundEmitterClient), deciding nothing.
//
// INIT'S ANSWER IS MANAGED'S. A table that will not load stops the client as
// the native row's false did; so does no managed side to ask, which says so.
//
// NO FILE-SCOPE STATE: the row is a function-local static, as
// shell_soundenvelope_client.cpp's controller is.
//=============================================================================//

#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!

namespace
{

class CSoundEmitterSystemRow : public CClientSystem
{
public:
	virtual char const *Name() OVERRIDE { return "CSoundEmitterSystem"; }

	virtual bool Init() OVERRIDE
	{
		IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient();
		if ( !pApi )
		{
			Warning( "[SourceSharp] CSoundEmitterSystem: no IManagedSoundEmitterClient, so the sound "
			         "scripts cannot load\n" );
			return false;
		}

		return pApi->ClientSoundSystemInit();
	}

	virtual void Shutdown() OVERRIDE
	{
		if ( IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient() )
			pApi->ClientSoundSystemShutdown();
	}

	virtual void LevelInitPreEntity() OVERRIDE
	{
		if ( IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient() )
			pApi->ClientSoundLevelInitPreEntity();
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		if ( IManagedSoundEmitterClient *pApi = ManagedSoundEmitterClient() )
			pApi->ClientSoundLevelShutdownPostEntity();
	}
};

} // namespace

CClientSystem *ClientSystem_SoundEmitter()
{
	static CSoundEmitterSystemRow s_Row;
	return &s_Row;
}
