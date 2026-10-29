//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The game DLL's two heap tiers, hanging off the one static root in shell_gamedll.cpp:
//
//   m_pDll    CServerDllState     new in DLLInit,   delete in DLLShutdown
//   m_pLevel  CServerLevelState   new in LevelInit, delete in LevelShutdown
//
// Heap rather than by-value members so construction happens after the engine is up,
// and so a level-tier field cannot leak into the next map.

#pragma once

#include "entity_slots.h"
#include "slot_table.h"
#include "generic_nettable.h"
#include "generic_payload.h"
#include "server_overlaps.h"
#include "world_nettable.h"
#include "standalone_player_sync.h"
#include "ss_shareddefs.h"
#include "server_engine_interfaces.h"
#include "server_services.h"

#include <memory>
#include <set>
#include <vector>
#include <string>

class CSharedEdictChangeInfo;
class CStandalonePlayer;
class CWorld;
class IEngineTrace;
class IFileSystem;
class IGameEventManager2;
class IUniformRandomStream;
class IVDebugOverlay;
class IMDLCache;
class IStaticPropMgrServer;
class IVModelInfo;
class IVEngineServer;
class INetworkStringTable;
class INetworkStringTableContainer;
class ISpatialPartition;
class IUploadGameStats;
struct edict_t;
class SendProp;
class SendTable;
class ServerClass;

// One heap tier: an owning pointer whose object exists between Begin and End. Begin
// destroys a stale object first, so a tier is never reused. End clears the pointer
// before deleting. The destructor does not delete: DLLShutdown ends the tiers.
template <typename T>
class CStateTier
{
public:
	CStateTier() = default;
	CStateTier(const CStateTier&) = delete;
	CStateTier& operator=(const CStateTier&) = delete;

	T* Get() const { return m_p; }

	T& Begin()
	{
		End();
		m_p = new T();
		return *m_p;
	}

	void End()
	{
		T* p = m_p;
		m_p = nullptr;
		delete p;
	}

private:
	T* m_p = nullptr;
};

// The DLL tier: DLLInit to DLLShutdown.
// The standalone hierarchy's one ServerClass per tier, built once and handed to the
// engine's class list, which keeps pointers to them for the life of the DLL.
class CServerStandaloneTiers
{
public:
	CServerStandaloneTiers() = default;
	CServerStandaloneTiers(const CServerStandaloneTiers&) = delete;
	CServerStandaloneTiers& operator=(const CServerStandaloneTiers&) = delete;

	SendProp* m_pProps[SOURCESHARP_TIER_MAX] = {};
	SendTable* m_pTables[SOURCESHARP_TIER_MAX] = {};
	ServerClass* m_pClasses[SOURCESHARP_TIER_MAX] = {};
	bool m_bRegistered = false;
};

// What the server networks managed types with: the published types, the tier
// ServerClasses built from them, worldspawn's table and the payload's prop names. DLL
// tier: the engine's class list keeps pointers into it for the life of the DLL.
class CServerNetTables
{
public:
	CServerNetTables() = default;
	CServerNetTables(const CServerNetTables&) = delete;
	CServerNetTables& operator=(const CServerNetTables&) = delete;

	CSourceSharpTypeTable m_Types;
	CSourceSharpPayloadPropNames m_PropNames;
	CServerStandaloneTiers m_Tiers;
	CWorldNetTable m_World;

	// Warned once: an entity of a class nobody published.
	bool m_bWarnedUnpublishedClass = false;
};

// The players' DLL-tier state: what survives a map change.
class CServerPlayersDll
{
public:
	CServerPlayersDll() = default;
	CServerPlayersDll(const CServerPlayersDll&) = delete;
	CServerPlayersDll& operator=(const CServerPlayersDll&) = delete;

	// The player's edict, in transit from ClientPutInServer to PostConstruct.
	edict_t* m_pPendingEdict = nullptr;

	// Where the engine-side player words sit in the net state, resolved per generation.
	SourceSharpPlayerFields_t m_Fields;

	// Shadows still in the solver, and those that outlived their environment, which is
	// recreated at every level change.
	int m_nShadowsLive = 0;
	int m_nShadowsOutlived = 0;

	// The engine's console client (IServerGameClients::SetCommandClient), a 0-based slot;
	// -1 for the dedicated console. The initial 0 is stock's.
	int m_nCommandClientSlot = 0;
};

// Every live standalone entity of this map, and the removals managed code did not take
// (a shell never bound on the managed side), emptied after the frame's thinks.
class CServerEntityShells
{
public:
	CServerEntityShells() = default;
	CServerEntityShells(const CServerEntityShells&) = delete;
	CServerEntityShells& operator=(const CServerEntityShells&) = delete;

	std::vector<CStandaloneEntity*> m_Live;
	std::vector<CStandaloneEntity*> m_PendingDeletes;
};

// The players connected on this map, by entindex, asked once per packet and per snapshot.
class CServerPlayersLevel
{
public:
	CServerPlayersLevel() = default;
	CServerPlayersLevel(const CServerPlayersLevel&) = delete;
	CServerPlayersLevel& operator=(const CServerPlayersLevel&) = delete;

	CStandalonePlayer* m_pByIndex[ss::kMaxPlayers + 1] = {};
};

class CServerDllState
{
public:
	CServerDllState() = default;
	CServerDllState(const CServerDllState&) = delete;
	CServerDllState& operator=(const CServerDllState&) = delete;

	// DLL tier: managed holds pointers into m_EntitySlots across a reload, and serials stay
	// monotone across maps so no handle is reissued. Managed unloads before this tier ends.
	CSourceSharpEntitySlots m_EntitySlots;
	CSourceSharpSlotTable m_SlotTable;

	CServerNetTables m_NetTables;
	CServerPlayersDll m_Players;
	CServerEngineInterfaces m_Engine;
	std::unique_ptr<CServerDllServices> m_pServices;

	// The world's handle serial: not reset between maps, so a client cannot resolve
	// the previous map's world with the new handle.
	unsigned short m_nWorldSerial = 0;

	// Handles from the engine-invisible range that reached native and did not resolve.
	int32_t m_nInvisibleRangeReach = 0;
};

// The level tier: LevelInit to LevelShutdown.
class CServerLevelState
{
public:
	CServerLevelState() = default;
	CServerLevelState(const CServerLevelState&) = delete;
	CServerLevelState& operator=(const CServerLevelState&) = delete;

	CServerOverlaps m_Overlaps;
	CServerPlayersLevel m_Players;
	CServerEntityShells m_Shells;

	// worldspawn, claimed at its construction; NULL until the map makes it.
	CWorld* m_pWorld = nullptr;

	// Warned once per map: an entity allocated a blob shorter than its published layout.
	bool m_bWarnedShortBlob = false;
};
