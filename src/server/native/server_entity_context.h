// What a server entity is handed when its factory makes it: the engine interfaces and the
// DLL-tier state its methods call, and the level it lives in. One object, owned by the
// DLL tier's services and outliving every entity; the game DLL binds the level for
// LevelInit..LevelShutdown, and no entity outlives its level.

#pragma once

class CServerLevelState;
class CServerPlayersDll;
class ISpatialPartition;
class IVEngineServer;
class IVModelInfo;
class IStaticPropMgrServer;
class IEngineTrace;
class CServerModelPrecache;
class CServerNetTables;
class CStandalonePlayer;
struct edict_t;
class CGlobalVars;
class CServerPhysics;
class IGameRules;
class INetworkStringTable;
struct CManagedHolders;

class CServerEntityContext
{
public:
	CServerEntityContext( IVEngineServer *pEngine, CGlobalVars *pGlobals, ISpatialPartition *pPartition,
						  IVModelInfo *pModelInfo, IStaticPropMgrServer *pStaticProps, IEngineTrace *pTrace,
						  const CManagedHolders &managed, const bool &bManagedLoaded,
						  const unsigned int &nManagedGeneration, CServerPlayersDll &players,
						  unsigned short &nWorldSerial, CServerModelPrecache &precache, CServerNetTables &netTables )
		: m_pEngine( pEngine )
		, m_pGlobals( pGlobals )
		, m_pPartition( pPartition )
		, m_pModelInfo( pModelInfo )
		, m_pStaticProps( pStaticProps )
		, m_pTrace( pTrace )
		, m_Managed( managed )
		, m_bManagedLoaded( bManagedLoaded )
		, m_nManagedGeneration( nManagedGeneration )
		, m_Players( players )
		, m_nWorldSerial( nWorldSerial )
		, m_Precache( precache )
		, m_NetTables( netTables )
	{
	}

	CServerEntityContext( const CServerEntityContext & ) = delete;
	CServerEntityContext &operator=( const CServerEntityContext & ) = delete;

	// This map's player at an entindex (1-based) or edict; NULL outside a level, for an
	// empty slot and for an edict that is not a player's (shell_standalone_player.cpp).
	CStandalonePlayer *PlayerFromIndex( int nEntIndex ) const;
	CStandalonePlayer *PlayerFromEdict( const edict_t *pEdict ) const;

	// The player who issued the console command being run, or NULL for the console.
	CStandalonePlayer *CommandClient() const;

	// Whether the command being run came from the listen server's host or the dedicated
	// server's console (shell_commandclient.cpp).
	bool CommandIssuedByServerAdmin() const;

	IVEngineServer *const m_pEngine;

	// The engine's globals, which it writes every frame.
	CGlobalVars *const m_pGlobals;
	ISpatialPartition *const m_pPartition;
	IVModelInfo *const m_pModelInfo;
	IStaticPropMgrServer *const m_pStaticProps;
	IEngineTrace *const m_pTrace;

	// Read when called: a managed reload replaces the objects behind it.
	const CManagedHolders &m_Managed;

	// False while the managed side is down, a reload included.
	const bool &m_bManagedLoaded;

	// The managed side's reload generation: field offsets resolved under another are stale.
	const unsigned int &m_nManagedGeneration;

	CServerPlayersDll &m_Players;

	// The world's handle serial, kept across maps so no handle is reissued.
	unsigned short &m_nWorldSerial;

	// Where an entity precaches the model it is given.
	CServerModelPrecache &m_Precache;

	// The published types and the ServerClasses an entity is networked through.
	CServerNetTables &m_NetTables;

	// NULL between levels.
	CServerLevelState *m_pLevel = nullptr;

	// The DLL tier's physics, bound by the services that own both.
	CServerPhysics *m_pPhysics = nullptr;

	// The rules' string table, bound by the game DLL when it creates the tables.
	INetworkStringTable *m_pGameRulesTable = nullptr;

	// The level's rules, which its world owns; NULL between levels (world_adapter.cpp).
	IGameRules *GameRules() const;
};
