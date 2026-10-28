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
struct CManagedHolders;

class CServerEntityContext
{
public:
	CServerEntityContext( IVEngineServer *pEngine, ISpatialPartition *pPartition, IVModelInfo *pModelInfo,
						  const CManagedHolders &managed, CServerPlayersDll &players, unsigned short &nWorldSerial )
		: m_pEngine( pEngine )
		, m_pPartition( pPartition )
		, m_pModelInfo( pModelInfo )
		, m_Managed( managed )
		, m_Players( players )
		, m_nWorldSerial( nWorldSerial )
	{
	}

	CServerEntityContext( const CServerEntityContext & ) = delete;
	CServerEntityContext &operator=( const CServerEntityContext & ) = delete;

	IVEngineServer *const m_pEngine;
	ISpatialPartition *const m_pPartition;
	IVModelInfo *const m_pModelInfo;

	// Read when called: a managed reload replaces the objects behind it.
	const CManagedHolders &m_Managed;

	CServerPlayersDll &m_Players;

	// The world's handle serial, kept across maps so no handle is reissued.
	unsigned short &m_nWorldSerial;

	// NULL between levels.
	CServerLevelState *m_pLevel = nullptr;
};
