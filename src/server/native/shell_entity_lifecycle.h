//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimEntityLifecycle: creating, spawning and removing the server's entities, the
// entity factory dictionary every creation goes through, and the classes managed code
// declares. One object, the DLL tier's; its slot bodies live beside the code they use.

#pragma once

#include "ishim_areas.h"

#include <memory>

class CServerEntityContext;
class CStandalonePlayer;
struct edict_t;

namespace ss
{
class IEntityFactoryDictionary;
}

// Makes the player entity for a client the engine puts in the server.
class IServerPlayerMaker
{
public:
	virtual CStandalonePlayer* CreatePlayer(edict_t* pEdict, const char* pPlayerName) = 0;
};

class CServerEntityLifecycle final : public IShimEntityLifecycle, public IServerPlayerMaker
{
public:
	explicit CServerEntityLifecycle(CServerEntityContext& entities);

	CServerEntityLifecycle(const CServerEntityLifecycle&) = delete;
	CServerEntityLifecycle& operator=(const CServerEntityLifecycle&) = delete;

	uint32_t CreateEntity(const char* pClassName) override;

	// Spawns what CreateEntity made. Keyvalues and position are set between the two.
	void EntitySpawn(uint32_t hEntity) override;
	void EntityRemove(uint32_t hEntity) override;
	void DestroyShell(const uint32_t* pHandles, int32_t nCount) override;

	// The managed map spawner's creation step: creates the entity and hands every key to
	// KeyValueShellOnly, which keeps the ones the shell needs. pPackedKeys is
	// key\0value\0..., nBytes long (map text holds no NUL).
	uint32_t MapSpawnCreate(const char* pClassName, const char* pPackedKeys, int32_t nBytes) override;

	// Declares an entity class; called once per [ScriptedEntity] at managed startup.
	void RegisterScriptedEntity(const char* pClassName, const char** ppOutputNames, int32_t nOutputCount,
								int32_t nKind) override;

	// The override mask. A mask for an unregistered name is ordinary and ignored.
	void RegisterClassOps(const char* pClassName, uint32_t nOpMask) override;

	int32_t EntityDispatch(uint32_t hEntity, int32_t nOp, const SourceSharpOpArgs* pArgs) override;

	// Enumeration, for the console dumps and the managed side.
	int32_t EntityFactoryCount() override;
	const char* EntityFactoryNameAt(int32_t index) override;
	int32_t EntityFactoryRefusalCount() override;
	const char* EntityFactoryRefusalNameAt(int32_t index) override;
	int32_t EntityFactoryRefusalTimesAt(int32_t index) override;
	size_t EntityFactorySizeAt(int32_t index);

	int32_t TemplateSpawnKeyed(const char* pClassNames, const char* pPackedKeys, const int32_t* pPackBytes,
							   const float* pOriginsXYZ, const float* pAnglesPYR, int32_t nCount, uint32_t* pOutHandles,
							   int32_t nMax) override;

	// From ClientPutInServer, and from ClientActive on a load the engine skipped it for: the
	// player through the registered "player" factory, bound to the engine's edict. NULL,
	// with a warning, when the class was never registered.
	CStandalonePlayer* CreatePlayer(edict_t* pEdict, const char* pPlayerName) override;

	// Every creation by classname goes through it: the map parse and CreateEntityByName.
	ss::IEntityFactoryDictionary& Factories();

private:
	// Idempotent by classname: C# re-declares every class on a reload.
	bool InstallStandaloneClass(const char* pClassName, const char** ppOutputNames, int nOutputCount, bool bServerOnly,
								int nSolidType, int nSolidFlags, bool bTouchEveryTick, bool bPlayer);

	// False for a name never registered.
	bool SetStandaloneClassOps(const char* pClassName, uint32_t nOpMask);

	// The dictionary, the declared classes and the world's factory (shell_standalone_class.cpp).
	// Freed through the deleter the constructor sets, so a TU that only destroys a lifecycle
	// needs neither Impl nor an out-of-line destructor.
	struct Impl;
	static void DeleteImpl(Impl* pImpl);

	CServerEntityContext& m_Entities;
	std::unique_ptr<Impl, void (*)(Impl*)> m_pImpl;
};
