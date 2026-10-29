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

void UTIL_SetClientVisibilityPVS(edict_t* pClient, const unsigned char* pvs, int pvssize);

#include "managed_convar_ref.h"

#include "shell_server.h"

#include "shell_standalone_player.h"
#include "shell_entity_lifecycle.h"
#include "server_entity_context.h"
#include "imanaged_areas.h"
#include "inetchannel.h"
#include "ss_client_message.h"

#include "shell_savegame.h"

#include "shell_boundary_probe.h"
#include "shell_factory.h"
#include "ss_string.h"
#include "shell_managed_areas.h"
#include "shell_diagnostics.h"
#include "ss_shareddefs.h"
#include "server_state.h"

CPlayerState* CServerGameClients::PlayerStateForEdict(edict_t* pEdict)
{
	CStandalonePlayer* pPlayer = m_Entities.PlayerFromEdict(pEdict);

	if (pPlayer != NULL)
	{
		SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_GetPlayerState_Standalone);
		return pPlayer->PlayerStatePtr();
	}

	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_GetPlayerState_Null);

	int nIndex = (pEdict != NULL) ? m_pEngine->IndexOfEdict(pEdict) : 0;
	if (nIndex < 0 || nIndex > ABSOLUTE_PLAYER_LIMIT)
		nIndex = 0;
	return &m_OrphanPlayerStates[nIndex];
}

// The block's stamp says whether this frame's answer is already held.
bool CServerGameClients::ClientViewOrigin(edict_t* pClient, Vector* pOut)
{
	if (pOut == NULL)
		return false;

	CStandalonePlayer* pPlayer = m_Entities.PlayerFromEdict(pClient);

	if (pPlayer == NULL)
		return false;

	EngineBlockServerTail_t& tail = pPlayer->Block().Server;

	if (tail.ViewOriginFrame != m_pGlobals->framecount)
	{
		float flEyes[3] = {0.0f, 0.0f, 0.0f};

		// A miss writes nothing and answers no; a zeroed flEyes is the world
		// origin, a real position.
		if (!EarPositionForClient(m_pEngine->IndexOfEdict(pClient), flEyes))
			return false;

		tail.ViewOrigin.Init(flEyes[0], flEyes[1], flEyes[2]);
		tail.ViewOriginFrame = m_pGlobals->framecount;
	}

	*pOut = tail.ViewOrigin;
	return true;
}

// Called when a player tries to connect; false refuses, with the reason in reject.
bool CServerGameClients::ClientConnect(edict_t* pEdict, const char* pszName, const char* pszAddress, char* reject,
									   int maxrejectlen)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientConnect);

	IGameRules* pRules = m_Entities.GameRules();
	if (!pRules)
		return false;

	return pRules->ClientConnected(pEdict, pszName, pszAddress, reject, maxrejectlen);
}

// A client's net message to the server is registered on each client's channel when
// it goes active. The channel lives for the connection, so a level change keeps the
// registration.
bool CServerGameClients::OnClientMessage(CSourceSharpClientMessage& msg)
{
	if (IManagedClients* pApi = m_Managed.m_pClients)
		pApi->OnClientNetMessage(msg.Owner(), msg.Key(), msg.Data(), msg.DataLength());
	return true;
}

void CServerGameClients::RegisterClientMessage(int nEntIndex)
{
	// A bot has no channel.
	INetChannel* pChannel = nEntIndex > 0 ? static_cast<INetChannel*>(m_pEngine->GetPlayerNetInfo(nEntIndex)) : NULL;
	if (!pChannel)
		return;

	// The channel keeps it (RegisterMessage stores the pointer), so it is on
	// the heap; a channel that has one already refuses a second.
	CSourceSharpClientMessage* pMsg = new CSourceSharpClientMessage(this);
	pMsg->SetOwner(nEntIndex);
	if (!pChannel->RegisterMessage(pMsg))
		delete pMsg;
}

// Called when a player is fully active (ready to receive messages).
void CServerGameClients::ClientActive(edict_t* pEdict, bool bLoadGame)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientActive);

	MDLCACHE_CRITICAL_SECTION_(m_pMdlCache);

	// On a savegame load the engine skips ClientPutInServer and calls only
	// ClientActive, and no saved entity table recreates the player, so create it
	// here. Not fatal on failure: the map is still standing and playable.
	if (ShellSave_LoadGameNeedsPlayer(bLoadGame, m_Entities.PlayerFromEdict(pEdict) != NULL))
	{
		const int nIndex = pEdict ? m_pEngine->IndexOfEdict(pEdict) : 0;
		const char* pszName = nIndex > 0 ? m_pEngine->GetClientConVarValue(nIndex, "name") : NULL;

		Msg("[SourceSharp][save] ClientActive( bLoadGame=1 ) -- the engine skipped "
			"ClientPutInServer, so no player exists on edict %d. Creating it here.\n",
			nIndex);

		if (!m_Players.CreatePlayer(pEdict, pszName ? pszName : ""))
		{
			Warning("[SourceSharp][save] the standalone player could NOT be created on "
					"edict %d. This load has no player: the map is standing, but nothing "
					"can be saved from it and the client has no viewpoint.\n",
					nIndex);
		}
	}

	// Before the managed side hears of the client: from here on it may ask the
	// client something, and the answer comes back as this message.
	RegisterClientMessage(pEdict ? m_pEngine->IndexOfEdict(pEdict) : 0);

	// Spawns when the client is fully connected, as stock's player does.
	if (CStandalonePlayer* pStandalone = m_Entities.PlayerFromEdict(pEdict))
		pStandalone->Spawn();

	if (IManagedClients* pApi = ManagedClientsIfLoaded())
		pApi->OnClientActive(pEdict ? pEdict->m_EdictIndex : -1, bLoadGame ? 1 : 0);

	if (m_pGlobals->eLoadType != MapLoad_LoadGame)
	{
		// notify all entities that the player is now in the game
	}
}

void CServerGameClients::ClientSpawned(edict_t* pPlayer)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientSpawned);

	if (IGameRules* pRules = m_Entities.GameRules())
	{
		pRules->ClientSpawned(pPlayer);
	}
}

// Called when a player disconnects from the server.
void CServerGameClients::ClientDisconnect(edict_t* pEdict)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientDisconnect);

	// Destroying the shell unhooks the edict without removing it (the engine owns
	// a client's edict). Managed must be told explicitly: this is the one removal
	// native starts, so it never passes through managed's removal path.
	if (CStandalonePlayer* pStandalone = m_Entities.PlayerFromEdict(pEdict))
	{
		// Since this client isn't around to think any more, reset their sound.
		ss::SoundEnt_ResetClientSound(m_Managed.m_pSoundEnt, pStandalone->EntIndex());

		// Before the delete: the announcement reads memory the delete frees.
		SourceSharp_AnnounceStandaloneRemoved(pStandalone);

		delete pStandalone;
		return;
	}
}

void CServerGameClients::ClientPutInServer(edict_t* pEntity, const char* playername)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientPutInServer);

	// The engine hands us an edict and expects the mod to attach a player entity to it.
	// Create fails only if managed registered no player class. Fail loudly: a client
	// admitted with no player entity dies somewhere unrelated later.
	if (!m_Players.CreatePlayer(pEntity, playername))
	{
		Error("[SourceSharp] ClientPutInServer: managed side registered no player class — cannot put '%s' in server\n",
			  playername);
		return;
	}

	if (IManagedClients* pApi = ManagedClientsIfLoaded())
		pApi->OnClientPutInServer(pEntity ? pEntity->m_EdictIndex : -1, playername);
}

void CServerGameClients::ClientCommand(edict_t* pEntity, const CCommand& args)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientCommand);

	if (CStandalonePlayer* pStandalone = m_Entities.PlayerFromEdict(pEntity))
	{
		StandalonePlayer_ClientCommand(pStandalone, args);
		return;
	}
}

// Called after the player changes userinfo, before the engine sends it on.
void CServerGameClients::ClientSettingsChanged(edict_t* pEdict)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientSettingsChanged);

	// Is the client spawned yet?
	if (!pEdict->GetUnknown())
		return;

	// Managed fires player_changename and answers whether the player takes the
	// new name; this carries it out.
	CStandalonePlayer* pPlayer = m_Entities.PlayerFromEdict(pEdict);
	IManagedClients* pApi = m_Managed.m_pClients;
	if (!pPlayer || !pApi)
		return;

	const int nIndex = m_pEngine->IndexOfEdict(pEdict);
	if (pApi->OnClientSettingsChanged(nIndex))
		pPlayer->SetPlayerName(m_pEngine->GetClientConVarValue(nIndex, "name"));
}

// Sets up the client's PVS from its view entity and eye position. Do not cache
// pvs: it is reusable engine memory, good for this frame only.
void CServerGameClients::ClientSetupVisibility(edict_t* pViewEntity, edict_t* pClient, unsigned char* pvs, int pvssize)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientSetupVisibility);

	// Initialised: neither arm below writes it for a client with no player (the
	// HLTV and Replay masters), and it reaches every areaportal.
	Vector org = vec3_origin;

	// Reset the PVS!!!
	m_pEngine->ResetPVS(pvs, pvssize);

	// Find the client's PVS
	if (pViewEntity)
	{
		// A standalone view entity (a point_viewcontrol) adds its origin too. The
		// player's own eye is still added, so the PVS is the union, never less than
		// stock's.
		if (ss::GameEntityFromServerEntity(pViewEntity->GetUnknown()))
		{
			org = pViewEntity->GetUnknown()->GetCollideable()->GetCollisionOrigin();
			m_pEngine->AddOriginToPVS(org);
		}
	}

	float fovDistanceAdjustFactor = 1;

	// No view origin for a client without a player, which keeps the HLTV and
	// Replay masters out of this arm.
	if (ClientViewOrigin(pClient, &org))
	{
		// Only when no view entity overrode the view, as CBasePlayer::SetupVisibility.
		if (!pViewEntity)
			m_pEngine->AddOriginToPVS(org);
	}

	// The area portal walk is managed; it answers the engine for every portal. A
	// portal number too large for the bits is named by managed, raised here.
	unsigned char portalBits[MAX_AREA_PORTAL_STATE_BYTES];
	memset(portalBits, 0, sizeof(portalBits));

	const float flOrg[3] = {org.x, org.y, org.z};
	const int nTooLarge = AreaPortalVisibility(flOrg, fovDistanceAdjustFactor, portalBits, sizeof(portalBits));
	if (nTooLarge >= 0)
		Error("ClientSetupVisibility: portal number (%d) too large", nTooLarge);

	(void)portalBits;
}

// The most commands one packet can queue; must equal managed's
// PlayerCommandQueue.MaxQueuedPerPacket.
#define SOURCESHARP_MAX_QUEUED_USERCMDS (64 + 24)

float CServerGameClients::ProcessUsercmds(edict_t* player, bf_read* buf, int numcmds, int totalcmds,
										  int dropped_packets, bool ignore, bool paused)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ProcessUsercmds);

	// Managed decodes the whole packet in one call and answers the commands to
	// queue, in run order, and where they end; native seeks past them.
	(void)paused;

	IManagedPlayerCommands* pCommands = m_Managed.m_pPlayerCommands;
	if (!pCommands)
	{
		// No decoder: refuse the packet, as an undecodable one is.
		Warning("[SourceSharp] usercmds arrived with no managed decoder; packet refused\n");
		buf->SetOverflowFlag();
		return 0.0f;
	}

	CStandalonePlayer* pStandalone = ignore ? NULL : m_Entities.PlayerFromEdict(player);

	SourceSharpDecodedUserCmd queued[SOURCESHARP_MAX_QUEUED_USERCMDS];
	int32_t nBitsRead = 0;
	const int nStart = buf->GetNumBitsRead();

	const int nQueued = pCommands->PlayerDecodeCommands(
		pStandalone ? pStandalone->ManagedHandle() : (uint32_t)INVALID_EHANDLE_INDEX, buf->GetBasePointer(), nStart,
		nStart + buf->GetNumBitsLeft(), numcmds, totalcmds, dropped_packets, pStandalone != NULL ? 1 : 0, queued,
		(int32_t)ARRAYSIZE(queued), &nBitsRead);

	// Out-of-range counts, a truncated stream or a plan that didn't fit: the
	// engine drops the packet.
	if (nQueued < 0)
	{
		buf->SetOverflowFlag();
		return 0.0f;
	}

	buf->Seek(nBitsRead);

	if (!pStandalone)
		return 0.0f;

	pStandalone->QueuedUsercmds(nQueued);
	return m_pGlobals->interval_per_tick;
}

void CServerGameClients::PostClientMessagesSent_DEPRECIATED()
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_PostClientMessagesSent);
}

// Sets the client index for the client who typed the command into his/her console
void CServerGameClients::SetCommandClient(int index)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_SetCommandClient);

	m_Entities.m_Players.m_nCommandClientSlot = index;
}

int CServerGameClients::GetReplayDelay(edict_t* pEdict, int& entity)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_GetReplayDelay);

	// No replay or HLTV: zero delay, and entity is never written. The method
	// must stay: the engine calls it by vtable position, and removing it would
	// shift every later slot.
	(void)pEdict;
	(void)entity;

	return 0;
}

void CServerGameClients::ClientEarPosition(edict_t* pEdict, Vector* pEarOrigin)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientEarPosition);

	// Which hierarchy answers, and in which order, is decided in managed code.
	// The answer is cached per client per frame, shared with ClientSetupVisibility.
	// The miss path (Assert, then vec3_origin) is stock's.
	if (ClientViewOrigin(pEdict, pEarOrigin))
		return;

	float flEars[3] = {0.0f, 0.0f, 0.0f};
	if (EarPositionForClient(pEdict ? m_pEngine->IndexOfEdict(pEdict) : 0, flEars))
	{
		pEarOrigin->Init(flEars[0], flEars[1], flEars[2]);
		return;
	}

	// Shouldn't happen
	Assert(0);
	*pEarOrigin = vec3_origin;
}

CPlayerState* CServerGameClients::GetPlayerState(edict_t* player)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_GetPlayerState);

	// May not answer NULL: the engine writes through it for the HLTV and Replay
	// masters before ClientPutInServer has run.
	return PlayerStateForEdict(player);
}

// Anything this game .dll wants to add to the bug reporter text.
void CServerGameClients::GetBugReportInfo(char* buf, int buflen)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_GetBugReportInfo);

	buf[0] = 0;
}

// A player sent a voice packet.
void CServerGameClients::ClientVoice(edict_t* pEdict)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientVoice);

	NOTE_UNUSED(pEdict);
}

// A user has had their network id set up and validated.
void CServerGameClients::NetworkIDValidated(const char* pszUserName, const char* pszNetworkID)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_NetworkIDValidated);
}

// The client has submitted a keyvalues command
void CServerGameClients::ClientCommandKeyValues(edict_t* pEntity, KeyValues* pKeyValues)
{
	SS_BOUNDARY_HIT(m_Probes, kBoundaryProbe_Clients_ClientCommandKeyValues);

	if (!pKeyValues)
		return;

	if (IGameRules* pRules = m_Entities.GameRules())
	{
		pRules->ClientCommandKeyValues(pEntity, pKeyValues);
	}
}

// Stock's limits, the same whether the engine asks the root before DLLInit or the
// DLL tier after it.
static void ServerPlayerLimits(int& minplayers, int& maxplayers, int& defaultMaxPlayers)
{
	minplayers = 1;
	maxplayers = ss::kMaxPlayers; // shareddefs.h's MAX_PLAYERS
	defaultMaxPlayers = 16;
}

void CServerGameClients::GetPlayerLimits(int& minplayers, int& maxplayers, int& defaultMaxPlayers) const
{
	SOURCESHARP_TRACE();
	ServerPlayerLimits(minplayers, maxplayers, defaultMaxPlayers);
}

void CRootServerGameClients::GetPlayerLimits(int& minplayers, int& maxplayers, int& defaultMaxPlayers) const
{
	ServerPlayerLimits(minplayers, maxplayers, defaultMaxPlayers);
}

IManagedClients* CServerGameClients::ManagedClientsIfLoaded() const
{
	return m_bManagedLoaded ? m_Managed.m_pClients : NULL;
}

int CServerGameClients::EarPositionForClient(int iClientEdict, float* pOutXYZ) const
{
	if (pOutXYZ == NULL)
		return 0;

	IManagedClients* pApi = ManagedClientsIfLoaded();
	if (!pApi)
		return 0;

	float flEars[3] = {0.0f, 0.0f, 0.0f};
	const int32_t nFound = pApi->ServerEarPositionForClient(iClientEdict, flEars);

	// Written only on a hit: the world origin is a real position.
	if (nFound)
	{
		pOutXYZ[0] = flEars[0];
		pOutXYZ[1] = flEars[1];
		pOutXYZ[2] = flEars[2];
	}

	return nFound ? 1 : 0;
}

int CServerGameClients::AreaPortalVisibility(const float* pOriginXYZ, float flFovDistanceAdjustFactor,
											 unsigned char* pPortalBits, int nPortalBytes) const
{
	IManagedClients* pApi = ManagedClientsIfLoaded();
	if (!pApi)
		return -1;

	return pApi->ServerAreaPortalVisibility(pOriginXYZ, flFovDistanceAdjustFactor, pPortalBits, nPortalBytes);
}
