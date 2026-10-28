//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IServerGameClients: the engine's interface for every question about one player.
#pragma once

#include "eiface.h"
#include "convar.h"

struct edict_t;

// Player / Client related functions
class CServerGameClients : public IServerGameClients
{
public:
	virtual bool			ClientConnect( edict_t *pEntity, char const* pszName, char const* pszAddress, char *reject, int maxrejectlen ) OVERRIDE;
	virtual void			ClientActive( edict_t *pEntity, bool bLoadGame ) OVERRIDE;
	virtual void			ClientDisconnect( edict_t *pEntity ) OVERRIDE;
	virtual void			ClientPutInServer( edict_t *pEntity, const char *playername ) OVERRIDE;
	virtual void			ClientCommand( edict_t *pEntity, const CCommand &args ) OVERRIDE;
	virtual void			ClientSettingsChanged( edict_t *pEntity ) OVERRIDE;
	virtual void			ClientSetupVisibility( edict_t *pViewEntity, edict_t *pClient, unsigned char *pvs, int pvssize ) OVERRIDE;
	virtual float			ProcessUsercmds( edict_t *player, bf_read *buf, int numcmds, int totalcmds,
								int dropped_packets, bool ignore, bool paused ) OVERRIDE;
	// Player is running a command
	virtual void			PostClientMessagesSent_DEPRECIATED( void ) OVERRIDE;
	virtual void			SetCommandClient( int index ) OVERRIDE;
	virtual CPlayerState	*GetPlayerState( edict_t *player ) OVERRIDE;
	virtual void			ClientEarPosition( edict_t *pEntity, Vector *pEarOrigin ) OVERRIDE;

	virtual void			GetPlayerLimits( int& minplayers, int& maxplayers, int &defaultMaxPlayers ) const OVERRIDE;
	
	// returns number of delay ticks if player is in Replay mode (0 = no delay)
	virtual int				GetReplayDelay( edict_t *player, int& entity ) OVERRIDE;
	// Anything this game .dll wants to add to the bug reporter text (e.g., the entity/model under the picker crosshair)
	//  can be added here
	virtual void			GetBugReportInfo( char *buf, int buflen ) OVERRIDE;
	virtual void			NetworkIDValidated( const char *pszUserName, const char *pszNetworkID ) OVERRIDE;

	// The client has submitted a keyvalues command
	virtual void			ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) OVERRIDE;

	// Notify that the player is spawned
	virtual void			ClientSpawned( edict_t *pPlayer ) OVERRIDE;

	virtual void			ClientVoice( edict_t* pEdict ) OVERRIDE;
};
