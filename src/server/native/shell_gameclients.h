//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IServerGameClients: everything the engine asks the game about one player. The
// engine holds the root's forwarder (late_bound_game_clients.g.h) from load; this
// is the DLL tier's implementation it forwards to, handed what it calls.

#pragma once

#include "eiface.h"
#include "PlayerState.h"
#include "ss_client_message.h"
#include "late_bound_game_clients.g.h"

class CGlobalVars;
class IMDLCache;
class IServerPlayerMaker;
class CBoundaryProbe;
class CServerEntityContext;
class IManagedClients;
struct CManagedAreas;
struct edict_t;

class CServerGameClients final : public IServerGameClients, private IClientMessageHandler
{
public:
	CServerGameClients( IVEngineServer *pEngine, CGlobalVars *pGlobals, IMDLCache *pMdlCache,
						const CManagedAreas &managed, const bool &bManagedLoaded, IServerPlayerMaker &players,
						CBoundaryProbe &probes, const CServerEntityContext &entities )
		: m_pEngine( pEngine )
		, m_pGlobals( pGlobals )
		, m_pMdlCache( pMdlCache )
		, m_Managed( managed )
		, m_bManagedLoaded( bManagedLoaded )
		, m_Players( players )
		, m_Probes( probes )
		, m_Entities( entities )
	{
	}

	CServerGameClients( const CServerGameClients & ) = delete;
	CServerGameClients &operator=( const CServerGameClients & ) = delete;

	bool ClientConnect( edict_t *pEntity, char const *pszName, char const *pszAddress, char *reject,
						int maxrejectlen ) override;
	void ClientActive( edict_t *pEntity, bool bLoadGame ) override;
	void ClientDisconnect( edict_t *pEntity ) override;
	void ClientPutInServer( edict_t *pEntity, const char *playername ) override;
	void ClientCommand( edict_t *pEntity, const CCommand &args ) override;
	void ClientSettingsChanged( edict_t *pEntity ) override;
	void ClientSetupVisibility( edict_t *pViewEntity, edict_t *pClient, unsigned char *pvs, int pvssize ) override;
	float ProcessUsercmds( edict_t *player, bf_read *buf, int numcmds, int totalcmds, int dropped_packets, bool ignore,
						   bool paused ) override;
	void PostClientMessagesSent_DEPRECIATED() override;
	void SetCommandClient( int index ) override;
	CPlayerState *GetPlayerState( edict_t *player ) override;
	void ClientEarPosition( edict_t *pEntity, Vector *pEarOrigin ) override;
	void GetPlayerLimits( int &minplayers, int &maxplayers, int &defaultMaxPlayers ) const override;
	int GetReplayDelay( edict_t *player, int &entity ) override;
	void GetBugReportInfo( char *buf, int buflen ) override;
	void NetworkIDValidated( const char *pszUserName, const char *pszNetworkID ) override;
	void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) override;
	void ClientSpawned( edict_t *pPlayer ) override;
	void ClientVoice( edict_t *pEdict ) override;

private:
	// A client's net message, handed to managed code, which decides what it means. A
	// message nobody claims, or no managed side, is not the client's fault.
	bool OnClientMessage( CSourceSharpClientMessage &msg ) override;

	void RegisterClientMessage( int nEntIndex );

	// Never NULL: two engine sites write through GetPlayerState unchecked, one on an
	// edict with no entity.
	CPlayerState *PlayerStateForEdict( edict_t *pEdict );

	// Where this client's eyes are, asked of managed code at most once per frame.
	bool ClientViewOrigin( edict_t *pClient, Vector *pOut );

	// The managed clients area while the managed side is up, else NULL.
	IManagedClients *ManagedClientsIfLoaded() const;

	// 1 with the client's ear position in pOutXYZ, else 0 (stock's miss path).
	int EarPositionForClient( int iClientEdict, float *pOutXYZ ) const;

	// The area portal walk for ClientSetupVisibility: sets the client's bits in pPortalBits
	// (zeroed by the caller) and returns -1, or a portal number too large for the bits.
	int AreaPortalVisibility( const float *pOriginXYZ, float flFovDistanceAdjustFactor, unsigned char *pPortalBits,
							  int nPortalBytes ) const;

	IVEngineServer *m_pEngine;
	CGlobalVars *m_pGlobals;
	IMDLCache *m_pMdlCache;
	const CManagedAreas &m_Managed;

	// False while the managed side is down, a reload included.
	const bool &m_bManagedLoaded;

	// Makes the player entity a client is put in the server with.
	IServerPlayerMaker &m_Players;

	// Counts the engine's calls into this object, for ss_boundary_probe_report.
	CBoundaryProbe &m_Probes;

	// Whose level's rules the engine's client events reach.
	const CServerEntityContext &m_Entities;

	// GetPlayerState's answer for an edict with no player, per edict, so the HLTV and
	// Replay masters don't share one.
	CPlayerState m_OrphanPlayerStates[ABSOLUTE_PLAYER_LIMIT + 1];
};

// The root's IServerGameClients, which the engine holds from load: it forwards to the
// DLL tier's CServerGameClients once DLLInit binds it, and answers GetPlayerLimits
// itself, since the engine asks for the limits while loading the DLL.
class CRootServerGameClients final : public CLateBoundServerGameClients
{
public:
	void GetPlayerLimits( int &minplayers, int &maxplayers, int &defaultMaxPlayers ) const override;
};
