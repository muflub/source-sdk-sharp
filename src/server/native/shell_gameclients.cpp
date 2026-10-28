//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// IServerGameClients: everything the engine asks the game about one player,
// exposed under three version strings so three engine vintages can load it.

#include "eiface.h"
#include "ss_entityhandle.h"
#include "shell_gameclients.h"
#include "igamerules.h"
#include "ivoiceserver.h"
#include "vstdlib/random.h"
#include "engine/IEngineSound.h"
#include "ss_soundent.h"
#include "physicslink.h"
#include "datacache/imdlcache.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
extern IMDLCache *mdlcache;
void ClientActive( edict_t *pEdict, bool bLoadGame );
void ClientPutInServer( edict_t *pEdict, const char *playername );
void UTIL_SetClientVisibilityPVS( edict_t *pClient, const unsigned char *pvs, int pvssize );
void UTIL_SetCommandClientIndex( int index );

#include "managed_convar_ref.h"

#include "shell_server.h"

#include "shell_standalone_player.h"
#include "imanaged_areas.h"
#include "inetchannel.h"
#include "ss_client_message.h"

#include "shell_savegame.h"

#include "shell_boundary_probe.h"
#include "shell_factory.h"
#include "ss_string.h"

// GetPlayerState's answer, never NULL: two engine sites write through it
// unchecked, one on an edict with no entity. Orphan states are per edict, so the
// HLTV and Replay masters don't share one.
static CPlayerState s_OrphanPlayerStates[ABSOLUTE_PLAYER_LIMIT + 1];

CPlayerState *SourceSharp_PlayerStateForEdict( edict_t *pEdict )
{
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pEdict );

	if ( pPlayer != NULL )
	{
		SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Standalone );
		return pPlayer->PlayerStatePtr();
	}

	SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Null );

	int nIndex = ( pEdict != NULL ) ? engine->IndexOfEdict( pEdict ) : 0;
	if ( nIndex < 0 || nIndex > ABSOLUTE_PLAYER_LIMIT )
		nIndex = 0;
	return &s_OrphanPlayerStates[nIndex];
}

// Where this client's eyes are, asked of managed code at most once per frame;
// the block's stamp says whether this frame's answer is already held.
bool SourceSharp_ClientViewOrigin( edict_t *pClient, Vector *pOut )
{
	if ( pOut == NULL )
		return false;

	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pClient );

	if ( pPlayer == NULL )
		return false;

	EngineBlockServerTail_t &tail = pPlayer->Block().Server;

	if ( tail.ViewOriginFrame != gpGlobals->framecount )
	{
		float flEyes[3] = { 0.0f, 0.0f, 0.0f };

		// A miss writes nothing and answers no; a zeroed flEyes is the world
		// origin, a real position.
		if ( !SourceSharp_EarPositionForClient( engine->IndexOfEdict( pClient ), flEyes ) )
			return false;

		tail.ViewOrigin.Init( flEyes[0], flEyes[1], flEyes[2] );
		tail.ViewOriginFrame = gpGlobals->framecount;
	}

	*pOut = tail.ViewOrigin;
	return true;
}

namespace
{
CServerGameClients s_ServerGameClients;
}

// INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3 is compatible with the latest since we're only adding things to the end, so expose that as well.
// Answered by the factory (shell_factory.cpp), which asks for each by its row.
static void *SourceSharp_GetServerGameClients003()
{
	return (IServerGameClients003 *)&s_ServerGameClients;
}

const SourceSharpExposed kExposeServerGameClients003 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3,
														 SourceSharp_GetServerGameClients003 };

static void *SourceSharp_GetServerGameClients004()
{
	return (IServerGameClients004 *)&s_ServerGameClients;
}

const SourceSharpExposed kExposeServerGameClients004 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_4,
														 SourceSharp_GetServerGameClients004 };

static void *SourceSharp_GetServerGameClients()
{
	return (IServerGameClients *)&s_ServerGameClients;
}

const SourceSharpExposed kExposeServerGameClients = { INTERFACEVERSION_SERVERGAMECLIENTS,
													  SourceSharp_GetServerGameClients };

// Called when a player tries to connect; false refuses, with the reason in reject.
bool CServerGameClients::ClientConnect( edict_t *pEdict, const char *pszName, const char *pszAddress, char *reject,
										int maxrejectlen )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientConnect );

	if ( !g_pGameRules )
		return false;

	return g_pGameRules->ClientConnected( pEdict, pszName, pszAddress, reject, maxrejectlen );
}

// A client's net message to the server: registered on each client's channel when
// it goes active, and handed to managed code, which decides what it means. The
// channel lives for the connection, so a level change keeps the registration.
namespace
{

// Process's answer is the engine's: false drops the client. A message nobody
// claims, or no managed side, is not the client's fault.
bool HandClientMessageToManaged( CSourceSharpClientMessage &msg )
{
	if ( IManagedClients *pApi = ManagedClients() )
		pApi->OnClientNetMessage( msg.Owner(), msg.Key(), msg.Data(), msg.DataLength() );
	return true;
}

void RegisterClientMessage( int nEntIndex )
{
	// A bot has no channel.
	INetChannel *pChannel = nEntIndex > 0 ? static_cast<INetChannel *>( engine->GetPlayerNetInfo( nEntIndex ) ) : NULL;
	if ( !pChannel )
		return;

	// The channel keeps it (RegisterMessage stores the pointer), so it is on
	// the heap; a channel that has one already refuses a second.
	CSourceSharpClientMessage *pMsg = new CSourceSharpClientMessage( HandClientMessageToManaged );
	pMsg->SetOwner( nEntIndex );
	if ( !pChannel->RegisterMessage( pMsg ) )
		delete pMsg;
}
}

// Called when a player is fully active (ready to receive messages).
void CServerGameClients::ClientActive( edict_t *pEdict, bool bLoadGame )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientActive );

	MDLCACHE_CRITICAL_SECTION();

	// On a savegame load the engine skips ClientPutInServer and calls only
	// ClientActive, and no saved entity table recreates the player, so create it
	// here. Not fatal on failure: the map is still standing and playable.
	if ( ShellSave_LoadGameNeedsPlayer( bLoadGame, CStandalonePlayer::FromEdict( pEdict ) != NULL ) )
	{
		const int nIndex = pEdict ? engine->IndexOfEdict( pEdict ) : 0;
		const char *pszName = nIndex > 0 ? engine->GetClientConVarValue( nIndex, "name" ) : NULL;

		Msg( "[SourceSharp][save] ClientActive( bLoadGame=1 ) -- the engine skipped "
			 "ClientPutInServer, so no player exists on edict %d. Creating it here.\n",
			 nIndex );

		if ( !StandalonePlayer_Create( pEdict, pszName ? pszName : "" ) )
		{
			Warning( "[SourceSharp][save] the standalone player could NOT be created on "
					 "edict %d. This load has no player: the map is standing, but nothing "
					 "can be saved from it and the client has no viewpoint.\n",
					 nIndex );
		}
	}

	// Before the managed side hears of the client: from here on it may ask the
	// client something, and the answer comes back as this message.
	RegisterClientMessage( pEdict ? engine->IndexOfEdict( pEdict ) : 0 );

	::ClientActive( pEdict, bLoadGame );

	if ( gpGlobals->eLoadType != MapLoad_LoadGame )
	{
		// notify all entities that the player is now in the game
	}
}

void CServerGameClients::ClientSpawned( edict_t *pPlayer )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSpawned );

	if ( g_pGameRules )
	{
		g_pGameRules->ClientSpawned( pPlayer );
	}
}

// Called when a player disconnects from the server.
void CServerGameClients::ClientDisconnect( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientDisconnect );

	// Destroying the shell unhooks the edict without removing it (the engine owns
	// a client's edict). Managed must be told explicitly: this is the one removal
	// native starts, so it never passes through managed's removal path.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )
	{
		// Since this client isn't around to think any more, reset their sound.
		ss::SoundEnt_ResetClientSound( pStandalone->EntIndex() );

		// Before the delete: the announcement reads memory the delete frees.
		SourceSharp_AnnounceStandaloneRemoved( pStandalone );

		delete pStandalone;
		return;
	}
}

void CServerGameClients::ClientPutInServer( edict_t *pEntity, const char *playername )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientPutInServer );

	::ClientPutInServer( pEntity, playername );
}

void CServerGameClients::ClientCommand( edict_t *pEntity, const CCommand &args )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientCommand );

	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEntity ) )
	{
		StandalonePlayer_ClientCommand( pStandalone, args );
		return;
	}
}

// Called after the player changes userinfo, before the engine sends it on.
void CServerGameClients::ClientSettingsChanged( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSettingsChanged );

	// Is the client spawned yet?
	if ( !pEdict->GetUnknown() )
		return;

	// Managed fires player_changename and answers whether the player takes the
	// new name; this carries it out.
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pEdict );
	IManagedClients *pApi = ManagedClients();
	if ( !pPlayer || !pApi )
		return;

	const int nIndex = engine->IndexOfEdict( pEdict );
	if ( pApi->OnClientSettingsChanged( nIndex ) )
		pPlayer->SetPlayerName( engine->GetClientConVarValue( nIndex, "name" ) );
}

// Sets up the client's PVS from its view entity and eye position. Do not cache
// pvs: it is reusable engine memory, good for this frame only.
void CServerGameClients::ClientSetupVisibility( edict_t *pViewEntity, edict_t *pClient, unsigned char *pvs,
												int pvssize )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSetupVisibility );

	// Initialised: neither arm below writes it for a client with no player (the
	// HLTV and Replay masters), and it reaches every areaportal.
	Vector org = vec3_origin;

	// Reset the PVS!!!
	engine->ResetPVS( pvs, pvssize );

	// Find the client's PVS
	if ( pViewEntity )
	{
		// A standalone view entity (a point_viewcontrol) adds its origin too. The
		// player's own eye is still added, so the PVS is the union, never less than
		// stock's.
		if ( ss::GameEntityFromEntityHandle( pViewEntity->GetUnknown() ) )
		{
			org = pViewEntity->GetUnknown()->GetCollideable()->GetCollisionOrigin();
			engine->AddOriginToPVS( org );
		}
	}

	float fovDistanceAdjustFactor = 1;

	// No view origin for a client without a player, which keeps the HLTV and
	// Replay masters out of this arm.
	if ( SourceSharp_ClientViewOrigin( pClient, &org ) )
	{
		// Only when no view entity overrode the view, as CBasePlayer::SetupVisibility.
		if ( !pViewEntity )
			engine->AddOriginToPVS( org );
	}

	// The area portal walk is managed; it answers the engine for every portal. A
	// portal number too large for the bits is named by managed, raised here.
	unsigned char portalBits[MAX_AREA_PORTAL_STATE_BYTES];
	memset( portalBits, 0, sizeof( portalBits ) );

	const float flOrg[3] = { org.x, org.y, org.z };
	const int nTooLarge =
		SourceSharp_AreaPortalVisibility( flOrg, fovDistanceAdjustFactor, portalBits, sizeof( portalBits ) );
	if ( nTooLarge >= 0 )
		Error( "ClientSetupVisibility: portal number (%d) too large", nTooLarge );

	(void)portalBits;
}

// The most commands one packet can queue; must equal managed's
// PlayerCommandQueue.MaxQueuedPerPacket.
#define SOURCESHARP_MAX_QUEUED_USERCMDS ( 64 + 24 )

float CServerGameClients::ProcessUsercmds( edict_t *player, bf_read *buf, int numcmds, int totalcmds,
										   int dropped_packets, bool ignore, bool paused )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ProcessUsercmds );

	// Managed decodes the whole packet in one call and answers the commands to
	// queue, in run order, and where they end; native seeks past them.
	(void)paused;

	IManagedPlayerCommands *pCommands = ManagedPlayerCommands();
	if ( !pCommands )
	{
		// No decoder: refuse the packet, as an undecodable one is.
		Warning( "[SourceSharp] usercmds arrived with no managed decoder; packet refused\n" );
		buf->SetOverflowFlag();
		return 0.0f;
	}

	CStandalonePlayer *pStandalone = ignore ? NULL : CStandalonePlayer::FromEdict( player );

	SourceSharpDecodedUserCmd queued[SOURCESHARP_MAX_QUEUED_USERCMDS];
	int32_t nBitsRead = 0;
	const int nStart = buf->GetNumBitsRead();

	const int nQueued = pCommands->PlayerDecodeCommands(
		pStandalone ? pStandalone->ManagedHandle() : (uint32_t)INVALID_EHANDLE_INDEX, buf->GetBasePointer(), nStart,
		nStart + buf->GetNumBitsLeft(), numcmds, totalcmds, dropped_packets, pStandalone != NULL ? 1 : 0, queued,
		(int32_t)ARRAYSIZE( queued ), &nBitsRead );

	// Out-of-range counts, a truncated stream or a plan that didn't fit: the
	// engine drops the packet.
	if ( nQueued < 0 )
	{
		buf->SetOverflowFlag();
		return 0.0f;
	}

	buf->Seek( nBitsRead );

	if ( !pStandalone )
		return 0.0f;

	pStandalone->QueuedUsercmds( nQueued );
	return gpGlobals->interval_per_tick;
}

void CServerGameClients::PostClientMessagesSent_DEPRECIATED()
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_PostClientMessagesSent );
}

// Sets the client index for the client who typed the command into his/her console
void CServerGameClients::SetCommandClient( int index )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_SetCommandClient );

	UTIL_SetCommandClientIndex( index );
}

int CServerGameClients::GetReplayDelay( edict_t *pEdict, int &entity )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetReplayDelay );

	// No replay or HLTV: zero delay, and entity is never written. The method
	// must stay: the engine calls it by vtable position, and removing it would
	// shift every later slot.
	(void)pEdict;
	(void)entity;

	return 0;
}

void CServerGameClients::ClientEarPosition( edict_t *pEdict, Vector *pEarOrigin )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientEarPosition );

	// Which hierarchy answers, and in which order, is decided in managed code.
	// The answer is cached per client per frame, shared with ClientSetupVisibility.
	// The miss path (Assert, then vec3_origin) is stock's.
	if ( SourceSharp_ClientViewOrigin( pEdict, pEarOrigin ) )
		return;

	float flEars[3] = { 0.0f, 0.0f, 0.0f };
	if ( SourceSharp_EarPositionForClient( pEdict ? engine->IndexOfEdict( pEdict ) : 0, flEars ) )
	{
		pEarOrigin->Init( flEars[0], flEars[1], flEars[2] );
		return;
	}

	// Shouldn't happen
	Assert( 0 );
	*pEarOrigin = vec3_origin;
}

CPlayerState *CServerGameClients::GetPlayerState( edict_t *player )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetPlayerState );

	// May not answer NULL: the engine writes through it for the HLTV and Replay
	// masters before ClientPutInServer has run.
	return SourceSharp_PlayerStateForEdict( player );
}

// Anything this game .dll wants to add to the bug reporter text.
void CServerGameClients::GetBugReportInfo( char *buf, int buflen )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetBugReportInfo );

	buf[0] = 0;
}

// A player sent a voice packet.
void CServerGameClients::ClientVoice( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientVoice );

	NOTE_UNUSED( pEdict );
}

// A user has had their network id set up and validated.
void CServerGameClients::NetworkIDValidated( const char *pszUserName, const char *pszNetworkID )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_NetworkIDValidated );
}

// The client has submitted a keyvalues command
void CServerGameClients::ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientCommandKeyValues );

	if ( !pKeyValues )
		return;

	if ( g_pGameRules )
	{
		g_pGameRules->ClientCommandKeyValues( pEntity, pKeyValues );
	}
}
