//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimLevel: the native steps of LevelInit, ServerActivate and LevelShutdown, one step
// per method. Managed owns their order and every condition.

#pragma once

#include "ishim_level.h"

class CGlobalVars;
class CServerEntityContext;
class IBaseFileSystem;
class IMDLCache;
class IVEngineServer;
class CServerModelPrecache;
class CServerSystem;

namespace ss
{
class IEntityFactoryDictionary;
}

class CServerLevel final : public IShimLevel
{
public:
	CServerLevel(IVEngineServer* pEngine, IBaseFileSystem* pFileSystem, IMDLCache* pMdlCache, CGlobalVars* pGlobals,
				 CServerEntityContext& entities, ss::IEntityFactoryDictionary& factories,
				 CServerModelPrecache& precache, CServerSystem& physics)
		: m_pEngine(pEngine)
		, m_pFileSystem(pFileSystem)
		, m_pMdlCache(pMdlCache)
		, m_pGlobals(pGlobals)
		, m_Entities(entities)
		, m_Factories(factories)
		, m_Precache(precache)
		, m_Physics(physics)
	{
	}

	CServerLevel(const CServerLevel&) = delete;
	CServerLevel& operator=(const CServerLevel&) = delete;

	// Empty, but still called: its position in managed's LevelInit marks two orderings
	// (low violence before the template precache, templates after the entities they read).
	void PrecachePointTemplates(const char* pMapDataBlobs, const int32_t* pStringSizes, int32_t nCount) override;

	// Empty, and still called for its position. Managed decides low violence.
	void SetLowViolence(const char* pMapName) override;

	// Empty, and still called for its position.
	void LoadMessageOfTheDay() override;

	void SetPrecacheAllowed(int32_t bAllowed) override;

	// The standalone hierarchy's teardown, which gEntList cannot see. Runs pre-entity,
	// while everything the hierarchy holds is still real.
	void ShutdownStandaloneEntities() override;

	// Stock's: "YWB: This entity pointer is going away now and is corrupting memory on
	// level transitions/restarts".
	void ShutdownSoundEnt() override;

	// Releases the world through its networkable. The world is not in the entity list,
	// so nothing else frees it, and the next map's CWorldFactory::Create needs it gone.
	void ClearEntityList() override;

	// Starts the engine's load-game read (stock's LevelInit loadGame arm). Managed
	// decides what a false answer means.
	int32_t LoadSaveGameState(const char* pMapName, int32_t bCreatePlayers) override;

	// The file's size on that search path, or -1 when it cannot be opened.
	int32_t FileSizeOnPath(const char* pPath, const char* pPathId) override;

	// The bytes read, or -1 when the file cannot be opened or does not fit.
	int32_t ReadFileOnPath(const char* pPath, const char* pPathId, void* pOut, int32_t nOutSize) override;

	// Builds the world from keys managed parsed (MapEntity_SpawnWorld).
	int32_t SpawnWorld(const char* const* ppKeys, const char* const* ppValues, int32_t nKeys) override;

	int32_t IsPrecacheAllowed() override;

	// The level's load type, chosen by managed.
	void SetLoadType(int32_t eLoadType) override;

	// The two native systems' stages, each under the model-cache lock stock's game-system
	// walk takes per system. Managed owns the order.
	int32_t PhysicsInit() override;
	void PhysicsLevelInitPreEntity() override;
	void PhysicsLevelInitPostEntity() override;
	void PhysicsLevelShutdownPreEntity() override;
	void PhysicsLevelShutdownPostEntity() override;
	void StringPoolLevelShutdownPostEntity() override;

	// After the managed level's end, since a state object may write during its entity's
	// teardown.
	void ResetEntitySlots() override;

private:
	IVEngineServer* m_pEngine;
	IBaseFileSystem* m_pFileSystem;
	IMDLCache* m_pMdlCache;
	CGlobalVars* m_pGlobals;
	CServerEntityContext& m_Entities;

	// What SpawnWorld creates the world through.
	ss::IEntityFactoryDictionary& m_Factories;

	// Holds the precache window SetPrecacheAllowed opens and closes.
	CServerModelPrecache& m_Precache;

	// The physics game system (CServerPhysics), whose level steps these run.
	CServerSystem& m_Physics;
};
