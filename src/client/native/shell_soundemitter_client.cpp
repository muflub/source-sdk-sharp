//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
// The sound emitter system's row in kClientSystems[]: each stage forwards to
// managed code. It stays second, before anything asks the table for a script.
// Init fails when the scripts will not load or managed code is absent.

#include "tier0/dbg.h"

#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

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
