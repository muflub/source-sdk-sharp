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

// The temp entity tables built from the managed plan. Leaked on purpose: the engine
// reads a SendTable for the life of the process.
enum
{
	SOURCESHARP_TE_MAX_TABLES = 64
};

struct SourceSharpTeBuilt_t
{
	SendProp *pProps;
	int nProps;
	SendTable *pTable;
	ServerClass *pClass; // NULL for a table registered without one
	const char *pClassName; // interned: what a managed reload asks for again
	uint32_t nShapeHash;
};

class CServerTempEnts
{
public:
	CServerTempEnts() = default;
	CServerTempEnts( const CServerTempEnts & ) = delete;
	CServerTempEnts &operator=( const CServerTempEnts & ) = delete;

	SourceSharpTeBuilt_t m_Built[SOURCESHARP_TE_MAX_TABLES] = {};
	int m_nBuilt = 0;
	bool m_bClosed = false;

	// Names the engine keeps by pointer. A std::set node never moves, so c_str() is stable.
	std::set<std::string> m_Names;
};

// One heap tier: an owning pointer whose object exists between Begin and End. Begin
// destroys a stale object first, so a tier is never reused. End clears the pointer
// before deleting. The destructor does not delete: DLLShutdown ends the tiers.
template <typename T>
class CStateTier
{
public:
	CStateTier() = default;
	CStateTier( const CStateTier & ) = delete;
	CStateTier &operator=( const CStateTier & ) = delete;

	T *Get() const { return m_p; }

	T &Begin()
	{
		End();
		m_p = new T();
		return *m_p;
	}

	void End()
	{
		T *p = m_p;
		m_p = nullptr;
		delete p;
	}

private:
	T *m_p = nullptr;
};

// The DLL tier: DLLInit to DLLShutdown.
// The standalone hierarchy's one ServerClass per tier, built once and handed to the
// engine's class list, which keeps pointers to them for the life of the DLL.
class CServerStandaloneTiers
{
public:
	CServerStandaloneTiers() = default;
	CServerStandaloneTiers( const CServerStandaloneTiers & ) = delete;
	CServerStandaloneTiers &operator=( const CServerStandaloneTiers & ) = delete;

	SendProp *m_pProps[SOURCESHARP_TIER_MAX] = {};
	SendTable *m_pTables[SOURCESHARP_TIER_MAX] = {};
	ServerClass *m_pClasses[SOURCESHARP_TIER_MAX] = {};
	bool m_bRegistered = false;
};

// The players' DLL-tier state: what survives a map change.
class CServerPlayersDll
{
public:
	CServerPlayersDll() = default;
	CServerPlayersDll( const CServerPlayersDll & ) = delete;
	CServerPlayersDll &operator=( const CServerPlayersDll & ) = delete;

	// The player's edict, in transit from ClientPutInServer to PostConstruct.
	edict_t *m_pPendingEdict = nullptr;

	// Where the engine-side player words sit in the net state, resolved per generation.
	SourceSharpPlayerFields_t m_Fields;

	// Shadows still in the solver, and those that outlived their environment, which is
	// recreated at every level change.
	int m_nShadowsLive = 0;
	int m_nShadowsOutlived = 0;
};

// Every live standalone entity of this map, and the removals managed code did not take
// (a shell never bound on the managed side), emptied after the frame's thinks.
class CServerEntityShells
{
public:
	CServerEntityShells() = default;
	CServerEntityShells( const CServerEntityShells & ) = delete;
	CServerEntityShells &operator=( const CServerEntityShells & ) = delete;

	std::vector<CStandaloneEntity *> m_Live;
	std::vector<CStandaloneEntity *> m_PendingDeletes;
};

// The players connected on this map, by entindex, asked once per packet and per snapshot.
class CServerPlayersLevel
{
public:
	CServerPlayersLevel() = default;
	CServerPlayersLevel( const CServerPlayersLevel & ) = delete;
	CServerPlayersLevel &operator=( const CServerPlayersLevel & ) = delete;

	CStandalonePlayer *m_pByIndex[ss::kMaxPlayers + 1] = {};
};

// Engine interfaces the server acquires in DLLInit (SourceSharp_AcquireEngineInterfaces)
// and reads through the accessors below; valid until DLLShutdown.
class CServerEngineInterfaces
{
public:
	IVEngineServer *m_pEngine = nullptr;
	IFileSystem *m_pFileSystem = nullptr;
	ISpatialPartition *m_pPartition = nullptr;
	IUploadGameStats *m_pGameStats = nullptr;
	INetworkStringTableContainer *m_pStringTables = nullptr;
	IVModelInfo *m_pModelInfo = nullptr;
	IStaticPropMgrServer *m_pStaticProps = nullptr;
	IEngineTrace *m_pTrace = nullptr;
	IMDLCache *m_pMdlCache = nullptr;
	IGameEventManager2 *m_pEvents = nullptr;
	IVDebugOverlay *m_pOverlay = nullptr;
	IUniformRandomStream *m_pRandom = nullptr;
	CSharedEdictChangeInfo *m_pChangeInfo = nullptr;
};

// The length of variant_t::ToString's answer buffer.
constexpr int kVariantBytes = 512;

class CServerDllState
{
public:
	CServerDllState() = default;
	CServerDllState( const CServerDllState & ) = delete;
	CServerDllState &operator=( const CServerDllState & ) = delete;

	// DLL tier: managed holds pointers into m_EntitySlots across a reload, and serials stay
	// monotone across maps so no handle is reissued. Managed unloads before this tier ends.
	CSourceSharpEntitySlots m_EntitySlots;
	CSourceSharpSlotTable m_SlotTable;

	// The published managed types the tier ServerClasses are built from.
	CSourceSharpTypeTable m_NetTypes;
	CSourceSharpPayloadPropNames m_PayloadPropNames;
	CServerStandaloneTiers m_StandaloneTiers;
	CWorldNetTable m_WorldNetTable;
	CServerPlayersDll m_Players;
	CServerEngineInterfaces m_Engine;
	std::unique_ptr<CServerDllServices> m_pServices;

	// The world's handle serial: not reset between maps, so a client cannot resolve
	// the previous map's world with the new handle.
	unsigned short m_nWorldSerial = 0;

	// scripts/sentences.txt is precached once for the DLL.
	bool m_bSentencesInit = false;

	// The engine's console client (IServerGameClients::SetCommandClient), a 0-based slot.
	int m_nCommandClientSlot = 0;

	// Handles from the engine-invisible range that reached native and did not resolve.
	int32_t m_nInvisibleRangeReach = 0;

	// variant_t::ToString's answer, overwritten by the next call as stock's static was.
	char m_szVariantString[kVariantBytes] = {};

	// Warn-once latches: an entity of an unpublished class, and the last game-entity
	// pointer a network handle was refused for.
	bool m_bWarnedUnpublishedClass = false;
	const void *m_pLastHandleComplaint = nullptr;

	// Built once at managed registration; a reload gets the existing tables back.
	CServerTempEnts m_TempEnts;

	// The two stock string tables native code adds to after creating them. The
	// engine creates tables before LevelInit, so they are not the level tier's.
	INetworkStringTable *m_pParticleEffectNames = nullptr;
	INetworkStringTable *m_pMaterials = nullptr;
};

// The level tier: LevelInit to LevelShutdown.
class CServerLevelState
{
public:
	CServerLevelState() = default;
	CServerLevelState( const CServerLevelState & ) = delete;
	CServerLevelState &operator=( const CServerLevelState & ) = delete;

	CServerOverlaps m_Overlaps;
	CServerPlayersLevel m_Players;
	CServerEntityShells m_Shells;

	// worldspawn, claimed at its construction; NULL until the map makes it.
	CWorld *m_pWorld = nullptr;

	// Warned once per map: an entity allocated a blob shorter than its published layout.
	bool m_bWarnedShortBlob = false;
};

// The tiers, NULL outside their windows. Defined in shell_gamedll.cpp.
CServerDllState *ShellGameDll_Dll();
CServerLevelState *ShellGameDll_Level();
