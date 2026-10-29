//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CServerPhysics: stock's physics_shared.cpp globals and its CPhysicsHook game system as
// one object, the DLL tier's: the vphysics interfaces, the level's environment and world
// object, and IShimPhysics. CServerLevel runs its game-system steps; entities reach it
// through the entity context the DLL tier binds it to.

#pragma once

#include <memory>

#include "ishim_areas.h"
#include "shell_serversystem.h"
#include "ss_init_factory.h"

class CGlobalVars;
class CServerEntityContext;
class CServerEntityResolver;
class CServerPlayersDll;
class CWorld;
class IPhysics;
class IPhysicsCollision;
class IPhysicsEnvironment;
class IPhysicsObject;
class IPhysicsSurfaceProps;
struct CManagedAreas;

class CServerPhysics final : public CServerSystem, public IShimPhysics
{
public:
	CServerPhysics(CGlobalVars* pGlobals, const ss::factorylist_t& factories, const CManagedAreas& managed,
				   CServerPlayersDll& players, const CServerEntityContext& entities, CServerEntityResolver& resolver);

	CServerPhysics(const CServerPhysics&) = delete;
	CServerPhysics& operator=(const CServerPhysics&) = delete;

	// The game system steps, as stock's CPhysicsHook.
	const char* Name() override { return "CPhysicsHook"; }

	bool Init() override;
	void LevelInitPreEntity() override;
	void LevelInitPostEntity() override;
	void LevelShutdownPreEntity() override;
	void LevelShutdownPostEntity() override;

	// IShimPhysics.
	void EntityDestroyPhysics(uint32_t hEntity) override;
	void* EntityPhysicsObject(uint32_t hEntity) override;
	void* PhysicsEnvironmentPointer() override;
	void* PhysicsWorldObject() override;
	int32_t PhysicsUpdateOwners(void** ppObjects, float* pVelocities, int32_t nCount, uint32_t* pOutOwners) override;
	int32_t EntitySwapPhysicsObject(uint32_t hEntity, void* pObject, void** ppPrevious) override;

	// From Init until the DLL tier goes.
	IPhysics* Physics() const { return m_pPhysics; }

	IPhysicsCollision* Collision() const { return m_pCollision; }

	IPhysicsSurfaceProps* SurfaceProps() const { return m_pSurfaceProps; }

	// The same pointer by reference, for what is built before Init fills it.
	IPhysicsSurfaceProps* const& SurfacePropsRef() const { return m_pSurfaceProps; }

	// The level's environment and world object; NULL between levels.
	IPhysicsEnvironment* Environment() const { return m_pEnv; }

	IPhysicsObject* WorldObject() const { return m_pWorldObject; }

	// The world, when pObject is its physics object: one compare. Both NULL answers no,
	// since the world object is NULL during level load.
	CWorld* WorldOwner(const IPhysicsObject* pObject) const;

private:
	// The shell environment and the mass-centre overrides (physics.cpp).
	struct Impl;
	static void DeleteImpl(Impl* pImpl);

	CGlobalVars* m_pGlobals;

	// The engine's factories; the physics DLL's is read at Init.
	const ss::factorylist_t& m_Factories;
	const CManagedAreas& m_Managed;
	CServerPlayersDll& m_Players;
	const CServerEntityContext& m_Entities;
	CServerEntityResolver& m_Resolver;

	IPhysics* m_pPhysics = nullptr;
	IPhysicsCollision* m_pCollision = nullptr;
	IPhysicsSurfaceProps* m_pSurfaceProps = nullptr;
	IPhysicsEnvironment* m_pEnv = nullptr;
	IPhysicsObject* m_pWorldObject = nullptr;
	bool m_bPaused = false;

	std::unique_ptr<Impl, void (*)(Impl*)> m_pImpl;
};
